using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Xunit;

namespace PandoraOverlay.Tests;

/// <summary>
/// The overlay's own surfaces through the click layer (C05; A98, A100, A104,
/// <c>PLAN-CLICKS.md</c> 1.3, 1.4, 6.1 and its appendix): the marks every
/// control that means more than its type carries (a ✕, Close, Later, Cancel
/// and Keep are <c>back</c>; the control panel's Exit is silent), each armed
/// button's two clicks (<c>arm</c> on the one that asks, <c>press</c> on the one
/// that acts, and the action still runs), the three things that are not
/// buttons (the colour wells and a stored dino card's summary), and that no
/// OTHER button of those windows was marked. Each on an STA thread of its own
/// with a fake sink on it (<see cref="ClickSeam"/>: a real left release through
/// the input manager, the button's own OnClick inside it; a real mouse click is
/// the owner's to see, A107). Every dialog has a counting fake behind its
/// website client and a stand-in clipboard, so nothing here asks the website,
/// reads the player's clipboard or touches the data folder: Sign out's second
/// click would (it saves the config and clears the sign-in browser's folder),
/// so that one is never made here.
/// </summary>
[Collection(TheThemeOnScreen.Name)]
public sealed class ClickSurfacesTests
{
    private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

    private static ClickCue[] Cues(FakeClickSink sink) => sink.Clicks.Select(c => c.Cue).ToArray();

    // ---- Finding things ------------------------------------------------------------------

