using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using PandoraOverlay.Games;

namespace PandoraOverlay;

/// <summary>
/// The grid (G1, G19): one tile per listed game, in the catalog's order, three
/// to a row, built in code from what each game says now. A tile is a card of
/// fixed size (<see cref="GamesFrame"/>); every text on it is one line that
/// trims, so no game's words and no theme's font can grow it. Its one button
/// arms in place for what can't be taken back (.claude/rules/settings.md: the
/// first click arms, the label changes and the button turns Danger, leaving
/// the tile or a click anywhere else disarms, nothing moves between the two
/// clicks, no timer, no modal box).
/// <para>
/// Every call into a game here is guarded (<see cref="HostedGame.Tile"/>,
/// <see cref="HostedGame.CreateTileArt"/>, placing the art), and a game that
/// faults while its tile is built is stopped by the guard, whose
/// ConditionChanged lands back here: a rebuild already under way ignores it
/// (<see cref="_building"/>) and draws the stopped game's words itself.
/// </para>
/// </summary>
internal partial class GamesWindow
{
    /// <summary>One tile's parts, kept so a tile change redraws its texts in place.</summary>
    private sealed class TileView
    {
        public TileView(HostedGame game) => Game = game;

        public HostedGame Game { get; }
        public Border Card { get; } = new();
        public Border Well { get; } = new();
        public TextBlock Title { get; } = Line("Theme.Size.13");
        public TextBlock Tag { get; } = Line("Theme.Size.9.5");
        public TextBlock Pitch { get; } = Line("Theme.Size.11");
        public TextBlock Status { get; } = Line("Theme.Size.11");
        public TextBlock Detail { get; } = Line("Theme.Size.10.5");
        public Button Button { get; } = new();
        public TextBlock ButtonText { get; } = new() { TextTrimming = TextTrimming.CharacterEllipsis };

        /// <summary>A click on the tile opens the game (a Ready game with its own tile only).</summary>
        public bool Opens { get; set; }

        /// <summary>The button arms first (Reset, Start fresh); false: one click (Start it again).</summary>
        public bool Arms { get; set; }

        public string Label { get; set; } = "";
        public string ArmedLabel { get; set; } = "";

        /// <summary>The detail line as drawn, before a refusal took its place.</summary>
        public string DetailText { get; set; } = "";

        public bool Hovered { get; set; }
    }

    private readonly List<TileView> _tiles = new();

    /// <summary>The tile whose button is armed; null when none.</summary>
    private TileView? _armed;

    /// <summary>The tile the left button went down on: a click opens it only when it comes up on the same tile.</summary>
    private TileView? _pressed;

    /// <summary>A tile is being built: a ConditionChanged raised from inside (a game that faulted on the way) is drawn by the build itself.</summary>
    private bool _building;

    /// <summary>Every tile again, in the catalog's order.</summary>
    private void BuildTiles()
    {
        Disarm();
        _pressed = null;
        Tiles.Width = GamesFrame.GridPanelWidth;
        Tiles.Margin = new Thickness(GamesFrame.GridSidePadding, GamesFrame.GridTopPadding, 0, 0);
        _building = true;
        try
        {
            Tiles.Children.Clear();
            _tiles.Clear();
            foreach (var game in _host.Games)
            {
                var tile = MakeTile(game);
                _tiles.Add(tile);
                Tiles.Children.Add(tile.Card);
            }
        }
        finally
        {
            _building = false;
        }
    }

    /// <summary>One tile again, in its place (RemoveAt, then Insert: never an assignment into an occupied index).</summary>
    private void RebuildTile(HostedGame game)
    {
        if (_building) return;
        var at = _tiles.FindIndex(t => t.Game == game);
        if (at < 0) return;
        if (_armed?.Game == game) Disarm();
        if (_pressed?.Game == game) _pressed = null;
        _building = true;
        try
        {
            var tile = MakeTile(game);
            Tiles.Children.RemoveAt(at);
            Tiles.Children.Insert(at, tile.Card);
            _tiles[at] = tile;
        }
        finally
        {
            _building = false;
        }
    }

