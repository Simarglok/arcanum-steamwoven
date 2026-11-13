using System.Buffers.Binary;
using Arcanum.Data.Area;
using Arcanum.Data.Content;
using Arcanum.Data.Scripts;
using Arcanum.Scripting.GlobalFlags;
using Arcanum.Scripting.Scr;
using Arcanum.Session;
using Arcanum.Simulation.Ai;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-MAGIC-1167 (MA-10, ledger RZ1-RZ3): the raise script (point 18, 0x12) of a creature a heal brings back, through the use-script host (<see cref="AreaUseScriptHost.RunRaise"/>) over an invented content stack and map (no original data): the healer is the triggerer, a skip-default answer (or a script that runs past its last line) refuses the raise, run default does not; the opt-in check reads the installed scripts.</summary>
[TestClass]
public sealed class AreaRaiseScriptTests
{
    private const byte Number = 3, GlobalFlag = 4, Local = 2, Triggerer = 0;
    private const int SkipFlag = 2411, DefaultFlag = 2412, PcHealerFlag = 2413, PastEndFlag = 2414, PcType = 15;

    private static AreaObjectKey Key(int index) => new(new string('a', 64), 1, AreaSourceKind.Mobile, 1, index, index * 100L, new string((char)('a' + index), 64), 100);

