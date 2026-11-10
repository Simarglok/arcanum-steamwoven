using System.Buffers.Binary;
using Arcanum.Data.Area;
using Arcanum.Data.Content;
using Arcanum.Data.Scripts;
using Arcanum.Scripting.GlobalFlags;
using Arcanum.Scripting.Scr;
using Arcanum.Session;
using Arcanum.TerrainViewer.Core;
using Arcanum.Simulation.Combat;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-DEV-312: a creature's dying script (attachment point 12, 0x45DA57) through the use-script host (<see cref="AreaUseScriptHost.RunDying"/>) over an invented content stack and map (no original data): the
/// killer is the triggerer, the script's global flag write reaches the session (the shape of the installed 1805, 2137 and 2173, which set flag 2365), and skip default (or a script that runs off its end) stops the death
/// processing while run default and no script do not. The opt-in check admits the three installed scripts.</summary>
[TestClass]
public sealed class AreaDyingScriptTests
{
    private const byte Attachee = 1, Number = 3, GlobalFlag = 4, Local = 2, Triggerer = 0;
    private const int PassedFlag = 2365, PcKillFlag = 2366, PcType = 15;

    private static AreaObjectKey Key(int index) => new(new string('a', 64), 1, AreaSourceKind.Mobile, 1, index, index * 100L, new string((char)('a' + index), 64), 100);

    // A row with a script slot at the given point.
    private static AreaObjectRecord Row(int index, int type, int script, int point = ScrPoint.Dying)
    {
        var slots = new List<AreaSparseElement> { new(point, [0, 0, script]) };
        var fields = new List<AreaFieldValue>
        {
            new(31, AreaPresence.Array, AreaValueOrigin.Instance, null, slots, null),
            new(18, AreaPresence.Value, AreaValueOrigin.Instance, 0u, [], null),
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
        var root = Directory.CreateTempSubdirectory("arc-dev-312-");
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
            // 901 (1805's first line): sets global flag 2365 and runs the default. 902: the same and skips the default. 903: only falls off its end after setting a local flag (the original skips the default there too).
            Write("scr/00901pass.scr", Program(Do(new(ScrCatalogue.Action.Assign, (GlobalFlag, PassedFlag), N(1))), Do(new(ScrCatalogue.Action.ReturnRunDefault))));
            Write("scr/00902skip.scr", Program(Do(new(ScrCatalogue.Action.Assign, (GlobalFlag, PassedFlag), N(1))), Do(new(ScrCatalogue.Action.ReturnSkipDefault))));
            Write("scr/00903falloff.scr", Program(Do(new(ScrCatalogue.Action.SetLocalFlag, N(1)))));
            // 904 (the killer test): sets flag 2366 only when the triggerer is a PC (object type 15), then runs the default.
            Write("scr/00904killer.scr", Program(
                Do(new(ScrCatalogue.Action.GetObjectType, (Triggerer, 0), (Local, 0))),
                If(new(ScrCatalogue.Condition.Equal, (Local, 0), N(PcType)), new(ScrCatalogue.Action.DoNothing), new(ScrCatalogue.Action.Goto, N(3))),
                Do(new(ScrCatalogue.Action.Assign, (GlobalFlag, PcKillFlag), N(1))),
                Do(new(ScrCatalogue.Action.ReturnRunDefault))));
            // 905: the quest, reputation and vicinity commands the installed dying scripts use are admitted (reputation of the PC, the loop over everyone near).
            Write("scr/00905rep.scr", Program(
                Do(new(ScrCatalogue.Action.AddReputation, (Triggerer, 0), N(3))),
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
    public void A_dying_script_that_sets_global_flag_2365_sets_it_and_lets_the_death_go_on()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create();
            var npc = Row(1, 16, 901);
            var host = Host(session, content, npc);
            Assert.IsFalse(Flag(session, PassedFlag));
            var ran = host.RunDying(AreaUseScriptHost.EntityId(npc), host.Pc, session.Rules.Scripts);
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, ran.Outcome);
            Assert.AreEqual((ScrOutcome.RunDefault, 901), (ran.Run!.Outcome, ran.Run.Script));
            Assert.IsTrue(Flag(session, PassedFlag), "global flag 2365 is set (the wards' gate of script 1643)");
            Assert.IsFalse(AreaUseScriptHost.DyingStops(ran), "run default: the death processing goes on");
        });
    }

