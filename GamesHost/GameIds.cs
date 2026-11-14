namespace PandoraOverlay;

/// <summary>
/// A game's id (the Contract's <c>IGame.Id</c>): stable for ever, because it is
/// the name of the game's save folder (A7). So it is held to a shape that can't
/// leave the folder: 1 to 32 characters of a-z, 0-9 and '-', not starting or
/// ending with '-'. No dot, no slash, no colon, no space, no capital: nothing a
/// game names can become "..", a rooted path, a drive, a stream or a second
/// folder level, and two ids that differ only in case (which Windows would
/// fold into one folder, a shared save against G3) can't both exist.
/// <para>
/// Nor a Windows device name (A37): con, prn, aux, nul, com0 to com9 and lpt0
/// to lpt9, as the whole id. Windows can't always make a folder of such a
/// name, and which ones it refuses differs by version (on a Windows 11 PC,
/// Nov 2026, "con" was made and "nul" refused; older versions refuse more),
/// so every write of that game would fail on some players' PCs and not on the
/// PC it was built on. Only the
/// exact names: "console", "con1" or "a-con" are ordinary folders.
/// </para>
/// A failing id is a build defect, not a player's problem: the save store
/// throws on one, and the catalog's test (A10) holds every listed game to this
/// rule. Pure, zero WPF.
/// </summary>
internal static class GameIds
{
    /// <summary>The longest id: a folder name with room to spare under any profile path.</summary>
    internal const int MaxLength = 32;

    /// <summary>The device names Windows reserves, in the only case an id can have (A37).</summary>
    internal static readonly IReadOnlySet<string> DeviceNames = new HashSet<string>(
        new[] { "con", "prn", "aux", "nul" }
            .Concat(Enumerable.Range(0, 10).Select(n => $"com{n}"))
            .Concat(Enumerable.Range(0, 10).Select(n => $"lpt{n}")),
        StringComparer.Ordinal);

    public static bool IsValid(string? id)
    {
        if (string.IsNullOrEmpty(id) || id.Length > MaxLength) return false;
        if (id[0] == '-' || id[^1] == '-') return false;
        foreach (var c in id)
        {
            // Plain ASCII ranges, not char.IsLower / IsDigit: those take any
            // script's letters and digits, which a folder name should not.
            if (c is not (>= 'a' and <= 'z' or >= '0' and <= '9' or '-')) return false;
        }
        return !DeviceNames.Contains(id);
    }
}
