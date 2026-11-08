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

    /// <summary>Ledger LR5 (LRA1; ARC-WORLD-073, the owner's decision of 2026-10-21 12:09 that outside combat NPCs do not block the
    /// way): outside combat mode a walk plans and steps through living critters in both modes (the request's flag 0x10, LR2), but the
    /// original checks the tile before each segment's last step for a living PC or NPC and ends the walk on one (0x427110). Steamwoven
    /// walks on through (the stop is the original's bug); Original keeps it.</summary>
    public bool SegmentEndCritterEndsWalk { get; init; }

    /// <summary>ARC-DEV-302 (GT148, GT153; owner decision D8 of 2026-11-07): the original's step start tests no entangled flag, so an entangled PC already running (goal 4, 55 or 56) runs
    /// on to the end of its planned path and only a new order fails at the plan (GT149). Original keeps it; Steamwoven holds an entangled PC's steps as a paralysed one's ("what is the
    /// point of entangling otherwise"). The paralysed and stoned PC is held in both.</summary>
    public bool EntangledPcKeepsMoving { get; init; }

    public static readonly NavigationRuleProfile Steamwoven = new() { Name = "steamwoven" };

    public static readonly NavigationRuleProfile Original = new() { Name = "original", FarClickDoesNotMove = true, SegmentEndCritterEndsWalk = true, EntangledPcKeepsMoving = true };

    public static IReadOnlyList<NavigationRuleProfile> All { get; } = new[] { Steamwoven, Original };

    public static NavigationRuleProfile? FromName(string? name) => All.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.Ordinal));
}
