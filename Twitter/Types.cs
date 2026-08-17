using System.Text.Json.Serialization;

namespace Shaun.Twitter;

internal record ParsedPostUrl(string StatusId);

internal record Credentials(string Pattern, string AuthToken, string CsrfToken)
{
    public bool HasAuth => !string.IsNullOrEmpty(CsrfToken);
}

internal record GuestTokenResponse([property: JsonPropertyName("guest_token")] string GuestToken);

internal record GraphQLResponse(GraphQLData? Data);

internal record GraphQLData(TweetResultContainer? TweetResult);

internal record TweetResultContainer(TweetData? Result);

internal record UserLookupResponse(UserLookupData? Data);

internal record UserLookupData(UserLookupContainer? User);

internal record UserLookupContainer(UserLookupResult? Result);

internal record UserLookupResult(User? Core);

internal record TweetData(TweetLegacy? Legacy, TweetCore? Core, TweetData? Tweet)
{
    // TweetWithVisibilityResults replies hide the actual tweet one level deeper in the tweet field, every other form
    // arrives flat like my ass
    public TweetData Unwrapped => Tweet ?? this;
}

internal record TweetCore([property: JsonPropertyName("user_results")] UserResults UserResults);

internal record UserResults(UserResult Result);

internal record UserResult(User Legacy);

internal record User(
    string Name = "unknown",
    [property: JsonPropertyName("screen_name")] string Handle = "unknown"
);

internal record TweetLegacy(
    [property: JsonPropertyName("created_at")] string CreatedAt,
    [property: JsonPropertyName("extended_entities")] MediaEntities? Media = null
);

internal record MediaEntities(List<MediaItem>? Media = null);

internal record MediaItem(
    string Type,
    [property: JsonPropertyName("media_url_https")] string? ImageUrl = null,
    [property: JsonPropertyName("video_info")] VideoInfo? VideoInfo = null
);

internal record VideoInfo(List<VideoVariant> Variants);

internal record VideoVariant(
    [property: JsonPropertyName("content_type")] string ContentType,
    int? Bitrate = null,
    string? Url = null
);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(GraphQLResponse))]
[JsonSerializable(typeof(GuestTokenResponse))]
[JsonSerializable(typeof(UserLookupResponse))]
internal partial class TwitterJsonContext : JsonSerializerContext { }
