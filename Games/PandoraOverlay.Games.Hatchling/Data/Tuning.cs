namespace PandoraOverlay.Games.Hatchling;

/// <summary>
/// Every number of the game, each with its one home here. A rule names the
/// constant; it never writes the number itself, so a retune is one edit and a
/// test can pin what the design says. The tables (builds, species, predators,
/// dangers, the lineage) keep their rows in their own classes under
/// <c>Data/</c>; everything else is here.
///
/// <para>The values are the design's, which ported most of them from the
/// prototype and changed some on the bot's evidence (the decisions cited). The
/// balance work may turn only the species' threat and, a little, the dangers' (H33):
/// every other number here is a design decision, not a knob.</para>
///
/// <para>Words: a price is paid from the wallet, and pay is what goes into it.
/// The currency's own word lives in <see cref="HatchlingText"/> only (A24, H35).</para>
/// </summary>
internal static class Tuning
{
    // ---- Rounding (H24) -----------------------------------------------------------

    /// <summary>
    /// Halves go up: 0.5 is 1, 2.5 is 3, -0.5 is 0. The prototype is JavaScript,
    /// whose rounding sends halves up; .NET's own rounding sends halves
    /// to the even neighbour, so a straight port would drift by a point of health
    /// here and there and the island and the bot's numbers would stop matching
    /// (H24). Every rounding in the game goes through this or <see cref="Round1"/>,
    /// and a test scans the source for the other.
    /// </summary>
    public static double Round(double x) => Math.Floor(x + 0.5);

    /// <summary>To one decimal, halves up, by <see cref="Round"/> (H24): 1.25 is 1.3.</summary>
    public static double Round1(double x) => Round(x * 10) / 10;

    // ---- The board ------------------------------------------------------------------

    /// <summary>Columns of the island's hex grid: the prototype's 11.</summary>
    public const int Columns = 11;
    /// <summary>Rows of the island's hex grid: the prototype's 11.</summary>
    public const int Rows = 11;
    /// <summary>Cells on the island; a cell is <c>row * Columns + column</c>.</summary>
    public const int Cells = Columns * Rows;

    // ---- The island ------------------------------------------------------

    /// <summary>The river's first column is this plus a pick of <see cref="RiverStartPicks"/>.</summary>
    public const int RiverStartBase = 2;
    /// <summary>How many columns the river's start is picked among (2 + pick of 7: columns 2 to 8).</summary>
    public const int RiverStartPicks = 7;
    /// <summary>The chance, each row, that the river bends one column aside.</summary>
    public const double RiverBend = 0.35;
    /// <summary>A bend goes left when its second roll is under this, else right.</summary>
    public const double RiverBendLeft = 0.5;
    /// <summary>Cells of the pond, grown as one cluster of water.</summary>
    public const int PondSize = 3;
    /// <summary>How many forests are grown.</summary>
    public const int Forests = 5;
    /// <summary>A forest's size is this plus a pick of <see cref="ForestSizePicks"/> (5 to 8 cells).</summary>
    public const int ForestSizeBase = 5;
    /// <summary>How many sizes a forest's is picked among.</summary>
    public const int ForestSizePicks = 4;
    /// <summary>How many rock outcrops are grown.</summary>
    public const int Rocks = 2;
    /// <summary>An outcrop's size is this plus a pick of <see cref="RockSizePicks"/> (2 to 4 cells).</summary>
    public const int RockSizeBase = 2;
    /// <summary>How many sizes an outcrop's is picked among.</summary>
    public const int RockSizePicks = 3;
    /// <summary>A grow's seed cell: up to this many more picks while the cell picked is not grass.</summary>
    public const int GrowSeedPicks = 60;
    /// <summary>A grow's spread: at most this many tries to add a cell to the cluster.</summary>
    public const int GrowTries = 80;
    /// <summary>The column of the cell the start is the nearest land to.</summary>
    public const int StartColumn = 5;
    /// <summary>The row of the cell the start is the nearest land to: the island's south.</summary>
    public const int StartRow = 9;
    /// <summary>A cell picked at random for something: this many tries, then the lowest cell that passes.</summary>
    public const int PickCellTries = 200;

    // ---- What is on the island ------------------------------------------------------

