namespace PandoraOverlay;

/// <summary>
/// What the rest of the overlay does that the Games window must answer (A13):
/// one value per row of the table <see cref="GamesGate.Closes"/> decides. The
/// first row closes the window (but <see cref="ShowingAll"/>); the second
/// leaves it as it is.
/// </summary>
internal enum OverlayEvent
{
    /// <summary>Hide-all hides the overlay (G16): Games goes with the widgets.</summary>
    HidingAll,

    /// <summary>Hide-all shows the overlay again: the widgets come back, and an open Games window stays, brought to the front of them.</summary>
    ShowingAll,

    /// <summary>Edit mode toggled (its hotkey, the tray, Esc in edit mode): editing is about the widgets, so Games closes first.</summary>
    EditModeToggled,

    /// <summary>Settings or Server rules opening: they are modal, and a theme switch can only happen with Games closed.</summary>
    SettingsOpening,

    /// <summary>The tray's sign-in window opening: modal and topmost, like Settings.</summary>
    SignInOpening,

    /// <summary>The map opening: one of the two at a time, and the map hides the widgets Games leaves up.</summary>
    MapOpening,

    /// <summary>A click on the game (<see cref="OverlayFocus.Left"/>, G17): out of the way at once, and nothing is lost.</summary>
    FocusLeft,

    /// <summary>Exit, Windows ending the session, an update's restart: closed, and every save flushed before the config is.</summary>
    Exiting,

    /// <summary>The not-in-game auto-hide hides the widgets: Games stays (a player in the spawn menu may be playing).</summary>
    AutoHidden,

    /// <summary>A spawn after the auto-hide shows the widgets again: Games stays, brought to the front of them.</summary>
    ShownAfterAutoHide,

    /// <summary>The Check Prime key or button: it works as always, and Games stays, on top of the Prime widget the check may bring up (A58).</summary>
    CheckPrime,

    /// <summary>The stats view key: it works as always, and Games stays (the stats panel is what shows your health, G15).</summary>
    StatsViewToggled,

    /// <summary>The minimap view key: it works as always, and Games stays.</summary>
    MinimapViewToggled,

    /// <summary>The heatmap key: it works as always, and Games stays.</summary>
    HeatmapToggled,

    /// <summary>The skin key: it works as always, and Games stays.</summary>
    SkinKey,

    /// <summary>A poll failed: Games plays on, and its header says health is not watched (A16).</summary>
    PollFailed,

    /// <summary>The website ended the session: Games plays on, and its header says so (A16).</summary>
    SessionEnded,
}

/// <summary>
/// The Games window against every other state of the overlay (A13, G15 to
/// G17), as one pure table: MainWindow asks it at each place that could close
/// the window (<c>CloseGamesFor</c> in MainWindow.Games.cs), so the table is
/// the code and not a comment beside it, and its test reads every row.
/// <para>
/// The reason behind it: one of edit mode, Settings, the map, the sign-in
/// window and Games is on screen at a time, so opening any of the others
/// closes Games (and opening Games locks edit mode and closes the map, which
/// is MainWindow's opening, not a row here). Hide-all hiding and a click on
/// the game close it too: the player wants the screen back. Everything else
/// the overlay does while Games is open leaves it alone, because Games keeps
/// the widgets up (G15) and is fed whatever happens: the auto-hide, the
/// mid-game keys and a lost connection change nothing about the window but
/// its watch note.
/// </para>
/// Zero WPF; it asks nothing and remembers nothing.
/// </summary>
internal static class GamesGate
{
    /// <summary>Whether this event closes the Games window (A13).</summary>
    public static bool Closes(OverlayEvent what) => what switch
    {
        OverlayEvent.HidingAll => true,
        OverlayEvent.ShowingAll => false,
        OverlayEvent.EditModeToggled => true,
        OverlayEvent.SettingsOpening => true,
        OverlayEvent.SignInOpening => true,
        OverlayEvent.MapOpening => true,
        OverlayEvent.FocusLeft => true,
        OverlayEvent.Exiting => true,
        // The second row: Games stays. Listed one by one, so an event added
        // to the enum is decided here, never left to a default.
        OverlayEvent.AutoHidden => false,
        OverlayEvent.ShownAfterAutoHide => false,
        OverlayEvent.CheckPrime => false,
        OverlayEvent.StatsViewToggled => false,
        OverlayEvent.MinimapViewToggled => false,
        OverlayEvent.HeatmapToggled => false,
        OverlayEvent.SkinKey => false,
        OverlayEvent.PollFailed => false,
        OverlayEvent.SessionEnded => false,
        _ => false, // a value no member names (a cast): it closes nothing, the window stays as the player left it
    };
}
