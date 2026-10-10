using Shaun.Bluesky;
using Shaun.Twitter;

namespace Shaun;

internal static class Backends
{
    private const string UserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 "
        + "(KHTML, like Gecko) Chrome/146.0.0.0 Safari/537.36";

    // twitter ships this bearer token to every web browser it serves; public and works for both guest and cookie
    // authed requests
    private const string Bearer =
        "Bearer AAAAAAAAAAAAAAAAAAAAANRILgAAAAAAnNwIzUejRCOuH5E6I8xnZz4"
        + "puTs%3D1Zv7ttfk8LF81IUq16cHjhLTvJu4FA33AGWWjCpTnA";

    private static readonly HttpClient Http = new(
        new SocketsHttpHandler { AllowAutoRedirect = true }
    );

    public static async Task<RunResult> RunAsync(
        DownloadOptions options,
        IProgress<string>? progress,
        CancellationToken ct
    )
    {
        List<RoutedUrl> routed = RouteUrls(options.Urls);
        if (routed.Count == 0)
            return new RunResult(0, 0, []);

        List<string> errors = [];
        List<DownloadTask> tasks = [];
        List<Task<DownloadTask[]>> quoteCollections = [];
        Twitter.DownloadPlanner? twitter = null;
        Bluesky.DownloadPlanner? bluesky = null;
        Dictionary<string, AuthorInfo>? blueskyAuthors = null;
        AdditionalAuthorBlocks<AuthorInfo>? blueskyAdditional = null;
        AdditionalAuthorBlocks<User>? twitterAdditional = null;

        // bang-delimited --also blocks map onto attachments by position across the whole run, so every post starts
        // from the number of attachments already planned
        int mediaOffset = 0;

        if (routed.Any(u => u is RoutedUrl.Twitter))
        {
            Credentials creds = CredentialsStore.Load();
            var api = new TwitterApi(Http, UserAgent, Bearer);
            string guestToken = creds.HasAuth ? "" : await api.FetchGuestToken(ct);

            if (creds.HasAuth)
            {
                twitterAdditional = await ResolveAdditionalAuthorsAsync(
                    options.Also,
                    async (id, ct) =>
                        await api.FetchUserByScreenNameAsync(id, creds, ct)
                        ?? throw new Exception($"user @{id} not found"),
                    NormalizeTwitterIdentifier,
                    progress,
                    ct
                );
            }
            else if (!string.IsNullOrWhiteSpace(options.Also))
            {
                progress?.Report(
                    "Warning: --also needs cookie auth (auth_token + ct0 in ~/.shaun.ini). Ignoring additional authors."
                );
            }

            twitter = new Twitter.DownloadPlanner(
                api,
                creds,
                guestToken,
                twitterAdditional ?? AdditionalAuthorBlocks<User>.None
            );
        }

        if (routed.Any(u => u is RoutedUrl.Bluesky))
        {
            var api = new BlueskyApi(Http);
            blueskyAdditional = await ResolveAdditionalAuthorsAsync(
                options.Also,
                (id, ct) => api.ResolveAuthorAsync(id, ct),
                NormalizeAuthorIdentifier,
                progress,
                ct
            );
            bluesky = new Bluesky.DownloadPlanner(api, blueskyAdditional, options.Compact);
            blueskyAuthors = await ResolveBlueskyPostAuthorsAsync(api, routed, errors, ct);
        }

        foreach (RoutedUrl routedUrl in routed)
        {
            try
            {
                DownloadTask[] collected = await CollectTasksAsync(routedUrl);
                tasks.AddRange(collected);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                errors.Add($"{routedUrl.Url}: {e.Message}");
            }
        }

        WarnExtraAuthorBlocks(twitterAdditional, mediaOffset, progress);
        WarnExtraAuthorBlocks(blueskyAdditional, mediaOffset, progress);

        DownloadTask[] primaryUnique = [.. tasks.DistinctBy(t => t.FileName)];
        if (primaryUnique.Length == 0 && quoteCollections.Count == 0)
            return new RunResult(0, errors.Count, errors);

        // eeeyuph.
        var downloader = new MediaDownloader(
            Http,
            options.OutputDirectory,
            preserveOrder: !options.Mtime
        );

        int attempted = 0;
        int failed = 0;

        async Task<int> DownloadBatchAsync(IReadOnlyList<DownloadTask> batch)
        {
            if (batch.Count == 0)
                return 0;

            DownloadResult result = options.Parallel
                ? await downloader.DownloadParallelAsync(batch, progress, ct)
                : await downloader.DownloadSerialAsync(batch, progress, ct);

            errors.AddRange(result.Errors.Select(e => $"{e.FileName}: {e.Message}"));
            attempted += batch.Count;
            if (options.Mtime)
                ApplyPostedMtimes(batch, options.OutputDirectory, progress);
            return result.FailedCount;
        }

        var plannedNames = new HashSet<string>(
            primaryUnique.Select(t => t.FileName),
            StringComparer.Ordinal
        );

        failed += await DownloadBatchAsync(primaryUnique);

        // quote walkers fetch quoted posts while the primary media downloads run
        foreach (Task<DownloadTask[]> collection in quoteCollections)
        {
            DownloadTask[] quoteTasks;
            try
            {
                quoteTasks = await collection;
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                errors.Add($"quote posts: {e.Message}");
                continue;
            }

            DownloadTask[] quoteUnique = [.. quoteTasks.Where(t => plannedNames.Add(t.FileName))];
            failed += await DownloadBatchAsync(quoteUnique);
        }

        int downloaded = attempted - failed;
        return new RunResult(downloaded, errors.Count, errors);

        async Task<DownloadTask[]> CollectTasksAsync(RoutedUrl routedUrl) =>
            routedUrl switch
            {
                RoutedUrl.Twitter tw => await CollectTwitterTasksAsync(tw),
                RoutedUrl.Bluesky bs => await CollectBlueskyTasksAsync(bs),
                _ => [],
            };

        async Task<DownloadTask[]> CollectTwitterTasksAsync(RoutedUrl.Twitter tw)
        {
            var (postTasks, mediaCount, tweet) = await twitter!.CollectDownloadTasksAsync(
                tw.Parsed,
                mediaOffset,
                ct
            );
            if (options.Quotes)
            {
                quoteCollections.Add(
                    twitter.CollectQuoteTasksAsync(tw.Parsed.StatusId, tweet, progress, ct)
                );
            }
            mediaOffset += mediaCount;
            return postTasks;
        }

        async Task<DownloadTask[]> CollectBlueskyTasksAsync(RoutedUrl.Bluesky bs)
        {
            if (!blueskyAuthors!.TryGetValue(bs.Parsed.HandleOrDid, out AuthorInfo? author))
                return [];

            var (postTasks, mediaCount, postRecord) = await bluesky!.CollectDownloadTasksAsync(
                bs.Parsed,
                author,
                mediaOffset,
                ct
            );
            if (options.Quotes)
            {
                quoteCollections.Add(
                    bluesky.CollectQuoteTasksAsync(
                        bs.Parsed.RecordKey,
                        author,
                        postRecord,
                        progress,
                        ct
                    )
                );
            }
            mediaOffset += mediaCount;
            return postTasks;
        }
    }

