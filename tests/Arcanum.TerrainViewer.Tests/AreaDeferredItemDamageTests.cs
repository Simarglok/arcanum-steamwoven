using System.Buffers.Binary;
using Arcanum.Data.Scripts;
using Arcanum.Scripting.Scr;
using Arcanum.Session;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Equipment;
using Arcanum.Simulation.Items;
using Arcanum.Simulation.Magic;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>
/// ARC-SCRIPT-1482 (ledger IT8, WE2, WE4, GT194–GT196): the deferred poison script of a wield-on item through the viewer's script host: the wield-on script of the installed poisoning items only schedules it (call script in), and it
/// runs from the event queue under the deferred-call set, which now holds the magick-tech adjustment, play script eye candy and, with the viewer's hooks, damage (60), unresistable damage (138) and heal hit points (75) of the PC. The scripts,
/// numbers and entity ids are invented; the installed scripts are checked on the data in the opt-in real-data test.
/// </summary>
[TestClass]
public sealed class AreaDeferredItemDamageTests
{
    private const int Poison = 796, Healer = 797, Fatigue = 798, Hero = 0;
    private static readonly SessionInsertCheck Accept = _ => SessionInsertVerdict.Accepted;

    // 796: the ratio of the attachee for the triggerer scales 40 into local 0, unresistable poison damage (type 1) of that amount to the triggerer, an eye candy, then the call again in 10 s with the same roles.
    private static byte[] PoisonProgram() => Program(
        (91, [(3, 40), (1, 0), (0, Hero), (2, 0)]),
        (138, [(0, Hero), (2, 0), (3, 1)]),
        (120, [(3, 30), (0, Hero)]),
        (54, [(3, Poison), (3, 0), (0, Hero), (1, 0), (3, 10)]),
        (1, []));

    // 797: damage 60 (resistable, normal) 7 to the triggerer, then heal hit points 3.
    private static byte[] HealerProgram() => Program((60, [(0, Hero), (3, 7), (3, 0)]), (75, [(0, Hero), (3, 3)]), (1, []));

    // 798 (ARC-SCRIPT-1586): get fatigue points of the triggerer into locals 0 (current) and 1 (maximum), unresistable poison damage of the current points, then the call again in 10 s.
    private static byte[] FatigueProgram() => Program(
        (108, [(0, Hero), (2, 0), (2, 1)]),
        (138, [(0, Hero), (2, 0), (3, 1)]),
        (54, [(3, Fatigue), (3, 0), (0, Hero), (1, 0), (3, 10)]),
        (1, []));

    private static SessionMapInventorySeed Seed() => new([],
    [
        new("ring", new(new(SessionItemType.Armor, 8214, 2866, 200), 0, 0, 0, null, []), new CellCoord(102, 200)),
    ]);

    private sealed class Hits
    {
        public List<(string Victim, int Amount, int Type, bool Unresistable)> Damage { get; } = [];
        public List<(string Victim, int Amount)> Heal { get; } = [];
    }

    // How the shown map's use host stands behind the script host: none (the talk set holds no query, no damage), one whose world gives no damage and heal, or one that gives them (ARC-SCRIPT-1563).
    private enum UseHost { None, WithoutDamage, WithDamage }

