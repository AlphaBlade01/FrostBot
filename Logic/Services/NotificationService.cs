using FrostBot.Data;
using FrostBot.Data.Models;
using FrostBot.Logic.Library;
using Microsoft.EntityFrameworkCore;
using NetCord.Gateway;
using NetCord.Rest;
using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Xml;

namespace FrostBot.Logic.Services;

public class NotificationService
{
    // The YouTube channel whose uploads the bot announces. YOUTUBER_NAME is only the display
    // name used in the notification message.
    public const string YOUTUBER_NAME = "Fr0stPh3onix";
    public const string CHANNEL_ID = "UC0fGA6bRAyCR2FK_qMvwZsA";

    // The channel's public RSS feed. The Data API's uploads playlist is the primary discovery
    // source; this feed is the fallback and the authoritative /shorts/ marker. Poll-only by
    // design: no inbound endpoint to expose, no subscription to renew.
    public const string CHANNEL_FEED_URL = "https://www.youtube.com/feeds/videos.xml?channel_id=" + CHANNEL_ID;

    private const string INNERTUBE_PLAYER_URL = "https://www.youtube.com/youtubei/v1/player?prettyPrint=false";
    private const string INNERTUBE_CLIENT_VERSION = "2.20240101.00.00";
    private const string CHECK_CLIENT_NAME = "youtube-check";
    private const string DATA_API_VIDEOS_URL = "https://www.googleapis.com/youtube/v3/videos";

    // Discovery endpoint: a channel's uploads playlist lists a video within seconds of it going
    // public, while the feed can sit behind a 15-minute CDN cache. One list call costs 1 unit.
    private const string DATA_API_PLAYLIST_ITEMS_URL = "https://www.googleapis.com/youtube/v3/playlistItems";
    private const int DISCOVERY_MAX_RESULTS = 10;

    // A channel's uploads playlist id is its channel id with the "UC" prefix swapped for "UU".
    private static readonly string UPLOADS_PLAYLIST_ID = "UU" + CHANNEL_ID[2..];

    // A long outage must not dump every video the feed still lists into the channel: entries older than this only advance the checkpoint.
    private static readonly TimeSpan MAX_BACKFILL = TimeSpan.FromHours(1);

    private const int DEDUPE_WINDOW_HOURS = 24;
    private const int MAX_CACHE_ENTRIES = 1000;

    private readonly IDbContextFactory<BotDbContext> _dbContextFactory;
    private readonly GatewayClient _client;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly LogService _logService;
    private readonly ILogger<NotificationService> _logger;
    private readonly string? _apiKey;

    // Set when the Data API rejects the key (bad key, API disabled, quota exhausted). The player
    // API then classifies for the rest of the run instead of repeating the same failing request.
    private string? _dataApiUnavailable;

    // Newest feed entry already handled (persisted in YoutubePollStates). Cached in memory so a
    // tick with no new uploads does no database work at all.
    private PollCheckpoint? _checkpoint;
    private bool _checkpointLoaded;

    // Video IDs recently handled in this process - ignores duplicate work from a retried or restarted poll.
    private readonly ConcurrentDictionary<string, DateTimeOffset> _recentVideos = new();

    public NotificationService(IDbContextFactory<BotDbContext> dbContextFactory, GatewayClient client, IHttpClientFactory httpClientFactory, LogService logService, IConfiguration configuration, ILogger<NotificationService> logger)
    {
        _dbContextFactory = dbContextFactory;
        _client = client;
        _httpClientFactory = httpClientFactory;
        _logService = logService;
        _logger = logger;
        // Free official API (10,000 units/day; one videos.list call costs 1 unit). Optional: when
        // absent the player API still resolves live/upcoming state.
        _apiKey = configuration["Youtube:ApiKey"];
        if (string.IsNullOrWhiteSpace(_apiKey))
            _logger.LogInformation("Youtube:ApiKey is not set; discovering uploads from the RSS feed and classifying with the player API.");
    }

