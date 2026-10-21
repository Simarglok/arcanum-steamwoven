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

    public static readonly NavigationRuleProfile Steamwoven = new() { Name = "steamwoven" };

    public static readonly NavigationRuleProfile Original = new() { Name = "original", FarClickDoesNotMove = true };

    public static IReadOnlyList<NavigationRuleProfile> All { get; } = new[] { Steamwoven, Original };

    public static NavigationRuleProfile? FromName(string? name) => All.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.Ordinal));
}
