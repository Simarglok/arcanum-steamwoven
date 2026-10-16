using Arcanum.Data.Area;
using Arcanum.Data.Characters;
using Arcanum.Data.Combat;
using Arcanum.Data.Objects;
using Arcanum.Data.StaticObjects;
using Arcanum.Data.Tests.Characters;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Data.Tests.Combat;

// ARC-DATA-021: combat fields of synthetic v119 prototypes and Area objects. Every number is invented; each slot gets a
// distinct value so a wrong slot shows up as a wrong value.
[TestClass]
public sealed class CombatFieldReaderTests
{
    private const int Weapon = 5, Ammo = 6, Armor = 7, Generic = 14, Pc = 15, Npc = 16, Scenery = 3;
    private const string Source = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    private static Dictionary<int, int> Map(params int[] values) => values.Select((v, i) => (v, i)).Where(p => p.v != 0).ToDictionary(p => p.i, p => p.v);

    // Common block: flags 18, spell flags 19, name 21, description 22, AC 25, HP 26..28, material 29, resistances 30.
    private static Dictionary<int, object> Common(Dictionary<int, object>? extra = null)
    {
        var values = new Dictionary<int, object>
        {
            [18] = 0x1000, [19] = 0x40, [21] = 2000, [22] = 6178, [25] = 7, [26] = 20, [27] = -3, [28] = 4, [29] = 5,
            [30] = Map(10, 20, 30, 40, 50),
        };
        foreach (var (slot, value) in extra ?? []) values[slot] = value;
        return values;
    }

    // Item block 36..58: flags 36, weight 38, worth 40, mana 41, location 43, complexity 45, discipline 46, spells 49..53,
    // spell mana 54, AI action 55.
    private static Dictionary<int, object> Item(int complexity = -5, Dictionary<int, object>? extra = null)
    {
        var values = Common(new()
        {
            [36] = 0x2, [38] = 20, [40] = 9, [41] = 3, [43] = 1004, [45] = complexity, [46] = 6,
            [49] = 10000, [50] = 31, [51] = 10000, [52] = 10000, [53] = 10000, [54] = 12, [55] = 4,
        });
        foreach (var (slot, value) in extra ?? []) values[slot] = value;
        return values;
    }

    private static Dictionary<int, object> WeaponValues(int flags = 0x1A, int complexity = -5, int range = 1, int ammo = CombatFieldReader.NoAmmo) => Item(complexity, new()
    {
        [59] = flags, [61] = 5, [62] = 2, [63] = Map(2, 0, 1, 0, 3), [64] = Map(6, 0, 4, 0, 9), [65] = Map(1, 0, 0, 2, 0), [66] = 6, [67] = -1,
        [68] = range, [69] = 3, [70] = 4, [71] = -2, [72] = ammo, [73] = 1, [76] = 2, [77] = 15, [78] = 25, [79] = 3, [80] = 35, [81] = 45,
    });

    private static Dictionary<int, object> CritterValues(Dictionary<int, object>? extra = null)
    {
        var stats = new int[28]; stats[0] = 11; stats[1] = 13; stats[2] = 9; stats[5] = 11; stats[17] = 3; stats[27] = 2;
        var spellTech = new int[25]; spellTech[3] = 2; spellTech[16] = -1; spellTech[17 + 4] = 1;
        var values = Common(new()
        {
            [36] = 0x2008, [37] = 0x10, [38] = Map(stats), [39] = Map(0, 2 | (1 << 6), 3 | (3 << 6)), [40] = Map(0, 5), [41] = Map(spellTech),
            [42] = 12, [43] = 2, [44] = 1, [45] = 1, [57] = 84, [63] = 91,
        });
        foreach (var (slot, value) in extra ?? []) values[slot] = value;
        return values;
    }

    private static Dictionary<int, object> NpcValues() => CritterValues(new()
    {
        [69] = 0x1102, [71] = 19, [74] = 40, [75] = 7, [81] = 9, [84] = 50, [85] = 3, [92] = Map(3, 7, 0, 0, 0, 0, 1, 2, 0, 0),
    });

