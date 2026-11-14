namespace PandoraOverlay.Games.Hatchling;

/// <summary>The ring under a creature's feet (H22): gold yours, red a hunter's, a heavier red the apex's, white the one acting in a playback.</summary>
internal enum Ring { None, You, Hostile, Apex, Acting }

/// <summary>The marks above a head: a hunter's alert and afflictions, and yours.</summary>
[Flags]
internal enum Marks
{
    None = 0,
    /// <summary>It hunts you: alert 4, or the apex.</summary>
    Hunt = 1,
    /// <summary>It searches: alert 1 to 3.</summary>
    Search = 2,
    /// <summary>It skips its next action (Headbutt).</summary>
    Dazed = 4,
    /// <summary>Its next actions are a step shorter (Venom).</summary>
    Slow = 8,
    /// <summary>It bleeds (Bleeder).</summary>
    Bleed = 16,
    /// <summary>Above you, on your turn.</summary>
    You = 32,
    /// <summary>Above you, while predators can't see you.</summary>
    Hidden = 64,
    /// <summary>Above you, at Elder (H21: the Elder is the Adult's picture with this mark).</summary>
    Elder = 128,
}

/// <summary>What a sprite is: it decides its stand-in, its marks, and who stands in front on a tie.</summary>
internal enum SpriteKind { SmallGame, Predator, You }

/// <summary>
/// One creature as the board draws it now. Mutable on purpose: the scene at rest
/// comes from the state (<see cref="Scene.Of"/>), and the playback moves, turns,
/// fades and re-rings these as its beats run, then the next rest replaces them.
/// </summary>
internal sealed class SceneSprite
{
    /// <summary>The creature's id in the run; 0 for you.</summary>
    public int Creature { get; set; }

    /// <summary>What it is.</summary>
    public SpriteKind Kind { get; set; }

    /// <summary>The art's subject: a predator's species, <c>smallgame</c>, or your species at your size (<c>Dryosaurus-s</c>, H21).</summary>
    public string Subject { get; set; } = "";

    /// <summary>The art's action: <c>stand</c>, <c>walk</c>, <c>strike</c>, <c>hurt</c>, <c>eat</c>.</summary>
    public string Action { get; set; } = ArtNames.Stand;

    /// <summary>The tick of its action, handed in by the view: nothing here keeps time.</summary>
    public int Tick { get; set; }

    /// <summary>Where its feet are, in the surface's pixels.</summary>
    public int X { get; set; }

    /// <summary>Where its feet are, in the surface's pixels.</summary>
    public int Y { get; set; }

    /// <summary>It faces left (it last stepped or struck toward smaller x). The view's, never saved.</summary>
    public bool Flip { get; set; }

    /// <summary>0 (gone) to 255 (all there): an arrival fades in, a defeat fades out. Its ring and marks fade with it.</summary>
    public byte Opacity { get; set; } = 255;

    /// <summary>The ring under its feet.</summary>
    public Ring Ring { get; set; }

    /// <summary>The marks above its head.</summary>
    public Marks Marks { get; set; }

    /// <summary>A predator's number among its species in the run (H7, H38); drawn from 2 to 99.</summary>
    public int Ordinal { get; set; }

    /// <summary>The grid row it stands in, or is walking into: the drawing order, top row first.</summary>
    public int Row { get; set; }
}

/// <summary>A rising number on the board: damage in <c>num-hurt</c>, a gain in <c>num-good</c>. The playback places and fades it.</summary>
internal sealed class SceneNumber
{
    /// <summary>The amount, drawn without a sign of its own (its sign is <see cref="Sign"/>).</summary>
    public int Value { get; set; }

    /// <summary>A gain (+); otherwise a hurt (-).</summary>
    public bool Good { get; set; }

    /// <summary>Draw its sign before the digits.</summary>
    public bool Sign { get; set; } = true;

    /// <summary>The horizontal middle of the number.</summary>
    public int X { get; set; }

    /// <summary>The number's top row.</summary>
    public int Y { get; set; }

    /// <summary>0 to 255: a number fades in its last third.</summary>
    public byte Opacity { get; set; } = 255;
}

/// <summary>
/// What the board shows, as data: every tile, item, mark, ring, creature and number
/// in the place it is drawn. The painter is a pure function of it
/// (<see cref="BoardPainter.Paint"/>), so a frame is a test (A17); the view makes one
/// at rest from the state and changes it as the playback runs.
///
/// <para>The arrays are the scene's own copies, one entry a cell; the lists are the
/// scene's own. Nothing in a scene refers back into a run.</para>
/// </summary>
internal sealed class Scene
{
    /// <summary>Each cell's ground.</summary>
    public Terrain[] Tiles { get; set; } = Array.Empty<Terrain>();

