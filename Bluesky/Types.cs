using System.Text.Json.Serialization;

namespace Shaun.Bluesky;

internal record ParsedPostUrl(string HandleOrDid, string RecordKey);

internal record AuthorInfo(string Did, string Handle, string PdsEndpoint)
{
    // did:plc: covers nearly every account except mental illnesses (i.e. did:web: et al)
    public string ShortDid => Did.StartsWith("did:plc:") ? Did[8..] : Did;
}

internal abstract record MediaItem(BlobInfo Blob)
{
    public abstract string FileExtension { get; }

    internal sealed record Image(BlobInfo Blob) : MediaItem(Blob)
    {
        public override string FileExtension =>
            Blob.MimeType.Contains("png", StringComparison.OrdinalIgnoreCase) ? "png" : "jpg";
    }

    internal sealed record Video(BlobInfo Blob) : MediaItem(Blob)
    {
        public override string FileExtension => "mp4";
    }
}

internal record DidDocument(
    List<Service> Service,
    [property: JsonPropertyName("alsoKnownAs")] List<string>? AlsoKnownAs
);

internal record Service(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("serviceEndpoint")] string ServiceEndpoint
);

internal record PostRecord(PostValue Value);

internal record PostValue(
    PostEmbed? Embed,
    [property: JsonPropertyName("createdAt")] string? CreatedAt = null
);

internal record PostEmbed(
    [property: JsonPropertyName("$type")] string Type,
    List<ImageEntry>? Images = null,
    BlobInfo? Video = null,
    [property: JsonPropertyName("items")] List<ImageEntry>? GalleryItems = null,
    PostEmbed? Media = null
);

internal record ImageEntry(BlobInfo Image);

internal record BlobInfo(
    [property: JsonPropertyName("ref")] BlobReference Ref,
    [property: JsonPropertyName("mimeType")] string MimeType
);

internal record BlobReference([property: JsonPropertyName("$link")] string Link);

internal record HandleResolution(string Did);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(HandleResolution))]
[JsonSerializable(typeof(DidDocument))]
[JsonSerializable(typeof(PostRecord))]
internal partial class BlueskyJsonContext : JsonSerializerContext { }
