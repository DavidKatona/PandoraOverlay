namespace PandoraOverlay;

/// <summary>
/// The one home of the app's volume steps (A82, moved here by A103): five
/// steps 6 dB apart, 1 = −24 dB up to 5 = 0 dB, the third by default. Two
/// levels read them, each from its own config key: the Games window's
/// (<c>GamesSoundVolume</c>, through <see cref="GamesSound"/>, whose own
/// <c>Steps</c>, <c>DefaultStep</c>, <c>StepOf</c>, <c>DecibelsOf</c> and
/// <c>GainOf</c> forward here, so its tests hold the same numbers) and the
/// overlay's clicks (<c>ClickSoundVolume</c>, Settings › General › Sounds,
/// through <see cref="ClickSound"/>). One mapping, so a step means the same
/// level in both, and a click keeps its place under the games' cues at any
/// step (<c>PLAN-CLICKS.md</c> 2 and 5.4).
/// <para>
/// A value out of range (a hand-edited <c>config.json</c>) is READ as the
/// nearest step and never written back by reading: only a control the
/// player clicks writes a step. Pure: no state, no IO.
/// </para>
/// </summary>
internal static class SoundSteps
{
    /// <summary>The volume's steps (A82): 1 to this, the loudest at 0 dB.</summary>
    public const int Count = 5;

    /// <summary>The volume a fresh config holds (A82, A103): −12 dB, well under The Isle's own sound, which is the player's warning.</summary>
    public const int Default = 3;

    /// <summary>Between two steps (A82): 6 dB, a clear "louder" or "quieter" each click.</summary>
    public const double DecibelsPerStep = 6;

    /// <summary>A value of the config as a step: held to 1..<see cref="Count"/>.</summary>
    public static int StepOf(int value) => Math.Clamp(value, 1, Count);

    /// <summary>A step's level in dB: 0 at the top step, <see cref="DecibelsPerStep"/> less for each below.</summary>
    public static double DecibelsOf(int step) => (StepOf(step) - Count) * DecibelsPerStep;

    /// <summary>
    /// The gain a step plays at, <c>10^(dB/20)</c> with 1 = −24 dB, 2 = −18,
    /// 3 = −12, 4 = −6, 5 = 0. A step out of range is read as the nearest.
    /// </summary>
    public static float GainOf(int step) => (float)Math.Pow(10, DecibelsOf(step) / 20);
}
