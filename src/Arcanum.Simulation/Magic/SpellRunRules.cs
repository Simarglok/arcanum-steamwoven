using System.Collections.Immutable;

namespace Arcanum.Simulation.Magic;

/// <summary>The five actions a run goes through, with the original's numbers.</summary>
public enum SpellRunAction { Begin = 0, Maintain = 1, End = 2, Callback = 3, EndCallback = 4 }

/// <summary>
/// A run's flags (+0x13C): active (set by the allocator), free (no BEGIN or maintenance cost: the invocation's request),
/// processed (set by every pass of the processing, MT9a-2; never cleared), reflected (a target's full reflection turned it, MT10), unresistable (<c>No_Resist:</c> or the invocation's request), forced end
/// (0x457270: the run was made to end) and final END (the scheduler's mark that the next event is the END, also set when the
/// source is detached). The original keeps unresistable and final END in one bit, 0x10 (MG19);
/// <see cref="MagicAnomalyProfile.UnresistableIsFinalEnd"/> keeps them one.
/// </summary>
[Flags]
public enum SpellRunFlags { None = 0, Active = 0x1, Free = 0x2, Processed = 0x4, Reflected = 0x8, Unresistable = 0x10, ForcedEnd = 0x40, FinalEnd = 0x10000 }

/// <summary>An object a run names, captured when the run is created: its id, object type and, for a PC or NPC, its effective
/// magick/tech aptitude (stat 16) at that moment.</summary>
public sealed record SpellRunObject(string Id, int Type, int Aptitude = 0)
{
    public bool IsCritter => Type is SpellObjectFacts.PcType or SpellObjectFacts.NpcType;
}

/// <summary>
/// One run (a 0x158-byte slot of the original's run table): its id (the slot), spell, current action; the source (the object
/// whose spell it is) and the parent (the caster, or an item's holder), the source's location at the start (+0x50), the target
/// object and tile, the other object (the invocation's, ARC-MAGIC-025: set only by an item executor's attacker global), each captured at creation; the flags; the item triggers it was cast with
/// (+0x140); the resistance margin (+0x144, written by the per-target stage); the trigger count left (+0x150,
/// <c>DurationTriggerCount:</c>).
/// </summary>
public sealed record SpellRun(int Id, int Spell, SpellRunAction Action, SpellRunObject? Source, SpellRunObject? Parent, SpellLocation StartLocation,
    SpellRunObject? Target, SpellLocation TargetTile, SpellRunObject? Other, SpellRunFlags Flags, int Triggers = 0, int Margin = 0, int TriggerCount = 0)
{
    public bool Free => (Flags & SpellRunFlags.Free) != 0;

    /// <summary>The spell flags the run's components set (+0x138, MT12–MT18); the anti-magic shell (0x800) among them lets the
    /// run go on although its source is shelled (MT9a-2).</summary>
    public int SetSpellFlags { get; init; }
}

/// <summary>
/// The original's run table: 512 slots (behind 0x6876E0), a slot free while it holds no run. Immutable: every change answers a
/// new table.
/// </summary>
public sealed class SpellRunTable
{
    public const int Slots = 512;

    private readonly ImmutableArray<SpellRun?> _slots;

    private SpellRunTable(ImmutableArray<SpellRun?> slots) => _slots = slots;

    public static SpellRunTable Empty { get; } = new(Enumerable.Repeat<SpellRun?>(null, Slots).ToImmutableArray());

    /// <summary>The runs in slot order.</summary>
    public IEnumerable<SpellRun> Runs => _slots.Where(r => r is not null)!;

    public int Count => _slots.Count(r => r is not null);

    public SpellRun? this[int id] => id is >= 0 and < Slots ? _slots[id] : null;

    /// <summary>The first free slot (0x455740), or −1 when all 512 hold runs.</summary>
    [OriginalFunction(0x455740, OriginalFunctionScope.Partial)]
    public int FirstFree() => _slots.IndexOf(null);

