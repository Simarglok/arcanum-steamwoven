using System.Buffers.Binary;
using Arcanum.Data.Area;
using Arcanum.Data.Content;
using Arcanum.Data.Scripts;
using Arcanum.Scripting.Scr;
using Arcanum.Session;
using Arcanum.TerrainViewer.Core;
using Arcanum.Simulation.Movement;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-SCRIPT-1481 (ledger VT1–VT6): the vitals commands of the use host's set (get hit points 107, get fatigue points 108, heal poison 128, and get stat of the poison level 24) over an invented content stack and map, for the PC (the session's
/// life) and an NPC of the shown map (the world's <see cref="AreaScriptWorld.Vitals"/>); the writes go to <see cref="AreaScriptWorld.ScriptHealLife"/>. The food use script 30282 itself is checked against the installed data in the opt-in test.</summary>
[TestClass]
public sealed class AreaVitalScriptTests
{
    private const byte Attachee = 1, Local = 2, Number = 3, Extra = 12;
    private const int ReadScript = 811, PcScript = 812, NegativeScript = 813, PoisonScript = 814, EatScript = 815, KeepScript = 816;

    private static AreaObjectKey Key(int index) => new(new string('a', 64), 1, AreaSourceKind.Mobile, 1, index, index * 100L, new string((char)('a' + index), 64), 100);
    private static AreaObjectRecord Row(int index, int type, int script)
    {
        var fields = new List<AreaFieldValue>
        {
            new(31, AreaPresence.Array, AreaValueOrigin.Instance, null, [new(ScrPoint.Use, [0, 0, script])], null),
            new(18, AreaPresence.Value, AreaValueOrigin.Instance, 0u, [], null),
        };
        return new(Key(index), 0, type, 0, "p", "p", "r", "s", AreaScope.Core, null, 10 + index, 20, Arcanum.Data.Area.AreaDisposition.BodyEligibleConditionally, [], fields, true, true, true);
    }

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

