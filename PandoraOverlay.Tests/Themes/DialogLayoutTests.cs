using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Xunit;

namespace PandoraOverlay.Tests;

/// <summary>
/// The dialogs in every theme (the themes' step 6): the Settings dialog
/// keeps its fixed frame (SettingsFrame, the owner's call, Oct 25 2026,
/// question 30) and every page fits inside it with nothing to scroll; and
/// no line of text in the Settings dialog, the update cards or the sign-in
/// window is ever cut off in a theme's font: it fits its room, or it ends in
/// an ellipsis (the owner, question 29: never another wording, never a size
/// of its own). The rules of the framework, held for every theme alike, at
/// its fit factor: no test names a theme, so one a player makes later is
/// held to the same.
/// <para>
/// Laid out, not guessed: each window is built as the harness builds it
/// (fresh objects, no cookie, a PollService never started, never shown),
/// on every page, and walked by the app's own probe (<see cref="FitProbe"/>,
/// this test's private code until the fit factor's release): each one-line
/// text's natural width in its own font against the narrowest of the boxes
/// around it, and the page column held whole. Since the fit factor's
/// release the pages not shown are Collapsed, so each page is measured on
/// its own. The pixels are the golden renders' windows mode. The builders
/// here are FitFactorTests' too: it lays the same windows out at every
/// candidate factor.
/// </para>
/// </summary>
[Collection(TheThemeOnScreen.Name)]
public class DialogLayoutTests
{
    private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

    /// <summary>Every page by its SetPage key, in the nav's order.</summary>
    internal static readonly string[] Pages =
        { "Account", "General", "Themes", "Hotkeys", "Widgets", "Maps", "Waypoints", "Route", "Friends", "Skins", "Storage", "Rules", "About" };

    public static IEnumerable<object[]> Themes() => Theme.All.Select(e => new object[] { e.Id });

    /// <summary>
    /// The Settings dialog is its frame in every theme, on every page, and
    /// every page fits it: nothing to scroll in the page column at 100%, so no
    /// theme's font puts a scrollbar there or pushes Close down.
    /// </summary>
    [Theory]
    [MemberData(nameof(Themes))]
    public void TheSettingsDialogKeepsItsFrameAndEveryPageFitsIt(string id) => OnScreen(id, () =>
    {
        var (dialog, site) = Dialog();
        var problems = new List<string>();
        foreach (var (page, root) in EveryPage(dialog)) problems.AddRange(FrameProblems(page, root));
        dialog.Close();
        Assert.True(problems.Count == 0, string.Join("; ", problems));
        Assert.Equal(0, site.Requests); // every page looked at, the website's three included: nothing asked
    });

    /// <summary>The page list fits the dialog in every theme: nothing would scroll it or say it ran out, its last row would just be cut off.</summary>
    [Theory]
    [MemberData(nameof(Themes))]
    public void TheNavFitsTheDialog(string id) => OnScreen(id, () =>
    {
        var (dialog, _) = Dialog();
        LaidOut(dialog);
        var nav = (StackPanel)dialog.FindName("Nav");
        var room = ((FrameworkElement)nav.Parent).ActualHeight; // the column's: a panel taller than its room still reports all of itself
        var last = (FrameworkElement)nav.Children[^1];
        var bottom = last.TranslatePoint(new Point(0, last.ActualHeight), nav).Y;
        dialog.Close();
        Assert.True(bottom <= room, FormattableString.Invariant($"{bottom:0.00} px of nav in {room:0.00}"));
    });

    /// <summary>The walk sees the page column it holds whole: a page made taller than the frame is caught, as a theme's font would make it.</summary>
    [Fact]
    public void ThePageColumnIsHeldWhole() => OnScreen(Theme.DefaultId, () =>
    {
        var (dialog, _) = Dialog();
        Call(dialog, "SetPage", "About");
        var about = ((Grid)dialog.FindName("Pages")).Children.OfType<FrameworkElement>().Single(p => (string)p.Tag == "About");
        Assert.Empty(FrameProblems("About", LaidOut(dialog)));
        about.MinHeight = SettingsFrame.Height; // taller than the column can show
        var spill = Assert.Single(FitProbe.Spills(LaidOut(dialog)));
        Assert.True(spill.Extent > spill.Viewport);
        dialog.Close();
    });

