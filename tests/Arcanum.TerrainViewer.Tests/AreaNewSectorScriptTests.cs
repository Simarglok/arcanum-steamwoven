using System.Buffers.Binary;
using Arcanum.Data.Area;
using Arcanum.Data.Content;
using Arcanum.Data.Scripts;
using Arcanum.Scripting.GlobalFlags;
using Arcanum.Scripting.Scr;
using Arcanum.Session;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-SCRIPT-2822 (ledger TQ7; 0x4F2330 → 0x441980, point 27): the new-sector script of a direct follower through the use-script host (<see cref="AreaUseScriptHost.RunNewSector"/>) over an invented content
/// stack and map (no original data): the hero is the triggerer, the follower the attachee; a follower of the shown map and one carried from another map (its body row from <see cref="AreaScriptWorld.CarriedRow"/>) run it, a
/// follower with no script at the point or no row runs nothing, and the slot's local flags are kept. The opt-in check admits Virgil's installed 1568 whole.</summary>
[TestClass]
public sealed class AreaNewSectorScriptTests
{
    private const byte Number = 3, GlobalFlag = 4, Local = 2, Triggerer = 0, AttacheeRole = 1;
    private const int RanFlag = 2371, PcFlag = 2372, PcType = 15;

    private static AreaObjectKey Key(int index, int map = 1) => new(new string('a', 64), map, AreaSourceKind.Mobile, 1, index, index * 100L, new string((char)('a' + index), 64), 100);

