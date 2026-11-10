using Arcanum.Session;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Items;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

public partial class TerrainViewerApp
{
    // Native observation (ARC-DEV-333, bug B48, triage F2; ARC_AREA_MAP_SMOKE_ONLY=loot-room, map mode with --hero): a take that has no free place in the hero's
    // grid is refused and the item stays where it was (the insert check's room, code 2, ledger WE4). A dead body of the map with at least two plain items (no
    // script, no gold, key or ammunition; a living NPC that is not a follower is killed through the session's life domain when no dead one has them) is looted
    // with the window's Take all. Its items are sized through the session's art hook (a 10-wide frame as high as the hero's free rows allow, so that some but not all
    // fit); the take moves the items that fit and leaves the rest in the body, no item of the hero is held outside the grid, a second Take all changes nothing, and
    // once the hook gives the items one cell each the next Take all takes the rest. The viewer's own art hook is attached again at the end. NONE without a hero,
    // combat values or such a body.
    private async Task RunAreaLootRoomSmokeAsync()
    {
        if (_areaSession is not { } session || session.QueryHero() is null || _combatHost is not { } combat || _inventoryHost?.Map(CurrentAreaMap) is null)
        { GD.Print($"ARC-DEV-333 native-smoke loot-room NONE map={_readyMapName} (no hero, combat values or inventory)"); return; }
        static void Require(bool value, string why)
        {
            if (value) return;
            GD.Print("ARC-DEV-333 loot-room smoke FAIL: " + why);
            throw new InvalidOperationException("AREA_LOOT_ROOM_SMOKE_FAILED");
        }
        if (InventoryOpen) EndInventory();
        var map = AreaRuntimes()[CurrentAreaMap];
        var mapId = new SessionMapId(map.RuntimeMap);
        static bool Plain(SessionInventoryItem item) =>
            item.Facts.Kind.Type is not (SessionItemType.Gold or SessionItemType.Key or SessionItemType.KeyRing or SessionItemType.Ammo) &&
            !item.Facts.HasScript(SessionScriptPoint.Get) && !item.Facts.HasScript(SessionScriptPoint.RemoveItem) &&
            !item.Facts.HasScript(SessionScriptPoint.InsertItem) && !item.Facts.HasScript(SessionScriptPoint.WieldOff);
        var bodies = session.QueryMapInventory(mapId)!.Holders.Where(h => h.Kind == SessionHolderKind.Npc && h.Items.Count(Plain) >= 2 &&
                (h.Dead || session.QueryCompanion(h.Entity).Standing == CompanionStanding.Free))
            .OrderBy(h => h.Entity, StringComparer.Ordinal).ToArray();
        if (bodies.Length == 0) { GD.Print($"ARC-DEV-333 native-smoke loot-room NONE map={map.RuntimeMap} (no NPC holds two plain items)"); return; }
        var body = bodies.FirstOrDefault(h => h.Dead) ?? bodies[0];
        if (!body.Dead && !session.QueryNpcKilled(body.Entity))
        {
            var build = combat.Build(session, map.RuntimeMap, body.Entity);
            Require(build.Combat is not null, $"{body.Entity}: no combat values ({build.Failure})");
            var killed = session.ApplyDamage(session.Version, build.Combat!.Target, new LifeDamage { HitPoints = LifeRules.MaxHitPoints(build.Combat.Facts, 0) + 1000, PlainDeath = true });
            Require(killed.Disposition != GameSessionDisposition.Rejected && session.QueryNpcKilled(body.Entity), $"{body.Entity}: killed: {killed.Failure}");
        }
        IReadOnlyList<SessionInventoryItem> Listed() => session.QueryMapInventory(mapId)!.Holders.Single(h => h.Entity == body.Entity).Items;
        var plain = Listed().Where(Plain).Select(item => item.Entity).ToArray();
        Require(plain.Length >= 2, $"{body.Entity} holds two plain items ({plain.Length})");
        var plainSet = plain.ToHashSet(StringComparer.Ordinal);

        // The height of a 10-wide frame such that the hero's free places take some of the plain items but not all (the first free place for the size, item by item, 0x4675A0).
        // The hero's gold pile may wait for a cell like before (the coins are another rule): it does not count.
        int Waiting() => session.QueryHeroGrid().Unplaced.Count(entity => entity != SessionHeroGrid.GoldEntity);
        var baselineUnplaced = Waiting();
        int Fits(int rows)
        {
            var occupied = InventoryGridRules.Occupied(session.QueryHeroGrid().Items.Select(item => new InventoryGridItem(item.Slot, item.Width, item.Height)), SessionHeroGrid.Rows);
            var count = 0;
            foreach (var _ in plain)
            {
                var slot = InventoryGridRules.FindSlot(occupied, SessionHeroGrid.Columns, rows);
                if (slot < 0) break;
                for (var row = 0; row < rows; row++)
                    for (var column = 0; column < SessionHeroGrid.Columns; column++) occupied[slot + row * SessionHeroGrid.Columns + column] = true;
                count++;
            }
            return count;
        }
        var height = Enumerable.Range(1, SessionHeroGrid.Rows).FirstOrDefault(rows => Fits(rows) is var fits && fits >= 1 && fits < plain.Length);
        if (height == 0) { GD.Print($"ARC-DEV-333 native-smoke loot-room NONE map={map.RuntimeMap} (the hero's free rows cannot take some of {plain.Length} items and not all)"); return; }
        var predicted = Fits(height);

        // The items are sized through the session's art hook; the viewer's own hook is attached again at the end.
        void Hook(uint frameHeight) => session.AttachItemArtSizes((item, kind) => plainSet.Contains(item) ? (320u, frameHeight) : InventoryItemArtSize(session, item, kind));
        Hook((uint)height * 32);
        Require(session.QueryItemCells(plain[0]) == (SessionHeroGrid.Columns, height), $"the hook sizes {plain[0]} as {SessionHeroGrid.Columns} x {height}");
        try
        {
            OpenInventory(body.Entity);
            Require(InventoryOpen && InventoryActive, "the window on the body opens");
            var before = session.QueryPcInventory().Count;
            TakeAllFromBody();
            var movedFirst = plain.Where(entity => session.QueryPcInventory().Any(item => item.Entity == entity)).ToArray();
            var leftFirst = Listed().Where(Plain).Select(item => item.Entity).ToArray();
            var grid = session.QueryHeroGrid();
            Require(movedFirst.Length >= 1, $"Take all moved some of {plain.Length} items (predicted {predicted}): {_inventoryStatus}");
            Require(movedFirst.Length <= predicted, $"no more items moved than the free places allow ({movedFirst.Length} > {predicted})");
            Require(leftFirst.Length >= 1 && movedFirst.Length + leftFirst.Length == plain.Length, $"what did not fit stays in the body ({movedFirst.Length} moved, {leftFirst.Length} left of {plain.Length})");
            Require(_inventoryStatus.Contains("no free room", StringComparison.Ordinal), $"the status names the refusal: {_inventoryStatus}");
            Require(Waiting() == baselineUnplaced, $"no item is held outside the grid ({Waiting()} vs {baselineUnplaced})");
            Require(movedFirst.All(entity => grid.Items.Any(item => item.Entity == entity)), "every moved item has its place in the grid");

            // Every plain item left in the body was refused for room (not for weight): asked once more, each answers the room.
            foreach (var entity in leftFirst)
            {
                var again = _inventoryHost!.Loot(session, body.Entity, entity);
                Require(!AreaInventoryHost.Moved(again) && again.Verdict == SessionInsertVerdict.NoRoom, $"{entity} stayed for lack of room ({again.Failure}, {again.Verdict})");
            }

            // A second Take all changes nothing for the plain items: the room did not grow.
            var pcAfterFirst = session.QueryPcInventory().Count;
            TakeAllFromBody();
            Require(plain.Count(entity => session.QueryPcInventory().Any(item => item.Entity == entity)) == movedFirst.Length, "a second Take all moves no further plain item");
            Require(Waiting() == baselineUnplaced, "still no item outside the grid");

            // The room grows (the hook gives every item one cell): the next Take all takes what stayed.
            Hook(32);
            TakeAllFromBody();
            var leftLast = Listed().Where(Plain).Select(item => item.Entity).ToArray();
            Require(leftLast.Length < leftFirst.Length, $"with room the next Take all takes more ({leftLast.Length} left of {leftFirst.Length})");
            Require(Waiting() == baselineUnplaced, "no item outside the grid at the end");
            GD.Print($"ARC-DEV-333 loot-room body={body.Entity} plain={plain.Length} frameRows={height} predicted={predicted} moved={movedFirst.Length} left={leftFirst.Length} later={leftLast.Length} pc={before}->{pcAfterFirst}");
        }
        finally
        {
            if (InventoryOpen) EndInventory();
            session.AttachItemArtSizes((item, kind) => InventoryItemArtSize(session, item, kind));
        }
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        GD.Print("ARC-DEV-333 native-smoke loot-room PASS");
    }
}
