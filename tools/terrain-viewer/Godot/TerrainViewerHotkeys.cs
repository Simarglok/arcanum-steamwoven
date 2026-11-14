using Arcanum.Input;
using Arcanum.Session;
using Arcanum.Simulation.Equipment;
using Arcanum.Simulation.Hotkeys;
using Arcanum.Simulation.Magic;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-UI-022: the original's hotkey bar in the AREA view (ledger "Hotkey bar", HK rows). The keys 1..0 activate the session's entries 0..9
// and A the latest recent action, as 0x54DE50 → 0x57F1D0 and 0x57DC60 do (not while another critter acts in a turn-based round). The bar's
// art and its drag and drop are ARC-UI-027 (TerrainViewerHotkeyBar.cs); the viewer keeps stand-in keys: Ctrl+1..0 puts the latest recent action on that entry
// (a stand-in for the skill bar, which the viewer does not draw: the original drags from the bars, not from the recent-action buttons, HK13), Ctrl+Shift+1..0 empties the entry, and 1..0 while an item is held in the
// inventory window drops it on the entry. A spell pressed in the spell pane and the palette's Prowl action (the skill bar's prowling
// button) become the latest recent action (0x57EFA0). The activation carries out what the viewer has: a spell (the spell UI's activation),
// an item's spell (its weapon wielded first), the four skill-bar buttons (prowling, pick pocket, disarm traps, the repair button's inventory window), and a weapon's or armour's hotkey wear;
// the item use and the held cursor are not ready and say so. ARC-UI-1113: the pick pocket button arms the pick pocket's cursor.
public partial class TerrainViewerApp
{
    private string? _hotkeyNotice;
    private int _hotkeyActivations;

    private bool HotkeysActive => SpellMapActive && _areaSession?.QueryHero() is not null;

    /// <summary>The keys 1..0 and A while the AREA session has a hero. Returns true when the key belongs to the bar, so it is not reported as
    /// a reserved, unimplemented original key.</summary>
    private bool RouteHotkeyKey(WorldKeyDecision decision, WorldKeyModifiers modifiers)
    {
        var entry = decision.Key is >= WorldKey.Digit0 and <= WorldKey.Digit9 ? (decision.Key == WorldKey.Digit0 ? 9 : decision.Key - WorldKey.Digit1) : (int?)null;
        if (!HotkeysActive || entry is null && decision.Key != WorldKey.A) return false;
        if (!decision.Pressed || decision.Echo) return true;
        if (!_worldFocused || PanelOutsideStackOpen || ConversationOpen || WorldUiFocus) { _hotkeyNotice = "hotkey ignored: palette, menu, dialog or UI focus owns input"; RefreshAreaStatus(); return true; }
        var session = _areaSession!;
        // ARC-UI-027: the entry a drag holds is skipped by its key (0x57F1D0, HK8, HK15), and the stand-in keys leave it alone.
        var dragged = entry is { } held && _hotkeyDrag is { Kind: Arcanum.Presentation.Hud.HotkeyDragSourceKind.Entry } drag && drag.Entry == held;
        if (dragged && (modifiers & WorldKeyModifiers.Ctrl) != 0) { _hotkeyNotice = $"hotkey {KeyName(entry!.Value)}: being dragged"; RefreshAreaStatus(); return true; }
        if (entry is { } slot && (modifiers & WorldKeyModifiers.Ctrl) != 0)
        {
            GameSessionMutationResult? result = (modifiers & WorldKeyModifiers.Shift) != 0 ? session.ClearHotkey(session.Version, slot)
                : session.QueryHotkeys().Recent[0] is { IsEmpty: false } latest ? session.AssignHotkey(session.Version, slot, latest)
                : null;
            _hotkeyNotice = result is not { } done ? "no recent action to put on the bar" : $"hotkey {KeyName(slot)}: {done.Disposition} {done.Failure}";
            GD.Print($"ARC-UI-022 hotkey-set slot={slot} disposition={result?.Disposition.ToString() ?? "-"} failure={result?.Failure.ToString() ?? "-"} "
                + $"entry={Describe(session.QueryHotkeys().Entries[slot])}");
            RefreshAreaStatus();
            return true;
        }
        // Another critter's turn ends the key handler for the digits (0x54DE50); the character A has no such test (HK8).
        var round = session.QueryCombatRound();
        var otherActs = entry is not null && InTurnBasedRound && round is not null && !round.Actor.IsHero;
        var target = entry is { } index ? session.QueryHotkeys().Entries[index] : session.QueryHotkeys().Recent[0];
        if (!HotkeyRules.KeyActivates(true, otherActs, false, target, dragged))
        {
            _hotkeyNotice = otherActs ? "hotkey ignored: another critter's turn" : dragged ? $"hotkey {KeyName(entry!.Value)}: being dragged" : $"hotkey {(entry is { } e ? KeyName(e) : "A")}: empty";
            RefreshAreaStatus();
            return true;
        }
        ActivateHotkey(target, entry is { } at ? KeyName(at) : "A");
        return true;
    }

