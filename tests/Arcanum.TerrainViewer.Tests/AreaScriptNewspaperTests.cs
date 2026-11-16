using System.Buffers.Binary;
using System.Text;
using Arcanum.Data.Area;
using Arcanum.Data.Content;
using Arcanum.Data.Scripts;
using Arcanum.Scripting.Scr;
using Arcanum.Session;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-SCRIPT-1764 (ledger NP9–NP12): the dialog host answers queue newspaper (85) through the session's queue and float newspaper headline (86) through its float list, with the headline of the
/// module's newspaper file; a host without the file floats nothing and stops nothing.</summary>
[TestClass]
public sealed class AreaScriptNewspaperTests
{
    private sealed record Fixture(GameSessionOwner Session, ScrObjectRef Npc, AreaScriptHost Host) : IDisposable
    {
        public void Dispose() => Session.Dispose();
    }

    private static Fixture Create(ScriptContent? content)
    {
        using var source = new AreaOwnerFixture(motionBodies: true);
        var area = source.Owner.LoadStrict();
        var row = area.Objects.First(o => o.WorldX is not null && o.WorldY is not null);
        var scripts = new AreaFieldValue(31, AreaPresence.Array, AreaValueOrigin.Instance, null, [new AreaSparseElement(ScrPoint.Dialog, [4, 0x0201, 777])], null);
        var speaker = new AreaSpeaker(0, row with { EffectiveFields = [.. row.EffectiveFields.Where(f => f.Slot != 31), scripts] }, 777, row.WorldX!.Value, row.WorldY!.Value);
        var session = ConversationSessionFixture.Create(area.RuntimeMap);
        var map = new SessionMapId(area.RuntimeMap);
        var state = new AreaConversationState(session, map, [AreaConversationState.EntityId(speaker.Row)], 50);
        var host = new AreaScriptHost(session, map, [speaker], state, content);
        return new(session, host.Object(speaker), host);
    }

    private const byte Attachee = 1, Number = 3;
    private readonly record struct Clause(int Type, params (byte Kind, int Value)[] Operands);
    private static (byte, int) N(int value) => (Number, value);
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

    // 777, the speaker's dialog script: queues 5030 after the count, 7 at the front (twice: the second finds it), 5030 again, floats the head issue's headline over the attachee and ends skip default.
    private static readonly byte[] NewsScript = Program(
        Do(new(ScrCatalogue.Action.QueueNewspaper, N(5030), N(0))), Do(new(ScrCatalogue.Action.QueueNewspaper, N(7), N(1))), Do(new(ScrCatalogue.Action.QueueNewspaper, N(7), N(1))),
        Do(new(ScrCatalogue.Action.QueueNewspaper, N(5030), N(0))), Do(new(ScrCatalogue.Action.FloatNewspaperHeadline, N(0), (Attachee, 0))), Do(new(ScrCatalogue.Action.ReturnSkipDefault)));

    private static void WithContent(string? newspaper, Action<ScriptContent> test)
    {
        var root = Directory.CreateTempSubdirectory("arc-script-1764-");
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
            Write("scr/00777news.scr", NewsScript);
            if (newspaper is not null) Write("mes/gamenewspaper.mes", Encoding.Latin1.GetBytes(newspaper));
            using var content = ScriptContent.Open(ContentMountPlan.Discover(root.FullName, "Syn").Plan!).Content!;
            test(content);
        }
        finally { root.Delete(true); }
    }

    private const string Headlines = "{5000}{30c Big news at the harbour}{5010}{31 Fire?}";

    private static ScrRunResult Run(Fixture f) =>
        ScrDispatcher.RunObject(new(ScrPoint.Dialog, f.Npc, f.Host.Pc, null), f.Host, new() { Rules = f.Session.Rules.Scripts, Admission = AreaScriptHost.Admission });

    [TestMethod]
    public void The_dialog_sets_hold_both_newspaper_actions_inside_S59()
    {
        foreach (var set in new[] { AreaScriptHost.Admission, AreaScriptHost.SkillAdmission, AreaScriptHost.TalkAdmission, AreaScriptHost.SkillTalkAdmission })
        {
            Assert.IsTrue(set.Actions.Contains(ScrCatalogue.Action.QueueNewspaper) && set.Actions.Contains(ScrCatalogue.Action.FloatNewspaperHeadline), set.Id);
            Assert.IsTrue(set.IsSubsetOfBesidesStores(ScrAdmissionSet.S65), set.Id);
        }
    }

    [TestMethod]
    public void A_dialog_script_queues_issues_by_the_rule_of_0x4BF140_and_floats_the_head_issues_headline()
    {
        WithContent(Headlines, content =>
        {
            using var f = Create(content);
            var run = Run(f);
            Assert.AreEqual((ScrOutcome.SkipDefault, ScrStop.ReturnSkipDefault), (run.Outcome, run.Stop), run.Stop.ToString());
            Assert.AreEqual(5, run.Effects.Newspapers.Count);
            Assert.IsTrue(f.Host.Apply(run.Effects));
            var queue = f.Session.QueryNewspaper();
            // 7 went in behind the head at the front (the second 7 was found: no change); 5030 (from 5000 on, never looked up) queued twice after the count.
            CollectionAssert.AreEqual(new[] { -1, -1, -1, -1, 5000, 7, 5030, 5030 }, queue.Entries.Take(8).ToArray());
            Assert.AreEqual(8, queue.Count);
            // A headline that does not end in a stop gets one and then a space; the float goes through the host's list, once.
            CollectionAssert.AreEqual(new[] { new AreaScriptFloat(f.Npc, "Big news at the harbour. ", f.Host.Pc) }, f.Host.TakeFloats().ToArray());
            Assert.AreEqual(0, f.Host.TakeFloats().Count);
        });
    }

    [TestMethod]
    public void A_headline_that_ends_in_a_question_mark_gets_only_a_space()
    {
        WithContent("{5000}{31 Fire?}", content =>
        {
            using var f = Create(content);
            Assert.IsTrue(f.Host.Apply(Run(f).Effects));
            CollectionAssert.AreEqual(new[] { new AreaScriptFloat(f.Npc, "Fire? ", f.Host.Pc) }, f.Host.TakeFloats().ToArray());
        });
    }

    [TestMethod]
    public void A_module_without_the_newspaper_file_floats_nothing_and_still_queues()
    {
        WithContent(null, content =>
        {
            using var f = Create(content);
            Assert.IsTrue(f.Host.Apply(Run(f).Effects));
            Assert.AreEqual(0, f.Host.TakeFloats().Count, "an empty headline floats nothing");
            Assert.AreEqual(8, f.Session.QueryNewspaper().Count, "the queue requests applied as before");
        });
    }
}
