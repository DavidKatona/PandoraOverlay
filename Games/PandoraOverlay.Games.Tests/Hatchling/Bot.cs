using PandoraOverlay.Games.Engine;
using PandoraOverlay.Games.Hatchling;

namespace PandoraOverlay.Games.Tests;

/// <summary>
/// The bot that plays Hatchling with nothing on screen (H32): the balance tests
/// B1 to B12 stand on it, so "no build is the easy pick" and "worth playing for
/// weeks" are tests, not hopes. It is the prototype's bot (<c>hatch-sim.js</c>'s
/// <c>botChoose</c>) with what the plan's rules need, rule for rule and in the
/// plan's order (<see cref="Choose"/>).
///
/// <para><b>It knows only what the player may know.</b> Creatures through
/// <see cref="Sight.Knows"/>, food through <see cref="Sight.ShowsFood"/> (H39), the
/// ground only of cells in <see cref="RunState.Seen"/>, and what the player's own
/// screen shows besides: your own numbers, a known predator's species, alert and
/// health, the cells a move can reach (<see cref="Moves.Targets"/>, which the board
/// marks, H53) and whom you can hit (<see cref="Moves.Attackable"/>). It never reads
/// a creature it does not know, a cell's ground it has not seen, or the run's
/// generator. Two tests hold it: hiding a predator the player does not know changes
/// no choice, and food on a cell never seen is no goal without Good nose or
/// Scavenger and a goal with them.</para>
///
/// <para><b>Pure, no clock.</b> A choice is a function of the run alone, and a run
/// played by it a function of its setup and seed alone, so the balance tests may run
/// many seeds side by side and every number still comes from its seed (B11).</para>
///
/// <para>It plays worse than a person, on purpose the same way the plan's numbers
/// were measured: it always takes the first instinct offered, never redraws, and in
/// a career never unlocks a species with amber.</para>
/// </summary>
internal static class Bot
{
    /// <summary>A run still going after this many turns is reported as such (B6: none may).</summary>
    public const int TurnLimit = 600;

    /// <summary>
    /// The most actions a run may take, picks and pace changes included: a guard
    /// so that a bot or a rule gone wrong can never hang the test run. Far above
    /// <see cref="TurnLimit"/> turns' worth (a turn is at most a pace change and a
    /// move, plus the few picks of a run).
    /// </summary>
    private const int ActionLimit = TurnLimit * 4;

    /// <summary>
    /// The prototype's distance to "the nearest known predator" when none is known,
    /// in <see cref="Flee"/>'s score.
    /// </summary>
    private const int NoPredatorDistance = 5;

    /// <summary>The plan's thresholds, by rule (5.2), named once.</summary>
    private const int AlertNear = 3;          // rule 2: within 3 cells
    private const int SprintWithin = 2;       // rule 2: sprint when it is within 2
    private const double HuntBelow = 90;      // rule 4: food under 90
    private const double EatBelow = 65;       // rule 5
    private const double DrinkBelow = 65;     // rule 6
    private const double RestBelow = 0.5;     // rule 7: health under half
    private const double TopUpBelow = 95;     // rule 8: on a goal, eat or drink under 95
    private const int FleeDistanceWeight = 2; // Flee's score
    private const int FleeCoverBonus = 3;
    private const int FleeWadeCost = 2;

    /// <summary>
    /// What the bot does next: <paramref name="Pace"/>, when set, is chosen first
    /// (no turn passes, <see cref="PlayerAction.SetPace"/>), then
    /// <paramref name="Action"/>.
    /// </summary>
    internal sealed record Choice(Pace? Pace, PlayerAction Action);

    /// <summary>
    /// How a played run came out: its last state, and whether it was still going
    /// at <see cref="TurnLimit"/> turns (then <c>Final.Over</c> is null).
    /// </summary>
    internal sealed record Outcome(RunState Final, bool TimedOut)
    {
        /// <summary>The growth the run reached.</summary>
        public double Best => Final.You.Best;

        /// <summary>It reached Adult (75%) or beyond.</summary>
        public bool ReachedAdult => Stages.Of(Final.You.Best) >= Stages.Adult;

        /// <summary>It was won: the apex outlasted or defeated.</summary>
        public bool Won => Final.Over is { Won: true };

        /// <summary>How it ended; null for a run still going at the limit.</summary>
        public EndReason? Reason => Final.Over?.Reason;

