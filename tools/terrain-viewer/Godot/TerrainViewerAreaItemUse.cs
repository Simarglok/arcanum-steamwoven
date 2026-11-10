using Arcanum.Session;
using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Combat;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-UI-025: the inventory window's drop and use (ledger IW5, IW6). The drop icon (or a Drop button) drops a held item on the hero's tile
// through the session (GameSessionOwner.DropHeroItem: drop script, no-drop flag, removal events, junk pile and decay); ARC-UI-029: an
// ammunition stack goes the stack path (DropHeroAmmo) after the quantity window (TerrainViewerAreaQuantity). A right click on an item (its release, IW5) or its Use button uses it with the hero
// as user and target, the use icon with the hero as target only while Shift is held; both run the original's item use (0x462CC0,
// AiExecutorRules.UseItemNow) over the hero's world below: read, heal with a healing kit, the item's magic through the cast host, spend
// food and scrolls, 4 action points on the hero's turn of a round. ARC-UI-063: in the own window an item whose use is a throw takes the
// map drop (flag 4) onto the game cursor (TerrainViewerHeldThrow).
public partial class TerrainViewerApp
{
    // The last use's branch and message (the self-test and the smoke read them).
    private (string Item, AreaItemUseGate Gate, AiItemUseStep? Step, string Message)? _lastItemUse;
    private InventoryMutationResult? _lastGroundDrop;
    internal (string Item, AreaItemUseGate Gate, AiItemUseStep? Step, string Message)? LastItemUseForSelfTest => _lastItemUse;
    internal InventoryMutationResult? LastGroundDropForSelfTest => _lastGroundDrop;

    /// <summary>A right click (its release) on an item of the window, or its Use button (IW5).</summary>
    private void UseItemFromWindow(string item) => UseHeroItem(item, AreaItemUseSource.RightClick, shift: false);

    private void UseHeroItem(string item, AreaItemUseSource source, bool shift)
    {
        _lastItemUse = null;
        if (!InventoryOpen || !InventoryActive) return;
        var holder = _inventoryPanel!.Holder;
        var session = _areaSession!;
        var name = _inventoryHost!.ItemName(session, item, _hoverNames?.Catalog);
        var worn = session.QueryEquipment().Worn.Any(entry => entry.Entity == item);
        AiItemUseStep? step = null;
        var gate = AreaItemUseGate.NotUsable;
        var factsRead = true;
        string message;
        if (source == AreaItemUseSource.RightClick && holder is not null) message = "nothing: a right click uses an item only in the hero's own window";
        else if (_inventoryHost.UseFacts(session, item, _areaKitUse) is not { } facts) { message = $"not used: {name}'s use facts do not read here"; factsRead = false; }
        else
        {
            gate = AreaItemUse.Gate(source, facts, worn, InTurnBasedRound, session.QueryCombatRound() is { Actor.IsHero: true });
            switch (gate)
            {
                case AreaItemUseGate.Wearable: message = $"nothing: {name} is worn or has a wear location (a right click uses other items)"; break;
                case AreaItemUseGate.NotUsable: message = $"not used: {name} cannot be used from the inventory"; break;
                case AreaItemUseGate.NotHeroTurn: message = "not used: another critter has the turn"; break;
                // ARC-UI-063: in the own window the use of a thrown item is the map drop onto the game cursor (IW6, IW17, IW18).
                case AreaItemUseGate.Throw when holder is null: LiftToGameCursor(item, "use-icon"); return;
                case AreaItemUseGate.Throw: message = $"not used: {name} is thrown (its use in a loot or steal window is NOT READY)"; break;
                default:
                    if (UseNotReady(item, facts) is { } notReady) { message = notReady; break; }
                    var world = new HeroItemUse(this, facts);
                    var used = AiExecutorRules.UseItemNow(world, AiSchedulerRules.Pc, item, AreaItemUse.Target(source, shift));
                    step = used.Step;
                    message = world.Message ?? StepText(used, name);
                    GD.Print($"ARC-UI-025 use item={item} source={source} shift={shift} step={used.Step} charged={used.Charged} spent={used.Spent} " +
                        $"aborted={world.Aborted} after={world.After?.ToString() ?? "-"}");
                    _lastItemUse = (item, gate, step, message);
                    // A read, a heal or armed lockpicks re-render or close the window themselves.
                    if (world.After is { } after) { after(); return; }
                    break;
            }
        }
        // ARC-DEV-336: the log says whether the item's facts read (an item whose facts do not read is refused with the same gate as one without the use flag).
        if (step is null) GD.Print($"ARC-UI-025 use item={item} source={source} gate={gate} facts={(factsRead ? "read" : "missing")}");
        _lastItemUse = (item, gate, step, message);
        _inventoryStatus = message;
        if (InventoryOpen) _inventoryPanel.Render(InventoryView(holder), message);
        SyncAreaInventory(); RefreshReadyStatus();
    }

