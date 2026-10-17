using Arcanum.Data.Area;
using Arcanum.Data.Content;
using Arcanum.Data.Messages;
using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Navigation;

namespace Arcanum.TerrainViewer.Core;

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
}

public enum AreaNamesStatus { Ready, ModuleOutsideGameRoot, MountFailed, StackFailed, CatalogFailed }

/// <param name="Catalog">The description tables; null unless <see cref="AreaNamesStatus.Ready"/>.</param>
public sealed record AreaNamesLoad(AreaNamesStatus Status, ObjectDescriptionCatalog? Catalog, string Detail);

/// <summary>
/// Hover picking and shown names for one published area map. The pick follows the original's screen target rule
/// (CE <c>sub_43D9F0</c>): the frontmost drawn body with an opaque pixel under the pointer, skipping bodies flagged
/// <c>OF_CLICK_THROUGH</c>. Names come from <see cref="ObjectDisplayName"/>; walls and projectiles are picked but
/// show no text, as in the original's message window.
/// </summary>
public sealed class AreaHoverIndex
{
    /// <summary>Effective slot of <c>OBJ_F_FLAGS</c> and the <c>OF_CLICK_THROUGH</c> bit.</summary>
    public const int FlagsSlot = 18;
    public const uint ClickThroughFlag = 0x800;
    public const int WallType = 0, ProjectileType = 4, NpcType = 16;
    /// <summary>The English original's description tables are ASCII; the NPC conversation declares Windows-1252 too.</summary>
    public const MesTextProfile TextProfile = MesTextProfile.Windows1252;

    private readonly Dictionary<AreaObjectKey, AreaObjectRecord> _rows;
    private readonly Dictionary<AreaObjectKey, string?> _names = [];

    /// <param name="objects">The map's object rows (<see cref="AreaSnapshot.Objects"/>).</param>
    /// <param name="commands">The map's plan commands in draw order (<see cref="AreaScenePlan.Commands"/>).</param>
    /// <param name="visitors">ARC-WORLD-017: rows of objects from another map that may stand here (companions); they are
    /// named and picked only where a moving command draws them, never from this map's plan.</param>
    public AreaHoverIndex(IEnumerable<AreaObjectRecord> objects, IReadOnlyList<AreaDrawCommand> commands, ObjectDescriptionCatalog? catalog,
        IEnumerable<AreaObjectRecord>? visitors = null)
    {
        ArgumentNullException.ThrowIfNull(objects);
        ArgumentNullException.ThrowIfNull(commands);
        _rows = objects.ToDictionary(row => row.Key);
        Catalog = catalog;
        // Plan order is draw order (the upright actor marker is inserted elsewhere), so the last hit is frontmost.
        Bodies = Array.AsReadOnly(commands.Where(command => command.Identity is { } key && _rows.TryGetValue(key, out var row) && !ClickThrough(row)).ToArray());
        foreach (var row in visitors ?? []) _rows.TryAdd(row.Key, row);
    }

    public ObjectDescriptionCatalog? Catalog { get; }
    /// <summary>The pickable drawn bodies in draw order.</summary>
    public IReadOnlyList<AreaDrawCommand> Bodies { get; }
    public AreaObjectRecord Row(AreaObjectKey key) => _rows[key];
    public bool Contains(AreaObjectKey key) => _rows.ContainsKey(key);

    public static bool ClickThrough(AreaObjectRecord row)
    {
        ArgumentNullException.ThrowIfNull(row);
        var flags = row.EffectiveFields.SingleOrDefault(field => field.Slot == FlagsSlot && field.Presence == AreaPresence.Value)?.Number;
        return flags is { } value && (unchecked((uint)value) & ClickThroughFlag) != 0;
    }

    /// <summary>The frontmost pickable body for which <paramref name="opaqueAt"/> reports a drawn opaque pixel.</summary>
    public AreaObjectKey? Pick(Func<AreaDrawCommand, bool> opaqueAt)
    {
        ArgumentNullException.ThrowIfNull(opaqueAt);
        for (var index = Bodies.Count - 1; index >= 0; index--)
            if (opaqueAt(Bodies[index])) return Bodies[index].Identity;
        return null;
    }

