namespace PandoraOverlay;

/// <summary>The Dino storage page's list request: the list (possibly the session's earlier copy), and what went wrong, if anything.</summary>
public sealed record StorageListResult(StoredDinoList? List, string? Problem = null);

/// <summary>
/// PollService, Dino storage part (1.33): everything the Dino storage page
/// asks of the network, gated here so the window stays a pure consumer.
/// Nothing in this file runs on a timer: the list is fetched every time the
/// page is opened, as the website's own page fetches it on every visit
/// (the owner's call, Oct 8 2026 — the storage changes with play), and for
/// Refresh; a rename or delete is sent only for a click. Same class as
/// PollService.cs, split for reading; see CLAUDE.md.
/// </summary>
public sealed partial class PollService
{
    /// <summary>The breather after every rename or delete, whatever its outcome: one change at a time, never a burst.</summary>
    private static readonly TimeSpan StorageEditGuard = TimeSpan.FromSeconds(3);

    private bool _storageListBusy;
    private bool _storageEditBusy;
    private DateTime _storageEditGuardUntilUtc;

    /// <summary>The last list fetched this session, kept up to date by the page's renames and deletes; null until the page was opened once.</summary>
    public StoredDinoList? Storage { get; private set; }

    /// <summary>
    /// The storage for the page: one request per call, but never two at
    /// once (a call while one is out answers with the session's copy). A
    /// failure keeps the last good copy and names the problem.
    /// </summary>
    public async Task<StorageListResult> GetStorageAsync()
    {
        if (_storageListBusy)
        {
            return new StorageListResult(Storage, Storage is null ? "still loading, try again in a moment" : null);
        }

        _storageListBusy = true;
        try
        {
            var answer = await _client.FetchStoredDinosAsync();
            if (answer.List is null)
            {
                return new StorageListResult(Storage, answer.Declined is { } said ? $"the website said: {said}" : "the website declined");
            }
            Storage = answer.List;
            return new StorageListResult(Storage);
        }
        catch (SessionEndedException)
        {
            return new StorageListResult(Storage, "the website session has ended, sign in again");
        }
        catch (Exception ex)
        {
            return new StorageListResult(Storage, ex.GetType().Name);
        }
        finally
        {
            _storageListBusy = false;
        }
    }

    /// <summary>One USER-TRIGGERED rename (a Save click on a card) — never call this from a timer. The name and description are cleaned here.</summary>
    public Task<StorageEditResult> RenameStoredDinoAsync(StoredDino dino, string name, string description)
    {
        var cleanName = StoredDinos.CleanName(name);
        var cleanDescription = StoredDinos.CleanDescription(description);
        return EditStorageAsync(dino, () => _client.RenameStoredDinoAsync(dino, cleanName, cleanDescription), deletes: false);
    }

    /// <summary>One USER-TRIGGERED delete (the armed Delete on a card) — never call this from a timer. It cannot be undone.</summary>
    public Task<StorageEditResult> DeleteStoredDinoAsync(StoredDino dino) =>
        EditStorageAsync(dino, () => _client.DeleteStoredDinoAsync(dino), deletes: true);

    /// <summary>
    /// The shared gate of the two writes: one at a time with a short
    /// breather after each, and none while the session is known to be over
    /// (answered locally, nothing sent). A success updates the session's
    /// copy, so a page reopened within the floor shows the change.
    /// </summary>
    private async Task<StorageEditResult> EditStorageAsync(StoredDino dino, Func<Task<StorageEditResult>> send, bool deletes)
    {
        if (IsSignedOut) return new StorageEditResult(StorageEditOutcome.SignedOut);
        if (_storageEditBusy || DateTime.UtcNow < _storageEditGuardUntilUtc) return new StorageEditResult(StorageEditOutcome.TooSoon);

        _storageEditBusy = true;
        try
        {
            _storageEditGuardUntilUtc = DateTime.UtcNow + StorageEditGuard;
            var result = await send();
            if (result.Outcome == StorageEditOutcome.Ok && Storage is not null)
            {
                Storage = deletes ? Storage.Without(dino.Id) : result.Dino is { } renamed ? Storage.With(renamed) : Storage;
            }
            return result;
        }
        catch (SessionEndedException)
        {
            return new StorageEditResult(StorageEditOutcome.SignedOut);
        }
        catch (Exception ex)
        {
            return new StorageEditResult(StorageEditOutcome.Failed, ex.GetType().Name);
        }
        finally
        {
            _storageEditBusy = false;
        }
    }

    /// <summary>A different login has a different storage: forget the list.</summary>
    private void ForgetStorage() => Storage = null;
}