    private static Func<int, PrototypeFile> Files(params (int Number, int Type, Dictionary<int, object> Values)[] prototypes)
    {
        var files = prototypes.ToDictionary(p => p.Number, p => new PrototypeFile(PrototypeFileStatus.Found, PremadeHeroTests.Prototype(p.Number, p.Type, p.Values)));
        return number => files.TryGetValue(number, out var file) ? file : new(PrototypeFileStatus.Missing, null);
    }

    private static CombatFields Read(int number, int type, Dictionary<int, object> values)
    {
        var read = CombatFieldReader.ReadPrototype(Files((number, type, values)), number);
        Assert.AreEqual(CombatFieldFailure.None, read.Failure, $"refused at {read.Detail}");
        return read.Fields!;
    }

    [TestMethod]
    public void AWeaponPrototypeReadsEveryStoredCombatField()
    {
        var fields = Read(6218, Weapon, WeaponValues());
        Assert.AreEqual(CombatFieldOrigin.Prototype, fields.Origin);
        var o = fields.Object;
        Assert.AreEqual((Weapon, 6218, 0x1000u, 0x40u, 2000, 6178, 7, 20, -3, 4, 5), (o.Type, o.Prototype, o.Flags, o.SpellFlags, o.Name, o.Description,
            o.ArmorClass, o.HitPoints, o.HitPointAdjustment, o.HitPointDamage, o.Material));
        CollectionAssert.AreEqual(new[] { 10, 20, 30, 40, 50 }, o.Resistances.ToArray());
        var i = fields.Item!;
        Assert.AreEqual((0x2u, 20, 9, 3, -5, 6, 1004, 12, 4), (i.Flags, i.Weight, i.Worth, i.ManaStore, i.MagickTechComplexity, i.Discipline,
            i.InventoryLocation, i.SpellManaStore, i.AiAction));
        CollectionAssert.AreEqual(new[] { 10000, 31, 10000, 10000, 10000 }, i.Spells.ToArray());
        var w = fields.Weapon!;
        Assert.AreEqual(CombatWeaponFlags.Silent | CombatWeaponFlags.HandCountFixed | CombatWeaponFlags.Throwable, w.Flags);
        Assert.AreEqual(CombatWeaponSkill.Melee, w.Skill);
        Assert.AreEqual((5, 2, 6, -1, 1, 3, 4, -2, CombatFieldReader.NoAmmo, 1), (w.BonusToHit, w.MagicHitAdjustment, w.SpeedFactor, w.MagicSpeedAdjustment,
            w.Range, w.MagicRangeAdjustment, w.MinimumStrength, w.MagicMinimumStrengthAdjustment, w.AmmoType, w.AmmoConsumption));
        Assert.AreEqual((2, 15, 25, 3, 35, 45), (w.CriticalHitChart, w.MagicCriticalHitChance, w.MagicCriticalHitEffect, w.CriticalMissChart,
            w.MagicCriticalMissChance, w.MagicCriticalMissEffect));
        // Damage by damage type: normal, poison, electrical, fire, fatigue.
        CollectionAssert.AreEqual(new CombatDamageRange[] { new(2, 6), new(0, 0), new(1, 4), new(0, 0), new(3, 9) }, w.Damage.ToArray());
        CollectionAssert.AreEqual(new[] { 1, 0, 0, 2, 0 }, w.MagicDamageAdjustment.ToArray());
        Assert.IsNull(fields.Ammo); Assert.IsNull(fields.Armor); Assert.IsNull(fields.Critter); Assert.IsNull(fields.Npc);
    }

