using System.Buffers.Binary;
using Arcanum.Data.Area;
using Arcanum.Data.Content;
using Arcanum.Data.Scripts;
using Arcanum.Scripting.GlobalFlags;
using Arcanum.Scripting.Scr;
using Arcanum.Session;
using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-WORLD-114: an NPC's heartbeat script through the use-script host (<see cref="AreaUseScriptHost.RunHeartbeat"/>) over an
/// invented content stack and map (no original data): kill (58) goes to the world, toggle state (56) switches the NPC off as a map change
/// that the map's gone set reads, the heartbeat slot is written back, and what the viewer cannot apply leaves the run unapplied. The
/// opt-in check runs the installed Crash Site pre-made heroes' scripts.</summary>
[TestClass]
public sealed class AreaHeartbeatScriptTests
{
    private const byte Attachee = 1, Number = 3, GlobalFlag = 4;
    private const int ChosenFlag = 2350;

    private static AreaObjectKey Key(int index) => new(new string('a', 64), 1, AreaSourceKind.Mobile, 1, index, index * 100L, new string((char)('a' + index), 64), 100);
    private static string Entity(int index) => "oid:" + Key(index).ObjectIdSha256;
    private static ScrObjectRef Ref(int index) => new(1, Entity(index));

    // A row with a heartbeat slot (point 19) and its object flags.
    private static AreaObjectRecord Row(int index, int type, int script, uint objectFlags = 0)
    {
        var fields = new List<AreaFieldValue>
        {
            new(31, AreaPresence.Array, AreaValueOrigin.Instance, null, [new AreaSparseElement(ScrPoint.Heartbeat, [0, 0, script])], null),
            new(18, AreaPresence.Value, AreaValueOrigin.Instance, objectFlags, [], null),
        };
        return new(Key(index), 0, type, 0, "p", "p", "r", "s", AreaScope.Core, null, 10 + index, 20, Arcanum.Data.Area.AreaDisposition.BodyEligibleConditionally,
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
        var root = Directory.CreateTempSubdirectory("arc-world-114-");
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
            // 781: the pre-made heroes' shape: chosen (flag 2350) switches itself off, removes the script and skips the default; else it
            // kills itself and runs the default.
            Write("scr/00781hero.scr", Program(
                If(new(ScrCatalogue.Condition.Equal, (GlobalFlag, ChosenFlag), N(1)), new(ScrCatalogue.Action.DoNothing), new(ScrCatalogue.Action.Goto, N(4))),
                Do(new(ScrCatalogue.Action.ToggleState, (Attachee, -1))),
                Do(new(ScrCatalogue.Action.RemoveThisScript)),
                Do(new(ScrCatalogue.Action.ReturnSkipDefault)),
                Do(new(ScrCatalogue.Action.Kill, (Attachee, -1))),
                Do(new(ScrCatalogue.Action.ReturnRunDefault))));
            // 782: toggles its attachee and runs the default (twice switches it back on).
            Write("scr/00782toggle.scr", Program(Do(new(ScrCatalogue.Action.ToggleState, (Attachee, -1))), Do(new(ScrCatalogue.Action.ReturnRunDefault))));
            // 783: a dialog, outside the heartbeat set.
            Write("scr/00783talk.scr", Program(Do(new(ScrCatalogue.Action.Dialog, (Attachee, -1), N(1))), Do(new(ScrCatalogue.Action.ReturnSkipDefault))));
            using var content = ScriptContent.Open(ContentMountPlan.Discover(root.FullName, "Syn").Plan!).Content!;
            test(content);
        }
        finally { root.Delete(true); }
    }

    private static AreaUseScriptHost Host(GameSessionOwner session, ScriptContent content, List<ScrObjectRef>? kills, params AreaObjectRecord[] shown) =>
        new(session, 1, shown, [], content, new AreaScriptWorld(() => null, _ => false, _ => null, () => null,
            kills is null ? null : obj => { kills.Add(obj); return true; }));

    private static void SetFlag(GameSessionOwner session, int flag)
    {
        var flags = session.Flags;
        Assert.AreEqual(GlobalFlagWriteDisposition.Applied,
            flags.Write(new GlobalFlagWriteRequest(new GlobalFlagRequestId(Guid.NewGuid()), flags.Version, new FlagId(flag), true)).Disposition);
    }

