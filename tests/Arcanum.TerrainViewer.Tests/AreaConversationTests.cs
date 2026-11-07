using System.Security.Cryptography;
using System.Text;
using Arcanum.Data;
using Arcanum.Data.Area;
using Arcanum.Scripting.Dialog;
using Arcanum.Scripting.Scr;
using Arcanum.Scripting.GlobalFlags;
using Arcanum.Session;
using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

[TestClass]
public sealed class AreaConversationTests
{
    // Invented synthetic records; no original dialogue.
    private const string Synthetic =
        "{1}{Hello @pcname@.}{Hello, madam.}{}{}{}{in -40, gf 7 1}" +
        "{2}{Friend.}{}{1}{re 50, ra 1}{10}{re 70, al +2}" +
        "{3}{Rude.}{}{1}{re -10}{0}{}" +
        "{10}{Then come along.}{}{}{}{}{mm 4}" +
        "{11}{Lead on.}{}{1}{lf 3 0}{0}{lf 3 1, jo 0 20}" +
        "{20}{Together, then.}{}{}{}{}{}" +
        "{21}{e:}{}{1}{}{0}{}";

    // Invented: a second visit whose replies depend on what the first talk left in the session.
    private const string Revisit =
        "{30}{Back again.}{}{}{}{}{}" +
        "{31}{First time here?}{}{1}{gf 7 0}{0}{}" +
        "{32}{We met before.}{}{1}{gf 7 1, lf 3 1, re 60}{0}{}" +
        "{40}{A task for you.}{}{}{}{}{qu 1003 2, ru 1001}";

    private const string Speaker0 = "oid:0000000000000000000000000000000000000000000000000000000000000000";

    [TestMethod]
    public void Diagnostic_state_owns_typed_effects_and_reaction_gates()
    {
        using var session = ConversationSessionFixture.Create();
        var state = State(session, 50);
        var conversation = Start(state, session);

        CollectionAssert.AreEqual(new[] { "1. Friend." }, conversation.Options.Select(AreaConversationText.Option).ToArray());
        conversation.Select(conversation.Options[0].Token);
        Assert.AreEqual(70, state.Reaction(0));
        Assert.AreEqual(2, state.Alignment);
        Assert.IsTrue(state.Areas.SetEquals(new[] { 4 }));
        CollectionAssert.AreEqual(new[] { 40 }, state.Items.ToArray());

        Assert.AreEqual(DialogOptionTransition.Join, conversation.Options[0].Transition);
        conversation.Select(conversation.Options[0].Token);
        Assert.IsTrue(state.Follows(0));
        Assert.IsTrue(state.LocalFlag(0, 3));
        Assert.AreEqual(new DialogNodeId(20), conversation.CurrentNode);
        Assert.AreEqual("1. [generated 'e:' phrase; generic text not loaded]", AreaConversationText.Option(conversation.Options.Single()));
        conversation.Select(conversation.Options[0].Token);
        Assert.AreEqual("[dialog ended; the caller script would continue at SCR line 5 (not executed)]", AreaConversationText.Outcome(conversation));
        CollectionAssert.AreEqual(new[]
        {
            "#0 turn 1: IN item name 40 NPC -> PC [Applied]",
            "#1 turn 1: GF 7 = 1 [Applied]",
            "#2 turn 1: RE target 70 [Applied]",
            "#3 turn 1: AL adjust +2 [Applied]",
            "#4 turn 2: MM area 4 known [Applied]",
            "#5 turn 2: LF 3 = 1 [Applied]",
            "#6 turn 2: JO follow request [Applied]",
        }, conversation.Effects.Select(AreaConversationText.Effect).ToArray());
    }

    [TestMethod]
    public void Low_reaction_and_other_race_change_the_offered_replies()
    {
        using var session = ConversationSessionFixture.Create();
        CollectionAssert.AreEqual(new[] { "Rude." }, Start(State(session, 5), session).Options.Select(o => o.Text).ToArray());
        var elf = Start(State(session, 50), session, race: 2);
        Assert.AreEqual(DialogOptionTextKind.Fallback, elf.Options.Single().TextKind);
    }

