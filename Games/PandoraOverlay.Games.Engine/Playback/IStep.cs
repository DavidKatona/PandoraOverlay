namespace PandoraOverlay.Games.Engine;

/// <summary>
/// One beat of a <see cref="Playback"/>: a sprite sliding one cell, a bar
/// falling to its new value, a short hold between two of a game's events.
/// It exists because the owner's one complaint about the first prototype was
/// that the board simply changed (G9): a game shows each of its events in
/// order, one beat at a time, and a beat is this.
/// <para>
/// <b>The rule every step keeps, a game's own steps included.</b> Running a
/// step to its end with <see cref="Advance"/>, and calling
/// <see cref="Finish"/> at any point (before <see cref="Begin"/>, between two
/// <see cref="Advance"/>s, or after it already finished), must leave what it
/// draws in exactly the same end state. That is what makes skipping safe: a
/// playback can be skipped by a click, cut short by a broken step
/// (<see cref="Playback.StepLimitTicks"/>) or run at any speed, and the
/// picture it leaves is the one a patient player would have seen at the end.
/// A step that draws a moving sprite, for one, sets the sprite's final place
/// in <see cref="Finish"/> as surely as in its last <see cref="Advance"/>.
/// </para>
/// <para>
/// <b>A step changes only what is drawn, never a game's state</b> (A20). The
/// rules have already produced the new state, and the game has committed and
/// saved it, before the first step is enqueued; the steps only walk the
/// picture from the old state to the new one. So a playback can be paused
/// (the health warning, G18), sped up, skipped or dropped with the window
/// (<see cref="Playback.Clear"/>), and nothing is lost: the next view paints
/// the final state. A step that wrote to the state would make all four of
/// those lose or corrupt a player's progress.
/// </para>
/// <para>
/// <b>Time.</b> One <see cref="Advance"/> is one engine tick
/// (<see cref="EngineTime.TicksPerSecond"/> a second at speed 1). A step
/// counts its own ticks and never reads a clock: the playback advances only
/// when it is told to (A19), so a paused or covered view freezes every step
/// exactly where it was, with nothing owed when it resumes.
/// </para>
/// <para>
/// <b>What a step can count on.</b> The <see cref="Playback"/>, and the stock
/// <see cref="Steps.Sequence"/> and <see cref="Steps.Together"/> towards
/// their children, call <see cref="Begin"/> at most once and before any
/// <see cref="Advance"/>; never <see cref="Advance"/> again once it answered
/// true; and <see cref="Finish"/> at most once, with nothing after it. A step
/// is played once: a fresh one is made for every playback. The stock steps
/// go further and shrug off any other order (a second <see cref="Begin"/>, an
/// <see cref="Advance"/> after <see cref="Finish"/>), so a game's own
/// composite step that slips cannot break the picture.
/// </para>
/// <para>
/// A step's code may <see cref="Playback.Enqueue"/> more steps (they go to
/// the back) but may not call <see cref="Playback.Advance"/>,
/// <see cref="Playback.SkipAll"/> or <see cref="Playback.Clear"/>: the
/// playback throws <see cref="InvalidOperationException"/> for those.
/// </para>
/// </summary>
public interface IStep
{
    /// <summary>
    /// Once, when the step becomes the current one (or, in
    /// <see cref="Steps.Together"/>, when its group does). It may draw the
    /// step's first picture: a tween puts its value at its start here.
    /// </summary>
    void Begin();

    /// <summary>
    /// One tick. True once the step has finished by itself; the playback then
    /// drops it and calls nothing more on it. Finishing by itself leaves the
    /// same picture as <see cref="Finish"/> would.
    /// </summary>
    bool Advance();

    /// <summary>
    /// Jump to the end: draw the step's final picture at once, whatever it
    /// has drawn so far, even when it was never begun. Called for a skip
    /// (<see cref="Playback.SkipAll"/>) and for a step that overran
    /// <see cref="Playback.StepLimitTicks"/>.
    /// </summary>
    void Finish();
}
