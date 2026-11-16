using PandoraOverlay.Games.Engine;

namespace PandoraOverlay.Games.Hatchling;

/// <summary>
/// The world's turn, after your move, and the end of a run (H1). This is the half
/// of a turn the owner could not follow in the prototype (G9), so it resolves in
/// ONE fixed order that never reshuffles, and every creature's action leaves events
/// that say exactly what it did, in the order it happened (H2): the turn script
/// makes the playback and the turn list from them and from nothing else.
///
/// <para>The order, A to F, each roll where the design takes it (the same seed and
/// the same moves give the same run for ever):</para>
/// <list type="bullet">
/// <item>A. One more turn; your numbers and whether you are hidden are taken now,
/// ONCE, and every predator's sight and bite read the same.</item>
/// <item>B. Each predator, in id order over the list as it stood at A (the order
/// they arrived in, H1): its mind (sees you, hears you), its steps or its wander,
/// its bite when it ends beside you, its bleeding.</item>
/// <item>C. Each small game, in id order: flee or roam (they move together on
/// screen, H4, but resolve one by one).</item>
/// <item>D. Time: hunger and thirst, healing, growth and the lines it crosses.</item>
/// <item>E. Arrivals: a predator, small game, plants out of sight, the apex at
/// Elder, and the apex's wait (outlasted, H15's turns).</item>
/// <item>F. What is in sight is seen; an owed offer of instincts opens (H14).</item>
/// </list>
/// It stops at once when the run ends: caught in B, out of food or water in D, the
/// apex outlasted in E, or the apex defeated by your spikes or its own bleeding in B.
///
/// <para>Every member changes the run it is given: it is called on
/// <see cref="TurnRules.Apply"/>'s own copy, never on a state a caller still holds
/// (A20). The generator is the caller's, which writes it back.</para>
/// </summary>
internal static class World
{
    /// <summary>
    /// The world's turn: A to F, in that order, taking every roll where the design
    /// says. <paramref name="noise"/> is your move's after your build (0 hears
    /// nothing); <paramref name="drain"/> is your move's factor on the food and water
    /// a turn costs. Stops when the run ends. A run already over is left as it is.
    /// </summary>
    public static void Turn(RunState run, Rng rng, int noise, double drain, List<TurnEvent> events)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(rng);
        ArgumentNullException.ThrowIfNull(events);
        if (run.Over is not null) return;

        // A. Your numbers and your cover, once: a predator that comes beside you
        // later in B, or growth in D, changes nothing about what B's predators saw.
        run.Turn += 1;
        var stats = Stats.Of(run);
        var hidden = Sight.Hidden(run, stats);

        // B. The predators as they stood at A: arrivals come in E, after them, and
        // act from the next turn.
        var predators = run.Creatures.Where(c => c.Kind == CreatureKind.Predator).Select(c => c.Id).ToList();
        foreach (var id in predators)
        {
            // One defeated on the way is gone and does not act: only its own bite
            // (your spikes) or its own bleeding can defeat one in B, so this is a
            // guard rather than a case; its Hurt already said Defeated.
            var predator = run.Creatures.Find(c => c.Id == id);
            if (predator is null) continue;
            Act(run, stats, hidden, rng, noise, predator, events);
            if (run.Over is not null) return;
        }

        // C. Small game, as it stood after the predators.
        var game = run.Creatures.Where(c => c.Kind == CreatureKind.SmallGame).Select(c => c.Id).ToList();
        foreach (var id in game)
        {
            var creature = run.Creatures.Find(c => c.Id == id);
            if (creature is not null) Roam(run, rng, creature, events);
        }

        // D. Time.
        Time(run, stats, drain, events);
        if (run.Over is not null) return;

        // E. Arrivals, and the apex's wait.
        Arrivals(run, stats, rng, events);
        if (run.Over is not null) return;

