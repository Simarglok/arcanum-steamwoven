using Arcanum.Scripting.Scr;
using Arcanum.Session;
using Arcanum.Simulation.Movement;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

public partial class TerrainViewerApp
{
    // ARC-MAGIC-042 (technology queue slice T-m1, ledger TI4–TI6): the hero's thrown item runs its hit (7) or miss (8) script where it lands. The six creature items of the installed data (flag 0x20) carry the
    // same script at both points: it transforms the item into a critter of a basic prototype (action 40), makes it follow the PC (21) or has it attack everyone in the vicinity (36). The hook is the thrown item's landing
    // (EndOnTile, after the session's landing): a strike that hit runs point 7 with the struck object as the extra object, a strike that missed or hit a wall and a flight that ended short run point 8. An NPC's thrown
    // item runs none (it is the AI's grenade: no script is attached to the installed ones).
    private readonly List<AreaThrownItemRun> _thrownItemRunsForSelfTest = [];

    /// <summary>The runs of thrown items' scripts since the smoke last cleared the list (smoke stages read it).</summary>
    internal IReadOnlyList<AreaThrownItemRun> ThrownItemRunsForSelfTest => _thrownItemRunsForSelfTest;

    /// <summary>The NPCs of the shown map that stand within <paramref name="reach"/> tiles of <paramref name="tile"/> on both axes, as the AI host places them (the map NPCs, the created critters and the followers).</summary>
    private IReadOnlyList<string> ScriptCrittersNear(CellCoord tile, int reach)
    {
        if (AreaAiHostNow() is not { } ai) return [];
        var near = new List<string>();
        foreach (var entity in ai.Scene.Npcs.Select(npc => npc.Entity).Concat(ai.Scene.Followers?.Npcs.Select(npc => npc.Entity) ?? []).Distinct(StringComparer.Ordinal))
            if (ai.Tile(entity) is { } at && Math.Abs((long)at.X - tile.X) <= reach && Math.Abs((long)at.Y - tile.Y) <= reach) near.Add(entity);
        return near;
    }

    /// <summary>
    /// Runs the script of the thrown item <paramref name="item"/> that landed on <paramref name="tile"/> at <paramref name="point"/> (the hit with <paramref name="target"/>, or the miss) and applies it: the item becomes
    /// a created critter the AI host runs, joined to the party when the script says follow, and the critter's attack events are raised. Nothing for the flight of another session or map, an NPC's thrown item or an item with no script
    /// there; a run the host refuses is said and applies nothing.
    /// </summary>
    private void RunThrownItemScript(AreaProjectile projectile, string item, CellCoord tile, int point, string? target)
    {
        var owner = projectile.Owner;
        if (projectile.Thrower is not null || projectile.Shooter is not null || !ReferenceEquals(owner, _areaSession) || projectile.Map != CurrentAreaMap) return;
        if (_inventoryHost?.ItemScripts(owner) is not { } items || AreaAiHostNow() is not { } ai)
        {
            GD.Print($"ARC-MAGIC-042 thrown-item-script item={item} point={point}: no item script host or AI host on the shown map");
            return;
        }
        var world = new AreaThrownItemWorld(ScriptCritterPrototype, ScriptCrittersNear, () => _worldActor.Session is AreaGridSession grid ? LiveCell(grid) : null,
            CritterMade: entity =>
            {
                SyncCreatedCritters(owner, ai); QueueCreatedInventory(entity);
                // ARC-DEV-402 (TG9): the transform's creation (0x4ED8B0 → 0x442260) ends with the location notification like any other creation.
                if (owner.QueryCreatedObject(entity) is { } made) QueueCreationNotice(entity, made.Cell);
            },
            Attack: (attacker, victim) => $"{ai.ScriptAttack(attacker, victim)}");
        var ran = new AreaThrownItemScripts(owner, items, world, CurrentAreaMap).Run(point, item, tile, target);
        if (ran.Outcome == AreaThrownItemOutcome.NoScript) { GD.Print($"ARC-MAGIC-042 thrown-item-script item={item} point={point}: no script at the point"); return; }
        _thrownItemRunsForSelfTest.Add(ran);
        GD.Print($"ARC-MAGIC-042 thrown-item-script item={item} point={point} at={tile.X},{tile.Y} target={target ?? "none"} outcome={ran.Outcome} critter={ran.Critter ?? "none"} followed={ran.Followed}"
            + $" attacks={string.Join(",", ran.Attacks.Select(a => $"{a.Attacker}>{a.Victim}:{a.Detail}"))} {ran.Detail}");
        if (ran.Critter is not null) { _groundStamp = null; SyncAreaInventory(); }
        RefreshAreaStatus();
    }
}
