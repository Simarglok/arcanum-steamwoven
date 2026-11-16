using System.Buffers.Binary;
using Arcanum.Data.Area;
using Arcanum.Data.Content;
using Arcanum.Data.Scripts;
using Arcanum.Scripting.Scr;
using Arcanum.Session;
using Arcanum.Simulation.Character;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>
/// ARC-SCRIPT-1772 (ledger CQ1–CQ8): the use-script host's answers to the critter getters (<see cref="IScrCritterFieldQueries"/>) and its writes of toggle invulnerability and rename through the session, over an invented map and the session's live state
/// (the object flags of the hero and an NPC, a set faction, a dialog's set origin, a renamed name number), and combat-point scripts that run them under the placed set (no original data).
/// </summary>
[TestClass]
public sealed class AreaCritterFieldScriptTests
{
    private const int NpcType = 16, ScenerySlotType = 3, StoredName = 21, StoredOrigin = 7, StoredFaction = 5, StoredClass = 2;
    private const byte Number = 3, Extra = 12;
    private const long InvulnerableFlag = 0x400000;

    private static AreaObjectKey Key(int index) => new(new string('a', 64), 1, AreaSourceKind.Mobile, 1, index, index * 100L, new string((char)('a' + index), 64), 100);

    // A critter row (type 16) with name 21 (slot 21), object flags (slot 18), origin 7 (slot 80), faction 5 (slot 81), social class 2 (slot 85) and the fields the combat reader needs.
    private static AreaObjectRecord Npc(int index, long objectFlags = 0)
    {
        AreaFieldValue Value(int slot, long value) => new(slot, AreaPresence.Value, AreaValueOrigin.Instance, value, [], null);
        AreaFieldValue Array(int slot, params long[] values) => new(slot, AreaPresence.Array, AreaValueOrigin.Instance, null, [.. values.Select((value, key) => new AreaSparseElement(key, [value]))], null);
        AreaFieldValue Empty(int slot) => new(slot, AreaPresence.Empty, AreaValueOrigin.Instance, null, [], null);
        return new(Key(index), 0, NpcType, 28359, "p", "p", "r", "s", AreaScope.Core, null, 10 + index, 20, Arcanum.Data.Area.AreaDisposition.BodyEligibleConditionally, [],
        [
            Value(0, 0x60000000), Value(18, objectFlags), Value(19, 0), Value(21, StoredName), Value(22, 2), Value(25, 0), Value(26, 20), Value(27, 0), Value(28, 0), Value(29, 4), Array(30), Empty(31),
            Value(36, 0), Value(37, 0), Array(38, 20, 20, 20, 20, 20, 20, 20, 20, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 20, 1, 0),
            Array(39, 0, 0, 0, 5, 0, 0, 0, 0, 0, 0, 0, 0), Array(40), Array(41), Value(42, 10), Value(43, 0), Value(44, 0), Value(45, 0), Empty(46), Empty(47),
            Value(57, 0), Value(63, 0), Value(69, 0), Value(71, 0), Value(74, 10), Value(75, 10), Value(80, StoredOrigin), Value(81, StoredFaction), Value(82, 0), Value(84, 0), Value(85, StoredClass), Value(89, 0), Array(92),
        ], true, true, true);
    }

    // A weapon-like row (type 5) or scenery (type 3) holding a hit script, with object flags (slot 18).
    private static AreaObjectRecord Plain(int index, int type, int script = 0, long objectFlags = 0)
    {
        var fields = new List<AreaFieldValue>
        {
            new(31, AreaPresence.Array, AreaValueOrigin.Instance, null, script == 0 ? [] : [new AreaSparseElement((int)ScrPoint.Hit, [0, 0, script])], null),
            new(18, AreaPresence.Value, AreaValueOrigin.Instance, objectFlags, [], null),
            new(21, AreaPresence.Value, AreaValueOrigin.Instance, StoredName, [], null),
        };
        return new(Key(index), 0, type, 0, "p", "p", "r", "s", AreaScope.Core, null, 10 + index, 20, Arcanum.Data.Area.AreaDisposition.BodyEligibleConditionally, [], fields, true, true, true);
    }

