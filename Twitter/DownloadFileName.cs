using System.Text.RegularExpressions;

namespace Shaun.Twitter;

internal static partial class DownloadFileName
{
    // display names carry zero-width characters and emoji such as the age restriction symbol alongside the usual
    // reserved characters, none of which survive on common filesystems
    [GeneratedRegex("""[\\/|<>*?:"\u200b-\u200d\u2060\ufeff\ud83d\udd1e]""")]
    private static partial Regex InvalidCharsRegex();

    public static string Create(
        string pattern,
        string userName,
        string handle,
        string statusId,
        string timestamp,
        string fileType,
        int mediaIndex,
        int mediaCount,
        string extension,
        IReadOnlyList<User> additionalAuthors
    )
    {
        // extra authors have no placeholder of their own, so fold 'em into the user id to keep filenames unique per
        // author set
        string userId = handle;
        foreach (User author in additionalAuthors)
        {
            userId += $"-{InvalidCharsRegex().Replace(author.Handle, "")}";
        }

        string filename = pattern
            .Replace("{user-name}", InvalidCharsRegex().Replace(userName, ""))
            .Replace("{user-id}", userId)
            .Replace("{status-id}", statusId)
            .Replace("{date-time}", timestamp)
            .Replace("{file-type}", fileType);

        if (mediaCount > 1)
        {
            filename += $"-{mediaIndex + 1}";
        }

        return $"{filename}.{extension}";
    }
}
