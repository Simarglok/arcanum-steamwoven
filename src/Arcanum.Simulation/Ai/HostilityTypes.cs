namespace Arcanum.Simulation.Ai;

/// <summary>The object types the hostility rules tell apart (object field 0x14F): a wall (type 0), any other non-critter, a PC
/// (15) or an NPC (16).</summary>
public enum HostilityKind
{
    Wall,
    Object,
    Pc,
    Npc,
}

/// <summary>
/// One object as the hostility rules read it (stage A3, slice I4; ARC-WORLD-080). The flags carry the original's bits:
/// object flags <see cref="Destroyed"/> 0x1, <see cref="Off"/> 0x2, <see cref="DontDraw"/> 0x100, <see cref="ObjectInvisible"/>
/// 0x200, <see cref="Invulnerable"/> 0x400000; critter flags <see cref="Undead"/> 0x4, <see cref="Animal"/> 0x8,
/// <see cref="NoFlee"/> 0x8000000, <see cref="NonLethal"/> 0x10000000, <see cref="AnimalEnshrouded"/> 0x40000000; critter flag 2
/// <see cref="NighInvulnerable"/> 0x200; spell flags <see cref="InvisibleSpell"/> 0x1, <see cref="DetectsInvisible"/> 0x200,
/// <see cref="Stoned"/> 0x10000, <see cref="MindControlled"/> 0x8000000, <see cref="Enshrouded"/> 0x20000000; NPC flags
/// <see cref="WaitingHere"/> 0x8, <see cref="KillOnSight"/> 0x100, <see cref="KosOverride"/> 0x800. <see cref="Monstrous"/> is
/// 0x45F730 (any of undead, animal, monster, mechanical), <see cref="Busted"/> 0x43F630 for a non-critter,
/// <see cref="Decoy"/> the clockwork decoy (name 6719), <see cref="Guard"/> social class 5, <see cref="MasterProwler"/> prowling
/// trained to 3 or more, <see cref="AttemptingAttack"/> an attack (goal 19) or spell (goal 27) attempt in progress,
/// <see cref="HealthPercent"/> the hit-point ratio (0x4AB400). <see cref="Leader"/> is the direct leader (0x45DE00),
/// <see cref="PcLeader"/> the leader chain's PC (0x45DDA0), null for a PC or a leaderless NPC.
/// </summary>
public sealed record HostilityCritter
{
    public required string Id { get; init; }
    public required HostilityKind Kind { get; init; }
    public bool Destroyed { get; init; }
    public bool Off { get; init; }
    public bool DontDraw { get; init; }
    public bool ObjectInvisible { get; init; }
    public bool Invulnerable { get; init; }
    public bool Busted { get; init; }
    public bool NighInvulnerable { get; init; }
    public bool Dead { get; init; }
    public bool Unconscious { get; init; }
    public bool Asleep { get; init; }
    public bool NonLethal { get; init; }
    public bool Undead { get; init; }
    public bool Animal { get; init; }
    public bool AnimalEnshrouded { get; init; }
    public bool Monstrous { get; init; }
    public bool NoFlee { get; init; }
    public bool InvisibleSpell { get; init; }
    public bool DetectsInvisible { get; init; }
    public bool Stoned { get; init; }
    public bool MindControlled { get; init; }
    public bool Enshrouded { get; init; }
    public bool Concealed { get; init; }
    public bool MasterProwler { get; init; }
    public bool AttemptingAttack { get; init; }
    public bool InCombatMode { get; init; }
    public bool Decoy { get; init; }
    public bool Guard { get; init; }
    public bool WaitingHere { get; init; }
    public bool KillOnSight { get; init; }
    public bool KosOverride { get; init; }
    public int Perception { get; init; }
    public int Intelligence { get; init; }
    public int Level { get; init; }
    public int Alignment { get; init; }
    public int HealthPercent { get; init; }
    public int Origin { get; init; }
    public string? Leader { get; init; }
    public string? PcLeader { get; init; }

