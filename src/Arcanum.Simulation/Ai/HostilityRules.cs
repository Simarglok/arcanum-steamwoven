using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Reputation;

namespace Arcanum.Simulation.Ai;

/// <summary>
/// Hostility and targets of the NPC AI (stage A3, research ARC-RES-057; slice I4, ARC-WORLD-080, re-read in the retail 1.0.7.4
/// executable): target validity, kill-on-sight, protect, upset attacking, the target scan, choosing between two targets, the
/// enemy-list pick, the danger update's target decision, the reaction to an attack, the attack event's steps, the noise
/// squares and the witness rule, as pure functions over an <see cref="IHostilityWorld"/> and an <see cref="IAiRollSource"/>.
/// Every roll is drawn exactly where the original draws. Writes (focus, who-hit-me-last, enemy list, lock, reaction, combat mode)
/// are the session's: the rules return what to write. The original's scan re-entrancy flag (0x5F8498) is modelled: validity
/// asked inside a scan never starts a nested scan. A nested scan's turn toward a concealed PC (validity test 5 outside a scan)
/// is not returned: it only rotates the NPC.
/// </summary>
public static class HostilityRules
{
    /// <summary>The validity leash, the choose-target reach and the scan's distance key for an unconscious critter.</summary>
    public const int LeashTiles = 20, UnconsciousKey = 1000;

    /// <summary>The scan keeps the first 50 critters of the rectangle query (HS20).</summary>
    public const int MaxScanCandidates = 50;

    /// <summary>A decoy fools an NPC of intelligence below 5; a smart NPC refuses it as a target.</summary>
    public const int DecoyIntelligence = 5;

    /// <summary>A surrendered NPC rolls to calm down at this hit-point percent or more, on 1 in <see cref="SurrenderCalmSides"/>.</summary>
    public const int SurrenderCalmHealth = 80, SurrenderCalmSides = 500;

    // ---- validity ----

