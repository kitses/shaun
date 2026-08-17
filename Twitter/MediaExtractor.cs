using System.Globalization;

namespace Shaun.Twitter;

internal static class MediaExtractor
{
    public static (IReadOnlyList<DownloadTask> Tasks, int MediaCount) ExtractFromTweet(
        TweetData tweet,
        string statusId,
        string filenamePattern,
        AdditionalAuthorBlocks<User> additionalAuthors,
        int mediaOffset
    )
    {
        var unwrapped = tweet.Unwrapped;
        var legacy = unwrapped.Legacy;
        var user = unwrapped.Core?.UserResults?.Result?.Legacy;
        var media = legacy?.Media?.Media;

        if (legacy is null || user is null || media is not { Count: > 0 })
        {
            return ([], 0);
        }

        DateTimeOffset? postedAt = DateTimeOffset.TryParseExact(
            legacy.CreatedAt,
            @"ddd MMM dd HH:mm:ss \+0000 yyyy",
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal,
            out DateTimeOffset parsed
        )
            ? parsed
            : null;

        string timestamp = (postedAt ?? DateTimeOffset.UnixEpoch).ToString(
            "yyyyMMddHHmmss",
            CultureInfo.InvariantCulture
        );

        List<DownloadTask> tasks = [];
        for (int i = 0; i < media.Count; i++)
        {
            DownloadTask? task = CreateDownloadTask(
                media[i],
                filenamePattern,
                user,
                statusId,
                timestamp,
                postedAt,
                i,
                media.Count,
                additionalAuthors.ForMedia(mediaOffset + i)
            );

            if (task is not null)
            {
                tasks.Add(task);
            }
        }

        return (tasks, media.Count);
    }

    private static DownloadTask? CreateDownloadTask(
        MediaItem item,
        string filenamePattern,
        User user,
        string statusId,
        string timestamp,
        DateTimeOffset? postedAt,
        int mediaIndex,
        int mediaCount,
        IReadOnlyList<User> additionalAuthors
    )
    {
        string fileType = item.Type.StartsWith("animated_", StringComparison.Ordinal)
            ? item.Type[9..]
            : item.Type;
        bool isPhoto = fileType == "photo";

        string? itemUrl = isPhoto
            ? item.ImageUrl
            : item
                .VideoInfo?.Variants?.Where(v => v.ContentType == "video/mp4")
                .MaxBy(v => v.Bitrate ?? -1)
                ?.Url;

        if (
            string.IsNullOrEmpty(itemUrl) || !Uri.TryCreate(itemUrl, UriKind.Absolute, out Uri? uri)
        )
        {
            return null;
        }

        string mediaUrl = uri.GetLeftPart(UriPartial.Path);
        string downloadUrl = isPhoto ? $"{mediaUrl}:orig" : mediaUrl;
        string extension = Path.GetExtension(mediaUrl).TrimStart('.');
        string fileName = DownloadFileName.Create(
            filenamePattern,
            user.Name,
            user.Handle,
            statusId,
            timestamp,
            fileType,
            mediaIndex,
            mediaCount,
            extension,
            additionalAuthors
        );

        return new DownloadTask(downloadUrl, fileName, postedAt);
    }
}
