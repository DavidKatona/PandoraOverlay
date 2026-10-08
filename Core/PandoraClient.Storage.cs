using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace PandoraOverlay;

/// <summary>
/// PandoraClient, Dino storage part (1.33): the three calls behind the
/// website's /extras Dino Storage page — the list (a read) and its two
/// WRITES, rename and delete. The writes are only ever sent for a click
/// (PollService.Storage holds the gating). All three are the site's own
/// requests: cookie-authed POSTs from the extras page, the writes with the
/// same JSON bodies its buttons send. Like the site, every answer's body is
/// read whatever the HTTP status (a refusal arrives as an error body with a
/// message); the site's "no session" answer is told apart, as for the poll.
/// Same class as PandoraClient.cs, split for reading; see CLAUDE.md.
/// </summary>
public sealed partial class PandoraClient
{
    private const string StorageEndpoint = "https://islapandora.eu/api/user/dinos";
    private const string StorageRenameEndpoint = "https://islapandora.eu/api/user/dinos/rename";
    private const string StorageDeleteEndpoint = "https://islapandora.eu/api/user/dinos/delete";
    private const string ExtrasPage = "https://islapandora.eu/extras"; // the page these calls come from on the site

    /// <summary>The account's stored dinos and its slot count — the empty-bodied POST the site's page sends on load.</summary>
    public async Task<StoredDinoFetch> FetchStoredDinosAsync(CancellationToken ct = default)
    {
        using var response = await SendStorageAsync(StorageEndpoint, null, ct).ConfigureAwait(false);
        using var doc = await ReadStorageAnswerAsync(response, ct).ConfigureAwait(false);
        return StoredDinos.ParseList(doc.RootElement) is { } list
            ? new StoredDinoFetch(list)
            : new StoredDinoFetch(null, StoredDinos.ServerWords(doc.RootElement));
    }

    /// <summary>Renames one stored dino and sets its description: {dinoId, name, description}. NEVER on a timer or without a click.</summary>
    public async Task<StorageEditResult> RenameStoredDinoAsync(StoredDino dino, string name, string description,
                                                               CancellationToken ct = default)
    {
        using var response = await SendStorageAsync(StorageRenameEndpoint, StoredDinos.RenameBody(dino, name, description), ct)
            .ConfigureAwait(false);
        using var doc = await ReadStorageAnswerAsync(response, ct).ConfigureAwait(false);
        return StoredDinos.ParseRename(doc.RootElement, dino, name, description);
    }

    /// <summary>Deletes one stored dino for good: {dinoId}. NEVER on a timer or without a click.</summary>
    public async Task<StorageEditResult> DeleteStoredDinoAsync(StoredDino dino, CancellationToken ct = default)
    {
        using var response = await SendStorageAsync(StorageDeleteEndpoint, StoredDinos.DeleteBody(dino), ct).ConfigureAwait(false);
        using var doc = await ReadStorageAnswerAsync(response, ct).ConfigureAwait(false);
        return StoredDinos.ParseDelete(doc.RootElement);
    }

    /// <param name="json">The request body, or null for the list's empty one.</param>
    private async Task<HttpResponseMessage> SendStorageAsync(string endpoint, string? json, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = json is null
                ? new ByteArrayContent(Array.Empty<byte>())
                : new StringContent(json, Encoding.UTF8, "application/json")
        };
        request.Headers.TryAddWithoutValidation("Cookie", _cookie);
        request.Headers.TryAddWithoutValidation("Referer", ExtrasPage);

        var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        UpdateRollingCookie(response);
        if (IsRefusal((int)response.StatusCode, response.Content.Headers.ContentType?.MediaType))
        {
            response.Dispose();
            throw new SessionEndedException();
        }
        return response;
    }

    /// <summary>The answer's JSON whatever the status; a body that isn't JSON is a failure (the status's, if it was one).</summary>
    private static async Task<JsonDocument> ReadStorageAnswerAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var bytes = await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
        try
        {
            return JsonDocument.Parse(bytes);
        }
        catch (JsonException)
        {
            response.EnsureSuccessStatusCode();
            throw;
        }
    }
}
