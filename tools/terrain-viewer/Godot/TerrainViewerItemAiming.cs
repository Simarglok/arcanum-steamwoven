using Arcanum.Session;
using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Magic;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-DEV-365 (technology audit slice T-g; ledger IA1–IA16): the item aiming cursor. An item the hero uses with no target and with OIF_NEEDS_TARGET (the use icon, 185 installed prototypes, IA16) opens the item
// target mode (interface mode 16, the same mode as armed lockpicks, which keep their own path, IA7) with the mask of AreaItemAiming.Mask (IA3). A left press picks as the spell cursor does (SU15–SU19, over the
// cast host's map) and applies: on an object the use at once (0x462CC0 with the target) or the item-use goal 39 (a walk next to the target, then the use), as AreaItemAiming.ApplyOnObject decides (IA8: a charged
// item in its slot-0 spell's range uses at once, out of range or with OIF_NO_RANGED_USE it walks first); on a tile a charged item uses at once (0x462FC0, IA12) and a trap device starts goal 41 (IA9, IA11; the device's own use is NOT READY,
// E6-b). A right press cancels (IA5); a press that finds nothing keeps the cursor open (IA6). The use script runs without the target object (the use script host has none yet), and a healing kit aimed at
// a target is NOT READY here (the heal skill on another critter is slice E8-g).
public partial class TerrainViewerApp
{
    /// <summary>The open item cursor: the item, its use facts, the target mask (IA3) and the slot-0 spell when the mask is the spell's.</summary>
    private sealed class ItemAiming(string item, AreaItemUseFacts facts, ulong mask, int? spell, bool charged)
    {
        public string Item { get; } = item;
        public AreaItemUseFacts Facts { get; } = facts;
        public ulong Mask { get; } = mask;
        public int? Spell { get; } = spell;
        public bool Charged { get; } = charged;
    }

    private ItemAiming? _itemAiming;
    // The last aim's refusal, press or apply, for the self-test and the status line.
    private string? _itemAimLast;
    internal string? ItemAimLastForSelfTest => _itemAimLast;
    internal (string Item, ulong Mask)? ItemAimingForSelfTest => _itemAiming is { } aim ? (aim.Item, aim.Mask) : null;

    /// <summary>The item cursor or armed lockpicks hold interface mode 16.</summary>
    private bool ItemTargetOpen => _lockpickArmed is not null || _itemAiming is not null;

    private (SessionSpellCaster Caster, SessionItemTileUse Use)? _itemTileUse;

    private SessionItemTileUse? ItemTileUseNow()
    {
        if (SpellCasterNow() is not { } caster || _readyArea is not { } area) return null;
        if (_itemTileUse is { } known && ReferenceEquals(known.Caster, caster)) return known.Use;
        var use = new SessionItemTileUse(caster, new AreaCombatWorld(area.Snapshot.RuntimeMap, AreaScanCritters, AreaCombatantFacts));
        _itemTileUse = (caster, use);
        return use;
    }

    private void ItemAimNotice(string text)
    {
        _itemAimLast = text; _portalNotice = text;
        GD.Print("ARC-DEV-365 " + text);
        RefreshAreaStatus();
    }