    /// <summary>An item held in the inventory window dropped on entry <paramref name="slot"/> (the window owns every key while open).</summary>
    private bool DropHeldOnHotkey(InputEventKey key)
    {
        var routed = key.Keycode != Key.None ? key.Keycode : key.PhysicalKeycode;
        if (!key.Pressed || key.Echo || routed is < Key.Key0 or > Key.Key9 || !HotkeysActive || _inventoryPanel?.Held is null) return false;
        DropHeldItemOnEntry(routed == Key.Key0 ? 9 : (int)(routed - Key.Key1));
        return true;
    }

    /// <summary>The item the inventory window holds dropped on entry <paramref name="slot"/> (by its key, or ARC-UI-027 a click on its slot).</summary>
    private void DropHeldItemOnEntry(int slot)
    {
        // ARC-UI-052: a looted holder's item is not the hero's: it does not go on the bar (it stays on the cursor).
        if (_inventoryPanel?.Held is not { } held || held.Partner || _areaSession is not { } session) return;
        if (held.Worn is not null)
        {
            _inventoryStatus = "a worn item cannot go on the hotkey bar here (not ready)";
            _inventoryPanel.Render(InventoryView(_inventoryPanel.Holder), _inventoryStatus);
            return;
        }
        _inventoryPanel.Hold(null);
        var result = session.AssignHotkey(session.Version, slot, HotkeyEntry.ForItem(held.Entity));
        GD.Print($"ARC-UI-022 hotkey-drop slot={slot} item={held.Entity} disposition={result.Disposition} failure={result.Failure}");
        _inventoryStatus = $"hotkey {KeyName(slot)}: {result.Disposition} {result.Failure}";
        _inventoryPanel.Render(InventoryView(_inventoryPanel.Holder), _inventoryStatus);
    }

    /// <summary>A press of the spell pane or the skill bar becomes the latest recent action (0x57EFA0, before the activation).</summary>
    private void RecordRecentAction(HotkeyEntry action)
    {
        if (_areaSession is not { } session || session.QueryHero() is null) return;
        var result = session.RecordHotkeyAction(session.Version, action);
        if (result.Disposition == GameSessionDisposition.Rejected) GD.Print($"ARC-UI-022 recent-refused {Describe(action)} failure={result.Failure}");
    }

    /// <summary>ARC-UI-2068 (DB2, 0x54FCF0, 0x551A80(0)): a hotkey that acts while the dialog is open ends it first, through the dialog's leave case
    /// (0x5678D0: no script runs, nothing resumes), as a window opener's pop does (ARC-UI-1467). The original's bars stay under the dialog (Original
    /// interface setting); Steamwoven hides them, so under Steamwoven no click reaches here. No-op without an open dialog (the pop outside a dialog re-enters the current mode).</summary>
    private void EndDialogForHotkey()
    {
        if (!ConversationOpen) return;
        GD.Print("ARC-UI-2068 hotkey ends the dialog before it acts");
        EndConversation();
    }

    /// <summary>ARC-UI-2068 (0x57BC70, 0x57C080): the spell activation pops the dialog after its own refusal tests (a valid spell) unless the BEGIN
    /// area carries the inventory bit 0x2000000, whose window opens over the dialog in the original (SU3; the viewer's opener refuses while a window is open, so
    /// the dialog stays and nothing opens). The place of the action-point test against the pop is not read (issue #2087).</summary>
    private bool SpellEndsDialog(int spell) => SpellCasterNow() is not null && _spellRecords is { } records && spell >= 0 && spell < records.Length && records[spell].Data.Enabled
        && !SpellUiRules.TargetsInventory(records[spell].Areas.Length > 0 ? records[spell].Areas[0].Targets : 0);