    internal bool IsCritter => Kind is HostilityKind.Pc or HostilityKind.Npc;
}

/// <summary>The AI parameter slots the hostility rules read (the NPC's row, slice I1): 4 flee distance (tiles), 10 hostile
/// reaction, 11 hostile alignment gap and 12 good follower stop (an alignment).</summary>
public readonly record struct HostilityParameters(int FleeDistance, int HostileReaction, int HostileAlignmentGap, int GoodFollowerStop);

/// <summary>An NPC's danger record as the hostility rules read it: the state read and its source, the combat focus (field
/// 0x11B, whatever the state), who-hit-me-last (0x11C) and the enemy list in its order.</summary>
public sealed record HostilityDanger(DangerState State, string? Source, string? Focus, string? HitBy, IReadOnlyList<string> Enemies)
{
    public static HostilityDanger None { get; } = new(DangerState.None, null, null, null, []);
}

/// <summary>
/// What the hostility rules ask of the world, by object id. Answers must be stable within one rule call. <see cref="Find"/>
/// returns null for a handle that names nothing; the other members are asked only about ids it knows.
/// A handle a danger record keeps (focus, source, who-hit-me-last, enemies) may name an object the world no longer knows; the
/// rules treat it as none. <see cref="Square"/> is the rectangle query of find target (0x4AE4E0 → 0x440B40): the PCs and NPCs on the tiles within
/// <paramref name="halfSize"/> of the NPC, both edges included, dead ones and the NPC itself too, in the query's order
/// (sector by sector, the most recently placed first). <see cref="HostilityReaction"/> is the AI reaction variant
/// (0x4C0CE0) of the NPC toward a PC; <see cref="AllowsKillOnSight"/> is false when the NPC's will-kos script (attachment
/// point 22, the candidate as triggerer) returns 0; <see cref="MindController"/> is the controller the NPC's mind-control
/// spell names (0x459040), null when none is found.
/// </summary>
public interface IHostilityWorld
{
    HostilityCritter? Find(string id);
    int Distance(string from, string to);
    bool FactionSame(string a, string b);
    int HostilityReaction(string npc, string pc);
    bool AllowsKillOnSight(string npc, string candidate);
    HostilityParameters Parameters(string npc);
    AwarenessResult See(string observer, string target);
    AwarenessResult Hear(string observer, string target, Loudness loudness);
    HostilityDanger Danger(string npc);
    string? MindController(string npc);
    IReadOnlyList<string> Square(string npc, int halfSize);
}

/// <summary>What an AI roll decides (design rule 3); the session labels it <c>ai.</c> and the name.</summary>
public enum AiRollPurpose
{
    /// <summary>Choose target's coin toss, d100 (0x4AB0B0): 1..50 compares level − distance, 51..100 nearness.</summary>
    ChooseTargetScore,
    /// <summary>The enemy-list pick's start, 0..n−1 (0x4AFA90); an empty list draws −1..0.</summary>
    EnemyListStart,
    /// <summary>A surrendered NPC at 80 % hit points or more calms down on 1 of 1..500 (danger update 0x4A92D0).</summary>
    SurrenderCalm,
    /// <summary>Combat perform (0x4AC6E0) starts a back-off on 1 of 1..20 when closer to its target than parameter 15 (slice I5).</summary>
    BackOffStart,
    /// <summary>A wake-up tick adds 0..5000 game milliseconds to the tick it schedules (0x4AD200, slice I6).</summary>
    TickWakeUpJitter,
    /// <summary>The gate clears blindness on 1 of 1..160 (0x4A8570, slice I6).</summary>
    GateBlindRecovery,
    /// <summary>The gate clears every crippled condition on 1 of 1..8000 (0x4A8570, slice I6).</summary>
    GateCrippleRecovery,
    /// <summary>A walk goal whose next step is blocked waits 0..300 game milliseconds before it gives up (the stepper's random delay,
    /// 0x423560 → 0x4CEF00; slice I7).</summary>
    BlockedStepWait,
    /// <summary>Heal (0x4A8940): in combat mode the NPC also cares for targets above 30 % HP when 1..100 is at most parameter 14
    /// (slice I8).</summary>
    HealCareRoll,
    /// <summary>The spell choice (0x4ABC70) goes on when 1..100 is at most its chance (slice I8).</summary>
    SpellChoiceRoll,
    /// <summary>The spell choice tries the defensive list when parameter 13 is below 1..100 (P7, slice I8).</summary>
    DefensiveListRoll,
    /// <summary>The chooser (0x4ABF10) starts at the first entry when 1..100 is below 80 (slice I8).</summary>
    ChooserStart,
    /// <summary>Otherwise the chooser starts at entry 0..n−1 (slice I8).</summary>
    ChooserEntry,
}