    /// <summary>
    /// The cursor's opener (0x4EE060 → 0x571AA0, IA1, IA2, IA4) for an item the hero used with no target: the mask (IA3), the window closing (a mask with no inventory bit closes it), then the begin: nothing for a hero that cannot act, an
    /// apply at the hero's own tile for a mask the activation applies at once, the action points (4), and the cursor; with the object bit in the mask and Shift held the item is applied to the hero at once.
    /// </summary>
    private void OpenItemAiming(string item, AreaItemUseFacts facts)
    {
        if (!InventoryOpen || !InventoryActive) return;
        if (SpellCasterNow() is not { } caster || ItemTileUseNow() is null || _areaSession is not { } session)
        {
            _inventoryStatus = "not used: the item cursor needs the map's cast host";
            _inventoryPanel!.Render(InventoryView(_inventoryPanel.Holder), _inventoryStatus);
            return;
        }
        var spells = HeroItemSpells(item);
        var charged = facts.Magical || spells is { Magical: true } || spells is { Charges: not 0 };
        int? spell = spells is { Spells.Count: > 0 } && spells.Spells[0] is var slot && slot is >= 0 and < SpellPaymentRules.Spells && SpellRecordsNow() is { } records && slot < records.Length ? slot : null;
        ulong? begin = spell is { } s ? (SpellRecordsNow()![s].Areas is { Length: > 0 } areas ? areas[0].Targets : 0) : null;
        var mask = AreaItemAiming.Mask(charged, facts.TrapDevice, begin);
        // IA2: the picker returns silently for a user that cannot act, before the window closes.
        var (dead, unconscious) = HeroDownState();
        if (dead || unconscious) { _inventoryStatus = "nothing happens: the hero cannot act"; _inventoryPanel!.Render(InventoryView(_inventoryPanel.Holder), _inventoryStatus); return; }
        if (!SpellUiRules.TargetsInventory(mask)) EndInventory();
        CancelPendingOrder("an item cursor opened");
        CancelSpellTargeting("an item cursor opened");
        var shift = Godot.Input.IsKeyPressed(Key.Shift);
        var decision = AreaItemAiming.Begin(mask, !dead && !unconscious, shift, session.QueryActionPointCheck(SessionCombatant.Hero, TurnBasedRules.UseItemCost));
        var name = _inventoryHost!.ItemName(session, item, _hoverNames?.Catalog);
        GD.Print($"ARC-DEV-365 aim-begin item={item} mask=0x{mask:X} charged={charged} spell={spell?.ToString() ?? "-"} begin={decision}");
        switch (decision)
        {
            case AreaItemAimBegin.Nothing: ItemAimNotice($"{name}: nothing happens (the hero cannot act)"); return;
            case AreaItemAimBegin.NoActionPoints: ItemAimNotice($"{name}: not enough action points"); return;
            case AreaItemAimBegin.InventoryTarget: ItemAimNotice($"not used: {name}'s target is an inventory item (NOT READY)"); return;
            case AreaItemAimBegin.UseAtOwnTile:
                if (caster.Facts(SessionSpellCaster.Hero)?.Location is { } own && own != SpellLocation.None) ApplyItemAim(new(item, facts, mask, spell, charged), null, own, false, false);
                else ItemAimNotice($"not used: {name}: the hero's tile is not known here");
                return;
        }
        if (!OpenTargetMode(InterfaceMode.ItemTarget, _ => { _lockpickArmed = null; _itemAiming = null; _hoverStamp = null; }))
        {
            ItemAimNotice($"not used: {name}: the interface mode did not open");
            return;
        }
        var aim = new ItemAiming(item, facts, mask, spell, charged);
        _itemAiming = aim;
        _hoverStamp = null;
        if (decision == AreaItemAimBegin.UseOnPlayer)
        {
            ApplyItemAim(aim, SessionSpellCaster.Hero, SpellLocation.None, false, true);
            return;
        }
        ItemAimNotice($"{name}: left-click a target, right click cancels");
    }

    /// <summary>The cursor's pick (0x4F2830 → 0x4F28A0, IA6; SU15–SU19): the object under the pointer (seeing through what the mask spares), the objects on the tile under it, the tile; a spell's mask is tested by the spell's
    /// BEGIN area, any other mask passes any object (and a tile only when the mask has the tile bit, IA3). Null when there is no cast host or no pointer target.</summary>
    private SpellPick? ItemAimPick(ItemAiming aim, Vector2 screen)
    {
        if (SpellCasterNow() is not { } caster || _spellCastMap is not { } map || _canvas is null || _worldActor.Session is not AreaGridSession grid) return null;
        var spared = caster.PickSpared(SessionSpellCaster.Hero, aim.Mask);
        var at = AreaObjectAtScreen(screen, spared: key => SpellPickId(key) is { } id && spared(id)) is { } found ? SpellPickId(found) : null;
        var chart = _canvas.GetGlobalTransformWithCanvas().AffineInverse() * screen;
        SpellLocation? tile = null;
        if (AreaCellPicking.Pick(new(chart.X, chart.Y)) is { } cell)
        {
            var world = grid.Chart.ToWorld(cell);
            tile = new SpellLocation(checked((uint)world.X), checked((uint)world.Y));
        }
        if (aim.Spell is { } spell) return caster.PickTarget(SessionSpellCaster.Hero, spell, aim.Mask, at, tile);
        var followers = map.Object(SessionSpellCaster.Hero) is { } self ? map.Party(self).Select(o => o.Id).Where(id => id != SessionSpellCaster.Hero).ToHashSet(StringComparer.Ordinal) : [];
        var mask = aim.Mask;
        return SpellUiRules.Pick(mask, SessionSpellCaster.Hero, followers, at is null ? null : map.Object(at), tile, map,
            (candidate, _) => new SpellTargetTest(candidate is not null || (mask & SpellUiRules.TileBit) != 0));
    }