    /// <summary>Plants at the start of a run.</summary>
    public const int Plants = 16;
    /// <summary>Carcasses at the start of a run.</summary>
    public const int Carcasses = 3;
    /// <summary>Meals in each carcass at the start.</summary>
    public const int CarcassMeals = 2;
    /// <summary>Small game at the start of a run.</summary>
    public const int SmallGame = 4;
    /// <summary>No more small game arrives while this many are on the island.</summary>
    public const int SmallGameMost = 5;
    /// <summary>Small game's health, in points.</summary>
    public const int SmallGameHealth = 6;
    /// <summary>Small game's defense.</summary>
    public const int SmallGameDefense = 0;
    /// <summary>Meals small game leaves when defeated.</summary>
    public const int SmallGameMeat = 1;
    /// <summary>Small game is placed at least this far from you.</summary>
    public const int SmallGameDistance = 3;
    /// <summary>A predator is placed at least this far from you, the first and every arrival.</summary>
    public const int PredatorDistance = 5;
    /// <summary>The first predator of every run, whatever the size class (H34): the first minute's lesson.</summary>
    public const PredatorType FirstPredator = PredatorType.Troodon;
    /// <summary>The apex: it arrives when you reach Elder, and the run is won by outlasting or defeating it.</summary>
    public const PredatorType Apex = PredatorType.Tyrannosaurus;
    /// <summary><c>RunState.ApexAt</c> before the apex has come.</summary>
    public const int ApexNotYet = -1;
    /// <summary>A creature's target when it has none: not a cell, so a rule that forgets to set one sends nobody to the island's corner (cell 0).</summary>
    public const int NoTarget = -1;

    // ---- You ------------------------------------------------------------------------

    /// <summary>Food at the start of a run, of <see cref="FoodMost"/>.</summary>
    public const double StartFood = 80;
    /// <summary>Water at the start of a run, of <see cref="WaterMost"/>.</summary>
    public const double StartWater = 80;
    /// <summary>Food is 0 to this; Eat is allowed only below it.</summary>
    public const double FoodMost = 100;
    /// <summary>Water is 0 to this; Drink is allowed only below it.</summary>
    public const double WaterMost = 100;
    /// <summary>Growth in a run is a percent, 0 to this.</summary>
    public const double GrowthMost = 100;
    /// <summary>The stages' lines, in percent of growth: Juvenile, Subadult, Adult, Elder. A value on a line is past it.</summary>
    public static readonly IReadOnlyList<double> StageLines = new[] { 25.0, 50.0, 75.0, 100.0 };
    /// <summary>Sight every dino has before its build, traits and instincts.</summary>
    public const int BaseSight = 3;
    /// <summary>Growth at the start of a run for each rank of Strong start, in percent.</summary>
    public const double StrongStartGrowth = 10;

    // ---- The scale: a dino's size by growth ------------------------------

    /// <summary>A hatchling's share of the adult's health and attack.</summary>
    public const double ScaleHatchling = 0.35;
    /// <summary>The growth at which the scale reaches 1 (the adult's numbers).</summary>
    public const double ScaleAdultAt = 75;
    /// <summary>What an Elder has on top of the adult's numbers, reached at full growth.</summary>
    public const double ScaleElderGain = 0.15;

    // ---- Your moves ------------------------------------------------------

    /// <summary>A walk's noise.</summary>
    public const int WalkNoise = 3;
    /// <summary>A sneak's noise.</summary>
    public const int SneakNoise = 1;
    /// <summary>A sprint's noise.</summary>
    public const int SprintNoise = 5;
    /// <summary>An attack's noise.</summary>
    public const int AttackNoise = 4;
    /// <summary>Eating plants or meat: its noise.</summary>
    public const int EatNoise = 1;
    /// <summary>Eating fish: its noise (splashing).</summary>
    public const int FishNoise = 2;
    /// <summary>A drink's noise.</summary>
    public const int DrinkNoise = 1;
    /// <summary>A rest's noise.</summary>
    public const int RestNoise = 0;
    /// <summary>The drain of a move with no drain of its own: food and water as a turn costs them.</summary>
    public const double PlainDrain = 1;
    /// <summary>A sneak costs this times a turn's food and water.</summary>
    public const double SneakDrain = 1.5;
    /// <summary>Entering water while wading (a walk or a sneak): this much more noise.</summary>
    public const int WadeNoise = 1;
    /// <summary>Entering water while wading: the drain is this many times more.</summary>
    public const double WadeDrain = 1.5;
    /// <summary>Food from a plant, before the meal's factors.</summary>
    public const double PlantFood = 30;
    /// <summary>Food from a meal of a carcass, before the meal's factors.</summary>
    public const double MeatFood = 45;
    /// <summary>Food from fishing, before the meal's factors.</summary>
    public const double FishFood = 25;
    /// <summary>Water from a drink.</summary>
    public const double DrinkWater = 40;
    /// <summary>A rest heals this share of full health.</summary>
    public const double RestHeal = 0.08;
    /// <summary>A rest with Fast healer heals this share instead.</summary>
    public const double FastHealerHeal = 0.16;

