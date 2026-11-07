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
    // hero wears changes, and the body's list loses them. Gold, keys and ammunition stacks are other rules and stay; an item that runs
    // a script the host refuses is named, not required. NONE without a hero, combat values or such an NPC.
    private Task RunAreaLootWornSmokeAsync()
    {
        if (_areaSession is not { } session || session.QueryHero() is null || _combatHost is not { } combat || _inventoryHost?.Map(CurrentAreaMap) is null)
        { GD.Print($"ARC-DEV-202 native-smoke loot-worn NONE map={_readyMapName} (no hero, combat values or inventory)"); return Task.CompletedTask; }
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
        var wearers = session.QueryMapInventory(mapId)!.Holders.Where(h => h.Kind == SessionHolderKind.Npc && h.Items.Any(item => item.Facts.Worn && Admitted(item)))
            .OrderBy(h => h.Entity, StringComparer.Ordinal).ToArray();
        if (wearers.Length == 0) { GD.Print($"ARC-DEV-202 native-smoke loot-worn NONE map={map.RuntimeMap} (no NPC wears an item)"); return Task.CompletedTask; }
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
        foreach (var item in listed.Items.Where(Admitted).ToArray())
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
        GD.Print($"ARC-DEV-202 loot-worn body={body.Entity} items={listed.Items.Count} worn={worn.Length} moved={taken.Count} wornMoved={worn.Count(taken.Contains)} left=[{string.Join(" ", left)}]");
        GD.Print("ARC-DEV-202 native-smoke loot-worn PASS");
        return Task.CompletedTask;
    }
}
