using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

// Native observation (ARC-DEV-111, MIG16 S4, SM23 of the open bugs plan; ARC_AREA_MAP_SMOKE_ONLY=inv-ui, map mode with --hero): the interface
// mode stack through the production key route (Input events the way a player's keys arrive). The window keys of the original's bar handler
// replace the open window (I, C, L: the inventory, the sheet and the logbook take each other's place and the stack and the panels agree after
// each); Escape is the main loop's key and opens the in-play menu over every mode (the inventory family closes under it, the logbook and a
// target mode stay, and Continue returns to them); the game view's mode guard follows the stack (a window owns the mouse, a target mode
// does not); the number row picks a hotkey slot with a window open. Not covered: the item steps of SM23 (boots, the quick bar drag, a note,
// the sort buttons, the worn/bag switch), which need the kit and the inventory rows the other stages own.
public partial class TerrainViewerApp
{
    private async Task RunAreaInterfaceModesSmokeAsync()
    {
        if (_areaSession is not { } session || session.QueryHero() is null || !InventoryActive || !SheetActive || !LogbookActive)
        { GD.Print($"ARC-DEV-111 native-smoke inv-ui NONE map={_readyMapName} (no hero or no window of the stack)"); return; }
        static void Require(bool value, string why)
        {
            if (value) return;
            GD.Print("ARC-DEV-111 inv-ui smoke FAIL: " + why);
            throw new InvalidOperationException("AREA_INV_UI_SMOKE_FAILED");
        }
        async Task Frames(int count = 2) { for (var i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
        async Task Press(Key key) { InventoryKey(key); await Frames(); }
        string State() => $"stack {StackText()} inventory={InventoryOpen} sheet={SheetOpen} logbook={LogbookOpen} menu={MainMenuOpen}";
        var simulatedFocus = _worldFocusSimulationForSelfTest; var focused = _worldFocused;
        _worldFocusSimulationForSelfTest = true;
        var steps = new List<string>();
        try
        {
            _worldFocused = true;
            if (AnyWindowOpen) { CloseOtherInterfaceWindows(); await Frames(); }
            Require(!AnyWindowOpen && StackText() == "Game" && GameViewModeOpen && WindowPanelsMatchStack && TargetsMatchStack, "the base: " + State());

            // Window keys replace each other (SA40, 0x54D959): the opener pops the open window and pushes its own.
            await Press(Key.I);
            Require(InventoryOpen && StackText() == "Game>Inventory" && WindowPanelsMatchStack && !GameViewModeOpen, "I opened the inventory: " + State());
            await Press(Key.C);
            Require(SheetOpen && !InventoryOpen && StackText() == "Game>Character" && WindowPanelsMatchStack, "C replaced the inventory by the sheet: " + State());
            await Press(Key.L);
            Require(LogbookOpen && !SheetOpen && StackText() == "Game>Logbook" && WindowPanelsMatchStack, "L replaced the sheet by the logbook: " + State());
            steps.Add("I, C, L replace each other");

            // The number row works with a window open (SA44: in every mode but the dialog).
            _hotkeyNotice = null;
            await Press(Key.Key1);
            Require(_hotkeyNotice is { } notice && notice.StartsWith("hotkey ", StringComparison.Ordinal) && !notice.Contains("ignored: palette", StringComparison.Ordinal),
                "the number row reached the hotkey slot with the logbook open: " + _hotkeyNotice);
            steps.Add("number row picks a slot over the logbook");

            // Escape opens the menu over the logbook, which stays; Continue returns to it (0x541180, 0x5412D0).
            await Press(Key.Escape);
            Require(MainMenuOpen && _mainMenu!.Window == AreaMenuWindow.InPlay && LogbookOpen && StackText() == "Game>Logbook" && WindowPanelsMatchStack,
                "Escape opened the menu (window 3) over the logbook, which stays: " + State());
            await Press(Key.Escape);
            Require(!MainMenuOpen && LogbookOpen && StackText() == "Game>Logbook", "Escape continued play with the logbook open: " + State());
            await Press(Key.L);
            Require(!LogbookOpen && StackText() == "Game" && GameViewModeOpen, "L closed the logbook: " + State());
            steps.Add("Esc over the logbook keeps it");

            // The inventory family closes under the menu.
            await Press(Key.I);
            Require(InventoryOpen, "I opened the inventory: " + State());
            await Press(Key.Escape);
            Require(MainMenuOpen && !InventoryOpen && StackText() == "Game" && WindowPanelsMatchStack, "Escape opened the menu, which closed the inventory: " + State());
            await Press(Key.Escape);
            Require(!MainMenuOpen && StackText() == "Game", "Escape continued play: " + State());
            steps.Add("Esc closes the inventory family");

            // A target mode: Escape opens the menu and the mode stays (SA51); the game view acts in it; a window replaces it (SA40).
            Require(OpenTargetMode(InterfaceMode.SkillTarget), "the skill target opened: " + State());
            await Frames();
            Require(PickPocketArmed && StackText() == "Game>SkillTarget" && GameViewModeOpen && TargetsMatchStack, "the skill target is armed and the game view acts: " + State());
            await Press(Key.Escape);
            Require(MainMenuOpen && PickPocketArmed && StackText() == "Game>SkillTarget", "Escape opened the menu over the target mode, which stays: " + State());
            await Press(Key.Escape);
            Require(!MainMenuOpen && PickPocketArmed && GameViewModeOpen, "Continue returned to the same target mode: " + State());
            await Press(Key.L);
            Require(LogbookOpen && !PickPocketArmed && StackText() == "Game>Logbook" && !GameViewModeOpen && TargetsMatchStack, "a window replaced the target mode: " + State());
            await Press(Key.L);
            Require(!AnyWindowOpen && StackText() == "Game", "L closed the logbook: " + State());
            steps.Add("Esc keeps a target mode, a window replaces it");

            GD.Print("ARC-DEV-111 native-smoke inv-ui PASS " + System.Text.Json.JsonSerializer.Serialize(new { Steps = steps }));
        }
        finally
        {
            if (AnyWindowOpen) CloseOtherInterfaceWindows();
            CancelTargetModes("the smoke ended");
            if (MainMenuOpen) InventoryKey(Key.Escape);
            _worldFocusSimulationForSelfTest = simulatedFocus; _worldFocused = focused;
        }
    }
}