    // ---- Time (H16) -----------------------------------------------------

    /// <summary>Food a turn costs, before the build's hunger and the traits (H16: was 1.2, and hunger never mattered).</summary>
    public const double FoodPerTurn = 1.6;
    /// <summary>Water a turn costs, before the traits (H16: was 1.6).</summary>
    public const double WaterPerTurn = 2.0;
    /// <summary>Health lost a turn with food at 0, and again with water at 0.</summary>
    public const double Starve = 0.05;
    /// <summary>Health regained a turn while food and water are both above <see cref="RegainAbove"/>.</summary>
    public const double Regain = 0.02;
    /// <summary>Food and water must both be above this for health to come back by itself.</summary>
    public const double RegainAbove = 50;
    /// <summary>Food and water must both be above this for you to grow.</summary>
    public const double GrowAbove = 25;
    /// <summary>Growth a turn, in percent, before Fast metabolism.</summary>
    public const double GrowthPerTurn = 1.4;

    // ---- Fights ----------------------------------------------------------

    /// <summary>Each point of defense takes this share off a blow...</summary>
    public const double ArmourPerDefense = 0.06;
    /// <summary>...up to this share, however much defense.</summary>
    public const double ArmourMost = 0.6;
    /// <summary>A blow's swing is this plus a roll times <see cref="SwingSpread"/>: 0.85 to 1.15.</summary>
    public const double SwingBase = 0.85;
    /// <summary>The width of a blow's swing.</summary>
    public const double SwingSpread = 0.3;
    /// <summary>The product of every boost that applies to your hit is capped here.</summary>
    public const double MaxBoost = 3;
    /// <summary>The bite at which Second wind steps in leaves you at this health.</summary>
    public const double SecondWindHealth = 0.25;
    /// <summary>Your chance to dodge a bite is capped here, whatever stacks.</summary>
    public const double DodgeMost = 0.6;
    /// <summary>Every blow takes at least this many points: no hit is ever nothing.</summary>
    public const int LeastDamage = 1;

    // ---- The perks (H13) ---------------------------------------------------

    /// <summary>Tireless: a sprint's drain is this, a walk's.</summary>
    public const double TirelessDrain = 1.0;
    /// <summary>Spikes: a predator that bites you takes this share of the bite.</summary>
    public const double SpikesShare = 0.35;
    /// <summary>Headbutt: a creature dazed can be dazed again only this many turns later.</summary>
    public const int HeadbuttAgainAfter = 3;
    /// <summary>Braced: a bite on a turn you did not move hurts this share of itself.</summary>
    public const double BracedShare = 0.7;
    /// <summary>Charge: your first hit on each creature is boosted by this.</summary>
    public const double ChargeBoost = 2;
    /// <summary>Big bites: every meal is this many times as much.</summary>
    public const double BigBitesMeal = 1.5;
    /// <summary>Thagomizer: every other creature beside you takes this share of your attack.</summary>
    public const double SweepShare = 0.6;
    /// <summary>Frill: no bite takes more than this share of your full health.</summary>
    public const double FrillMost = 0.15;
    /// <summary>High view: sight, one hex further.</summary>
    public const int HighViewSight = 1;
    /// <summary>Venom: a creature you hit takes one step less in each of its next this many actions.</summary>
    public const int VenomActions = 2;
    /// <summary>Pounce: your hit from cover is boosted by this.</summary>
    public const double PounceBoost = 2;
    /// <summary>Latch: each hit already landed in a row on the same creature adds this to the boost...</summary>
    public const double LatchStep = 0.25;
    /// <summary>...counting at most this many hits (up to double).</summary>
    public const int LatchMost = 4;
    /// <summary>Fleet: sprints this many hexes further...</summary>
    public const int FleetRange = 1;
    /// <summary>...at this share of the build's sprint drain.</summary>
    public const double FleetDrain = 0.7;
    /// <summary>Scavenger: meat feeds this many times as much.</summary>
    public const double ScavengerMeal = 1.5;
    /// <summary>Spit: you can hit a creature this many hexes away...</summary>
    public const int SpitRange = 2;
    /// <summary>...for this share.</summary>
    public const double SpitShare = 0.6;
    /// <summary>Lunge: your hit from the water is boosted by this.</summary>
    public const double LungeBoost = 2;
    /// <summary>Bleeder: a creature you hit bleeds for its next this many actions...</summary>
    public const int BleedActions = 3;
    /// <summary>...losing this share of that hit in each.</summary>
    public const double BleedShare = 0.25;
    /// <summary>Evasive: this chance to dodge any bite.</summary>
    public const double EvasiveDodge = 0.20;