    private static ScrObjectRef Ref(AreaObjectRecord row) => new(1, AreaUseScriptHost.EntityId(row));

    [TestMethod]
    public void The_getters_read_an_NPC_s_fields_and_answer_zero_for_the_hero_and_other_objects()
    {
        using var session = ConversationSessionFixture.CreateWithHero(1);
        var npc = Npc(1);
        var scenery = Plain(2, ScenerySlotType);
        var host = new AreaUseScriptHost(session, 1, [npc, scenery], [], null, null);
        Assert.AreEqual((StoredClass, StoredOrigin, StoredFaction), (host.SocialClassOf(Ref(npc)), host.OriginOf(Ref(npc)), host.FactionOf(Ref(npc))));
        Assert.AreEqual((0, 0, 0), (host.SocialClassOf(Ref(scenery)), host.OriginOf(Ref(scenery)), host.FactionOf(Ref(scenery))), "the original answers 0 for any object type but an NPC (0x10)");
        Assert.AreEqual((0, 0, 0), (host.SocialClassOf(host.Pc), host.OriginOf(host.Pc), host.FactionOf(host.Pc)), "the hero is type 0xF");
        var nobody = new ScrObjectRef(1, "oid:nobody");
        Assert.IsNull(host.SocialClassOf(nobody));
        Assert.IsNull(host.OriginOf(nobody));
        Assert.IsNull(host.FactionOf(nobody), "an object the host has no row for is not known");
    }

    [TestMethod]
    public void The_getters_read_a_set_faction_and_a_set_origin_over_the_map_data()
    {
        using var session = ConversationSessionFixture.CreateWithHero(1);
        var npc = Npc(1);
        var host = new AreaUseScriptHost(session, 1, [npc], [], null, null);
        var entity = AreaUseScriptHost.EntityId(npc);
        Assert.AreEqual(GameSessionDisposition.Changed, session.ApplyChanges(session.Version, null, [new(new SessionMapId(1), MapChangeKey.ForEntity(entity, AreaUseScriptHost.FactionProperty), 11)]).Disposition);
        Assert.AreEqual(GameSessionDisposition.Changed, session.ApplyChanges(session.Version, null, [new(new SessionMapId(1), SessionNpcOrigin.Key(entity), 3)]).Disposition);
        Assert.AreEqual((3, 11, StoredClass), (host.OriginOf(Ref(npc)), host.FactionOf(Ref(npc)), host.SocialClassOf(Ref(npc))));
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

    private static (int Type, (byte Kind, int Value)[] Operands) Act(int type, params (byte, int)[] operands) => (type, operands);

    private static AreaScriptedUse RunHit(GameSessionOwner session, ScriptContent content, AreaObjectRecord weapon, AreaObjectRecord attacker, AreaObjectRecord victim, out AreaUseScriptHost host)
    {
        host = new AreaUseScriptHost(session, 1, [weapon, attacker, victim], [], content, new(() => null, _ => false, _ => null, () => null));
        return host.RunCombatPoint(ScrPoint.Hit, Ref(weapon), Ref(attacker), Ref(victim), session.Rules.Scripts);
    }

    private static void WithScripts(Action<ScriptContent> body)
    {
        var root = Directory.CreateTempSubdirectory("arc-script-1772-");
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
            // 941: toggle invulnerability of the extra object (the victim). 942: rename it to 6409. 943: rename it to its map data's number. 944: toggle then read it back (the run's own flip) into a global flag.
            Write("scr/00941toggle.scr", Program((Act(0), Act(ScrCatalogue.Action.ToggleInvulnerability, (Extra, 0)), Act(0)), (Act(0), Act(ScrCatalogue.Action.ReturnRunDefault), Act(0))));
            Write("scr/00942rename.scr", Program((Act(0), Act(ScrCatalogue.Action.Rename, (Extra, 0), (Number, 6409)), Act(0)), (Act(0), Act(ScrCatalogue.Action.ReturnRunDefault), Act(0))));
            Write("scr/00943restore.scr", Program((Act(0), Act(ScrCatalogue.Action.Rename, (Extra, 0), (Number, StoredName)), Act(0)), (Act(0), Act(ScrCatalogue.Action.ReturnRunDefault), Act(0))));
            using var content = ScriptContent.Open(ContentMountPlan.Discover(root.FullName, "Syn").Plan!).Content!;
            body(content);
        }
        finally { root.Delete(true); }
    }

