using Arcanum.Session;
using Arcanum.Simulation.Combat;
using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

public partial class TerrainViewerApp
{
    // Native observation (ARC-DEV-202, bug B03, SM03; ARC_AREA_MAP_SMOKE_ONLY=loot-worn, map mode with --hero): an NPC of the map that
    // wears an item (a dead one if the map data has any, else the first living one in entity order, killed through the session's life
    // domain) is looted item by item through the host, as Take all does: the worn items move to the hero's list unwielded, nothing the
    // hero wears changes, and the body's list loses them. Gold, keys and ammunition stacks are other rules and stay; an unworn item the
    // host refuses is named, not required, and a worn one may be refused only for a script or the insert check. A living NPC that is
    // a follower of the hero is never the body. NONE without a hero, combat values or such an NPC.
    private async Task RunAreaLootWornSmokeAsync()
    {
        if (_areaSession is not { } session || session.QueryHero() is null || _combatHost is not { } combat || _inventoryHost?.Map(CurrentAreaMap) is null)
        { GD.Print($"ARC-DEV-202 native-smoke loot-worn NONE map={_readyMapName} (no hero, combat values or inventory)"); return; }
        static void Require(bool value, string why)
        {
            if (value) return;
            GD.Print("ARC-DEV-202 loot-worn smoke FAIL: " + why);
            throw new InvalidOperationException("AREA_LOOT_WORN_SMOKE_FAILED");
        }
        if (InventoryOpen) EndInventory();
        var map = AreaRuntimes()[CurrentAreaMap];
        var mapId = new SessionMapId(map.RuntimeMap);
        static bool Admitted(SessionInventoryItem item) => item.Facts.Kind.Type is not (SessionItemType.Gold or SessionItemType.Key or SessionItemType.KeyRing or SessionItemType.Ammo);
        var wearers = session.QueryMapInventory(mapId)!.Holders.Where(h => h.Kind == SessionHolderKind.Npc && h.Items.Any(item => item.Facts.Worn && Admitted(item)) &&
                (h.Dead || session.QueryCompanion(h.Entity).Standing == CompanionStanding.Free))
            .OrderBy(h => h.Entity, StringComparer.Ordinal).ToArray();
        if (wearers.Length == 0) { GD.Print($"ARC-DEV-202 native-smoke loot-worn NONE map={map.RuntimeMap} (no NPC wears an item)"); return; }
        var body = wearers.FirstOrDefault(h => h.Dead) ?? wearers[0];
        if (!body.Dead && !session.QueryNpcKilled(body.Entity))
        {
            var build = combat.Build(session, map.RuntimeMap, body.Entity);
            Require(build.Combat is not null, $"{body.Entity}: no combat values ({build.Failure})");
            var killed = session.ApplyDamage(session.Version, build.Combat!.Target, new LifeDamage { HitPoints = LifeRules.MaxHitPoints(build.Combat.Facts, 0) + 1000, PlainDeath = true });
            Require(killed.Disposition != GameSessionDisposition.Rejected && session.QueryNpcKilled(body.Entity), $"{body.Entity}: killed: {killed.Failure}");
        }
        var listed = session.QueryMapInventory(mapId)!.Holders.Single(h => h.Entity == body.Entity);
        Require(listed.Dead, "the body is a dead NPC to loot");
        var worn = listed.Items.Where(item => item.Facts.Worn && Admitted(item)).Select(item => item.Entity).ToArray();
        var heroWorn = session.QueryEquipment().Worn.Select(item => (item.Slot, item.Entity)).ToArray();
        var taken = new List<string>(); var left = new List<string>();
        // ARC-UI-072 (B30): the window on the body in the game's art offers the doll view: the doll button shows the worn items in their slots,
        // a press on one released on a free cell of the hero's grid takes it, and the bag button brings the grid back.
        // ARC_AREA_LOOT_DOLL_CAPTURE=PATH.png saves a readback of the doll view before the take.
        OpenInventory(body.Entity);
        string? dragged = null;
        if (InventoryOpen && _inventoryPanel is { ArtMode: true } panel && panel.GridAreaForSelfTest is { LootLook: true } grid)
        {
            Require(grid.TogglesShown, "the doll and bag buttons are shown on a body");
            Require(!grid.BodyDoll, "the grid view shows first");
            Vector2 Centre((int X, int Y) at, int art) => new(at.X + grid.Look!.Art[art].GetWidth() / 2f, at.Y + grid.Look.Art[art].GetHeight() / 2f);
            var doll = Centre(AreaLootArtLayout.DollButton, AreaLootArtLayout.DollButtonArt);
            Require(grid.ToggleAt(doll) == true, "the point on the doll button hits it");
            grid.PressAt(doll); grid.ReleaseAt(doll);
            Require(grid.BodyDoll && grid.Held is null, "the doll button shows the doll view");
            var shown = panel.ViewForSelfTest!.BodyDoll.Where(slot => slot.Entity is not null).ToArray();
            Require(shown.Length > 0 && worn.Intersect(shown.Select(slot => slot.Entity!)).Any(), "the doll holds the body's worn items");
            if (System.Environment.GetEnvironmentVariable("ARC_AREA_LOOT_DOLL_CAPTURE") is { Length: > 0 } capture)
            {
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                using var shot = GetViewport().GetTexture().GetImage();
                if (shot.SavePng(capture) != Error.Ok) throw new InvalidOperationException("AREA_LOOT_DOLL_CAPTURE_FAILED");
            }
            var wornSlot = shown.First(slot => worn.Contains(slot.Entity!));
            var rect = AreaLootArtLayout.BodySlotRects[wornSlot.Slot];
            var from = new Vector2(rect.X + rect.Width / 2f, rect.Y + rect.Height / 2f);
            var free = Enumerable.Range(0, SessionHeroGrid.Slots).Cast<int?>().FirstOrDefault(cell => AreaInventoryDrag.ItemAt(panel.ViewForSelfTest!.Grid, cell!.Value) is null);
            Require(free is not null, "a free cell in the hero's grid");
            grid.PressAt(from);
            Require(grid.Held is { Partner: true } held && held.Entity == wornSlot.Entity, "a press on the worn slot lifts its item");
            grid.ReleaseAt(AreaInventoryGridArea.CellCentre(free!.Value));
            for (var frame = 0; frame < 4 && !session.QueryPcInventory().Any(item => item.Entity == wornSlot.Entity); frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Require(session.QueryPcInventory().Any(item => item.Entity == wornSlot.Entity), "the drag from the doll took the worn item to the hero");
            Require(session.QueryMapInventory(mapId)!.Holders.Single(h => h.Entity == body.Entity).Items.All(item => item.Entity != wornSlot.Entity), "the body lost it");
            Require(panel.ViewForSelfTest!.BodyDoll.All(slot => slot.Entity != wornSlot.Entity), "the doll shows the slot empty");
            dragged = wornSlot.Entity;
            var bag = Centre(AreaLootArtLayout.BagButton, AreaLootArtLayout.BagButtonArt);
            grid.PressAt(bag); grid.ReleaseAt(bag);
            Require(!grid.BodyDoll, "the bag button shows the grid again");
        }
        else GD.Print($"ARC-DEV-202 loot-worn: no original-look window (open={InventoryOpen}); the doll view is not driven (named, the interface art does not read)");
        if (InventoryOpen) EndInventory();
        if (dragged is not null) taken.Add(dragged);
        listed = session.QueryMapInventory(mapId)!.Holders.Single(h => h.Entity == body.Entity);
        // The worn items first, so the hero's weight taken by the unworn ones cannot refuse them.
        foreach (var item in listed.Items.Where(Admitted).OrderByDescending(item => item.Facts.Worn).ToArray())
        {
            var result = _inventoryHost!.Loot(session, body.Entity, item.Entity);
            if (AreaInventoryHost.Moved(result)) taken.Add(item.Entity);
            else { left.Add($"{item.Entity}:{result.Failure}"); Require(!worn.Contains(item.Entity) || result.Failure is GameSessionFailure.ItemScriptUnsupported or GameSessionFailure.InsertRefused,
                $"the worn {item.Entity} of {body.Entity} was refused: {result.Failure}"); }
        }
        Require(worn.Any(taken.Contains), $"a worn item moved (worn {worn.Length}, left {string.Join(",", left)})");
        var pc = session.QueryPcInventory().Select(item => item.Entity).ToHashSet();
        Require(taken.All(pc.Contains), "every moved item is in the hero's list");
        var after = session.QueryMapInventory(mapId)!.Holders.Single(h => h.Entity == body.Entity).Items.Select(item => item.Entity).ToHashSet();
        Require(!taken.Any(after.Contains), "the body's list lost the moved items");
        Require(heroWorn.SequenceEqual(session.QueryEquipment().Worn.Select(item => (item.Slot, item.Entity))), "nothing the hero wears changed");
        GD.Print($"ARC-DEV-202 loot-worn body={body.Entity} doll={(dragged is null ? "not-driven" : "driven")} dragged={dragged ?? "-"} items={listed.Items.Count} worn={worn.Length} moved={taken.Count} wornMoved={worn.Count(taken.Contains)} left=[{string.Join(" ", left)}]");
        GD.Print("ARC-DEV-202 native-smoke loot-worn PASS");
    }
}
