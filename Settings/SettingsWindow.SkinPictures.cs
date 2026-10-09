using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace PandoraOverlay;

/// <summary>
/// SettingsWindow, Skins page, the PICTURES: loaded only for tiles on
/// screen, from the thumbnail saved on disk when there is one, else by one
/// cookie-less download that is shrunk, saved and dropped (SkinThumbnails);
/// and "Reload pictures", which discards the saved copies. The tiles
/// themselves are SettingsWindow.Skins.cs. Same class as
/// SettingsWindow.xaml.cs, split for reading; see CLAUDE.md.
/// </summary>
public partial class SettingsWindow
{
    private readonly Dictionary<string, ImageSource> _skinPictures = new();
    private readonly Dictionary<string, string> _skinPictureProblems = new(); // skin id → why its picture can't be shown
    private readonly HashSet<string> _stalePictures = new();                  // shown from before "Reload pictures", until the fresh one arrives
    private readonly Dictionary<string, Image> _tilePictures = new();
    private readonly Dictionary<string, TextBlock> _tileNotes = new();        // the words in an empty picture well
    private bool _picturesLoading;

    /// <summary>Starts the loader once the tiles have been laid out — before that nothing counts as on screen.</summary>
    private void QueueVisibleSkinPictures() => Dispatcher.BeginInvoke(DispatcherPriority.Loaded, LoadVisibleSkinPictures);

    /// <summary>
    /// Loads the pictures of the tiles that are ON SCREEN, one after another,
    /// and stops when none is left — scrolling (or coming back to the page)
    /// starts it again. The site does the same with loading="lazy", and for
    /// the same reason: the pictures are served at full size, ~5 MB each and
    /// some ninety of them, so fetching what nobody looks at would cost
    /// hundreds of MB. One loop at a time; never for a page that is hidden.
    /// No margin of tiles just out of view is preloaded, on purpose: only
    /// what is actually on screen costs a download.
    /// </summary>
    private async void LoadVisibleSkinPictures()
    {
        if (_picturesLoading || _skins is null) return;
        _picturesLoading = true;
        try
        {
            while (!_skinsClosed && NextVisibleSkinWithoutPicture() is { } skin) await LoadSkinPictureAsync(skin);
        }
        finally
        {
            _picturesLoading = false;
        }
    }

    private PatreonSkin? NextVisibleSkinWithoutPicture()
    {
        if (SkinList.ViewportHeight <= 0) return null;
        foreach (var tile in SkinTiles.Children.OfType<Border>())
        {
            if (tile.Tag is not PatreonSkin skin || skin.Pictures.Count == 0) continue;
            if (_skinPictureProblems.ContainsKey(skin.Id)) continue;
            if (_skinPictures.ContainsKey(skin.Id) && !_stalePictures.Contains(skin.Id)) continue;
            if (!tile.IsVisible) return null; // the page itself is hidden
            var top = tile.TranslatePoint(new Point(0, 0), SkinList).Y;
            if (top + tile.ActualHeight > 0 && top < SkinList.ViewportHeight) return skin;
        }
        return null;
    }

    /// <summary>
    /// One skin's picture: the thumbnail saved on disk if there is one (no
    /// request at all), else a download — never with the cookie — that is
    /// shrunk to a thumbnail, saved for next time and dropped. The skin's
    /// thumbnail address is tried first, then its full image, like the
    /// site's own fallback. When everything fails the well says so, with the
    /// reasons on hover (a picture from before a reload simply stays).
    /// </summary>
    private async Task LoadSkinPictureAsync(PatreonSkin skin)
    {
        var addresses = skin.Pictures.Select(PatreonSkins.ResolvePicture).OfType<Uri>().ToList();
        foreach (var address in addresses)
        {
            if (SkinThumbnails.TryLoad(address, DateTime.UtcNow) is not { } saved) continue;
            ShowSkinPicture(skin.Id, saved);
            return;
        }

        var problems = new List<string>();
        if (addresses.Count == 0) problems.Add("not a usable address");
        foreach (var address in addresses)
        {
            var fetched = await _poll.GetSkinPictureAsync(address);
            if (_skinsClosed) return;
            if (fetched.Bytes is null)
            {
                problems.Add(fetched.Problem ?? "no answer");
            }
            else if (SkinThumbnails.Make(fetched.Bytes) is { } thumbnail)
            {
                SkinThumbnails.TrySave(address, thumbnail);
                ShowSkinPicture(skin.Id, thumbnail);
                return;
            }
            else
            {
                problems.Add($"{address.Host}: not a picture format this Windows can show");
            }
        }
        _skinPictureProblems[skin.Id] = string.Join("; ", problems);
        if (_tileNotes.TryGetValue(skin.Id, out var note)) note.Text = PictureUnavailable;
    }

    private void ShowSkinPicture(string skinId, ImageSource picture)
    {
        _skinPictures[skinId] = picture;
        _stalePictures.Remove(skinId);
        if (_tilePictures.TryGetValue(skinId, out var image)) image.Source = picture;
        if (_tileNotes.TryGetValue(skinId, out var note)) note.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// Reload pictures: discards every saved thumbnail, so each picture is
    /// downloaded again — still only as its tile is on screen, and the one
    /// already shown stays until its replacement arrives, so nothing flashes
    /// back to "loading…". Its own button and NOT part of Refresh (owner's
    /// call, Oct 2 2026): Refresh asks about the LIST and costs one small
    /// request; this costs ~5 MB per tile looked at and only matters when
    /// the site replaced a picture under the same address (a new skin has a
    /// new address and loads by itself). The list is changed in place, not
    /// re-rendered, so the view stays where it was.
    /// </summary>
    private void SkinReloadPictures_Click(object sender, RoutedEventArgs e)
    {
        if (_skins is null) return;
        if (!_poll.TryBeginSkinPictureReload())
        {
            SetSkinStatus("The pictures were reloaded a moment ago. Give it half a minute.", HintWarn);
            return;
        }

        var discarded = SkinThumbnails.Clear();
        _skinPictureProblems.Clear();
        _stalePictures.UnionWith(_skinPictures.Keys);
        foreach (var note in _tileNotes.Values)
        {
            if (note.Text == PictureUnavailable) note.Text = PictureLoading; // failed before: it gets another try
        }
        // Short enough for the status row's two lines beside "Apply again".
        SetSkinStatus(discarded == 0
            ? "Each picture downloads again when its tile is in view."
            : $"{discarded} saved {(discarded == 1 ? "picture" : "pictures")} discarded. Each downloads again when its tile is in view.", HintNeutral);
        LoadVisibleSkinPictures();
    }
}
