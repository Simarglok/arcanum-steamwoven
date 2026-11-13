using System.Buffers.Binary;
using Arcanum.Data.Area;
using Arcanum.Data.Content;
using Arcanum.Data.Scripts;
using Arcanum.Scripting.Scr;
using Arcanum.Session;
using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Reaction;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>
/// ARC-SCRIPT-1733 (ledger CS1–CS12): the use-script host's answers to the nine critter-state and social conditions (<see cref="IScrCritterStateQueries"/>) over an invented map and the session's live state (spell flags and object flags of a row, critter flags, the reaction
/// record, the danger record, the hero's concealment), and a script run under the placed set that asks one (no original data).
/// </summary>
[TestClass]
public sealed class AreaCritterStateScriptTests
{
    private const int NpcType = 16, ScenerySlotType = 3;
    private const byte Number = 3, GlobalFlag = 4, Extra = 12;
    private const int InvisibleSeen = 2430;

    private static AreaObjectKey Key(int index) => new(new string('a', 64), 1, AreaSourceKind.Mobile, 1, index, index * 100L, new string((char)('a' + index), 64), 100);

    // A critter row (type 16) with the stored critter flags (slot 36), spell flags (slot 19), object flags (slot 18) and every primary stat 20: the complete fields the combat reader needs.
    private static AreaObjectRecord Npc(int index, long critterFlags = 0, long spellFlags = 0, long objectFlags = 0, int script = 0)
    {
        AreaFieldValue Value(int slot, long value) => new(slot, AreaPresence.Value, AreaValueOrigin.Instance, value, [], null);
        AreaFieldValue Array(int slot, params long[] values) => new(slot, AreaPresence.Array, AreaValueOrigin.Instance, null, [.. values.Select((value, key) => new AreaSparseElement(key, [value]))], null);
        AreaFieldValue Empty(int slot) => new(slot, AreaPresence.Empty, AreaValueOrigin.Instance, null, [], null);
        var scripts = script == 0 ? Empty(31) : new(31, AreaPresence.Array, AreaValueOrigin.Instance, null, [new AreaSparseElement(7, [0, 0, script])], null);
        return new(Key(index), 0, NpcType, 28359, "p", "p", "r", "s", AreaScope.Core, null, 10 + index, 20, Arcanum.Data.Area.AreaDisposition.BodyEligibleConditionally, [],
        [
            Value(0, 0x60000000), Value(18, objectFlags), Value(19, spellFlags), Value(21, 1), Value(22, 2), Value(25, 0), Value(26, 20), Value(27, 0), Value(28, 0), Value(29, 4), Array(30), scripts,
            Value(36, critterFlags), Value(37, 0), Array(38, 20, 20, 20, 20, 20, 20, 20, 20, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 20, 1, 0),
            Array(39, 0, 0, 0, 5, 0, 0, 0, 0, 0, 0, 0, 0), Array(40), Array(41), Value(42, 10), Value(43, 0), Value(44, 0), Value(45, 0), Empty(46), Empty(47),
            Value(57, 0), Value(63, 0), Value(69, 0), Value(71, 0), Value(74, 10), Value(75, 10), Value(81, 0), Value(82, 0), Value(84, 0), Value(85, 0), Value(89, 0), Array(92),
        ], true, true, true);
    }

    // A weapon-like row (type 5) holding a hit script, or scenery (type 3) with no script, with the given spell flags (slot 19) and object flags (slot 18).
    private static AreaObjectRecord Plain(int index, int type, long spellFlags = 0, long objectFlags = 0, int script = 0)
    {
        var fields = new List<AreaFieldValue>
        {
            new(31, AreaPresence.Array, AreaValueOrigin.Instance, null, script == 0 ? [] : [new AreaSparseElement((int)ScrPoint.Hit, [0, 0, script])], null),
            new(18, AreaPresence.Value, AreaValueOrigin.Instance, objectFlags, [], null),
            new(19, AreaPresence.Value, AreaValueOrigin.Instance, spellFlags, [], null),
        };
        return new(Key(index), 0, type, 0, "p", "p", "r", "s", AreaScope.Core, null, 10 + index, 20, Arcanum.Data.Area.AreaDisposition.BodyEligibleConditionally, [], fields, true, true, true);
    }

    private static ScrObjectRef Ref(AreaObjectRecord row) => new(1, AreaUseScriptHost.EntityId(row));

