using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace Shaun.Bluesky;

internal static partial class BlueskyPostUrl
{
    [GeneratedRegex(@"https?://[^/]+/(?:profile/)?([^/]+)/(?:post/)?([^/?#]+)")]
    private static partial Regex UrlRegex();

    public static bool TryParse(string url, [NotNullWhen(true)] out ParsedPostUrl? parsedUrl)
    {
        Match match = UrlRegex().Match(url);
        if (!match.Success)
        {
            parsedUrl = null;
            return false;
        }

        parsedUrl = new ParsedPostUrl(match.Groups[1].Value, match.Groups[2].Value);
        return true;
    }
}
