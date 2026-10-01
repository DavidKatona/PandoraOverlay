using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Xunit;

namespace PandoraOverlay.Tests;

public sealed class SkinThumbnailsTests : IDisposable
{
    private static readonly Uri Address = new("https://islapandora.eu/skins/clay.png");
    private static readonly DateTime Now = new(2026, 10, 1, 20, 0, 0, DateTimeKind.Utc);

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "pandora-thumb-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch { /* nothing was written */ }
    }

    /// <summary>A PNG of the given size, standing in for a downloaded picture.</summary>
    private static byte[] Png(int width, int height)
    {
        var pixels = new byte[width * height * 4];
        for (var i = 0; i < pixels.Length; i += 4) { pixels[i] = 40; pixels[i + 1] = 120; pixels[i + 2] = 200; pixels[i + 3] = 255; }
        var source = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    [Fact]
    public void TheFileNameIsAHashNotAPieceOfTheAddress()
    {
        var name = SkinThumbnails.FileNameFor(new Uri("https://islapandora.eu/skins/../../etc/clay.png?x=1"));

        Assert.Matches("^[0-9a-f]{32}\\.jpg$", name);
        Assert.Equal(name, SkinThumbnails.FileNameFor(new Uri("https://islapandora.eu/skins/../../etc/clay.png?x=1")));
        Assert.NotEqual(name, SkinThumbnails.FileNameFor(Address));
    }

    [Fact]
    public void ALargePictureBecomesASmallThumbnail()
    {
        var thumbnail = SkinThumbnails.Make(Png(1920, 1080))!;

        Assert.Equal(SkinThumbnails.Width, thumbnail.PixelWidth);
        Assert.Equal(180, thumbnail.PixelHeight); // the shape is kept
        Assert.True(thumbnail.IsFrozen);
    }

    [Fact]
    public void SomethingThatIsNoPictureMakesNoThumbnail()
    {
        Assert.Null(SkinThumbnails.Make(new byte[] { 1, 2, 3, 4, 5 }));
        Assert.Null(SkinThumbnails.Make(Array.Empty<byte>()));
    }

    [Fact]
    public void ASavedThumbnailComesBackWithoutADownload()
    {
        Assert.Null(SkinThumbnails.TryLoad(Address, Now, _folder)); // nothing saved yet

        Assert.True(SkinThumbnails.TrySave(Address, SkinThumbnails.Make(Png(1280, 720))!, _folder));
        var back = SkinThumbnails.TryLoad(Address, DateTime.UtcNow, _folder)!;

        Assert.Equal(SkinThumbnails.Width, back.PixelWidth);
        Assert.Equal(180, back.PixelHeight);
        var file = new FileInfo(Path.Combine(_folder, SkinThumbnails.FileNameFor(Address)));
        Assert.InRange(file.Length, 1, 100_000); // a few KB, not the megabytes that were downloaded
        Assert.Empty(Directory.GetFiles(_folder, "*.tmp"));
    }

    [Fact]
    public void AnotherAddressHasItsOwnThumbnail()
    {
        SkinThumbnails.TrySave(Address, SkinThumbnails.Make(Png(640, 360))!, _folder);
        Assert.Null(SkinThumbnails.TryLoad(new Uri("https://islapandora.eu/skins/ember.png"), DateTime.UtcNow, _folder));
    }

    [Fact]
    public void AnOldThumbnailIsFetchedAfresh()
    {
        SkinThumbnails.TrySave(Address, SkinThumbnails.Make(Png(640, 360))!, _folder);
        var written = File.GetLastWriteTimeUtc(Path.Combine(_folder, SkinThumbnails.FileNameFor(Address)));

        Assert.NotNull(SkinThumbnails.TryLoad(Address, written + SkinThumbnails.KeepFor - TimeSpan.FromMinutes(1), _folder));
        Assert.Null(SkinThumbnails.TryLoad(Address, written + SkinThumbnails.KeepFor + TimeSpan.FromMinutes(1), _folder));
    }

    [Fact]
    public void ADamagedFileIsAsGoodAsAbsent()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllBytes(Path.Combine(_folder, SkinThumbnails.FileNameFor(Address)), new byte[] { 9, 9, 9 });

        Assert.Null(SkinThumbnails.TryLoad(Address, DateTime.UtcNow, _folder));
    }

    [Fact]
    public void AFolderThatCannotBeWrittenFailsSoftly()
    {
        var thumbnail = SkinThumbnails.Make(Png(640, 360))!;
        Assert.False(SkinThumbnails.TrySave(Address, thumbnail, "Z:\\no\\such\\drive\\<bad>|path"));
    }
}
