using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using GamepadLab.Core;
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
        // Saves go to a folder of their own, never the player's.
        _saveLibraryForSelfTest = new(System.IO.Directory.CreateTempSubdirectory("arc-dev-111-").FullName);
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
            // Save Game over the open logbook: the list opens above it (the menu hides, the window stays) and its Escape returns to the menu.
            var save = _mainMenu!.Shown.FirstOrDefault(button => button.Message == 30) ?? throw new InvalidOperationException("ARC-DEV-111 inv-ui smoke: no Save Game button");
            var letter = _mainMenu.Texts.Hotkey(save) ?? throw new InvalidOperationException("ARC-DEV-111 inv-ui smoke: no letter for Save Game");
            InputWorld(new InputEventKey { Keycode = letter is >= 'A' and <= 'Z' ? (Key)letter : Key.None, PhysicalKeycode = Key.None, Unicode = char.ToLowerInvariant(letter), Pressed = true });
            InputWorld(new InputEventKey { Keycode = letter is >= 'A' and <= 'Z' ? (Key)letter : Key.None, PhysicalKeycode = Key.None, Unicode = char.ToLowerInvariant(letter), Pressed = false });
            for (var frame = 0; frame < 30 && !SaveListOpen; frame++) await Frames(1);
            Require(SaveListOpen && LogbookOpen && StackText() == "Game>Logbook", "Save Game opened the list over the logbook: " + State() + " " + _saveListStatus);
            await Press(Key.Escape);
            Require(!SaveListOpen && MainMenuOpen && LogbookOpen, "Escape closed the list and the menu came back over the logbook: " + State());
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

            // ARC-DEV-200 (SA44, 0x551F70 table 0x5C72B4): the arrow keys scroll the game view in the dialog's mode (no scroll hook is set there)
            // and in no other window's mode. A stack-only dialog stands in for the conversation, whose own panel needs an NPC.
            Vector2 CameraAt() => new((float)_camera!.State.X, (float)_camera.State.Y);
            RawInputFrame Arrow() => new(-1, default) { Keyboard = true, KeyboardCamera = new(1, 0), KeyboardCameraHeld = true, CompleteObservation = true, FrameObservationId = (long)Time.GetTicksMsec() };
            try
            {
                Require(OpenWindowMode(InterfaceMode.Dialog, () => { }, () => { }) && _interfaceWindows.ScrollsGameView && WorldModal, "the stack-only dialog stands: " + State());
                var before = CameraAt();
                for (var frame = 0; frame < 3; frame++) WorldObservation(() => ApplyWorldObservedFrame(.1, true, Arrow(), [], "ARC-DEV-200 smoke: complete-level frame"));
                Require(CameraAt() != before, $"the arrow key scrolled the view under the dialog ({before} -> {CameraAt()})");
                CloseWindowMode(InterfaceMode.Dialog);
                Require(StackText() == "Game", "the stack-only dialog closed: " + State());
                await Press(Key.L);
                Require(LogbookOpen && !_interfaceWindows.ScrollsGameView, "the logbook is open: " + State());
                before = CameraAt();
                for (var frame = 0; frame < 3; frame++) WorldObservation(() => ApplyWorldObservedFrame(.1, true, Arrow(), [], "ARC-DEV-200 smoke: complete-level frame"));
                Require(CameraAt() == before, $"the arrow key scrolls nothing under the logbook ({before} -> {CameraAt()})");
                await Press(Key.L);
                Require(!AnyWindowOpen, "L closed the logbook: " + State());
                steps.Add("arrow scroll runs under the dialog, not under the logbook");
            }
            finally { if (_interfaceWindows.IsOpen(InterfaceMode.Dialog)) CloseWindowMode(InterfaceMode.Dialog); }

            // ARC-UI-099 (SA44, 0x401F50): the pointer on the exact border pixel scrolls the view through the same table as the arrows; the pixel
            // next to the border does not. The pointer position is the one a mouse event reported (set here), no frame holds a key.
            RawInputFrame Idle() => new(-1, default) { CompleteObservation = true, FrameObservationId = (long)Time.GetTicksMsec() };
            var edgeSize = WorldViewport;
            var east = new Vector2(edgeSize.X - 1, (float)Math.Floor(edgeSize.Y / 2));
            try
            {
                Require(OpenWindowMode(InterfaceMode.Dialog, () => { }, () => { }) && _interfaceWindows.ScrollsGameView && WorldModal, "the stack-only dialog stands for the edge: " + State());
                WorldPointerForSelfTest(new Vector2(edgeSize.X - 2, east.Y));
                var before = CameraAt();
                for (var frame = 0; frame < 3; frame++) WorldObservation(() => ApplyWorldObservedFrame(.1, true, Idle(), [], "ARC-UI-099 smoke: complete-level frame"));
                Require(CameraAt() == before, $"the pixel next to the east border scrolls nothing ({before} -> {CameraAt()})");
                WorldPointerForSelfTest(east);
                for (var frame = 0; frame < 3; frame++) WorldObservation(() => ApplyWorldObservedFrame(.1, true, Idle(), [], "ARC-UI-099 smoke: complete-level frame"));
                var scrolled = CameraAt();
                Require(scrolled.X > before.X && scrolled.Y == before.Y, $"the east border pixel scrolled the view east under the dialog ({before} -> {scrolled})");
                // ARC-UI-100 (0x40E0A0, 0x40E940): a step is the original's constant 20 pixels across (level 1), once per 16 ms, 24 and 12 on a diagonal.
                WorldPointerForSelfTest(null);
                WorldObservation(() => ApplyWorldObservedFrame(.1, true, Idle(), [], "ARC-UI-100 smoke: pause"));
                WorldPointerForSelfTest(east); before = CameraAt();
                WorldObservation(() => ApplyWorldObservedFrame(.1, true, Idle(), [], "ARC-UI-100 smoke: one step"));
                Require(CameraAt() - before == new Vector2(20, 0), $"one frame after a pause is one 20 pixel step east ({before} -> {CameraAt()})");
                before = CameraAt();
                WorldObservation(() => ApplyWorldObservedFrame(.005, true, Idle(), [], "ARC-UI-100 smoke: too soon"));
                Require(CameraAt() == before, $"a frame 5 ms after a step does not step ({before} -> {CameraAt()})");
                WorldObservation(() => ApplyWorldObservedFrame(.02, true, Idle(), [], "ARC-UI-100 smoke: next step"));
                Require(CameraAt() - before == new Vector2(20, 0), $"25 ms after the step it steps again ({before} -> {CameraAt()})");
                WorldPointerForSelfTest(new Vector2(edgeSize.X - 1, edgeSize.Y - 1)); before = CameraAt();
                WorldObservation(() => ApplyWorldObservedFrame(.1, true, Idle(), [], "ARC-UI-100 smoke: diagonal"));
                Require(CameraAt() - before == new Vector2(24, 12), $"the south-east corner pixel steps (24, 12) ({before} -> {CameraAt()})");
                scrolled = CameraAt();
                WorldPointerForSelfTest(null);
                for (var frame = 0; frame < 3; frame++) WorldObservation(() => ApplyWorldObservedFrame(.1, true, Idle(), [], "ARC-UI-099 smoke: complete-level frame"));
                Require(CameraAt() == scrolled, $"a pointer that left the window scrolls nothing ({scrolled} -> {CameraAt()})");
                CloseWindowMode(InterfaceMode.Dialog);
                await Press(Key.L);
                Require(LogbookOpen && !_interfaceWindows.ScrollsGameView, "the logbook is open for the edge: " + State());
                before = CameraAt(); WorldPointerForSelfTest(east);
                for (var frame = 0; frame < 3; frame++) WorldObservation(() => ApplyWorldObservedFrame(.1, true, Idle(), [], "ARC-UI-099 smoke: complete-level frame"));
                Require(CameraAt() == before, $"the east border pixel scrolls nothing under the logbook ({before} -> {CameraAt()})");
                await Press(Key.L);
                Require(!AnyWindowOpen, "L closed the logbook after the edge: " + State());
                steps.Add("edge scroll runs on the border pixel under the dialog, not under the logbook");
            }
            finally { WorldPointerForSelfTest(null); if (_interfaceWindows.IsOpen(InterfaceMode.Dialog)) CloseWindowMode(InterfaceMode.Dialog); }

            // ARC-DEV-201 (0x54CAF9): a right press on a bar pops the logbook, the sheet and the schematic window; a press over the window does not.
            var simulatedBars = _gameBarForSelfTest; _gameBarForSelfTest = true;
            try
            {
                await Frames(3);
                var size = GetViewport().GetVisibleRect().Size;
                Require(AreaGameBarPanel.TopInset > 0 && AreaGameBarPanel.BottomInset > 0, $"the bars show for the right-press step (insets {AreaGameBarPanel.TopInset}/{AreaGameBarPanel.BottomInset})");
                async Task Right(Vector2 at)
                {
                    InputWorld(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true, Position = at });
                    InputWorld(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = false, Position = at });
                    await Frames();
                }
                foreach (var (key, name) in new[] { (Key.L, "logbook"), (Key.C, "sheet"), (Key.T, "schematic") })
                {
                    await Press(key);
                    Require(AnyWindowOpen && WindowPanelsMatchStack, $"{key} opened the {name}: " + State());
                    var open = StackText();
                    await Right(new Vector2(size.X / 2, size.Y / 2));
                    Require(StackText() == open && AnyWindowOpen, $"a right press over the {name} pops nothing: " + State());
                    await Right(new Vector2(size.X / 2, size.Y - AreaGameBarPanel.BottomInset / 2));
                    Require(!AnyWindowOpen && StackText() == "Game" && GameViewModeOpen && WindowPanelsMatchStack, $"a right press on the bottom bar closed the {name}: " + State());
                    await Press(key);
                    await Right(new Vector2(size.X / 2, AreaGameBarPanel.TopInset / 2));
                    Require(!AnyWindowOpen && StackText() == "Game", $"a right press on the top bar closed the {name}: " + State());
                }
                // A Ctrl+press (macOS' Ctrl+left click) is no bar press: it pops nothing.
                await Press(Key.L);
                InputWorld(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true, CtrlPressed = true, Position = new Vector2(size.X / 2, size.Y - AreaGameBarPanel.BottomInset / 2) });
                await Frames();
                Require(LogbookOpen && StackText() == "Game>Logbook", "a Ctrl+right press on the bar pops nothing: " + State());
                await Press(Key.L);
                Require(!AnyWindowOpen, "L closed the logbook: " + State());
                // The world map hides the bars here, but the original tests the strips' coordinates: a press there pops it all the same.
                if (WorldMapActive)
                {
                    await Press(Key.W);
                    Require(WorldMapOpen && StackText() == "Game>WorldMap" && AreaGameBarPanel.BottomInset == 0, "W opened the world map, which hides the bars: " + State());
                    var factor = AreaGameBarPanel.ScreenFactor(size);
                    await Right(new Vector2(size.X / 2, size.Y - AreaGameBarLayout.BottomHeight * factor / 2));
                    Require(!WorldMapOpen && StackText() == "Game", "a right press on the bottom strip popped the world map: " + State());
                    steps.Add("right press on the strip pops the world map");
                }
                // The game view's base mode has no window to pop. (Not exercised here: the held item, the menu, another's turn, the reading window.)
                await Right(new Vector2(size.X / 2, size.Y - AreaGameBarPanel.BottomInset / 2));
                Require(StackText() == "Game", "a right press on a bar over the base mode changes nothing: " + State());
                steps.Add("right press on a bar pops logbook, sheet, schematic");
            }
            finally { _gameBarForSelfTest = simulatedBars; }

            GD.Print("ARC-DEV-111 native-smoke inv-ui PASS " + System.Text.Json.JsonSerializer.Serialize(new { Steps = steps }));
        }
        finally
        {
            if (AnyWindowOpen) CloseOtherInterfaceWindows();
            CancelTargetModes("the smoke ended");
            if (SaveListOpen) EndSaveList();
            if (MainMenuOpen) InventoryKey(Key.Escape);
            _saveLibraryForSelfTest = null; _quickKeys.Reset();
            _worldFocusSimulationForSelfTest = simulatedFocus; _worldFocused = focused;
        }
    }
}
