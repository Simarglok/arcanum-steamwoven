using Arcanum.Session;
using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Party;

namespace Arcanum.TerrainViewer.Core;

/// <summary>
/// ARC-WORLD-046: the AREA viewer's side of the follower orders. <see cref="Issue"/> gathers the facts of the companions of the shown
/// map (following and waiting ones), sends the order through the session (<see cref="GameSessionOwner.IssueCompanionOrder"/>) and runs
/// the goals its outcomes ask for: walk here to the order's tile and the spread-out step, both ordered goals of the companion's follower
/// that following does not replace. Lines are named by their message range only.
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
    /// and its wait-timer facts. A companion whose reaction the session cannot answer is left out (out of reach).
    /// </summary>
    public static IReadOnlyList<CompanionOrderFacts> Facts(GameSessionOwner session, AreaCompanionGroup group, CellCoord pc,
        AreaConversationState state, Func<AreaCompanion, string> name)
    {
        ArgumentNullException.ThrowIfNull(session); ArgumentNullException.ThrowIfNull(group); ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(name);
        var facts = new List<CompanionOrderFacts>();
        foreach (var companion in group.Companions)
        {
            if (session.QueryCompanion(companion.Entity).Standing == CompanionStanding.Free) continue;
            if (AreaCompanionLoyalty.Facts(session, companion, state) is not { } consent) continue;
            facts.Add(new(companion.Entity, name(companion), FollowDistanceRules.Distance(companion.Grid.State.CommittedCell, pc), false, consent,
                AreaCompanionRoster.WaitFacts(companion.Facts)));
        }
        return facts;
    }

    /// <summary>
    /// Sends the order and runs its goals at <paramref name="at"/>: a walk-here outcome walks the companion to <see cref="CompanionOrderRequest.Tile"/>,
    /// a spread-out outcome that asks for the step walks it <see cref="SpreadStep"/> cells away from the PC. Returns the session's result
    /// and the companions whose goal did not start (its cell is off the map's steps); the group's standings are read again.
    /// </summary>
    public static (SessionCompanionOrderResult Result, IReadOnlyList<string> GoalsNotStarted) Issue(GameSessionOwner session, AreaCompanionGroup group,
        CompanionOrderRequest request, CellCoord pc, SimulationTime at)
    {
        ArgumentNullException.ThrowIfNull(session); ArgumentNullException.ThrowIfNull(group); ArgumentNullException.ThrowIfNull(request);
        var result = session.IssueCompanionOrder(session.Version, request);
        group.Refresh(session);
        var notStarted = new List<string>();
        foreach (var outcome in result.Outcomes)
        {
            if (group.Companions.FirstOrDefault(c => c.Entity == outcome.Entity) is not { } companion) continue;
            var started = outcome.Effect == CompanionOrderEffect.WalkRequested && request.Tile is { } tile ? companion.Follow.WalkTo(tile, at)
                : outcome.StartSpreadGoal ? companion.Follow.WalkTo(Away(companion.Grid.State.CommittedCell, pc), at)
                : true;
            if (!started) notStarted.Add(outcome.Entity);
        }
        return (result, notStarted);
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
        + (outcome.Failure != GameSessionFailure.None ? $" failure={outcome.Failure}" : "");

    /// <summary>The log line of an order.</summary>
    public static string Log(string source, CompanionOrderType type, SessionCompanionOrderResult result) =>
        $"{LogPrefix} order source={source} type={type} disposition={result.Disposition} failure={result.Failure} reached={result.Outcomes.Count}"
        + string.Concat(result.Outcomes.Select(o => " | " + Describe(o)));
}