    /// <summary>Every one-line text on every Settings page, the nav, the title and the footer, with a few waypoints, three skins' tiles and an open stored dino.</summary>
    [Theory]
    [MemberData(nameof(Themes))]
    public void NoLineInTheSettingsDialogIsCutOff(string id) => OnScreen(id, () =>
    {
        var (dialog, site) = Dialog();
        var problems = new List<string>();
        foreach (var (page, root) in EveryPage(dialog)) problems.AddRange(LineProblems(page, root));
        dialog.Close();
        Assert.True(problems.Count == 0, string.Join("; ", problems));
        Assert.Equal(0, site.Requests);
    });

    /// <summary>The update cards (available, downloading, updated, What's new) and the sign-in window's states.</summary>
    [Theory]
    [MemberData(nameof(Themes))]
    public void NoLineInTheCardsOrTheSignInWindowIsCutOff(string id) => OnScreen(id, () =>
    {
        var problems = CardsAndSignIn().SelectMany(w => LineProblems(w.Where, w.Root)).ToList();
        Assert.True(problems.Count == 0, string.Join("; ", problems));
    });

    // ---- What the walk finds, as the messages say it ---------------------------------

    /// <summary>The dialog not its frame, or a page that would scroll.</summary>
    internal static IEnumerable<string> FrameProblems(string page, FrameworkElement root)
    {
        if (root.DesiredSize != new Size(SettingsFrame.Width, SettingsFrame.Height))
            yield return FormattableString.Invariant($"{page}: the dialog is {root.DesiredSize.Width:0.##} x {root.DesiredSize.Height:0.##}, not its frame");
        foreach (var spill in FitProbe.Spills(root))
            yield return FormattableString.Invariant($"{page}: {spill.Extent:0.0} px of pages in {spill.Viewport:0.0}");
    }

    /// <summary>The framework's rule, the same for every theme: a line that runs past its room ends in an ellipsis, and the ellipsis shows.</summary>
    internal static IEnumerable<string> LineProblems(string where, FrameworkElement root) =>
        FitProbe.LongLines(root).Where(l => l.CutOff).Select(o => o.Trims
            ? FormattableString.Invariant($"{where}: \"{o.Text}\" ends in an ellipsis past the edge, so the ellipsis is cut off ({o.Room:0.0} px of room)")
            : FormattableString.Invariant($"{where}: \"{o.Text}\" {o.Natural:0.0} px in {o.Room:0.0}, cut off without an ellipsis"));

    // ---- The windows, as the harness builds them -------------------------------------

    /// <summary>
    /// Fresh objects, no cookie, a PollService never started; three waypoints
    /// (the skins and the stored dino come with their pages). Its website
    /// client is the counting fake: the pages it opens (Account, Skins, Dino
    /// storage) are held back by their signed-out guards, and should one ever
    /// get past them, the test fails on the count instead of asking the
    /// website (the 1.39 constraint review, near miss N1).
    /// </summary>
    internal static (SettingsWindow Dialog, CountingSite Site) Dialog()
    {
        var config = new OverlayConfig();
        var library = new WaypointLibrary();
        library.Add("Near Spot", 1000, 2000, 0);
        library.Add("A waypoint with a long name here", -150000, 230000, 4); // 32 letters, the most a name takes
        library.Add("Lake", 52000, -98000, 9);
        var poll = new PollService(config);
        var site = CountingSite.Install(poll);
        var book = new FriendBook();
        // A route on all three, and you somewhere: every state's words are the longest the page has.
        var actions = new MapActions(config, library, book, name => name);
        foreach (var waypoint in library.Items) actions.AddToRoute(waypoint);
        actions.SkipStop();
        actions.RouteAt(900_000, 900_000);
        var dialog = new SettingsWindow(config, library, book, poll, actions: actions);
        // The frame is the test's, not this PC's: the work area's limit is a small screen's safety net.
        ((FrameworkElement)dialog.Content).MaxHeight = double.PositiveInfinity;
        return (dialog, site);
    }

