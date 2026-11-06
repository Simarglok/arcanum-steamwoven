using Arcanum.Simulation.Movement;

namespace Arcanum.Simulation.Combat;

/// <summary>What charging an action's cost in a turn-based round did (<see cref="TurnBasedRules.Charge"/>).</summary>
public enum ActionPointChargeKind
{
    /// <summary>The cost was paid from the action points left.</summary>
    Paid,
    /// <summary>The local PC could not pay but acts anyway: its action points drop to 0 and it takes
    /// <see cref="TurnBasedRules.OverexertionFatigue"/> fatigue damage.</summary>
    Overexerted,
    /// <summary>The action fails: the action points drop to 0 and the actor's subturn ends.</summary>
    Refused,
}

/// <summary>A charge and the action points left after it.</summary>
public readonly record struct ActionPointCharge(ActionPointChargeKind Kind, int ActionPointsLeft);

/// <summary>
/// The stalled-actor check of a turn-based round (the source's "animations complete" check): the actor last seen and its
/// action points at that check. An actor whose action points did not change between two checks in a row is forced to
/// spend the rest. <see cref="Observe"/> returns the next tracker; the round resets it at the start of every round.
/// </summary>
public readonly record struct TurnBasedStall<TActor>(TActor? LastActor, int LastActionPoints, bool Forced)
    where TActor : class
{
    /// <summary>
    /// One check of <paramref name="actor"/> with <paramref name="actionPoints"/> left. The same actor as last time with
    /// the same action points sets <see cref="Forced"/>; the action points are then remembered. Another actor only
    /// becomes the last actor: with <paramref name="keepPreviousActionPoints"/> (the original, anomaly A11) the action
    /// points of the previous actor stay the reference, so the second check of the new actor compares against them;
    /// without it (Steamwoven) the new actor's own action points become the reference and a flag set for the previous actor is dropped (the original's flag is global and
    /// stays set for whoever is current when the handler runs, goal-table.md GT23, GT24).
    /// </summary>
    public TurnBasedStall<TActor> Observe(TActor actor, int actionPoints, bool keepPreviousActionPoints)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (Equals(actor, LastActor))
            return this with { Forced = Forced || actionPoints == LastActionPoints, LastActionPoints = actionPoints };
        return keepPreviousActionPoints ? this with { LastActor = actor } : this with { LastActor = actor, LastActionPoints = actionPoints, Forced = false };
    }
}

/// <summary>
/// The turn-based round rules as pure functions (ARC-COMBAT-003; the source's turn-based combat, ARC-RES-030 C6–C14):
/// who takes part and in which order, the action points a subturn grants, the cost of actions, charging a cost with the
/// local PC's overexertion, and the NPC's low-fatigue gate. Numbers are checked in the
/// original 1.0.7.4 executable (see
/// docs/world/turn-based-round-runtime.md). Every function validates its inputs (<see cref="ArgumentException"/>).
/// </summary>
public static class TurnBasedRules
{
    /// <summary>The smallest scan radius around the PC.</summary>
    public const int MinScanRadius = 10;
    /// <summary>The fewest action points a subturn grants.</summary>
    public const int MinActionPoints = 5;
    /// <summary>Game time a round adds when its last subturn ends.</summary>
    public const long RoundGameTimeMs = 1000;
    /// <summary>Fatigue damage of an overexertion, before fatigue resistance.</summary>
    public const int OverexertionFatigue = 2;
    /// <summary>An NPC above this share of its maximum fatigue (in percent) may start an action it cannot pay.</summary>
    public const int NpcLowFatiguePercent = 35;

    public const int WalkStepCost = 2;
    public const int RunStepCost = 1;
    public const int UseObjectCost = 2;
    public const int PickUpCost = 1;
    public const int CastSpellCost = 4;
    public const int UseSkillCost = 4;
    public const int UseItemCost = 4;
    /// <summary>ARC-COMBAT-023: the throw goal's charge (goal 37's end, the shared goal callback 0x433270, ledger AG10, TT rows).</summary>
    public const int ThrowCost = 2;
    /// <summary>Getting up in a round: only on the critter's own subturn, for this cost (anomaly A3, as the original).</summary>
    public const int GetUpCost = 5;
    /// <summary>The weapon speed of an unarmed attack.</summary>
    public const int UnarmedWeaponSpeed = 10;
    /// <summary>The bonus to weapon speed with at least apprentice training in the weapon's skill.</summary>
    public const int TrainedWeaponSpeedBonus = 5;

    /// <summary>Bound of the magnitude of a stat, speed or weapon speed these rules read.</summary>
    public const int MaxStat = 1000;
    /// <summary>Bound of the action points of a subturn.</summary>
    public const int MaxActionPoints = 1000;
    /// <summary>Bound of an action cost (a long move costs more than any subturn grants).</summary>
    public const int MaxActionCost = 1_000_000;
    /// <summary>Bound of a path length in steps.</summary>
    public const int MaxPathSteps = 10_000;
    /// <summary>Bound of the magnitude of a fatigue value.</summary>
    public const int MaxFatigue = 1_000_000;

