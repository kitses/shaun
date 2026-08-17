using System.Net.Http.Json;
using System.Text.Json.Serialization.Metadata;

namespace Shaun.Bluesky;

internal sealed class BlueskyApi(HttpClient http)
{
    private const string PlcDirectoryBase = "https://plc.directory";
    private const string BskySocialBase = "https://bsky.social";

    private async Task<T> GetJsonAsync<T>(
        string url,
        JsonTypeInfo<T> typeInfo,
        CancellationToken ct
    )
    {
        using var res = await http.GetAsync(url, ct);
        if (!res.IsSuccessStatusCode)
        {
            throw new Exception($"HTTP request failed with status {res.StatusCode}");
        }

        var result = await res.Content.ReadFromJsonAsync(typeInfo, ct);
        return result ?? throw new Exception("Failed to parse JSON response");
    }

    public static string GetBlobUrl(string pdsEndpoint, string did, string contentId) =>
        $"{pdsEndpoint}/xrpc/com.atproto.sync.getBlob?did={Uri.EscapeDataString(did)}&cid={Uri.EscapeDataString(contentId)}";

    public async Task<string> ResolveToDidAsync(string handle, CancellationToken ct)
    {
        if (handle.StartsWith("did:"))
        {
            return handle;
        }

        string resolveUrl =
            $"{BskySocialBase}/xrpc/com.atproto.identity.resolveHandle?handle={Uri.EscapeDataString(handle)}";

        var response = await GetJsonAsync(
            resolveUrl,
            BlueskyJsonContext.Default.HandleResolution,
            ct
        );
        return response.Did;
    }

    public async Task<(string PdsEndpoint, string Handle)> FetchPdsAndHandleAsync(
        string did,
        CancellationToken ct
    )
    {
        string plcUrl = $"{PlcDirectoryBase}/{Uri.EscapeDataString(did)}";
        var document = await GetJsonAsync(plcUrl, BlueskyJsonContext.Default.DidDocument, ct);

        var pdsService =
            document.Service.FirstOrDefault(s => s.Type == "AtprotoPersonalDataServer")
            ?? throw new Exception("No AtprotoPersonalDataServer found in DID document");

        string pdsEndpoint = pdsService.ServiceEndpoint.TrimEnd('/');

        // .bsky.social handles are dead weight so trim that shit
        string handleName = did;
        if (document.AlsoKnownAs is { Count: > 0 })
        {
            string akaUri = document.AlsoKnownAs[0];
            string stripped = akaUri.StartsWith("at://") ? akaUri[5..] : akaUri;
            handleName = stripped.EndsWith(".bsky.social")
                ? stripped[..^".bsky.social".Length]
                : stripped;
        }

        return (pdsEndpoint, handleName);
    }

    public async Task<PostRecord> FetchPostRecordAsync(
        string pdsEndpoint,
        string did,
        string recordKey,
        CancellationToken ct
    )
    {
        string recordUrl =
            $"{pdsEndpoint}/xrpc/com.atproto.repo.getRecord?repo={Uri.EscapeDataString(did)}&collection=app.bsky.feed.post&rkey={Uri.EscapeDataString(recordKey)}";

        return await GetJsonAsync(recordUrl, BlueskyJsonContext.Default.PostRecord, ct);
    }

    public async Task<AuthorInfo> ResolveAuthorAsync(string handleOrDid, CancellationToken ct)
    {
        string did = await ResolveToDidAsync(handleOrDid, ct);
        var (pdsEndpoint, handle) = await FetchPdsAndHandleAsync(did, ct);
        return new AuthorInfo(did, handle, pdsEndpoint);
    }
}
