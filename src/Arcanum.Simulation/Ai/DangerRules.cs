using Arcanum.Simulation.Combat;

namespace Arcanum.Simulation.Ai;

/// <summary>An NPC's danger state (0x4AAB00, core loop S4); the numbers are the original's.</summary>
public enum DangerState
{
    None = 0,
    CombatFocus = 1,
    Flee = 2,
    Surrender = 3,
}

/// <summary>
/// The danger bits of one NPC that the AI writes (project names; the original's words in brackets). One set for both rule
/// settings (design rule 7). The invulnerability flags and the NO_FLEE of the critter's data are facts the caller supplies
/// (<see cref="DangerSetterFacts"/>); the NO_FLEE the AI sets at run time is <see cref="NoFlee"/>.
/// </summary>
[Flags]
public enum DangerFlags
{
    None = 0,
    /// <summary>NPC flag 0x1 (FIGHTING): the state read reports <see cref="DangerState.CombatFocus"/>.</summary>
    Fighting = 1 << 0,
    /// <summary>NPC flag 0x40 (CHECK_WIELD): the gate's next pass re-wields every wear slot.</summary>
    CheckWield = 1 << 1,
    /// <summary>NPC flag 0x40000000 (CHECK_GRENADE).</summary>
    CheckGrenade = 1 << 2,
    /// <summary>NPC flag 0x10000000 (BACKING_OFF).</summary>
    BackingOff = 1 << 3,
    /// <summary>NPC flag 0x1000000 (DEMAINTAIN_SPELLS): the gate ends the NPC's maintained spells.</summary>
    DemaintainSpells = 1 << 4,
    /// <summary>NPC flag 0x20000000 (NO_ATTACK): a fight request becomes NONE (setter rule 4).</summary>
    NoAttack = 1 << 5,
    /// <summary>Critter flag 0x10 (FLEEING): the state read reports <see cref="DangerState.Flee"/>.</summary>
    Fleeing = 1 << 6,
    /// <summary>Critter flag 0x4000 (SURRENDERED): the state read reports <see cref="DangerState.Surrender"/>.</summary>
    Surrendered = 1 << 7,
    /// <summary>Critter flag 2 0x200000 (the target lock): the setter ignores every fight request (rule 6).</summary>
    TargetLocked = 1 << 8,
    /// <summary>Critter flag 0x10000 (SPELL_FLEE): the flight's flee spell or item was chosen (0x4AAF50, slice I5).</summary>
    SpellFlee = 1 << 9,
    /// <summary>NPC flag 0x80 (CHECK_WEAPON): the gate's next pass wields the best weapon (slice I6; the full re-wield clears it
    /// too, CL2).</summary>
    CheckWeapon = 1 << 10,
    /// <summary>Critter flag 0x8000000 (NO_FLEE) as the AI sets it for good (0x4AF1D0: a flight's cornered move-away, slice I7); the
    /// setter also honours the caller's <see cref="DangerSetterFacts.NoFlee"/> from the critter's data.</summary>
    NoFlee = 1 << 11,
    /// <summary>Every defined bit.</summary>
    All = (1 << 12) - 1,
}

/// <summary>
/// What the setter reads besides the NPC's danger bits (0x4AABE0). <see cref="Invulnerable"/>: object flag 0x400000, critter
/// flag 2 0x200 (nigh-invulnerable) or the clockwork decoy (name 6719). <see cref="NoFlee"/>: critter flag 0x8000000.
/// <see cref="LeaderExtraordinaryCharisma"/>: the NPC's leader has extraordinary charisma. <see cref="NpcFightingOff"/>: the
/// global "NPC fighting" switch (0x5B50CC) is off; it is on in a normal game, so the default facts are the usual ones.
/// <see cref="FloatsLine"/>: the host's float callback is set and
/// the NPC is active (0x45D790), the condition of the fleeing line. <see cref="TargetIsPc"/>: the target is a PC (rule 8).
/// </summary>
public readonly record struct DangerSetterFacts(
    bool Invulnerable = false,
    bool NoFlee = false,
    bool LeaderExtraordinaryCharisma = false,
    bool NpcFightingOff = false,
    bool FloatsLine = false,
    bool TargetIsPc = false);

/// <summary>The rule that decided the applied state (it may equal the requested one, as NONE for an invulnerable NPC), or stopped
/// the setter early.</summary>
public enum DangerSetterRule
{
    /// <summary>The request was applied as asked.</summary>
    None,
    /// <summary>Rule 1: an invulnerable NPC (or the decoy) gets NONE.</summary>
    Invulnerable,
    /// <summary>Rule 2: FLEE with NO_FLEE becomes COMBAT_FOCUS.</summary>
    NoFlee,
    /// <summary>Rule 3: FLEE or SURRENDER under a leader of extraordinary charisma becomes COMBAT_FOCUS.</summary>
    LeaderCharisma,
    /// <summary>Rule 4: COMBAT_FOCUS with the global switch off becomes NONE (followers' FO8 reads this).</summary>
    FightingOff,
    /// <summary>Rule 4: COMBAT_FOCUS with NO_ATTACK becomes NONE (FO8).</summary>
    NoAttack,
    /// <summary>Rule 6: COMBAT_FOCUS on a target-locked NPC: no NPC flag, focus or reaction change.</summary>
    TargetLocked,
}

