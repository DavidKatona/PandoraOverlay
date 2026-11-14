namespace PandoraOverlay.Games.Engine;

/// <summary>
/// How a <see cref="Steps.Tween"/> spends its ticks between its start (0) and
/// its end (1). Each one starts at exactly 0 and ends at exactly 1, so the
/// end state of a tween never depends on its easing, only the way there.
/// The formulas live in one place, <see cref="Steps.Ease"/>.
/// </summary>
public enum Easing
{
    /// <summary>Even steps all the way: <c>t</c>.</summary>
    Linear,

    /// <summary>Starts slowly, quickest in the middle, slows to a stop: <c>t * t * (3 - 2t)</c> (smoothstep).</summary>
    InOut,

    /// <summary>Starts quickly and slows to a stop: <c>1 - (1 - t) * (1 - t)</c>.</summary>
    Out,
}
