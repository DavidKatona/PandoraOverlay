using System.Globalization;
using System.Windows.Media;
using Xunit;
using Xunit.Abstractions;

namespace PandoraOverlay.Tests;

/// <summary>
/// The fit factor (FIT.md, the owner's decisions of Oct 29 and Oct 30 2026):
/// for every theme, the largest factor at which its font fits the frames
/// that never move, computed here, from full size down by 0.01 to the floor,
/// and held to the one its file records ("Fit", outside Tokens; recorded ≠
/// computed fails, so nobody types it). At each candidate the theme's tokens
/// are put on screen at that factor and measured two ways: the frame
/// contract's strings (FrameContract: the widgets' and the control panel's
/// tight places), then the walk (FitProbe) over the windows the tests can
/// build, the Settings dialog on every page, the update cards and the sign-in
/// window, laid out off-screen with fresh objects and the counting fake
/// behind the dialog's website client, never shown (a test must never become
/// a request). Below the floor the font is refused, with a message that
/// names the theme, the font, the factor and what did not fit.
/// <para>
/// Tests test the framework, never a theme (the owner, Oct 27 2026): every
/// theme in Theme.All is held to the same rule, and the two samples that
/// prove the computation are themes of their own, built from Default's
/// tokens with a font Windows brings. The factors are printed (the session
/// that builds a theme reports them).
/// </para>
/// </summary>
[Collection(TheThemeOnScreen.Name)]
public class FitFactorTests
{
    private readonly ITestOutputHelper _out;

    public FitFactorTests(ITestOutputHelper output) => _out = output;

    public static IEnumerable<object[]> Themes() => Theme.All.Select(e => new object[] { e.Id });

    /// <summary>What the computation found: the factor (null when refused), and the first thing that did not fit one step above it (at the floor, when refused).</summary>
    internal sealed record Fit(double? Factor, string? FailedAbove, int Requests);

    // ---- Every theme ----------------------------------------------------------------

    /// <summary>
    /// Every theme's font fits the frames at a factor no lower than the floor,
    /// and that factor is the one its file records (Default's, 1.00, is in
    /// code). A theme built later gets its "Fit" from this test's message.
    /// </summary>
    [Theory]
    [MemberData(nameof(Themes))]
    public void EveryThemesFactorIsTheOneItsFontFits(string id)
    {
        var entry = Theme.All.Single(e => e.Id == id);
        var fit = Compute(entry.Tokens);
        _out.WriteLine(Describe(id, entry.Tokens, fit));

        Assert.Equal(0, fit.Requests);
        Assert.True(fit.Factor is not null, Refusal(id, entry.Tokens, fit));
        Assert.True(Math.Abs(fit.Factor!.Value - entry.Fit) < 0.001,
            Invariant($"{id}: the file records \"Fit\": {entry.Fit:0.00}, its font fits at {fit.Factor:0.00}; write \"Fit\": {fit.Factor:0.00} into Assets/Themes/{id}.json") +
            (fit.FailedAbove is null ? "" : $" (at the step above: {fit.FailedAbove})"));
    }

    /// <summary>Default is the layout's own font: full size, in code.</summary>
    [Fact]
    public void DefaultsFactorIsFull()
    {
        Assert.Equal(Theme.DefaultId, Theme.All[0].Id);
        Assert.Equal(FitFactor.Full, Theme.All[0].Fit);
        Assert.Equal(FitFactor.Full, DefaultTheme.Tokens.Fit);
    }

    /// <summary>The candidates: full size down to the floor in steps of 0.01, each exactly its two decimals (the owner's answers 2 and 3).</summary>
    [Fact]
    public void TheCandidatesRunFromFullToTheFloorByHundredths()
    {
        var candidates = FitFactor.Candidates().ToList();
        Assert.Equal(16, candidates.Count);
        Assert.Equal(1.00, candidates[0]);
        Assert.Equal(0.85, candidates[^1]);
        Assert.All(candidates, c => Assert.Equal(Math.Round(c, 2), c));
        Assert.Equal(candidates.OrderByDescending(c => c), candidates);
    }

    // ---- The samples ------------------------------------------------------------------

    /// <summary>
    /// A deliberately wide body font lands below full size and above the
    /// floor, and says what limited it: Courier New, a monospace about a tenth
    /// wider than Segoe UI (measured Oct 30 2026: 0.94, Prime's longest row
    /// one step above). Only the bounds are held, not the number: the rule,
    /// not the font, is under test.
    /// </summary>
    [Fact]
    public void AWideFontIsDrawnSmaller()
    {
        var tokens = Sample(Installed("Courier New"));
        var fit = Compute(tokens);
        _out.WriteLine(Describe("sample", tokens, fit));

        Assert.Equal(0, fit.Requests);
        Assert.NotNull(fit.Factor);
        Assert.InRange(fit.Factor!.Value, FitFactor.Floor, FitFactor.Full - FitFactor.Step);
        Assert.NotNull(fit.FailedAbove); // what failed one step up
        Assert.StartsWith(Invariant($"{fit.Factor + FitFactor.Step:0.00}: "), fit.FailedAbove);
    }