    /// <summary>A left press while the item cursor is open (the mode-16 branch of 0x54DE50, IA6): a target that passes is applied; with none, the "not damaged" message (spell.mes 606) when that is the area test's reason, and the
    /// cursor stays open either way.</summary>
    private void ItemAimClick(Vector2 screen, bool ctrl, bool shift)
    {
        if (_itemAiming is not { } aim) return;
        if (ItemAimPick(aim, screen) is not { } pick) { ItemAimNotice("no target here"); return; }
        if (!pick.Test.Passes)
        {
            if (SpellUiRules.NoTargetMessage(pick.Test.Reason) is { } message) { var text = AreaSpellCasting.Message(_spellData?.Data?.Texts, message); PostSpellMessage(text); ItemAimNotice(text); }
            else ItemAimNotice("not a valid target");
            return;
        }
        ApplyItemAim(aim, pick.Object?.Id, pick.Tile, ctrl, shift);
    }

    /// <summary>
    /// The apply (0x571CB0, IA7–IA10): the cursor closes (IA5), then an object target is the use at once or goal 39 (<see cref="AreaItemAiming.ApplyOnObject"/>: the slot-0 spell's range against the distance, IA8), a tile target the use
    /// on a tile at once or goal 41 for a trap device (<see cref="AreaItemAiming.ApplyOnTile"/>, IA9). <paramref name="target"/> null means a tile.
    /// </summary>
    private void ApplyItemAim(ItemAiming aim, string? target, SpellLocation tile, bool ctrl, bool shift)
    {
        if (_areaSession is not { } session || SpellCasterNow() is not { } caster) return;
        if (_itemAiming is not null) CloseTargetMode(InterfaceMode.ItemTarget);
        var name = _inventoryHost!.ItemName(session, aim.Item, _hoverNames?.Catalog);
        if (target is not null)
        {
            var inRange = true;
            if (aim.Charged && aim.Spell is { } spell && SpellRecordsNow() is { } records && caster.Facts(SessionSpellCaster.Hero)?.Location is { } from && caster.Facts(target)?.Location is { } there)
                inRange = Math.Max(Math.Abs((long)from.X - there.X), Math.Abs((long)from.Y - there.Y)) <= records[spell].Data.Range;
            var decision = AreaItemAiming.ApplyOnObject(aim.Charged, inRange, aim.Facts.NoRangedUse);
            GD.Print($"ARC-DEV-365 aim-apply item={aim.Item} object={target} in-range={inRange} decision={decision}");
            if (decision == AreaItemAimObject.UseNow) { ItemAimNotice(ItemAimUse(aim.Item, aim.Facts, target, null)); return; }
            var request = HeroRequests.Apply(new UseItemRequest(aim.Item, target, ctrl, shift));
            ItemAimNotice(request.Outcome == HeroRequestOutcome.Done ? $"{name}: walking to the target to use it (goal 39)" : $"not used: {name}: the use was refused ({request.Outcome})");
            return;
        }
        var onTile = AreaItemAiming.ApplyOnTile(aim.Charged, aim.Facts.TrapDevice);
        GD.Print($"ARC-DEV-365 aim-apply item={aim.Item} tile={tile.X},{tile.Y} decision={onTile}");
        switch (onTile)
        {
            case AreaItemAimTile.UseNow: ItemAimNotice(ItemAimUse(aim.Item, aim.Facts, null, tile)); break;
            case AreaItemAimTile.TrapGoal:
                var started = HeroRequests.Apply(new UseItemOnTileRequest(aim.Item, new(checked((int)tile.X), checked((int)tile.Y))));
                ItemAimNotice(started.Outcome == HeroRequestOutcome.Done ? $"{name}: walking to the tile to use it (goal 41)" : $"not used: {name}: the use was refused ({started.Outcome})");
                break;
            default: ItemAimNotice($"not used: {name} thrown at a tile with the item cursor is NOT READY"); break;
        }
    }

