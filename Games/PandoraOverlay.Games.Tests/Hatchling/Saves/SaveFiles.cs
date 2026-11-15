using System.IO;
using System.Text;
using PandoraOverlay.Games.Hatchling;

namespace PandoraOverlay.Games.Tests;

/// <summary>
/// The save's files in the tests' source folder, and the helper that writes them
/// once (task H08; A9): the four fixtures in <c>Hatchling/Fixtures/</c> (that folder holds
/// fixtures ONLY: the engine's <c>AssertAllLoad</c> fails on anything else there) and
/// the pinned text of the format here in <c>Hatchling/Saves/</c>.
///
/// <para><b>Written once, never edited.</b> With <c>HATCHLING_RECORD_SAVES=1</c> set,
/// a test run writes a file that is MISSING, from its sample; a file that is there is
/// never touched. A fixture is the payload a real save holds and must load in every
/// later build (A9): edited to make a test pass, it would hide exactly the save a new
/// build can no longer read. The pinned text changes only with a new version of the
/// format, and then by deleting it and recording it again, on purpose.</para>
/// </summary>
internal static class SaveFiles
{
    /// <summary>The variable that lets a test run write the missing files.</summary>
    public const string RecordVariable = "HATCHLING_RECORD_SAVES";

    /// <summary>The fixtures' folder, relative to the tests' source folder, as <c>AssertAllLoad</c> takes it (A47).</summary>
    public const string FixturesFolder = "Hatchling/Fixtures";

    public static bool Recording => Environment.GetEnvironmentVariable(RecordVariable) == "1";

    /// <summary>A fixture's file: <c>v&lt;version&gt;-&lt;name&gt;.json</c>.</summary>
    public static string FixturePath(string name) =>
        Path.Combine(FrameGolden.TestsFolder, "Hatchling", "Fixtures", $"v{HatchlingSave.Version}-{name}.json");

    /// <summary>The pinned text of the format at this version.</summary>
    public static string PinnedPath =>
        Path.Combine(FrameGolden.TestsFolder, "Hatchling", "Saves", $"pinned-v{HatchlingSave.Version}.json");

    /// <summary>
    /// The file's text with its one closing line ending taken off, or null when it is
    /// missing. Recording, a missing file is written first with <paramref name="text"/>
    /// (UTF-8, no byte-order mark, one closing LF, as the repo keeps its files).
    /// </summary>
    public static string? ReadOrRecord(string path, string text)
    {
        if (!File.Exists(path) && Recording)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text + "\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
        return File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8).TrimEnd('\n', '\r') : null;
    }

    /// <summary>The sample's payload, as a save would hold it: the schema's text of the save's shape.</summary>
    public static string PayloadOf(GameState state) => HatchlingSave.Schema.Save(HatchlingSave.From(state));

    /// <summary>The message for a file that is missing: how to write it, once.</summary>
    public static string Missing(string path) =>
        $"{Repo.Relative(path)} is missing. Run the tests once with {RecordVariable}=1 to write it from its sample, look at it, and check it in.";
}
