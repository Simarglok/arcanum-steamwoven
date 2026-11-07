using Arcanum.World;

namespace Arcanum.TerrainViewer.Core;

/// <summary>ARC-DEV-109 (MIG16 S2): what became of a request to open a window mode on the viewer's stack.</summary>
public enum AreaWindowOpen
{
    /// <summary>The mode is on the stack and its window was shown.</summary>
    Opened,
    /// <summary>The mode is on the stack already; nothing changed. The opener's toggle is the viewer's own End routine, not this call.</summary>
    AlreadyOpen,
    /// <summary>Another window mode is on the stack; nothing changed. The viewer's openers close the other window first (the bar
    /// button, the reading from the inventory); a window-to-window move by the stack is slice S4's.</summary>
    AnotherWindowOpen,
    /// <summary>The mode is not a window mode, or the stack refused the change (the lock, the depth limit); nothing changed.</summary>
    Refused,
}

/// <summary>ARC-DEV-110 (MIG16 S3): what became of a request to open a target or overlay mode on the viewer's stack.</summary>
public enum AreaTargetOpen
{
    /// <summary>The mode is on the stack; its leave step will run the host given at the open.</summary>
    Opened,
    /// <summary>The mode is on the stack already; nothing changed.</summary>
    AlreadyOpen,
    /// <summary>A window mode is on the stack and the target would replace it (it has no inventory bit); nothing changed. The viewer's
    /// openers close the window through its End routine first, as they do for another window.</summary>
    WindowOpen,
    /// <summary>The mode is not a target or overlay mode the viewer hosts, or the stack refused the change (the lock, the depth limit); nothing changed.</summary>
    Refused,
}

/// <summary>
/// ARC-DEV-109 (MIG16 S2, TM13, GR35): the viewer's one interface mode stack (<see cref="InterfaceModeStack"/>) and the hosts of the
/// window modes on it (conversation 3, barter 4, world map 5, logbook 7, inventory 8, character sheet 9, loot 10, steal 11, schematic 14,
/// reading 15). A window opens by asking the stack for its mode; the stack's enter step shows the panel and its leave step hides it, so
/// the stack is the one answer to "which window is open" and "who owns the mouse". The show and hide actions are the panel's own open
/// and close only: the viewer's End routines keep their steps before and after (sounds, session closes, quarantine), because an
/// observer callback must not ask the stack for another change (the stack's lock makes that a no-op, SA37).
/// ARC-DEV-110 (MIG16 S3): the target modes (spell 1, skill 2, item use 16) and the overlays (quantity picker 13, follower order 18) are
/// on the same stack. Each is opened with the host that closes its cursor or question, and that host runs from the leave step when the
/// mode is popped, whatever popped it (the viewer's own cancel, a window that replaces it, a window closing underneath it): the stack
/// is the one answer to "is a target open", and the viewer's state for it (the spell, the lockpicks, the question) is cleared by the
/// leave step only. A held item is put back by the leave steps that ask for it (<see cref="PutHeldItemBack"/>). Godot-free.
/// </summary>
public sealed class AreaInterfaceWindows : IInterfaceModeObserver
{
    private static readonly InterfaceMode[] WindowModes = [.. Enum.GetValues<InterfaceMode>().Where(InterfaceModes.IsWindow)];

    // The show and hide of each window that is open or being opened.
    private readonly Dictionary<InterfaceMode, (Action Show, Action Hide)> _windows = [];
    // ARC-DEV-110: the leave host of each target or overlay mode that is on the stack.
    private readonly Dictionary<InterfaceMode, Action<InterfaceModeStep>> _targets = [];

    public AreaInterfaceWindows() => Stack = new InterfaceModeStack(this);

    /// <summary>The one stack. Read by the cursor and the modal tests; windows and target modes are opened on it.</summary>
    public InterfaceModeStack Stack { get; }

    /// <summary>ARC-DEV-110: runs from a leave step that puts a held item back (<see cref="InterfaceModeStep.PutsHeldItemBack"/>,
    /// 0x575770). Idempotent in the viewer: the item may already be back.</summary>
    public Action? PutHeldItemBack { get; set; }

    /// <summary>True when the mode is on the stack.</summary>
    public bool IsOpen(InterfaceMode mode) => Stack.Contains(mode);

    /// <summary>True when any window mode is on the stack (the window part of the viewer's modal test).</summary>
    public bool AnyOpen
    {
        get
        {
            foreach (var mode in WindowModes) if (Stack.Contains(mode)) return true;
            return false;
        }
    }

