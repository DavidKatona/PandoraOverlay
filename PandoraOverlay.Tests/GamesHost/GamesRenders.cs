using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Xunit;
using Xunit.Abstractions;

namespace PandoraOverlay.Tests;

/// <summary>
/// The render tool for a reviewer's eyes (A27): with the environment variable
/// <c>PANDORA_GAMES_RENDERS</c> naming a folder, a PNG of every view of the
/// Games window (<see cref="GamesLayoutTests.EveryView"/>) in every theme, at
/// 96 DPI, as <c>&lt;theme id&gt;-&lt;where&gt;.png</c>. Compared with nothing:
/// the drawing is guarded by the layout tests in every theme, and the owner's
/// golden-render harness, outside the repo, is not touched. Without the
/// variable it returns at once, so CI and every ordinary run skip it.
/// </summary>
[Collection(TheThemeOnScreen.Name)]
public class GamesRenders
{
    public const string Variable = "PANDORA_GAMES_RENDERS";

    private readonly ITestOutputHelper _out;

    public GamesRenders(ITestOutputHelper output) => _out = output;

    [Fact]
    public void RenderEveryViewInEveryTheme()
    {
        var folder = System.Environment.GetEnvironmentVariable(Variable);
        if (string.IsNullOrWhiteSpace(folder)) return;
        Directory.CreateDirectory(folder);
        var written = 0;
        foreach (var theme in Theme.All)
        {
            GamesLayoutTests.OnScreen(theme.Id, () =>
            {
                foreach (var (where, root) in GamesLayoutTests.EveryView())
                {
                    var bitmap = new RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth), (int)Math.Ceiling(root.ActualHeight), 96, 96, PixelFormats.Pbgra32);
                    bitmap.Render(root);
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using var file = File.Create(Path.Combine(folder, $"{theme.Id}-{FileName(where)}.png"));
                    encoder.Save(file);
                    written++;
                }
            });
        }
        _out.WriteLine($"{written} renders in {folder}");
    }

    /// <summary>A view's description as a file name: letters, digits and dashes.</summary>
    internal static string FileName(string where) =>
        System.Text.RegularExpressions.Regex.Replace(where.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
}