    [TestMethod]
    public void Consequences_are_session_state_that_survive_a_map_round_trip_and_a_save()
    {
        using var session = ConversationSessionFixture.Create();
        var state = State(session, 50);
        var before = Start(state, session, Revisit, 30);
        CollectionAssert.AreEqual(new[] { "First time here?" }, before.Options.Select(o => o.Text).ToArray());
        before.Close();

        // The first talk: GF, LF, reaction and the follow consent land in the session, not in the state object.
        var talk = Start(state, session);
        talk.Select(talk.Options[0].Token);
        talk.Select(talk.Options[0].Token);
        Assert.AreEqual(true, Flag(session, 7));
        var entity = new SessionMapId(1);
        CollectionAssert.AreEqual(new[]
        {
            new MapChange(MapChangeKey.ForEntity(Speaker0, AreaConversationState.LocalFlagsProperty), 8),
            new MapChange(MapChangeKey.ForEntity(Speaker0, AreaConversationState.FollowProperty), 1),
        }, session.QueryMapChanges(entity).ToArray());
        // ARC-WORLD-026: the reaction is the session reaction domain's, no longer a map change.
        Assert.AreEqual(new SessionReaction(Speaker0, 70, null), session.QueryReactionRecord(Speaker0));

        // The cave trip and back, then save and load: a new state over the restored session sees the same NPC.
        Assert.AreEqual(GameSessionDisposition.Changed, session.PlaceActor(session.Version, session.QueryActor() with { Map = new(79) }).Disposition);
        Assert.AreEqual(GameSessionDisposition.Changed, session.PlaceActor(session.Version, session.QueryActor() with { Map = entity }).Disposition);
        using var loaded = ConversationSessionFixture.SaveAndLoad(session);
        var after = State(loaded, 50);
        Assert.IsTrue(after.Follows(0));
        Assert.IsTrue(after.LocalFlag(0, 3));
        Assert.AreEqual(70, after.Reaction(0));
        Assert.AreEqual(0, after.Alignment, "alignment is not session state yet");

        // The next conversation offers what the first one unlocked.
        var revisit = Start(after, loaded, Revisit, 30);
        CollectionAssert.AreEqual(new[] { "We met before." }, revisit.Options.Select(o => o.Text).ToArray());
    }

    [TestMethod]
    public void Quests_and_rumors_follow_the_session_rules_and_lift_the_reaction()
    {
        using var session = ConversationSessionFixture.Create();
        var state = State(session, 30);
        var task = Start(state, session, Revisit, 40);
        Assert.AreEqual(DialogConversationPhase.AwaitingSelection, task.Phase, task.Failure?.Kind.ToString());
        Assert.AreEqual(new SessionQuest(1003, SessionQuestState.Accepted, false, 54_000_000), session.QueryQuest(1003));
        Assert.AreEqual(54_000_000, session.QueryRumor(1001));
        Assert.AreEqual(AreaConversationState.AcceptedQuestReaction, state.Reaction(0), "accepting a quest lifts the reaction to 41");
        CollectionAssert.AreEqual(new[] { "#0 turn 1: QU 1003 -> 2 [Applied]", "#1 turn 1: RU 1001 logged [Applied]" },
            task.Effects.Select(AreaConversationText.Effect).ToArray());

        // Repeating the same talk changes nothing: the quest is not lowered, the rumor keeps its time.
        Assert.AreEqual(GameSessionDisposition.Changed, session.SetQuestState(session.Version, 1003, SessionQuestState.Completed).Disposition);
        var revision = session.Version.Revision;
        var again = Start(state, session, Revisit, 40);
        Assert.AreEqual(DialogConversationPhase.AwaitingSelection, again.Phase);
        Assert.AreEqual(revision, session.Version.Revision);
        Assert.AreEqual(SessionQuestState.Completed, session.QueryQuest(1003)!.Value.Effective);

        // A restored session value this port cannot read is refused when the state is built.
        Assert.AreEqual(GameSessionDisposition.Changed, session.SetMapChange(session.Version, new(1),
            MapChangeKey.ForEntity(Speaker0, AreaConversationState.LocalFlagsProperty), -1).Disposition);
        Assert.ThrowsExactly<ArgumentException>(() => State(session, 30));

        // A quest number outside the source range is refused by the session and fails the dialog.
        var invalid = DialogConversation.Start(DlgReader.Read(Encoding.Latin1.GetBytes("{1}{?}{}{}{}{}{qu 7 1}")), Request(),
            new DialogConversationPorts(state, state, session.Flags, state));
        Assert.AreEqual(DialogConversationFailureKind.CommandRejected, invalid.Failure!.Kind);
    }

