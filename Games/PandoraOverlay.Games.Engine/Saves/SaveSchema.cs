using System.Text.Json;
using System.Text.Json.Nodes;

namespace PandoraOverlay.Games.Engine;

/// <summary>
/// A game's save as versioned JSON (A9): the state turned into text and back, with
/// one upgrade step per version, and three honest answers on a load:
/// <see cref="SaveOutcome.Ok"/>, <see cref="SaveOutcome.Newer"/>,
/// <see cref="SaveOutcome.Corrupt"/>. It exists because a save must survive every
/// later version of the overlay, and a save the code can't read must be reported,
/// never guessed at: losing a player's progress is the worst bug this feature can
/// have.
///
/// <para><b>Text only.</b> The schema touches no file: the host owns the disk, the
/// envelope round the text, the version it was written with, the backups and the
/// held saves (A7, A29). A game hands <see cref="Save"/>'s text and
/// <see cref="CurrentVersion"/> to <c>IGameStorage.Write</c>, and a
/// <c>SaveRead</c>'s version and payload to <see cref="Load"/>. A read with no
/// save to give carries version 0 (A34), which is <see cref="SaveOutcome.Corrupt"/>
/// here; a game asks the <c>SaveRead</c>'s state first and starts fresh on
/// <c>None</c> without calling <see cref="Load"/>.</para>
///
/// <para><b>When a step is needed: only when a meaning changes.</b> This is the rule
/// game authors work by. Unknown properties are skipped on a load, and a property
/// the text lacks keeps what the type's constructor gives it, so:</para>
/// <list type="bullet">
/// <item>A field ADDED with a sound default needs no step and no version: give it
/// the default in the type (a property initialiser, or a constructor parameter's
/// default). Mind that a reference type with no initialiser defaults to null, and a
/// constructor parameter can only default to null for one: give the type a real
/// default, or write a step.</item>
/// <item>A field DROPPED needs no step: its old value is skipped.</item>
/// <item>A rename, a change of unit or of type, a split or a merge, a value worked
/// out from others, a default that differs for old saves from new ones: each changes
/// what the old text MEANS, and needs a new version and a step from the one before.
/// A step is a function from version n's <see cref="JsonObject"/> to version
/// n + 1's; it may change the object it is given and return it.</item>
/// </list>
///
/// <para><b>What is saved.</b> The state's public properties and public fields, as
/// <typeparamref name="T"/> declares them (not a subclass's). A property read back
/// needs a public setter or <c>init</c>, or a matching constructor parameter: a
/// private setter is written but NOT read back, and the value is silently lost. A
/// round trip in the game's tests (<c>SaveFixtures.AssertRoundTrips</c>) is what
/// catches that. Numbers round-trip exactly: a <c>uint</c> (an <c>Rng</c>'s state),
/// a <c>long</c> (a wallet), a <c>double</c> to the same bits. NaN and infinity
/// can't be written: <see cref="Save"/> throws for them, so a broken number is
/// found when it is saved and never becomes a save that reads back broken. A null
/// for a number is refused, but a null for a string, a list or a record is not
/// (the reader does not hold reference types to their nullability): the game's own
/// check (<c>isValid</c>) is where a state that can't be played is refused. A
/// member whose type the reader can't build (a class whose constructor's
/// parameters match none of its properties, such as <c>Rng</c> itself: save its
/// <c>State</c> instead) is written fine and then fails every load as Corrupt; the
/// round trip finds that too.</para>
///
/// <para><b>Versions.</b> An int from 1. Every version that ever ships keeps a
/// fixture in the game's tests, mid-run saves included, and it must load in every
/// later build (<c>SaveFixtures.AssertAllLoad</c>); fixtures are added, never
/// edited. Until the owner releases a game its version stays 1, and a shape changed
/// during development is simply Corrupt (held, then set aside on the player's
/// click).</para>
///
/// <para>A schema is built once, with its steps, and then only read: it is not
/// meant to be changed while saves are being loaded. The loads and the saves run
/// on the UI thread, as everything of a game does.</para>
/// </summary>
/// <typeparam name="T">The game's save state.</typeparam>
public sealed class SaveSchema<T> where T : class
{
    private readonly Func<T, bool>? _isValid;
    private readonly Dictionary<int, Func<JsonObject, JsonObject>> _steps = new();

    /// <summary>
    /// A schema at <paramref name="currentVersion"/>, 1 or more. Every version below
    /// it needs its step (<see cref="Upgrade"/>) before the first load.
    /// </summary>
    /// <param name="currentVersion">The version this build writes.</param>
    /// <param name="isValid">
    /// The game's own check of a state just read, after every step: false (or a
    /// throw) makes the load <see cref="SaveOutcome.Corrupt"/>. For what the JSON
    /// can't say: a null list, a number out of its range, a run whose turn is
    /// negative. It is not called for a <see cref="SaveOutcome.Newer"/> save.
    /// </param>
    public SaveSchema(int currentVersion, Func<T, bool>? isValid = null)
    {
        if (currentVersion < 1)
            throw new ArgumentOutOfRangeException(nameof(currentVersion), currentVersion, "A save's version starts at 1 (A34).");
        CurrentVersion = currentVersion;
        _isValid = isValid;
    }

    /// <summary>The version this build writes and reads into.</summary>
    public int CurrentVersion { get; }

