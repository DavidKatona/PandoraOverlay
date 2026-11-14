namespace PandoraOverlay.Games;

/// <summary>
/// The overlay's own log, under the games' category. It holds facts only,
/// as the whole log does (CLAUDE.md, constraint 4): never a name, a Steam ID
/// or a position, never text from outside the game. An exception is not
/// logged here: it goes to <see cref="IGameHost.Fault"/>, which logs its type
/// and stack, never its message.
/// </summary>
public interface IGameLog
{
    /// <summary>Facts and counts only: never a name, never free text from outside.</summary>
    void Log(string fact);

    /// <summary>Something went wrong and the game carried on (facts and counts only, as <see cref="Log"/>).</summary>
    void Warning(string fact);
}
