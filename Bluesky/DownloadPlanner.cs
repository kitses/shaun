using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Shaun.Bluesky;

internal sealed class DownloadPlanner(
    BlueskyApi api,
    AdditionalAuthorBlocks<AuthorInfo> additionalAuthors,
    bool compact
)
{
    public async Task<(
        DownloadTask[] Tasks,
        int MediaCount,
        PostRecord Record
    )> CollectDownloadTasksAsync(
        ParsedPostUrl postUrl,
        AuthorInfo author,
        int mediaOffset,
        CancellationToken ct
    )
    {
        var postRecord = await api.FetchPostRecordAsync(
            author.PdsEndpoint,
            author.Did,
            postUrl.RecordKey,
            ct
        );

        IReadOnlyList<MediaItem> media = MediaExtractor.ExtractFromPost(postRecord);
        if (media.Count == 0)
        {
            return ([], 0, postRecord);
        }

        return (
            [
                .. BuildDownloadTasks(
                    author,
                    media,
                    postUrl.RecordKey,
                    postRecord,
                    additionalAuthors,
                    mediaOffset
                ),
            ],
            media.Count,
            postRecord
        );
    }

    public async Task<DownloadTask[]> CollectQuoteTasksAsync(
        string primaryRecordKey,
        AuthorInfo primaryAuthor,
        PostRecord primaryPostRecord,
        IProgress<string>? progress,
        CancellationToken ct
    )
    {
        List<DownloadTask> tasks = [];
        var noExtraAuthors = AdditionalAuthorBlocks<AuthorInfo>.None;
        var visited = new HashSet<string>(StringComparer.Ordinal)
        {
            $"at://{primaryAuthor.Did}/app.bsky.feed.post/{primaryRecordKey}",
        };

        await WalkAsync(primaryPostRecord, 0);
        return [.. tasks];

        async Task WalkAsync(PostRecord postRecord, int depth)
        {
            if (depth >= QuoteWalk.MaxDepth)
                return;

            string? quoteUri = MediaExtractor.GetQuoteUri(postRecord);
            if (quoteUri is not null)
                await VisitAsync(quoteUri, depth + 1);
        }

        async Task VisitAsync(string quoteUri, int depth)
        {
            if (!visited.Add(quoteUri))
                return;

            if (!TryParseQuoteUri(quoteUri, out string? repo, out string? recordKey))
            {
                progress?.Report($"Warning: unrecognised quoted post URI: {quoteUri}");
                return;
            }

            AuthorInfo author;
            PostRecord quotedPostRecord;
            try
            {
                author = await api.ResolveAuthorAsync(repo, ct);
                quotedPostRecord = await api.FetchPostRecordAsync(
                    author.PdsEndpoint,
                    author.Did,
                    recordKey,
                    ct
                );
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                progress?.Report($"Warning: quoted post not fetched ({quoteUri}): {e.Message}");
                return;
            }

            IReadOnlyList<MediaItem> media = MediaExtractor.ExtractFromPost(quotedPostRecord);
            if (media.Count > 0)
            {
                tasks.AddRange(
                    BuildDownloadTasks(
                        author,
                        media,
                        recordKey,
                        quotedPostRecord,
                        noExtraAuthors,
                        0
                    )
                );
            }

            await WalkAsync(quotedPostRecord, depth + 1);
        }
    }

    private static bool TryParseQuoteUri(
        string uri,
        [NotNullWhen(true)] out string? repo,
        [NotNullWhen(true)] out string? recordKey
    )
    {
        repo = null;
        recordKey = null;
        if (!uri.StartsWith("at://"))
            return false;

        string[] segments = uri[5..].Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length != 3 || segments[1] != "app.bsky.feed.post")
            return false;

        repo = segments[0];
        recordKey = segments[2];
        return true;
    }

    private List<DownloadTask> BuildDownloadTasks(
        AuthorInfo author,
        IReadOnlyList<MediaItem> media,
        string recordKey,
        PostRecord postRecord,
        AdditionalAuthorBlocks<AuthorInfo> blocks,
        int mediaOffset
    )
    {
        string? createdAt = postRecord.Value.CreatedAt;
        DateTimeOffset? postedAt =
            !string.IsNullOrEmpty(createdAt)
            && DateTimeOffset.TryParse(
                createdAt,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out DateTimeOffset parsed
            )
                ? parsed
                : null;

        List<DownloadTask> tasks = [];
        for (int i = 0; i < media.Count; i++)
        {
            MediaItem item = media[i];
            string contentId = item.Blob.Ref.Link;
            if (string.IsNullOrEmpty(contentId))
                continue;

            string fileName = DownloadFileName.Create(
                author,
                blocks.ForMedia(mediaOffset + i),
                recordKey,
                i,
                media.Count,
                item.FileExtension,
                compact
            );

            string blobUrl = BlueskyApi.GetBlobUrl(author.PdsEndpoint, author.Did, contentId);
            tasks.Add(new DownloadTask(blobUrl, fileName, postedAt));
        }

        return tasks;
    }
}