    /// <summary>
    /// Registers the step that turns version <paramref name="fromVersion"/>'s object
    /// into version <paramref name="fromVersion"/> + 1's, and returns the schema, for
    /// chaining. A version outside 1 to <see cref="CurrentVersion"/> - 1, a version
    /// given twice, or no step, throws: each can only be a mistake in the game's code.
    /// </summary>
    public SaveSchema<T> Upgrade(int fromVersion, Func<JsonObject, JsonObject> step)
    {
        ArgumentNullException.ThrowIfNull(step);
        if (fromVersion < 1 || fromVersion >= CurrentVersion)
            throw new ArgumentOutOfRangeException(
                nameof(fromVersion), fromVersion,
                CurrentVersion == 1
                    ? "A schema at version 1 has no upgrade step: there is no older version."
                    : $"An upgrade step is from a version of 1 to {CurrentVersion - 1}: the schema is at {CurrentVersion}.");
        if (!_steps.TryAdd(fromVersion, step))
            throw new ArgumentException($"There is already an upgrade step from version {fromVersion}.", nameof(fromVersion));
        return this;
    }

    /// <summary>
    /// The state in <paramref name="payload"/>, written at <paramref name="version"/>.
    /// Never throws for bad data:
    /// <list type="bullet">
    /// <item>above <see cref="CurrentVersion"/>: <see cref="SaveOutcome.Newer"/>, and
    /// the text is not even parsed (a later build wrote it, and it must be left alone,
    /// A7);</item>
    /// <item>a version under 1, no text, text that is not a JSON object, a step that
    /// throws or gives no object, a result that does not read as
    /// <typeparamref name="T"/> or reads as null, or <c>isValid</c> answering false or
    /// throwing: <see cref="SaveOutcome.Corrupt"/>;</item>
    /// <item>otherwise the steps from <paramref name="version"/> up run in order on the
    /// parsed object, the result is read as <typeparamref name="T"/>, and the answer is
    /// <see cref="SaveOutcome.Ok"/>, <see cref="SaveLoad{T}.Upgraded"/> when a step
    /// ran.</item>
    /// </list>
    /// A step missing for a version below the current one is a defect of the build,
    /// not bad data: <see cref="InvalidOperationException"/>, naming the version,
    /// before anything is parsed, whatever the version and the text (so the game's
    /// first test that loads anything finds it).
    /// </summary>
    public SaveLoad<T> Load(int version, string payload) => Read(version, payload).Result;

    /// <summary>
    /// <paramref name="state"/> as compact JSON, to hand to the host with
    /// <see cref="CurrentVersion"/>. A null state throws, and so does a state the
    /// rules can't write (a NaN, an infinity, a cycle): a defect of the game, found
    /// by its tests, which must never become a save that reads back broken.
    /// </summary>
    public string Save(T state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return JsonSerializer.Serialize(state, SaveJson.Options);
    }

    /// <summary>
    /// <see cref="Load"/>, and for a Corrupt answer what was wrong, in words, for the
    /// tests' messages (<c>SaveFixtures</c>). Internal on purpose: the words can hold
    /// an exception's message, and the host logs an exception's type only
    /// (constraint 4's log rule), so they never leave the tests.
    /// </summary>
    internal (SaveLoad<T> Result, string? Problem) Read(int version, string? payload)
    {
        RequireEveryStep();

        if (version > CurrentVersion)
            return (new SaveLoad<T>(SaveOutcome.Newer, null, false), null);
        if (version < 1)
            return Corrupt($"its version is {version}: a save's version starts at 1, and 0 is \"no version\" (A34)");
        if (payload is null)
            return Corrupt("it has no text");

        JsonObject tree;
        try
        {
            if (JsonNode.Parse(payload, SaveJson.NodeOptions) is not JsonObject parsed)
                return Corrupt("its text is not a JSON object");
            // The object reads its properties on first use; a name given twice
            // throws there. Touching it now makes that a parse failure, here,
            // rather than a surprise inside a game's step.
            _ = parsed.Count;
            tree = parsed;
        }
        catch (Exception e)
        {
            return Corrupt($"its text is not JSON ({e.GetType().Name}: {e.Message})");
        }

        for (int from = version; from < CurrentVersion; from++)
        {
            JsonObject? next;
            try
            {
                next = _steps[from](tree);
            }
            catch (Exception e)
            {
                return Corrupt($"the upgrade step from version {from} threw {e.GetType().Name}: {e.Message}");
            }
            if (next is null)
                return Corrupt($"the upgrade step from version {from} gave no object");
            tree = next;
        }

        T? state;
        try
        {
            state = tree.Deserialize<T>(SaveJson.Options);
        }
        catch (Exception e)
        {
            return Corrupt($"it does not read as {typeof(T).Name} ({e.GetType().Name}: {e.Message})");
        }
        if (state is null)
            return Corrupt($"it reads as no {typeof(T).Name}");

        try
        {
            if (_isValid is not null && !_isValid(state))
                return Corrupt("the game's own check refused the state");
        }
        catch (Exception e)
        {
            return Corrupt($"the game's own check threw {e.GetType().Name}: {e.Message}");
        }

        return (new SaveLoad<T>(SaveOutcome.Ok, state, version < CurrentVersion), null);
    }

    private static (SaveLoad<T>, string?) Corrupt(string problem) =>
        (new SaveLoad<T>(SaveOutcome.Corrupt, null, false), problem);

    /// <summary>
    /// Every version below the current one has its step, or the build is wrong. A
    /// count is enough: <see cref="Upgrade"/> takes only versions in range, each once.
    /// </summary>
    private void RequireEveryStep()
    {
        if (_steps.Count == CurrentVersion - 1) return;
        int missing = 1;
        while (_steps.ContainsKey(missing)) missing++;
        throw new InvalidOperationException(
            $"The save schema of {typeof(T).Name} is at version {CurrentVersion} and has no upgrade step from version {missing}: " +
            "every version below the current one needs one (A9). This is a defect of the build, not a bad save.");
    }
}
