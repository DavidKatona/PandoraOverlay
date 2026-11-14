using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using PandoraOverlay.Games.Engine;
using PandoraOverlay.Games.Hatchling;
using Xunit;

namespace PandoraOverlay.Games.Tests;

/// <summary>
/// G3 as a test: one wallet per game, never shared. The game's state holds exactly
/// one <see cref="Wallet"/>, no other type of the game holds one, and no function
/// of <c>Progress/</c> takes a wallet of its own or a second state (whose wallet
/// would be a second one): everything is paid into and out of the state's.
/// </summary>
public class OneWalletTests
{
    private static readonly Assembly Game = typeof(GameState).Assembly;

    private const BindingFlags Everything =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    /// <summary><paramref name="type"/> is a wallet, or holds one as an element or a type argument (a list of wallets is wallets).</summary>
    private static bool Holds(Type type, Type what) =>
        type == what
        || (type.HasElementType && Holds(type.GetElementType()!, what))
        || (type.IsGenericType && type.GetGenericArguments().Any(a => Holds(a, what)));

    private static bool IsGenerated(Type type) =>
        type.IsDefined(typeof(CompilerGeneratedAttribute), false)
        || (type.DeclaringType is not null && IsGenerated(type.DeclaringType));

    /// <summary>The wallet-holding members reachable from <paramref name="type"/> through the game's own types, as "Type.Member".</summary>
    private static List<string> WalletsUnder(Type type, HashSet<Type> seen)
    {
        var found = new List<string>();
        if (!seen.Add(type)) return found;
        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
        {
            var t = property.PropertyType;
            if (Holds(t, typeof(Wallet))) found.Add($"{type.Name}.{property.Name}");
            foreach (var inner in GameTypesIn(t)) found.AddRange(WalletsUnder(inner, seen));
        }
        return found;
    }

    /// <summary>The game's own types a member's type is or holds.</summary>
    private static IEnumerable<Type> GameTypesIn(Type type)
    {
        if (type.Assembly == Game && !type.IsEnum) yield return type;
        if (type.HasElementType)
            foreach (var t in GameTypesIn(type.GetElementType()!)) yield return t;
        if (type.IsGenericType)
            foreach (var a in type.GetGenericArguments())
            foreach (var t in GameTypesIn(a)) yield return t;
    }

    [Fact]
    public void TheStateHoldsExactlyOneWallet()
    {
        Assert.Equal(new[] { "GameState.Wallet" }, WalletsUnder(typeof(GameState), new HashSet<Type>()));
    }

    [Fact]
    public void NoOtherTypeOfTheGameHoldsAWallet()
    {
        var holders = Game.GetTypes()
            .Where(t => !IsGenerated(t))
            .SelectMany(t => t.GetFields(Everything).Where(f => Holds(f.FieldType, typeof(Wallet))).Select(f => $"{t.Name}.{f.Name}"))
            .ToList();
        Assert.Equal(new[] { "GameState.<Wallet>k__BackingField" }, holders);
    }

    /// <summary>The types declared in the game's <c>Progress/</c> files, read from the source so a new file there is scanned too.</summary>
    private static IReadOnlyList<Type> ProgressTypes()
    {
        var names = GameSource.Files()
            .Where(f => f.Path.StartsWith("Progress/", StringComparison.Ordinal) && f.Path.EndsWith(".cs", StringComparison.Ordinal))
            .SelectMany(f => Regex.Matches(f.Text, @"^\s*internal\s+(?:sealed\s+|static\s+|abstract\s+)*(?:class|record|struct)\s+(\w+)", RegexOptions.Multiline)
                .Select(m => m.Groups[1].Value))
            .Distinct()
            .ToList();
        return names.Select(n => Game.GetType("PandoraOverlay.Games.Hatchling." + n, throwOnError: true)!).ToList();
    }

    [Fact]
    public void TheScanFindsTheProgressFiles()
    {
        var names = ProgressTypes().Select(t => t.Name).ToList();
        foreach (var expected in new[] { "GameState", "SpeciesRecord", "JournalState", "Totals", "LatelyLine", "LineageRules", "Unlocks", "Camp", "Settle" })
            Assert.Contains(expected, names);
    }

    [Fact]
    public void NoFunctionOfProgressTakesASecondWallet()
    {
        var bad = new List<string>();
        foreach (var type in ProgressTypes())
        {
            var functions = type.GetMethods(Everything).Cast<MethodBase>().Concat(type.GetConstructors(Everything));
            foreach (var function in functions)
            {
                // The state's own property: its setter puts THE wallet in place (a
                // save being read, a clone), it does not add a second one.
                if (type == typeof(GameState) && function.Name == "set_Wallet") continue;
                var parameters = function.GetParameters();
                if (parameters.Any(p => Holds(p.ParameterType, typeof(Wallet))))
                    bad.Add($"{type.Name}.{function.Name} takes a wallet");
                if (parameters.Count(p => Holds(p.ParameterType, typeof(GameState))) > 1)
                    bad.Add($"{type.Name}.{function.Name} takes two states");
            }
        }
        Assert.Empty(bad);
    }

    [Fact]
    public void TheScanWouldSeeAWalletParameter()
    {
        // The check itself, on a method that would break G3: Holds sees a wallet
        // wherever it sits in a parameter's type.
        var method = typeof(OneWalletTests).GetMethod(nameof(PlantedTransfer), BindingFlags.NonPublic | BindingFlags.Static)!;
        Assert.All(method.GetParameters(), p => Assert.True(Holds(p.ParameterType, typeof(Wallet))));
    }

    private static void PlantedTransfer(Wallet to, List<Wallet> from, Wallet[] more) { }
}
