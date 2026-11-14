using PandoraOverlay.Games.Engine;

namespace PandoraOverlay.Games.Hatchling;

/// <summary>
/// Blows: armour, your hit with every perk and instinct that acts in it, and what
/// becomes of a creature that falls. This is where most of the 21 species differ
/// in the hand (H13): a Pachycephalosaurus dazes, a Troodon slows, an Allosaurus
/// bleeds, a Stegosaurus sweeps, a Tyrannosaurus ignores armour, each one exact
/// rule below. A predator's bite on you is the world's (H04) and joins this class.
///
/// <para>The members change the run they are given: they are called on a rule's
/// own copy (<see cref="TurnRules.Apply"/>, the world's turn), never on a state a
/// caller still holds (A20).</para>
/// </summary>
internal static class Fights
{
    /// <summary>
    /// What is left of a blow after <paramref name="defense"/>: each point takes
    /// <see cref="Tuning.ArmourPerDefense"/> off, up to <see cref="Tuning.ArmourMost"/>,
    /// so no defense ever makes a creature or you unhurt.
    /// </summary>
    public static double Armour(int defense) =>
        1 - Math.Min(Tuning.ArmourMost, Tuning.ArmourPerDefense * defense);

    /// <summary>
    /// Your hit on <paramref name="target"/> and, with Thagomizer, on the others
    /// beside you (the design's six steps), its events added to
    /// <paramref name="events"/>. True when the apex fell.
    ///
    /// <list type="number">
    /// <item>The boost: the product of Ambusher (x1.5) and Pounce (x2) from cover,
    /// Lunge (x2) from water, Charge (x2) on a creature you never hit, Latch
    /// (x1 + 0.25 a hit already landed on it in a row, four counted at most) and Spit
    /// from two cells (x0.6), capped at <see cref="Tuning.MaxBoost"/>.</item>
    /// <item>The damage: <c>max(1, Round(Attack * boost * Swing * armour))</c>, one
    /// roll for the swing; Crushing bite ignores the armour.</item>
    /// <item>The target loses it; at 0 or less it falls (<see cref="Defeat"/>).</item>
    /// <item>If it lives and is a predator: Headbutt dazes it (not again for three
    /// turns), Venom slows it, Bleeder makes it bleed a quarter of the hit. Small game
    /// takes the damage and nothing else (H51): the world's turn reads no daze, slow
    /// or bleed on it, so one would be a mark that never acts.</item>
    /// <item>Thagomizer: every OTHER creature beside you, in id order, takes 60% of
    /// your attack with a swing of its own and no boost, by steps 3 and 4.</item>
    /// <item>The target is marked as hit by you (Charge is spent on it), and the
    /// Latch streak moves to it: one more if it was already on it, else 1.</item>
    /// </list>
    ///
    /// The rolls are taken in that order: the target's swing, then one for each
    /// swept creature in id order. The blow resolves whole before the caller ends a
    /// run the apex's fall has won: the sweep is part of the same blow. The hit's
    /// noise rides on the first <see cref="Struck"/> only, so a sweep never counts
    /// the same noise twice; it is an attack's, through your build (<see cref="Moves.NoiseOf"/>).
    /// </summary>
    public static bool YourHit(RunState run, Stats stats, Rng rng, Creature target, List<TurnEvent> events)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(stats);
        ArgumentNullException.ThrowIfNull(rng);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(events);

        var you = run.You.At;
        var perk = stats.Species.Perk;
        var ranged = Island.Grid.Distance(you, target.At) > 1;
        var streak = run.You.LatchOn == target.Id ? run.You.LatchStreak : 0;
        var noise = Moves.NoiseOf(stats, Tuning.AttackNoise);

        // The others beside you are the ones standing there as the blow lands: taken
        // before the target can fall, though the target is never one of them.
        var swept = perk == Perk.Thagomizer
            ? run.Creatures.Where(c => c.Id != target.Id && Island.Grid.Distance(you, c.At) == 1).OrderBy(c => c.Id).ToList()
            : new List<Creature>();

        // 1 and 2: the boost and the damage.
        var boost = Boost(run, stats, target, ranged, streak);
        var damage = Damage(stats.Attack * boost, rng, perk, target);
        var apexFell = Land(run, stats, target, damage, events, boosted: boost > 1, ranged, sweep: false, noise);

        // 5: Thagomizer's sweep, a swing each, in id order.
        foreach (var other in swept)
        {
            var sweepDamage = Damage(stats.Attack * Tuning.SweepShare, rng, perk, other);
            apexFell |= Land(run, stats, other, sweepDamage, events, boosted: false, ranged: false, sweep: true, noise: 0);
        }

