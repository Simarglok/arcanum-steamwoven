using Arcanum.Data.Area;
using Arcanum.Session;
using Arcanum.Simulation.Combat;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

public partial class TerrainViewerApp
{
    // Native observation (ARC-WORLD-063) with --hero, the last stage of map mode: the hero breaks the shown map's weakest container that
    // holds two items or more through the session's object attack; its items drop one by one on its cell, so from the second on they
    // gather into a junk pile. The pile is drawn there from the pile prototype's art and picked as a pile; opened, the loot window lists
    // its items and holds their decay; one item is taken and the window closed, which dissolves a pile left with one item. The broken
    // container, the taken item and the pile stay (the stage runs last). NONE on a map without such a container.
    private Task RunAreaJunkPileSmokeAsync()
    {
        RunAreaMapPileSmoke();
        if (_areaSession is not { } session || session.QueryHero() is null || _readyArea is not { } area || ObjectTargets() is not { } targets
            || session.QueryMapInventory(new SessionMapId(CurrentAreaMap)) is not { } inventory)
        { GD.Print($"ARC-WORLD-063 native-smoke junk-pile NONE map={_readyMapName} (no hero or no inventory)"); return Task.CompletedTask; }
        static void Require(bool value, string why)
        {
            if (value) return;
            GD.Print("ARC-WORLD-063 junk-pile smoke FAIL: " + why);
            throw new InvalidOperationException("AREA_JUNK_PILE_SMOKE_FAILED");
        }
        var mapId = new SessionMapId(CurrentAreaMap);
        var held = inventory.Holders.Where(h => h.Kind == SessionHolderKind.Container && h.Items.Count >= 2).ToDictionary(h => h.Entity, h => h.Items.Count, StringComparer.Ordinal);
        var candidate = area.Snapshot.Objects.Where(row => row.Scope == AreaScope.Core && row.Type == ContainerType)
            .Select(row => targets.Target(session, row.Key, AreaTrapped))
            .Where(t => t is { Refusal: null, Kind: DamageableObjectKind.Container, Object: { } o } && !o.Baseline.JunkPile && !o.Baseline.Invulnerable
                && held.ContainsKey(t.Entity))
            .OrderBy(t => session.QueryObject(t!.Object!).HitPoints).ThenBy(t => t!.Entity, StringComparer.Ordinal).FirstOrDefault();
        if (candidate is null) { GD.Print($"ARC-WORLD-063 native-smoke junk-pile NONE map={_readyMapName} (no container with two items)"); return Task.CompletedTask; }
        var spilled = inventory.Holders.Single(h => h.Entity == candidate.Entity).Items.Select(item => item.Entity).ToArray();
        var attacks = 0;
        for (; attacks < 400 && session.QueryObject(candidate.Object!).Condition == SessionObjectCondition.Whole; attacks++)
        {
            // A critical self-hit may hurt the hero: it recovers between attacks.
            session.ClearStun(session.Version, SessionLifeTarget.Hero);
            session.Heal(session.Version, SessionLifeTarget.Hero, new LifeHeal { Full = true });
            var hit = session.AttackObject(session.Version, new() { Target = candidate.Object, HeroWorn = AreaCombatHost.HeroWorn(session, _areaKitDurability) });
            Require(hit.Disposition != GameSessionDisposition.Rejected, $"attack {attacks} accepted ({hit.Failure})");
        }
        Require(session.QueryObject(candidate.Object!).Condition == SessionObjectCondition.Destroyed,
            $"the container broke within {attacks} attacks (hit points {session.QueryObject(candidate.Object!).HitPoints} left; a hero whose hits stay under its damage threshold cannot break it)");
        var cell = new Arcanum.Simulation.Movement.CellCoord(checked((int)candidate.Row.WorldX!.Value), checked((int)candidate.Row.WorldY!.Value));
        var current = session.QueryMapInventory(mapId)!;
        var pile = current.JunkPiles.SingleOrDefault(p => p.Cell == cell);
        Require(pile is not null && pile.Items.Count >= 2, $"the spilled items gathered into one junk pile on {cell}");
        var loose = current.Ground.Where(g => g.Cell == cell).Select(g => g.Item.Entity).ToArray();
        Require(spilled.All(item => pile!.Items.Any(i => i.Entity == item) || loose.Contains(item)), "every spilled item lies in the pile or beside it");
        var decaying = session.QueryItemDecays().Where(d => spilled.Contains(d.Entity)).Select(d => d.Entity).ToHashSet(StringComparer.Ordinal);
        var kept = inventory.Holders.Single(h => h.Entity == candidate.Entity).Items.Where(item => item.Facts.NoDecay).Select(item => item.Entity).ToHashSet(StringComparer.Ordinal);
        Require(spilled.All(item => decaying.Contains(item) != kept.Contains(item)),
            $"every spilled item decays unless it has the no-decay flag ({decaying.Count} decaying, {kept.Count} flagged, {spilled.Length} spilled)");

        SyncAreaInventory(); SyncAreaGroundBodies();
        Require(_junkPileArt is not null, "the junk pile prototype's art reads");
        var key = PlacedKey(area.Snapshot.RuntimeMap, pile!.Entity);
        var drawn = _groundBodies.GetValueOrDefault(key);
        Require(drawn is not null && drawn.MapX == cell.X && drawn.MapY == cell.Y, "the pile is drawn at its cell");
        Require(AreaMovingCommands()?.Contains(drawn!) == true, "among the moved bodies the canvas draws");
        var map = _inventoryHost!.Maps.First(m => m.RuntimeMap == area.Snapshot.RuntimeMap);
        Require(pile.Items.All(item => !map.TryGetRow(item.Entity, out var row) || !_groundBodies.ContainsKey(row.Key)), "its items are not drawn on the ground");
        Require(AreaInventoryTargetOf(key) is { Kind: AreaInventoryTargetKind.JunkPile } target && target.Entity == pile.Entity, "picked as a junk pile");

        OpenInventory(pile.Entity);
        Require(InventoryOpen && _inventoryPanel!.ViewForSelfTest is { } view && view.Loot.Count == pile.Items.Count && view.HolderTitle == AreaInventoryHost.JunkPileTitle,
            "the loot window lists the pile's items");
        Require(AreaEvents.LootWindowShowsContainer, "the open pile holds the items' decay");
        var first = pile.Items[0].Entity;
        TakeFromBody(first);
        Require(session.QueryPcInventory().Any(item => item.Entity == first), $"{first} was taken: {_inventoryStatus}");
        var left = session.QueryJunkPiles().Single(p => p.Entity == pile.Entity).Items.Count;
        EndInventory();
        Require(!AreaEvents.LootWindowShowsContainer, "closed, the window holds nothing");
        var after = session.QueryMapInventory(mapId)!;
        var dissolved = after.JunkPiles.All(p => p.Entity != pile.Entity);
        Require(dissolved == left <= 1, $"a pile left with {left} item(s) {(left <= 1 ? "dissolves" : "stays")} on closing");
        SyncAreaGroundBodies();
        Require(dissolved != _groundBodies.ContainsKey(key), "the pile's body follows the pile");
        GD.Print("ARC-WORLD-063 native-smoke junk-pile PASS " + System.Text.Json.JsonSerializer.Serialize(new
        {
            Map = _readyMapName, Container = candidate.Entity, Attacks = attacks, Spilled = spilled.Length, NoDecay = kept.Count, Piled = pile.Items.Count, Loose = loose.Length,
            Art = $"0x{_junkPileArt:X8}", Taken = first, Left = left, Dissolved = dissolved,
            Parity = "NOT_VERIFIED (rules read in the original executable; no original-runtime run)",
        }));
        return Task.CompletedTask;
    }