    private static List<RoutedUrl> RouteUrls(string[] urls)
    {
        List<RoutedUrl> routed = [];
        foreach (string raw in urls)
        {
            string url = raw.Trim();
            if (url.Length == 0)
                continue;

            if (TwitterPostUrl.TryParse(url, out var tw))
                routed.Add(new RoutedUrl.Twitter(url, tw));
            else if (BlueskyPostUrl.TryParse(url, out var bs))
                routed.Add(new RoutedUrl.Bluesky(url, bs));
        }
        return routed;
    }

    private static async Task<AdditionalAuthorBlocks<T>> ResolveAdditionalAuthorsAsync<T>(
        string? also,
        Func<string, CancellationToken, Task<T>> resolve,
        Func<string, string> normalize,
        IProgress<string>? progress,
        CancellationToken ct
    )
    {
        if (string.IsNullOrWhiteSpace(also))
        {
            return AdditionalAuthorBlocks<T>.None;
        }

        bool perMedia = also.Contains('!');
        string[] rawBlocks = also.Split('!', StringSplitOptions.TrimEntries);

        // trailing bangs must not become empty blocks because they'd make the extra block warning angry
        int blockCount = rawBlocks.Length;
        while (blockCount > 0 && rawBlocks[blockCount - 1].Length == 0)
            blockCount--;

        string[][] identifiers =
        [
            .. rawBlocks[..blockCount]
                .Select(block =>
                    block
                        .Split(
                            ',',
                            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
                        )
                        .Select(normalize)
                        .ToArray()
                ),
        ];

        string[] distinct =
        [
            .. identifiers.SelectMany(block => block).Distinct(StringComparer.OrdinalIgnoreCase),
        ];

        var byIdentifier = new Dictionary<string, T>(StringComparer.OrdinalIgnoreCase);
        Task<T>[] tasks = [.. distinct.Select(id => resolve(id, ct))];
        foreach ((string id, Task<T> task) in distinct.Zip(tasks))
        {
            try
            {
                byIdentifier[id] = await task;
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                progress?.Report($"Warning: ignoring additional author \"{id}\": {e.Message}");
            }
        }

        IReadOnlyList<IReadOnlyList<T>> blocks =
        [
            .. identifiers.Select(block =>
                (IReadOnlyList<T>)
                    [
                        .. block
                            .Where(id => byIdentifier.ContainsKey(id))
                            .Select(id => byIdentifier[id]),
                    ]
            ),
        ];
        return new AdditionalAuthorBlocks<T>(blocks, perMedia);
    }