    [TestMethod]
    public void AmmunitionArmourAndOtherItemsReadTheirOwnBlocks()
    {
        var ammo = Read(7059, Ammo, Item(0, new() { [59] = 0x1, [60] = 15, [61] = (int)CombatAmmoType.Bullet }));
        Assert.AreEqual(new CombatAmmoFields(0x1, 15, 1), ammo.Ammo);
        Assert.IsNull(ammo.Weapon); Assert.IsNotNull(ammo.Item);

        var armor = Read(8114, Armor, Item(0, new()
        {
            [59] = 0x12, [61] = 3, [62] = 1, [63] = Map(3, -5, 0, 0, 7), [64] = Map(0, 0, 2, 0, 0), [65] = -10, [66] = 4, [67] = 6,
        }));
        var a = armor.Armor!;
        Assert.AreEqual(CombatArmorFlags.SizeMedium | CombatArmorFlags.FemaleOnly, a.Flags);
        Assert.AreEqual((3, 1, -10, 4, 6), (a.ArmorClassAdjustment, a.MagicArmorClassAdjustment, a.SilentMoveAdjustment, a.MagicSilentMoveAdjustment, a.UnarmedBonusDamage));
        // Resistance order: normal, fire, electrical, poison, magic.
        CollectionAssert.AreEqual(new[] { 3, -5, 0, 0, 7 }, a.ResistanceAdjustment.ToArray());
        CollectionAssert.AreEqual(new[] { 0, 0, 2, 0, 0 }, a.MagicResistanceAdjustment.ToArray());

        var generic = Read(15100, Generic, Item(0, new() { [59] = 0x8 }));
        Assert.IsNotNull(generic.Item);
        Assert.IsTrue(generic.Weapon is null && generic.Ammo is null && generic.Armor is null && generic.Critter is null);
    }

    [TestMethod]
    public void CrittersReadStoredStatsSkillsAndNpcsTheirMonsterDamage()
    {
        var npc = Read(28359, Npc, NpcValues());
        var c = npc.Critter!;
        Assert.AreEqual((0x2008u, 0x10u, 12, 2, 1, 1, 84, 91), (c.Flags, c.Flags2, c.FatiguePoints, c.FatigueAdjustment, c.FatigueDamage,
            c.CriticalHitChart, c.InventorySource, c.AutoLevelScheme));
        Assert.AreEqual(28, c.BaseStats.Count);
        Assert.AreEqual((11, 13, 9, 11, 3, 2, 0), (c.BaseStats[0], c.BaseStats[1], c.BaseStats[2], c.BaseStats[5], c.BaseStats[17], c.BaseStats[27], c.BaseStats[6]));
        Assert.AreEqual((12, 4, 25), (c.BasicSkills.Count, c.TechSkills.Count, c.SpellTech.Count));
        Assert.AreEqual((2, 1), (CombatFieldReader.SkillPoints(c.BasicSkills[1]), CombatFieldReader.SkillTraining(c.BasicSkills[1])));
        Assert.AreEqual((3, 3), (CombatFieldReader.SkillPoints(c.BasicSkills[2]), CombatFieldReader.SkillTraining(c.BasicSkills[2])));
        Assert.AreEqual(5, c.TechSkills[1]);
        // The mastery college is stored as −1 in an unsigned array.
        Assert.AreEqual((2, -1, 1), (c.SpellTech[3], c.SpellTech[16], c.SpellTech[21]));
        var n = npc.Npc!;
        Assert.AreEqual((0x1102u, 19, 40, 7, 9, 50, 3), (n.Flags, n.AiData, n.ExperienceWorth, n.ExperiencePool, n.Faction, n.ReactionBase, n.SocialClass));
        CollectionAssert.AreEqual(new CombatDamageRange[] { new(3, 7), new(0, 0), new(0, 0), new(1, 2), new(0, 0) }, n.Damage.ToArray());
        Assert.IsNull(npc.Item);

        var pc = Read(16089, Pc, CritterValues());
        Assert.IsNotNull(pc.Critter); Assert.IsNull(pc.Npc);
    }

