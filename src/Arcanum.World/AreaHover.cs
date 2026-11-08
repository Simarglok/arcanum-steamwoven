using Arcanum.Data.Area;
using Arcanum.Data.Content;
using Arcanum.Data.Messages;
using Arcanum.Session;
using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Navigation;

namespace Arcanum.World;

/// <summary>What a left press at the pointer would do in crash-site-area (ARC-UI-005). The original's main-mode
/// cursor does not change per target; this action cursor is project policy
/// (docs/rendering/terrain-viewer.md, "Hover tooltip and action cursor").</summary>
public enum AreaHoverAction
{
    /// <summary>The press would be ignored: the pointer is off the world, over GUI, or world input is guarded.</summary>
    None,
    /// <summary>A ground press sets a click-to-move goal that passes the planner's cheap refusals (see
    /// <see cref="AreaHover.Ground"/>); the bounded search itself can still end without a route.</summary>
    Walk,
    /// <summary>A ground press would set a goal the planner refuses outright: outside the area, beyond its axis
    /// reach from the actor, or a cell no neighbour step can enter.</summary>
    NoRoute,
    Talk,
    WalkToTalk,
    Use,
    WalkToUse,
    /// <summary>ARC-UI-004: an item on the ground within reach goes to the PC.</summary>
    PickUp,
    WalkToPickUp,
    /// <summary>ARC-UI-004: a dead NPC within reach opens as a container in the inventory window.</summary>
    Loot,
    WalkToLoot,
    /// <summary>ARC-COMBAT-005: in combat mode a living NPC within the weapon's reach is attacked.</summary>
    Attack,
    WalkToAttack,
    /// <summary>ARC-MAGIC-015: the spell's targeting cursor is open and the object or tile under it passes the spell's area.</summary>
    Cast,
    /// <summary>ARC-MAGIC-015: the targeting cursor is open and nothing under it passes (a press shows at most a message).</summary>
    CastNoTarget,
}

public enum AreaNamesStatus { Ready, ModuleOutsideGameRoot, MountFailed, StackFailed, CatalogFailed }

/// <param name="Catalog">The description tables; null unless <see cref="AreaNamesStatus.Ready"/>.</param>
public sealed record AreaNamesLoad(AreaNamesStatus Status, ObjectDescriptionCatalog? Catalog, string Detail);

/// <summary>Hover decisions shared by the viewer's pointer feedback; Godot-free so they are unit tested.</summary>
public static class AreaHover
{
    /// <summary>The planner's goal pre-check (<see cref="RoutePlanner"/>): some admitted neighbour enters the goal by
    /// an Open step. A false answer is what a click there would end as (NoRoute without a search).</summary>
    public static bool GoalEnterable(IRouteStepSource source, CellCoord goal)
    {
        ArgumentNullException.ThrowIfNull(source);
        foreach (var direction in Enum.GetValues<NeighborDirection>())
        {
            var delta = RoutePlanner.Neighbor(default, direction);
            var (dx, dy) = (delta.X, delta.Y);
            if ((long)goal.X - dx is < int.MinValue or > int.MaxValue || (long)goal.Y - dy is < int.MinValue or > int.MaxValue) continue;
            var neighbor = new CellCoord(goal.X - dx, goal.Y - dy);
            if (source.Contains(neighbor) && source.Assess(neighbor, direction) == RouteStepVerdict.Open) return true;
        }
        return false;
    }

