using System.Collections.Immutable;

namespace Arcanum.Simulation.Ai;

/// <summary>The ten AI action lists (spell record line +4, <c>AI_Flee:</c> .. <c>AI_Resurrect:</c>; an item's AI action field 0x6A).</summary>
public enum AiActionList
{
    Flee = 0,
    Summon = 1,
    Defensive = 2,
    Offensive = 3,
    HealingLight = 4,
    HealingMedium = 5,
    HealingHeavy = 6,
    CurePoison = 7,
    FatigueRecover = 8,
    Resurrect = 9,
}

/// <summary>
/// One entry of an action list (16 bytes in the original): a spell (0..222) with the item that holds it (null for a spell the
/// critter knows), or <see cref="ItemAction"/> with an item whose AI action is the list.
/// </summary>
public readonly record struct AiListEntry(int Spell, string? Item)
{
    /// <summary>The spell of an item AI-action entry.</summary>
    public const int ItemAction = -1;

    public bool IsItemAction => Spell == ItemAction;
}

/// <summary>
/// The AI values of one spell record (line +4): one per <see cref="AiActionList"/>, −1 when the key is absent, and the second value
/// of <c>AI_Defensive:(value@second)</c> (spell record +0x154), which sends a defensive spell at the pass's target when not 0.
/// </summary>
public sealed record AiSpellValues
{
    public AiSpellValues(IReadOnlyList<int> values, int defensiveSecond)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Count != AiGlobalLists.Lists) throw new ArgumentException($"A spell has {AiGlobalLists.Lists} AI values.", nameof(values));
        Values = [.. values];
        DefensiveSecond = defensiveSecond;
    }

    public ImmutableArray<int> Values { get; }
    public int DefensiveSecond { get; }

    public int this[AiActionList list] => Values[(int)list];

    /// <summary>A record without AI keys.</summary>
    public static AiSpellValues None { get; } = new(Enumerable.Repeat(-1, AiGlobalLists.Lists).ToArray(), 0);

    public bool Equals(AiSpellValues? other) => other is not null && Values.SequenceEqual(other.Values) && DefensiveSecond == other.DefensiveSecond;
    public override int GetHashCode() => HashCode.Combine(Values.Aggregate(17, HashCode.Combine), DefensiveSecond);
}

/// <summary>
/// One critter as the support rules read it besides <see cref="HostilityCritter"/>: critter flag <see cref="Mechanical"/> 0x20000000,
/// NPC flag <see cref="CastHighest"/>, <see cref="FatiguePercent"/> (0x4AB430, signed,
/// truncated), <see cref="CurrentFatigue"/> (0x45D700), the poison level (stat 24), willpower (stat 6) and magick points (stat 22).
/// </summary>
public sealed record AiSupportCritter
{
    public bool Mechanical { get; init; }
    public bool CastHighest { get; init; }
    public int FatiguePercent { get; init; }
    public int CurrentFatigue { get; init; }
    public int PoisonLevel { get; init; }
    public int Willpower { get; init; }
    public int MagickPoints { get; init; }
}

/// <summary>The AI parameter slots the support rules read (the NPC's row, slice I1): 13 the defensive list's skip threshold,
/// 14 the per-pass chance (in combat mode) of also caring for targets above 30 % HP, 15 the spell-chance and back-off value.</summary>
public readonly record struct AiSupportParameters(int DefensiveSkip, int HealOthersChance, int BackOffDistance);

/// <summary>
/// An inventory item as the list build and the chooser read it: the mana store (field 0x69), the identified flag (item flag 0x1),
/// <see cref="IdentificationExempt"/> (0x504550 of the item's field 1 answers 4 or 5: food or a scroll, research ARC-RES-059), the
/// five spell slots (fields 0x64..0x68, −1 for none, the original's 10000), <see cref="ValidAiAction"/> (item flag 0x200000) and the
/// AI action (field 0x6A). Slots past the given ones count as empty.
/// </summary>
public sealed record AiListItem
{
    public int ManaStore { get; init; }
    public bool Identified { get; init; }
    public bool IdentificationExempt { get; init; }
    public ImmutableArray<int> Spells { get; init; } = [-1, -1, -1, -1, -1];
    public bool ValidAiAction { get; init; }
    public int AiAction { get; init; } = -1;
}

/// <summary>What the eligibility test reads of a spell record: maintained (maintenance period above 0, 0x4503A0), the minimum
/// intelligence and willpower (0x4B1750, 0x4B1760) and whether it has item triggers (record +0x38, read by 0x4CC160).</summary>
public readonly record struct AiSpellFacts(bool Maintained = false, int MinimumIntelligence = 0, int MinimumWillpower = 0, bool ItemTriggers = false);

/// <summary>The line of fire (0x4ADE00) from a critter to a target's tile: its cost and the first object that stops it (null: none).</summary>
public readonly record struct AiLineOfFire(long Cost, string? Blocker);

