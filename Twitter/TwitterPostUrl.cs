using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace Shaun.Twitter;

internal static partial class TwitterPostUrl
{
    [GeneratedRegex(@"(?:twitter\.com|x\.com)/[^/]+/status/(\d+)")]
    private static partial Regex UrlRegex();

    public static bool TryParse(string url, [NotNullWhen(true)] out ParsedPostUrl? parsedUrl)
    {
        Match match = UrlRegex().Match(url);
        if (!match.Success)
        {
            parsedUrl = null;
            return false;
        }

        parsedUrl = new ParsedPostUrl(match.Groups[1].Value);
        return true;
    }
}