    /// <summary>Three skins as the website lists them, one locked, none with a picture (so nothing is ever fetched; SettingsThemeTests draws them too).</summary>
    internal const string Skins = """
        { "success": true, "skins": [
          { "id": 12, "name": "Ember", "requiredRole": "Patreon | Silver", "locked": false,
            "colors": { "md": {"R":0.9,"G":0.3,"B":0.1}, "m": {"R":0.3,"G":0.1,"B":0.05}, "b": {"R":0.95,"G":0.7,"B":0.2} } },
          { "id": 13, "name": "Glacier", "requiredRole": "Pandorian Fanatic", "locked": true },
          { "id": 15, "name": "Night", "locked": false }
        ] }
        """;

    /// <summary>
    /// Every page on screen in turn, laid out at the dialog's own size, and the
    /// Waypoints page once more with an import's result shown under its list:
    /// the list takes the frame's room but the two lines the status keeps (Oct
    /// 31 2026), and a page that fitted only while its status was hidden would
    /// scroll on an import. The Route page in each state it has: walking (the
    /// dialog's start), asking before a paste or a clear, with a paste's longest
    /// result under the list, paused (the most on its button row), finished and
    /// empty, in that order since clearing it can't be undone. The caller closes
    /// the dialog.
    /// </summary>
    internal static IEnumerable<(string Page, FrameworkElement Root)> EveryPage(SettingsWindow dialog)
    {
        foreach (var page in Pages)
        {
            yield return (page, ShowPage(dialog, page));
            if (page == "Route")
            {
                var actions = (MapActions)(typeof(SettingsWindow).GetField("_actions", Any)?.GetValue(dialog)
                                           ?? throw new MissingFieldException(nameof(SettingsWindow), "_actions"));
                var routeStatus = (TextBlock)dialog.FindName("RouteStatus");
                routeStatus.Text = RouteCode.PastedWords(stops: Route.Capacity, added: 40, reused: 24, inText: 80); // the longest line it writes
                routeStatus.Visibility = Visibility.Visible;
                yield return ("Route, after a paste", LaidOut(dialog));
                var (paste, clear) = ((Button)dialog.FindName("RoutePasteButton"), (Button)dialog.FindName("RouteClearButton"));
                actions.Track(null); // paused: Resume is the widest thing on the row, and the armed labels the widest there
                paste.Content = "Replace route?";
                yield return ("Route, paused, asking before a paste", LaidOut(dialog));
                paste.Content = "Paste";
                clear.Content = "Clear all?";
                yield return ("Route, paused, asking before a clear", LaidOut(dialog));
                clear.Content = "Clear";
                routeStatus.Visibility = Visibility.Hidden;
                yield return ("Route, paused", LaidOut(dialog));
                actions.ResumeRoute();
                actions.SkipStop();
                actions.SkipStop();
                yield return ("Route, finished", LaidOut(dialog));
                actions.ClearRoute();
                yield return ("Route, empty", LaidOut(dialog));
            }
            if (page != "Waypoints") continue;
            var status = (TextBlock)dialog.FindName("WaypointStatus"); // the longest line the page writes: every clause, a 32-letter pack name
            status.Text = "Imported 122 from \"A pack with a thirty-two letters\" · 134 already here · 122 didn't fit (256 max). They start hidden: Show all on the pack, or tick Show per waypoint.";
            status.Visibility = Visibility.Visible;
            yield return ("Waypoints, after an import", LaidOut(dialog));
            status.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>One page on screen, laid out at the dialog's own size (as WPF lays the shown window out).</summary>
    private static FrameworkElement ShowPage(SettingsWindow dialog, string page)
    {
        Call(dialog, "SetPage", page);
        if (page == "Skins")
        {
            // Signed out, the page shows no tiles: draw them from a list it holds, every one.
            Set(dialog, "_skins", PatreonSkins.ParseList(JsonDocument.Parse(Skins).RootElement));
            ((RadioButton)dialog.FindName("SkinsAll")).IsChecked = true;
            Call(dialog, "RenderSkins");
        }
        if (page == "Storage")
        {
            // Signed out, its opening drops any list: draw one it holds, its card open.
            var list = StoredDinos.ParseList(JsonDocument.Parse(SettingsThemeTests.Storage).RootElement)
                       ?? throw new InvalidOperationException("the storage sample did not parse");
            Set(dialog, "_storage", list);
            Set(dialog, "_openDinoId", list.Dinos[0].Id);
            Call(dialog, "RenderStorage");
        }
        return LaidOut(dialog);
    }

    private static FrameworkElement LaidOut(Window window)
    {
        var root = (FrameworkElement)window.Content;
        root.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        root.Arrange(new Rect(root.DesiredSize));
        root.UpdateLayout();
        return root;
    }

    /// <summary>
    /// The update cards in four states and the sign-in window in five, each
    /// laid out at its width (the sign-in at its frame) and closed once the
    /// caller has walked it. Nothing here talks to anyone: the cards' install
    /// is a stand-in, and the sign-in's WebView2 starts only on Loaded, which a
    /// window never shown never reaches.
    /// </summary>
    internal static IEnumerable<(string Where, FrameworkElement Root)> CardsAndSignIn()
    {
        const string notes = """
            ### New
            - A Themes page in Settings: every window changes at once, sizes and positions stay.
            ### Fixes
            - A fix with a line long enough to wrap onto a second line inside the card's notes box, as real notes do.
            """;
        static FrameworkElement At(Window window, double width, double? height = null)
        {
            var root = (FrameworkElement)window.Content;
            root.Measure(new Size(width, height ?? double.PositiveInfinity));
            root.Arrange(new Rect(new Size(width, height ?? root.DesiredSize.Height)));
            root.UpdateLayout();
            return root;
        }

        var windows = new List<(string, Func<Window>, double, double?)>
        {
            ("the update card", () => UpdateCardWindow.Available("1.39.0", "1.38.0", notes, _ => Task.FromResult(false)), 460, null),
            ("the update card, downloading", () =>
            {
                var downloading = UpdateCardWindow.Available("1.39.0", "1.38.0", notes, _ => Task.FromResult(false));
                ((FrameworkElement)downloading.FindName("ButtonRow")).Visibility = Visibility.Collapsed;
                ((FrameworkElement)downloading.FindName("ProgressRow")).Visibility = Visibility.Visible;
                Call(downloading, "ShowProgress", 42);
                return downloading;
            }, 460, null),
            ("the updated card", () => UpdateCardWindow.Updated("1.39.0", "1.38.0", notes), 460, null),
            ("What's new", () => UpdateCardWindow.Notes("1.38.0", notes), 460, null),
        };
        // Never shown: its WebView2 starts only on Loaded. At its frame's size (SignInFrame).
        void SignIn(string where, Action<SignInWindow> state) =>
            windows.Add(($"the sign-in window, {where}", () => { var w = new SignInWindow(); state(w); return w; }, SignInFrame.Width, SignInFrame.Height));
        SignIn("browsing", w => Call(w, "ShowAddress", "https://discord.com/login"));
        SignIn("checking", w => Call(w, "ShowChecking"));
        SignIn("done", w => Call(w, "ShowDone", new AccountInfo(true, "Dave", "76561190000000001", null, true, true)));
        SignIn("no Steam", w => Call(w, "ShowNoSteam", new AccountInfo(true, "Dave", null, "AB12CD", true, false)));
        SignIn("failed", w => Call(w, "ShowFailed", "the website did not answer (HttpRequestException)", SignInPolicy.FailureAdvice));
        var (blocked, blockedAdvice) = SignInPolicy.ConfirmFailure(new System.Net.Http.HttpRequestException("", null, System.Net.HttpStatusCode.Forbidden));
        SignIn("blocked", w => Call(w, "ShowFailed", blocked, blockedAdvice));

        foreach (var (where, build, width, height) in windows)
        {
            var window = build();
            try
            {
                yield return (where, At(window, width, height));
            }
            finally
            {
                window.Close();
            }
        }
    }

    // ---- On screen --------------------------------------------------------------

    /// <summary>The theme on screen for the test, on an STA thread, and Default again after it, whatever happened.</summary>
    private static void OnScreen(string id, Action test) => TheThemeOnScreen.OnSta(() =>
    {
        Theme.EnsureLoaded();
        try
        {
            Theme.Apply(id);
            test();
        }
        finally
        {
            Theme.Apply(Theme.DefaultId);
        }
    });

    private static void Set(object target, string field, object? value) =>
        (target.GetType().GetField(field, Any) ?? throw new MissingFieldException(target.GetType().Name, field)).SetValue(target, value);

    private static void Call(object target, string method, params object[] args) =>
        (target.GetType().GetMethods(Any).FirstOrDefault(m => m.Name == method && m.GetParameters().Length == args.Length)
            ?? throw new MissingMethodException(target.GetType().Name, method)).Invoke(target, args);
}
