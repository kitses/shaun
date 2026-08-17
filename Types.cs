using BlueskyPost = Shaun.Bluesky.ParsedPostUrl;
using TwitterPost = Shaun.Twitter.ParsedPostUrl;

namespace Shaun;

internal abstract record RoutedUrl(string Url)
{
    public sealed record Twitter(string Url, TwitterPost Parsed) : RoutedUrl(Url);

    public sealed record Bluesky(string Url, BlueskyPost Parsed) : RoutedUrl(Url);
}

internal sealed record DownloadOptions(
    string[] Urls,
    string? Also,
    bool Compact,
    bool Parallel,
    bool Mtime,
    string OutputDirectory
);

internal sealed record RunResult(int Downloaded, int Failed, IReadOnlyList<string> Errors);

internal sealed record AdditionalAuthorBlocks<T>(
    IReadOnlyList<IReadOnlyList<T>> Blocks,
    bool PerMedia
)
{
    // per-media input maps one block onto each attachment by position. without bangs the single block applies to every
    // attachment
    public IReadOnlyList<T> ForMedia(int index) =>
        PerMedia
            ? index < Blocks.Count
                ? Blocks[index]
                : []
            : Blocks.Count > 0
                ? Blocks[0]
                : [];
}
