using System.Buffers.Binary;
using Arcanum.Data.Area;
using Arcanum.Data.Content;
using Arcanum.Data.Scripts;
using Arcanum.Scripting.Scr;
using Arcanum.Session;
using Arcanum.Simulation.Traps;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-DEV-260 (ledger OD4, OD15): the host side of an object break over an invented content stack and map (scripts, entities and
/// cells are invented): the BUST script (point 30) of an object with the breaker as its triggerer, the trap the attack fires (a native run's
/// effect with or without the hero, an ordinary trap script) and the order trap first, BUST second.</summary>
[TestClass]
public sealed class AreaObjectBreakHostTests
{
    private const byte Attachee = 1, Number = 3, Triggerer = 0, Extra = 12;
    private const int Scenery = 3, Container = 4, ContainerType = 2, BustFlag = 2301, TrapUse = 1, BustPoint = ScrPoint.Bust;

    private static AreaObjectKey Key(int index) => new(new string('a', 64), 1, AreaSourceKind.Mobile, 1, index, index * 100L, new string((char)('a' + index), 64), 100);

    // A row on (10 + index, 20) with a use slot (point 1) and a BUST slot (point 30); a script of 0 leaves the slot out.
    private static AreaObjectRecord Row(int index, int type, int use, int bust, int destroy = 0)
    {
        var slots = new List<AreaSparseElement>();
        if (use != 0) slots.Add(new(TrapUse, [0, 0, use]));
        if (destroy != 0) slots.Add(new(ScrPoint.Destroy, [0, 0, destroy]));
        if (bust != 0) slots.Add(new(BustPoint, [0, 0, bust]));
        var fields = new List<AreaFieldValue> { new(31, AreaPresence.Array, AreaValueOrigin.Instance, null, slots, null) };
        return new(Key(index), 0, type, 0, "p", "p", "r", "s", AreaScope.Core, null, 10 + index, 20, Arcanum.Data.Area.AreaDisposition.BodyEligibleConditionally,
            [], fields, true, true, true);
    }

    private readonly record struct Clause(int Type, params (byte Kind, int Value)[] Operands);
    private static (byte, int) N(int value) => (Number, value);
    private static (Clause, Clause, Clause) Do(Clause action) => (new(0), action, new(0));

    private static byte[] Program(uint headerFlags, params (Clause Condition, Clause OnTrue, Clause OnFalse)[] lines)
    {
        var bytes = new byte[64 + lines.Length * 132];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(48), headerFlags);
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

    // 30001: a non-magical trap (header 0x1); 30004: one that removes itself (0x4); 709: an ordinary trap script (0x1) that sets flag 2302 and runs
    // the default; 830: a BUST script that sets flag 2301, does 4 damage to its triggerer and runs the default; 831: sets a local flag only.
    private static void WithContent(Action<ScriptContent> test)
    {
        var root = Directory.CreateTempSubdirectory("arc-dev-260-");
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
            var skip = ScrCatalogue.Action.ReturnSkipDefault;
            Write("scr/30001normal.scr", Program(0x1, Do(new(skip))));
            Write("scr/30004fire.scr", Program(0x1 | 0x4, Do(new(skip))));
            Write("scr/00709trap.scr", Program(0x1, Do(new(ScrCatalogue.Action.SetGlobalFlag, N(2302))), Do(new(ScrCatalogue.Action.ReturnRunDefault))));
            Write("scr/00830bust.scr", Program(0, Do(new(ScrCatalogue.Action.SetGlobalFlag, N(BustFlag))),
                Do(new(ScrCatalogue.Action.Damage, (Triggerer, 0), N(4), N(0))), Do(new(ScrCatalogue.Action.ReturnRunDefault))));
            // 832 (ARC-SCRIPT-2338, BS1): a BUST script with the commands of the installed script 1734: set PC quest state for the triggerer, a script eye candy on the attachee and the story's rumor and reputation commands.
            Write("scr/00832story.scr", Program(0, Do(new(ScrCatalogue.Action.SetPcQuestState, (Triggerer, 0), N(1016), N(2))), Do(new(ScrCatalogue.Action.PlayScriptEyeCandy, N(7), (Attachee, -1))),
                Do(new(ScrCatalogue.Action.SetRumor, N(1100), (Triggerer, 0))), Do(new(ScrCatalogue.Action.AddReputation, (Triggerer, 0), N(1005))), Do(new(ScrCatalogue.Action.ReturnRunDefault))));
            // 833 (ARC-SCRIPT-2338, BS2): a script that sets flag 2301 and then destroys its own scenery.
            Write("scr/00833destroy.scr", Program(0, Do(new(ScrCatalogue.Action.SetGlobalFlag, N(BustFlag))), Do(new(ScrCatalogue.Action.Destroy, (Attachee, 0))), Do(new(ScrCatalogue.Action.ReturnRunDefault))));
            // ARC-SCRIPT-2501 (BS3): destroy point scripts of a scenery: 834 sets flag 2303 and runs the default (the object goes), 835 sets flag 2304 and skips the default (the object stays), 836 holds a command no set admits (queue slide).
            // 837 destroys the neighbour scenery given by the extra object (a use script), 838 destroys its own attachee at its destroy point (the guard).
            Write("scr/00834goes.scr", Program(0, Do(new(ScrCatalogue.Action.SetGlobalFlag, N(2303))), Do(new(ScrCatalogue.Action.ReturnRunDefault))));
            Write("scr/00835stays.scr", Program(0, Do(new(ScrCatalogue.Action.SetGlobalFlag, N(2304))), Do(new(skip))));
            Write("scr/00836refused.scr", Program(0, Do(new(ScrCatalogue.Action.QueueSlide, N(1))), Do(new(ScrCatalogue.Action.ReturnRunDefault))));
            Write("scr/00837neighbour.scr", Program(0, Do(new(ScrCatalogue.Action.Destroy, (Extra, -1))), Do(new(ScrCatalogue.Action.ReturnRunDefault))));
            Write("scr/00838itself.scr", Program(0, Do(new(ScrCatalogue.Action.SetGlobalFlag, N(2305))), Do(new(ScrCatalogue.Action.Destroy, (Attachee, 0))), Do(new(ScrCatalogue.Action.ReturnRunDefault))));
            // 839: a destroy point that sets a local flag and skips the default (its slot is kept, the object stays); 840: a destroy point that teleports its scenery (a nested effect no host models: the point's own run is not applied).
            Write("scr/00839localskip.scr", Program(0, Do(new(ScrCatalogue.Action.SetLocalFlag, N(1))), Do(new(skip))));
            Write("scr/00840nestedrefused.scr", Program(0, Do(new(ScrCatalogue.Action.Teleport, (Attachee, -1), N(5000), N(30), N(40))), Do(new(ScrCatalogue.Action.ReturnRunDefault))));
            Write("scr/00831local.scr", Program(0, Do(new(ScrCatalogue.Action.SetLocalFlag, N(1))), Do(new(ScrCatalogue.Action.ReturnRunDefault))));
            using var content = ScriptContent.Open(ContentMountPlan.Discover(root.FullName, "Syn").Plan!).Content!;
            test(content);
        }
        finally { root.Delete(true); }
    }

    private sealed class Probe
    {
        public List<string> Order { get; } = [];
        public List<string> Damaged { get; } = [];
        public AreaUseScriptHost? Host { get; set; }
        public bool ApplyWithoutTriggerer { get; set; } = true;
        public AreaTrapEffects Effects => new((row, run, counters, flags) => { Order.Add($"trap-hero:{run.Script}:bust={Host!.GlobalFlag(BustFlag)}"); return SessionTrapEffectResult.None; }, 0)
        {
            ApplyWithoutTriggerer = ApplyWithoutTriggerer
                ? (row, run, counters, flags) => { Order.Add($"trap-none:{run.Script}:bust={Host!.GlobalFlag(BustFlag)}"); return SessionTrapEffectResult.None; }
                : null,
        };
    }

    private static (AreaUseScriptHost Host, AreaTraps Traps) Hosts(GameSessionOwner session, ScriptContent content, Probe probe, bool castHost, params AreaObjectRecord[] shown)
    {
        var host = new AreaUseScriptHost(session, 1, shown, [], content, new AreaScriptWorld(() => null, _ => false, _ => null, () => null,
            ScriptDamage: (victim, amount, type, unresistable) => { probe.Damaged.Add($"{victim}:{amount}"); return "damaged"; }));
        probe.Host = host;
        var traps = new AreaTraps(session, host, shown, _ => false, () => castHost ? probe.Effects : null);
        host.Traps = traps.Use;
        return (host, traps);
    }

    private const SessionObjectHostSteps Both = SessionObjectHostSteps.TrapFires | SessionObjectHostSteps.BustScript;

    [TestMethod]
    public void A_bust_script_runs_with_the_breaker_as_the_triggerer_and_without_one()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create();
            var window = Row(1, Scenery, 0, 830);
            var probe = new Probe();
            var (host, _) = Hosts(session, content, probe, true, window);
            var rules = session.Rules.Scripts;
            var byHero = host.RunBust(AreaUseScriptHost.EntityId(window), host.Pc, rules);
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, byHero.Outcome, byHero.Detail);
            Assert.IsTrue(host.GlobalFlag(BustFlag));
            CollectionAssert.AreEqual(new[] { $"{Arcanum.Simulation.Ai.AiSchedulerRules.Pc}:4" }, probe.Damaged, "the breaker is the triggerer of the script's action");
            Assert.AreEqual(0, host.RunBust("oid:nobody", host.Pc, rules).Run?.Script ?? 0, "not a row of the shown map");
            Assert.AreEqual("no script at the point", host.RunBust(AreaUseScriptHost.EntityId(Row(2, Scenery, 0, 0)), host.Pc, rules).Detail);
        });
    }

    // ARC-SCRIPT-2338 (BS1): the bust host applies the story effects of the run as the use host does (the quest, rumor and reputation requests for the PC, a script eye candy for the viewer); the heartbeat set alone refuses them.
    [TestMethod]
    public void A_bust_script_applies_the_quest_rumor_and_reputation_commands_and_the_eye_candy_of_its_run()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create();
            var statue = Row(1, Scenery, 0, 832);
            var (host, _) = Hosts(session, content, new Probe(), true, statue);
            var ran = host.RunBust(AreaUseScriptHost.EntityId(statue), host.Pc, session.Rules.Scripts);
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, ran.Outcome, ran.Detail);
            Assert.AreEqual(SessionQuestState.Accepted, session.QueryQuest(1016)!.Value.State);
            Assert.IsNotNull(session.QueryRumor(1100));
            Assert.IsTrue(session.HeroHasReputation(1005));
            var candies = host.TakeEyeCandies();
            Assert.AreEqual(1, candies.Count, "the play script eye candy of the run reaches the viewer's request list");
            Assert.IsFalse(ScrAdmission.Admit(Read(content, 832), AreaUseScriptHost.HeartbeatAdmission).IsAdmitted, "the narrow heartbeat set stays without the quest commands");
            Assert.IsTrue(ScrAdmission.Admit(Read(content, 832), AreaUseScriptHost.BustAdmission).IsAdmitted);
            Assert.IsTrue(AreaUseScriptHost.BustAdmission.Actions.IsSupersetOf(AreaUseScriptHost.HeartbeatAdmission.Actions), "the bust set only adds");
        });
        // A bust with no attacker has no PC triggerer, so the quest, rumor and reputation requests that name the triggerer ask for nobody (fresh session: nothing set before).
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create();
            var statue = Row(1, Scenery, 0, 832);
            var (host, _) = Hosts(session, content, new Probe(), true, statue);
            var nobody = host.RunBust(AreaUseScriptHost.EntityId(statue), null, session.Rules.Scripts);
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, nobody.Outcome, nobody.Detail);
            Assert.IsNull(session.QueryQuest(1016));
            Assert.IsNull(session.QueryRumor(1100));
            Assert.IsFalse(session.HeroHasReputation(1005));
        });
    }

    private static ScrProgram Read(ScriptContent content, int number) => content.Script(number).Program!;

    private static bool Destroyed(GameSessionOwner session, AreaObjectRecord row) =>
        session.QueryObjectCondition(new SessionMapId(1), AreaUseScriptHost.EntityId(row), Arcanum.Simulation.Combat.DamageableObjectKind.Scenery) == SessionObjectCondition.Destroyed;

    // ARC-SCRIPT-2501 (BS3, TD2): a script's destroy of a scenery of the shown map is applied: the session records the scenery destroyed (the view reads it as gone), and the rest of the run applies.
    [TestMethod]
    public void A_script_that_destroys_its_scenery_destroys_it_and_the_run_applies()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create();
            var statue = Row(1, Scenery, 833, 832);
            var (host, _) = Hosts(session, content, new Probe(), true, statue);
            var ran = host.Run(statue, session.Rules.Scripts);
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, ran.Outcome, ran.Detail);
            Assert.IsTrue(host.GlobalFlag(BustFlag), "the run's other effects applied");
            Assert.IsTrue(Destroyed(session, statue));
        });
    }

    // The destroy point (2) of the scenery runs first (TD2: the object is the triggerer and the attachee); a run-default answer lets the destroy go on.
    [TestMethod]
    public void The_destroy_point_of_a_scenery_runs_before_the_destroy_and_a_default_answer_lets_it_go()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create();
            var statue = Row(1, Scenery, 833, 0, destroy: 834);
            var (host, _) = Hosts(session, content, new Probe(), true, statue);
            var ran = host.Run(statue, session.Rules.Scripts);
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, ran.Outcome, ran.Detail);
            Assert.IsTrue(host.GlobalFlag(2303), "the destroy point ran");
            Assert.IsTrue(Destroyed(session, statue));
            // Destroyed once: a second destroy of the same object is left as it is and runs the point no more.
            var again = host.Run(statue, session.Rules.Scripts);
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, again.Outcome, again.Detail);
        });
    }

    // The destroyed scenery need not be the attachee: a use script destroys the extra object, whose destroy point runs with that scenery as triggerer and attachee.
    [TestMethod]
    public void A_script_that_destroys_another_scenery_runs_that_sceneries_destroy_point()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create();
            var lever = Row(1, Scenery, 837, 0);
            var statue = Row(2, Scenery, 0, 0, destroy: 834);
            var (host, _) = Hosts(session, content, new Probe(), true, lever, statue);
            var ran = host.RunUseScript(AreaUseScriptHost.EntityId(lever), host.Pc, host.ObjectRef(AreaUseScriptHost.EntityId(statue)), session.Rules.Scripts);
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, ran.Outcome, ran.Detail);
            Assert.IsTrue(host.GlobalFlag(2303), "the neighbour's destroy point ran");
            Assert.IsTrue(Destroyed(session, statue));
            Assert.IsFalse(Destroyed(session, lever));
        });
    }

    // The destroy point's return 0 (skip default) keeps the object, as 0x43CCA0 returns at once.
    [TestMethod]
    public void A_destroy_point_that_skips_the_default_keeps_the_scenery()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create();
            var statue = Row(1, Scenery, 833, 0, destroy: 835);
            var (host, _) = Hosts(session, content, new Probe(), true, statue);
            var ran = host.Run(statue, session.Rules.Scripts);
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, ran.Outcome, ran.Detail);
            Assert.IsTrue(host.GlobalFlag(2304), "the destroy point ran");
            Assert.IsFalse(Destroyed(session, statue), "the skip default kept the object");
        });
    }

    // A destroy point script no set admits refuses the whole run before anything applies (the object's script would not run).
    [TestMethod]
    public void A_destroy_point_script_the_set_does_not_admit_refuses_the_destroy_run_whole()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create();
            var statue = Row(1, Scenery, 833, 0, destroy: 836);
            var (host, _) = Hosts(session, content, new Probe(), true, statue);
            var ran = host.Run(statue, session.Rules.Scripts);
            Assert.AreEqual(AreaScriptedUseOutcome.EffectsRejected, ran.Outcome, ran.Detail);
            StringAssert.Contains(ran.Detail, "destroy script 836");
            Assert.IsFalse(host.GlobalFlag(BustFlag), "nothing of the refused run applied");
            Assert.IsFalse(Destroyed(session, statue));
        });
    }

    // A destroy point that destroys its own object is left to the running destroy (no second run of the point).
    [TestMethod]
    public void A_destroy_point_that_destroys_its_own_scenery_does_not_run_again()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create();
            var statue = Row(1, Scenery, 833, 0, destroy: 838);
            var (host, _) = Hosts(session, content, new Probe(), true, statue);
            var ran = host.Run(statue, session.Rules.Scripts);
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, ran.Outcome, ran.Detail);
            Assert.IsTrue(host.GlobalFlag(2305));
            Assert.IsTrue(Destroyed(session, statue));
        });
    }

    // The destroy point's own slot is kept as map changes (scr2.*): a point that sets a local flag and skips the default keeps the object and the flag.
    [TestMethod]
    public void A_destroy_point_that_writes_a_local_flag_and_skips_the_default_keeps_the_scenery_and_its_slot()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create();
            var statue = Row(1, Scenery, 833, 0, destroy: 839);
            var (host, _) = Hosts(session, content, new Probe(), true, statue);
            var ran = host.Run(statue, session.Rules.Scripts);
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, ran.Outcome, ran.Detail);
            Assert.IsFalse(Destroyed(session, statue), "the skip default kept the object");
            Assert.AreNotEqual(0u, host.Slot(new ScrObjectRef(1, AreaUseScriptHost.EntityId(statue)), ScrPoint.Destroy)!.Value.LocalFlags, "the destroy slot is kept as map changes (scr2.lf)");
        });
    }

    // A destroy point whose own run is not applied (it teleports a scenery, which no host models) does not destroy the scenery: the outer run fails instead of dropping the point silently.
    [TestMethod]
    public void A_destroy_point_whose_own_run_is_refused_leaves_the_scenery_and_fails_the_run()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create();
            var chest = Row(1, Container, 0, 0);
            var statue = Row(2, Scenery, 0, 0, destroy: 840);
            var lever = Row(3, Scenery, 837, 0);
            var (host, _) = Hosts(session, content, new Probe(), true, chest, statue, lever);
            var ran = host.RunUseScript(AreaUseScriptHost.EntityId(lever), host.Pc, host.ObjectRef(AreaUseScriptHost.EntityId(statue)), session.Rules.Scripts);
            Assert.AreNotEqual(AreaScriptedUseOutcome.NoTeleport, ran.Outcome, "the run fails");
            Assert.IsFalse(Destroyed(session, statue), "the scenery stays");
            Assert.IsFalse(ran.Detail?.Contains("which the destroy-point set does not admit") == true, "the failure is the nested run's, not the plan's: " + ran.Detail);
        });
    }

    // ARC-WORLD-2502: a container of the shown map is destroyed too (its invulnerable items are dropped by the session); a portal is still not modelled: the run is refused whole with the reason.
    [TestMethod]
    public void A_script_that_destroys_a_container_applies_and_one_that_destroys_a_portal_is_still_refused_whole()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create();
            var chest = Row(1, ContainerType, 833, 0);
            var (host, _) = Hosts(session, content, new Probe(), true, chest);
            var ran = host.Run(chest, session.Rules.Scripts);
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, ran.Outcome, ran.Detail);
            Assert.IsTrue(host.GlobalFlag(BustFlag), "the rest of the run applied");
            Assert.AreEqual(SessionObjectCondition.Destroyed, session.QueryObjectCondition(new SessionMapId(1), AreaUseScriptHost.EntityId(chest), Arcanum.Simulation.Combat.DamageableObjectKind.Container));
        });
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create();
            var door = Row(1, 1, 833, 0);
            var (host, _) = Hosts(session, content, new Probe(), true, door);
            var ran = host.Run(door, session.Rules.Scripts);
            Assert.AreEqual(AreaScriptedUseOutcome.EffectsRejected, ran.Outcome, ran.Detail);
            StringAssert.Contains(ran.Detail, "is not modelled (only an NPC, a scenery or a container of the shown map)");
            Assert.IsFalse(host.GlobalFlag(BustFlag), "nothing of the refused run applied");
        });
    }

    // ARC-WORLD-2502 (TD2, DD1): a container's destroy runs its destroy point (2) first, as a scenery's does: a run default destroys it, a skip default keeps it; a destroyed container (a break, an earlier destroy) is left as it is.
    [TestMethod]
    public void A_containers_destroy_point_runs_first_and_a_skip_default_keeps_the_container()
    {
        Arcanum.Simulation.Combat.DamageableObjectKind kind = Arcanum.Simulation.Combat.DamageableObjectKind.Container;
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create();
            var goes = Row(1, ContainerType, 833, 0, destroy: 834);
            var (host, _) = Hosts(session, content, new Probe(), true, goes);
            var ran = host.Run(goes, session.Rules.Scripts);
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, ran.Outcome, ran.Detail);
            Assert.IsTrue(host.GlobalFlag(2303), "the destroy point ran");
            Assert.AreEqual(SessionObjectCondition.Destroyed, session.QueryObjectCondition(new SessionMapId(1), AreaUseScriptHost.EntityId(goes), kind));
        });
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create();
            var stays = Row(1, ContainerType, 833, 0, destroy: 835);
            var (host, _) = Hosts(session, content, new Probe(), true, stays);
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, host.Run(stays, session.Rules.Scripts).Outcome);
            Assert.IsTrue(host.GlobalFlag(2304), "the destroy point ran and skipped the default");
            Assert.AreEqual(SessionObjectCondition.Whole, session.QueryObjectCondition(new SessionMapId(1), AreaUseScriptHost.EntityId(stays), kind), "the container stays");
        });
        // A container that broke earlier (its items spilled) is destroyed already: the destroy point does not run again.
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create();
            var broken = Row(1, ContainerType, 833, 0, destroy: 834);
            var (host, traps) = Hosts(session, content, new Probe(), true, broken);
            var key = AreaUseScriptHost.EntityId(broken);
            Assert.AreEqual(GameSessionDisposition.Changed, session.ApplyChanges(session.Version, null,
                [new(new SessionMapId(1), MapChangeKey.ForEntity(key, GameSessionOwner.ObjectConditionProperty), (long)SessionObjectCondition.Destroyed)]).Disposition);
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, host.Run(broken, session.Rules.Scripts).Outcome);
            Assert.IsFalse(host.GlobalFlag(2303), "TD2: left as it is, its destroy point not run");
        });
    }

    // The bust set holds destroy (68): the container script 2092 loops over the sceneries around it and destroys them.
    [TestMethod]
    public void The_bust_set_admits_destroy_and_the_destroy_point_set_is_the_bust_and_dying_sets_together()
    {
        Assert.IsTrue(AreaUseScriptHost.BustAdmission.Actions.Contains(ScrCatalogue.Action.Destroy));
        Assert.IsFalse(AreaUseScriptHost.HeartbeatAdmission.Actions.Contains(ScrCatalogue.Action.Destroy), "the heartbeat set stays narrow");
        Assert.IsTrue(AreaUseScriptHost.DestroyPointAdmission.Actions.IsSupersetOf(AreaUseScriptHost.BustAdmission.Actions));
        Assert.IsTrue(AreaUseScriptHost.DestroyPointAdmission.Actions.IsSupersetOf(AreaUseScriptHost.DyingAdmission.Actions));
    }

    [TestMethod]
    public void A_bust_script_of_an_object_with_no_attacker_gets_no_triggerer_and_keeps_its_local_flags()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create();
            var statue = Row(1, Scenery, 0, 831);
            var (host, _) = Hosts(session, content, new Probe(), true, statue);
            var ran = host.RunBust(AreaUseScriptHost.EntityId(statue), null, session.Rules.Scripts);
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, ran.Outcome, ran.Detail);
            Assert.AreEqual(831, ran.Run!.Script);
            var slot = host.Slot(new ScrObjectRef(1, AreaUseScriptHost.EntityId(statue)), ScrPoint.Bust)!.Value;
            Assert.AreNotEqual(0u, slot.LocalFlags, "the BUST slot is kept as map changes (scr30.lf)");
        });
    }

    [TestMethod]
    public void An_attack_fires_a_native_trap_before_the_bust_script_and_never_removes_the_trap()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create();
            var chest = Row(1, Container, 30004, 830);
            var probe = new Probe();
            var (host, traps) = Hosts(session, content, probe, true, chest);
            var ran = AreaObjectBreakHost.Run(host, traps, chest, Both, byHero: true, ranged: false, session.Rules.Scripts);
            Assert.IsTrue(ran.Any);
            Assert.AreEqual(SessionTrapCause.Attack, ran.Trap!.Cause);
            Assert.AreEqual(SessionTrapOutcome.Fired, ran.Trap.Outcome);
            Assert.IsNull(ran.TrapScript, "a native trap needs no ordinary script");
            CollectionAssert.AreEqual(new[] { "trap-hero:30004:bust=False" }, probe.Order, "the trap runs first, with the hero as the triggerer");
            Assert.IsTrue(host.GlobalFlag(BustFlag), "then the BUST script");
            Assert.IsFalse(host.TrapState(chest)!.Value.Removed, "an attack's trap stays (OD6)");
            StringAssert.Contains(ran.Describe(), "bust script 830");
        });
    }

    [TestMethod]
    public void A_ranged_attack_and_a_damage_with_no_attacker_fire_the_trap_with_no_triggerer()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create();
            var chest = Row(1, Container, 30001, 830);
            var probe = new Probe();
            var (host, traps) = Hosts(session, content, probe, true, chest);
            var rules = session.Rules.Scripts;
            AreaObjectBreakHost.Run(host, traps, chest, Both, byHero: true, ranged: true, rules);
            Assert.IsTrue(host.GlobalFlag(BustFlag), "the hero still breaks it");
            CollectionAssert.AreEqual(new[] { $"{Arcanum.Simulation.Ai.AiSchedulerRules.Pc}:4" }, probe.Damaged, "a ranged break still names the hero as the BUST breaker");
            probe.Damaged.Clear();
            AreaObjectBreakHost.Run(host, traps, chest, Both, byHero: false, ranged: false, rules);
            CollectionAssert.AreEqual(new[] { "trap-none:30001:bust=False", "trap-none:30001:bust=True" }, probe.Order.Where(o => o.StartsWith("trap-none")).ToArray());
            Assert.AreEqual(0, probe.Damaged.Count, "no attacker: the BUST script has no triggerer, so its damage names none");
            Assert.AreEqual(0, probe.Order.Count(o => o.StartsWith("trap-hero")));
        });
    }

    [TestMethod]
    public void An_object_that_is_the_attacker_of_a_break_is_the_bust_breaker_and_fires_the_trap_with_no_triggerer()
    {
        // ARC-TECHNOLOGY-1760 (OD4, OD15): a trap's shot is a ranged context whose attacker is the shooter object: the trap of the broken object has no triggerer, the BUST script has the shooter object as its triggerer.
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create();
            var chest = Row(1, Container, 30001, 830);
            var shooter = Row(2, Scenery, 0, 0);
            var probe = new Probe();
            var (host, traps) = Hosts(session, content, probe, true, chest, shooter);
            var shooterEntity = AreaUseScriptHost.EntityId(shooter);
            var ran = AreaObjectBreakHost.Run(host, traps, chest, Both, byHero: false, ranged: true, session.Rules.Scripts, shooterEntity);
            Assert.IsTrue(host.GlobalFlag(BustFlag));
            CollectionAssert.AreEqual(new[] { "trap-none:30001:bust=False" }, probe.Order, "a ranged context fires the trap with no triggerer");
            CollectionAssert.AreEqual(new[] { shooterEntity + ":4" }, probe.Damaged.ToArray(), "the shooter object is the triggerer of the script's action");
            Assert.IsNotNull(ran.Bust?.Run);
        });
    }

    [TestMethod]
    public void An_ordinary_trap_script_runs_on_an_attack_and_a_native_trap_without_a_cast_host_is_not_consumed()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create();
            var ordinary = Row(1, Scenery, 709, 0);
            var native = Row(2, Container, 30001, 830);
            var probe = new Probe();
            var (host, traps) = Hosts(session, content, probe, false, ordinary, native);
            var rules = session.Rules.Scripts;
            var first = AreaObjectBreakHost.Run(host, traps, ordinary, SessionObjectHostSteps.TrapFires, true, false, rules);
            Assert.AreEqual(SessionTrapOutcome.Fired, first.Trap!.Outcome);
            Assert.IsNull(first.Trap.Result.Run);
            Assert.AreEqual(709, first.TrapScript!.Run!.Script);
            Assert.IsTrue(host.GlobalFlag(2302), "the ordinary script ran");
            var second = AreaObjectBreakHost.Run(host, traps, native, Both, true, false, rules);
            Assert.IsNull(second.Trap, "no cast host: the native trap is not met, so not consumed");
            Assert.IsTrue(host.GlobalFlag(BustFlag), "the BUST script runs anyway");
        });
    }

    [TestMethod]
    public void Only_the_steps_the_session_names_run_and_a_host_without_the_effect_for_no_triggerer_meets_no_such_trap()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create();
            var chest = Row(1, Container, 30001, 830);
            var probe = new Probe { ApplyWithoutTriggerer = false };
            var (host, traps) = Hosts(session, content, probe, true, chest);
            var rules = session.Rules.Scripts;
            Assert.IsFalse(AreaObjectBreakHost.Run(host, traps, chest, SessionObjectHostSteps.None, true, false, rules).Any);
            Assert.IsFalse(host.GlobalFlag(BustFlag));
            Assert.IsNull(AreaObjectBreakHost.Run(host, traps, chest, SessionObjectHostSteps.TrapFires, false, false, rules).Trap, "no triggerer-less effect: not met");
            Assert.AreEqual(0, probe.Order.Count);
            Assert.IsNotNull(AreaObjectBreakHost.Run(host, traps, chest, SessionObjectHostSteps.TrapFires, true, false, rules).Trap, "with the hero it is");
            Assert.IsNull(AreaObjectBreakHost.Run(host, null, chest, SessionObjectHostSteps.TrapFires, true, false, rules).Trap, "no trap host");
        });
    }
}
