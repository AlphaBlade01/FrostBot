using FrostBot.Data;
using FrostBot.Data.Models;
using FrostBot.Logic.Library;
using Microsoft.EntityFrameworkCore;
using NetCord.Gateway;
using NetCord.Rest;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace FrostBot.Logic.Services;

public class NotificationService
{
    // Fr0stPh3onix's YouTube channel, subscribed to via PubSubHubbub
    public const string YOUTUBER_NAME = "Fr0stPh3onix";
    public const string CHANNEL_ID = "UC0fGA6bRAyCR2FK_qMvwZsA";
    public const string HUB_SUBSCRIBE_URL = "https://pubsubhubbub.appspot.com/subscribe";
    public const string TOPIC_URL = "https://www.youtube.com/xml/feeds/videos.xml?channel_id=" + CHANNEL_ID;
    public const string CALLBACK_PATH = "/webhook/youtube";
    // The hub requires the callback to be publicly reachable over HTTPS.
    // Override via the "Youtube:CallbackUrl" config value (e.g. an ngrok URL for local testing).
    public const string CALLBACK_URL = "https://frostbotyt.duckdns.org" + CALLBACK_PATH;

    private const string INNERTUBE_PLAYER_URL = "https://www.youtube.com/youtubei/v1/player?prettyPrint=false";
    private const string INNERTUBE_CLIENT_VERSION = "2.20240101.00.00";

    private const int DEDUPE_WINDOW_HOURS = 24;
    private const int MAX_CACHE_ENTRIES = 1000;

    private readonly IDbContextFactory<BotDbContext> _dbContextFactory;
    private readonly GatewayClient _client;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly LogService _logService;
    private readonly ILogger<NotificationService> _logger;
    private readonly IHostEnvironment _hostEnvironment;
    private readonly string _callbackUrl;

    // Video IDs of recently handled notifications (sent or skipped), used to ignore duplicate hub deliveries
    private readonly ConcurrentDictionary<string, DateTimeOffset> _recentVideos = new();

    public NotificationService(IDbContextFactory<BotDbContext> dbContextFactory, GatewayClient client, IHttpClientFactory httpClientFactory, LogService logService, IConfiguration configuration, ILogger<NotificationService> logger, IHostEnvironment hostEnvironment)
    {
        _dbContextFactory = dbContextFactory;
        _client = client;
        _httpClientFactory = httpClientFactory;
        _logService = logService;
        _logger = logger;
        _hostEnvironment = hostEnvironment;

        _callbackUrl = configuration["Youtube:CallbackUrl"] ?? CALLBACK_URL;
        if (!_callbackUrl.EndsWith(CALLBACK_PATH, StringComparison.Ordinal))
            _logger.LogWarning("Configured Youtube:CallbackUrl ({CallbackUrl}) does not end with the callback path {CallbackPath}.", _callbackUrl, CALLBACK_PATH);
    }