    private static void With(UseHost use, int complexity, Action<AreaConversationHost, GameSessionOwner, SessionScriptRole, SessionScriptRole, Hits> test)
    {
        var root = Directory.CreateTempSubdirectory("arc-script-1482-");
        try
        {
            File.WriteAllBytes(Path.Combine(root.FullName, "tig.dat"), AreaOwnerFixture.Dat());
            Directory.CreateDirectory(Path.Combine(root.FullName, "modules"));
            File.WriteAllBytes(Path.Combine(root.FullName, "modules", "Syn.dat"), AreaOwnerFixture.Dat());
            Directory.CreateDirectory(Path.Combine(root.FullName, "scr"));
            File.WriteAllBytes(Path.Combine(root.FullName, "scr", "00796poison.scr"), PoisonProgram());
            File.WriteAllBytes(Path.Combine(root.FullName, "scr", "00797healer.scr"), HealerProgram());
            File.WriteAllBytes(Path.Combine(root.FullName, "scr", "00798fatigue.scr"), FatigueProgram());
            using var content = AreaContentRoot.OpenScripts(Path.Combine(root.FullName, "modules", "Syn.dat")).Content!;
            using var source = new AreaOwnerFixture(motionBodies: true);
            var snapshot = source.Owner.LoadStrict();
            using var session = ConversationSessionFixture.CreateWithHero(snapshot.RuntimeMap);
            Assert.AreEqual(GameSessionDisposition.Changed, session.AttachMapInventory(new(snapshot.RuntimeMap), Seed()).Disposition);
            Assert.AreEqual(GameSessionDisposition.Changed, session.PickUpItem(session.Version, "ring", Accept).Disposition);
            var kind = session.QueryPcInventory().Single(item => item.Entity == "ring").Kind;
            var ring = new SessionWearableItem(kind, [], false, new()
            {
                Kind = EquipmentItemKind.Armor, MagickTechComplexity = complexity, Armor = new EquipmentArmor { Coverage = ArmorCoverage.Ring },
            });
            Assert.AreEqual(GameSessionDisposition.Changed, session.Equip(session.Version, "ring", EquipmentSlot.Ring1, ring, (_, _) => true, Accept).Disposition);
            var host = new AreaConversationHost(snapshot, content, session);
            var hits = new Hits();
            if (use != UseHost.None)
            {
                var gives = use == UseHost.WithDamage;
                var useHost = new AreaUseScriptHost(session, snapshot.RuntimeMap, [], [], content,
                    new AreaScriptWorld(() => new(12, 20), _ => false, _ => null, () => null,
                        ScriptDamage: gives ? (victim, amount, type, unresistable) => { hits.Damage.Add((victim, amount, type, unresistable)); return "damaged"; } : null,
                        ScriptHeal: gives ? (victim, amount) => { hits.Heal.Add((victim, amount)); return "healed"; } : null));
                host.Scripts.QueryHost = () => useHost;
            }
            test(host, session, new(new(snapshot.RuntimeMap), SessionEventKinds.PlayerEntity), new(new(snapshot.RuntimeMap), "ring"), hits);
        }
        finally { root.Delete(true); }
    }

    [TestMethod]
    public void A_deferred_poison_script_damages_the_wearer_by_the_items_power_ratio_and_schedules_itself_again()
    {
        With(UseHost.WithDamage, complexity: 0, (host, session, pc, ring, hits) =>
        {
            var call = new SessionScriptCall(Poison, 0, pc, ring);
            Assert.IsTrue(host.RunCall(call, out var detail), detail);
            // A neutral item's ratio is 100: 40 into the damage; poison type, no resistance skipped.
            CollectionAssert.AreEqual(new[] { (Arcanum.Simulation.Ai.AiSchedulerRules.Pc, 40, 1, true) }, hits.Damage);
            Assert.AreEqual("damaged", host.Scripts.LastDamage);
            var next = session.QueryScriptCalls().Single();
            Assert.AreEqual((call, session.QueryClockSnapshot().GameTimestampMs + 10 * 8000L), (next.Call, next.DeadlineMs));
        });
    }

