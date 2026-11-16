using System.Buffers.Binary;
using System.Text;
using Arcanum.Data.Area;
using Arcanum.Data.Content;
using Arcanum.Data.Scripts;
using Arcanum.Scripting.Scr;
using Arcanum.Session;
using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-SCRIPT-2198: the state actions a talk's dialog script holds while the shown map's use host answers (<see cref="AreaScriptHost.TalkStateActions"/>): loops, play script eye candy, change script
/// of another point of an object, critter disband and create item through the use host, a transform refused whole, and the movie and end-game actions of the endings. Invented content stack and map; no original data.</summary>
[TestClass]
public sealed class AreaTalkStateTests
{
    private const byte Triggerer = 0, Attachee = 1, Local = 2, Number = 3, GlobalFlag = 4;
    private const int Map = 1, AreaFlag = 100;

    private static AreaObjectKey Key(int index) => new(new string('a', 64), 1, AreaSourceKind.Mobile, 1, index, index * 100L, new string((char)('a' + index), 64), 100);

    private static AreaObjectRecord Row(int index, int dialog)
    {
        var fields = new List<AreaFieldValue> { new(31, AreaPresence.Array, AreaValueOrigin.Instance, null, [new AreaSparseElement(ScrPoint.Dialog, [0, 0, dialog]), new AreaSparseElement(ScrPoint.Use, [0, 0, 5000])], null) };
        return new(Key(index), 0, 16, 0, "p", "p", "r", "s", AreaScope.Core, null, 10 + index, 20, Arcanum.Data.Area.AreaDisposition.BodyEligibleConditionally, [], fields, true, true, true);
    }

    private readonly record struct Clause(int Type, params (byte Kind, int Value)[] Operands);
    private static (byte, int) N(int value) => (Number, value);
    private static (Clause, Clause, Clause) If(Clause condition, Clause onTrue, Clause onFalse) => (condition, onTrue, onFalse);
    private static (Clause, Clause, Clause) Do(Clause action) => (new(0), action, new(0));

