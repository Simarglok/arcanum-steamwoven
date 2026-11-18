using System.Buffers.Binary;
using Arcanum.Data.Area;
using Arcanum.Data.Content;
using Arcanum.Data.Scripts;
using Arcanum.Scripting.GlobalFlags;
using Arcanum.Scripting.Scr;
using Arcanum.Session;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>
/// ARC-SCRIPT-1483 (script trigger audit slice 5, ledger CP1–CP5): the combat-point scripts of a hit through the use-script host (<see cref="AreaUseScriptHost.RunCombatPoint"/>) over an invented content stack and map (no original data).
/// The hit (7) and critical-hit (34) scripts are a weapon's, with the attacker as the triggerer and the victim as the extra object; the critter-hits script (26) is the attacker's, with the victim as the triggerer and no extra object
/// (0x4B3770, 0x4B2F60). The scripts report who they saw through global flags; a poison weapon's script deals its damage through the host's script damage; a slot's local flags are kept between hits. The opt-in census check
/// (<c>ScriptTriggerCensusTests</c>) counts the installed attachments each point's set admits.
/// </summary>
[TestClass]
public sealed class AreaCombatPointScriptTests
{
    private const byte Attachee = 1, Number = 3, GlobalFlag = 4, Local = 2, Triggerer = 0, Extra = 12;
    private const int AttackerIsNpc = 2410, VictimIsPc = 2411, AttacheeIsWeapon = 2412, CritterHitsTriggererIsPc = 2413, SecondHit = 2414, CriticalSeen = 2415;
    private const int PcType = 15, NpcType = 16, WeaponType = 5, AnimalSeen = 2416, CombatSeen = 2417, WallType = 0, WallSeen = 2418, WallDamaged = 2419;

    private static AreaObjectKey Key(int index) => new(new string('a', 64), 1, AreaSourceKind.Mobile, 1, index, index * 100L, new string((char)('a' + index), 64), 100);

    // A row with a script slot (the three words are local flags, counters and script) at the given point.
    private static AreaObjectRecord Row(int index, int type, int script, int point, int? complexity = null)
    {
        var slots = new List<AreaSparseElement> { new(point, [0, 0, script]) };
        var fields = new List<AreaFieldValue>
        {
            new(31, AreaPresence.Array, AreaValueOrigin.Instance, null, slots, null),
            new(18, AreaPresence.Value, AreaValueOrigin.Instance, 0u, [], null),
        };
        // ARC-SCRIPT-1562: a complete weapon row (every field the combat reader needs) with the given magick-tech complexity (slot 45), which the magick-tech adjustments read; the script array replaces the empty one.
        if (complexity is { } stored)
        {
            AreaFieldValue Value(int slot, long value) => new(slot, AreaPresence.Value, AreaValueOrigin.Instance, value, [], null);
            AreaFieldValue Array(int slot, params long[] values) => new(slot, AreaPresence.Array, AreaValueOrigin.Instance, null, [.. values.Select((value, key) => new AreaSparseElement(key, [value]))], null);
            fields = [Value(0, 0x60000000), Value(18, 0), Value(19, 0), Value(21, 1), Value(22, 2), Value(25, 0), Value(26, 10), Value(27, 0), Value(28, 0), Value(29, 4), Array(30), fields[0],
                Value(36, 0), Value(38, 15), Value(39, 0), Value(40, 1), Value(41, 0), Value(42, 0x60000000), Value(43, 1004), Value(44, 0x60000000), Value(45, stored), Value(46, 0),
                Value(49, 10000), Value(50, 10000), Value(51, 10000), Value(52, 10000), Value(53, 10000), Value(54, 0), Value(55, 0),
                Value(59, 0x200), Value(61, 0), Value(62, 0), Array(63, 2, 0, 0, 0, 0), Array(64, 6, 0, 0, 0, 0), Array(65, 0, 0, 0, 0, 0), Value(66, 10), Value(67, 0), Value(68, 1), Value(69, 0),
                Value(70, 0), Value(71, 0), Value(72, 10000), Value(73, 0), Value(74, -1), Value(76, 0), Value(77, 0), Value(78, 0), Value(79, 0), Value(80, 0), Value(81, 0)];
        }
        return new(Key(index), 0, type, 0, "p", "p", "r", "s", AreaScope.Core, null, 10 + index, 20, Arcanum.Data.Area.AreaDisposition.BodyEligibleConditionally,
            [], fields, true, true, true);
    }

