using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Xunit;

namespace PandoraOverlay.Tests;

/// <summary>
/// The dialogs in every theme (the themes' step 6): the Settings dialog
/// keeps its fixed frame (SettingsFrame, the owner's call, Oct 25 2026,
/// question 30) and every page fits inside it with nothing to scroll; and
/// no line of text in the Settings dialog, the update cards or the sign-in
/// window runs past its room in a theme's font. A string that no longer fits
/// ends in an ellipsis (the owner, question 29: never another wording, never
/// a size of its own) and is listed in <see cref="TrimmedByDesign"/>, so a
/// NEW overflow still fails and reaches the owner before the theme ships.
/// <para>
/// Laid out, not guessed: each window is built as the harness builds it
/// (fresh objects, no cookie, a PollService never started, never shown),
/// on every page, and each one-line text's natural width in its own font is
/// held to the narrowest of the boxes around it. A wrapping line can't run
/// past an edge; it can only make its page taller, which the frame check
/// catches (the hints, the footer sentence beside Close). The pixels are the
/// golden renders' windows mode.
/// </para>
/// </summary>
[Collection(TheThemeOnScreen.Name)]
public class DialogLayoutTests
{
    private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

    /// <summary>Layout rounding's room: a line within half a pixel of its box is drawn whole.</summary>
    private const double Slack = 0.5;

    /// <summary>Every page by its SetPage key, in the nav's order.</summary>
    private static readonly string[] Pages =
        { "Account", "General", "Themes", "Hotkeys", "Widgets", "Maps", "Waypoints", "Friends", "Skins", "Storage", "Rules", "About" };

    /// <summary>
    /// What fits in Default but not in a theme's font, and ends in an
    /// ellipsis BY DESIGN (question 29). Listed one by one: a string that runs
    /// over and isn't here fails, one listed here must trim (never run past
    /// the edge), and one that fits again must come off the list.
    /// </summary>
    private static readonly HashSet<(string Theme, string Window, string Text)> TrimmedByDesign = new()
    {
        // Widgets → Activity, 12 px Montserrat Medium: 450 px in the 437 the
        // page leaves a checkbox's label (Segoe UI: 392). The checkbox trims
        // its label (Controls.xaml's Check) rather than wrap it: a second
        // line would move the rows under it in Isle only.
        ("isle", Settings, "Name the island's areas: entering one, and where you or a friend spawned"),
        // Skins, a locked tile's tier line, 10 px: 133 px in the tile's 124
        // (Segoe UI: 117). The line has always trimmed a long tier; the tile
        // is dimmed and its button says Locked, so the cut word is said twice.
        ("isle", Settings, "Pandorian Fanatic · locked"),
    };

