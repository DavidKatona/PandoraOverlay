using System.Collections;
using System.Reflection;
using PandoraOverlay.Games.Hatchling;

namespace PandoraOverlay.Games.Tests;

/// <summary>
/// The save tests' two walks by reflection, written once (task H08):
/// <list type="bullet">
/// <item><see cref="Differences"/>: two states equal field for field, every public
/// property of every object in them, lists in order, dictionaries by key, sets as
/// sets, a double to its bits, a moment to its tick and its kind. It walks
/// <see cref="GameState"/>, <see cref="RunState"/>, <see cref="You"/>,
/// <see cref="Creature"/> and everything else by what they declare, so a field added
/// to the game after the save was written is compared too, and a save that leaves it
/// out fails the round trip instead of losing it unnoticed.</item>
/// <item><see cref="DefaultMembers"/>: the members of the save's shape that hold the
/// value a lost member would read back as, so a sample proves nothing about them:
/// what the round trip's "something in every member" (A51) is checked with.</item>
/// </list>
/// </summary>
internal static class Deep
{
    /// <summary>Every place where <paramref name="a"/> and <paramref name="b"/> differ, as paths ("Run.You.Health: 0.5 against 0.25"); none when they are equal.</summary>
    public static List<string> Differences(object? a, object? b)
    {
        var found = new List<string>();
        Walk(a, b, "state", found, 0);
        return found;
    }

    /// <summary>The paths of <see cref="Differences"/>, at most ten, one a line, for a message.</summary>
    public static string Show(IReadOnlyList<string> differences) =>
        string.Join("\n", differences.Take(10)) + (differences.Count > 10 ? $"\n... and {differences.Count - 10} more" : "");

