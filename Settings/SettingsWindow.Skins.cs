using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace PandoraOverlay;

/// <summary>
/// SettingsWindow, Skins part (v1.28): the website's Patreon skins page as
/// tiles — picture, name, tier, the seven colour dots, Apply. Two things set
/// it apart from every other page: it talks to the site (the list and the
/// pictures, fetched when the page is first looked at and kept for the
/// session), and Apply acts AT ONCE instead of on Save — it changes your
/// dino's skin in game, like the site's own button. Apply turns into the
/// six pattern buttons (A–F) in place of the site's pop-up. All requests go
/// through PollService.Skins, which holds the gating; this file only draws.
/// Same class as SettingsWindow.xaml.cs, split for reading; see CLAUDE.md.
/// </summary>
public partial class SettingsWindow
{
    private const string PatreonPageUrl = "https://islapandora.eu/patreon";
    private const double TileWidth = 138;     // three across the page (436 px) with their 6 px gaps, and a little slack
    private const double PictureWidth = 124;  // the tile minus its padding and border
    private const double PictureHeight = 74;
    private const int MaxPictures = 120;      // skins beyond this say "not loaded"
    private const string NoPicture = "no picture";
    private const string PictureLoading = "loading…";
    private const string PictureUnavailable = "picture unavailable";
    private static readonly TimeSpan ServerSkinCooldown = TimeSpan.FromMinutes(15); // shown as a hint only, never enforced here