    private static byte[] Program(params (Clause Condition, Clause OnTrue, Clause OnFalse)[] lines)
    {
        var bytes = new byte[64 + lines.Length * 132];
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(52), lines.Length);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(56), lines.Length);
        for (var line = 0; line < lines.Length; line++)
            foreach (var (offset, clause) in new[] { (0, lines[line].Condition), (44, lines[line].OnTrue), (88, lines[line].OnFalse) })
            {
                var slot = bytes.AsSpan(64 + line * 132 + offset, 44);
                BinaryPrimitives.WriteInt32LittleEndian(slot, clause.Type);
                for (var i = 0; i < clause.Operands.Length; i++) { slot[4 + i] = clause.Operands[i].Kind; BinaryPrimitives.WriteInt32LittleEndian(slot[(12 + 4 * i)..], clause.Operands[i].Value); }
            }
        return bytes;
    }

    private static void WithContent(Action<ScriptContent> test)
    {
        var root = Directory.CreateTempSubdirectory("arc-script-2198-");
        try
        {
            void Write(string relative, byte[] bytes)
            {
                var path = Path.Combine(root.FullName, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllBytes(path, bytes);
            }
            Write("tig.dat", AreaOwnerFixture.Dat());
            Write("modules/Syn.dat", AreaOwnerFixture.Dat());
            // 800: change script at the attachee's use point to 4001, critter disband of the attachee (it follows no one), skip default.
            Write("scr/00800state.scr", Program(
                Do(new(ScrCatalogue.Action.ChangeScript, (Attachee, -1), N(ScrPoint.Use), N(4001))),
                Do(new(ScrCatalogue.Action.CritterDisband, (Attachee, -1))),
                Do(new(ScrCatalogue.Action.ReturnSkipDefault))));
            // 801: the attachee transforms into prototype 27368 (Kerghan's change of shape), skip default.
            Write("scr/00801transform.scr", Program(
                Do(new(ScrCatalogue.Action.SetGlobalFlag, N(AreaFlag))),
                Do(new(ScrCatalogue.Action.TransformObject, N(27368))),
                Do(new(ScrCatalogue.Action.ReturnSkipDefault))));
            // 802: create item 8278 in the PC (the installed 1105 line 68's shape), skip default; the invented world holds no item prototype.
            Write("scr/00802create.scr", Program(
                Do(new(ScrCatalogue.Action.SetGlobalFlag, N(AreaFlag))),
                Do(new(ScrCatalogue.Action.CreateItem, N(8278), (Triggerer, -1))),
                Do(new(ScrCatalogue.Action.ReturnSkipDefault))));
            // 803: the ending's shape: a movie through fade (103), the end of the game (132), skip default.
            Write("scr/00803ending.scr", Program(
                Do(new(ScrCatalogue.Action.Fade, N(5), N(0), N(3), N(2))),
                Do(new(ScrCatalogue.Action.EndGame)),
                Do(new(ScrCatalogue.Action.ReturnSkipDefault))));
            // 804: a loop over the attachee's followers with play script eye candy and a loop end, skip default (admission only).
            Write("scr/00804loop.scr", Program(
                Do(new(ScrCatalogue.Action.LoopFor, (2, -1))),
                Do(new(ScrCatalogue.Action.LoopEnd)),
                Do(new(ScrCatalogue.Action.ReturnSkipDefault))));
            using var content = ScriptContent.Open(ContentMountPlan.Discover(root.FullName, "Syn").Plan!).Content!;
            test(content);
        }
        finally { root.Delete(true); }
    }

    private sealed record Talk(GameSessionOwner Session, AreaSpeaker Speaker, AreaScriptHost Host) : IDisposable
    {
        public void Dispose() => Session.Dispose();
        public ScrRunResult Run() => ScrDispatcher.RunObject(new(ScrPoint.Dialog, Host.Object(Speaker), Host.Pc, null), Host,
            new() { Rules = Session.Rules.Scripts, Admission = Host.RunAdmission });
        public bool Flag => Host.GlobalFlag(AreaFlag);
    }

    private static Talk Create(ScriptContent content, int dialog, bool useHost = true)
    {
        var session = ConversationSessionFixture.Create(Map);
        var row = Row(1, dialog);
        var speaker = new AreaSpeaker(0, row, dialog, row.WorldX!.Value, row.WorldY!.Value);
        var map = new SessionMapId(Map);
        var state = new AreaConversationState(session, map, [AreaConversationState.EntityId(row)], 50);
        var use = new AreaUseScriptHost(session, Map, [row], [], content, new AreaScriptWorld(() => new(12, 20), _ => false, _ => null, () => null));
        var host = new AreaScriptHost(session, map, [speaker], state, content) { QueryHost = useHost ? () => use : null };
        return new(session, speaker, host);
    }

    [TestMethod]
    public void A_change_script_of_another_point_and_a_disband_apply_through_the_use_host()
    {
        WithContent(content =>
        {
            using var talk = Create(content, 800);
            var run = talk.Run();
            Assert.AreEqual((ScrOutcome.SkipDefault, ScrStop.ReturnSkipDefault), (run.Outcome, run.Stop), run.Refusal?.ToString());
            Assert.AreEqual(1, run.Effects.SlotWrites.Count);
            var attachee = talk.Host.Object(talk.Speaker);
            Assert.AreEqual(5000, talk.Host.Slot(attachee, ScrPoint.Use)!.Value.Script);
            Assert.IsTrue(talk.Host.Apply(run.Effects));
            Assert.AreEqual(4001, talk.Host.Slot(attachee, ScrPoint.Use)!.Value.Script, "the use slot's script is the written one");
        });
    }

    [TestMethod]
    public void A_transform_refuses_the_run_whole_before_anything_applies()
    {
        WithContent(content =>
        {
            using var talk = Create(content, 801);
            var run = talk.Run();
            Assert.AreEqual(ScrOutcome.SkipDefault, run.Outcome, run.Refusal?.ToString());
            Assert.IsFalse(talk.Host.Apply(run.Effects), "no host changes a critter's prototype in place");
            Assert.IsFalse(talk.Flag, "the flag before the transform was not applied: the run is refused whole");
        });
    }

    [TestMethod]
    public void A_create_item_without_an_item_prototype_refuses_the_run_whole()
    {
        WithContent(content =>
        {
            using var talk = Create(content, 802);
            var run = talk.Run();
            Assert.AreEqual(ScrOutcome.SkipDefault, run.Outcome, run.Refusal?.ToString());
            Assert.IsFalse(talk.Host.Apply(run.Effects));
            Assert.IsFalse(talk.Flag);
        });
    }

    [TestMethod]
    public void A_movie_and_the_end_of_the_game_reach_the_viewer_through_the_talk_host()
    {
        WithContent(content =>
        {
            using var talk = Create(content, 803);
            var run = talk.Run();
            Assert.AreEqual(ScrOutcome.SkipDefault, run.Outcome, run.Refusal?.ToString());
            Assert.IsTrue(talk.Host.Apply(run.Effects));
            CollectionAssert.AreEqual(new[] { 3 }, talk.Host.TakeFades().Select(f => f.Movie).ToArray());
            Assert.IsTrue(talk.Host.TakeEndGame());
        });
    }

    [TestMethod]
    public void The_loop_and_state_actions_are_admitted_only_while_a_use_host_answers()
    {
        WithContent(content =>
        {
            var loop = content.Script(804).Program!;
            Assert.IsTrue(ScrAdmission.Admit(loop, AreaScriptHost.TalkAdmission).IsAdmitted, "the talk set holds a loop");
            Assert.AreEqual(ScrCatalogue.Action.LoopFor, ScrAdmission.Admit(loop, AreaScriptHost.Admission).Refusal?.Type, "the bare set does not");
            using var talk = Create(content, 800, useHost: false);
            Assert.AreSame(AreaScriptHost.Admission, talk.Host.RunAdmission);
            Assert.AreEqual(ScrCatalogue.Action.ChangeScript, talk.Run().Refusal?.Type);
        });
    }
}