    /// <summary>The use at once (0x462CC0 with a target, 0x462FC0 with a tile) with the hero as user and the item's facts; the message is the use's.</summary>
    private string ItemAimUse(string item, AreaItemUseFacts facts, string? target, SpellLocation? tile)
    {
        var session = _areaSession!;
        var name = _inventoryHost!.ItemName(session, item, _hoverNames?.Catalog);
        if (UseNotReady(item, facts) is { } notReady) return notReady;
        // The heal skill on another critter is slice E8-g: nothing is charged or run for a healing kit aimed at a target.
        if (facts.HealingKit && !facts.Magical && HeroItemSpells(item) is not { Charges: not 0 }) return $"not used: {name} aimed at a target (the heal skill on another critter is NOT READY)";
        var world = new HeroItemUse(this, facts);
        var used = tile is { } at ? AiExecutorRules.UseItemOnTileNow(world, AiSchedulerRules.Pc, item, at) : AiExecutorRules.UseItemNow(world, AiSchedulerRules.Pc, item, target);
        var message = world.Message ?? StepText(used, name);
        GD.Print($"ARC-DEV-365 aim-use item={item} target={target ?? "-"} tile={(tile is { } t ? $"{t.X},{t.Y}" : "-")} step={used.Step} charged={used.Charged} spent={used.Spent} aborted={world.Aborted}");
        _lastItemUse = (item, AreaItemUseGate.Use, used.Step, message);
        world.After?.Invoke();
        SyncAreaInventory();
        return message;
    }

    // ---- the item use goals 39 and 41 (the hero's table ports) ----

    // Only the hero's own item use run reads the cast host's objects as targets of the walk and the range test; every other goal keeps its own target kinds.
    private bool ItemAimRunTargets(string target) => HeroRunsHost()?.ItemUseRun is { } run && run.Bottom.Target == target;

    /// <summary>The cell of an object of the cast host's map, for the goals' range test and approach (a target picked by the item cursor is named by the cast host's id).</summary>
    private CellCoord? ItemAimObjectCell(string target) =>
        ItemAimRunTargets(target) && SpellCasterNow()?.Facts(target)?.Location is { } at && at != SpellLocation.None ? new(checked((int)at.X), checked((int)at.Y)) : null;

    /// <summary>The cell the hero walks to so as to stand next to the cast host's object <paramref name="target"/> (goal 8 under goal 39).</summary>
    private CellCoord? ItemAimApproach(string target, int range) =>
        ItemAimRunTargets(target) && _worldActor.Session is AreaGridSession grid && SpellCasterNow()?.Facts(target)?.Location is { } at && at != SpellLocation.None
            && ApproachCell(grid, at.X, at.Y, range) is { } cell ? WorldTileOf(grid, cell) : null;

    /// <summary>The cell the hero walks to so as to stand next to a tile (goal 41's walk).</summary>
    internal CellCoord? ItemTileApproach(CellCoord tile, int range) =>
        _worldActor.Session is AreaGridSession grid && ApproachCell(grid, tile.X, tile.Y, range) is { } cell ? WorldTileOf(grid, cell) : null;

    private static CellCoord WorldTileOf(AreaGridSession grid, CellCoord chartTile)
    {
        var world = grid.Chart.ToWorld(chartTile);
        return new(checked((int)world.X), checked((int)world.Y));
    }

    // The use goals 39 and 41 ask for the use; it is made at the frame's next safe point (the cast it may start, the item it may destroy and the windows it may open change runs, so they do not run inside the table's step).
    private (string Item, string? Target, CellCoord? Tile)? _itemUsePending;

    /// <summary>The last node of goal 39 (0x428CD0): the use of the item on the object is asked for.</summary>
    internal void HeroUseItem(string item, string target) => _itemUsePending = (item, target, null);

    /// <summary>The last node of goal 41 (0x429040): the use of the item on the tile is asked for.</summary>
    internal void HeroUseItemOnTile(string item, CellCoord tile) => _itemUsePending = (item, null, tile);

    /// <summary>The frame's safe point of the item use goals: the use they asked for is made; the item must still be the hero's (node 3 of the goals: facts that do not read mean it left the hero's hands). Also run before a save.</summary>
    private void ProcessItemUsePending()
    {
        if (_itemUsePending is not { } asked) return;
        _itemUsePending = null;
        if (_areaSession is not { } session || _inventoryHost is null) return;
        var facts = _inventoryHost.UseFacts(session, asked.Item, _areaKitUse);
        if (facts is null) { ItemAimNotice("not used: the item is no longer the hero's"); return; }
        ItemAimNotice(ItemAimUse(asked.Item, facts, asked.Target, asked.Tile is { } t ? new SpellLocation(checked((uint)t.X), checked((uint)t.Y)) : null));
    }
}