    private static void Walk(object? a, object? b, string path, List<string> found, int depth)
    {
        if (depth > 24)
        {
            found.Add($"{path}: deeper than the states go");
            return;
        }
        if (a is null || b is null)
        {
            if (a is not null || b is not null) found.Add($"{path}: {Text(a)} against {Text(b)}");
            return;
        }
        var type = a.GetType();
        if (type != b.GetType())
        {
            found.Add($"{path}: a {type.Name} against a {b.GetType().Name}");
            return;
        }
        if (IsSimple(type))
        {
            if (!SimpleEquals(a, b)) found.Add($"{path}: {Text(a)} against {Text(b)}");
            return;
        }
        if (a is IDictionary da)
        {
            var db = (IDictionary)b;
            if (da.Count != db.Count) found.Add($"{path}: {da.Count} entries against {db.Count}");
            foreach (var key in da.Keys)
            {
                if (!db.Contains(key)) found.Add($"{path}[{key}]: missing from the second");
                else Walk(da[key], db[key], $"{path}[{key}]", found, depth + 1);
            }
            return;
        }
        if (IsSet(type))
        {
            var sa = ((IEnumerable)a).Cast<object>().ToHashSet();
            var sb = ((IEnumerable)b).Cast<object>().ToHashSet();
            if (!sa.SetEquals(sb)) found.Add($"{path}: {{{string.Join(", ", sa)}}} against {{{string.Join(", ", sb)}}}");
            return;
        }
        if (a is IEnumerable ea)
        {
            var la = ea.Cast<object?>().ToList();
            var lb = ((IEnumerable)b).Cast<object?>().ToList();
            if (la.Count != lb.Count) found.Add($"{path}: {la.Count} items against {lb.Count}");
            for (var i = 0; i < Math.Min(la.Count, lb.Count); i++) Walk(la[i], lb[i], $"{path}[{i}]", found, depth + 1);
            return;
        }
        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!property.CanRead || property.GetIndexParameters().Length > 0) continue;
            Walk(property.GetValue(a), property.GetValue(b), $"{path}.{property.Name}", found, depth + 1);
        }
        foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
            Walk(field.GetValue(a), field.GetValue(b), $"{path}.{field.Name}", found, depth + 1);
    }

    private static bool IsSimple(Type type) =>
        type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal)
        || type == typeof(DateTime) || type == typeof(TimeSpan) || type == typeof(Guid);

    private static bool SimpleEquals(object a, object b) => (a, b) switch
    {
        // The bits: a double must come back exactly, -0.0 included.
        (double x, double y) => BitConverter.DoubleToInt64Bits(x) == BitConverter.DoubleToInt64Bits(y),
        (float x, float y) => BitConverter.SingleToInt32Bits(x) == BitConverter.SingleToInt32Bits(y),
        // DateTime's own Equals ignores the kind; a moment saved as local would not be the one kept.
        (DateTime x, DateTime y) => x.Ticks == y.Ticks && x.Kind == y.Kind,
        _ => a.Equals(b),
    };

    private static bool IsSet(Type type) =>
        type.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ISet<>));

    private static string Text(object? value) => value switch
    {
        null => "null",
        double d => d.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
        DateTime t => t.ToString("O", System.Globalization.CultureInfo.InvariantCulture) + " " + t.Kind,
        string s => $"\"{s}\"",
        _ => value.ToString() ?? "?",
    };

    // ---- something in every member ------------------------------------------------------------

    /// <summary>
    /// The members of the save's shape (<see cref="SaveData"/> and the classes in it)
    /// that hold, in EVERY one of <paramref name="instances"/>, the value a member lost
    /// on the way back would read as: null, an empty string or list, its type's default,
    /// or the default its class gives it (a new instance's). A member of a class inside
    /// a list or a dictionary counts as filled when any item fills it. Paths are
    /// "Run.Creatures[].Dazed", "Species{}.Fresh".
    /// </summary>
    public static List<string> DefaultMembers(Type type, IReadOnlyList<object> instances, string prefix = "")
    {
        var found = new List<string>();
        var fresh = Activator.CreateInstance(type)!;
        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var name = prefix + property.Name;
            var values = instances.Select(property.GetValue).ToList();
            var propertyType = property.PropertyType;

            if (IsShapeClass(propertyType))
            {
                var parts = values.Where(v => v is not null).Cast<object>().ToList();
                if (parts.Count == 0) found.Add(name);
                else found.AddRange(DefaultMembers(propertyType, parts, name + "."));
                continue;
            }
            if (ShapeItems(propertyType) is var (itemType, mark) && itemType is not null)
            {
                var items = values.Where(v => v is not null).SelectMany(v => Items(v!)).ToList();
                if (items.Count == 0) found.Add(name);
                else found.AddRange(DefaultMembers(itemType, items, name + mark + "."));
                continue;
            }
            var unset = property.GetValue(fresh);
            if (values.All(v => IsDefault(v, propertyType, unset))) found.Add(name);
        }
        return found;
    }

    /// <summary>A class of the save's shape: one of the game's, not a string.</summary>
    public static bool IsShapeClass(Type type) =>
        type.IsClass && type != typeof(string) && type.Assembly == typeof(SaveData).Assembly;

    /// <summary>A list's or a dictionary's item type when it is a class of the shape, with the path's mark ("[]" or "{}").</summary>
    private static (Type? Item, string Mark) ShapeItems(Type type)
    {
        if (!type.IsGenericType) return (null, "");
        var definition = type.GetGenericTypeDefinition();
        var arguments = type.GetGenericArguments();
        if (definition == typeof(List<>) && IsShapeClass(arguments[0])) return (arguments[0], "[]");
        if (definition == typeof(Dictionary<,>) && IsShapeClass(arguments[1])) return (arguments[1], "{}");
        return (null, "");
    }

    private static IEnumerable<object> Items(object collection) =>
        collection is IDictionary d ? d.Values.Cast<object>() : ((IEnumerable)collection).Cast<object>();

    private static bool IsDefault(object? value, Type type, object? unset) => value switch
    {
        null => true,
        string s => s.Length == 0 || s == (string?)unset,
        ICollection c => c.Count == 0,
        _ => value.Equals(Activator.CreateInstance(type)) || value.Equals(unset),
    };
}