/// <summary>The script attachment points of the danger state (SAP numbers of the original, script surface section 1).</summary>
public enum DangerScriptPoint
{
    /// <summary>13, ENTER_COMBAT: the setter's rule 7, the new target as triggerer.</summary>
    EnterCombat = 13,
    /// <summary>14, EXIT_COMBAT: the setter's rule 10, the requested target (may be none) as triggerer.</summary>
    ExitCombat = 14,
    /// <summary>15, START_COMBAT: the gate (slice I6), the danger source as triggerer.</summary>
    StartCombat = 15,
    /// <summary>16, END_COMBAT: the gate in real time and the subturn end (slice I6).</summary>
    EndCombat = 16,
}

/// <summary>
/// One setter call (0x4AABE0) as data: the requested and applied state, the rule that changed it, the danger bits after the
/// call and what the caller must do in the same step. <see cref="WritesFleeingFrom"/>: fleeing-from := target (rule 5).
/// <see cref="LeavesCombatMode"/>: a surrender leaves combat mode (0x4B3F20, when in combat mode and the toggle acts; CL4 is
/// the caller's). <see cref="FleeLine"/>: the fleeing float line toward the target. <see cref="Script"/>: the hook to run,
/// with the target as triggerer. <see cref="AlertSound"/>: the alerted sound (0x4F0ED0, sound 5) for a caller that asks.
/// <see cref="LowersReaction"/>: the NPC's hostility-variant reaction toward the PC target is lowered to parameter 10 when
/// above it (rule 8). <see cref="WritesFocus"/>: combat focus := target (rule 9).
/// </summary>
public readonly record struct DangerTransition(
    DangerState Requested,
    DangerState Applied,
    DangerSetterRule Rule,
    DangerFlags Flags,
    bool WritesFleeingFrom,
    bool LeavesCombatMode,
    bool FleeLine,
    DangerScriptPoint? Script,
    bool AlertSound,
    bool LowersReaction,
    bool WritesFocus);

/// <summary>
/// The danger state machine of the NPC AI (stage A1, core loop section 5; slice I3, ARC-WORLD-079): the state read and the
/// one setter every transition goes through, as pure functions over the NPC's danger bits. Handles (focus, fleeing-from) and
/// the effects outside the bits (scripts, sound, reaction, combat mode) are the caller's, as <see cref="DangerTransition"/>
/// lists them. The network rule 0 does not apply (single player, CL3).
/// </summary>
public static class DangerRules
{
    private const DangerFlags FleeBits = DangerFlags.Fleeing | DangerFlags.Surrendered;
    private const DangerFlags EnterBits = DangerFlags.Fighting | DangerFlags.CheckWield | DangerFlags.CheckGrenade;

    /// <summary>
    /// The state read: FLEEING gives FLEE, else SURRENDERED gives SURRENDER (both with the fleeing-from field as source), else
    /// FIGHTING gives COMBAT_FOCUS (the combat focus field as source), else NONE with no source. The source is the caller's
    /// field (the session's <c>QueryDanger</c>).
    /// </summary>
    [OriginalFunction(0x4AAB00, OriginalFunctionScope.Partial)]
    public static DangerState Read(DangerFlags flags)
    {
        if ((flags & ~DangerFlags.All) != 0) throw new ArgumentOutOfRangeException(nameof(flags));
        if (flags.HasFlag(DangerFlags.Fleeing)) return DangerState.Flee;
        if (flags.HasFlag(DangerFlags.Surrendered)) return DangerState.Surrender;
        return flags.HasFlag(DangerFlags.Fighting) ? DangerState.CombatFocus : DangerState.None;
    }

