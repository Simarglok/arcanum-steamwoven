using System.Collections.Immutable;
using Arcanum.Simulation.Combat;

namespace Arcanum.Simulation.Ai;

/// <summary>
/// Support and magic of the NPC AI (stage A5, research ARC-RES-059; slice I8, ARC-WORLD-088, the chooser and the list build re-read
/// in the retail 1.0.7.4 executable): the heal ladder, the spell and item choice of a fight, the list chooser and the eligibility
/// test with the original's refusal codes, as pure functions over an <see cref="IAiSupportWorld"/> and an <see cref="IAiRollSource"/>.
/// They write the pass record's choice (<see cref="AiPassChoice"/>), which perform carries out (slice I7, the goals of slice I8b); an
/// item AI-action entry is the chooser's immediate item use, which the host runs before it goes on.
/// </summary>
public static class AiSupportRules
{
    /// <summary>The heal ladder's thresholds in HP% and fatigue%.</summary>
    public const int CareThreshold = 30, SkillThreshold = 90, FatigueThreshold = 20, HeavyThreshold = 40, MediumThreshold = 55,
        LightThreshold = 70;

    /// <summary>The spell choice's chances and the HP% above which a led NPC and its leader stay calm.</summary>
    public const int SpellChance = 25, HighSpellChance = 50, CalmHealth = 50;

    /// <summary>The chooser starts at the first entry when its d100 is below this.</summary>
    public const int FirstEntryRoll = 80;

    /// <summary>A fail chance at or above this refuses an entry (refusal 8).</summary>
    public const int FailChanceLimit = 38;

    /// <summary>A line of fire at or above this cost refuses an entry (refusal 5).</summary>
    public const long LineOfFireLimit = 100;

    /// <summary>The healing college (white necromancy); its spells skip the fail-chance test on a target at or below
    /// <see cref="CareThreshold"/> % HP (an item spell also for a user at or above <see cref="RestedFatigue"/> % fatigue, SP8).</summary>
    public const int HealingCollege = 12;

    /// <summary>The user's fatigue% at which an item spell of the healing college skips the fail-chance test (SP8).</summary>
    public const int RestedFatigue = 80;

    /// <summary>Spells per college.</summary>
    public const int CollegeSpells = 5;

    // ---- heal ----