    // Poll the channel for new uploads and announce every video published since the last checkpoint.
    public async Task PollForRecentUploadsAsync(CancellationToken cancellationToken = default)
    {
        // The feed is fetched on every tick regardless of where discovery comes from: it carries
        // the /shorts/ marker, and it is the discovery source when the Data API is unavailable.
        List<YoutubeFeedEntry> feed = await FetchFeedAsync(cancellationToken);

        List<YoutubeFeedEntry> entries =
            await FetchDataApiUploadsAsync(feed, cancellationToken) ?? feed;
        if (entries.Count == 0) return; // both sources failed; logged, the next tick retries

        PollCheckpoint? checkpoint = await GetCheckpointAsync(cancellationToken);

        // Only entries that were actually handled move this forward, so a failed announcement
        // (Discord unreachable, rate limited) is retried on the next tick instead of being lost.
        PollCheckpoint? handled = checkpoint;

        // Both sources are newest-first; announcing oldest-first keeps multiple uploads in order.
        foreach (YoutubeFeedEntry entry in entries.Where(e => IsAfterCheckpoint(e, checkpoint)).Reverse())
        {
            if (DateTimeOffset.UtcNow - entry.Published > MAX_BACKFILL)
            {
                _logger.LogInformation(
                    "Not announcing YouTube video {VideoId}: published {Published:u}, older than the {Hours}h backfill window.",
                    entry.VideoId, entry.Published, MAX_BACKFILL.TotalHours);
                handled = new PollCheckpoint(entry.VideoId, entry.Published);
                continue;
            }

            try
            {
                await AnnounceAsync(entry, cancellationToken);
                handled = new PollCheckpoint(entry.VideoId, entry.Published);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                // Retry this upload (and the newer ones behind it) on the next tick.
                _logger.LogError(ex, "Failed to announce YouTube upload {VideoId}; retrying next tick.", entry.VideoId);
                break;
            }
        }

        if (handled is not null) await SaveCheckpointAsync(handled, cancellationToken);
    }

    private async Task AnnounceAsync(YoutubeFeedEntry entry, CancellationToken cancellationToken)
    {
        if (!TrackVideo(entry.VideoId))
        {
            _logger.LogInformation("Ignoring YouTube upload {VideoId} (already handled).", entry.VideoId);
            return;
        }

        await using BotDbContext dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        GuildConfig[] configs = await dbContext.GuildConfigs
            .Where(g => g.VideoNotificationChannelId != null)
            .AsNoTracking()
            .ToArrayAsync(cancellationToken);

        if (configs.Length == 0)
        {
            _logger.LogWarning("YouTube upload {VideoId} detected, but no video notification channel is configured.", entry.VideoId);
            return;
        }

        string videoUrl = $"https://www.youtube.com/watch?v={entry.VideoId}";

        // Only standard uploads get posted. Shorts are identified by the feed's /shorts/ link, the
        // canonical URL probe or the player API; livestreams, live VODs and not-yet-public
        // broadcasts by the video's live state.
        (VideoKind kind, string detail) = await ClassifyAsync(entry, cancellationToken);
        if (kind is VideoKind.Short or VideoKind.Live or VideoKind.Upcoming)
        {
            await LogSkippedAsync(configs, entry, videoUrl, kind switch
            {
                VideoKind.Short => "YouTube Short",
                VideoKind.Live => "Livestream / live VOD",
                _ => "Not yet public (scheduled premiere/livestream)",
            });
            return;
        }

        if (kind == VideoKind.Unknown)
        {
            // Either source already proves this is a new upload from the channel, so an
            // unverifiable live state errs towards announcing rather than missing a genuine upload.
            _logger.LogWarning("Could not confirm {VideoId} is a standard upload ({Detail}); announcing it anyway.", entry.VideoId, detail);
        }

        string content = $"**{YOUTUBER_NAME}** just uploaded a new video!\n{videoUrl}";

        foreach (GuildConfig config in configs)
        {
            string message = config.VideoNotificationRoleId != null
                ? $"{content} <@&{config.VideoNotificationRoleId}>"
                : content;

            // One broken destination (channel deleted, role mention forbidden, missing permission)
            // must not stop the other guilds and must not fail the whole video: an exception here
            // would leave the checkpoint behind this video and retry it - and so block every later
            // upload - on every tick.
            try
            {
                await _client.Rest.SendMessageAsync(config.VideoNotificationChannelId!.Value,
                    new MessageProperties { Content = message, AllowedMentions = AllowedMentionsProperties.All });
                _logger.LogInformation("Sent YouTube upload notification for {VideoUrl} to guild {GuildId}.", videoUrl, config.GuildId);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Could not send YouTube upload notification for {VideoUrl} to guild {GuildId}; skipping that channel.", videoUrl, config.GuildId);
            }
        }
    }

