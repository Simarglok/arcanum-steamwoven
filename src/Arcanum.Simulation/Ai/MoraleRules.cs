using Arcanum.Simulation.Combat;

namespace Arcanum.Simulation.Ai;

/// <summary>
/// Morale of the NPC AI (stage A4, research ARC-RES-058; slice I5, ARC-WORLD-082, re-read in the retail 1.0.7.4 executable): the
/// flee question an attack asks, the party sums it compares, the flee-spell step of a fleeing NPC, the flight's turn into a
/// surrender, the surrender's end, stop fleeing and the back-off with its start in combat and after a PC's kill, as pure
/// functions over an <see cref="IMoraleWorld"/> and an <see cref="IAiRollSource"/>. The setter calls and the bit writes are the
/// session's (<c>SetDangerState</c> and the morale operations); the flee goal, the "away" path and the run are the executor's
/// (slice I7).
/// </summary>
public static class MoraleRules
{
    /// <summary>A back-off runs to parameter 15 tiles, or to this many when parameter 15 is at most 1.</summary>
    public const int DefaultBackOffRange = 5;

    /// <summary>Combat perform starts a back-off on 1 in this many.</summary>
    public const int BackOffStartSides = 20;

    /// <summary>A PC's kill sets the back-off in the square of this half-size around the PC.</summary>
    public const int KillBackOffHalfSize = 3;

    /// <summary>
    /// The "away" path's direction offsets from the direction toward the target (0x41F6C0), tried in order: straight away, 45°
    /// either side, 90°, 135° and last straight toward the target. Each is a straight line of <c>range − distance</c> tiles that
    /// stops at a blocking tile, a blocked traversal or a spotted trap (path flag 0x800, at most 100 steps; fire is not checked).
    /// </summary>
    public static IReadOnlyList<int> AwayOffsets { get; } = Array.AsReadOnly(new[] { 4, 5, 3, 6, 2, 7, 1, 0 });

    // ---- the flee question ----

