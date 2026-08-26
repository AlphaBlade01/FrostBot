using FrostBot.Logic.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using System.Xml;
using System.Xml.Linq;

namespace FrostBot.Endpoints;

public class Youtube : IEndpoint
{
    private static readonly XNamespace ATOM_NS = "http://www.w3.org/2005/Atom";
    private static readonly XNamespace YT_NS = "http://www.youtube.com/xml/schemas/2015";

    private readonly ILogger<Youtube> _logger;

    public Youtube(ILogger<Youtube> logger)
    {
        _logger = logger;
    }

    public void Map(IEndpointRouteBuilder app)
    {
        app.MapGet(NotificationService.CALLBACK_PATH, HandleVerification);
        app.MapPost(NotificationService.CALLBACK_PATH, HandleNotification);
    }

    // PubSubHubbub subscription verification - echo the challenge back for our topic.
    // Query keys use dots (hub.mode, hub.topic, hub.challenge) so they cannot be bound
    // as ordinary parameter names; read them from the query string directly.
    private IResult HandleVerification(HttpRequest request)
    {
        string? hubMode = request.Query["hub.mode"];
        string? hubTopic = request.Query["hub.topic"];
        string? hubChallenge = request.Query["hub.challenge"];

        if (hubMode is not ("subscribe" or "unsubscribe") || hubTopic != NotificationService.TOPIC_URL)
        {
            _logger.LogWarning("Ignoring hub verification request (mode: {Mode}, topic: {Topic})", hubMode ?? "null", hubTopic ?? "null");
            return Results.NotFound();
        }

        _logger.LogInformation("Acknowledged hub {Mode} verification for YouTube channel.", hubMode);
        return Results.Text(hubChallenge ?? "");
    }

    // Upload notification - parse the Atom feed and forward each video
    private async Task<IResult> HandleNotification(HttpRequest request, NotificationService notificationService)
    {
        try
        {
            XDocument document = await XDocument.LoadAsync(request.Body, LoadOptions.None, request.HttpContext.RequestAborted);
            IEnumerable<XElement> entries = document.Root?.Elements(ATOM_NS + "entry") ?? [];
            int forwarded = 0;

            foreach (XElement entry in entries)
            {
                string? videoId = (string?)entry.Element(YT_NS + "videoId");
                string? channelId = (string?)entry.Element(YT_NS + "channelId");
                if (videoId == null) continue;
                if (channelId != null && channelId != NotificationService.CHANNEL_ID) continue;

                await notificationService.SendYoutubeNotificationAsync($"https://www.youtube.com/watch?v={videoId}");
                forwarded++;
            }

            _logger.LogInformation("Processed YouTube notification feed with {EntryCount} entry(ies), forwarded {Forwarded} video(s).", entries.Count(), forwarded);
            return Results.Ok();
        }
        catch (XmlException ex)
        {
            _logger.LogWarning(ex, "Received malformed XML in YouTube notification.");
            return Results.BadRequest();
        }
    }
}
