namespace Shaun;

internal sealed record DownloadTask(string Url, string FileName, DateTimeOffset? PostedAt = null);

internal sealed record DownloadError(string FileName, string Message);

internal sealed record DownloadResult(int FailedCount, IReadOnlyList<DownloadError> Errors);