    [TestMethod]
    public void Quest_log_conditions_read_the_log_the_QU_action_writes()
    {
        // ARC-DEV-180 (ledger DCC4): qu, qa and qb read the session's quest log through the quest port; the QU action writes the same log.
        using var session = ConversationSessionFixture.Create();
        var state = State(session, 30);
        var ports = new DialogConversationPorts(state, state, session.Flags, state, Quests: new SessionDialogQuestPort(session, AreaConversationState.PcParticipant));
        var document = DlgReader.Read(Encoding.Latin1.GetBytes("{1}{?}{}{}{}{}{}{2}{Always}{}{1}{}{0}{}{3}{Ask}{}{1}{qu1003 2, qa1003 1, qb1003 3}{0}{}{4}{Done}{}{1}{qu1003 4}{0}{}"));
        string[] Offered() => DialogConversation.Start(document, Request(), ports).Options.Select(option => option.Text).ToArray();
        CollectionAssert.AreEqual(new[] { "Always" }, Offered(), "an untouched quest is Unknown");
        Assert.AreEqual(GameSessionDisposition.Changed, session.SetQuestState(session.Version, 1003, SessionQuestState.Accepted).Disposition);
        CollectionAssert.AreEqual(new[] { "Always", "Ask" }, Offered());
        Assert.AreEqual(GameSessionDisposition.Changed, session.SetQuestState(session.Version, 1003, SessionQuestState.Completed).Disposition);
        CollectionAssert.AreEqual(new[] { "Always", "Done" }, Offered(), "completed: not equal 2, not at most 3, equal 4");
        // Without the port the conditions stay outside the subset.
        var plain = DialogConversation.Start(document, Request(), new DialogConversationPorts(state, state, session.Flags, state));
        Assert.IsTrue(plain.Frontier is not null || plain.Options.Any(option => option.Block is not null));
    }

    [TestMethod]
    public void Text_formatting_marks_generated_blocked_and_frontier_states()
    {
        var document = DlgReader.Read(Encoding.Latin1.GetBytes("{1}{?}{}{}{}{}{}{2}{b:}{}{1}{}{0}{}{3}{k:}{}{1}{}{0}{}{4}{x}{}{1}{}{0}{zz 1}"));
        using var session = ConversationSessionFixture.Create();
        var state = State(session, 50);
        var conversation = DialogConversation.Start(document, Request(), new DialogConversationPorts(state, state, session.Flags, state));
        CollectionAssert.AreEqual(new[]
        {
            "1. [generated 'b:' phrase; generic text not loaded]  (not supported here: UnsupportedInteraction)",
            "2. [generated 'k:' phrase; generic text not loaded]",
            "3. x  (not supported here: UnsupportedOperation, record 4 Action op 0)",
        }, conversation.Options.Select(AreaConversationText.Option).ToArray());

        var stopped = DialogConversation.Start(DlgReader.Read(Encoding.Latin1.GetBytes("{1}{?}{}{}{}{}{wa 1}{2}{x}{}{1}{}{0}{}")), Request(),
            new DialogConversationPorts(state, state, session.Flags, state));
        Assert.AreEqual("[stopped at a frontier: UnsupportedOperation at node 1, record 1 Action op 0]", AreaConversationText.Outcome(stopped));
    }