    // ---- The instincts (H14) -----------------------------------------

    /// <summary>Keen eyes: sight, one hex further.</summary>
    public const int KeenEyesSight = 1;
    /// <summary>Soft feet: everything you do makes this much less noise.</summary>
    public const int SoftFeetNoise = 1;
    /// <summary>Long stride: sprints this many hexes further.</summary>
    public const int LongStrideRange = 1;
    /// <summary>Thick skin: this much more defense.</summary>
    public const int ThickSkinDefense = 2;
    /// <summary>Ambusher: your hits from cover are boosted by this.</summary>
    public const double AmbusherBoost = 1.5;
    /// <summary>Big appetite: every meal is this many times as much.</summary>
    public const double BigAppetiteMeal = 1.5;
    /// <summary>Quick: this much more chance to dodge a bite.</summary>
    public const double QuickDodge = 0.15;
    /// <summary>Fierce: attack times this.</summary>
    public const double FierceAttack = 1.2;
    /// <summary>Wary: predators are sensed this many hexes beyond your sight.</summary>
    public const int WarySense = 2;
    /// <summary>Hardy: full health times this.</summary>
    public const double HardyHealth = 1.15;

    // ---- The lineage's steps (H11) ----------------------------------------

    /// <summary>Deep stomach: food drains this share slower a rank.</summary>
    public const double DeepStomachStep = 0.08;
    /// <summary>Thick hide: this much more defense a rank.</summary>
    public const int ThickHideStep = 1;
    /// <summary>Dry spells: water drains this share slower a rank.</summary>
    public const double DrySpellsStep = 0.10;
    /// <summary>Keen nose: sight, this much further a rank.</summary>
    public const int KeenNoseStep = 1;
    /// <summary>Quick reflexes: this much more chance to dodge a rank.</summary>
    public const double QuickReflexesStep = 0.06;
    /// <summary>Light step: this much less noise a rank.</summary>
    public const int LightStepStep = 1;
    /// <summary>Escape artist: sprints this many hexes further.</summary>
    public const int EscapeArtistStep = 1;
    /// <summary>Fast metabolism: growth this share faster a rank.</summary>
    public const double FastMetabolismStep = 0.06;
    /// <summary>Legacy: a run's pay this share more a rank, on top of the danger's.</summary>
    public const double LegacyStep = 0.10;
    /// <summary>Elder's patience: the apex gives up this many turns sooner, and pays nothing extra (H31).</summary>
    public const int EldersPatienceTurns = 5;
    /// <summary>The price of rank r of a trait in the Survival, Senses and Growth branches is its tier's base times r, tier 1 to 4 (H11).</summary>
    public static readonly IReadOnlyList<int> TierPrice = new[] { 60, 200, 800, 4000 };
    /// <summary>The Instinct branch's four traits, one rank each, tier 1 to 4 (H11).</summary>
    public static readonly IReadOnlyList<int> InstinctTierPrice = new[] { 500, 1500, 3000, 6000 };

    // ---- Minds ------------------------------------------------------

    /// <summary>A predator's alert when it sees you (or is the apex): it hunts.</summary>
    public const int AlertSeen = 4;
    /// <summary>A predator's alert when it hears you, or arrives alerted: it searches, one less a turn.</summary>
    public const int AlertHeard = 3;
    /// <summary>A fast predator's steps an action.</summary>
    public const int FastSteps = 2;
    /// <summary>A predator's steps an action that is not fast.</summary>
    public const int SlowSteps = 1;
    /// <summary>The chance that a predator on no alert wanders a cell.</summary>
    public const double Wander = 0.4;
    /// <summary>The chance that small game near you flees...</summary>
    public const double Flee = 0.65;
    /// <summary>...when you are this near or nearer.</summary>
    public const int FleeWithin = 2;
    /// <summary>The chance that small game that did not flee roams a cell.</summary>
    public const double Roam = 0.3;

    // ---- Arrivals ------------------------------------------------------