        /// <summary>It ran out of food or water.</summary>
        public bool Starved => Reason is EndReason.Hunger or EndReason.Thirst;

        /// <summary>What it paid; 0 for a run still going.</summary>
        public int Pay => Final.Over?.Total ?? 0;

        /// <summary>It ended while the apex was on the island (it came, and the run did not outlast it).</summary>
        public bool EndedInApexTurns => Final.ApexAt != Tuning.ApexNotYet && !Won && Final.Over is not null;
    }

    private static readonly PlayerAction RestAction = new PlayerAction.Rest();

    // ---- The choice -------------------------------------------------------------------

    /// <summary>
    /// The bot's next action on <paramref name="run"/>: the first of the plan's rules
    /// that applies (5.2), with <c>s</c> your numbers now and <c>known</c> the
    /// predators the player knows, in id order.
    /// <list type="number">
    /// <item>(0) An offer is open: the first choice; never a redraw.</item>
    /// <item>(1) A predator beside you: fight the lowest id when the odds say so, else
    /// sprint away, and fight when there is nowhere to go.</item>
    /// <item>(2) A known predator on an alert (or the apex) within 3: when the odds
    /// are bad, rest if hidden (or Freeze) and it is neither beside you nor the apex,
    /// else sprint (within 2) or sneak away; nowhere to go, rest.</item>
    /// <item>(3) Spit: a known predator exactly 2 away with good odds is spat at.</item>
    /// <item>(4) A meat eater under 90 food hits small game it can reach.</item>
    /// <item>(5) Eat under 65 food with food here. (6) Drink under 65 water by water.</item>
    /// <item>(7) Rest under half health with no predator known.</item>
    /// <item>(8) Go for the lower of food and water, by the shortest walk.</item>
    /// <item>(9) A move the rules refuse is a Rest (<see cref="Play(RunState)"/>).</item>
    /// </list>
    /// Pure: it reads the run and changes nothing.
    /// </summary>
    public static Choice Choose(RunState run)
    {
        ArgumentNullException.ThrowIfNull(run);

        // 0.
        if (run.Offer is not null) return new Choice(null, new PlayerAction.Pick(0));

        var s = Stats.Of(run);
        var you = run.You.At;
        var known = run.Creatures
            .Where(c => c.Kind == CreatureKind.Predator && Sight.Knows(run, s, c))
            .OrderBy(c => c.Id)
            .ToList();

        // 1. A predator beside you (every one beside you is in sight, so known).
        var beside = known.Where(c => Distance(you, c.At) == 1).ToList();
        if (beside.Count > 0)
        {
            var c = beside[0];
            var (kill, die) = Odds(run, s, c);
            if (kill * beside.Count <= die - 1) return Attack(c);
            return Flee(run, s, known, Pace.Sprint) ?? Attack(c);
        }

        // 2. A known predator on an alert, or the apex, within 3.
        var near = known.FirstOrDefault(c => (c.Alert > 0 || Fights.IsApex(c)) && Distance(you, c.At) <= AlertNear);
        if (near is not null)
        {
            var (kill, die) = Odds(run, s, near);
            if (kill > die - 2)
            {
                var distance = Distance(you, near.At);
                if ((Sight.Hidden(run, s) || s.Species.Perk == Perk.Freeze) && distance > 1 && !Fights.IsApex(near))
                    return new Choice(null, RestAction);
                return Flee(run, s, known, distance <= SprintWithin ? Pace.Sprint : Pace.Sneak)
                    ?? new Choice(null, RestAction);
            }
        }

        // 3. Spit at a known predator exactly two away, when the odds are good.
        if (s.Species.Perk == Perk.Spit)
        {
            var attackable = Moves.Attackable(run, s);
            foreach (var c in known)
            {
                if (Distance(you, c.At) != Tuning.SpitRange) continue;
                var (kill, die) = Odds(run, s, c);
                if (kill <= die && attackable.Any(a => a.Id == c.Id)) return Attack(c);
            }
        }

        // 4. Small game you can hit, for a meat eater under 90 food.
        if (s.Eats.HasFlag(Diet.Meat) && run.You.Food < HuntBelow)
        {
            var game = Moves.Attackable(run, s).FirstOrDefault(c => c.Kind == CreatureKind.SmallGame);
            if (game is not null) return Attack(game);
        }

        // 5 to 7.
        if (run.You.Food < EatBelow && Moves.FoodHere(run, s) is not null) return new Choice(null, new PlayerAction.Eat());
        if (run.You.Water < DrinkBelow && Moves.WaterNear(run)) return new Choice(null, new PlayerAction.Drink());
        if (run.You.Health < RestBelow && known.Count == 0) return new Choice(null, RestAction);

        // 8.
        return GoFor(run, s, known);
    }

