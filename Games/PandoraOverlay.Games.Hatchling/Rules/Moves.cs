namespace PandoraOverlay.Games.Hatchling;

/// <summary>
/// The questions every one of your moves asks: where you can go, by which cells,
/// what you can eat here and what it gives, whether water is near, whom you can
/// hit, and how loud a move is with your build. Read-only: none of them changes the
/// run, and none rolls. <see cref="TurnRules"/> asks them to allow or refuse a move,
/// and the board, the context line and the bot ask the same ones, so what the
/// screen offers is exactly what the rules accept (G9: the turns must be
/// learnable from the screen).
///
/// <para>The prototype's <c>hxMoveTargets</c>, <c>hxFoodHere</c>,
/// <c>hxWaterNear</c> and <c>hxAttackable</c> are the ancestors; the species'
/// perks (H13) and the instincts (H14) are new, and Spit widens the reach.</para>
/// </summary>
internal static class Moves
{
    /// <summary>
    /// <paramref name="cell"/>'s ground lets you onto it, whoever stands there.
    /// Walking or sneaking: rock only if you fly, every other ground always (a
    /// non-swimmer wades into water, which costs: see <see cref="Sight.WouldWade"/>).
    /// Sprinting: a flyer as a walk; a swimmer water only (it is fast only where it
    /// swims); everyone else neither water nor rock (nobody sprints through a river).
    /// </summary>
    public static bool OpenToYou(RunState run, Stats stats, int cell, bool sprinting)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(stats);
        var ground = run.Tiles[cell];
        if (!sprinting || stats.Flies)
            return ground != Terrain.Rock || stats.Flies;
        if (stats.Swims)
            return ground == Terrain.Water;
        return ground != Terrain.Water && ground != Terrain.Rock;
    }

    /// <summary>
    /// The cells a move takes you to at the run's pace, in rising cell order. A walk
    /// or a sneak: the neighbours open to you with no creature on them. A sprint:
    /// every cell reached in <see cref="Stats.SprintRange"/> steps or fewer through
    /// cells open to a sprint with no creature on them (the target itself one of
    /// them, A40), your own cell never.
    /// </summary>
    public static IReadOnlyList<int> Targets(RunState run, Stats stats)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(stats);
        var from = run.You.At;
        List<int> cells;
        if (run.Pace == Pace.Sprint)
        {
            cells = Island.Grid.Reach(from, stats.SprintRange, cell => SprintOpen(run, stats, cell)).Keys.ToList();
        }
        else
        {
            cells = Island.Grid.Neighbours(from)
                .Where(cell => OpenToYou(run, stats, cell, sprinting: false) && Island.CreatureAt(run, cell) is null)
                .ToList();
        }
        cells.Sort();
        return cells;
    }

    /// <summary>
    /// The cells a move to <paramref name="cell"/> passes through, after yours and
    /// ending on it: one cell for a walk or a sneak; for a sprint the shortest way
    /// over cells open to a sprint with no creature on them (<c>HexGrid.Path</c>,
    /// ties by neighbour order, the same walk <see cref="Targets"/> measures by, so
    /// it is never longer than the range). Empty when <paramref name="cell"/> is not
    /// one of <see cref="Targets"/>: a path is only ever shown for a move the rules
    /// would take.
    /// </summary>
    public static IReadOnlyList<int> PathTo(RunState run, Stats stats, int cell)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(stats);
        if (!Targets(run, stats).Contains(cell))
            return Array.Empty<int>();
        if (run.Pace != Pace.Sprint)
            return new[] { cell };
        return Island.Grid.Path(run.You.At, cell, c => SprintOpen(run, stats, c));
    }

    /// <summary>
    /// What Eat would eat here, the first that applies: plants (you eat plants and a
    /// plant is on your cell), meat (you eat meat and a carcass is), fish (you eat
    /// fish and you stand on water); null for nothing. The order is the prototype's:
    /// a Forager on a cell with a plant and a carcass eats the plant.
    /// </summary>
    public static Food? FoodHere(RunState run, Stats stats)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(stats);
        var at = run.You.At;
        if (stats.Eats.HasFlag(Diet.Plants) && run.Plants[at]) return Food.Plants;
        if (stats.Eats.HasFlag(Diet.Meat) && run.Meat[at] > 0) return Food.Meat;
        if (stats.Eats.HasFlag(Diet.Fish) && run.Tiles[at] == Terrain.Water) return Food.Fish;
        return null;
    }

    /// <summary>
    /// The food a meal of <paramref name="kind"/> gives: its base (plants 30, meat 45,
    /// fish 25) times <see cref="Stats.Meal"/> (Big appetite, Big bites, Scavenger on
    /// meat), rounded halves up (H24). What the Eat button shows and what the meal
    /// adds, before food is held at its most.
    /// </summary>
    public static int MealGain(Stats stats, Food kind)
    {
        ArgumentNullException.ThrowIfNull(stats);
        var food = kind switch
        {
            Food.Plants => Tuning.PlantFood,
            Food.Meat => Tuning.MeatFood,
            Food.Fish => Tuning.FishFood,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "not a food"),
        };
        return (int)Tuning.Round(food * stats.Meal(kind));
    }

    /// <summary>
    /// What Eat would add to food now (H49): what goes into the bar, never more than
    /// it holds, rounded halves up (H24); 0 when Eat would be refused (nothing you eat
    /// here, or food at its most). The Eat button's second line and the
    /// <see cref="Ate"/> event both ask this, so the screen never promises more than
    /// the move gives: food 90 and a plant says +10, and the plant is still gone.
    /// <see cref="MealGain"/> stays what a meal is worth.
    /// </summary>
    public static int EatGain(RunState run, Stats stats)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(stats);
        var kind = FoodHere(run, stats);
        var food = run.You.Food;
        if (kind is null || food >= Tuning.FoodMost) return 0;
        return (int)Tuning.Round(Math.Min(Tuning.FoodMost, food + MealGain(stats, kind.Value)) - food);
    }

    /// <summary>
    /// What Drink would add to water now (H49): <see cref="Tuning.DrinkWater"/> held
    /// at <see cref="Tuning.WaterMost"/>, rounded halves up; 0 when Drink would be
    /// refused (no water near, or water at its most). Asked by the Drink button and
    /// the <see cref="Drank"/> event alike.
    /// </summary>
    public static int DrinkGain(RunState run)
    {
        ArgumentNullException.ThrowIfNull(run);
        var water = run.You.Water;
        if (!WaterNear(run) || water >= Tuning.WaterMost) return 0;
        return (int)Tuning.Round(Math.Min(Tuning.WaterMost, water + Tuning.DrinkWater) - water);
    }

    /// <summary>
    /// What Rest would add to health now, in percent points (H49): your rest's share
    /// held at full health, rounded halves up; 0 at full health. Rest is always
    /// allowed, so this is never a refusal. Asked by the Rest button and the
    /// <see cref="Rested"/> event alike.
    /// </summary>
    public static int RestGain(RunState run, Stats stats)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(stats);
        var health = run.You.Health;
        return (int)Tuning.Round((Math.Min(1, health + stats.RestHeal) - health) * 100);
    }

    /// <summary>You stand on water or beside it: where Drink is allowed.</summary>
    public static bool WaterNear(RunState run)
    {
        ArgumentNullException.ThrowIfNull(run);
        var at = run.You.At;
        return run.Tiles[at] == Terrain.Water
            || Island.Grid.Neighbours(at).Any(cell => run.Tiles[cell] == Terrain.Water);
    }

    /// <summary>
    /// The creatures you can hit, in id order: every creature beside you; with Spit
    /// (H13) also those two cells away that you know of (<see cref="Sight.Knows"/>:
    /// you can't spit at what you could not know is there).
    /// </summary>
    public static IReadOnlyList<Creature> Attackable(RunState run, Stats stats)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(stats);
        var you = run.You.At;
        var spits = stats.Species.Perk == Perk.Spit;
        return run.Creatures
            .Where(c =>
            {
                var d = Island.Grid.Distance(you, c.At);
                return d == 1 || (spits && d == Tuning.SpitRange && Sight.Knows(run, stats, c));
            })
            .OrderBy(c => c.Id)
            .ToList();
    }

    /// <summary>
    /// A move's noise with your build, Light step and Soft feet: the move's own
    /// noise plus <see cref="Stats.NoiseMod"/>, never under 0 (a quiet dino resting
    /// makes no noise, not a negative one that would cancel another's).
    /// </summary>
    public static int NoiseOf(Stats stats, int baseNoise)
    {
        ArgumentNullException.ThrowIfNull(stats);
        return Math.Max(0, baseNoise + stats.NoiseMod);
    }

    /// <summary>A cell a sprint may pass through or end on: open to a sprint, and no creature on it.</summary>
    private static bool SprintOpen(RunState run, Stats stats, int cell) =>
        OpenToYou(run, stats, cell, sprinting: true) && Island.CreatureAt(run, cell) is null;
}
