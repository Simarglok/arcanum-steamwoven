using Arcanum.Input;
using Arcanum.Input.Godot;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-DEV-109 (MIG16 S2, TM13, GR35; docs/architecture/interface-modes.md): the viewer owns one interface mode stack, as the original
// does (one stack, one change function). The windows that are modes on it (conversation 3, barter 4, world map 5, logbook 7, inventory
// 8, character sheet 9, loot 10 and steal 11 (the inventory window on a body, a container or a pocket), schematic 14, reading 15) open
// and close through it: the stack's enter step shows the panel and its leave step hides it, and "is this window open" is a question to
// the stack. The cursor (SA41) and the window part of WorldModal read it. The panels outside the stack (the palette, the main menu, the
// save list, the bug report, the hero-death cover) keep their own flags; the input gating and Esc are ARC-DEV-111 (S4), below.
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
        && (_readingPanel?.IsOpen ?? false) == WindowOpen(InterfaceMode.Book)
        && (_sleepPanel?.IsOpen ?? false) == WindowOpen(InterfaceMode.Sleep);

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
        var (dead, unconscious) = HeroDownState();
        // ARC-DEV-367: the call, the panel's own open included.
        using var span = PerfProbe.Enabled ? PerfProbe.Span("window.open." + mode) : default;
        var opened = _interfaceWindows.Open(mode, show, hide, dead, unconscious);
        if (opened != AreaWindowOpen.Opened)
        {
            GD.Print($"ARC-DEV-109 {mode} not opened: {opened} (stack {StackText()})");
            RecordEvidence(EvidenceCommand.OpenWindow, EvidenceOutcome.Refused);
            return false;
        }
        GD.Print($"ARC-DEV-109 interface mode {mode} opened (stack {StackText()})");
        RecordEvidence(EvidenceCommand.OpenWindow);
        return true;
    }

    /// <summary>ARC-UI-1125: a panel outside the stack that owns the input (the palette, the save list, the main menu, the death cover, a movie, the bug
    /// report). The window openers of the original know none of them (they are no modes), so a programmatic open waits for these as it always did; it
    /// does not wait for a window.</summary>
    private bool ProgrammaticOpenBlocked => PaletteOpen || SaveListOpen || MainMenuOpen || HeroDeathCovering || MovieCovering || BugReportOpen || WorldMapTravelling;

    /// <summary>ARC-UI-1125: a journey runs on the open world map. Closing the map would land the hero and start a map transfer (the player's cancel), so
    /// a programmatic open waits for the journey as it did.</summary>
    private bool WorldMapTravelling => WorldMapOpen && _worldMap is { Travelling: true };

    /// <summary>
    /// ARC-UI-1125 (MIG16 tail; the window openers 0x56BC00, 0x56D130 and 0x567460 with 0x553320, read in the executable, SA40): an opener pops the
    /// stack and pushes its own mode whoever calls it, so a script's reading or schematic window, the dialog a script, the hero's talk goal or an
    /// NPC's heartbeat asks for opens at once over an open window and the window leaves through its leave case, as it does for the player's key.
    /// The viewer ran the window's End routine (its sounds, its session close) and then opens the new one. Returns true when the way is clear: no
    /// window is open or the open ones left. False (and logged) when the opener refuses for the hero's state (SA52: the opener returns before it pops,
    /// so the window stays), when a dialog is open (the dialog opener refuses at once while the PC's mode is 3, 0x567460; the other openers wait for
    /// a dialog's end here as they did, a dialog ends by the player's choice) or when a window would not leave.
    /// </summary>
    private bool ClearWindowsForProgrammaticOpen(InterfaceMode mode, string who)
    {
        if (!AnyWindowOpen && !FateOpen) return true;
        if (ConversationOpen) return false;
        if (WindowRefusedForHero(mode))
        {
            GD.Print($"ARC-UI-1125 {who} refused for the hero; the open window stays (stack {StackText()})");
            return false;
        }
        GD.Print($"ARC-UI-1125 {who} replaces the open window (stack {StackText()})");
        CloseOtherInterfaceWindows();
        return !AnyWindowOpen;
    }

    /// <summary>Closes the window through the stack: the leave step runs the hide given at the open.</summary>
    private void CloseWindowMode(InterfaceMode mode)
    {
        if (!_interfaceWindows.Close(mode)) GD.Print($"ARC-DEV-109 {mode} not closed (stack {StackText()})");
        else
        {
            GD.Print($"ARC-DEV-109 interface mode {mode} closed (stack {StackText()})");
            RecordEvidence(EvidenceCommand.CloseWindow);
        }
    }

    /// <summary>ARC-DEV-110: the armed pick pocket is the skill target mode (2) on the stack: the stack is the flag.</summary>
    private bool PickPocketArmed => TargetModeOpen(InterfaceMode.SkillTarget) && !_disarmArmed;

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
        CancelDisarm(why);
        CancelPendingOrder(why);
    }

    /// <summary>ARC-DEV-110: opens a target mode (1, 2 or 16) through the stack, which replaces the top (a target already open is left
    /// first and its leave step clears its state). <paramref name="onLeave"/> clears the viewer's state for the mode and runs from the leave
    /// step. False (and logged) when a window is open or the stack did not take it; nothing changed then.</summary>
    private bool OpenTargetMode(InterfaceMode mode, Action<InterfaceModeStep>? onLeave = null, bool overInventory = false)
    {
        var opened = _interfaceWindows.OpenTarget(mode, overInventory, onLeave);
        // A target that is already open stays open (the palette's Pick pocket pressed twice, lockpicks chosen again).
        if (opened == AreaTargetOpen.AlreadyOpen) return true;
        if (opened != AreaTargetOpen.Opened)
        {
            GD.Print($"ARC-DEV-110 {mode} not opened: {opened} (stack {StackText()})");
            RecordEvidence(EvidenceCommand.Target, EvidenceOutcome.Refused);
            return false;
        }
        GD.Print($"ARC-DEV-110 interface mode {mode} opened (stack {StackText()})");
        RecordEvidence(EvidenceCommand.Target);
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
            RecordEvidence(EvidenceCommand.Target, EvidenceOutcome.Refused);
            return false;
        }
        GD.Print($"ARC-DEV-110 interface mode {mode} opened (stack {StackText()})");
        RecordEvidence(EvidenceCommand.Target);
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
        && ItemTargetOpen == TargetModeOpen(InterfaceMode.ItemTarget)
        && (_quantityMove is not null) == TargetModeOpen(InterfaceMode.QuantityPicker)
        && (_pendingOrder is not null) == TargetModeOpen(InterfaceMode.FollowerOrder);

    /// <summary>ARC-DEV-111 (SA44): a round runs and the participant whose turn it is is not the local hero. The original's game view does
    /// nothing then, in every mode.</summary>
    private bool AnotherHasTheTurn => InTurnBasedRound && _areaSession?.QueryCombatRound() is { } round && !round.Actor.IsHero;

    /// <summary>ARC-DEV-111: the panels outside the stack (the palette, the save list, the main menu, the hero-death cover, the bug report);
    /// each owns the input while it is open.</summary>
    private bool PanelOutsideStackOpen => PaletteOpen || SaveListOpen || MainMenuOpen || HeroDeathCovering || MovieCovering || BugReportOpen;

    /// <summary>ARC-DEV-200 (SA44): the stack's mode scrolls the isometric game view (0, 1, 2, 3, 16 and 18; the world map's scroll moves
    /// the map) and no panel outside the stack owns the input. The main menu runs its own loop in the original, which never scrolls.</summary>
    private bool WorldScrollsGameView => !PanelOutsideStackOpen && !WorldUiFocus && _interfaceWindows.ScrollsGameView;

    /// <summary>ARC-DEV-111 (SA44): the mode part of the game view's guard: no panel outside the stack is open and the stack's mode lets the
    /// game view act (0, 1, 2, 3, 16 and 18). Another participant's turn is the click route's own
    /// test, which keeps its notice.</summary>
    private bool GameViewModeOpen => !PanelOutsideStackOpen && _interfaceWindows.GameViewActs(anotherHasTheTurn: false);

    /// <summary>ARC-DEV-111 (SA52): true when the opener of this window refuses for the hero's state; the original's opener refuses before it
    /// pops, so the open window stays.</summary>
    private bool WindowRefusedForHero(InterfaceMode mode) { var (dead, unconscious) = HeroDownState(); return InterfaceModes.OpenerRefuses(mode, dead, unconscious); }

    /// <summary>ARC-DEV-111 (SA52): the hero's state the window openers refuse for.</summary>
    private (bool Dead, bool Unconscious) HeroDownState() => _areaSession?.QueryHeroLife()?.Status is { } life ? (life.IsDead, life.IsUnconscious) : (false, false);

    /// <summary>ARC-MAGIC-1128: a spell's or an item's target mode stands over the player's own inventory window (the spell's target area has the inventory bit,
    /// IA4, SU3): the window is the cursor's picking surface, so it does not drop the target.</summary>
    private bool TargetOverInventory => (TargetModeOpen(InterfaceMode.SpellTarget) || _itemAiming is not null && TargetModeOpen(InterfaceMode.ItemTarget)) && WindowOpen(InterfaceMode.Inventory);

    /// <summary>ARC-DEV-111 (SA51, 0x541180): what drops an open target mode. A window that opens pops it (SA40) and so do the palette, the
    /// death cover and the bug report; the in-play menu does not (it is no mode: it changes nothing on the stack, and Continue returns to the
    /// same cursor).</summary>
    private bool TargetsDroppedByModal => PaletteOpen || AnyWindowOpen && !TargetOverInventory || HeroDeathCovering || MovieCovering || BugReportOpen;

    /// <summary>
    /// ARC-DEV-111 (MIG16 S4; SA45, SA40, SA44; main loop 0x401560 and bar handler 0x54C8E0, read in the executable): with a window open,
    /// Escape, the other windows' keys and the number row are global keys that the window itself does not handle. Escape opens the menu
    /// (<see cref="RouteGameMenuKey"/>: the inventory family closes under it, the other windows and the targets stay). C, I, L, T and W
    /// toggle their own window (the window's own handler) or replace the window that is open (its opener pops and pushes; the dialog's
    /// modal hook refuses every one of them). The number row picks a hotkey slot in every mode but the dialog; an item held over the
    /// inventory is dropped on the entry first. True when the key was taken.
    /// </summary>
    private bool HandleGlobalKeyOverInterface(InputEventKey key)
    {
        if (!AnyWindowOpen || PanelOutsideStackOpen) return false;
        var routed = key.Keycode != Key.None ? key.Keycode : key.PhysicalKeycode;
        var world = GodotInputAdapter.ToWorldKey(routed);
        var decision = new WorldKeyDecision(world, key.Pressed, key.Echo, WorldKeyRole.OriginalReserved, false, false, null);
        if (routed == Key.Escape)
        {
            RouteGameMenuKey(decision);
            return true;
        }
        // ARC-DEV-323 (SL12; main loop 0x401834): no window takes F7 (the inventory handler answers 0 to a key), so the quick save goes on over the steal window.
        if (routed == Key.F7 && !ConversationOpen && InventoryOpen && InterfaceModes.QuickSaveClosesWindow(_interfaceWindows.Stack.Current))
        {
            RouteQuickSaveOverWindow(decision);
            return true;
        }
        if (ConversationOpen || !key.Pressed || key.Echo) return false;
        var modifiers = KeyModifiers(key);
        if (OriginalWorldKeyboard.CharacterKeySilenced(world, modifiers)) return false;
        if (routed is >= Key.Key0 and <= Key.Key9)
        {
            if (_inventoryPanel?.Asking == true || DropHeldOnHotkey(key)) return false;
            return RouteHotkeyKey(decision, modifiers);
        }
        // ARC-WORLD-1160: S asks the sleep opener for its own decision (0x57B180): it leaves the open window as it is when it refuses and replaces it when it opens.
        if (routed == Key.S && !SleepOpen)
        {
            if (!SleepActive || _inventoryPanel?.Asking == true) return false;
            if (!_worldFocused || WorldUiFocus) return true;
            OpenSleep();
            return true;
        }
        var (own, active, open) = routed switch
        {
            Key.C => (SheetOpen, SheetActive, (Action)OpenSheet),
            Key.I => (InventoryOpen, InventoryActive, (Action)(() => OpenInventory())),
            Key.L => (LogbookOpen, LogbookActive, (Action)OpenLogbook),
            Key.T => (SchematicsOpen, SchematicsActive, (Action)(() => OpenSchematics())),
            Key.W => (WorldMapOpen, WorldMapActive, (Action)OpenWorldMap),
            _ => (false, false, (Action?)null),
        };
        // The own key toggles through the window's own handler; a key that is no window key falls through to it too.
        if (open is null || own || !active || _inventoryPanel?.Asking == true) return false;
        if (!_worldFocused || WorldUiFocus) return true;
        if (WindowRefusedForHero(routed switch { Key.C => InterfaceMode.Character, Key.I => InterfaceMode.Inventory, Key.L => InterfaceMode.Logbook, Key.T => InterfaceMode.Schematic, _ => InterfaceMode.WorldMap }))
        { GD.Print($"ARC-DEV-111 window key {routed} refused for the hero; the open window stays (stack {StackText()})"); return true; }
        GD.Print($"ARC-DEV-111 window key {routed} replaces the open window (stack {StackText()})");
        CloseOtherInterfaceWindows();
        open();
        return true;
    }

    /// <summary>
    /// ARC-DEV-201 (MIG16; bar handler 0x54C8E0, press subtype 2 at 0x54CAF9, per-mode table 0x54DA40, read in the executable): a right
    /// press outside the game view's window (on the top or bottom bar strip) pops the world map, logbook, character sheet, schematic or
    /// reading window by one mode; a press over the window itself pops nothing, no window has a right-press handler of its own, and the
    /// press is ignored (a macOS Ctrl+click arrives as a right press with Ctrl held and is the original's Ctrl+left click, ARC-WORLD-020) while the dialog is up, in a round when another than the hero has the turn, with the menu up and with an inventory
    /// item held on the cursor. The window leaves through its own End routine, with no sound of its own. The hotkey bar's own press handling
    /// still runs (the event is not taken). True when a window was closed.
    /// </summary>
    private bool HandleRightPressOverWindow(Vector2 position, bool ctrl)
    {
        if (!InterfaceModes.RightPressOutsidePops(_interfaceWindows.Stack.Current)) return false;
        if (PanelOutsideStackOpen || ConversationOpen || AnotherHasTheTurn || _gameCursorItem is not null || WorldUiFocus || !_worldFocused || ctrl) return false;
        if (!OutsideGameViewWindow(position)) return false;
        GD.Print($"ARC-DEV-201 right press on the bar pops mode {(int)_interfaceWindows.Stack.Current} (stack {StackText()})");
        CloseOtherInterfaceWindows();
        return true;
    }

    /// <summary>ARC-DEV-201: the game view's window is the screen between the original's top strip (0..41) and bottom strip (441..600) of its
    /// 800×600 screen, which the viewer draws at a whole factor, centred. The original tests the coordinates, not whether the bars are drawn,
    /// so the strips count while the world map and the reading window hide the bars. A press beside the screen (the margins of a wider window)
    /// is no press on the original's screen and pops nothing.</summary>
    private bool OutsideGameViewWindow(Vector2 position)
    {
        var size = GetViewport().GetVisibleRect().Size;
        var factor = AreaGameBarPanel.ScreenFactor(size);
        var left = AreaGameBarPanel.ScreenLeft(size, factor);
        if (position.X < left || position.X >= left + AreaGameBarLayout.ScreenWidth * factor) return false;
        return position.Y < AreaGameBarLayout.TopHeight * factor || position.Y >= size.Y - AreaGameBarLayout.BottomHeight * factor;
    }

    private string StackText() => string.Join(">", _interfaceWindows.Stack.Modes);
}