    /// <summary>
    /// Rule 8: go for the lower of food and water (water on a tie, as the
    /// prototype). The goals, as the player's board shows them:
    /// <list type="bullet">
    /// <item>food: a plant or a carcass <see cref="Sight.ShowsFood"/> shows, of a food
    /// you eat; a seen cell holding small game you know (meat); seen water (fish).</item>
    /// <item>water: a seen cell on water or beside water that is seen.</item>
    /// </list>
    /// The shortest walk (<see cref="HexGrid.FirstStepToward"/>, the neighbour order
    /// on a tie) goes over cells open to you; a creature you know closes its cell,
    /// unless it is small game on a goal; a cell never seen counts as open (the
    /// player would walk toward the smell and find out) and its ground is never read.
    /// With no goal reachable, toward the nearest cell not yet seen; with neither,
    /// Rest. On a goal: Eat (food here, under 95), else Drink (water near, under 95),
    /// else Rest. A first step onto small game: Rest. Sneak while a predator is
    /// known, else Walk.
    /// </summary>
    private static Choice GoFor(RunState run, Stats s, IReadOnlyList<Creature> known)
    {
        var you = run.You.At;
        var wantFood = run.You.Food < run.You.Water;
        var plants = s.Eats.HasFlag(Diet.Plants);
        var meat = s.Eats.HasFlag(Diet.Meat);
        var fish = s.Eats.HasFlag(Diet.Fish);

        // Every creature the player knows, by its cell: predators as known, small game in sight.
        var knownAt = new Dictionary<int, Creature>();
        foreach (var c in run.Creatures)
            if (Sight.Knows(run, s, c)) knownAt[c.At] = c;

        bool SeenWater(int cell) => run.Seen[cell] && run.Tiles[cell] == Terrain.Water;

        bool IsGoal(int cell)
        {
            if (wantFood)
            {
                if (plants && Sight.ShowsFood(run, s, cell, Food.Plants)) return true;
                if (meat && Sight.ShowsFood(run, s, cell, Food.Meat)) return true;
                if (meat && run.Seen[cell] && knownAt.TryGetValue(cell, out var game) && game.Kind == CreatureKind.SmallGame) return true;
                return fish && SeenWater(cell);
            }
            return run.Seen[cell] && (SeenWater(cell) || Island.Grid.Neighbours(cell).Any(SeenWater));
        }

        // "Unless it is small game on a goal" is read as small game that IS the goal:
        // a meat eater going for food. Read literally, a plant eater took a plant under
        // small game for its goal; small game boxed into a corner by the dino never
        // moves, and the bot rested beside it for ever (a Rest's 8% outheals hunger's
        // 5%), which no player would do: three fresh runs reached 600 turns so.
        var gameIsFood = wantFood && meat;

        bool Passable(int cell)
        {
            if (knownAt.TryGetValue(cell, out var creature))
                return gameIsFood && creature.Kind == CreatureKind.SmallGame && IsGoal(cell);
            if (!run.Seen[cell]) return true;
            return Moves.OpenToYou(run, s, cell, sprinting: false);
        }

        if (IsGoal(you))
        {
            if (Moves.FoodHere(run, s) is not null && run.You.Food < TopUpBelow) return new Choice(null, new PlayerAction.Eat());
            if (Moves.WaterNear(run) && run.You.Water < TopUpBelow) return new Choice(null, new PlayerAction.Drink());
            return new Choice(null, RestAction);
        }

        var step = Island.Grid.FirstStepToward(you, IsGoal, Passable);
        if (step < 0) step = Island.Grid.FirstStepToward(you, cell => !run.Seen[cell], Passable);
        if (step < 0) return new Choice(null, RestAction);
        if (knownAt.TryGetValue(step, out var there) && there.Kind == CreatureKind.SmallGame)
            return new Choice(null, RestAction);
        return new Choice(known.Count > 0 ? Pace.Sneak : Pace.Walk, new PlayerAction.Move(step));
    }