    private static AreaUseScriptHost Host(GameSessionOwner session, AreaScriptWorld? world, params AreaObjectRecord[] shown) => new(session, 1, shown, [], null, world);

    private static AreaScriptWorld World(Func<string, string, bool?>? canSee = null, Func<string, string, bool?>? canHear = null) =>
        new(() => null, _ => false, _ => null, () => null, CanSee: canSee, CanHear: canHear);

    [TestMethod]
    public void Obj_is_invisible_reads_bit_one_of_the_spell_flags_of_any_object()
    {
        using var session = ConversationSessionFixture.CreateWithHero(1);
        var hidden = Npc(1, spellFlags: 0x1);
        var visible = Npc(2, spellFlags: 0x200);
        var item = Plain(3, 5, spellFlags: 0x1);
        var host = Host(session, null, hidden, visible, item);
        Assert.IsTrue(host.IsInvisible(Ref(hidden)));
        Assert.IsFalse(host.IsInvisible(Ref(visible))!.Value, "detect invisible (0x200) is not the invisible bit");
        Assert.IsTrue(host.IsInvisible(Ref(item)), "the original reads field 0x14 of any object type");
        Assert.IsFalse(host.IsInvisible(host.Pc)!.Value, "the hero has no invisibility spell");
        Assert.IsNull(host.IsInvisible(new(1, "oid:nobody")), "an object the host has no row for is not known");
    }

    [TestMethod]
    public void Obj_is_invulnerable_reads_object_flag_0x400000_of_the_row_and_the_hero()
    {
        using var session = ConversationSessionFixture.CreateWithHero(1);
        var immortal = Npc(1, objectFlags: 0x400000);
        var mortal = Npc(2, objectFlags: 0x2);
        var statue = Plain(3, ScenerySlotType, objectFlags: 0x400000);
        var host = Host(session, null, immortal, mortal, statue);
        Assert.IsTrue(host.IsInvulnerable(Ref(immortal)));
        Assert.IsFalse(host.IsInvulnerable(Ref(mortal))!.Value);
        Assert.IsTrue(host.IsInvulnerable(Ref(statue)), "any object type");
        Assert.IsFalse(host.IsInvulnerable(host.Pc)!.Value);
        Assert.IsNull(host.IsInvulnerable(new(1, "oid:nobody")));
    }

    [TestMethod]
    public void Obj_is_undead_reads_the_critter_flag_of_an_NPC_and_is_false_for_the_hero_and_other_objects()
    {
        using var session = ConversationSessionFixture.CreateWithHero(1);
        var undead = Npc(1, critterFlags: 0x4);
        var undeadAmongOthers = Npc(2, critterFlags: 0x4004);
        var animal = Npc(3, critterFlags: 0x8);
        var scenery = Plain(4, ScenerySlotType);
        var host = Host(session, null, undead, undeadAmongOthers, animal, scenery);
        Assert.IsTrue(host.IsUndead(Ref(undead)));
        Assert.IsTrue(host.IsUndead(Ref(undeadAmongOthers)));
        Assert.IsFalse(host.IsUndead(Ref(animal))!.Value, "the animal bit is 0x8");
        Assert.IsFalse(host.IsUndead(Ref(scenery))!.Value);
        Assert.IsFalse(host.IsUndead(host.Pc)!.Value, "the original tests the object type 0x10 first");
        Assert.IsNull(host.IsUndead(new(1, "oid:nobody")));
    }

    [TestMethod]
    public void Prowling_follows_the_hero_s_concealment_and_is_false_for_a_stranger_and_other_objects()
    {
        using var session = ConversationSessionFixture.CreateWithHero(1);
        var stranger = Npc(1);
        var scenery = Plain(2, ScenerySlotType);
        var prowler = Npc(3, critterFlags: 0x1);
        var host = Host(session, null, stranger, scenery, prowler);
        Assert.IsTrue(host.IsProwling(Ref(prowler)), "an NPC whose stored critter flags hold the prowling bit (0x45EDE0 reads 0xDA bit 1 of any NPC)");
        Assert.IsFalse(host.IsProwling(host.Pc)!.Value);
        Assert.AreEqual(GameSessionDisposition.Changed, session.ToggleHeroConcealment(session.Version).Disposition);
        Assert.IsTrue(host.IsProwling(host.Pc));
        Assert.IsFalse(host.IsProwling(Ref(stranger))!.Value);
        Assert.IsFalse(host.IsProwling(Ref(scenery))!.Value);
        Assert.IsNull(host.IsProwling(new(1, "oid:nobody")));
    }

