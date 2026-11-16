namespace PandoraOverlay.Tests;

/// <summary>
/// A speaker that plays nothing and remembers everything (A80's
/// <see cref="ISpeaker"/>): each Play with its samples and gain, each
/// StopAll, each Dispose, in one ordered list too, so a test of the games'
/// sound gate (<see cref="GamesSound"/>) sees exactly what would have reached
/// the PC's audio device, and when.
/// </summary>
internal sealed class FakeSpeaker : ISpeaker
{
    /// <summary>Every sound passed on, in order: the array the gate handed over, and the gain.</summary>
    public List<(short[] Samples, float Gain)> Played { get; } = new();

    /// <summary>How many times every sound was stopped.</summary>
    public int Stops { get; private set; }

    /// <summary>How many times the speaker was disposed.</summary>
    public int Disposals { get; private set; }

    /// <summary>Every call, in order: "play", "stop", "dispose".</summary>
    public List<string> Calls { get; } = new();

    public void Play(short[] samples, float gain)
    {
        Played.Add((samples, gain));
        Calls.Add("play");
    }

    public void StopAll()
    {
        Stops++;
        Calls.Add("stop");
    }

    public void Dispose()
    {
        Disposals++;
        Calls.Add("dispose");
    }
}
