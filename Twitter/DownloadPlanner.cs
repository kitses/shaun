namespace Shaun.Twitter;

internal sealed class DownloadPlanner(
    TwitterApi api,
    Credentials credentials,
    string guestToken,
    AdditionalAuthorBlocks<User> additionalAuthors
)
{
    public async Task<(DownloadTask[] Tasks, int MediaCount)> CollectDownloadTasksAsync(
        ParsedPostUrl postUrl,
        int mediaOffset,
        CancellationToken ct
    )
    {
        TweetData? tweet =
            await api.FetchTweetData(postUrl.StatusId, credentials, guestToken, ct)
            ?? throw new Exception($"Tweet not found: {postUrl.StatusId}");

        var (tasks, mediaCount) = MediaExtractor.ExtractFromTweet(
            tweet,
            postUrl.StatusId,
            credentials.Pattern,
            additionalAuthors,
            mediaOffset
        );

        return ([.. tasks], mediaCount);
    }
}
