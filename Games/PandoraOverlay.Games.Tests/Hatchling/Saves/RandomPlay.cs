using PandoraOverlay.Games.Engine;
using PandoraOverlay.Games.Hatchling;

namespace PandoraOverlay.Games.Tests;

/// <summary>
/// Careers played by a seeded random player through the game's own steps (task H08): the
/// rules, server play's seen steps, the lineage, the unlocks, the journal, Camp, and
/// <see cref="HatchlingGame.Advance"/> and <see cref="HatchlingGame.Started"/>, the two
/// pure steps the game object commits. Nothing here is a rule of its own: it only
/// chooses, with the engine's <see cref="Rng"/>, among what the game allows.
///
/// <para>So the states it makes are states the game makes, which is what the round
/// trip must be tested on: a save that reads back every hand-built sample but not
/// the states real play leaves would fail a real player. Two runs from equal states
/// with dice of one seed make equal choices, so <see cref="Step"/> is also how "the
/// generator restored rolls the same" is tested.</para>
/// </summary>
internal static class RandomPlay
{
    /// <summary>What a career is made to show, by its index: the round trip covers runs in every phase.</summary>
    public enum Phase { None, TurnZero, MidRun, OfferOpen, Afflicted, Apex, Over }

    /// <summary>The species an afflicting career plays, one for each perk that afflicts: Headbutt's daze, Venom's slow, Bleeder's bleed.</summary>
    private static readonly string[] Afflicters = { "Pachycephalosaurus", "Troodon", "Allosaurus" };

    /// <summary>How many sets of dice a career is played on before it gives up on its phase.</summary>
    private const int Attempts = 25;

    /// <summary>
    /// Career <paramref name="index"/>: a fresh save at its own moment (so its own
    /// seeds, H25), a stretch of server play and purchases, then a run played toward
    /// <see cref="PhaseOf"/>(index).
    /// <para><b>The phase whatever the balance (H74).</b> The save's tests must not lean
    /// on the balance: with the tuned threats of task H09 no random career lived to the
    /// apex, and a test failed that had nothing to do with saving. So a career whose run
    /// ended before it reached its phase is played again on other dice (the first set
    /// is the one it always had), up to <see cref="Attempts"/> times; and the two phases
    /// a random player reaches only by growing, an offer open and the apex about, are
    /// brought about at the run's start, before any play: its growth set just under the
    /// line that makes them (Juvenile's for the offer, Elder's for the apex), fed,
    /// watered and whole (<see cref="Nudged"/>). From there everything is the rules':
    /// the line crossed, the offer drawn, the apex's arrival.</para>
    /// <para>After every attempt the career may still miss its phase (the test then
    /// names the phase no career reached): that would take a balance in which no set of
    /// twenty-five dice lives through a few turns.</para>
    /// </summary>
    public static GameState Career(int index)
    {
        var aim = PhaseOf(index);
        GameState? state = null;
        for (var attempt = 0u; attempt < Attempts; attempt++)
        {
            state = Attempt(index, attempt);
            if (PhasesOf(state).Contains(aim)) return state;
        }
        return state!;
    }

    /// <summary>One play of career <paramref name="index"/> on its <paramref name="attempt"/>-th set of dice (the 0th is the career's own).</summary>
    private static GameState Attempt(int index, uint attempt)
    {
        var dice = new Rng(Rng.Mix((uint)index, 0xB0A7u + attempt));
        var state = GameState.Fresh(SaveSamples.Moment.AddMinutes(37 * index));
        for (var k = 0; k < 40; k++) state = ServerStep(state, dice);
        for (var k = 0; k < 8; k++)
        {
            var trait = Lineage.All[dice.Next(Lineage.All.Count)].Trait;
            state = LineageRules.Buy(state, trait) ?? state;
        }

        var phase = PhaseOf(index);
        var species = phase == Phase.Afflicted
            ? Afflicters[index / 7 % Afflicters.Length]
            : state.Unlocked[dice.Next(state.Unlocked.Count)];
        if (!Unlocks.IsUnlocked(state, species))
        {
            state = state.Clone();
            Unlocks.Grant(state, species);
        }
        state = Camp.Select(state, species);
        state = Camp.SetDanger(state, 1 + dice.Next(state.MaxDanger)) ?? state;

        if (phase == Phase.None)
        {
            // A career on Camp, with a run behind it.
            state = PlayUntil(Started(state), dice, s => s.Run?.Over is not null, 400);
            return Settle.BackToCamp(state);
        }

        state = Started(state);
        return phase switch
        {
            Phase.TurnZero => state,
            Phase.MidRun => PlayUntil(state, dice, _ => false, 5 + dice.Next(55)),
            Phase.OfferOpen => PlayUntil(Nudged(state, JustUnderJuvenile), dice, s => s.Run?.Offer is not null && s.Run.Over is null, 300),
            Phase.Afflicted => PlayUntil(state, dice, s => Afflicted(s) || s.Run?.Over is not null, 400, aggressive: true),
            Phase.Apex => PlayUntil(Nudged(state, JustUnderElder), dice, s => s.Run!.ApexAt != Tuning.ApexNotYet, 40),
            _ => PlayUntil(state, dice, s => s.Run?.Over is not null, 600),
        };
    }

