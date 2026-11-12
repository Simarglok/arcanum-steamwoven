using Arcanum.Data.Area;
using Arcanum.Session;
using Arcanum.Simulation.Items;
using Arcanum.Simulation.Locks;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

public partial class TerrainViewerApp
{
    // Native observation (ARC-WORLD-101) with --hero, map mode, before the junk pile stage (which breaks a container): once the barter data
    // has loaded, the loot open of a standing container with flag 0x200 that the player's attempt opens passes its admission, generates the
    // container once from the hero's seed (its source then 0, so the window's restock schedule sets nothing), opens the window on its new
    // goods, holds the items' decay while open, lets one item be taken and closes the container again; a second open generates nothing. A
    // locked container of the map refuses its open (no window). NONE without such containers.
    private async Task RunAreaContainerLootSmokeAsync()
    {
        if (_areaSession is not { } session || session.QueryHero() is null || _readyArea is not { } area || _inventoryHost?.Map(CurrentAreaMap) is not { } map)
        { GD.Print($"ARC-WORLD-101 native-smoke container NONE map={_readyMapName} (no hero or no inventory)"); return; }
        static void Require(bool value, string why)
        {
            if (value) return;
            GD.Print("ARC-WORLD-101 container smoke FAIL: " + why);
            throw new InvalidOperationException("AREA_CONTAINER_SMOKE_FAILED");
        }
        for (var i = 0; i < 1200 && RestockHostNow() is null; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Require(RestockHostNow() is not null, "the restock host is ready (barter data loaded)");
        if (InventoryOpen) EndInventory();
        var mapId = new SessionMapId(CurrentAreaMap);
        var containers = area.Snapshot.Objects.Where(row => row.Scope == AreaScope.Core && row.Type == ContainerType && map.TryGetEntity(row.Key, out _))
            .Select(row => _inventoryHost.Target(session, CurrentAreaMap, row.Key)).OfType<AreaInventoryTarget>()
            .Where(target => target.Kind == AreaInventoryTargetKind.Container).OrderBy(target => target.Entity, StringComparer.Ordinal).ToArray();
        AreaInventoryTarget? Find(Func<AreaInventoryTarget, SessionOpenableBaseline, bool> test) => containers.FirstOrDefault(target =>
            Arcanum.Data.Combat.CombatFieldReader.ReadBreakable(target.Row).Fields is { } fields && test(target, AreaObjectTargets.ContainerBaseline(target.Row, fields)));
        // The locked container is plain, so that its attempt decides.
        bool Plain(AreaInventoryTarget target) => !Arcanum.Data.Traversal.AreaStepInspector.HasUnlockScript(target.Row) &&
            !map.Seed.Holders.First(h => h.Entity == target.Entity).ScriptPoints.Contains(SessionScriptPoint.Use);
        bool Unlockable(AreaInventoryTarget target) => !Arcanum.Data.Traversal.AreaStepInspector.HasUnlockScript(target.Row);
        bool Crate(AreaInventoryTarget target, SessionOpenableBaseline baseline) => (map.RestockSeed(target.Entity).ContainerFlags & RestockRules.GeneratedOnceFlag) != 0 &&
            map.RestockSeed(target.Entity).InventorySource != 0 && !session.QueryContainerGenerated(mapId, target.Entity) &&
            session.AttemptOpen(new(mapId, target.Entity), baseline, false) == OpenAttempt.Opens;
        // A crate without a use script first; else one whose use script the viewer runs (a trap script is not modelled).
        var crate = Find((target, baseline) => Plain(target) && Crate(target, baseline)) ?? Find((target, baseline) => Unlockable(target) && Crate(target, baseline));
        var locked = Find((target, baseline) => Plain(target) && session.AttemptOpen(new(mapId, target.Entity), baseline, false) == OpenAttempt.Locked);
        if (crate is null)
        {
            var seen = string.Join(",", map.Seed.Holders.Where(h => h.Kind == SessionHolderKind.Container && (map.RestockSeed(h.Entity).ContainerFlags & RestockRules.GeneratedOnceFlag) != 0)
                .Select(h => $"{h.Entity[^6..]}:target={containers.Any(t => t.Entity == h.Entity)}:scripts={string.Join("+", h.ScriptPoints)}:generated={session.QueryContainerGenerated(mapId, h.Entity)}"));
            GD.Print($"ARC-WORLD-101 native-smoke container NONE map={_readyMapName} (no openable container with flag 0x200; {containers.Length} containers; 0x200: {seen})");
            return;
        }

        var before = session.QueryMapInventory(mapId)!.Holders.Single(h => h.Entity == crate.Entity).Items.Count;
        OpenContainer(crate);
        var open = _lastContainerOpen;
        if (open is { Outcome: AreaContainerOpenOutcome.UseScriptStopped or AreaContainerOpenOutcome.UseScriptUnsupported or AreaContainerOpenOutcome.TrapScriptUnsupported
            or AreaContainerOpenOutcome.TrapSpotted or AreaContainerOpenOutcome.TrapFired or AreaContainerOpenOutcome.TrapRefused })
        {
            GD.Print($"ARC-WORLD-101 native-smoke container NONE map={_readyMapName} (the crate's use script: {open.Outcome} {open.Detail})");
            return;
        }
        Require(open is { Outcome: AreaContainerOpenOutcome.Opened }, $"the crate opens ({open?.Outcome} {open?.Detail})");
        var done = open!.Generation?.Regeneration;
        Require(open.Generation?.Disposition == GameSessionDisposition.Changed && done?.Outcome is SessionRestockOutcome.Regenerated or SessionRestockOutcome.Cleared,
            $"the first open generated it ({open.Generation?.Disposition}/{done?.Outcome}; the hero's name has {session.QueryHero()!.PlayerName.Length} bytes)");
        Require(session.QueryContainerGenerated(mapId, crate.Entity), "the session records it as generated");
        Require(!session.QueryRestocks().Any(r => r.Holder == crate.Entity), "the window's schedule set no restock (its source is 0 now)");
        Require(InventoryOpen && _inventoryPanel!.Holder == crate.Entity, "the loot window shows the crate");
        var listed = _inventoryPanel!.ViewForSelfTest!.Loot.Select(line => line.Entity).ToArray();
        Require(listed.Length == session.QueryMapInventory(mapId)!.Holders.Single(h => h.Entity == crate.Entity).Items.Count, "the window lists its current goods");
        Require(AreaEvents.LootWindowShowsContainer, "the open container holds the items' decay");
        // ARC-RES-1410 (ledger XR16 (g)): the open lays the container's grid out again (0x5727B0 -> 0x4670A0), so the window draws the arrangement of the session's list.
        var arrangedAtOpen = 0;
        void RequireArranged(string when)
        {
            var items = session.QueryMapInventory(mapId)!.Holders.Single(h => h.Entity == crate.Entity).Items;
            var list = items.Select(item => { var (width, height) = session.QueryItemCells(item.Entity); return new Arcanum.Simulation.Items.InventoryArrangeItem(item.Entity, item.Facts.InventoryLocation, width, height); }).ToArray();
            // A list the original cannot lay out (an item beyond the grid) writes nothing, so there is nothing to compare.
            if (Arcanum.Simulation.Items.InventoryArrangeRules.Arrange(list, Arcanum.Simulation.Items.InventoryGridRules.ContainerRows, Arcanum.Simulation.Items.InventoryArrangeLayout.Horizontal) is not { } placed) return;
            var shown = _inventoryPanel!.ViewForSelfTest!.LootGrid.ToDictionary(item => item.Entity, item => item.Location);
            Require(placed.All(p => shown.TryGetValue(p.Key, out var slot) && slot == p.Slot), $"{when}: the window draws the arranged grid ({string.Join(" ", placed.Select(p => $"{p.Key}@{p.Slot}"))} vs {string.Join(" ", shown.Select(p => $"{p.Key}@{p.Value}"))})");
            arrangedAtOpen += placed.Count;
        }
        RequireArranged("first open");
        var current = session.QueryMapInventory(mapId)!.Holders.Single(h => h.Entity == crate.Entity).Items;
        var takeable = listed.FirstOrDefault(entity => current.Any(item => item.Entity == entity && item.Facts.Kind.Type is not SessionItemType.Gold
            and not SessionItemType.Ammo and not SessionItemType.Key and not SessionItemType.KeyRing && !item.Facts.Worn && item.Facts.ScriptPoints.Count == 0));
        if (takeable is not null)
        {
            TakeFromBody(takeable);
            Require(session.QueryPcInventory().Any(item => item.Entity == takeable) || session.QueryMadeItems().Any(item => item.Entity == takeable),
                $"{takeable} was taken: {_inventoryStatus}");
        }
        // The coin pile goes through the quantity window (ARC-UI-029), all of it.
        var pile = done!.Pile;
        var gold = session.QueryGold();
        if (pile is not null && done.Coins > 1)
        {
            TakeFromBody(pile);
            Require(_quantityMove is { } asked && asked.Item == pile, $"the coin pile asks how many: {_inventoryStatus}");
            Require(TargetModeOpen(InterfaceMode.QuantityPicker) && TargetsMatchStack && WindowPanelsMatchStack && AnyWindowOpen,
                $"the question is mode 13 over the loot window: {StackText()}");
            AcceptQuantity(done.Coins);
            Require(session.QueryGold() == gold + done.Coins, $"the {done.Coins} coins were taken: {_inventoryStatus}");
        }
        EndInventory();
        Require(!InventoryOpen && !AreaEvents.LootWindowShowsContainer, "closed, the window holds nothing");
        OpenContainer(crate);
        Require(_lastContainerOpen is { Outcome: AreaContainerOpenOutcome.Opened, Generation: null }, "a second open generates nothing");
        RequireArranged("second open after the takes");
        EndInventory();

        string lockedResult = "NONE";
        if (locked is not null)
        {
            OpenContainer(locked);
            Require(_lastContainerOpen is { Outcome: AreaContainerOpenOutcome.Locked, LockedSound: true } && !InventoryOpen, "a locked container refuses its open");
            lockedResult = "refused";
        }
        GD.Print("ARC-WORLD-101 native-smoke container PASS " + System.Text.Json.JsonSerializer.Serialize(new
        {
            Map = _readyMapName, Containers = containers.Length, Crate = crate.Entity, Before = before, done!.Outcome, done.Removed, done.Coins, Created = done.Created.Count,
            done.Refused, done.Skipped, Listed = listed.Length, Arranged = arrangedAtOpen, Taken = takeable, Gold = session.QueryGold() - gold, Opening = open.Opening, Locked = lockedResult,
            UseScript = open.UseScript is { } ran ? $"{ran.Outcome}/{ran.Run?.Outcome}" : "none",
            Parity = "NOT_VERIFIED (rules read in the original executable; no original-runtime run)",
        }));
    }
}
