using System.Collections.ObjectModel;

namespace PandoraOverlay.Games.Engine;

/// <summary>
/// Frames shown one after another, each for a number of the engine's ticks
/// (<see cref="EngineTime.TicksPerSecond"/>, 30 a second: the art's
/// animations count the same ticks as the loop, A19, A28). An animation knows
/// nothing of time itself: the view asks <see cref="At"/> for the frame of the
/// tick it is drawing, so a paused view shows the same frame, and a view
/// repainted from its state after a close starts the animation where it likes.
///
/// <para>The rules are checked when it is made, so a frame table that breaks
/// them is a build defect the tests catch (an atlas turns these into errors
/// naming the atlas and the entry): at least one frame, one tick count per
/// frame, each count at least 1. The lists are copied, so a caller changing
/// its own list afterwards changes nothing here: an animation is immutable,
/// like the atlas that holds it.</para>
/// </summary>
public sealed record SpriteAnimation(string Name, IReadOnlyList<Sprite> Frames, IReadOnlyList<int> Ticks, bool Loops)
{
    // The properties are declared get-only on purpose: a `with` that swapped
    // the ticks would skip the checks and leave TotalTicks stale. TotalTicks
    // comes first because member initializers run in the order written, and
    // the checks must run before the copies (a null list is an
    // ArgumentNullException, never a NullReferenceException).

    /// <summary>The sum of every frame's ticks: one pass of the animation.</summary>
    public int TotalTicks { get; } = Checked(Name, Frames, Ticks);

    /// <summary>The animation's name in its atlas: <c>&lt;subject&gt;/&lt;action&gt;</c>.</summary>
    public string Name { get; } = Name;

    /// <summary>The frames in the order they show.</summary>
    public IReadOnlyList<Sprite> Frames { get; } = new ReadOnlyCollection<Sprite>(Frames.ToArray());

    /// <summary>How many ticks each frame shows for, one count per frame, each at least 1.</summary>
    public IReadOnlyList<int> Ticks { get; } = new ReadOnlyCollection<int>(Ticks.ToArray());

    /// <summary>True: after the last frame comes the first again. False: the last frame holds.</summary>
    public bool Loops { get; } = Loops;

    /// <summary>
    /// The frame to show at a tick counted from the animation's start. A
    /// negative tick is the first frame (an animation not started yet). A
    /// looping animation takes the tick modulo <see cref="TotalTicks"/>; one
    /// that does not loop holds its last frame from its end on, for ever.
    /// </summary>
    public Sprite At(int tick)
    {
        if (tick <= 0) return Frames[0];
        int left = Loops ? tick % TotalTicks : Math.Min(tick, TotalTicks - 1);
        for (int i = 0; i < Frames.Count; i++)
        {
            if (left < Ticks[i]) return Frames[i];
            left -= Ticks[i];
        }
        return Frames[^1]; // not reached: left is always under the total
    }

    /// <summary>The rules, before anything is copied; the sum of the ticks when they hold.</summary>
    private static int Checked(string name, IReadOnlyList<Sprite> frames, IReadOnlyList<int> ticks)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(frames);
        ArgumentNullException.ThrowIfNull(ticks);
        if (frames.Count == 0)
            throw new ArgumentException($"The animation '{name}' has no frame: it needs at least one.", nameof(frames));
        if (ticks.Count != frames.Count)
            throw new ArgumentException($"The animation '{name}' has {frames.Count} frames and {ticks.Count} tick counts: one count per frame.", nameof(ticks));
        long total = 0;
        for (int i = 0; i < frames.Count; i++)
        {
            if (frames[i] is null)
                throw new ArgumentException($"The animation '{name}' has no sprite at frame {i}.", nameof(frames));
            if (ticks[i] < 1)
                throw new ArgumentException($"The animation '{name}' shows frame {i} for {ticks[i]} ticks: every frame shows for at least 1.", nameof(ticks));
            total += ticks[i];
        }
        if (total > int.MaxValue)
            throw new ArgumentException($"The animation '{name}' runs for more ticks than can be counted.", nameof(ticks));
        return (int)total;
    }
}
