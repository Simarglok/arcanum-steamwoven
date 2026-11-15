using Arcanum.Data.Magic;
using Arcanum.Presentation.Hud;
using Arcanum.Session;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Magic;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

public partial class TerrainViewerApp
{
    // ARC-UI-2304: forces the strip's weapon parts off (the diagnostic pane takes the weapon again), for the "before" capture only.
    private bool _weaponStripOffForSelfTest;

    // ARC-UI-2304 native observation, inside the hotkey-bar smoke of the cast stage (the bars show; the hero wields a kit weapon, lent the
    // spell facts of a weapon prototype with a castable critter spell): the strip shows the button art 563 in the message mode; the release
    // over it opens bar mode 8 (3000 and 3001) with the background art 564 and an icon button per shown slot at its place; entering a slot
    // names its spell in mode 8's text area; the release over it opens the cursor with the weapon as the source; a second release over the
    // button gives the message mode back; M replaces mode 8 with the spells mode; taking the weapon off closes mode 8 and hides the button,
    // wielding it again shows the button. ARC_AREA_WEAPON_PANE_CAPTURE=PREFIX saves PREFIX-before.png (the diagnostic pane, the look before
    // ARC-UI-2304), PREFIX-button.png (the message mode with the button) and PREFIX-pane.png (mode 8 with a slot's name).
    private async Task RunWeaponPaneStripSmokeAsync(GameSessionOwner session, Func<int, Task> frames, Func<Vector2, Task> click, Func<Vector2, Vector2?, Task> hover)
    {
        static void Require(bool value, string why) { if (!value) { GD.Print("ARC-UI-2304 weapon-pane smoke FAIL " + why); throw new InvalidOperationException("ARC-UI-2304 weapon-pane smoke: " + why); } }
        if (session.QueryEquipment().Worn.FirstOrDefault(w => w.Slot == Arcanum.Simulation.Equipment.EquipmentSlot.Weapon) is not { Item.Weapon: not null } wielded
            || session.QueryHeroKit().Where(k => k.Entity == wielded.Entity).Select(k => (SessionKitItem?)k).FirstOrDefault() is not { } kit
            || _spellRecords is not { } records)
        { GD.Print("ARC-UI-2304 native-smoke weapon-pane SKIP (the hero wields no kit weapon or no spell data)"); return; }
        bool Castable(int value) => value is >= 0 and < SpellRecord.Count && records[value] is { } r && !r.Data.IsTechnology && r.Data.ItemTriggers == 0
            && r.Data.Enabled && r.Areas.Length > 0 && (r.Areas[0].Targets & 0x40) != 0 && !SpellUiRules.CastsAtOnce(r.Areas[0].Targets)
            && !SpellUiRules.TargetsInventory(r.Areas[0].Targets) && (r.Areas[0].Targets & SpellUiRules.DamagedBit) == 0;
        var lent = LentWeaponSpells(wielded.Entity, Castable);
        Require(lent is not null, "a weapon prototype with a castable critter spell: " + _lentItemSpellsDetail);
        var capture = System.Environment.GetEnvironmentVariable("ARC_AREA_WEAPON_PANE_CAPTURE") is { Length: > 0 } prefix ? prefix : null;
        async Task Capture(string name)
        {
            if (capture is null) return;
            await frames(2);
            using var shot = GetViewport().GetTexture().GetImage();
            if (shot.SavePng($"{capture}-{name}.png") != Error.Ok) throw new InvalidOperationException("AREA_WEAPON_PANE_CAPTURE_FAILED");
        }
        CancelSpellTargeting(null);
        _spellPane?.HidePane();
        _lentItemSpellsForSelfTest = lent; _weaponPaneCache = null; _itemPaneItem = null;
        try
        {
            if (capture is not null)
            {
                // The look before ARC-UI-2304: the diagnostic spell pane's weapon button and its text pane.
                _weaponStripOffForSelfTest = true;
                ShowSpellPane();
                await frames(3);
                _spellPane!.PressWeaponForSelfTest();
                await Capture("before");
                _spellPane.HidePane(); _itemPaneItem = null; _weaponPaneCache = null;
                _weaponStripOffForSelfTest = false;
                await frames(3);
            }
            var slot = Enumerable.Range(0, lent!.Spells.Count).First(i => Castable(lent.Spells[i]));
            var spell = lent.Spells[slot];
            await hover(new Vector2(40, 120), null);
            await frames(3);
            var view = _weaponBarView;
            Require(view is { Visible: true } && !_messageLog.PaneMode, "the strip shows the weapon parts in the message mode");
            Require(view!.ButtonArtForSelfTest is { } buttonArt && Art(MagickWeaponPaneRules.ButtonArt) is { } buttonFrames && buttonFrames.Contains(buttonArt),
                "the weapon button draws its art 563");
            Require(view.ButtonPositionForSelfTest == new Vector2(MagickWeaponPaneRules.ButtonX, MagickWeaponPaneRules.ButtonY - AreaGameBarLayout.BottomTop),
                $"the button stands at ({MagickWeaponPaneRules.ButtonX}, {MagickWeaponPaneRules.ButtonY}): {view.ButtonPositionForSelfTest}");
            Require(view.BackgroundForSelfTest is null && view.ShownSlotsForSelfTest.Count == 0, "no pane before the press");
            Require(_spellPane is null || !_spellPane.DrawnForSelfTest, "the diagnostic pane shows nothing");
            await Capture("button");

            _gameBarSoundsForSelfTest.Clear();
            await click(view.ButtonCentreForSelfTest!.Value);
            await frames(2);
            Require(_itemPaneItem == wielded.Entity && _messageLog.PaneMode && MessagePaneMode == MagickWeaponPaneRules.BarMode, "the release opened bar mode 8 for the weapon");
            Require(_gameBarSoundsForSelfTest.Select(s => s.Id).SequenceEqual([AreaGameBarLayout.PressSound, AreaGameBarLayout.ReleaseSound]),
                "the weapon button played 3000 then 3001: " + string.Join(",", _gameBarSoundsForSelfTest.Select(s => s.Id)));
            Require(!_gameBar.IsOn(AreaBarButton.Spells) && !_gameBar.IsOn(AreaBarButton.Skills), "the Spells and Skills toggles are up in mode 8");
            Require(view.BackgroundForSelfTest == Art(MagickWeaponPaneRules.BackgroundArt)?[0], "the pane draws the background art 564");
            var pane = WeaponPaneNow();
            Require(pane is { Open: true } && view.ShownSlotsForSelfTest.SequenceEqual(pane.Buttons.Select(b => b.Slot)) && view.ShownSlotsForSelfTest.Contains(slot),
                $"the shown slots are the pane's: {string.Join(",", view.ShownSlotsForSelfTest)}");
            foreach (var shown in pane!.Buttons)
            {
                Require(view.SlotPositionForSelfTest(shown.Slot) == new Vector2(MagickWeaponPaneRules.SlotButtonX[shown.Slot], MagickWeaponPaneRules.SlotButtonY - AreaGameBarLayout.BottomTop),
                    $"slot {shown.Slot} stands at its place: {view.SlotPositionForSelfTest(shown.Slot)}");
                Require(view.SlotArtForSelfTest(shown.Slot) is { } art && Art(MagickWeaponPaneRules.SpellIcon(shown.Spell, false)) is { } icon && icon.Contains(art),
                    $"slot {shown.Slot} draws spell {shown.Spell}'s icon");
            }

            var at = view.SlotCentreForSelfTest(slot)!.Value;
            await hover(at, at + new Vector2(0, 40));
            Require(_messageLog.PaneMode && _gameBarPanel!.MessageLog.PaneNameForSelfTest == AreaSpellsPage.SpellName(_spellData?.Data, spell),
                $"entering slot {slot} names spell {spell}: '{_gameBarPanel!.MessageLog.PaneNameForSelfTest}'");
            await Capture("pane");

            _gameBarSoundsForSelfTest.Clear();
            await click(at);
            Require(_spellTargeting is { Item: { } source } && source == wielded.Entity && _spellTargeting.Spell == spell,
                $"the release over slot {slot} opened the cursor with the weapon as the source: {_spellNotice}");
            Require(_gameBarSoundsForSelfTest.Select(s => s.Id).SequenceEqual([AreaGameBarLayout.PressSound, AreaGameBarLayout.ReleaseSound]),
                "the slot played 3000 then 3001: " + string.Join(",", _gameBarSoundsForSelfTest.Select(s => s.Id)));
            CancelSpellTargeting(null);
            Require(_itemPaneItem == wielded.Entity, "the activation leaves mode 8 up");

            await click(view.ButtonCentreForSelfTest!.Value);
            await frames(2);
            Require(_itemPaneItem is null && !_messageLog.PaneMode && view.BackgroundForSelfTest is null, "a second release gives the message mode back");

            await click(view.ButtonCentreForSelfTest!.Value);
            await frames(1);
            Require(_itemPaneItem == wielded.Entity, "the pane opened again");
            InventoryKey(Key.M);
            await frames(2);
            Require(_itemPaneItem is null && MessagePaneMode == SpellPaneRules.BarMode && view.BackgroundForSelfTest is null && _spellBarView is { Visible: true },
                "M replaced mode 8 with the spells mode");
            _spellPane!.HidePane();
            await frames(2);

            await click(view.ButtonCentreForSelfTest!.Value);
            await frames(1);
            Require(_itemPaneItem == wielded.Entity, "the pane opened before the weapon goes");
            var host = new SessionItemTriggers(SpellCasterNow()!, CombatWorldOrNull() ?? NoSpellCombat.Instance);
            var off = session.Unequip(session.Version, Arcanum.Simulation.Equipment.EquipmentSlot.Weapon, _ => SessionInsertVerdict.Accepted, triggers: host);
            Require(off.Disposition == GameSessionDisposition.Changed, $"the weapon is taken off: {off.Failure} {off.Refusal}");
            await frames(3);
            Require(_itemPaneItem is null && !_messageLog.PaneMode && view.ButtonCentreForSelfTest is null, "taking the weapon off closed mode 8 and hid the button");
            Require(session.Equip(session.Version, wielded.Entity, Arcanum.Simulation.Equipment.EquipmentSlot.Weapon, new(kit.Kind, [], false, wielded.Item),
                (_, _) => true, _ => SessionInsertVerdict.Accepted, triggers: host).Disposition == GameSessionDisposition.Changed, "the weapon is wielded again");
            await frames(3);
            Require(view.ButtonCentreForSelfTest is not null && _itemPaneItem is null, "the weapon button is back, the bar in the message mode");
            GD.Print($"ARC-UI-2304 native-smoke weapon-pane PASS weapon={wielded.Entity} lent={_lentItemSpellsDetail} slots={string.Join(",", pane.Buttons.Select(b => $"{b.Slot}:{b.Spell}"))} "
                + $"slot={slot} spell={spell} capture={capture ?? "none"}");
        }
        finally
        {
            CancelSpellTargeting(null);
            _weaponStripOffForSelfTest = false;
            _lentItemSpellsForSelfTest = null; _itemPaneItem = null; _weaponPaneCache = null;
            await frames(2);
        }
    }
}
