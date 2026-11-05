using Arcanum.Presentation.Hud;
using Arcanum.Session;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-UI-029: the quantity window (the original's interface mode 13, ledger QW1–QW6) in the inventory window. A gold pile or an
// ammunition stack of more than one unit taken from a dead NPC (ARC-WORLD-101: or a container), and an ammunition stack the hero drops on its tile, first ask how
// many units move (QuantityBar.Ask; one unit moves at once by the same stack path); the window starts at 0, Accept moves the count shown (0 moves nothing), Cancel nothing. Take all
// moves whole stacks without asking (the original's flag 0x20). A junk pile's stacks still move whole (its partial take is not
// modelled). The moves are the session's: TakeGoldFromNpc, TakeAmmoFromNpc and DropHeroAmmo. No action points (IW8).
public partial class TerrainViewerApp
{
    /// <summary>The move the quantity window asks about: a take from a looted NPC (<see cref="Holder"/>) or a drop on the hero's tile.</summary>
    private sealed record AreaQuantityMove(string Item, string Name, string? Holder, SessionItemKind Kind, int Units);

    private AreaQuantityMove? _quantityMove;
    private InventoryMutationResult? _lastQuantityMove;
    internal InventoryMutationResult? LastQuantityMoveForSelfTest => _lastQuantityMove;

    /// <summary>A take button on a gold or ammunition line of a dead NPC: asks, or moves at once (QW1). False for any other line.</summary>
    private bool TakeStackFromBody(string holder, string item)
    {
        if (SessionJunkPile.Number(holder) is not null || InventoryView(holder).Loot.FirstOrDefault(line => line.Entity == item) is not { StackKind: { } kind } line) return false;
        var move = new AreaQuantityMove(item, _inventoryHost!.Name(item, _hoverNames?.Catalog), holder, kind, line.Units);
        // One unit moves at once by the same stack path (0x4658E0 or 0x464830 with count 1, QW1, QW5).
        if (QuantityBar.Ask((int)kind.Type, line.Units, intoLootedHolder: false, takeAll: false) is { } count) MoveQuantity(move, count);
        else AskQuantity(move);
        return true;
    }

    /// <summary>A drop of one of the hero's ammunition stacks (a Drop button or the drop icon): asks, or drops its one unit (QW1).</summary>
    private void DropStackToGround(string item, string name)
    {
        var holder = _inventoryPanel!.Holder;
        if (InventoryView(holder).Pc.FirstOrDefault(line => line.Entity == item) is not { StackKind: { } kind } line)
        {
            _inventoryStatus = $"not dropped: {name}'s stack facts do not read here";
            _inventoryPanel.Render(InventoryView(holder), _inventoryStatus);
            return;
        }
        var move = new AreaQuantityMove(item, name, null, kind, line.Units);
        if (QuantityBar.Ask((int)kind.Type, line.Units, intoLootedHolder: false, takeAll: false) is { } count) MoveQuantity(move, count);
        else AskQuantity(move);
    }

    private void AskQuantity(AreaQuantityMove move)
    {
        // ARC-DEV-110 (MIG16 S3): the question is interface mode 13 (quantity picker), pushed over the inventory window, which keeps its
        // window (mode table); the leave step, from Accept, Cancel or the window's own close, drops the question.
        if (!OpenOverlayMode(InterfaceMode.QuantityPicker, _ => _quantityMove = null))
        {
            _inventoryStatus = $"not moved: the quantity window did not open for {move.Name}";
            _inventoryPanel!.Render(InventoryView(_inventoryPanel.Holder), _inventoryStatus);
            RefreshReadyStatus();
            return;
        }
        _quantityMove = move;
        _inventoryStatus = $"how many of {move.Name}? (0..{move.Units})";
        _inventoryPanel!.AskQuantity(move.Name, move.Units, _inventoryStatus);
        GD.Print($"ARC-UI-029 quantity-ask item={move.Item} kind={move.Kind.Type} units={move.Units} from={(move.Holder is null ? "hero" : ReferenceEquals(_containerLootSession, _areaSession) ? "container" : "dead-npc")}");
        RefreshReadyStatus();
    }

    /// <summary>The quantity window's Accept: the move with the count shown.</summary>
    private void AcceptQuantity(int count)
    {
        if (_quantityMove is not { } move) return;
        CloseTargetMode(InterfaceMode.QuantityPicker);
        if (!InventoryOpen || !InventoryActive) return;
        MoveQuantity(move, count);
    }

    private void CancelQuantity()
    {
        if (_quantityMove is not { } move) return;
        CloseTargetMode(InterfaceMode.QuantityPicker);
        if (!InventoryOpen || !InventoryActive) return;
        _inventoryStatus = $"nothing moved: {move.Name} stays";
        GD.Print($"ARC-UI-029 quantity-cancel item={move.Item}");
        _inventoryPanel!.Render(InventoryView(_inventoryPanel.Holder), _inventoryStatus);
        RefreshReadyStatus();
    }

    private void MoveQuantity(AreaQuantityMove move, int count)
    {
        var session = _areaSession!;
        var holder = _inventoryPanel!.Holder;
        InventoryMutationResult? result = null;
        string message;
        if (move.Holder is { } npc)
        {
            result = move.Kind.Type == SessionItemType.Gold ? session.TakeGoldFromNpc(session.Version, npc, count, move.Item)
                : session.TakeAmmoFromNpc(session.Version, npc, move.Item, count, move.Kind, _inventoryHost!.InsertCheck);
            message = result.Value.Disposition switch
            {
                GameSessionDisposition.Changed => $"took {count} of {move.Name}",
                GameSessionDisposition.NoChange => $"took nothing: 0 of {move.Name}",
                _ => $"not taken: {AreaInventoryHost.Refusal(result.Value.Failure, result.Value.Verdict)}",
            };
            if (result.Value.Disposition == GameSessionDisposition.Changed) PlayTakenItemSound(move.Item);
        }
        // The session's actor is the hero's tile only after a placement (walking does not move it).
        else if (!PlaceHeroForDrop()) message = "not dropped: the hero's tile is not known here";
        else
        {
            result = session.DropHeroAmmo(session.Version, move.Item, count, move.Kind);
            _lastGroundDrop = result;
            message = result.Value.Disposition switch
            {
                GameSessionDisposition.Changed => $"dropped {count} of {move.Name} on the ground",
                GameSessionDisposition.NoChange => $"dropped nothing: 0 of {move.Name}",
                _ => $"not dropped: {DropRefusal(result.Value.Failure)}",
            };
        }
        _lastQuantityMove = result;
        GD.Print($"ARC-UI-029 quantity-move item={move.Item} kind={move.Kind.Type} count={count} of={move.Units} from={(move.Holder is null ? "hero" : ReferenceEquals(_containerLootSession, _areaSession) ? "container" : "dead-npc")} " +
            $"disposition={result?.Disposition.ToString() ?? "-"} failure={result?.Failure.ToString() ?? "-"} new={result?.Item ?? "-"} gold={session.QueryGold()}");
        _inventoryStatus = message;
        _inventoryPanel.Render(InventoryView(holder), message);
        SyncAreaInventory(); RefreshReadyStatus();
    }

    /// <summary>The quantity window's keys (QW4): the keypad digits, Backspace and Delete on a key press; Enter accepts; Escape and I
    /// cancel (the original's Escape there was not traced). Every other key does nothing while it asks.</summary>
    private void HandleQuantityKey(InputEventKey key)
    {
        if (!key.Pressed) return;
        var routed = key.Keycode != Key.None ? key.Keycode : key.PhysicalKeycode;
        var digit = KeypadDigit(key.Keycode) ?? KeypadDigit(key.PhysicalKeycode);
        if (digit is { } value) _inventoryPanel!.PressQuantity(QuantityAction.Digit, value);
        else if (routed is Key.Backspace or Key.Delete) _inventoryPanel!.PressQuantity(QuantityAction.Erase);
        else if (key.Echo) return;
        else if (routed is Key.Enter or Key.KpEnter) _inventoryPanel!.AcceptQuantity();
        else if (routed == Key.Escape || (routed == Key.I && !CharacterKeySilenced(key, routed))) _inventoryPanel!.CancelQuantity();
    }

    private static int? KeypadDigit(Key key) => key is >= Key.Kp0 and <= Key.Kp9 ? (int)(key - Key.Kp0) : null;
}