    private static void WithContent(Action<ScriptContent> test)
    {
        var root = Directory.CreateTempSubdirectory("arc-script-1481-");
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
            // 811: reads its attachee's hit points (L4 current, L5 maximum) and fatigue (L6, L7), adds the two currents into L0 and heals that much poison; then reads the poison level (stat 24) and heals that much more.
            Write("scr/00811read.scr", Program(
                Do(new(ScrCatalogue.Action.GetHitPoints, (Attachee, -1), (Local, 4), (Local, 5))),
                Do(new(ScrCatalogue.Action.GetFatiguePoints, (Attachee, -1), (Local, 6), (Local, 7))),
                Do(new(ScrCatalogue.Action.Add, (Local, 0), (Local, 4), (Local, 6))),
                Do(new(ScrCatalogue.Action.HealPoison, (Attachee, -1), (Local, 0))),
                Do(new(ScrCatalogue.Action.GetStat, N(24), (Attachee, -1), (Local, 1))),
                Do(new(ScrCatalogue.Action.HealPoison, (Attachee, -1), (Local, 1))),
                Do(new(ScrCatalogue.Action.ReturnRunDefault))));
            // 812: the PC as the extra object (the use's target): the maximum hit points as the amount of a poison heal, then 3 points of fatigue.
            Write("scr/00812pc.scr", Program(
                Do(new(ScrCatalogue.Action.GetHitPoints, (Extra, -1), (Local, 4), (Local, 5))),
                Do(new(ScrCatalogue.Action.HealPoison, (Extra, -1), (Local, 5))),
                Do(new(ScrCatalogue.Action.HealFatigue, (Extra, -1), N(3))),
                Do(new(ScrCatalogue.Action.GetStat, N(24), (Extra, -1), (Local, 1))),
                Do(new(ScrCatalogue.Action.HealPoison, (Extra, -1), (Local, 1))),
                Do(new(ScrCatalogue.Action.ReturnRunDefault))));
            // 813: a negative amount raises the poison level in the original, which the host does not model.
            Write("scr/00813negative.scr", Program(Do(new(ScrCatalogue.Action.HealPoison, (Extra, -1), N(-4))), Do(new(ScrCatalogue.Action.ReturnRunDefault))));
            // 814: heal poison of the attachee by 6 only.
            Write("scr/00814poison.scr", Program(Do(new(ScrCatalogue.Action.HealPoison, (Attachee, -1), N(6))), Do(new(ScrCatalogue.Action.ReturnRunDefault))));
            // 815: the food use script's tail: reads the target's hit points, destroys the item in use, heals 4 hit points and 7 poison of the target, and skips the default (as 30282 does).
            Write("scr/00815eat.scr", Program(
                Do(new(ScrCatalogue.Action.GetHitPoints, (Extra, -1), (Local, 4), (Local, 5))),
                Do(new(ScrCatalogue.Action.Destroy, (Attachee, -1))),
                Do(new(ScrCatalogue.Action.HealHitPoints, (Extra, -1), N(4))),
                Do(new(ScrCatalogue.Action.HealPoison, (Extra, -1), N(7))),
                Do(new(ScrCatalogue.Action.ReturnSkipDefault))));
            // 816: destroys the extra object, not the item in use.
            Write("scr/00816other.scr", Program(Do(new(ScrCatalogue.Action.Destroy, (Extra, -1))), Do(new(ScrCatalogue.Action.ReturnSkipDefault))));
            using var content = ScriptContent.Open(ContentMountPlan.Discover(root.FullName, "Syn").Plan!).Content!;
            test(content);
        }
        finally { root.Delete(true); }
    }

    private static AreaUseScriptHost Host(GameSessionOwner session, ScriptContent content, AreaScriptWorld world, params AreaObjectRecord[] shown) => new(session, 1, shown, [], content, world);

    private static AreaScriptWorld World(Func<string, AreaScriptVitals?>? vitals, Func<string, int, int, string>? heal, Func<Arcanum.Scripting.Scr.ScrObjectRef, int, bool>? healFatigue = null) =>
        new(() => null, _ => false, _ => null, () => null, HealFatigue: healFatigue, Vitals: vitals, ScriptHealLife: heal);

    [TestMethod]
    public void An_npcs_vitals_and_poison_level_feed_the_script_and_its_heal_poison_goes_to_the_life_step()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create();
            var npc = Row(1, 16, ReadScript);
            var healed = new List<(string Victim, int Fatigue, int Poison)>();
            var host = Host(session, content, World(code => code == AreaUseScriptHost.EntityId(npc) ? new AreaScriptVitals(4, 9, 2, 7, 11) : null,
                (victim, fatigue, poison) => { healed.Add((victim, fatigue, poison)); return "healed"; }), npc);
            var ran = host.RunUseScript(AreaUseScriptHost.EntityId(npc), null, null, session.Rules.Scripts);
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, ran.Outcome, ran.Detail);
            // 4 hit points plus 2 fatigue (the currents), then the poison level 11 read as stat 24.
            CollectionAssert.AreEqual(new[] { (AreaUseScriptHost.EntityId(npc), 0, 6), (AreaUseScriptHost.EntityId(npc), 0, 11) }, healed);
            Assert.AreEqual("healed", host.LastHealLife);
        });
    }

    [TestMethod]
    public void The_pcs_vitals_come_from_the_session_life_and_the_pcs_fatigue_and_poison_heals_go_to_the_life_step_with_the_scheduler_id()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.CreateWithHero();
            var life = session.QueryHeroLife()!;
            var scenery = Row(2, 2, PcScript);
            var healed = new List<(string Victim, int Fatigue, int Poison)>();
            var host = Host(session, content, World(null, (victim, fatigue, poison) => { healed.Add((victim, fatigue, poison)); return "healed"; }), scenery);
            var pc = host.Pc;
            var ran = host.RunUseScript(AreaUseScriptHost.EntityId(scenery), pc, pc, session.Rules.Scripts);
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, ran.Outcome, ran.Detail);
            var pcId = Arcanum.Simulation.Ai.AiSchedulerRules.Pc;
            // The unharmed hero's maximum hit points as the poison amount, 3 points of fatigue, then its poison level (0: nothing to heal).
            CollectionAssert.AreEqual(new[] { (pcId, 0, life.Status.MaxHitPoints), (pcId, 3, 0) }, healed);
            Assert.AreEqual(0, life.Record.State.PoisonLevel);
        });
    }

    [TestMethod]
    public void A_heal_without_the_life_step_or_a_negative_heal_poison_leaves_the_run_unapplied_and_a_vitals_question_about_another_object_is_not_modelled()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.CreateWithHero();
            var negative = Row(3, 2, NegativeScript);
            var poison = Row(4, 2, PoisonScript);
            var door = Row(5, 3, ReadScript);
            var host = Host(session, content, World(null, (_, _, _) => "healed"), negative, poison, door);
            var pc = host.Pc;
            Assert.AreEqual(AreaScriptedUseOutcome.EffectsRejected, host.RunUseScript(AreaUseScriptHost.EntityId(negative), pc, pc, session.Rules.Scripts).Outcome, "a negative amount");
            var none = Host(session, content, World(null, null), poison);
            Assert.AreEqual(AreaScriptedUseOutcome.EffectsRejected, none.RunUseScript(AreaUseScriptHost.EntityId(poison), pc, pc, session.Rules.Scripts).Outcome, "no life step");
            // A door has no vitals the host knows: the dispatch ends as not modelled, nothing applied.
            Assert.AreEqual(AreaScriptedUseOutcome.Refused, host.RunUseScript(AreaUseScriptHost.EntityId(door), pc, pc, session.Rules.Scripts).Outcome, "a door's hit points");
        });
    }

    private static AreaInventoryMap Food(int script) => new(1, new([], [new SessionSeedItem(Entity(6), new(new(SessionItemType.Food, 4060, 60, 160), 1, 0, 0, null, []), new CellCoord(16, 20))]),
        new[] { Row(6, 9, script) }.ToDictionary(row => Entity(row.Key.SourceIndex)));
    private static string Entity(int index) => "oid:" + Key(index).ObjectIdSha256;

    [TestMethod]
    public void An_item_that_destroys_itself_leaves_the_pcs_list_and_its_heals_go_to_the_life_step_while_it_skips_the_default()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.CreateWithHero();
            var items = Food(EatScript);
            var inventory = new AreaInventoryHost([items]);
            Assert.AreEqual(GameSessionFailure.None, inventory.Attach(session));
            Assert.AreEqual(GameSessionDisposition.Changed, inventory.PickUp(session, Entity(6)).Disposition);
            var healed = new List<(string Victim, int Fatigue, int Poison)>();
            var hp = new List<(string Victim, int Amount)>();
            var world = new AreaScriptWorld(() => null, _ => false, _ => null, () => null, ScriptHeal: (victim, amount) => { hp.Add((victim, amount)); return "healed"; },
                ScriptHealLife: (victim, fatigue, poison) => { healed.Add((victim, fatigue, poison)); return "healed"; });
            var host = new AreaUseScriptHost(session, 2, [], [items], content, world);
            var ran = host.RunItemUse(1, Entity(6), session.Rules.Scripts, targetIsPc: true);
            Assert.AreEqual((AreaScriptedUseOutcome.NoTeleport, ScrOutcome.SkipDefault), (ran.Outcome, ran.Run!.Outcome), ran.Detail);
            Assert.IsTrue(session.IsItemDestroyed(Entity(6)), "the food left the hero's list");
            var pcId = Arcanum.Simulation.Ai.AiSchedulerRules.Pc;
            CollectionAssert.AreEqual(new[] { (pcId, 4) }, hp);
            CollectionAssert.AreEqual(new[] { (pcId, 0, 7) }, healed);
        });
    }

    [TestMethod]
    public void Without_the_use_target_the_extra_object_is_absent_and_a_destroy_of_another_object_is_not_modelled()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.CreateWithHero();
            var items = Food(EatScript);
            var inventory = new AreaInventoryHost([items]);
            Assert.AreEqual(GameSessionFailure.None, inventory.Attach(session));
            Assert.AreEqual(GameSessionDisposition.Changed, inventory.PickUp(session, Entity(6)).Disposition);
            var world = new AreaScriptWorld(() => null, _ => false, _ => null, () => null, ScriptHeal: (_, _) => "healed", ScriptHealLife: (_, _, _) => "healed");
            var host = new AreaUseScriptHost(session, 2, [], [items], content, world);
            // No target: the extra object is none, the read of its hit points stops the run (the original reads a null handle's fields); nothing is destroyed.
            var none = host.RunItemUse(1, Entity(6), session.Rules.Scripts);
            Assert.AreEqual(ScrStop.MissingObject, none.Run!.Stop, none.Detail);
            Assert.IsFalse(session.IsItemDestroyed(Entity(6)));
            var other = Food(KeepScript);
            var otherInventory = new AreaInventoryHost([other]);
            using var second = ConversationSessionFixture.CreateWithHero();
            Assert.AreEqual(GameSessionFailure.None, otherInventory.Attach(second));
            Assert.AreEqual(GameSessionDisposition.Changed, otherInventory.PickUp(second, Entity(6)).Disposition);
            var secondHost = new AreaUseScriptHost(second, 2, [], [other], content, world);
            Assert.AreEqual(AreaScriptedUseOutcome.EffectsRejected, secondHost.RunItemUse(1, Entity(6), second.Rules.Scripts, targetIsPc: true).Outcome, "destroy of the extra object (the PC)");
            Assert.IsFalse(second.IsItemDestroyed(Entity(6)));
        });
    }

    [TestMethod, TestCategory("RealData")]
    public void The_installed_food_use_script_30282_is_admitted_by_the_use_set_and_leaves_a_hero_at_full_health_alone()
    {
        var root = Environment.GetEnvironmentVariable("DAT_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Fail("DAT_ROOT is required for the opt-in food script check; this is not a synthetic pass.");
        var discovery = ContentMountPlan.Discover(root);
        Assert.AreEqual(ContentMountStatus.Ready, discovery.Status, discovery.Detail);
        var opened = ContentStack.Open(discovery.Plan!);
        Assert.AreEqual(ContentStackStatus.Ready, opened.Status, opened.Detail);
        using var content = ScriptContent.Over(opened.Stack!);
        var read = content.Script(30282);
        Assert.AreEqual(ScriptContentStatus.Read, read.Status, read.Detail);
        Assert.IsTrue(ScrAdmission.Admit(read.Program!, AreaUseScriptHost.Admission).IsAdmitted, "the use set admits the food script whole");
        Assert.IsFalse(ScrAdmission.Admit(read.Program!, ScrAdmissionSet.S38).IsAdmitted, "the earlier widest set refused it (get hit points, get fatigue points, heal poison)");
        // Mode 0 (counter 2 is 0: hit points only): a hero with all its hit points reads equal current and maximum and the script skips the default, eating nothing.
        using var session = ConversationSessionFixture.CreateWithHero();
        var food = Row(7, 9, 30282);
        var healed = new List<(string, int, int)>();
        var host = Host(session, content, World(null, (victim, fatigue, poison) => { healed.Add((victim, fatigue, poison)); return "healed"; }), food);
        var ran = host.RunUseScript(AreaUseScriptHost.EntityId(food), host.Pc, host.Pc, session.Rules.Scripts);
        Assert.AreEqual((AreaScriptedUseOutcome.NoTeleport, ScrOutcome.SkipDefault), (ran.Outcome, ran.Run!.Outcome), ran.Detail);
        Assert.AreEqual(0, healed.Count);
    }

    [TestMethod]
    public void The_use_set_holds_the_vitals_commands_and_the_ones_the_food_script_needs()
    {
        foreach (var action in new[] { ScrCatalogue.Action.GetHitPoints, ScrCatalogue.Action.GetFatiguePoints, ScrCatalogue.Action.HealPoison, ScrCatalogue.Action.HealHitPoints,
            ScrCatalogue.Action.HealFatigue, ScrCatalogue.Action.MagickTechAdjustment, ScrCatalogue.Action.PlayScriptEyeCandy, ScrCatalogue.Action.Destroy, ScrCatalogue.Action.GetStat })
            Assert.IsTrue(AreaUseScriptHost.Admission.Actions.Contains(action), $"action {action}");
        Assert.IsTrue(AreaUseScriptHost.Admission.IsSubsetOf(ScrAdmissionSet.S41));
    }
}
