using Arcanum.Data.Area;
using Arcanum.Session;
using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Magic;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

public partial class TerrainViewerApp
{
    private const int UnseenForceSpell = 2;

    // ARC-MAGIC-1230 (UO5, UO6 of ARC-RES-1109): Unseen Force (spell 2, a script's free cast) at an item lying on the map's ground with weight below 5: the cast's Knockback request starts the goal run on the item and the run
    // slides its ground cell four tiles away from the caster (or to a block); the item's body is drawn on the cell it was left on, it is picked there, and the session's ground cell is the same. A heavier item and an item in nobody's reach are never
    // tried. The candidates, nearest to the hero first, are tried until one is pushed (a first step into a wall leaves the item where it lay). NONE when the map has no such item the cast reaches.
    private string RunAreaItemKnockbackSmoke(GameSessionOwner session, Action tick)
    {
        static void Require(bool value, string why) { if (!value) throw new InvalidOperationException("ARC-MAGIC-1230 item knockback smoke: " + why); }
        if (_aiHost is null || _inventoryHost is null || HeroWorldCell() is not { } hero || session.QueryHeroLife() is not { Status.IsActive: true })
        {
            GD.Print("ARC-MAGIC-1230 native-smoke item-knockback SKIP (no AI host, inventory host or active hero)");
            return "skipped";
        }
        var map = new SessionMapId(CurrentAreaMap);
        var candidates = (session.QueryMapInventory(map)?.Ground ?? [])
            .Select(ground => session.QueryGroundItem(ground.Item.Entity)).OfType<SessionLyingItem>()
            .Where(item => item.Weight < 5 && AiExecutorRules.Distance(hero, item.Cell) <= 24)
            .OrderBy(item => AiExecutorRules.Distance(hero, item.Cell)).Take(12).ToArray();
        if (candidates.Length == 0)
        {
            GD.Print("ARC-MAGIC-1230 native-smoke item-knockback NONE (no ground item of weight below 5 near the hero)");
            return "none";
        }
        var tried = new List<string>();
        var pushed = new List<string>();
        foreach (var item in candidates)
        {
            var endsSeen = _knockbackEndCountForSelfTest;
            // A script's cast without a caster: no parent, so no casting goal and none of the hero's stat checks (a premade hero's willpower is under the spell's minimum); its BEGIN is an event of the session's queue, which the
            // next frames fire. The push comes from the run's start location, which is no tile here (the cast has no source object), so the item slides away from the map's corner.
            var attempt = SpellCasterNow()?.CastByScript(null, UnseenForceSpell, item.Entity, free: true, unresistable: true);
            tried.Add($"{item.Entity}@{item.Cell.X},{item.Cell.Y}w{item.Weight}:{attempt?.Outcome}");
            if (attempt?.Outcome != SpellCastAttemptOutcome.Started) continue;
            var frames = 0;
            for (; frames < 400 && (_knockbackEndCountForSelfTest == endsSeen || KnockbackHost().Count > 0); frames++)
            {
                _areaSmokeTime++;
                tick();
                WorldFrameForSelfTest(new(-1, default), new[] { StickLevel(default) }, .05, true, SimulationTime.At(_areaSmokeTime, 20));
                FlushSpellLog(null);
            }
            if (_knockbackEndCountForSelfTest == endsSeen) continue;
            var end = _knockbackEndsForSelfTest[^1];
            if (end.Target != item.Entity || end.Path.Count == 0) continue;
            Require(session.QueryGroundItem(item.Entity) is { } now && now.Cell == end.Tile, $"the item's ground cell is the tile the push left it on ({end.Tile.X},{end.Tile.Y})");
            Require(end.Path.Count <= KnockbackRules.Distance && AiExecutorRules.Distance(item.Cell, end.Tile) == end.Path.Count, $"the item moved {end.Path.Count} tiles in a line: {item.Cell.X},{item.Cell.Y} -> {end.Tile.X},{end.Tile.Y}");
            Require(end.Outcome != KnockbackOutcome.Exhausted || end.Path.Count == KnockbackRules.Distance, "a free push goes four tiles");
            Require(!session.QueryGoals().RunsOf(item.Entity).Any(r => r.Bottom.Goal == Arcanum.Simulation.Goals.GoalType.Knockback), "no knockback run is left on the item");
            // The draw follows: the body stands on the new cell and the pick resolves to it.
            _groundStamp = null;
            SyncAreaGroundBodies();
            var key = _inventoryHost.Maps.FirstOrDefault(m => m.RuntimeMap == map.RuntimeMap) is { } home && home.TryGetRow(item.Entity, out var row) ? row.Key : PlacedKey(map.RuntimeMap, item.Entity);
            Require(_groundBodies.TryGetValue(key, out var drawn) && drawn.MapX == end.Tile.X && drawn.MapY == end.Tile.Y, $"the item is drawn on the cell it was left on ({end.Tile.X},{end.Tile.Y})");
            Require(AreaInventoryTargetOf(key) is { Kind: AreaInventoryTargetKind.GroundItem } target && target.Row.WorldX == end.Tile.X && target.Row.WorldY == end.Tile.Y,
                "the pick walks to the cell the item was left on");
            GD.Print($"ARC-MAGIC-1230 item-knockback item={item.Entity} weight={item.Weight} {item.Cell.X},{item.Cell.Y} -> {end.Tile.X},{end.Tile.Y} outcome={end.Outcome} tiles={end.Path.Count} frames={frames} broken={session.IsItemDestroyed(item.Entity)}");
            pushed.Add($"{end.Outcome} {item.Cell.X},{item.Cell.Y}->{end.Tile.X},{end.Tile.Y}");
            // A push that ran its whole line is the one the smoke wants; a blocked one (a wall, a critter, an object) is kept and the next candidate is tried for a free line.
            if (end.Outcome == KnockbackOutcome.Exhausted) break;
        }
        if (pushed.Count > 0)
        {
            GD.Print($"ARC-MAGIC-1230 native-smoke item-knockback PASS pushed={string.Join(" | ", pushed)} tried={tried.Count}");
            return pushed[^1];
        }
        GD.Print($"ARC-MAGIC-1230 native-smoke item-knockback NOT-PUSHED tried={string.Join(" | ", tried)}");
        Require(false, "no candidate item was pushed: " + string.Join(" | ", tried));
        return "failed";
    }
}