    [TestMethod]
    public void Met_before_is_false_during_the_first_talk_true_after_it_and_for_the_same_object_and_false_for_other_pairs()
    {
        using var session = ConversationSessionFixture.CreateWithHero(1);
        var npc = Npc(1);
        var other = Npc(2);
        var host = Host(session, null, npc, other);
        var entity = AreaUseScriptHost.EntityId(npc);
        var facts = new ReactionNpcFacts(50, CharacterRace.Human, false, false);
        Assert.IsFalse(host.MetPcBefore(Ref(npc), host.Pc)!.Value, "a stranger");
        Assert.AreEqual(GameSessionDisposition.Changed, session.BeginReactionConversation(session.Version, entity, facts).Disposition);
        Assert.IsFalse(host.MetPcBefore(Ref(npc), host.Pc)!.Value, "the conversation slot is not a remembered one (0x4C0C40 reads slots 1..9)");
        Assert.AreEqual(GameSessionDisposition.Changed, session.EndReactionConversation(session.Version, entity, facts).Disposition);
        Assert.IsTrue(host.MetPcBefore(Ref(npc), host.Pc));
        Assert.IsFalse(host.MetPcBefore(Ref(other), host.Pc)!.Value, "another NPC has not met the hero");
        Assert.IsTrue(host.MetPcBefore(Ref(other), Ref(other)), "the same object twice is true");
        Assert.IsFalse(host.MetPcBefore(host.Pc, Ref(npc))!.Value, "the second object must be the PC");
        Assert.IsFalse(host.MetPcBefore(Ref(npc), Ref(other))!.Value);
        Assert.IsNull(host.MetPcBefore(new(1, "oid:nobody"), host.Pc), "an NPC the host has no row for is not known");
    }

    [TestMethod]
    public void Obj_is_in_dialog_reads_the_conversation_slot_of_an_NPC_and_stops_for_any_other_object()
    {
        using var session = ConversationSessionFixture.CreateWithHero(1);
        var npc = Npc(1);
        var scenery = Plain(2, ScenerySlotType);
        var host = Host(session, null, npc, scenery);
        var entity = AreaUseScriptHost.EntityId(npc);
        var facts = new ReactionNpcFacts(50, CharacterRace.Human, false, false);
        Assert.IsFalse(host.IsInDialog(Ref(npc))!.Value);
        Assert.AreEqual(GameSessionDisposition.Changed, session.BeginReactionConversation(session.Version, entity, facts).Disposition);
        Assert.IsTrue(host.IsInDialog(Ref(npc)));
        Assert.AreEqual(GameSessionDisposition.Changed, session.EndReactionConversation(session.Version, entity, facts).Disposition);
        Assert.IsFalse(host.IsInDialog(Ref(npc))!.Value, "the talk ended");
        // Any other object type goes to the dialog layer's callback (0x567400 over the PC's dialog slot), which this host does not hold.
        Assert.IsNull(host.IsInDialog(host.Pc));
        Assert.IsNull(host.IsInDialog(Ref(scenery)));
        Assert.IsNull(host.IsInDialog(new(1, "oid:nobody")));
    }

    [TestMethod]
    public void Obj_has_surrendered_reads_the_danger_state_of_an_NPC_and_is_false_for_the_hero_and_other_objects()
    {
        using var session = ConversationSessionFixture.CreateWithHero(1);
        var npc = Npc(1);
        var calm = Npc(2);
        var scenery = Plain(3, ScenerySlotType);
        var host = Host(session, null, npc, calm, scenery);
        Assert.IsFalse(host.IsSurrendered(Ref(npc))!.Value);
        var set = session.SetDangerState(session.Version, new SessionDangerRequest { Entity = AreaUseScriptHost.EntityId(npc), State = DangerState.Surrender, Target = SessionCombatant.Hero });
        Assert.AreEqual(GameSessionDisposition.Changed, set.Disposition, $"{set.Failure}");
        Assert.IsTrue(host.IsSurrendered(Ref(npc)));
        Assert.IsFalse(host.IsSurrendered(Ref(calm))!.Value);
        Assert.IsFalse(host.IsSurrendered(Ref(scenery))!.Value);
        Assert.IsFalse(host.IsSurrendered(host.Pc)!.Value);
        Assert.IsNull(host.IsSurrendered(new(1, "oid:nobody")));
    }

