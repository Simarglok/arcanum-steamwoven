using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-DEV-109 (MIG16 S2, TM13, GR35; docs/architecture/interface-modes.md): the viewer owns one interface mode stack, as the original
// does (one stack, one change function). The windows that are modes on it (conversation 3, barter 4, world map 5, logbook 7, inventory
// 8, character sheet 9, loot 10 and steal 11 (the inventory window on a body, a container or a pocket), schematic 14, reading 15) open
// and close through it: the stack's enter step shows the panel and its leave step hides it, and "is this window open" is a question to
// the stack. The cursor (SA41) and the window part of WorldModal read it. The panels outside the stack (the palette, the main menu, the
// save list, the bug report, the hero-death cover) keep their own flags; the target modes (S3) and the input gating (S4) are not here.
public partial class TerrainViewerApp
{
    private readonly AreaInterfaceWindows _interfaceWindows = new();

    /// <summary>True when the window mode is on the stack.</summary>
    private bool WindowOpen(InterfaceMode mode) => _interfaceWindows.IsOpen(mode);

    /// <summary>True when any window mode is on the stack: the window part of <see cref="WorldModal"/>.</summary>
    private bool AnyWindowOpen => _interfaceWindows.AnyOpen;

    /// <summary>The modes on the stack from the base up, for the self-test and the log.</summary>
    internal IReadOnlyList<InterfaceMode> InterfaceModesForSelfTest => _interfaceWindows.Stack.Modes;

    /// <summary>
    /// The first test of an opener: false when the window is on the stack (the opener's old guard, silent) or when another window mode is
    /// (logged: every viewer opener closes the other window first or waits, and a window-to-window move by the stack is slice S4's), so
    /// an opener refuses before it changes anything.
    /// </summary>
    private bool WindowCanOpen(InterfaceMode mode)
    {
        if (WindowOpen(mode)) return false;
        if (!AnyWindowOpen) return true;
        GD.Print($"ARC-DEV-109 {mode} not opened: another window is open (stack {StackText()})");
        return false;
    }

    /// <summary>Opens the window through the stack: the enter step runs <paramref name="show"/> (the panel's Open) and the leave step
    /// that closes the window runs <paramref name="hide"/> (its CloseView). False (and logged) when the stack did not take it.</summary>
    private bool OpenWindowMode(InterfaceMode mode, Action show, Action hide)
    {
        var opened = _interfaceWindows.Open(mode, show, hide);
        if (opened != AreaWindowOpen.Opened)
        {
            GD.Print($"ARC-DEV-109 {mode} not opened: {opened} (stack {StackText()})");
            return false;
        }
        GD.Print($"ARC-DEV-109 interface mode {mode} opened (stack {StackText()})");
        return true;
    }

    /// <summary>Closes the window through the stack: the leave step runs the hide given at the open.</summary>
    private void CloseWindowMode(InterfaceMode mode)
    {
        if (!_interfaceWindows.Close(mode)) GD.Print($"ARC-DEV-109 {mode} not closed (stack {StackText()})");
        else GD.Print($"ARC-DEV-109 interface mode {mode} closed (stack {StackText()})");
    }

    private string StackText() => string.Join(">", _interfaceWindows.Stack.Modes);
}