    // An NPC row with a script slot at the given point.
    private static AreaObjectRecord Row(int index, int script, int point = ScrPoint.NewSector, int map = 1, uint? npcFlags = null, int type = 16)
    {
        var fields = new List<AreaFieldValue>
        {
            new(31, AreaPresence.Array, AreaValueOrigin.Instance, null, [new AreaSparseElement(point, [0, 0, script])], null),
            new(18, AreaPresence.Value, AreaValueOrigin.Instance, 0u, [], null),
        };
        if (npcFlags is { } flags) fields.Add(new(69, AreaPresence.Value, AreaValueOrigin.Instance, (long)flags, [], null));
        return new(Key(index, map), 0, type, 0, "p", "p", "r", "s", AreaScope.Core, null, 10 + index, 20, Arcanum.Data.Area.AreaDisposition.BodyEligibleConditionally, [], fields, true, true, true);
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
        var root = Directory.CreateTempSubdirectory("arc-script-2822-");
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
            // 911: sets global flag 2371, and flag 2372 only when the triggerer is a PC (object type 15); runs the default.
            Write("scr/00911newsector.scr", Program(
                Do(new(ScrCatalogue.Action.Assign, (GlobalFlag, RanFlag), N(1))),
                Do(new(ScrCatalogue.Action.GetObjectType, (Triggerer, 0), (Local, 0))),
                If(new(ScrCatalogue.Condition.Equal, (Local, 0), N(PcType)), new(ScrCatalogue.Action.DoNothing), new(ScrCatalogue.Action.Goto, N(4))),
                Do(new(ScrCatalogue.Action.Assign, (GlobalFlag, PcFlag), N(1))),
                Do(new(ScrCatalogue.Action.ReturnRunDefault))));
            // 912: sets the slot's local flag 1 and global flag 2371.
            Write("scr/00912local.scr", Program(
                Do(new(ScrCatalogue.Action.SetLocalFlag, N(1))),
                Do(new(ScrCatalogue.Action.Assign, (GlobalFlag, RanFlag), N(1)))));
            // 913 (the shape of Virgil's 1568): reads the area of its attachee and sets global flag 2371 only in area 7.
            Write("scr/00913area.scr", Program(
                Do(new(ScrCatalogue.Action.GetArea, (AttacheeRole, 0), (Local, 0))),
                If(new(ScrCatalogue.Condition.Equal, (Local, 0), N(7)), new(ScrCatalogue.Action.DoNothing), new(ScrCatalogue.Action.Goto, N(3))),
                Do(new(ScrCatalogue.Action.Assign, (GlobalFlag, RanFlag), N(1)))));
            using var content = ScriptContent.Open(ContentMountPlan.Discover(root.FullName, "Syn").Plan!).Content!;
            test(content);
        }
        finally { root.Delete(true); }
    }

    private static bool Flag(GameSessionOwner session, int flag) =>
        session.Flags.Read(new(new(Guid.NewGuid()), session.FlagVersion, new FlagId(flag))).Value == true;

    private static AreaUseScriptHost Host(GameSessionOwner session, ScriptContent content, Func<ScrObjectRef, AreaObjectRecord?>? carried, params AreaObjectRecord[] shown) =>
        new(session, 1, shown, [], content, new AreaScriptWorld(() => null, _ => false, _ => null, () => null, CarriedRow: carried));

    [TestMethod]
    public void A_follower_of_the_shown_map_runs_its_new_sector_script_with_the_hero_as_the_triggerer()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create();
            var follower = Row(1, 911);
            var host = Host(session, content, null, follower);
            var ran = host.RunNewSector(new ScrObjectRef(1, AreaUseScriptHost.EntityId(follower)), session.Rules.Scripts);
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, ran.Outcome, ran.Detail);
            Assert.AreEqual((ScrOutcome.RunDefault, 911), (ran.Run!.Outcome, ran.Run.Script));
            Assert.IsTrue(Flag(session, RanFlag), "the script ran");
            Assert.IsTrue(Flag(session, PcFlag), "the hero is the triggerer");
        });
    }

    [TestMethod]
    public void A_follower_carried_from_another_map_runs_it_through_its_carried_body_row()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create();
            var body = Row(2, 911, map: 3);
            var carried = new ScrObjectRef(3, AreaUseScriptHost.EntityId(body));
            var host = Host(session, content, obj => obj == carried ? body : null);
            var ran = host.RunNewSector(carried, session.Rules.Scripts);
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, ran.Outcome, ran.Detail);
            Assert.AreEqual(911, ran.Run!.Script);
            Assert.IsTrue(Flag(session, RanFlag), "no map, distance or alive test gates the point");
            // The carried row answers for the running follower only.
            Assert.IsFalse(host.KnowsObject(carried), "the other readers keep their answers for a body outside the window");
        });
    }

    [TestMethod]
    public void A_follower_with_no_script_at_the_point_or_no_row_runs_nothing()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create();
            var otherPoint = Row(3, 911, ScrPoint.Heartbeat);
            var host = Host(session, content, null, otherPoint);
            var rules = session.Rules.Scripts;
            var noPoint = host.RunNewSector(new ScrObjectRef(1, AreaUseScriptHost.EntityId(otherPoint)), rules);
            Assert.IsNull(noPoint.Run);
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, noPoint.Outcome);
            var noRow = host.RunNewSector(new ScrObjectRef(5, "nobody"), rules);
            Assert.IsNull(noRow.Run);
            Assert.IsFalse(Flag(session, RanFlag), "nothing ran");
        });
    }

    [TestMethod]
    public void Get_area_is_admitted_and_answers_the_shown_maps_area()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create();
            var follower = Row(6, 913);
            AreaUseScriptHost With(int area) => new(session, 1, [follower], [], content, new AreaScriptWorld(() => null, _ => false, _ => null, () => null, MapArea: () => (area, false)));
            var attachee = new ScrObjectRef(1, AreaUseScriptHost.EntityId(follower));
            var elsewhere = With(3).RunNewSector(attachee, session.Rules.Scripts);
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, elsewhere.Outcome, elsewhere.Detail);
            Assert.IsFalse(Flag(session, RanFlag), "area 3 is not 7");
            var here = With(7).RunNewSector(attachee, session.Rules.Scripts);
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, here.Outcome, here.Detail);
            Assert.IsTrue(Flag(session, RanFlag), "area 7");
        });
    }

    [TestMethod]
    public void The_slots_local_flags_are_kept_for_the_next_sector_entry()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create();
            var follower = Row(4, 912);
            var host = Host(session, content, null, follower);
            var attachee = new ScrObjectRef(1, AreaUseScriptHost.EntityId(follower));
            Assert.AreEqual(0u, host.Slot(attachee, ScrPoint.NewSector)!.Value.LocalFlags);
            var ran = host.RunNewSector(attachee, session.Rules.Scripts);
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, ran.Outcome, ran.Detail);
            Assert.AreNotEqual(0u, host.Slot(attachee, ScrPoint.NewSector)!.Value.LocalFlags, "the local flag set by the run is the slot's now");
        });
    }

    [TestMethod]
    public void A_step_across_a_sector_border_crosses_one_and_a_step_inside_a_sector_does_not()
    {
        Assert.IsTrue(Arcanum.World.AreaMapTransfers.CrossesSector(new(63, 10), new(64, 10)));
        Assert.IsTrue(Arcanum.World.AreaMapTransfers.CrossesSector(new(10, 64), new(10, 63)));
        Assert.IsFalse(Arcanum.World.AreaMapTransfers.CrossesSector(new(10, 10), new(11, 10)));
        Assert.IsFalse(Arcanum.World.AreaMapTransfers.CrossesSector(new(10, 10), new(10, 10)), "an arrival has no border; the viewer enters its sector on arrival anyway");
    }

    /// <summary>Opt-in (<c>DAT_ROOT</c>, the game folder): Virgil's installed new-sector script 1568 is admitted whole by the set the point runs under and runs for a follower (numbers only; no text is read or printed).</summary>
    [TestMethod, TestCategory("RealData")]
    public void Virgils_installed_script_1568_is_admitted_and_runs_at_the_new_sector_point()
    {
        var root = Environment.GetEnvironmentVariable("DAT_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Fail("DAT_ROOT is required for the opt-in check; this is not a synthetic pass.");
        using var content = ConversationSessionFixture.Scripts(root);
        var read = content.Script(1568);
        Assert.AreEqual(ScriptContentStatus.Read, read.Status, $"script 1568 {read.Detail}");
        Console.WriteLine("script 1568 lines (condition/true/false types): " + string.Join(" ", read.Program!.Lines.Select(l => $"{l.Number}:{l.Condition.Type}/{l.OnTrue.Type}/{l.OnFalse.Type}")));
        var admission = ScrAdmission.Admit(read.Program!, AreaUseScriptHost.NewSectorAdmission);
        Assert.IsTrue(admission.IsAdmitted, $"script 1568 is admitted whole: {admission.Refusal}");

        using var session = ConversationSessionFixture.CreateWithHero();
        var follower = Row(1, 1568);
        var host = new AreaUseScriptHost(session, 1, [follower], [], content, new AreaScriptWorld(() => null, _ => false, _ => null, () => null, MapArea: () => (0, false)));
        var ran = host.RunNewSector(new ScrObjectRef(1, AreaUseScriptHost.EntityId(follower)), session.Rules.Scripts);
        Console.WriteLine($"script 1568: outcome {ran.Outcome}, run {ran.Run?.Outcome}/{ran.Run?.Stop}, lines {ran.Run?.Trace.Count}, detail {ran.Detail}");
        Assert.IsNotNull(ran.Run, ran.Detail);
        Assert.AreNotEqual(AreaScriptedUseOutcome.Refused, ran.Outcome, ran.Detail);
    }

    [TestMethod]
    public void A_monster_generator_follower_runs_no_script_and_answers_run_default()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create();
            // NPC flag 0x80000 (the generator's mark, 0x4BA5B0) stops the interpreter's entry (0x4449B0) before the script; other NPC flag bits do not.
            var generator = Row(1, 911, npcFlags: 0x80000 | 0x4);
            var plain = Row(2, 911, npcFlags: 0x4);
            var notNpc = Row(3, 911, npcFlags: 0x80000, type: 2);
            var host = Host(session, content, null, generator, plain, notNpc);
            Assert.IsTrue(host.IsMonsterGenerator(new ScrObjectRef(1, AreaUseScriptHost.EntityId(generator))));
            Assert.IsFalse(host.IsMonsterGenerator(new ScrObjectRef(1, AreaUseScriptHost.EntityId(plain))));
            Assert.IsFalse(host.IsMonsterGenerator(new ScrObjectRef(1, AreaUseScriptHost.EntityId(notNpc))), "the NPC flag field is read for an NPC only");
            var ran = host.RunNewSector(new ScrObjectRef(1, AreaUseScriptHost.EntityId(generator)), session.Rules.Scripts);
            Assert.AreEqual(ScrOutcome.RunDefault, ran.Run!.Outcome, ran.Detail);
            Assert.AreEqual(ScrStop.MonsterGenerator, ran.Run.Stop);
            Assert.IsFalse(Flag(session, RanFlag), "a generator's script does not run");
            var other = host.RunNewSector(new ScrObjectRef(1, AreaUseScriptHost.EntityId(plain)), session.Rules.Scripts);
            Assert.AreEqual(911, other.Run!.Script);
            Assert.IsTrue(Flag(session, RanFlag), "an NPC without the mark runs it");
        });
    }

    [TestMethod]
    public void The_follower_point_runs_for_a_sector_border_and_an_arrival_only()
    {
        var a = new Arcanum.Data.Traversal.AreaStepCell(10, 10);
        // A crossing of a sector border (64 tiles wide) and an arrival (from equal to to) enter a sector; a step inside one sector and a path-walk tile do not.
        Assert.IsTrue(AreaMapTransfers.RunsFollowerNewSector(new(63, 10), new(64, 10), false));
        Assert.IsTrue(AreaMapTransfers.RunsFollowerNewSector(a, a, false), "the arrival, teleport landing and placement of mode 2");
        Assert.IsFalse(AreaMapTransfers.RunsFollowerNewSector(new(10, 10), new(11, 10), false), "a step inside one sector is mode 1");
        Assert.IsFalse(AreaMapTransfers.RunsFollowerNewSector(new(63, 10), new(64, 10), true), "the spell path walk runs the tile scripts only");
        Assert.IsFalse(AreaMapTransfers.RunsFollowerNewSector(a, a, true));
    }
}