/// <summary>
/// What the heal ladder, the spell choice, the action lists and the chooser ask of the world, by object id, besides the hostility
/// facts. <see cref="List"/> is the list request (0x4CCA90) the host answers through <see cref="AiListCache.Request"/>; the magic
/// members are the magic domain's predicates (cast cost 0x4B1660, free maintenance count 0x450B40, fail-chance predictors
/// 0x453B20 and 0x453CC0, under the influence 0x459380, the item gate 0x456A10). Answers must be stable within one rule call,
/// except that the fail-chance predictors draw their d20 from the magic random source.
/// </summary>
public interface IAiSupportWorld : IHostilityWorld
{
    /// <summary>The support facts of an object <see cref="IHostilityWorld.Find"/> knows.</summary>
    AiSupportCritter Support(string id);
    /// <summary>The follower list with followers of followers (0x441260), in its order.</summary>
    IReadOnlyList<string> Followers(string id);
    AiSupportParameters SupportParameters(string npc);
    /// <summary>The global "NPC fighting" switch (0x5B50CC), on in a normal game.</summary>
    bool NpcFighting { get; }
    /// <summary>The ten global lists built at init.</summary>
    AiGlobalLists GlobalLists { get; }
    /// <summary>The critter's list (0x4CCA90): built fresh, or through the Original setting's cache.</summary>
    ImmutableArray<AiListEntry> List(string critter, AiActionList list);
    /// <summary>The wielded weapon (slot 1004) when it has item flag 0x40 (hexed), else null.</summary>
    string? HexedWeapon(string critter);
    /// <summary>The inventory (fields 0xED/0xEE) in its order.</summary>
    IReadOnlyList<string> Inventory(string critter);
    AiListItem Item(string item);
    /// <summary>The holder may use the item's spells (0x456A10, <c>SpellInvocationRules.HolderCanUseItem</c>).</summary>
    bool CanUseItem(string critter, string item);
    /// <summary>The critter knows the spell (0x4B1950).</summary>
    bool Knows(string critter, int spell);
    AiSpellFacts Spell(int spell);
    /// <summary>The critter's level in a college (0x4B1AB0).</summary>
    int CollegeLevel(string critter, int college);
    int CastCost(string critter, int spell);
    int FreeMaintainSlots(string critter);
    bool UnderInfluence(string target, int spell);
    int FailChance(string critter, string target, int spell);
    int ItemFailChance(string critter, string item, string target, int spell);
    AiLineOfFire LineOfFire(string critter, string target);
    /// <summary>The NPC's first healing item (the heal skill's supplementary item, 0x4C91F0), or null.</summary>
    string? HealingItem(string npc);
    /// <summary>The heal skill check (0x4AE570): a generic item with the healing flag on a PC or NPC that is neither undead nor
    /// mechanical.</summary>
    bool HealSkillAccepts(string npc, string item, string target);
}

/// <summary>Whom the chooser aims an entry at (the request's mode bits, tested in this order: 1, 2, 8; mode 4 decides per entry).</summary>
public enum AiChooserMode
{
    /// <summary>1: the NPC itself.</summary>
    Self = 1,
    /// <summary>2: the pass's target.</summary>
    PassTarget = 2,
    /// <summary>4: per entry, the pass target when the spell's second defensive value is not 0, else the NPC (SP18 for item entries).</summary>
    PerSpell = 4,
    /// <summary>8: the request's own target (the heal ladder's).</summary>
    Requested = 8,
}

/// <summary>The eligibility test's answer (0x4AE720): accepted, or the original's refusal code.</summary>
public enum AiCastRefusal
{
    Accepted = 0,
    /// <summary>1: the critter does not know the spell.</summary>
    Unknown = 1,
    /// <summary>2: too tired (an NPC without a PC leader below 20 % fatigue, or the cost at least the current fatigue), or an empty
    /// mana store.</summary>
    Tired = 2,
    /// <summary>3: intelligence below the spell's minimum.</summary>
    Intelligence = 3,
    /// <summary>4: a cast-highest NPC and not the highest spell of its college level.</summary>
    NotHighest = 4,
    /// <summary>5: the line of fire costs 100 or more or another object stops it.</summary>
    LineOfFire = 5,
    /// <summary>6: an NPC, a maintained spell and no free maintenance slot.</summary>
    Maintenance = 6,
    /// <summary>7: willpower below the spell's minimum.</summary>
    Willpower = 7,
    /// <summary>8: a fail chance of 38 % or more.</summary>
    FailChance = 8,
}

/// <summary>The pass record's choice fields the support rules write: the action, the spell, the skill, the item and the target.</summary>
public sealed record AiPassChoice(AiPassAction Action, int Spell, int Skill, string? Item, string? Target)
{
    /// <summary>The heal skill (10).</summary>
    public const int HealSkill = 10;
}