    // A row with script slots at the given points.
    private static AreaObjectRecord Row(int index, params (int Point, int Script)[] attached)
    {
        var slots = attached.Select(a => new AreaSparseElement(a.Point, [0, 0, a.Script])).ToList();
        var fields = new List<AreaFieldValue>
        {
            new(31, AreaPresence.Array, AreaValueOrigin.Instance, null, slots, null),
            new(18, AreaPresence.Value, AreaValueOrigin.Instance, 0u, [], null),
        };
        return new(Key(index), 0, 16, 0, "p", "p", "r", "s", AreaScope.Core, null, 10 + index, 20, Arcanum.Data.Area.AreaDisposition.BodyEligibleConditionally,
            [], fields, true, true, true);
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
        var root = Directory.CreateTempSubdirectory("arc-magic-1167-");
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
            // 931: sets flag 2411, flag 2413 only when the triggerer (the healer) is a PC (object type 15), then skips the default (refuses the raise).
            Write("scr/00931skip.scr", Program(
                Do(new(ScrCatalogue.Action.Assign, (GlobalFlag, SkipFlag), N(1))),
                Do(new(ScrCatalogue.Action.GetObjectType, (Triggerer, 0), (Local, 0))),
                If(new(ScrCatalogue.Condition.Equal, (Local, 0), N(PcType)), new(ScrCatalogue.Action.DoNothing), new(ScrCatalogue.Action.Goto, N(4))),
                Do(new(ScrCatalogue.Action.Assign, (GlobalFlag, PcHealerFlag), N(1))),
                Do(new(ScrCatalogue.Action.ReturnSkipDefault))));
            // 932: sets flag 2412 and runs the default (allows the raise).
            Write("scr/00932default.scr", Program(Do(new(ScrCatalogue.Action.Assign, (GlobalFlag, DefaultFlag), N(1))), Do(new(ScrCatalogue.Action.ReturnRunDefault))));
            // 933: sets flag 2414 and runs past its last line (the dispatcher's answer is skip default).
            Write("scr/00933end.scr", Program(Do(new(ScrCatalogue.Action.Assign, (GlobalFlag, PastEndFlag), N(1)))));
            using var content = ScriptContent.Open(ContentMountPlan.Discover(root.FullName, "Syn").Plan!).Content!;
            test(content);
        }
        finally { root.Delete(true); }
    }

    private static AreaUseScriptHost Host(GameSessionOwner session, ScriptContent content, params AreaObjectRecord[] shown) =>
        new(session, 1, shown, [], content, new AreaScriptWorld(() => null, _ => false, _ => null, () => null));

    private static bool Flag(GameSessionOwner session, int flag) =>
        session.Flags.Read(new(new(Guid.NewGuid()), session.FlagVersion, new FlagId(flag))).Value == true;

    [TestMethod]
    public void A_skip_default_raise_script_refuses_the_raise_and_the_healer_is_its_triggerer()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create();
            var dead = Row(1, (ScrPoint.Raise, 931));
            var other = Row(2, (ScrPoint.Heartbeat, 931));
            var host = Host(session, content, dead, other);
            var rules = session.Rules.Scripts;
            var id = AreaUseScriptHost.EntityId(dead);

            var byNpc = host.RunRaise(id, new ScrObjectRef(1, AreaUseScriptHost.EntityId(other)), rules);
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, byNpc.Outcome, byNpc.Detail);
            Assert.AreEqual((ScrOutcome.SkipDefault, 931), (byNpc.Run!.Outcome, byNpc.Run.Script));
            Assert.IsTrue(AreaUseScriptHost.RaiseRefuses(byNpc));
            Assert.IsTrue(Flag(session, SkipFlag));
            Assert.IsFalse(Flag(session, PcHealerFlag), "an NPC healer is no PC");

            host.RunRaise(id, host.Pc, rules);
            Assert.IsTrue(Flag(session, PcHealerFlag), "the hero is the triggerer");
        });
    }

    [TestMethod]
    public void A_run_default_raise_script_allows_the_raise_and_one_that_runs_past_its_end_refuses_it()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create();
            var allows = Row(1, (ScrPoint.Raise, 932));
            var runsOut = Row(2, (ScrPoint.Raise, 933));
            var host = Host(session, content, allows, runsOut);
            var rules = session.Rules.Scripts;

            var allowed = host.RunRaise(AreaUseScriptHost.EntityId(allows), host.Pc, rules);
            Assert.AreEqual(ScrOutcome.RunDefault, allowed.Run!.Outcome);
            Assert.IsFalse(AreaUseScriptHost.RaiseRefuses(allowed));
            Assert.IsTrue(Flag(session, DefaultFlag));

            var ranOut = host.RunRaise(AreaUseScriptHost.EntityId(runsOut), null, rules);
            Assert.IsTrue(Flag(session, PastEndFlag));
            Assert.IsTrue(AreaUseScriptHost.RaiseRefuses(ranOut), "past its last line: skip default, as the dispatcher answers");
        });
    }

    [TestMethod]
    public void A_creature_without_a_script_at_the_raise_point_runs_nothing_and_refuses_nothing()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create();
            var none = Row(1);
            var heartbeatOnly = Row(2, (ScrPoint.Heartbeat, 931));
            var host = Host(session, content, none, heartbeatOnly);
            var rules = session.Rules.Scripts;
            foreach (var target in new[] { AreaUseScriptHost.EntityId(none), AreaUseScriptHost.EntityId(heartbeatOnly), "oid:nobody" })
            {
                var ran = host.RunRaise(target, host.Pc, rules);
                Assert.IsNull(ran.Run, target);
                Assert.IsFalse(AreaUseScriptHost.RaiseRefuses(ran), target);
            }
            Assert.IsFalse(Flag(session, SkipFlag));
        });
    }

    [TestMethod, TestCategory("RealData")]
    public void The_installed_raise_scripts_are_admitted_by_the_raise_set()
    {
        // ARC-MAGIC-1167: the 13 scripts the census (script-refusal-census, point 18) finds at the raise point of 15 placed sites.
        var root = Environment.GetEnvironmentVariable("DAT_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Fail("DAT_ROOT is required for the opt-in raise-script check; this is not a synthetic pass.");
        var discovery = ContentMountPlan.Discover(root);
        Assert.AreEqual(ContentMountStatus.Ready, discovery.Status, discovery.Detail);
        var opened = ContentStack.Open(discovery.Plan!);
        Assert.AreEqual(ContentStackStatus.Ready, opened.Status, opened.Detail);
        using var content = ScriptContent.Over(opened.Stack!);
        foreach (var number in new[] { 2053, 2273, 2401, 2408, 2421, 2717, 2790, 3000, 3054, 3075, 3076, 3077, 3078 })
        {
            var read = content.Script(number);
            Assert.AreEqual(ScriptContentStatus.Read, read.Status, number.ToString());
            var admitted = ScrAdmission.Admit(read.Program!, AreaUseScriptHost.DyingAdmission);
            Assert.IsTrue(admitted.IsAdmitted, $"script {number}: {admitted.Refusal}");
        }
    }
}
