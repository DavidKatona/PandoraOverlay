namespace PandoraOverlay.Games.Tests;

/// <summary>
/// The host's sound in a test (<see cref="FakeHost.Sound"/>): it records every
/// <see cref="GameSound"/> a game hands it, in order, and plays nothing. What a
/// game asks for is the game's to test; whether the host plays it (on screen,
/// not paused, not muted, its limits) is the app's gate, tested in the app's
/// tests (A79, A81).
/// </summary>
public sealed class FakeSound : IGameSound
{
    private readonly List<GameSound> _sounds = new();

    /// <summary>Every sound handed over, in order, as the objects themselves.</summary>
    public IReadOnlyList<GameSound> Sounds => _sounds;

    /// <summary>Every sound handed over, in order, by its name.</summary>
    public IReadOnlyList<string> Played => _sounds.Select(s => s.Name).ToList();

    public void Play(GameSound sound) => _sounds.Add(sound);
}