    [TestMethod]
    public void A_script_s_toggle_invulnerability_flips_the_flag_of_an_NPC_in_the_session_and_a_second_run_flips_it_back()
    {
        WithScripts(content =>
        {
            using var session = ConversationSessionFixture.CreateWithHero(1);
            var weapon = Plain(1, 5, script: 941);
            var attacker = Npc(2);
            var victim = Npc(3);
            var entity = AreaUseScriptHost.EntityId(victim);
            var ran = RunHit(session, content, weapon, attacker, victim, out var host);
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, ran.Outcome, ran.Detail);
            Assert.IsTrue(host.IsInvulnerable(Ref(victim)));
            Assert.AreEqual(0x400000, session.QueryObjectFlags(entity).ObjectSet, "the session holds the written bit over the map data's");
            ran = RunHit(session, content, weapon, attacker, victim, out host);
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, ran.Outcome, ran.Detail);
            Assert.IsFalse(host.IsInvulnerable(Ref(victim)));
            Assert.IsTrue(session.QueryObjectFlags(entity).IsEmpty, "back to the data's value: nothing is kept");
        });
    }

    [TestMethod]
    public void Toggle_invulnerability_clears_a_bit_the_map_data_stores_and_a_non_NPC_target_refuses_the_run()
    {
        WithScripts(content =>
        {
            using var session = ConversationSessionFixture.CreateWithHero(1);
            var weapon = Plain(1, 5, script: 941);
            var attacker = Npc(2);
            var immortal = Npc(3, objectFlags: InvulnerableFlag);
            var ran = RunHit(session, content, weapon, attacker, immortal, out var host);
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, ran.Outcome, ran.Detail);
            Assert.IsFalse(host.IsInvulnerable(Ref(immortal)), "the stored bit is cleared by the toggle");
            // A scenery target: not modelled, the run is left unapplied and the session is untouched.
            var scenery = Plain(4, ScenerySlotType, objectFlags: InvulnerableFlag);
            var before = session.Version;
            var refused = RunHit(session, content, weapon, attacker, scenery, out _);
            Assert.AreEqual(AreaScriptedUseOutcome.EffectsRejected, refused.Outcome, refused.Detail);
            Assert.AreEqual(before, session.Version);
        });
    }

    [TestMethod]
    public void A_script_s_rename_gives_an_NPC_a_new_name_number_the_name_readers_see_and_the_data_s_number_clears_it()
    {
        WithScripts(content =>
        {
            using var session = ConversationSessionFixture.CreateWithHero(1);
            var weapon = Plain(1, 5, script: 942);
            var attacker = Npc(2);
            var victim = Npc(3);
            var host = new AreaUseScriptHost(session, 1, [weapon, attacker, victim], [], content, new(() => null, _ => false, _ => null, () => null));
            Assert.AreEqual(StoredName, host.NameOf(Ref(victim)));
            var ran = RunHit(session, content, weapon, attacker, victim, out host);
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, ran.Outcome, ran.Detail);
            Assert.AreEqual(6409, host.NameOf(Ref(victim)));
            Assert.AreEqual(6409, AreaUseScriptHost.NameOf(session, victim), "the dialog's follower test reads it too");
            Assert.AreEqual(StoredName, AreaUseScriptHost.NameOf(victim), "the map data's number is untouched");
            Assert.AreEqual(StoredName, host.NameOf(Ref(attacker)), "another NPC keeps its own");
            CollectionAssert.AreEqual(new[] { SessionNpcName.Property }, session.QueryObjectState(AreaUseScriptHost.EntityId(victim)).Select(field => field.Property).ToArray());
            var restore = Plain(5, 5, script: 943);
            ran = RunHit(session, content, restore, attacker, victim, out host);
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, ran.Outcome, ran.Detail);
            Assert.AreEqual(StoredName, host.NameOf(Ref(victim)));
            Assert.AreEqual(0, session.QueryObjectState(AreaUseScriptHost.EntityId(victim)).Count, "equal to the data's number: no record is kept");
        });
    }

    [TestMethod]
    public void Rename_of_a_non_NPC_refuses_the_run_and_the_new_name_survives_a_save()
    {
        WithScripts(content =>
        {
            using var session = ConversationSessionFixture.CreateWithHero(1);
            var weapon = Plain(1, 5, script: 942);
            var attacker = Npc(2);
            var scenery = Plain(3, ScenerySlotType);
            var before = session.Version;
            var refused = RunHit(session, content, weapon, attacker, scenery, out _);
            Assert.AreEqual(AreaScriptedUseOutcome.EffectsRejected, refused.Outcome, refused.Detail);
            StringAssert.Contains(refused.Detail, "only an NPC");
            Assert.AreEqual(before, session.Version);
            var victim = Npc(4);
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, RunHit(session, content, weapon, attacker, victim, out _).Outcome);
            var text = GameSessionSnapshotCodec.Encode(session.Capture());
            StringAssert.Contains(text, "npc.name");
        });
    }

    [TestMethod]
    public void The_hero_s_invulnerability_toggles_through_the_session_and_renaming_the_hero_or_toggling_after_a_rename_in_one_run_behaves()
    {
        WithScripts(content =>
        {
            using var session = ConversationSessionFixture.CreateWithHero(1);
            var weapon = Plain(1, 5, script: 941);
            var attacker = Npc(2);
            var host = new AreaUseScriptHost(session, 1, [weapon, attacker], [], content, new(() => null, _ => false, _ => null, () => null));
            var ran = host.RunCombatPoint(ScrPoint.Hit, Ref(weapon), Ref(attacker), host.Pc, session.Rules.Scripts);
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, ran.Outcome, ran.Detail);
            Assert.IsTrue(host.IsInvulnerable(host.Pc));
            Assert.AreEqual(0x400000, session.QueryObjectFlags(AreaScriptHost.PcCode).ObjectSet);
            ran = host.RunCombatPoint(ScrPoint.Hit, Ref(weapon), Ref(attacker), host.Pc, session.Rules.Scripts);
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, ran.Outcome, ran.Detail);
            Assert.IsFalse(host.IsInvulnerable(host.Pc));
            Assert.IsTrue(session.QueryObjectFlags(AreaScriptHost.PcCode).IsEmpty);
            // The hero's name number is not kept: a rename of the hero refuses the run, nothing applies.
            var rename = Plain(3, 5, script: 942);
            var hosted = new AreaUseScriptHost(session, 1, [rename, attacker], [], content, new(() => null, _ => false, _ => null, () => null));
            var before = session.Version;
            var refused = hosted.RunCombatPoint(ScrPoint.Hit, Ref(rename), Ref(attacker), hosted.Pc, session.Rules.Scripts);
            Assert.AreEqual(AreaScriptedUseOutcome.EffectsRejected, refused.Outcome, refused.Detail);
            StringAssert.Contains(refused.Detail, "rename of the PC");
            Assert.AreEqual(before, session.Version);
        });
    }

    [TestMethod]
    public void The_use_placed_heartbeat_dying_and_combat_point_sets_hold_the_five_commands_and_the_talk_set_the_getters()
    {
        foreach (var set in new[] { AreaUseScriptHost.Admission, AreaUseScriptHost.PlacedAdmission, AreaUseScriptHost.HeartbeatAdmission, AreaUseScriptHost.DyingAdmission, AreaUseScriptHost.CombatPointAdmission })
        {
            Assert.IsTrue(new[] { 38, 39, 57, 89, 92 }.All(set.Actions.Contains), set.Id);
            Assert.IsTrue(set.IsSubsetOfBesidesStores(ScrAdmissionSet.S65), set.Id);
        }
        Assert.IsTrue(new[] { 38, 39, 89 }.All(AreaScriptHost.TalkAdmission.Actions.Contains));
        Assert.IsFalse(AreaScriptHost.TalkAdmission.Actions.Contains(ScrCatalogue.Action.ToggleInvulnerability) || AreaScriptHost.TalkAdmission.Actions.Contains(ScrCatalogue.Action.Rename));
    }

    [TestMethod]
    public void Get_armor_reads_the_armour_part_of_the_stored_current_art_and_the_bare_hero_has_none()
    {
        using var session = ConversationSessionFixture.CreateWithHero(1);
        AreaObjectRecord WithArt(int index, uint art) { var row = Npc(index); return row with { EffectiveFields = [.. row.EffectiveFields.Select(f => f.Slot == 0 ? f with { Number = art } : f)] }; }
        var chain = WithArt(1, (2u << 28) | (3u << 20));
        var plate = WithArt(2, (2u << 28) | (6u << 20) | 0x30);
        var monster = WithArt(3, (12u << 28) | (5u << 20) | (0xFu << 23));
        var unique = WithArt(4, (13u << 28) | (7u << 20));
        var host = new AreaUseScriptHost(session, 1, [chain, plate, monster, unique], [], null, null);
        // ARC-SCRIPT-2326 (AR1–AR3): bits 20..23 of a critter art id, bits 20..22 of a monster's, 0 for any other art type; the bare hero shows underwear (0).
        Assert.AreEqual((3, 6, 5, 0), (host.ArmorOf(Ref(chain)), host.ArmorOf(Ref(plate)), host.ArmorOf(Ref(monster)), host.ArmorOf(Ref(unique))));
        Assert.AreEqual(0, host.ArmorOf(host.Pc));
        Assert.IsNull(host.ArmorOf(new ScrObjectRef(1, "oid:nobody")), "an object the host has no row for is not known");
        Assert.IsTrue(AreaUseScriptHost.Admission.Actions.Contains(ScrCatalogue.Action.GetArmor) && AreaScriptHost.TalkAdmission.Actions.Contains(ScrCatalogue.Action.GetArmor));
    }

    // ARC-SCRIPT-2342 (AC3): the hero's worn-armour path. The hero wears the body armour the session records; the world's hero body reads its use art (null: not read) and the armour part is bits 6..9 of it.
    private static uint UseArt(int part) => (6u << 28) | ((uint)part << 6);

    // A hero with a two-item kit (a weapon and body armour), as the character sheet tests start one.
    private static GameSessionOwner HeroWithKit()
    {
        var hero = new SessionHeroDefinition(16089, CharacterRace.HalfElf, CharacterGender.Male, [8, 8, 8, 8, 8, 8, 8, 8, 0, 0], 1, 0, 0, null, null, 385,
            [new(new(SessionItemType.Weapon, 6049, 4021, 101)), new(new(SessionItemType.Armor, 8188, 4022, 102))]);
        var load = new AreaHeroLoad(AreaHeroStatus.Ready, AreaStartingHero.Find("half-elf-male")!, hero, 2352, 0, null, "synthetic");
        return AreaTransitionCoordinator.CreateSession(ConversationSessionFixture.Clock, Guid.NewGuid(), Guid.NewGuid(), AreaGridSession.Cell(AreaGridSession.Spawn), 1, load).Owner
            ?? throw new InvalidOperationException("SESSION_HERO");
    }

    [TestMethod]
    public void Get_armor_of_the_hero_is_bare_not_modelled_while_the_worn_use_art_is_not_read_and_the_part_of_that_art_otherwise()
    {
        using var bareSession = ConversationSessionFixture.CreateWithHero(1);
        var bare = new AreaUseScriptHost(bareSession, 1, [], [], null, null);
        Assert.AreEqual(0, bare.ArmorOf(bare.Pc), "bare: underwear");

        // Worn, the viewer's hero body does not read the item's use art: the answer is not modelled.
        uint? use = null;
        using var session = HeroWithKit();
        var mail = new Arcanum.Simulation.Equipment.EquipmentItem
        {
            Kind = Arcanum.Simulation.Equipment.EquipmentItemKind.Armor,
            Armor = new Arcanum.Simulation.Equipment.EquipmentArmor { Coverage = Arcanum.Simulation.Equipment.ArmorCoverage.Torso, Sizes = Arcanum.Simulation.Equipment.ArmorSizes.Medium },
        };
        Assert.AreEqual(GameSessionFailure.None, session.Equip(session.Version, "kit:2", Arcanum.Simulation.Equipment.EquipmentSlot.Armor,
            new(session.QueryHeroKit()[1].Kind, [], false, mail), (_, _) => true, _ => SessionInsertVerdict.Accepted).Failure);
        var host = new AreaUseScriptHost(session, 1, [], [], null, new AreaScriptWorld(() => null, _ => false, _ => null, () => null, BodyArmorUseArt: () => use));
        Assert.IsNull(host.ArmorOf(host.Pc), "worn armour whose use art is not read is not modelled");
        Assert.IsNull(new AreaUseScriptHost(session, 1, [], [], null, null).ArmorOf(host.Pc), "no world reads it either");

        // Part 1..8 is the art's armour part; part 9 or more gives a critter art no part (0x504060 sets 0 and the original logs it).
        foreach (var part in new[] { 1, 3, 6, 8 })
        {
            use = UseArt(part);
            Assert.AreEqual(part, host.ArmorOf(host.Pc), $"part {part}");
        }
        foreach (var part in new[] { 9, 12, 15 })
        {
            use = UseArt(part);
            Assert.AreEqual(0, host.ArmorOf(host.Pc), $"part {part} does not stand in a critter art id");
        }
        use = (7u << 28) | (5u << 6);
        Assert.AreEqual(0, host.ArmorOf(host.Pc), "a use art that is no item art has no armour part");

        // A polymorphed hero wears a monster body whose armour part is 0, whatever its worn armour's art says.
        use = UseArt(4);
        Assert.AreEqual(4, host.ArmorOf(host.Pc));
        Assert.AreEqual(GameSessionDisposition.Changed, session.WriteTraitArt(session.Version, SessionSpellCaster.Hero, new(5, 0)).Disposition);
        Assert.AreEqual(0, host.ArmorOf(host.Pc), "polymorphed hero");
        Assert.AreEqual(GameSessionDisposition.Changed, session.WriteTraitArt(session.Version, SessionSpellCaster.Hero, null).Disposition);
        Assert.AreEqual(4, host.ArmorOf(host.Pc), "the polymorph ended");
    }

    [TestMethod]
    public void Get_armor_of_a_polymorphed_npc_is_the_monster_bodys_part_0_and_the_stored_art_again_when_the_polymorph_ends()
    {
        using var session = ConversationSessionFixture.CreateWithHero(1);
        var row = Npc(1);
        row = row with { EffectiveFields = [.. row.EffectiveFields.Select(f => f.Slot == 0 ? f with { Number = (2u << 28) | (3u << 20) } : f)] };
        var host = new AreaUseScriptHost(session, 1, [row], [], null, null);
        var who = Ref(row);
        Assert.AreEqual(3, host.ArmorOf(who));
        Assert.AreEqual(GameSessionDisposition.Changed, session.WriteTraitArt(session.Version, who.Code, new(5, 0)).Disposition);
        Assert.AreEqual(0, host.ArmorOf(who), "the apply takes the worn items off and writes a monster art with part 0");
        Assert.AreEqual(GameSessionDisposition.Changed, session.WriteTraitArt(session.Version, who.Code, null).Disposition);
        Assert.AreEqual(3, host.ArmorOf(who));
    }
}
