namespace Arcanum.Simulation.Navigation;

/// <summary>
/// The click route's rule-mode switches (ARC-WORLD-072, contract docs/world/rule-modes.md): the Original value of a switch is
/// <c>true</c>. Read when a click sets a goal (<see cref="RouteFollower.SetTarget"/>), not saved.
/// </summary>
public sealed record NavigationRuleProfile
{
    public required string Name { get; init; }

    /// <summary>Ledger LR4 and LR7 (owner decision 2026-10-21): the original has no long walking route, so a click more than 32
    /// tiles away (the larger axis) cannot start a walk and the PC does not move. Steamwoven follows such a goal along a
    /// <see cref="RouteCorridor"/> (ARC-WORLD-042).</summary>
    public bool FarClickDoesNotMove { get; init; }

    /// <summary>Ledger LRA1 (LR2, LR5; ARC-WORLD-073, the owner's decision 2026-10-21 made it a switch): outside combat mode the
    /// original plans a walk with living critters ignored (the request's flag 0x10) and checks them only before each segment's last
    /// step, so the walker passes through one on any other step. Steamwoven (the project's proposal): living critters block the plan
    /// and every step, and a refused step ends the walk. ARC-WORLD-073 implements the planning side (read when a plan is made); the
    /// walk-through in the movement's step admission is ARC-WORLD-074 (NOT READY: until then a refused step ends the walk in both modes).</summary>
    public bool MidSegmentCrittersWalkedThrough { get; init; }

    public static readonly NavigationRuleProfile Steamwoven = new() { Name = "steamwoven" };

    public static readonly NavigationRuleProfile Original = new() { Name = "original", FarClickDoesNotMove = true, MidSegmentCrittersWalkedThrough = true };

    public static IReadOnlyList<NavigationRuleProfile> All { get; } = new[] { Steamwoven, Original };

    public static NavigationRuleProfile? FromName(string? name) => All.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.Ordinal));
}