    /// <summary>The cells seen at any time this run: the others are fog.</summary>
    public bool[] Seen { get; set; } = Array.Empty<bool>();

    /// <summary>The cells in sight now: a seen cell out of sight is dimmed.</summary>
    public bool[] InSight { get; set; } = Array.Empty<bool>();

    /// <summary>The cells whose plant is drawn: exactly where <see cref="Sight.ShowsFood"/> says so (H29, H39).</summary>
    public bool[] Plants { get; set; } = Array.Empty<bool>();

    /// <summary>The meals of carcass drawn on each cell (0 none): where <see cref="Sight.ShowsFood"/> says so, as many as are there.</summary>
    public int[] Meat { get; set; } = Array.Empty<int>();

    /// <summary>The cells whose item is drawn though the cell is unseen (Good nose, Scavenger, H39). The painter draws them as any other; the view and the tests read it.</summary>
    public bool[] FoodShown { get; set; } = Array.Empty<bool>();

    /// <summary>The creatures drawn, in drawing order (<see cref="Sort"/>).</summary>
    public List<SceneSprite> Sprites { get; } = new();

    /// <summary>The rising numbers, drawn last.</summary>
    public List<SceneNumber> Numbers { get; } = new();

    /// <summary>The cells you can move to: <c>mark/move</c> on each. Empty but on your turn with no offer open.</summary>
    public List<int> MoveCells { get; } = new();

    /// <summary>The cells of the creatures you can attack: <c>mark/attack</c> on each. Empty but on your turn with no offer open.</summary>
    public List<int> AttackCells { get; } = new();

    /// <summary>The cell under the mouse, or -1 for none: <c>mark/hover</c> on it.</summary>
    public int HoverCell { get; set; } = -1;

    /// <summary>
    /// 0 or 1: the frame of the two-frame marks that breathe while the board waits
    /// for you (<c>mark/move</c>, <c>mark/you</c>, <c>ring/acting</c>). The view flips
    /// it on the engine's ambient beat; a missing frame 1 is drawn as frame 0.
    /// </summary>
    public int AmbientFrame { get; set; }

    /// <summary>
    /// Whether <c>mark/you</c> may show over a sprite marked <see cref="Marks.You"/>:
    /// the scene at rest sets both on your turn; the playback turns this off while it
    /// runs and back on at the next rest, leaving the sprite's marks as they are.
    /// </summary>
    public bool ShowYourMark { get; set; }

    /// <summary>
    /// The scene at rest: exactly what the state shows the player and nothing more.
    /// A creature is in it only when <see cref="Sight.Knows"/> says the player may know
    /// it (the apex once it has come, H6, wherever it is); a plant or a carcass only
    /// where <see cref="Sight.ShowsFood"/> says so (H29, H39). The scene has no rule of
    /// its own for either.
    ///
    /// <para>On your turn with no offer open and the run not over, and only then, the
    /// cells you can move to are marked, as <see cref="Moves.Targets"/> answers them at
    /// the run's pace, and the cells of the creatures you can attack, as
    /// <see cref="Moves.Attackable"/> answers them; the cell under the mouse
    /// (<paramref name="hoverCell"/>, -1 for none) too. This is the scene's one way in
    /// (H53): nothing outside it hands a scene at rest its cells, so the board offers
    /// exactly what the rules accept (G9). An attackable creature the player does not
    /// know is left out all the same (<see cref="Sight.Knows"/>'s rule, a belt): no
    /// mark can point at one.</para>
    ///
    /// <para>Everyone stands at rest: the <c>stand</c> action, tick 0, facing right
    /// (facing is the view's and is not saved), fully there. Each predator's marks come
    /// from its alert and afflictions, its ring from what it is (the apex's is heavier,
    /// H22); your size from your stage (H21); your marks from your hiding, Elder, and
    /// <c>mark/you</c> on your turn while the run goes on (H53: an open offer keeps it,
    /// it is still you who chooses; a run that is over never shows it).</para>
    /// </summary>
    public static Scene Of(RunState run, Stats stats, bool yourTurn, int hoverCell)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(stats);
        var waiting = yourTurn && run.Over is null;

