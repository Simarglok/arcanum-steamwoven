using System.Buffers.Binary;
using Arcanum.Data.Area;
using Arcanum.Data.Content;
using Arcanum.Data.Scripts;
using Arcanum.Scripting.Scr;
using Arcanum.Session;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>
/// ARC-SCRIPT-1771 (ledger CS12–CS14): the use-script host's answers to obj has bad associates (11) and obj is monster of type (19) over an invented map and the session's party, spell flags, object flags and critter flags, and a script run under the placed set
/// that asks obj is within range (34) of the object's location (no original data).
/// </summary>
[TestClass]
public sealed class AreaCritterTailScriptTests
{
    private const int NpcType = 16, ScenerySlotType = 3;
    private const byte Number = 3, GlobalFlag = 4, Extra = 12;
    private const int NearSeen = 2431;
    private const long Summoned = 0x4000;

    private static AreaObjectKey Key(int index) => new(new string('a', 64), 1, AreaSourceKind.Mobile, 1, index, index * 100L, new string((char)('a' + index), 64), 100);

    // A critter row (type 16) with the stored critter flags (slot 36), spell flags (slot 19), object flags (slot 18), NPC flags (slot 69) and base art (slot 23, left out when null) and every primary stat 20.
    private static AreaObjectRecord Npc(int index, long critterFlags = 0, long spellFlags = 0, long objectFlags = 0, long npcFlags = 0, long? baseArt = null, int script = 0)
    {
        AreaFieldValue Value(int slot, long value) => new(slot, AreaPresence.Value, AreaValueOrigin.Instance, value, [], null);
        AreaFieldValue Array(int slot, params long[] values) => new(slot, AreaPresence.Array, AreaValueOrigin.Instance, null, [.. values.Select((value, key) => new AreaSparseElement(key, [value]))], null);
        AreaFieldValue Empty(int slot) => new(slot, AreaPresence.Empty, AreaValueOrigin.Instance, null, [], null);
        var scripts = script == 0 ? Empty(31) : new(31, AreaPresence.Array, AreaValueOrigin.Instance, null, [new AreaSparseElement(7, [0, 0, script])], null);
        var fields = new List<AreaFieldValue>
        {
            Value(0, 0x60000000), Value(18, objectFlags), Value(19, spellFlags), Value(21, 1), Value(22, 2), Value(25, 0), Value(26, 20), Value(27, 0), Value(28, 0), Value(29, 4), Array(30), scripts,
            Value(36, critterFlags), Value(37, 0), Array(38, 20, 20, 20, 20, 20, 20, 20, 20, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 20, 1, 0),
            Array(39, 0, 0, 0, 5, 0, 0, 0, 0, 0, 0, 0, 0), Array(40), Array(41), Value(42, 10), Value(43, 0), Value(44, 0), Value(45, 0), Empty(46), Empty(47),
            Value(57, 0), Value(63, 0), Value(69, npcFlags), Value(71, 0), Value(74, 10), Value(75, 10), Value(81, 0), Value(82, 0), Value(84, 0), Value(85, 0), Value(89, 0), Array(92),
        };
        if (baseArt is { } art) fields.Add(Value(23, art));
        return new(Key(index), 0, NpcType, 28359, "p", "p", "r", "s", AreaScope.Core, null, 10 + index, 20, Arcanum.Data.Area.AreaDisposition.BodyEligibleConditionally, [], fields, true, true, true);
    }

    private static AreaObjectRecord Plain(int index, int type, long? baseArt = null, int script = 0)
    {
        var fields = new List<AreaFieldValue>
        {
            new(31, AreaPresence.Array, AreaValueOrigin.Instance, null, script == 0 ? [] : [new AreaSparseElement((int)ScrPoint.Hit, [0, 0, script])], null),
            new(18, AreaPresence.Value, AreaValueOrigin.Instance, 0L, [], null),
            new(19, AreaPresence.Value, AreaValueOrigin.Instance, 0L, [], null),
        };
        if (baseArt is { } art) fields.Add(new(23, AreaPresence.Value, AreaValueOrigin.Instance, art, [], null));
        return new(Key(index), 0, type, 0, "p", "p", "r", "s", AreaScope.Core, null, 10 + index, 20, Arcanum.Data.Area.AreaDisposition.BodyEligibleConditionally, [], fields, true, true, true);
    }

    private static ScrObjectRef Ref(AreaObjectRecord row) => new(1, AreaUseScriptHost.EntityId(row));

    private static AreaUseScriptHost Host(GameSessionOwner session, params AreaObjectRecord[] shown) => new(session, 1, shown, [], null, new(() => null, _ => false, _ => null, () => null));