    /// <summary>
    /// The plan's odds against <paramref name="c"/>: <c>kill</c> is the hits you need
    /// to defeat it, <c>die</c> the bites it needs to catch you, each at an even swing
    /// (<c>mine = max(1, Attack * armour)</c>, armour 1 with Crushing bite;
    /// <c>theirs = max(1, Attack * Threat * Armour(Defense))</c>). The prototype's
    /// <c>strengthVs</c>. It reads a known predator's health, as the player's board shows.
    /// </summary>
    internal static (int Kill, int Die) Odds(RunState run, Stats s, Creature c)
    {
        var armour = s.Species.Perk == Perk.CrushingBite ? 1 : Fights.Armour(Fights.DefenseOf(c));
        var mine = Math.Max(1, s.Attack * armour);
        var theirs = Math.Max(1, Predators.Of(c.Type).Attack * Island.Threat(run) * Fights.Armour(s.Defense));
        var kill = (int)Math.Ceiling(c.Health / mine);
        var die = (int)Math.Ceiling(run.You.Health * s.MaxHealth / theirs);
        return (kill, die);
    }

    /// <summary>
    /// Away from the predators you know, at <paramref name="pace"/>: of the cells that
    /// pace reaches (<see cref="Moves.Targets"/>, in rising cell order), the one with
    /// the highest <c>2 * (distance to the nearest known predator, 5 with none)
    /// + (3 if it is cover for you) - (2 if you would wade there)</c>, the first on a
    /// tie; null when there is nowhere to go. Cover and wading are counted only on a
    /// seen cell: the ground of a cell never seen is not on the player's screen.
    /// </summary>
    private static Choice? Flee(RunState run, Stats s, IReadOnlyList<Creature> known, Pace pace)
    {
        var at = run;
        if (run.Pace != pace)
        {
            at = run.Clone();   // Targets reads the run's pace; the run given is never changed
            at.Pace = pace;
        }
        var targets = Moves.Targets(at, s);
        if (targets.Count == 0) return null;

        var best = targets[0];
        var bestScore = int.MinValue;
        foreach (var cell in targets)
        {
            var nearest = known.Count > 0 ? known.Min(c => Distance(c.At, cell)) : NoPredatorDistance;
            var seen = run.Seen[cell];
            var cover = seen && ((run.Tiles[cell] == Terrain.Forest && s.Hides) || (run.Tiles[cell] == Terrain.Water && s.Swims));
            var wade = seen && Sight.WouldWade(run, s, cell);
            var score = FleeDistanceWeight * nearest + (cover ? FleeCoverBonus : 0) - (wade ? FleeWadeCost : 0);
            if (score > bestScore)
            {
                bestScore = score;
                best = cell;
            }
        }
        return new Choice(pace, new PlayerAction.Move(best));
    }

    private static Choice Attack(Creature c) => new(null, new PlayerAction.Attack(c.Id));

    private static int Distance(int a, int b) => Island.Grid.Distance(a, b);

    // ---- A run --------------------------------------------------------------------------

    /// <summary>A run of <paramref name="setup"/> on <paramref name="seed"/>'s island, played to its end or <see cref="TurnLimit"/> turns.</summary>
    public static Outcome Play(RunSetup setup, uint seed) => Play(Island.NewRun(setup, seed));

    /// <summary>
    /// <paramref name="start"/> played to its end or <see cref="TurnLimit"/> turns: each
    /// choice's pace first, then its action through <see cref="TurnRules.Apply"/>, the
    /// one way a run changes (A20); an action the rules refuse becomes a Rest (rule 9).
    /// The run given is not changed.
    /// </summary>
    public static Outcome Play(RunState start)
    {
        ArgumentNullException.ThrowIfNull(start);
        var run = start;
        for (var actions = 0; run.Over is null && run.Turn < TurnLimit; actions++)
        {
            if (actions >= ActionLimit)
                throw new InvalidOperationException($"The bot took {ActionLimit} actions in {run.Turn} turns: a rule or the bot is stuck.");
            var choice = Choose(run);
            if (choice.Pace is { } pace && pace != run.Pace)
                run = TurnRules.Apply(run, new PlayerAction.SetPace(pace))!.After;
            var result = TurnRules.Apply(run, choice.Action) ?? TurnRules.Apply(run, RestAction)
                ?? throw new InvalidOperationException("The rules refused a Rest: only an open offer can, and the bot picks first.");
            run = result.After;
        }
        return new Outcome(run, run.Over is null);
    }

    // ---- A career -----------------------------------------------------------------------

