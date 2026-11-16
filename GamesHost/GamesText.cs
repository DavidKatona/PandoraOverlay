namespace PandoraOverlay;

/// <summary>
/// Every word the Games window shows (the plan's rule for the window: its
/// strings in one place), as constants for its XAML (<c>x:Static</c>) and
/// small functions for its code, so a rewording is one edit here and the
/// layout tests read the very strings the window draws. A game's own words
/// are the game's (its title, pitch and tile); these are the window's chrome
/// and the host's words for a game that can't play.
/// <para>
/// Player-facing text states only what the overlay knows (CLAUDE.md,
/// Conventions): the health warning says the health is dropping and by how
/// much, never who or what, and never more than the API shows.
/// </para>
/// </summary>
internal static class GamesText
{
    // ---- The window -------------------------------------------------------------

    /// <summary>The window's own name (no title bar shows it; what Windows lists it as).</summary>
    public const string WindowName = "Pandora Games";

    /// <summary>The header's title on the grid.</summary>
    public const string GridTitle = "Games";

    /// <summary>The back control, in a game (G13).</summary>
    public const string Back = "‹ Games";

    /// <summary>The header's dim subtitle in a game while the health warning covers it.</summary>
    public const string Paused = "Paused";

    /// <summary>The dim hint at the header's right (G12).</summary>
    public const string Hint = "J or Esc closes";

    /// <summary>The close button's glyph (a font glyph, never an image: CLAUDE.md, Conventions).</summary>
    public const string Close = "✕";

    /// <summary>The grid's subtitle: how many games are listed (0 included: the library exists from day one, G19).</summary>
    public static string GameCount(int count) => count == 1 ? "1 game" : $"{count} games";

    // ---- The sound group (A82) ------------------------------------------------------
    // Font glyphs, never image files (CLAUDE.md, Conventions): WPF draws the two
    // speakers through its font fallback, in the button's own colour, the same in
    // every bundled body font. Written as escapes, so the source holds no
    // character an editor might mangle: U+1F50A, U+1F507 and U+2212.

    /// <summary>The speaker button while sound is on: a speaker with sound waves (a click mutes).</summary>
    public const string SoundOn = "\U0001F50A";

    /// <summary>The speaker button while muted: a speaker struck through (a click turns sound on).</summary>
    public const string SoundOff = "\U0001F507";

    /// <summary>One step quieter: a minus sign, as wide as the plus.</summary>
    public const string SoundDown = "−";

    /// <summary>One step louder.</summary>
    public const string SoundUp = "+";

    // ---- The watch note (A16) ---------------------------------------------------

    public const string WatchDisconnected = "Disconnected · health not watched";

    public const string WatchSignedOut = "Not signed in · health not watched";

    /// <summary>The header's note for a watch state, or "" when there is nothing to say (watching, or not in game).</summary>
    public static string WatchNote(GamesWatch watch) => watch switch
    {
        GamesWatch.Disconnected => WatchDisconnected,
        GamesWatch.SignedOut => WatchSignedOut,
        _ => "",
    };

    // ---- A tile's button and the host's words -------------------------------------
    // Each written to fit its line whole (A52): a tile's line is 230 px, and the
    // ellipsis is for a font wider than Default's, never for words too long for
    // any theme. GamesLayoutTests holds them whole in Default.

    public const string Reset = "Reset";

    public const string ResetArmed = "Click again to reset";

    public const string StartAgain = "Start it again";

    public const string StartFresh = "Start fresh";

    public const string StartFreshArmed = "Click again to start fresh";

    public const string FaultedStatus = "Stopped after an error";

    public const string FaultedDetail = "Nothing is lost: it starts from its last save.";

    public const string UnreadableStatus = "Its save could not be read";

    public const string UnreadableDetail = "The file is kept, even if you start fresh.";

    public const string NewerStatus = "Its save is from a newer overlay";

    public const string NewerDetail = "Update the overlay to keep playing it.";

    /// <summary>The detail line after an armed Reset or Start fresh the host refused (A38): nothing was reset.</summary>
    public const string ResetRefused = "Not reset: its save could not be kept first.";

    /// <summary>The detail line after a "Start it again" the host refused (A49): nothing was started, nothing is lost.</summary>
    public const string StartAgainRefused = "Not started: it could not be saved first.";

    // ---- The health warning (G18, A15) -------------------------------------------

    public const string WarningTitle = "Your health is dropping";

    public const string WarningHint = "Click the game to go straight back to it.";

    /// <summary>The warning's button in a game.</summary>
    public const string KeepPlaying = "Keep playing";

    /// <summary>The warning's button on the grid.</summary>
    public const string BackToGames = "Back to Games";

    /// <summary>The warning's second line: the player's health as the stats panel shows it, a whole percent.</summary>
    public static string WarningHealth(int healthPercent) => $"Health {healthPercent}%";
}