    // A human NPC row (type 16) with every primary stat 20 and the given critter flags (slot 36): the complete fields the combat reader needs.
    private static AreaObjectRecord CritterRow(int index, long critterFlags)
    {
        AreaFieldValue Value(int slot, long value) => new(slot, AreaPresence.Value, AreaValueOrigin.Instance, value, [], null);
        AreaFieldValue Array(int slot, params long[] values) => new(slot, AreaPresence.Array, AreaValueOrigin.Instance, null, [.. values.Select((value, key) => new AreaSparseElement(key, [value]))], null);
        AreaFieldValue Empty(int slot) => new(slot, AreaPresence.Empty, AreaValueOrigin.Instance, null, [], null);
        return new(Key(index), 0, NpcType, 28359, "p", "p", "r", "s", AreaScope.Core, null, 10 + index, 20, Arcanum.Data.Area.AreaDisposition.BodyEligibleConditionally, [],
        [
            Value(0, 0x60000000), Value(18, 0), Value(19, 0), Value(21, 1), Value(22, 2), Value(25, 0), Value(26, 20), Value(27, 0), Value(28, 0), Value(29, 4), Array(30), Empty(31),
            Value(36, critterFlags), Value(37, 0), Array(38, 20, 20, 20, 20, 20, 20, 20, 20, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 20, 1, 0),
            Array(39, 0, 0, 0, 5, 0, 0, 0, 0, 0, 0, 0, 0), Array(40), Array(41), Value(42, 10), Value(43, 0), Value(44, 0), Value(45, 0), Empty(46), Empty(47),
            Value(57, 0), Value(63, 0), Value(69, 0), Value(71, 0), Value(74, 10), Value(75, 10), Value(81, 0), Value(82, 0), Value(84, 0), Value(85, 0), Value(89, 0), Array(92),
        ], true, true, true);
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

    // One test line pair: when the object type of the focus equals the value, set the global flag; falls through otherwise.
    private static (Clause, Clause, Clause)[] FlagIfType(byte focus, int type, int flag, int firstLine) =>
    [
        Do(new(ScrCatalogue.Action.GetObjectType, (focus, 0), (Local, 0))),
        If(new(ScrCatalogue.Condition.Equal, (Local, 0), N(type)), new(ScrCatalogue.Action.DoNothing), new(ScrCatalogue.Action.Goto, N(firstLine + 3))),
        Do(new(ScrCatalogue.Action.Assign, (GlobalFlag, flag), N(1))),
    ];

    private static void WithContent(Action<ScriptContent> test)
    {
        var root = Directory.CreateTempSubdirectory("arc-script-1483-");
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
            // 911 (a weapon's hit or critical-hit script): the attacker is an NPC (triggerer), the victim the PC (extra object) and the attachee a weapon item.
            Write("scr/00911roles.scr", Program(
                [.. FlagIfType(Triggerer, NpcType, AttackerIsNpc, 0), .. FlagIfType(Extra, PcType, VictimIsPc, 3), .. FlagIfType(Attachee, WeaponType, AttacheeIsWeapon, 6), Do(new(ScrCatalogue.Action.ReturnRunDefault))]));
            // 912 (the attacker's critter-hits script): the triggerer is the victim, here the PC.
            Write("scr/00912critter.scr", Program([.. FlagIfType(Triggerer, PcType, CritterHitsTriggererIsPc, 0), Do(new(ScrCatalogue.Action.ReturnRunDefault))]));
            // 913: the first run sets local flag 1 (kept in the slot), the second finds it and sets a global flag.
            Write("scr/00913twice.scr", Program(
                If(new(ScrCatalogue.Condition.LocalFlag, N(1)), new(ScrCatalogue.Action.Assign, (GlobalFlag, SecondHit), N(1)), new(ScrCatalogue.Action.SetLocalFlag, N(1))),
                Do(new(ScrCatalogue.Action.ReturnRunDefault))));
            // 914 (a poison weapon): damage of type 1 to the extra object.
            Write("scr/00914poison.scr", Program(Do(new(ScrCatalogue.Action.Damage, (Extra, 0), N(17), N(1))), Do(new(ScrCatalogue.Action.ReturnRunDefault))));
            // 915: marks that the critical-hit point ran.
            Write("scr/00915critical.scr", Program(Do(new(ScrCatalogue.Action.Assign, (GlobalFlag, CriticalSeen), N(1))), Do(new(ScrCatalogue.Action.ReturnRunDefault))));
            // ARC-SCRIPT-1562: the poison weapons' shape (the magick-tech adjustment 91, then get magick-tech adjustment 80 and the damage): 916 scales 100 by the cancel chance of the weapon, its wielder and the victim and deals the result as poison to the
            // victim; 917 scales 100 by the weapon's power ratio for the wielder and deals it; 918 plays eye candy 30 on the victim first.
            Write("scr/00916cancel.scr", Program(Do(new(ScrCatalogue.Action.GetMagickTechAdjustment, N(100), (Attachee, 0), (Triggerer, 0), (Extra, 0), (Local, 0))),
                Do(new(ScrCatalogue.Action.Damage, (Extra, 0), (Local, 0), N(1))), Do(new(ScrCatalogue.Action.ReturnRunDefault))));
            Write("scr/00917ratio.scr", Program(Do(new(ScrCatalogue.Action.MagickTechAdjustment, N(100), (Attachee, 0), (Triggerer, 0), (Local, 0))),
                Do(new(ScrCatalogue.Action.Damage, (Extra, 0), (Local, 0), N(1))), Do(new(ScrCatalogue.Action.ReturnRunDefault))));
            // 919 (a hunting weapon, as 28007): sets a global flag when the extra object (the victim) is an animal.
            Write("scr/00919animal.scr", Program(If(new(ScrCatalogue.Condition.IsAnimal, (Extra, 0)), new(ScrCatalogue.Action.Assign, (GlobalFlag, AnimalSeen), N(1)), new(0)), Do(new(ScrCatalogue.Action.ReturnRunDefault))));
            // ARC-SCRIPT-1661: 920 (an area weapon's test of the victim, as 30064's looped critter): sets a global flag when the extra object is in combat; 921 (the alignment weapon 30252's shape): the triggerer's alignment (stat 19) falls by 200.
            Write("scr/00920incombat.scr", Program(If(new(ScrCatalogue.Condition.IsInCombat, (Extra, 0)), new(ScrCatalogue.Action.Assign, (GlobalFlag, CombatSeen), N(1)), new(0)), Do(new(ScrCatalogue.Action.ReturnRunDefault))));
            Write("scr/00921alignment.scr", Program(Do(new(ScrCatalogue.Action.AdjustStat, N(19), (Triggerer, 0), N(-200))), Do(new(ScrCatalogue.Action.ReturnRunDefault))));
            Write("scr/00927extra.scr", Program(Do(new(ScrCatalogue.Action.AdjustStat, N(19), (Extra, 0), N(300))), Do(new(ScrCatalogue.Action.ReturnRunDefault))));
            Write("scr/00922twice.scr", Program(Do(new(ScrCatalogue.Action.AdjustStat, N(19), (Triggerer, 0), N(-200))), Do(new(ScrCatalogue.Action.AdjustStat, N(19), (Triggerer, 0), N(50))), Do(new(ScrCatalogue.Action.ReturnRunDefault))));
            // ARC-COMBAT-1973: 923 (a weapon's test of the victim, as 911 for a wall): sets a flag when the extra object is a wall (object type 0); 924 (a wall's taking-damage script): marks that it ran and skips the default.
            Write("scr/00923wall.scr", Program([.. FlagIfType(Extra, WallType, WallSeen, 0), Do(new(ScrCatalogue.Action.ReturnRunDefault))]));
            Write("scr/00924walldamage.scr", Program(Do(new(ScrCatalogue.Action.Assign, (GlobalFlag, WallDamaged), N(1))), Do(new(ScrCatalogue.Action.ReturnSkipDefault))));
            // ARC-SCRIPT-1704: 930 (an area weapon's shape, as 30064): damages every critter in the vicinity of the attachee (focus kind 8) by 5.
            Write("scr/00930vicinity.scr", Program(Do(new(ScrCatalogue.Action.Damage, (8, 0), N(5), N(1))), Do(new(ScrCatalogue.Action.ReturnRunDefault))));
            Write("scr/00918eyecandy.scr", Program(Do(new(ScrCatalogue.Action.PlayScriptEyeCandy, N(30), (Extra, 0))), Do(new(ScrCatalogue.Action.ReturnRunDefault))));
            using var content = ScriptContent.Open(ContentMountPlan.Discover(root.FullName, "Syn").Plan!).Content!;
            test(content);
        }
        finally { root.Delete(true); }
    }

    private static AreaUseScriptHost Host(GameSessionOwner session, ScriptContent content, List<(string Victim, int Amount, int Type)>? damaged, params AreaObjectRecord[] shown) =>
        HostWith(session, content, damaged, null, shown);

    // ARC-SCRIPT-1562: the aptitudes the world gives the NPC rows by entity (an NPC without one has none known).
    private static AreaUseScriptHost HostWith(GameSessionOwner session, ScriptContent content, List<(string Victim, int Amount, int Type)>? damaged, Dictionary<AreaObjectRecord, int>? aptitudes, params AreaObjectRecord[] shown) =>
        new(session, 1, shown, [], content, new AreaScriptWorld(() => null, _ => false, _ => null, () => null,
            ScriptDamage: (victim, amount, type, unresistable) => { damaged?.Add((victim, amount, type)); return "damaged"; },
            Aptitude: entity => aptitudes?.FirstOrDefault(pair => AreaUseScriptHost.EntityId(pair.Key) == entity) is { Key: not null } found ? found.Value : null));