    [TestMethod]
    public void TheWeaponSkillFollowsTheSourceRule()
    {
        Assert.AreEqual(CombatWeaponSkill.Throwing, CombatFieldReader.WeaponSkill(CombatWeaponFlags.Boomerangs, -5, 5, (int)CombatAmmoType.Bullet));
        Assert.AreEqual(CombatWeaponSkill.Firearms, CombatFieldReader.WeaponSkill(CombatWeaponFlags.None, -1, 3, (int)CombatAmmoType.Bullet));
        Assert.AreEqual(CombatWeaponSkill.Firearms, CombatFieldReader.WeaponSkill(CombatWeaponFlags.None, -1, 3, (int)CombatAmmoType.PowerCell));
        Assert.AreEqual(CombatWeaponSkill.Firearms, CombatFieldReader.WeaponSkill(CombatWeaponFlags.None, -1, 3, (int)CombatAmmoType.Fuel));
        Assert.AreEqual(CombatWeaponSkill.Melee, CombatFieldReader.WeaponSkill(CombatWeaponFlags.None, -1, 2, (int)CombatAmmoType.Bullet), "range below 3");
        Assert.AreEqual(CombatWeaponSkill.Melee, CombatFieldReader.WeaponSkill(CombatWeaponFlags.None, 0, 5, (int)CombatAmmoType.Bullet), "not technological");
        Assert.AreEqual(CombatWeaponSkill.Bow, CombatFieldReader.WeaponSkill(CombatWeaponFlags.None, -5, 8, (int)CombatAmmoType.Arrow), "an arrow weapon is a bow even when technological");
        Assert.AreEqual(CombatWeaponSkill.Melee, CombatFieldReader.WeaponSkill(CombatWeaponFlags.Throwable, 0, 1, CombatFieldReader.NoAmmo), "throwable alone stays melee");
        Assert.AreEqual(CombatWeaponSkill.Firearms, Read(6122, Weapon, WeaponValues(flags: 0, complexity: -5, range: 5, ammo: 1)).Weapon!.Skill);
    }

    [TestMethod]
    public void SparseArraysReadLikeTheSourceAndRefuseKeysOutOfRange()
    {
        // A null or empty array, or a missing key, is 0.
        var values = WeaponValues(); values.Remove(63); values.Remove(65);
        var weapon = Read(6218, Weapon, values).Weapon!;
        CollectionAssert.AreEqual(new[] { 0, 0, 0, 0, 0 }, weapon.Damage.Select(d => d.Minimum).ToArray());
        CollectionAssert.AreEqual(new[] { 0, 0, 0, 0, 0 }, weapon.MagicDamageAdjustment.ToArray());
        CollectionAssert.AreEqual(new[] { 6, 0, 4, 0, 9 }, weapon.Damage.Select(d => d.Maximum).ToArray());

        var wide = WeaponValues(); wide[64] = new Dictionary<int, int> { [5] = 1 };
        var read = CombatFieldReader.ReadPrototype(Files((6218, Weapon, wide)), 6218);
        Assert.AreEqual((CombatFieldFailure.ArrayMalformed, 64), (read.Failure, read.Detail!.Value));
        Assert.IsNull(read.Fields);

        var stats = CritterValues(); stats[38] = new Dictionary<int, int> { [28] = 1 };
        Assert.AreEqual((CombatFieldFailure.ArrayMalformed, 38), Refusal(CombatFieldReader.ReadPrototype(Files((16089, Pc, stats)), 16089)));
    }