        // F. What is in sight is seen (you did not move since the move's own
        // reveal, so this only says the rule), and an owed offer opens.
        Sight.Reveal(run, stats);
        if (run.Offer is null && run.PicksOwed > 0)
        {
            Offers.OpenIfOwed(run, rng);
            if (run.Offer is not null)
                events.Add(new InstinctsOffered(run.Offer.ToList()));   // a copy: an event never shares the state's list
        }
    }

    /// <summary>
    /// Ends the run: fills <see cref="RunState.Over"/> with how it ended, your stage,
    /// and <see cref="RunPay"/>'s lines and their total, and adds
    /// <see cref="RunEnded"/>, the last event of the run. <paramref name="by"/> is the
    /// predator that caught you, else null: its id goes in the event, its species
    /// and ordinal in <see cref="RunOver"/>, so the summary can name it after it is
    /// gone from the island (your spikes can defeat the one that bites you).
    /// Settling (banking the pay, the records) is the game's, in the same step (H20).
    /// </summary>
    public static void End(RunState run, bool won, EndReason reason, Creature? by, List<TurnEvent> events)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(events);
        var over = new RunOver
        {
            Won = won,
            Reason = reason,
            ByType = by?.Type ?? default,
            ByOrdinal = by?.Ordinal ?? 0,
            Stage = Stages.Of(run.You.Growth),
        };
        // The pay reads how the run ended (the win's line), so Over is in place first.
        run.Over = over;
        var lines = RunPay.Of(run);
        over.Lines = lines.ToList();
        over.Total = RunPay.Total(lines);
        events.Add(new RunEnded(won, reason, by?.Id ?? 0));
    }

    /// <summary>
    /// The most predators, the apex not counted, while you are at
    /// <paramref name="stage"/> in <paramref name="run"/>'s danger (H15): an arrival
    /// comes only while there are fewer.
    /// </summary>
    public static int MostPredators(RunState run, int stage)
    {
        ArgumentNullException.ThrowIfNull(run);
        return Dangers.Of(run.Danger).Most(stage);
    }

    /// <summary>
    /// The turns the apex must be outlasted, counted from the turn it came: the
    /// danger's (15 to 21), less <see cref="Tuning.EldersPatienceTurns"/> with Elder's
    /// patience (H31), which pays nothing extra.
    /// </summary>
    public static int OutlastTurns(RunState run)
    {
        ArgumentNullException.ThrowIfNull(run);
        var turns = Dangers.Of(run.Danger).Outlast;
        return run.Rank(Trait.EldersPatience) > 0 ? turns - Tuning.EldersPatienceTurns : turns;
    }

    // ---- B. A predator's action ---------------------------------------------------------

    /// <summary>
    /// One predator's action, B1 to B7. Its events stand in the order things happen
    /// (H2): what it noticed (spotted or heard), its one <see cref="Stepped"/> or
    /// <see cref="Stayed"/>, a lost trail (after the steps), its bite and what the bite
    /// brought (Second wind, your spikes), its bleeding.
    /// </summary>
    private static void Act(RunState run, Stats stats, bool hidden, Rng rng, int noise, Creature predator, List<TurnEvent> events)
    {
        // B1. Dazed: the action is lost, the daze is over. It does not look, step or
        // bite (so what it saw last stays what it saw), but it still bleeds.
        if (predator.Dazed)
        {
            predator.Dazed = false;
            events.Add(new Stayed(predator.Id, Idle.Dazed));
            Bleed(run, predator, events);
            return;
        }

        var info = Predators.Of(predator.Type);
        var you = run.You.At;
        var apex = Fights.IsApex(predator);

        // B2. It sees you within its own sight, unless you are hidden; hidden or
        // not, nothing beside you goes unseen. "Spots you" is news only when it did
        // not see you on its last action: the apex counts as seeing, so after its
        // first action it never spots you again (it never lost you).
        var distance = Island.Grid.Distance(predator.At, you);
        var sees = distance <= info.Sight && (!hidden || distance <= 1);
        var sawYou = predator.SawYou;
        predator.SawYou = sees || apex;

        // B3. Seen (or the apex): it hunts your cell. Else heard: it searches for
        // the cell it heard you on, news only when it was unaware.
        if (apex || sees)
        {
            if (sees && !sawYou) events.Add(new Noticed(predator.Id, Notice.Spotted));
            predator.Target = you;
            predator.Alert = Tuning.AlertSeen;
        }
        else if (noise > 0 && distance <= noise)
        {
            if (predator.Alert == 0) events.Add(new Noticed(predator.Id, Notice.Heard));
            predator.Target = you;
            predator.Alert = Tuning.AlertHeard;
        }

        if (predator.Alert > 0) Hunt(run, predator, info, you, sees || apex, events);
        else Wander(run, rng, predator, you, events);

        // B6. A slow wears off an action at a time; then, beside you, it bites.
        if (predator.Slowed > 0) predator.Slowed--;
        if (Island.Grid.Distance(predator.At, you) == 1)
        {
            Fights.Bite(run, stats, rng, predator, events);
            if (run.Over is not null) return;                                   // caught, or the apex fell to your spikes
            if (run.Creatures.TrueForAll(c => c.Id != predator.Id)) return;     // your spikes defeated it
        }

        // B7.
        Bleed(run, predator, events);
    }

    /// <summary>
    /// B4: an alerted predator walks to its target, two steps when fast, one less
    /// when slowed. Before each step it stops if it is beside you; each step is the
    /// first of a shortest walk (<see cref="HexGrid.FirstStepToward"/>) to its target
    /// or, when the target is your cell, to any open cell beside you (a goal must
    /// itself be open, A40: your cell never is); a step onto water ends the walk.
    /// Then, unless it sees you, is the apex or now stands beside you, its alert
    /// falls by one: at 0 it has lost your trail. One beside you has found you and
    /// bites in B6 (H43), so a lost trail and a bite are never in one action.
    /// </summary>
    private static void Hunt(RunState run, Creature predator, PredatorInfo info, int you, bool hunting, List<TurnEvent> events)
    {
        var steps = (info.Fast ? Tuning.FastSteps : Tuning.SlowSteps) - (predator.Slowed > 0 ? 1 : 0);
        var from = predator.At;
        var path = new List<int>(Tuning.FastSteps);
        var target = predator.Target;
        for (var step = 0; step < steps; step++)
        {
            if (Island.Grid.Distance(predator.At, you) <= 1) break;
            var next = Island.Grid.FirstStepToward(predator.At,
                cell => cell == target || (target == you && Island.Grid.Distance(cell, you) == 1),
                cell => OpenToPredator(run, cell));
            if (next < 0) break;
            predator.At = next;
            path.Add(next);
            if (run.Tiles[next] == Terrain.Water) break;   // water slows a hunter to a stop
        }

        if (path.Count > 0) events.Add(new Stepped(predator.Id, from, path, hunting ? Gait.Hunt : Gait.Search));
        // Held is the slow's doing: its steps came to none before it could look for a
        // way. Any other stop (beside you already, no way, at its target) waits.
        else events.Add(new Stayed(predator.Id, steps <= 0 ? Idle.Held : Idle.Waits));

        if (!hunting && Island.Grid.Distance(predator.At, you) > 1)
        {
            predator.Alert--;
            if (predator.Alert == 0) events.Add(new Noticed(predator.Id, Notice.LostTrail));
        }
    }

    /// <summary>
    /// B5: an unaware predator rolls; under <see cref="Tuning.Wander"/> it goes to one
    /// of its open land neighbours (picked by a second roll, taken only when there is
    /// one), else it waits.
    /// </summary>
    private static void Wander(RunState run, Rng rng, Creature predator, int you, List<TurnEvent> events)
    {
        if (rng.Chance(Tuning.Wander))
        {
            var near = Near(run, predator.At, you);
            if (near.Count > 0)
            {
                var from = predator.At;
                predator.At = near[rng.Next(near.Count)];
                events.Add(new Stepped(predator.Id, from, new[] { predator.At }, Gait.Wander));
                return;
            }
        }
        events.Add(new Stayed(predator.Id, Idle.Waits));
    }

    /// <summary>
    /// B7: a bleeding predator loses its bleed's damage and one action of the bleed.
    /// At 0 health it is defeated (<see cref="Fights.Defeat"/>: its meat on its cell,
    /// a kill unless it is the apex); the apex bled out ends the run won, as its fall
    /// to your hit does (the apex can never be outlasted once gone).
    /// </summary>
    private static void Bleed(RunState run, Creature predator, List<TurnEvent> events)
    {
        if (predator.Bleed <= 0) return;
        predator.Health -= predator.BleedDamage;
        predator.Bleed--;
        var fell = predator.Health <= 0;
        events.Add(new Hurt(predator.Id, predator.At, predator.BleedDamage, HurtBy.Bleed, fell));
        if (!fell) return;
        Fights.Defeat(run, predator);
        if (Fights.IsApex(predator)) End(run, won: true, EndReason.DefeatedApex, by: null, events);
    }

    /// <summary>A predator may step on <paramref name="cell"/>: not rock, not your cell, no creature on it. Water is open (it stops a walk).</summary>
    private static bool OpenToPredator(RunState run, int cell) =>
        run.Tiles[cell] != Terrain.Rock && cell != run.You.At && Island.CreatureAt(run, cell) is null;

    /// <summary>The land neighbours of <paramref name="cell"/> that are not your cell and hold no creature, in neighbour order: where a creature may wander, roam or flee.</summary>
    private static List<int> Near(RunState run, int cell, int you)
    {
        var near = new List<int>(6);
        foreach (var next in Island.Grid.Neighbours(cell))
            if (Island.IsLand(run, next) && next != you && Island.CreatureAt(run, next) is null) near.Add(next);
        return near;
    }

    // ---- C. Small game ------------------------------------------------------------------

    /// <summary>
    /// C: small game boxed in does nothing and rolls nothing. Within
    /// <see cref="Tuning.FleeWithin"/> of you it rolls to flee, to the neighbour
    /// furthest from you (a tie: the earlier in neighbour order), and is done.
    /// Otherwise (further, or the flee roll failed) it rolls to roam, and only then
    /// picks a neighbour. It takes no affliction (H51), so nothing else is read.
    /// </summary>
    private static void Roam(RunState run, Rng rng, Creature game, List<TurnEvent> events)
    {
        var you = run.You.At;
        var near = Near(run, game.At, you);
        if (near.Count == 0) return;

        var from = game.At;
        if (Island.Grid.Distance(game.At, you) <= Tuning.FleeWithin && rng.Chance(Tuning.Flee))
        {
            var furthest = near[0];
            foreach (var cell in near)
                if (Island.Grid.Distance(cell, you) > Island.Grid.Distance(furthest, you)) furthest = cell;
            game.At = furthest;
            events.Add(new Stepped(game.Id, from, new[] { furthest }, Gait.Flee));
            return;
        }
        if (rng.Chance(Tuning.Roam))
        {
            game.At = near[rng.Next(near.Count)];
            events.Add(new Stepped(game.Id, from, new[] { game.At }, Gait.Roam));
        }
    }

    // ---- D. Time ------------------------------------------------------------------------

    /// <summary>
    /// D: food and water drain at your move's factor (never below 0); an empty one
    /// costs <see cref="Tuning.Starve"/> of health each; both above
    /// <see cref="Tuning.RegainAbove"/> heal <see cref="Tuning.Regain"/>. Exactly one
    /// <see cref="TimePassed"/> says it (all zeros when nothing changed: the Time
    /// passes beat hangs on it), its Regained being what went into the bar (as H49
    /// counts a rest). At 0 health: Second wind, or the run ends out of food (food
    /// is 0) or out of water. Then growth, fed and watered, rises by your rate, with
    /// <see cref="Grew"/>, and each line crossed has its <see cref="StageReached"/>,
    /// owes a pick (Juvenile, Subadult, Adult; Elder with Late bloomer, H14), and
    /// Adult crossed unbitten is remembered for good (H46).
    /// </summary>
    private static void Time(RunState run, Stats stats, double drain, List<TurnEvent> events)
    {
        var you = run.You;
        you.Food = Math.Max(0, you.Food - stats.FoodDrain * drain);
        you.Water = Math.Max(0, you.Water - stats.WaterDrain * drain);

        var lost = (int)Tuning.Round(Tuning.Starve * 100);
        var hunger = 0;
        var thirst = 0;
        var regained = 0;
        if (you.Food <= 0)
        {
            you.Health -= Tuning.Starve;
            hunger = lost;
        }
        if (you.Water <= 0)
        {
            you.Health -= Tuning.Starve;
            thirst = lost;
        }
        if (you.Food > Tuning.RegainAbove && you.Water > Tuning.RegainAbove)
        {
            var before = you.Health;
            you.Health = Math.Min(1, you.Health + Tuning.Regain);
            regained = (int)Tuning.Round((you.Health - before) * 100);
        }
        events.Add(new TimePassed(hunger, thirst, regained));

        if (you.Health <= 0)
        {
            if (you.Wind)
            {
                you.Wind = false;
                you.Health = Tuning.SecondWindHealth;
                events.Add(new SecondWind());
            }
            else
            {
                you.Health = 0;
                End(run, won: false, you.Food <= 0 ? EndReason.Hunger : EndReason.Thirst, by: null, events);
                return;
            }
        }

        if (you.Food > Tuning.GrowAbove && you.Water > Tuning.GrowAbove && you.Growth < Tuning.GrowthMost)
        {
            var from = you.Growth;
            var to = Math.Min(Tuning.GrowthMost, from + stats.GrowthRate);
            you.Growth = to;
            you.Best = Math.Max(you.Best, to);
            events.Add(new Grew(from, to));
            for (var stage = Stages.Of(from) + 1; stage <= Stages.Of(to); stage++)
            {
                events.Add(new StageReached(stage));
                if (stage < Stages.Elder || run.Rank(Trait.LateBloomer) > 0) Offers.Owe(run, 1);
                // H46: "Unseen" is earned at the crossing; a bite later takes nothing back.
                if (stage == Stages.Adult && !you.Bitten) you.AdultUnbitten = true;
            }
        }
    }

    // ---- E. Arrivals ----------------------------------------------------------------------

    /// <summary>
    /// E1 to E5, at your stage now. A predator on the danger's beat while there are
    /// fewer than its most (the pick from your size's pool, then the alerted roll,
    /// then the cell's picks); small game on its beat, up to
    /// <see cref="Tuning.SmallGameMost"/>; two plants out of your sight on theirs;
    /// the apex once growth is full, alerted and relentless, numbered as any arrival
    /// (H38); then, while it lives, its wait: outlasted ends the run won, otherwise
    /// <see cref="ApexWaits"/> says the turns left. The apex's arrival is
    /// <see cref="ApexArrived"/> alone; every other is <see cref="Arrived"/>. An
    /// arrival that finds no cell is no arrival (and spends no id, no number).
    /// </summary>
    private static void Arrivals(RunState run, Stats stats, Rng rng, List<TurnEvent> events)
    {
        var you = run.You;
        var stage = Stages.Of(you.Growth);
        var danger = Dangers.Of(run.Danger);

        // E1.
        if (run.Turn % danger.Every == 0
            && run.Creatures.Count(c => c.Kind == CreatureKind.Predator && !Fights.IsApex(c)) < MostPredators(run, stage))
        {
            var pool = Predators.Pool(stats.Build.Size, stage);
            var type = pool[rng.Next(pool.Count)];
            var alerted = rng.Chance(Tuning.ArrivesAlerted);
            var predator = Island.AddPredator(run, rng, type, alerted);
            if (predator is not null) events.Add(new Arrived(predator.Id, predator.At, alerted));
        }

        // E2.
        if (run.Turn % Tuning.SmallGameEvery == 0
            && run.Creatures.Count(c => c.Kind == CreatureKind.SmallGame) < Tuning.SmallGameMost)
        {
            var game = Island.AddSmallGame(run, rng);
            if (game is not null) events.Add(new Arrived(game.Id, game.At, Alerted: false));
        }

        // E3. Out of your sight by rule: a plant never appears where you are looking.
        if (run.Turn % Tuning.PlantsEvery == 0)
        {
            var grown = new List<int>(Tuning.PlantsAtATime);
            for (var k = 0; k < Tuning.PlantsAtATime; k++)
            {
                var at = Island.PickCell(run, rng, cell =>
                    Island.IsLand(run, cell) && !run.Plants[cell] && Island.Grid.Distance(cell, you.At) > stats.Sight);
                if (at < 0) continue;
                run.Plants[at] = true;
                grown.Add(at);
            }
            if (grown.Count > 0) events.Add(new PlantsGrew(grown));
        }

        // E4. The apex comes when growth is full. "Has not come" is ApexAt: a turn
        // whose arrival found no cell leaves it unset, and the next turn tries again.
        if (you.Growth >= Tuning.GrowthMost && run.ApexAt == Tuning.ApexNotYet)
        {
            var apex = Island.AddPredator(run, rng, Tuning.Apex, alerted: true);
            if (apex is not null)
            {
                apex.Relentless = true;
                run.ApexAt = run.Turn;
                events.Add(new ApexArrived(apex.Id, apex.At));
            }
        }

        // E5. Outlasted, or one turn nearer.
        if (run.ApexAt != Tuning.ApexNotYet && run.Creatures.Exists(Fights.IsApex))
        {
            var waited = run.Turn - run.ApexAt;
            var outlast = OutlastTurns(run);
            if (waited >= outlast)
            {
                End(run, won: true, EndReason.Outlasted, by: null, events);
                return;
            }
            if (waited > 0) events.Add(new ApexWaits(outlast - waited));
        }
    }
}
