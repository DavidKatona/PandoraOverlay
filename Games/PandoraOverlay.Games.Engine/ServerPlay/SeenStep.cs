namespace PandoraOverlay.Games.Engine;

/// <summary>
/// Growth the overlay saw a dino make on the server (A6): one step of
/// <see cref="SeenGrowth"/>, from the life's highest growth seen so far to the
/// new reading. What it pays is the game's; the engine only says that it was
/// seen.
/// </summary>
/// <param name="Species">The dino, as the website spells it in this reading.</param>
/// <param name="Gender">Its gender as the website spells it, "" when the website sent none.</param>
/// <param name="From">The life's high-water mark before this step.</param>
/// <param name="To">This reading's growth, the new mark: always above <paramref name="From"/> when <see cref="SeenGrowth"/> made it.</param>
public readonly record struct SeenStep(string Species, string Gender, double From, double To)
{
    /// <summary>
    /// The growth seen in this step, <see cref="To"/> − <see cref="From"/>: only
    /// the rise above the life's mark, so a wobble of the website's last digit
    /// is never counted twice.
    /// </summary>
    public double Grown => To - From;

    /// <summary>
    /// The stage this step reached, when a stage line lies in
    /// (<see cref="From"/>, <see cref="To"/>]: the highest such line, as 1 (at
    /// <see cref="GrowthStages.Juvenile"/>) to 4 (at
    /// <see cref="GrowthStages.Elder"/>); null when none does. A step that starts
    /// exactly on a line does not reach it again (the mark was already there),
    /// and a step over two lines reports the higher, as the overlay's own
    /// milestones do. Because <see cref="From"/> is the life's mark, each line is
    /// reported at most once a life.
    /// </summary>
    public int? StageReached
    {
        get
        {
            var after = GrowthStages.IndexOf(To);
            return after > GrowthStages.IndexOf(From) ? after : null;
        }
    }
}