    [TestMethod]
    public void PrototypeFilesAreRefusedTyped()
    {
        Assert.AreEqual((CombatFieldFailure.PrototypeMissing, 6218), Refusal(CombatFieldReader.ReadPrototype(Files(), 6218)));
        Assert.AreEqual((CombatFieldFailure.PrototypeMissing, -1), Refusal(CombatFieldReader.ReadPrototype(Files(), -1)));
        Assert.AreEqual((CombatFieldFailure.PrototypeAmbiguous, 6218),
            Refusal(CombatFieldReader.ReadPrototype(_ => new(PrototypeFileStatus.Ambiguous, null), 6218)));
        Assert.AreEqual((CombatFieldFailure.PrototypeInvalid, 6218),
            Refusal(CombatFieldReader.ReadPrototype(_ => new(PrototypeFileStatus.Found, [1, 2, 3]), 6218)));
        // A file whose own prototype number is not the one asked for.
        var other = Files((6219, Weapon, WeaponValues()));
        Assert.AreEqual((CombatFieldFailure.PrototypeInvalid, 6218), Refusal(CombatFieldReader.ReadPrototype(n => other(n + 1), 6218)));
        // Trailing bytes after the record.
        var bytes = PremadeHeroTests.Prototype(6218, Weapon, WeaponValues()).Append((byte)0).ToArray();
        Assert.AreEqual((CombatFieldFailure.PrototypeInvalid, 6218), Refusal(CombatFieldReader.ReadPrototype(_ => new(PrototypeFileStatus.Found, bytes), 6218)));
        Assert.AreEqual((CombatFieldFailure.TypeUnsupported, Scenery), Refusal(CombatFieldReader.ReadPrototype(Files((4047, Scenery, Common())), 4047)));
        Assert.ThrowsExactly<ArgumentNullException>(() => CombatFieldReader.ReadPrototype(null!, 1));
    }

    // ---------------------------------------------------------------- Area objects

    private static readonly StaticObjectLoaderOptions Limits = new() { MaxPrototypeBytes = 65536, MaxArrayElements = 4096, MaxStringBytes = 4096 };

    // Effective fields of an Area object: the synthetic prototype's fields, each replaced by an instance value where given.
    private static AreaObjectRecord Row(int index, int prototype, int type, Dictionary<int, object> values, params AreaFieldValue[] instance)
    {
        var record = ObjectWireReader.Read(PremadeHeroTests.Prototype(prototype, type, values), Limits, prototype: true);
        var effective = record.Fields.Select(p => instance.FirstOrDefault(f => f.Slot == p.Key) ?? new AreaFieldValue(p.Key,
            (AreaPresence)((int)p.Value.Presence + 1), AreaValueOrigin.Prototype, p.Value.Number,
            p.Value.Elements.Select(e => new AreaSparseElement(e.Key, e.Words)).ToArray(), p.Value.Fingerprint)).ToArray();
        return new(new(Source, 7, AreaSourceKind.Mobile, null, index, index * 1000L, Source, 300), 119, type, prototype, Source, Source, Source, Source,
            AreaScope.Core, null, 10, 20, AreaDisposition.BodyEligibleConditionally, instance, effective, true, true, true);
    }

    private static AreaFieldValue Instance(int slot, long value) => new(slot, AreaPresence.Value, AreaValueOrigin.Instance, value, [], null);

    private static AreaSnapshot Snapshot(IEnumerable<AreaObjectRecord> objects, bool strict = true)
    {
        var plan = new AreaSourcePlan(5051, 7, new AreaSectorCoordinate(1, 1), 1, "maps/test", [new AreaInputIdentity(new(AreaSourceRole.Module), 1, Source)], []);
        var snapshot = new AreaSnapshot(new object(), 1, plan, [], [], objects, [], new AreaReconciliation(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0), []);
        if (strict) snapshot.MarkStrictlyPublished();
        return snapshot;
    }

    [TestMethod]
    public void AnAreaObjectReadsItsInstanceValuesOverItsPrototype()
    {
        var npc = Row(4, 28359, Npc, NpcValues(), Instance(27, -6), Instance(71, 14),
            new AreaFieldValue(92, AreaPresence.Array, AreaValueOrigin.Instance, null, [new(0, [5]), new(1, [10])], null));
        var read = CombatFieldReader.Read(npc);
        Assert.AreEqual(CombatFieldFailure.None, read.Failure);
        Assert.AreEqual(CombatFieldOrigin.AreaObject, read.Fields!.Origin);
        Assert.AreEqual((28359, -6, 14, 9), (read.Fields.Object.Prototype, read.Fields.Object.HitPointAdjustment, read.Fields.Npc!.AiData, read.Fields.Npc.Faction));
        // An instance array replaces the prototype's whole array.
        CollectionAssert.AreEqual(new CombatDamageRange[] { new(5, 10), new(0, 0), new(0, 0), new(0, 0), new(0, 0) }, read.Fields.Npc.Damage.ToArray());
        Assert.AreEqual(11, read.Fields.Critter!.BaseStats[0]);

        var weapon = Row(5, 6218, Weapon, WeaponValues(), Instance(43, 1004), Instance(70, 9));
        Assert.AreEqual((1004, 9), (CombatFieldReader.Read(weapon).Fields!.Item!.InventoryLocation, CombatFieldReader.Read(weapon).Fields!.Weapon!.MinimumStrength));
    }

