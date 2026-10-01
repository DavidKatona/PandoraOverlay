namespace PandoraOverlay;

/// <summary>The Skins page's list request: the skins (possibly the last good copy), and what went wrong, if anything.</summary>
public sealed record SkinListResult(IReadOnlyList<PatreonSkin>? Skins, string? Problem = null);

/// <summary>
/// PollService, Skins part (v1.28): everything the Patreon skins page asks
/// of the network, gated here so the window stays a pure consumer. Nothing
/// in this file runs on a timer — the list is fetched when the page is
/// opened (and kept for the session), pictures when a tile needs them, and
/// a skin is applied only for a click. Same class as PollService.cs, split
/// for reading; see CLAUDE.md.
/// </summary>
public sealed partial class PollService
{
    /// <summary>A list this fresh is shown again without asking: tiers and skins change rarely.</summary>
    private static readonly TimeSpan SkinListFreshFor = TimeSpan.FromMinutes(10);

    /// <summary>Floor under list requests, Refresh button included — reopening the page in a hurry is not a request each time.</summary>
    private static readonly TimeSpan SkinListFloor = TimeSpan.FromSeconds(30);

    /// <summary>The breather after every apply request, whatever its outcome: it can never be mashed.</summary>
    private static readonly TimeSpan SkinApplyGuard = TimeSpan.FromSeconds(15);

    private const int MaxCachedPictures = 200;

    private readonly Dictionary<string, byte[]?> _skinPictures = new();
    private bool _skinListBusy;
    private bool _skinBusy;
    private DateTime _skinListAskedUtc;
    private DateTime _skinListGotUtc;
    private DateTime _skinGuardUntilUtc;

    /// <summary>The last list fetched this session; null until the page was opened once.</summary>
    public IReadOnlyList<PatreonSkin>? Skins { get; private set; }

    /// <summary>The last poll saw you spawned in.</summary>
    public bool InGame => _inGame;

    /// <summary>The species the last in-game poll reported, for the page's "apply again" offer.</summary>
    public string? CurrentDino => _dino;

    /// <summary>Raised after a successful apply — MainWindow posts it to the Activity feed.</summary>
    public event Action<SkinApplyResult>? SkinApplied;

    /// <summary>
    /// The skins for the page. Answers from the session's copy while it is
    /// fresh (or, with refresh, at least 30 s old), else asks the server
    /// once. A failure keeps the last good copy and names the problem.
    /// </summary>
    public async Task<SkinListResult> GetSkinsAsync(bool refresh)
    {
        var now = DateTime.UtcNow;
        if (Skins is not null && !refresh && now - _skinListGotUtc < SkinListFreshFor) return new SkinListResult(Skins);
        if (_skinListBusy || now - _skinListAskedUtc < SkinListFloor)
        {
            return new SkinListResult(Skins, Skins is null ? "asked a moment ago — try again shortly" : null);
        }

        _skinListBusy = true;
        _skinListAskedUtc = now;
        try
        {
            var list = await _client.FetchPatreonSkinsAsync();
            if (list is null) return new SkinListResult(Skins, "the server declined");
            Skins = list;
            _skinListGotUtc = DateTime.UtcNow;
            return new SkinListResult(list);
        }
        catch (Exception ex)
        {
            return new SkinListResult(Skins, ex.GetType().Name);
        }
        finally
        {
            _skinListBusy = false;
        }
    }

    /// <summary>One tile picture, fetched once per session (a failure is remembered too) and never with the cookie.</summary>
    public async Task<byte[]?> GetSkinPictureAsync(string? address)
    {
        if (PatreonSkins.ResolvePicture(address) is not { } uri) return null;
        var key = uri.AbsoluteUri;
        if (_skinPictures.TryGetValue(key, out var known)) return known;
        if (_skinPictures.Count >= MaxCachedPictures) return null;

        _skinPictures[key] = null; // claimed: a second tile asking meanwhile gets "none yet", not a second download
        var bytes = await _client.FetchPictureAsync(uri);
        _skinPictures[key] = bytes;
        return bytes;
    }

    /// <summary>
    /// One USER-TRIGGERED apply — never call this from a timer. Not spawned in
    /// (after one regular poll to refresh a stale state, as with Check Prime)
    /// or inside the 15 s breather answers locally with no request sent. A
    /// success is remembered per species in config (persisted with the next
    /// Save) and announced through SkinApplied. The server keeps the final
    /// word on its own cooldown: nothing here assumes its length.
    /// </summary>
    public async Task<SkinApplyResult> ApplySkinAsync(string skinId, bool idIsNumber, string skinName, int pattern)
    {
        pattern = Math.Clamp(pattern, 0, PatreonSkins.PatternCount - 1);
        if (_skinBusy || DateTime.UtcNow < _skinGuardUntilUtc)
        {
            return new SkinApplyResult(SkinApplyOutcome.TooSoon, skinName, pattern);
        }

        _skinBusy = true;
        try
        {
            if (!_inGame && PollIsStale) await PollNowAsync();
            if (!_inGame) return new SkinApplyResult(SkinApplyOutcome.NotInGame, skinName, pattern);

            _skinGuardUntilUtc = DateTime.UtcNow + SkinApplyGuard;
            var result = await _client.ApplyPatreonSkinAsync(PatreonSkins.IdJson(skinId, idIsNumber), skinName, pattern);
            if (result.Outcome == SkinApplyOutcome.Ok)
            {
                _config.SkinLastAppliedUtc = DateTime.UtcNow;
                if (!string.IsNullOrWhiteSpace(_dino))
                {
                    _config.SkinChoices[_dino] = new SkinChoice(skinId, idIsNumber, skinName, pattern);
                }
                SkinApplied?.Invoke(result);
            }
            return result;
        }
        catch (Exception ex)
        {
            return new SkinApplyResult(SkinApplyOutcome.Failed, skinName, pattern, ex.GetType().Name);
        }
        finally
        {
            _skinBusy = false;
        }
    }

    /// <summary>A different login may see different skins: forget the list (pictures are public addresses and may stay).</summary>
    private void ForgetSkins()
    {
        Skins = null;
        _skinListGotUtc = default;
        _skinListAskedUtc = default;
    }
}
