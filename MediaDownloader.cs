namespace Shaun;

internal sealed class MediaDownloader
{
    private const int MaxParallelDownloads = 8;

    private readonly HttpClient _http;
    private readonly string _outputDirectory;
    private readonly bool _preserveOrder;

    public MediaDownloader(HttpClient http, string outputDirectory, bool preserveOrder)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentException.ThrowIfNullOrEmpty(outputDirectory);
        _http = http;
        _outputDirectory = outputDirectory;
        _preserveOrder = preserveOrder;
    }

    public async Task<DownloadResult> DownloadSerialAsync(
        IReadOnlyList<DownloadTask> tasks,
        IProgress<string>? progress,
        CancellationToken ct
    )
    {
        List<DownloadError> errors = [];

        foreach (DownloadTask task in tasks)
        {
            progress?.Report(task.FileName);

            try
            {
                await DownloadOneAsync(task, ct);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                errors.Add(new DownloadError(task.FileName, e.Message));
            }
        }

        return new DownloadResult(errors.Count, errors);
    }

    public async Task<DownloadResult> DownloadParallelAsync(
        IReadOnlyList<DownloadTask> tasks,
        IProgress<string>? progress,
        CancellationToken ct
    )
    {
        using var semaphore = new SemaphoreSlim(MaxParallelDownloads);
        List<DownloadError> errors = [];
        object errorsLock = new();

        var downloads = tasks.Select(task =>
            DownloadWithConcurrencyLimitAsync(task, semaphore, errors, errorsLock, progress, ct)
        );

        await Task.WhenAll(downloads);

        // downloads finish in a near-random order, so rewrite mtimes in task order one second apart to satisfy my
        // pedantism
        if (_preserveOrder)
        {
            DateTime baseTime = DateTime.UtcNow;
            string[] paths = [.. tasks.Select(t => Path.Combine(_outputDirectory, t.FileName))];
            ApplyStableModificationTimes(paths, baseTime, progress);
        }

        return new DownloadResult(errors.Count, errors);
    }

    private async Task DownloadWithConcurrencyLimitAsync(
        DownloadTask task,
        SemaphoreSlim semaphore,
        List<DownloadError> errors,
        object errorsLock,
        IProgress<string>? progress,
        CancellationToken ct
    )
    {
        await semaphore.WaitAsync(ct);
        try
        {
            progress?.Report(task.FileName);
            await DownloadOneAsync(task, ct);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            lock (errorsLock)
            {
                errors.Add(new DownloadError(task.FileName, e.Message));
            }
        }
        finally
        {
            semaphore.Release();
        }
    }

    private async Task DownloadOneAsync(DownloadTask task, CancellationToken ct)
    {
        string fullPath = Path.Combine(_outputDirectory, task.FileName);
        string? directory = Path.GetDirectoryName(fullPath);
        if (directory is not null)
        {
            Directory.CreateDirectory(directory);
        }

        try
        {
            using var res = await _http.GetAsync(
                task.Url,
                HttpCompletionOption.ResponseHeadersRead,
                ct
            );

            if (!res.IsSuccessStatusCode)
            {
                throw new Exception($"HTTP {(int)res.StatusCode}");
            }

            using var fs = new FileStream(
                fullPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.Read,
                4096,
                FileOptions.Asynchronous
            );
            await res.Content.CopyToAsync(fs, ct);
        }
        catch
        {
            if (File.Exists(fullPath))
            {
                try
                {
                    File.Delete(fullPath);
                }
                catch { }
            }
            throw;
        }
    }

    private static void ApplyStableModificationTimes(
        string[] paths,
        DateTime baseTime,
        IProgress<string>? progress
    )
    {
        for (int i = 0; i < paths.Length; i++)
        {
            string file = paths[i];
            if (!File.Exists(file))
            {
                continue;
            }

            try
            {
                File.SetLastWriteTimeUtc(file, baseTime.AddSeconds(i));
            }
            catch (Exception e)
            {
                progress?.Report($"Error setting mtime for {file}: {e.Message}");
            }
        }
    }
}
