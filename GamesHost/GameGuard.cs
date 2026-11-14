namespace PandoraOverlay;

/// <summary>
/// The first of the three layers that keep a game's exception from taking the
/// overlay down (A10): EVERY call the app makes into a game or its view goes
/// through here, by rule (the host's own calls, and the window's on a view:
/// Root, Pause, Resume, Dispose). A call that throws is caught, logged as
/// <c>AppLog.Games.Exception(Error, "&lt;id&gt; failed in &lt;what&gt;", ex)</c>
/// (the type and the stack, never the message: a message can carry anything,
/// CLAUDE.md constraint 4), and the game is stopped (<see cref="GameCondition.Faulted"/>):
/// fed nothing, its view closed by the window, its tile offering to start it
/// again from its last save. The other two layers are the engine's own guard
/// round a view's handlers and its loop, which report through
/// <c>IGameHost.Fault</c>, and <see cref="GameFaults"/>, the last net in
/// App's handler.
/// <para>
/// It catches every exception, on purpose: whatever a game threw, its state
/// in memory can no longer be trusted, and its last save is a whole state
/// (a turn is committed and saved before it is played back, A20), so stopping
/// it loses nothing. A second fault of a game already stopped is only logged.
/// </para>
/// </summary>
internal static class GameGuard
{
    /// <summary>The call, guarded. False: it threw, and the game is now Faulted.</summary>
    public static bool Run(HostedGame game, string what, Action call)
    {
        try
        {
            call();
            return true;
        }
        catch (Exception ex)
        {
            game.Owner.Stop(game, what, ex);
            return false;
        }
    }

    /// <summary>The call's answer, guarded. Default (null for an object): it threw, and the game is now Faulted.</summary>
    public static T? Run<T>(HostedGame game, string what, Func<T> call)
    {
        try
        {
            return call();
        }
        catch (Exception ex)
        {
            game.Owner.Stop(game, what, ex);
            return default;
        }
    }

    /// <summary>
    /// <see cref="Run(HostedGame, string, Action)"/> for the one call made on
    /// every poll (the reading, <see cref="GamesHost.Feed"/>): what it needs
    /// travels in <paramref name="state"/> and the call is a static lambda, so
    /// nothing is allocated (a lambda that captured the game and the reading
    /// would make a closure and a delegate on every poll, for every game).
    /// </summary>
    public static bool Run<TState>(HostedGame game, string what, TState state, Action<TState> call)
    {
        try
        {
            call(state);
            return true;
        }
        catch (Exception ex)
        {
            game.Owner.Stop(game, what, ex);
            return false;
        }
    }
}