    // ARC-SCRIPT-1586 (VT2): a deferred run reads the hero's fatigue points (108) and goes on to its damage and its next call; without the use host the hero is still answered from the session.
    [TestMethod]
    public void A_deferred_script_reads_the_heros_fatigue_points_and_goes_on_to_its_damage_and_its_next_call()
    {
        With(UseHost.WithDamage, complexity: 0, (host, session, pc, ring, hits) =>
        {
            var life = session.QueryHeroLife()!;
            var fatigue = life.Status.Fatigue;
            Assert.IsTrue(fatigue > 0);
            Assert.AreEqual((fatigue, life.Status.MaxFatigue), host.Scripts.FatiguePoints(new(pc.Map.RuntimeMap, pc.Entity)));
            Assert.AreEqual((life.Status.HitPoints, life.Status.MaxHitPoints), host.Scripts.HitPoints(new(pc.Map.RuntimeMap, pc.Entity)));
            Assert.IsNull(host.Scripts.FatiguePoints(new(pc.Map.RuntimeMap, "oid:npc")), "an object the use host does not know is not modelled");
            var call = new SessionScriptCall(Fatigue, 0, pc, ring);
            Assert.IsTrue(host.RunCall(call, out var detail), detail);
            CollectionAssert.AreEqual(new[] { (Arcanum.Simulation.Ai.AiSchedulerRules.Pc, fatigue, 1, true) }, hits.Damage);
            Assert.AreEqual(call, session.QueryScriptCalls().Single().Call);
        });
        With(UseHost.None, complexity: 0, (host, session, pc, _, _) =>
        {
            var life = session.QueryHeroLife()!;
            Assert.AreEqual((life.Status.Fatigue, life.Status.MaxFatigue), host.Scripts.FatiguePoints(new(pc.Map.RuntimeMap, pc.Entity)));
            Assert.IsNull(host.Scripts.FatiguePoints(new(pc.Map.RuntimeMap, "oid:npc")));
            Assert.IsTrue(host.Scripts.CallAdmissionFor(null).Actions.Contains(ScrCatalogue.Action.GetFatiguePoints));
        });
    }

    [TestMethod]
    public void The_script_reads_the_ratio_of_a_worn_item_only_and_a_stranger_item_stops_the_run()
    {
        With(UseHost.WithDamage, complexity: 0, (host, session, pc, ring, hits) =>
        {
            Assert.AreEqual(100, host.Scripts.PowerRatio(new(ring.Map.RuntimeMap, "ring"), new(pc.Map.RuntimeMap, pc.Entity)));
            // The item role may name the map the call was scheduled on after the hero travelled: the worn item is found by its entity alone.
            Assert.AreEqual(100, host.Scripts.PowerRatio(new(ring.Map.RuntimeMap + 5, "ring"), new(pc.Map.RuntimeMap, pc.Entity)));
            Assert.IsNull(host.Scripts.PowerRatio(new(ring.Map.RuntimeMap, "stranger"), new(pc.Map.RuntimeMap, pc.Entity)));
            Assert.IsNull(host.Scripts.PowerRatio(new(ring.Map.RuntimeMap, "ring"), new(pc.Map.RuntimeMap, "oid:npc")));
            // A run whose item is not worn stops at the ratio (the original reads a null handle's fields) with nothing applied: no damage, and no next call.
            Assert.IsTrue(host.RunCall(new SessionScriptCall(Poison, 0, pc, new(ring.Map, "stranger")), out var detail), detail);
            StringAssert.Contains(detail, "MissingObject");
            Assert.AreEqual(0, hits.Damage.Count);
            Assert.AreEqual(0, session.QueryScriptCalls().Count);
        });
    }

    // ARC-SCRIPT-1662 (WE2; exe 0x4614A0, 0x461540): a deferred call's owner that is no critter (a container of the shown map) reads a ratio of 100, through the use host that answers; without it the run still stops.
    [TestMethod]
    public void The_script_reads_a_ratio_of_100_for_a_container_owner_while_the_use_host_answers()
    {
        var container = new Arcanum.Data.Area.AreaObjectRecord(new(new string('a', 64), 1, Arcanum.Data.Area.AreaSourceKind.Mobile, 1, 1, 100L, new string('b', 64), 100), 0, 2, 0, "p", "p", "r", "s",
            Arcanum.Data.Area.AreaScope.Core, null, 10, 20, Arcanum.Data.Area.AreaDisposition.BodyEligibleConditionally, [], [], true, true, true);
        var entity = AreaUseScriptHost.EntityId(container);
        With(UseHost.WithDamage, complexity: 60, (host, session, pc, ring, _) =>
        {
            var owner = new Arcanum.Scripting.Scr.ScrObjectRef(pc.Map.RuntimeMap, entity);
            Assert.IsNull(host.Scripts.PowerRatio(new(ring.Map.RuntimeMap, "ring"), owner), "the shown map's use host holds no row of this container");
            var useHost = new AreaUseScriptHost(session, pc.Map.RuntimeMap, [container], [], null, new AreaScriptWorld(() => new(12, 20), _ => false, _ => null, () => null));
            host.Scripts.QueryHost = () => useHost;
            Assert.AreEqual(100, host.Scripts.PowerRatio(new(ring.Map.RuntimeMap, "ring"), owner));
            Assert.AreNotEqual(100, host.Scripts.PowerRatio(new(ring.Map.RuntimeMap, "ring"), new(pc.Map.RuntimeMap, pc.Entity)), "the hero's own ratio of the complexity 60 is no neutral one");
            host.Scripts.QueryHost = null;
            Assert.IsNull(host.Scripts.PowerRatio(new(ring.Map.RuntimeMap, "ring"), owner), "without a use host of the map the run stops as before");
        });
    }