    /// <summary>ARC-WORLD-016: the same pick over the bodies as drawn, with <paramref name="moving"/> (companions where
    /// they stand) in place of their T0 commands, each at the place plan order gives it.</summary>
    public AreaObjectKey? Pick(Func<AreaDrawCommand, bool> opaqueAt, IReadOnlyList<AreaDrawCommand>? moving)
    {
        if (moving is null || moving.Count == 0) return Pick(opaqueAt);
        ArgumentNullException.ThrowIfNull(opaqueAt);
        var keys = moving.Select(command => command.Identity).ToHashSet();
        var bodies = Bodies.Where(body => !keys.Contains(body.Identity)).ToList();
        foreach (var command in moving)
        {
            if (command.Identity is not { } key || !_rows.TryGetValue(key, out var row) || ClickThrough(row)) continue;
            var at = bodies.FindLastIndex(body => body.Phase < command.Phase
                || (body.Phase == command.Phase && AreaScenePlan.CompareUpright(body, command) <= 0)) + 1;
            bodies.Insert(at, command);
        }
        for (var index = bodies.Count - 1; index >= 0; index--)
            if (opaqueAt(bodies[index])) return bodies[index].Identity;
        return null;
    }

    /// <summary>The text shown for a hovered object, or null when the original shows none (walls, projectiles, an
    /// empty description). Without a catalog the object type is named instead. NPC recognition ("met before") is not
    /// session state yet, so every NPC is shown as not recognized (the new-game state).</summary>
    public string? Name(AreaObjectKey key)
    {
        if (_names.TryGetValue(key, out var cached)) return cached;
        var text = Text(_rows[key], Catalog);
        _names.Add(key, text);
        return text;
    }

    /// <summary>The shown text of one object row (see <see cref="Name"/>), uncached; the inventory window (ARC-UI-004)
    /// names items by the same rule.</summary>
    public static string? Text(AreaObjectRecord row, ObjectDescriptionCatalog? catalog)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (row.Type is WallType or ProjectileType) return null;
        if (catalog is null) return $"[{TypeName(row.Type)}: name unavailable]";
        var resolution = ObjectDisplayName.Resolve(row, catalog, CritterRecognition.NotRecognized);
        return resolution.Status switch
        {
            ObjectNameStatus.Resolved => MesText.Decode(resolution.Message!, TextProfile) is { Status: MesDecodeStatus.Decoded, Text: { } decoded }
                ? decoded.Length == 0 ? null : decoded : $"[{TypeName(row.Type)}: name not decodable]",
            ObjectNameStatus.NoText => null,
            _ => $"[{TypeName(row.Type)}: {resolution.Status}]",
        };
    }

    public static string TypeName(int type) => type switch
    {
        0 => "wall", 1 => "portal", 2 => "container", 3 => "scenery", 4 => "projectile", 5 => "weapon", 6 => "ammo",
        7 => "armor", 8 => "gold", 9 => "food", 10 => "scroll", 11 => "key", 12 => "key ring", 13 => "written",
        14 => "generic item", 15 => "player character", 16 => "NPC", 17 => "trap", _ => "object",
    };
}

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
    /// (<see cref="RoutePlanner.Plan"/>): both cells inside the area, the goal within the axis reach of the actor, and
    /// the goal enterable (<see cref="GoalEnterable"/>). The actor's own cell is a walk (the route is already there).</summary>
    public static AreaHoverAction Ground(IRouteStepSource source, CellCoord actor, CellCoord goal, RouteLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        limits ??= new RouteLimits();
        if (!source.Contains(actor) || !source.Contains(goal)) return AreaHoverAction.NoRoute;
        if (goal == actor) return AreaHoverAction.Walk;
        if (Math.Abs((long)goal.X - actor.X) > limits.AxisDistance || Math.Abs((long)goal.Y - actor.Y) > limits.AxisDistance) return AreaHoverAction.NoRoute;
        return GoalEnterable(source, goal) ? AreaHoverAction.Walk : AreaHoverAction.NoRoute;
    }

    /// <summary>The action label shown under the name, and alone in the status line.</summary>
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
