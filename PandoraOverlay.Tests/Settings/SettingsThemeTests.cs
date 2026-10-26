using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Windows.Controls;
using Xunit;

namespace PandoraOverlay.Tests;

/// <summary>
/// Tests that put a theme on screen: Theme.Current is the app's, so they
/// never run beside another test (a map name measured meanwhile would take
/// the other theme's font).
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class TheThemeOnScreen
{
    public const string Name = "The theme on screen";

    /// <summary>A window needs an STA thread; the test's failure comes back to xunit's.</summary>
    public static void OnSta(Action test)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { test(); }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) ExceptionDispatchInfo.Capture(failure is TargetInvocationException { InnerException: { } inner } ? inner : failure).Throw();
    }
}

/// <summary>
/// The Settings dialog's own repaint on a theme switch (the themes' step 5,
/// SettingsWindow.Themes.cs): a page built in code is redrawn from what it
/// holds, the page shown at once and the others on their next look, and
/// never by its Open…, so a theme click sends the website nothing (CLAUDE.md,
/// constraint 2: Dino storage's opening asks for the list past its guards).
/// The pixels are the golden renders' windows mode; this is the behaviour.
/// The dialog is built as the harness builds it (fresh objects, a PollService
/// never started, never shown), but SIGNED IN with a made-up cookie, so the
/// pages' guards would let a stray Open… through; its website client is a
/// counting fake (<see cref="CountingSite"/>), which counts that request and
/// lets nothing reach the website (the 1.39 constraint review, note 1: signed
/// out, the count could not fail).
/// </summary>
[Collection(TheThemeOnScreen.Name)]
public class SettingsThemeTests
{
    private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

    /// <summary>One stored dino, as /api/user/dinos answers (the parse tests' sample, cut down; DialogLayoutTests draws it too).</summary>
    internal const string Storage = """
        {
            "success": true,
            "dinos": [
                {
                    "_id": "0123456789abcdef01234567", "name": "Lefty", "dinoClass": "BP_Herrerasaurus_C",
                    "growth": 1, "gender": "Female",
                    "health": 225, "maxHealth": 225, "hunger": 35.9, "maxHunger": 74.25, "thirst": 839.9, "maxThirst": 1000,
                    "blood": 225, "maxBlood": 225, "stamina": 1000, "maxStamina": 1000,
                    "elderStacks": 0, "primeStatus": true, "isCompensated": false, "storedAt": "2026-09-23T09:42:33.776Z"
                }
            ],
            "storageLimit": 10
        }
        """;

    [Fact]
    public void ASwitchRedrawsTheBuiltPagesFromWhatTheyHoldAndAsksTheWebsiteNothing() => TheThemeOnScreen.OnSta(() =>
    {
        Theme.EnsureLoaded();
        var before = DefaultTheme.Tokens.Card.Surface.ToBrush();
        var other = DefaultTheme.Tokens with { Card = DefaultTheme.Tokens.Card with { Surface = ThemeColor.Parse("#123456") } };
        var after = other.Card.Surface.ToBrush();
        try
        {
            var config = new OverlayConfig();
            config.SetCookie("made-up-test-value-not-a-session"); // signed in: a stray Open… would get past the pages' guards
            var library = new WaypointLibrary();
            library.Add("Near Spot", 1000, 2000, 0);
            var poll = new PollService(config); // never started
            var site = CountingSite.Install(poll);
            SetStatic(typeof(SettingsWindow), "_lastPage", "General"); // the dialog opens on a page that asks nothing (signed in, Account asks auth/me)
            var dialog = new SettingsWindow(config, library, new FriendBook(), poll);
            Assert.Equal(0, site.Requests);

            // Three code-built pages drawn in Default: Dino storage and the skin tiles from held lists
            // (drawn, never opened: signed in, their openings would ask), the waypoint rows on their first look.
            Set(dialog, "_storage", StoredDinos.ParseList(JsonDocument.Parse(Storage).RootElement));
            Call(dialog, "RenderStorage");
            var storageCards = (StackPanel)dialog.FindName("StorageCards");
            Set(dialog, "_skins", PatreonSkins.ParseList(JsonDocument.Parse(DialogLayoutTests.Skins).RootElement)); // no pictures: nothing to fetch
            Call(dialog, "RenderSkins");
            var skinTiles = (Panel)dialog.FindName("SkinTiles");
            Call(dialog, "SetPage", "Waypoints");
            var waypointCards = (StackPanel)dialog.FindName("WaypointRows");
            Assert.Same(before, ((Border)storageCards.Children[0]).Background);
            Assert.Same(before, ((Border)skinTiles.Children[0]).Background);
            Assert.Same(before, ((Border)waypointCards.Children[0]).Background);

            // A theme switch with Dino storage on screen (MainWindow.ApplyTheme: the swap, then the dialog's repaint).
            SetStatic(typeof(SettingsWindow), "_lastPage", "Storage");
            Install(other);
            dialog.ApplyTheme();
            Assert.Equal(0, site.Requests); // the redraw asked nothing (an opening would have asked for the list)

            Assert.Same(after, ((Border)storageCards.Children[0]).Background); // the page shown: redrawn at once
            Assert.NotNull(Get(dialog, "_storage")); // from the list it held
            Assert.Same(before, ((Border)skinTiles.Children[0]).Background); // the pages not shown wait for their look
            Assert.Same(before, ((Border)waypointCards.Children[0]).Background);

            // The same switch seen from Skins (its opening would nudge a poll and ask for the list).
            SetStatic(typeof(SettingsWindow), "_lastPage", "Skins");
            dialog.ApplyTheme();
            Assert.Equal(0, site.Requests);
            Assert.Same(after, ((Border)skinTiles.Children[0]).Background); // redrawn from the held list
            Assert.NotNull(Get(dialog, "_skins"));

            Call(dialog, "SetPage", "Waypoints");
            Assert.Same(after, ((Border)waypointCards.Children[0]).Background); // redrawn on its next look

            Assert.Equal(0, site.Requests);

            // The fake does see what the page would send, had the redraw asked: one list request.
            Task.Run(poll.GetStorageAsync).GetAwaiter().GetResult();
            Assert.Equal(1, site.Requests);
        }
        finally
        {
            Install(DefaultTheme.Tokens);
            SetStatic(typeof(SettingsWindow), "_lastPage", "Account");
        }
    });

    /// <summary>Puts tokens on screen as Theme.Apply does, for a theme no file has (one card colour changed).</summary>
    private static void Install(ThemeTokens tokens) =>
        (typeof(Theme).GetMethod("Install", Any) ?? throw new MissingMethodException(nameof(Theme), "Install")).Invoke(null, new object[] { tokens });

    private static object? Get(object target, string field) =>
        (target.GetType().GetField(field, Any) ?? throw new MissingFieldException(target.GetType().Name, field)).GetValue(target);

    private static void Set(object target, string field, object? value) =>
        (target.GetType().GetField(field, Any) ?? throw new MissingFieldException(target.GetType().Name, field)).SetValue(target, value);

    private static void SetStatic(Type type, string field, object? value) =>
        (type.GetField(field, Any) ?? throw new MissingFieldException(type.Name, field)).SetValue(null, value);

    private static void Call(object target, string method, params object[] args) =>
        (target.GetType().GetMethods(Any).FirstOrDefault(m => m.Name == method && m.GetParameters().Length == args.Length)
            ?? throw new MissingMethodException(target.GetType().Name, method)).Invoke(target, args);
}