    /// <summary>ARC-DEV-111 (MIG16 S4, SA44): the game view (clicks, hover, the right release) acts only in modes 0, 1, 2, 3, 16 and 18, and
    /// does nothing while another participant has the turn in a round; in every other mode the window owns the mouse.
    /// <paramref name="dialogOwnsMouse"/>: a host whose dialog panel keeps the mouse although the original's game view still acts in
    /// mode 3 (a ground click walks the hero, read at 0x5517A0). The viewer takes the original's table; the dialog's pause (the closed
    /// world gate until MIG12 stops pausing for windows, GR22) is what keeps the click out of the world there for now.</summary>
    public bool GameViewActs(bool anotherHasTheTurn, bool dialogOwnsMouse = false) =>
        !(dialogOwnsMouse && Stack.Current == InterfaceMode.Dialog) && Stack.GameViewActs(anotherHasTheTurn);

    /// <summary>ARC-DEV-111 (SA44): F1–F6 give the follower orders at the mouse tile in modes 0, 1, 2 and 18 only, and not on another
    /// participant's turn.</summary>
    public bool FollowerOrderKeys(bool anotherHasTheTurn) => InterfaceModes.FollowerOrderKeys(Stack.Current, anotherHasTheTurn);

    /// <summary>ARC-DEV-111 (SA44): the number row picks a hotkey slot in every mode (the viewer's text fields and its rule-mode choice
    /// decide whether a window lets the key through).</summary>
    public bool HotkeyKeys => true;

    /// <summary>ARC-DEV-111 (SA44), ARC-DEV-200: arrow and edge scrolling run only in modes 0, 1, 2, 3, 5, 16 and 18, and go on while another
    /// participant has the turn.</summary>
    public bool ScrollsView => InterfaceModes.ScrollsView(Stack.Current);

    /// <summary>ARC-DEV-200: the scroll moves the isometric game view in this mode (the world map's scroll moves the map instead).</summary>
    public bool ScrollsGameView => InterfaceModes.ScrollsGameView(Stack.Current);

    /// <summary>
    /// Opens a window through the stack: the opener of <see cref="InterfaceModeStack.OpenWindow"/> pops and pushes, and its enter step
    /// runs <paramref name="show"/>. A mode already on the stack and a stack that holds another window change nothing (see
    /// <see cref="AreaWindowOpen"/>). ARC-DEV-111 (SA52): the hero's state is the opener's own refusal, as the original's openers have it
    /// (<see cref="InterfaceModes.OpenerRefuses"/>: a dead hero is refused by the logbook, sheet, reading, schematic, world map, dialog and
    /// inventory, a dead or unconscious one by the world map and the dialog); the viewer's own earlier checks stay as they were.
    /// </summary>
    /// <param name="show">Shows the panel (its Open); runs once, from the enter step, with the stack already holding the mode.</param>
    /// <param name="hide">Hides the panel (its CloseView); runs from the leave step that closes the window.</param>
    public AreaWindowOpen Open(InterfaceMode mode, Action show, Action hide, bool heroDead = false, bool heroUnconscious = false)
    {
        ArgumentNullException.ThrowIfNull(show);
        ArgumentNullException.ThrowIfNull(hide);
        if (!InterfaceModes.IsWindow(mode)) return AreaWindowOpen.Refused;
        if (Stack.Contains(mode)) return AreaWindowOpen.AlreadyOpen;
        if (AnyOpen) return AreaWindowOpen.AnotherWindowOpen;
        _windows[mode] = (show, hide);
        var change = Stack.OpenWindow(mode, heroDead, heroUnconscious);
        if (change.Applied && Stack.Contains(mode)) return AreaWindowOpen.Opened;
        _windows.Remove(mode);
        return AreaWindowOpen.Refused;
    }

    /// <summary>Closes a window through the stack (the opener's toggle of a window that is on it: pops until it is gone), and its leave
    /// step runs the hide given at the open. ARC-DEV-110: a target or overlay mode closes by popping until it is gone, and its leave
    /// step runs the host given at the open (a spell or skill target takes the mode below it with it, SA38: an inventory window below
    /// is hidden by its leave step without its End routine; the viewer opens no target over a window yet). False when the mode is not
    /// on the stack or the stack refused (it was in a change).</summary>
    public bool Close(InterfaceMode mode)
    {
        if (!Stack.Contains(mode)) return false;
        if (InterfaceModes.IsWindow(mode)) return Stack.OpenWindow(mode) is { Applied: true } && !Stack.Contains(mode);
        while (Stack.Contains(mode))
        {
            if (!Stack.Pop().Applied) return false;
        }
        return true;
    }

    /// <summary>True when the mode on top is one of the inventory windows (own list 8, barter 4, loot 10, steal 11, kinds 19 and 20) and its
    /// leave case keeps its window going to <paramref name="mode"/> (mode table rows 4, 8, 10, 11, 19, 20): the windows a target with the
    /// inventory bit or the quantity picker sits over. A push that would close the window below would hide its panel without its End
    /// routine (the viewer's End routines run only outside the stack's callbacks), so it is refused.</summary>
    private bool InventoryOnTop(InterfaceMode mode) => Stack.Current is InterfaceMode.Inventory or InterfaceMode.Barter or InterfaceMode.Loot
        or InterfaceMode.Steal or InterfaceMode.InventoryKind5 or InterfaceMode.InventoryKind6
        && !InterfaceModes.LeaveClosesWindow(Stack.Current, mode);

