using System.Reflection;
using PandoraOverlay.Games.Hatchling;
using Xunit;

namespace PandoraOverlay.Games.Tests;

/// <summary>
/// The game's words (A24, H35): every string filled, the amounts, articles and
/// names built as the design says, the turn list's rows read off their codes, and
/// the currency's word in one source file only.
/// </summary>
public class TextTests
{
    // ---- Nothing empty -----------------------------------------------------------------

    [Fact]
    public void NoFixedStringIsEmpty()
    {
        var fields = typeof(HatchlingText)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.FieldType == typeof(string))
            .ToList();
        Assert.True(fields.Count > 150, $"only {fields.Count} strings found");
        Assert.All(fields, f => Assert.False(string.IsNullOrWhiteSpace((string?)f.GetValue(null)), f.Name));
    }

    [Fact]
    public void EveryNameAndRuleOfEveryTableHasWords()
    {
        foreach (var build in Enum.GetValues<Build>())
        {
            Assert.NotEmpty(HatchlingText.BuildName(build));
            Assert.NotEmpty(HatchlingText.BuildLine(build));
        }
        foreach (var perk in Enum.GetValues<Perk>())
        {
            Assert.NotEmpty(HatchlingText.PerkName(perk));
            Assert.NotEmpty(HatchlingText.PerkText(perk));
        }
        foreach (var instinct in Enum.GetValues<Instinct>())
        {
            Assert.NotEmpty(HatchlingText.InstinctName(instinct));
            Assert.NotEmpty(HatchlingText.InstinctText(instinct));
        }
        foreach (var trait in Enum.GetValues<Trait>())
        {
            Assert.NotEmpty(HatchlingText.TraitName(trait));
            Assert.NotEmpty(HatchlingText.TraitText(trait));
        }
        for (var branch = 0; branch < Lineage.Branches; branch++) Assert.NotEmpty(HatchlingText.BranchName(branch));
        for (var stage = 0; stage <= 4; stage++) Assert.NotEmpty(HatchlingText.Stage(stage));
        foreach (var food in Enum.GetValues<Food>()) Assert.NotEmpty(HatchlingText.FoodWord(food));
        Assert.Equal(24, HatchlingText.AchievementIds.Count);
        Assert.Equal(24, HatchlingText.AchievementIds.Distinct(StringComparer.Ordinal).Count());
        foreach (var id in HatchlingText.AchievementIds)
        {
            Assert.NotEmpty(HatchlingText.AchievementName(id));
            Assert.NotEmpty(HatchlingText.AchievementText(id));
        }
        Assert.Throws<ArgumentOutOfRangeException>(() => HatchlingText.AchievementName("nope"));
    }

    // ---- Amounts, articles, names -------------------------------------------------------

    [Fact]
    public void MoneyHasAThousandsCommaAndTheCurrency()
    {
        Assert.Equal("1,234,567 " + HatchlingText.Currency, HatchlingText.Money(1234567));
        Assert.Equal("0 " + HatchlingText.Currency, HatchlingText.Money(0));
        Assert.Equal("+60", HatchlingText.Plus(60));
        Assert.Equal("+1,240", HatchlingText.Plus(1240));
    }

    [Theory]
    [InlineData("Allosaurus", "an")]
    [InlineData("Elder", "an")]
    [InlineData("Omniraptor", "an")]
    [InlineData("Maiasaura", "a")]
    [InlineData("Juvenile", "a")]
    [InlineData("Hatchling", "a")]
    public void TheArticleIsAnBeforeAVowel(string name, string article)
    {
        Assert.Equal(article, HatchlingText.A(name));
    }

    [Fact]
    public void APredatorIsNamedByItsSpeciesWithANumberFromTheSecond()
    {
        Assert.Equal("Carnotaurus", HatchlingText.Name(PredatorType.Carnotaurus, 1));
        Assert.Equal("Carnotaurus 2", HatchlingText.Name(PredatorType.Carnotaurus, 2));
        Assert.Equal("the Troodon", HatchlingText.The(PredatorType.Troodon, 1));
        Assert.Equal("the Troodon 2", HatchlingText.The(PredatorType.Troodon, 2));
        Assert.Equal("The Allosaurus 3 caught you", HatchlingText.EndCaught(PredatorType.Allosaurus, 3));
    }

    [Fact]
    public void TheSingularsAreTheirOwn()
    {
        Assert.Equal("1 turn", HatchlingText.Turns(1));
        Assert.Equal("9 turns", HatchlingText.Turns(9));
        Assert.Equal("1 hex", HatchlingText.Hexes(1));
        Assert.Equal("3 hexes", HatchlingText.Hexes(3));
        Assert.Equal("Carcass · 1 meal", HatchlingText.SeeCarcass(1));
        Assert.Equal("Carcass · 2 meals", HatchlingText.SeeCarcass(2));
        Assert.Equal("Outlast the apex: 1 turn", HatchlingText.StatusApex(1));
        Assert.Equal("Outlast the apex: 21 turns", HatchlingText.StatusApex(21));
        Assert.Equal("Dryosaurus: 1 run · best 40%", HatchlingText.ChosenLine("Dryosaurus", 1, 40, 0));
        Assert.Equal("Dryosaurus: 14 runs · best 100% · won at danger 2", HatchlingText.ChosenLine("Dryosaurus", 14, 100, 2));
        Assert.Equal("Carnotaurus 2 · hunting you · 1 hex",
            HatchlingText.SeePredator(PredatorType.Carnotaurus, 2, HatchlingText.StateHunting, 1));
    }

    // ---- The words the design gives, word for word ----------------------------------------

    [Fact]
    public void TheSentencesBuiltFromNumbersReadAsTheDesignWritesThem()
    {
        var currency = HatchlingText.Currency;
        Assert.Equal("Bites hurt 30% less on a turn you did not move.", HatchlingText.PerkText(Perk.Braced));
        Assert.Equal("No bite takes more than 7% of its full health.", HatchlingText.PerkText(Perk.Frill));
        Assert.Equal("Everything you do makes 1 less noise.", HatchlingText.InstinctText(Instinct.Feet));
        Assert.Equal("+2 defense.", HatchlingText.InstinctText(Instinct.Skin));
        Assert.Equal("Dodge 15% more bites.", HatchlingText.InstinctText(Instinct.Quick));
        Assert.Equal("+20% attack.", HatchlingText.InstinctText(Instinct.Fierce));
        Assert.Equal("+15% health.", HatchlingText.InstinctText(Instinct.Hardy));
        Assert.Equal("Food drains 8% slower a rank.", HatchlingText.TraitText(Trait.DeepStomach));
        Assert.Equal("+1 defense a rank.", HatchlingText.TraitText(Trait.ThickHide));
        Assert.Equal("Water drains 10% slower a rank.", HatchlingText.TraitText(Trait.DrySpells));
        Assert.Equal("Once a run, a blow that would end it leaves you at 25% health.", HatchlingText.TraitText(Trait.SecondWind));
        Assert.Equal("Dodge 6% more bites a rank.", HatchlingText.TraitText(Trait.QuickReflexes));
        Assert.Equal("Everything you do makes 1 less noise a rank.", HatchlingText.TraitText(Trait.LightStep));
        Assert.Equal("Grow 6% faster a rank.", HatchlingText.TraitText(Trait.FastMetabolism));
        Assert.Equal("Start each run 10% grown a rank.", HatchlingText.TraitText(Trait.StrongStart));
        Assert.Equal($"10% more {currency} from runs a rank.", HatchlingText.TraitText(Trait.Legacy));
        Assert.Equal("The apex gives up 5 turns sooner.", HatchlingText.TraitText(Trait.EldersPatience));
        Assert.Equal("A species earns a star at 50%, 150% and 300% grown on the server. Each star gives it 5% more health and attack in runs.",
            HatchlingText.StarsNote);
        Assert.Equal("A species you have not grown lately pays ×3.0. One you keep growing falls toward ×1.0 and comes back by itself.",
            HatchlingText.PaysNote);
        Assert.Equal("Grow these on the server, in your own time. Only growth the overlay sees counts. It never asks for a Tyrannosaurus or a Deinosuchus.",
            HatchlingText.JournalIntro);
        Assert.Equal($"Grow from a hatchling to an Elder, then outlast the apex. Predators hear noise, and forest hides most dinos. Every run earns {currency} for the lineage.",
            HatchlingText.NoteFirst);
        Assert.Equal(char.ToUpperInvariant(currency[0]) + currency[1..] + " from this run", HatchlingText.PayTotal);
    }

    [Fact]
    public void CampsAndTheRunsValuesReadAsTheDesignShowsThem()
    {
        Assert.Equal("Grazer · plants", HatchlingText.CardKind(Build.Grazer, Diet.Plants));
        Assert.Equal("Forager · eats plants and meat", HatchlingText.PanelKind(Build.Forager, Diet.Plants | Diet.Meat));
        // H37: three foods in a sentence take commas and one "and"; on a card, commas only.
        Assert.Equal("Forager · eats plants, meat and fish", HatchlingText.PanelKind(Build.Forager, Diet.Plants | Diet.Meat | Diet.Fish));
        Assert.Equal("Forager · plants, meat, fish", HatchlingText.CardKind(Build.Forager, Diet.Plants | Diet.Meat | Diet.Fish));
        Assert.Equal("Grazer · eats plants", HatchlingText.PanelKind(Build.Grazer, Diet.Plants));
        Assert.Equal("Server growth ×2.6", HatchlingText.CardFresh(2.6));
        Assert.Equal("Server growth ×2.5", HatchlingText.CardFresh(2.45));   // halves up (H24)
        Assert.Equal("★★☆", HatchlingText.Stars(2));
        Assert.Equal("☆☆☆", HatchlingText.Stars(0));
        Assert.Equal("●●○○○", HatchlingText.Pips(2, 5));
        Assert.Equal("Tougher hunters, more of them · pays ×1.5", HatchlingText.DangerMore(Dangers.Of(3).Pay));
        Assert.Equal("Tougher hunters, more of them · pays ×1.25", HatchlingText.DangerMore(Dangers.Of(2).Pay));
        Assert.Equal("Needs Deep stomach first", HatchlingText.TraitNeeds(Trait.DeepStomach));
        Assert.Equal("Juvenile · 31%", HatchlingText.StageLine(1, 31));
        Assert.Equal("2 hexes · noise 5", HatchlingText.SprintLine(2, 5));
        Assert.Equal("Instincts: Keen eyes, Ambusher", HatchlingText.InstinctList(new[] { Instinct.Eyes, Instinct.Ambush }));
        Assert.Equal("No instinct yet", HatchlingText.InstinctList(Array.Empty<Instinct>()));
        Assert.Equal("Carnotaurus 2 bites you: 12", HatchlingText.Bites(PredatorType.Carnotaurus, 2, 12));
        Assert.Equal("Your spikes hurt the Carnotaurus 2: 4 and is defeated", HatchlingText.SpikesHurt(PredatorType.Carnotaurus, 2, 4, true));
        Assert.Equal("The Troodon caught you as an Adult.", HatchlingText.ReasonCaught(PredatorType.Troodon, 1, 3));
        Assert.Equal("Grow a Maiasaura by 75%", HatchlingText.Entry("Maiasaura", 75));
        Assert.Equal("Grow an Allosaurus by 50%", HatchlingText.Entry("Allosaurus", 50));
        Assert.Equal("Maiasaura grew 3.2% at ×2.4", HatchlingText.LatelyGrew("Maiasaura", 3.2, 2.4));
        Assert.Equal("Stars: 7 of 63", HatchlingText.StarsCount(7));
        Assert.Equal("5 of 21", HatchlingText.SpeciesCount(5));
        Assert.Equal($"5 of 21 species · 1,240 {HatchlingText.Currency}", HatchlingText.TileIdle(5, 1240));
        Assert.Equal("Under way: Juvenile Dryosaurus", HatchlingText.TileUnderWay(1, "Dryosaurus"));
        Assert.Equal("Walk here · noise 3 · ", HatchlingText.MoveWalk(3));
    }

    [Fact]
    public void APayLineReadsByItsKind()
    {
        Assert.Equal("Grew 45%", HatchlingText.PayLineText(new PayLine(PayKind.Grew, 45, 22)));
        Assert.Equal("Reached Subadult", HatchlingText.PayLineText(new PayLine(PayKind.Reached, 2, 10)));
        Assert.Equal("Predators defeated: 4", HatchlingText.PayLineText(new PayLine(PayKind.Defeated, 4, 12)));
        Assert.Equal("Outlasted the apex", HatchlingText.PayLineText(new PayLine(PayKind.Outlasted, 0, 60)));
        Assert.Equal("Defeated the apex", HatchlingText.PayLineText(new PayLine(PayKind.DefeatedApex, 0, 60)));
        Assert.Equal("Danger 3", HatchlingText.PayLineText(new PayLine(PayKind.Danger, 3, 98)));
        Assert.Equal("Legacy +20%", HatchlingText.PayLineText(new PayLine(PayKind.Legacy, 20, 59)));
    }

    // ---- The turn list's rows, from their codes -------------------------------------------

    private static TurnRow Row(RowActor actor, RowCode first, RowCode second = RowCode.None, int a = 0, int b = 0,
        PredatorType type = PredatorType.Carnotaurus, int ordinal = 2, Food food = Food.Plants,
        PredatorType targetType = PredatorType.Troodon, int targetOrdinal = 1, bool smallGame = false) =>
        new(actor, type, ordinal, 0, first, second, a, b, food, targetType, targetOrdinal, smallGame);

    [Fact]
    public void YourRowSaysWhatYouDid()
    {
        Assert.Equal("You", HatchlingText.RowName(Row(RowActor.You, RowCode.Walked)));
        Assert.Equal("hatched", HatchlingText.RowText(Row(RowActor.You, RowCode.Hatched)));
        Assert.Equal("walked · noise 3", HatchlingText.RowText(Row(RowActor.You, RowCode.Walked, a: 3)));
        Assert.Equal("sneaked · noise 1", HatchlingText.RowText(Row(RowActor.You, RowCode.Sneaked, a: 1)));
        Assert.Equal("sprinted 2 · noise 5", HatchlingText.RowText(Row(RowActor.You, RowCode.Sprinted, a: 2, b: 5)));
        Assert.Equal("ate plants · +30", HatchlingText.RowText(Row(RowActor.You, RowCode.Ate, a: 30)));
        Assert.Equal("ate meat · +45", HatchlingText.RowText(Row(RowActor.You, RowCode.Ate, a: 45, food: Food.Meat)));
        Assert.Equal("ate fish · +25", HatchlingText.RowText(Row(RowActor.You, RowCode.Ate, a: 25, food: Food.Fish)));
        Assert.Equal("drank · +40", HatchlingText.RowText(Row(RowActor.You, RowCode.Drank, a: 40)));
        Assert.Equal("rested · +8%", HatchlingText.RowText(Row(RowActor.You, RowCode.Rested, a: 8)));
        Assert.Equal("hit the Troodon: 7", HatchlingText.RowText(Row(RowActor.You, RowCode.Hit, a: 7)));
        Assert.Equal("defeated the Troodon", HatchlingText.RowText(Row(RowActor.You, RowCode.Defeated)));
        Assert.Equal("hit small game: 4", HatchlingText.RowText(Row(RowActor.You, RowCode.Hit, a: 4, smallGame: true)));
        Assert.Equal("caught small game", HatchlingText.RowText(Row(RowActor.You, RowCode.Defeated, smallGame: true)));
    }

    [Fact]
    public void APredatorsRowSaysWhatItNoticedBeforeWhatItDid()
    {
        Assert.Equal("Carnotaurus 2", HatchlingText.RowName(Row(RowActor.Predator, RowCode.Waited)));
        Assert.Equal("Troodon", HatchlingText.RowName(Row(RowActor.Predator, RowCode.Waited, type: PredatorType.Troodon, ordinal: 1)));
        Assert.Equal("spotted you · bit you: 12", HatchlingText.RowText(Row(RowActor.Predator, RowCode.Spotted, RowCode.Bit, a: 12)));
        Assert.Equal("spotted you", HatchlingText.RowText(Row(RowActor.Predator, RowCode.Spotted, RowCode.Hunting)));
        Assert.Equal("heard you", HatchlingText.RowText(Row(RowActor.Predator, RowCode.Heard, RowCode.Searching)));
        Assert.Equal("lost your trail", HatchlingText.RowText(Row(RowActor.Predator, RowCode.Lost, RowCode.Wandered)));
        Assert.Equal("bit you: 12", HatchlingText.RowText(Row(RowActor.Predator, RowCode.None, RowCode.Bit, a: 12)));
        Assert.Equal("missed you", HatchlingText.RowText(Row(RowActor.Predator, RowCode.None, RowCode.Missed)));
        Assert.Equal("hunting you", HatchlingText.RowText(Row(RowActor.Predator, RowCode.None, RowCode.Hunting)));
        Assert.Equal("dazed", HatchlingText.RowText(Row(RowActor.Predator, RowCode.None, RowCode.Dazed)));
        Assert.Equal("held", HatchlingText.RowText(Row(RowActor.Predator, RowCode.None, RowCode.Held)));
        Assert.Equal("searching", HatchlingText.RowText(Row(RowActor.Predator, RowCode.None, RowCode.Searching)));
        Assert.Equal("wandered", HatchlingText.RowText(Row(RowActor.Predator, RowCode.None, RowCode.Wandered)));
        Assert.Equal("waited", HatchlingText.RowText(Row(RowActor.Predator, RowCode.None, RowCode.Waited)));
        Assert.Equal("arrived", HatchlingText.RowText(Row(RowActor.Predator, RowCode.None, RowCode.Arrived)));
        Assert.Equal("defeated", HatchlingText.RowText(Row(RowActor.Predator, RowCode.None, RowCode.WasDefeated)));
        Assert.Equal("", HatchlingText.RowText(Row(RowActor.Predator, RowCode.None)));   // still to act
    }

    [Fact]
    public void SmallGameAndTheUnseenRowsSayTheirWord()
    {
        Assert.Equal("Small game", HatchlingText.RowName(Row(RowActor.SmallGame, RowCode.Scattered)));
        Assert.Equal("scattered", HatchlingText.RowText(Row(RowActor.SmallGame, RowCode.Scattered)));
        Assert.Equal("moved", HatchlingText.RowText(Row(RowActor.SmallGame, RowCode.Moved)));
        Assert.Equal("still", HatchlingText.RowText(Row(RowActor.SmallGame, RowCode.Still)));
        Assert.Equal("Out of sight", HatchlingText.RowName(Row(RowActor.Unseen, RowCode.Quiet)));
        Assert.Equal("quiet", HatchlingText.RowText(Row(RowActor.Unseen, RowCode.Quiet)));
        Assert.Equal("something heard you", HatchlingText.RowText(Row(RowActor.Unseen, RowCode.HeardYou)));
    }

    [Fact]
    public void EveryRowCodeHasWords()
    {
        foreach (var code in Enum.GetValues<RowCode>().Where(c => c != RowCode.None))
            Assert.NotEmpty(HatchlingText.RowText(Row(RowActor.Predator, code)));
    }

    // ---- The currency in one place (A24, H35) ---------------------------------------------

    private static IEnumerable<(string Path, string Text)> CodeAndXaml(IEnumerable<(string Path, string Text)> files) =>
        files.Where(f => f.Path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) || f.Path.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The word is a placeholder nobody checked against the server's own (H35), so
    /// a rename must be one edit: no identifier, comment, file name or string of
    /// the game holds it but in <c>HatchlingText.cs</c>, in any casing.
    /// </summary>
    [Fact]
    public void TheCurrencysWordIsInTheTextFileOnly()
    {
        var word = HatchlingText.Currency;
        var files = GameSource.Files();
        Assert.Contains(files, f => f.Path == "HatchlingText.cs");   // the scan reaches the game's source
        Assert.Empty(GameSource.LinesHolding(CodeAndXaml(files).Where(f => f.Path != "HatchlingText.cs"), word));
        Assert.DoesNotContain(files, f => f.Path.Contains(word, StringComparison.OrdinalIgnoreCase));
        Assert.NotEmpty(GameSource.LinesHolding(files.Where(f => f.Path == "HatchlingText.cs"), word));
    }

    [Fact]
    public void TheCurrencyScanFindsABadLineInAnyCasing()
    {
        var word = HatchlingText.Currency;
        var capital = char.ToUpperInvariant(word[0]) + word[1..];
        var files = new[]
        {
            ("Progress/Fine.cs", "var price = Tuning.SwapPrice;\n"),
            ("Progress/Bad.cs", $"// pays the {word} back\nvar x = 1;\nlong {capital}Total = 0;\n"),
            ("Views/Bad.xaml", $"<TextBlock Text=\"{word.ToUpperInvariant()}\" />\n"),
        };
        Assert.Equal(new[] { "Progress/Bad.cs:1", "Progress/Bad.cs:3", "Views/Bad.xaml:1" },
            GameSource.LinesHolding(CodeAndXaml(files), word));
    }
}
