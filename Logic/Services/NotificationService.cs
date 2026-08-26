using FrostBot.Data;
using FrostBot.Data.Models;
using Microsoft.EntityFrameworkCore;
using NetCord.Gateway;
using NetCord.Rest;
using System.Collections.Concurrent;

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

    private const int DEDUPE_WINDOW_HOURS = 24;
    private const int MAX_CACHE_ENTRIES = 1000;

    private readonly IDbContextFactory<BotDbContext> _dbContextFactory;
    private readonly GatewayClient _client;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<NotificationService> _logger;
    private readonly string _callbackUrl;

    // Video IDs of recently sent notifications, used to ignore duplicate hub deliveries
    private readonly ConcurrentDictionary<string, DateTimeOffset> _recentVideos = new();

    public NotificationService(IDbContextFactory<BotDbContext> dbContextFactory, GatewayClient client, IHttpClientFactory httpClientFactory, IConfiguration configuration, ILogger<NotificationService> logger)
    {
        _dbContextFactory = dbContextFactory;
        _client = client;
        _httpClientFactory = httpClientFactory;
        _logger = logger;

        _callbackUrl = configuration["Youtube:CallbackUrl"] ?? CALLBACK_URL;
        if (!_callbackUrl.EndsWith(CALLBACK_PATH, StringComparison.Ordinal))
            _logger.LogWarning("Configured Youtube:CallbackUrl ({CallbackUrl}) does not end with the callback path {CallbackPath}.", _callbackUrl, CALLBACK_PATH);
    }

    public async Task SubscribeToYoutubeAsync()
    {
        // Reuse the pooled client (registered in Program.cs) so failed retries
        // don't leak sockets on a small instance
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

    public async Task SendYoutubeNotificationAsync(string videoUrl)
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