    /// <summary>The activation (0x54FCF0): the PC's test, then the entry's type.</summary>
    private void ActivateHotkey(HotkeyEntry entry, string key)
    {
        var session = _areaSession!;
        var life = session.QueryHeroLife()?.Status;
        _hotkeyActivations++;
        string outcome;
        if (!HotkeyRules.PcCanActivate(false, true, life?.IsDead == true, life?.IsUnconscious == true, false)) outcome = "the hero cannot act";
        else switch (entry.Kind)
        {
            case HotkeyKind.Spell:
                if (SpellEndsDialog(entry.Index)) EndDialogForHotkey();
                ActivateSpell(entry.Index);
                outcome = "spell " + entry.Index;
                break;
            case HotkeyKind.ItemSpell:
                outcome = ActivateItemSpellHotkey(session, entry.Item!, entry.Index);
                break;
            case HotkeyKind.Skill:
                // 0x579FA0: the opener pops the dialog unless the skill row keeps it (row 3, the repair button: its item window opens over the dialog).
                if ((SkillBarButton)entry.Index != SkillBarButton.Repair) EndDialogForHotkey();
                outcome = ActivateSkillButton(session, (SkillBarButton)entry.Index);
                break;
            case HotkeyKind.Item:
                outcome = ActivateItemHotkey(session, entry.Item!);
                break;
            default:
                outcome = "empty";
                break;
        }
        _hotkeyNotice = $"hotkey {key}: {outcome}";
        GD.Print($"ARC-UI-022 hotkey-activate key={key} entry={Describe(entry)} outcome={outcome}");
        RefreshAreaStatus();
    }

    // The type-4 branch: the wielded weapon goes straight to 0x57C080; another item is first wielded into slot 1004. The wield is a move, so
    // the entry empties (HK6); the original then reads the emptied entry and its cast is lost, here the item and spell read before the wield
    // are cast (HK4, a technical slip fixed in both modes). The session's wear is one step: the original takes the old weapon off first and
    // leaves it off when the wield fails (HK3).
    private string ActivateItemSpellHotkey(GameSessionOwner session, string item, int spell)
    {
        var held = session.QueryHeroKit().Any(kit => kit.Entity == item) || session.QueryPcInventory().Any(pc => pc.Entity == item)
            || session.QueryMadeItems().Any(made => made.Entity == item);
        var plan = HotkeyRules.PlanItemSpell(held, HeroWeapon() == item);
        if (plan == HotkeyItemSpellAction.Nothing) return "the hero no longer holds the item";
        if (plan == HotkeyItemSpellAction.WieldThenActivate && WearFromHotkey(session, item, EquipmentSlot.Weapon) is { } refused) return refused;
        if (SpellCasterNow() is not { } caster || !HotkeyRules.SpellActivates(spell, caster.ItemTriggered)) return $"spell {spell} does not activate";
        if (SpellEndsDialog(spell)) EndDialogForHotkey();
        ActivateSpell(spell, item);
        return $"spell {spell} of {item}";
    }

    // The type-1 branch: its plan, carried out where the viewer has the step.
    private string ActivateItemHotkey(GameSessionOwner session, string item)
    {
        // 0x54FCF0: the item branch pops the dialog first, before it even tests that the hero still holds the item.
        EndDialogForHotkey();
        if (SpellCasterNow() is not { } caster || caster.Item(item) is not { } facts) return "the item's facts do not read here";
        int Wear(string entity) => _inventoryHost?.Wearable(session, entity, _areaKitWear) is { } wearable && EquipmentRules.WearSlot(wearable.Item) is { } at ? (int)at : -1;
        HotkeyItemFacts Facts(string entity, SessionItemSpells spells) => new(entity, spells.Type, spells.Flags, Wear(entity));
        var other = session.QueryHotkeySameKind(item) is { } same && caster.Item(same) is { } sameFacts ? Facts(same, sameFacts) : (HotkeyItemFacts?)null;
        var plan = HotkeyRules.PlanItem(Facts(item, facts), other, true, Godot.Input.IsKeyPressed(Key.Shift), _gameCursorItem?.Item ?? _inventoryPanel?.Held?.Entity);
        return plan.Action switch
        {
            HotkeyItemAction.Wear => WearFromHotkey(session, plan.Item!, (EquipmentSlot)plan.WearSlot) ?? $"{plan.Item} worn",
            HotkeyItemAction.Use => $"use {plan.Item}{(plan.TargetSelf ? " on the hero" : "")}: the item use is not ready",
            HotkeyItemAction.Hold => HoldItemOnGameCursor(plan.Item!, "hotkey") ?? $"{plan.Item} on the cursor: left-click a tile to throw it, right-click puts it back",
            _ => "nothing for this item",
        };
    }