    /// <summary>The table with <paramref name="run"/> in its slot (its id).</summary>
    public SpellRunTable With(SpellRun run)
    {
        ArgumentNullException.ThrowIfNull(run);
        if (run.Id is < 0 or >= Slots) throw new ArgumentOutOfRangeException(nameof(run));
        return new(_slots.SetItem(run.Id, run));
    }

    /// <summary>ARC-DEV-210 (MIG14 S4, saved-runs.md): the table of a save, each run in the slot of its id; null when two runs share a slot or an id is outside the table.</summary>
    public static SpellRunTable? Restore(IEnumerable<SpellRun> runs)
    {
        ArgumentNullException.ThrowIfNull(runs);
        var slots = Empty._slots.ToBuilder();
        foreach (var run in runs)
        {
            if (run is null || run.Id is < 0 or >= Slots || slots[run.Id] is not null) return null;
            slots[run.Id] = run;
        }
        return new(slots.ToImmutable());
    }

    /// <summary>The table with a slot freed (0x4558D0, 0x455960; the run's queued events are the caller's, MT9a-2).</summary>
    [OriginalFunction(0x4558D0, OriginalFunctionScope.Partial)]
    public SpellRunTable Free(int id) => id is >= 0 and < Slots && _slots[id] is not null ? new(_slots.SetItem(id, null)) : this;
}

/// <summary>
/// A request to start a run: the invocation (MT7's structure) and its flags — free (no BEGIN or maintenance cost),
/// unresistable (the script's "cast unresistable"), the cast without the casting goal (the invocation's flag 0x1: goal 73) —
/// and the item triggers it was cast with.
/// </summary>
public sealed record SpellRunRequest(SpellInvocation Invocation, bool Free = false, bool Unresistable = false, bool SkipCastGoal = false, int Triggers = 0);

/// <summary>A record's <c>Duration:</c> block (+0x1C): the seconds, the <c>DurationStatInfo:</c> stat (−1 none), its level and
/// the seconds per point the stat is below it.</summary>
public readonly record struct SpellDuration(int Seconds, int Stat = -1, int StatLevel = 0, int PerPoint = 0)
{
    /// <summary>A record without a duration (seconds 0 and no stat).</summary>
    public bool None => Seconds == 0 && Stat == -1;
}

/// <summary>
/// The record facts a run reads: technology, <c>No_Resist:</c>, <c>ChargeBeginCost: 0</c> (+0x34 flag 0x100), <c>Cost:</c>,
/// <c>Maintain:</c> cost and period (+0x14, +0x18), the duration block, <c>DurationTriggerCount:</c> (+0x2C), the willpower minimum
/// (+0x04), the casting animation (+0x68, −1 none), whether it names stacking rules (+0x110), charge cells, and the invocation
/// check's record facts.
/// </summary>
public sealed record SpellRunRecord(bool Technology = false, bool NoResist = false, bool SkipsBeginCost = false, int Cost = 0, int MaintainCost = 0,
    int MaintainPeriod = 0, SpellDuration Duration = default, int TriggerCount = 0, int WillpowerMinimum = 0, int CastAnimation = -1,
    bool StackingRules = false, bool ChargesFromCells = false, SpellInvocationRecord Invocation = default)
{
    public SpellPaymentRecord Payment => new(ChargesFromCells, MaintainPeriod, Duration.Seconds);

    /// <summary>The component count of each action (+0xE8 + 8 × action, in action order; MT9a-2); none when not given.</summary>
    public ImmutableArray<int> ActionComponents { get; init; }

    /// <summary>Whether the record names item triggers (+0x38 non-zero: <c>ItemTriggers:</c>), which gate its components and keep
    /// an untimed run from ending when its source faints (MT9a-2).</summary>
    public bool ItemTriggers { get; init; }

    /// <summary>The record's <c>No_Reflect:</c> (+0x34 flag 0x400): a target's full reflection does not turn it (MT10).</summary>
    public bool NoReflect { get; init; }

    /// <summary><c>Cancels_SF:</c> (+0x118) and <c>Cancels_EnvSF:</c> (+0x128), spell-flag bits (MT10).</summary>
    public int CancelsSpellFlags { get; init; }
    public int CancelsEnvironmentSpellFlags { get; init; }

    /// <summary>Whether the BEGIN area has the self bit (+0x70 bit 0x1): a self spell, which the no-stack test matches by its
    /// parent when it has no target object (MT10).</summary>
    public bool SelfSpell { get; init; }

    /// <summary>The component count of an action.</summary>
    public int Components(SpellRunAction action) =>
        !ActionComponents.IsDefault && (int)action >= 0 && (int)action < ActionComponents.Length ? ActionComponents[(int)action] : 0;
}

