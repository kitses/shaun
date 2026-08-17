using System.Net.Http.Json;
using System.Text.Json.Serialization.Metadata;

namespace Shaun.Twitter;

internal sealed class TwitterApi(HttpClient http, string userAgent, string bearer)
{
    public async Task<string> FetchGuestToken(CancellationToken ct)
    {
        var req = new HttpRequestMessage(
            HttpMethod.Post,
            "https://api.twitter.com/1.1/guest/activate.json"
        );
        AddCommonHeaders(req);

        var response = await SendJsonAsync(
            req,
            TwitterJsonContext.Default.GuestTokenResponse,
            ct,
            "Guest token failed"
        );

        return response?.GuestToken ?? throw new Exception("Failed to parse guest token response");
    }

    public async Task<TweetData?> FetchTweetData(
        string statusId,
        Credentials creds,
        string guestToken,
        CancellationToken ct
    )
    {
        string vars =
            $$"""{"tweetId":"{{statusId}}","with_rux_injections":false,"includePromotedContent":true,"withCommunity":true,"withQuickPromoteEligibilityTweetFields":true,"withBirdwatchNotes":true,"withVoice":true,"withV2Timeline":true}""";

        const string feats =
            """{"articles_preview_enabled":true,"c9s_tweet_anatomy_moderator_badge_enabled":true,"freedom_of_speech_not_reach_fetch_enabled":true,"graphql_is_translatable_rweb_tweet_is_translatable_enabled":true,"longform_notetweets_inline_media_enabled":true,"profile_label_improvements_pcf_label_in_post_enabled":true,"responsive_web_twitter_article_tweet_consumption_enabled":true,"rweb_tipjar_consumption_enabled":true,"standardized_nudges_misinfo":true,"tweet_with_visibility_results_prefer_gql_limited_actions_policy_enabled":true,"view_counts_everywhere_api_enabled":true}""";

        // path carries the id of a persisted graphql query, and the feature flags have to match the set that query was
        // saved with or the endpoint rejects the call
        string url =
            "https://twitter.com/i/api/graphql/2ICDjqPd81tulZcYrtpTuQ/TweetResultByRestId"
            + $"?variables={Uri.EscapeDataString(vars)}"
            + $"&features={Uri.EscapeDataString(feats)}";

        var req = new HttpRequestMessage(HttpMethod.Get, url);
        AddCommonHeaders(req);
        AddGraphQlHeaders(req);

        if (creds.HasAuth)
        {
            req.Headers.Add("x-csrf-token", creds.CsrfToken);
            req.Headers.Add("cookie", $"auth_token={creds.AuthToken}; ct0={creds.CsrfToken}");
        }
        else
        {
            req.Headers.Add("x-guest-token", guestToken);
        }

        var response = await SendJsonAsync(req, TwitterJsonContext.Default.GraphQLResponse, ct);

        return response?.Data?.TweetResult?.Result;
    }

    // user lookups reject guest tokens, so this needs cookie credentials even when tweet fetching runs unauthed
    public async Task<User?> FetchUserByScreenNameAsync(
        string screenName,
        Credentials creds,
        CancellationToken ct
    )
    {
        string vars = $$"""{"screen_name":"{{screenName}}","withSafetyModeUserFields":true}""";

        const string feats =
            """{"creator_subscriptions_tweet_preview_api_enabled":true,"hidden_profile_subscriptions_enabled":true,"highlights_tweets_tab_ui_enabled":true,"responsive_web_graphql_exclude_directive_enabled":true,"responsive_web_graphql_skip_user_profile_image_extensions_enabled":false,"responsive_web_graphql_timeline_navigation_enabled":true,"rweb_tipjar_consumption_enabled":true,"subscriptions_feature_can_gift_premium":true,"subscriptions_verification_info_is_identity_verified_enabled":true,"subscriptions_verification_info_verified_since_enabled":true,"verified_phone_label_enabled":false,"responsive_web_twitter_article_notes_tab_enabled":true}""";

        string url =
            "https://twitter.com/i/api/graphql/IGgvgiOx4QZndDHuD3x9TQ/UserByScreenName"
            + $"?variables={Uri.EscapeDataString(vars)}"
            + $"&features={Uri.EscapeDataString(feats)}";

        var req = new HttpRequestMessage(HttpMethod.Get, url);
        AddCommonHeaders(req);
        AddGraphQlHeaders(req);
        req.Headers.Add("x-csrf-token", creds.CsrfToken);
        req.Headers.Add("cookie", $"auth_token={creds.AuthToken}; ct0={creds.CsrfToken}");

        var response = await SendJsonAsync(req, TwitterJsonContext.Default.UserLookupResponse, ct);

        return response?.Data?.User?.Result?.Core;
    }

    private async Task<T> SendJsonAsync<T>(
        HttpRequestMessage request,
        JsonTypeInfo<T> typeInfo,
        CancellationToken ct,
        string? errorPrefix = null
    )
    {
        using (request)
        using (var response = await http.SendAsync(request, ct))
        {
            if (!response.IsSuccessStatusCode)
            {
                string prefix = errorPrefix is null ? "HTTP" : $"{errorPrefix}: HTTP";
                throw new Exception($"{prefix} {(int)response.StatusCode}");
            }

            var result = await response.Content.ReadFromJsonAsync(typeInfo, ct);
            return result ?? throw new Exception("Failed to parse JSON response");
        }
    }

    private void AddCommonHeaders(HttpRequestMessage request)
    {
        request.Headers.Add("authorization", bearer);
        request.Headers.Add("user-agent", userAgent);
    }

    private static void AddGraphQlHeaders(HttpRequestMessage request)
    {
        request.Headers.Add("x-twitter-active-user", "yes");
        request.Headers.Add("x-twitter-client-language", "en");
    }
}
