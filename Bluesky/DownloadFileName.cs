namespace Shaun.Bluesky;

internal static class DownloadFileName
{
    public static string Create(
        AuthorInfo author,
        IReadOnlyList<AuthorInfo> additionalAuthors,
        string recordKey,
        int mediaIndex,
        int mediaCount,
        string extension,
        bool compact
    )
    {
        List<string> parts = [author.Handle];
        if (!compact)
        {
            parts.Add(author.ShortDid);
        }

        foreach (AuthorInfo additionalAuthor in additionalAuthors)
        {
            parts.Add(additionalAuthor.Handle);
            if (!compact)
            {
                parts.Add(additionalAuthor.ShortDid);
            }
        }

        string indexSuffix = mediaCount > 1 ? $"-{mediaIndex + 1}" : "";
        return $"{string.Join("-", parts)}-{recordKey}{indexSuffix}.{extension}";
    }
}