    /// <summary>The chance that an arriving predator comes already alerted, heading for you.</summary>
    public const double ArrivesAlerted = 0.4;
    /// <summary>Small game arrives on every turn that is a multiple of this.</summary>
    public const int SmallGameEvery = 10;
    /// <summary>Plants grow on every turn that is a multiple of this...</summary>
    public const int PlantsEvery = 8;
    /// <summary>...this many at a time, out of your sight.</summary>
    public const int PlantsAtATime = 2;

    // ---- Stars -----------------------------------------------------------

    /// <summary>Each star gives its species this share more health and attack in runs.</summary>
    public const double StarBonus = 0.05;
    /// <summary>A species earns a star at each of these percents of growth seen on the server: three in all.</summary>
    public static readonly IReadOnlyList<double> StarsAt = new[] { 50.0, 150.0, 300.0 };

    // ---- A run's pay (H8, H9) ---------------------------------------------

    /// <summary>Pay for each percent grown in the run (H8: only growth gained in it).</summary>
    public const double GrowthPay = 0.5;
    /// <summary>Pay for crossing Juvenile, Subadult, Adult and Elder in the run, in that order.</summary>
    public static readonly IReadOnlyList<int> StagePay = new[] { 5, 10, 20, 40 };
    /// <summary>Pay for each predator defeated (the apex not counted).</summary>
    public const int KillPay = 3;
    /// <summary>Pay for outlasting or defeating the apex.</summary>
    public const int WinPay = 60;

    // ---- Offers of instincts (H14) ----------------------------------------

    /// <summary>Instincts in an offer.</summary>
    public const int OfferSize = 3;
    /// <summary>Instincts in an offer with Sharp instincts.</summary>
    public const int SharpOfferSize = 4;
    /// <summary>The first paid redraw's price; each paid redraw in a run doubles it.</summary>
    public const int RedrawPrice = 25;

    // ---- Unlocking a species (H10) ----------------------------------------

    /// <summary>The first species bought costs this...</summary>
    public const int UnlockPrice = 400;
    /// <summary>...each one bought after it this much more...</summary>
    public const int UnlockStep = 100;
    /// <summary>...and never more than this. Growing one to Adult is the way meant; buying is the way round.</summary>
    public const int UnlockMost = 1500;

    // ---- Server play (H10, H18) -------------------------------------------

    /// <summary>Pay for each percent of growth seen on the server, before freshness.</summary>
    public const double ServerRate = 4;
    /// <summary>The freshness of a species not grown lately, and of one never grown.</summary>
    public const double FreshMax = 3.0;
    /// <summary>Freshness never falls below this: a species grown all the time still pays.</summary>
    public const double FreshFloor = 1.0;
    /// <summary>Freshness falls this much for each percent grown.</summary>
    public const double FreshPerPercent = 0.02;
    /// <summary>Freshness comes back this much a calendar day, worked out when needed, never shown as a wait (H18).</summary>
    public const double FreshPerDay = 0.25;
    /// <summary>A card shows the freshness in the good colour from this multiplier up.</summary>
    public const double FreshGood = 2.0;

    // ---- The field journal (H12) ------------------------------------------

    /// <summary>Open entries in the journal.</summary>
    public const int JournalEntries = 3;
    /// <summary>An entry's target, in percent of growth seen...</summary>
    public const double JournalTarget = 75;
    /// <summary>...and its pay.</summary>
    public const int JournalReward = 400;
    /// <summary>A big build's entry's target (Tank, Apex, Stalker, Swimmer)...</summary>
    public const double JournalBigTarget = 50;
    /// <summary>...and its pay.</summary>
    public const int JournalBigReward = 500;
    /// <summary>A species weighs 1 / (1 + Grown / this) in the draw, so the journal leans to what you have grown least.</summary>
    public const double JournalLean = 50;
    /// <summary>The price of swapping an entry for a new draw.</summary>
    public const int SwapPrice = 50;

    // ---- The rest -------------------------------------------------------------------

    /// <summary>The context line gives hints until this many runs have been finished (H23).</summary>
    public const int HintRuns = 3;
    /// <summary>A hint suggests resting below this share of health.</summary>
    public const double HintHurt = 0.5;
    /// <summary>A hint about hunger below this much food.</summary>
    public const double HintHungry = 40;
    /// <summary>A hint about thirst below this much water.</summary>
    public const double HintThirsty = 40;
    /// <summary>The next run's seed moves by this after each settled run (wrapping): the golden ratio's 32 bits, so seeds spread.</summary>
    public const uint SeedStep = 0x9E3779B9;
}
