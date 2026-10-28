using Arcanum.Presentation.Hud;
using Arcanum.Session;
using Arcanum.Simulation.Hotkeys;
using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

public partial class TerrainViewerApp
{
    // ARC-UI-027 native observation, inside the cast stage (a hero who knows <spell>; an automated run, which draws the bars only because
    // this smoke asks): pointer events go through the window's GUI. The bar shows the game's slot art; a right press on the spell's button
    // in the pane and a right release over slot 2 put the spell there with its icon; a left click on slot 2 opens the spell's cursor; a right
    // drag from slot 2 to slot 6 moves it and one from slot 6 off the bar empties it; an item entry shows its name and its count; the latest
    // recent action's button shows its icon and a click on it opens its cursor with the bar's button sounds.
    private async Task RunAreaHotkeyBarSmokeAsync(GameSessionOwner session, int spell)
    {
        static void Require(bool value, string why) { if (!value) { GD.Print("ARC-UI-027 hotkey-bar smoke FAIL " + why); throw new InvalidOperationException("ARC-UI-027 hotkey-bar smoke: " + why); } }
        var barForSelfTest = _gameBarForSelfTest;
        _gameBarForSelfTest = true; _gameBarSoundsForSelfTest.Clear();
        try
        {
            async Task Frames(int count)
            {
                for (var i = 0; i < count; i++)
                {
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                    await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                }
            }
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
            async Task Drag(Vector2 from, Vector2 to) { await Button(from, MouseButton.Right, true); await Button(to, MouseButton.Right, false); }
            async Task Click(Vector2 at) { await Button(at, MouseButton.Left, true); await Button(at, MouseButton.Left, false); }
            Texture2D? SpellArt(int number) => HotkeyBarRules.SpellIcon(number) is { } art ? Art(art)?[0] : null;

            CancelSpellTargeting(null);
            foreach (var slot in Enumerable.Range(0, HotkeyBarRules.Entries)) session.ClearHotkey(session.Version, slot);
            await Frames(3);
            Require(_gameBarPanel is { Visible: true } && _hotkeyBarView is { } && _hotkeyBarContent is { } content && content.ReadyCount == HotkeyBarRules.ArtNumbers.Count,
                "the bar shows with its art: " + _hotkeyBarContent?.Detail);
            var view = _hotkeyBarView!;
            Require(Enumerable.Range(0, HotkeyBarRules.Entries).All(i => view.SlotArtForSelfTest(i) == Art(HotkeyBarRules.SlotArt)![0]), "every slot draws HKSlt1");

            // A right drag from the spell pane onto slot 2.
            ShowSpellPane();
            await Frames(3);
            var paneButton = _spellPane!.SpellButtonCentreForSelfTest(spell);
            Require(paneButton is not null, $"the pane shows spell {spell}");
            await Button(paneButton!.Value, MouseButton.Right, true);
            Require(_hotkeyDrag is { Kind: HotkeyDragSourceKind.Spell } && view.GhostShownForSelfTest, "a right press on the pane's spell starts a drag with its icon");
            await Button(view.SlotCentreForSelfTest(2), MouseButton.Right, false);
            Require(_hotkeyDrag is null && session.QueryHotkeys().Entries[2] == HotkeyEntry.ForSpell(spell), "the release over slot 2 put the spell there: " + _hotkeyNotice);
            await Frames(1);
            Require(view.IconForSelfTest(2) is { } icon && icon == SpellArt(spell), "slot 2 shows the spell's icon");
            _spellPane.HidePane();

            // While slot 2 is dragged its key skips it (HK8, HK15); a right release on the slot itself ends the drag with no change.
            var keyed = _hotkeyActivations;
            await Button(view.SlotCentreForSelfTest(2), MouseButton.Right, true);
            HotkeyKey(Key.Key3);
            Require(_hotkeyDrag is not null && _hotkeyActivations == keyed && _spellTargeting is null, "the key 3 skipped the dragged entry: " + _hotkeyNotice);
            await Button(view.SlotCentreForSelfTest(2), MouseButton.Right, false);
            Require(_hotkeyDrag is null && session.QueryHotkeys().Entries[2] == HotkeyEntry.ForSpell(spell), "the drop on its own slot changed nothing");

            // A left click on slot 2 activates it.
            var activations = _hotkeyActivations;
            await Click(view.SlotCentreForSelfTest(2));
            Require(_spellTargeting?.Spell == spell && _hotkeyActivations == activations + 1, "a click on slot 2 opened the spell's cursor: " + _hotkeyNotice);
            CancelSpellTargeting(null);

            // A right drag between slots moves the entry; one off the bar empties it.
            await Drag(view.SlotCentreForSelfTest(2), view.SlotCentreForSelfTest(6));
            Require(session.QueryHotkeys().Entries[6] == HotkeyEntry.ForSpell(spell) && session.QueryHotkeys().Entries[2].IsEmpty, "the drag moved slot 2 to slot 6: " + _hotkeyNotice);
            await Frames(1);
            Require(view.IconForSelfTest(6) == SpellArt(spell) && view.IconForSelfTest(2) is null, "the icon moved with it");
            await Drag(view.SlotCentreForSelfTest(6), new Vector2(40, 120));
            Require(session.QueryHotkeys().Entries[6].IsEmpty && !view.GhostShownForSelfTest, "the drag off the bar emptied slot 6: " + _hotkeyNotice);

            // An item entry: its name stands in for the icon (not ready) and its count is printed.
            // A hero whose kit is all worn lends a worn piece other than the weapon for the check and wears it again after.
            var worn = session.QueryEquipment().Worn.ToDictionary(w => w.Entity, w => w.Slot, StringComparer.Ordinal);
            var held = session.QueryHeroKit().Select(kit => (kit.Entity, kit.Kind)).Concat(session.QueryPcInventory().Select(pc => (pc.Entity, pc.Kind)))
                .Where(item => item.Kind.Type is not (SessionItemType.Ammo or SessionItemType.Gold)).ToArray();
            var item = held.FirstOrDefault(h => !worn.ContainsKey(h.Entity)).Entity;
            Arcanum.Simulation.Equipment.EquipmentSlot? lent = null;
            if (item is null && held.FirstOrDefault(h => worn.TryGetValue(h.Entity, out var at) && at != Arcanum.Simulation.Equipment.EquipmentSlot.Weapon).Entity is { } piece)
            {
                lent = worn[piece];
                Require(session.Unequip(session.Version, lent.Value, _ => SessionInsertVerdict.Accepted).Disposition == GameSessionDisposition.Changed, $"{piece} taken off for the item entry");
                item = piece;
            }
            string? itemCount = null;
            if (item is not null)
            {
                Require(session.AssignHotkey(session.Version, 3, HotkeyEntry.ForItem(item)).Disposition == GameSessionDisposition.Changed, $"item {item} goes on entry 3");
                await Frames(2);
                itemCount = view.CountForSelfTest(3);
                Require(view.StandInForSelfTest(3).Length > 0 && itemCount == session.QueryHotkeyCount(3)?.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    $"slot 3 shows the item's name '{view.StandInForSelfTest(3)}' and count '{itemCount}'");
                session.ClearHotkey(session.Version, 3);
                if (lent is { } slot) Require(WearFromHotkey(session, item, slot) is null, $"{item} worn again");
            }
            else GD.Print("ARC-UI-027 hotkey-bar smoke: the hero holds no item for an item entry");

            // The latest recent action's button: its icon, and a click activates it with the button sounds.
            var latest = session.QueryHotkeys().Recent[0];
            Require(latest.Kind == HotkeyKind.Spell, "the latest recent action is a spell: " + latest);
            await Frames(1);
            Require(view.RecentArtForSelfTest(0) == SpellArt(latest.Index), "the recent-action button shows the spell's icon");
            _gameBarSoundsForSelfTest.Clear();
            activations = _hotkeyActivations;
            await Click(view.RecentCentreForSelfTest(0));
            Require(_spellTargeting?.Spell == latest.Index && _hotkeyActivations == activations + 1, "a click on the recent action opened its cursor: " + _hotkeyNotice);
            Require(_gameBarSoundsForSelfTest.Select(s => s.Id).SequenceEqual([AreaGameBarLayout.PressSound, AreaGameBarLayout.ReleaseSound]),
                "the button played 3000 then 3001: " + string.Join(",", _gameBarSoundsForSelfTest.Select(s => s.Id)));
            CancelSpellTargeting(null);
            GD.Print($"ARC-UI-027 native-smoke hotkey-bar PASS spell={spell} item={item ?? "-"} count={itemCount ?? "-"} recent={latest.Index} art={_hotkeyBarContent!.ReadyCount}");
        }
        finally
        {
            HideHotkeyBar(); _spellPane?.HidePane();
            _gameBarForSelfTest = barForSelfTest;
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
    }
}
