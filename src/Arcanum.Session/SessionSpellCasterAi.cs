using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Magic;

namespace Arcanum.Session;

/// <summary>
/// ARC-WORLD-102 (NPC AI slice I10, the casting host): the magic half an AI host answers the support rules' spell questions with
/// (<see cref="IAiSupportWorld"/>: the global lists, a critter's spell book, the record facts, the cast cost, the free maintenance
/// count, under the influence and the fail-chance predictor) and perform's cast (<see cref="CastByAi"/>, 0x4AC250), whose casting goal
/// is the host's through <see cref="StartGoal"/>, <see cref="Release"/> and <see cref="EndGoal"/>, as the spell UI's is.
/// </summary>
public sealed partial class SessionSpellCaster
{
    private AiGlobalLists? _aiLists;

    /// <summary>The ten global action lists (0x4CC240) of this host's records: every spell 0..222 by its record's AI values (line +4);
    /// a spell without a record has none. Built once.</summary>
    public AiGlobalLists AiLists => _aiLists ??= AiGlobalLists.Build([.. Enumerable.Range(0, AiGlobalLists.Spells).Select(AiValues)]);

    private AiSpellValues AiValues(int spell) => _records(spell)?.Data is { } data && data.AiPriorities.Count == AiGlobalLists.Lists
        ? new(data.AiPriorities, data.AiDefensiveSecond) : AiSpellValues.None;

    /// <summary>A critter's spell book: the hero's, an NPC's session record (a levelled follower, a mastery given here), else the book its
    /// fields store; null for anything else.</summary>
    public SpellBook? Book(string critter)
    {
        ArgumentNullException.ThrowIfNull(critter);
        if (critter == Hero) return _owner.QueryHeroSpellBook();
        return _map.Critter(critter) is { } npc ? _owner.QueryNpcSpellBook(critter) ?? npc.Book : null;
    }

    /// <summary>Whether the critter knows the spell (0x4B1950, <see cref="SpellBookRules.IsKnown"/>).</summary>
    public bool AiKnows(string critter, int spell) => Book(critter) is { } book && SpellBookRules.IsKnown(book, spell);

    /// <summary>The critter's rank in a college (0x4B1AB0), 0 without a book or for a college outside 0..15.</summary>
    public int AiCollegeLevel(string critter, int college) =>
        Book(critter) is { } book && college is >= 0 and < SpellBookRules.Colleges ? book.Ranks[college] : 0;

    /// <summary>What the eligibility test reads of a record: maintained (a maintenance period above 0, 0x4503A0), the minimum
    /// intelligence (0x4B1750: the constant 5) and willpower (0x4B1760: the loaded table's value) and its item triggers (+0x38).
    /// A spell without a record reads none of them.</summary>
    public AiSpellFacts AiSpell(int spell) => _records(spell) is { } record
        ? new(record.Run.MaintainPeriod > 0, SpellBookRules.MinimumIntelligence, record.Data.MinimumWillpower, record.Run.ItemTriggers)
        : default;

    /// <summary>The cost the AI weighs (0x4B1660, <see cref="SpellCostRules.CastCost"/>): the record's cost, doubled for a dwarf, halved
    /// for the caster's mastered college; 0 without a record.</summary>
    public int AiCastCost(string critter, int spell) =>
        _records(spell) is { } record ? SpellCostRules.CastCost(record.Run.Cost, spell, Caster(RunObject(critter))) : 0;

    /// <summary>The critter's free maintenance slots (0x450B40, <see cref="SpellRunRules.FreeMaintainSlots"/>) by its effective
    /// intelligence; 0 for anything but a PC or NPC.</summary>
    public int AiFreeMaintainSlots(string critter)
    {
        if (RunObject(critter) is not { IsCritter: true } who) return 0;
        var intelligence = Stat(who, (int)CharacterStat.Intelligence) ?? 0;
        return SpellRunRules.FreeMaintainSlots(_owner.QuerySpellRuns(), who, intelligence, Record, Profile);
    }

    /// <summary>Whether the target is under the influence of the spell (0x459380, <see cref="SpellRunRules.UnderInfluence"/>).</summary>
    public bool AiUnderInfluence(string target, int spell) => SpellRunRules.UnderInfluence(_owner.QuerySpellRuns(), target, spell);

    /// <summary>
    /// The fail-chance predictor (0x453B20, <see cref="SpellResistanceRules.FailChance"/>) of the critter's spell on the target, with
    /// its own d20 from the magic random source. The original passes the fate-maximised flag the last processed action left (a
    /// global); the session does not keep it, so the predictor reads it unset (inference: only a PC's armed "spell at maximum" sets
    /// it, for its own run's pass).
    /// </summary>
    public int AiFailChance(string critter, string target, int spell)
    {
        ArgumentNullException.ThrowIfNull(critter);
        ArgumentNullException.ThrowIfNull(target);
        if (_records(spell) is null) return 0;
        return SpellResistanceRules.FailChance(ResistRecord(spell), ResistTarget(spell, critter, RunObject(target)), false, Rolls, Profile);
    }

    /// <summary>
    /// Perform's cast (0x4AC250, ledger AG14, IV12) of <paramref name="caster"/>'s own spell at <paramref name="targetObject"/> (null:
    /// none): the invocation (0x455A20) with the caster as source and parent and the target object, in a turn-based round with the
    /// round's actor; its check (0x4564E0), a refusal doing nothing; then the run (0x455AC0 → 0x455C30), whose activation checks it a
    /// second time (so a technologist target's MG5 d100 is drawn twice) and whose casting goal, 26 or 82 by the record's casting
    /// animation, is the host's (<see cref="StartGoal"/>). Unlike the spell UI's cast (<see cref="CastAt"/>) it tests neither the cost
    /// nor the line of fire itself (the AI's eligibility test did). Nothing for a missing caster, record or target object. An item
    /// source (its magic call 0x4574D0 and a spent food or scroll) is not ready: the host does not choose item entries yet.
    /// </summary>
    public SpellCastAttempt CastByAi(string caster, int spell, string? targetObject)
    {
        ArgumentNullException.ThrowIfNull(caster);
        if (_records(spell) is not { } record || Facts(caster) is not { } facts) return new(SpellCastAttemptOutcome.Nothing);
        var target = targetObject is null ? null : Facts(targetObject);
        if (targetObject is not null && target is null) return new(SpellCastAttemptOutcome.Nothing);
        var round = _owner.QueryCombatRound();
        var invocation = new SpellInvocation(spell, facts, facts, target, SpellLocation.None, ParentIsLocalPlayer: caster == Hero,
            TurnBased: round is not null, TurnBasedActor: round is null ? null : round.Actor.Entity ?? Hero);
        var world = InvocationWorld();
        if (!SpellInvocationRules.Check(invocation, record.Run.Invocation, world, _owner.SpellRolls).Allowed)
            return new(SpellCastAttemptOutcome.Refused);
        var started = _owner.StartSpellRun(_owner.Version, new(invocation), record.Run, world, _owner.SpellRolls);
        if (started.Disposition != GameSessionDisposition.Changed || started.Creation?.Run is not { } run)
            return new(SpellCastAttemptOutcome.Refused, null, started);
        _itemChains.Remove(run.Id);
        return new(SpellCastAttemptOutcome.Started, null, started, run.Id);
    }
}
