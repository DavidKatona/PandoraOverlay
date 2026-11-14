namespace PandoraOverlay;

/// <summary>
/// A game's id (the Contract's <c>IGame.Id</c>): stable for ever, because it is
/// the name of the game's save folder (A7). So it is held to a shape that can't
/// leave the folder: 1 to 32 characters of a-z, 0-9 and '-', not starting or
/// ending with '-'. No dot, no slash, no colon, no space, no capital: nothing a
/// game names can become "..", a rooted path, a drive, a stream or a second
/// folder level, and two ids that differ only in case (which Windows would
/// fold into one folder, a shared save against G3) can't both exist.
/// A failing id is a build defect, not a player's problem: the save store
/// throws on one, and the catalog's test (A10) holds every listed game to this
/// rule. Pure, zero WPF.
/// </summary>
internal static class GameIds
{
    /// <summary>The longest id: a folder name with room to spare under any profile path.</summary>
    internal const int MaxLength = 32;

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
        return true;
    }
}
