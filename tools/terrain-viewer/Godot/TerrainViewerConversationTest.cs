using System.Text;
using Arcanum.Data;
using Arcanum.Data.Area;
using Arcanum.Scripting.Dialog;
using Arcanum.TerrainViewer.Core;
using Arcanum.TerrainViewer.Tests;
using GamepadLab.Core;
using Godot;
using System.Reflection;

namespace Arcanum.TerrainViewer;

public static partial class TerrainViewerSelfTest
{
    // Invented synthetic records; no original dialogue.
    private const string SyntheticDialog =
        "{1}{Greetings, @pcname@. The road is closed.}{Greetings, madam.}{}{}{}{GF 7 1, QU 1003 1}" +
        "{2}{Why is it closed?}{}{1}{GF 7 1}{10}{}" +
        "{3}{Barter.}{}{1}{}{0}{}" +
        "{4}{b:}{}{1}{}{0}{}" +
        "{10}{A bridge fell. Take care on the way north.}{}{}{}{}{RU 1001}" +
        "{11}{Thank you.}{}{1}{}{0}{LF 2 1}";

    /// <summary>Synthetic panel flow: open, answer by number, end, dismiss. Headless and native.</summary>
    private static async Task AssertConversationPanelAsync(Node host)
    {
        var panel = new AreaConversationPanel(); host.AddChild(panel);
        var closed = 0; panel.CloseRequested += () => { closed++; panel.CloseView(); };
        try
        {
            await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
            using var session = ConversationSessionFixture.Create();
            var state = new AreaConversationState(session, new(1), ["oid:synthetic-0"], 50);
            var conversation = DialogConversation.Start(DlgReader.Read(Encoding.Latin1.GetBytes(SyntheticDialog)),
                new DialogConversationRequest(new DialogNodeId(1),
                    new DialogParticipants(new DialogId(5), AreaConversationState.PcParticipant, AreaConversationState.Npc(0)),
                    new DialogScriptLine(4), AreaConversationProfile.Diagnostic.Pc, AreaConversationProfile.Diagnostic.Names),
                new DialogConversationPorts(state, state, session.Flags, state));
            panel.Open("Synthetic NPC", conversation);
            await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
            Require(panel.IsOpen && panel.NpcLineForSelfTest == "Greetings, [PC]. The road is closed.", "panel shows the substituted NPC line");
            Require(panel.ButtonsForSelfTest.Count == 3 && panel.ButtonsForSelfTest[0].Text == "1. Why is it closed?", "eligible replies in order");
            Require(panel.ButtonsForSelfTest[2].Disabled && panel.ButtonsForSelfTest[2].Text.Contains("UnsupportedInteraction"), "generated barter is shown blocked");
            panel.Choose(3);
            Require(conversation.Turn == 1 && panel.OutcomeForSelfTest.Contains("UnsupportedTransition"), "blocked reply runs nothing");
            panel.Choose(1);
            Require(conversation.CurrentNode.Value == 10 && panel.NpcLineForSelfTest.StartsWith("A bridge fell.") && panel.ButtonsForSelfTest.Count == 1, "Direct reply continues");
            await ScreenshotAsync(host, "synthetic");
            panel.Choose(1);
            Require(conversation.Phase == DialogConversationPhase.Ended && panel.OutcomeForSelfTest.Contains("SCR line 5") && panel.ButtonsForSelfTest.Count == 0, "Exit returns to the caller's next SCR line");
            Require(state.LocalFlag(0, 2) && session.QueryQuest(1003)?.State == Arcanum.Session.SessionQuestState.Mentioned && session.QueryRumor(1001) is not null,
                "typed effects reached the session");
            panel.Choose(1);
            Require(closed == 1 && !panel.IsOpen, "any choice dismisses a finished dialog");
            panel.Open("Synthetic NPC", conversation); panel.LeaveForSelfTest.EmitSignal(BaseButton.SignalName.Pressed);
            Require(closed == 2 && !panel.IsOpen, "Close dismisses a finished dialog");
            GD.Print("ARC-SCRIPT-005 self-test PASS synthetic conversation panel; original runtime parity NOT_VERIFIED");
        }
        finally { panel.QueueFree(); }
    }