    public async Task SubscribeToYoutubeAsync()
    {
        // Skip subscription on dev machine
        if (_hostEnvironment.IsDevelopment())
        {
            _logger.LogInformation("Skipping YouTube subscription in development environment");
            return;
        }

        // Subscribe to PubSubHubBub for the channel's video feed
        using HttpClient httpClient = _httpClientFactory.CreateClient("youtube-hub");

        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["hub.mode"] = "subscribe",
            ["hub.topic"] = TOPIC_URL,
            ["hub.callback"] = _callbackUrl,
            ["hub.verify"] = "sync",
            // Ask for the maximum lease; the hub may grant less (e.g. 5 days), so we renew daily regardless
            ["hub.lease_seconds"] = "604800"
        });

        _logger.LogInformation("Subscribing to YouTube channel {ChannelId} (callback: {CallbackUrl})", CHANNEL_ID, _callbackUrl);
        HttpResponseMessage response = await httpClient.PostAsync(HUB_SUBSCRIBE_URL, form);
        response.EnsureSuccessStatusCode();
        _logger.LogInformation("YouTube subscription verified by hub (HTTP {(int)response.StatusCode}).");
    }

    public async Task SendYoutubeNotificationAsync(string videoUrl, string? title = null)
    {
        // Ignore duplicate hub deliveries for the same video
        string? videoId = GetVideoId(videoUrl);
        if (videoId == null || !TrackVideo(videoId))
        {
            _logger.LogInformation("Ignoring YouTube notification for {VideoUrl} (duplicate or unparseable video id).", videoUrl);
            return;
        }

        await using BotDbContext dbContext = await _dbContextFactory.CreateDbContextAsync();
        GuildConfig[] configs = await dbContext.GuildConfigs
            .Where(g => g.VideoNotificationChannelId != null)
            .ToArrayAsync();

        if (configs.Length == 0)
        {
            _logger.LogWarning("YouTube notification for {VideoUrl} received, but no guilds have a video notification channel configured.", videoUrl);
            return;
        }

        // Only standard uploads (and premieres, which are indistinguishable from standard
        // uploads once aired) get posted. Shorts, livestreams, live VODs and unverifiable
        // videos are logged to the logs channel instead.
        VideoKind kind = await ClassifyVideoAsync(videoId);
        if (kind != VideoKind.Standard)
        {
            _logger.LogInformation("Skipping YouTube video {VideoId} ({Reason}); not a standard upload.", videoId, kind);
            foreach (GuildConfig config in configs)
            {
                await _logService.SendLogEmbed(config.GuildId, BuildSkippedEmbed(videoId, title, videoUrl, kind));
            }
            return;
        }

        string content = $"**{YOUTUBER_NAME}** just uploaded a new video!\n{videoUrl}";

        foreach (GuildConfig config in configs)
        {
            string message = config.VideoNotificationRoleId != null
                ? $"{content} <@&{config.VideoNotificationRoleId}>"
                : content;
            await _client.Rest.SendMessageAsync(config.VideoNotificationChannelId!.Value,
                new MessageProperties { Content = message, AllowedMentions = AllowedMentionsProperties.All });
            _logger.LogInformation("Sent YouTube upload notification for {VideoUrl} to guild {GuildId}.", videoUrl, config.GuildId);
        }
    }

    private enum VideoKind
    {
        Standard,     // standard upload, or a premiere delivered at its start time - post the notification
        Short,        // YouTube Short - skip
        Live,         // livestream or live VOD - skip
        Upcoming,     // scheduled premiere/livestream not yet public - skip
        WrongChannel, // video does not belong to the configured channel - skip (anti-spoofing)
        Unknown,      // could not be verified - skip rather than risk a wrong post
    }

    private async Task<VideoKind> ClassifyVideoAsync(string videoId)
    {
        VideoKind result = VideoKind.Unknown;
        // One retry covers transient network failures before giving up (and logging the skip)
        for (int attempt = 1; attempt <= 2 && result == VideoKind.Unknown; attempt++)
        {
            result = await ClassifyOnceAsync(videoId);
        }
        return result;
    }

    private async Task<VideoKind> ClassifyOnceAsync(string videoId)
    {
        try
        {
            VideoKind shortCheck = await IsShortAsync(videoId);
            if (shortCheck != VideoKind.Standard) return shortCheck; // Short or Unknown
            return await IsLiveAsync(videoId); // Live, Upcoming, Standard or Unknown
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(ex, "Video classification failed for {VideoId}.", videoId);
            return VideoKind.Unknown;
        }
    }

    private async Task<VideoKind> IsShortAsync(string videoId)
    {
        using HttpClient client = _httpClientFactory.CreateClient("youtube-scrape");
        using HttpResponseMessage response = await client.SendAsync(
            new HttpRequestMessage(HttpMethod.Head, $"https://www.youtube.com/shorts/{videoId}"),
            HttpCompletionOption.ResponseHeadersRead);

        int status = (int)response.StatusCode;
        if (status == 200) return VideoKind.Short;
        if (status is >= 300 and < 400) return VideoKind.Standard; // redirected to /watch - not a Short
        return VideoKind.Unknown;
    }

    private async Task<VideoKind> IsLiveAsync(string videoId)
    {
        using HttpClient client = _httpClientFactory.CreateClient("youtube-scrape");

        var requestBody = JsonSerializer.SerializeToUtf8Bytes(new
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

        using var content = new ByteArrayContent(requestBody);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        using HttpResponseMessage response = await client.PostAsync(INNERTUBE_PLAYER_URL, content);
        if (!response.IsSuccessStatusCode) return VideoKind.Unknown;

        await using Stream stream = await response.Content.ReadAsStreamAsync();
        using JsonDocument document = await JsonDocument.ParseAsync(stream);
        if (!document.RootElement.TryGetProperty("videoDetails", out JsonElement details))
            return VideoKind.Unknown;

        // Anti-spoofing: the hub POST body is unauthenticated and its channelId field is forgeable,
        // so trust the player API's record of the video's real owner instead. A mismatch means the
        // entry was not a genuine upload from the configured channel.
        if (!details.TryGetProperty("channelId", out JsonElement videoChannel)
            || !string.Equals(videoChannel.GetString(), CHANNEL_ID, StringComparison.Ordinal))
            return VideoKind.WrongChannel;

        // isLiveContent is the definitive marker for livestreams and their VODs (verified on live,
        // scheduled and ended streams). Premieres never carry it, so they fall through to Standard.
        if (details.TryGetProperty("isLiveContent", out JsonElement isLiveContent) && isLiveContent.GetBoolean())
            return VideoKind.Live;
        // The hub only delivers public videos, so a scheduled item should not normally reach us;
        // if it does, ignore it (per design: premieres are only notified at their start time).
        if (details.TryGetProperty("isUpcoming", out JsonElement isUpcoming) && isUpcoming.GetBoolean())
            return VideoKind.Upcoming;

        return VideoKind.Standard;
    }

    private static EmbedProperties BuildSkippedEmbed(string videoId, string? title, string videoUrl, VideoKind kind)
    {
        string reason = kind switch
        {
            VideoKind.Short => "YouTube Short",
            VideoKind.Live => "Livestream / live VOD",
            VideoKind.Upcoming => "Not yet public (scheduled premiere/livestream)",
            VideoKind.WrongChannel => "Video does not belong to the configured channel",
            _ => "Could not be verified as a standard upload",
        };

        return new EmbedProperties()
            .WithTitle("Skipped video notification")
            .WithFields([
                new() { Name = "Reason", Value = reason },
                new() { Name = "Video", Value = title != null ? $"{title}\n{videoUrl}" : $"{videoUrl} ({videoId})" },
            ])
            .WithColor(BotColours.Fail);
    }

    private static string? GetVideoId(string videoUrl)
    {
        int index = videoUrl.IndexOf("v=", StringComparison.Ordinal);
        if (index < 0) return null;
        string id = videoUrl[(index + 2)..];
        int ampIndex = id.IndexOf('&');
        return ampIndex >= 0 ? id[..ampIndex] : id;
    }

    private bool TrackVideo(string videoId)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        if (_recentVideos.TryGetValue(videoId, out DateTimeOffset sentAt)
            && now - sentAt < TimeSpan.FromHours(DEDUPE_WINDOW_HOURS))
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