    /// <summary>One run of a career, as it was settled.</summary>
    /// <param name="Run">1, 2, 3 ... in the career.</param>
    /// <param name="Pay">What the run itself paid (its summary's total).</param>
    /// <param name="Spent">The lineage's cost bought by the end of this run, in all.</param>
    /// <param name="Ranks">The lineage after this run's purchases.</param>
    internal sealed record CareerRun(int Run, string Species, bool Won, bool TimedOut, int Turns, int Pay, long Spent,
        IReadOnlyDictionary<Trait, int> Ranks);

    /// <summary>A whole career: every run, and the state it ended in.</summary>
    internal sealed record CareerResult(IReadOnlyList<CareerRun> Runs, GameState Final)
    {
        /// <summary>The first run after which a trait was bought; 0 for none.</summary>
        public int FirstTrait => Runs.FirstOrDefault(r => r.Spent > 0)?.Run ?? 0;

        /// <summary>The first run won; 0 for none.</summary>
        public int FirstWin => Runs.FirstOrDefault(r => r.Won)?.Run ?? 0;

        /// <summary>The run after which <paramref name="share"/> of the lineage's whole price was bought; 0 for never.</summary>
        public int RunAtShare(double share) =>
            Runs.FirstOrDefault(r => r.Spent >= share * Lineage.WholePrice - 1e-9)?.Run ?? 0;

        /// <summary>The run after which the lineage was complete; 0 for never.</summary>
        public int Complete => RunAtShare(1);
    }

    /// <summary>
    /// Runs one after another on <paramref name="start"/>, as a player would play
    /// them through the game's own rules: each run begins from the lineage and the
    /// stars as they are (<see cref="GameState.SetupFor"/>, H19) on the save's next
    /// seed, is played by the bot, settled (<see cref="Settle.Run"/>: its pay banked,
    /// its achievements granted, the seed moved) and left (<see cref="Settle.BackToCamp"/>);
    /// a run still going at <see cref="TurnLimit"/> is left first (H20). After each
    /// run the bot buys the cheapest rank it can afford (the table's order on a tie),
    /// again and again until it can afford none; it never unlocks, swaps or redraws.
    /// Then <paramref name="between"/>, when given, feeds what happened outside
    /// Hatchling before the next run (server play, on its own calendar).
    /// <paramref name="species"/> picks each run's species from the state and the
    /// run's index (0 first). It stops once the lineage is complete, or after
    /// <paramref name="most"/> runs.
    /// </summary>
    public static CareerResult Career(GameState start, Func<GameState, int, string> species, int danger, int most,
        Func<GameState, int, GameState>? between = null)
    {
        ArgumentNullException.ThrowIfNull(start);
        ArgumentNullException.ThrowIfNull(species);
        var state = start.Clone();
        var runs = new List<CareerRun>();
        long spent = 0;
        for (var k = 0; k < most && !LineageRules.Complete(state); k++)
        {
            var name = species(state, k);
            var outcome = Play(state.SetupFor(name, danger), state.NextSeed);
            var final = outcome.Final;
            if (outcome.TimedOut)
                final = TurnRules.Apply(final, new PlayerAction.Leave())!.After;

            var ended = state.Clone();
            ended.Run = final;
            state = Settle.BackToCamp(Settle.Run(ended));

            for (var bought = BuyCheapest(state); bought is not null; bought = BuyCheapest(state))
            {
                spent += bought.Value.Price;
                state = bought.Value.State;
            }

            runs.Add(new CareerRun(k + 1, name, final.Over!.Won, outcome.TimedOut, final.Turn, final.Over.Total, spent,
                new Dictionary<Trait, int>(state.Ranks)));
            if (between is not null) state = between(state, k);
        }
        return new CareerResult(runs, state);
    }

    /// <summary>The cheapest rank the wallet affords now, the table's order on a tie, bought; null when none is.</summary>
    private static (GameState State, int Price)? BuyCheapest(GameState state)
    {
        TraitInfo? cheapest = null;
        var price = 0;
        foreach (var info in Lineage.All)
        {
            if (!LineageRules.CanBuy(state, info.Trait)) continue;
            var p = LineageRules.PriceOf(state, info.Trait);
            if (cheapest is null || p < price)
            {
                cheapest = info;
                price = p;
            }
        }
        if (cheapest is null) return null;
        var next = LineageRules.Buy(state, cheapest.Trait)
            ?? throw new InvalidOperationException("CanBuy said yes and Buy said no.");
        return (next, price);
    }
}