    /// <summary>ARC-UI-071 (B29): the synthetic flow with the original's text fonts (synthetic glyphs): every text, the replies' and Leave's too,
    /// is drawn as art text broken at the panel's content width, the buttons take their art text's height, a reply still works, and taking
    /// the fonts away draws the labels again. Headless and native.</summary>
    private static async Task AssertConversationPanelArtFontsAsync(Node host)
    {
        var panel = new AreaConversationPanel(); host.AddChild(panel);
        try
        {
            await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
            var frames = Enumerable.Range(0, 225).Select(_ => new AreaInterfaceFrame(5, 12, new byte[5 * 12 * 4], 4)).ToArray();
            var fonts = new AreaTextFonts(new AreaArtFontFace(frames), new AreaArtFontFace(frames));
            using var session = ConversationSessionFixture.Create();
            var state = new AreaConversationState(session, new(1), ["oid:synthetic-0"], 50);
            var conversation = DialogConversation.Start(DlgReader.Read(Encoding.Latin1.GetBytes(SyntheticDialog)),
                new DialogConversationRequest(new DialogNodeId(1),
                    new DialogParticipants(new DialogId(5), AreaConversationState.PcParticipant, AreaConversationState.Npc(0)),
                    new DialogScriptLine(4), AreaConversationProfile.Diagnostic.Pc, AreaConversationProfile.Diagnostic.Names),
                new DialogConversationPorts(state, state, session.Flags, state));
            panel.SetFonts(fonts);
            panel.Open("Synthetic NPC", conversation);
            await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
            await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
            var wrap = Math.Max(1, Math.Min(960, (int)host.GetViewport().GetVisibleRect().Size.X - 48)) - 32;
            Require(panel.ArtFontsForSelfTest && panel.NpcLineForSelfTest == "Greetings, [PC]. The road is closed.", "art mode shows the substituted NPC line");
            var texts = panel.ArtTextsForSelfTest();
            var npc = texts.Single(text => text.Name == "npc");
            Require(npc.Visible && npc.Text == panel.NpcLineForSelfTest && npc.Lines > 0 && npc.Wrap == wrap, $"the NPC line is art text at the content width {wrap}: {npc}");
            Require(texts.Where(text => text.Name is "header" or "hint" or "leave").All(text => text.Visible && text.Lines > 0), "header, hint and Leave are art text");
            Require(panel.ButtonsForSelfTest.Count == 3 && AreaConversationPanel.ReplyTextForSelfTest(panel.ButtonsForSelfTest[0]) == "1. Why is it closed?"
                && panel.ButtonsForSelfTest.All(button => button.Text.Length == 0), "the replies are art text, the buttons draw none");
            foreach (var button in panel.ButtonsForSelfTest.Append(panel.LeaveForSelfTest))
            {
                var reply = texts.Single(text => text.Name == (button == panel.LeaveForSelfTest ? "leave" : button.Name.ToString()));
                Require(reply.Lines > 0 && reply.Wrap == wrap - 16 && button.CustomMinimumSize.Y == reply.Lines * 12 + 8 && button.GetCombinedMinimumSize().Y == button.CustomMinimumSize.Y,
                    $"{button.Name}: lines {reply.Lines} wrap {reply.Wrap} height {button.CustomMinimumSize.Y} real minimum {button.GetCombinedMinimumSize().Y}");
            }
            Require(panel.ButtonsForSelfTest[2].Disabled, "the blocked reply stays disabled");
            panel.Choose(1);
            await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
            Require(conversation.CurrentNode.Value == 10 && panel.ArtTextsForSelfTest().Single(text => text.Name == "npc").Text.StartsWith("A bridge fell."), "a reply continues the dialog in art mode");
            panel.SetFonts(null);
            Require(!panel.ArtFontsForSelfTest && panel.ButtonsForSelfTest.Count == 1 && panel.ButtonsForSelfTest[0].Text.Length > 0
                && panel.ArtTextsForSelfTest().All(text => !text.Visible), "without the fonts the labels and buttons draw again");
            GD.Print("ARC-UI-071 self-test PASS conversation panel in the original's font art");
        }
        finally { panel.QueueFree(); }
    }