    [TestMethod]
    public void An_unchosen_hero_is_killed_through_the_world_and_keeps_its_script()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create();
            var npc = Row(1, 16, 781);
            var kills = new List<ScrObjectRef>();
            var ran = Host(session, content, kills, npc).RunHeartbeat(npc, ScrPoint.Heartbeat, session.Rules.Scripts);
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, ran.Outcome, ran.Detail);
            Assert.AreEqual(ScrOutcome.RunDefault, ran.Run!.Outcome);
            CollectionAssert.AreEqual(new[] { Ref(1) }, kills);
            Assert.IsFalse(AreaUseScriptHost.SwitchedOff(session, 1, Entity(1)));
            Assert.IsNull(session.QueryMapChange(new SessionMapId(1), MapChangeKey.ForEntity(Entity(1), AreaUseScriptHost.SlotProperty(ScrPoint.Heartbeat, "script"))));
        });
    }

    [TestMethod]
    public void The_chosen_hero_is_switched_off_its_script_removed_and_the_map_no_longer_has_it()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create();
            SetFlag(session, ChosenFlag);
            var npc = Row(1, 16, 781);
            var kills = new List<ScrObjectRef>();
            var host = Host(session, content, kills, npc);
            var targets = new AreaObjectTargets(1, [npc], new Dictionary<AreaObjectKey, (string, SessionOpenableBaseline)>(), null);
            Assert.AreEqual(0, targets.Gone(session).Count);
            var ran = host.RunHeartbeat(npc, ScrPoint.Heartbeat, session.Rules.Scripts);
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, ran.Outcome, ran.Detail);
            Assert.AreEqual(ScrOutcome.SkipDefault, ran.Run!.Outcome);
            Assert.AreEqual(0, kills.Count);
            Assert.IsTrue(AreaUseScriptHost.SwitchedOff(session, 1, Entity(1)));
            CollectionAssert.AreEqual(new[] { npc.Key }, targets.Gone(session).ToArray());
            // The heartbeat slot holds no script now: the next tick runs nothing.
            Assert.AreEqual(new ScrSlot(0, 0, 0), host.Slot(Ref(1), ScrPoint.Heartbeat));
            var again = host.RunHeartbeat(npc, ScrPoint.Heartbeat, session.Rules.Scripts);
            Assert.IsNull(again.Run?.Script, again.Detail);
            Assert.IsTrue(AreaUseScriptHost.SwitchedOff(session, 1, Entity(1)), "still off");
        });
    }

    [TestMethod]
    public void A_second_toggle_switches_the_npc_back_on()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create();
            var npc = Row(1, 16, 782);
            var host = Host(session, content, null, npc);
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, host.RunHeartbeat(npc, ScrPoint.Heartbeat, session.Rules.Scripts).Outcome);
            Assert.IsTrue(AreaUseScriptHost.SwitchedOff(session, 1, Entity(1)));
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, host.RunHeartbeat(npc, ScrPoint.Heartbeat, session.Rules.Scripts).Outcome);
            Assert.IsFalse(AreaUseScriptHost.SwitchedOff(session, 1, Entity(1)));
            Assert.IsNull(session.QueryMapChange(new SessionMapId(1), MapChangeKey.ForEntity(Entity(1), AreaUseScriptHost.OffProperty)), "back to its map data");
        });
    }

    [TestMethod]
    public void What_the_viewer_cannot_apply_leaves_the_run_unapplied()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create();
            // A kill without the world's kill.
            var npc = Row(1, 16, 781);
            var noKill = Host(session, content, null, npc).RunHeartbeat(npc, ScrPoint.Heartbeat, session.Rules.Scripts);
            Assert.AreEqual(AreaScriptedUseOutcome.EffectsRejected, noKill.Outcome, noKill.Detail);
            // A kill the world says it cannot apply (no combat values) is refused before anything applies.
            var kills = new List<ScrObjectRef>();
            var cannot = new AreaUseScriptHost(session, 1, [npc], [], content, new AreaScriptWorld(() => null, _ => false, _ => null, () => null,
                obj => { kills.Add(obj); return true; }, _ => false)).RunHeartbeat(npc, ScrPoint.Heartbeat, session.Rules.Scripts);
            Assert.AreEqual(AreaScriptedUseOutcome.EffectsRejected, cannot.Outcome, cannot.Detail);
            Assert.AreEqual(0, kills.Count);
            // Switching on an NPC its map data has off.
            var off = Row(2, 16, 782, objectFlags: 0x2);
            var on = Host(session, content, null, off).RunHeartbeat(off, ScrPoint.Heartbeat, session.Rules.Scripts);
            Assert.AreEqual(AreaScriptedUseOutcome.EffectsRejected, on.Outcome, on.Detail);
            Assert.IsFalse(AreaUseScriptHost.SwitchedOff(session, 1, Entity(2)));
            // A script outside the heartbeat set, and a row that is no NPC.
            var talker = Row(3, 16, 783);
            Assert.AreEqual(AreaScriptedUseOutcome.Refused, Host(session, content, null, talker).RunHeartbeat(talker, ScrPoint.Heartbeat, session.Rules.Scripts).Outcome);
            var scenery = Row(4, 3, 782);
            Assert.AreEqual(AreaScriptedUseOutcome.Refused, Host(session, content, null, scenery).RunHeartbeat(scenery, ScrPoint.Heartbeat, session.Rules.Scripts).Outcome);
            Assert.Throws<ArgumentOutOfRangeException>(() => Host(session, content, null, npc).RunHeartbeat(npc, ScrPoint.Use, session.Rules.Scripts));
            Assert.AreEqual(0, new AreaObjectTargets(1, [npc, off, talker], new Dictionary<AreaObjectKey, (string, SessionOpenableBaseline)>(), null).Gone(session).Count,
                "nothing was switched off");
        });
    }

    // ---- opt-in: the installed Crash Site ----

    [TestMethod, TestCategory("RealData")]
    public void The_installed_crash_site_kills_the_eleven_unchosen_heroes_and_switches_the_chosen_one_off()
    {
        if (Environment.GetEnvironmentVariable("DAT_ROOT") is not { Length: > 0 } root) { Assert.Inconclusive("DAT_ROOT is required for the opt-in heartbeat check; this is not a synthetic pass."); return; }
        using var content = ConversationSessionFixture.Scripts(root);
        var result = AreaLoadOperation.Execute(1, TerrainViewerArguments.Parse(["--mode", "crash-site-area", "--module", Path.Combine(root, "modules", "Arcanum.dat"),
            "--resource", Path.Combine(root, "arcanum2.dat"), "--source-id", "map-test", "--actor-resource", Path.Combine(root, "arcanum1.dat"),
            "--game-root", root, "--area-map", "Arcanum1-024-fixed"]), default);
        var rows = result.Area!.Snapshot.Objects;
        // The twelve NPCs whose heartbeat (point 19) is 1781 + i, the pre-made hero i's script (character-creation-evidence.md).
        var heroes = rows.Where(r => r.Type == 16 && AreaUseScriptHost.LoadedSlot(r, ScrPoint.Heartbeat) is { Script: >= 1781 and <= 1792 })
            .GroupBy(r => AreaUseScriptHost.EntityId(r)).Select(g => g.First()).OrderBy(r => AreaUseScriptHost.LoadedSlot(r, ScrPoint.Heartbeat)!.Value.Script).ToArray();
        Assert.AreEqual(12, heroes.Length);
        using var session = ConversationSessionFixture.Create(result.Area.Snapshot.RuntimeMap);
        // Solomon Doone (index 5, flag 2355) chosen, as in the owner's report.
        SetFlag(session, 2355);
        var kills = new List<ScrObjectRef>();
        var host = new AreaUseScriptHost(session, result.Area.Snapshot.RuntimeMap, rows, [], content,
            new AreaScriptWorld(() => null, _ => false, _ => null, () => null, obj => { kills.Add(obj); return true; }));
        foreach (var hero in heroes)
        {
            var ran = host.RunHeartbeat(hero, ScrPoint.Heartbeat, session.Rules.Scripts);
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, ran.Outcome, ran.Detail);
        }
        var solomon = AreaUseScriptHost.EntityId(heroes[5]);
        CollectionAssert.AreEquivalent(heroes.Where((_, i) => i != 5).Select(h => AreaUseScriptHost.EntityId(h)).ToArray(), kills.Select(k => k.Code).ToArray());
        Assert.IsTrue(AreaUseScriptHost.SwitchedOff(session, result.Area.Snapshot.RuntimeMap, solomon));
        Assert.AreEqual(1, heroes.Count(h => AreaUseScriptHost.SwitchedOff(session, result.Area.Snapshot.RuntimeMap, AreaUseScriptHost.EntityId(h))));
    }
}