    /// <summary>
    /// Heal (0x4A8940), the pipeline's first step: nothing while the NPC sleeps or flees. The combat argument is 1, or 0 in combat
    /// mode when <c>d100 &gt; parameter 14</c> (<see cref="AiRollPurpose.HealCareRoll"/>). Targets in order, stopping at the first the
    /// helper acts on: the NPC, its leader (its PC leader, else its NPC leader), then that leader's followers (followers of followers
    /// included) without the NPC, or the NPC's own followers when it has no leader.
    /// </summary>
    [OriginalFunction(0x4A8940, OriginalFunctionScope.Full)]
    public static AiHealResult Heal(IAiSupportWorld world, string npc, IAiRollSource rolls, CombatAnomalyProfile profile)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(rolls);
        ArgumentNullException.ThrowIfNull(profile);
        var n = Get(world, npc);
        if (n.Asleep) return new(AiHealStop.Asleep, [], null);
        var state = world.Danger(npc).State;
        if (state == DangerState.Flee) return new(AiHealStop.Fleeing, [], null);
        var cares = !n.InCombatMode || rolls.Roll(AiRollPurpose.HealCareRoll, 1, 100) <= world.SupportParameters(npc).HealOthersChance;
        var attempts = ImmutableArray.CreateBuilder<AiHealAttempt>();
        var leader = n.PcLeader ?? n.Leader;
        var targets = new List<string> { npc };
        if (leader is not null) targets.Add(leader);
        targets.AddRange(world.Followers(leader ?? npc).Where(f => f != npc));
        foreach (var target in targets)
        {
            if (world.Find(target) is null) continue;
            var (attempt, choice) = HealOne(world, n, state, target, cares, rolls, profile);
            attempts.Add(attempt);
            if (attempt.Found) return new(AiHealStop.None, attempts.ToImmutable(), choice);
        }
        return new(AiHealStop.None, attempts.ToImmutable(), null);
    }

    /// <summary>
    /// The heal helper (0x4A8AA0) for one target, with the healer's lists and the chooser in mode 8: a mechanical target nothing
    /// (P4); a dead one the resurrect list, ending the helper; above 30 % HP with the combat argument 0 nothing; out of danger below
    /// 90 % the heal skill (10) with the NPC's healing item when the skill check accepts; in danger while the healer's fatigue is below
    /// 20 % the fatigue list (in the Steamwoven setting only for the healer itself, SP17,
    /// <see cref="CombatAnomalyProfile.HealFatigueRungForOthers"/>); heavy below 40 %, medium below 55 %, cure poison when poisoned,
    /// light below 70 % (90 % out of danger).
    /// </summary>
    [OriginalFunction(0x4A8AA0, OriginalFunctionScope.Full)]
    private static (AiHealAttempt Attempt, AiPassChoice? Choice) HealOne(IAiSupportWorld world, HostilityCritter n, DangerState state, string target,
        bool cares, IAiRollSource rolls, CombatAnomalyProfile profile)
    {
        var t = Get(world, target);
        var facts = world.Support(target);
        if (facts.Mechanical) return (new(target, AiHealStep.Mechanical, false, null), null);
        AiHealAttempt Rung(AiHealStep step, AiActionList list, out AiPassChoice? chosen)
        {
            var result = Choose(world, n.Id, null, AiChooserMode.Requested, world.List(n.Id, list), target, rolls);
            chosen = result.Choice;
            return new(target, step, result.Found, result);
        }
        AiPassChoice? choice;
        if (t.Dead) return (Rung(AiHealStep.Resurrect, AiActionList.Resurrect, out choice), choice);
        if (t.HealthPercent > CareThreshold && !cares) return (new(target, AiHealStep.NotHurtEnough, false, null), null);
        if (state == DangerState.None && t.HealthPercent < SkillThreshold && world.HealingItem(n.Id) is { } item && world.HealSkillAccepts(n.Id, item, target))
            return (new(target, AiHealStep.HealSkill, true, null), new(AiPassAction.UseSkill, -1, AiPassChoice.HealSkill, item, target));
        var rungs = new List<(bool Test, AiHealStep Step, AiActionList List)>
        {
            (state != DangerState.None && world.Support(n.Id).FatiguePercent < FatigueThreshold && (target == n.Id || profile.HealFatigueRungForOthers),
                AiHealStep.FatigueRecover, AiActionList.FatigueRecover),
            (t.HealthPercent < HeavyThreshold, AiHealStep.HealingHeavy, AiActionList.HealingHeavy),
            (t.HealthPercent < MediumThreshold, AiHealStep.HealingMedium, AiActionList.HealingMedium),
            (facts.PoisonLevel > 0, AiHealStep.CurePoison, AiActionList.CurePoison),
            (t.HealthPercent < LightThreshold || state == DangerState.None && t.HealthPercent < SkillThreshold, AiHealStep.HealingLight, AiActionList.HealingLight),
        };
        foreach (var (test, step, list) in rungs)
        {
            if (!test) continue;
            var attempt = Rung(step, list, out choice);
            if (attempt.Found) return (attempt, choice);
        }
        return (new(target, AiHealStep.Nothing, false, null), null);
    }

    // ---- spell choice in a fight ----

    /// <summary>
    /// The spell and item choice (0x4ABC20 → 0x4ABC70) for the danger update's target: nothing for an invulnerable or
    /// nigh-invulnerable NPC or with the "NPC fighting" switch off; otherwise a d100 against <see cref="Chance"/>
    /// (<see cref="AiRollPurpose.SpellChoiceRoll"/>), then the summon list (mode 1) when the NPC has no followers, the defensive
    /// list (mode 4) when <c>parameter 13 &lt; d100</c> (<see cref="AiRollPurpose.DefensiveListRoll"/>, P7), the offensive list
    /// (mode 2), the first list whose chooser finds an entry ending the choice. Without a choice the pass's action type is 0.
    /// </summary>
    [OriginalFunction(0x4ABC20, OriginalFunctionScope.Full)]
    public static AiSpellChoiceResult ChooseSpell(IAiSupportWorld world, string npc, string target, IAiRollSource rolls)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(rolls);
        var n = Get(world, npc);
        if (n.Invulnerable || n.NighInvulnerable) return new(AiSpellChoiceStop.Invulnerable, 0, [], null);
        var parameters = world.SupportParameters(npc);
        var chance = Chance(world, n, target, parameters);
        if (!world.NpcFighting) return new(AiSpellChoiceStop.FightingOff, chance, [], null);
        if (rolls.Roll(AiRollPurpose.SpellChoiceRoll, 1, 100) > chance) return new(AiSpellChoiceStop.NoRoll, chance, [], null);
        var lists = ImmutableArray.CreateBuilder<AiChooserResult>();
        AiSpellChoiceResult? Try(AiSpellChoiceStop stop, AiActionList list, AiChooserMode mode)
        {
            var result = Choose(world, npc, target, mode, world.List(npc, list), null, rolls);
            lists.Add(result);
            return result.Found ? new(stop, chance, lists.ToImmutable(), result.Choice) : null;
        }
        if (world.Followers(npc).Count == 0 && Try(AiSpellChoiceStop.Summon, AiActionList.Summon, AiChooserMode.Self) is { } summon) return summon;
        if (parameters.DefensiveSkip < rolls.Roll(AiRollPurpose.DefensiveListRoll, 1, 100) &&
            Try(AiSpellChoiceStop.Defensive, AiActionList.Defensive, AiChooserMode.PerSpell) is { } defensive) return defensive;
        return Try(AiSpellChoiceStop.Offensive, AiActionList.Offensive, AiChooserMode.PassTarget)
            ?? new(AiSpellChoiceStop.NothingFound, chance, lists.ToImmutable(), null);
    }

    /// <summary>
    /// The spell chance (0x4ABE20): 0 when the NPC has a leader, the NPC and the leader are both above 50 % HP and the target is not
    /// <see cref="Stronger"/>; otherwise 50 when parameter 15 is above 1, else 25.
    /// </summary>
    [OriginalFunction(0x4ABE20, OriginalFunctionScope.Full)]
    public static int Chance(IAiSupportWorld world, string npc, string target)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(target);
        return Chance(world, Get(world, npc), target, world.SupportParameters(npc));
    }

    private static int Chance(IAiSupportWorld world, HostilityCritter n, string target, AiSupportParameters parameters)
    {
        if (n.Leader is { } leader && world.Find(leader) is { } l && n.HealthPercent > CalmHealth && l.HealthPercent > CalmHealth &&
            !(world.Find(target) is { } t && Stronger(t.Level, n.Level)))
            return 0;
        return parameters.BackOffDistance > 1 ? HighSpellChance : SpellChance;
    }

    /// <summary>Stronger (0x4ABEB0): the target's level is at least the NPC's + 10 below level 20, + 5 from 20 to 39, + 0 from 40.</summary>
    [OriginalFunction(0x4ABEB0, OriginalFunctionScope.Full)]
    public static bool Stronger(int targetLevel, int npcLevel) =>
        targetLevel >= unchecked(npcLevel + (targetLevel < 20 ? 10 : targetLevel < 40 ? 5 : 0));

    /// <summary>
    /// The flee-spell step's choice (<see cref="MoraleRules.FleeSpell"/>, 0x4AAF50 in state FLEE): the chooser over the NPC's flee list
    /// in mode 1, aimed at the NPC itself; a found entry makes <see cref="MoraleRules.FleeSpell"/> set SPELL_FLEE.
    /// </summary>
    public static AiChooserResult ChooseFleeAction(IAiSupportWorld world, string npc, IAiRollSource rolls)
    {
        ArgumentNullException.ThrowIfNull(world);
        return Choose(world, npc, null, AiChooserMode.Self, world.List(npc, AiActionList.Flee), null, rolls);
    }

    // ---- the chooser ----

    /// <summary>
    /// The chooser (0x4ABF10) over one list: an empty list chooses nothing. The target by mode (1 the NPC, 2 the pass target, 8 the
    /// request's; 4 per entry, the pass target when the spell's second defensive value is not 0, else the NPC). The start is entry 0
    /// when <c>d100 &lt; 80</c> (<see cref="AiRollPurpose.ChooserStart"/>), else a random entry (<see cref="AiRollPurpose.ChooserEntry"/>);
    /// the entries are walked circularly from it. An item AI-action entry is used at once, unchecked, without writing the pass record
    /// (in mode 4 on the NPC itself in both settings: the original's out-of-bounds read decides there, SP18). Any other entry is
    /// skipped when the target is under the influence of the spell or <see cref="Eligibility"/> refuses; the first other entry becomes
    /// the choice: use item (2) when it has an item that passes <see cref="AiActionLists.UsesItem"/>, else cast (1).
    /// </summary>
    [OriginalFunction(0x4ABF10, OriginalFunctionScope.Partial)]
    public static AiChooserResult Choose(IAiSupportWorld world, string npc, string? passTarget, AiChooserMode mode, ImmutableArray<AiListEntry> list,
        string? requestTarget, IAiRollSource rolls)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(npc);
        ArgumentNullException.ThrowIfNull(rolls);
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode), mode, "An undefined chooser mode.");
        if (list.IsDefaultOrEmpty) return AiChooserResult.Empty;
        var n = list.Length;
        var start = rolls.Roll(AiRollPurpose.ChooserStart, 1, 100) < FirstEntryRoll ? 0 : rolls.Roll(AiRollPurpose.ChooserEntry, 0, n - 1);
        var checks = ImmutableArray.CreateBuilder<AiEntryCheck>();
        for (var i = 0; i < n; i++)
        {
            var index = (int)(((long)start + i) % n);
            var entry = list[index];
            var target = mode switch
            {
                AiChooserMode.Self => npc,
                AiChooserMode.PassTarget => passTarget,
                AiChooserMode.Requested => requestTarget,
                _ => !entry.IsItemAction && world.GlobalLists.DefensiveSecond(entry.Spell) != 0 ? passTarget : npc,
            };
            if (entry.IsItemAction)
            {
                checks.Add(new(index, entry, target, false, AiCastRefusal.Accepted));
                return new(AiChooserOutcome.ItemUsedAtOnce, entry, target, null, checks.ToImmutable());
            }
            if (target is not null && world.UnderInfluence(target, entry.Spell))
            {
                checks.Add(new(index, entry, target, true, AiCastRefusal.Accepted));
                continue;
            }
            var refusal = Eligibility(world, npc, entry.Item, target, entry.Spell);
            checks.Add(new(index, entry, target, false, refusal));
            if (refusal != AiCastRefusal.Accepted) continue;
            var action = entry.Item is { } item && AiActionLists.UsesItem(world.Item(item), world.Spell) ? AiPassAction.UseItem : AiPassAction.Cast;
            return new(AiChooserOutcome.Chosen, entry, target, new(action, entry.Spell, -1, entry.Item, target), checks.ToImmutable());
        }
        return new(AiChooserOutcome.NoneAccepted, null, null, null, checks.ToImmutable());
    }

    // ---- eligibility ----

    /// <summary>
    /// The eligibility test (0x4AE720) for a spell of the critter (<paramref name="item"/> null) or of an item, the first failed test
    /// in the original's order. Without an item: unknown (1); an NPC without a PC leader below 20 % fatigue (2); an NPC, a maintained
    /// spell and no free maintenance slot (6, exactly 0); the cast cost at least the current fatigue (2); intelligence (3); willpower
    /// (7); a cast-highest NPC and not spell <c>5 × college + level − 1</c> (4); no target → accepted; unless a healing-college spell on
    /// a target at or below 30 % HP, a fail chance of 38 or more (8); a target other than the critter with a line of fire of cost 100 or
    /// more or another object stopping it (5). With an item: maintenance (6), an empty mana store (2), no target → accepted, the item's
    /// fail chance (8; also skipped for a user at or above 80 % fatigue, SP8), the line of fire (5).
    /// </summary>
    [OriginalFunction(0x4AE720, OriginalFunctionScope.Full)]
    public static AiCastRefusal Eligibility(IAiSupportWorld world, string critter, string? item, string? target, int spell)
    {
        ArgumentNullException.ThrowIfNull(world);
        var c = Get(world, critter);
        var support = world.Support(critter);
        var npc = c.Kind == HostilityKind.Npc;
        var facts = world.Spell(spell);
        var college = spell / CollegeSpells;
        bool NoSlot() => npc && facts.Maintained && world.FreeMaintainSlots(critter) == 0;
        bool Spared(int fatigue) => college == HealingCollege && target is not null &&
            (fatigue >= RestedFatigue || world.Find(target) is { HealthPercent: <= CareThreshold });
        if (item is null)
        {
            if (!world.Knows(critter, spell)) return AiCastRefusal.Unknown;
            if (npc && c.PcLeader is null && support.FatiguePercent < FatigueThreshold) return AiCastRefusal.Tired;
            if (NoSlot()) return AiCastRefusal.Maintenance;
            if (world.CastCost(critter, spell) >= support.CurrentFatigue) return AiCastRefusal.Tired;
            if (c.Intelligence < facts.MinimumIntelligence) return AiCastRefusal.Intelligence;
            if (support.Willpower < facts.MinimumWillpower) return AiCastRefusal.Willpower;
            if (support.CastHighest && spell != unchecked(CollegeSpells * college + world.CollegeLevel(critter, college) - 1)) return AiCastRefusal.NotHighest;
            if (target is null) return AiCastRefusal.Accepted;
            if (!Spared(int.MinValue) && world.FailChance(critter, target, spell) >= FailChanceLimit) return AiCastRefusal.FailChance;
        }
        else
        {
            if (NoSlot()) return AiCastRefusal.Maintenance;
            if (world.Item(item).ManaStore == 0) return AiCastRefusal.Tired;
            if (target is null) return AiCastRefusal.Accepted;
            if (!Spared(support.FatiguePercent) && world.ItemFailChance(critter, item, target, spell) >= FailChanceLimit) return AiCastRefusal.FailChance;
        }
        if (target == critter) return AiCastRefusal.Accepted;
        var line = world.LineOfFire(critter, target);
        return line.Cost >= LineOfFireLimit || line.Blocker is { } blocker && blocker != target ? AiCastRefusal.LineOfFire : AiCastRefusal.Accepted;
    }

    private static HostilityCritter Get(IHostilityWorld world, string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        return world.Find(id) ?? throw new ArgumentException($"The world does not know '{id}'.", nameof(id));
    }
}
