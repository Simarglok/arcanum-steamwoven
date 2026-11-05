using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-DEV-109 (MIG16 S2, TM13, GR35; docs/architecture/interface-modes.md): the viewer owns one interface mode stack, as the original
// does (one stack, one change function). The windows that are modes on it (conversation 3, barter 4, world map 5, logbook 7, inventory
// 8, character sheet 9, loot 10 and steal 11 (the inventory window on a body, a container or a pocket), schematic 14, reading 15) open
// and close through it: the stack's enter step shows the panel and its leave step hides it, and "is this window open" is a question to
// the stack. The cursor (SA41) and the window part of WorldModal read it. The panels outside the stack (the palette, the main menu, the
// save list, the bug report, the hero-death cover) keep their own flags; the input gating and Esc (S4) are not here.
// ARC-DEV-110 (MIG16 S3): the target modes (spell 1, skill 2 = the armed pick pocket, item use 16 = armed lockpicks) and the overlays
// (quantity picker 13, follower order 18) are on the same stack. The viewer's state for each (the targeting spell, the armed lockpicks,
// the pick-pocket flag, the question, the pending order) is set after the stack took the mode and is cleared by the mode's leave step
// only, so "is a target open" is one question to the stack and a window that replaces a target closes its cursor with the pop.
public partial class TerrainViewerApp
{
    private readonly AreaInterfaceWindows _interfaceWindows;

    public TerrainViewerApp() => _interfaceWindows = new() { PutHeldItemBack = () => PutBackGameCursorItem("the interface mode changed") };

    /// <summary>True when the window mode is on the stack.</summary>
    private bool WindowOpen(InterfaceMode mode) => _interfaceWindows.IsOpen(mode);

    /// <summary>True when any window mode is on the stack: the window part of <see cref="WorldModal"/>.</summary>
    private bool AnyWindowOpen => _interfaceWindows.AnyOpen;

    /// <summary>Self-test: each window panel is shown exactly when its mode is on the stack (the stack opens and closes the panels, and no
    /// other code does, so a difference is a window that the stack and its panel disagree on).</summary>
    internal bool WindowPanelsMatchStack =>
        (_conversationPanel?.IsOpen ?? false) == WindowOpen(InterfaceMode.Dialog)
        && (_barterPanel?.IsOpen ?? false) == WindowOpen(InterfaceMode.Barter)
        && (_worldMapPanel?.IsOpen ?? false) == WindowOpen(InterfaceMode.WorldMap)
        && (_logbookPanel?.IsOpen ?? false) == WindowOpen(InterfaceMode.Logbook)
        && (_inventoryPanel?.IsOpen ?? false) == InventoryOpen
        && (_sheetPanel?.IsOpen ?? false) == WindowOpen(InterfaceMode.Character)
        && (_schematicsPanel?.IsOpen ?? false) == WindowOpen(InterfaceMode.Schematic)
        && (_readingPanel?.IsOpen ?? false) == WindowOpen(InterfaceMode.Book);

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
        // ARC-DEV-110: a window replaces the top mode (SA40), so an open target mode leaves first. The viewer closes each cursor with its
        // reason here, ahead of the stack's own replace, which would pop it silently.
        CancelTargetModes("a window opened");
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

    /// <summary>ARC-DEV-110: the armed pick pocket is the skill target mode (2) on the stack: the stack is the flag.</summary>
    private bool PickPocketArmed => TargetModeOpen(InterfaceMode.SkillTarget);

    /// <summary>ARC-DEV-110: true when the target or overlay mode is on the stack.</summary>
    private bool TargetModeOpen(InterfaceMode mode) => _interfaceWindows.IsOpen(mode);

    /// <summary>ARC-DEV-110: leaves every open target mode and the follower order (each with its own notice and log); nothing when none is
    /// open. A window opens over the base, as the original's openers pop the top first.</summary>
    private void CancelTargetModes(string why)
    {
        CancelSpellTargeting(why);
        CancelArmedLockpicks(why);
        // Only the armed skill: a walk to a victim already under way (its press left the mode) keeps going, as before.
        if (PickPocketArmed) CancelPickPocket(why);
        CancelPendingOrder(why);
    }

    /// <summary>ARC-DEV-110: opens a target mode (1, 2 or 16) through the stack, which replaces the top (a target already open is left
    /// first and its leave step clears its state). <paramref name="onLeave"/> clears the viewer's state for the mode and runs from the leave
    /// step. False (and logged) when a window is open or the stack did not take it; nothing changed then.</summary>
    private bool OpenTargetMode(InterfaceMode mode, Action<InterfaceModeStep>? onLeave = null)
    {
        var opened = _interfaceWindows.OpenTarget(mode, overInventory: false, onLeave);
        // A target that is already open stays open (the palette's Pick pocket pressed twice, lockpicks chosen again).
        if (opened == AreaTargetOpen.AlreadyOpen) return true;
        if (opened != AreaTargetOpen.Opened)
        {
            GD.Print($"ARC-DEV-110 {mode} not opened: {opened} (stack {StackText()})");
            return false;
        }
        GD.Print($"ARC-DEV-110 interface mode {mode} opened (stack {StackText()})");
        return true;
    }

    /// <summary>ARC-DEV-110: opens an overlay mode (13 or 18) through the stack, pushed over the top. As <see cref="OpenTargetMode"/>.</summary>
    private bool OpenOverlayMode(InterfaceMode mode, Action<InterfaceModeStep>? onLeave = null)
    {
        var opened = _interfaceWindows.OpenOverlay(mode, onLeave);
        if (opened == AreaTargetOpen.AlreadyOpen) return true;
        if (opened != AreaTargetOpen.Opened)
        {
            GD.Print($"ARC-DEV-110 {mode} not opened: {opened} (stack {StackText()})");
            return false;
        }
        GD.Print($"ARC-DEV-110 interface mode {mode} opened (stack {StackText()})");
        return true;
    }

    /// <summary>ARC-DEV-110: closes a target or overlay mode through the stack; its leave step clears the viewer's state. A spell or skill
    /// target takes the mode below it with it (SA38).</summary>
    private void CloseTargetMode(InterfaceMode mode)
    {
        if (!_interfaceWindows.Close(mode)) GD.Print($"ARC-DEV-110 {mode} not closed (stack {StackText()})");
        else GD.Print($"ARC-DEV-110 interface mode {mode} closed (stack {StackText()})");
    }

    /// <summary>Self-test: each target state of the viewer is set exactly when its mode is on the stack (the stack opens and closes them,
    /// and no other code does, so a difference is a cursor the stack and the viewer disagree on).</summary>
    internal bool TargetsMatchStack =>
        (_spellTargeting is not null) == TargetModeOpen(InterfaceMode.SpellTarget)
        && (_lockpickArmed is not null) == TargetModeOpen(InterfaceMode.ItemTarget)
        && (_quantityMove is not null) == TargetModeOpen(InterfaceMode.QuantityPicker)
        && (_pendingOrder is not null) == TargetModeOpen(InterfaceMode.FollowerOrder);

    private string StackText() => string.Join(">", _interfaceWindows.Stack.Modes);
}