    [TestMethod]
    public void Damage_and_heal_of_hit_points_go_to_the_hooks_in_action_order_for_the_pc_only()
    {
        With(UseHost.WithDamage, complexity: 0, (host, _, pc, ring, hits) =>
        {
            Assert.IsTrue(host.RunCall(new SessionScriptCall(Healer, 0, pc, ring), out var detail), detail);
            CollectionAssert.AreEqual(new[] { (Arcanum.Simulation.Ai.AiSchedulerRules.Pc, 7, 0, false) }, hits.Damage);
            CollectionAssert.AreEqual(new[] { (Arcanum.Simulation.Ai.AiSchedulerRules.Pc, 3) }, hits.Heal);
            Assert.AreEqual(("damaged", "healed"), (host.Scripts.LastDamage, host.Scripts.LastHeal));
            // Another object as the triggerer is left as it is (the run goes on).
            hits.Damage.Clear(); hits.Heal.Clear();
            Assert.IsTrue(host.RunCall(new SessionScriptCall(Healer, 0, new(pc.Map, "oid:npc"), ring), out detail), detail);
            Assert.AreEqual(0, hits.Damage.Count + hits.Heal.Count);
        });
    }

    [TestMethod]
    public void A_magickal_item_scales_the_damage_by_its_power_ratio_under_the_heros_aptitude()
    {
        With(UseHost.WithDamage, complexity: 60, (host, session, pc, ring, hits) =>
        {
            var ratio = session.QueryWornPowerRatio("ring");
            Assert.IsNotNull(ratio);
            Assert.AreNotEqual(100, ratio, "a complexity of 60 against the fixture hero's aptitude is no neutral item");
            Assert.IsTrue(host.RunCall(new SessionScriptCall(Poison, 0, pc, ring), out var detail), detail);
            Assert.AreEqual((ratio!.Value * 40 + 50) / 100, hits.Damage.Single().Amount);
        });
    }

    [TestMethod]
    public void The_use_host_reads_a_worn_item_with_no_map_row_for_the_combat_point_adjustments()
    {
        // ARC-SCRIPT-1562 (CP7, CP8): the hero's worn item has no row of the shown map (here the use host holds none): action 91 reads the session's worn ratio and action 80 its worn complexity under the hero's aptitude.
        With(UseHost.WithDamage, complexity: 60, (host, session, pc, ring, _) =>
        {
            var use = host.Scripts.QueryHost!()!;
            var item = new Arcanum.Scripting.Scr.ScrObjectRef(ring.Map.RuntimeMap, "ring");
            Assert.AreEqual(session.QueryWornPowerRatio("ring"), use.PowerRatio(item, use.Pc));
            Assert.AreEqual(60, session.QueryWornComplexity("ring"));
            Assert.IsNull(session.QueryWornComplexity("stranger"));
            var aptitude = session.QueryHeroScriptStat(GameSessionOwner.ScriptStatAptitude)!.Value;
            Assert.AreEqual(Arcanum.Simulation.Combat.AttackRules.MagickCancelChance(60, aptitude, aptitude), use.MagickCancelPercent(item, use.Pc, use.Pc));
            Assert.IsNull(use.MagickCancelPercent(new(item.Map, "stranger"), use.Pc, use.Pc), "an item neither a row nor worn is not known");
        });
    }