public enum SpellRunCreationStatus
{
    Created,
    /// <summary>The magic system is switched off (0x5E3510): nothing happens.</summary>
    Disabled,
    /// <summary>The invocation check refused (<see cref="SpellRunCreation.Check"/>).</summary>
    Refused,
    /// <summary>All 512 slots hold runs: the original logs and quits the game (MG28); here the cast is refused, in both modes.</summary>
    TableFull,
}

/// <summary>
/// The answer of a run's creation: the status, the table, the new run, the check's answer, and how the run starts — with a
/// parent the casting goal (26, or 82 when the record names a casting animation, or 73 without the goal's animation) whose
/// release frame starts BEGIN; without one a magic-tech event for BEGIN at once (MT9a-2). The original frees the run when the goal
/// cannot be started; the caller does (<see cref="SpellRunTable.Free"/>).
/// </summary>
public sealed record SpellRunCreation(SpellRunCreationStatus Status, SpellRunTable Table, SpellRun? Run, SpellInvocationResult? Check, int CastGoal, bool BeginEvent);

/// <summary>What the stage reads of the source and parent now: whether the source is dead (no hit points) or cannot act
/// (fatigue gone, not undead), the parent's intelligence and willpower, whether the parent is the local player, the source as a
/// payer and as a caster (race, mastery), and the cheat flag.</summary>
public sealed record SpellStageFacts(bool SourceDead = false, bool SourceCannotAct = false, int ParentIntelligence = 0, int ParentWillpower = 0,
    bool ParentIsLocalPlayer = false, SpellPayer? SourcePayer = null, SpellCaster SourceCaster = default, bool Cheat = false);

/// <summary>The stage's questions for the per-target stage's rules (MT10): another run of the spell that the stacking rules make
/// this one yield to on an object (0x453410; its id, or null), and the cancels a starting run makes (0x4534E0).</summary>
public interface ISpellRunStacking
{
    int? Blocking(SpellRun run, SpellRunObject target);

    void Cancel(SpellRun run);
}

/// <summary>Why the stage stops a run: the message the local player sees (600 the cost, 601 the maintenance, 602 intelligence or
/// willpower), or none.</summary>
public enum SpellStageStatus { Proceeds, SourceDown, Checks, BeginCost, MaintainCost, Blocked }

/// <summary>
/// The stage's answer: whether the run goes on, the status, the message the local player is shown (600, 601 or 602; null for
/// anyone else or none), the payments made in order with the cost each asked (each applied by the host whatever the status),
/// whether the run now ends (a failed MAINTAIN: the run turns to END, its target list is emptied and it is processed; the
/// local player's maintain bar is updated), and the blocking run.
/// </summary>
public sealed record SpellStageResult(bool Proceeds, SpellStageStatus Status, int? Message, IReadOnlyList<SpellPayment> Payments, bool Ends,
    int? BlockedBy = null)
{
    /// <summary>The cost each payment asked, in order.</summary>
    public IReadOnlyList<int> Costs { get; init; } = [];
}

/// <summary>What the time scaling reads (0x455250): a critter parent, the college it mastered, a critter target, and both
/// aptitudes (MG6: never used).</summary>
public readonly record struct SpellTimeFacts(bool ParentIsCritter, int MasteredCollege = -1, bool TargetIsCritter = false, int ParentAptitude = 0, int TargetAptitude = 0);

