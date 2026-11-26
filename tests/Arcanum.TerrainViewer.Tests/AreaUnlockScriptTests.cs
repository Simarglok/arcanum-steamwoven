using System.Buffers.Binary;
using Arcanum.Data.Area;
using Arcanum.Data.Content;
using Arcanum.Data.Scripts;
using Arcanum.Data.Traversal;
using Arcanum.Scripting.GlobalFlags;
using Arcanum.Scripting.Scr;
using Arcanum.Session;
using Arcanum.Simulation.Locks;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>
/// ARC-SCRIPT-2860 (TQ11, ledger UL1–UL3): the unlock script (attachment point 3) of a portal or container, which both open attempts (0x4AEB70, 0x4AED80) run before the lock test, through the use-script host
/// (<see cref="AreaUseScriptHost.RunUnlock"/>) over an invented content stack and map (no original data): the opener is the triggerer and the object the attachee; answer 0 refuses the opening, anything else goes on to the lock
/// test (so also for an unlocked object); a busted object and a junk pile open before it is asked; path planning asks it for the walker. The opt-in check admits the installed script 1151 whole.
/// </summary>
[TestClass]
public sealed class AreaUnlockScriptTests
{
    private const byte Triggerer = 0, Number = 3, GlobalFlag = 4, Local = 2;
    private const int PcType = 15, SeenFlag = 2381, OpenerSeenFlag = 2382;

    private static AreaObjectKey Key(int index) => new(new string('a', 64), 1, AreaSourceKind.Mobile, 1, index, index * 100L, new string((char)('a' + index), 64), 100);

