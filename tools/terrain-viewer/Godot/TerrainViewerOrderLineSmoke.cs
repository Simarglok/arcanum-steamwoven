using Arcanum.Presentation.Hud;
using Arcanum.Session;
using Arcanum.Simulation.Party;
using Godot;

namespace Arcanum.TerrainViewer;

public partial class TerrainViewerApp
{
    // ARC-UI-2305 (ledger BM5): the typed order line, a part of the "companion-click" stage (it needs the joined companion the stage already has): Enter opens bar mode 7 with the Legacy
    // art and an empty text, typed characters edit it (Backspace), Enter sends "name wait" to the follower he names and closes the line, an empty Enter closes it and sends nothing, and
    // Escape opens the menu, which closes the line. Every fact is added to the stage's list.
    private async Task OrderLineSmokePartAsync(string follower, string name, Action<string, bool, string> fact)
    {
        if (ConversationOpen || WorldModal) { fact("the order line smoke part ran", false, $"a window is up (talk {ConversationOpen}, modal {WorldModal})"); return; }
        // The bars show in a smoke only when asked for (the viewer's automated storage hides them).
        var simulatedBars = _gameBarForSelfTest; _gameBarForSelfTest = true;
        try { await OrderLineSmokeStepsAsync(follower, name, fact); }
        finally { _gameBarForSelfTest = simulatedBars; await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
    }

    private async Task OrderLineSmokeStepsAsync(string follower, string name, Action<string, bool, string> fact)
    {
        async Task Frames(int n = 3) { for (var f = 0; f < n; f++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
        async Task Press(Key key, char text = '\0')
        {
            InputWorld(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = true, Unicode = text });
            InputWorld(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = false });
            await Frames(1);
        }
        async Task Type(string text) { foreach (var c in text) await Press(c == ' ' ? Key.Space : (Key)char.ToUpperInvariant(c), c); }
        // ARC_AREA_FOLLOWER_PANEL_CAPTURE saves in-app readbacks (the strip before and with the line open) to the caller's ignored path, never committed.
        var capture = System.Environment.GetEnvironmentVariable("ARC_AREA_FOLLOWER_PANEL_CAPTURE");
        async Task Shot(string label)
        {
            if (string.IsNullOrWhiteSpace(capture)) return;
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            using var image = GetViewport().GetTexture().GetImage();
            if (image.SavePng($"{capture}-order-line-{label}.png") != Error.Ok) GD.Print("ARC-UI-2305 capture not saved: " + label);
        }
        await Frames();
        await Shot("before");
        fact("the order line is closed at first", !_orderLine.IsOpen && OrderLineViewForSelfTest is null, $"open {_orderLine.IsOpen}");

        await Press(Key.Enter);
        await Frames();
        var art = OrderLineViewForSelfTest?.BackgroundForSelfTest;
        fact("Enter opens bar mode 7: the line over the strip with art 291 and an empty text (one space), caret at 0",
            _orderLine.IsOpen && OrderLineViewForSelfTest is { } && OrderLineViewForSelfTest.TextForSelfTest == " " && _orderLine.Caret == 0 && (art is not null || _gameBarContent?.Art.ContainsKey(OrderLine.BackgroundArt) != true),
            $"open {_orderLine.IsOpen} view {OrderLineViewForSelfTest is not null} text '{OrderLineViewForSelfTest?.TextForSelfTest}' art {art is not null} (input open {OrderInputOpen}, bars {_gameBarPanel?.Visible}, fonts {TextFonts() is not null}, focused {_worldFocused}, ui focus {WorldUiFocus}, panel {PanelOutsideStackOpen})");

        await Type(name + " waitx");
        await Press(Key.Backspace);
        // Letters whose release acts as a hotkey (M the spell pane, K the skill pane, E the end of the hero's subturn) are typed text while the line is open: no pane opens, the line stays.
        await Type("mke");
        for (var back = 0; back < 3; back++) await Press(Key.Backspace);
        await Frames();
        var expected = name + " wait";
        fact("typed characters and Backspace edit the text the strip draws", _orderLine.Text == expected && OrderLineViewForSelfTest?.TextForSelfTest == expected,
            $"text '{_orderLine.Text}' drawn '{OrderLineViewForSelfTest?.TextForSelfTest}'");

        await Shot("after");
        var before = _areaSession!.QueryCompanion(follower).Standing;
        await Press(Key.Enter);
        await Frames();
        var after = _areaSession.QueryCompanion(follower).Standing;
        fact("Enter sends the text to the follower it names (his wait order) and closes the line",
            !_orderLine.IsOpen && OrderLineViewForSelfTest is null && before == CompanionStanding.Following && after == CompanionStanding.Waiting && _orderLineLastSent == expected,
            $"open {_orderLine.IsOpen} standing {before}->{after} sent '{_orderLineLastSent}' {_companionNotice}");

        _orderLineLastSent = null;
        await Press(Key.Enter);
        await Frames();
        var openedAgain = _orderLine.IsOpen;
        await Press(Key.Enter);
        await Frames();
        fact("an empty Enter closes the line and sends nothing", openedAgain && !_orderLine.IsOpen && _orderLineLastSent is null, $"reopened {openedAgain} open {_orderLine.IsOpen} sent '{_orderLineLastSent}'");

        await Press(Key.Enter);
        await Type("abc");
        await Frames();
        var typed = _orderLine.Text;
        await Press(Key.Escape);
        await Frames(4);
        fact("Escape opens the menu, which closes the line and drops its text", typed == "abc" && !_orderLine.IsOpen && MainMenuOpen && _orderLine.Text.Length == 0,
            $"typed '{typed}' open {_orderLine.IsOpen} menu {MainMenuOpen}");
        for (var close = 0; close < 3 && MainMenuOpen; close++) { await Press(Key.Escape); await Frames(3); }
        fact("the menu closes again", !MainMenuOpen, $"menu {MainMenuOpen}");
    }
}
