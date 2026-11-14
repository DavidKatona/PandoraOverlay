using System.Text.Json.Nodes;
using PandoraOverlay.Games.Engine;

namespace PandoraOverlay.Games.Tests;

/// <summary>
/// The save tests' own state, at its version 3: numbers of every kind a game keeps
/// (an int, a generator's <c>uint</c>, a wallet's <c>long</c>, a <c>double</c>), a
/// string, a list, a nested record and a dictionary. Equality is by value, the lists
/// and the dictionary included, so a test can compare a loaded state with the one
/// saved.
///
/// <para>Its history, which the steps in <see cref="SampleSaves"/> undo: version 1
/// called <see cref="Name"/> "Title" (a rename); version 2 kept "Wins" and "Losses",
/// and version 3 keeps <see cref="Wins"/> and <see cref="Played"/>, worked out from
/// the two (a field derived from two others; "Losses" dropped).</para>
/// </summary>
public sealed record SampleSave
{
    public string Name { get; init; } = "";
    public int Turn { get; init; }
    public uint Seed { get; init; }
    public long Banked { get; init; }
    public double Rate { get; init; }
    public int Wins { get; init; }
    public int Played { get; init; }
    public List<int> Scores { get; init; } = new();
    public SampleSpot Home { get; init; } = new(0, 0);
    public Dictionary<string, int> Counts { get; init; } = new();

    public bool Equals(SampleSave? other) =>
        other is not null
        && Name == other.Name && Turn == other.Turn && Seed == other.Seed && Banked == other.Banked
        // The bits, not ==: -0.0 must come back as -0.0, and a NaN would never equal itself.
        && BitConverter.DoubleToInt64Bits(Rate) == BitConverter.DoubleToInt64Bits(other.Rate)
        && Wins == other.Wins && Played == other.Played
        && (Scores ?? new()).SequenceEqual(other.Scores ?? new())
        && Home == other.Home
        && (Counts ?? new()).Count == (other.Counts ?? new()).Count
        && (Counts ?? new()).All(kv => other.Counts!.TryGetValue(kv.Key, out int v) && v == kv.Value);

    public override int GetHashCode() => HashCode.Combine(Name, Turn, Seed, Banked);

    /// <summary>A state with something in every member.</summary>
    public static SampleSave Full() => new()
    {
        Name = "Ridge run",
        Turn = 41,
        Seed = 2693262067u,
        Banked = 1234567,
        Rate = 0.75,
        Wins = 7,
        Played = 10,
        Scores = new() { 3, 1, 4, 1, 5 },
        Home = new(12, 9),
        Counts = new() { ["plants"] = 4, ["water"] = 2 },
    };
}

/// <summary>A nested record of the sample state.</summary>
public sealed record SampleSpot(int X, int Y);

/// <summary>The sample state's schema at version 3, with its two steps, and probes to see which steps ran.</summary>
public static class SampleSaves
{
    /// <summary>
    /// Version 3, the steps from 1 and 2. <paramref name="ran"/>, when given, collects
    /// the version each step started from, in the order they ran. The check refuses
    /// a negative turn and a null list, as a game's own would.
    /// </summary>
    public static SaveSchema<SampleSave> Schema(List<int>? ran = null) =>
        new SaveSchema<SampleSave>(3, s => s.Turn >= 0 && s.Scores is not null && s.Counts is not null && s.Home is not null)
            .Upgrade(1, RenameTitle(ran))
            .Upgrade(2, WorkOutPlayed(ran));

    /// <summary>Version 1 to 2: "Title" became "Name". A rename changes what the old text means: it needs a step.</summary>
    public static Func<JsonObject, JsonObject> RenameTitle(List<int>? ran = null) => o =>
    {
        ran?.Add(1);
        var title = o["Title"];
        o.Remove("Title");
        o["Name"] = title;
        return o;
    };

    /// <summary>Version 2 to 3: "Played" is "Wins" plus "Losses", and "Losses" goes.</summary>
    public static Func<JsonObject, JsonObject> WorkOutPlayed(List<int>? ran = null) => o =>
    {
        ran?.Add(2);
        int wins = o["Wins"]!.GetValue<int>();
        int losses = o["Losses"]!.GetValue<int>();
        o["Played"] = wins + losses;
        o.Remove("Losses");
        return o;
    };
}