    // Opt-in: ARC_AREA_CONVERSATION_GAME_ROOT names the user-owned installation (read-only). Structural
    // identifiers only; no original text is asserted or printed.
    [TestMethod, TestCategory("RealData")]
    public void Original_virgil_conversation_runs_from_the_scr_entry_to_its_script_exit()
    {
        var root = Environment.GetEnvironmentVariable("ARC_AREA_CONVERSATION_GAME_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Inconclusive("ARC_AREA_CONVERSATION_GAME_ROOT is required.");
        string P(string relative) => Path.Combine(root, relative);
        var dialogHash = Fingerprint(P("arcanum3.dat"));
        Assert.AreEqual("cbadd76bb3df798fa25dcc05871af1ea751c1164fb1426dccc520cd7d9edc499", dialogHash, "pinned dialog archive");
        using var data = AreaOriginalValidator.OpenFiles(P("modules/Arcanum.dat"), P("arcanum1.dat"), P("arcanum2.dat"), P("modules/Arcanum/maps/qrmvs-420-1zjcnpgn"), P("data/proto"));
        var snapshot = data.LoadStrict();
        using var session = ConversationSessionFixture.Create(snapshot.RuntimeMap);
        // ARC-SCRIPT-011: the game's content stack, as the viewer opens it: Virgil's script is Arcanum4.dat's copy.
        using var scripts = ConversationSessionFixture.Scripts(root);
        var host = new AreaConversationHost(snapshot, scripts, session);
        var plan = AreaScenePlan.Create(snapshot);
        var body = plan.Commands.Last(command => command.Identity == host.Speakers[0].Row.Key && !command.Additive);
        Assert.AreSame(host.Speakers[0], host.SpeakerAt(plan.Commands, body.Destination.X + body.Destination.Width / 2.0, body.Destination.Y + body.Destination.Height / 2.0));
        Assert.IsNull(host.SpeakerAt(plan.Commands, body.Destination.X - 1000, body.Destination.Y - 1000));

        Assert.AreEqual(1, host.Speakers.Count);
        Assert.AreEqual(1324, host.Speakers[0].ScriptId);
        Assert.IsNull(host.Nearest(host.Speakers[0].WorldX + 4, host.Speakers[0].WorldY));
        var start = host.Begin(host.Nearest(host.Speakers[0].WorldX + 3, host.Speakers[0].WorldY - 3));
        Assert.AreEqual(AreaConversationStartStatus.Started, start.Status, start.Detail);
        Assert.AreEqual("script 1324 SCR line 4 -> DLG node 1", start.Detail);
        var conversation = start.Conversation!;
        var nodes = new List<int>();
        while (conversation.Phase == DialogConversationPhase.AwaitingSelection && nodes.Count < 64)
        {
            nodes.Add(conversation.CurrentNode.Value);
            var option = conversation.Options.FirstOrDefault(o => o.Block is null && o.Transition == DialogOptionTransition.Direct)
                ?? conversation.Options.First(o => o.Block is null);
            conversation.Select(option.Token);
        }
        CollectionAssert.AreEqual(new[] { 1, 9, 15, 19, 24, 30, 32, 35, 57, 71, 120, 210, 74 }, nodes);
        Assert.AreEqual(new DialogConversationEnd(DialogEndKind.ReturnToScript, new DialogScriptLine(5)), conversation.End);
        CollectionAssert.AreEqual(new[]
        {
            "GlobalFlagWriteCommand", "GlobalFlagWriteCommand", "GlobalFlagWriteCommand", "GiveItemCommand", "GlobalFlagWriteCommand",
            "QuestStateCommand", "RumorLogCommand", "ReactionCommand", "RumorLogCommand", "MarkAreaCommand", "MarkAreaCommand",
            "MarkAreaCommand", "JoinCommand", "MarkAreaCommand", "MarkAreaCommand", "AlignmentCommand",
        }, conversation.Effects.Select(e => e.Command.GetType().Name).ToArray());
        Assert.IsTrue(conversation.Effects.All(e => e.Outcome == DialogCommandAttemptOutcome.Applied));
        Assert.IsTrue(host.State.Follows(0));
        // ARC-SCRIPT-009: the talk ran SAP_DIALOG through the dispatcher (FOLLOWING, LF 31, JILTED, LF 1 false, then the
        // dialog action on line 4); the DLG's return resumes the script on line 5, which returns with skip default.
        Assert.AreEqual((ScrOutcome.SkipDefault, ScrStop.Dialog, 4), (host.LastRun!.Outcome, host.LastRun.Stop, host.LastRun.StopLine));
        CollectionAssert.AreEqual(new[] { 0, 1, 2, 3, 4 }, host.LastRun.Trace.Select(step => step.Line).ToArray());
        var resumed = host.Resume(host.Speakers[0], conversation.End!.ScriptLine!.Value);
        Assert.AreEqual(AreaConversationStartStatus.ScriptEnded, resumed.Status, resumed.Detail);
        Assert.AreEqual("script 1324 ReturnSkipDefault at SCR line 5: skip default", resumed.Detail);
        Assert.IsNull(resumed.Conversation);
        // A resumption the S3 set cannot run (line 16 shows a float line) is refused before anything runs.
        var refused = host.Resume(host.Speakers[0], new DialogScriptLine(16));
        Assert.AreEqual((AreaConversationStartStatus.EntryNotAdmitted, "script 1324 SCR line 16: ActionNotAdmitted type 23"), (refused.Status, refused.Detail));
        // ARC-SCRIPT-006: every flag, the quest, both rumors and the follow consent are session state.
        var written = conversation.Effects.Select(e => e.Command).OfType<GlobalFlagWriteCommand>().ToArray();
        Assert.AreEqual(4, written.Length);
        var story = session.QueryStory();
        Assert.AreEqual((1, 2), (story.Quests.Count, story.Rumors.Count));

        // The cave trip and back, then save and load: a fresh host over the restored session.
        Assert.AreEqual(GameSessionDisposition.Changed, session.PlaceActor(session.Version, session.QueryActor() with { Map = new(79) }).Disposition);
        Assert.AreEqual(GameSessionDisposition.Changed, session.PlaceActor(session.Version, session.QueryActor() with { Map = new(snapshot.RuntimeMap) }).Disposition);
        using var loaded = ConversationSessionFixture.SaveAndLoad(session);
        foreach (var last in written.GroupBy(w => w.Flag).Select(g => g.Last())) Assert.AreEqual(last.Value, Flag(loaded, last.Flag), $"GF {last.Flag}");
        host = new AreaConversationHost(snapshot, scripts, loaded);
        Assert.IsTrue(host.State.Follows(0));

        // Following now: the SCR picks another entry; that follower dialog needs WA, an explicit frontier.
        var again = host.Begin(host.Speakers[0]);
        Assert.AreEqual("script 1324 SCR line 6 -> DLG node 385", again.Detail);
        CollectionAssert.AreEqual(new[] { 0, 6 }, host.LastRun!.Trace.Select(step => step.Line).ToArray());
        Assert.AreEqual(DialogFrontierKind.UnsupportedOperation, again.Conversation!.Frontier!.Kind);
        Assert.AreEqual(0, again.Conversation.Effects.Count);
        Assert.AreEqual(dialogHash, Fingerprint(P("arcanum3.dat")), "input unchanged");

        // ARC-SCRIPT-011: without the game's script content nothing runs; the reason is the load's.
        var none = new AreaConversationHost(snapshot, null, loaded, contentDetail: "ModuleOutsideGameRoot: test");
        Assert.AreEqual((AreaConversationStartStatus.ContentUnavailable, "ModuleOutsideGameRoot: test"), (none.Begin(none.Speakers[0]).Status, none.Begin(none.Speakers[0]).Detail));
        Assert.IsFalse(scripts.Changed);
    }

    private static AreaConversationState State(GameSessionOwner session, int reaction) =>
        new(session, new SessionMapId(1), [Speaker0], reaction);

    private static DialogConversation Start(AreaConversationState state, GameSessionOwner session, int race = 0) =>
        Start(state, session, Synthetic, 1, race);

    private static DialogConversation Start(AreaConversationState state, GameSessionOwner session, string records, int entry, int race = 0) =>
        DialogConversation.Start(DlgReader.Read(Encoding.Latin1.GetBytes(records)), Request(race, entry), new DialogConversationPorts(state, state, session.Flags, state));

    private static bool? Flag(GameSessionOwner session, int flag) =>
        session.Flags.Read(new(new(Guid.NewGuid()), session.FlagVersion, new FlagId(flag))).Value;

    private static DialogConversationRequest Request(int race = 0, int entry = 1) => new(new DialogNodeId(entry),
        new DialogParticipants(new DialogId(5), AreaConversationState.PcParticipant, AreaConversationState.Npc(0)), new DialogScriptLine(4),
        AreaConversationProfile.Diagnostic.Pc with { Race = race }, AreaConversationProfile.Diagnostic.Names);

    private static string Fingerprint(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
}