    /// <summary>Production app routing in crash-site-area mode over the synthetic strict owner: T, modal gate, Esc.</summary>
    private static async Task AssertConversationRoutingAsync(Node host)
    {
        using var source = new AreaOwnerFixture(motionBodies: true);
        var read = typeof(TerrainLoadCoordinator).Assembly.GetType("Arcanum.TerrainViewer.Core.AreaLoadOperation")!
            .GetMethod("ReadArea", BindingFlags.NonPublic | BindingFlags.Static)!;
        var area = (AreaLoadResult)read.Invoke(null, [7L, source.Owner, CancellationToken.None, null])!;
        var steps = AreaOwnerFixture.Steps(area.Snapshot, source.Streams[new(AreaSourceRole.Module)].Bytes, 7);
        source.Owner.Dispose();
        var result = SyntheticActorFixture.Create(human: true, area: true).Result with { Generation = 7, Area = area, AreaStepSnapshot = steps };
        var arguments = TerrainViewerArguments.Parse(["--mode", "crash-site-area", "--module", "synthetic-module",
            "--resource", "synthetic-terrain", "--actor-resource", "synthetic-actor", "--prototypes", "synthetic-prototypes",
            "--area-mobile", "synthetic-mobile", "--area-effects", "synthetic-effects", "--area-patch", "synthetic-patch", "--source-id", "synthetic"]);
        var viewport = new SubViewport { Size = new(800, 600), TransparentBg = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
        var app = new TerrainViewerApp { SelfTestBootstrap = true };
        viewport.AddChild(app); host.AddChild(viewport);
        await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
        app.SetProcess(false); app.SetProcessInput(false);
        try
        {
            app.PublishSyntheticForSelfTest(arguments, result);
            await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
            app.ConversationRoutingForSelfTest();
        }
        finally
        {
            app.ClearSyntheticForSelfTest();
            viewport.Free();
        }
        GD.Print("ARC-SCRIPT-005 self-test PASS crash-site-area talk routing (LMB/T off-NPC no-op, modal panel, gate closed, key events and polled levels not redirected, no palette over dialog, Close); positive NPC click covered by the opt-in original host test");
    }

    /// <summary>
    /// Opt-in local observation (ARC_AREA_CONVERSATION_GAME_ROOT): the first original area speaker through the
    /// production host and panel. Prints identifiers only; screenshots stay at the caller's ignored path.
    /// </summary>
    private static async Task AssertOriginalConversationAsync(Node host)
    {
        var root = System.Environment.GetEnvironmentVariable("ARC_AREA_CONVERSATION_GAME_ROOT");
        if (string.IsNullOrWhiteSpace(root)) throw new InvalidOperationException("CONVERSATION_GAME_ROOT_REQUIRED");
        using var data = AreaOriginalValidator.OpenFiles(Path.Combine(root, "modules/Arcanum.dat"), Path.Combine(root, "arcanum1.dat"),
            Path.Combine(root, "arcanum2.dat"), Path.Combine(root, "modules/Arcanum/maps/qrmvs-420-1zjcnpgn"), Path.Combine(root, "data/proto"));
        var snapshot = data.LoadStrict();
        using var session = ConversationSessionFixture.Create(snapshot.RuntimeMap);
        using var scripts = ConversationSessionFixture.Scripts(root);
        var conversations = new AreaConversationHost(snapshot, scripts, session);
        var start = conversations.Begin(conversations.Speakers.FirstOrDefault());
        GD.Print($"ARC-SCRIPT-005 original speakers={conversations.Speakers.Count} status={start.Status} detail={start.Detail}");
        if (start.Conversation is not { } conversation) throw new InvalidOperationException("ORIGINAL_CONVERSATION_NOT_STARTED");
        var panel = new AreaConversationPanel(); host.AddChild(panel);
        try
        {
            await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
            panel.Open($"Dialog NPC (script {start.Speaker!.ScriptId})", conversation);
            await ScreenshotAsync(host, "original-turn1");
            while (conversation.Phase == DialogConversationPhase.AwaitingSelection && conversation.Turn < 64)
            {
                var option = conversation.Options.FirstOrDefault(candidate => candidate.Block is null && candidate.Transition == DialogOptionTransition.Direct)
                    ?? conversation.Options.First(candidate => candidate.Block is null);
                GD.Print($"ARC-SCRIPT-005 original turn={conversation.Turn} node={conversation.CurrentNode.Value} options={conversation.Options.Count} choose={option.ResponseId.Value} {option.Transition}");
                panel.Choose(option.Number);
                await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
                if (conversation.Turn == 4) await ScreenshotAsync(host, "original-turn4");
            }
            await ScreenshotAsync(host, "original-end");
            GD.Print($"ARC-SCRIPT-005 original phase={conversation.Phase} end={conversation.End?.Kind} line={conversation.End?.ScriptLine?.Value} turns={conversation.Turn} effects={conversation.Effects.Count} gameplay parity NOT_VERIFIED");
            // ARC-WORLD-043: the talk's end closes the NPC's reaction conversation before the script resumes.
            Require(conversations.EndReaction(), "the started conversation opened the reaction slot");
            // ARC-SCRIPT-009: the DLG's return resumes the dialog script through the dispatcher.
            if (conversation.End?.ScriptLine is { } line)
            {
                var resumed = conversations.Resume(start.Speaker, line);
                GD.Print($"ARC-SCRIPT-009 original resume line={line.Value} status={resumed.Status} detail={resumed.Detail}");
                Require(resumed.Status == AreaConversationStartStatus.ScriptEnded, "the resumed dialog script returns");
            }
        }
        finally { panel.QueueFree(); }
    }

    internal static void Require(bool value, string why) { if (!value) throw new InvalidOperationException("CONVERSATION: " + why); }

    private static async Task ScreenshotAsync(Node host, string name)
    {
        var directory = System.Environment.GetEnvironmentVariable("ARC_AREA_CONVERSATION_SCREENSHOTS");
        if (string.IsNullOrWhiteSpace(directory) || DisplayServer.GetName() == "headless") return;
        await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
        await host.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using var image = host.GetViewport().GetTexture().GetImage();
        image.SavePng(Path.Combine(directory, $"conversation-{name}.png"));
    }
}

public partial class TerrainViewerApp
{
    internal void ConversationRoutingForSelfTest()
    {
        if (!SelfTestBootstrap || _readyMode != TerrainViewerMode.Area || _conversationHost is null || _readyArea is null)
            throw new InvalidOperationException("CONVERSATION_ROUTING_READY_REQUIRED");
        var simulatedFocus = _worldFocusSimulationForSelfTest;
        _worldFocusSimulationForSelfTest = true; _worldFocused = true;
        void Press(Key key) { InputWorld(new InputEventKey { Keycode = key, Pressed = true }); InputWorld(new InputEventKey { Keycode = key, Pressed = false }); }
        // The ground-click path (WorldRouteClick → TryAreaTalk) is the native unhandled LMB press.
        void Click(Vector2 at) { InputWorld(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = at }); _UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = at, GlobalPosition = at }); }
        try
        {
            TerrainViewerSelfTest.Require(!ConversationOpen && !WorldModal && _conversationHost.Speakers.Count == 0, "closed at publication; the synthetic core has no dialog NPC");
            Click(GetViewport().GetVisibleRect().Size / 2); Press(Key.T);
            TerrainViewerSelfTest.Require(!ConversationOpen && !WorldModal, "LMB off a dialog NPC and T (schematics, refused without a hero) do not open a dialog");
            // A row standing in for a speaker; the synthetic module is not inside a game folder, so there is no script
            // content (ARC-SCRIPT-011) and the start is a typed status.
            var row = _readyArea.Snapshot.Objects[0];
            // ARC-WORLD-048: no host lists the stand-in, so the self-test names it as its own (production routing refuses it).
            _selfTestStandInSpeaker = new AreaSpeaker(0, row, 1, row.WorldX ?? 0, row.WorldY ?? 0);
            BeginConversation(_selfTestStandInSpeaker);
            TerrainViewerSelfTest.Require(ConversationOpen && WorldModal && WindowPanelsMatchStack && !_worldGate.IsOpen && _conversationPanel!.Conversation is null
                && (_conversationPanel.OutcomeForSelfTest.Contains(nameof(AreaConversationStartStatus.EntryError))
                    || _conversationPanel.OutcomeForSelfTest.Contains(nameof(AreaConversationStartStatus.EntryNotAdmitted))
                    || _conversationPanel.OutcomeForSelfTest.Contains(nameof(AreaConversationStartStatus.GenericGreeting))
                    || _conversationPanel.OutcomeForSelfTest.Contains(nameof(AreaConversationStartStatus.SourceChanged))
                    || _conversationPanel.OutcomeForSelfTest.Contains(nameof(AreaConversationStartStatus.ContentUnavailable))),
                "the modal panel opens with the typed start status: " + (_conversationPanel?.OutcomeForSelfTest ?? "no panel") + $" open={ConversationOpen} modal={WorldModal} gate={_worldGate.IsOpen}");
            foreach (var key in new[] { Key.Key1, Key.Enter, Key.T }) Press(key);
            TerrainViewerSelfTest.Require(ConversationOpen, "keyboard key events are not redirected to dialog actions");
            // ARC-DEV-111 (SF1, SA45): Escape is the main loop's global key, never a dialog action: it opens the limited in-play menu (window 4,
            // kind 3) over the dialog, which stays; Escape again continues play.
            Press(Key.Escape);
            TerrainViewerSelfTest.Require(ConversationOpen && (!MainMenuOpen || _mainMenu!.Window == AreaMenuWindow.InPlayLimited), "Escape opens only the limited menu over the dialog");
            if (MainMenuOpen) { Press(Key.Escape); TerrainViewerSelfTest.Require(!MainMenuOpen && ConversationOpen, "Escape in the menu continues play with the dialog still open"); }
            // Polled keyboard levels too: full simulated frames with held Escape/Enter/Down-equivalent levels.
            var none = new RawInputFrame(-1, default);
            // The key and click events above were observed at real time; the simulated frames continue after the newest
            // of them (from the session's start time they would race the 0.1 s step and be refused as non-monotonic).
            _worldTestTime ??= WorldTime;
            for (var i = 0; i < 3; i++) WorldFrameForSelfTest(none, Array.Empty<RawInputFrame>(), .1);
            foreach (var held in new[] { none with { Cancel = true }, none with { Confirm = true }, none with { MenuDown = true }, none with { MenuUp = true } })
            {
                WorldFrameForSelfTest(held, Array.Empty<RawInputFrame>(), .1);
                WorldFrameForSelfTest(none, Array.Empty<RawInputFrame>(), .1);
            }
            TerrainViewerSelfTest.Require(ConversationOpen && _conversationPanel!.Conversation is null, "polled keyboard Confirm/Cancel/menu levels do not drive the dialog");
            ToggleWorldSettings();
            TerrainViewerSelfTest.Require(!PaletteOpen && ConversationOpen, "the palette cannot open over the conversation");
            _conversationPanel!.LeaveForSelfTest.EmitSignal(BaseButton.SignalName.Pressed);
            TerrainViewerSelfTest.Require(!ConversationOpen && !WorldModal, "Close ends the modal surface");
        }
        finally { _worldFocusSimulationForSelfTest = simulatedFocus; _selfTestStandInSpeaker = null; }
    }
}
