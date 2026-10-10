namespace Shaun.Twitter;

internal sealed class DownloadPlanner(
    TwitterApi api,
    Credentials credentials,
    string guestToken,
    AdditionalAuthorBlocks<User> additionalAuthors
)
{
    public async Task<(
        DownloadTask[] Tasks,
        int MediaCount,
        TweetData Tweet
    )> CollectDownloadTasksAsync(ParsedPostUrl postUrl, int mediaOffset, CancellationToken ct)
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

        return ([.. tasks], mediaCount, tweet);
    }

    public async Task<DownloadTask[]> CollectQuoteTasksAsync(
        string primaryStatusId,
        TweetData primaryTweet,
        IProgress<string>? progress,
        CancellationToken ct
    )
    {
        List<DownloadTask> tasks = [];
        var noExtraAuthors = AdditionalAuthorBlocks<User>.None;
        var visited = new HashSet<string>(StringComparer.Ordinal) { primaryStatusId };

        await WalkAsync(primaryTweet, 0);
        return [.. tasks];

        async Task WalkAsync(TweetData tweet, int depth)
        {
            if (depth >= QuoteWalk.MaxDepth)
                return;

            string? quotedStatusId = tweet.Unwrapped.QuotedStatusResult?.Result?.Unwrapped.RestId;
            if (quotedStatusId is null || !visited.Add(quotedStatusId))
                return;

            TweetData? quotedTweet;
            try
            {
                quotedTweet = await api.FetchTweetData(quotedStatusId, credentials, guestToken, ct);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                progress?.Report(
                    $"Warning: quoted tweet not fetched ({quotedStatusId}): {e.Message}"
                );
                return;
            }

            if (quotedTweet is null)
            {
                progress?.Report($"Warning: quoted tweet not found: {quotedStatusId}");
                return;
            }

            var (quoteTasks, _) = MediaExtractor.ExtractFromTweet(
                quotedTweet,
                quotedStatusId,
                credentials.Pattern,
                noExtraAuthors,
                0
            );
            tasks.AddRange(quoteTasks);

            await WalkAsync(quotedTweet, depth + 1);
        }
    }
}