        // 6: Charge spent on the target, and the Latch streak on it.
        target.HitByYou = true;
        run.You.LatchOn = target.Id;
        run.You.LatchStreak = streak + 1;
        return apexFell;
    }

    /// <summary>
    /// A creature at 0 health or less leaves: it is gone from the island, its cell
    /// gains its meat (a predator's table, small game <see cref="Tuning.SmallGameMeat"/>),
    /// and a predator that is not the apex counts as defeated (<see cref="RunState.Kills"/>;
    /// the apex is a win, not a kill). The caller's event says <c>Defeated</c>. Shared
    /// with the world's turn, where your spikes and its bleeding defeat a predator too.
    /// </summary>
    public static void Defeat(RunState run, Creature creature)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(creature);
        run.Creatures.RemoveAll(c => c.Id == creature.Id);
        run.Meat[creature.At] += MeatOf(creature);
        if (creature.Kind == CreatureKind.Predator && !IsApex(creature))
            run.Kills++;
    }

    /// <summary>A creature's defense: its predator table's, or small game's <see cref="Tuning.SmallGameDefense"/>.</summary>
    public static int DefenseOf(Creature creature) =>
        creature.Kind == CreatureKind.Predator ? Predators.Of(creature.Type).Defense : Tuning.SmallGameDefense;

    /// <summary>The meals a creature leaves when it falls: its predator table's, or small game's <see cref="Tuning.SmallGameMeat"/>.</summary>
    public static int MeatOf(Creature creature) =>
        creature.Kind == CreatureKind.Predator ? Predators.Of(creature.Type).Meat : Tuning.SmallGameMeat;

    /// <summary>The apex: the run's only Tyrannosaurus (as <see cref="Sight.Knows"/> tells it).</summary>
    public static bool IsApex(Creature creature) =>
        creature.Kind == CreatureKind.Predator && creature.Type == Tuning.Apex;

    /// <summary>
    /// Step 1: the product of every boost that applies to a hit on
    /// <paramref name="target"/>, capped at <see cref="Tuning.MaxBoost"/>. Cover is
    /// <see cref="Sight.InCover"/> (forest for a hider, water for a swimmer), not
    /// Freeze's stillness: an ambush comes from somewhere.
    /// </summary>
    internal static double Boost(RunState run, Stats stats, Creature target, bool ranged, int streak)
    {
        var perk = stats.Species.Perk;
        var cover = Sight.InCover(run, stats);
        var boost = 1.0;
        if (cover && run.Has(Instinct.Ambush)) boost *= Tuning.AmbusherBoost;
        if (cover && perk == Perk.Pounce) boost *= Tuning.PounceBoost;
        if (perk == Perk.Lunge && run.Tiles[run.You.At] == Terrain.Water) boost *= Tuning.LungeBoost;
        if (perk == Perk.Charge && !target.HitByYou) boost *= Tuning.ChargeBoost;
        if (perk == Perk.Latch) boost *= 1 + Tuning.LatchStep * Math.Min(streak, Tuning.LatchMost);
        if (ranged) boost *= Tuning.SpitShare;
        return Math.Min(Tuning.MaxBoost, boost);
    }

    /// <summary>
    /// Step 2: <c>max(1, Round(blow * Swing * armour))</c>, one roll for the swing
    /// (<see cref="Tuning.SwingBase"/> plus up to <see cref="Tuning.SwingSpread"/>);
    /// the armour is 1 for Crushing bite.
    /// </summary>
    private static int Damage(double blow, Rng rng, Perk perk, Creature target)
    {
        var swing = Tuning.SwingBase + Tuning.SwingSpread * rng.NextDouble();
        var armour = perk == Perk.CrushingBite ? 1 : Armour(DefenseOf(target));
        return (int)Math.Max(Tuning.LeastDamage, Tuning.Round(blow * swing * armour));
    }

    /// <summary>
    /// Steps 3 and 4 for one creature, the target or a swept one alike: it loses
    /// <paramref name="damage"/>; it falls, or, living and a predator, takes what your
    /// perk leaves on it (small game never does, H51). Its <see cref="Struck"/> comes
    /// first, its <see cref="Afflicted"/> right after. True when it was the apex and fell.
    /// </summary>
    private static bool Land(RunState run, Stats stats, Creature creature, int damage, List<TurnEvent> events,
        bool boosted, bool ranged, bool sweep, int noise)
    {
        creature.Health -= damage;
        var fell = creature.Health <= 0;
        events.Add(new Struck(creature.Id, creature.At, damage, boosted, ranged, sweep, fell, noise));
        if (fell)
        {
            Defeat(run, creature);
            return IsApex(creature);
        }
        if (creature.Kind != CreatureKind.Predator) return false;   // H51

        switch (stats.Species.Perk)
        {
            case Perk.Headbutt when run.Turn >= creature.DazeFreeAt:
                creature.Dazed = true;
                creature.DazeFreeAt = run.Turn + Tuning.HeadbuttAgainAfter;
                events.Add(new Afflicted(creature.Id, Affliction.Dazed));
                break;
            case Perk.Venom:
                creature.Slowed = Tuning.VenomActions;
                events.Add(new Afflicted(creature.Id, Affliction.Slowed));
                break;
            case Perk.Bleeder:
                // A new hit replaces the old bleed: its count starts again at this hit's share.
                creature.Bleed = Tuning.BleedActions;
                creature.BleedDamage = (int)Math.Max(Tuning.LeastDamage, Tuning.Round(damage * Tuning.BleedShare));
                events.Add(new Afflicted(creature.Id, Affliction.Bleeding));
                break;
        }
        return false;
    }

    // ---- A predator's bite (the world's turn) ------------------------------------------

    /// <summary>
    /// <paramref name="predator"/>, ending its action beside you, bites (the world's
    /// turn, B6). <paramref name="stats"/> are your numbers as the world's turn took
    /// them at its start: its dodge, defense and full health are what every bite of
    /// the turn reads.
    ///
    /// <list type="number">
    /// <item>One roll: under your <see cref="Stats.Dodge"/> you dodged
    /// (<see cref="Bit"/> with <c>Dodged</c>, 0 damage), and nothing more. The roll is
    /// taken whatever the chance, 0 included, so a dodge bought never shifts the rolls
    /// after it.</item>
    /// <item>The damage, a second roll for the swing:
    /// <c>max(1, Round(Attack * Threat * Swing * Armour(Defense)))</c>, the
    /// predator's table attack and the run's threat (H17).</item>
    /// <item>Braced (your move this turn was not a walk, a sneak or a sprint) takes
    /// it to <see cref="Tuning.BracedShare"/>, at least 1; Frill caps it at
    /// <see cref="Tuning.FrillMost"/> of your full health, rounded up.
    /// <c>Softened</c> says when either changed the number.</item>
    /// <item>You lose it, as a share of your full health; the run notes a bite
    /// landed (<see cref="You.Bitten"/>).</item>
    /// <item>At 0 health or less: Second wind, if still to spend, leaves you at
    /// <see cref="Tuning.SecondWindHealth"/> (<see cref="SecondWind"/>, right after the
    /// bite); else the run ends at once, caught by this predator.</item>
    /// <item>Spikes: the predator loses <see cref="Tuning.SpikesShare"/> of the bite,
    /// at least 1 (<see cref="Hurt"/>); at 0 it is defeated (<see cref="Defeat"/>),
    /// and the apex so defeated ends the run won.</item>
    /// </list>
    ///
    /// Second wind and the end come before the spikes because that is the order of
    /// a predator's events the turn script plays, which is the order things happen
    /// (H2): bite, Second wind, your spikes. So a bite that catches you ends the run
    /// before your spikes answer it. (The design's list of the bite's steps puts the
    /// spikes before the end; the events' order, which the player is shown, is the
    /// one followed here, so what is shown is what happened.)
    /// It changes the run given: called on the world's turn's own copy (A20).
    /// </summary>
    public static void Bite(RunState run, Stats stats, Rng rng, Creature predator, List<TurnEvent> events)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(stats);
        ArgumentNullException.ThrowIfNull(rng);
        ArgumentNullException.ThrowIfNull(predator);
        ArgumentNullException.ThrowIfNull(events);

        // 1. The dodge.
        if (rng.NextDouble() < stats.Dodge)
        {
            events.Add(new Bit(predator.Id, 0, Dodged: true, Softened: false));
            return;
        }

        // 2. The damage.
        var perk = stats.Species.Perk;
        var swing = Tuning.SwingBase + Tuning.SwingSpread * rng.NextDouble();
        var blow = Predators.Of(predator.Type).Attack * Island.Threat(run);
        var raw = (int)Math.Max(Tuning.LeastDamage, Tuning.Round(blow * swing * Armour(stats.Defense)));

        // 3. What your perk takes off it.
        var damage = raw;
        if (perk == Perk.Braced && !Moved(run.You.LastMove))
            damage = (int)Math.Max(Tuning.LeastDamage, Tuning.Round(damage * Tuning.BracedShare));
        if (perk == Perk.Frill)
            damage = Math.Min(damage, (int)Math.Ceiling(Tuning.FrillMost * stats.MaxHealth));

        // 4. You lose it.
        run.You.Health -= (double)damage / stats.MaxHealth;
        run.You.Bitten = true;
        events.Add(new Bit(predator.Id, damage, Dodged: false, Softened: damage != raw));

        // 5. Second wind, or caught.
        if (run.You.Health <= 0)
        {
            if (!run.You.Wind)
            {
                run.You.Health = 0;
                World.End(run, won: false, EndReason.Caught, predator, events);
                return;
            }
            run.You.Wind = false;
            run.You.Health = Tuning.SecondWindHealth;
            events.Add(new SecondWind());
        }

        // 6. Your spikes answer the bite.
        if (perk != Perk.Spikes) return;
        var back = (int)Math.Max(Tuning.LeastDamage, Tuning.Round(damage * Tuning.SpikesShare));
        predator.Health -= back;
        var fell = predator.Health <= 0;
        events.Add(new Hurt(predator.Id, predator.At, back, HurtBy.Spikes, fell));
        if (!fell) return;
        Defeat(run, predator);
        if (IsApex(predator)) World.End(run, won: true, EndReason.DefeatedApex, by: null, events);
    }

    /// <summary>Your move was a walk, a sneak or a sprint: Braced holds only when you stood your ground.</summary>
    private static bool Moved(MoveKind move) => move is MoveKind.Walk or MoveKind.Sneak or MoveKind.Sprint;
}
