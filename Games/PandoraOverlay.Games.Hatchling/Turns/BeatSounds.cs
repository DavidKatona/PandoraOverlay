namespace PandoraOverlay.Games.Hatchling;

/// <summary>One cue of a beat, and the tick of that beat it plays on (0 when the beat begins).</summary>
internal readonly record struct BeatSound(int Tick, Cue Cue);

/// <summary>
/// When a turn's beats sound (H96, H97, H98): the one pure rule, <c>PLAN-SOUND.md</c>
/// 3.2's table row by row, and 3.4's skip. Data in, data out: no WPF, no engine type,
/// nothing kept; the beat's step plays what this answers (<c>BeatStep</c>).
///
/// <para><b>On the played ticks, never at a beat's end</b> (H96). A pair is played when
/// the playback PLAYS its tick (<c>Begin</c> is tick 0, each <c>Advance</c> the next),
/// and never from <c>Finish</c>: a skip finishes the beats it never played, so sounds
/// tied to the played ticks can't flood the moment a player skips. Every tick here is
/// under its beat's ticks (a test, over the 200 seeded turns and the longest turn), so
/// none of them is the beat's end.</para>
///
/// <para><b>The ticks are the beat's own</b> (H93's table, <see cref="BeatTicks"/>, and the
/// step's constants): a walk's cell at c times the pace's ticks a cell, so each step
/// lands where the board starts the next cell; a strike's and a bite's on the blow
/// (<c>BeatStep.WindUpTicks</c>, 6), where the board shows it land; a fall where the
/// beat's own fade begins (<c>BeatStep.StrikeFadeFrom</c> for your hit, 18;
/// <c>BeatStep.FadeFrom</c> of the beat's ticks for a swept one or a hurt, 8): the
/// same numbers the step draws with, named once there, so the sound can't drift off
/// the picture.</para>
///
/// <para><b>No sound says more than the banner</b> (H3, H96). A beat exists only for
/// what the player may know, so a hunter out of sight has no beat and no cue; and the
/// one Out of sight beat carries no event, so whether "Something heard you" is told by
/// its ticks alone (<see cref="BeatTicks.UnseenHeard"/> against
/// <see cref="BeatTicks.UnseenQuiet"/>, the one flag <c>TurnScript.ShowUnseen</c> picks
/// both by), never by a word of its banner. It sounds <see cref="Cue.Heard"/> and
/// nothing that says which, how many or where.</para>
///
/// <para><b>Silent</b> (H97): the Gap, a wait or a daze or a hold, small game (not a
/// hunter, H4), Time passes (every turn has one), the quiet Out of sight. What happens
/// every turn would only be noise.</para>
/// </summary>
internal static class BeatSounds
{
    private static readonly BeatSound[] None = Array.Empty<BeatSound>();

