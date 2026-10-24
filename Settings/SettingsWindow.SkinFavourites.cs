using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace PandoraOverlay;

/// <summary>
/// SettingsWindow, Skins favourites part (1.38): one favourite skin and
/// pattern per species, chosen with the ☆ in a tile's picture corner while
/// you play that species, and applied by the skin hotkey or the page's
/// "Apply your favourite". The idea of the player who was attacked while
/// applying a skin (Oct 24 2026), the owner's design calls: per species, and
/// it replaced 1.28–1.37's "Apply again" (the skin last applied). Choosing
/// or clearing a favourite sends nothing: it is local, like a friend's
/// nickname, and applying it is the same request with the same guards as a
/// tile's letters. The ☆ opens the tile's A–F row in gold, the favourite's
/// pattern, without applying it: the server allows a skin only every few
/// minutes, and a favourite must not cost one. Same class as
/// SettingsWindow.xaml.cs, split for reading; see CLAUDE.md.
/// </summary>
public partial class SettingsWindow
{
    private readonly Dictionary<string, Button> _tileStars = new(); // skin id → its ☆, so a new favourite re-marks the tiles in place

    /// <summary>The ☆ (★ and the letter on the favourite) in the picture's top-right corner, and its gold A–F row in the tile's Apply area.</summary>
    private void AddFavouriteStar(PatreonSkin skin, Grid wellGrid, Grid area)
    {
        var star = new Button
        {
            FontSize = 11, Height = 20, MinWidth = 22, Padding = new Thickness(4, 0, 4, 0),
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(3),
            Style = (Style)FindResource("NeutralButtonStyle")
        };
        _tileStars[skin.Id] = star;
        MarkStar(skin, star);
        wellGrid.Children.Add(star);

        var apply = (Button)area.Children[0]; // BuildApplyArea's first child
        UniformGrid? letters = null; // built on the first ☆, like the apply letters
        star.Click += (_, _) =>
        {
            if (_poll.MyFavourite?.SkinId == skin.Id)
            {
                ClearFavourite();
                return;
            }
            if (!_poll.InGame || _poll.CurrentDino is not { Length: > 0 } dino)
            {
                SetSkinStatus("Spawn in as the dino it is for: a favourite is kept per species. Nothing was sent.", HintWarn);
                return;
            }
            _disarmTile?.Invoke();
            if (letters is null)
            {
                letters = BuildFavouriteLetters(skin);
                area.Children.Add(letters);
            }
            var row = letters;
            apply.Visibility = Visibility.Collapsed;
            row.Visibility = Visibility.Visible;
            _disarmTile = () =>
            {
                row.Visibility = Visibility.Collapsed;
                apply.Visibility = Visibility.Visible;
            };
            SetSkinStatus($"{skin.Name}: choose the pattern for your {dino} favourite, A to F. Nothing is sent.", HintNeutral);
        };
    }

    /// <summary>The A–F row in gold: a letter makes the skin and that pattern the favourite for the dino you are on.</summary>
    private UniformGrid BuildFavouriteLetters(PatreonSkin skin)
    {
        var row = new UniformGrid { Rows = 1 };
        for (var i = 0; i < PatreonSkins.PatternCount; i++)
        {
            var pattern = i;
            var letter = PatreonSkins.PatternLetter(i);
            var button = new Button
            {
                Content = letter.ToString(), FontSize = 11, Padding = new Thickness(0, 3, 0, 3),
                Margin = new Thickness(i == 0 ? 0 : 2, 0, 0, 0), Style = (Style)FindResource("NeutralButtonStyle"),
                Foreground = Highlight, ToolTip = $"Make {skin.Name} with pattern {letter} your favourite. Nothing is sent."
            };
            button.Click += (_, _) => SetFavourite(skin, pattern);
            row.Children.Add(button);
        }
        return row;
    }

    private void SetFavourite(PatreonSkin skin, int pattern)
    {
        if (!_poll.InGame || _poll.CurrentDino is not { Length: > 0 } dino) return; // left the game while the row was open
        _config.SkinFavourites[dino] = new SkinChoice(skin.Id, skin.IdIsNumber, skin.Name, pattern);
        _disarmTile?.Invoke();
        _disarmTile = null;
        RefreshStars();
        UpdateFavouriteButton();
        SetSkinStatus($"{skin.Name} · {PatreonSkins.PatternLetter(pattern)} is your {dino} favourite. {SkinHotkeyText()} applies it in game, or the button above.", HintGood);
    }

    private void ClearFavourite()
    {
        if (_poll.CurrentDino is not { Length: > 0 } dino) return;
        _config.SkinFavourites.Remove(dino);
        RefreshStars();
        UpdateFavouriteButton();
        SetSkinStatus($"Your {dino} favourite is cleared. Nothing was sent.", HintNeutral);
    }

    private void RefreshStars()
    {
        foreach (var (id, star) in _tileStars)
        {
            if (_skins?.FirstOrDefault(s => s.Id == id) is { } skin) MarkStar(skin, star);
        }
    }

    /// <summary>★ and its letter on the favourite of the dino you are on, a plain ☆ elsewhere.</summary>
    private void MarkStar(PatreonSkin skin, Button star)
    {
        var favourite = _poll.MyFavourite;
        if (favourite?.SkinId == skin.Id)
        {
            star.Content = $"★ {PatreonSkins.PatternLetter(favourite.Pattern)}";
            star.Foreground = Highlight;
            star.ToolTip = $"Your {_poll.CurrentDino} favourite, pattern {PatreonSkins.PatternLetter(favourite.Pattern)}. Click to clear it.";
        }
        else
        {
            star.Content = "☆";
            star.ClearValue(ForegroundProperty);
            star.ToolTip = $"Make this your favourite for the dino you are on: {SkinHotkeyText()} (or the button at the top) applies it";
        }
    }

    /// <summary>"Apply your favourite: Ember · C" for the species you are on, when you have one.</summary>
    private void UpdateFavouriteButton()
    {
        var favourite = _poll.MyFavourite;
        SkinFavouriteButton.Visibility = favourite is null ? Visibility.Hidden : Visibility.Visible;
        if (favourite is null) return;
        SkinFavouriteButton.Content = $"Apply your favourite: {favourite.Name} · {PatreonSkins.PatternLetter(favourite.Pattern)}";
        SkinFavouriteButton.ToolTip = $"Your favourite as a {_poll.CurrentDino}. {SkinHotkeyText()} applies it from the game too.";
    }

    private async void SkinFavourite_Click(object sender, RoutedEventArgs e)
    {
        if (_poll.MyFavourite is not { } favourite) return;
        if (_skins?.FirstOrDefault(s => s.Id == favourite.SkinId) is { Locked: true })
        {
            SetSkinStatus($"{favourite.Name} is locked for your current tier.", HintWarn);
            return;
        }
        await ApplySkinAsync(favourite.SkinId, favourite.IdIsNumber, favourite.Name, favourite.Pattern);
    }

    /// <summary>The skin hotkey as the Hotkeys page has it now (it works once the dialog is closed).</summary>
    private string SkinHotkeyText() => _hotkeyEntries[SkinHotkeyBox].Chosen.ToString();
}