    private static void Follow(GameSessionOwner session, AreaObjectRecord row) =>
        Assert.AreEqual(GameSessionDisposition.Changed, session.ForceJoinParty(session.Version, AreaUseScriptHost.EntityId(row)).Disposition);

    [TestMethod]
    public void Bad_associates_of_the_hero_is_true_when_a_follower_is_summoned_and_not_an_animal_nor_a_stray_nor_flagged()
    {
        using var session = ConversationSessionFixture.CreateWithHero(1);
        var ordinary = Npc(1);
        var animal = Npc(2, spellFlags: Summoned, critterFlags: 0x8);
        var stray = Npc(3, spellFlags: Summoned, npcFlags: 0x8000);
        var dead = Npc(4, spellFlags: Summoned, objectFlags: 0x2);
        var hidden = Npc(5, spellFlags: Summoned, objectFlags: 0x100);
        var summon = Npc(6, spellFlags: Summoned);
        var host = Host(session, ordinary, animal, stray, dead, hidden, summon);
        Assert.IsFalse(host.HasBadAssociates(host.Pc)!.Value, "no followers");
        foreach (var row in new[] { ordinary, animal, stray, dead, hidden })
        {
            Follow(session, row);
            Assert.IsFalse(host.HasBadAssociates(host.Pc)!.Value, $"{AreaUseScriptHost.EntityId(row)} does not make one (not summoned, animal, stray, or object flag 0x2 / 0x100 set)");
        }
        Follow(session, summon);
        Assert.IsTrue(host.HasBadAssociates(host.Pc), "a summoned follower with none of the exclusions");
    }

    [TestMethod]
    public void Followers_of_the_hero_are_the_party_followers_in_join_order_and_other_leaders_and_objects_have_none()
    {
        // ARC-SCRIPT-2320 (FK2): the direct followers, unfiltered (a switched-off follower stays); an NPC without followers and a non-critter name none.
        using var session = ConversationSessionFixture.CreateWithHero(1);
        var first = Npc(1);
        var off = Npc(2, objectFlags: 0x2);
        var stranger = Npc(3);
        var scenery = Plain(4, ScenerySlotType);
        var host = Host(session, first, off, stranger, scenery);
        Assert.AreEqual(0, host.FollowersOf(host.Pc).Count, "no followers yet");
        Follow(session, first); Follow(session, off);
        CollectionAssert.AreEqual(new[] { Ref(first), Ref(off) }, host.FollowersOf(host.Pc).ToArray());
        Assert.AreEqual(0, host.FollowersOf(Ref(stranger)).Count, "an NPC that leads nobody");
        Assert.AreEqual(0, host.FollowersOf(Ref(scenery)).Count, "a scenery has no follower array");
        Assert.AreEqual(0, host.FollowersOf(new(1, "oid:nobody")).Count);
        Assert.AreEqual(0, Host(ConversationSessionFixture.Create(), first).FollowersOf(new(1, "pc")).Count, "a session without a hero has no party");
    }

    [TestMethod]
    public void The_use_placed_heartbeat_combat_point_and_dying_sets_admit_the_follower_focus_kinds_and_stay_inside_S62()
    {
        foreach (var set in new[] { AreaUseScriptHost.Admission, AreaUseScriptHost.PlacedAdmission, AreaUseScriptHost.HeartbeatAdmission, AreaUseScriptHost.CombatPointAdmission, AreaUseScriptHost.DyingAdmission })
        {
            Assert.IsTrue(set.FocusKinds.Contains(ScrFocusKind.EveryFollower) && set.FocusKinds.Contains(ScrFocusKind.AnyFollower), set.Id);
            Assert.IsTrue(set.IsSubsetOfBesidesStores(ScrAdmissionSet.S65), set.Id);
        }
    }

    [TestMethod]
    public void Bad_associates_of_a_follower_reads_the_hero_s_followers_and_is_false_without_a_leader_and_for_other_objects()
    {
        using var session = ConversationSessionFixture.CreateWithHero(1);
        var companion = Npc(1);
        var stranger = Npc(2);
        var summon = Npc(3, spellFlags: Summoned);
        var scenery = Plain(4, ScenerySlotType);
        var host = Host(session, companion, stranger, summon, scenery);
        Follow(session, companion);
        Assert.IsFalse(host.HasBadAssociates(Ref(companion))!.Value);
        Follow(session, summon);
        Assert.IsTrue(host.HasBadAssociates(Ref(companion)), "the leader of the companion is the hero, whose followers include the summon");
        Assert.IsFalse(host.HasBadAssociates(Ref(stranger))!.Value, "an NPC without a leader (0x45DDA0 gives none)");
        Assert.IsFalse(host.HasBadAssociates(Ref(scenery))!.Value, "an object that is no critter has no leader");
        Assert.IsNull(host.HasBadAssociates(new(1, "oid:nobody")));
    }