/// <summary>The AI's random source: one inclusive range draw per call, the original's range draw (0x4CEF00); equal bounds
/// draw nothing.</summary>
public interface IAiRollSource
{
    int Roll(AiRollPurpose purpose, int min, int max);
}

/// <summary>Target validity (0x4AB990): <see cref="Valid"/> or the first failed test, in the original's order.</summary>
public enum TargetValidity
{
    Valid,
    /// <summary>1: no object.</summary>
    Missing,
    /// <summary>2: destroyed, off, not drawn or invulnerable.</summary>
    Gone,
    /// <summary>3: the NPC itself.</summary>
    Self,
    /// <summary>4: a dead critter.</summary>
    Dead,
    /// <summary>5: unconscious while the NPC fights non-lethally or, outside a scan, while the scan finds another target.</summary>
    Unconscious,
    /// <summary>6: the NPC's leader.</summary>
    Leader,
    /// <summary>7: a follower of the NPC's (non-null) leader.</summary>
    FellowFollower,
    /// <summary>8: the NPC's follower.</summary>
    Follower,
    /// <summary>9: stoned.</summary>
    Stoned,
    /// <summary>10: invisible to the NPC (0x45A580).</summary>
    Invisible,
    /// <summary>11: the decoy for an NPC of intelligence 5 or more.</summary>
    Decoy,
    /// <summary>12: a wall.</summary>
    Wall,
    /// <summary>12: a busted non-critter.</summary>
    Busted,
    /// <summary>13: farther than 20 tiles from the NPC.</summary>
    TooFar,
    /// <summary>14: farther than 20 tiles from the NPC's leader.</summary>
    TooFarFromLeader,
    /// <summary>15: a concealed master prowler the NPC does not see.</summary>
    UnseenProwler,
}

/// <summary>Kill-on-sight (0x4AE120), reason codes of the original.</summary>
public enum KillOnSight
{
    No = 0,
    Faction = 1,
    Reaction = 2,
    Alignment = 3,
    Guard = 4,
    Decoy = 5,
}

/// <summary>Protect (0x4AE3A0), reason codes of the original.</summary>
public enum Protection
{
    No = 0,
    Faction = 1,
    Origin = 2,
    Group = 3,
    Self = 4,
}

/// <summary>Upset attacking (0x4ADCC0): why the NPC refuses to attack a target on behalf of a leader; reason codes of the original.</summary>
public enum UpsetReason
{
    None = 0,
    Good = 1,
    Origin = 2,
    Faction = 3,
    Party = 4,
}

/// <summary>How find target (0x4AB460) ended.</summary>
public enum ScanStep
{
    /// <summary>Nothing perceived qualified.</summary>
    None,
    /// <summary>The NPC is invulnerable, nigh-invulnerable or the decoy: no scan.</summary>
    Refused,
    /// <summary>Step c: a kill-on-sight candidate.</summary>
    KillOnSight,
    /// <summary>Step e: an ally's danger source.</summary>
    AllyHelp,
    /// <summary>Step f: the focus of a dead NPC the scanner protects.</summary>
    Avenge,
}

/// <summary>A scan's result: the target (none for <see cref="ScanStep.None"/> and <see cref="ScanStep.Refused"/>), the step that
/// found it, the kill-on-sight reason for step c, and the concealed PC the NPC turns toward when nothing was found.</summary>
public readonly record struct ScanResult(string? Target, ScanStep Step, KillOnSight Reason, string? TurnToward);

