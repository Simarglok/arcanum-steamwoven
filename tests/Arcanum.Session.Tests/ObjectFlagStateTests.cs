using Arcanum.Simulation.Combat;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

/// <summary>ARC-DEV-302 (MT13, schema 83): the flags the spell components write on critters, kept by the session. Entities and values are invented.</summary>
[TestClass]
public sealed class ObjectFlagStateTests
{
    private const string Npc = "oid:00000000-0000-0000-0000-00000000002a";

    [TestMethod]
    public void A_write_turns_one_bit_on_or_off_over_the_data_and_the_same_write_changes_nothing()
    {
        using var owner = Create();
        Assert.IsTrue(owner.QueryObjectFlags(Npc).IsEmpty);
        Changed(owner.WriteObjectFlag(owner.Version, Npc, SessionObjectFlagField.Spell, 0x1000000, true), owner, 1);
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.WriteObjectFlag(owner.Version, Npc, SessionObjectFlagField.Spell, 0x1000000, true).Disposition);
        Changed(owner.WriteObjectFlag(owner.Version, Npc, SessionObjectFlagField.Critter, 0x40, true), owner, 2);
        var written = owner.QueryObjectFlags(Npc);
        Assert.AreEqual(0x1000000 | 0x8, written.Spell(0x8), "over the data's bits");
        Assert.AreEqual(0x40, written.Critter(0));
        // Off over a data bit: the bit is cleared whatever the data says.
        Changed(owner.WriteObjectFlag(owner.Version, Npc, SessionObjectFlagField.Spell, 0x10000, false), owner, 3);
        Assert.AreEqual(0x1000000, owner.QueryObjectFlags(Npc).Spell(0x10000));
        Assert.AreEqual(new SessionObjectFlags(0x1000000, 0x10000, 0x40, 0), owner.QueryObjectFlags(Npc));
        Changed(owner.WriteObjectFlag(owner.Version, Npc, SessionObjectFlagField.Spell, 0x1000000, false), owner, 4);
        Assert.AreEqual(new SessionObjectFlags(0, 0x1010000, 0x40, 0), owner.QueryObjectFlags(Npc));
        CollectionAssert.AreEqual(new[] { Npc }, owner.QueryAllObjectFlags().Select(r => r.Entity).ToArray());
    }

    [TestMethod]
    public void A_write_that_gives_back_the_datas_value_keeps_no_change()
    {
        using var owner = Create();
        owner.WriteObjectFlag(owner.Version, Npc, SessionObjectFlagField.Critter, 0x40, true, dataHas: false);
        Assert.AreEqual(new SessionObjectFlags(0, 0, 0x40, 0), owner.QueryObjectFlags(Npc));
        Changed(owner.WriteObjectFlag(owner.Version, Npc, SessionObjectFlagField.Critter, 0x40, false, dataHas: false), owner, 2);
        Assert.AreEqual(0, owner.QueryAllObjectFlags().Count, "the entry is gone");
        // A data bit cleared and given back.
        owner.WriteObjectFlag(owner.Version, Npc, SessionObjectFlagField.Spell, 0x10000, false, dataHas: true);
        Assert.AreEqual(new SessionObjectFlags(0, 0x10000, 0, 0), owner.QueryObjectFlags(Npc));
        owner.WriteObjectFlag(owner.Version, Npc, SessionObjectFlagField.Spell, 0x10000, true, dataHas: true);
        Assert.IsTrue(owner.QueryObjectFlags(Npc).IsEmpty);
    }

    [TestMethod]
    public void Only_the_hero_and_mobile_objects_carry_written_flags()
    {
        using var owner = Create();
        foreach (var entity in new[] { "sector:1:2", "row:Mobile:3", "", "npc-1" })
            Assert.AreEqual(GameSessionFailure.InvalidChangeKey, owner.WriteObjectFlag(owner.Version, entity, SessionObjectFlagField.Spell, 0x1, true).Failure, entity);
        Assert.AreEqual(GameSessionFailure.InvalidChangeKey, owner.WriteObjectFlag(owner.Version, Npc, SessionObjectFlagField.Spell, 0, true).Failure, "a zero mask");
        Assert.AreEqual(GameSessionFailure.InvalidChangeKey, owner.WriteObjectFlag(owner.Version, Npc, (SessionObjectFlagField)7, 0x1, true).Failure);
        Assert.AreEqual(GameSessionFailure.StaleVersion, owner.WriteObjectFlag(owner.Version with { Revision = 9 }, Npc, SessionObjectFlagField.Spell, 0x1, true).Failure);
        Assert.AreEqual(0L, owner.Version.Revision);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.WriteObjectFlag(owner.Version, SessionSpellCaster.Hero, SessionObjectFlagField.Spell, 0x1, true).Disposition);
    }

    [TestMethod]
    public void A_written_paralysis_or_stone_reaches_an_NPC_life_through_its_facts()
    {
        using var owner = Create();
        var facts = new LifeFacts { HitPointPoints = 20, FatiguePoints = 20, Level = 1, Strength = 8, PoisonedStrength = [8, 8, 8], Constitution = 8, Willpower = 8 };
        var npc = SessionLifeTarget.Critter(Npc, facts, new LifeState(0, 0, 0, LifeConditions.None, 0));
        Assert.IsTrue(owner.QueryLife(npc).View!.Status.IsActive);
        owner.WriteObjectFlag(owner.Version, Npc, SessionObjectFlagField.Critter, SessionSpellCaster.ParalysedCritterFlag, true);
        Assert.IsFalse(owner.QueryLife(npc).View!.Status.IsActive, "paralysed");
        owner.WriteObjectFlag(owner.Version, Npc, SessionObjectFlagField.Critter, SessionSpellCaster.ParalysedCritterFlag, false);
        Assert.IsTrue(owner.QueryLife(npc).View!.Status.IsActive);
        owner.WriteObjectFlag(owner.Version, Npc, SessionObjectFlagField.Spell, SessionSpellCaster.StonedSpellFlag, true);
        Assert.IsFalse(owner.QueryLife(npc).View!.Status.IsActive, "stoned");
        // A data trait the session cleared is off.
        var heldByData = SessionLifeTarget.Critter(Npc, facts with { Traits = LifeTraits.Paralyzed }, new LifeState(0, 0, 0, LifeConditions.None, 0));
        owner.WriteObjectFlag(owner.Version, Npc, SessionObjectFlagField.Spell, SessionSpellCaster.StonedSpellFlag, false);
        owner.WriteObjectFlag(owner.Version, Npc, SessionObjectFlagField.Critter, SessionSpellCaster.ParalysedCritterFlag, false);
        Assert.IsTrue(owner.QueryLife(heldByData).View!.Status.IsActive);
    }

    [TestMethod]
    public void The_written_flags_survive_the_snapshot_and_a_damaged_list_is_refused()
    {
        using var owner = Create();
        owner.WriteObjectFlag(owner.Version, Npc, SessionObjectFlagField.Critter, 0x40, true);
        owner.WriteObjectFlag(owner.Version, SessionSpellCaster.Hero, SessionObjectFlagField.Spell, 0x1000000, true);
        owner.WriteObjectFlag(owner.Version, SessionSpellCaster.Hero, SessionObjectFlagField.Spell, 0x10000, false);
        owner.WriteObjectFlag(owner.Version, SessionSpellCaster.Hero, SessionObjectFlagField.Object, 0x200, true);
        owner.WriteObjectFlag(owner.Version, SessionSpellCaster.Hero, SessionObjectFlagField.Object, 0x80, false);
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        var list = "\"objectFlags\":[{\"entity\":\"" + Npc + "\",\"spellSet\":0,\"spellCleared\":0,\"critterSet\":64,\"critterCleared\":0,\"objectSet\":0,\"objectCleared\":0,\"critter2Set\":0,\"critter2Cleared\":0,\"blitSet\":0,\"blitCleared\":0},"
            + "{\"entity\":\"pc\",\"spellSet\":16777216,\"spellCleared\":65536,\"critterSet\":0,\"critterCleared\":0,\"objectSet\":512,\"objectCleared\":128,\"critter2Set\":0,\"critter2Cleared\":0,\"blitSet\":0,\"blitCleared\":0}]";
        StringAssert.Contains(text, list);
        using var restored = Restore(Decode(text));
        Assert.AreEqual(new SessionObjectFlags(0x1000000, 0x10000, 0, 0, 0x200, 0x80), restored.QueryObjectFlags(SessionSpellCaster.Hero));
        Assert.AreEqual(new SessionObjectFlags(0, 0, 0x40, 0), restored.QueryObjectFlags(Npc));
        Assert.AreEqual(text, GameSessionSnapshotCodec.Encode(restored.Capture()), "canonical");

        void Fails(string damaged, SnapshotDecodeFailure failure, string path)
        {
            var result = GameSessionSnapshotCodec.TryDecode(damaged);
            Assert.AreEqual((failure, path), (result.Failure, result.Path));
        }
        void Refused(string damaged) => Assert.AreEqual(GameSessionFailure.InvalidSnapshot, GameSessionOwner.Restore(Decode(damaged), RestoreOptions()).Failure);
        Fails(text.Replace("\"entity\":\"pc\"", "\"entity\":\"" + Npc + "\""), SnapshotDecodeFailure.NotCanonical, "$.spells.objectFlags[1].entity");
        Fails(text.Replace("\"critterSet\":64", "\"critterSet\":\"64\""), SnapshotDecodeFailure.WrongType, "$.spells.objectFlags[0].critterSet");
        Fails(text.Replace(",\"objectSet\":512,\"objectCleared\":128,\"critter2Set\":0,\"critter2Cleared\":0,\"blitSet\":0,\"blitCleared\":0}]", "}]"), SnapshotDecodeFailure.MissingField, "$.spells.objectFlags[1].objectSet");
        Fails(text.Replace("\"objectCleared\":128", "\"objectCleared\":\"128\""), SnapshotDecodeFailure.WrongType, "$.spells.objectFlags[1].objectCleared");
        Refused(text.Replace("\"entity\":\"pc\"", "\"entity\":\"sector:1:2\""));
        Refused(text.Replace("\"critterSet\":64,\"critterCleared\":0", "\"critterSet\":64,\"critterCleared\":64"));
        Refused(text.Replace("\"critterSet\":64", "\"critterSet\":0"));
        Refused(text.Replace("\"objectSet\":512,\"objectCleared\":128", "\"objectSet\":512,\"objectCleared\":512"));
    }

    // ARC-DEV-358 (MA-1, schema 88): the object flags (obj_f_flags) are a third field of the written flags.
    [TestMethod]
    public void The_object_flags_are_a_third_field_over_the_datas_value()
    {
        using var owner = Create();
        Changed(owner.WriteObjectFlag(owner.Version, Npc, SessionObjectFlagField.Object, 0x200, true), owner, 1);
        Changed(owner.WriteObjectFlag(owner.Version, Npc, SessionObjectFlagField.Object, 0x80, false), owner, 2);
        var written = owner.QueryObjectFlags(Npc);
        Assert.AreEqual(new SessionObjectFlags(0, 0, 0, 0, 0x200, 0x80), written);
        Assert.AreEqual(0x200 | 0x4, written.Object(0x4 | 0x80), "over the data's bits: set, cleared, the rest kept");
        Assert.AreEqual(0x1234, written.Spell(0x1234), "the other fields are untouched");
        // Given back to the data's value: no change is kept.
        owner.WriteObjectFlag(owner.Version, Npc, SessionObjectFlagField.Object, 0x200, false, dataHas: false);
        owner.WriteObjectFlag(owner.Version, Npc, SessionObjectFlagField.Object, 0x80, true, dataHas: true);
        Assert.IsTrue(owner.QueryObjectFlags(Npc).IsEmpty);
    }

    // ARC-MAGIC-047 (schema 90): the second critter flags and the blit flags are a fourth and fifth field of the written flags; an assignment (MG10) is a word whose every bit is set or cleared.
    [TestMethod]
    public void The_critter_flags_2_and_the_blit_flags_are_written_assigned_saved_and_restored()
    {
        using var owner = Create();
        Changed(owner.WriteObjectFlag(owner.Version, Npc, SessionObjectFlagField.Critter2, 0x400000, true), owner, 1);
        Changed(owner.AssignObjectFlag(owner.Version, SessionSpellCaster.Hero, SessionObjectFlagField.Critter2, 2), owner, 2);
        Changed(owner.AssignObjectFlag(owner.Version, SessionSpellCaster.Hero, SessionObjectFlagField.Blit, 0x10), owner, 3);
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.AssignObjectFlag(owner.Version, SessionSpellCaster.Hero, SessionObjectFlagField.Blit, 0x10).Disposition);
        var hero = owner.QueryObjectFlags(SessionSpellCaster.Hero);
        Assert.AreEqual(2, hero.Critter2(0x400080), "the assignment wipes the data's bits");
        Assert.AreEqual(0x10, hero.Blit(0xFF), "the blit flags word is exactly the value");
        Assert.AreEqual(0x400000 | 0x80, owner.QueryObjectFlags(Npc).Critter2(0x80));
        Assert.AreEqual(GameSessionFailure.InvalidChangeKey, owner.AssignObjectFlag(owner.Version, "sector:1:2", SessionObjectFlagField.Blit, 1).Failure);
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        StringAssert.Contains(text, "\"critter2Set\":2,\"critter2Cleared\":-3,\"blitSet\":16,\"blitCleared\":-17");
        using var restored = Restore(Decode(text));
        Assert.AreEqual(hero, restored.QueryObjectFlags(SessionSpellCaster.Hero));
        Assert.AreEqual(text, GameSessionSnapshotCodec.Encode(restored.Capture()), "canonical");
    }

    [TestMethod]
    public void The_eighty_nine_to_ninety_step_gives_every_row_the_zero_critter_flags_2_and_blit_flags_and_refuses_a_row_that_has_them()
    {
        using var owner = Create();
        owner.WriteObjectFlag(owner.Version, Npc, SessionObjectFlagField.Critter, 0x40, true);
        var current = GameSessionSnapshotCodec.Encode(owner.Capture());
        var older = SnapshotUpgradeTests.Schema89(current);
        StringAssert.Contains(older, "\"schemaVersion\":89,\"world\"");
        Assert.IsFalse(older.Contains("critter2Set", StringComparison.Ordinal));
        var upgraded = Decode(older);
        Assert.AreEqual(GameSessionSnapshot.CurrentSchemaVersion, upgraded.SchemaVersion);
        Assert.AreEqual(new SessionObjectFlags(0, 0, 0x40, 0), upgraded.Spells.ObjectFlags.Single().Flags);
        Assert.AreEqual(current, GameSessionSnapshotCodec.Encode(upgraded));
        var damaged = GameSessionSnapshotCodec.TryDecode(older.Replace("\"objectCleared\":0}]", "\"objectCleared\":0,\"blitSet\":0}]"));
        Assert.AreEqual((SnapshotDecodeFailure.UnknownField, "$.spells.objectFlags[0].blitSet"), (damaged.Failure, damaged.Path));
    }

    [TestMethod]
    public void The_eighty_seven_to_eighty_eight_step_gives_every_row_the_zero_object_flags_and_refuses_a_row_that_has_them()
    {
        using var owner = Create();
        owner.WriteObjectFlag(owner.Version, Npc, SessionObjectFlagField.Critter, 0x40, true);
        var current = GameSessionSnapshotCodec.Encode(owner.Capture());
        var older = SnapshotUpgradeTests.Schema87(current);
        StringAssert.Contains(older, "\"schemaVersion\":87,\"world\"");
        StringAssert.Contains(older, "\"critterCleared\":0}]");
        Assert.IsFalse(older.Contains("objectSet", StringComparison.Ordinal));
        var upgraded = Decode(older);
        Assert.AreEqual(GameSessionSnapshot.CurrentSchemaVersion, upgraded.SchemaVersion);
        Assert.AreEqual(new SessionObjectFlags(0, 0, 0x40, 0), upgraded.Spells.ObjectFlags.Single().Flags);
        Assert.AreEqual(current, GameSessionSnapshotCodec.Encode(upgraded));
        var damaged = GameSessionSnapshotCodec.TryDecode(older.Replace("\"critterCleared\":0}]", "\"critterCleared\":0,\"objectSet\":0}]"));
        Assert.AreEqual((SnapshotDecodeFailure.UnknownField, "$.spells.objectFlags[0].objectSet"), (damaged.Failure, damaged.Path));
    }

    [TestMethod]
    public void The_eighty_two_to_eighty_three_step_adds_the_empty_list_and_refuses_one_that_is_already_there()
    {
        var older = SnapshotUpgradeTests.Schema82(GoldenJson);
        StringAssert.Contains(older, "\"schemaVersion\":82,\"world\"");
        Assert.IsFalse(older.Contains("objectFlags", StringComparison.Ordinal));
        var upgraded = Decode(older);
        Assert.AreEqual(GameSessionSnapshot.CurrentSchemaVersion, upgraded.SchemaVersion);
        Assert.AreEqual(0, upgraded.Spells.ObjectFlags.Count, "no older build wrote a flag");
        Assert.AreEqual(GoldenJson, GameSessionSnapshotCodec.Encode(upgraded));
        var damaged = older.Replace(NoSpells82, NoSpells);
        var result = GameSessionSnapshotCodec.TryDecode(damaged);
        Assert.AreEqual((SnapshotDecodeFailure.UnknownField, "$.spells.objectFlags"), (result.Failure, result.Path));
    }
}
