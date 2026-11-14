using PandoraOverlay.Games.Engine;

namespace PandoraOverlay.Games.Hatchling;

/// <summary>
/// The one way a run changes (A20): every caller, the game object, the bot and the
/// tests, hands an action to <see cref="Apply"/> and gets back the state before, the
/// state after and the turn's events in the order things happened (H1, H2), or null
/// when the action is not allowed. The state given is never changed: the rule
/// works on a copy, so the game can commit and save the new state before the
/// playback shows it, and closing in the middle of a playback loses nothing.
///
/// <para>A turn is your move (<see cref="YourMove"/>), then the world's
/// (<see cref="World.Turn"/>, task H04), unless your move ended the run. What a move
/// is allowed to do, what it costs and what it makes is the design's table of moves;
/// the questions it asks are <see cref="Moves"/>' and the hit is
/// <see cref="Fights.YourHit"/>, so the screen and the bot ask exactly what the
/// rules decide by.</para>
/// </summary>
internal static class TurnRules
{
    /// <summary>
    /// What your move left for the rest of the turn: its <paramref name="Noise"/>
    /// after your build, the <paramref name="Drain"/> time will take at, and whether
    /// it <paramref name="Won"/> the run (the apex defeated), so no world's turn follows.
    /// </summary>
    internal readonly record struct Done(int Noise, double Drain, bool Won);

    /// <summary>
    /// The new state and the turn's events for <paramref name="action"/>, or null
    /// when it is not allowed. Never changes <paramref name="run"/> (A20).
    /// <list type="number">
    /// <item>A run that is over allows nothing. While an offer is open only Pick,
    /// Redraw and Leave are allowed (H14); a pick or a redraw passes no turn and adds
    /// no event, but a pick that opens the next owed offer adds
    /// <see cref="InstinctsOffered"/>.</item>
    /// <item>SetPace: the pace changes, no turn, no event.</item>
    /// <item>Leave: the run ends as left (H20), with no world's turn.</item>
    /// <item>Move, Eat, Drink, Rest, Attack: a turn. Your move, what is now in sight
    /// seen, then the world's turn with the move's noise and drain, unless the move
    /// ended the run (the apex defeated). The generator's state goes back into the
    /// new state. The turn list's rows (<see cref="RunState.LastTurn"/>) are not
    /// this rule's: the turn script makes them and the game stores them.</item>
    /// </list>
    /// </summary>
    public static TurnResult? Apply(RunState run, PlayerAction action)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(action);
        if (run.Over is not null) return null;

        if (run.Offer is not null)
        {
            return action switch
            {
                PlayerAction.Pick pick => Picked(run, Offers.Pick(run, pick.Index)),
                PlayerAction.Redraw => Redrawn(run, Offers.Redraw(run)),
                PlayerAction.Leave => Leave(run),
                _ => null,
            };
        }

        switch (action)
        {
            case PlayerAction.SetPace pace:
                return SetPace(run, pace.Pace);
            case PlayerAction.Leave:
                return Leave(run);
            case PlayerAction.Pick or PlayerAction.Redraw:
                return null;   // no offer is open: nothing to answer
        }

        var copy = run.Clone();
        var stats = Stats.Of(copy);   // your numbers before the move: growth only changes in the world's turn
        var rng = new Rng(copy.Rng);
        var events = new List<TurnEvent>();
        var done = YourMove(copy, stats, rng, action, events);
        if (done is null) return null;

