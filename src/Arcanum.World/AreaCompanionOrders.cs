using Arcanum.Data.Area;
using Arcanum.Session;
using Arcanum.Simulation;
using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Party;

namespace Arcanum.World;

/// <summary>
/// ARC-WORLD-046: the AREA viewer's side of the follower orders. <see cref="Issue"/> gathers the facts of the companions of the shown
/// map (following and waiting ones), sends the order through the session (<see cref="GameSessionOwner.IssueCompanionOrder"/>) and runs
/// the goals its outcomes ask for: walk here to the order's tile and the spread-out step, both ordered goals of the companion's follower
/// that following does not replace. Lines are named by their message range only. ARC-WORLD-117: with the shown map's AI host the
/// orders carry each follower's combat facts, the attack order its target (the highlighted object, 0x43C570) and the host as the
/// ordered attack event's world, and each reached follower gets its AI pass through the host (FO1); a back off's interrupt is the host's.
/// ARC-WORLD-119: so are a leave's interrupt (<see cref="AreaAiHost.ObserveLeaves"/>) and the curse's attack event
/// (<see cref="AreaAiHost.OrderAttack"/>), run before the former follower's pass.
/// </summary>
public static class AreaCompanionOrders
{
    public const string LogPrefix = "ARC-WORLD-046";

    /// <summary>The cells the spread-out step moves a companion away from the PC (project approximation of the original's "attempt
    /// spread out" goal, whose move-away distance is not read).</summary>
    public const int SpreadStep = 3;

    /// <summary>The viewer's menu label of an item (Steamwoven words; the original reads its labels from <c>mes\follower_ui.mes</c>).</summary>
    public static string Label(FollowerMenuItem item) => item switch
    {
        FollowerMenuItem.Walk => "walk",
        FollowerMenuItem.Attack => "attack",
        FollowerMenuItem.StayClose => "stay close",
        FollowerMenuItem.SpreadOut => "spread out",
        FollowerMenuItem.BackOff => "back off",
        FollowerMenuItem.Wait => "wait",
        FollowerMenuItem.Inventory => "inventory",
        FollowerMenuItem.CharacterSheet => "character sheet",
        _ => throw new ArgumentOutOfRangeException(nameof(item)),
    };

    /// <summary>
    /// The order facts of the group's following and waiting companions: the name the PC knows (<paramref name="name"/>), the Chebyshev
    /// distance of its cell to the PC's, no mind control (the viewer has none), the follow facts of <see cref="AreaCompanionLoyalty.Facts"/>
    /// and its wait-timer facts. A companion whose reaction the session cannot answer is left out (out of reach). ARC-WORLD-117:
    /// <paramref name="combat"/> gives a companion's combat facts (<see cref="AreaAiHost.OrderFacts"/>); none: the session's defaults.
    /// </summary>
    public static IReadOnlyList<CompanionOrderFacts> Facts(GameSessionOwner session, AreaCompanionGroup group, CellCoord pc,
        AreaConversationState state, Func<AreaCompanion, string> name, Func<string, SessionPeaceFacts>? combat = null)
    {
        ArgumentNullException.ThrowIfNull(session); ArgumentNullException.ThrowIfNull(group); ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(name);
        var facts = new List<CompanionOrderFacts>();
        foreach (var companion in group.Companions)
        {
            if (session.QueryCompanion(companion.Entity).Standing == CompanionStanding.Free) continue;
            if (AreaCompanionLoyalty.Facts(session, companion, state) is not { } consent) continue;
            facts.Add(new(companion.Entity, name(companion), FollowDistanceRules.Distance(companion.Grid.State.CommittedCell, pc), false, consent,
                AreaCompanionRoster.WaitFacts(companion.Facts, session.QueryHeroPartySide()), combat?.Invoke(companion.Entity)));
        }
        return facts;
    }

    /// <summary>
    /// Sends the order and runs its goals at <paramref name="at"/>: a walk-here outcome walks the companion to <see cref="CompanionOrderRequest.Tile"/>,
    /// a spread-out outcome that asks for the step walks it <see cref="SpreadStep"/> cells away from the PC. Returns the session's result
    /// and the companions whose goal did not start (its cell is off the map's steps); the group's standings are read again. With
    /// <paramref name="ai"/> the order goes through <see cref="Send"/>; the goals start right after each follower's order, before its AI
    /// pass (ARC-WORLD-117: the original sets them inside 0x4C31A0, before call 0x4C389D).
    /// </summary>
    public static (SessionCompanionOrderResult Result, IReadOnlyList<string> GoalsNotStarted) Issue(GameSessionOwner session, AreaCompanionGroup group,
        CompanionOrderRequest request, CellCoord pc, SimulationTime at, AreaAiHost? ai = null)
    {
        ArgumentNullException.ThrowIfNull(session); ArgumentNullException.ThrowIfNull(group); ArgumentNullException.ThrowIfNull(request);
        var notStarted = new List<string>();
        void StartGoal(CompanionOrderOutcome outcome)
        {
            if (group.Companions.FirstOrDefault(c => c.Entity == outcome.Entity) is not { } companion) return;
            var started = outcome.Effect == CompanionOrderEffect.WalkRequested && request.Tile is { } tile ? companion.Follow.WalkTo(tile, at)
                : outcome.StartSpreadGoal ? companion.Follow.WalkTo(Away(companion.Grid.State.CommittedCell, pc), at)
                : true;
            if (!started) notStarted.Add(outcome.Entity);
        }
        var result = Send(session, request, ai, StartGoal);
        group.Refresh(session);
        return (result, notStarted);
    }