    /// <summary>
    /// The (tick, cue) pairs of <paramref name="beat"/>, in tick order; none for a silent
    /// beat. <paramref name="apex"/> is whether the beat's creature is the apex: only a
    /// Focus asks it (<see cref="Cue.FocusApex"/>, so the apex is heard as the apex, H95).
    /// </summary>
    public static IReadOnlyList<BeatSound> Of(Beat beat, bool apex)
    {
        ArgumentNullException.ThrowIfNull(beat);
        switch (beat.Kind)
        {
            case BeatKind.YouWalk:
                // Your walk sounds every cell, in its pace (H97, G9): Sneak heard quiet,
                // Sprint loud, which is the rule the player is learning.
                if (beat.Events.OfType<Walked>().FirstOrDefault() is not { } walked) return None;
                var (step, perCell) = walked.Pace switch
                {
                    Pace.Sneak => (Cue.StepSneak, BeatTicks.SneakPerCell),
                    Pace.Sprint => (Cue.StepSprint, BeatTicks.SprintPerCell),
                    _ => (Cue.Step, BeatTicks.WalkPerCell),
                };
                return Cells(beat, step, walked.Path.Count, perCell);

            case BeatKind.YouEat:
                return One(0, Cue.Eat);
            case BeatKind.YouDrink:
                return One(0, Cue.Drink);
            case BeatKind.YouRest:
                return One(0, Cue.Rest);

            case BeatKind.YouStrike:
                // On the blow, not the wind-up (H96); a defeat where its fade begins,
                // after its hurt (the table's 18).
                return Blow(beat, BeatStep.WindUpTicks, Defeated(beat), BeatStep.StrikeFadeFrom);

            case BeatKind.AlsoStruck:
                // A swept one is struck at once (its hurt from tick 0) and falls in its
                // hurt's last third.
                return Blow(beat, 0, Defeated(beat), BeatStep.FadeFrom(beat.Ticks));

            case BeatKind.Focus:
                return One(0, apex ? Cue.FocusApex : Cue.Focus);

            case BeatKind.Notices:
                if (beat.Events.OfType<Noticed>().FirstOrDefault() is not { } noticed) return None;
                return One(0, noticed.Kind switch
                {
                    Notice.Spotted => Cue.Spotted,
                    Notice.Heard => Cue.Heard,
                    _ => Cue.LostTrail,
                });

            case BeatKind.Steps:
                // A known hunter is heard moving, every cell, quieter and lower than you (H97, H93).
                if (beat.Events.OfType<Stepped>().FirstOrDefault() is not { } stepped) return None;
                return Cells(beat, Cue.HunterStep, stepped.Path.Count, BeatTicks.StepPerCell);

            case BeatKind.Bites:
                return One(BeatStep.WindUpTicks, Cue.Bite);
            case BeatKind.Dodged:
                return One(BeatStep.WindUpTicks, Cue.Dodge);

            case BeatKind.IsHurt:
                var falls = beat.Events.OfType<Hurt>().Any(h => h.Defeated);
                return Blow(beat, 0, falls, BeatStep.FadeFrom(beat.Ticks), Cue.Sting);

            case BeatKind.SecondWind:
                return One(0, Cue.SecondWind);
            case BeatKind.Arrives:
                return One(0, Cue.Arrive);

            case BeatKind.Unseen:
                // Told by the ticks alone (the summary): the beat carries no event (H3).
                return beat.Ticks == BeatTicks.UnseenHeard ? One(0, Cue.Heard) : None;

            case BeatKind.TimeHurts:
                return One(0, Cue.Starve);
            case BeatKind.Stage:
                return One(0, Cue.Stage);
            case BeatKind.Apex:
                return One(0, Cue.Apex);

            case BeatKind.End:
                if (beat.Events.OfType<RunEnded>().FirstOrDefault() is not { } ended) return None;
                return One(0, ended.Won ? Cue.Win : Cue.Lost);

            // Gap, Stays, SmallGame, TimePasses: silent (H97).
            default:
                return None;
        }
    }

    /// <summary>
    /// The one cue a skip plays (H98), over the beats from <paramref name="from"/> (the
    /// first not begun: their cues have not played): the run's end (<see cref="Cue.Win"/>
    /// or <see cref="Cue.Lost"/>) if one is there, else the apex's coming
    /// (<see cref="Cue.Apex"/>), else a stage (<see cref="Cue.Stage"/>), else none. So a
    /// player who skips every turn still hears the three outcomes that change a run, and
    /// nothing else; the beats it finishes sound nothing of their own.
    /// </summary>
    public static Cue? OnSkip(IReadOnlyList<Beat> beats, int from)
    {
        ArgumentNullException.ThrowIfNull(beats);
        Cue? found = null;
        for (var i = Math.Max(0, from); i < beats.Count; i++)
        {
            var beat = beats[i];
            if (beat.Kind == BeatKind.End && beat.Events.OfType<RunEnded>().FirstOrDefault() is { } ended)
                return ended.Won ? Cue.Win : Cue.Lost;   // the end outranks everything: no need to look further
            if (beat.Kind == BeatKind.Apex) found = Cue.Apex;
            else if (beat.Kind == BeatKind.Stage && found is null) found = Cue.Stage;
        }
        return found;
    }

    // ---- the rows' shapes ------------------------------------------------------------

    private static BeatSound[] One(int tick, Cue cue) => new[] { new BeatSound(tick, cue) };

    /// <summary>
    /// A cue at each cell c of a walk, at c times <paramref name="perCell"/>: the first
    /// cell when the beat begins. A tick at or past the beat's end is left out: only a
    /// beat the script held to <see cref="TurnScript.MaxBeatTicks"/> could ask one (a
    /// walk of more cells than the rules ever make), and its end is never a played tick.
    /// </summary>
    private static BeatSound[] Cells(Beat beat, Cue cue, int cells, int perCell)
    {
        var pairs = new List<BeatSound>(cells);
        for (var c = 0; c < cells && c * perCell < beat.Ticks; c++) pairs.Add(new BeatSound(c * perCell, cue));
        return pairs.ToArray();
    }

    /// <summary>A blow's cue (<see cref="Cue.Hit"/> unless named) at <paramref name="at"/>, then <see cref="Cue.Defeat"/> at <paramref name="fallsAt"/> when it fell.</summary>
    private static BeatSound[] Blow(Beat beat, int at, bool falls, int fallsAt, Cue blow = Cue.Hit)
    {
        if (!falls || fallsAt >= beat.Ticks) return One(at, blow);
        return new[] { new BeatSound(at, blow), new BeatSound(fallsAt, Cue.Defeat) };
    }

    private static bool Defeated(Beat beat) => beat.Events.OfType<Struck>().Any(s => s.Defeated);
}