    /// <summary>The ground action for a goal, from the planner's refusals that need no search
    /// (<see cref="RoutePlanner.Plan"/>): both cells inside the area and the goal enterable (<see cref="GoalEnterable"/>).
    /// A goal beyond the planner's axis reach is a walk too: the follower walks it along a corridor (ARC-WORLD-042), unless
    /// <paramref name="farClickDoesNotMove"/> (the Original rule mode, ARC-WORLD-072: the click is refused,
    /// <c>ROUTE_TARGET_OUT_OF_RANGE</c>). The actor's own cell is a walk (the route is already there).</summary>
    public static AreaHoverAction Ground(IRouteStepSource source, CellCoord actor, CellCoord goal, bool farClickDoesNotMove = false)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!source.Contains(actor) || !source.Contains(goal)) return AreaHoverAction.NoRoute;
        if (goal == actor) return AreaHoverAction.Walk;
        if (farClickDoesNotMove && Math.Max(Math.Abs((long)goal.X - actor.X), Math.Abs((long)goal.Y - actor.Y)) > RouteLimits.AxisDistanceMaximum)
            return AreaHoverAction.NoRoute;
        return GoalEnterable(source, goal) ? AreaHoverAction.Walk : AreaHoverAction.NoRoute;
    }

    /// <summary>The action label shown under the name, and alone in the status line.</summary>
    /// <summary>ARC-DEV-304 (GT115): the cursor over a dead critter out of combat mode, the way the click resolves it: a body that carries the spoken-with-dead spell flag is talked to
    /// (the hero talks from less than <paramref name="talkRange"/> cells), any other is looted from the next cell (<paramref name="lootReach"/>).</summary>
    public static AreaHoverAction Body(bool talksDead, int distance, int lootReach, int talkRange) => talksDead
        ? distance < talkRange ? AreaHoverAction.Talk : AreaHoverAction.WalkToTalk
        : distance <= lootReach ? AreaHoverAction.Loot : AreaHoverAction.WalkToLoot;

    public static string Label(AreaHoverAction action) => action switch
    {
        AreaHoverAction.Walk => "walk here",
        AreaHoverAction.NoRoute => "no route",
        AreaHoverAction.Talk => "talk",
        AreaHoverAction.WalkToTalk => "walk to talk",
        AreaHoverAction.Use => "use",
        AreaHoverAction.WalkToUse => "walk to use",
        AreaHoverAction.PickUp => "pick up",
        AreaHoverAction.WalkToPickUp => "walk to pick up",
        AreaHoverAction.Loot => "loot",
        AreaHoverAction.WalkToLoot => "walk to loot",
        AreaHoverAction.Attack => "attack",
        AreaHoverAction.WalkToAttack => "walk to attack",
        AreaHoverAction.Cast => "cast",
        AreaHoverAction.CastNoTarget => "no target",
        _ => "none",
    };

    /// <summary>Tooltip text: the shown name and the left-click action. No tooltip without a named object.</summary>
    public static string? Tooltip(string? name, AreaHoverAction action) =>
        name is null ? null : action == AreaHoverAction.None ? name : $"{name}\nLeft click: {Label(action)}";

    /// <summary>Loads the description tables for the module archive the area was read from, through the game
    /// folder's content stack (<see cref="AreaContentRoot.Discover"/>); otherwise no names are loaded.</summary>
    public static AreaNamesLoad LoadNames(string modulePath)
    {
        var found = AreaContentRoot.Discover(modulePath);
        if (found.Status != AreaContentRootStatus.Ready)
            return new(found.Status == AreaContentRootStatus.MountFailed ? AreaNamesStatus.MountFailed : AreaNamesStatus.ModuleOutsideGameRoot, null, found.Detail);
        var opened = ContentStack.Open(found.Plan!);
        if (opened.Status != ContentStackStatus.Ready) return new(AreaNamesStatus.StackFailed, null, opened.Status.ToString());
        using var stack = opened.Stack!;
        var loaded = ObjectDescriptionCatalog.Load(stack);
        return loaded.Status == DescriptionCatalogStatus.Ready
            ? new(AreaNamesStatus.Ready, loaded.Catalog, "description tables loaded")
            : new(AreaNamesStatus.CatalogFailed, null, $"{loaded.Status} {loaded.File} {loaded.Parse?.Status} {loaded.Read}");
    }
}