    // What the viewer cannot run of this use, checked before anything changes: the magic branch and the scroll test need the map's cast
    // host; a healing kit whose use or destroy script the session's heal refuses (the use script would already have run and charged).
    private string? UseNotReady(string item, AreaItemUseFacts facts)
    {
        var spells = HeroItemSpells(item);
        var magic = facts.Magical || spells is { Magical: true } || spells is { Charges: not 0 };
        if ((magic || facts.Type == AreaItemUseFacts.ScrollType) && ItemTriggersNow() is not SessionItemTriggers)
            return "not used: the item's magic needs the map's cast host";
        if (magic || !facts.HealingKit) return null;
        if (_inventoryHost!.HealingItem(_areaSession!, item, _areaKitHealing) is not { } kit) return "not used: the healing kit's facts do not read here";
        return kit.ScriptPoints.Contains(SessionHealingItem.UseScript) || kit.ScriptPoints.Contains(SessionHealingItem.DestroyScript)
            ? "not used: the healing kit's scripts do not run with the heal here" : null;
    }

    private static string StepText(AiItemUseResult used, string name) => used.Step switch
    {
        AiItemUseStep.ScrollTooHard => $"not used: {name} needs more intelligence",
        AiItemUseStep.ScriptStopped => $"{name}: its use script stopped the use",
        AiItemUseStep.NotLockable => $"nothing: {name} needs a lock to pick",
        AiItemUseStep.Spent => $"used {name}" + (used.Spent ? " (used up)" : string.Empty),
        AiItemUseStep.Magic => $"used {name}'s magic" + (used.Spent ? " (used up)" : string.Empty),
        AiItemUseStep.Nothing => $"nothing: {name} does nothing when used",
        _ => $"used {name}",
    };

    /// <summary>The drop icon or a Drop button: the item leaves the hero onto its tile (0x461A70, IW6).</summary>
    private void DropToGround(string item)
    {
        _lastGroundDrop = null;
        if (!InventoryOpen || !InventoryActive) return;
        var holder = _inventoryPanel!.Holder;
        var session = _areaSession!;
        var name = _inventoryHost!.ItemName(session, item, _hoverNames?.Catalog);
        string message;
        // ARC-UI-091: the hero's gold pile goes the original's gold path (0x464830, 0x464970), after the quantity window when it holds more than one coin.
        if (item == SessionHeroGrid.GoldEntity) { DropGoldToGround(name); return; }
        // ARC-UI-029: an ammunition stack goes the original's stack path (0x4658E0, 0x465A40), after the quantity window when it holds more than one unit.
        if (session.QueryAmmo(item) is not null) { DropStackToGround(item, name); return; }
        if (_inventoryHost.GridMoveItem(session, item, _areaKitGridMove) is not { } facts) message = $"not dropped: {name}'s facts do not read here";
        else if (!HeroLocationKnown()) message = "not dropped: the hero's tile is not known here";
        else
        {
            var triggers = ItemTriggersNow();
            var result = session.DropHeroItem(session.Version, item, facts, _inventoryHost.ItemScripts(session), triggers);
            _lastGroundDrop = result;
            message = result.Disposition == GameSessionDisposition.Rejected ? $"not dropped: {DropRefusal(result.Failure)}"
                : result.DeclinedBy is not null ? $"{name} stays: its drop script kept it" : $"dropped {name} on the ground";
            var placed = session.QueryPlacedItems().FirstOrDefault(entry => entry.Entity == item);
            GD.Print($"ARC-UI-025 ground-drop item={item} disposition={result.Disposition} failure={result.Failure} declined-by={result.DeclinedBy?.ToString() ?? "-"} " +
                $"cell={placed?.Cell.ToString() ?? "-"} pile={session.QueryJunkPiles().Any(pile => pile.Items.Any(entry => entry.Entity == item))} " +
                $"scripts={result.Scripts.Count} spells={result.ItemSpells.Count} decays={session.QueryItemDecays().Any(entry => entry.Entity == item)}");
        }
        _inventoryStatus = message;
        _inventoryPanel.Render(InventoryView(holder), message);
        SyncAreaInventory(); RefreshReadyStatus();
    }

