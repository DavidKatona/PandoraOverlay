namespace PandoraOverlay.Games.Tests;

/// <summary>
/// The overlay's log in a test: it keeps every line, so a test can see what a
/// game reported (a held save it carried on with, a count) and, as much, that a
/// line holds only facts (constraint 4 of the overlay: never a name, a Steam ID
/// or a position, never text from outside).
/// </summary>
public sealed class CollectingLog : IGameLog
{
    private readonly List<string> _lines = new();
    private readonly List<string> _warnings = new();

    /// <summary>Every line, Log and Warning alike, in the order written, as written.</summary>
    public IReadOnlyList<string> Lines => _lines;

    /// <summary>The warnings alone, in order: what a test asserts is empty when nothing went wrong.</summary>
    public IReadOnlyList<string> Warnings => _warnings;

    public void Log(string fact) => _lines.Add(fact);

    public void Warning(string fact)
    {
        _lines.Add(fact);
        _warnings.Add(fact);
    }
}