    /// <summary>One point under the Juvenile line (a turn's growing is 1.4 or more): the next growing turn crosses it and owes an offer (H14).</summary>
    private static readonly double JustUnderJuvenile = Tuning.StageLines[0] - 1;

    /// <summary>One point under full growth: the next growing turn reaches Elder, and the apex comes.</summary>
    private static readonly double JustUnderElder = Tuning.StageLines[^1] - 1;

    /// <summary>
    /// A run at its start brought about (H74): grown to <paramref name="growth"/>
    /// (never below what it is), fed, watered and whole, in a copy. A line it was set
    /// past owes no pick: the rules owe one only for a line a turn crosses.
    /// </summary>
    private static GameState Nudged(GameState state, double growth)
    {
        var copy = state.Clone();
        var you = copy.Run!.You;
        you.Growth = Math.Max(you.Growth, growth);
        you.Best = Math.Max(you.Best, you.Growth);
        you.Food = 90;
        you.Water = 90;
        you.Health = 1;
        return copy;
    }

    /// <summary>The phase career <paramref name="index"/> aims at.</summary>
    public static Phase PhaseOf(int index) => (Phase)(index % 7);

    /// <summary>The phases a state is in (a state can be in several: an offer open with the apex about).</summary>
    public static IEnumerable<Phase> PhasesOf(GameState state)
    {
        var run = state.Run;
        if (run is null)
        {
            yield return Phase.None;
            yield break;
        }
        if (run.Over is not null)
        {
            if (run.Over.Settled) yield return Phase.Over;
            yield break;
        }
        yield return run.Turn == 0 ? Phase.TurnZero : Phase.MidRun;
        if (run.Offer is not null) yield return Phase.OfferOpen;
        if (Afflicted(state)) yield return Phase.Afflicted;
        if (run.ApexAt != Tuning.ApexNotYet) yield return Phase.Apex;
    }

    /// <summary>
    /// One random move of a whole game, as a player could make it: in a run under way
    /// mostly an action of the run, sometimes a seen step or a purchase; with a run
    /// ended, back to camp or a seen step; on Camp, a start, a seen step, a purchase,
    /// an unlock, a swap or a choice. Pure: the state given is never changed.
    /// </summary>
    public static GameState Step(GameState state, Rng dice)
    {
        var roll = dice.Next(100);
        var run = state.Run;
        if (run is { Over: null })
        {
            if (roll < 85) return RunStep(state, dice, aggressive: false);
            if (roll < 95) return ServerStep(state, dice);
            return LineageRules.Buy(state, Lineage.All[dice.Next(Lineage.All.Count)].Trait) ?? state;
        }
        if (run is not null) return roll < 60 ? Settle.BackToCamp(state) : ServerStep(state, dice);

        if (roll < 35) return HatchlingGame.Started(state) ?? state;
        if (roll < 60) return ServerStep(state, dice);
        if (roll < 75) return LineageRules.Buy(state, Lineage.All[dice.Next(Lineage.All.Count)].Trait) ?? state;
        if (roll < 82) return Unlocks.Buy(state, SpeciesTable.All[dice.Next(SpeciesTable.All.Count)].Name) ?? state;
        if (roll < 87) return Journal.Swap(state, dice.Next(Tuning.JournalEntries)) ?? state;
        if (roll < 94) return Camp.Select(state, state.Unlocked[dice.Next(state.Unlocked.Count)]);
        return Camp.SetDanger(state, 1 + dice.Next(Dangers.Highest)) ?? state;
    }