    [TestMethod]
    public void Skip_default_and_a_script_that_runs_off_its_end_stop_the_death_processing_but_their_writes_apply()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create();
            var skips = Row(1, 16, 902);
            var falls = Row(2, 16, 903);
            var host = Host(session, content, skips, falls);
            var rules = session.Rules.Scripts;
            var ran = host.RunDying(AreaUseScriptHost.EntityId(skips), null, rules);
            Assert.IsTrue(AreaUseScriptHost.DyingStops(ran));
            Assert.IsTrue(Flag(session, PassedFlag), "the flag is written either way");
            var off = host.RunDying(AreaUseScriptHost.EntityId(falls), host.Pc, rules);
            Assert.IsTrue(AreaUseScriptHost.DyingStops(off), $"off the end: skip default ({off.Outcome} {off.Run?.Outcome} {off.Run?.Stop} {off.Detail})");
        });
    }

    [TestMethod]
    public void The_killer_is_the_triggerer_and_a_death_with_no_source_has_none()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create();
            var npc = Row(1, 16, 904);
            var killer = Row(2, 16, 0, ScrPoint.Heartbeat);
            var host = Host(session, content, npc, killer);
            var rules = session.Rules.Scripts;
            host.RunDying(AreaUseScriptHost.EntityId(npc), new ScrObjectRef(1, AreaUseScriptHost.EntityId(killer)), rules);
            Assert.IsFalse(Flag(session, PcKillFlag), "an NPC killer is no PC");
            var byHero = host.RunDying(AreaUseScriptHost.EntityId(npc), host.Pc, rules);
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, byHero.Outcome);
            Assert.IsTrue(Flag(session, PcKillFlag), "the hero is the triggerer");
            Assert.IsFalse(AreaUseScriptHost.DyingStops(byHero));
        });
    }

    [TestMethod]
    public void A_kill_through_the_session_runs_the_dying_script_with_the_hero_as_killer_and_sets_flag_2365()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create();
            var passes = Row(1, 16, 901);
            var holds = Row(2, 16, 902);
            var host = Host(session, content, passes, holds);
            session.DyingScript = request => !AreaUseScriptHost.DyingStops(host.RunDying(request.Victim, request.Killer is null ? null : request.Killer == SessionSpellCaster.Hero ? host.Pc : new ScrObjectRef(1, request.Killer), session.Rules.Scripts));
            SessionLifeTarget Target(AreaObjectRecord row) => SessionLifeTarget.Critter(AreaUseScriptHost.EntityId(row), new LifeFacts(), default);
            var killed = session.ApplyDamage(session.Version, Target(passes), new LifeDamage { HitPoints = 10_000 }, killer: SessionSpellCaster.Hero);
            Assert.IsTrue(Flag(session, PassedFlag), "the death script set global flag 2365");
            Assert.IsFalse(killed.DeathStopped);
            Assert.IsNotNull(session.QueryLifeRecord(AreaUseScriptHost.EntityId(passes))!.Value.DeathTimeMs);
            var held = session.Kill(session.Version, Target(holds));
            Assert.IsTrue(held.DeathStopped, "skip default stops the death processing");
            Assert.IsNull(session.QueryLifeRecord(AreaUseScriptHost.EntityId(holds))!.Value.DeathTimeMs);
        });
    }

    [TestMethod]
    public void A_victim_without_a_dying_script_runs_nothing_and_stops_nothing()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create();
            var none = Row(1, 16, 0);
            var other = Row(2, 16, 902, ScrPoint.Heartbeat); // a script at the heartbeat point only
            var host = Host(session, content, none, other);
            var rules = session.Rules.Scripts;
            foreach (var victim in new[] { AreaUseScriptHost.EntityId(none), AreaUseScriptHost.EntityId(other), "oid:nobody" })
            {
                var ran = host.RunDying(victim, host.Pc, rules);
                Assert.IsNull(ran.Run, victim);
                Assert.IsFalse(AreaUseScriptHost.DyingStops(ran), victim);
            }
            Assert.IsFalse(Flag(session, PassedFlag));
        });
    }

    [TestMethod]
    public void The_dying_set_admits_the_commands_of_the_installed_dying_scripts()
    {
        var set = AreaUseScriptHost.DyingAdmission;
        foreach (var action in new[] { ScrCatalogue.Action.Assign, ScrCatalogue.Action.AddReputation, ScrCatalogue.Action.SetPcQuestState, ScrCatalogue.Action.LoopFor, ScrCatalogue.Action.LoopEnd,
                     ScrCatalogue.Action.GetObjectType, ScrCatalogue.Action.Kill, ScrCatalogue.Action.ToggleState, ScrCatalogue.Action.CallScriptEx })
            Assert.IsTrue(set.Actions.Contains(action), "action " + action);
        Assert.IsTrue(set.Conditions.Contains(ScrCatalogue.Condition.PcQuestState));
        Assert.IsTrue(set.FocusKinds.Contains(ScrFocusKind.EveryoneInVicinity));
    }

    [TestMethod]
    public void A_reputation_command_in_a_dying_script_is_admitted_and_an_unapplied_run_stops_nothing()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create();
            var npc = Row(1, 16, 905);
            var host = Host(session, content, npc);
            var ran = host.RunDying(AreaUseScriptHost.EntityId(npc), host.Pc, session.Rules.Scripts);
            // Admitted (not Refused); the fixture session holds no reputation catalogue, so the effects are rejected: a run that is not applied stops nothing.
            Assert.AreEqual(AreaScriptedUseOutcome.EffectsRejected, ran.Outcome, ran.Detail);
            Assert.AreEqual(ScrOutcome.RunDefault, ran.Run!.Outcome);
            Assert.IsFalse(AreaUseScriptHost.DyingStops(ran));
        });
    }

    [TestMethod, TestCategory("RealData")]
    public void The_installed_dying_scripts_1805_2137_and_2173_are_admitted()
    {
        var root = Environment.GetEnvironmentVariable("DAT_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Fail("DAT_ROOT is required for the opt-in dying-script check; this is not a synthetic pass.");
        var discovery = ContentMountPlan.Discover(root);
        Assert.AreEqual(ContentMountStatus.Ready, discovery.Status, discovery.Detail);
        var opened = ContentStack.Open(discovery.Plan!);
        Assert.AreEqual(ContentStackStatus.Ready, opened.Status, opened.Detail);
        using var content = ScriptContent.Over(opened.Stack!);
        foreach (var number in new[] { 1805, 2137, 2173 })
        {
            var read = content.Script(number);
            Assert.AreEqual(ScriptContentStatus.Read, read.Status, number.ToString());
            var admitted = ScrAdmission.Admit(read.Program!, AreaUseScriptHost.DyingAdmission);
            Assert.IsTrue(admitted.IsAdmitted, $"script {number}: {admitted.Refusal}");
            // Each one's first line assigns global flag 2365 (ledger PW4).
            var first = read.Program!.Lines[0].OnTrue;
            Assert.AreEqual(ScrCatalogue.Action.Assign, first.Type, number.ToString());
            Assert.AreEqual((GlobalFlag, PassedFlag), (first.Kind(0), first.Value(0)), number.ToString());
        }
    }
}