    private static async Task<Dictionary<string, AuthorInfo>> ResolveBlueskyPostAuthorsAsync(
        BlueskyApi api,
        IReadOnlyList<RoutedUrl> routed,
        List<string> errors,
        CancellationToken ct
    )
    {
        string[] uniqueIds =
        [
            .. routed
                .OfType<RoutedUrl.Bluesky>()
                .Select(bs => bs.Parsed.HandleOrDid)
                .Distinct(StringComparer.OrdinalIgnoreCase),
        ];

        var authors = new Dictionary<string, AuthorInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (string id in uniqueIds)
        {
            try
            {
                authors[id] = await api.ResolveAuthorAsync(id, ct);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                errors.Add($"author {id}: {e.Message}");
            }
        }
        return authors;
    }

    private static string NormalizeAuthorIdentifier(string author) =>
        author.Contains('.') || author.StartsWith("did:") ? author : $"{author}.bsky.social";

    private static string NormalizeTwitterIdentifier(string handle) =>
        handle.StartsWith('@') ? handle[1..] : handle;

    private static void WarnExtraAuthorBlocks<T>(
        AdditionalAuthorBlocks<T>? additional,
        int mediaOffset,
        IProgress<string>? progress
    )
    {
        if (additional is not { PerMedia: true } || additional.Blocks.Count <= mediaOffset)
            return;

        progress?.Report(
            $"Warning: --also has {additional.Blocks.Count} author block(s) but the given post(s) have {mediaOffset} attachment(s) in total. Ignoring the extra block(s)."
        );
    }

    private static void ApplyPostedMtimes(
        IReadOnlyList<DownloadTask> tasks,
        string outputDirectory,
        IProgress<string>? progress
    )
    {
        foreach (DownloadTask task in tasks)
        {
            if (task.PostedAt is not { } posted)
                continue;

            string path = Path.Combine(outputDirectory, task.FileName);
            if (!File.Exists(path))
                continue;

            try
            {
                File.SetLastWriteTimeUtc(path, posted.UtcDateTime);
            }
            catch (Exception e)
            {
                progress?.Report($"Error setting mtime for {task.FileName}: {e.Message}");
            }
        }
    }
}
