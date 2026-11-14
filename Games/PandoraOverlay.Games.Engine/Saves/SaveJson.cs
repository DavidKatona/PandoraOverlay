using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;

namespace PandoraOverlay.Games.Engine;

/// <summary>
/// The one set of JSON rules every game's save is written and read with (A9). One
/// instance for every schema, frozen, so no game can loosen it for itself and a
/// save means the same to every build: the rules below are part of the save format,
/// and changing one is a change to every shipped save. <see cref="SaveSchema{T}"/>
/// summarises them for game authors.
/// </summary>
internal static class SaveJson
{
    /// <summary>
    /// <list type="bullet">
    /// <item>Compact (not indented): saves are written often, on every committed
    /// turn, and the host wraps the text in its own envelope (A7).</item>
    /// <item>Names exactly as the type spells them, matched with case: an upgrade
    /// step renames a key on a <c>JsonObject</c>, which is case-sensitive, so the
    /// load must be too, or a step and the load would disagree on what a key
    /// is.</item>
    /// <item>Unknown properties are skipped (the serializer's default): a dropped
    /// field needs no step.</item>
    /// <item>A property the text lacks keeps what the type's constructor gave it
    /// (the serializer's default): an added field needs no step when that is a
    /// sound value.</item>
    /// <item>Public fields are saved as well as public properties: a public field
    /// left out silently would be progress lost at the next launch, the worst bug
    /// this feature can have.</item>
    /// <item>Numbers are numbers, strictly (the default): no number as a string,
    /// and no NaN or infinity, which can't be written, so a state holding one
    /// fails at <see cref="SaveSchema{T}.Save"/>, where a test sees it, instead of
    /// being written and then read back as a broken state. Doubles are written as
    /// their shortest round-trip text and read back to the same bits; whole numbers
    /// (an <c>Rng</c> state is a <c>uint</c>, a wallet a <c>long</c>) exactly.</item>
    /// <item>No comments, no trailing commas, a nesting depth of 64 (the defaults):
    /// a save is what the schema wrote, not a hand-edited file.</item>
    /// </list>
    /// </summary>
    public static JsonSerializerOptions Options { get; } = Create();

    /// <summary>How a payload is parsed into the tree the steps work on: names matched with case, as <see cref="Options"/>.</summary>
    public static JsonNodeOptions NodeOptions { get; } = new() { PropertyNameCaseInsensitive = false };

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = false,
            PropertyNameCaseInsensitive = false,
            IncludeFields = true,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        };
        // Frozen: a later change to the instance would throw, so the rules above
        // are the rules for the whole run.
        options.MakeReadOnly();
        return options;
    }
}
