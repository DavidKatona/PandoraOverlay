using PandoraOverlay.Games.Engine;

namespace PandoraOverlay.Games.Hatchling;

/// <summary>
/// What the side panel shows of your dino while a turn plays (G9: the panel follows
/// the beats, not the end of the turn): health as a fraction of your maximum, food and
/// water 0 to 100, growth a percent.
/// </summary>
internal readonly record struct PanelValues(double Health, double Food, double Water, double Growth)
{
    /// <summary>The values of a run as it stands.</summary>
    public static PanelValues Of(RunState run) => new(run.You.Health, run.You.Food, run.You.Water, run.You.Growth);
}

/// <summary>
/// One turn being played back: the scene the beats move, and what the turn bar, the
/// turn list and the panel read at this moment of it. It is DATA the beats change
/// (A20: a step changes only what is drawn), and the Run page reflects it on screen
/// each frame, so the picture a beat leaves can be compared, field by field, with the
/// one a skip leaves (A20's "skip equals run", A20).
/// </summary>
internal sealed class TurnPlay
{
    public TurnPlay(TurnResult result, TurnScript script, Scene scene, ArtBook art)
    {
        Result = result;
        Script = script;
        Scene = scene;
        Art = art;
        Panel = PanelValues.Of(result.Before);
        RowTexts = new string?[script.Rows.Count];
        Species = result.Before.Species;
    }

    /// <summary>The turn: the state before, the state after (already saved, A20), the events.</summary>
    public TurnResult Result { get; }

    /// <summary>The beats and the rows, from the events by the one function (H2).</summary>
    public TurnScript Script { get; }

    /// <summary>The board as it stands at this moment of the playback.</summary>
    public Scene Scene { get; }

    /// <summary>The game's art: a beat asks it whether an action is drawn, for the stand-ins (H30).</summary>
    public ArtBook Art { get; }

    /// <summary>The species played: your sprite's size is named from it (H21).</summary>
    public string Species { get; }

    /// <summary>What the banner reads now (G9: whose turn it is, and what is happening).</summary>
    public string Banner { get; set; } = "";

    /// <summary>The row of the turn list that has the marker: the one acting; -1 for none.</summary>
    public int Marker { get; set; } = -1;

    /// <summary>Each row's words as written so far: null for a row still to act (H28: nothing in its third column).</summary>
    public string?[] RowTexts { get; }

    /// <summary>What the side panel shows now.</summary>
    public PanelValues Panel { get; set; }

    /// <summary>
    /// The playback's own clock: one per beat tick played. The scene's ambient frame
    /// follows it, every 8 ticks, so the acting ring pulses (H55). Counted by the beats
    /// themselves, so it never reads a clock (the step's rule, A19).
    /// </summary>
    public int Ticks { get; set; }

    /// <summary>The scene's ambient frame flips every this many playback ticks (H55).</summary>
    public const int RingPulseTicks = 8;
}