    // The hotkey wear (0x550000): the slot's occupant goes back to the pack; a refusal leaves both where they were. Null when worn.
    private string? WearFromHotkey(GameSessionOwner session, string item, EquipmentSlot slot)
    {
        if (WornSlotOf(session, item) == slot) return null;
        if (_inventoryHost?.Wearable(session, item, _areaKitWear) is not { } wearable) return $"{item} has no wear values here";
        var result = session.Equip(session.Version, item, slot, wearable, (_, _) => true, _inventoryHost.InsertCheck, _inventoryHost.ItemScripts(session), ItemTriggersNow());
        return result.Disposition == GameSessionDisposition.Rejected ? $"not worn: {result.Failure} {result.Refusal}" : null;
    }

    private static EquipmentSlot? WornSlotOf(GameSessionOwner session, string item) =>
        session.QueryEquipment().Worn.FirstOrDefault(worn => worn.Entity == item)?.Slot;

    /// <summary>The skill bar's button press (0x579FA0 → 0x57A0A0), the same for a hotkey entry of type 2 and for the skill pane's button
    /// (ARC-UI-1113): prowling toggles concealment, pick pocket and disarm traps arm the skill cursor (interface mode 2, each replacing the
    /// other), repair opens the inventory window. The caller has tested that the hero can act (0x45D8D0, 0x45D800).</summary>
    private string ActivateSkillButton(GameSessionOwner session, SkillBarButton button) => button switch
    {
        SkillBarButton.Prowling => ToggleProwlFromBar(session),
        SkillBarButton.PickPocket => ArmPickPocketFromBar(),
        SkillBarButton.DisarmTraps => ArmDisarmTraps(),
        SkillBarButton.Repair => OpenRepair(),
        _ => $"{button}: no such skill button",
    };

    private string ToggleProwlFromBar(GameSessionOwner session)
    {
        var result = session.ToggleHeroConcealment(session.Version);
        BindGaitPalette();
        return result.Disposition == GameSessionDisposition.Rejected ? $"prowl refused: {result.Failure}" : HeroConcealed ? "prowling" : "prowling off";
    }

    // The repair button opens the inventory to pick the item (0x57A0A0 slot 3); the viewer's window lists a Repair button per damaged item.
    private string OpenRepair()
    {
        if (!InventoryActive) return "repair: no inventory here";
        if (!InventoryOpen) OpenInventory();
        return InventoryOpen ? "repair: pick the item in the inventory window" : "repair: ignored while another window is open";
    }

    private static string KeyName(int slot) => slot == 9 ? "0" : (slot + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static string Describe(HotkeyEntry entry) => entry.Kind switch
    {
        HotkeyKind.None => "-",
        HotkeyKind.Item => "item " + entry.Item,
        HotkeyKind.Skill => ((SkillBarButton)entry.Index).ToString(),
        HotkeyKind.Spell => "spell " + entry.Index,
        _ => $"spell {entry.Index} of {entry.Item}",
    };

    private string HotkeyStatus()
    {
        if (!HotkeysActive) return "";
        var bar = _areaSession!.QueryHotkeys();
        var set = Enumerable.Range(0, HotkeyRules.Entries).Where(i => !bar.Entries[i].IsEmpty).Select(i => $"{KeyName(i)} {Describe(bar.Entries[i])}");
        return $" | hotkeys [{string.Join(", ", set)}] A {Describe(bar.Recent[0])}" + (_hotkeyNotice is null ? "" : ": " + _hotkeyNotice);
    }
}