/// <summary>
/// ARC-MAGIC-009 (magic plan MT9a-1): a run's creation (0x455A20, 0x455AC0, 0x455C30), its stage at BEGIN and MAINTAIN
/// (0x452F20 with the maintenance payments 0x4532F0, 0x453370), its times (0x455250, the duration and maintenance intervals the
/// scheduler computes) and the maintenance cap (0x4545E0, 0x450AC0, 0x450B40, 0x450B90), as the 1.0.7.4 executable has them
/// (contract docs/world/spell-run-rules.md, ledger "Spell runs", RN1–RN14). Pure functions; the scheduler, the events and
/// interrupts are MT9a-2's.
/// </summary>
public static class SpellRunRules
{
    public const int GoalCast = 26, GoalCastAnimation = 82, GoalCastWithoutGoal = 73;
    public const int MessageCost = 600, MessageMaintain = 601, MessageChecks = 602;
    /// <summary>Game time runs 8 times as fast as the records' seconds (the scheduler's <c>× 8</c>).</summary>
    public const int GameTimeFactor = 8;

    /// <summary>
    /// Creates a run (0x455C30, from 0x455AC0): nothing while magic is switched off; the invocation check (MT7) first — a refusal
    /// creates nothing; then the first free slot; the run captures the source, parent, the source's location, target object and
    /// tile and the other object (ARC-MAGIC-025) with their types and the critters' aptitudes, the spell, action BEGIN, the request's triggers and the
    /// record's trigger count; its flags are active, free (the request), unresistable (the request or <c>No_Resist:</c>; with the
    /// final END too in the Original setting, MG19).
    /// </summary>
    [OriginalFunction(0x455C30, OriginalFunctionScope.Partial)]
    public static SpellRunCreation Create(SpellRunTable table, SpellRunRequest request, SpellRunRecord record, ISpellInvocationWorld world,
        ISpellRollSource rolls, MagicAnomalyProfile profile, bool magicDisabled = false)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(profile);
        if (magicDisabled) return new(SpellRunCreationStatus.Disabled, table, null, null, 0, false);
        var invocation = request.Invocation;
        var check = SpellInvocationRules.Check(invocation, record.Invocation, world, rolls);
        if (!check.Allowed) return new(SpellRunCreationStatus.Refused, table, null, check, 0, false);
        var id = table.FirstFree();
        if (id < 0) return new(SpellRunCreationStatus.TableFull, table, null, check, 0, false);
        var flags = SpellRunFlags.Active;
        if (request.Free) flags |= SpellRunFlags.Free;
        if (request.Unresistable || record.NoResist) flags |= Unresistable(profile);
        var target = check.ReplacedTile.IsNone ? invocation.TargetObject : null;
        var tile = check.ReplacedTile.IsNone ? invocation.TargetTile : check.ReplacedTile;
        var run = new SpellRun(id, invocation.Spell, SpellRunAction.Begin, Capture(invocation.Source), Capture(invocation.Parent),
            invocation.Source?.Location ?? SpellLocation.None, Capture(target), tile, Capture(invocation.Other), flags, request.Triggers, 0, record.TriggerCount);
        var goal = run.Parent is null ? 0 : request.SkipCastGoal ? GoalCastWithoutGoal : record.CastAnimation == -1 ? GoalCast : GoalCastAnimation;
        return new(SpellRunCreationStatus.Created, table.With(run), run, check, goal, run.Parent is null);
    }

    /// <summary>The flags "unresistable" means in a profile: one bit with the final END in the original (MG19).</summary>
    public static SpellRunFlags Unresistable(MagicAnomalyProfile profile) =>
        profile.UnresistableIsFinalEnd ? SpellRunFlags.Unresistable | SpellRunFlags.FinalEnd : SpellRunFlags.Unresistable;

    /// <summary>The flags the scheduler's "final END" sets (MT9a-2): one bit with unresistable in the original (MG19).</summary>
    public static SpellRunFlags FinalEnd(MagicAnomalyProfile profile) =>
        profile.UnresistableIsFinalEnd ? SpellRunFlags.Unresistable | SpellRunFlags.FinalEnd : SpellRunFlags.FinalEnd;

    private static SpellRunObject? Capture(SpellObjectFacts? facts) => facts is null ? null
        : new(facts.Id, facts.Type, facts.IsCritter ? facts.Aptitude : 0);

    /// <summary>
    /// The stage (0x452F20) before an action's components. BEGIN: a PC or NPC source that is dead or cannot act stops the run
    /// silently; for a PC or NPC parent and a magic record, intelligence below 5 and willpower below the record's minimum fail
    /// (only when the source is a PC or NPC), then the BEGIN cost is paid by the source (<see cref="SpellCostRules.RunCost"/> and
    /// <see cref="SpellPaymentRules.Pay"/>) unless the run is free or the record skips its BEGIN cost; a technology record skips
    /// the checks and the cost. Then, with stacking rules, a run of the spell the target (else the source) already carries may
    /// block it; then the cancels run. MAINTAIN: for a PC or NPC parent — the technology flag is not tested — the same checks,
    /// the maintenance (<see cref="MaintainCost"/>) unless free or the intelligence check failed, and with a resistance margin
    /// the surcharged maintenance (<see cref="SurchargedCost"/>; MG3: the original pays both, Steamwoven only the surcharged; the
    /// surcharge is paid even for a free run or after a failed check, MG29, in Original only, <see cref="MagicAnomalyProfile.SurchargeAlways"/>);
    /// a failure turns the run to END. Other
    /// actions proceed.
    /// </summary>
    [OriginalFunction(0x452F20, OriginalFunctionScope.Partial)]
    public static SpellStageResult Stage(SpellRun run, SpellRunRecord record, SpellStageFacts facts, ISpellCellSource cells, ISpellRunStacking stacking,
        MagicAnomalyProfile profile)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(cells);
        ArgumentNullException.ThrowIfNull(stacking);
        ArgumentNullException.ThrowIfNull(profile);
        var payments = new List<SpellPayment>();
        var costs = new List<int>();
        var sourceCritter = run.Source?.IsCritter == true;
        if (run.Action == SpellRunAction.Begin)
        {
            if (sourceCritter && (facts.SourceDead || facts.SourceCannotAct)) return new(false, SpellStageStatus.SourceDown, null, payments, false) { Costs = costs };
            if (run.Parent?.IsCritter == true && !record.Technology)
            {
                var pay = !run.Free;
                var failed = false;
                var status = SpellStageStatus.Proceeds;
                if (facts.ParentIntelligence < SpellBookRules.MinimumIntelligence && sourceCritter) (failed, pay, status) = (true, false, SpellStageStatus.Checks);
                if (facts.ParentWillpower < record.WillpowerMinimum && sourceCritter) return Stop(SpellStageStatus.Checks, facts, payments, costs, false);
                if (pay && !record.SkipsBeginCost && run.Source is not null)
                {
                    var cost = SpellCostRules.RunCost(record.Cost, run.Spell, facts.SourceCaster, facts.Cheat);
                    var payment = SpellPaymentRules.Pay(facts.SourcePayer ?? new(SpellPayerKind.Other), cost, true, run.Spell, record.Payment, cells);
                    payments.Add(payment);
                    costs.Add(cost);
                    if (!payment.Succeeded) return Stop(SpellStageStatus.BeginCost, facts, payments, costs, false);
                }
                if (failed) return Stop(status, facts, payments, costs, false);
            }
            if (record.StackingRules && (run.Target ?? run.Source) is { } on && stacking.Blocking(run, on) is { } other && other != run.Id)
                return new(false, SpellStageStatus.Blocked, null, payments, false, other) { Costs = costs };
            stacking.Cancel(run);
            return new(true, SpellStageStatus.Proceeds, null, payments, false) { Costs = costs };
        }
        if (run.Action != SpellRunAction.Maintain || run.Parent?.IsCritter != true) return new(true, SpellStageStatus.Proceeds, null, payments, false) { Costs = costs };
        return Maintain(run, record, facts, cells, profile, payments, costs, sourceCritter);
    }

    // The MAINTAIN branch of the stage (from 0x453168; the plan's "0x453170" lies inside it).
    private static SpellStageResult Maintain(SpellRun run, SpellRunRecord record, SpellStageFacts facts, ISpellCellSource cells, MagicAnomalyProfile profile,
        List<SpellPayment> payments, List<int> costs, bool sourceCritter)
    {
        var pay = !run.Free;
        var failed = false;
        var status = SpellStageStatus.Proceeds;
        if (facts.ParentIntelligence < SpellBookRules.MinimumIntelligence && sourceCritter) (failed, pay, status) = (true, false, SpellStageStatus.Checks);
        var willpowerFailed = facts.ParentWillpower < record.WillpowerMinimum && sourceCritter;
        if (willpowerFailed) (failed, status) = (true, SpellStageStatus.Checks);
        else if (pay && !(run.Margin != 0 && !profile.MaintenancePaysBoth))
        {
            var cost = MaintainCost(record.MaintainCost, facts.SourceCaster);
            var payment = PayMaintenance(cost, run, record, facts, cells);
            if (payment is not null)
            {
                payments.Add(payment);
                costs.Add(cost);
                if (!payment.Succeeded) (failed, status) = (true, SpellStageStatus.MaintainCost);
            }
        }
        // MG29: the original charges the surcharge in every case; Steamwoven not for a free run, after a failure, or at a
        // maintenance cost of 0.
        if (run.Margin != 0 && (profile.SurchargeAlways || (pay && !failed && record.MaintainCost != 0)))
        {
            var cost = SurchargedCost(record.MaintainCost, run.Margin, facts.SourceCaster);
            var payment = PayMaintenance(cost, run, record, facts, cells);
            if (payment is not null)
            {
                payments.Add(payment);
                costs.Add(cost);
                if (!payment.Succeeded) return Stop(SpellStageStatus.MaintainCost, facts, payments, costs, true);
            }
        }
        return failed ? Stop(status, facts, payments, costs, true) : new(true, SpellStageStatus.Proceeds, null, payments, false) { Costs = costs };
    }

    private static SpellPayment? PayMaintenance(int cost, SpellRun run, SpellRunRecord record, SpellStageFacts facts, ISpellCellSource cells) =>
        run.Source is null ? null : SpellPaymentRules.Pay(facts.SourcePayer ?? new(SpellPayerKind.Other), cost, true, run.Spell, record.Payment, cells);

    private static SpellStageResult Stop(SpellStageStatus status, SpellStageFacts facts, List<SpellPayment> payments, List<int> costs, bool ends)
    {
        var message = status switch
        {
            SpellStageStatus.Checks => MessageChecks,
            SpellStageStatus.BeginCost => MessageCost,
            SpellStageStatus.MaintainCost => MessageMaintain,
            _ => (int?)null,
        };
        return new(false, status, facts.ParentIsLocalPlayer ? message : null, payments, ends) { Costs = costs };
    }

    /// <summary>The maintenance a tick charges (0x4532F0): the record's <c>Maintain:</c> cost, doubled for a dwarf PC or NPC source.</summary>
    [OriginalFunction(0x4532F0, OriginalFunctionScope.Partial)]
    public static int MaintainCost(int maintainCost, SpellCaster source) => Dwarf(source) ? unchecked(maintainCost * 2) : maintainCost;

    /// <summary>The surcharged maintenance with a resistance margin (0x453370): the cost plus <c>cost × margin / 100</c> (32-bit,
    /// toward zero; a share of 0 counts 1), doubled for a dwarf PC or NPC source.</summary>
    [OriginalFunction(0x453370, OriginalFunctionScope.Partial)]
    public static int SurchargedCost(int maintainCost, int margin, SpellCaster source)
    {
        var share = unchecked(maintainCost * margin) / 100;
        if (share == 0) share = 1;
        var cost = unchecked(maintainCost + share);
        return Dwarf(source) ? unchecked(cost * 2) : cost;
    }

    private static bool Dwarf(SpellCaster caster) => caster.IsCritter && caster.Race == SpellCostRules.DwarfRace;

    /// <summary>
    /// The time scaling of a duration or maintenance interval (0x455250), in milliseconds: for a PC or NPC parent, doubled when
    /// the spell (0..79) is of the college it mastered; a PC or NPC target would scale it by the aptitude difference, but the
    /// original takes that difference's magnitude and tests it for being negative, so it never does (MG6, both modes).
    /// </summary>
    [OriginalFunction(0x455250, OriginalFunctionScope.Full)]
    public static uint ScaleTime(uint milliseconds, int spell, SpellTimeFacts facts)
    {
        if (!facts.ParentIsCritter) return milliseconds;
        if (spell is >= 0 and < SpellCostRules.CollegeSpells && facts.MasteredCollege == spell / SpellCostRules.Ranks) milliseconds = unchecked(milliseconds * 2);
        if (facts.TargetIsCritter && milliseconds > 0)
        {
            // The larger minus the smaller (32-bit): the magnitude, negative only when it wraps. A wrap to −100 divides by zero in
            // the original; here it leaves the time (no-crash; aptitudes run −100..100, so no data reaches it).
            var difference = facts.ParentAptitude > facts.TargetAptitude ? unchecked(facts.ParentAptitude - facts.TargetAptitude)
                : unchecked(facts.TargetAptitude - facts.ParentAptitude);
            if (difference < 0 && difference != -100) milliseconds = unchecked(milliseconds * 100) / unchecked((uint)(difference + 100));
        }
        return milliseconds;
    }

    /// <summary>
    /// A timed run's interval (the scheduler, 0x453FA0): the record's seconds (0 counts 1) plus, with a <c>DurationStatInfo:</c>
    /// stat, <c>per point × max(0, level − stat)</c> of the parent (a PC or NPC), or without a parent of a PC or NPC target; in
    /// milliseconds (0 gives a second), scaled (<see cref="ScaleTime"/>) and turned into game time (× 8).
    /// </summary>
    public static uint DurationTime(SpellDuration duration, int spell, int? statValue, SpellTimeFacts facts)
    {
        var seconds = duration.Seconds == 0 ? 1u : (uint)duration.Seconds;
        if (duration.Stat > -1 && statValue is { } value)
        {
            var below = duration.StatLevel - value;
            if (below < 0) below = 0;
            seconds = unchecked(seconds + (uint)(duration.PerPoint * below));
        }
        var milliseconds = seconds > 0 ? unchecked(seconds * 1000) : 1000u;
        return unchecked(ScaleTime(milliseconds, spell, facts) << 3);
    }

    /// <summary>A maintained run's interval (0x454790): the record's period in milliseconds (0 counts 1), scaled and turned into
    /// game time (× 8).</summary>
    [OriginalFunction(0x454790, OriginalFunctionScope.Partial)]
    public static uint MaintainTime(int periodSeconds, int spell, SpellTimeFacts facts)
    {
        var milliseconds = unchecked((uint)periodSeconds * 1000);
        if (milliseconds == 0) milliseconds = 1;
        return unchecked(ScaleTime(milliseconds, spell, facts) << 3);
    }

    /// <summary>The DurationStatInfo stat's holder (the scheduler): the parent when it is a PC or NPC; with no parent a PC or NPC
    /// target; otherwise none.</summary>
    public static SpellRunObject? DurationStatHolder(SpellRun run)
    {
        ArgumentNullException.ThrowIfNull(run);
        if (run.Parent is { } parent) return parent.IsCritter ? parent : null;
        return run.Target?.IsCritter == true ? run.Target : null;
    }

    /// <summary>A timed run's trigger count at a tick (the scheduler): none left makes the next event the END (the final END
    /// flags, MG19); otherwise one fewer.</summary>
    public static SpellRun CountTrigger(SpellRun run, MagicAnomalyProfile profile)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(profile);
        return run.TriggerCount <= 0 ? run with { Action = SpellRunAction.End, Flags = run.Flags | FinalEnd(profile) } : run with { TriggerCount = run.TriggerCount - 1 };
    }

    /// <summary>
    /// ARC-WORLD-102: whether an object is under the influence of a spell (0x459380, the AI chooser's skip): an active run of
    /// <paramref name="spell"/> (by slot, 0x458CF0 and 0x458D90) whose target object is <paramref name="target"/>, or whose two object
    /// lists (+0x130, +0x134) hold it; a null object is under none. The run's object lists are not modelled.
    /// </summary>
    [OriginalFunction(0x459380, OriginalFunctionScope.Partial)]
    public static bool UnderInfluence(SpellRunTable table, string? target, int spell)
    {
        ArgumentNullException.ThrowIfNull(table);
        return target is not null && table.Runs.Any(r => (r.Flags & SpellRunFlags.Active) != 0 && r.Spell == spell && r.Target?.Id == target);
    }

    /// <summary>The maintenance slots a critter has (0x450B90): its intelligence / 4, toward zero.</summary>
    [OriginalFunction(0x450B90, OriginalFunctionScope.Full)]
    public static int MaintainSlots(int intelligence) => intelligence / 4;

    // A run that holds a maintenance slot of a parent: active, processed at least once, a magic record with a maintenance
    // period, that parent.
    private static bool HoldsSlot(SpellRun run, string parent, Func<int, SpellRunRecord> records) =>
        (run.Flags & SpellRunFlags.Active) != 0 && (run.Flags & SpellRunFlags.Processed) != 0 && run.Parent?.Id == parent
        && records(run.Spell) is { Technology: false, MaintainPeriod: > 0 };

    /// <summary>
    /// The maintained runs a critter holds (0x450AC0): the original walks the table and answers 0 (MG11); Steamwoven counts the
    /// runs that hold its slots (the cap's count).
    /// </summary>
    [OriginalFunction(0x450AC0, OriginalFunctionScope.Partial)]
    public static int MaintainedRuns(SpellRunTable table, SpellRunObject? critter, Func<int, SpellRunRecord> records, MagicAnomalyProfile profile)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(profile);
        if (critter?.IsCritter != true || profile.MaintainedCountIsZero) return 0;
        return table.Runs.Count(r => HoldsSlot(r, critter.Id, records));
    }

    /// <summary>A critter's free maintenance slots (0x450B40): its slots less <see cref="MaintainedRuns"/>; 0 for anything else. In
    /// Steamwoven it can fall below 0 until the cap ends the excess run, so a "can cast" test (the original's <c>!= 0</c>) must
    /// test <c>&gt; 0</c>.</summary>
    [OriginalFunction(0x450B40, OriginalFunctionScope.Full)]
    public static int FreeMaintainSlots(SpellRunTable table, SpellRunObject? critter, int intelligence, Func<int, SpellRunRecord> records, MagicAnomalyProfile profile) =>
        critter?.IsCritter == true ? MaintainSlots(intelligence) - MaintainedRuns(table, critter, records, profile) : 0;

    /// <summary>
    /// The maintenance cap of a run (0x4545E0, asked by the scheduler before a maintenance tick): a run without a source is within
    /// it; one whose parent is not a PC or NPC (none included) is not; otherwise the runs holding the parent's slots (counted for
    /// the cap in both modes) may not exceed its intelligence / 4. The original also asks the range test and ignores its answer.
    /// </summary>
    [OriginalFunction(0x4545E0, OriginalFunctionScope.Partial)]
    public static bool WithinCap(SpellRun run, SpellRunTable table, int parentIntelligence, Func<int, SpellRunRecord> records)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(records);
        if (run.Source is null) return true;
        if (run.Parent?.IsCritter != true) return false;
        return table.Runs.Count(r => HoldsSlot(r, run.Parent.Id, records)) <= MaintainSlots(parentIntelligence);
    }
}