        Sight.Reveal(copy, stats);
        if (done.Value.Won) World.End(copy, won: true, EndReason.DefeatedApex, by: null, events);
        else World.Turn(copy, rng, done.Value.Noise, done.Value.Drain, events);
        copy.Rng = rng.State;
        return new TurnResult(run, copy, events);
    }

    /// <summary>
    /// Your half of a turn: <paramref name="action"/> (a Move, Eat, Drink, Rest or
    /// Attack) done on <paramref name="run"/> ITSELF, its events added; null, and the
    /// run untouched, when it is not allowed. The kind of move is remembered (Braced
    /// and Freeze ask it) and the Latch streak broken by anything but a hit. It is
    /// <see cref="Apply"/>'s first half, called on Apply's own copy; the moves' tests
    /// call it to see a move apart from the world's turn that follows it.
    /// </summary>
    internal static Done? YourMove(RunState run, Stats stats, Rng rng, PlayerAction action, List<TurnEvent> events)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(stats);
        ArgumentNullException.ThrowIfNull(rng);
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(events);

        var (done, kind) = action switch
        {
            PlayerAction.Move move => (Move(run, stats, move.To, events), KindOf(run.Pace)),
            PlayerAction.Eat => (Eat(run, stats, events), MoveKind.Eat),
            PlayerAction.Drink => (Drink(run, stats, events), MoveKind.Drink),
            PlayerAction.Rest => (Rest(run, stats, events), MoveKind.Rest),
            PlayerAction.Attack attack => (Attack(run, stats, rng, attack.Creature, events), MoveKind.Attack),
            _ => ((Done?)null, MoveKind.None),
        };
        if (done is null) return null;

        run.You.LastMove = kind;
        if (kind != MoveKind.Attack)
        {
            // The streak is hits in a row, turn after turn: any other move breaks it.
            // A hit sets it itself (Fights.YourHit, step 6).
            run.You.LatchOn = 0;
            run.You.LatchStreak = 0;
        }
        return done;
    }

    // ---- No turn ---------------------------------------------------------------------

    /// <summary>A pick taken: no turn; the next owed offer, if it opened, is the one event.</summary>
    private static TurnResult? Picked(RunState run, RunState? next)
    {
        if (next is null) return null;
        var events = new List<TurnEvent>();
        if (next.Offer is not null)
            events.Add(new InstinctsOffered(next.Offer.ToList()));   // a copy: an event never shares the state's list
        return new TurnResult(run, next, events);
    }

    /// <summary>A redraw: no turn, no event; the new offer is shown in the old one's place.</summary>
    private static TurnResult? Redrawn(RunState run, RunState? next) =>
        next is null ? null : new TurnResult(run, next, Array.Empty<TurnEvent>());

    /// <summary>The pace is part of the run and stays as chosen; choosing it is not a move.</summary>
    private static TurnResult SetPace(RunState run, Pace pace)
    {
        var copy = run.Clone();
        copy.Pace = pace;
        return new TurnResult(run, copy, Array.Empty<TurnEvent>());
    }

    /// <summary>The player's armed click (H20): the run ends as left, paying what was reached; no world's turn.</summary>
    private static TurnResult Leave(RunState run)
    {
        var copy = run.Clone();
        var events = new List<TurnEvent>();
        World.End(copy, won: false, EndReason.Left, by: null, events);
        return new TurnResult(run, copy, events);
    }

    // ---- Your moves ------------------------------------------------------------------

    /// <summary>The kind of move a move at <paramref name="pace"/> is.</summary>
    private static MoveKind KindOf(Pace pace) => pace switch
    {
        Pace.Sneak => MoveKind.Sneak,
        Pace.Sprint => MoveKind.Sprint,
        _ => MoveKind.Walk,
    };

    /// <summary>
    /// Walk, sneak or sprint (the run's pace) to <paramref name="to"/>, which must be
    /// one of <see cref="Moves.Targets"/>. Walk: noise 3, plain drain; sneak: noise 1,
    /// drain x1.5; sprint: noise 5, your sprint's drain. A walk or a sneak into water
    /// you wade costs one more noise and half again the drain (never for a Wader,
    /// <see cref="Sight.WouldWade"/>).
    /// </summary>
    private static Done? Move(RunState run, Stats stats, int to, List<TurnEvent> events)
    {
        if (!Moves.Targets(run, stats).Contains(to)) return null;

        var from = run.You.At;
        var path = Moves.PathTo(run, stats, to);
        var (noise, drain) = run.Pace switch
        {
            Pace.Sneak => (Tuning.SneakNoise, Tuning.SneakDrain),
            Pace.Sprint => (Tuning.SprintNoise, stats.SprintDrain),
            _ => (Tuning.WalkNoise, Tuning.PlainDrain),
        };
        // A sprint never wades: it enters water only as a swimmer or a flyer, who don't.
        var waded = run.Pace != Pace.Sprint && Sight.WouldWade(run, stats, to);
        if (waded)
        {
            noise += Tuning.WadeNoise;
            drain *= Tuning.WadeDrain;
        }

        run.You.At = to;
        noise = Moves.NoiseOf(stats, noise);
        events.Add(new Walked(run.Pace, from, path, waded, noise));
        return new Done(noise, drain, Won: false);
    }

    /// <summary>
    /// Eat what <see cref="Moves.FoodHere"/> finds, while food is under its most: the
    /// meal's worth (<see cref="Moves.MealGain"/>), food held at its most; a plant is
    /// gone, a carcass loses a meal, fish is the water and stays. The event says what
    /// went into the bar (<see cref="Moves.EatGain"/>, asked before it changes, H49),
    /// as the Eat button did. Noise 1, fish 2.
    /// </summary>
    private static Done? Eat(RunState run, Stats stats, List<TurnEvent> events)
    {
        var food = Moves.FoodHere(run, stats);
        if (food is null || run.You.Food >= Tuning.FoodMost) return null;

        var kind = food.Value;
        var gain = Moves.EatGain(run, stats);
        run.You.Food = Math.Min(Tuning.FoodMost, run.You.Food + Moves.MealGain(stats, kind));
        var at = run.You.At;
        if (kind == Food.Plants) run.Plants[at] = false;
        if (kind == Food.Meat) run.Meat[at] -= 1;

        var noise = Moves.NoiseOf(stats, kind == Food.Fish ? Tuning.FishNoise : Tuning.EatNoise);
        events.Add(new Ate(kind, gain, noise));
        return new Done(noise, Tuning.PlainDrain, Won: false);
    }

    /// <summary>
    /// Drink, on water or beside it, while water is under its most: +40, held at its
    /// most; the event says what went in (<see cref="Moves.DrinkGain"/>, H49). Noise 1.
    /// </summary>
    private static Done? Drink(RunState run, Stats stats, List<TurnEvent> events)
    {
        if (!Moves.WaterNear(run) || run.You.Water >= Tuning.WaterMost) return null;

        var gain = Moves.DrinkGain(run);
        run.You.Water = Math.Min(Tuning.WaterMost, run.You.Water + Tuning.DrinkWater);
        var noise = Moves.NoiseOf(stats, Tuning.DrinkNoise);
        events.Add(new Drank(gain, noise));
        return new Done(noise, Tuning.PlainDrain, Won: false);
    }

    /// <summary>
    /// Rest, always allowed: health up by your rest's share (Fast healer's 16%, else
    /// 8%), to full at most; the event says what was regained
    /// (<see cref="Moves.RestGain"/>, H49), 0 at full health. Noise 0.
    /// </summary>
    private static Done Rest(RunState run, Stats stats, List<TurnEvent> events)
    {
        var healed = Moves.RestGain(run, stats);
        run.You.Health = Math.Min(1, run.You.Health + stats.RestHeal);
        var noise = Moves.NoiseOf(stats, Tuning.RestNoise);
        events.Add(new Rested(healed, noise));
        return new Done(noise, Tuning.PlainDrain, Won: false);
    }

    /// <summary>
    /// Hit the creature whose id is <paramref name="creature"/>, which must be one of
    /// <see cref="Moves.Attackable"/>: <see cref="Fights.YourHit"/>, which also sets
    /// the Latch streak. Noise 4. The apex's fall wins the run.
    /// </summary>
    private static Done? Attack(RunState run, Stats stats, Rng rng, int creature, List<TurnEvent> events)
    {
        var target = Moves.Attackable(run, stats).FirstOrDefault(c => c.Id == creature);
        if (target is null) return null;

        var won = Fights.YourHit(run, stats, rng, target, events);
        return new Done(Moves.NoiseOf(stats, Tuning.AttackNoise), Tuning.PlainDrain, won);
    }
}
