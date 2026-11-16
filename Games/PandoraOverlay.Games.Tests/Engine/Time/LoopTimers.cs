using Xunit;

namespace PandoraOverlay.Games.Tests;

/// <summary>
/// Every test that gives a <c>GameLoop</c> a timer, or counts
/// <c>GameLoop.LiveTimers</c>: one collection, run alone after the parallel
/// ones, because the count is the whole process's (a loop on another test's
/// thread would show in it). A game's view test that asserts on the count
/// joins it.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class LoopTimers
{
    public const string Name = "the engine's loop timers";
}