    private async Task<List<YoutubeFeedEntry>> FetchFeedAsync(CancellationToken cancellationToken)
    {
        try
        {
            using HttpClient client = _httpClientFactory.CreateClient(CHECK_CLIENT_NAME);

            // YouTube serves this feed with "Cache-Control: public, max-age=900", so a plain GET can
            // hand back a copy up to 15 minutes old and a fresh upload looks invisible. A unique
            // query value plus no-cache headers stop a CDN copy from masking a new entry.
            using HttpRequestMessage request = new(HttpMethod.Get,
                $"{CHANNEL_FEED_URL}&_={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}");
            request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true, NoStore = true };
            request.Headers.Pragma.ParseAdd("no-cache");

            using HttpResponseMessage response = await client.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("YouTube upload poll skipped: channel feed returned HTTP {StatusCode}.", (int)response.StatusCode);
                return [];
            }

            await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            return await YoutubeFeed.ParseAsync(stream, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or XmlException)
        {
            _logger.LogWarning(ex, "YouTube upload poll could not read the channel feed; retrying next tick.");
            return [];
        }
    }

    // Primary discovery: the channel's uploads playlist. Returns null when the Data API cannot be
    // used, so the caller falls back to the feed.
    private async Task<List<YoutubeFeedEntry>?> FetchDataApiUploadsAsync(
        List<YoutubeFeedEntry> feed, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_apiKey) || _dataApiUnavailable is not null) return null;

        try
        {
            using HttpClient client = _httpClientFactory.CreateClient(CHECK_CLIENT_NAME);
            using HttpResponseMessage response = await client.GetAsync(
                $"{DATA_API_PLAYLIST_ITEMS_URL}?part=snippet&playlistId={UPLOADS_PLAYLIST_ID}&maxResults={DISCOVERY_MAX_RESULTS}&key={_apiKey}",
                cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                await NoteDataApiFailureAsync(response, cancellationToken);
                _logger.LogWarning("YouTube upload poll could not list the uploads playlist (HTTP {StatusCode}); using the RSS feed.",
                    (int)response.StatusCode);
                return null;
            }

            await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using JsonDocument document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

            // Carry the feed's links over for videos it already lists - they are the only marker
            // the Data API does not expose (a Short is an ordinary video to it).
            Dictionary<string, string?> feedLinks = feed
                .GroupBy(entry => entry.VideoId)
                .ToDictionary(group => group.Key, group => group.First().Link);

            return YoutubeFeed.ParsePlaylistItems(document.RootElement, feedLinks);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        {
            _logger.LogWarning(ex, "YouTube upload poll could not list the uploads playlist; using the RSS feed.");
            return null;
        }
    }

    private enum VideoKind
    {
        Standard,  // standard upload or an already-aired premiere - post the notification
        Short,     // YouTube Short - skip
        Live,      // livestream or live VOD - skip
        Upcoming,  // scheduled premiere/livestream not yet public - skip
        Unknown,   // live state could not be verified - announce anyway
    }

    // Cheapest source first: the feed link, then the Data API (authoritative live state, immune to
    // bot-walls), then the canonical URL probe - the Data API cannot tell a Short from an ordinary
    // video - and only what is still undecided reaches the keyless player API.
    private async Task<(VideoKind Kind, string Detail)> ClassifyAsync(YoutubeFeedEntry entry, CancellationToken cancellationToken)
    {
        if (entry.IsShort) return (VideoKind.Short, "feed link is /shorts/");

        string videoId = entry.VideoId;

        (VideoKind apiKind, string? apiDetail) = await CheckDataApiAsync(videoId, cancellationToken);
        if (apiKind is VideoKind.Live or VideoKind.Upcoming) return (apiKind, apiDetail ?? "ok");

        // A link that is not /shorts/ already answers the Shorts question, so only a video the
        // feed does not list yet (discovered from the uploads playlist) is probed.
        bool? isShort = entry.Link is null
            ? await ProbeIsShortAsync(videoId, cancellationToken)
            : false;
        if (isShort == true) return (VideoKind.Short, "canonical URL is /shorts/");

        // Both sources agree on an ordinary upload - one player API request fewer per video.
        if (apiKind == VideoKind.Standard && isShort == false) return (VideoKind.Standard, apiDetail ?? "ok");

        (VideoKind playerKind, string? playerDetail) = await CheckPlayerAsync(videoId, cancellationToken);
        if (playerKind is VideoKind.Live or VideoKind.Upcoming or VideoKind.Short)
            return (playerKind, playerDetail ?? "ok");

        if (apiKind == VideoKind.Standard) return (VideoKind.Standard, apiDetail ?? "ok");
        if (playerKind == VideoKind.Standard) return (VideoKind.Standard, playerDetail ?? "ok");

        return (VideoKind.Unknown, $"Data API: {apiDetail ?? "inconclusive"}; Player: {playerDetail ?? "inconclusive"}");
    }

    // YouTube answers /shorts/<id> with 200 for a Short and 303-redirects everything else to
    // /watch?v=<id>, so the final path is the marker. (null) = undetermined.
    private async Task<bool?> ProbeIsShortAsync(string videoId, CancellationToken cancellationToken)
    {
        try
        {
            using HttpClient client = _httpClientFactory.CreateClient(CHECK_CLIENT_NAME);
            using HttpRequestMessage request = new(HttpMethod.Head, $"https://www.youtube.com/shorts/{videoId}");

            // Without a consent cookie YouTube bounces a browser User-Agent to consent.youtube.com,
            // whose URL says nothing about the video. This keeps the redirect on the video itself.
            request.Headers.Add("Cookie", "SOCS=CAI");

            using HttpResponseMessage response = await client.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            string? finalPath = response.RequestMessage?.RequestUri?.AbsolutePath;
            if (finalPath is null) return null;
            if (finalPath.StartsWith("/shorts/", StringComparison.OrdinalIgnoreCase)) return true;
            if (finalPath.Equals("/watch", StringComparison.OrdinalIgnoreCase)) return false;

            return null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return null;
        }
    }

    // The official Data API. Authoritative for live state; only consulted when an API key is
    // configured. (Unknown kind) = inconclusive, fall through to the player API.
    private async Task<(VideoKind Kind, string? Detail)> CheckDataApiAsync(string videoId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
            return (VideoKind.Unknown, "no API key configured");

        if (_dataApiUnavailable is not null)
            return (VideoKind.Unknown, _dataApiUnavailable);

        try
        {
            using HttpClient client = _httpClientFactory.CreateClient(CHECK_CLIENT_NAME);
            using HttpResponseMessage response = await client.GetAsync(
                $"{DATA_API_VIDEOS_URL}?part=snippet,liveStreamingDetails&id={videoId}&key={_apiKey}", cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                await NoteDataApiFailureAsync(response, cancellationToken);
                return (VideoKind.Unknown, $"HTTP {(int)response.StatusCode}");
            }

            await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using JsonDocument document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

            JsonElement item = document.RootElement.GetProperty("items").EnumerateArray().FirstOrDefault();
            if (item.ValueKind == JsonValueKind.Undefined)
                return (VideoKind.Unknown, "video not found (private, deleted or not yet public)");

            // Ownership needs no check: the video came from this channel's own feed, so only the
            // broadcast state matters here.
            string? broadcast = item.GetProperty("snippet").TryGetProperty("liveBroadcastContent", out JsonElement liveBroadcastContent)
                ? liveBroadcastContent.GetString()
                : null;
            if (broadcast == "upcoming")
                return (VideoKind.Upcoming, "scheduled premiere/livestream");
            if (broadcast == "live" || item.TryGetProperty("liveStreamingDetails", out _))
                return (VideoKind.Live, "livestream or broadcast VOD");

            return (VideoKind.Standard, "standard upload");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return (VideoKind.Unknown, ex.GetType().Name);
        }
    }

    // Rejected credentials, a disabled API or exhausted quota will not recover during this run, so
    // stop calling the Data API and let the feed / player API take over until restart.
    private async Task NoteDataApiFailureAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        int status = (int)response.StatusCode;
        if (status is not (400 or 401 or 403)) return;

        _dataApiUnavailable = await DescribeDataApiFailureAsync(response, status, cancellationToken);
        _logger.LogWarning(
            "YouTube Data API unavailable ({Detail}); falling back to the RSS feed and player API until restart. Check the Youtube:ApiKey value.",
            _dataApiUnavailable);
    }

    // Turns the API's error body into something worth logging ("quotaExceeded", "keyInvalid", ...)
    // without ever echoing the request URL, which contains the key.
    private static async Task<string> DescribeDataApiFailureAsync(HttpResponseMessage response, int status, CancellationToken cancellationToken)
    {
        string detail = $"HTTP {status}";

        try
        {
            await using Stream body = await response.Content.ReadAsStreamAsync(cancellationToken);
            using JsonDocument document = await JsonDocument.ParseAsync(body, cancellationToken: cancellationToken);
            if (!document.RootElement.TryGetProperty("error", out JsonElement error)) return detail;

            string? reason = error.TryGetProperty("errors", out JsonElement errors)
                && errors.ValueKind == JsonValueKind.Array
                && errors.GetArrayLength() > 0
                && errors[0].TryGetProperty("reason", out JsonElement reasonElement)
                    ? reasonElement.GetString()
                    : null;
            string? message = error.TryGetProperty("message", out JsonElement messageElement)
                ? messageElement.GetString()
                : null;
            string? described = reason ?? message;

            return string.IsNullOrWhiteSpace(described) ? detail : $"HTTP {status} {described}";
        }
        catch (JsonException)
        {
            // Not the documented error shape; the status code is still the useful part.
            return detail;
        }
    }

    // The keyless player API. Proves live/upcoming state and repeats the canonical URL's Shorts
    // marker; (Unknown) means inconclusive.
    private async Task<(VideoKind Kind, string? Detail)> CheckPlayerAsync(string videoId, CancellationToken cancellationToken)
    {
        try
        {
            using HttpClient client = _httpClientFactory.CreateClient(CHECK_CLIENT_NAME);

            byte[] requestBody = JsonSerializer.SerializeToUtf8Bytes(new
            {
                context = new
                {
                    client = new
                    {
                        clientName = "WEB",
                        clientVersion = INNERTUBE_CLIENT_VERSION,
                        hl = "en"
                    }
                },
                videoId
            });

            using ByteArrayContent content = new(requestBody);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            using HttpResponseMessage response = await client.PostAsync(INNERTUBE_PLAYER_URL, content, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return (VideoKind.Unknown, $"HTTP {(int)response.StatusCode}");

            await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using JsonDocument document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            JsonElement root = document.RootElement;
            if (!root.TryGetProperty("videoDetails", out JsonElement details))
                return (VideoKind.Unknown, "no videoDetails (likely bot-walled)");

            // isLiveContent is the definitive marker for livestreams and their VODs; premieres
            // never carry it.
            if (details.TryGetProperty("isLiveContent", out JsonElement isLiveContent) && isLiveContent.ValueKind == JsonValueKind.True)
                return (VideoKind.Live, "isLiveContent");

            if (details.TryGetProperty("isUpcoming", out JsonElement isUpcoming) && isUpcoming.ValueKind == JsonValueKind.True)
                return (VideoKind.Upcoming, "isUpcoming");

            if (root.TryGetProperty("microformat", out JsonElement microformat)
                && microformat.TryGetProperty("playerMicroformatRenderer", out JsonElement renderer)
                && renderer.TryGetProperty("canonicalUrl", out JsonElement canonicalUrl)
                && canonicalUrl.GetString()?.Contains("/shorts/", StringComparison.Ordinal) == true)
                return (VideoKind.Short, "canonical URL is /shorts/");

            return (VideoKind.Standard, "standard upload");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        {
            return (VideoKind.Unknown, ex.GetType().Name);
        }
    }

    private async Task LogSkippedAsync(GuildConfig[] configs, YoutubeFeedEntry entry, string videoUrl, string reason)
    {
        _logger.LogInformation("Skipping YouTube video {VideoId} ({Reason}); not a standard upload.", entry.VideoId, reason);

        foreach (GuildConfig config in configs)
        {
            EmbedProperties embed = new EmbedProperties()
                .WithTitle("Skipped video notification")
                .WithFields([
                    new() { Name = "Reason", Value = reason },
                    new() { Name = "Video", Value = $"{videoUrl} ({entry.VideoId})" },
                ])
                .WithColor(BotColours.Fail);
            try
            {
                await _logService.SendLogEmbed(config.GuildId, embed);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // A skip is best-effort logging; it must never stall the poll.
                _logger.LogWarning(ex, "Could not log skipped YouTube video {VideoId} for guild {GuildId}.", entry.VideoId, config.GuildId);
            }
        }
    }

    private sealed record PollCheckpoint(string VideoId, DateTimeOffset Published);

    private static bool IsAfterCheckpoint(YoutubeFeedEntry entry, PollCheckpoint? checkpoint)
        => checkpoint is null
            || entry.Published > checkpoint.Published
            || (entry.Published == checkpoint.Published && entry.VideoId != checkpoint.VideoId);

    private async Task<PollCheckpoint?> GetCheckpointAsync(CancellationToken cancellationToken)
    {
        if (_checkpointLoaded) return _checkpoint;

        await using BotDbContext dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        YoutubePollState? state = await dbContext.YoutubePollStates
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.ChannelId == CHANNEL_ID, cancellationToken);

        _checkpoint = state?.LastVideoId != null && state.LastPublishedAt != null
            ? new PollCheckpoint(state.LastVideoId, state.LastPublishedAt.Value)
            : null;
        _checkpointLoaded = true;
        return _checkpoint;
    }

    private async Task SaveCheckpointAsync(PollCheckpoint checkpoint, CancellationToken cancellationToken)
    {
        // Never move the checkpoint backwards (a rotated feed must not re-announce old videos).
        if (_checkpoint is not null && checkpoint.Published < _checkpoint.Published) return;
        if (_checkpoint == checkpoint) return;

        await using BotDbContext dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        YoutubePollState? state = await dbContext.YoutubePollStates
            .FirstOrDefaultAsync(s => s.ChannelId == CHANNEL_ID, cancellationToken);

        if (state is null)
        {
            dbContext.YoutubePollStates.Add(new YoutubePollState
            {
                ChannelId = CHANNEL_ID,
                LastVideoId = checkpoint.VideoId,
                LastPublishedAt = checkpoint.Published,
            });
        }
        else
        {
            state.LastVideoId = checkpoint.VideoId;
            state.LastPublishedAt = checkpoint.Published;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        _checkpoint = checkpoint;
        _checkpointLoaded = true;
    }

    private bool TrackVideo(string videoId)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        if (_recentVideos.TryGetValue(videoId, out DateTimeOffset handledAt)
            && now - handledAt < TimeSpan.FromHours(DEDUPE_WINDOW_HOURS))
        {
            return false;
        }

        _recentVideos[videoId] = now;
        if (_recentVideos.Count > MAX_CACHE_ENTRIES) CleanExpiredVideos();
        return true;
    }

    private void CleanExpiredVideos()
    {
        DateTimeOffset cutoff = DateTimeOffset.UtcNow.AddHours(-DEDUPE_WINDOW_HOURS);
        foreach (var entry in _recentVideos)
        {
            if (entry.Value < cutoff) _recentVideos.TryRemove(entry);
        }
    }
}
