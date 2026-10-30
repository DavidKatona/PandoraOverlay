namespace PandoraOverlay;

/// <summary>
/// The fit factor's numbers (FIT.md, the owner's answers of Oct 30 2026). A
/// theme's factor is the largest of <see cref="Candidates"/> at which every
/// string of the frame contract (<see cref="FrameContract"/>) fits its box and
/// the walk (<see cref="FitProbe"/>) finds nothing cut off and no page that
/// scrolls, with the body font's sizes multiplied by it. Below
/// <see cref="Floor"/> the font is REFUSED, never drawn smaller: unreadable
/// text is answered by refusal (FIT.md section 2). <c>FitFactorTests</c>
/// compute it for every theme and hold the file's recorded "Fit" to it; the
/// theme editor will compute it the same way (EDITOR.md).
/// </summary>
public static class FitFactor
{
    /// <summary>Full size: Default's factor, and every theme's whose font fits the frames as Segoe UI does.</summary>
    public const double Full = 1.00;

    /// <summary>The floor (the owner, Oct 30 2026): a 12 px body at 10.2 px, an 11 px label at 9.35. Below it a font is refused.</summary>
    public const double Floor = 0.85;

    /// <summary>The step, so the factor has two decimals (the owner, Oct 30 2026: WPF draws fractional sizes; steps of 0.05 would waste up to 4% of size).</summary>
    public const double Step = 0.01;

    /// <summary>Every factor a theme can have, from full size down to the floor by the step, each exactly its two decimals.</summary>
    public static IEnumerable<double> Candidates()
    {
        for (var hundredths = (int)Math.Round(Full * 100); hundredths >= (int)Math.Round(Floor * 100); hundredths--)
            yield return hundredths / 100.0;
    }
}