    /// <summary>
    /// ARC-DEV-110 (MIG16 S3): opens a target mode (spell 1, skill 2, item use 16) through the stack's target opener (SA40): with
    /// <paramref name="overInventory"/> (the target mask has the inventory bit 0x2000000) it is pushed over the inventory window on top
    /// (<see cref="AreaTargetOpen.Refused"/> when none is), which stays; without it it replaces the top mode, so a target already open (any other target mode) is left first, and its host
    /// runs. A window mode on the stack with no inventory bit is <see cref="AreaTargetOpen.WindowOpen"/>: the stack's replace would
    /// close it without its End routine, so the viewer closes it first. The hero's refusals (SA52) stay with the viewer's own checks.
    /// </summary>
    /// <param name="onLeave">Closes the cursor (clears the viewer's state for the mode); runs once, from the leave step that pops the mode,
    /// with the stack already in its final state. Optional for a mode whose state is the stack itself.</param>
    public AreaTargetOpen OpenTarget(InterfaceMode mode, bool overInventory, Action<InterfaceModeStep>? onLeave = null)
    {
        if (!InterfaceModes.IsTarget(mode)) return AreaTargetOpen.Refused;
        if (Stack.Contains(mode)) return AreaTargetOpen.AlreadyOpen;
        if (!overInventory && AnyOpen) return AreaTargetOpen.WindowOpen;
        if (overInventory && !InventoryOnTop(mode)) return AreaTargetOpen.Refused;
        _targets[mode] = onLeave ?? (_ => { });
        if (Stack.OpenTarget(mode, overInventory) is { Applied: true } && Stack.Contains(mode)) return AreaTargetOpen.Opened;
        _targets.Remove(mode);
        return AreaTargetOpen.Refused;
    }

    /// <summary>
    /// ARC-DEV-110 (MIG16 S3): opens an overlay mode, pushed over the top without a pop (SA40): the quantity picker 13 over an inventory
    /// window (which keeps its window, mode table rows 4, 8, 10, 11) or the follower order 18. <paramref name="onLeave"/> as for
    /// <see cref="OpenTarget"/>.
    /// </summary>
    public AreaTargetOpen OpenOverlay(InterfaceMode mode, Action<InterfaceModeStep>? onLeave = null)
    {
        if (mode is not (InterfaceMode.QuantityPicker or InterfaceMode.FollowerOrder)) return AreaTargetOpen.Refused;
        if (Stack.Contains(mode)) return AreaTargetOpen.AlreadyOpen;
        if (mode == InterfaceMode.QuantityPicker && !InventoryOnTop(mode)) return AreaTargetOpen.Refused;
        // A window under the follower order would be closed by its leave case, and a spell or skill target below would pop the overlay at
        // once (SA38: its leave case asks for another pop, and this one would be the pop): nothing changes.
        if (mode == InterfaceMode.FollowerOrder && (AnyOpen && InterfaceModes.LeaveClosesWindow(Stack.Current, mode) || InterfaceModes.LeavePopsAgain(Stack.Current)))
            return AreaTargetOpen.Refused;
        _targets[mode] = onLeave ?? (_ => { });
        if (Stack.OpenOverlay(mode) is { Applied: true } && Stack.Contains(mode)) return AreaTargetOpen.Opened;
        _targets.Remove(mode);
        return AreaTargetOpen.Refused;
    }

    /// <summary>One leave or enter case of a change (<see cref="IInterfaceModeObserver"/>): the enter of a pushed window shows it; the
    /// leave that closes it hides it. A window re-entered by a pop (the mode below an overlay) is still shown; a dialog is ended by a pop
    /// and only suspended by a push (mode table, row 3), and the viewer pushes nothing over a dialog.</summary>
    public void Step(InterfaceModeStep step)
    {
        // ARC-DEV-110: the leave case that asks for it puts a held item back (the original does it after the case's own work).
        try
        {
            if (!InterfaceModes.IsWindow(step.Mode)) { StepTarget(step); return; }
            if (step.Leave)
            {
                var closes = step.Mode == InterfaceMode.Dialog ? step.Popped : step.ClosesWindow;
                if (closes && _windows.Remove(step.Mode, out var window)) window.Hide();
            }
            else if (!step.Popped && _windows.TryGetValue(step.Mode, out var window)) window.Show();
        }
        finally
        {
            if (step.PutsHeldItemBack) PutHeldItemBack?.Invoke();
        }
    }

    // A target or overlay mode that is popped runs its host (closes its cursor or question). The viewer pushes nothing over a target
    // mode or an overlay, so a leave that is not a pop is ignored here: the mode stays on the stack with its host.
    private void StepTarget(InterfaceModeStep step)
    {
        if (!step.Leave || !step.Popped || !_targets.Remove(step.Mode, out var leave)) return;
        leave(step);
    }
}