    /// <summary>
    /// Target validity (0x4AB990) of <paramref name="candidate"/> for <paramref name="npc"/> outside a scan: the tests in the
    /// original's order (<see cref="TargetValidity"/>); test 5 runs the scan when the NPC fights lethally.
    /// </summary>
    [OriginalFunction(0x4AB990, OriginalFunctionScope.Partial)]
    public static TargetValidity Validate(IHostilityWorld world, string npc, string? candidate, CombatAnomalyProfile profile)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(profile);
        return Check(world, Get(world, npc), candidate, false, profile);
    }

    private static TargetValidity Check(IHostilityWorld world, HostilityCritter npc, string? candidate, bool scanning, CombatAnomalyProfile profile)
    {
        if (candidate is null || world.Find(candidate) is not { } c) return TargetValidity.Missing;
        if (c.Destroyed || c.Off || c.DontDraw || c.Invulnerable) return TargetValidity.Gone;
        if (c.Id == npc.Id) return TargetValidity.Self;
        if (c.IsCritter)
        {
            if (c.Dead) return TargetValidity.Dead;
            if (c.Unconscious && (npc.NonLethal || !scanning && Scan(world, npc, profile).Target is { } other && other != c.Id))
                return TargetValidity.Unconscious;
            if (c.Id == npc.Leader) return TargetValidity.Leader;
            if (c.Leader is not null && c.Leader == npc.Leader) return TargetValidity.FellowFollower;
            if (c.Leader == npc.Id) return TargetValidity.Follower;
            if (c.Stoned) return TargetValidity.Stoned;
            if (!CanTargetInvisible(npc, c)) return TargetValidity.Invisible;
            if (npc.Intelligence >= DecoyIntelligence && c.Decoy) return TargetValidity.Decoy;
        }
        else if (c.Kind == HostilityKind.Wall) return TargetValidity.Wall;
        else if (c.Busted) return TargetValidity.Busted;
        if (world.Distance(npc.Id, c.Id) > LeashTiles) return TargetValidity.TooFar;
        if (npc.Leader is { } leader && world.Distance(leader, c.Id) > LeashTiles) return TargetValidity.TooFarFromLeader;
        if (c.IsCritter && c.Concealed && c.MasterProwler && !world.See(npc.Id, c.Id).Perceived) return TargetValidity.UnseenProwler;
        return TargetValidity.Valid;
    }

    /// <summary>
    /// 0x45A580: whether the NPC may target <paramref name="target"/> despite invisibility: yes when it has no invisible spell flag
    /// or the NPC detects invisibility; else no when it has the invisible object flag; else yes for a non-PC and for a PC that
    /// attempts an attack or a spell or is in combat mode.
    /// </summary>
    [OriginalFunction(0x45A580, OriginalFunctionScope.Full)]
    public static bool CanTargetInvisible(HostilityCritter npc, HostilityCritter target)
    {
        ArgumentNullException.ThrowIfNull(npc);
        ArgumentNullException.ThrowIfNull(target);
        if (!target.InvisibleSpell || npc.DetectsInvisible) return true;
        if (target.ObjectInvisible) return false;
        return target.Kind != HostilityKind.Pc || target.AttemptingAttack || target.InCombatMode;
    }

    /// <summary>0x4AF800: <paramref name="candidate"/> is a decoy for the NPC: alive, the NPC's intelligence below 5, the decoy.</summary>
    [OriginalFunction(0x4AF800, OriginalFunctionScope.Full)]
    public static bool DecoyFor(HostilityCritter npc, HostilityCritter candidate)
    {
        ArgumentNullException.ThrowIfNull(npc);
        ArgumentNullException.ThrowIfNull(candidate);
        return !candidate.Dead && npc.Intelligence < DecoyIntelligence && candidate.Decoy;
    }

    // ---- decision tables ----

    /// <summary>
    /// Kill-on-sight (0x4AE120) outside a scan (the dialog callers): invalid → NO; the will-kos script's veto → NO; surrendered to
    /// the candidate → NO; for an NPC without a PC leader: KOS against an enshrouded candidate (undead NPC) or an animal-enshrouded
    /// one (animal NPC) → NO, KOS without override and not of one faction → FACTION, a PC whose AI reaction is at most parameter 10
    /// → REACTION, an alignment gap of at least parameter 11 → ALIGNMENT, a decoy → DECOY; then GUARD (an NPC fighting someone the
    /// NPC protects, or a leaderless monster fighting anyone while the NPC is a guard, unless upset); else NO.
    /// </summary>
    [OriginalFunction(0x4AE120, OriginalFunctionScope.Full)]
    public static KillOnSight KillOnSight(IHostilityWorld world, string npc, string candidate, CombatAnomalyProfile profile)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(profile);
        return Kos(world, Get(world, npc), candidate, false, profile);
    }

    private static KillOnSight Kos(IHostilityWorld world, HostilityCritter npc, string candidate, bool scanning, CombatAnomalyProfile profile)
    {
        if (Check(world, npc, candidate, scanning, profile) != TargetValidity.Valid) return Ai.KillOnSight.No;
        if (!world.AllowsKillOnSight(npc.Id, candidate)) return Ai.KillOnSight.No;
        var danger = world.Danger(npc.Id);
        if (danger.State == DangerState.Surrender && danger.Source == candidate) return Ai.KillOnSight.No;
        var c = Get(world, candidate);
        if (npc.PcLeader is null)
        {
            if (npc.KillOnSight)
            {
                if (c.Enshrouded && npc.Undead || c.AnimalEnshrouded && npc.Animal) return Ai.KillOnSight.No;
                if (!npc.KosOverride && !world.FactionSame(npc.Id, c.Id)) return Ai.KillOnSight.Faction;
            }
            var parameters = world.Parameters(npc.Id);
            if (c.Kind == HostilityKind.Pc && world.HostilityReaction(npc.Id, c.Id) <= parameters.HostileReaction) return Ai.KillOnSight.Reaction;
            if (Math.Abs((long)npc.Alignment - c.Alignment) >= parameters.HostileAlignmentGap) return Ai.KillOnSight.Alignment;
            if (DecoyFor(npc, c)) return Ai.KillOnSight.Decoy;
        }
        if (c.Kind != HostilityKind.Npc) return Ai.KillOnSight.No;
        var fight = world.Danger(c.Id);
        if (fight.State != DangerState.CombatFocus || fight.Source is not { } victim) return Ai.KillOnSight.No;
        // A stale handle (an object the world no longer knows) is protected by nobody.
        var defends = world.Find(victim) is { } v && Protect(world, npc, v) != Protection.No || npc.Guard && c.Monstrous && c.Leader is null;
        return defends && Upset(world, npc, c, npc.Leader, profile) == UpsetReason.None ? Ai.KillOnSight.Guard : Ai.KillOnSight.No;
    }

    /// <summary>
    /// Protect (0x4AE3A0): SELF for the protector itself, GROUP for its (non-null) leader, NO while it is mind-controlled, FACTION
    /// for one faction, ORIGIN for a guard of the same origin, else NO. It does not test whether the protected is a follower.
    /// </summary>
    [OriginalFunction(0x4AE3A0, OriginalFunctionScope.Full)]
    public static Protection Protect(IHostilityWorld world, string protector, string protectedCritter)
    {
        ArgumentNullException.ThrowIfNull(world);
        return Protect(world, Get(world, protector), Get(world, protectedCritter));
    }

    private static Protection Protect(IHostilityWorld world, HostilityCritter p, HostilityCritter q)
    {
        if (p.Id == q.Id) return Protection.Self;
        if (p.Leader is not null && p.Leader == q.Id) return Protection.Group;
        if (p.MindControlled) return Protection.No;
        if (world.FactionSame(p.Id, q.Id)) return Protection.Faction;
        return p.Guard && ReputationRules.OriginSame(p.Origin, q.Origin) ? Protection.Origin : Protection.No;
    }

    /// <summary>
    /// Upset attacking (0x4ADCC0): whether the NPC refuses to attack <paramref name="target"/> on behalf of <paramref name="leader"/>.
    /// A mind-controlled NPC whose controller is the leader, or (no controller found) that has a PC leader, obeys (NONE); a target
    /// that is not a critter → NONE; the target's leader equal to the leader → PARTY; with a leader, a good NPC (alignment above
    /// 0) and a target of alignment at least parameter 12 → GOOD; one origin → ORIGIN; one faction → FACTION; else NONE. HS9: two
    /// null leaders match only in the Original setting (<see cref="CombatAnomalyProfile.UpsetPartyMatchesMissingLeader"/>).
    /// </summary>
    [OriginalFunction(0x4ADCC0, OriginalFunctionScope.Full)]
    public static UpsetReason Upset(IHostilityWorld world, string npc, string target, string? leader, CombatAnomalyProfile profile)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(profile);
        return Upset(world, Get(world, npc), Get(world, target), leader, profile);
    }

    private static UpsetReason Upset(IHostilityWorld world, HostilityCritter npc, HostilityCritter target, string? leader, CombatAnomalyProfile profile)
    {
        if (npc.MindControlled)
        {
            if (world.MindController(npc.Id) is { } controller) { if (controller == leader) return UpsetReason.None; }
            else if (npc.PcLeader is not null) return UpsetReason.None;
        }
        if (!target.IsCritter) return UpsetReason.None;
        if (target.Leader == leader && (leader is not null || profile.UpsetPartyMatchesMissingLeader)) return UpsetReason.Party;
        if (leader is not null && npc.Alignment > 0 && target.Alignment >= world.Parameters(npc.Id).GoodFollowerStop) return UpsetReason.Good;
        if (ReputationRules.OriginSame(npc.Origin, target.Origin)) return UpsetReason.Origin;
        return world.FactionSame(npc.Id, target.Id) ? UpsetReason.Faction : UpsetReason.None;
    }

    // ---- the scan ----

    /// <summary>
    /// Find target (0x4AB460): nothing for an invulnerable, nigh-invulnerable or decoy NPC; else the square of half-size
    /// <c>clamp(Perception, 1, 10)</c> (<see cref="IHostilityWorld.Square"/>), its first 50 critters, ordered by key (0 for a decoy
    /// for this NPC, else the distance; +1000 when unconscious; stable). Per candidate: when perceived (heard at Silent for a
    /// concealed one, else Normal, then seen), a concealed PC is remembered, a kill-on-sight candidate is the target (c), and for
    /// an NPC fighting, fleeing or surrendered, its valid danger source is the target when the NPC protects the candidate (or is a
    /// guard and the source a leaderless monster), is not upset by it and perceives it (e). Then, perceived or not, a dead NPC the
    /// scanner protects hands over its valid, perceived focus (f, avenge 0x4AE450). With nothing found the NPC turns toward the
    /// last concealed PC it perceived.
    /// </summary>
    [OriginalFunction(0x4AB460, OriginalFunctionScope.Partial)]
    public static ScanResult FindTarget(IHostilityWorld world, string npc, CombatAnomalyProfile profile)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(profile);
        return Scan(world, Get(world, npc), profile);
    }

    private static ScanResult Scan(IHostilityWorld world, HostilityCritter npc, CombatAnomalyProfile profile)
    {
        if (npc.Invulnerable || npc.NighInvulnerable || npc.Decoy) return new(null, ScanStep.Refused, Ai.KillOnSight.No, null);
        var square = world.Square(npc.Id, AwarenessRules.PerceptionDistance(npc.Perception)) ?? throw new InvalidOperationException("The world gave no square.");
        var candidates = square.Take(MaxScanCandidates).Select(id => Get(world, id)).ToArray();
        if (candidates.Length > 1)
            candidates = candidates.OrderBy(c => (DecoyFor(npc, c) ? 0L : world.Distance(npc.Id, c.Id)) + (c.Unconscious ? UnconsciousKey : 0)).ToArray();
        string? concealed = null;
        foreach (var c in candidates)
        {
            if (Perceives(world, npc, c))
            {
                if (c.Kind == HostilityKind.Pc && c.Concealed) concealed = c.Id;
                var kos = Kos(world, npc, c.Id, true, profile);
                if (kos != Ai.KillOnSight.No) return new(c.Id, ScanStep.KillOnSight, kos, null);
                if (c.Kind == HostilityKind.Npc && AllyHelp(world, npc, c, profile) is { } source) return new(source, ScanStep.AllyHelp, Ai.KillOnSight.No, null);
            }
            if (Avenge(world, npc, c, profile) is { } killer && Perceives(world, npc, Get(world, killer)))
                return new(killer, ScanStep.Avenge, Ai.KillOnSight.No, null);
        }
        return new(null, ScanStep.None, Ai.KillOnSight.No, concealed);
    }

    // Step e: the ally's danger source S, valid for the scanner, when the scanner protects the ally (or guards against a
    // leaderless monster), is not upset by S on its own leader's behalf, and perceives S.
    private static string? AllyHelp(IHostilityWorld world, HostilityCritter npc, HostilityCritter ally, CombatAnomalyProfile profile)
    {
        var danger = world.Danger(ally.Id);
        if (Check(world, npc, danger.Source, true, profile) != TargetValidity.Valid || danger.State == DangerState.None) return null;
        var source = Get(world, danger.Source!);
        if (Protect(world, npc, ally) == Protection.No && !(npc.Guard && source.Monstrous && source.Leader is null)) return null;
        if (Upset(world, npc, source, npc.Leader, profile) != UpsetReason.None) return null;
        return Perceives(world, npc, source) ? source.Id : null;
    }

    // Avenge (0x4AE450): a dead NPC's focus, valid for the scanner, when the scanner protects the dead NPC.
    [OriginalFunction(0x4AE450, OriginalFunctionScope.Partial)]
    private static string? Avenge(IHostilityWorld world, HostilityCritter npc, HostilityCritter dead, CombatAnomalyProfile profile)
    {
        if (!dead.Dead || dead.Kind != HostilityKind.Npc || world.Danger(dead.Id).Focus is not { } focus) return null;
        if (Check(world, npc, focus, true, profile) != TargetValidity.Valid) return null;
        return Protect(world, npc, dead) != Protection.No ? focus : null;
    }

    // The scan's perception: heard (Silent for a concealed critter, else Normal), else seen.
    private static bool Perceives(IHostilityWorld world, HostilityCritter npc, HostilityCritter target) =>
        world.Hear(npc.Id, target.Id, target.Concealed ? Loudness.Silent : Loudness.Normal).Perceived || world.See(npc.Id, target.Id).Perceived;

    // ---- choosing ----

    /// <summary>
    /// Choose target (0x4AB0B0) between the current <paramref name="current"/> and the newcomer <paramref name="newcomer"/>: either
    /// missing gives the other; a non-critter newcomer keeps the current, a non-critter current takes the newcomer; an unconscious
    /// one, then one farther than 20 tiles, loses (newcomer tested first); a decoy for the NPC wins (current first); else a d100
    /// (<see cref="AiRollPurpose.ChooseTargetScore"/>): 1..50 scores <c>level − distance</c>, 51..100 <c>−distance</c>, and the
    /// newcomer wins only with a higher score.
    /// </summary>
    [OriginalFunction(0x4AB0B0, OriginalFunctionScope.Full)]
    public static string? ChooseTarget(IHostilityWorld world, string npc, string? current, string? newcomer, IAiRollSource rolls)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(rolls);
        var n = Get(world, npc);
        // A stale handle (an object the world no longer knows) counts as none.
        if (current is null || world.Find(current) is null) return newcomer;
        if (newcomer is null || world.Find(newcomer) is null) return current;
        var (a, b) = (Get(world, current), Get(world, newcomer));
        if (!b.IsCritter) return current;
        if (!a.IsCritter) return newcomer;
        if (b.Unconscious) return current;
        if (a.Unconscious) return newcomer;
        var distanceB = world.Distance(n.Id, b.Id);
        if (distanceB > LeashTiles) return current;
        var distanceA = world.Distance(n.Id, a.Id);
        if (distanceA > LeashTiles) return newcomer;
        if (DecoyFor(n, a)) return current;
        if (DecoyFor(n, b)) return newcomer;
        var byLevel = rolls.Roll(AiRollPurpose.ChooseTargetScore, 1, 100) <= 50;
        long Score(HostilityCritter c, int distance) => byLevel ? (long)c.Level - distance : -(long)distance;
        return Score(b, distanceB) > Score(a, distanceA) ? newcomer : current;
    }

    /// <summary>
    /// The enemy-list pick (0x4AFA90): <c>start := random(0, n − 1)</c> (<see cref="AiRollPurpose.EnemyListStart"/>; an empty list
    /// draws −1..0, a one-entry list draws nothing), then entries <c>start, start + 1, …</c> modulo n, the first
    /// <paramref name="valid"/> one; none → default. The original first purges entries of objects that no longer exist; that is the
    /// caller's step (<see cref="DangerRules.PurgeEnemies{T}"/>, which <see cref="DangerUpdate"/> runs on the list it passes here), so
    /// the roll's bounds are the purged count.
    /// </summary>
    [OriginalFunction(0x4AFA90, OriginalFunctionScope.Partial)]
    public static T? PickEnemy<T>(IReadOnlyList<T> enemies, Func<T, bool> valid, IAiRollSource rolls) where T : class
    {
        ArgumentNullException.ThrowIfNull(enemies);
        ArgumentNullException.ThrowIfNull(valid);
        ArgumentNullException.ThrowIfNull(rolls);
        var n = enemies.Count;
        var start = rolls.Roll(AiRollPurpose.EnemyListStart, Math.Min(0, n - 1), Math.Max(0, n - 1));
        for (var i = 0; i < n; i++)
        {
            var entry = enemies[(int)(((long)start + i) % n)];
            if (valid(entry)) return entry;
        }
        return null;
    }

    // ---- the danger update ----

    /// <summary>
    /// The danger update's decision (0x4A92D0) for one NPC. Asleep → skipped. NONE: the scan's target is fought, else nothing.
    /// COMBAT_FOCUS: the focus while valid; else the lock is cleared and the scan's target fought (0x4ABBC0); else who-hit-me-last
    /// while valid; else the enemy-list pick; else the setter is asked for NONE. FLEE: the fleeing helper (<see cref="MoraleRules.FleeSpell"/>). SURRENDER:
    /// at 80 % hit points or more a 1..500 roll of 1 (<see cref="AiRollPurpose.SurrenderCalm"/>) asks for NONE.
    /// </summary>
    [OriginalFunction(0x4A92D0, OriginalFunctionScope.Partial)]
    [OriginalFunction(0x4ABBC0, OriginalFunctionScope.Partial)]
    public static DangerUpdatePlan DangerUpdate(IHostilityWorld world, string npc, IAiRollSource rolls, CombatAnomalyProfile profile)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(rolls);
        ArgumentNullException.ThrowIfNull(profile);
        var n = Get(world, npc);
        if (n.Asleep) return new(DangerUpdatePath.Asleep, null, null, false, null);
        var danger = world.Danger(npc);
        switch (danger.State)
        {
            case DangerState.None:
            {
                var scan = Scan(world, n, profile);
                return scan.Target is { } found
                    ? new(DangerUpdatePath.Scan, found, DangerState.CombatFocus, false, scan)
                    : new(DangerUpdatePath.NoTarget, null, null, false, scan);
            }
            case DangerState.CombatFocus:
            {
                if (Check(world, n, danger.Focus, false, profile) == TargetValidity.Valid)
                    return new(DangerUpdatePath.Focus, danger.Focus, DangerState.CombatFocus, false, null);
                var scan = Scan(world, n, profile);
                if (scan.Target is { } found) return new(DangerUpdatePath.Rescan, found, DangerState.CombatFocus, true, scan);
                if (Check(world, n, danger.HitBy, false, profile) == TargetValidity.Valid)
                    return new(DangerUpdatePath.HitBy, danger.HitBy, DangerState.CombatFocus, true, scan);
                // ARC-AI-1317 (VAG6): the pick first purges the list of objects that no longer exist (0x4AF9D0, null handle) and rolls over what is left.
                var enemies = DangerRules.PurgeEnemies(danger.Enemies, world.Exists);
                var purges = enemies.Length != danger.Enemies.Count;
                var pick = PickEnemy(enemies, entry => Check(world, n, entry, false, profile) == TargetValidity.Valid, rolls);
                return pick is not null
                    ? new(DangerUpdatePath.EnemyList, pick, DangerState.CombatFocus, true, scan, purges)
                    : new(DangerUpdatePath.CalmDown, null, DangerState.None, true, scan, purges);
            }
            case DangerState.Flee:
                return new(DangerUpdatePath.Flee, null, null, false, null);
            case DangerState.Surrender:
                return n.HealthPercent >= SurrenderCalmHealth && rolls.Roll(AiRollPurpose.SurrenderCalm, 1, SurrenderCalmSides) == 1
                    ? new(DangerUpdatePath.SurrenderCalms, null, DangerState.None, false, null)
                    : new(DangerUpdatePath.SurrenderStays, null, null, false, null);
            default:
                throw new InvalidOperationException($"The world gave an undefined danger state {danger.State}.");
        }
    }

    /// <summary>
    /// The setter's target after the spell and item choice: the found target, except on the enemy-list path in the Original
    /// setting, where the pass record's target is passed even when the choice replaced it (SP16,
    /// <see cref="CombatAnomalyProfile.EnemyListFocusTakesSpellTarget"/>). Without a fight request there is no target.
    /// </summary>
    public static string? FocusTarget(DangerUpdatePlan plan, string? recordTarget, CombatAnomalyProfile profile)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(profile);
        if (!plan.ChoosesAction) return null;
        return plan.Path == DangerUpdatePath.EnemyList && profile.EnemyListFocusTakesSpellTarget ? recordTarget : plan.Target;
    }

    /// <summary>
    /// Whether the action chosen for the danger update's target stands after the setter call: always, except when setter rule 4
    /// refused the fight (no-attack flag or the "NPC fighting" switch off), where the Original setting keeps it (FO8,
    /// <see cref="CombatAnomalyProfile.NoAttackKeepsChosenAction"/>).
    /// </summary>
    public static bool KeepsChosenAction(DangerTransition transition, CombatAnomalyProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return transition.Rule is not (DangerSetterRule.NoAttack or DangerSetterRule.FightingOff) || profile.NoAttackKeepsChosenAction;
    }

    // ---- attacks ----

    /// <summary>
    /// The reaction to an attack (0x4AA620) of <paramref name="npc"/> attacked by <paramref name="attacker"/>. Dead → nothing. NONE →
    /// weigh. COMBAT_FOCUS → weigh when the attacker is the focus or <see cref="ChooseTarget"/> prefers it, else keep the focus.
    /// FLEE → keep fleeing when it flees the attacker; else weigh when mind-controlled or farther than parameter 4 from what it
    /// flees (0x4AB030). SURRENDER → weigh when it surrendered to the attacker or choose target prefers it, else a flee goal away
    /// from the attacker. Weighing (0x4AB2A0) asks <paramref name="shouldFlee"/> (<see cref="MoraleRules.ShouldFlee"/>, which <see cref="MoraleRules.ReactToAttack"/> passes) and requests FLEE
    /// from or COMBAT_FOCUS on the attacker. Every NPC that is not dead joins the turn-based round.
    /// </summary>
    [OriginalFunction(0x4AA620, OriginalFunctionScope.Partial)]
    public static AttackReaction ReactToAttack(IHostilityWorld world, string npc, string attacker, Func<bool> shouldFlee, IAiRollSource rolls)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(attacker);
        ArgumentNullException.ThrowIfNull(shouldFlee);
        ArgumentNullException.ThrowIfNull(rolls);
        var n = Get(world, npc);
        if (n.Dead) return new(AttackReactionStep.Dead, null, null, false);
        var danger = world.Danger(npc);
        var weigh = danger.State switch
        {
            DangerState.None => true,
            DangerState.CombatFocus or DangerState.Surrender => danger.Source == attacker || ChooseTarget(world, npc, danger.Source, attacker, rolls) == attacker,
            _ => danger.Source != attacker && FleesFar(world, npc),
        };
        if (weigh) return new(AttackReactionStep.Weigh, shouldFlee() ? DangerState.Flee : DangerState.CombatFocus, attacker, true);
        var step = danger.State switch
        {
            DangerState.CombatFocus => AttackReactionStep.KeepFocus,
            DangerState.Flee => AttackReactionStep.KeepFleeing,
            _ => AttackReactionStep.FleeGoal,
        };
        return new(step, null, null, true);
    }

    /// <summary>
    /// 0x4AB030: whether a fleeing NPC may turn on a new attacker: it is mind-controlled, or farther than parameter 4 (flee
    /// distance) from its danger source. With no source recorded, or one the world no longer knows, it counts as far (inference:
    /// the original measures to a null handle).
    /// </summary>
    [OriginalFunction(0x4AB030, OriginalFunctionScope.Partial)]
    public static bool FleesFar(IHostilityWorld world, string npc)
    {
        ArgumentNullException.ThrowIfNull(world);
        var n = Get(world, npc);
        return n.MindControlled || world.Danger(npc).Source is not { } source || world.Find(source) is null ||
            world.Distance(npc, source) > world.Parameters(npc).FleeDistance;
    }

    /// <summary>
    /// ARC-COMBAT-2164 (EX15, 0x4B3170): who receives the attack event of an attack that does not fly when the line of fire struck <paramref name="blocker"/> in place of <paramref name="aimed"/>. In the original the dispatcher sends the
    /// event to the aimed target before the roll (the event's accidental test, struck against aimed, is never true there), and the ranged branch's redirect (0x4ADE00 at 0x4B33E9) then replaces only the struck object, so the aimed target is
    /// alerted, deliberately, and the blocker is not (<see cref="CombatAnomalyProfile.RangedBlockerHitNotifiesAimedTarget"/>, the Original setting). Steamwoven alerts the blocker with the accidental bit, as a stray missile's strike does (EX14).
    /// <paramref name="aimed"/> null (the aimed target is no critter the event can name) leaves the Original setting no victim. A shot that flies is not asked here: its event comes at the strike.
    /// </summary>
    /// <returns>The victim and the event's flags; a null victim raises no event.</returns>
    public static (string? Victim, AttackEventFlags Flags) RangedBlockerNotice(CombatAnomalyProfile profile, string? aimed, string blocker)
    {
        ArgumentNullException.ThrowIfNull(profile); ArgumentNullException.ThrowIfNull(blocker);
        return profile.RangedBlockerHitNotifiesAimedTarget ? (aimed, AttackEventFlags.None) : (blocker, AttackEventFlags.Accidental);
    }

    /// <summary>
    /// The attack event (0x4A9650) of <paramref name="attacker"/> on <paramref name="victim"/> as data. Stops: a missing side; a
    /// nigh-invulnerable or dead critter victim (a sleeping one is woken first); a victim off, not drawn or invulnerable, the decoy
    /// or a wall; an attack on itself. A non-critter attacker makes an NPC victim fight it. An ordered attack runs only the
    /// follower rule of the attacker's leader. Otherwise the attacker's followers are told (unless no fan-out); a non-critter victim
    /// stops; the victim's head loses concealment; an NPC victim loses its KOS override; unless no fan-out, a PC victim responds,
    /// the victim's followers are told and a deliberate attack raises noise; a PC victim stops; the NPC victim turns, records who hit
    /// it (unless accidental between one faction), takes the attacker's team onto its enemy list (an NPC attacker takes the
    /// victim's), changes its reaction toward a PC attacker and reacts to the attack (unless accidental between one faction).
    /// </summary>
    [OriginalFunction(0x4A9650, OriginalFunctionScope.Partial)]
    public static AttackEventPlan AttackEvent(IHostilityWorld world, string? attacker, string? victim, AttackEventFlags flags)
    {
        ArgumentNullException.ThrowIfNull(world);
        if ((flags & ~AttackEventFlags.All) != 0) throw new ArgumentOutOfRangeException(nameof(flags));
        if (attacker is null || victim is null || world.Find(attacker) is not { } a || world.Find(victim) is not { } v)
            return new() { Stop = AttackEventStop.Missing };
        var plan = new AttackEventPlan();
        if (v.IsCritter)
        {
            if (v.NighInvulnerable) return plan with { Stop = AttackEventStop.VictimProtected };
            if (v.Dead) return plan with { Stop = AttackEventStop.VictimDead };
            plan = plan with { WakesVictim = v.Asleep };
        }
        if (v.Off || v.DontDraw || v.Invulnerable) return plan with { Stop = AttackEventStop.VictimGone };
        if (v.Decoy) return plan with { Stop = AttackEventStop.VictimDecoy };
        if (v.Kind == HostilityKind.Wall) return plan with { Stop = AttackEventStop.VictimWall };
        if (a.Id == v.Id) return plan with { Stop = AttackEventStop.Self };
        if (!a.IsCritter) return plan with { Stop = AttackEventStop.ObjectAttacker, VictimFightsObject = v.Kind == HostilityKind.Npc };
        if (flags.HasFlag(AttackEventFlags.Ordered)) return plan with { Stop = AttackEventStop.Ordered, OrderedLeader = a.Leader };
        var fanOut = !flags.HasFlag(AttackEventFlags.NoFanOut);
        var accidental = flags.HasFlag(AttackEventFlags.Accidental);
        plan = plan with { TellsAttackerFollowers = fanOut };
        if (!v.IsCritter) return plan with { Stop = AttackEventStop.ObjectVictim };
        var head = v.PcLeader ?? v.Id;
        plan = plan with
        {
            LosesConcealment = Get(world, head).Concealed ? head : null,
            ClearsKosOverride = v.Kind == HostilityKind.Npc,
            PcVictimResponds = fanOut && v.Kind == HostilityKind.Pc,
            TellsVictimFollowers = fanOut,
            RaisesNoise = fanOut && !accidental,
        };
        if (v.Kind != HostilityKind.Npc) return plan with { Stop = AttackEventStop.PcVictim };
        var shielded = accidental && world.FactionSame(a.Id, v.Id);
        var reaction = a.Kind != HostilityKind.Pc ? AttackReactionChange.None
            : v.Leader == a.Id ? (accidental ? AttackReactionChange.FollowerAccidental : AttackReactionChange.FollowerDeliberate)
            : accidental ? AttackReactionChange.Accidental : AttackReactionChange.Deliberate;
        return plan with
        {
            TurnsVictim = true,
            WritesHitBy = !shielded,
            AddsAttackerTeam = true,
            AddsVictimTeam = a.Kind == HostilityKind.Npc,
            Reaction = reaction,
            ReactsToAttack = !shielded,
        };
    }

    /// <summary>
    /// The noise fan-out's witnesses (0x4A9E10) in order: the NPCs of the square around the attacker when the attacker and the
    /// victim are less than twice the loud radius (30 tiles) apart, then those of the square around the victim when they are more
    /// than 1 tile apart (both squares of half-size <c>NoiseRadius(loudness)</c>, the caller's queries). An NPC in both squares is
    /// handled once, at its first place; the Original setting handles it twice (AW16,
    /// <see cref="CombatAnomalyProfile.WitnessHandledTwice"/>).
    /// </summary>
    [OriginalFunction(0x4A9E10, OriginalFunctionScope.Partial)]
    public static IReadOnlyList<T> NoiseWitnesses<T>(int distance, IReadOnlyList<T> attackerSquare, IReadOnlyList<T> victimSquare,
        CombatAnomalyProfile profile) where T : notnull
    {
        ArgumentNullException.ThrowIfNull(attackerSquare);
        ArgumentNullException.ThrowIfNull(victimSquare);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentOutOfRangeException.ThrowIfNegative(distance);
        var witnesses = new List<T>();
        if (distance < 2 * AwarenessRules.NoiseRadius(Loudness.Loud)) witnesses.AddRange(attackerSquare);
        if (distance > 1)
            witnesses.AddRange(profile.WitnessHandledTwice ? victimSquare : victimSquare.Where(npc => !witnesses.Contains(npc)).ToArray());
        return witnesses.AsReadOnly();
    }

    /// <summary>
    /// The witness rule (0x4A9F10) for <paramref name="witness"/> of <paramref name="attacker"/>'s attack on <paramref name="victim"/>:
    /// nothing for the victim, the attacker, or a witness whose leader (none while it waits) is either; a sleeping witness is woken;
    /// one that protects the victim reacts to an attack by the attacker when it sees or hears the victim; else one that protects
    /// the attacker reacts to an attack by the victim when it sees or hears the attacker; else one that is no guard, has no NO_FLEE
    /// and is in no danger state flees from the attacker when it sees or hears the victim. Sight is tested before hearing, at the
    /// attack's loudness; the caller's sight and hearing see the witness awake.
    /// </summary>
    [OriginalFunction(0x4A9F10, OriginalFunctionScope.Partial)]
    public static WitnessOutcome Witness(IHostilityWorld world, string witness, string attacker, string victim, Loudness loudness)
    {
        ArgumentNullException.ThrowIfNull(world);
        if (!Enum.IsDefined(loudness)) throw new ArgumentOutOfRangeException(nameof(loudness));
        var w = Get(world, witness);
        var (a, v) = (Get(world, attacker), Get(world, victim));
        var leader = w.WaitingHere ? null : w.Leader;
        if (w.Id == v.Id || w.Id == a.Id || leader == a.Id || leader == v.Id) return new(WitnessResponse.None, false);
        var wakes = w.Asleep;
        bool Notices(HostilityCritter target) => world.See(w.Id, target.Id).Perceived || world.Hear(w.Id, target.Id, loudness).Perceived;
        if (Protect(world, w, v) != Protection.No) return new(Notices(v) ? WitnessResponse.ReactToAttacker : WitnessResponse.None, wakes);
        if (Protect(world, w, a) != Protection.No) return new(Notices(a) ? WitnessResponse.ReactToVictim : WitnessResponse.None, wakes);
        if (w.Guard || w.NoFlee || world.Danger(w.Id).State != DangerState.None) return new(WitnessResponse.None, wakes);
        return new(Notices(v) ? WitnessResponse.FleeFromAttacker : WitnessResponse.None, wakes);
    }

    private static HostilityCritter Get(IHostilityWorld world, string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        return world.Find(id) ?? throw new ArgumentException($"The world knows no object '{id}'.", nameof(id));
    }
}
