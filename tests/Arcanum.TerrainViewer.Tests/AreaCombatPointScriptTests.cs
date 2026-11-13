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
    private const int PcType = 15, NpcType = 16, WeaponType = 5;

    private static AreaObjectKey Key(int index) => new(new string('a', 64), 1, AreaSourceKind.Mobile, 1, index, index * 100L, new string((char)('a' + index), 64), 100);

    // A row with a script slot (the three words are local flags, counters and script) at the given point.
    private static AreaObjectRecord Row(int index, int type, int script, int point)
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
            using var content = ScriptContent.Open(ContentMountPlan.Discover(root.FullName, "Syn").Plan!).Content!;
            test(content);
        }
        finally { root.Delete(true); }
    }

    private static AreaUseScriptHost Host(GameSessionOwner session, ScriptContent content, List<(string Victim, int Amount, int Type)>? damaged, params AreaObjectRecord[] shown) =>
        new(session, 1, shown, [], content, new AreaScriptWorld(() => null, _ => false, _ => null, () => null,
            ScriptDamage: (victim, amount, type, unresistable) => { damaged?.Add((victim, amount, type)); return "damaged"; }));

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
    public void The_combat_point_set_is_the_placed_set_with_the_damage_and_cast_commands()
    {
        var set = AreaUseScriptHost.CombatPointAdmission;
        Assert.AreSame(AreaUseScriptHost.PlacedAdmission, set);
        foreach (var action in new[] { ScrCatalogue.Action.Damage, ScrCatalogue.Action.DamageUnresistable, ScrCatalogue.Action.CastSpell, ScrCatalogue.Action.CastFreeSpell })
            Assert.IsTrue(set.Actions.Contains(action), "action " + action);
    }
}