        var cells = run.Tiles.Length;
        var scene = new Scene
        {
            Tiles = (Terrain[])run.Tiles.Clone(),
            Seen = (bool[])run.Seen.Clone(),
            InSight = new bool[cells],
            Plants = new bool[cells],
            Meat = new int[cells],
            FoodShown = new bool[cells],
        };
        for (var cell = 0; cell < cells; cell++)
        {
            scene.InSight[cell] = Sight.InSight(run, stats, cell);
            scene.Plants[cell] = Sight.ShowsFood(run, stats, cell, Food.Plants);
            scene.Meat[cell] = Sight.ShowsFood(run, stats, cell, Food.Meat) ? run.Meat[cell] : 0;
            scene.FoodShown[cell] = !run.Seen[cell] && (scene.Plants[cell] || scene.Meat[cell] > 0);
        }

        foreach (var creature in run.Creatures)
        {
            if (!Sight.Knows(run, stats, creature)) continue;
            scene.Sprites.Add(creature.Kind == CreatureKind.SmallGame ? SmallGame(creature) : Predator(creature));
        }
        scene.Sprites.Add(You(run, stats, waiting));
        scene.Sort();

        scene.ShowYourMark = waiting;
        if (waiting && run.Offer is null)
        {
            scene.MoveCells.AddRange(Moves.Targets(run, stats));
            foreach (var creature in Moves.Attackable(run, stats))
                if (Sight.Knows(run, stats, creature)) scene.AttackCells.Add(creature.At);
            if (hoverCell >= 0 && hoverCell < cells) scene.HoverCell = hoverCell;
        }
        return scene;
    }

    /// <summary>
    /// The sprites in drawing order: by row, top first; then by x; on a tie small
    /// game under predators under you. The scene at rest is sorted; the view sorts
    /// again after it moves someone into another row. Allocates nothing.
    /// </summary>
    public void Sort() => Sprites.Sort(DrawingOrder.Instance);

    private static SceneSprite Predator(Creature creature)
    {
        var apex = creature.Type == Tuning.Apex;   // as Sight.Knows tells the apex: by its species, which only the apex has
        var marks = Marks.None;
        if (creature.Alert >= Tuning.AlertSeen || apex) marks |= Marks.Hunt;
        else if (creature.Alert > 0) marks |= Marks.Search;
        if (creature.Dazed) marks |= Marks.Dazed;
        if (creature.Slowed > 0) marks |= Marks.Slow;
        if (creature.Bleed > 0) marks |= Marks.Bleed;
        return Place(new SceneSprite
        {
            Creature = creature.Id,
            Kind = SpriteKind.Predator,
            Subject = ArtNames.PredatorOf(creature.Type),
            Ring = apex ? Ring.Apex : Ring.Hostile,
            Marks = marks,
            Ordinal = creature.Ordinal,
        }, creature.At);
    }

    private static SceneSprite SmallGame(Creature creature) => Place(new SceneSprite
    {
        Creature = creature.Id,
        Kind = SpriteKind.SmallGame,
        Subject = ArtNames.SmallGame,
        Ring = Ring.None,
    }, creature.At);

    private static SceneSprite You(RunState run, Stats stats, bool waiting)
    {
        var stage = Stages.Of(run.You.Growth);
        var marks = Marks.None;
        if (waiting) marks |= Marks.You;
        if (Sight.Hidden(run, stats)) marks |= Marks.Hidden;
        if (stage == Stages.Elder) marks |= Marks.Elder;
        return Place(new SceneSprite
        {
            Creature = 0,
            Kind = SpriteKind.You,
            Subject = ArtBook.StageSubject(run.Species, stage),
            Ring = Ring.You,
            Marks = marks,
        }, run.You.At);
    }

    private static SceneSprite Place(SceneSprite sprite, int cell)
    {
        (sprite.X, sprite.Y) = BoardLayout.Feet(cell);
        sprite.Row = Island.Grid.Row(cell);
        return sprite;
    }

    /// <summary>Row, then x, then kind (small game, predator, you): who is drawn over whom.</summary>
    private sealed class DrawingOrder : IComparer<SceneSprite>
    {
        public static readonly DrawingOrder Instance = new();

        public int Compare(SceneSprite? a, SceneSprite? b)
        {
            if (ReferenceEquals(a, b)) return 0;
            if (a is null) return -1;
            if (b is null) return 1;
            var byRow = a.Row.CompareTo(b.Row);
            if (byRow != 0) return byRow;
            var byX = a.X.CompareTo(b.X);
            return byX != 0 ? byX : ((int)a.Kind).CompareTo((int)b.Kind);   // as ints: an enum's CompareTo boxes
        }
    }
}