    /// <summary>
    /// ARC-WORLD-117: sends the order. <paramref name="goals"/> is the host's part of each reached follower's order (its ordered goals),
    /// run right after the order. With the shown map's AI host <paramref name="ai"/> it is the ordered attack event's world, a back
    /// off's stop interrupts the follower's run right after its order (<see cref="AreaAiHost.ApplyStop"/>), a leave (an order's or the
    /// curse's) ends the leaver's run (<see cref="AreaAiHost.ObserveLeaves"/>, FO20), then the goals start, the curse's attack event runs
    /// (<see cref="AreaAiHost.OrderAttack"/>: 0x4C3703 raises it inside the order, after the leave and the insult) and each reached
    /// follower's AI pass runs through the host after that (FO1). Without one: no attack world (an attack order with a target fails
    /// after lifting the stop), no curse attack event and no pass.
    /// </summary>
    public static SessionCompanionOrderResult Send(GameSessionOwner session, CompanionOrderRequest request, AreaAiHost? ai,
        Action<CompanionOrderOutcome>? goals = null)
    {
        ArgumentNullException.ThrowIfNull(session); ArgumentNullException.ThrowIfNull(request);
        if (ai is null) return session.IssueCompanionOrder(session.Version, goals is null ? request : request with { Ordered = goals });
        return session.IssueCompanionOrder(session.Version, request with
        {
            World = ai, Ai = ai,
            Ordered = outcome =>
            {
                if (outcome.Stop is { } stop) ai.ApplyStop(stop);
                ai.ObserveLeaves();
                goals?.Invoke(outcome);
                if (outcome.CurseAttack is { } attack) ai.OrderAttack(attack);
            },
        });
    }

    /// <summary>ARC-WORLD-117: the attack order's target, the highlighted object <paramref name="key"/> (0x43C570) as its session
    /// entity on the shown map; null without one (the order then does nothing, FO3).</summary>
    [OriginalFunction(0x43C570, OriginalFunctionScope.Partial)]
    public static SessionCombatant? Target(AreaCombatHost combat, int runtimeMap, AreaObjectKey? key)
    {
        ArgumentNullException.ThrowIfNull(combat);
        return key is { } k && combat.Entity(runtimeMap, k) is { } entity ? SessionCombatant.Critter(entity) : null;
    }

    /// <summary>The cell <see cref="SpreadStep"/> cells farther from <paramref name="pc"/> along each axis the companion stands off it
    /// (straight away from the PC's side; on the PC's own cell, toward +X); clamped to the cell range, where the step source refuses it.</summary>
    public static CellCoord Away(CellCoord cell, CellCoord pc)
    {
        var dx = Math.Sign((long)cell.X - pc.X); var dy = Math.Sign((long)cell.Y - pc.Y);
        if (dx == 0 && dy == 0) dx = 1;
        static int Clamp(long value) => (int)Math.Clamp(value, int.MinValue, int.MaxValue);
        return new(Clamp(cell.X + (long)dx * SpreadStep), Clamp(cell.Y + (long)dy * SpreadStep));
    }

    /// <summary>One outcome for the log and the HUD: entity, order, effect and the line range shown.</summary>
    public static string Describe(CompanionOrderOutcome outcome) =>
        $"{outcome.Entity} {outcome.Type} -> {outcome.Effect}"
        + (outcome.Lines is { } lines ? $" lines {lines.First}-{lines.Last}" : "")
        + (outcome.Consent is { } consent && consent != FollowConsent.Accepted ? $" ({consent})" : "")
        + (outcome.Failure != GameSessionFailure.None ? $" failure={outcome.Failure}" : "")
        + (outcome.CurseAttack is not null ? " curse-attack" : "")
        + (outcome.Pass is { } pass ? " pass=" + (pass.Failure != GameSessionFailure.None ? pass.Failure.ToString()
            : pass.Pass is { Performed: true } ? "performed" : pass.Pass?.Gate is { } gate ? $"gate {gate.Stop}" : "ran")
            + (pass.WriteFailure != GameSessionFailure.None ? $" write-failure={pass.WriteFailure}" : "") : "");

    /// <summary>The log line of an order.</summary>
    public static string Log(string source, CompanionOrderType type, SessionCompanionOrderResult result) =>
        $"{LogPrefix} order source={source} type={type} disposition={result.Disposition} failure={result.Failure} reached={result.Outcomes.Count}"
        + string.Concat(result.Outcomes.Select(o => " | " + Describe(o)));
}