    /// <summary>Half-width of the square scanned around the PC: <c>max(10, PE / 2 + 5)</c> from its effective perception.</summary>
    public static int ScanRadius(int perception)
    {
        CheckStat(perception, nameof(perception));
        return Math.Max(MinScanRadius, perception / 2 + 5);
    }

    /// <summary>Whether <paramref name="cell"/> lies in the square <c>center ± radius</c>, bounds included.</summary>
    public static bool InScanSquare(CellCoord center, CellCoord cell, int radius)
    {
        if (radius < 0) throw new ArgumentOutOfRangeException(nameof(radius));
        return Math.Abs((long)cell.X - center.X) <= radius && Math.Abs((long)cell.Y - center.Y) <= radius;
    }

    /// <summary>
    /// The round order from the scan order: participants that are not of the PC's party keep their order, then the PCs
    /// and the PCs' followers in their order (anomaly A1: the original also moves followers). There is no initiative roll.
    /// </summary>
    public static IReadOnlyList<T> Order<T>(IReadOnlyList<T> scanOrder, Func<T, bool> pcParty)
    {
        ArgumentNullException.ThrowIfNull(scanOrder);
        ArgumentNullException.ThrowIfNull(pcParty);
        var party = new List<T>();
        var result = new List<T>(scanOrder.Count);
        foreach (var participant in scanOrder) (pcParty(participant) ? party : result).Add(participant);
        result.AddRange(party);
        return result;
    }

    /// <summary>
    /// Whether a participant loses its subturn: not active (dead, unconscious, stunned, paralysed, stoned, destroyed or
    /// switched off), or hidden from drawing while not sleeping.
    /// </summary>
    public static bool IsSkipped(bool active, bool hidden, bool sleeping) => !active || hidden && !sleeping;

    /// <summary>The action points of a subturn: <c>max(5, Speed)</c> from the actor's effective speed.</summary>
    public static int ActionPointGrant(int speed)
    {
        CheckStat(speed, nameof(speed));
        return Math.Max(MinActionPoints, speed);
    }

    /// <summary>
    /// The weapon speed an attack's cost reads: the weapon's speed factor plus its magic speed adjustment (already
    /// adjusted for the wielder's aptitude), +5 with at least apprentice training in the weapon's skill.
    /// <see cref="UnarmedWeaponSpeed"/> without a weapon.
    /// </summary>
    public static int WeaponSpeed(int speedFactor, int magicSpeedAdjustment, SkillTraining training)
    {
        CheckStat(speedFactor, nameof(speedFactor));
        CheckStat(magicSpeedAdjustment, nameof(magicSpeedAdjustment));
        if (!Enum.IsDefined(training)) throw new ArgumentOutOfRangeException(nameof(training));
        return speedFactor + magicSpeedAdjustment + (training >= SkillTraining.Apprentice ? TrainedWeaponSpeedBonus : 0);
    }

    /// <summary>An attack's cost from the weapon speed: 1 above 24, 2 above 20, else <c>max(1, 8 − speed / 3)</c>.</summary>
    public static int AttackCost(int weaponSpeed)
    {
        CheckStat(weaponSpeed, nameof(weaponSpeed));
        return weaponSpeed > 24 ? 1 : weaponSpeed > 20 ? 2 : Math.Max(1, 8 - weaponSpeed / 3);
    }

    /// <summary>The estimate of a move: 2 per path step, 1 for a PC that always runs.</summary>
    public static int MoveCost(int pathSteps, bool alwaysRunningPc)
    {
        if (pathSteps is < 0 or > MaxPathSteps) throw new ArgumentOutOfRangeException(nameof(pathSteps));
        return (alwaysRunningPc ? RunStepCost : WalkStepCost) * pathSteps;
    }

    /// <summary>One step: 1 when running with fatigue above 0, else 2 (a runner without fatigue walks).</summary>
    public static int StepCost(bool running, int currentFatigue)
    {
        CheckFatigue(currentFatigue, nameof(currentFatigue));
        return running && currentFatigue > 0 ? RunStepCost : WalkStepCost;
    }

    /// <summary>
    /// The cost the attack preview checks: the attack cost, plus the move to an adjacent cell when the attacker reaches
    /// only one cell (unarmed or a weapon of range ≤ 1) and the target is farther than one cell.
    /// </summary>
    public static int AttackActionCost(int attackCost, bool reachOne, int distance, int adjacentPathSteps, bool alwaysRunningPc)
    {
        CheckCost(attackCost, nameof(attackCost));
        if (distance < 0) throw new ArgumentOutOfRangeException(nameof(distance));
        return attackCost + (reachOne && distance > 1 ? MoveCost(adjacentPathSteps, alwaysRunningPc) : 0);
    }