/// <summary>One visited list entry: whom it was aimed at and why it was skipped (<see cref="AiCastRefusal.Accepted"/> with
/// <see cref="UnderInfluence"/> false: taken).</summary>
public readonly record struct AiEntryCheck(int Index, AiListEntry Entry, string? Target, bool UnderInfluence, AiCastRefusal Refusal);

/// <summary>What the chooser did with one list.</summary>
public enum AiChooserOutcome
{
    /// <summary>An empty list: nothing, no roll.</summary>
    Empty,
    /// <summary>Every entry was skipped.</summary>
    NoneAccepted,
    /// <summary>An item AI-action entry: the item is used at once on <see cref="AiChooserResult.Target"/> (0x462CC0, the host's),
    /// with no check, and the pass record is not written.</summary>
    ItemUsedAtOnce,
    /// <summary>An entry passed: <see cref="AiChooserResult.Choice"/> is the pass record's new choice.</summary>
    Chosen,
}

/// <summary>The chooser's answer (0x4ABF10): the outcome, the entry and its target, the pass record's choice when written, and every
/// visited entry in order (the overlay's "why").</summary>
public sealed record AiChooserResult(AiChooserOutcome Outcome, AiListEntry? Entry, string? Target, AiPassChoice? Choice, ImmutableArray<AiEntryCheck> Checks)
{
    /// <summary>The chooser reports success (an entry was used or chosen).</summary>
    public bool Found => Outcome is AiChooserOutcome.ItemUsedAtOnce or AiChooserOutcome.Chosen;

    internal static AiChooserResult Empty { get; } = new(AiChooserOutcome.Empty, null, null, null, []);
}

/// <summary>A heal-ladder rung (0x4A8AA0) for one target, in the original's order.</summary>
public enum AiHealStep
{
    /// <summary>A mechanical target: never healed (P4).</summary>
    Mechanical,
    /// <summary>A dead target: the resurrect list; the helper ends whatever it gives.</summary>
    Resurrect,
    /// <summary>Above 30 % HP while the combat argument is 0: nothing.</summary>
    NotHurtEnough,
    /// <summary>Out of danger below 90 %: the heal skill with the NPC's healing item.</summary>
    HealSkill,
    FatigueRecover,
    HealingHeavy,
    HealingMedium,
    CurePoison,
    HealingLight,
    /// <summary>No rung found an entry.</summary>
    Nothing,
}

/// <summary>One target the heal ladder tried: the rung that ended the helper and its chooser answer (null for a rung without a
/// list, and for <see cref="AiHealStep.Nothing"/>).</summary>
public sealed record AiHealAttempt(string Target, AiHealStep Step, bool Found, AiChooserResult? Chooser);

/// <summary>Why the heal step (0x4A8940) did not try anyone.</summary>
public enum AiHealStop
{
    None,
    Asleep,
    Fleeing,
}

/// <summary>
/// The heal step's answer (0x4A8940): the targets tried in order and, when one was acted on, the choice the pass record takes
/// (null for an item used at once, <see cref="AiChooserOutcome.ItemUsedAtOnce"/>).
/// </summary>
public sealed record AiHealResult(AiHealStop Stop, ImmutableArray<AiHealAttempt> Attempts, AiPassChoice? Choice)
{
    /// <summary>Heal chose an action: the pipeline goes straight to perform.</summary>
    public bool Chose => !Attempts.IsDefaultOrEmpty && Attempts[^1].Found;

    /// <summary>The last attempt's chooser answer when it used an item at once.</summary>
    public AiChooserResult? ItemUse => Chose && Attempts[^1].Chooser is { Outcome: AiChooserOutcome.ItemUsedAtOnce } c ? c : null;
}

/// <summary>Where the spell choice (0x4ABC20) stopped.</summary>
public enum AiSpellChoiceStop
{
    /// <summary>Invulnerable or nigh-invulnerable: nothing, no roll.</summary>
    Invulnerable,
    /// <summary>The "NPC fighting" switch is off: nothing, no roll.</summary>
    FightingOff,
    /// <summary>The d100 was above the chance.</summary>
    NoRoll,
    /// <summary>The summon list (no followers) found an entry.</summary>
    Summon,
    /// <summary>The defensive list found an entry.</summary>
    Defensive,
    /// <summary>The offensive list found an entry.</summary>
    Offensive,
    /// <summary>No list found an entry.</summary>
    NothingFound,
}

/// <summary>
/// The spell choice's answer: where it stopped, the chance it rolled against, each list's chooser answer in order and the pass
/// record's choice (null: the action type becomes 0; also for an item used at once).
/// </summary>
public sealed record AiSpellChoiceResult(AiSpellChoiceStop Stop, int Chance, ImmutableArray<AiChooserResult> Lists, AiPassChoice? Choice)
{
    public AiChooserResult? ItemUse => Lists.IsDefaultOrEmpty ? null : Lists[^1] is { Outcome: AiChooserOutcome.ItemUsedAtOnce } c ? c : null;
}
