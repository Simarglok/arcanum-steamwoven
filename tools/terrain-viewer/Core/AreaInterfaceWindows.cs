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

/// <summary>
/// ARC-DEV-109 (MIG16 S2, TM13, GR35): the viewer's one interface mode stack (<see cref="InterfaceModeStack"/>) and the hosts of the
/// window modes on it (conversation 3, barter 4, world map 5, logbook 7, inventory 8, character sheet 9, loot 10, steal 11, schematic 14,
/// reading 15). A window opens by asking the stack for its mode; the stack's enter step shows the panel and its leave step hides it, so
/// the stack is the one answer to "which window is open" and "who owns the mouse". The show and hide actions are the panel's own open
/// and close only: the viewer's End routines keep their steps before and after (sounds, session closes, quarantine), because an
/// observer callback must not ask the stack for another change (the stack's lock makes that a no-op, SA37). Godot-free.
/// </summary>
public sealed class AreaInterfaceWindows : IInterfaceModeObserver
{
    private static readonly InterfaceMode[] WindowModes = [.. Enum.GetValues<InterfaceMode>().Where(InterfaceModes.IsWindow)];

    // The show and hide of each window that is open or being opened.
    private readonly Dictionary<InterfaceMode, (Action Show, Action Hide)> _windows = [];

    public AreaInterfaceWindows() => Stack = new InterfaceModeStack(this);

    /// <summary>The one stack. Read by the cursor and the modal tests; target modes (slice S3) are pushed on it.</summary>
    public InterfaceModeStack Stack { get; }

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

    /// <summary>
    /// Opens a window through the stack: the opener of <see cref="InterfaceModeStack.OpenWindow"/> pops and pushes, and its enter step
    /// runs <paramref name="show"/>. A mode already on the stack and a stack that holds another window change nothing (see
    /// <see cref="AreaWindowOpen"/>). The hero's refusals (SA52) stay with the viewer's own checks in this slice; they are not passed.
    /// </summary>
    /// <param name="show">Shows the panel (its Open); runs once, from the enter step, with the stack already holding the mode.</param>
    /// <param name="hide">Hides the panel (its CloseView); runs from the leave step that closes the window.</param>
    public AreaWindowOpen Open(InterfaceMode mode, Action show, Action hide)
    {
        ArgumentNullException.ThrowIfNull(show);
        ArgumentNullException.ThrowIfNull(hide);
        if (!InterfaceModes.IsWindow(mode)) return AreaWindowOpen.Refused;
        if (Stack.Contains(mode)) return AreaWindowOpen.AlreadyOpen;
        if (AnyOpen) return AreaWindowOpen.AnotherWindowOpen;
        _windows[mode] = (show, hide);
        var change = Stack.OpenWindow(mode);
        if (change.Applied && Stack.Contains(mode)) return AreaWindowOpen.Opened;
        _windows.Remove(mode);
        return AreaWindowOpen.Refused;
    }

    /// <summary>Closes a window through the stack (the opener's toggle of a window that is on it: pops until it is gone), and its leave
    /// step runs the hide given at the open. False when the mode is not on the stack or the stack refused (it was in a change).</summary>
    public bool Close(InterfaceMode mode)
    {
        if (!Stack.Contains(mode)) return false;
        var change = Stack.OpenWindow(mode);
        return change.Applied && !Stack.Contains(mode);
    }

    /// <summary>One leave or enter case of a change (<see cref="IInterfaceModeObserver"/>): the enter of a pushed window shows it; the
    /// leave that closes it hides it. A window re-entered by a pop (the mode below an overlay) is still shown; a dialog is ended by a pop
    /// and only suspended by a push (mode table, row 3), and the viewer pushes nothing over a dialog.</summary>
    public void Step(InterfaceModeStep step)
    {
        if (!InterfaceModes.IsWindow(step.Mode)) return;
        if (step.Leave)
        {
            var closes = step.Mode == InterfaceMode.Dialog ? step.Popped : step.ClosesWindow;
            if (closes && _windows.Remove(step.Mode, out var window)) window.Hide();
        }
        else if (!step.Popped && _windows.TryGetValue(step.Mode, out var window)) window.Show();
    }
}
