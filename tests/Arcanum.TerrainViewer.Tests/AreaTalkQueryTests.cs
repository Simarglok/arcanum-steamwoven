using System.Buffers.Binary;
using System.Text;
using Arcanum.Data.Area;
using Arcanum.Data.Content;
using Arcanum.Data.Scripts;
using Arcanum.Scripting.Scr;
using Arcanum.Session;
using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-SCRIPT-050: a talk's dialog script with the shown map's use host answering its queries (<see cref="AreaScriptHost.QueryHost"/>):
/// float line (23) floats its DLG line over the speaker, get area (84) reads the shown map's map-list area, and the set widens only while a use
/// host of the talk's map answers. Invented content stack and map; no original data.</summary>
[TestClass]
public sealed class AreaTalkQueryTests
{
    private const byte Attachee = 1, Local = 2, Number = 3, GlobalFlag = 4;
    private const int Map = 1, AreaFlag = 100;

    private static AreaObjectKey Key(int index) => new(new string('a', 64), 1, AreaSourceKind.Mobile, 1, index, index * 100L, new string((char)('a' + index), 64), 100);

    private static AreaObjectRecord Row(int index, int dialog)
    {
        var fields = new List<AreaFieldValue> { new(31, AreaPresence.Array, AreaValueOrigin.Instance, null, [new AreaSparseElement(ScrPoint.Dialog, [0, 0, dialog])], null) };
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
        var root = Directory.CreateTempSubdirectory("arc-script-050-");
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
            // 790: float line 1 over the attachee, get area of the attachee into local 0, set global flag 100 when it is 7, skip default.
            Write("scr/00790float.scr", Program(
                Do(new(ScrCatalogue.Action.FloatLine, N(1), (Attachee, -1))),
                Do(new(ScrCatalogue.Action.GetArea, (Attachee, -1), (Local, 0))),
                If(new(ScrCatalogue.Condition.Equal, (Local, 0), N(7)), new(ScrCatalogue.Action.SetGlobalFlag, N(AreaFlag)), new(ScrCatalogue.Action.DoNothing)),
                Do(new(ScrCatalogue.Action.ReturnSkipDefault))));
            // Line 1's action field holds spaces only; line 2 acts.
            Write("dlg/00790float.dlg", Encoding.Latin1.GetBytes("{1}{Not now, @pcname@.}{Not now, lady.}{}{}{}{  }\n{2}{Acted.}{}{}{}{}{gf 5 1}\n"));
            // 791: sets global flag 100, then floats line 2 of 790's DLG shape (an action field the viewer does not run).
            Write("scr/00791acting.scr", Program(
                Do(new(ScrCatalogue.Action.SetGlobalFlag, N(AreaFlag))),
                Do(new(ScrCatalogue.Action.FloatLine, N(2), (Attachee, -1))),
                Do(new(ScrCatalogue.Action.ReturnSkipDefault))));
            Write("dlg/00791acting.dlg", Encoding.Latin1.GetBytes("{1}{Plain.}{}{}{}{}{}\n{2}{Acted.}{}{}{}{}{gf 5 1}\n"));
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

    // The PC stands next to the NPC (row cell (11, 20)); the shown map's map-list row has the given area.
    private static Talk Create(ScriptContent content, int dialog, (int Area, bool StartMap)? mapArea, int useMap = Map)
    {
        var session = ConversationSessionFixture.Create(Map);
        var row = Row(1, dialog);
        var speaker = new AreaSpeaker(0, row, dialog, row.WorldX!.Value, row.WorldY!.Value);
        var map = new SessionMapId(Map);
        var state = new AreaConversationState(session, map, [AreaConversationState.EntityId(row)], 50);
        var use = new AreaUseScriptHost(session, useMap, [row], [], content,
            new AreaScriptWorld(() => new(12, 20), _ => false, _ => null, () => null, MapArea: () => mapArea));
        var host = new AreaScriptHost(session, map, [speaker], state, content) { QueryHost = () => use };
        return new(session, speaker, host);
    }

    [TestMethod]
    public void A_float_line_floats_the_speakers_line_and_get_area_reads_the_shown_maps_area()
    {
        WithContent(content =>
        {
            using var talk = Create(content, 790, (7, false));
            Assert.AreSame(AreaScriptHost.TalkAdmission, talk.Host.RunAdmission, "no hero: the talk set without get skill");
            var run = talk.Run();
            Assert.AreEqual((ScrOutcome.SkipDefault, ScrStop.ReturnSkipDefault), (run.Outcome, run.Stop), run.Refusal?.ToString());
            CollectionAssert.AreEqual(new[] { new ScrFloatLine(790, 1, talk.Host.Object(talk.Speaker), talk.Host.Pc) }, run.Effects.Floats.ToArray());
            Assert.IsTrue(talk.Host.Apply(run.Effects));
            Assert.IsTrue(talk.Flag, "get area read 7");
            // The diagnostic profile: a male PC named [PC]; the separators-only action field runs nothing.
            CollectionAssert.AreEqual(new[] { new AreaScriptFloat(talk.Host.Object(talk.Speaker), "Not now, [PC].") }, talk.Host.TakeFloats().ToArray());
            Assert.AreEqual(0, talk.Host.TakeFloats().Count);
        });
    }

    [TestMethod]
    public void Get_area_is_zero_off_the_start_map_and_stops_the_talk_on_it()
    {
        WithContent(content =>
        {
            using (var plain = Create(content, 790, (0, false)))
            {
                Assert.IsTrue(plain.Host.Apply(plain.Run().Effects));
                Assert.IsFalse(plain.Flag);
            }
            // GA3 is not modelled: the start map with area 0, or a world without the map list's row (outside map mode), ends the dispatch at
            // get area with nothing applied (the float line before it included).
            foreach (var mapArea in new (int, bool)?[] { (0, true), null })
            {
                using var stopped = Create(content, 790, mapArea);
                var run = stopped.Run();
                Assert.AreEqual((ScrOutcome.SkipDefault, ScrStop.NotModelled), (run.Outcome, run.Stop), mapArea?.ToString() ?? "no map list");
                Assert.IsTrue(run.Effects.IsEmpty);
            }
        });
    }

    [TestMethod]
    public void A_floated_line_with_an_action_field_leaves_the_run_unapplied()
    {
        WithContent(content =>
        {
            using var talk = Create(content, 791, (7, false));
            var run = talk.Run();
            Assert.AreEqual(1, run.Effects.Floats.Count);
            Assert.IsFalse(talk.Host.Apply(run.Effects));
            Assert.IsFalse(talk.Flag, "nothing applied");
            Assert.AreEqual(0, talk.Host.TakeFloats().Count);
        });
    }

    [TestMethod]
    public void The_talk_set_widens_only_while_a_use_host_of_its_map_answers()
    {
        WithContent(content =>
        {
            using var other = Create(content, 790, (7, false), useMap: Map + 1);
            Assert.AreSame(AreaScriptHost.Admission, other.Host.RunAdmission);
            other.Host.QueryHost = null;
            Assert.AreSame(AreaScriptHost.Admission, other.Host.RunAdmission);
            // Without the query commands the talk is refused at its float line, as before.
            Assert.AreEqual((ScrOutcome.Refused, ScrCatalogue.Action.FloatLine), (other.Run().Outcome, other.Run().Refusal!.Type));
            Assert.IsTrue(AreaScriptHost.Admission.IsSubsetOf(AreaScriptHost.TalkAdmission) && AreaScriptHost.SkillAdmission.IsSubsetOf(AreaScriptHost.SkillTalkAdmission));
            Assert.IsTrue(AreaScriptHost.SkillTalkAdmission.IsSubsetOf(ScrAdmissionSet.S19));
            CollectionAssert.AreEquivalent(new[] { ScrCatalogue.Action.Random, ScrCatalogue.Action.FloatLine, ScrCatalogue.Action.GetArea },
                AreaScriptHost.TalkAdmission.Actions.Except(AreaScriptHost.Admission.Actions).ToArray());
            CollectionAssert.AreEquivalent(ScrAdmissionSet.ObjectQueryConditions.Concat(ScrAdmissionSet.ObjectStateQueryConditions).ToArray(), AreaScriptHost.TalkAdmission.Conditions.Except(AreaScriptHost.Admission.Conditions).ToArray());
        });
    }
}