    private static bool Flag(GameSessionOwner session, int flag) =>
        session.Flags.Read(new(new(Guid.NewGuid()), session.FlagVersion, new FlagId(flag))).Value == true;

    private static ScrObjectRef Ref(AreaObjectRecord row) => new(1, AreaUseScriptHost.EntityId(row));

    [TestMethod]
    public void The_hit_and_critical_hit_scripts_are_the_weapon_s_with_the_attacker_as_triggerer_and_the_victim_as_extra()
    {
        foreach (var point in new[] { ScrPoint.Hit, ScrPoint.CriticalHit })
            WithContent(content =>
            {
                using var session = ConversationSessionFixture.Create();
                var weapon = Row(1, WeaponType, 911, point);
                var attacker = Row(2, NpcType, 0, ScrPoint.Heartbeat);
                var host = Host(session, content, null, weapon, attacker);
                var ran = host.RunCombatPoint(point, Ref(weapon), Ref(attacker), host.Pc, session.Rules.Scripts);
                Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, ran.Outcome, $"point {point}: {ran.Detail}");
                Assert.AreEqual((ScrOutcome.RunDefault, 911), (ran.Run!.Outcome, ran.Run.Script));
                Assert.IsTrue(Flag(session, AttackerIsNpc), $"point {point}: the triggerer is the attacker");
                Assert.IsTrue(Flag(session, VictimIsPc), $"point {point}: the extra object is the victim");
                Assert.IsTrue(Flag(session, AttacheeIsWeapon), $"point {point}: the attachee is the weapon");
            });
    }

    // ARC-COMBAT-1719 (0x4B2870): the two ends of a flight run the weapon's hit (7) and miss (8) scripts, and for a critical weapon shot the critical-hit (34) and critical-miss (35) ones, as the swing's do.
    [TestMethod]
    public void The_miss_and_critical_miss_scripts_are_the_weapon_s_with_the_shooter_as_triggerer_and_the_target_as_extra()
    {
        foreach (var point in new[] { ScrPoint.Miss, ScrPoint.CriticalMiss })
            WithContent(content =>
            {
                using var session = ConversationSessionFixture.Create();
                var weapon = Row(1, WeaponType, 911, point);
                var attacker = Row(2, NpcType, 0, ScrPoint.Heartbeat);
                var host = Host(session, content, null, weapon, attacker);
                var ran = host.RunCombatPoint(point, Ref(weapon), Ref(attacker), host.Pc, session.Rules.Scripts);
                Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, ran.Outcome, $"point {point}: {ran.Detail}");
                Assert.AreEqual((ScrOutcome.RunDefault, 911), (ran.Run!.Outcome, ran.Run.Script));
                Assert.IsTrue(Flag(session, AttackerIsNpc), $"point {point}: the triggerer is the shooter");
                Assert.IsTrue(Flag(session, VictimIsPc), $"point {point}: the extra object is the target");
                Assert.IsTrue(Flag(session, AttacheeIsWeapon), $"point {point}: the attachee is the weapon");
            });
    }

    [TestMethod]
    public void A_shot_with_no_target_runs_the_miss_point_with_no_extra_object_and_each_end_reads_its_own_slot()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create();
            var onlyMiss = Row(1, WeaponType, 915, ScrPoint.Miss);
            var shooter = Row(2, NpcType, 0, ScrPoint.Heartbeat);
            var host = Host(session, content, null, onlyMiss, shooter);
            var rules = session.Rules.Scripts;
            Assert.IsNull(host.RunCombatPoint(ScrPoint.Hit, Ref(onlyMiss), Ref(shooter), null, rules).Run, "the hit slot is empty");
            Assert.IsNull(host.RunCombatPoint(ScrPoint.CriticalMiss, Ref(onlyMiss), Ref(shooter), null, rules).Run, "the critical-miss slot is empty");
            Assert.IsFalse(Flag(session, CriticalSeen));
            Assert.IsNotNull(host.RunCombatPoint(ScrPoint.Miss, Ref(onlyMiss), Ref(shooter), null, rules).Run);
            Assert.IsTrue(Flag(session, CriticalSeen), "the miss slot's script 915 ran with no extra object");
        });
    }

    [TestMethod]
    public void A_flight_s_end_runs_the_hit_or_miss_point_then_the_critical_one_for_a_weapon_and_the_hero_s_thrown_item_only_the_critical_one()
    {
        CollectionAssert.AreEqual(new[] { ScrPoint.Hit }, AreaShotEndScripts.Points(destination: true, critical: false, weapon: true, thrownByHero: false).ToArray());
        CollectionAssert.AreEqual(new[] { ScrPoint.Miss }, AreaShotEndScripts.Points(destination: false, critical: false, weapon: true, thrownByHero: false).ToArray());
        CollectionAssert.AreEqual(new[] { ScrPoint.Hit, ScrPoint.CriticalHit }, AreaShotEndScripts.Points(true, true, true, false).ToArray());
        CollectionAssert.AreEqual(new[] { ScrPoint.Miss, ScrPoint.CriticalMiss }, AreaShotEndScripts.Points(false, true, true, false).ToArray());
        CollectionAssert.AreEqual(new[] { ScrPoint.Hit }, AreaShotEndScripts.Points(true, true, false, false).ToArray(), "a critical shot of an item that is no weapon runs no critical point");
        CollectionAssert.AreEqual(new[] { ScrPoint.CriticalMiss }, AreaShotEndScripts.Points(false, true, true, true).ToArray(), "the hero's thrown item's hit and miss are the thrown-item host's");
        Assert.AreEqual(0, AreaShotEndScripts.Points(false, false, true, true).Count);
    }

    [TestMethod]
    public void The_critter_hits_script_is_the_attacker_s_with_the_victim_as_triggerer()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create();
            var attacker = Row(1, NpcType, 912, ScrPoint.CritterHits);
            var host = Host(session, content, null, attacker);
            var ran = host.RunCombatPoint(ScrPoint.CritterHits, Ref(attacker), host.Pc, null, session.Rules.Scripts);
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, ran.Outcome, ran.Detail);
            Assert.AreEqual(912, ran.Run!.Script);
            Assert.IsTrue(Flag(session, CritterHitsTriggererIsPc), "the victim is the triggerer");
        });
    }

    [TestMethod]
    public void A_poison_weapon_s_hit_script_deals_its_poison_damage_to_the_victim_through_the_host()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create();
            var damaged = new List<(string Victim, int Amount, int Type)>();
            var weapon = Row(1, WeaponType, 914, ScrPoint.Hit);
            var victim = Row(2, NpcType, 0, ScrPoint.Heartbeat);
            var attacker = Row(3, NpcType, 0, ScrPoint.Heartbeat);
            var host = Host(session, content, damaged, weapon, victim, attacker);
            var ran = host.RunCombatPoint(ScrPoint.Hit, Ref(weapon), Ref(attacker), Ref(victim), session.Rules.Scripts);
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, ran.Outcome, ran.Detail);
            CollectionAssert.AreEqual(new[] { (AreaUseScriptHost.EntityId(victim), 17, 1) }, damaged);
            // Without the viewer's delegate the run stays unapplied: the hit goes on as without the script.
            var bare = Host(session, content, null, weapon, victim, attacker);
            Assert.AreEqual(1, damaged.Count);
            Assert.IsNotNull(bare.RunCombatPoint(ScrPoint.Hit, Ref(weapon), Ref(attacker), Ref(victim), session.Rules.Scripts).Run);
        });
    }

    // ARC-COMBAT-1973 (ledger RH5): the hit on a wall offers the weapon's hit script with the wall as the extra object, and the wall's own taking-damage script (point 23) as any object's; a poison weapon's damage to the wall is a script
    // damage of a wall, which the damage step leaves at once (the viewer's delegate answers it).
    [TestMethod]
    public void A_hit_on_a_wall_runs_the_weapon_s_hit_script_with_the_wall_as_the_extra_object_and_the_wall_s_taking_damage_script()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create();
            var weapon = Row(1, WeaponType, 923, ScrPoint.Hit);
            var attacker = Row(2, NpcType, 0, ScrPoint.Heartbeat);
            var wall = Row(3, WallType, 924, ScrPoint.TakingDamage);
            var host = Host(session, content, null, weapon, attacker, wall);
            var ran = host.RunCombatPoint(ScrPoint.Hit, Ref(weapon), Ref(attacker), Ref(wall), session.Rules.Scripts);
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, ran.Outcome, ran.Detail);
            Assert.IsTrue(Flag(session, WallSeen), "the extra object is the wall");
            // Negative control: with an NPC as the extra object the same script sets nothing (the flag is not the type's default value).
            using var control = ConversationSessionFixture.Create();
            var controlWeapon = Row(4, WeaponType, 923, ScrPoint.Hit);
            var controlHost = Host(control, content, null, controlWeapon, attacker);
            Assert.IsNotNull(controlHost.RunCombatPoint(ScrPoint.Hit, Ref(controlWeapon), Ref(attacker), Ref(attacker), control.Rules.Scripts).Run);
            Assert.IsFalse(Flag(control, WallSeen), "an NPC is no wall");
            var damage = host.RunTakingDamage(AreaUseScriptHost.EntityId(wall), host.Pc, session.Rules.Scripts);
            Assert.IsTrue(AreaUseScriptHost.TakingDamageVetoes(damage), damage.Detail);
            Assert.IsTrue(Flag(session, WallDamaged), "the wall's own taking-damage script ran");
        });
    }

    [TestMethod]
    public void A_poison_weapon_s_hit_script_on_a_wall_leaves_the_hit_going_on()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create();
            var damaged = new List<(string Victim, int Amount, int Type)>();
            var weapon = Row(1, WeaponType, 914, ScrPoint.Hit);
            var attacker = Row(2, NpcType, 0, ScrPoint.Heartbeat);
            var wall = Row(3, WallType, 0, ScrPoint.Heartbeat);
            var host = Host(session, content, damaged, weapon, attacker, wall);
            var ran = host.RunCombatPoint(ScrPoint.Hit, Ref(weapon), Ref(attacker), Ref(wall), session.Rules.Scripts);
            // Whatever the host makes of the wall as a damage target, the run neither throws nor cancels anything (no answer of a combat point cancels the hit).
            Assert.IsNotNull(ran.Run, ran.Detail);
            Assert.AreEqual(0, damaged.Count, "a wall is no damage target of a script (the damage step leaves it at once)");
        });
    }

    [TestMethod]
    public void A_weapon_without_a_script_at_the_point_runs_nothing_and_each_point_reads_its_own_slot()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create();
            var onlyHit = Row(1, WeaponType, 915, ScrPoint.Hit);
            var attacker = Row(2, NpcType, 0, ScrPoint.Heartbeat);
            var host = Host(session, content, null, onlyHit, attacker);
            var rules = session.Rules.Scripts;
            foreach (var point in new[] { ScrPoint.CriticalHit, ScrPoint.CritterHits })
                Assert.IsNull(host.RunCombatPoint(point, Ref(onlyHit), Ref(attacker), host.Pc, rules).Run, $"point {point}");
            Assert.IsNull(host.RunCombatPoint(ScrPoint.Hit, new ScrObjectRef(1, "oid:nobody"), Ref(attacker), host.Pc, rules).Run, "an object that is no row has no script");
            Assert.IsFalse(Flag(session, CriticalSeen));
            Assert.IsNotNull(host.RunCombatPoint(ScrPoint.Hit, Ref(onlyHit), Ref(attacker), host.Pc, rules).Run);
            Assert.IsTrue(Flag(session, CriticalSeen), "the hit slot's script 915 ran");
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => host.RunCombatPoint(ScrPoint.Heartbeat, Ref(onlyHit), Ref(attacker), host.Pc, rules));
        });
    }

    [TestMethod]
    public void The_slot_s_local_flags_are_kept_between_hits()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create();
            var weapon = Row(1, WeaponType, 913, ScrPoint.Hit);
            var attacker = Row(2, NpcType, 0, ScrPoint.Heartbeat);
            var host = Host(session, content, null, weapon, attacker);
            var rules = session.Rules.Scripts;
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, host.RunCombatPoint(ScrPoint.Hit, Ref(weapon), Ref(attacker), host.Pc, rules).Outcome);
            Assert.IsFalse(Flag(session, SecondHit), "the first hit only sets the local flag");
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, host.RunCombatPoint(ScrPoint.Hit, Ref(weapon), Ref(attacker), host.Pc, rules).Outcome);
            Assert.IsTrue(Flag(session, SecondHit), "the second hit finds the flag the first one kept in the slot");
        });
    }

    [TestMethod]
    public void The_combat_point_set_is_the_placed_set_with_the_damage_and_cast_commands_and_the_magick_tech_adjustments()
    {
        var set = AreaUseScriptHost.CombatPointAdmission;
        Assert.IsTrue(AreaUseScriptHost.PlacedAdmission.IsSubsetOf(set), "everything the placed set runs");
        Assert.IsTrue(set.IsSubsetOfBesidesStores(ScrAdmissionSet.S65), "the interpreter's largest set holds it");
        foreach (var action in new[] { ScrCatalogue.Action.Damage, ScrCatalogue.Action.DamageUnresistable, ScrCatalogue.Action.CastSpell, ScrCatalogue.Action.CastFreeSpell,
            ScrCatalogue.Action.MagickTechAdjustment, ScrCatalogue.Action.GetMagickTechAdjustment, ScrCatalogue.Action.PlayScriptEyeCandy })
            Assert.IsTrue(set.Actions.Contains(action), "action " + action);
        // The other placed sets stay as they were: a tile script has no magick-tech adjustment.
        Assert.IsFalse(AreaUseScriptHost.PlacedAdmission.Actions.Contains(ScrCatalogue.Action.GetMagickTechAdjustment));
    }

    // One hit of an NPC with the weapon (script <script>, complexity) on a victim: the damage the script dealt, or null when it dealt none.
    private static (AreaScriptedUse Ran, List<(string Victim, int Amount, int Type)> Damaged) WeaponHit(int script, int complexity, int? attackerAptitude, int? victimAptitude, int victimType = NpcType)
    {
        (AreaScriptedUse, List<(string, int, int)>) result = default;
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create();
            var damaged = new List<(string Victim, int Amount, int Type)>();
            var weapon = Row(1, WeaponType, script, ScrPoint.Hit, complexity);
            var victim = Row(2, victimType, 0, ScrPoint.Heartbeat);
            var attacker = Row(3, NpcType, 0, ScrPoint.Heartbeat);
            var aptitudes = new Dictionary<AreaObjectRecord, int>();
            if (attackerAptitude is { } a) aptitudes[attacker] = a;
            if (victimAptitude is { } v) aptitudes[victim] = v;
            var host = HostWith(session, content, damaged, aptitudes, weapon, victim, attacker);
            result = (host.RunCombatPoint(ScrPoint.Hit, Ref(weapon), Ref(attacker), Ref(victim), session.Rules.Scripts), damaged);
        });
        return result;
    }

    [TestMethod]
    public void Get_magick_tech_adjustment_scales_the_damage_by_the_chance_the_victim_s_aptitude_cancels_the_weapon()
    {
        // The weapon's complexity, the wielder's and the victim's aptitudes, the damage left of 100 (0x461620: a magickal weapon against a technological victim, a technological one against a magickal victim).
        (int Complexity, int Attacker, int Victim, int Expected)[] cases =
        [
            (40, 20, -50, 60), (40, -30, -50, 50), (-40, 0, 60, 40), (-40, 50, 60, 40), (-40, -50, 60, 70), (0, 20, -50, 100), (40, 20, 50, 100), (-40, 20, -50, 100),
        ];
        foreach (var (complexity, attacker, victim, expected) in cases)
        {
            var (ran, damaged) = WeaponHit(916, complexity, attacker, victim);
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, ran.Outcome, $"{complexity}/{attacker}/{victim}: {ran.Detail}");
            Assert.IsTrue(damaged.Count > 0, $"stop={ran.Run?.Stop} detail={ran.Detail}");
            Assert.AreEqual(expected, damaged.Single().Amount, $"complexity {complexity}, wielder {attacker}, victim {victim}");
            Assert.AreEqual(1, damaged.Single().Type);
        }
    }

    [TestMethod]
    public void Get_magick_tech_adjustment_cancels_nothing_for_a_victim_that_is_no_critter_and_stops_for_an_aptitude_the_world_does_not_give()
    {
        // A scenery row is no PC or NPC: the original's type test gives a chance of 0, whatever the weapon.
        var (scenery, damaged) = WeaponHit(916, 40, 20, null, victimType: 3);
        Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, scenery.Outcome, scenery.Detail);
        Assert.AreEqual(100, damaged.Single().Amount);
        // An NPC whose aptitude the world does not give is not modelled: the run stops and deals nothing.
        foreach (var (attacker, victim) in new[] { ((int?)null, (int?)-50), ((int?)20, (int?)null) })
        {
            var (ran, none) = WeaponHit(916, 40, attacker, victim);
            Assert.AreEqual(ScrStop.MissingObject, ran.Run!.Stop, "the run stops where the host has no answer");
            Assert.AreEqual(0, none.Count);
        }
    }

    [TestMethod]
    public void The_magick_tech_adjustment_scales_by_the_weapon_s_power_ratio_for_an_NPC_wielder()
    {
        // Magickal 40 in the hands of aptitude 20: power (20 + 40) / 2 = 30, ratio 75; technological −40 at aptitude 20: power −10, ratio 25; a neutral weapon 100 (ledger WE2, EQ9).
        foreach (var (complexity, aptitude, expected) in new[] { (40, 20, 75), (-40, 20, 25), (0, 20, 100), (40, 100, 100) })
        {
            var (ran, damaged) = WeaponHit(917, complexity, aptitude, 0);
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, ran.Outcome, $"{complexity}/{aptitude}: {ran.Detail}");
            Assert.AreEqual(expected, damaged.Single().Amount, $"complexity {complexity}, wielder aptitude {aptitude}");
        }
        // A wielder whose aptitude is not known stops the run.
        Assert.AreEqual(0, WeaponHit(917, 40, null, 0).Damaged.Count);
    }

    [TestMethod]
    public void The_magick_tech_adjustment_scales_by_the_weapon_s_power_ratio_for_the_hero_and_the_cancel_chance_reads_the_hero_s_aptitude()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.CreateWithHero(1);
            var aptitude = session.QueryHeroScriptStat(GameSessionOwner.ScriptStatAptitude)!.Value;
            var weapon = Row(1, WeaponType, 917, ScrPoint.Hit, 60);
            var cancelling = Row(2, WeaponType, 916, ScrPoint.Hit, 60);
            var victim = Row(3, NpcType, 0, ScrPoint.Heartbeat);
            var damaged = new List<(string Victim, int Amount, int Type)>();
            var host = HostWith(session, content, damaged, new() { [victim] = -50 }, weapon, cancelling, victim);
            var rules = session.Rules.Scripts;
            var ratio = Arcanum.Simulation.Equipment.EquipmentRules.EffectivePowerRatio(60, aptitude);
            Assert.AreNotEqual(100, ratio, "a complexity of 60 against the fixture hero's aptitude is no neutral item");
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, host.RunCombatPoint(ScrPoint.Hit, Ref(weapon), host.Pc, Ref(victim), rules).Outcome);
            Assert.AreEqual(ratio, damaged.Single().Amount, "100 × the ratio / 100, the hero's aptitude under the hero's weapon");
            damaged.Clear();
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, host.RunCombatPoint(ScrPoint.Hit, Ref(cancelling), host.Pc, Ref(victim), rules).Outcome);
            Assert.AreEqual(100 - Arcanum.Simulation.Combat.AttackRules.MagickCancelChance(60, aptitude, -50), damaged.Single().Amount, "the hero is the wielder");
            // The item a host has no row for (a made one) stops the run.
            Assert.IsNull(host.PowerRatio(new(1, "oid:made"), host.Pc));
        });
    }

    // ARC-SCRIPT-1662 (WE2, CP8; exe 0x4614A0, 0x461540): an owner that is neither the PC nor an NPC (scenery, a container) has its complexity as the item's power: the ratio is 100 whatever the item's complexity, and a run of the ratio script
    // with such a wielder stores 100 x 100 / 100 instead of stopping.
    [TestMethod]
    public void The_power_ratio_for_a_scenery_or_container_owner_is_100_and_the_run_goes_on()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.CreateWithHero(1);
            var weapon = Row(1, WeaponType, 917, ScrPoint.Hit, 60);
            var neutral = Row(2, WeaponType, 917, ScrPoint.Hit, 0);
            var scenery = Row(3, 3, 0, ScrPoint.Heartbeat);
            var container = Row(4, 2, 0, ScrPoint.Heartbeat);
            var victim = Row(5, NpcType, 0, ScrPoint.Heartbeat);
            var damaged = new List<(string Victim, int Amount, int Type)>();
            var host = HostWith(session, content, damaged, null, weapon, neutral, scenery, container, victim);
            var aptitude = session.QueryHeroScriptStat(GameSessionOwner.ScriptStatAptitude)!.Value;
            Assert.AreNotEqual(100, Arcanum.Simulation.Equipment.EquipmentRules.EffectivePowerRatio(60, aptitude), "the hero reads the same item as no neutral one");
            foreach (var owner in new[] { scenery, container })
            {
                Assert.AreEqual(100, host.PowerRatio(Ref(weapon), Ref(owner)));
                Assert.AreEqual(100, host.PowerRatio(Ref(neutral), Ref(owner)), "a complexity of 0 reads 100 as well");
                damaged.Clear();
                var ran = host.RunCombatPoint(ScrPoint.Hit, Ref(weapon), Ref(owner), Ref(victim), session.Rules.Scripts);
                Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, ran.Outcome, ran.Detail);
                Assert.AreEqual(100, damaged.Single().Amount, "100 x the ratio of 100 / 100");
            }
            // An owner the host holds no row for, an NPC whose aptitude is unknown and an item it does not know still stop the run.
            Assert.IsNull(host.PowerRatio(Ref(weapon), new(1, "oid:nobody")));
            Assert.IsNull(host.PowerRatio(Ref(weapon), Ref(victim)));
            Assert.IsNull(host.PowerRatio(new(1, "oid:made"), Ref(scenery)));
        });
    }

    [TestMethod]
    public void Obj_is_animal_reads_the_victim_s_critter_flags_and_is_false_for_the_hero_and_for_other_objects()
    {
        foreach (var (name, flags, expected) in new[] { ("an animal", 0x8L, true), ("an animal among other flags", 0x4008L, true), ("no animal flag", 0x4L, false), ("no flags", 0L, false) })
            WithContent(content =>
            {
                using var session = ConversationSessionFixture.Create();
                var weapon = Row(1, WeaponType, 919, ScrPoint.Hit);
                var attacker = Row(2, NpcType, 0, ScrPoint.Heartbeat);
                var victim = CritterRow(3, flags);
                var host = Host(session, content, null, weapon, attacker, victim);
                var ran = host.RunCombatPoint(ScrPoint.Hit, Ref(weapon), Ref(attacker), Ref(victim), session.Rules.Scripts);
                Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, ran.Outcome, $"{name}: {ran.Detail}");
                Assert.AreEqual(expected, Flag(session, AnimalSeen), name);
            });
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create();
            var weapon = Row(1, WeaponType, 919, ScrPoint.Hit);
            var attacker = Row(2, NpcType, 0, ScrPoint.Heartbeat);
            var scenery = Row(3, 3, 0, ScrPoint.Heartbeat);
            var host = Host(session, content, null, weapon, attacker, scenery);
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, host.RunCombatPoint(ScrPoint.Hit, Ref(weapon), Ref(attacker), host.Pc, session.Rules.Scripts).Outcome);
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, host.RunCombatPoint(ScrPoint.Hit, Ref(weapon), Ref(attacker), Ref(scenery), session.Rules.Scripts).Outcome);
            Assert.IsFalse(Flag(session, AnimalSeen), "the hero and a scenery row are no animals");
            Assert.IsFalse(host.IsAnimal(host.Pc)!.Value);
            Assert.IsNull(host.IsAnimal(new(1, "oid:nobody")), "an object the host has no row for is not known");
        });
    }

    private static readonly SessionCombatModeContext NpcCombat = new() { Facts = new() { Speed = 7, Life = new Arcanum.Simulation.Combat.LifeFacts() } };

    [TestMethod]
    public void Obj_is_in_combat_reads_the_session_s_combat_mode_of_the_hero_and_of_an_NPC_and_is_false_for_other_objects()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.CreateWithHero(1);
            var weapon = Row(1, WeaponType, 920, ScrPoint.Hit);
            var attacker = Row(2, NpcType, 0, ScrPoint.Heartbeat);
            var calm = Row(3, NpcType, 0, ScrPoint.Heartbeat);
            var fighting = Row(4, NpcType, 0, ScrPoint.Heartbeat);
            var scenery = Row(5, 3, 0, ScrPoint.Heartbeat);
            var host = Host(session, content, null, weapon, attacker, calm, fighting, scenery);
            var rules = session.Rules.Scripts;
            bool Seen(ScrObjectRef victim)
            {
                var ran = host.RunCombatPoint(ScrPoint.Hit, Ref(weapon), Ref(attacker), victim, rules);
                Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, ran.Outcome, ran.Detail);
                return Flag(session, CombatSeen);
            }
            Assert.IsFalse(Seen(Ref(calm)), "an NPC the session holds in no combat mode");
            Assert.IsFalse(Seen(host.Pc), "the hero before combat mode");
            Assert.AreEqual(GameSessionDisposition.Changed, session.SetCombatMode(session.Version, SessionCombatant.Critter(AreaUseScriptHost.EntityId(fighting)), true, NpcCombat).Disposition);
            Assert.IsTrue(host.IsInCombat(Ref(fighting)));
            Assert.IsFalse(host.IsInCombat(Ref(calm))!.Value);
            Assert.IsTrue(Seen(Ref(fighting)), "an NPC in combat mode");
            Assert.AreEqual(GameSessionDisposition.Changed, session.SetCombatMode(session.Version, SessionCombatant.Hero, true, SessionCombatModeContext.RealTime).Disposition);
            Assert.IsTrue(host.IsInCombat(host.Pc));
            Assert.IsTrue(Seen(host.Pc), "the hero in combat mode");
            // A scenery row is no critter whatever the session holds; an object the host has no row for is not known.
            Assert.IsFalse(host.IsInCombat(Ref(scenery))!.Value);
            Assert.IsNull(host.IsInCombat(new(1, "oid:nobody")));
        });
    }

    [TestMethod]
    public void Adjust_stat_lowers_the_hero_s_alignment_by_the_number_clamped_to_the_stat_range_in_action_order()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.CreateWithHero(1);
            var weapon = Row(1, WeaponType, 921, ScrPoint.Hit, 0);
            var victim = Row(2, NpcType, 0, ScrPoint.Heartbeat);
            var host = Host(session, content, null, weapon, victim);
            var rules = session.Rules.Scripts;
            Assert.AreEqual(GameSessionDisposition.Changed, session.ChangeHeroAlignment(session.Version, 300).Disposition);
            foreach (var (before, after) in new[] { (300, 100), (100, -100), (-100, -300), (-900, -1000), (-1000, -1000) })
            {
                if (session.QueryHeroStoredStat(19) != before) Assert.AreEqual(GameSessionDisposition.Changed, session.ChangeHeroAlignment(session.Version, before).Disposition);
                var ran = host.RunCombatPoint(ScrPoint.Hit, Ref(weapon), host.Pc, Ref(victim), rules);
                Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, ran.Outcome, ran.Detail);
                Assert.AreEqual(after, session.QueryHeroStoredStat(19), $"{before} − 200, the setter clamps at −1000");
            }
        });
    }

    [TestMethod]
    public void Adjust_stat_of_an_NPC_wielder_writes_its_stored_alignment_clamped_in_action_order_and_leaves_the_hero_s_alone()
    {
        // ARC-SCRIPT-1705 (CP12, 0x4B0980 for an NPC): 921 is -200 on the triggerer, 922 is -200 then +50; the entry is the written base stat 19 of the NPC (no follower arming, no change of the hero).
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.CreateWithHero(1);
            var weapon = Row(1, WeaponType, 921, ScrPoint.Hit, 0);
            var twice = Row(4, WeaponType, 922, ScrPoint.Hit, 0);
            var wielder = CritterRow(2, 0);
            var victim = Row(3, NpcType, 0, ScrPoint.Heartbeat);
            var host = Host(session, content, null, weapon, twice, wielder, victim);
            var entity = AreaUseScriptHost.EntityId(wielder);
            var hero = session.QueryHeroStoredStat(19);
            Assert.IsTrue(host.CanAdjustStat(Ref(wielder), 19));
            Assert.IsFalse(host.CanAdjustStat(Ref(wielder), 0), "another stat is not modelled");
            Assert.IsFalse(host.CanAdjustStat(Ref(victim), 19), "an NPC row without combat fields has no alignment to read");
            int Alignment() => AreaWrittenCritterFields.Read(session, wielder)!.Critter!.BaseStats[19];
            Assert.AreEqual(0, Alignment());
            var ran = host.RunCombatPoint(ScrPoint.Hit, Ref(weapon), Ref(wielder), Ref(victim), session.Rules.Scripts);
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, ran.Outcome, ran.Detail);
            Assert.AreEqual(-200, Alignment());
            Assert.AreEqual(-200, session.QueryCritterIndexedWrites(entity).Stats[19]);
            Assert.AreEqual(hero, session.QueryHeroStoredStat(19), "the hero's alignment is untouched");
            foreach (var expected in new[] { -400, -600, -800, -1000, -1000 })
            {
                Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, host.RunCombatPoint(ScrPoint.Hit, Ref(weapon), Ref(wielder), Ref(victim), session.Rules.Scripts).Outcome);
                Assert.AreEqual(expected, Alignment(), "the setter clamps at -1000");
            }
            // 922 from -1000: -200 clamps to -1000, then +50 gives -950 (the sum -150 applied at once would give -1150 clamped to -1000).
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, host.RunCombatPoint(ScrPoint.Hit, Ref(twice), Ref(wielder), Ref(victim), session.Rules.Scripts).Outcome);
            Assert.AreEqual(-950, Alignment());
        });
    }

    [TestMethod]
    public void Adjust_stat_of_an_NPC_the_hero_hits_is_the_extra_object_s_stored_alignment_too()
    {
        // A script that names the extra object (the victim) instead of the triggerer: 927 raises the extra object's alignment by 300.
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.CreateWithHero(1);
            var weapon = Row(1, WeaponType, 927, ScrPoint.Hit, 0);
            var victim = CritterRow(2, 0);
            var host = Host(session, content, null, weapon, victim);
            var ran = host.RunCombatPoint(ScrPoint.Hit, Ref(weapon), host.Pc, Ref(victim), session.Rules.Scripts);
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, ran.Outcome, ran.Detail);
            Assert.AreEqual(300, AreaWrittenCritterFields.Read(session, victim)!.Critter!.BaseStats[19]);
        });
    }

    [TestMethod]
    public void Obj_is_in_combat_is_false_for_the_PC_of_a_session_without_a_hero()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create();
            var host = Host(session, content, null, Row(1, WeaponType, 920, ScrPoint.Hit));
            Assert.IsFalse(host.IsInCombat(host.Pc)!.Value);
        });
    }

    [TestMethod]
    public void Adjust_stat_clamps_each_of_two_adjustments_of_one_run_in_order()
    {
        // 922: −200 then +50 on the triggerer's alignment: from −900 the first clamps to −1000, the second gives −950 (applying the sum −150 at once would give −1000).
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.CreateWithHero(1);
            var weapon = Row(1, WeaponType, 922, ScrPoint.Hit, 0);
            var victim = Row(2, NpcType, 0, ScrPoint.Heartbeat);
            var host = Host(session, content, null, weapon, victim);
            Assert.AreEqual(GameSessionDisposition.Changed, session.ChangeHeroAlignment(session.Version, -900).Disposition);
            var ran = host.RunCombatPoint(ScrPoint.Hit, Ref(weapon), host.Pc, Ref(victim), session.Rules.Scripts);
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, ran.Outcome, ran.Detail);
            Assert.AreEqual(-950, session.QueryHeroStoredStat(19), "−900 − 200 clamps to −1000, then + 50");
        });
    }

    [TestMethod]
    public void Adjust_stat_of_a_critter_created_at_run_time_or_of_an_NPC_of_another_loaded_map_writes_its_alignment_like_a_shown_map_NPC()
    {
        // ARC-SCRIPT-2574 (CP13 tail): 921 is -200 on the triggerer. A created critter (the world's CreatedRow answers its base record) and an NPC row of another given map are written as the shown map's NPC is (the entry npc.stat.19 by identity).
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.CreateWithHero(1);
            var weapon = Row(1, WeaponType, 921, ScrPoint.Hit, 0);
            var victim = Row(3, NpcType, 0, ScrPoint.Heartbeat);
            var created = CritterRow(5, 0);
            var other = CritterRow(6, 0);
            var otherEntity = AreaConversationState.EntityId(other);
            var map = new AreaInventoryMap(2, new([new(otherEntity, SessionHolderKind.Npc, false, false, [], [])], []), new Dictionary<string, AreaObjectRecord> { [otherEntity] = other });
            var host = new AreaUseScriptHost(session, 1, [weapon, victim], [map], content, new AreaScriptWorld(() => null, _ => false, _ => null, () => null,
                CreatedRow: entity => entity == AreaUseScriptHost.EntityId(created) ? created : null));
            var createdRef = Ref(created);
            var otherRef = new ScrObjectRef(2, otherEntity);
            Assert.IsTrue(host.CanAdjustStat(createdRef, 19));
            Assert.IsTrue(host.CanAdjustStat(otherRef, 19));
            Assert.IsFalse(host.CanAdjustStat(createdRef, 0), "another stat is not modelled");
            Assert.IsFalse(host.CanAdjustStat(new ScrObjectRef(1, AreaUseScriptHost.EntityId(CritterRow(7, 0))), 19), "an object with no row here is unknown");
            int Alignment(AreaObjectRecord row) => AreaWrittenCritterFields.Read(session, row)!.Critter!.BaseStats[19];
            foreach (var (row, wielder) in new[] { (created, createdRef), (other, otherRef) })
            {
                Assert.AreEqual(0, Alignment(row));
                var ran = host.RunCombatPoint(ScrPoint.Hit, Ref(weapon), wielder, Ref(victim), session.Rules.Scripts);
                Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, ran.Outcome, ran.Detail);
                Assert.AreEqual(-200, Alignment(row));
                Assert.AreEqual(-200, session.QueryCritterIndexedWrites(AreaConversationState.EntityId(row)).Stats[19]);
                foreach (var expected in new[] { -400, -600, -800, -1000, -1000 })
                {
                    Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, host.RunCombatPoint(ScrPoint.Hit, Ref(weapon), wielder, Ref(victim), session.Rules.Scripts).Outcome);
                    Assert.AreEqual(expected, Alignment(row), "the setter clamps at -1000");
                }
            }
        });
    }

    [TestMethod]
    public void Adjust_stat_of_an_NPC_wielder_or_without_a_hero_ends_the_run_as_not_modelled_and_changes_nothing()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.CreateWithHero(1);
            var weapon = Row(1, WeaponType, 921, ScrPoint.Hit, 0);
            var wielder = Row(2, NpcType, 0, ScrPoint.Heartbeat);
            var victim = Row(3, NpcType, 0, ScrPoint.Heartbeat);
            var host = Host(session, content, null, weapon, wielder, victim);
            var before = session.QueryHeroStoredStat(19);
            var ran = host.RunCombatPoint(ScrPoint.Hit, Ref(weapon), Ref(wielder), Ref(victim), session.Rules.Scripts);
            Assert.AreEqual(AreaScriptedUseOutcome.Refused, ran.Outcome, "an NPC's alignment is the world's, not the session's");
            Assert.AreEqual(before, session.QueryHeroStoredStat(19));
            Assert.IsFalse(host.CanAdjustStat(Ref(wielder), 19));
            Assert.IsFalse(host.CanAdjustStat(host.Pc, 0), "the hero's strength is not modelled here");
            Assert.IsTrue(host.CanAdjustStat(host.Pc, 19));
        });
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create();
            var weapon = Row(1, WeaponType, 921, ScrPoint.Hit, 0);
            var victim = Row(2, NpcType, 0, ScrPoint.Heartbeat);
            var host = Host(session, content, null, weapon, victim);
            Assert.IsFalse(host.CanAdjustStat(host.Pc, 19), "a session without a hero has no alignment to write");
            Assert.AreEqual(AreaScriptedUseOutcome.Refused, host.RunCombatPoint(ScrPoint.Hit, Ref(weapon), host.Pc, Ref(victim), session.Rules.Scripts).Outcome);
        });
    }

    [TestMethod]
    public void The_combat_point_set_holds_obj_is_in_combat_and_adjust_stat_and_the_other_placed_sets_do_not()
    {
        Assert.IsTrue(AreaUseScriptHost.CombatPointAdmission.Conditions.Contains(ScrCatalogue.Condition.IsInCombat));
        Assert.IsTrue(AreaUseScriptHost.CombatPointAdmission.Actions.Contains(ScrCatalogue.Action.AdjustStat));
        Assert.IsFalse(AreaUseScriptHost.PlacedAdmission.Actions.Contains(ScrCatalogue.Action.AdjustStat));
        // ARC-SCRIPT-1733: obj is in combat and the critter-state conditions are in the placed set too since the set S53 (the three tail conditions since S54); adjust stat stays the combat points' alone.
        Assert.IsTrue(AreaUseScriptHost.PlacedAdmission.Conditions.Contains(ScrCatalogue.Condition.IsInCombat));
    }

    // ARC-SCRIPT-1704 (CP14, IV2): the vicinity of a weapon is scanned around the weapon's own location field, never its holder's. A weapon row of the shown map (an NPC's) is centred on its row tile; the critters around it, not the far one, are damaged.
    [TestMethod]
    public void The_area_weapon_s_vicinity_is_the_tiles_around_the_weapon_s_own_location_not_the_holder_s()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create();
            var damaged = new List<(string Victim, int Amount, int Type)>();
            var weapon = Row(1, WeaponType, 930, ScrPoint.Hit);   // tile (11, 20)
            var near = Row(2, NpcType, 0, ScrPoint.Heartbeat);    // (12, 20)
            var far = Row(20, NpcType, 0, ScrPoint.Heartbeat);    // (30, 20)
            var holder = Row(21, NpcType, 0, ScrPoint.Heartbeat); // (31, 20): the wielder, far from the weapon's own location
            var host = Host(session, content, damaged, weapon, near, far, holder);
            var ran = host.RunCombatPoint(ScrPoint.Hit, Ref(weapon), Ref(holder), Ref(near), session.Rules.Scripts);
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, ran.Outcome, ran.Detail);
            CollectionAssert.AreEquivalent(new[] { AreaUseScriptHost.EntityId(near) }, damaged.Select(d => d.Victim).ToArray(), "only the critter around the weapon's tile, not the far ones nor the holder");
        });
    }

    // ARC-SCRIPT-1704: the hero's weapon is carried from the map it was taken on, so the combat point names it on that map (the viewer's weaponMap = the item's origin) while the run is on another shown map: the scan still centres on the row's stale tile
    // and finds the critters of the shown map around it (the original scans the current map around the item's location field).
    [TestMethod]
    public void A_weapon_carried_from_another_map_scans_the_shown_map_around_its_own_location()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create();
            var damaged = new List<(string Victim, int Amount, int Type)>();
            var weapon = Row(1, WeaponType, 930, ScrPoint.Hit);
            var near = Row(2, NpcType, 0, ScrPoint.Heartbeat);
            var far = Row(20, NpcType, 0, ScrPoint.Heartbeat);
            var world = new AreaScriptWorld(() => null, _ => false, _ => null, () => null, ScriptDamage: (victim, amount, type, unresistable) => { damaged.Add((victim, amount, type)); return "damaged"; });
            var host = new AreaUseScriptHost(session, 2, [near, far], [], content, world) { OtherMapRow = obj => obj.Map == 1 && obj.Code == AreaUseScriptHost.EntityId(weapon) ? weapon : null };
            var ran = host.RunCombatPoint(ScrPoint.Hit, Ref(weapon), host.Pc, Ref(near), session.Rules.Scripts);
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, ran.Outcome, ran.Detail);
            CollectionAssert.AreEqual(new[] { AreaUseScriptHost.EntityId(near) }, damaged.Select(d => d.Victim).ToArray());
            // Outside the item's run an object of an unheld map has no centre (an empty scan), and a critter of another map is no centre either.
            Assert.AreEqual(0, host.CrittersInVicinity(Ref(weapon)).Count);
            Assert.AreEqual(0, host.CrittersInVicinity(Ref(near)).Count, "a critter named on another map than the shown one");
        });
    }

    [TestMethod]
    public void Play_script_eye_candy_runs_in_a_combat_point_script_and_shows_nothing_here()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create();
            var weapon = Row(1, WeaponType, 918, ScrPoint.Hit);
            var victim = Row(2, NpcType, 0, ScrPoint.Heartbeat);
            var attacker = Row(3, NpcType, 0, ScrPoint.Heartbeat);
            var ran = Host(session, content, null, weapon, victim, attacker).RunCombatPoint(ScrPoint.Hit, Ref(weapon), Ref(attacker), Ref(victim), session.Rules.Scripts);
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, ran.Outcome, ran.Detail);
            Assert.AreEqual(918, ran.Run!.Script);
        });
    }
}
