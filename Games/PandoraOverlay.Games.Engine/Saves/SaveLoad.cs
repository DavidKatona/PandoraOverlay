namespace PandoraOverlay.Games.Engine;

/// <summary>
/// The answer of <see cref="SaveSchema{T}.Load"/> (A9).
/// </summary>
/// <param name="Outcome">Read, newer, or unreadable.</param>
/// <param name="State">The state read, at the current version: set exactly when <paramref name="Outcome"/> is <see cref="SaveOutcome.Ok"/>, null otherwise.</param>
/// <param name="Upgraded">
/// True when the save was older than the current version and its upgrade steps ran.
/// The game then writes the upgraded state back once (a <c>Now</c> write), so the
/// steps run once per save and not at every launch; it is the one write a game makes
/// at <c>Attach</c>. The host keeps a copy of the old version's file before that
/// write (A7), so a wrong step can still be undone by hand.
/// </param>
public readonly record struct SaveLoad<T>(SaveOutcome Outcome, T? State, bool Upgraded) where T : class;
