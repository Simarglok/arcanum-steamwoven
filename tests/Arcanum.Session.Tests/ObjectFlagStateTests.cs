using Arcanum.Simulation.Combat;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

/// <summary>ARC-DEV-302 (MT13, schema 82): the flags the spell components write on critters, kept by the session. Entities and values are invented.</summary>
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
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        var list = "\"objectFlags\":[{\"entity\":\"" + Npc + "\",\"spellSet\":0,\"spellCleared\":0,\"critterSet\":64,\"critterCleared\":0},"
            + "{\"entity\":\"pc\",\"spellSet\":16777216,\"spellCleared\":65536,\"critterSet\":0,\"critterCleared\":0}]";
        StringAssert.Contains(text, list);
        using var restored = Restore(Decode(text));
        Assert.AreEqual(new SessionObjectFlags(0x1000000, 0x10000, 0, 0), restored.QueryObjectFlags(SessionSpellCaster.Hero));
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
        Fails(text.Replace(",\"critterCleared\":0}]", "}]"), SnapshotDecodeFailure.MissingField, "$.spells.objectFlags[1].critterCleared");
        Refused(text.Replace("\"entity\":\"pc\"", "\"entity\":\"sector:1:2\""));
        Refused(text.Replace("\"critterSet\":64,\"critterCleared\":0", "\"critterSet\":64,\"critterCleared\":64"));
        Refused(text.Replace("\"critterSet\":64", "\"critterSet\":0"));
    }

    [TestMethod]
    public void The_eighty_one_to_eighty_two_step_adds_the_empty_list_and_refuses_one_that_is_already_there()
    {
        var older = SnapshotUpgradeTests.Schema81(GoldenJson);
        StringAssert.Contains(older, "\"schemaVersion\":81,\"world\"");
        Assert.IsFalse(older.Contains("objectFlags", StringComparison.Ordinal));
        var upgraded = Decode(older);
        Assert.AreEqual(GameSessionSnapshot.CurrentSchemaVersion, upgraded.SchemaVersion);
        Assert.AreEqual(0, upgraded.Spells.ObjectFlags.Count, "no older build wrote a flag");
        Assert.AreEqual(GoldenJson, GameSessionSnapshotCodec.Encode(upgraded));
        var damaged = older.Replace(NoSpells81, NoSpells);
        var result = GameSessionSnapshotCodec.TryDecode(damaged);
        Assert.AreEqual((SnapshotDecodeFailure.UnknownField, "$.spells.objectFlags"), (result.Failure, result.Path));
    }
}