    [TestMethod]
    public void Without_a_use_host_that_gives_damage_a_damaging_script_is_refused_and_the_set_follows_the_use_host()
    {
        With(UseHost.None, complexity: 0, (host, _, pc, ring, hits) =>
        {
            var set = host.Scripts.CallAdmissionFor(null);
            Assert.IsTrue(set.Actions.Contains(ScrCatalogue.Action.MagickTechAdjustment) && set.Actions.Contains(ScrCatalogue.Action.PlayScriptEyeCandy));
            Assert.IsFalse(set.Actions.Contains(ScrCatalogue.Action.Damage), "the talk set holds damage only while the shown map's use host answers");
            Assert.IsFalse(host.RunCall(new SessionScriptCall(Healer, 0, pc, ring), out var detail));
            StringAssert.Contains(detail, "ActionNotAdmitted");
            Assert.AreEqual(0, hits.Damage.Count);
        });
        // A use host whose world gives no damage admits the actions and refuses the run whole before anything applies (ARC-SCRIPT-1563).
        With(UseHost.WithoutDamage, complexity: 0, (host, session, pc, ring, hits) =>
        {
            Assert.IsTrue(host.Scripts.CallAdmissionFor(null).Actions.Contains(ScrCatalogue.Action.Damage));
            Assert.IsFalse(host.RunCall(new SessionScriptCall(Poison, 0, pc, ring), out _));
            Assert.AreEqual(0, session.QueryScriptCalls().Count, "no next call: nothing applied");
            Assert.AreEqual(0, hits.Damage.Count);
        });
        Assert.IsFalse(AreaScriptHost.Admission.Actions.Contains(ScrCatalogue.Action.Damage), "a dialog script without the use host does not get damage");
    }

    // ARC-SCRIPT-1482 (opt-in, DAT_ROOT): the six callers of the installed wield-on poisoning items (five attached at point 24) are admitted by the item-event set from line 0 (they only schedule the poison with call script in), and the
    // six deferred poison scripts are admitted by the deferred-call set with the use host (random draw, damage and, since ARC-SCRIPT-1586, get fatigue points: 2677 2679 2681 2682).
    [TestMethod, TestCategory("RealData")]
    public void The_installed_wield_on_poison_scripts_are_admitted_by_the_sets_that_run_them()
    {
        var root = Environment.GetEnvironmentVariable("DAT_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Fail("DAT_ROOT is required for the opt-in poison admission check; this is not a synthetic pass.");
        using var content = ConversationSessionFixture.Scripts(root);
        foreach (var caller in new[] { 30088, 30103, 30115, 30147, 30165, 30197 })
            Assert.IsTrue(ScrAdmission.AdmitFrom(content.Script(caller).Program!, SessionItemScripts.Admission, 0).IsAdmitted, $"wield on {caller}");
        With(UseHost.WithDamage, complexity: 0, (host, _, _, _, _) =>
        {
            // With the shown map's use host the set holds the random draw (37) and the damage actions the scripts use.
            var withDraw = host.Scripts.CallAdmissionFor(null);
            // ARC-SCRIPT-1586: the four that read get fatigue points (108) are admitted too since the set holds the vitals questions.
            foreach (var script in new[] { 2664, 2678, 2677, 2679, 2681, 2682 })
                Assert.IsTrue(ScrAdmission.AdmitFrom(content.Script(script).Program!, withDraw, 0).IsAdmitted, $"poison {script}");
        });
    }

    private static byte[] Program(params (int Action, (byte Kind, int Value)[] Operands)[] lines)
    {
        var bytes = new byte[64 + lines.Length * 132];
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(52), lines.Length);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(56), lines.Length);
        for (var line = 0; line < lines.Length; line++)
        {
            var clause = bytes.AsSpan(64 + line * 132 + 44, 44);
            BinaryPrimitives.WriteInt32LittleEndian(clause, lines[line].Action);
            for (var i = 0; i < lines[line].Operands.Length; i++)
            {
                clause[4 + i] = lines[line].Operands[i].Kind;
                BinaryPrimitives.WriteInt32LittleEndian(clause[(12 + 4 * i)..], lines[line].Operands[i].Value);
            }
        }
        Assert.AreEqual(ScrReadStatus.Read, ScrProgram.Read(bytes).Status);
        return bytes;
    }
}