    [TestMethod]
    public void AnAreaObjectWithoutAMandatoryFieldIsRefusedAtThatSlot()
    {
        var row = Row(4, 28359, Npc, NpcValues());
        var missing = row with { EffectiveFields = row.EffectiveFields.Where(f => f.Slot != 81).ToArray() };
        Assert.AreEqual((CombatFieldFailure.FieldUnresolved, 81), Refusal(CombatFieldReader.Read(missing)));
        var absent = row with { EffectiveFields = row.EffectiveFields.Select(f => f.Slot == 71 ? f with { Presence = AreaPresence.Absent } : f).ToArray() };
        Assert.AreEqual((CombatFieldFailure.FieldUnresolved, 71), Refusal(CombatFieldReader.Read(absent)));
        var notArray = row with { EffectiveFields = row.EffectiveFields.Select(f => f.Slot == 38 ? Instance(38, 1) : f).ToArray() };
        Assert.AreEqual((CombatFieldFailure.FieldUnresolved, 38), Refusal(CombatFieldReader.Read(notArray)));
        var wideElement = row with { EffectiveFields = row.EffectiveFields.Select(f => f.Slot == 30
            ? new AreaFieldValue(30, AreaPresence.Array, AreaValueOrigin.Instance, null, [new(0, [1, 2])], null) : f).ToArray() };
        Assert.AreEqual((CombatFieldFailure.ArrayMalformed, 30), Refusal(CombatFieldReader.Read(wideElement)));
    }

    [TestMethod]
    public void AnAreaReadListsMobileItemsAndCrittersOnlyFromAStrictSnapshot()
    {
        // A sector-file (static) armour and a scenery object are not listed.
        var armor = Row(7, 8114, Armor, Item(0));
        var objects = new[]
        {
            Row(9, 6218, Weapon, WeaponValues()), Row(2, 28359, Npc, NpcValues()), Row(5, 4047, Scenery, Common()),
            armor with { Key = armor.Key with { Kind = AreaSourceKind.Static, Sector = 1 } },
        };
        var read = CombatFieldReader.ReadArea(Snapshot(objects));
        Assert.AreEqual(CombatFieldFailure.None, read.Failure);
        CollectionAssert.AreEqual(new[] { (2, 28359), (9, 6218) }, read.Objects!.Select(o => (o.SourceIndex, o.Prototype)).ToArray());
        Assert.AreEqual(CombatFieldFailure.SnapshotNotStrict, CombatFieldReader.ReadArea(Snapshot(objects, strict: false)).Failure);

        var broken = objects[1] with { EffectiveFields = objects[1].EffectiveFields.Where(f => f.Slot != 92).ToArray() };
        var refused = CombatFieldReader.ReadArea(Snapshot([objects[0], broken]));
        Assert.AreEqual((CombatFieldFailure.FieldUnresolved, 2, 92), (refused.Failure, refused.SourceIndex!.Value, refused.Detail!.Value));
        Assert.IsNull(refused.Objects);
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        Assert.ThrowsExactly<OperationCanceledException>(() => CombatFieldReader.ReadArea(Snapshot(objects), cancelled.Token));
    }

    private static (CombatFieldFailure, int) Refusal(CombatFieldRead read)
    {
        Assert.IsNull(read.Fields);
        return (read.Failure, read.Detail!.Value);
    }
}