/// <summary>Which path of the danger update (0x4A92D0) ran.</summary>
public enum DangerUpdatePath
{
    /// <summary>A sleeping NPC skips the update.</summary>
    Asleep,
    /// <summary>NONE: the scan found nothing; no setter call.</summary>
    NoTarget,
    /// <summary>NONE: the scan's target is fought.</summary>
    Scan,
    /// <summary>COMBAT_FOCUS: the valid focus is kept.</summary>
    Focus,
    /// <summary>COMBAT_FOCUS: the focus was invalid, the lock is cleared and the scan's target fought (0x4ABBC0).</summary>
    Rescan,
    /// <summary>COMBAT_FOCUS: who-hit-me-last is fought.</summary>
    HitBy,
    /// <summary>COMBAT_FOCUS: the enemy-list pick is fought.</summary>
    EnemyList,
    /// <summary>COMBAT_FOCUS: nothing found; the setter is asked for NONE.</summary>
    CalmDown,
    /// <summary>FLEE: the fleeing action helper runs (0x4AAF50, <see cref="MoraleRules.FleeSpell"/>).</summary>
    Flee,
    /// <summary>SURRENDER: nothing happens.</summary>
    SurrenderStays,
    /// <summary>SURRENDER: the calm-down roll came up; the setter is asked for NONE.</summary>
    SurrenderCalms,
}

/// <summary>
/// One danger update: the path, the target found (the pass record's target), the setter request (none for
/// <see cref="DangerUpdatePath.Asleep"/>, <see cref="DangerUpdatePath.NoTarget"/>, <see cref="DangerUpdatePath.Flee"/> and
/// <see cref="DangerUpdatePath.SurrenderStays"/>), whether the target lock is cleared first and the scan that ran. For a fight
/// the spell and item choice (0x4ABC20, slice I8) runs before the setter; <see cref="HostilityRules.FocusTarget"/> then names
/// the setter's target (SP16).
/// </summary>
public sealed record DangerUpdatePlan(DangerUpdatePath Path, string? Target, DangerState? Request, bool Unlocks, ScanResult? Scan)
{
    /// <summary>A fight request: the spell and item choice runs for <see cref="Target"/> before the setter.</summary>
    public bool ChoosesAction => Request == DangerState.CombatFocus;
}

/// <summary>What the reaction to an attack (0x4AA620) decided.</summary>
public enum AttackReactionStep
{
    /// <summary>A dead NPC does nothing.</summary>
    Dead,
    /// <summary>The fight-or-flee step ran (0x4AB2A0): the setter is asked for FLEE from or COMBAT_FOCUS on the attacker.</summary>
    Weigh,
    /// <summary>COMBAT_FOCUS: the current focus is kept.</summary>
    KeepFocus,
    /// <summary>FLEE: the NPC keeps fleeing what it flees.</summary>
    KeepFleeing,
    /// <summary>SURRENDER: the fleeing line (when the NPC is active) and a flee goal away from the attacker (0x434980).</summary>
    FleeGoal,
}

/// <summary>A reaction to an attack: the step, the setter request and its target for <see cref="AttackReactionStep.Weigh"/>,
/// and whether the NPC joins the turn-based round (0x4B7E00; every NPC that is not dead).</summary>
public readonly record struct AttackReaction(AttackReactionStep Step, DangerState? Request, string? Target, bool JoinsRound);

/// <summary>The attack event's flag bits (0x4A9650): 0x1 accidental, 0x2 no fan-out (the script action), 0x4 ordered (the
/// PC's attack order).</summary>
[Flags]
public enum AttackEventFlags
{
    None = 0,
    Accidental = 1,
    NoFanOut = 2,
    Ordered = 4,
    All = 7,
}

