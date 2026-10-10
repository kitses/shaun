namespace Shaun.Bluesky;

internal static class MediaExtractor
{
    public static IReadOnlyList<MediaItem> ExtractFromPost(PostRecord record)
    {
        return record.Value.Embed is { } embed ? ExtractFromEmbed(embed) : [];
    }

    public static string? GetQuoteUri(PostRecord record) =>
        record.Value.Embed?.Record?.StrongRefUri;

    private static IReadOnlyList<MediaItem> ExtractFromEmbed(PostEmbed embed)
    {
        return embed.Type switch
        {
            "app.bsky.embed.images" => ExtractImages(embed.Images),
            "app.bsky.embed.gallery" => ExtractImages(embed.GalleryItems),
            "app.bsky.embed.video" when embed.Video is not null =>
            [
                new MediaItem.Video(embed.Video),
            ],
            "app.bsky.embed.recordWithMedia" when embed.Media is not null => ExtractFromEmbed(
                embed.Media
            ),
            _ => [],
        };
    }

    private static List<MediaItem> ExtractImages(List<ImageEntry>? images)
    {
        return images?.Select(e => (MediaItem)new MediaItem.Image(e.Image)).ToList() ?? [];
    }
}