    private TileView? TileOf(HostedGame game) => _tiles.FirstOrDefault(t => t.Game == game);

    /// <summary>A tile's one-line text at a body size (the theme's, at its fit factor), trimmed: a game's long words end in an ellipsis, never grow the tile.</summary>
    private static TextBlock Line(string sizeKey)
    {
        var text = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.NoWrap };
        text.SetResourceReference(TextBlock.FontSizeProperty, sizeKey);
        text.SetResourceReference(TextBlock.FontWeightProperty, "Theme.Font.BodyWeight");
        return text;
    }

    // ---- Building a tile -----------------------------------------------------------

    /// <summary>A card of fixed size: the art in its well, the title and the tag, the pitch, the status, the detail, a button row at the bottom.</summary>
    private TileView MakeTile(HostedGame game)
    {
        var t = new TileView(game);
        var card = t.Card;
        card.Width = GamesFrame.TileWidth;
        card.Height = GamesFrame.TileHeight;
        card.BorderThickness = new Thickness(1);
        card.Padding = new Thickness(4);
        card.Margin = new Thickness(0, 0, GamesFrame.TileGap, GamesFrame.TileGap);
        card.SetResourceReference(Border.BackgroundProperty, "Theme.Card.Surface");
        card.SetResourceReference(Border.CornerRadiusProperty, "Theme.Corner.Card");
        card.MouseEnter += (_, _) => { t.Hovered = true; ShowLine(t); };
        card.MouseLeave += (_, _) =>
        {
            t.Hovered = false;
            ShowLine(t);
            if (_armed == t) Disarm(); // leaving the tile disarms
            if (_pressed == t) _pressed = null;
        };
        card.MouseLeftButtonDown += (_, _) => _pressed = t.Opens ? t : null; // the button handles its own presses: they never reach here
        card.MouseLeftButtonUp += (_, _) =>
        {
            if (_pressed != t) return;
            _pressed = null;
            if (t.Opens && t.Game.Plays) ShowGame(t.Game);
        };

        var well = t.Well;
        well.Width = GamesFrame.TileArtWidth + 2;
        well.Height = GamesFrame.TileArtHeight + 2;
        well.BorderThickness = new Thickness(1);
        well.SetResourceReference(Border.BackgroundProperty, "Theme.Well.Surface");
        well.SetResourceReference(Border.BorderBrushProperty, "Theme.Well.Line");
        well.SetResourceReference(Border.CornerRadiusProperty, "Theme.Corner.Well");
        DockPanel.SetDock(well, Dock.Top);

        t.Title.SetResourceReference(TextBlock.ForegroundProperty, "Theme.Text.Bright");
        t.Title.SetResourceReference(TextBlock.FontWeightProperty, "Theme.Font.TitleWeight");
        t.Tag.SetResourceReference(TextBlock.ForegroundProperty, "Theme.Text.Dim");
        t.Tag.Margin = new Thickness(6, 0, 0, 0);
        t.Tag.VerticalAlignment = VerticalAlignment.Center;
        t.Tag.MaxWidth = GamesFrame.TileWidth / 3; // a long tag trims before it takes the title's room
        DockPanel.SetDock(t.Tag, Dock.Right);
        var titleRow = new DockPanel { LastChildFill = true };
        titleRow.Children.Add(t.Tag);
        titleRow.Children.Add(t.Title);
        t.Pitch.SetResourceReference(TextBlock.ForegroundProperty, "Theme.Text.Dim");
        t.Pitch.Margin = new Thickness(0, 1, 0, 0);
        t.Status.Margin = new Thickness(0, 3, 0, 0);
        t.Detail.Margin = new Thickness(0, 1, 0, 0);
        var texts = new StackPanel { Margin = new Thickness(2, 4, 2, 0) };
        texts.Children.Add(titleRow);
        texts.Children.Add(t.Pitch);
        texts.Children.Add(t.Status);
        texts.Children.Add(t.Detail);

        var button = t.Button;
        button.Width = GamesFrame.TileButtonWidth;
        button.Padding = new Thickness(6, 2, 6, 2);
        button.HorizontalAlignment = HorizontalAlignment.Right;
        button.VerticalAlignment = VerticalAlignment.Center;
        button.Focusable = false; // the keyboard closes the window here, it never clicks a tile's button
        button.Content = t.ButtonText;
        button.SetResourceReference(StyleProperty, "NeutralButtonStyle");
        button.Click += (_, _) => TileButton_Click(t);
        var buttonRow = new Grid { Height = GamesFrame.TileButtonRowHeight };
        buttonRow.Children.Add(button);
        DockPanel.SetDock(buttonRow, Dock.Bottom);

        var dock = new DockPanel { LastChildFill = true };
        dock.Children.Add(well);
        dock.Children.Add(buttonRow);
        dock.Children.Add(texts);
        card.Child = dock;

        Fill(t);
        ShowLine(t);
        return t;
    }

    /// <summary>
    /// What the tile says, by the game's condition: a Ready game its own tile
    /// and art; one that can't play the host's words and its one button (A10,
    /// A7), its title where its art would be. A game that faults while asked
    /// is drawn as the stopped game it now is.
    /// </summary>
    private void Fill(TileView t)
    {
        var game = t.Game;
        t.Title.Text = game.Title;
        t.Pitch.Text = game.Pitch;
        if (game.Plays && game.Tile() is { } tile && game.CreateTileArt() is { } art &&
            GameGuard.Run(game, "CreateTileArt", () => { t.Well.Child = art; }))
        {
            t.Opens = true;
            t.Card.Cursor = Cursors.Hand;
            ShowTexts(t, tile);
            return;
        }

        t.Well.Child = null;
        t.Opens = false;
        t.Card.Cursor = null;
        t.Tag.Text = "";
        var name = new TextBlock
        {
            Text = game.Title,
            TextTrimming = TextTrimming.CharacterEllipsis,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 8, 0),
        };
        name.SetResourceReference(TextBlock.FontSizeProperty, "Theme.Size.14");
        name.SetResourceReference(TextBlock.FontWeightProperty, "Theme.Font.TitleWeight");
        name.SetResourceReference(TextBlock.ForegroundProperty, "Theme.Text.Dim");
        t.Well.Child = name;

        var (status, detail, label, armed) = game.Condition switch
        {
            GameCondition.Faulted => (GamesText.FaultedStatus, GamesText.FaultedDetail, GamesText.StartAgain, ""),
            GameCondition.SaveUnreadable => (GamesText.UnreadableStatus, GamesText.UnreadableDetail, GamesText.StartFresh, GamesText.StartFreshArmed),
            GameCondition.SaveFromNewerVersion => (GamesText.NewerStatus, GamesText.NewerDetail, GamesText.StartFresh, GamesText.StartFreshArmed),
            _ => ("", "", "", ""), // Ready but not attached yet: nothing to say, nothing to click (MainWindow attaches on Loaded, before any opening)
        };
        t.Status.Text = status;
        t.Status.SetResourceReference(TextBlock.ForegroundProperty, "Theme.Text.Caution");
        ShowDetail(t, detail);
        ShowButton(t, label, armed);
    }

    /// <summary>A Ready game's own words, in place: status in its tone, detail, tag, and the Reset when it offers one.</summary>
    private void ShowTexts(TileView t, GameTile tile)
    {
        t.Tag.Text = tile.Tag ?? "";
        t.Status.Text = tile.Status;
        t.Status.SetResourceReference(TextBlock.ForegroundProperty, tile.Tone switch
        {
            GameTileTone.Good => "Theme.Text.Good",
            GameTileTone.Attention => "Theme.Text.Caution",
            _ => "Theme.Text.Dim",
        });
        ShowDetail(t, tile.Detail ?? "");
        ShowButton(t, tile.CanReset ? GamesText.Reset : "", GamesText.ResetArmed);
    }

    /// <summary>The detail line as the game or the host says it (dim): a refusal shown there is gone.</summary>
    private static void ShowDetail(TileView t, string text)
    {
        t.DetailText = text;
        t.Detail.Text = text;
        t.Detail.SetResourceReference(TextBlock.ForegroundProperty, "Theme.Text.Dim");
    }

    /// <summary>The tile's one button: none for an empty label; arming when it has an armed label.</summary>
    private static void ShowButton(TileView t, string label, string armedLabel)
    {
        t.Label = label;
        t.ArmedLabel = armedLabel;
        t.Arms = armedLabel.Length > 0;
        t.ButtonText.Text = label;
        t.Button.SetResourceReference(StyleProperty, "NeutralButtonStyle");
        t.Button.Visibility = label.Length > 0 ? Visibility.Visible : Visibility.Hidden; // Hidden: the row keeps its room
    }

    /// <summary>The card's line: the game played last wears the highlight, under the mouse too (A46); another the hover line under the mouse.</summary>
    private void ShowLine(TileView t)
    {
        var key = t.Game.Id == _config.GamesLastPlayed ? "Theme.Text.Highlight"
                : t.Hovered ? "Theme.Card.HoverLine"
                : "Theme.Card.Line";
        t.Card.SetResourceReference(Border.BorderBrushProperty, key);
    }

    /// <summary>The game said its tile changed: its texts in place. A tile that faults on the way has been rebuilt as a stopped game's.</summary>
    private void RedrawTile(TileView t)
    {
        if (!t.Opens || t.Game.Tile() is not { } tile) return;
        if (TileOf(t.Game) != t) return; // rebuilt meanwhile (the game faulted answering)
        if (_armed == t) Disarm();
        ShowTexts(t, tile);
    }

    // ---- The button -----------------------------------------------------------------

    /// <summary>
    /// "Start it again" at once; "Reset" and "Start fresh" on the armed second
    /// click. The host saves and keeps first (A38, A49): a refusal resets or
    /// starts nothing, and the tile's detail line says so in place, until the
    /// tile is next drawn or its button is clicked again.
    /// </summary>
    private void TileButton_Click(TileView t)
    {
        var game = t.Game;
        if (!t.Arms)
        {
            ShowDetail(t, t.DetailText);
            // False means one thing: what the stopped game had handed over could
            // not be written first, so nothing was started (A49). True: the host
            // raised ConditionChanged where something changed.
            if (!_host.StartAgain(game) && TileOf(game) == t) Refuse(t, GamesText.StartAgainRefused);
            return;
        }
        if (_armed != t)
        {
            Arm(t);
            return;
        }
        Disarm();
        var before = game.Condition;
        // A Reset that threw answers false too, but the game is then Faulted and
        // its tile already rebuilt: the refusal line is only for a game whose
        // condition is what it was (nothing happened at all).
        if (!_host.StartFresh(game) && game.Condition == before && TileOf(game) == t) Refuse(t, GamesText.ResetRefused);
    }

    private static void Refuse(TileView t, string words)
    {
        t.Detail.Text = words;
        t.Detail.SetResourceReference(TextBlock.ForegroundProperty, "Theme.Text.Caution");
    }

    /// <summary>The first click: the label says what the second does, the button turns Danger, nothing moves (its width is fixed).</summary>
    private void Arm(TileView t)
    {
        Disarm();
        ShowDetail(t, t.DetailText); // armed again: a refusal shown there is gone
        _armed = t;
        t.ButtonText.Text = t.ArmedLabel;
        t.Button.SetResourceReference(StyleProperty, "DangerButtonStyle");
    }

    /// <summary>Whatever is armed, back to its first label and its neutral look.</summary>
    private void Disarm()
    {
        if (_armed is not { } t) return;
        _armed = null;
        t.ButtonText.Text = t.Label;
        t.Button.SetResourceReference(StyleProperty, "NeutralButtonStyle");
    }

    /// <summary>A click anywhere but on the armed button disarms it (before that click does its own work: on another tile's button, that one arms).</summary>
    private void Window_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_armed is { } t && !Within(e.OriginalSource as DependencyObject, t.Button)) Disarm();
    }

    private static bool Within(DependencyObject? element, DependencyObject ancestor)
    {
        for (var up = element; up is not null; up = up is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(up) : LogicalTreeHelper.GetParent(up))
            if (ReferenceEquals(up, ancestor)) return true;
        return false;
    }
}