    private static readonly Brush TileName = new SolidColorBrush(Color.FromRgb(0xEC, 0xF2, 0xF8));
    private static readonly Brush PictureWell = new SolidColorBrush(Color.FromRgb(0x1A, 0x21, 0x29));
    private static readonly Brush DotRim = new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF));

    /// <summary>The filter and search survive reopening the dialog (session-only, like the page you were on).</summary>
    private static bool _skinsShowAll;
    private static string _skinSearch = "";

    private readonly PollService _poll;
    private readonly Dictionary<string, ImageSource> _skinPictures = new();
    private readonly Dictionary<string, string> _skinPictureProblems = new(); // skin id → why its picture can't be shown
    private readonly Dictionary<string, Image> _tilePictures = new();
    private readonly Dictionary<string, TextBlock> _tileNotes = new();        // the words in an empty picture well
    private IReadOnlyList<PatreonSkin>? _skins;
    private bool _skinsOpened;
    private bool _skinApplying;
    private bool _skinsClosed;
    private Action? _disarmTile; // puts the Apply button back on the tile showing its pattern row

    // ---- Opening the page ---------------------------------------------------

    /// <summary>First look at the page this dialog: one list request (or the session's copy), then the pictures.</summary>
    private async void OpenSkinsPage()
    {
        if (_skinsOpened) return;
        _skinsOpened = true;
        Closed += (_, _) => _skinsClosed = true;

        SkinSearch.Text = _skinSearch;
        (_skinsShowAll ? SkinsAll : SkinsAvailable).IsChecked = true;
        SkinAgainButton.Visibility = Visibility.Hidden;

        if (_firstRun)
        {
            SetSkinStatus("Connect your account on the Account page first — the skins come from your islapandora.eu login.", HintWarn);
            SkinRefreshButton.IsEnabled = false;
            return;
        }
        _poll.Nudge(); // idling may not have seen a fresh spawn yet
        await LoadSkinsAsync(refresh: false);
    }

    private async Task LoadSkinsAsync(bool refresh)
    {
        SetSkinStatus(_skins is null ? "Loading skins…" : "Refreshing…", HintNeutral);
        SkinRefreshButton.IsEnabled = false;
        var result = await _poll.GetSkinsAsync(refresh);
        if (_skinsClosed) return;
        SkinRefreshButton.IsEnabled = true;

        if (result.Skins is null)
        {
            SetSkinStatus($"Couldn't load the skins ({result.Problem}). Press Refresh to try again.", HintBad);
            return;
        }
        var first = _skins is null;
        _skins = result.Skins;
        if (first && !_skinsShowAll && _skins.Count > 0 && _skins.All(s => s.Locked)) SkinsAll.IsChecked = true; // nothing unlocked: show what there is
        RenderSkins();
        if (result.Problem is null) ShowSkinHint(); else SetSkinStatus($"Showing the earlier list — refresh failed ({result.Problem}).", HintWarn);
        UpdateSkinAgain();
        _ = LoadSkinPicturesAsync();
    }

    /// <summary>
    /// Pictures one after another, what you can apply first; each address is
    /// fetched once per session and never with the cookie. A skin's thumbnail
    /// is tried first, then its full image — the site falls back the same
    /// way. A skin whose pictures all fail keeps an empty well that says so,
    /// with the reason on hover.
    /// </summary>
    private async Task LoadSkinPicturesAsync()
    {
        if (_skins is null) return;
        var loaded = 0;
        foreach (var skin in PatreonSkins.Sorted(_skins))
        {
            if (_skinsClosed) return;
            if (_skinPictures.ContainsKey(skin.Id) || _skinPictureProblems.ContainsKey(skin.Id) || skin.Pictures.Count == 0) continue;
            if (++loaded > MaxPictures)
            {
                NotePictureProblem(skin.Id, "not loaded (too many pictures)");
                continue;
            }

            var problems = new List<string>();
            foreach (var address in skin.Pictures)
            {
                var fetched = await _poll.GetSkinPictureAsync(address);
                if (_skinsClosed) return;
                if (fetched.Bytes is null)
                {
                    problems.Add(fetched.Problem ?? "no answer");
                }
                else if (DecodePicture(fetched.Bytes) is { } picture)
                {
                    _skinPictures[skin.Id] = picture;
                    if (_tilePictures.TryGetValue(skin.Id, out var image)) image.Source = picture;
                    if (_tileNotes.TryGetValue(skin.Id, out var note)) note.Visibility = Visibility.Collapsed;
                    break;
                }
                else
                {
                    problems.Add("not a picture format this Windows can show");
                }
            }
            if (!_skinPictures.ContainsKey(skin.Id)) NotePictureProblem(skin.Id, string.Join("; ", problems));
        }
    }

    private void NotePictureProblem(string skinId, string problem)
    {
        _skinPictureProblems[skinId] = problem;
        if (_tileNotes.TryGetValue(skinId, out var note)) note.Text = PictureUnavailable;
    }

    /// <summary>Decoded small and frozen; null for a format this Windows can't read.</summary>
    private static ImageSource? DecodePicture(byte[] bytes)
    {
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = (int)(PictureWidth * 2.5); // sharp at 150% scaling and in the hover preview
            image.StreamSource = new MemoryStream(bytes);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch
        {
            return null;
        }
    }

    // ---- Tiles ----------------------------------------------------------------

    private void RenderSkins()
    {
        if (_skins is null) return;
        _disarmTile = null;
        _tilePictures.Clear();
        _tileNotes.Clear();
        SkinTiles.Children.Clear();

        var available = _skins.Count(s => !s.Locked);
        var locked = _skins.Count - available;
        SkinCount.Text = locked > 0 ? $"{available} available · {locked} locked" : $"{available} available";

        var shown = PatreonSkins.Sorted(_skins.Where(s => (_skinsShowAll || !s.Locked) && PatreonSkins.Matches(s, _skinSearch)));
        foreach (var skin in shown) SkinTiles.Children.Add(BuildSkinTile(skin));
        if (shown.Count > 0) return;

        SkinTiles.Children.Add(new TextBlock
        {
            Text = _skins.Count == 0 ? "No Patreon skins on this account. The website's Patreon page shows how to unlock them."
                 : _skinSearch.Length > 0 ? "No skins match the search."
                 : "Nothing unlocked yet — choose All to see what each tier has.",
            Foreground = HintNeutral, FontSize = 12, TextWrapping = TextWrapping.Wrap, Width = 3 * TileWidth, Margin = new Thickness(2, 8, 0, 0)
        });
    }

    private Border BuildSkinTile(PatreonSkin skin)
    {
        var body = new StackPanel();

        // Picture well: a quiet dark box that SAYS what it is waiting for or
        // missing. (A first build filled it with the seven colours as stripes;
        // that read as a broken image — the dots below already show the colours.)
        var picture = new Image { Stretch = Stretch.UniformToFill };
        var has = _skinPictures.TryGetValue(skin.Id, out var known);
        if (has) picture.Source = known;
        var note = new TextBlock
        {
            Text = skin.Pictures.Count == 0 ? NoPicture : _skinPictureProblems.ContainsKey(skin.Id) ? PictureUnavailable : PictureLoading,
            Foreground = RuleNumber, FontSize = 10, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            Visibility = has ? Visibility.Collapsed : Visibility.Visible
        };
        _tilePictures[skin.Id] = picture;
        _tileNotes[skin.Id] = note;
        var well = new Border
        {
            Width = PictureWidth, Height = PictureHeight, Background = PictureWell, CornerRadius = new CornerRadius(3),
            ClipToBounds = true, Child = new Grid { Children = { note, picture } }, ToolTip = BuildSkinTip(skin, picture)
        };
        body.Children.Add(well);

        body.Children.Add(new TextBlock
        {
            Text = skin.Name, Foreground = TileName, FontSize = 12, FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 5, 0, 0)
        });
        body.Children.Add(new TextBlock
        {
            Text = skin.RequiredRole is null ? (skin.Locked ? "locked" : " ") : skin.Locked ? $"{skin.RequiredRole} · locked" : skin.RequiredRole,
            Foreground = skin.Locked ? HintBad : Highlight, FontSize = 10, TextTrimming = TextTrimming.CharacterEllipsis
        });

        var dots = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 5, 0, 6) };
        foreach (var hex in skin.Colours)
        {
            dots.Children.Add(new Ellipse
            {
                Width = 10, Height = 10, Fill = BrushOf(hex), Stroke = DotRim, StrokeThickness = 1, Margin = new Thickness(0, 0, 4, 0)
            });
        }
        body.Children.Add(dots);
        body.Children.Add(BuildApplyArea(skin));

        return new Border
        {
            Child = body, Width = TileWidth, Margin = new Thickness(0, 0, 6, 6), Padding = new Thickness(6),
            CornerRadius = new CornerRadius(4), BorderBrush = CardBorder, BorderThickness = new Thickness(1),
            Background = CardSurface, Opacity = skin.Locked ? 0.55 : 1
        };
    }

    /// <summary>Apply, which turns into the six pattern buttons — one tile at a time; a locked skin only says so.</summary>
    private Grid BuildApplyArea(PatreonSkin skin)
    {
        var area = new Grid { Height = 24 };
        var apply = new Button
        {
            Content = skin.Locked ? "Locked" : "Apply", IsEnabled = !skin.Locked, FontSize = 11, Padding = new Thickness(0, 3, 0, 3),
            Style = (Style)FindResource("NeutralButtonStyle")
        };
        area.Children.Add(apply);
        if (skin.Locked) return area;

        var patterns = new UniformGrid { Rows = 1, Visibility = Visibility.Collapsed };
        for (var i = 0; i < PatreonSkins.PatternCount; i++)
        {
            var index = i;
            var button = new Button
            {
                Content = PatreonSkins.PatternLetter(i).ToString(), FontSize = 11, Padding = new Thickness(0, 3, 0, 3),
                Margin = new Thickness(i == 0 ? 0 : 2, 0, 0, 0), Style = (Style)FindResource("SaveButtonStyle"),
                ToolTip = $"Apply {skin.Name} with pattern {PatreonSkins.PatternLetter(i)}"
            };
            button.Click += async (_, _) => await ApplySkinAsync(skin.Id, skin.IdIsNumber, skin.Name, index);
            patterns.Children.Add(button);
        }
        area.Children.Add(patterns);

        apply.Click += (_, _) =>
        {
            _disarmTile?.Invoke();
            apply.Visibility = Visibility.Collapsed;
            patterns.Visibility = Visibility.Visible;
            _disarmTile = () =>
            {
                patterns.Visibility = Visibility.Collapsed;
                apply.Visibility = Visibility.Visible;
            };
            SetSkinStatus($"{skin.Name}: choose its pattern, A to F. That applies it at once.", HintNeutral);
        };
        return area;
    }

    /// <summary>Hover preview in the dialog's own colours: the picture larger, the name, tier and description.</summary>
    private ToolTip BuildSkinTip(PatreonSkin skin, Image tilePicture)
    {
        var panel = new StackPanel { MaxWidth = 300 };
        var large = new Image { Width = 300, Stretch = Stretch.Uniform, Margin = new Thickness(0, 0, 0, 6) };
        panel.Children.Add(large);
        panel.Children.Add(new TextBlock { Text = skin.Name, Foreground = TileName, FontSize = 12, FontWeight = FontWeights.SemiBold });
        if (skin.RequiredRole is not null)
        {
            panel.Children.Add(new TextBlock { Text = skin.Locked ? $"{skin.RequiredRole} · locked" : skin.RequiredRole, Foreground = HintNeutral, FontSize = 11 });
        }
        if (skin.Description is not null)
        {
            panel.Children.Add(new TextBlock { Text = skin.Description, Foreground = RuleText, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) });
        }
        var why = new TextBlock { Foreground = HintNeutral, FontSize = 10, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
        panel.Children.Add(why);
        var tip = new ToolTip { Content = panel, Background = CardSurface, BorderBrush = CardBorder, Padding = new Thickness(8), HasDropShadow = false };
        tip.Opened += (_, _) =>
        {
            large.Source = tilePicture.Source; // whatever has arrived by now
            large.Visibility = large.Source is null ? Visibility.Collapsed : Visibility.Visible;
            // Why the well is empty, when it is: nothing to show, or what went wrong fetching it.
            why.Text = large.Source is not null ? ""
                     : skin.Pictures.Count == 0 ? "The website has no picture for this skin."
                     : _skinPictureProblems.TryGetValue(skin.Id, out var problem) ? $"Picture unavailable — {problem}"
                     : "The picture is still loading.";
            why.Visibility = why.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        };
        return tip;
    }

    private static Brush BrushOf(string hex)
    {
        try
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            brush.Freeze();
            return brush;
        }
        catch
        {
            return Brushes.Gray;
        }
    }

    // ---- Applying -------------------------------------------------------------

    private async Task ApplySkinAsync(string skinId, bool idIsNumber, string name, int pattern)
    {
        if (_skinApplying) return;
        _skinApplying = true;
        var letter = PatreonSkins.PatternLetter(pattern);
        SetSkinStatus($"Applying {name} (pattern {letter})…", HintNeutral);
        try
        {
            var result = await _poll.ApplySkinAsync(skinId, idIsNumber, name, pattern);
            if (_skinsClosed) return;
            switch (result.Outcome)
            {
                case SkinApplyOutcome.Ok:
                    SetSkinStatus($"Applied {name} (pattern {letter}). It can take 5–10 seconds to show in game.", HintGood);
                    _disarmTile?.Invoke();
                    _disarmTile = null;
                    break;
                case SkinApplyOutcome.NotInGame:
                    SetSkinStatus("You need to be spawned in to apply a skin. Nothing was sent.", HintWarn);
                    break;
                case SkinApplyOutcome.TooSoon:
                    SetSkinStatus("Give it a few seconds between applies. Nothing was sent.", HintWarn);
                    break;
                case SkinApplyOutcome.Refused:
                    SetSkinStatus(result.Message is null ? "The server declined, without saying why." : $"The server said: {result.Message}", HintWarn);
                    break;
                default:
                    SetSkinStatus($"Couldn't apply the skin ({result.Message}).", HintBad);
                    break;
            }
            UpdateSkinAgain();
        }
        finally
        {
            _skinApplying = false;
        }
    }

    /// <summary>The resting line: where you stand with the server's cooldown, or how the page works.</summary>
    private void ShowSkinHint()
    {
        if (!_poll.InGame)
        {
            SetSkinStatus("You aren't spawned in. Browse away; applying needs a dino in game.", HintNeutral);
        }
        else if (_config.SkinLastAppliedUtc is { } last && DateTime.UtcNow - last is var ago && ago >= TimeSpan.Zero && ago < ServerSkinCooldown)
        {
            SetSkinStatus($"Last applied {Math.Max(1, (int)ago.TotalMinutes)} min ago. The server allows a new skin about every {ServerSkinCooldown.TotalMinutes:0} minutes.", HintNeutral);
        }
        else
        {
            SetSkinStatus("Apply, then pick the pattern (A–F). It acts at once and shows in game after a few seconds.", HintNeutral);
        }
    }

    /// <summary>"Apply again: Ember · C" for the species you are on, when you applied one before.</summary>
    private void UpdateSkinAgain()
    {
        var choice = SkinChoiceForCurrentDino();
        SkinAgainButton.Visibility = choice is null ? Visibility.Hidden : Visibility.Visible;
        if (choice is null) return;
        SkinAgainButton.Content = $"Apply again: {choice.Name} · {PatreonSkins.PatternLetter(choice.Pattern)}";
        SkinAgainButton.ToolTip = $"The skin you last applied as a {_poll.CurrentDino}";
    }

    private SkinChoice? SkinChoiceForCurrentDino() =>
        _poll.InGame && _poll.CurrentDino is { Length: > 0 } dino && _config.SkinChoices.TryGetValue(dino, out var choice) ? choice : null;

    private async void SkinAgain_Click(object sender, RoutedEventArgs e)
    {
        if (SkinChoiceForCurrentDino() is not { } choice) return;
        if (_skins?.FirstOrDefault(s => s.Id == choice.SkinId) is { Locked: true })
        {
            SetSkinStatus($"{choice.Name} is locked for your current tier.", HintWarn);
            return;
        }
        await ApplySkinAsync(choice.SkinId, choice.IdIsNumber, choice.Name, choice.Pattern);
    }

    // ---- Filter, search, buttons ------------------------------------------------

    private void SkinFilter_Changed(object sender, RoutedEventArgs e)
    {
        if (!_skinsOpened) return; // raised while the dialog is still being built
        _skinsShowAll = SkinsAll.IsChecked == true;
        RenderSkins();
    }

    private void SkinSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_skinsOpened) return;
        _skinSearch = SkinSearch.Text.Trim();
        RenderSkins();
    }

    private async void SkinRefresh_Click(object sender, RoutedEventArgs e) => await LoadSkinsAsync(refresh: true);

    private void OpenPatreonPage_Click(object sender, RoutedEventArgs e) => OpenUrl(PatreonPageUrl);

    private void SetSkinStatus(string text, Brush brush)
    {
        SkinStatus.Text = text;
        SkinStatus.Foreground = brush;
    }
}
