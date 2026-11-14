using System.Text;
using Arcanum.Data;
using Arcanum.Data.Area;
using Arcanum.Presentation.Hud;
using Arcanum.Scripting.Dialog;
using Arcanum.Session;
using Arcanum.TerrainViewer.Core;
using Arcanum.TerrainViewer.Tests;
using Arcanum.World;
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

    // ARC-UI-2027: one reply whose action is a final line (FL 10): the dialog ends on the NPC's line of node 10.
    internal const string FinalLineDialog = "{1}{Hello.}{Hello.}{}{}{}{}{2}{Go on.}{}{1}{}{10}{FL 10}{10}{Fine.}{}{}{}{}{}";

    // ARC-UI-2019: one reply to a closing line, for the digit-key check of the app routing.
    internal const string DigitDialog = "{1}{Hello.}{Hello.}{}{}{}{}{2}{Go on.}{}{1}{}{10}{}{10}{Fine.}{}{}{}{}{}";

    /// <summary>Synthetic panel flow: open, answer by number, end, dismiss. Headless and native.</summary>
    private static async Task AssertConversationPanelAsync(Node host)
    {
        var panel = new AreaConversationPanel(); host.AddChild(panel);
        var closed = 0; panel.CloseRequested += () => { closed++; panel.CloseView(); };
        // ARC-UI-2027: the NPC line goes to the owner (as a persistent bubble while a reply is awaited), a processed reply removes it first.
        var lines = new List<string>(); var picks = 0;
        panel.NpcLineShown += (text, persistent) => lines.Add($"{(persistent ? "P" : "T")}:{text}"); panel.ReplyPicking += () => { picks++; lines.Add("removed"); };
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
            Require(lines.Count == 1 && lines[0] == "P:Greetings, [PC]. The road is closed.", $"the open hands the NPC line out as a persistent bubble: {string.Join("|", lines)}");
            Require(panel.ButtonsForSelfTest.Count == 3 && panel.ButtonsForSelfTest[0].Text == "1. Why is it closed?", "eligible replies in order");
            Require(panel.ButtonsForSelfTest[2].Disabled && panel.ButtonsForSelfTest[2].Text.Contains("UnsupportedInteraction"), "generated barter is shown blocked");
            panel.Choose(3);
            Require(conversation.Turn == 1 && panel.OutcomeForSelfTest.Contains("UnsupportedTransition"), "blocked reply runs nothing");
            Require(picks == 0 && lines.Count == 1, "a reply that was not processed keeps the bubble");
            // ARC-UI-2019: Steamwoven numbers the replies and a digit key chooses an offered, unblocked one; Original draws no number and reads no digit.
            var turn = conversation.Turn;
            Require(!panel.ChooseByDigit(3) && !panel.ChooseByDigit(9) && !panel.ChooseByDigit(0) && conversation.Turn == turn, "a blocked or absent number chooses nothing");
            // The setting switched while the panel is open (an Options change over the dialog) redraws it at once and moves the digits with it.
            panel.NumberedReplies = false;
            Require(panel.ButtonsForSelfTest[0].Text == "Why is it closed?" && !panel.ButtonsForSelfTest[2].Text.StartsWith("3.") && !panel.ChooseByDigit(1) && conversation.Turn == turn,
                "Original draws no number and a digit chooses nothing");
            await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
            await ScreenshotAsync(host, "original-unnumbered");
            panel.NumberedReplies = true;
            await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
            await ScreenshotAsync(host, "steamwoven-numbered");
            Require(panel.ButtonsForSelfTest[0].Text == "1. Why is it closed?", "Steamwoven draws the number again");
            // ARC-UI-2023: Steamwoven keeps the diagnostic chrome; Original shows only the reply rows (no header, outcome, effects, hint, Leave, nor a cancel leave).
            Require(panel.ChromeShownForSelfTest.Header && panel.ChromeShownForSelfTest.Hint && panel.ChromeShownForSelfTest.Leave && panel.LeaveOfferedForSelfTest, "Steamwoven keeps the diagnostic chrome");
            await ScreenshotAsync(host, "steamwoven-chrome");
            panel.DiagnosticChromeHidden = true; panel.NumberedReplies = false; panel.OriginalReplyRows = true;
            await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
            await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
            await ScreenshotAsync(host, "original-chrome-hidden");
            Require(panel.ChromeShownForSelfTest == (false, false, false, false, false) && !panel.LeaveOfferedForSelfTest && panel.ButtonsForSelfTest.Count > 0,
                $"Original shows only the reply rows: {panel.ChromeShownForSelfTest}");
            var closedAtCancel = closed;
            panel.Cancel();
            Require(closed == closedAtCancel && panel.IsOpen && conversation.Phase == DialogConversationPhase.AwaitingSelection, "Original: the cancel does not leave an awaiting dialog");
            panel.DiagnosticChromeHidden = false; panel.NumberedReplies = true; panel.OriginalReplyRows = false;
            // ARC-UI-2023: an awaiting dialog with no enabled reply (every reply blocked) and a message-only panel keep Leave under Original: no soft-lock.
            {
                var blockedOnly = DialogConversation.Start(DlgReader.Read(Encoding.Latin1.GetBytes("{1}{Hi.}{}{}{}{}{}{4}{b:}{}{1}{}{0}{}")),
                    new DialogConversationRequest(new DialogNodeId(1),
                        new DialogParticipants(new DialogId(5), AreaConversationState.PcParticipant, AreaConversationState.Npc(0)),
                        new DialogScriptLine(4), AreaConversationProfile.Diagnostic.Pc, AreaConversationProfile.Diagnostic.Names),
                    new DialogConversationPorts(state, state, session.Flags, state));
                var shut = closed; var mark = lines.Count;
                panel.DiagnosticChromeHidden = true;
                panel.Open("Synthetic NPC", blockedOnly);
                Require(blockedOnly.Phase == DialogConversationPhase.AwaitingSelection && panel.ButtonsForSelfTest.All(button => button.Disabled) && panel.LeaveOfferedForSelfTest,
                    "Original: an awaiting dialog with no enabled reply keeps Leave");
                panel.Cancel();
                Require(closed == shut + 1 && !panel.IsOpen, "Original: the cancel leaves a dialog with no enabled reply");
                panel.Open("Synthetic NPC", null, "No dialog could start");
                Require(panel.LeaveOfferedForSelfTest && panel.OutcomeForSelfTest == "No dialog could start", "Original: a message-only panel keeps its message and Close");
                panel.Cancel();
                panel.DiagnosticChromeHidden = false;
                panel.Open("Synthetic NPC", conversation);
                closed = shut; // the closes of this side check are not part of the flow counted below
                lines.RemoveRange(mark, lines.Count - mark); // the bubbles of this side check are not part of the flow asserted below
                await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
            }
            // ARC-UI-2025: under Original rows a blocked reply (row 2, y 40-59) and an empty slot (row 3) are refused by a press and change nothing.
            panel.OriginalReplyRows = true;
            await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
            await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
            var before = conversation.Turn;
            Require(panel.BandInputForSelfTest(Press(new(10, 45))) && panel.BandInputForSelfTest(Press(new(10, 65))), "a press on the blocked reply or the empty slot is taken");
            await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
            Require(conversation.Turn == before && conversation.Phase == DialogConversationPhase.AwaitingSelection, "the blocked reply and the empty slot choose nothing");
            panel.OriginalReplyRows = false;
            Require(!panel.BandShownForSelfTest && !panel.StackInBandForSelfTest, "Steamwoven: the stack is back in the panel and the band is gone");
            Require(panel.ChooseByDigit(1), "a digit chooses the numbered reply");
            Require(conversation.CurrentNode.Value == 10 && panel.NpcLineForSelfTest.StartsWith("A bridge fell.") && panel.ButtonsForSelfTest.Count == 1, "Direct reply continues");
            Require(picks == 1 && lines.Count == 3 && lines[1] == "removed" && lines[2].StartsWith("P:A bridge fell."), $"a processed reply removes the bubble, then the answer is a new persistent one: {string.Join("|", lines)}");
            await ScreenshotAsync(host, "synthetic");
            // ARC-UI-2025: a reply is chosen on the press, in both settings (Steamwoven: the button; Original: the band, y / 20, empty slots refused).
            Require(panel.ButtonsForSelfTest.All(button => button.ActionMode == BaseButton.ActionModeEnum.Press) && panel.LeaveForSelfTest.ActionMode == BaseButton.ActionModeEnum.Press,
                "the reply buttons act on the press");
            panel.OriginalReplyRows = true;
            await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
            await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
            var button0 = panel.ButtonsForSelfTest[0];
            Require(panel.BandWidthForSelfTest > 0 && Math.Abs(button0.Position.Y - 3) < .5f && Math.Abs(button0.Size.Y - 16) < .5f, $"Original: the first row is 16 px tall 3 px down ({button0.Position.Y}, {button0.Size.Y})");
            var node = conversation.CurrentNode.Value;
            Require(!panel.BandInputForSelfTest(Press(new(-3, 5))) && !panel.BandInputForSelfTest(Press(new(10, 100))), "a press outside the band is not taken");
            Require(panel.BandInputForSelfTest(Press(new(10, 25))) && conversation.CurrentNode.Value == node, "an empty slot is refused");
            Require(!panel.BandInputForSelfTest(Release(new(10, 2))) && conversation.CurrentNode.Value == node, "the band takes no release (the reply buttons act on the press, not the release)");
            // ARC-UI-2022: the band is the original's strip over the view: 400 px wide (short replies), centred, 100 tall, its top by the view height, a subtract shade of 18.
            var view = host.GetViewport().GetVisibleRect().Size;
            var rect = panel.BandRectForSelfTest;
            Require(panel.BandShownForSelfTest && rect.W == DialogReplyBand.MinWidth && rect.H == DialogReplyBand.Height && rect.X == DialogReplyBand.BoxLeft((int)view.X, rect.W) && rect.Y == DialogReplyBand.BoxTop((int)view.Y),
                $"Original: the band is a 400x100 strip centred in the view at {DialogReplyBand.BoxTop((int)view.Y)}: {rect} in {view}");
            Require(panel.BandShadeForSelfTest == "18,18,18|Sub", $"the band darkens by subtracting 18: {panel.BandShadeForSelfTest}");
            Require(!panel.BandInputForSelfTest(Press(new(rect.W, 5))) && !panel.BandInputForSelfTest(Press(new(rect.W + 50, 5))), "a press right of the band's width is outside it (not the panel's width)");
            Require(panel.RowColourForSelfTest(0) is (255, 255, 255), $"a normal row is pure white: {panel.RowColourForSelfTest(0)}");
            if (panel.ArtFontsForSelfTest) Require(panel.RowShadowForSelfTest(0) == new Vector2(1, 1), "a row draws a shadow one pixel right and down");
            await ScreenshotAsync(host, "original-band");
            panel.BandInputForSelfTest(new InputEventMouseMotion { Position = new(10, 5) });
            Require(panel.RowLitForSelfTest(0), "the pointer on the first row's drawn pixels lights it");
            // The process-frame signal comes before the nodes' own process: two frames make sure the panel's _Process has coloured the row.
            await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
            await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
            Require(panel.RowColourForSelfTest(0) is (255, 255, 0), $"the lit row is yellow: {panel.RowColourForSelfTest(0)}");
            await ScreenshotAsync(host, "original-band-lit");
            // ARC-UI-2052: the rows are rebuilt (here by the numbering) under a stationary pointer: the new row is lit against the live cursor, no motion needed.
            panel.NumberedReplies = false;
            await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
            await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
            Require(panel.RowLitForSelfTest(0) && panel.ButtonsForSelfTest[0].HasMeta("rowLit") && panel.ButtonsForSelfTest[0].GetMeta("rowLit").AsBool(),
                "after a re-render the row under the stationary pointer is lit again, with no new motion");
            panel.BandInputForSelfTest(new InputEventMouseMotion { Position = new(10, 20) });
            Require(!panel.RowLitForSelfTest(0), "moving off the row (into the gap below) unlights it");
            panel.NumberedReplies = true;
            await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
            await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
            Require(!panel.RowLitForSelfTest(0), "after a re-render with the pointer in the gap no row is lit");
            panel.BandInputForSelfTest(new InputEventMouseMotion { Position = new(10, 5) });
            Require(panel.RowLitForSelfTest(0), "the pointer back on the row lights it");
            panel.NumberedReplies = false;
            await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
            await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
            panel.NumberedReplies = true;
            await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
            Require(panel.RowLitForSelfTest(0), "the row stays lit across two re-renders under the stationary pointer");
            panel.BandInputForSelfTest(new InputEventMouseMotion { Position = new(-5, 5) });
            await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
            Require(!panel.RowLitForSelfTest(0), "a pointer outside the band lights nothing");
            await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
            await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
            Require(panel.RowColourForSelfTest(0) is (255, 255, 255), "the unlit row is white again");
            // With the chrome hidden (ARC-UI-2023) the framed panel is empty and not drawn: the band alone is on screen.
            Require(panel.PanelShownForSelfTest, "Original with the chrome shown still draws the framed panel above the band");
            panel.DiagnosticChromeHidden = true;
            await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
            await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
            Require(!panel.PanelShownForSelfTest && panel.BandShownForSelfTest, "Original with the chrome hidden draws only the band");
            await ScreenshotAsync(host, "original-band-only");
            panel.DiagnosticChromeHidden = false;
            await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
            Require(panel.BandInputForSelfTest(Press(new(10, 2))), "a press above the first row's drawn top is still in its click band");
            await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
            Require(picks == 2 && lines.Count == 4 && lines[3] == "removed", $"the ending reply removes the bubble and shows no new persistent one: {string.Join("|", lines)}");
            Require(conversation.Phase == DialogConversationPhase.Ended && panel.OutcomeForSelfTest.Contains("SCR line 5") && panel.ButtonsForSelfTest.Count == 0, "Exit returns to the caller's next SCR line");
            await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
            Require(!panel.BandShownForSelfTest, "a finished dialog has no reply, so no band is drawn");
            panel.OriginalReplyRows = false;
            Require(state.LocalFlag(0, 2) && session.QueryQuest(1003)?.State == Arcanum.Session.SessionQuestState.Mentioned && session.QueryRumor(1001) is not null,
                "typed effects reached the session");
            panel.Choose(1);
            Require(closed == 1 && !panel.IsOpen, "any choice dismisses a finished dialog");
            // ARC-UI-2023: a finished dialog keeps its Close button even under Original (nothing else could close it); the rest of the chrome stays hidden.
            panel.DiagnosticChromeHidden = true;
            panel.Open("Synthetic NPC", conversation);
            Require(panel.LeaveOfferedForSelfTest && panel.ChromeShownForSelfTest is { Leave: true, Header: false, Hint: false }, "Original: a finished dialog keeps only Close");
            panel.LeaveForSelfTest.EmitSignal(BaseButton.SignalName.Pressed);
            panel.DiagnosticChromeHidden = false;
            Require(closed == 2 && !panel.IsOpen, "Close dismisses a finished dialog");
            // ARC-UI-2027: a reply that ends on a new final line removes the bubble, then shows the line with the default life (not persistent).
            {
                var finalLines = new List<string>(); var finalPanel = new AreaConversationPanel(); host.AddChild(finalPanel);
                try
                {
                    await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
                    finalPanel.NpcLineShown += (text, persistent) => finalLines.Add($"{(persistent ? "P" : "T")}:{text}"); finalPanel.ReplyPicking += () => finalLines.Add("removed");
                    using var finalSession = ConversationSessionFixture.Create();
                    var finalState = new AreaConversationState(finalSession, new(1), ["oid:synthetic-0"], 50);
                    var finalDialog = DialogConversation.Start(DlgReader.Read(Encoding.Latin1.GetBytes(FinalLineDialog)),
                        new DialogConversationRequest(new DialogNodeId(1),
                            new DialogParticipants(new DialogId(5), AreaConversationState.PcParticipant, AreaConversationState.Npc(0)),
                            new DialogScriptLine(4), AreaConversationProfile.Diagnostic.Pc, AreaConversationProfile.Diagnostic.Names),
                        new DialogConversationPorts(finalState, finalState, finalSession.Flags, finalState));
                    finalPanel.Open("Final line", finalDialog);
                    finalPanel.Choose(1);
                    Require(finalDialog.End?.Kind == DialogEndKind.NpcFinalLine && string.Join("|", finalLines) == "P:Hello.|removed|T:Fine.", $"a reply that ends on a new final line: {string.Join("|", finalLines)} / {finalDialog.End?.Kind}");
                }
                finally { finalPanel.QueueFree(); }
            }
            // ARC-UI-2028: a reply that leads to the NPC's final line closes the window at once (the host floats the line); no finished panel stays to be dismissed.
            var finalConversation = DialogConversation.Start(DlgReader.Read(Encoding.Latin1.GetBytes(FinalLineDialog)),
                new DialogConversationRequest(new DialogNodeId(1),
                    new DialogParticipants(new DialogId(5), AreaConversationState.PcParticipant, AreaConversationState.Npc(0)),
                    new DialogScriptLine(4), AreaConversationProfile.Diagnostic.Pc, AreaConversationProfile.Diagnostic.Names),
                new DialogConversationPorts(state, state, session.Flags, state));
            panel.Open("Synthetic NPC", finalConversation);
            await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
            var closedBefore = closed;
            panel.Choose(1);
            Require(finalConversation.End?.Kind == DialogEndKind.NpcFinalLine && AreaConversationText.EndsWithNpcLine(finalConversation) && finalConversation.NpcLine?.Text == "Fine." && closed == closedBefore + 1 && !panel.IsOpen,
                "the final line ends the dialog and closes the window at once");
            GD.Print("ARC-SCRIPT-005 self-test PASS synthetic conversation panel; original runtime parity NOT_VERIFIED");
        }
        finally { panel.QueueFree(); }
    }

    private static InputEventMouseButton Press(Vector2 at) => new() { ButtonIndex = MouseButton.Left, Pressed = true, Position = at };
    private static InputEventMouseButton Release(Vector2 at) => new() { ButtonIndex = MouseButton.Left, Pressed = false, Position = at };

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
            // ARC-UI-2027: the NPC line is a bubble over the NPC, no longer a paragraph of the panel.
            Require(texts.All(text => text.Name != "npc"), "the panel draws no NPC line (it is a bubble over the NPC)");
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
            Require(conversation.CurrentNode.Value == 10 && panel.NpcLineForSelfTest.StartsWith("A bridge fell."), "a reply continues the dialog in art mode");
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
        var read = typeof(TerrainLoadCoordinator).Assembly.GetType("Arcanum.World.AreaLoadOperation")!
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
            // ARC-UI-2026 (SA43, main loop O branch 0x401560, menu start kind 4): the dialog's handler lets the release of O through; it opens the
            // options window (window 6) over the dialog, which stays; the press alone opens nothing, the clock is not paused, and O closes it again.
            {
                void OKey(bool pressed) => InputWorld(new InputEventKey { Keycode = Key.O, PhysicalKeycode = Key.O, Pressed = pressed });
                OKey(true);
                TerrainViewerSelfTest.Require(ConversationOpen && !MainMenuOpen && _mainMenu is null, "the O press alone opens nothing over the dialog");
                OKey(false);
                TerrainViewerSelfTest.Require(ConversationOpen && MainMenuOpen && _mainMenu is { Window: AreaMenuWindow.Options, OpenedByOptionsKey: true } && !WorldMenuPause,
                    "the O release opens the options window over the dialog without the pause: " + (_mainMenu?.Window.ToString() ?? "no menu"));
                OKey(true); OKey(false);
                TerrainViewerSelfTest.Require(ConversationOpen && !MainMenuOpen && _mainMenu is null, "O closes the options window and the dialog is still open");
                // A release whose press another owner took (the menu was open) opens nothing later.
                OKey(false);
                TerrainViewerSelfTest.Require(ConversationOpen && !MainMenuOpen, "a lone O release opens nothing");
            }
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
            // ARC-UI-2052: Close and the replies act on the press, so the panel is gone when the matching left release arrives: that lone release
            // (through the world's input callback and the unhandled path) sets no route, picks nothing, opens nothing.
            {
                var clickBefore = _worldRouteClick; var routeBefore = _worldRouteDiagnostic;
                var releaseAt = GetViewport().GetVisibleRect().Size / 2;
                var lone = new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = releaseAt, GlobalPosition = releaseAt };
                InputWorld(lone); _UnhandledInput(lone);
                TerrainViewerSelfTest.Require(_worldRouteClick == clickBefore && _worldRouteDiagnostic == routeBefore && !ConversationOpen && !WorldModal,
                    $"the lone left release after the closing press acts on nothing (click '{clickBefore}' -> '{_worldRouteClick}')");
            }
            // ARC-UI-2024 (DB1, DB3): Original leaves the bars drawn under the dialog and lays the panel above them; Steamwoven hides them under it.
            {
                var session = _areaSession!;
                var priorRules = session.Rules.Mode; var priorBars = _gameBarForSelfTest;
                _gameBarForSelfTest = true;
                try
                {
                    foreach (var rules in new[] { RuleMode.Original, RuleMode.Steamwoven })
                    {
                        TerrainViewerSelfTest.Require(session.SelectRules(rules) != GameSessionDisposition.Rejected, $"{rules} selected");
                        var stays = rules == RuleMode.Original;
                        SyncGameBar();
                        TerrainViewerSelfTest.Require(_gameBarPanel is { Visible: true } && AreaGameBarPanel.BottomInset > 0, $"{rules}: the bars show before the dialog");
                        using var barSession = ConversationSessionFixture.Create();
                        var barState = new AreaConversationState(barSession, new(1), ["oid:synthetic-0"], 50);
                        var barDialog = DialogConversation.Start(DlgReader.Read(System.Text.Encoding.Latin1.GetBytes(TerrainViewerSelfTest.DigitDialog)),
                            new DialogConversationRequest(new DialogNodeId(1),
                                new DialogParticipants(new DialogId(5), AreaConversationState.PcParticipant, AreaConversationState.Npc(0)),
                                new DialogScriptLine(4), AreaConversationProfile.Diagnostic.Pc, AreaConversationProfile.Diagnostic.Names),
                            new DialogConversationPorts(barState, barState, barSession.Flags, barState));
                        ShowConversationPanel("Bars test", barDialog, null);
                        SyncGameBar();
                        TerrainViewerSelfTest.Require(ConversationOpen && (_gameBarPanel is { Visible: true } && AreaGameBarPanel.BottomInset > 0) == stays,
                            $"{rules}: the bars {(stays ? "stay" : "hide")} under the open dialog (bars={_gameBarPanel?.Visible} inset={AreaGameBarPanel.BottomInset})");
                        // A 100 px tall panel lies above the bottom bar when the bars show and 24 px above the view's bottom when not.
                        var view = GetViewport().GetVisibleRect().Size;
                        var at = AreaConversationPanel.PanelPosition(view, 752, 100);
                        TerrainViewerSelfTest.Require(stays ? at.Y + 100 <= view.Y - AreaGameBarPanel.BottomInset && at.Y >= AreaGameBarPanel.TopInset : Math.Abs(at.Y - (view.Y - 124)) < 1f,
                            $"{rules}: the dialog panel lies in the part the bars leave free (y {at.Y} view {view.Y} insets {AreaGameBarPanel.TopInset}/{AreaGameBarPanel.BottomInset})");
                        // DB2: the clock and the pane buttons are consumed without effect while the dialog is open.
                        var presses = _skillBarPressesForSelfTest + _spellBarPressesForSelfTest;
                        ClockButton(); SkillBarReleased(0, true); SpellBarSpellReleased(0, true);
                        TerrainViewerSelfTest.Require(ClockLineForSelfTest is null && _skillBarPressesForSelfTest + _spellBarPressesForSelfTest == presses, $"{rules}: the clock and the pane buttons do nothing under the dialog");
                        CloseWindowMode(InterfaceMode.Dialog);
                        TerrainViewerSelfTest.Require(!ConversationOpen, $"{rules}: the dialog closed");
                        SyncGameBar();
                    }
                }
                finally { session.SelectRules(priorRules); _gameBarForSelfTest = priorBars; SyncGameBar(); }
            }
            // ARC-UI-2019: with a numbered conversation open the digit keys choose a reply (an unknown number and Enter do nothing).
            {
                using var digitSession = ConversationSessionFixture.Create();
                var state = new AreaConversationState(digitSession, new(1), ["oid:synthetic-0"], 50);
                var digitDialog = DialogConversation.Start(DlgReader.Read(System.Text.Encoding.Latin1.GetBytes(TerrainViewerSelfTest.DigitDialog)),
                    new DialogConversationRequest(new DialogNodeId(1),
                        new DialogParticipants(new DialogId(5), AreaConversationState.PcParticipant, AreaConversationState.Npc(0)),
                        new DialogScriptLine(4), AreaConversationProfile.Diagnostic.Pc, AreaConversationProfile.Diagnostic.Names),
                    new DialogConversationPorts(state, state, digitSession.Flags, state));
                ShowConversationPanel("Digit test", digitDialog, null);
                TerrainViewerSelfTest.Require(ConversationOpen && DialogRepliesNumbered && _conversationPanel!.Conversation == digitDialog
                    && AreaConversationPanel.ReplyTextForSelfTest(_conversationPanel.ButtonsForSelfTest[0]).StartsWith("1. "), "the Steamwoven panel numbers its replies");
                Press(Key.Key9); Press(Key.Enter);
                TerrainViewerSelfTest.Require(digitDialog.CurrentNode.Value == 1, "an unknown digit and Enter choose nothing");
                Press(Key.Key1);
                TerrainViewerSelfTest.Require(digitDialog.CurrentNode.Value == 10, "the digit key 1 chose the first reply");
                CloseWindowMode(InterfaceMode.Dialog);
            }
            // ARC-UI-2028: the reply that leads to a final line closes the window in the same call and floats the line over the speaker as a timed bubble.
            {
                using var finalSession = ConversationSessionFixture.Create();
                var state = new AreaConversationState(finalSession, new(1), ["oid:synthetic-0"], 50);
                var finalDialog = DialogConversation.Start(DlgReader.Read(System.Text.Encoding.Latin1.GetBytes(TerrainViewerSelfTest.FinalLineDialog)),
                    new DialogConversationRequest(new DialogNodeId(1),
                        new DialogParticipants(new DialogId(5), AreaConversationState.PcParticipant, AreaConversationState.Npc(0)),
                        new DialogScriptLine(4), AreaConversationProfile.Diagnostic.Pc, AreaConversationProfile.Diagnostic.Names),
                    new DialogConversationPorts(state, state, finalSession.Flags, state));
                ShowConversationPanel("Final line test", finalDialog, null);
                _conversationSpeaker = _selfTestStandInSpeaker;
                var speakerKey = AreaConversationState.EntityId(_selfTestStandInSpeaker!.Row);
                Press(Key.Key1);
                var bubble = _floaters?.Bubbles.Of(speakerKey);
                TerrainViewerSelfTest.Require(finalDialog.End?.Kind == DialogEndKind.NpcFinalLine && !ConversationOpen && !WorldModal && bubble is { Text: "Fine.", Persistent: false },
                    $"the final line closed the window at once and floats as a timed bubble (open={ConversationOpen} bubble={bubble?.Text})");
                _floaters?.Bubbles.Clear();
            }
        }
        finally { _worldFocusSimulationForSelfTest = simulatedFocus; _selfTestStandInSpeaker = null; }
    }
}