    private static AreaObjectRecord Row(int index, int script, int type = 1) =>
        new(Key(index), 0, type, 0, "p", "p", "r", "s", AreaScope.Core, null, 10 + index, 20, Arcanum.Data.Area.AreaDisposition.BodyEligibleConditionally, [],
        [
            new(31, AreaPresence.Array, AreaValueOrigin.Instance, null, [new(ScrPoint.Unlock, [0, 0, script])], null),
            new(18, AreaPresence.Value, AreaValueOrigin.Instance, 0u, [], null),
        ], true, true, true);

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
        var root = Directory.CreateTempSubdirectory("arc-script-2860-");
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
            // 931: records that it ran (global flag 2381) and, when the triggerer is the PC (type 15), also global flag 2382; the answer is skip default.
            Write("scr/00931refuse.scr", Program(
                Do(new(ScrCatalogue.Action.Assign, (GlobalFlag, SeenFlag), N(1))),
                Do(new(ScrCatalogue.Action.GetObjectType, (Triggerer, 0), (Local, 0))),
                If(new(ScrCatalogue.Condition.Equal, (Local, 0), N(PcType)), new(ScrCatalogue.Action.Assign, (GlobalFlag, OpenerSeenFlag), N(1)), new(ScrCatalogue.Action.DoNothing)),
                Do(new(ScrCatalogue.Action.ReturnSkipDefault))));
            // 932: the same record, answer run default.
            Write("scr/00932allow.scr", Program(
                Do(new(ScrCatalogue.Action.Assign, (GlobalFlag, SeenFlag), N(1))),
                Do(new(ScrCatalogue.Action.ReturnRunDefault))));
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
    public void The_opener_is_the_triggerer_and_the_object_the_attachee_and_answer_zero_refuses()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create();
            var door = Row(1, 931);
            var host = Host(session, content, door);
            var ran = host.RunUnlock(AreaUseScriptHost.EntityId(door), host.Pc, session.Rules.Scripts);
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, ran.Outcome, ran.Detail);
            Assert.AreEqual((ScrOutcome.SkipDefault, 931), (ran.Run!.Outcome, ran.Run.Script));
            Assert.IsTrue(Flag(session, SeenFlag), "the script ran");
            Assert.IsTrue(Flag(session, OpenerSeenFlag), "the PC opener is the triggerer");
            Assert.AreEqual(true, AreaUseScriptHost.UnlockRefused(ran));
        });
    }

    [TestMethod]
    public void Run_default_goes_on_to_the_lock_test_and_no_script_runs_nothing()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create();
            var door = Row(1, 932);
            var plain = Row(2, 0);
            var host = Host(session, content, door, plain);
            var ran = host.RunUnlock(AreaUseScriptHost.EntityId(door), host.Pc, session.Rules.Scripts);
            Assert.AreEqual((ScrOutcome.RunDefault, 932), (ran.Run!.Outcome, ran.Run.Script));
            Assert.AreEqual(false, AreaUseScriptHost.UnlockRefused(ran));
            var none = host.RunUnlock(AreaUseScriptHost.EntityId(plain), host.Pc, session.Rules.Scripts);
            Assert.IsNull(none.Run);
            Assert.AreEqual(false, AreaUseScriptHost.UnlockRefused(none), "no attachment: the dispatcher answers 1");
            Assert.IsNull(host.RunUnlock("oid:elsewhere", host.Pc, session.Rules.Scripts).Run);
        });
    }

    [TestMethod]
    public void A_run_that_did_not_finish_is_no_answer()
    {
        using var session = ConversationSessionFixture.Create();
        var host = new AreaUseScriptHost(session, 1, [Row(1, 931)], [], null, new AreaScriptWorld(() => null, _ => false, _ => null, () => null));
        var ran = host.RunUnlock(AreaUseScriptHost.EntityId(Row(1, 931)), host.Pc, session.Rules.Scripts);
        Assert.AreEqual(AreaScriptedUseOutcome.ContentUnavailable, ran.Outcome);
        Assert.IsNull(AreaUseScriptHost.UnlockRefused(ran));
    }

    // ---- portals ----

    private static AreaPortal Portal(string entity, long x, int flags, bool script = true)
    {
        var key = new AreaObjectKey(new string('a', 64), 1, AreaSourceKind.Static, null, 0, x, new string('b', 64), 1);
        return new(key, entity, new(x, 5), new(OpenableKind.Portal, flags, 0, 0), new(true, 2, 7), 10, script, []);
    }

    [TestMethod]
    public void A_portal_opener_runs_the_unlock_script_before_the_lock_test_so_also_for_an_unlocked_door()
    {
        using var session = ConversationSessionFixture.Create();
        var portals = AreaPortals.Of(new(1), [Portal("oid:door", 10, 0)]);
        var door = portals.All.Single();
        var asked = new List<(string, string)>();
        bool? answer = false;
        portals.UnlockScripts = (entity, opener) => { asked.Add((entity, opener)); return answer; };
        Assert.IsNull(portals.OpenRefusal(session, door, "oid:thug"), "run default: an unlocked door opens");
        answer = true;
        Assert.AreEqual(AreaPortalUse.ScriptRefused, portals.OpenRefusal(session, door, "oid:thug"), "0 refuses before the lock test");
        answer = null;
        Assert.AreEqual(AreaPortalUse.ScriptGuarded, portals.OpenRefusal(session, door), "a run that did not finish keeps the old refusal");
        CollectionAssert.AreEqual(new[] { ("oid:door", "oid:thug"), ("oid:door", "oid:thug"), ("oid:door", AreaHeroRunsHost.Hero) }, asked);
        portals.UnlockScripts = null;
        Assert.AreEqual(AreaPortalUse.ScriptGuarded, portals.OpenRefusal(session, door), "no host: the old refusal");
    }

    [TestMethod]
    public void A_script_that_goes_on_leaves_the_lock_test_to_decide()
    {
        using var session = ConversationSessionFixture.Create();
        var portals = AreaPortals.Of(new(1), [Portal("oid:locked", 10, LockRules.LockedBit), Portal("oid:jammed", 20, LockRules.JammedBit)]);
        portals.UnlockScripts = (_, _) => false;
        Assert.AreEqual(AreaPortalUse.Locked, portals.OpenRefusal(session, portals.All.Single(p => p.Entity == "oid:locked")));
        Assert.AreEqual(AreaPortalUse.Jammed, portals.OpenRefusal(session, portals.All.Single(p => p.Entity == "oid:jammed")));
    }

    [TestMethod]
    public void A_busted_portal_opens_before_the_script_is_asked_and_a_portal_without_a_script_never_asks()
    {
        using var session = ConversationSessionFixture.Create();
        var portals = AreaPortals.Of(new(1), [Portal("oid:busted", 10, LockRules.BustedBit), Portal("oid:plain", 20, 0, script: false)]);
        var asked = 0;
        portals.UnlockScripts = (_, _) => { asked++; return true; };
        Assert.IsNull(portals.OpenRefusal(session, portals.All.Single(p => p.Entity == "oid:busted")));
        Assert.IsNull(portals.OpenRefusal(session, portals.All.Single(p => p.Entity == "oid:plain")));
        Assert.AreEqual(0, asked);
    }

    [TestMethod]
    public void The_door_goals_attempt_names_the_opener()
    {
        using var session = ConversationSessionFixture.Create();
        var portals = AreaPortals.Of(new(1), [Portal("oid:door", 10, 0)]);
        var asked = new List<string>();
        portals.UnlockScripts = (_, opener) => { asked.Add(opener); return opener == "oid:thug"; };
        var host = new AreaDoorHost(session, portals, () => [], (_, _) => { });
        Assert.IsTrue(host.DoorRefusesOpen("oid:thug", "oid:door"));
        Assert.IsFalse(host.DoorRefusesOpen(AreaHeroRunsHost.Hero, "oid:door"));
        CollectionAssert.AreEqual(new[] { "oid:thug", AreaHeroRunsHost.Hero }, asked);
    }

    // ---- path planning (UL3) ----

    [TestMethod]
    public void Path_planning_asks_the_script_for_the_walker_for_a_closed_portal_only()
    {
        using var session = ConversationSessionFixture.Create();
        var portals = AreaPortals.Of(new(1), [Portal("oid:door", 10, 0), Portal("oid:busted", 20, LockRules.BustedBit)]);
        var door = portals.All.Single(p => p.Entity == "oid:door");
        var busted = portals.All.Single(p => p.Entity == "oid:busted");
        var asked = new List<string>();
        bool? answer = true;
        portals.UnlockScripts = (_, walker) => { asked.Add(walker); return answer; };
        Assert.AreEqual(AreaPortalPassage.Blocked, portals.Passages(session, "oid:thug")[door.Key], "0 blocks the step");
        Assert.AreEqual(AreaPortalPassage.Open, portals.Passages(session, "oid:thug")[busted.Key], "a busted portal passes before the script is asked");
        answer = false;
        Assert.AreEqual(AreaPortalPassage.Open, portals.Passages(session)[door.Key], "run default: the lock test passes an unlocked door");
        answer = null;
        Assert.AreEqual(AreaPortalPassage.Unresolved, portals.Passages(session)[door.Key], "no answer: the T0 rule stands");
        CollectionAssert.AreEqual(new[] { "oid:thug", AreaHeroRunsHost.Hero, AreaHeroRunsHost.Hero }, asked.ToArray());
        portals.UnlockScripts = null;
        Assert.AreEqual(AreaPortalPassage.Unresolved, portals.Passages(session)[door.Key]);
    }

    [TestMethod]
    public void An_open_portal_is_not_asked()
    {
        using var session = ConversationSessionFixture.Create();
        var portals = AreaPortals.Of(new(1), [Portal("oid:door", 10, 0)]);
        var door = portals.All.Single();
        session.SetOpenableFrame(session.Version, portals.Openable(door), door.Baseline, door.OpenFrame);
        var asked = 0;
        portals.UnlockScripts = (_, _) => { asked++; return true; };
        Assert.AreEqual(AreaPortalPassage.Open, portals.Passages(session)[door.Key]);
        Assert.AreEqual(0, asked);
    }

    // ---- containers ----

    private static AreaFieldValue Field(int slot, long value) => new(slot, AreaPresence.Value, AreaValueOrigin.Instance, value, [], null);

    // A container's common block (as AreaContainerLootTests) with its flags (36), lock key (38) and the unlock script (31: point 3 → script).
    private static AreaObjectRecord Container(int index, long flags, int script) =>
        new(Key(index), 0, 2, 1000, "p", "p", "r", "s", AreaScope.Core, null, 100 + index, 100, Arcanum.Data.Area.AreaDisposition.BodyEligibleConditionally, [],
        [
            Field(0, 0x20000000), Field(18, 0), Field(19, 0), Field(21, 1), Field(22, 2), Field(25, 0), Field(26, 10), Field(27, 0), Field(28, 0), Field(29, 2),
            new AreaFieldValue(30, AreaPresence.Array, AreaValueOrigin.Instance, null, [], null),
            new AreaFieldValue(31, AreaPresence.Array, AreaValueOrigin.Instance, null, [new(ScrPoint.Unlock, [0, 0, script])], null),
            Field(36, flags), Field(38, 0),
        ], true, true, true);

    private static string Entity(int index) => "oid:" + Key(index).ObjectIdSha256;

    // 0 an unlocked container with unlock script 931 (refuses), 1 a locked one with 932 (goes on), 2 a busted one with 931, 3 an unlocked one with 932.
    private static (GameSessionOwner Session, AreaInventoryMap Map, AreaEventWorld World, AreaObjectRecord[] Rows) ContainerMap()
    {
        var session = ConversationSessionFixture.Create();
        var rows = new[] { Container(0, 0, 931), Container(1, LockRules.LockedBit, 932), Container(2, LockRules.BustedBit, 931), Container(3, 0, 932) };
        var seed = new SessionMapInventorySeed(rows.Select(row => new SessionSeedHolder(Entity(row.Key.SourceIndex), SessionHolderKind.Container, false, false, [3], [])).ToArray(), []);
        var map = new AreaInventoryMap(1, seed, rows.ToDictionary(row => Entity(row.Key.SourceIndex)));
        Assert.AreEqual(GameSessionFailure.None, new AreaInventoryHost([map]).Attach(session));
        return (session, map, new AreaEventWorld(() => null, () => null), rows);
    }

    [TestMethod]
    public void A_container_open_runs_the_unlock_script_before_the_lock_test_and_zero_refuses_with_the_locked_sound()
    {
        WithContent(content =>
        {
            var (session, map, world, rows) = ContainerMap();
            using var owned = session;
            var host = Host(session, content, rows);
            var asked = new List<string>();
            AreaScriptedUse Run(AreaObjectRecord row) { asked.Add(AreaUseScriptHost.EntityId(row)); return host.RunUnlock(AreaUseScriptHost.EntityId(row), host.Pc, session.Rules.Scripts); }
            var refused = AreaContainerLoot.Open(session, map, Entity(0), 2, world, unlockScript: Run);
            Assert.AreEqual((AreaContainerOpenOutcome.UnlockScriptRefused, true), (refused.Outcome, refused.LockedSound), refused.Detail);
            Assert.IsNotNull(refused.UnlockScript);
            Assert.IsTrue(Flag(session, OpenerSeenFlag), "the PC opener is the triggerer");
            var goes = AreaContainerLoot.Open(session, map, Entity(3), 2, world, unlockScript: Run);
            Assert.AreEqual(AreaContainerOpenOutcome.Opened, goes.Outcome, "run default on an unlocked container: it opens");
            var locked = AreaContainerLoot.Open(session, map, Entity(1), 2, world, unlockScript: Run);
            Assert.AreEqual(AreaContainerOpenOutcome.Locked, locked.Outcome, "a script that goes on leaves the lock test to decide");
            CollectionAssert.AreEqual(new[] { Entity(0), Entity(3), Entity(1) }, asked);
            Assert.AreEqual(AreaContainerOpenOutcome.UnlockScriptUnsupported, AreaContainerLoot.Open(session, map, Entity(0), 2, world).Outcome, "no host: the old refusal");
            var unfinished = AreaContainerLoot.Open(session, map, Entity(0), 2, world, unlockScript: _ => new(AreaScriptedUseOutcome.Refused, null, [], null, "synthetic"));
            Assert.AreEqual((AreaContainerOpenOutcome.UnlockScriptUnsupported, false), (unfinished.Outcome, unfinished.LockedSound));
        });
    }

    [TestMethod]
    public void A_busted_container_opens_before_the_unlock_script_is_asked()
    {
        var (session, map, world, _) = ContainerMap();
        using var owned = session;
        var asked = 0;
        var open = AreaContainerLoot.Open(session, map, Entity(2), 2, world, unlockScript: _ => { asked++; return new(AreaScriptedUseOutcome.Refused, null, [], null, "synthetic"); });
        Assert.AreEqual(AreaContainerOpenOutcome.Opened, open.Outcome);
        Assert.AreEqual(0, asked);
    }

    // ---- installed data ----

    [TestMethod, TestCategory("RealData")]
    public void The_installed_unlock_script_1151_is_admitted_whole_by_the_unlock_set()
    {
        var root = Environment.GetEnvironmentVariable("DAT_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Fail("DAT_ROOT is required for the opt-in unlock-script check; this is not a synthetic pass.");
        using var content = ConversationSessionFixture.Scripts(root);
        var read = content.Script(1151);
        Assert.AreEqual(ScriptContentStatus.Read, read.Status, read.Detail);
        var admission = ScrAdmission.Admit(read.Program!, AreaUseScriptHost.UnlockAdmission);
        Assert.IsTrue(admission.IsAdmitted, $"script 1151 is admitted whole by the unlock set: {admission.Refusal}");
    }

    /// <summary>ARC-SCRIPT-2860 (AC1): the installed script 1151, on a door row of an invented map, runs through the host's unlock point to its end with the real content (the door's one installed attachment sits on a window of the main map, which the
    /// whole-map loads do not open; the slot census of <c>docs/research/scr-census.md</c> counts it). Opt-in: <c>DAT_ROOT</c>.</summary>
    [TestMethod, TestCategory("RealData")]
    public void The_installed_script_1151_runs_through_the_unlock_point_on_a_door_row()
    {
        var root = Environment.GetEnvironmentVariable("DAT_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Fail("DAT_ROOT is required for the opt-in unlock-script check; this is not a synthetic pass.");
        using var content = ConversationSessionFixture.Scripts(root);
        using var session = ConversationSessionFixture.Create();
        var door = Row(1, 1151);
        var host = Host(session, content, door);
        var ran = host.RunUnlock(AreaUseScriptHost.EntityId(door), host.Pc, session.Rules.Scripts);
        Assert.IsNotNull(ran.Run, $"{ran.Outcome} {ran.Detail}");
        Assert.AreEqual((AreaScriptedUseOutcome.NoTeleport, 1151), (ran.Outcome, ran.Run.Script), ran.Detail);
        Assert.IsNotNull(AreaUseScriptHost.UnlockRefused(ran), ran.Detail);
    }
}
