namespace PandoraOverlay.Games.Hatchling;

/// <summary>
/// Hatchling's sounds, by name (H95): the 29 of <c>PLAN-SOUND.md</c> 3.1, in its order,
/// 24 on the turn's beats and 5 on the pages. A cue is only a name: the game hands
/// the host the engine's <c>SoundBank</c> sound of this name (<c>SoundBook</c>), through the view's
/// one door (<c>HatchlingView.Sound</c>), and the host plays it or drops it (A77, A79).
/// Each cue's file is <c>Sound/&lt;file&gt;.wav</c>, embedded in this assembly, the
/// file written by the sound tool, never by hand (A84). Its level (the peak, in dBFS),
/// its length and its recipe are the tool's (<c>tools/games-sound/hatchling/cues.js</c>,
/// R1 to R7), not this game's: the game only says WHEN a cue plays (<see cref="BeatSounds"/>
/// for a beat, H96 to H98; the pages for a click, H99).
/// </summary>
internal enum Cue
{
    // ---- your turn ----

    /// <summary>A cell you walk (the owner's pick, "Pad").</summary>
    Step,
    /// <summary>A cell you sneak: the Pad, darker and softer.</summary>
    StepSneak,
    /// <summary>A cell you sprint: the Pad, tighter and brighter.</summary>
    StepSprint,
    /// <summary>You eat: plants, meat or fish.</summary>
    Eat,
    /// <summary>You drink.</summary>
    Drink,
    /// <summary>You rest.</summary>
    Rest,
    /// <summary>Your strike lands, and each one a sweep strikes.</summary>
    Hit,
    /// <summary>A creature falls: your hit, your spikes, its bleeding.</summary>
    Defeat,

    // ---- a hunter's turn ----

    /// <summary>A known predator's turn begins (the owner's pick, "Ping").</summary>
    Focus,
    /// <summary>The apex's turn begins: the Ping an octave down.</summary>
    FocusApex,
    /// <summary>It spots you (the owner's pick, "Chirp up").</summary>
    Spotted,
    /// <summary>It hears you; and "Something heard you" out of sight.</summary>
    Heard,
    /// <summary>It loses your trail.</summary>
    LostTrail,
    /// <summary>A cell a known predator walks: quieter and lower than yours.</summary>
    HunterStep,
    /// <summary>A bite on you (the owner's pick, "Crunch"): the loudest of the set (R4).</summary>
    Bite,
    /// <summary>You dodge a bite.</summary>
    Dodge,
    /// <summary>Your spikes hurt it, or it bleeds.</summary>
    Sting,
    /// <summary>Second wind.</summary>
    SecondWind,
    /// <summary>A predator arrives where you see it.</summary>
    Arrive,

    // ---- the world ----

    /// <summary>Hunger or thirst takes health.</summary>
    Starve,
    /// <summary>You grow into a stage (the owner's pick, "Arpeggio").</summary>
    Stage,
    /// <summary>The apex has your scent.</summary>
    Apex,
    /// <summary>The run is won (the owner's pick, "Fanfare").</summary>
    Win,
    /// <summary>The run is lost: caught, out of food, out of water (the owner's pick, "Fall").</summary>
    Lost,

    // ---- the pages ----

    /// <summary>A run starts: Camp's "Start a run".</summary>
    Hatch,
    /// <summary>An instinct picked; a trait's rank taken.</summary>
    Choose,
    /// <summary>A redraw; a journal entry swapped.</summary>
    Shuffle,
    /// <summary>A species unlocked with the wallet (H10).</summary>
    Unlock,
    /// <summary>An achievement earned by the click, instead of its own cue (H99).</summary>
    Reward,
}

/// <summary>Each <see cref="Cue"/>'s file, the one table of them (H95).</summary>
internal static class Cues
{
    /// <summary>
    /// Every cue, in <see cref="Cue"/>'s order, beside its file's name (without the
    /// folder and the <c>.wav</c>): exactly the name the sound tool writes and its
    /// manifest lists (<c>tools/games-sound/hatchling/manifest.json</c>), which a test
    /// holds. A two-word cue is written with a hyphen, the rest are their names in
    /// lower case.
    /// </summary>
    public static readonly IReadOnlyList<(Cue Cue, string File)> All = new[]
    {
        (Cue.Step, "step"),
        (Cue.StepSneak, "step-sneak"),
        (Cue.StepSprint, "step-sprint"),
        (Cue.Eat, "eat"),
        (Cue.Drink, "drink"),
        (Cue.Rest, "rest"),
        (Cue.Hit, "hit"),
        (Cue.Defeat, "defeat"),
        (Cue.Focus, "focus"),
        (Cue.FocusApex, "focus-apex"),
        (Cue.Spotted, "spotted"),
        (Cue.Heard, "heard"),
        (Cue.LostTrail, "lost-trail"),
        (Cue.HunterStep, "hunter-step"),
        (Cue.Bite, "bite"),
        (Cue.Dodge, "dodge"),
        (Cue.Sting, "sting"),
        (Cue.SecondWind, "second-wind"),
        (Cue.Arrive, "arrive"),
        (Cue.Starve, "starve"),
        (Cue.Stage, "stage"),
        (Cue.Apex, "apex"),
        (Cue.Win, "win"),
        (Cue.Lost, "lost"),
        (Cue.Hatch, "hatch"),
        (Cue.Choose, "choose"),
        (Cue.Shuffle, "shuffle"),
        (Cue.Unlock, "unlock"),
        (Cue.Reward, "reward"),
    };

    /// <summary>The file of <paramref name="cue"/> (without the folder and the extension), from <see cref="All"/>.</summary>
    public static string FileOf(Cue cue)
    {
        foreach (var (each, file) in All)
            if (each == cue) return file;
        throw new ArgumentOutOfRangeException(nameof(cue), cue, "a cue with no file in Cues.All");
    }
}
