using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;

namespace FrostBot.Logic.Library;

public sealed record YoutubeFeedEntry(string VideoId, DateTimeOffset Published, string? Link)
{
    // YouTube's feed links Shorts as /shorts/<id> and standard uploads as /watch?v=<id>.
    public bool IsShort => Link?.Contains("/shorts/", StringComparison.Ordinal) == true;
}

// Parses the two public sources of new uploads - the channel's Atom feed and the Data API's
// uploads playlist - into the entries the upload poll cares about, newest first.
public static class YoutubeFeed
{
    private static readonly XNamespace AtomNs = "http://www.w3.org/2005/Atom";
    private static readonly XNamespace YouTubeNs = "http://www.youtube.com/xml/schemas/2015";

    public static async Task<List<YoutubeFeedEntry>> ParseAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        XDocument document = await XDocument.LoadAsync(stream, LoadOptions.None, cancellationToken);

        List<YoutubeFeedEntry> entries = [];
        if (document.Root is null) return entries;

        foreach (XElement element in document.Root.Elements(AtomNs + "entry"))
        {
            string? videoId = (string?)element.Element(YouTubeNs + "videoId");
            string? publishedText = (string?)element.Element(AtomNs + "published");
            if (!IsWellFormedVideoId(videoId)
                || !DateTimeOffset.TryParse(publishedText, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTimeOffset published))
            {
                continue;
            }

            entries.Add(new YoutubeFeedEntry(
                videoId!,
                published,
                (string?)element.Element(AtomNs + "link")?.Attribute("href")));
        }

        // Newest first: the poll compares the head of this list against its checkpoint.
        entries.Sort((left, right) => right.Published.CompareTo(left.Published));
        return entries;
    }

    // Parses a Data API playlistItems.list response (the channel's uploads playlist). The API has
    // no /shorts/ marker, so the caller's known feed links are carried over for videos listed in
    // both; videos only the API knows yet keep a null link and get probed instead.
    public static List<YoutubeFeedEntry> ParsePlaylistItems(
        JsonElement root, IReadOnlyDictionary<string, string?>? feedLinks = null)
    {
        List<YoutubeFeedEntry> entries = [];
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("items", out JsonElement items)
            || items.ValueKind != JsonValueKind.Array)
        {
            return entries;
        }

        // Every level is type-checked before being read: an unexpected shape must never throw and
        // cost the whole poll tick, it just skips the entry.
        foreach (JsonElement item in items.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object
                || !item.TryGetProperty("snippet", out JsonElement snippet)
                || snippet.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            string? videoId = snippet.TryGetProperty("resourceId", out JsonElement resourceId)
                && resourceId.ValueKind == JsonValueKind.Object
                && resourceId.TryGetProperty("videoId", out JsonElement videoIdElement)
                && videoIdElement.ValueKind == JsonValueKind.String
                    ? videoIdElement.GetString()
                    : null;
            if (!IsWellFormedVideoId(videoId)) continue;

            string? publishedText = snippet.TryGetProperty("publishedAt", out JsonElement publishedAt)
                && publishedAt.ValueKind == JsonValueKind.String
                    ? publishedAt.GetString()
                    : null;
            if (!DateTimeOffset.TryParse(publishedText, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTimeOffset published))
            {
                continue;
            }

            entries.Add(new YoutubeFeedEntry(videoId!, published,
                feedLinks is not null && feedLinks.TryGetValue(videoId!, out string? link) ? link : null));
        }

        // Newest first, exactly like the feed: the poll compares the list against its checkpoint.
        entries.Sort((left, right) => right.Published.CompareTo(left.Published));
        return entries;
    }

    // YouTube ids are exactly 11 characters of [A-Za-z0-9_-]; anything else must never be
    // interpolated into a request URL or a Discord message.
    public static bool IsWellFormedVideoId(string? videoId)
        => !string.IsNullOrEmpty(videoId)
            && videoId.Length == 11
            && videoId.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-');
}