    /// <summary>
    /// One seen step of server play (H10): a species of the table, a rise of 0.1% to
    /// 0.5% from anywhere in a life, at a moment a little after freshness was last
    /// brought up to date (so freshness comes back between steps, H18).
    /// </summary>
    public static GameState ServerStep(GameState state, Rng dice)
    {
        var species = SpeciesTable.All[dice.Next(SpeciesTable.All.Count)].Name;
        var from = dice.Next(995) / 1000.0;
        var to = from + (1 + dice.Next(5)) / 1000.0;
        var moment = state.FreshAsOf.AddMinutes(1 + dice.Next(600));
        return ServerPlay.Apply(state, new SeenStep(species, "Female", from, to), moment).State;
    }

    /// <summary>A started run (the career's species is unlocked and chosen, so Camp always lets it start).</summary>
    private static GameState Started(GameState state) =>
        HatchlingGame.Started(state) ?? throw new InvalidOperationException("The career could not start its run.");

    /// <summary>Run steps until <paramref name="done"/> holds, the run ends, or <paramref name="most"/> steps were taken.</summary>
    private static GameState PlayUntil(GameState state, Rng dice, Func<GameState, bool> done, int most, bool aggressive = false)
    {
        for (var k = 0; k < most && !done(state) && state.Run is { Over: null }; k++)
            state = RunStep(state, dice, aggressive);
        return state;
    }

    /// <summary>
    /// A predator on the island carries a slow or a bleed, or a daze. A daze is spent at
    /// the dazed one's next action, which is in the same turn's world phase, so a run
    /// under way as it is saved shows it only by its lasting trace, the turn from which
    /// it can be dazed again (<c>DazeFreeAt</c>); <c>Dazed</c> itself is seen only in a
    /// run that ended before that action.
    /// </summary>
    private static bool Afflicted(GameState state) =>
        state.Run?.Creatures.Any(c => c.Dazed || c.DazeFreeAt > 0 || c.Slowed > 0 || c.Bleed > 0) == true;

    /// <summary>One action of the run, chosen and taken through the game's own step; a refused one leaves the state as it was.</summary>
    private static GameState RunStep(GameState state, Rng dice, bool aggressive)
    {
        var action = Choose(state.Run!, dice, aggressive);
        return HatchlingGame.Advance(state, action)?.State ?? state;
    }

    /// <summary>
    /// A player's choice. With an offer open: a pick, or now and then a redraw. Else a
    /// careful player: rest when hurt, eat and drink when low, often attack what is
    /// beside it, else move; the aggressive one goes for the nearest predator it knows.
    /// Now and then a pace. Only what the board offers (<see cref="Moves"/>, <see cref="Sight.Knows"/>).
    /// </summary>
    private static PlayerAction Choose(RunState run, Rng dice, bool aggressive)
    {
        if (run.Offer is not null)
            return dice.Chance(0.15) ? new PlayerAction.Redraw() : new PlayerAction.Pick(dice.Next(run.Offer.Count));

        var stats = Stats.Of(run);
        if (dice.Chance(0.05)) return new PlayerAction.SetPace((Pace)dice.Next(3));
        if (run.You.Health < 0.45 && !aggressive) return new PlayerAction.Rest();
        if (run.You.Food < 55 && Moves.EatGain(run, stats) > 0) return new PlayerAction.Eat();
        if (run.You.Water < 55 && Moves.DrinkGain(run) > 0) return new PlayerAction.Drink();

        var attackable = Moves.Attackable(run, stats);
        if (attackable.Count > 0 && (aggressive || dice.Chance(0.5)))
            return new PlayerAction.Attack(attackable[dice.Next(attackable.Count)].Id);

        var targets = Moves.Targets(run, stats);
        if (targets.Count == 0 || dice.Chance(0.15)) return new PlayerAction.Rest();
        if (aggressive)
        {
            var known = run.Creatures.Where(c => c.Kind == CreatureKind.Predator && Sight.Knows(run, stats, c)).ToList();
            if (known.Count > 0)
            {
                var nearest = known.MinBy(c => Island.Grid.Distance(run.You.At, c.At))!;
                return new PlayerAction.Move(targets.MinBy(cell => Island.Grid.Distance(cell, nearest.At)));
            }
        }
        return new PlayerAction.Move(targets[dice.Next(targets.Count)]);
    }
}