    /// <summary>
    /// A font that can't fit even at the floor is refused, and the message
    /// names the theme, the font, the factor and what did not fit: Gabriola,
    /// whose line box is so tall that the control panel's 9 px captions
    /// overflow their 12 px row at 0.85 (measured Oct 30 2026; Segoe Print
    /// does the same, the stand-in where Gabriola is not installed).
    /// </summary>
    [Fact]
    public void AFontThatCannotFitIsRefused()
    {
        var font = Installed("Gabriola", "Segoe Print");
        var tokens = Sample(font);
        var fit = Compute(tokens);
        var message = Refusal("sample", tokens, fit);
        _out.WriteLine(message);

        Assert.Equal(0, fit.Requests);
        Assert.Null(fit.Factor);
        Assert.Contains("sample", message);
        Assert.Contains(font, message);
        Assert.Contains("0.85", message);
        Assert.NotNull(fit.FailedAbove);
        Assert.Contains(fit.FailedAbove!, message);
    }

    /// <summary>The first of these Windows has (a sample needs a real font: a missing one falls back to Segoe UI and fits).</summary>
    private static string Installed(params string[] families)
    {
        var installed = Fonts.SystemFontFamilies.Select(f => f.Source).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return families.FirstOrDefault(installed.Contains)
               ?? throw new InvalidOperationException($"none of {string.Join(", ", families)} is installed: the sample needs a font Windows brings");
    }

    // ---- The computation -----------------------------------------------------------

    /// <summary>A sample theme: Default's tokens with another body font, through the loader as a file would be.</summary>
    internal static ThemeTokens Sample(string family) =>
        ThemeFile.Parse("sample", $$"""{ "Name": "Sample", "Order": 9, "Tokens": { "Font": { "Family": "{{family}}" } } }""", DefaultTheme.Tokens).Tokens;

    /// <summary>
    /// The largest candidate at which nothing fails, on an STA thread with the
    /// tokens on screen at each factor in turn, and Default again after it.
    /// </summary>
    internal static Fit Compute(ThemeTokens tokens)
    {
        Fit? result = null;
        TheThemeOnScreen.OnSta(() =>
        {
            Theme.EnsureLoaded();
            var requests = 0;
            string? above = null;
            try
            {
                foreach (var factor in FitFactor.Candidates())
                {
                    var failure = FirstFailure(tokens with { Fit = factor }, ref requests);
                    if (failure is null)
                    {
                        result = new Fit(factor, above, requests);
                        return;
                    }
                    above = Invariant($"{factor:0.00}: {failure}");
                }
                result = new Fit(null, above, requests);
            }
            finally
            {
                Theme.Apply(Theme.DefaultId);
            }
        });
        return result!;
    }

    /// <summary>The first thing that does not fit at these tokens' factor, or null: the contract's strings first (cheap), then the dialog page by page, then the cards and the sign-in window.</summary>
    private static string? FirstFailure(ThemeTokens tokens, ref int requests)
    {
        Theme.Install(tokens);
        if (FrameContract.Misses(tokens).FirstOrDefault() is { } miss) return miss.ToString();

        var (dialog, site) = DialogLayoutTests.Dialog();
        try
        {
            foreach (var (page, root) in DialogLayoutTests.EveryPage(dialog))
                if (DialogLayoutTests.FrameProblems(page, root).Concat(DialogLayoutTests.LineProblems(page, root)).FirstOrDefault() is { } problem)
                    return problem;
        }
        finally
        {
            dialog.Close();
            requests += site.Requests;
        }
        foreach (var (where, root) in DialogLayoutTests.CardsAndSignIn())
            if (DialogLayoutTests.LineProblems(where, root).FirstOrDefault() is { } problem)
                return problem;
        return null;
    }

    private static string Describe(string id, ThemeTokens tokens, Fit fit) =>
        Invariant($"{id} ({tokens.Font.Family}): ") +
        (fit.Factor is { } f ? Invariant($"fits at {f:0.00}") : Invariant($"refused at the floor, {FitFactor.Floor:0.00}")) +
        (fit.FailedAbove is null ? "" : $"; first failure at the step above: {fit.FailedAbove}");

    /// <summary>The refusal, as the owner hears it: the theme, the font, the factor and what did not fit there.</summary>
    internal static string Refusal(string id, ThemeTokens tokens, Fit fit) =>
        Invariant($"{id}: the font {tokens.Font.Family} is refused: it does not fit the frames even at the floor, {FitFactor.Floor:0.00}") +
        (fit.FailedAbove is null ? "" : $" ({fit.FailedAbove})");

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}