    private static string DropRefusal(GameSessionFailure failure) => failure switch
    {
        GameSessionFailure.ItemNotDroppable => "it cannot be dropped",
        GameSessionFailure.ItemScriptUnsupported => "its scripts do not run here",
        GameSessionFailure.ItemEffectUnsupported => "its spells need the cast host",
        GameSessionFailure.DropTileUnfit => "the hero's tile is outside the map",
        var other => AreaInventoryHost.Refusal(other),
    };

    /// <summary>
    /// The hero's side of the item use (0x462CC0) from the inventory window. Each member runs where the original calls it; a step that
    /// closes or re-renders the window waits in <see cref="After"/> until the use returns. A use script that cannot run here aborts the
    /// use: nothing more happens and no action points are charged (<see cref="Aborted"/>).
    /// </summary>
    private sealed class HeroItemUse(TerrainViewerApp app, AreaItemUseFacts facts) : IItemUseWorld
    {
        public string? Message { get; private set; }
        public Action? After { get; private set; }
        public bool Aborted { get; private set; }

        public AiUsedItem UsedItem(string user, string item) =>
            AreaItemUse.Used(facts, app.HeroItemSpells(item), facts.Type == AreaItemUseFacts.ScrollType && app.SpellCasterNow()?.ScrollTooHard(item) == true);

        public bool RunUseScript(string user, string item, string? target)
        {
            if (!facts.UseScript) return true;
            if (app.ItemUseScript(item, reading: false) is not { } stopped) return true;
            Message = stopped;
            // A script the viewer could not run did not stop the use: nothing more happens.
            if (stopped.StartsWith("not used", StringComparison.Ordinal)) Aborted = true;
            return false;
        }

        public void OpenTargeting(string user, string item)
        {
            // The targeting cursor (0x4EE060): lockpicks arm the viewer's lock pick; any other item's cursor is not built.
            if (facts.Lockpick) After = () => app.ArmLockpick(item);
            else Message = "not used: the item needs a target (the item targeting cursor is NOT READY)";
        }

        public void UseTrapDevice(string user, string item, string? target) => Message = "not used: a trap device's use is NOT READY";

        public void ReadWritten(string user, string item) => After = () => app.ReadItem(item, scriptRan: facts.UseScript);

        // Only the hero is a target here, and a critter has no lock.
        public bool Lockable(string target) => false;

        public void StartSkillWithItem(string user, string item, string target, int skill)
        {
            if (skill == AiPassChoice.HealSkill) After = () => app.HealSelf(item, charged: true);
            else Message = $"not used: skill {skill} with an item is NOT READY here";
        }

        public void UseItemMagic(string user, string item, string? target)
        {
            if (app.ItemTriggersNow() is not SessionItemTriggers triggers) { Message = "not used: the item's magic needs the map's cast host"; Aborted = true; return; }
            var used = triggers.UseItem(item, target);
            GD.Print($"ARC-UI-025 use-magic item={item} outcome={used.Outcome} cast={used.Cast} spent={used.Spent}");
            if (used.Outcome != SessionItemUseOutcome.Used) { Message = $"not used: {used.Outcome}"; Aborted = true; }
        }

        public void SpendItem(string item)
        {
            // The magic branch's cast host already spent a used food or scroll; a use that could not run spends nothing.
            var session = app._areaSession!;
            if (Aborted || session.IsItemDestroyed(item)) return;
            var spent = session.DestroyUsedItem(session.Version, item);
            if (spent.Disposition == GameSessionDisposition.Rejected) Message = $"not spent: {spent.Failure}";
        }

        public bool InRound => app.InTurnBasedRound;
        public string? RoundActor => app._areaSession!.QueryCombatRound() is { } round ? round.Actor.IsHero ? AiSchedulerRules.Pc : "npc" : null;

        public void Consume(string id, int cost)
        {
            if (!Aborted) app.ChargeHeroAction(cost, "use");
        }
    }
}
