using System.Globalization;
using static System.FormattableString;

namespace PandoraOverlay.Games.Hatchling;

/// <summary>
/// Every word the player reads in Hatchling, and nowhere else (A24): the views'
/// XAML reads the fixed ones by <c>{x:Static}</c> (which is why the class and its
/// constants are public), and code calls the functions, which build every
/// sentence that holds a value. No other file of the game builds a sentence.
///
/// <para><b>The currency (H35).</b> <see cref="Currency"/> is the one place its
/// word is written. Every string that names an amount is built by
/// <see cref="Money"/> or <see cref="Plus"/>, and the few sentences that name the
/// currency are built from the constant. A test scans the game's source and finds
/// the word in this file only: it is a placeholder nobody has checked, and a
/// rename must be one edit.</para>
///
/// <para><b>Numbers.</b> Invariant culture everywhere, thousands with a comma
/// ("1,234,567"), one decimal where a multiplier is shown, rounded by
/// <see cref="Tuning.Round1"/> (H24) so the text and the rules never disagree on
/// a half. A sentence that states a tunable number builds it from
/// <see cref="Tuning"/>, so the number keeps its one home.</para>
///
/// <para><b>Wording.</b> Never "died" or "killed": a run "ended", a predator
/// "caught you" or is "defeated" (the overlay's rule, CLAUDE.md). A predator is
/// named by its species, with " 2", " 3" ... from the second of its species in the
/// run, for its whole life (H7).</para>
///
/// <para>Functions are internal: most take the game's own (internal) types, and
/// XAML can only read constants and fields.</para>
/// </summary>
public static class HatchlingText
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    // ===== The game: the Games window's tile and header ================================

    public const string Title = "Hatchling";
    public const string Pitch = "Grow from hatchling to elder. Outlast the apex.";
    public const string Tag = "Turn-based";

    /// <summary>The currency's word, the one place it is written (H35, A24).</summary>
    public const string Currency = "amber";

    /// <summary>The currency's word at the start of a sentence.</summary>
    public static readonly string CurrencyCapital = char.ToUpperInvariant(Currency[0]) + Currency[1..];

    public const string TileEnded = "Your run has ended";

    internal static string TileUnderWay(int stage, string species) => $"Under way: {Stage(stage)} {species}";
    internal static string TileIdle(int unlocked, long wallet) => $"{Count(unlocked)} of {Count(SpeciesTable.All.Count)} species · {Money(wallet)}";
    internal static string TileDetail(int earned, int total) => $"{Count(earned)} of {Count(total)} achievements";

    // ===== Amounts, articles, names ======================================================

    /// <summary>An amount of the currency: "1,240 amber".</summary>
    internal static string Money(long n) => $"{Count(n)} {Currency}";

    /// <summary>An amount gained, in a column of amounts: "+60".</summary>
    internal static string Plus(long n) => n < 0 ? Count(n) : "+" + Count(n);

    /// <summary>A whole number with a comma for thousands, invariant: "1,234,567".</summary>
    internal static string Count(long n) => n.ToString("N0", Inv);

    /// <summary>A multiplier to one decimal, halves up (H24): 2.45 is "2.5".</summary>
    internal static string OneDecimal(double x) => Tuning.Round1(x).ToString("0.0", Inv);

    // A share as a whole percent, halves up: 0.08 is "8". For the sentences that
    // state a tunable share, so the share keeps its one home in Tuning.
    private static string Percent(double share) => ((long)Tuning.Round(share * 100)).ToString(Inv);

    /// <summary>The article before <paramref name="name"/>: "an" before a vowel's letter (A, E, I, O, U), else "a".</summary>
    internal static string A(string name) =>
        name.Length > 0 && "AEIOU".Contains(char.ToUpperInvariant(name[0])) ? "an" : "a";

    /// <summary>A predator's name (H7): "Carnotaurus" for the first of its species in the run, "Carnotaurus 2" for the second, for its whole life.</summary>
    internal static string Name(PredatorType type, int ordinal) =>
        ordinal >= 2 ? Invariant($"{Predators.NameOf(type)} {ordinal}") : Predators.NameOf(type);

    /// <summary>A predator inside a sentence: "the Carnotaurus 2".</summary>
    internal static string The(PredatorType type, int ordinal) => "the " + Name(type, ordinal);

    /// <summary>A predator at the start of a sentence: "The Carnotaurus 2".</summary>
    internal static string TheAtStart(PredatorType type, int ordinal) => "The " + Name(type, ordinal);

    /// <summary>"1 hex", "3 hexes".</summary>
    internal static string Hexes(int n) => n == 1 ? "1 hex" : $"{Count(n)} hexes";

    /// <summary>"1 turn", "9 turns".</summary>
    internal static string Turns(int n) => n == 1 ? "1 turn" : $"{Count(n)} turns";

    /// <summary>"1 meal", "2 meals".</summary>
    internal static string Meals(int n) => n == 1 ? "1 meal" : $"{Count(n)} meals";

    /// <summary>"1 run", "14 runs".</summary>
    internal static string Runs(int n) => n == 1 ? "1 run" : $"{Count(n)} runs";

    // ===== Stages, builds, foods =========================================================

    private static readonly string[] StageNames = { "Hatchling", "Juvenile", "Subadult", "Adult", "Elder" };

    /// <summary>A stage's name, 0 Hatchling to 4 Elder.</summary>
    internal static string Stage(int stage) =>
        stage >= 0 && stage < StageNames.Length
            ? StageNames[stage]
            : throw new ArgumentOutOfRangeException(nameof(stage), stage, "a stage is 0 to 4");

    /// <summary>A stage with its article: "a Juvenile", "an Adult".</summary>
    internal static string AStage(int stage) => A(Stage(stage)) + " " + Stage(stage);

    internal static string BuildName(Build build) => build switch
    {
        Build.Runner => "Runner",
        Build.Grazer => "Grazer",
        Build.Tank => "Tank",
        Build.Hunter => "Hunter",
        Build.Stalker => "Stalker",
        Build.Apex => "Apex",
        Build.Swimmer => "Swimmer",
        Build.Flyer => "Flyer",
        Build.Forager => "Forager",
        _ => throw new ArgumentOutOfRangeException(nameof(build), build, "not a build"),
    };

    /// <summary>The build's line on Camp.</summary>
    internal static string BuildLine(Build build) => build switch
    {
        Build.Runner => "Small, quick and quiet. Hides in forest.",
        Build.Grazer => "Sturdy. Hides in forest.",
        Build.Tank => "Hard to hurt, loud, and too big to hide.",
        Build.Hunter => "Lives on carcasses and small game. Hides in forest.",
        Build.Stalker => "A strong hunter. Hides in forest.",
        Build.Apex => "Very loud, always hungry, and too big to hide.",
        Build.Swimmer => "Swims and fishes. Hides in water, and sprints only there.",
        Build.Flyer => "Crosses rock and water, sees further, and fishes.",
        Build.Forager => "Eats plants and meat. Quiet. Hides in forest.",
        _ => throw new ArgumentOutOfRangeException(nameof(build), build, "not a build"),
    };

    internal static string FoodWord(Food food) => food switch
    {
        Food.Plants => "plants",
        Food.Meat => "meat",
        Food.Fish => "fish",
        _ => throw new ArgumentOutOfRangeException(nameof(food), food, "not a food"),
    };

    /// <summary>What a diet eats on a card: ", " between all, "plants, meat, fish".</summary>
    internal static string FoodsOnCard(Diet diet) => string.Join(", ", FoodWords(diet));

    /// <summary>What a diet eats in a sentence (H37): "plants", "plants and meat", "plants, meat and fish".</summary>
    internal static string FoodsInSentence(Diet diet)
    {
        var words = FoodWords(diet);
        return words.Count <= 1
            ? string.Join("", words)
            : string.Join(", ", words.Take(words.Count - 1)) + " and " + words[^1];
    }

    // The diet's foods, in the order plants, meat, fish.
    private static List<string> FoodWords(Diet diet)
    {
        var words = new List<string>(3);
        if (diet.HasFlag(Diet.Plants)) words.Add(FoodWord(Food.Plants));
        if (diet.HasFlag(Diet.Meat)) words.Add(FoodWord(Food.Meat));
        if (diet.HasFlag(Diet.Fish)) words.Add(FoodWord(Food.Fish));
        return words;
    }

    // ===== Tabs ==========================================================================

    public const string TabCamp = "Camp";
    public const string TabRun = "Run";
    public const string TabLineage = "Lineage";
    public const string TabJournal = "Journal";
    public const string TabRecords = "Records";

    // ===== The perks: Camp's chosen species ==============================================

    internal static string PerkName(Perk perk) => perk switch
    {
        Perk.Tireless => "Tireless",
        Perk.Freeze => "Freeze",
        Perk.Spikes => "Spikes",
        Perk.Headbutt => "Headbutt",
        Perk.Braced => "Braced",
        Perk.Charge => "Charge",
        Perk.BigBites => "Big bites",
        Perk.Thagomizer => "Thagomizer",
        Perk.Frill => "Frill",
        Perk.HighView => "High view",
        Perk.Venom => "Venom",
        Perk.Pounce => "Pounce",
        Perk.Latch => "Latch",
        Perk.Fleet => "Fleet",
        Perk.Scavenger => "Scavenger",
        Perk.Spit => "Spit",
        Perk.Lunge => "Lunge",
        Perk.Bleeder => "Bleeder",
        Perk.CrushingBite => "Crushing bite",
        Perk.Wader => "Wader",
        Perk.Evasive => "Evasive",
        _ => throw new ArgumentOutOfRangeException(nameof(perk), perk, "not a perk"),
    };

    internal static string PerkText(Perk perk) => perk switch
    {
        Perk.Tireless => "A sprint costs no more food and water than a walk.",
        Perk.Freeze => "After a Rest you are hidden anywhere, until you move again.",
        Perk.Spikes => "A predator that bites you takes a third of the bite itself.",
        Perk.Headbutt => "Your hit dazes: the creature loses its next action.",
        Perk.Braced => $"Bites hurt {Percent(1 - Tuning.BracedShare)}% less on a turn you did not move.",
        Perk.Charge => "Your first hit on each creature deals double.",
        Perk.BigBites => "Every meal feeds it half again as much.",
        Perk.Thagomizer => "Your hit also strikes every other creature beside you.",
        Perk.Frill => $"No bite takes more than {Percent(Tuning.FrillMost)}% of its full health.",
        Perk.HighView => "Sees one hex further still.",
        Perk.Venom => "A creature you hit is slowed for two turns.",
        Perk.Pounce => "Your hit from cover deals double.",
        Perk.Latch => "Each hit in a row on one creature deals more, up to double.",
        Perk.Fleet => "Sprints one hex further, for less.",
        Perk.Scavenger => "Meat feeds it half again as much. It smells every carcass.",
        Perk.Spit => "Hits a creature two hexes away, for a little less.",
        Perk.Lunge => "Your hit from the water deals double.",
        Perk.Bleeder => "A creature you hit bleeds for three turns.",
        Perk.CrushingBite => "Your hits ignore defense.",
        Perk.Wader => "Wades without cost, and eats fish.",
        Perk.Evasive => "Dodges one bite in five.",
        _ => throw new ArgumentOutOfRangeException(nameof(perk), perk, "not a perk"),
    };

    // ===== The instincts: the offer's cards and the run's panel ==========================

    internal static string InstinctName(Instinct instinct) => instinct switch
    {
        Instinct.Eyes => "Keen eyes",
        Instinct.Feet => "Soft feet",
        Instinct.Stride => "Long stride",
        Instinct.Skin => "Thick skin",
        Instinct.Ambush => "Ambusher",
        Instinct.Appetite => "Big appetite",
        Instinct.Healer => "Fast healer",
        Instinct.Quick => "Quick",
        Instinct.Nose => "Good nose",
        Instinct.Fierce => "Fierce",
        Instinct.Wary => "Wary",
        Instinct.Hardy => "Hardy",
        _ => throw new ArgumentOutOfRangeException(nameof(instinct), instinct, "not an instinct"),
    };

    internal static string InstinctText(Instinct instinct) => instinct switch
    {
        Instinct.Eyes => "See one hex further.",
        Instinct.Feet => Invariant($"Everything you do makes {Tuning.SoftFeetNoise} less noise."),
        Instinct.Stride => "Sprint one hex further.",
        Instinct.Skin => Invariant($"+{Tuning.ThickSkinDefense} defense."),
        Instinct.Ambush => "Your hits from cover deal half again as much.",
        Instinct.Appetite => "Every meal feeds you half again as much.",
        Instinct.Healer => "A Rest heals twice as much.",
        Instinct.Quick => $"Dodge {Percent(Tuning.QuickDodge)}% more bites.",
        Instinct.Nose => "Food you can eat shows through the fog.",
        Instinct.Fierce => $"+{Percent(Tuning.FierceAttack - 1)}% attack.",
        Instinct.Wary => "Sense predators two hexes beyond your sight.",
        Instinct.Hardy => $"+{Percent(Tuning.HardyHealth - 1)}% health.",
        _ => throw new ArgumentOutOfRangeException(nameof(instinct), instinct, "not an instinct"),
    };

    // ===== The lineage ===================================================================

    public const string LineageIntro = "Traits are permanent. A trait taken during a run counts from the next one.";
    public const string TraitComplete = "Complete";

    private static readonly string[] BranchNames = { "Survival", "Senses", "Growth", "Instinct" };

    /// <summary>A branch's name, 0 Survival to 3 Instinct.</summary>
    internal static string BranchName(int branch) =>
        branch >= 0 && branch < BranchNames.Length
            ? BranchNames[branch]
            : throw new ArgumentOutOfRangeException(nameof(branch), branch, "a branch is 0 to 3");

    internal static string TraitName(Trait trait) => trait switch
    {
        Trait.DeepStomach => "Deep stomach",
        Trait.ThickHide => "Thick hide",
        Trait.DrySpells => "Dry spells",
        Trait.SecondWind => "Second wind",
        Trait.KeenNose => "Keen nose",
        Trait.QuickReflexes => "Quick reflexes",
        Trait.LightStep => "Light step",
        Trait.EscapeArtist => "Escape artist",
        Trait.FastMetabolism => "Fast metabolism",
        Trait.StrongStart => "Strong start",
        Trait.Legacy => "Legacy",
        Trait.EldersPatience => "Elder's patience",
        Trait.SharpInstincts => "Sharp instincts",
        Trait.SecondThoughts => "Second thoughts",
        Trait.LateBloomer => "Late bloomer",
        Trait.BornReady => "Born ready",
        _ => throw new ArgumentOutOfRangeException(nameof(trait), trait, "not a trait"),
    };

    internal static string TraitText(Trait trait) => trait switch
    {
        Trait.DeepStomach => $"Food drains {Percent(Tuning.DeepStomachStep)}% slower a rank.",
        Trait.ThickHide => Invariant($"+{Tuning.ThickHideStep} defense a rank."),
        Trait.DrySpells => $"Water drains {Percent(Tuning.DrySpellsStep)}% slower a rank.",
        Trait.SecondWind => $"Once a run, a blow that would end it leaves you at {Percent(Tuning.SecondWindHealth)}% health.",
        Trait.KeenNose => "See one hex further a rank.",
        Trait.QuickReflexes => $"Dodge {Percent(Tuning.QuickReflexesStep)}% more bites a rank.",
        Trait.LightStep => Invariant($"Everything you do makes {Tuning.LightStepStep} less noise a rank."),
        Trait.EscapeArtist => "Sprint one hex further.",
        Trait.FastMetabolism => $"Grow {Percent(Tuning.FastMetabolismStep)}% faster a rank.",
        Trait.StrongStart => Invariant($"Start each run {Tuning.StrongStartGrowth}% grown a rank."),
        Trait.Legacy => $"{Percent(Tuning.LegacyStep)}% more {Currency} from runs a rank.",
        Trait.EldersPatience => $"The apex gives up {Turns(Tuning.EldersPatienceTurns)} sooner.",
        Trait.SharpInstincts => "Choose from four instincts, not three.",
        Trait.SecondThoughts => "Your first redraw of each run is free.",
        Trait.LateBloomer => "One more instinct when you reach Elder.",
        Trait.BornReady => "Every run starts with an instinct to choose.",
        _ => throw new ArgumentOutOfRangeException(nameof(trait), trait, "not a trait"),
    };

    /// <summary>A trait's next rank's price.</summary>
    internal static string TraitPrice(long price) => Money(price);

    /// <summary>"Needs Deep stomach first": the trait above it is at rank 0.</summary>
    internal static string TraitNeeds(Trait above) => $"Needs {TraitName(above)} first";

    /// <summary>A trait's ranks: "●●○○○", a ● for each rank owned, a ○ for each left.</summary>
    internal static string Pips(int owned, int max) =>
        new string('●', Math.Clamp(owned, 0, max)) + new string('○', Math.Max(0, max - Math.Clamp(owned, 0, max)));

    // ===== Camp ==========================================================================

    public const string CardLocked = "Locked";
    public const string RowHealth = "Health as an adult";
    public const string RowAttack = "Attack";
    public const string RowDefense = "Defense";
    public const string RowSight = "Sight";
    public const string ServerCaption = "ON THE SERVER";
    public const string DangerCaption = "DANGER";
    public const string DangerOne = "The island as it is.";
    public static readonly string NoteFirst =
        $"Grow from a hatchling to an Elder, then outlast the apex. Predators hear noise, and forest hides most dinos. Every run earns {Currency} for the lineage.";
    public const string NoteStart = "Each run starts from a hatchling, on a new island.";
    public static readonly string NoteLocked =
        $"Grow one to Adult on the server while the overlay is running and it unlocks here. Or save up {Currency}.";
    public const string StartRun = "Start a run";
    public const string UnlockArmed = "Click again to unlock";

    /// <summary>A species card's second line: "Grazer · plants".</summary>
    internal static string CardKind(Build build, Diet eats) => $"{BuildName(build)} · {FoodsOnCard(eats)}";

    /// <summary>"danger 3": the highest danger this species has won at.</summary>
    internal static string CardWon(int danger) => $"danger {Count(danger)}";

    /// <summary>"Server growth ×2.6".</summary>
    internal static string CardFresh(double fresh) => $"Server growth ×{OneDecimal(fresh)}";

    /// <summary>A species' stars: "★★☆", a ★ for each earned and a ☆ for each not, three in all.</summary>
    internal static string Stars(int earned)
    {
        var most = Tuning.StarsAt.Count;
        var have = Math.Clamp(earned, 0, most);
        return new string('★', have) + new string('☆', most - have);
    }

    /// <summary>The chosen species' kind: "Grazer · eats plants".</summary>
    internal static string PanelKind(Build build, Diet eats) => $"{BuildName(build)} · eats {FoodsInSentence(eats)}";

    /// <summary>"4 hexes".</summary>
    internal static string SightValue(int hexes) => Hexes(hexes);

    /// <summary>"62% grown · next star at 150%".</summary>
    internal static string ServerGrown(int percent, int nextStar) => $"{Count(percent)}% grown · next star at {Count(nextStar)}%";

    /// <summary>"340% grown · every star".</summary>
    internal static string ServerGrownAll(int percent) => $"{Count(percent)}% grown · every star";

    /// <summary>"Growing one pays ×2.6 now".</summary>
    internal static string ServerPays(double fresh) => $"Growing one pays ×{OneDecimal(fresh)} now";

    /// <summary>"Tougher hunters, more of them · pays ×1.5": the danger's pay as written in its table (×1.25 keeps two decimals).</summary>
    internal static string DangerMore(double pay) => $"Tougher hunters, more of them · pays ×{pay.ToString("0.##", Inv)}";

    /// <summary>The unlock button while the wallet is short: "Unlock: 400 amber".</summary>
    internal static string UnlockShort(long price) => $"Unlock: {Money(price)}";

    /// <summary>The unlock button: "Unlock for 400 amber".</summary>
    internal static string Unlock(long price) => $"Unlock for {Money(price)}";

    // ===== Run: the panel ================================================================

    public const string Fast = "Fast";
    public const string HealthLabel = "Health";
    public const string FoodLabel = "Food";
    public const string WaterLabel = "Water";
    public const string StatusForest = "Hidden in the forest";
    public const string StatusWater = "Hidden in the water";
    public const string StatusStill = "Hidden: standing still";
    public const string StatusWading = "Wading: slow and loud";
    public const string StatusOpen = "In the open";
    public const string NoInstinct = "No instinct yet";
    public const string YourMove = "YOUR MOVE";
    public const string Walk = "Walk";
    public const string Sneak = "Sneak";
    public const string Sprint = "Sprint";
    public const string Eat = "Eat";
    public const string Drink = "Drink";
    public const string Rest = "Rest";
    public const string EatNothing = "nothing here";
    public const string DrinkNothing = "no water";
    public const string TurnOrder = "TURN ORDER";
    public const string Leave = "Leave this run";
    public const string LeaveArmed = "Click again to leave this run";

    /// <summary>The marker on the turn list's row that is acting (or on "You" at rest).</summary>
    public const string RowMarker = "▸";

    /// <summary>"Juvenile · 31%".</summary>
    internal static string StageLine(int stage, int percent) => $"{Stage(stage)} · {Count(percent)}%";

    /// <summary>"38/62".</summary>
    internal static string HealthValue(int current, int max) => $"{Count(current)}/{Count(max)}";

    /// <summary>"Outlast the apex: 9 turns".</summary>
    internal static string StatusApex(int turnsLeft) => $"Outlast the apex: {Turns(turnsLeft)}";

    /// <summary>"Instincts: Keen eyes, Ambusher", or "No instinct yet".</summary>
    internal static string InstinctList(IEnumerable<Instinct> instincts)
    {
        var names = instincts.Select(InstinctName).ToList();
        return names.Count == 0 ? NoInstinct : "Instincts: " + string.Join(", ", names);
    }

    /// <summary>"noise 3".</summary>
    internal static string Noise(int noise) => $"noise {Count(noise)}";

    /// <summary>"2 hexes · noise 5".</summary>
    internal static string SprintLine(int range, int noise) => $"{Hexes(range)} · {Noise(noise)}";

    /// <summary>"+30 food".</summary>
    internal static string EatGain(int food) => $"+{Count(food)} food";

    /// <summary>"+40 water".</summary>
    internal static string DrinkGain(int water) => $"+{Count(water)} water";

    /// <summary>"+8% health".</summary>
    internal static string RestGain(int percent) => $"+{Count(percent)}% health";

    /// <summary>"turn 23".</summary>
    internal static string TurnNumber(int turn) => $"turn {Count(turn)}";

    // ===== Run: the banner ===============================================================

    public const string YourTurn = "Your turn";
    public const string ChooseInstinct = "Choose an instinct";
    public const string YouWalk = "You walk";
    public const string YouSneak = "You sneak";
    public const string YouSprint = "You sprint";
    public const string YouEat = "You eat";
    public const string YouDrink = "You drink";
    public const string YouRest = "You rest";
    public const string YouCaughtGame = "You catch small game";
    public const string SecondWind = "Second wind: you are still standing";
    public const string GameScatters = "Small game scatters";
    public const string GameMoves = "Small game moves";
    public const string UnseenQuiet = "Out of sight: quiet";
    public const string UnseenHeard = "Something heard you";
    public const string TimePasses = "Time passes";
    public const string Starving = "You are starving";
    public const string Parched = "You are parched";
    public const string StarvingAndParched = "You are starving and parched";
    public const string ApexScent = "A Tyrannosaurus has your scent";
    public const string EndFood = "You ran out of food";
    public const string EndWater = "You ran out of water";
    public const string EndOutlasted = "You outlasted the apex";
    public const string EndDefeated = "You defeated the apex";

    /// <summary>Added to a hurt's banner when the hurt defeated it.</summary>
    public const string AndDefeated = " and is defeated";

    internal static string YouHit(PredatorType type, int ordinal, int damage) => $"You hit {The(type, ordinal)}: {Count(damage)}";
    internal static string YouDefeated(PredatorType type, int ordinal) => $"You defeated {The(type, ordinal)}";
    internal static string AndHit(PredatorType type, int ordinal, int damage) => $"and {The(type, ordinal)}: {Count(damage)}";
    internal static string YouHitGame(int damage) => $"You hit small game: {Count(damage)}";
    internal static string AndHitGame(int damage) => $"and small game: {Count(damage)}";
    internal static string Spots(PredatorType type, int ordinal) => $"{Name(type, ordinal)} spots you";
    internal static string Hears(PredatorType type, int ordinal) => $"{Name(type, ordinal)} hears you";
    internal static string Loses(PredatorType type, int ordinal) => $"{Name(type, ordinal)} loses your trail";
    internal static string Hunts(PredatorType type, int ordinal) => $"{Name(type, ordinal)} hunts you";
    internal static string Searches(PredatorType type, int ordinal) => $"{Name(type, ordinal)} searches";
    internal static string Wanders(PredatorType type, int ordinal) => $"{Name(type, ordinal)} wanders";
    internal static string Waits(PredatorType type, int ordinal) => $"{Name(type, ordinal)} waits";
    internal static string IsDazed(PredatorType type, int ordinal) => $"{Name(type, ordinal)} is dazed";
    internal static string IsHeld(PredatorType type, int ordinal) => $"{Name(type, ordinal)} is held by venom";
    internal static string Bites(PredatorType type, int ordinal, int damage) => $"{Name(type, ordinal)} bites you: {Count(damage)}";
    internal static string YouDodge(PredatorType type, int ordinal) => $"You dodge {The(type, ordinal)}";
    internal static string SpikesHurt(PredatorType type, int ordinal, int damage, bool defeated) =>
        $"Your spikes hurt {The(type, ordinal)}: {Count(damage)}" + (defeated ? AndDefeated : "");
    internal static string Bleeds(PredatorType type, int ordinal, int damage, bool defeated) =>
        $"{Name(type, ordinal)} bleeds: {Count(damage)}" + (defeated ? AndDefeated : "");
    internal static string Arrives(PredatorType type, int ordinal) => $"{Name(type, ordinal)} arrives";
    internal static string GrewTo(int stage) => $"You grew to {Stage(stage)}";
    internal static string EndCaught(PredatorType type, int ordinal) => $"{TheAtStart(type, ordinal)} caught you";

    /// <summary>The end's banner for <paramref name="reason"/>; <paramref name="byType"/> and <paramref name="byOrdinal"/> name the one that caught you. Left has no beat, so no banner (it falls back to the summary's title).</summary>
    internal static string EndBanner(EndReason reason, PredatorType byType, int byOrdinal) => reason switch
    {
        EndReason.Caught => EndCaught(byType, byOrdinal),
        EndReason.Hunger => EndFood,
        EndReason.Thirst => EndWater,
        EndReason.Outlasted => EndOutlasted,
        EndReason.DefeatedApex => EndDefeated,
        _ => OverLost,
    };

    // ===== Run: the turn list ============================================================

    public const string RowYou = "You";
    public const string RowGame = "Small game";
    public const string RowUnseen = "Out of sight";
    public const string DidHatch = "hatched";
    public const string DidCatchGame = "caught small game";
    public const string ItSpotted = "spotted you";
    public const string ItHeard = "heard you";
    public const string ItLost = "lost your trail";
    public const string ItMissed = "missed you";
    public const string ItDazed = "dazed";
    public const string ItHeld = "held";
    public const string ItHunts = "hunting you";
    public const string ItSearches = "searching";
    public const string ItWandered = "wandered";
    public const string ItWaited = "waited";
    public const string ItArrived = "arrived";
    public const string ItDefeated = "defeated";
    public const string GameScattered = "scattered";
    public const string GameMoved = "moved";
    public const string GameStill = "still";
    public const string UnseenRowQuiet = "quiet";
    public const string UnseenRowHeard = "something heard you";

    internal static string DidWalk(int noise) => $"walked · {Noise(noise)}";
    internal static string DidSneak(int noise) => $"sneaked · {Noise(noise)}";
    internal static string DidSprint(int cells, int noise) => $"sprinted {Count(cells)} · {Noise(noise)}";
    internal static string DidEat(Food food, int gain) => $"ate {FoodWord(food)} · +{Count(gain)}";
    internal static string DidDrink(int gain) => $"drank · +{Count(gain)}";
    internal static string DidRest(int percent) => $"rested · +{Count(percent)}%";
    internal static string DidHit(PredatorType type, int ordinal, int damage) => $"hit {Name(type, ordinal)}: {Count(damage)}";
    internal static string DidDefeat(PredatorType type, int ordinal) => $"defeated {Name(type, ordinal)}";
    internal static string DidHitGame(int damage) => $"hit small game: {Count(damage)}";
    internal static string ItBit(int damage) => $"bit you: {Count(damage)}";

    /// <summary>Two things a row says: "spotted you · bit you: 12".</summary>
    internal static string Joined(string a, string b) => $"{a} · {b}";

    /// <summary>A row's name: "You", "Carnotaurus 2", "Small game", "Out of sight".</summary>
    internal static string RowName(TurnRow row) => row.Actor switch
    {
        RowActor.You => RowYou,
        RowActor.Predator => Name(row.Type, row.Ordinal),
        RowActor.SmallGame => RowGame,
        RowActor.Unseen => RowUnseen,
        _ => "",
    };

    /// <summary>
    /// What a row's actor did this turn, from its codes: the turn list's third
    /// column. A predator's notice comes first when both are worth saying
    /// ("spotted you · bit you: 12"); "spotted you" then "hunting you" is said as
    /// "spotted you", "heard you" then "searching" as "heard you", and "lost your
    /// trail" stands alone. A row with no codes (one still to act) says nothing.
    /// </summary>
    internal static string RowText(TurnRow row)
    {
        if (row.First == RowCode.Lost || row.Second == RowCode.Lost) return ItLost;
        var first = CodeText(row, row.First);
        var second = CodeText(row, row.Second);
        if (row.First == RowCode.Spotted && row.Second == RowCode.Hunting) return first;
        if (row.First == RowCode.Heard && row.Second == RowCode.Searching) return first;
        if (first.Length == 0) return second;
        if (second.Length == 0) return first;
        return Joined(first, second);
    }

    // One code's words, with the numbers and the target the row carries.
    private static string CodeText(TurnRow row, RowCode code) => code switch
    {
        RowCode.None => "",
        RowCode.Hatched => DidHatch,
        RowCode.Walked => DidWalk(row.A),
        RowCode.Sneaked => DidSneak(row.A),
        RowCode.Sprinted => DidSprint(row.A, row.B),
        RowCode.Ate => DidEat(row.Food, row.A),
        RowCode.Drank => DidDrink(row.A),
        RowCode.Rested => DidRest(row.A),
        RowCode.Hit => row.TargetIsSmallGame ? DidHitGame(row.A) : DidHit(row.TargetType, row.TargetOrdinal, row.A),
        RowCode.Defeated => row.TargetIsSmallGame ? DidCatchGame : DidDefeat(row.TargetType, row.TargetOrdinal),
        RowCode.Spotted => ItSpotted,
        RowCode.Heard => ItHeard,
        RowCode.Lost => ItLost,
        RowCode.Bit => ItBit(row.A),
        RowCode.Missed => ItMissed,
        RowCode.Dazed => ItDazed,
        RowCode.Held => ItHeld,
        RowCode.Hunting => ItHunts,
        RowCode.Searching => ItSearches,
        RowCode.Wandered => ItWandered,
        RowCode.Waited => ItWaited,
        RowCode.Arrived => ItArrived,
        RowCode.WasDefeated => ItDefeated,
        RowCode.Scattered => GameScattered,
        RowCode.Moved => GameMoved,
        RowCode.Still => GameStill,
        RowCode.Quiet => UnseenRowQuiet,
        RowCode.HeardYou => UnseenRowHeard,
        _ => "",
    };

    // ===== Run: the offer and the summary ================================================

    public const string OfferStart = "A new instinct";
    public const string OfferLine = "Choose one. It lasts for this run.";
    public const string RedrawFree = "Redraw: free";
    public const string OverWonOutlast = "You outlasted the apex";
    public const string OverWonDefeat = "You defeated the apex";
    public const string OverLost = "Your run ended";
    public const string ReasonFood = "You ran out of food.";
    public const string ReasonWater = "You ran out of water.";
    public const string ReasonLeft = "You left the run.";
    public const string ReasonOutlasted = "The Tyrannosaurus gave up.";
    public const string ReasonDefeated = "The apex is defeated.";
    public const string PayOutlasted = "Outlasted the apex";
    public const string PayApex = "Defeated the apex";
    public static readonly string PayTotal = $"{CurrencyCapital} from this run";
    public const string BackToCamp = "Back to camp";

    /// <summary>The offer's title for one owed by a stage: "You grew to Juvenile".</summary>
    internal static string OfferGrew(int stage) => GrewTo(stage);

    /// <summary>"Redraw: 25 amber".</summary>
    internal static string Redraw(long price) => $"Redraw: {Money(price)}";

    /// <summary>"The Carnotaurus 2 caught you as an Adult."</summary>
    internal static string ReasonCaught(PredatorType type, int ordinal, int stage) =>
        $"{TheAtStart(type, ordinal)} caught you as {AStage(stage)}.";

    /// <summary>The summary's title.</summary>
    internal static string OverTitle(RunOver over) =>
        !over.Won ? OverLost : over.Reason == EndReason.DefeatedApex ? OverWonDefeat : OverWonOutlast;

    /// <summary>The summary's reason.</summary>
    internal static string OverReason(RunOver over) => over.Reason switch
    {
        EndReason.Caught => ReasonCaught(over.ByType, over.ByOrdinal, over.Stage),
        EndReason.Hunger => ReasonFood,
        EndReason.Thirst => ReasonWater,
        EndReason.Left => ReasonLeft,
        EndReason.Outlasted => ReasonOutlasted,
        EndReason.DefeatedApex => ReasonDefeated,
        _ => "",
    };

    internal static string PayGrew(int percent) => $"Grew {Count(percent)}%";
    internal static string PayReached(int stage) => $"Reached {Stage(stage)}";
    internal static string PayDefeated(int count) => $"Predators defeated: {Count(count)}";
    internal static string PayDanger(int danger) => $"Danger {Count(danger)}";
    internal static string PayLegacy(int percent) => $"Legacy +{Count(percent)}%";

    /// <summary>A pay line's words (its amount is <see cref="Plus"/>, at the right).</summary>
    internal static string PayLineText(PayLine line) => line.Kind switch
    {
        PayKind.Grew => PayGrew(line.Value),
        PayKind.Reached => PayReached(line.Value),
        PayKind.Defeated => PayDefeated(line.Value),
        PayKind.Outlasted => PayOutlasted,
        PayKind.DefeatedApex => PayApex,
        PayKind.Danger => PayDanger(line.Value),
        PayKind.Legacy => PayLegacy(line.Value),
        _ => "",
    };

    internal static string PayAchievement(string name) => $"Achievement: {name}";
    internal static string PayMore(int count) => $"and {Count(count)} more";
    internal static string DangerUnlocked(int danger) => $"Danger {Count(danger)} unlocked";

    // ===== Run: the context line =========================================================

    public const string Skip = "Click the board to skip";
    public const string StateScent = "has your scent";
    public const string StateHunting = "hunting you";
    public const string StateHeard = "heard you";
    public const string StateUnaware = "unaware";
    public const string ClickAttack = " · click to attack";
    public const string SeeGame = "Small game";
    public const string SeePlants = "Plants";
    public const string SeeForest = "Forest";
    public const string SeeForestHides = " · hides you from their eyes";
    public const string SeeWater = "Water · Drink on it or beside it";
    public const string SeeWaterWade = " · wading is slow and loud";
    public const string SeeRock = "Rock · no way through";
    public const string SeeRockFly = "Rock · only you can cross it";
    public const string SeeGrass = "Grass";
    public const string SeeUnseen = "Not seen yet";
    public const string HintBeside = "It is beside you. Click it to fight, or get away.";
    public const string HintHuntedHide = "It hunts you. Break its line of sight in forest, or outrun it.";
    public const string HintHunted = "It hunts you. Keep your distance, or fight.";
    public const string HintRest = "Rest to heal.";
    public const string HintThirst = "Thirsty: stand on water or beside it, and Drink.";
    public const string HintHungerPlants = "Hungry: stand on plants and Eat.";
    public const string HintHungerMeat = "Hungry: Eat at a carcass, or catch small game.";
    public const string HintFirst = "Click a lit hex to move. Predators hear noise: Sneak is quiet, Sprint is loud.";
    public const string HintGrow = "Fed and watered, you grow every turn.";

    /// <summary>"Walk here · noise 3 · ", the move's prefix to what the cell holds.</summary>
    internal static string MoveWalk(int noise) => $"Walk here · {Noise(noise)} · ";
    internal static string MoveSneak(int noise) => $"Sneak here · {Noise(noise)} · ";
    internal static string MoveSprint(int noise) => $"Sprint here · {Noise(noise)} · ";

    /// <summary>"Carnotaurus 2 · hunting you · 3 hexes"; <paramref name="state"/> is one of the State constants.</summary>
    internal static string SeePredator(PredatorType type, int ordinal, string state, int hexes) =>
        $"{Name(type, ordinal)} · {state} · {Hexes(hexes)}";

    /// <summary>"You · Juvenile Dryosaurus".</summary>
    internal static string SeeYou(int stage, string species) => $"You · {Stage(stage)} {species}";

    /// <summary>" · stand here and Eat: +30 food".</summary>
    internal static string SeePlantsEat(int food) => $" · stand here and Eat: +{Count(food)} food";

    /// <summary>"Carcass · 2 meals".</summary>
    internal static string SeeCarcass(int meals) => $"Carcass · {Meals(meals)}";

    /// <summary>" · stand here and Eat: +45 food".</summary>
    internal static string SeeCarcassEat(int food) => $" · stand here and Eat: +{Count(food)} food";

    /// <summary>" · Eat here: +25 food".</summary>
    internal static string SeeWaterFish(int food) => $" · Eat here: +{Count(food)} food";

    // ===== Journal =======================================================================

    public const string JournalTitle = "Field journal";
    public static readonly string JournalIntro =
        $"Grow these on the server, in your own time. Only growth the overlay sees counts. It never asks for {A(SpeciesTable.JournalNever[0])} {SpeciesTable.JournalNever[0]} or {A(SpeciesTable.JournalNever[1])} {SpeciesTable.JournalNever[1]}.";
    public const string EntryNotPlaying = "Not the dino you are playing now";
    public const string SwapArmed = "Click again";
    public const string PaysCaption = "WHAT SERVER GROWTH PAYS";

    /// <summary>
    /// The Journal's one sentence on freshness (H88): what a rested species pays and
    /// where a species you keep growing settles, and nothing about it coming back.
    /// The rule says nothing about freshness beyond the public post's "a species you
    /// haven't grown in a while earns more", and "comes back by itself" invited
    /// waiting, the shape of an energy meter (G4).
    /// </summary>
    public static readonly string PaysNote =
        $"A species you have not grown lately pays ×{OneDecimal(Tuning.FreshMax)}. One you keep growing falls toward ×{OneDecimal(Tuning.FreshFloor)}.";
    public const string LatelyCaption = "ON THE SERVER LATELY";
    public const string LatelyNone = "Nothing yet. Grow your dino with the overlay running.";
    public const string LatelyNew = "new";
    public const string LatelyStarMark = "★";
    public const string StarsCaption = "STARS";
    public static readonly string StarsNote =
        $"A species earns a star at {Percent(Tuning.StarsAt[0] / 100)}%, {Percent(Tuning.StarsAt[1] / 100)}% and {Percent(Tuning.StarsAt[2] / 100)}% grown on the server. Each star gives it {Percent(Tuning.StarBonus)}% more health and attack in runs.";
    public const string RealDinoOut = "Not in game right now.";
    public const string RealDinoWaiting = "Waiting for the overlay's next reading.";

    /// <summary>"Grow a Maiasaura by 75%".</summary>
    internal static string Entry(string species, int target) => $"Grow {A(species)} {species} by {Count(target)}%";

    /// <summary>"32 of 75%".</summary>
    internal static string EntryProgress(int seen, int target) => $"{Count(seen)} of {Count(target)}%";

    /// <summary>"Your Maiasaura now: 42%".</summary>
    internal static string EntryPlaying(string species, int growth) => $"Your {species} now: {Count(growth)}%";

    /// <summary>"Swap: 50 amber".</summary>
    internal static string Swap(long price) => $"Swap: {Money(price)}";

    /// <summary>"Maiasaura · 62% grown".</summary>
    internal static string PaysRow(string species, int grown) => $"{species} · {Count(grown)}% grown";

    /// <summary>"×2.4".</summary>
    internal static string Times(double fresh) => $"×{OneDecimal(fresh)}";

    /// <summary>"Maiasaura grew 3.2% at ×2.4".</summary>
    internal static string LatelyGrew(string species, double percent, double fresh) =>
        $"{species} grew {OneDecimal(percent)}% at ×{OneDecimal(fresh)}";

    internal static string LatelyStar(string species) => $"{species}: a new star";
    internal static string LatelyUnlocked(string species) => $"{species} unlocked in Hatchling";
    internal static string LatelyJournal(string species, int target) => $"Journal: {species} by {Count(target)}%";

    /// <summary>Every star there is: three for each species.</summary>
    internal static int StarsInAll => SpeciesTable.All.Count * Tuning.StarsAt.Count;

    /// <summary>"Stars: 7 of 63".</summary>
    internal static string StarsCount(int stars) => $"Stars: {StarsOf(stars)}";

    /// <summary>"Your real dino: Maiasaura, 42%".</summary>
    internal static string RealDino(string species, int growth) => $"Your real dino: {species}, {Count(growth)}%";

    // ===== Records =======================================================================

    public const string AchievementsTitle = "Achievements";
    public const string Earned = "✓";
    public const string NotEarned = "○";
    public const string LineageCaption = "YOUR LINEAGE";
    public const string TotalRuns = "Runs";
    public const string TotalWins = "Wins";
    public const string TotalKills = "Predators defeated";
    public static readonly string TotalEarned = $"{CurrencyCapital} earned";
    public const string TotalSpecies = "Species unlocked";
    public const string TotalStars = "Stars";
    public const string TotalJournal = "Journal entries";
    public const string TotalDanger = "Highest danger won";
    public const string ChosenCaption = "THE CHOSEN SPECIES";

    /// <summary>"5 of 24".</summary>
    internal static string AchievementsCount(int earned, int total) => $"{Count(earned)} of {Count(total)}";

    /// <summary>"5 of 21".</summary>
    internal static string SpeciesCount(int unlocked) => $"{Count(unlocked)} of {Count(SpeciesTable.All.Count)}";

    /// <summary>"7 of 63".</summary>
    internal static string StarsOf(int stars) => $"{Count(stars)} of {Count(StarsInAll)}";

    /// <summary>"Dryosaurus: 14 runs · best 100% · won at danger 2"; with no win the last part is left out.</summary>
    internal static string ChosenLine(string species, int runs, int best, int wonAt) =>
        $"{species}: {Runs(runs)} · best {Count(best)}%" + (wonAt > 0 ? $" · won at danger {Count(wonAt)}" : "");

    // The 24 achievements' names and texts, by id, in the table's order (H26). The
    // ids, the order and the rewards are the achievement list's; the words are here.
    private static readonly (string Id, string Name, string Text)[] AchievementWords =
    {
        ("first", "First steps", "Finish a run"),
        ("juvenile", "Juvenile", "Grow to Juvenile in a run"),
        ("adult", "Grown up", "Grow to Adult in a run"),
        ("elder", "Elder", "Grow to Elder in a run"),
        ("win", "Outlasted", "Win a run"),
        ("slayer", "Apex no more", "Defeat the apex"),
        ("hunter", "Hunter", "Defeat 25 predators, over all runs"),
        ("underdog", "Underdog", "Win as a Runner or a Forager"),
        ("heavy", "Heavyweight", "Win as a Tank or an Apex"),
        ("shapes", "Every shape", "Win with all nine builds"),
        ("danger3", "Into danger", "Win at danger 3"),
        ("danger5", "Nothing left to fear", "Win at danger 5"),
        ("unseen", "Unseen", "Grow to Adult in a run without being bitten"),
        ("gentle", "Live and let live", "Win a run without defeating a predator"),
        ("six", "A wide lineage", "Unlock 6 species"),
        ("island", "The whole island", "Unlock all 21 species"),
        ("roots", "Deep roots", "Take 10 ranks of traits"),
        ("blood", "Ancient blood", "Complete the lineage"),
        ("researcher", "Field researcher", "Finish 3 journal entries"),
        ("naturalist", "Naturalist", "Finish 15 journal entries"),
        ("diverse", "Diverse", "Grow 5 species to Adult on the server"),
        ("star21", "Every species", "A star on all 21 species"),
        ("mastery", "Mastery", "Three stars on all 21 species"),
        ("all", "All of them", "Win a run with every species"),
    };

    /// <summary>The achievement ids that have words here, in the table's order: a test holds the achievement list to them.</summary>
    internal static IReadOnlyList<string> AchievementIds { get; } = AchievementWords.Select(a => a.Id).ToArray();

    /// <summary>An achievement's name. Throws for an id with no words: a defect the tests catch, since only the list's own ids are ever shown.</summary>
    internal static string AchievementName(string id) => Words(id).Name;

    /// <summary>What earns it.</summary>
    internal static string AchievementText(string id) => Words(id).Text;

    private static (string Id, string Name, string Text) Words(string id)
    {
        foreach (var words in AchievementWords)
            if (string.Equals(words.Id, id, StringComparison.Ordinal)) return words;
        throw new ArgumentOutOfRangeException(nameof(id), id, "no achievement has this id");
    }
}
