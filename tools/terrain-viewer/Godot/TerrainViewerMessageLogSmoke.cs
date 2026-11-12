using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

public partial class TerrainViewerApp
{
    /// <summary>ARC-UI-023 native observation, a part of the bar smoke (the bars shown, the pointer through the window's GUI): message
    /// entries with the player's encumbrance texts go through the production post, each drawn in the bottom strip's message window with
    /// the background, its icon and sound 3004; a repeat is not stored and plays again; the upper and lower halves step through the log;
    /// a bar button's name waits for the quiet time and leaving it shows the entry again; a pane takes the window, an entry takes it back,
    /// a move over the strip returns it and a move above the strip gives it to the log again; a long text is drawn in the wide layout. The smoke's entries are cleared at the end. Counts
    /// and checks only; no game text is printed.</summary>
    private async Task RunAreaMessageLogSmokeAsync(AreaGameBarPanel panel)
    {
        static void Require(bool value, string why) { if (!value) { GD.Print("ARC-UI-023 message log smoke FAIL " + why); throw new InvalidOperationException("ARC-UI-023 message log smoke: " + why); } }
        async Task Frames(int count)
        {
            for (var i = 0; i < count; i++)
            {
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            }
        }
        async Task Move(Vector2 at)
        {
            GetViewport().PushInput(new InputEventMouseMotion { Position = at, GlobalPosition = at });
            await Frames(1);
        }
        async Task Click(Vector2 at)
        {
            await Move(at);
            foreach (var pressed in new[] { true, false })
            {
                GetViewport().PushInput(new InputEventMouseButton
                {
                    ButtonIndex = MouseButton.Left, Pressed = pressed, Position = at, GlobalPosition = at, ButtonMask = pressed ? MouseButtonMask.Left : 0,
                });
                await Frames(1);
            }
        }
        var view = panel.MessageLog;
        var texts = LevelTexts(TimeSpan.FromSeconds(60)) ?? AreaLevelTexts.None;
        string Line(int level) => texts.Text(AreaLevelTexts.CritterPath, AreaLevelMessages.EncumbranceMessage + level)
            ?? throw new InvalidOperationException($"ARC-UI-023 message log smoke: critter.mes line {AreaLevelMessages.EncumbranceMessage + level} missing");
        ClearLevelMessages();
        var played = _interfaceSoundsPlayed;
        foreach (var level in new[] { 1, 2, 3 }) PostMessage(AreaLevelMessages.Encumbrance(level, texts), "smoke message");
        await Frames(1);
        Require(_messageLog.Count == 3 && view.ShownTextForSelfTest == Line(3) && view.IconShownForSelfTest && view.BackgroundShownForSelfTest && !view.WideForSelfTest,
            $"three entries, the newest drawn with its icon (count {_messageLog.Count}, icon {view.IconShownForSelfTest}, background {view.BackgroundShownForSelfTest})");
        Require(_audio is null || _interfaceSoundsPlayed == played + 3, "each entry played 3004: " + _cueNotice);
        PostMessage(AreaLevelMessages.Encumbrance(3, texts), "smoke message");
        Require(_messageLog.Count == 3 && (_audio is null || _interfaceSoundsPlayed == played + 4), "a repeat is not stored and plays again");

        await Click(view.OlderCentreForSelfTest);
        Require(view.ShownTextForSelfTest == Line(2), "the upper half showed the older entry");
        await Click(view.OlderCentreForSelfTest); await Click(view.OlderCentreForSelfTest);
        Require(view.ShownTextForSelfTest == Line(1), "no further back than the oldest");
        await Click(view.NewerCentreForSelfTest);
        Require(view.ShownTextForSelfTest == Line(2) && (_audio is null || _interfaceSoundsPlayed == played + 4), "the lower half showed the newer entry, silently");

        // A bar button's name: not within 3 s of the last entry, then shown with the hover icon; leaving shows the entry again.
        var character = panel.ButtonCentreForSelfTest(AreaBarButton.Character);
        await Move(character);
        Require(_gameBar.HoverMessage is not null && panel.HoverTextForSelfTest == "" && view.ShownTextForSelfTest == Line(2), "the name waits for the quiet time");
        _messageClockShiftForSelfTest += AreaMessageLog.QuietMilliseconds + 1;
        await Move(character + Vector2.One);
        Require(panel.HoverTextForSelfTest == "Character Editor" && view.IconShownForSelfTest, $"the name after the quiet time: '{panel.HoverTextForSelfTest}'");
        var strip = panel.StripRectForSelfTest(AreaBarStrip.Bottom);
        var empty = strip.Position + new Vector2(400, 20) * panel.Factor;
        await Move(empty);
        Require(panel.HoverTextForSelfTest == "" && view.ShownTextForSelfTest == Line(2), "leaving the button showed the entry again");

        // A pane takes the window; an entry takes it back until the pointer moves over the strip.
        var paned = false;
        if (SpellMapActive)
        {
            await Click(panel.ButtonCentreForSelfTest(AreaBarButton.Spells));
            await Frames(1);
            Require(_spellPane?.Shown == true && _messageLog.PaneMode && view.ShownTextForSelfTest == "" && !view.BackgroundShownForSelfTest, "Spells took the window");
            PostMessage(AreaLevelMessages.Encumbrance(4, texts), "smoke message");
            Require(!_messageLog.PaneMode && view.ShownTextForSelfTest == Line(4), "an entry took the window back");
            await Move(empty + new Vector2(0, 30) * panel.Factor);
            Require(_messageLog.PaneMode && view.ShownTextForSelfTest == "", "a move over the strip gave it to the pane again");
            await Move(strip.Position + new Vector2(400, -20) * panel.Factor);
            Require(!_messageLog.PaneMode && view.ShownTextForSelfTest == Line(4), "a move above the strip gave the window to the log");
            await Move(empty + new Vector2(0, 30) * panel.Factor);
            Require(_messageLog.PaneMode, "and back over the strip to the pane");
            await Click(panel.ButtonCentreForSelfTest(AreaBarButton.Spells));
            await Frames(1);
            Require(!_messageLog.PaneMode && view.ShownTextForSelfTest == Line(4), "Spells up: the entry shows again");
            paned = true;
        }

        // A text too long for the icon layout is drawn wide, without the icon, cut until it fits.
        // Wide capitals: the original's narrower font art fits 197 lower-case characters in the wide layout.
        var words = string.Join(" ", Enumerable.Repeat("WMWMWMWMWM", 18));
        PostMessage(AreaMessageEntry.Text(AreaMessageKind.Plain, words), "smoke message");
        await Frames(1);
        Require(view.WideForSelfTest && !view.IconShownForSelfTest && view.ShownTextForSelfTest.Length > 0 && view.ShownTextForSelfTest.Length < words.Length,
            $"the long text is drawn wide and cut ({view.ShownTextForSelfTest.Length} of {words.Length})");
        // A level-up entry with the player's texts: every one of its texts with a line on screen.
        var levelEntry = AreaLevelMessages.Compose(new Arcanum.Session.SessionLevelUp(5, 3, null), texts);
        PostMessage(levelEntry, "smoke level");
        await Frames(1);
        var texted = new[] { levelEntry.Title, levelEntry.Line, levelEntry.Detail }.Count(text => text.Length > 0);
        Require(texted >= 2 && view.DrawnTextsForSelfTest == texted && view.IconShownForSelfTest, $"the level-up entry drew {view.DrawnTextsForSelfTest} of {texted} texts");
        // ARC-SCRIPT-1118 (PW2, PY1-PY4): the print line types 1, 2, 3 and 12 through the production script-line path: each stored one with its icon, its
        // drawn texts (the poison notice two, a curse and a blessing the title only) and its sound; the type 12 text not stored and drawn after the quiet time.
        // ARC_UI_MSGLOG_CAPTURE saves each drawing to the caller's ignored path.
        var scriptCapture = System.Environment.GetEnvironmentVariable("ARC_UI_MSGLOG_CAPTURE");
        void ScriptShot(string name)
        {
            if (string.IsNullOrWhiteSpace(scriptCapture)) return;
            using var image = GetViewport().GetTexture().GetImage();
            Require(image.SavePng($"{scriptCapture}-{name}.png") == Error.Ok, "capture saved");
        }
        var scriptCount = _messageLog.Count; var scriptPlayed = _interfaceSoundsPlayed;
        ScriptShot("before");
        foreach (var (type, name, drawn) in new[] { (1, "type1", 2), (2, "type2", 1), (3, "type3", 1) })
        {
            var scripted = AreaScriptMessages.Entries([new(type, "Scripted " + type)], texts).Single();
            PostMessage(scripted, "smoke script line");
            await Frames(2);
            Require(view.IconShownForSelfTest && view.DrawnTextsForSelfTest == drawn && !view.WideForSelfTest && _messageLog.Shown == scripted,
                $"script print line type {type} drew {view.DrawnTextsForSelfTest} of {drawn} texts with its icon");
            ScriptShot(name);
        }
        Require(_messageLog.Count == Math.Min(AreaMessageLog.Capacity, scriptCount + 3) && (_audio is null || _interfaceSoundsPlayed == scriptPlayed + 3),
            $"the three script entries were stored and played their sounds ({_messageLog.Count}, {_interfaceSoundsPlayed - scriptPlayed})");
        _messageClockShiftForSelfTest += AreaMessageLog.QuietMilliseconds + 1;
        var scriptHover = AreaScriptMessages.Entries([new(12, "Scripted hover")], texts).Single();
        var storedBefore = _messageLog.Count;
        PostMessage(scriptHover, "smoke script line");
        await Frames(2);
        Require(_messageLog.Count == storedBefore && _messageLog.Shown == scriptHover && view.IconShownForSelfTest && view.DrawnTextsForSelfTest == 1,
            "a type 12 text is drawn after the quiet time and not stored");
        ScriptShot("type12");
        var stored = _messageLog.Count;
        ClearLevelMessages();
        Require(view.ShownTextForSelfTest == "", "the smoke's entries are cleared");
        GD.Print("ARC-UI-023 native-smoke message-log PASS " + System.Text.Json.JsonSerializer.Serialize(new {
            Stored = stored, Sounds = _interfaceSoundsPlayed - played, Pane = paned, Factor = panel.Factor, Parity = "NOT_VERIFIED" }));
    }
}
