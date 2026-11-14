namespace PandoraOverlay.Games.Hatchling;

// What a turn produces, in the order things happened. The events are
// the ONLY thing the playback and the turn list are made from (H2): one pure
// function (the turn script) turns them into both, so the two can never
// disagree, and there is no other log. The order is the rules' own (H1): your
// move, each predator by id, small game, then time and arrivals.

/// <summary>What became of a creature you hit.</summary>
internal enum Affliction { Dazed, Slowed, Bleeding }

/// <summary>What a predator noticed about you.</summary>
internal enum Notice { Spotted, Heard, LostTrail }

/// <summary>How a creature moved: a predator hunts (it sees you, or is the apex), searches (alerted, not seeing you) or wanders; small game flees or roams.</summary>
internal enum Gait { Hunt, Search, Wander, Flee, Roam }

/// <summary>Why a predator did not move.</summary>
internal enum Idle { Waits, Dazed, Held }

/// <summary>What hurt a creature outside a hit of yours.</summary>
internal enum HurtBy { Spikes, Bleed }

/// <summary>One thing that happened in a turn.</summary>
internal abstract record TurnEvent;

// ---- your move ------------------------------------------------------------------

/// <summary>
/// You walked, sneaked or sprinted: the first event of a turn whose move was a
/// move. <paramref name="Path"/> is the cells after <paramref name="From"/>,
/// ending where you stand (one for a walk or a sneak). <paramref name="Waded"/>:
/// the move entered water while wading. <paramref name="Noise"/> is the
/// move's noise after every modifier, never below 0.
/// </summary>
internal sealed record Walked(Pace Pace, int From, IReadOnlyList<int> Path, bool Waded, int Noise) : TurnEvent;

/// <summary>You ate: <paramref name="Gain"/> is the food gained, rounded, after the meal's factors (a plant is gone, a carcass lost a meal).</summary>
internal sealed record Ate(Food Kind, int Gain, int Noise) : TurnEvent;

/// <summary>You drank: <paramref name="Gain"/> is the water gained.</summary>
internal sealed record Drank(int Gain, int Noise) : TurnEvent;

/// <summary>You rested: <paramref name="Healed"/> is the health regained, in percent points.</summary>
internal sealed record Rested(int Healed, int Noise) : TurnEvent;

/// <summary>
/// Your hit landed on <paramref name="Creature"/> (an id), at cell
/// <paramref name="At"/>, for <paramref name="Damage"/> points.
/// <paramref name="Boosted"/>: a boost above 1 applied. <paramref name="Ranged"/>:
/// Spit, from two cells. <paramref name="Swept"/>: Thagomizer's strike on another
/// creature beside you, one event each, in id order, after the main hit.
/// <paramref name="Defeated"/>: it is gone and its meat is on its cell.
/// </summary>
internal sealed record Struck(int Creature, int At, int Damage, bool Boosted, bool Ranged, bool Swept, bool Defeated, int Noise) : TurnEvent;

/// <summary>A creature you hit and that lived is dazed (Headbutt), slowed (Venom) or bleeding (Bleeder); right after its <see cref="Struck"/>.</summary>
internal sealed record Afflicted(int Creature, Affliction Kind) : TurnEvent;

// ---- a creature's action --------------------------------------------------------

/// <summary>
/// A predator noticed you, at the start of its action. Spotted only when it did
/// not see you on its last action (<c>Creature.SawYou</c>); Heard only when its
/// alert was 0; LostTrail when its alert fell to 0.
/// </summary>
internal sealed record Noticed(int Creature, Notice Kind) : TurnEvent;

/// <summary>
/// A creature moved at least one cell: <paramref name="Path"/> is the cells after
/// <paramref name="From"/>, in order. Every living predator produces exactly one
/// of this or <see cref="Stayed"/>; small game produces this only when it moved.
/// </summary>
internal sealed record Stepped(int Creature, int From, IReadOnlyList<int> Path, Gait Gait) : TurnEvent;

/// <summary>
/// A predator did not move: Dazed (it lost the action), Held (on an alert, but
/// slowed to no step), Waits (any other).
/// </summary>
internal sealed record Stayed(int Creature, Idle Why) : TurnEvent;

/// <summary>
/// A predator ended its action beside you and bit: <paramref name="Damage"/> in
/// points of your health, 0 when <paramref name="Dodged"/>. <paramref name="Softened"/>:
/// Braced or Frill changed the number. At most one a predator a turn.
/// </summary>
internal sealed record Bit(int Creature, int Damage, bool Dodged, bool Softened) : TurnEvent;

/// <summary>
/// A creature was hurt by something other than your hit: your Spikes (right after
/// its bite) or its bleeding (at the end of its action). <paramref name="Defeated"/>:
/// it is gone.
/// </summary>
internal sealed record Hurt(int Creature, int At, int Damage, HurtBy By, bool Defeated) : TurnEvent;

/// <summary>A blow that would have ended the run left you at 25% health instead, spending Second wind.</summary>
internal sealed record SecondWind : TurnEvent;

// ---- the world ------------------------------------------------------------------

/// <summary>
/// Time passed, every turn: <paramref name="Hunger"/> and
/// <paramref name="Thirst"/> are the percent points of health lost to each,
/// <paramref name="Regained"/> those regained.
/// </summary>
internal sealed record TimePassed(int Hunger, int Thirst, int Regained) : TurnEvent;

/// <summary>You grew, from <paramref name="From"/> to <paramref name="To"/> percent.</summary>
internal sealed record Grew(double From, double To) : TurnEvent;

/// <summary>You crossed a stage's line this turn: 1 Juvenile to 4 Elder; one event for each line crossed.</summary>
internal sealed record StageReached(int Stage) : TurnEvent;

/// <summary>A predator or small game arrived at cell <paramref name="At"/>; <paramref name="Alerted"/>: a predator that came already heading for you.</summary>
internal sealed record Arrived(int Creature, int At, bool Alerted) : TurnEvent;

/// <summary>Plants grew, out of your sight by rule: never shown as a beat.</summary>
internal sealed record PlantsGrew(IReadOnlyList<int> Cells) : TurnEvent;

/// <summary>The apex came, when you reached Elder: from now on it is always shown (H6).</summary>
internal sealed record ApexArrived(int Creature, int At) : TurnEvent;

/// <summary>Every turn the apex is alive after its first: <paramref name="TurnsLeft"/> to outlast it.</summary>
internal sealed record ApexWaits(int TurnsLeft) : TurnEvent;

/// <summary>An offer of instincts opened: the choices, in the order shown.</summary>
internal sealed record InstinctsOffered(IReadOnlyList<Instinct> Choices) : TurnEvent;

/// <summary>The run ended: the last event of its last turn. <paramref name="By"/>: the creature that caught you, else 0.</summary>
internal sealed record RunEnded(bool Won, EndReason Reason, int By) : TurnEvent;
