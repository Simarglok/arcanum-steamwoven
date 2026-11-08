using Arcanum.Presentation.Hud;
using Arcanum.Session;
using Arcanum.Simulation.Hotkeys;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

// Native observation (ARC-DEV-306, the item steps of SM23 in the stage inv-ui; B31, B32, B41): the inventory window through real pointer
// events pushed into the viewport. An item pressed in the grid and released over a hotkey slot goes on that entry (the mouse drop, B31:
// the window's own area keeps the mouse, so the bar saw no press); a right release on a usable item uses it (B32: the use gate reads
// the item's facts, an item taken on another map included); a written item's use opens the reading window (0x462CC0 → 0x4606F0); the
// bar's Spells button (its handler, not a pointer press) opens the spell pane with the colleges the hero has a rank in (B41). The items are
// taken on the current map, so the resolver's read of another map is covered by AreaInventoryHostOffMapFactsTests, not here.
public partial class TerrainViewerApp
{
    private async Task RunInventoryItemStepsAsync(List<string> steps)
    {
        static void Require(bool value, string why)
        {
            if (value) return;
            GD.Print("ARC-DEV-306 inv-ui item steps FAIL: " + why);
            throw new InvalidOperationException("AREA_INV_UI_ITEM_STEPS_FAILED");
        }
        async Task Frames(int count = 2) { for (var i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
        async Task Button(Vector2 at, MouseButton button, bool pressed)
        {
            GetViewport().PushInput(new InputEventMouseMotion { Position = at, GlobalPosition = at });
            GetViewport().PushInput(new InputEventMouseButton
            {
                ButtonIndex = button, Pressed = pressed, Position = at, GlobalPosition = at,
                ButtonMask = pressed ? (button == MouseButton.Left ? MouseButtonMask.Left : MouseButtonMask.Right) : 0,
            });
            await Frames(1);
        }
        var session = _areaSession!;
        var simulatedBars = _gameBarForSelfTest; _gameBarForSelfTest = true;
        try
        {
            if (AnyWindowOpen) { CloseOtherInterfaceWindows(); await Frames(); }
            await Frames(3);
            Require(AreaGameBarPanel.BottomInset > 0 && _hotkeyBarView is { } && _gameBarPanel is { Visible: true }, "the bars show for the item steps");
            var view = _hotkeyBarView!;
            foreach (var slot in Enumerable.Range(0, HotkeyBarRules.Entries)) session.ClearHotkey(session.Version, slot);

            // The pre-made hero holds worn gear only: it takes two food items (and a written one when the ground has any) from the map.
            var ground = session.QueryMapInventory(new(CurrentAreaMap))?.Ground ?? [];
            var wanted = ground.Where(g => g.Item.Facts.Kind.Type == SessionItemType.Food).Take(2).Concat(ground.Where(g => g.Item.Facts.Kind.Type == SessionItemType.Written).Take(1)).ToArray();
            foreach (var g in wanted)
                Require(session.PickUpItem(session.Version, g.Item.Entity, _ => SessionInsertVerdict.Accepted).Disposition == GameSessionDisposition.Changed, $"the hero took {g.Item.Entity}");
            Require(wanted.Length > 0, $"the ground lists food for the hero to take (ground {ground.Count})");
            // A written item the hero can take from a container or a dead critter's list when the ground has none.
            if (wanted.All(g => g.Item.Facts.Kind.Type != SessionItemType.Written) && session.QueryMapInventory(new(CurrentAreaMap)) is { } shown)
                foreach (var holder in shown.Holders.Where(h => h.Kind == SessionHolderKind.Container || h.Dead))
                    if (holder.Items.FirstOrDefault(i => i.Facts.Kind.Type == SessionItemType.Written) is { Entity: not null } written
                        && session.LootItem(session.Version, holder.Entity, written.Entity, _ => SessionInsertVerdict.Accepted).Disposition == GameSessionDisposition.Changed) break;
            GD.Print($"ARC-DEV-306 inv-ui items: ground {ground.Count}, written held {session.QueryPcInventory().Count(i => i.Kind.Type == SessionItemType.Written)}, "
                + $"holders with a written item {session.QueryMapInventory(new(CurrentAreaMap))?.Holders.Count(h => h.Items.Any(i => i.Facts.Kind.Type == SessionItemType.Written))}");

            InventoryKey(Key.I);
            await Frames(3);
            Require(InventoryOpen && InventoryActive && _inventoryPanel is not null, "I opened the inventory");
            var area = _inventoryPanel!.GridAreaForSelfTest;
            Vector2 CellAt(SessionHeroGridItem item) => area.ScreenPointForSelfTest(AreaInventoryGridArea.CellCentre(item.Slot));
            SessionHeroGridItem[] Grid() => session.QueryHeroGrid().Items.Where(item => item.Entity != SessionHeroGrid.GoldEntity).ToArray();
            SessionItemKind? KindOf(string entity) => HeroItemKind(session, entity);
            var worn = session.QueryEquipment().Worn.Select(entry => entry.Entity).ToHashSet();

            // B32: every item the hero took from a map reads its use facts (an item taken on another map reads through the resolver).
            var unread = session.QueryPcInventory().Where(item => _inventoryHost!.UseFacts(session, item.Entity, _areaKitUse) is null).Select(item => item.Entity).ToArray();
            Require(unread.Length == 0, "every taken item reads its use facts: " + string.Join(", ", unread));

            // B31: a mouse drop. The item is pressed in the grid and released over slot 7 of the bar.
            var droppables = Grid().Where(item => KindOf(item.Entity) is { Type: not (SessionItemType.Ammo or SessionItemType.Gold) } && !worn.Contains(item.Entity)).Take(2).ToArray();
            Require(droppables.Length == 2, $"the hero holds two items for the bar (kit {session.QueryHeroKit().Count}, taken {session.QueryPcInventory().Count}, made {session.QueryMadeItems().Count}, grid {session.QueryHeroGrid().Items.Count}, worn {worn.Count})");
            var (droppable, spare) = (droppables[0], droppables[1]);
            const int slotNumber = 7;
            await Button(CellAt(droppable), MouseButton.Left, true);
            Require(_inventoryPanel.Held is { } held && held.Entity == droppable.Entity, "a press on the item took it onto the cursor");
            var target = view.SlotCentreForSelfTest(slotNumber);
            GetViewport().PushInput(new InputEventMouseMotion { Position = target, GlobalPosition = target });
            await Frames(1);
            await Button(target, MouseButton.Left, false);
            await Frames(2);
            var entry = session.QueryHotkeys().Entries[slotNumber];
            Require(entry.Kind == HotkeyKind.Item && entry.Item == droppable.Entity && _inventoryPanel.Held is null,
                $"the release over slot {slotNumber} put {droppable.Entity} there (entry {entry.Kind} {entry.Item}, held {_inventoryPanel.Held?.Entity ?? "none"}): {_inventoryStatus}");
            // A release over the strip but off the slots puts the item back (the window's own release): the next item stays on the grid.
            var gap = (view.SlotCentreForSelfTest(4) + view.SlotCentreForSelfTest(5)) / 2;
            await Button(CellAt(spare), MouseButton.Left, true);
            Require(_inventoryPanel.Held is not null, "the next item is pressed");
            GetViewport().PushInput(new InputEventMouseMotion { Position = gap, GlobalPosition = gap });
            await Button(gap, MouseButton.Left, false);
            await Frames(2);
            Require(_inventoryPanel.Held is null && session.QueryHotkeys().Entries[4].IsEmpty && session.QueryHotkeys().Entries[5].IsEmpty
                && session.QueryHeroGrid().Items.Any(item => item.Entity == spare.Entity), "a release between the slots changed no entry and put the item back");
            session.ClearHotkey(session.Version, slotNumber);
            // The window draws what it was last given: the entry's item is back in the grid, so it is drawn again.
            _inventoryPanel.Render(InventoryView(_inventoryPanel.Holder), null);
            await Frames(3);
            steps.Add("mouse drop of an item on a hotkey slot");

            // B32: a right release on a usable item uses it.
            var usable = Grid().Select(item => (Item: item, Facts: _inventoryHost!.UseFacts(session, item.Entity, _areaKitUse)))
                .FirstOrDefault(pair => !worn.Contains(pair.Item.Entity) && pair.Facts is { CanUseBox: true, Wearable: false, UseIsThrow: false, Magical: false, Type: AreaItemUseFacts.FoodType });
            Require(usable.Facts is not null, "the hero holds a plain food item to use");
            {
                _lastItemUse = null;
                await Button(CellAt(usable.Item), MouseButton.Right, true);
                await Button(CellAt(usable.Item), MouseButton.Right, false);
                await Frames(3);
                Require(LastItemUseForSelfTest is { Gate: AreaItemUseGate.Use, Step: not null } used && used.Item == usable.Item.Entity,
                    $"a right release on {usable.Item.Entity} used it (held {_inventoryPanel.Held?.Entity ?? "none"}, in view {_inventoryPanel.ViewForSelfTest?.Pc.Any(line => line.Entity == usable.Item.Entity)}): {LastItemUseForSelfTest?.Gate} {LastItemUseForSelfTest?.Step} {LastItemUseForSelfTest?.Message}");
                steps.Add("right release uses a food item");
            }

            // B32: a written item the hero holds reads through its use.
            var note = Grid().FirstOrDefault(item => KindOf(item.Entity) is { Type: SessionItemType.Written });
            Require(note is not null, "the hero holds a written item to read");
            {
                await Button(CellAt(note!), MouseButton.Right, true);
                await Button(CellAt(note!), MouseButton.Right, false);
                for (var frame = 0; frame < 20 && !ReadingOpen; frame++) await Frames(1);
                Require(ReadingOpen && !InventoryOpen, $"a right release on the note {note!.Entity} opened the reading window: {LastItemUseForSelfTest?.Gate} {LastItemUseForSelfTest?.Message} {_readingStatus}");
                CloseReadingByPlayer();
                await Frames(2);
                steps.Add("right release reads a note");
            }
            if (InventoryOpen) { InventoryKey(Key.I); await Frames(2); }
            Require(!InventoryOpen, "the inventory closed after the item steps");

            // B41: the bar's Spells button opens the spell pane with the colleges the hero has a rank in, and shuts it again.
            _gameBar.SetOn(AreaBarButton.Spells, true);
            Require(BarSpells() && _spellPane?.Shown == true, "the Spells button opened the spell pane: " + _gameBarNotice);
            await Frames(3);
            var ranks = session.QueryHeroSpellBook()?.Ranks.ToArray() ?? new int[SpellPaneRules.Colleges];
            Require(_spellBarView is { Visible: true } bar && bar.ShownCollegesForSelfTest.SequenceEqual(SpellPaneRules.ShownColleges(ranks)),
                $"the pane lists the colleges with a rank ({string.Join(",", SpellPaneRules.ShownColleges(ranks))}): {string.Join(",", _spellBarView?.ShownCollegesForSelfTest ?? [])}");
            _gameBar.SetOn(AreaBarButton.Spells, false);
            BarSpells();
            await Frames(2);
            Require(_spellPane?.Shown != true, "the Spells button shut the pane");
            steps.Add("Spells button opens and shuts the pane with the ranked colleges");
        }
        finally
        {
            foreach (var slot in Enumerable.Range(0, HotkeyBarRules.Entries)) session.ClearHotkey(session.Version, slot);
            if (AnyWindowOpen) CloseOtherInterfaceWindows();
            if (_spellPane?.Shown == true) { _gameBar.SetOn(AreaBarButton.Spells, false); BarSpells(); }
            _gameBarForSelfTest = simulatedBars;
        }
    }
}