    /// <summary>Using an object: 2, plus the move to an adjacent cell when not on the object's cell.</summary>
    public static int UseObjectActionCost(bool sameCell, int adjacentPathSteps, bool alwaysRunningPc) =>
        UseObjectCost + (sameCell ? 0 : MoveCost(adjacentPathSteps, alwaysRunningPc));

    /// <summary>Picking up an item: 1, plus the move to its cell when not on it.</summary>
    public static int PickUpActionCost(bool sameCell, int pathSteps, bool alwaysRunningPc) =>
        PickUpCost + (sameCell ? 0 : MoveCost(pathSteps, alwaysRunningPc));

    /// <summary>
    /// ARC-COMBAT-023 (ledger TT rows): what a throw (goal 37, from the inventory or a "default throws" weapon) costs its thrower in a
    /// round: the goal charges <see cref="ThrowCost"/> through the shared goal callback (0x433270), which exempts the local PC, and
    /// nothing else of the throw charges (the starter's gate 0x4348E0 is called with 0, the attack cost 0x4B7C30 is skipped). With
    /// <see cref="CombatAnomalyProfile.PcThrowCostsNothing"/> the local PC throws for 0, as in the original; Steamwoven charges it
    /// <see cref="ThrowCost"/> too. An NPC's end-step charge of its pending cost (EX6) is the AI host's; the turn-based host charges it
    /// (the viewer's "default throws" attack still pays the attack cost until ARC-UI-063).
    /// </summary>
    public static int ThrowActionCost(bool localPc, CombatAnomalyProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return localPc && profile.PcThrowCostsNothing ? 0 : ThrowCost;
    }

    /// <summary>
    /// The preview check of a cost: affordable when the action points cover it, or for the local PC with action points
    /// left and current fatigue above 1 (it would overexert).
    /// </summary>
    public static bool CanAfford(int actionPoints, int cost, bool localPc, int currentFatigue)
    {
        CheckActionPoints(actionPoints);
        CheckCost(cost, nameof(cost));
        CheckFatigue(currentFatigue, nameof(currentFatigue));
        return actionPoints >= cost || actionPoints > 0 && localPc && currentFatigue > 1;
    }

    /// <summary>
    /// Charges a cost to the actor whose subturn it is: paid when the action points cover it; else the local PC with
    /// action points left and current fatigue above 1 overexerts (action points 0, fatigue damage); else the action is
    /// refused (action points 0, the subturn ends).
    /// </summary>
    public static ActionPointCharge Charge(int actionPoints, int cost, bool localPc, int currentFatigue)
    {
        CheckActionPoints(actionPoints);
        CheckCost(cost, nameof(cost));
        CheckFatigue(currentFatigue, nameof(currentFatigue));
        if (actionPoints >= cost) return new(ActionPointChargeKind.Paid, actionPoints - cost);
        return actionPoints > 0 && localPc && currentFatigue > 1
            ? new(ActionPointChargeKind.Overexerted, 0)
            : new(ActionPointChargeKind.Refused, 0);
    }

    /// <summary>
    /// The fatigue damage of an overexertion after fatigue resistance: 2 less <c>¾ × normal resistance</c> percent of it
    /// (only a positive resistance counts), as the damage step applies it.
    /// </summary>
    public static int OverexertionFatigueDamage(int normalResistance)
    {
        if (Math.Abs(normalResistance) > LifeLimits.MaxResistance) throw new ArgumentOutOfRangeException(nameof(normalResistance));
        var resistance = 3 * normalResistance / 4;
        return resistance > 0 ? OverexertionFatigue - resistance * OverexertionFatigue / 100 : OverexertionFatigue;
    }

    /// <summary>
    /// Whether an NPC may start an action in its subturn: when its action points cover the cost, or while its current
    /// fatigue is above 35 % of its maximum (<c>100 × current / max &gt; 35</c>); the later charge then decides. A
    /// maximum of 0 or below counts as not above (the original divides by it; both rule modes avoid the fault).
    /// </summary>
    public static bool NpcMayStartAction(int actionPoints, int cost, int currentFatigue, int maxFatigue)
    {
        CheckActionPoints(actionPoints);
        CheckCost(cost, nameof(cost));
        CheckFatigue(currentFatigue, nameof(currentFatigue));
        CheckFatigue(maxFatigue, nameof(maxFatigue));
        if (cost <= actionPoints) return true;
        return maxFatigue > 0 && 100L * currentFatigue / maxFatigue > NpcLowFatiguePercent;
    }

    private static void CheckStat(int value, string name)
    {
        if (value is < -MaxStat or > MaxStat) throw new ArgumentOutOfRangeException(name);
    }

    private static void CheckActionPoints(int value)
    {
        if (value is < 0 or > MaxActionPoints) throw new ArgumentOutOfRangeException("actionPoints");
    }

    private static void CheckCost(int value, string name)
    {
        if (value is < 0 or > MaxActionCost) throw new ArgumentOutOfRangeException(name);
    }

    private static void CheckFatigue(int value, string name)
    {
        if (value is < -MaxFatigue or > MaxFatigue) throw new ArgumentOutOfRangeException(name);
    }
}