    [TestMethod]
    public void Bad_associates_is_unknown_for_a_follower_without_a_row_unless_another_follower_decides_it()
    {
        using var session = ConversationSessionFixture.CreateWithHero(1);
        var away = Npc(1, spellFlags: Summoned);
        var summon = Npc(2, spellFlags: Summoned);
        var host = Host(session, summon);
        Follow(session, away);
        Assert.IsNull(host.HasBadAssociates(host.Pc), "the follower is on no loaded map: its flags are unknown");
        Follow(session, summon);
        Assert.IsTrue(host.HasBadAssociates(host.Pc), "another follower decides it");
        Assert.IsNull(Host(ConversationSessionFixture.Create(), summon).HasBadAssociates(new(1, "pc")), "a session without a hero asks nothing of the party");
    }

    [TestMethod]
    public void Monster_of_type_reads_the_art_class_and_the_monster_number_of_the_base_art()
    {
        using var session = ConversationSessionFixture.CreateWithHero(1);
        long Monster(int number) => (0xCL << 28) | ((long)number << 23);
        var rat = Npc(1, baseArt: Monster(21));
        var human = Npc(2, baseArt: 0x20000000 | (21L << 23));
        var unique = Npc(3, baseArt: 0xD0000000L | (21L << 23));
        var unread = Npc(4);
        var scenery = Plain(5, ScenerySlotType);
        var painted = Plain(6, ScenerySlotType, baseArt: Monster(21));
        var host = Host(session, rat, human, unique, unread, scenery, painted);
        Assert.IsTrue(host.IsMonsterOfType(Ref(rat), 21));
        Assert.IsFalse(host.IsMonsterOfType(Ref(rat), 20)!.Value);
        Assert.IsFalse(host.IsMonsterOfType(Ref(human), 21)!.Value, "a human art (class 2) has no monster number");
        Assert.IsFalse(host.IsMonsterOfType(Ref(unique), 21)!.Value, "a unique NPC art (class 13) is not a monster art");
        Assert.IsNull(host.IsMonsterOfType(Ref(unread), 0), "an NPC whose art field does not read is not known");
        Assert.IsFalse(host.IsMonsterOfType(Ref(scenery), 0)!.Value, "a row without the field reads 0, art class 0");
        Assert.IsTrue(host.IsMonsterOfType(Ref(painted), 21), "the original reads field 0x18 of any object");
        Assert.IsFalse(host.IsMonsterOfType(host.Pc, 0)!.Value, "the hero's base art is a human critter art");
        Assert.IsNull(host.IsMonsterOfType(new(1, "oid:nobody"), 0));
    }

    private static byte[] Program(params ((int Type, (byte Kind, int Value)[] Operands) Condition, (int Type, (byte Kind, int Value)[] Operands) OnTrue, (int Type, (byte Kind, int Value)[] Operands) OnFalse)[] lines)
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

    [TestMethod]
    public void A_combat_point_script_asking_obj_is_within_range_runs_under_the_placed_set_and_reads_the_victim_s_tile()
    {
        var root = Directory.CreateTempSubdirectory("arc-script-1771-");
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
            // The victim stands at tile (13, 20): within 3 tiles of (10, 20) and not within 2.
            foreach (var (range, expected) in new[] { (3, true), (2, false) })
            {
                Write("scr/00932near.scr", Program(
                    ((ScrCatalogue.Condition.WithinRange, [(Extra, 0), (Number, range), (Number, 10), (Number, 20)]), (ScrCatalogue.Action.Assign, [(GlobalFlag, NearSeen), (Number, 1)]), (0, [])),
                    ((0, []), (ScrCatalogue.Action.ReturnRunDefault, []), (0, []))));
                using var content = ScriptContent.Open(ContentMountPlan.Discover(root.FullName, "Syn").Plan!).Content!;
                using var session = ConversationSessionFixture.CreateWithHero(1);
                var weapon = Plain(1, 5, script: 932);
                var attacker = Npc(2);
                var victim = Npc(3);
                var host = new AreaUseScriptHost(session, 1, [weapon, attacker, victim], [], content, new(() => null, _ => false, _ => null, () => null));
                var ran = host.RunCombatPoint(ScrPoint.Hit, Ref(weapon), Ref(attacker), Ref(victim), session.Rules.Scripts);
                Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, ran.Outcome, ran.Detail);
                Assert.AreEqual(expected, session.Flags.Read(new(new(Guid.NewGuid()), session.FlagVersion, new Arcanum.Scripting.GlobalFlags.FlagId(NearSeen))).Value == true, $"range {range}");
            }
        }
        finally { root.Delete(true); }
    }
}