    /// <summary>
    /// The setter in the original's rule order (core loop transition table, re-read for ARC-WORLD-079). Rule 1 invulnerable →
    /// NONE; rule 2 FLEE with NO_FLEE (the caller's fact or the stored <see cref="DangerFlags.NoFlee"/>) → COMBAT_FOCUS, skipping rule 3; rule 3 FLEE or SURRENDER under an extraordinary leader →
    /// COMBAT_FOCUS; rule 4 COMBAT_FOCUS with the switch off or NO_ATTACK → NONE. Rule 5: FLEE or SURRENDER sets its bit and
    /// clears the other (the line only when neither was set), a surrender leaves combat mode, fleeing-from := target; any
    /// other state clears both. Rule 6: COMBAT_FOCUS on a target-locked NPC stops there. Rule 7: not yet FIGHTING → FIGHTING,
    /// CHECK_WIELD and CHECK_GRENADE, the enter-combat hook and the alerted sound; rules 8 and 9 on every such call. Rule 10:
    /// any other state clears BACKING_OFF and, when FIGHTING, clears it, sets DEMAINTAIN_SPELLS unless fleeing and runs the
    /// exit-combat hook. FO14 (<see cref="CombatAnomalyProfile.LockedNoneStateFights"/> off): an applied state other than
    /// COMBAT_FOCUS also clears the target lock, which the original keeps. MR7
    /// (<see cref="CombatAnomalyProfile.StaleSpellFleeSkipsFleeSpell"/> off): an applied state other than FLEE also clears
    /// SPELL_FLEE, which the original clears only in its flee-spell step (ARC-WORLD-082).
    /// </summary>
    [OriginalFunction(0x4AABE0, OriginalFunctionScope.Partial)]
    public static DangerTransition Set(DangerFlags flags, DangerState requested, DangerSetterFacts facts, CombatAnomalyProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if ((flags & ~DangerFlags.All) != 0) throw new ArgumentOutOfRangeException(nameof(flags));
        if (!Enum.IsDefined(requested)) throw new ArgumentOutOfRangeException(nameof(requested));
        var state = requested;
        var rule = DangerSetterRule.None;
        if (facts.Invulnerable) (state, rule) = (DangerState.None, DangerSetterRule.Invulnerable);
        if (state == DangerState.Flee && (facts.NoFlee || flags.HasFlag(DangerFlags.NoFlee))) (state, rule) = (DangerState.CombatFocus, DangerSetterRule.NoFlee);
        else if (state is DangerState.Flee or DangerState.Surrender && facts.LeaderExtraordinaryCharisma)
            (state, rule) = (DangerState.CombatFocus, DangerSetterRule.LeaderCharisma);
        if (state == DangerState.CombatFocus && (facts.NpcFightingOff || flags.HasFlag(DangerFlags.NoAttack)))
            (state, rule) = (DangerState.None, facts.NpcFightingOff ? DangerSetterRule.FightingOff : DangerSetterRule.NoAttack);

        var line = false;
        var after = flags & ~FleeBits;
        if (state != DangerState.Flee && !profile.StaleSpellFleeSkipsFleeSpell) after &= ~DangerFlags.SpellFlee;
        var fleeing = state is DangerState.Flee or DangerState.Surrender;
        if (fleeing)
        {
            line = facts.FloatsLine && (flags & FleeBits) == 0;
            after |= state == DangerState.Flee ? DangerFlags.Fleeing : DangerFlags.Surrendered;
        }
        if (state == DangerState.CombatFocus)
        {
            if (flags.HasFlag(DangerFlags.TargetLocked))
                return new(requested, state, DangerSetterRule.TargetLocked, after, false, false, false, null, false, false, false);
            var entering = !flags.HasFlag(DangerFlags.Fighting);
            if (entering) after |= EnterBits;
            return new(requested, state, rule, after, false, false, false, entering ? DangerScriptPoint.EnterCombat : null, entering,
                facts.TargetIsPc, true);
        }
        after &= ~DangerFlags.BackingOff;
        DangerScriptPoint? script = null;
        if (flags.HasFlag(DangerFlags.Fighting))
        {
            after &= ~DangerFlags.Fighting;
            if (state != DangerState.Flee) after |= DangerFlags.DemaintainSpells;
            script = DangerScriptPoint.ExitCombat;
        }
        if (!profile.LockedNoneStateFights) after &= ~DangerFlags.TargetLocked;
        return new(requested, state, rule, after, fleeing, state == DangerState.Surrender, line, script, false, false, false);
    }

    /// <summary>
    /// The enemy-list remove (0x4AF9D0): every entry equal to <paramref name="entry"/> goes, the last entry filling its hole and
    /// the same index checked again, so the order is not kept. The original also drops null entries, which a session list
    /// never holds.
    /// </summary>
    [OriginalFunction(0x4AF9D0, OriginalFunctionScope.Partial)]
    public static T[] RemoveEnemy<T>(IReadOnlyList<T> enemies, T entry) where T : notnull
    {
        ArgumentNullException.ThrowIfNull(enemies);
        var list = enemies.ToList();
        for (var i = 0; i < list.Count; i++)
        {
            if (!EqualityComparer<T>.Default.Equals(list[i], entry)) continue;
            list[i] = list[^1];
            list.RemoveAt(list.Count - 1);
            i--;
        }
        return list.ToArray();
    }

    /// <summary>
    /// Whether the enemy-list add (0x4AF930) skips <paramref name="entry"/>: when the entry's PC leader (the entry itself for a
    /// PC) exists and is the owner's PC leader, or when the entry is already listed. Otherwise it is appended at the end.
    /// </summary>
    /// <param name="sameLeader">The entry (a PC) or the entry's PC leader is not null and equals the owner's PC leader.</param>
    [OriginalFunction(0x4AF930, OriginalFunctionScope.Partial)]
    public static bool SkipsEnemy<T>(IReadOnlyList<T> enemies, T entry, bool sameLeader) where T : notnull
    {
        ArgumentNullException.ThrowIfNull(enemies);
        return sameLeader || enemies.Contains(entry);
    }
}
