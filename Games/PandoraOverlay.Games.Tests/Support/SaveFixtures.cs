using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using PandoraOverlay.Games.Engine;
using Xunit;

namespace PandoraOverlay.Games.Tests;

/// <summary>
/// The rule that keeps a player's old saves loading for ever (A9), as a test every
/// game's tests call on their own folder, and the round trip that catches a state
/// whose shape loses something on the way back.
///
/// <para><b>Fixtures.</b> A fixture is a game's payload text exactly as a real save
/// held it (the <c>Payload</c> inside the host's envelope, not the envelope), mid-run
/// saves included, in a file named <c>v&lt;n&gt;-&lt;name&gt;.json</c>, where n is the
/// version it was written with. A game's folder is <c>&lt;Game&gt;/Fixtures</c> in this
/// project's source folder (A47). The rule: a version that ever shipped keeps its
/// fixture, and it must load as <see cref="SaveOutcome.Ok"/> in every later build.
/// Fixtures are ADDED, never edited: a fixture changed to make a test pass would hide
/// exactly the save of a real player that the new build can no longer read.</para>
///
/// <para>Every problem in a folder is reported at once, each naming its file or its
/// version, so one run shows the whole damage of a change.</para>
/// </summary>
public static class SaveFixtures
{
    /// <summary>A fixture's file name: "v", the version (1 or more, no leading zero), "-", a name, ".json".</summary>
    private static readonly Regex FixtureName = new(@"^v(?<version>[1-9][0-9]{0,8})-(?<name>.+)\.json$", RegexOptions.CultureInvariant);

    /// <summary>
    /// Every file "v&lt;n&gt;-&lt;name&gt;.json" in the folder (relative to the tests'
    /// source folder) loads as Ok through the schema, and every version from 1 to the
    /// schema's current has at least one. Anything else in the folder (a file of
    /// another name, a folder) fails too: a fixture misnamed would be a fixture never
    /// loaded.
    /// </summary>
    public static void AssertAllLoad<T>(SaveSchema<T> schema, string folder) where T : class
    {
        ArgumentNullException.ThrowIfNull(schema);
        AssertAllLoadIn(schema, Resolve(folder), folder);
    }

    /// <summary>
    /// <paramref name="state"/> saved and loaded back is Ok, not upgraded, and saves
    /// to the same text again: nothing the state's text holds was lost on the way
    /// back. This is what finds a property with a private setter (written, never read
    /// back), or a member whose type the reader can't build (every load Corrupt),
    /// before a player's restart does. A game calls it with a state that has
    /// something in every member, mid-run.
    /// </summary>
    public static void AssertRoundTrips<T>(SaveSchema<T> schema, T state) where T : class
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(state);
        string text = schema.Save(state);
        var (loaded, problem) = schema.Read(schema.CurrentVersion, text);
        if (loaded.Outcome != SaveOutcome.Ok)
        {
            Assert.Fail($"A {typeof(T).Name} saved at version {schema.CurrentVersion} loads back as {loaded.Outcome}: {problem}.");
            return;
        }
        if (loaded.Upgraded)
            Assert.Fail($"A {typeof(T).Name} saved at the current version loads back as upgraded.");
        string again = schema.Save(loaded.State!);
        if (again == text) return;
        int at = 0;
        while (at < text.Length && at < again.Length && text[at] == again[at]) at++;
        Assert.Fail(
            $"A {typeof(T).Name} does not survive a save and a load: the text differs from character {at}, " +
            $"saved \"{Around(text, at)}\", read back and saved again \"{Around(again, at)}\". " +
            "A property with a private setter is written but never read back: give it a public setter or init, or a constructor parameter.");
    }

    /// <summary>
    /// <see cref="AssertAllLoad{T}"/> on any folder: the public one resolves a name in
    /// the source folder; the helper's own tests use a temporary copy, so proving a
    /// failure leaves the repo's fixtures as they are.
    /// </summary>
    internal static void AssertAllLoadIn<T>(SaveSchema<T> schema, string path, string label) where T : class
    {
        if (!Directory.Exists(path))
        {
            Assert.Fail($"The fixture folder '{label}' ({path}) does not exist: a game's save fixtures are <Game>/Fixtures (A47).");
            return;
        }

        var problems = new List<string>();
        var covered = new HashSet<int>();

        foreach (string sub in Directory.GetDirectories(path).OrderBy(p => p, StringComparer.Ordinal))
            problems.Add($"'{Path.GetFileName(sub)}' is a folder: fixtures are files directly in '{label}', and nothing below is read.");

        foreach (string file in Directory.GetFiles(path).OrderBy(p => p, StringComparer.Ordinal))
        {
            string name = Path.GetFileName(file);
            var match = FixtureName.Match(name);
            if (!match.Success)
            {
                problems.Add($"{name} is not named as a fixture (v<n>-<name>.json, n from 1): it would never be loaded.");
                continue;
            }

            int version = int.Parse(match.Groups["version"].Value, System.Globalization.CultureInfo.InvariantCulture);
            if (version > schema.CurrentVersion)
            {
                problems.Add($"{name} is from version {version}, above the schema's current {schema.CurrentVersion}: a version never goes down.");
                continue;
            }

            string text = File.ReadAllText(file, Encoding.UTF8);
            var (loaded, problem) = schema.Read(version, text);
            if (loaded.Outcome == SaveOutcome.Ok)
                covered.Add(version);
            else
                problems.Add($"{name} (version {version}) loads as {loaded.Outcome}: {problem}.");
        }

        var missing = Enumerable.Range(1, schema.CurrentVersion).Where(v => !covered.Contains(v)).ToList();
        if (missing.Count > 0)
            problems.Add(
                $"{(missing.Count == 1 ? "version" : "versions")} {string.Join(", ", missing)} of {typeof(T).Name} " +
                $"{(missing.Count == 1 ? "has" : "have")} no fixture that loads: a version that ever shipped keeps one, and it must load for ever (A9).");

        if (problems.Count > 0)
            Assert.Fail($"The save fixtures in '{label}' fail:\n- " + string.Join("\n- ", problems));
    }

    /// <summary>A relative folder name in this project's source folder, folders allowed, never leaving it.</summary>
    private static string Resolve(string folder)
    {
        ArgumentException.ThrowIfNullOrEmpty(folder);
        if (Path.IsPathRooted(folder) || folder.Split('/', '\\').Any(part => part is "" or "." or ".."))
            throw new ArgumentException($"'{folder}' is not a fixture folder: a relative name such as \"Hatchling/Fixtures\".", nameof(folder));
        return Path.Combine(FrameGolden.TestsFolder, folder.Replace('/', Path.DirectorySeparatorChar));
    }

    /// <summary>Up to 30 characters of <paramref name="text"/> from <paramref name="at"/>, for a message.</summary>
    private static string Around(string text, int at) =>
        at >= text.Length ? "(the end)" : text.Substring(at, Math.Min(30, text.Length - at));
}