/// <summary>Where the attack event stopped; <see cref="None"/> when it ran to the reaction step.</summary>
public enum AttackEventStop
{
    None,
    /// <summary>Step 1: no attacker or no victim.</summary>
    Missing,
    /// <summary>Step 1: a nigh-invulnerable critter victim.</summary>
    VictimProtected,
    /// <summary>Step 1: a dead critter victim.</summary>
    VictimDead,
    /// <summary>Step 1: a victim off, not drawn or invulnerable.</summary>
    VictimGone,
    /// <summary>Step 1: the decoy.</summary>
    VictimDecoy,
    /// <summary>Step 1: a wall.</summary>
    VictimWall,
    /// <summary>Step 1: the attacker attacks itself.</summary>
    Self,
    /// <summary>Step 2: a non-critter attacker (an NPC victim is asked to fight it).</summary>
    ObjectAttacker,
    /// <summary>Step 3: an ordered attack (only the follower rule ran, or nothing without a leader).</summary>
    Ordered,
    /// <summary>Step 5: a non-critter victim.</summary>
    ObjectVictim,
    /// <summary>Step 9: a PC victim.</summary>
    PcVictim,
}

/// <summary>Step 12 of the attack event: which reaction change a PC's attack makes (<c>ReactionRules.PcHit</c> applies it).</summary>
public enum AttackReactionChange
{
    None,
    /// <summary>The PC's direct follower, deliberate: parameter 9 and the follow check.</summary>
    FollowerDeliberate,
    /// <summary>The PC's direct follower, accidental: the follow check only.</summary>
    FollowerAccidental,
    /// <summary>Another NPC, accidental: −10.</summary>
    Accidental,
    /// <summary>Another NPC, deliberate: lowered to parameter 10.</summary>
    Deliberate,
}

/// <summary>
/// The attack event (0x4A9650) as data, in the original's order: where it stopped and each step the caller carries out.
/// <see cref="VictimFightsObject"/>: an NPC victim of a non-critter attacker is asked into COMBAT_FOCUS on it.
/// <see cref="OrderedLeader"/>: the ordered attack's follower rule (the attacker, its leader, the victim; slice I9).
/// <see cref="LosesConcealment"/>: the victim's PC leader, or the victim, stops prowling. <see cref="PcVictimResponds"/>: the PC
/// victim's UI callback, combat-mode entry and auto-attack (0x4A9AD0). <see cref="RaisesNoise"/>: the noise fan-out
/// (0x4A9E10, <see cref="HostilityRules.NoiseWitnesses{T}"/>). The session's <c>ApplyAttackEvent</c> writes
/// <see cref="WritesHitBy"/> and both team adds.
/// </summary>
public sealed record AttackEventPlan
{
    public AttackEventStop Stop { get; init; }
    public bool WakesVictim { get; init; }
    public bool VictimFightsObject { get; init; }
    public string? OrderedLeader { get; init; }
    public bool TellsAttackerFollowers { get; init; }
    public string? LosesConcealment { get; init; }
    public bool ClearsKosOverride { get; init; }
    public bool PcVictimResponds { get; init; }
    public bool TellsVictimFollowers { get; init; }
    public bool RaisesNoise { get; init; }
    public bool TurnsVictim { get; init; }
    public bool WritesHitBy { get; init; }
    public bool AddsAttackerTeam { get; init; }
    public bool AddsVictimTeam { get; init; }
    public AttackReactionChange Reaction { get; init; }
    public bool ReactsToAttack { get; init; }
}

/// <summary>What a witness of an attack does (0x4A9F10).</summary>
public enum WitnessResponse
{
    None,
    /// <summary>It protects the victim and perceives it: its reaction to an attack by the attacker.</summary>
    ReactToAttacker,
    /// <summary>It protects the attacker and perceives it: its reaction to an attack by the victim.</summary>
    ReactToVictim,
    /// <summary>A bystander that perceives the victim: the setter puts it into FLEE from the attacker.</summary>
    FleeFromAttacker,
}

/// <summary>A witness's response and whether it is woken first (a sleeping witness that is not excluded, AW19).</summary>
public readonly record struct WitnessOutcome(WitnessResponse Response, bool Wakes);