    /// <summary>Everything under the root by the LOGICAL tree (a window never shown has no visual one): a Button's content, a Border's child, a panel's children.</summary>
    private static IEnumerable<DependencyObject> Tree(DependencyObject root)
    {
        yield return root;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var below in Tree(child)) yield return below;
    }

    /// <summary>A button's words: its text, or the text in the panel it holds.</summary>
    private static string Words(ButtonBase button) => button.Content switch
    {
        string text => text,
        DependencyObject content => string.Concat(Tree(content).OfType<TextBlock>().Select(t => t.Text)),
        _ => "",
    };

    private static string Label(ButtonBase button) => string.IsNullOrEmpty(button.Name) ? Words(button) : button.Name;

    private static Button Named(DependencyObject root, string name) => (Button)Tree(root).OfType<Button>().Single(b => b.Name == name);

    private static Button Worded(DependencyObject root, string words) => Tree(root).OfType<Button>().Single(b => Words(b) == words);

    private static T Field<T>(object owner, string name) =>
        (T)(owner.GetType().GetField(name, Any) ?? throw new MissingFieldException(owner.GetType().Name, name)).GetValue(owner)!;

    private static void Call(object owner, string method, params object[] args) =>
        owner.GetType().GetMethods(Any).Single(m => m.Name == method && m.GetParameters().Length == args.Length).Invoke(owner, args);

    /// <summary>Every button of the window that carries a mark, as "label=Mark": the marks, and only the marks, a surface has.</summary>
    private static string[] Marked(DependencyObject root) =>
        Tree(root).OfType<ButtonBase>().Where(b => UiClicks.GetCue(b) != ClickMark.Auto)
            .Select(b => $"{Label(b)}={UiClicks.GetCue(b)}").OrderBy(s => s, StringComparer.Ordinal).ToArray();

    /// <summary>A left press or release on something that is not a button, as its mouse handler receives it.</summary>
    private static void Mouse(UIElement element, RoutedEvent routed) =>
        element.RaiseEvent(new MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = routed, Source = element });

    /// <summary>The theme loaded, as the dialogs' code-built pages need it, and this thread's sink.</summary>
    private static void OnSta(Action<FakeClickSink> test) => ClickSeam.OnSta(sink =>
    {
        Theme.EnsureLoaded();
        test(sink);
    });

    // ---- The marks (sections 1 and 2) ----------------------------------------------------

    [Fact]
    public void SettingsMarksItsTwoClosesBackAndItsFiveArmedButtonsArm() => OnSta(_ =>
    {
        var (dialog, _) = DialogLayoutTests.Dialog();
        Assert.Equal(new[]
        {
            "Close=Back", "DefaultKeysButton=Arm", "DeleteAllButton=Arm", "ResetPositionsButton=Arm",
            "RouteClearButton=Arm", "RoutePasteButton=Arm", "SignOutButton=Arm", "✕=Back",
        }, Marked(dialog));
        dialog.Close();
    });

    [Fact]
    public void TheUpdateCardsCloseAndLaterAreBackInEveryCard() => OnSta(_ =>
    {
        const string notes = "### New\n- A line.";
        var cards = new[]
        {
            UpdateCardWindow.Available("1.39.0", "1.38.0", notes, _ => Task.FromResult(false)),
            UpdateCardWindow.Updated("1.39.0", "1.38.0", notes),
            UpdateCardWindow.Notes("1.38.0", notes),
        };
        var available = cards[0];
        Assert.Equal(new[] { "CloseButton=Back", "LaterButton=Back" }, Marked(available));
        Assert.Equal(ClickMark.Auto, UiClicks.GetCue(Named(available, "PrimaryButton"))); // the primary clicks by type
        foreach (var card in cards)
        {
            // Whatever buttons a card has, only its ✕ and its Later are marked.
            Assert.All(Marked(card), m => Assert.True(m is "CloseButton=Back" or "LaterButton=Back", m));
            card.Close();
        }
    });

    [Fact]
    public void TheSignInWindowsCloseAndCancelAreBack() => OnSta(_ =>
    {
        var window = new SignInWindow();
        Assert.Equal(new[] { "CancelButton=Back", "✕=Back" }, Marked(window));
        window.Close();
    });

    [Fact]
    public void TheControlPanelsExitIsSilentAndNothingElseOnItIsMarked() => OnSta(sink =>
    {
        var exits = 0;
        var panel = new ControlPanelWindow(new OverlayConfig(), () => { }, () => { }, () => { }, () => { }, () => { }, () => { }, () => { },
                                           () => new PanelLayers(false, false, false, false, null, null), () => { }, () => { }, () => { }, () => { },
                                           () => { }, () => exits++);
        Assert.Equal(new[] { "Exit=None" }, Marked(panel));

        ClickSeam.Click(Worded(panel, "Exit"));

        Assert.Equal(1, exits); // it still exits
        Assert.Empty(sink.Clicks); // and the speaker's dispose would cut a cue (A90)
        ClickSeam.Click(Worded(panel, "Settings…"));
        Assert.Equal(new[] { ClickCue.Press }, Cues(sink)); // a neighbour still clicks by type
        panel.Close();
    });

    [Fact]
    public void ACloseButtonOfSettingsAndTheCardsSoundsBack() => OnSta(sink =>
    {
        var (dialog, _) = DialogLayoutTests.Dialog();
        ClickSeam.Click(Worded(dialog, "✕"));
        Assert.Equal(new[] { ClickCue.Back }, Cues(sink));

        var card = UpdateCardWindow.Available("1.39.0", "1.38.0", "### New\n- A line.", _ => Task.FromResult(false));
        ClickSeam.Click(Named(card, "LaterButton"));
        ClickSeam.Click(Named(card, "CloseButton"));
        Assert.Equal(new[] { ClickCue.Back, ClickCue.Back, ClickCue.Back }, Cues(sink));

        var signIn = new SignInWindow();
        ClickSeam.Click(Named(signIn, "CancelButton"));
        Assert.Equal(4, sink.Clicks.Count);
        Assert.Equal(ClickCue.Back, sink.Clicks[^1].Cue);
        signIn.Close();
    });

    // ---- The armed buttons (section 3): arm, then press, and the action still runs ------------

    /// <summary>The first click asks and sounds <c>arm</c>; the second acts and sounds <c>press</c>.</summary>
    private static void AskThenAct(FakeClickSink sink, ButtonBase button, Action whileAsking, Action afterActing)
    {
        ClickSeam.Click(button);
        Assert.Equal(new[] { ClickCue.Arm }, Cues(sink));
        whileAsking();
        ClickSeam.Click(button);
        Assert.Equal(new[] { ClickCue.Arm, ClickCue.Press }, Cues(sink));
        afterActing();
    }

    [Fact]
    public void ResetPositionsAsksThenActs() => OnSta(sink =>
    {
        var (dialog, _) = DialogLayoutTests.Dialog();
        var reset = Named(dialog, "ResetPositionsButton");

        AskThenAct(sink, reset,
            () => Assert.Equal("Really reset?", reset.Content),
            () =>
            {
                Assert.Equal("Reset positions", reset.Content);
                Assert.False(Field<bool>(dialog, "_resetPositionsArmed"));
                Assert.True(Field<SettingsChange>(dialog, "_pending").HasFlag(SettingsChange.Positions)); // the action ran
            });
        dialog.Close();
    });

    [Fact]
    public void UseTheDefaultKeysAsksThenActs() => OnSta(sink =>
    {
        var (dialog, _) = DialogLayoutTests.Dialog();
        var keys = Named(dialog, "DefaultKeysButton");

        AskThenAct(sink, keys,
            () => Assert.Equal("Really change them all?", keys.Content),
            () =>
            {
                Assert.Equal("Use the default keys", keys.Content);
                Assert.False(Field<bool>(dialog, "_defaultKeysArmed"));
                Assert.NotEqual("", ((TextBlock)dialog.FindName("HotkeyHint")).Text); // the action answered, one way or the other
            });
        dialog.Close();
    });

    [Fact]
    public void SignOutAsksOnItsFirstClickAndItsSecondIsNeverMadeHere() => OnSta(sink =>
    {
        // The second click saves the config and clears the sign-in browser's folder: the player's own files.
        var (dialog, site) = DialogLayoutTests.Dialog();
        var signOut = Named(dialog, "SignOutButton");

        ClickSeam.Click(signOut);

        Assert.Equal(new[] { ClickCue.Arm }, Cues(sink));
        Assert.Equal("Really sign out?", signOut.Content);
        Assert.True(Field<bool>(dialog, "_signOutArmed"));
        Assert.Equal(0, site.Requests);
        dialog.Close();
    });

    [Fact]
    public void DeleteAllAsksThenActs() => OnSta(sink =>
    {
        var (dialog, _) = DialogLayoutTests.Dialog();
        Call(dialog, "SetPage", "Waypoints");
        var deleteAll = Named(dialog, "DeleteAllButton");
        Assert.True(deleteAll.IsEnabled);

        AskThenAct(sink, deleteAll,
            () =>
            {
                Assert.Equal("Really delete all?", deleteAll.Content);
                Assert.Equal(3, Field<List<Waypoint>>(dialog, "_draft").Count);
            },
            () => Assert.Empty(Field<List<Waypoint>>(dialog, "_draft")));
        dialog.Close();
    });

    [Fact]
    public void APacksDeleteAsksThenActsAndAWaypointsOwnDeleteJustPresses() => OnSta(sink =>
    {
        var config = new OverlayConfig();
        var library = new WaypointLibrary();
        library.Add("Own", 1000, 2000, 0);
        library.Add("In a pack", 3000, 4000, 1)!.Pack = "A pack";
        var poll = new PollService(config);
        CountingSite.Install(poll);
        var dialog = new SettingsWindow(config, library, new FriendBook(), poll);
        Call(dialog, "SetPage", "Waypoints");
        var pack = Tree(dialog).OfType<Button>().Single(b => Equals(b.ToolTip, "Delete this pack"));
        var own = Tree(dialog).OfType<Button>().First(b => Equals(b.ToolTip, "Delete this waypoint"));
        Assert.Equal(ClickMark.Arm, UiClicks.GetCue(pack));
        Assert.Equal(ClickMark.Auto, UiClicks.GetCue(own)); // a row's ✕ is one click: by type

        AskThenAct(sink, pack,
            () => Assert.Equal(2, Field<List<Waypoint>>(dialog, "_draft").Count),
            () => Assert.Equal("Own", Assert.Single(Field<List<Waypoint>>(dialog, "_draft")).Name));
        sink.Clicks.Clear();

        ClickSeam.Click(own);
        Assert.Equal(new[] { ClickCue.Press }, Cues(sink));
        Assert.Empty(Field<List<Waypoint>>(dialog, "_draft"));
        dialog.Close();
    });

    [Fact]
    public void TheRoutesClearAsksThenActs() => OnSta(sink =>
    {
        var (dialog, _) = DialogLayoutTests.Dialog();
        var actions = Field<MapActions>(dialog, "_actions");
        Call(dialog, "SetPage", "Route");
        var clear = Named(dialog, "RouteClearButton");
        Assert.False(actions.Route.IsEmpty);

        AskThenAct(sink, clear,
            () =>
            {
                Assert.Equal("Clear all?", clear.Content);
                Assert.False(actions.Route.IsEmpty);
            },
            () =>
            {
                Assert.True(actions.Route.IsEmpty);
                Assert.Equal("Clear", clear.Content);
            });
        dialog.Close();
    });

    private const string ARouteCode = "pandora:5000,0 Delta\npandora:6000,0 Forks";

    [Fact]
    public void ThePastesFirstClickAsksOnlyWhenItHasSomethingToAsk() => OnSta(sink =>
    {
        var (dialog, _) = DialogLayoutTests.Dialog();
        var actions = Field<MapActions>(dialog, "_actions");
        string? clipboard = ARouteCode; // never the player's
        actions.ReadClipboard = () => clipboard;
        actions.WriteClipboard = text => clipboard = text;
        Call(dialog, "SetPage", "Route");
        var paste = Named(dialog, "RoutePasteButton");
        Assert.False(actions.Route.IsEmpty);

        // A route to replace and a route on the clipboard: it asks, then pastes.
        AskThenAct(sink, paste,
            () => Assert.Equal("Replace route?", paste.Content),
            () =>
            {
                Assert.Equal("Paste", paste.Content);
                Assert.Equal(2, actions.Route.Count); // the pasted route took the place of the three stops
            });

        // Nothing on the clipboard: nothing to ask, so a plain press at once.
        sink.Clicks.Clear();
        clipboard = null;
        ClickSeam.Click(paste);
        Assert.Equal(new[] { ClickCue.Press }, Cues(sink));
        Assert.Equal("Paste", paste.Content);

        // An empty route needs no asking either.
        sink.Clicks.Clear();
        actions.ClearRoute();
        clipboard = ARouteCode;
        ClickSeam.Click(paste);
        Assert.Equal(new[] { ClickCue.Press }, Cues(sink));
        Assert.Equal(2, actions.Route.Count);
        dialog.Close();
    });

    [Fact]
    public void AStoredDinosDeleteAsksEveryTimeAndKeepBacksOut() => OnSta(sink =>
    {
        var (dialog, _) = DialogLayoutTests.Dialog();
        DrawStorage(dialog);
        var cards = (StackPanel)dialog.FindName("StorageCards");
        var summary = (StackPanel)((StackPanel)((Border)cards.Children[0]).Child).Children[0];

        // The card's summary is not a button: open is a switch on, closed a switch off.
        Mouse(summary, UIElement.MouseLeftButtonUpEvent);
        Assert.Equal(new[] { ClickCue.ToggleOn }, Cues(sink));
        var delete = Worded(cards, "Delete…");
        var keep = Worded(cards, "Keep");
        Assert.Equal(ClickMark.Arm, UiClicks.GetCue(delete));
        Assert.Equal(ClickMark.Back, UiClicks.GetCue(keep));
        sink.Clicks.Clear();

        ClickSeam.Click(delete);
        Assert.Equal(new[] { ClickCue.Arm }, Cues(sink));
        Assert.Equal(Visibility.Visible, ((Grid)LogicalTreeHelper.GetParent(keep)).Visibility); // it asked: Delete and Keep are up
        ClickSeam.Click(keep);
        Assert.Equal(new[] { ClickCue.Arm, ClickCue.Back }, Cues(sink));
        ClickSeam.Click(delete); // every click of it arms, also after a Keep
        Assert.Equal(new[] { ClickCue.Arm, ClickCue.Back, ClickCue.Arm }, Cues(sink));

        sink.Clicks.Clear();
        Mouse(summary, UIElement.MouseLeftButtonUpEvent);
        Assert.Equal(new[] { ClickCue.ToggleOff }, Cues(sink));
        dialog.Close();
    });

    [Fact]
    public void AStoredDinosRenameCancelBacksOut() => OnSta(sink =>
    {
        var (dialog, site) = DialogLayoutTests.Dialog();
        DrawStorage(dialog);
        var cards = (StackPanel)dialog.FindName("StorageCards");
        var summary = (StackPanel)((StackPanel)((Border)cards.Children[0]).Child).Children[0];
        Mouse(summary, UIElement.MouseLeftButtonUpEvent);
        sink.Clicks.Clear();

        ClickSeam.Click(Worded(cards, "Rename…")); // opens the form: an ordinary press (A113)
        Assert.Equal(new[] { ClickCue.Press }, Cues(sink));
        var cancel = Worded(cards, "Cancel");
        Assert.Equal(ClickMark.Back, UiClicks.GetCue(cancel));

        ClickSeam.Click(cancel);

        Assert.Equal(new[] { ClickCue.Press, ClickCue.Back }, Cues(sink));
        Assert.DoesNotContain(Tree(cards).OfType<Button>(), b => Words(b) == "Cancel"); // the form is gone
        Assert.Equal(0, site.Requests);
        dialog.Close();
    });

    /// <summary>Settings → Dino storage with one stored dino drawn, as DialogLayoutTests draws it (signed out the page drops any list, so it is handed one), its card closed.</summary>
    private static void DrawStorage(SettingsWindow dialog)
    {
        Call(dialog, "SetPage", "Storage");
        var list = StoredDinos.ParseList(System.Text.Json.JsonDocument.Parse(SettingsThemeTests.Storage).RootElement)
                   ?? throw new InvalidOperationException("the storage sample did not parse");
        dialog.GetType().GetField("_storage", Any)!.SetValue(dialog, list);
        Call(dialog, "RenderStorage");
    }

    // ---- The map menu's armed entry ------------------------------------------------------

    [Fact]
    public void TheMapMenusPasteRouteAsksThenActs() => OnSta(sink =>
    {
        var config = new OverlayConfig();
        var library = new WaypointLibrary();
        var actions = new MapActions(config, library, new FriendBook(), name => name);
        string? clipboard = ARouteCode; // never the player's
        actions.ReadClipboard = () => clipboard;
        actions.WriteClipboard = text => clipboard = text;
        actions.AddToRoute(library.Add("A", 100_000, 0, 0)!);
        var notices = new List<string>();
        var closed = 0;
        var items = new StackPanel();
        new MapMenuBuilder(items, new Style(typeof(Button)), () => Brushes.Gray, actions, _ => Brushes.White, () => null, () => closed++, notices.Add)
            .Build(new MapMenuSpot((5, 5), null, null));
        var paste = Worded(items, "Paste route");
        Assert.Equal(ClickMark.Arm, UiClicks.GetCue(paste));

        ClickSeam.Click(paste);

        Assert.Equal(new[] { ClickCue.Arm }, Cues(sink));
        Assert.Equal("Replace your route?", Words(paste));
        Assert.Equal(0, closed); // asking leaves the menu open
        Assert.Equal(1, actions.Route.Count);

        ClickSeam.Click(paste);

        Assert.Equal(new[] { ClickCue.Arm, ClickCue.Press }, Cues(sink));
        Assert.Equal(1, closed);
        Assert.Equal(2, actions.Route.Count); // the pasted route
    });

    [Fact]
    public void TheMapMenusOtherEntriesStayByType() => OnSta(sink =>
    {
        var actions = new MapActions(new OverlayConfig(), new WaypointLibrary(), new FriendBook(), name => name);
        var items = new StackPanel();
        new MapMenuBuilder(items, new Style(typeof(Button)), () => Brushes.Gray, actions, _ => Brushes.White, () => null, () => { }, _ => { })
            .Build(new MapMenuSpot((5, 5), null, null));

        Assert.Empty(Marked(items)); // an empty route: Paste route is a plain entry, and no entry is marked
        ClickSeam.Click(items.Children.OfType<Button>().First());
        Assert.Equal(new[] { ClickCue.Press }, Cues(sink));
    });

    // ---- Not buttons (section 4) ---------------------------------------------------------

    [Fact]
    public void AWaypointsColourWellPressesAfterTheColourChanges() => OnSta(sink =>
    {
        var config = new OverlayConfig();
        var library = new WaypointLibrary();
        library.Add("Own", 1000, 2000, 0);
        var poll = new PollService(config);
        CountingSite.Install(poll);
        var dialog = new SettingsWindow(config, library, new FriendBook(), poll);
        Call(dialog, "SetPage", "Waypoints");
        var well = Well(dialog);
        var before = Field<List<Waypoint>>(dialog, "_draft")[0].Colour;

        Mouse(well, UIElement.MouseLeftButtonDownEvent);

        Assert.Equal(new[] { ClickCue.Press }, Cues(sink));
        Assert.Equal(WaypointPalette.Wrap(before + 1), Field<List<Waypoint>>(dialog, "_draft")[0].Colour);
        dialog.Close();
    });

    [Fact]
    public void AFriendsColourWellPresses() => OnSta(sink =>
    {
        var config = new OverlayConfig();
        var book = PackRowsTests.Book(new[] { PackRowsTests.F("f01") });
        var poll = new PollService(config);
        CountingSite.Install(poll);
        var dialog = new SettingsWindow(config, new WaypointLibrary(), book, poll);
        Call(dialog, "SetPage", "Friends");
        var well = Well(dialog);
        var entry = Field<List<FriendEntry>>(dialog, "_friendDraft").Single();
        var before = entry.Colour;

        Mouse(well, UIElement.MouseLeftButtonDownEvent);

        Assert.Equal(new[] { ClickCue.Press }, Cues(sink));
        Assert.NotEqual(before, entry.Colour);
        dialog.Close();
    });

    /// <summary>The one colour well on the page (a 15 px bordered disc with a hand cursor: nothing else is).</summary>
    private static Border Well(DependencyObject root) =>
        Tree(root).OfType<Border>().Single(b => b is { Width: 15, Height: 15 } && ReferenceEquals(b.Cursor, Cursors.Hand));
}