    // Native observation (ARC-DEV-310, B04) before the stage above breaks anything: a junk pile of the map's data (a standing container of
    // prototype 3043, such as the Crash Site cave's) is opened as a container, taken from and closed through the window's own paths; it stays
    // while the window shows it and, closed with no item left (or one, which then lies loose on its cell), is gone like a pile the session
    // made. NONE on a map without such a pile.
    private void RunAreaMapPileSmoke()
    {
        if (_areaSession is not { } session || session.QueryHero() is null || _readyArea is not { } area || ObjectTargets() is not { } targets
            || _inventoryHost?.Map(CurrentAreaMap) is not { } map || session.QueryMapInventory(new SessionMapId(CurrentAreaMap)) is not { } inventory)
        { GD.Print($"ARC-DEV-310 native-smoke map-pile NONE map={_readyMapName} (no hero or no inventory)"); return; }
        static void Require(bool value, string why)
        {
            if (value) return;
            GD.Print("ARC-DEV-310 map-pile smoke FAIL: " + why);
            throw new InvalidOperationException("AREA_MAP_PILE_SMOKE_FAILED");
        }
        var mapId = new SessionMapId(CurrentAreaMap);
        var candidate = area.Snapshot.Objects.Where(row => row.Scope == AreaScope.Core && row.Type == ContainerType)
            .Select(row => targets.Target(session, row.Key, AreaTrapped))
            .Where(t => t is { Refusal: null, Kind: DamageableObjectKind.Container, Object: { } o } && o.Baseline.JunkPile
                && inventory.Holders.Any(h => h.Entity == t.Entity && h.Items.Count >= 1))
            .OrderByDescending(t => inventory.Holders.First(h => h.Entity == t!.Entity).Items.Count).ThenBy(t => t!.Entity, StringComparer.Ordinal).FirstOrDefault();
        if (candidate is null) { GD.Print($"ARC-DEV-310 native-smoke map-pile NONE map={_readyMapName} (no junk pile in the map's data with an item)"); return; }
        var before = inventory.Holders.First(h => h.Entity == candidate.Entity).Items.Select(item => item.Entity).ToArray();
        var target = _inventoryHost.Target(session, CurrentAreaMap, candidate.Row.Key);
        Require(target is { Kind: AreaInventoryTargetKind.Container }, "the pile is picked as a container");
        OpenContainer(target!);
        Require(InventoryOpen && ContainerLootOpen && _inventoryPanel!.Holder == candidate.Entity, "the loot window opens on the pile");
        TakeAllFromBody();
        var left = session.QueryMapInventory(mapId)!.Holders.First(h => h.Entity == candidate.Entity).Items.Count;
        Require(AreaContainerLoot.Lootable(session, map, candidate.Entity), "the pile stays while the window shows it");
        var keyGone = targets.Gone(session).Contains(candidate.Row.Key);
        Require(!keyGone, "and is still drawn");
        EndInventory();
        var dissolved = !AreaContainerLoot.Lootable(session, map, candidate.Entity);
        Require(dissolved == left <= 1, $"a pile left with {left} item(s) {(left <= 1 ? "goes" : "stays")} on closing");
        Require(targets.Gone(session).Contains(candidate.Row.Key) == dissolved, "the drawn pile follows the pile");
        var lying = left == 1 ? session.QueryMapInventory(mapId)!.Ground.Count(g => before.Contains(g.Item.Entity)) : 0;
        Require(left != 1 || lying + session.QueryJunkPiles().Count(p => p.Items.Any(i => before.Contains(i.Entity))) >= 1, "the last item lies on the pile's cell");
        GD.Print("ARC-DEV-310 native-smoke map-pile PASS " + System.Text.Json.JsonSerializer.Serialize(new
        {
            Map = _readyMapName, Pile = candidate.Entity, Items = before.Length, Left = left, Dissolved = dissolved, Lying = lying,
            Parity = "NOT_VERIFIED (rules read in the original executable; no original-runtime run)",
        }));
    }
}
