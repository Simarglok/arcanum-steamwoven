using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Items;
using Arcanum.Data.Magic;
using Arcanum.Session;
using Arcanum.Simulation.Goals;
using Arcanum.Simulation.Magic;
using Arcanum.Simulation.Movement;
using GamepadLab.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-MAGIC-1128: a spell whose target area has the inventory bit (spell 9, Divine Magick) opens the hero's inventory and stands its cursor over it
// (interface modes 8 and 1 on the stack); a left press on an item of the window is the cast at it (0x573850, 0x575FA0, ledger IW44), the cursor stays
// (SU9), and a cancel closes the cursor and the window (SA38). Run by the cast stage; prints its own marker.
public partial class TerrainViewerApp
{
    private async Task InventoryItemCastSmokeAsync(GameSessionOwner session, IReadOnlyList<SessionSpellRecord> records)
    {
        try { await InventoryItemCastStepsAsync(session, records); }
        catch (InvalidOperationException e) { GD.Print("ARC-MAGIC-1128 native-smoke inventory-target FAIL " + e.Message); throw; }
    }

    private async Task InventoryItemCastStepsAsync(GameSessionOwner session, IReadOnlyList<SessionSpellRecord> records)
    {
        static void Require(bool value, string why) { if (!value) throw new InvalidOperationException("ARC-MAGIC-1128 inventory-target smoke: " + why); }
        // The activation does not ask whether the hero knows the spell (the pane lists the known ones): spell 9 is rank 5 of its college, which the smoke would have to buy.
        var identify = records.FirstOrDefault(r => r.Data.Number == 9 && r.Areas.Length > 0 && SpellUiRules.TargetsInventory(r.Areas[0].Targets));
        if (identify is null) { GD.Print("ARC-MAGIC-1128 native-smoke inventory-target SKIP (spell 9 is not in the spell data)"); return; }
        if (AnyWindowOpen) { CloseOtherInterfaceWindows(); await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
        Require(!AnyWindowOpen && _spellTargeting is null, "the base before: " + StackText());
        session.Heal(session.Version, SessionLifeTarget.Hero, new LifeHeal { Fatigue = 1000 });
        // The smoke hero is no master of spell 9's college (its checks stop the hero's own cast, message 602): the cast is a magical item's, as a wand of Divine Magick is. Another worn item lends its slot 0.
        var worn = session.QueryEquipment().Worn;
        var weaponSlot = worn.FirstOrDefault(w => w.Slot == Arcanum.Simulation.Equipment.EquipmentSlot.Weapon);
        var wand = worn.FirstOrDefault(w => w.Entity != weaponSlot?.Entity);
        if (weaponSlot is null || wand is null) { GD.Print("ARC-MAGIC-1128 native-smoke inventory-target SKIP (the hero wears no second item to be the wand)"); return; }
        _lentItemSpellsForSelfTest = new(wand.Entity, SessionSpellCaster.Hero, 14, [9, 10000, 10000, 10000, 10000], -1, 1, ItemSpellPaneRules.IdentifiedFlag); _weaponPaneCache = null;
        try { await InventoryItemCastBodyAsync(session, wand.Entity); }
        finally { _lentItemSpellsForSelfTest = null; _weaponPaneCache = null; }
    }

    // The two openers of the same cursor: the spell pane's activation (mode 1) and the wand's use from the window (mode 16, IA4).
    private async Task InventoryItemCastBodyAsync(GameSessionOwner session, string wand)
    {
        await InventoryItemCastPathAsync(session, wand, viaItem: false);
        await InventoryItemCastPathAsync(session, wand, viaItem: true);
    }

    private async Task InventoryItemCastPathAsync(GameSessionOwner session, string wand, bool viaItem)
    {
        static void Require(bool value, string why) { if (!value) throw new InvalidOperationException("ARC-MAGIC-1128 inventory-target smoke: " + why); }
        var way = viaItem ? "item" : "spell";
        var cursorMode = viaItem ? InterfaceMode.ItemTarget : InterfaceMode.SpellTarget;
        var stack = "Game>Inventory>" + cursorMode;
        bool CursorOpen() => viaItem ? _itemAiming is not null : _spellTargeting is not null;
        session.Heal(session.Version, SessionLifeTarget.Hero, new LifeHeal { Fatigue = 1000 });
        if (viaItem)
        {
            OpenInventory();
            Require(WindowOpen(InterfaceMode.Inventory), "the inventory opened for the wand: " + StackText());
            var facts = _inventoryHost!.UseFacts(session, wand, _areaKitUse);
            Require(facts is not null, "the wand's use facts read");
            OpenItemAiming(wand, facts!);
        }
        else ActivateSpell(9, wand);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Require(CursorOpen() && WindowOpen(InterfaceMode.Inventory) && TargetModeOpen(cursorMode) && StackText() == stack,
            $"[{way}] the opener left the inventory with the cursor over it: {StackText()} {_spellNotice} {_itemAimLast}");
        Require(!viaItem || _itemAiming!.Mask == SpellUiRules.ObjectBit + SpellUiRules.InventoryBit, "the item cursor has the spell's mask");
        Require(TargetsMatchStack && WindowPanelsMatchStack && _inventoryPanel is { TargetPicking: true }, $"[{way}] the stack, the panels and the picking flag agree: " + StackText());
        // The hero's own list: an item not yet identified, in the window's grid.
        var view = _inventoryPanel!.ViewForSelfTest!;
        // The smoke hero's pack may hold only gold: the wielded weapon is taken off through the window (the cursor stays over it) and put on again at the end.
        string? takenOff = null;
        if (view.Grid.All(g => SpellCasterNow()?.Item(g.Entity) is null) && session.QueryEquipment().Worn.FirstOrDefault(w => w.Slot == Arcanum.Simulation.Equipment.EquipmentSlot.Weapon) is { } wielded)
        {
            TakeOffItem(Arcanum.Simulation.Equipment.EquipmentSlot.Weapon);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            takenOff = wielded.Entity;
            view = _inventoryPanel.ViewForSelfTest!;
            Require(CursorOpen() && WindowOpen(InterfaceMode.Inventory), $"[{way}] a take-off through the window leaves the cursor over it: " + _inventoryStatus);
        }
        var item = view.Grid.FirstOrDefault(g => !session.QueryItemIdentified(g.Entity) && SpellCasterNow()?.Item(g.Entity) is not null)
            ?? view.Grid.FirstOrDefault(g => viaItem && SpellCasterNow()?.Item(g.Entity) is not null);
        var wasIdentified = item is not null && session.QueryItemIdentified(item.Entity);
        Require(item is not null, $"[{way}] a listed item the cast host knows: " + string.Join(",", view.Grid.Select(g => g.Entity)));
        // A press on an empty cell picks nothing and the cursor stays.
        var empty = Enumerable.Range(0, 60).First(cell => view.Grid.All(g => g.Slot != cell && !(cell >= g.Slot && cell < g.Slot + g.Width)));
        var casts = _spellCasts;
        _inventoryPanel.GridAreaForSelfTest.PressAt(AreaInventoryGridArea.CellCentre(empty));
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Require(CursorOpen() && _spellCasts == casts && _inventoryPanel.GridAreaForSelfTest.Held is null, $"[{way}] a press on an empty cell cast nothing and held nothing");
        // A press on the item casts at it through the window's own pointer path.
        var logStart = SpellCasterNow()!.Log.Count;
        _inventoryPanel.GridAreaForSelfTest.PressAt(AreaInventoryGridArea.CellCentre(item!.Slot));
        for (var i = 0; i < 30 && _spellCasts == casts && !(viaItem && !CursorOpen()); i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Require(_inventoryPanel.GridAreaForSelfTest.Held is null, $"[{way}] the press lifted nothing");
        // The casting goal plays on the world frames' clock, as in the other cast steps.
        for (var frames = 0; frames < 800 && (_spellGoal is not null || _spellMissiles.Count > 0); frames++)
        {
            _areaSmokeTime++;
            SpellSmokeTick();
            WorldFrameForSelfTest(new(-1, default), new[] { StickLevel(default) }, .05, true, SimulationTime.At(_areaSmokeTime, 20));
        }
        Require(_spellGoal is null, $"[{way}] the cast ended");
        var log = SpellCasterNow()!.Log.Skip(logStart).Select(e => e.Kind).ToArray();
        if (viaItem)
        {
            // The smoke hero has no usable magical item (its kit is a weapon and clothes: the use of Robes does nothing), so the wand is lent for the cursor only: the pick reached the use of the wand on the picked item (the use's own cast is
            // ARC-DEV-365's, for an item type that casts).
            Require(_lastItemUse is { } used && used.Item == wand, $"[item] the press reached the wand's use on the picked item: {_lastItemUse} {_itemAimLast}");
        }
        else
        {
            Require(!wasIdentified, "[spell] the first path starts with an unidentified item");
            Require(session.QueryItemIdentified(item.Entity), $"[spell] the item {item.Entity} is identified: {_spellNotice} log={string.Join(",", log)}");
            Require(log.Contains(SpellCastEventKind.ItemIdentified), "[spell] the cast host logged the identification: " + string.Join(",", log));
        }
        if (viaItem)
        {
            // IA5: the item use ends its cursor (mode 16 pops once) and the window stays.
            Require(_itemAiming is null && WindowOpen(InterfaceMode.Inventory) && StackText() == "Game>Inventory" && TargetsMatchStack && !_inventoryPanel.TargetPicking, "the use ended the item cursor and kept the window: " + StackText());
        }
        else
        {
            // SU9: the cursor stays open after a cast; the inventory under it too.
            Require(_spellTargeting is not null && WindowOpen(InterfaceMode.Inventory) && StackText() == stack, "the cursor and the window stay after the cast: " + StackText());
        }
        if (takenOff is not null)
        {
            WearItem(takenOff, Arcanum.Simulation.Equipment.EquipmentSlot.Weapon);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Require(session.QueryEquipment().Worn.Any(w => w.Entity == takenOff), $"[{way}] the weapon is worn again: " + _inventoryStatus);
        }
        if (!viaItem)
        {
            // A right release in the window cancels the cursor, which takes the window with it (SA38).
            _inventoryPanel.GridAreaForSelfTest.RightReleaseAt(AreaInventoryGridArea.CellCentre(empty));
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Require(_spellTargeting is null && !InventoryOpen && StackText() == "Game" && TargetsMatchStack && WindowPanelsMatchStack && !_inventoryPanel.TargetPicking,
                "a right release cancelled the cursor and closed the window: " + StackText());
        }
        else
        {
            EndInventory();
            Require(!InventoryOpen && StackText() == "Game" && TargetsMatchStack && WindowPanelsMatchStack, "the window closed: " + StackText());
        }
        GD.Print($"ARC-MAGIC-1128 native-smoke inventory-target {way} PASS item={item.Entity} log={string.Join(",", log)}");
    }
}