    // The windows the list names: what each test lays out.
    private const string Settings = "the Settings dialog";
    private const string Cards = "the cards and the sign-in window";

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
        var dialog = Dialog();
        var problems = new List<string>();
        foreach (var page in Pages)
        {
            var root = ShowPage(dialog, page);
            if (root.DesiredSize != new Size(SettingsFrame.Width, SettingsFrame.Height))
                problems.Add($"{page}: the dialog is {root.DesiredSize.Width:0.##} x {root.DesiredSize.Height:0.##}, not its frame");
            var column = (ScrollViewer)((FrameworkElement)dialog.FindName("Pages")).Parent;
            if (column.ExtentHeight > column.ViewportHeight + Slack)
                problems.Add($"{page}: {column.ExtentHeight:0.0} px of pages in {column.ViewportHeight:0.0}");
        }
        dialog.Close();
        Assert.True(problems.Count == 0, string.Join("; ", problems));
    });

    /// <summary>Every one-line text on every Settings page, the nav, the title and the footer, with a few waypoints, three skins' tiles and an open stored dino.</summary>
    [Theory]
    [MemberData(nameof(Themes))]
    public void NoLineInTheSettingsDialogRunsPastItsRoom(string id) => OnScreen(id, () =>
    {
        var dialog = Dialog();
        var found = new List<Overflow>();
        foreach (var page in Pages)
        {
            var root = ShowPage(dialog, page);
            found.AddRange(Overflows(root).Select(o => o with { Where = page }));
        }
        dialog.Close();
        AssertOnlyByDesign(id, Settings, found);
    });

    /// <summary>The update cards (available, downloading, updated, What's new) and the sign-in window's states.</summary>
    [Theory]
    [MemberData(nameof(Themes))]
    public void NoLineInTheCardsOrTheSignInWindowRunsPastItsRoom(string id) => OnScreen(id, () =>
    {
        const string notes = """
            ### New
            - A Themes page in Settings: Default, the look you have, and Isle, the game's own look.
            ### Fixes
            - A fix with a line long enough to wrap onto a second line inside the card's notes box, as real notes do.
            """;
        var found = new List<Overflow>();
        void Check(string where, Window window, double width, double? height = null)
        {
            var root = (FrameworkElement)window.Content;
            root.Measure(new Size(width, height ?? double.PositiveInfinity));
            root.Arrange(new Rect(new Size(width, height ?? root.DesiredSize.Height)));
            root.UpdateLayout();
            found.AddRange(Overflows(root).Select(o => o with { Where = where }));
            window.Close();
        }

        Check("the update card", UpdateCardWindow.Available("1.39.0", "1.38.0", notes, _ => Task.FromResult(false)), 460);
        var downloading = UpdateCardWindow.Available("1.39.0", "1.38.0", notes, _ => Task.FromResult(false));
        ((FrameworkElement)downloading.FindName("ButtonRow")).Visibility = Visibility.Collapsed;
        ((FrameworkElement)downloading.FindName("ProgressRow")).Visibility = Visibility.Visible;
        Call(downloading, "ShowProgress", 42);
        Check("the update card, downloading", downloading, 460);
        Check("the updated card", UpdateCardWindow.Updated("1.39.0", "1.38.0", notes), 460);
        Check("What's new", UpdateCardWindow.Notes("1.38.0", notes), 460);

        // Never shown: its WebView2 starts only on Loaded. 620 x 690, its XAML's size.
        void SignIn(string where, Action<SignInWindow> state)
        {
            var window = new SignInWindow();
            state(window);
            Check($"the sign-in window, {where}", window, 620, 690);
        }
        SignIn("browsing", w => Call(w, "ShowAddress", "https://discord.com/login"));
        SignIn("checking", w => Call(w, "ShowChecking"));
        SignIn("done", w => Call(w, "ShowDone", new AccountInfo(true, "Dave", "76561190000000001", null, true, true)));
        SignIn("no Steam", w => Call(w, "ShowNoSteam", new AccountInfo(true, "Dave", null, "AB12CD", true, false)));
        SignIn("failed", w => Call(w, "ShowFailed", "the website did not answer (HttpRequestException)"));

        AssertOnlyByDesign(id, Cards, found);
    });

    // ---- The dialog, as the harness builds it --------------------------------------

    /// <summary>Fresh objects, no cookie, a PollService never started; three waypoints (the skins and the stored dino come with their pages).</summary>
    private static SettingsWindow Dialog()
    {
        var config = new OverlayConfig();
        var library = new WaypointLibrary();
        library.Add("Near Spot", 1000, 2000, 0);
        library.Add("A waypoint with a long name here", -150000, 230000, 4); // 32 letters, the most a name takes
        library.Add("Lake", 52000, -98000, 9);
        var poll = new PollService(config);
        var dialog = new SettingsWindow(config, library, new FriendBook(), poll);
        // The frame is the test's, not this PC's: the work area's limit is a small screen's safety net.
        ((FrameworkElement)dialog.Content).MaxHeight = double.PositiveInfinity;
        return dialog;
    }

    /// <summary>Three skins as the website lists them, one locked, none with a picture (so nothing is ever fetched).</summary>
    private const string Skins = """
        { "success": true, "skins": [
          { "id": 12, "name": "Ember", "requiredRole": "Patreon | Silver", "locked": false,
            "colors": { "md": {"R":0.9,"G":0.3,"B":0.1}, "m": {"R":0.3,"G":0.1,"B":0.05}, "b": {"R":0.95,"G":0.7,"B":0.2} } },
          { "id": 13, "name": "Glacier", "requiredRole": "Pandorian Fanatic", "locked": true },
          { "id": 15, "name": "Night", "locked": false }
        ] }
        """;

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
        var root = (FrameworkElement)dialog.Content;
        root.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        root.Arrange(new Rect(root.DesiredSize));
        root.UpdateLayout();
        return root;
    }

    // ---- The check ------------------------------------------------------------

    private sealed record Overflow(string Text, double Natural, double Room, bool Trims, string Where = "");

    /// <summary>
    /// Every one-line text that is on screen (no hidden page or collapsed
    /// part around it) and runs past its room: its natural width, in its own
    /// font, against the narrowest box around it up to the window's root (a
    /// trimming text's room is its own width).
    /// </summary>
    private static IEnumerable<Overflow> Overflows(FrameworkElement root)
    {
        foreach (var text in Walk(root).OfType<TextBlock>())
        {
            if (text.TextWrapping != TextWrapping.NoWrap || !Shown(text, root) || NaturalWidth(text) is not { } natural) continue;
            var origin = text.TranslatePoint(new Point(0, 0), root).X;
            var room = text.TextTrimming == TextTrimming.None ? double.MaxValue : text.ActualWidth;
            for (var up = VisualTreeHelper.GetParent(text); up is not null; up = VisualTreeHelper.GetParent(up))
            {
                if (up is FrameworkElement { ActualWidth: > 0 } box)
                    room = Math.Min(room, box.TranslatePoint(new Point(box.ActualWidth, 0), root).X - origin);
                if (ReferenceEquals(up, root)) break;
            }
            if (natural > room + Slack) yield return new Overflow(TextOf(text), natural, room, text.TextTrimming != TextTrimming.None);
        }
    }

    private static void AssertOnlyByDesign(string id, string window, IReadOnlyList<Overflow> found)
    {
        var problems = new List<string>();
        foreach (var o in found)
        {
            var listed = TrimmedByDesign.Contains((id, window, o.Text));
            if (!listed) problems.Add($"{o.Where}: \"{o.Text}\" {o.Natural:0.0} px in {o.Room:0.0}");
            else if (!o.Trims) problems.Add($"{o.Where}: \"{o.Text}\" is listed as trimmed by design but runs past the edge without an ellipsis");
        }
        foreach (var (_, _, text) in TrimmedByDesign.Where(t => t.Theme == id && t.Window == window))
        {
            if (!found.Any(o => o.Text == text)) problems.Add($"\"{text}\" fits again in {id}: take it off TrimmedByDesign");
        }
        Assert.True(problems.Count == 0, string.Join("; ", problems));
    }

    /// <summary>On screen if nothing around it is hidden or collapsed (a window never shown has no IsVisible to ask).</summary>
    private static bool Shown(DependencyObject element, FrameworkElement root)
    {
        for (var up = element; up is not null; up = VisualTreeHelper.GetParent(up))
        {
            if (up is UIElement { Visibility: not Visibility.Visible }) return false;
            if (ReferenceEquals(up, root)) return true;
        }
        return true;
    }

    /// <summary>A text's one line in its own fonts, run by run; null for what is no single line of runs (a line break, nothing at all).</summary>
    private static double? NaturalWidth(TextBlock text)
    {
        if (text.Inlines.Count == 0) return string.IsNullOrEmpty(text.Text) ? null : Width(text.Text, text);
        if (text.Inlines.Any(i => i is not Run)) return null;
        var width = text.Inlines.Cast<Run>().Sum(r => string.IsNullOrEmpty(r.Text) ? 0 : Width(r.Text, r));
        return width > 0 ? width : null;
    }

    private static string TextOf(TextBlock text) =>
        text.Inlines.Count == 0 ? text.Text : string.Concat(text.Inlines.OfType<Run>().Select(r => r.Text));

    private static double Width(string s, DependencyObject font) =>
        new FormattedText(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface((FontFamily)font.GetValue(TextElement.FontFamilyProperty), (FontStyle)font.GetValue(TextElement.FontStyleProperty),
                             (FontWeight)font.GetValue(TextElement.FontWeightProperty), (FontStretch)font.GetValue(TextElement.FontStretchProperty)),
                (double)font.GetValue(TextElement.FontSizeProperty), Brushes.White, 1.0)
            .WidthIncludingTrailingWhitespace;

    private static IEnumerable<DependencyObject> Walk(DependencyObject d)
    {
        yield return d;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++)
            foreach (var child in Walk(VisualTreeHelper.GetChild(d, i))) yield return child;
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
