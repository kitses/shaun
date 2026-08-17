using System.Globalization;

namespace Shaun.Bluesky;

internal sealed class DownloadPlanner(
    BlueskyApi api,
    AdditionalAuthorBlocks<AuthorInfo> additionalAuthors,
    bool compact
)
{
    public async Task<(DownloadTask[] Tasks, int MediaCount)> CollectDownloadTasksAsync(
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
            return ([], 0);
        }

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
                additionalAuthors.ForMedia(mediaOffset + i),
                postUrl.RecordKey,
                i,
                media.Count,
                item.FileExtension,
                compact
            );

            string blobUrl = BlueskyApi.GetBlobUrl(author.PdsEndpoint, author.Did, contentId);
            tasks.Add(new DownloadTask(blobUrl, fileName, postedAt));
        }

        return ([.. tasks], media.Count);
    }
}