    [TestMethod]
    public void Can_see_and_can_hear_go_through_the_world_for_an_NPC_of_the_shown_map_toward_an_NPC_or_the_hero()
    {
        using var session = ConversationSessionFixture.CreateWithHero(1);
        var guard = Npc(1);
        var thief = Npc(2);
        var item = Plain(3, 5);
        var asked = new List<(string Kind, string From, string To)>();
        var world = World((from, to) => { asked.Add(("see", from, to)); return from == AreaUseScriptHost.EntityId(guard); }, (from, to) => { asked.Add(("hear", from, to)); return to == "pc"; });
        var host = Host(session, world, guard, thief, item);
        Assert.IsTrue(host.CanSee(Ref(guard), Ref(thief)));
        Assert.IsFalse(host.CanSee(Ref(thief), Ref(guard))!.Value);
        Assert.IsTrue(host.CanHear(Ref(guard), host.Pc), "the PC target goes by the scheduler's id");
        Assert.IsFalse(host.CanHear(Ref(guard), Ref(thief))!.Value);
        CollectionAssert.AreEqual(new[]
        {
            ("see", AreaUseScriptHost.EntityId(guard), AreaUseScriptHost.EntityId(thief)), ("see", AreaUseScriptHost.EntityId(thief), AreaUseScriptHost.EntityId(guard)),
            ("hear", AreaUseScriptHost.EntityId(guard), "pc"), ("hear", AreaUseScriptHost.EntityId(guard), AreaUseScriptHost.EntityId(thief)),
        }, asked);
        // The hero as the observer (its facing is not tracked), a row that is no NPC, an object of another map or none, and a host without the world's check: not modelled.
        Assert.IsNull(host.CanSee(host.Pc, Ref(guard)));
        Assert.IsNull(host.CanSee(Ref(item), Ref(guard)));
        Assert.IsNull(host.CanSee(Ref(guard), Ref(item)));
        Assert.IsNull(host.CanSee(new(2, AreaUseScriptHost.EntityId(guard)), Ref(thief)));
        Assert.IsNull(host.CanHear(Ref(guard), new(1, "oid:nobody")));
        Assert.IsNull(Host(session, null, guard, thief).CanSee(Ref(guard), Ref(thief)));
        Assert.AreEqual(4, asked.Count, "a refused pair asks nothing");
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
    public void A_combat_point_script_asking_obj_is_invisible_runs_under_the_placed_set_and_reads_the_victim_s_spell_flags()
    {
        var root = Directory.CreateTempSubdirectory("arc-script-1733-");
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
            // 931: when the extra object (the victim) is invisible, set a global flag.
            Write("scr/00931invisible.scr", Program(
                ((ScrCatalogue.Condition.IsInvisible, [(Extra, 0)]), (ScrCatalogue.Action.Assign, [(GlobalFlag, InvisibleSeen), (Number, 1)]), (0, [])),
                ((0, []), (ScrCatalogue.Action.ReturnRunDefault, []), (0, []))));
            using var content = ScriptContent.Open(ContentMountPlan.Discover(root.FullName, "Syn").Plan!).Content!;
            foreach (var (spellFlags, expected) in new[] { (0x1L, true), (0x0L, false) })
            {
                using var session = ConversationSessionFixture.CreateWithHero(1);
                var weapon = Plain(1, 5, script: 931);
                var attacker = Npc(2);
                var victim = Npc(3, spellFlags: spellFlags);
                var host = new AreaUseScriptHost(session, 1, [weapon, attacker, victim], [], content, World());
                var ran = host.RunCombatPoint(ScrPoint.Hit, Ref(weapon), Ref(attacker), Ref(victim), session.Rules.Scripts);
                Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, ran.Outcome, ran.Detail);
                Assert.AreEqual(expected, session.Flags.Read(new(new(Guid.NewGuid()), session.FlagVersion, new Arcanum.Scripting.GlobalFlags.FlagId(InvisibleSeen))).Value == true, $"spell flags {spellFlags}");
            }
        }
        finally { root.Delete(true); }
    }
}
