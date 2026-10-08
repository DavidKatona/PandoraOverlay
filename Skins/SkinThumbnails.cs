using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Media.Imaging;

namespace PandoraOverlay;

/// <summary>
/// Small copies of the skin pictures, kept on disk so each picture is
/// downloaded ONCE instead of once per session. The site serves them at full
/// size — close to 5 MB apiece, some ninety of them — while a tile shows 124
/// px: the picture is decoded straight down to a 320 px thumbnail, saved as
/// a JPEG of a few dozen KB under cache/skins next to the app, and the
/// download is dropped. A copy is trusted for 30 days, then fetched afresh
/// — or sooner, when the page's "Reload pictures" clears the folder.
/// Derived data only: deleting the folder costs nothing but the downloads.
/// Fail-soft like everything on disk here — nothing in this class throws.
/// </summary>
public static class SkinThumbnails
{
    /// <summary>Wide enough for the tile at 150% scaling and for the 300 px hover preview.</summary>
    public const int Width = 320;

    public static readonly TimeSpan KeepFor = TimeSpan.FromDays(30);

    public static string DefaultFolder { get; } = Path.Combine(DataFolder.Path, DataFolder.Cache, "skins");

    /// <summary>The cache file for an address: a hash, so no part of a URL ever becomes a path.</summary>
    public static string FileNameFor(Uri address) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(address.AbsoluteUri)))[..32].ToLowerInvariant() + ".jpg";

    /// <summary>The downloaded picture as a frozen thumbnail <paramref name="width"/> px wide (the skin tiles' size by default — the Account card's avatar asks for less); null when it isn't something this Windows can decode.</summary>
    public static BitmapSource? Make(byte[] original, int width = Width)
    {
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = width;
            image.StreamSource = new MemoryStream(original);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>The saved thumbnail for an address, if there is one and it is still fresh.</summary>
    public static BitmapSource? TryLoad(Uri address, DateTime nowUtc, string? folder = null)
    {
        try
        {
            var path = Path.Combine(folder ?? DefaultFolder, FileNameFor(address));
            if (!File.Exists(path) || nowUtc - File.GetLastWriteTimeUtc(path) >= KeepFor) return null;

            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad; // read fully, so the file is not held open
            image.StreamSource = new MemoryStream(File.ReadAllBytes(path));
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch
        {
            return null; // unreadable or damaged: as good as absent, the next fetch rewrites it
        }
    }

    /// <summary>Saves a thumbnail for an address; false when the folder can't be written (the picture is then simply fetched again next time).</summary>
    public static bool TrySave(Uri address, BitmapSource thumbnail, string? folder = null)
    {
        try
        {
            var directory = folder ?? DefaultFolder;
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, FileNameFor(address));
            var encoder = new JpegBitmapEncoder { QualityLevel = 88 };
            encoder.Frames.Add(BitmapFrame.Create(thumbnail));
            var temp = path + ".tmp";
            using (var file = File.Create(temp)) encoder.Save(file);
            File.Move(temp, path, overwrite: true); // never a half-written thumbnail under the real name
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Deletes every saved thumbnail — the page's "Reload pictures" — and
    /// says how many went. Only this folder's own files (the .jpg copies and
    /// any .tmp a crash left behind), nothing below it; a file that won't go
    /// is skipped.
    /// </summary>
    public static int Clear(string? folder = null)
    {
        var removed = 0;
        try
        {
            var directory = folder ?? DefaultFolder;
            if (!Directory.Exists(directory)) return 0;
            foreach (var path in Directory.GetFiles(directory))
            {
                var thumbnail = path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase);
                if (!thumbnail && !path.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)) continue;
                try
                {
                    File.Delete(path);
                    if (thumbnail) removed++;
                }
                catch
                {
                    // held by something else: it stays, and counts as saved
                }
            }
        }
        catch
        {
            // the folder can't be listed: nothing deleted
        }
        return removed;
    }
}