    /// <summary>
    /// The party the flee test counts for a critter (0x4AE020, 0x4AE0A0): a PC counts itself and its recursive follower list; a
    /// critter with a leader counts its leader's party (itself among the followers); a leaderless NPC counts itself and its own
    /// followers, except in the Original setting, where it counts alone (MR3, <see cref="CombatAnomalyProfile.NpcLeaderPartyCountsAlone"/>).
    /// The level sum adds each member's own level (P2). A leader the world no longer knows counts as none.
    /// </summary>
    [OriginalFunction(0x4AE020, OriginalFunctionScope.Partial)]
    [OriginalFunction(0x4AE0A0, OriginalFunctionScope.Partial)]
    public static PartyStrength Party(IMoraleWorld world, string id, CombatAnomalyProfile profile)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(profile);
        var critter = Get(world, id);
        if (critter.Kind == HostilityKind.Pc) return PartyOf(world, critter);
        if (critter.Leader is { } leader && world.Find(leader) is { } head) return PartyOf(world, head);
        return profile.NpcLeaderPartyCountsAlone ? new(1, critter.Level) : PartyOf(world, critter);
    }

    /// <summary>
    /// Should-flee (0x4AB2F0) of <paramref name="npc"/> attacked by <paramref name="target"/>, first match wins: NO_FLEE or mind
    /// control → fight; the target's hit-point percent below parameter 3 → fight; own hit-point percent at most parameter 0 →
    /// flee; the target's party size minus its own at least parameter 1 → flee; the target's party level sum minus its own at
    /// least parameter 2 → flee; otherwise fight. Differences are signed 32-bit, as in the original.
    /// </summary>
    [OriginalFunction(0x4AB2F0, OriginalFunctionScope.Full)]
    public static FleeDecision ShouldFlee(IMoraleWorld world, string npc, string target, CombatAnomalyProfile profile)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(profile);
        var n = Get(world, npc);
        var t = Get(world, target);
        if (n.NoFlee) return new(FleeReason.NoFlee, null, null);
        if (n.MindControlled) return new(FleeReason.MindControlled, null, null);
        var row = world.MoraleParameters(npc);
        if (t.HealthPercent < row.StandWhileTargetWeakPercent) return new(FleeReason.TargetWeak, null, null);
        if (n.HealthPercent <= row.FleeHpPercent) return new(FleeReason.LowHealth, null, null);
        var theirs = Party(world, target, profile);
        var own = Party(world, npc, profile);
        if (unchecked(theirs.Size - own.Size) >= row.FleeOutnumbered) return new(FleeReason.Outnumbered, own, theirs);
        return new(unchecked(theirs.LevelSum - own.LevelSum) >= row.FleeOutlevelled ? FleeReason.Outlevelled : FleeReason.Fight, own, theirs);
    }

    /// <summary>
    /// The reaction to an attack (<see cref="HostilityRules.ReactToAttack"/>) with its weighing (0x4AB2A0) asking
    /// <see cref="ShouldFlee"/> about the attacker: yes requests FLEE from it, no COMBAT_FOCUS on it. The answer is returned for the
    /// overlay; the setter call is the caller's.
    /// </summary>
    [OriginalFunction(0x4AB2A0, OriginalFunctionScope.Partial)]
    public static MoraleReaction ReactToAttack(IMoraleWorld world, string npc, string attacker, IAiRollSource rolls, CombatAnomalyProfile profile)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(profile);
        FleeDecision? decision = null;
        var reaction = HostilityRules.ReactToAttack(world, npc, attacker, () =>
        {
            decision = ShouldFlee(world, npc, attacker, profile);
            return decision.Value.Flees;
        }, rolls);
        return new(reaction, decision);
    }

    // ---- flight and surrender ----

    /// <summary>
    /// The flee-spell step (0x4AAF50) the danger update runs for a fleeing NPC: with SPELL_FLEE set it is cleared and nothing is
    /// chosen; otherwise <paramref name="choosesFleeAction"/> (the list chooser 0x4ABF10 over the NPC's flee list with the NPC as
    /// target, slice I8) is asked, and a choice sets SPELL_FLEE and becomes the pass's action.
    /// </summary>
    [OriginalFunction(0x4AAF50, OriginalFunctionScope.Partial)]
    public static FleeSpellStep FleeSpell(DangerFlags flags, Func<bool> choosesFleeAction)
    {
        ArgumentNullException.ThrowIfNull(choosesFleeAction);
        if ((flags & ~DangerFlags.All) != 0) throw new ArgumentOutOfRangeException(nameof(flags));
        if (flags.HasFlag(DangerFlags.SpellFlee)) return new(FleeSpellOutcome.ClearsFlag, flags & ~DangerFlags.SpellFlee);
        return choosesFleeAction() ? new(FleeSpellOutcome.Chosen, flags | DangerFlags.SpellFlee) : new(FleeSpellOutcome.NoneChosen, flags);
    }

    /// <summary>
    /// Perform fleeing (0x4AC620) after its setter call: the setter was asked for SURRENDER with the pass's danger source, and the
    /// flee goal (type 25) away from that source starts when the surrender was applied, or always in the Original setting (MR9,
    /// <see cref="CombatAnomalyProfile.FleeGoalAfterRefusedSurrender"/>), where it also starts after the setter turned the
    /// surrender into NONE (invulnerable) or COMBAT_FOCUS (a leader of extraordinary charisma). The session's
    /// <c>PerformFleeing</c> runs both steps.
    /// </summary>
    public static bool StartsFleeGoal(DangerTransition surrender, CombatAnomalyProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (surrender.Requested != DangerState.Surrender) throw new ArgumentException("Perform fleeing asks the setter for SURRENDER.", nameof(surrender));
        return surrender.Applied == DangerState.Surrender || profile.FleeGoalAfterRefusedSurrender;
    }

    /// <summary>
    /// Perform surrender (0x4AC660) of a surrendered NPC: true when what it surrendered to (its fleeing-from handle) is none, no
    /// longer known, dead or unconscious, and the caller then asks the setter for NONE with no target; otherwise nothing is done
    /// (no new animation). The other end of a surrender, the calm-down roll, is the danger update's
    /// (<see cref="DangerUpdatePath.SurrenderCalms"/>).
    /// </summary>
    [OriginalFunction(0x4AC660, OriginalFunctionScope.Partial)]
    public static bool SurrenderEnds(IHostilityWorld world, string npc)
    {
        ArgumentNullException.ThrowIfNull(world);
        Get(world, npc);
        var danger = world.Danger(npc);
        if (danger.State != DangerState.Surrender) throw new InvalidOperationException($"Perform surrender runs in SURRENDER, not {danger.State}.");
        return danger.Source is not { } source || world.Find(source) is not { } s || s.Dead || s.Unconscious;
    }

    /// <summary>
    /// Stop fleeing (0x4AF170) for <paramref name="critter"/> in <paramref name="state"/>: an NPC in FLEE asks the setter for NONE
    /// with no target (a surrendered NPC stays surrendered), and every flee goal of an NPC is interrupted whatever its state; any
    /// other object is left alone. Callers: the fear effect's end, the kill notification (on the victim), set no-flee and the
    /// resurrection.
    /// </summary>
    [OriginalFunction(0x4AF170, OriginalFunctionScope.Partial)]
    public static StopFleeingStep StopFleeing(HostilityCritter critter, DangerState state)
    {
        ArgumentNullException.ThrowIfNull(critter);
        if (!Enum.IsDefined(state)) throw new ArgumentOutOfRangeException(nameof(state));
        var npc = critter.Kind == HostilityKind.Npc;
        return new(npc && state == DangerState.Flee, npc);
    }

    // ---- back off ----

    /// <summary>
    /// The back-off start of combat perform (0x4AC6E0): when parameter 15 is above 1 and the NPC is closer than it to its target,
    /// a 1..20 roll (<see cref="AiRollPurpose.BackOffStart"/>) of 1 starts a back-off (BACKING_OFF and CHECK_GRENADE, then the run
    /// at once); otherwise no roll is made and combat perform goes on to its grenade and attack goal.
    /// </summary>
    [OriginalFunction(0x4AC6E0, OriginalFunctionScope.Partial)]
    public static bool StartsBackOff(IMoraleWorld world, string npc, string target, IAiRollSource rolls)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(rolls);
        Get(world, npc);
        Get(world, target);
        var range = world.MoraleParameters(npc).CombatDistance;
        return range > 1 && world.Distance(npc, target) < range && rolls.Roll(AiRollPurpose.BackOffStart, 1, BackOffStartSides) == 1;
    }

    /// <summary>
    /// The back-off run (0x4AC7B0) before its path request: nothing without BACKING_OFF; the range is parameter 15, or 5 when it
    /// is at most 1; at least the range from the target the back-off ends (<see cref="BackOffRunStep.Reached"/>), else the NPC
    /// asks the "away" path (<see cref="AwayOffsets"/>) toward a tile <c>range − distance</c> out and runs to its end, and with no
    /// path the back-off ends (MR15: the original's retry of a failed line changes nothing).
    /// </summary>
    [OriginalFunction(0x4AC7B0, OriginalFunctionScope.Partial)]
    public static BackOffRun BackOff(IMoraleWorld world, string npc, string target, DangerFlags flags)
    {
        ArgumentNullException.ThrowIfNull(world);
        if ((flags & ~DangerFlags.All) != 0) throw new ArgumentOutOfRangeException(nameof(flags));
        Get(world, npc);
        Get(world, target);
        var parameter = world.MoraleParameters(npc).CombatDistance;
        var range = parameter > 1 ? parameter : DefaultBackOffRange;
        if (!flags.HasFlag(DangerFlags.BackingOff)) return new(BackOffRunStep.NotBackingOff, range);
        return new(world.Distance(npc, target) >= range ? BackOffRunStep.Reached : BackOffRunStep.Runs, range);
    }

    /// <summary>
    /// The kill notification (0x4AA1B0) of <paramref name="victim"/> killed by <paramref name="killer"/> (none for a death without
    /// a killer): the victim stops fleeing (<see cref="StopFleeing"/>); when the killer is a PC, every NPC of the square of
    /// half-size 3 around the killer (<see cref="IHostilityWorld.Square"/> centred on the PC, NPCs only) that is not dead, lacks
    /// NO_FLEE, shares the victim's faction and is in COMBAT_FOCUS on the killer starts backing off (BACKING_OFF, not
    /// CHECK_GRENADE). A killer the world no longer knows counts as none.
    /// </summary>
    [OriginalFunction(0x4AA1B0, OriginalFunctionScope.Partial)]
    public static KillNotificationPlan KillNotification(IMoraleWorld world, string victim, string? killer)
    {
        ArgumentNullException.ThrowIfNull(world);
        var v = Get(world, victim);
        var stop = StopFleeing(v, world.Danger(victim).State);
        if (killer is null || world.Find(killer) is not { Kind: HostilityKind.Pc }) return new(stop, []);
        var backsOff = new List<string>();
        foreach (var id in world.Square(killer, KillBackOffHalfSize))
        {
            if (world.Find(id) is not { Kind: HostilityKind.Npc } n || n.Dead || n.NoFlee || !world.FactionSame(victim, id)) continue;
            var danger = world.Danger(id);
            if (danger.State == DangerState.CombatFocus && danger.Source == killer) backsOff.Add(id);
        }
        return new(stop, backsOff.AsReadOnly());
    }

    private static PartyStrength PartyOf(IMoraleWorld world, HostilityCritter root)
    {
        int size = 1, levels = root.Level;
        foreach (var follower in world.Followers(root.Id))
        {
            size++;
            levels = unchecked(levels + Get(world, follower).Level);
        }
        return new(size, levels);
    }

    private static HostilityCritter Get(IHostilityWorld world, string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        return world.Find(id) ?? throw new ArgumentException($"The world knows no object '{id}'.", nameof(id));
    }
}
