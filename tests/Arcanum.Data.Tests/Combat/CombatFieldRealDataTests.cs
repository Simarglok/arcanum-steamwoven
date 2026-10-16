using System.Text.Json;
using Arcanum.Data.Area;
using Arcanum.Data.Characters;
using Arcanum.Data.Combat;
using Arcanum.Data.Inventory;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Data.Tests.Combat;

/// <summary>
/// Opt-in check of ARC-DATA-021 on the installed original (inputs as in the inventory real-data tests: ARC_AREA_MODULE,
/// ARC_AREA_RESOURCE1/2, ARC_AREA_MOBILE, ARC_AREA_PROTOTYPES, ARC_CAVE_MOBILE). First observation 2026-10-16; only numbers
/// are compared and printed, never original text.
/// </summary>
[TestClass]
public sealed class CombatFieldRealDataTests
{
    private const int WieldedWeapon = 1004, WornArmor = 1006;

    // Creature prototypes of the Crash Site exterior: core count, context count, AI row, monster normal damage, ST, DX, CN, PE,
    // level, resistances (normal, fire, electrical, poison, magic).
    private static readonly (int Prototype, int Core, int Context, int Ai, int Min, int Max, int[] Stats, int[] Resistances)[] ExteriorCreatures =
    [
        (28359, 10, 3, 19, 1, 3, [2, 4, 3, 9, 1], [0, 0, 0, 0, 0]),
        (27356, 3, 0, 14, 3, 8, [3, 8, 4, 10, 3], [10, 10, 10, 10, 10]),
        (28447, 3, 0, 19, 1, 4, [5, 4, 3, 6, 2], [20, 20, 20, 20, 0]),
        (27359, 1, 0, 15, 5, 10, [2, 5, 4, 8, 12], [5, 5, 5, 5, 30]),
        (28339, 0, 5, 19, 3, 7, [11, 13, 9, 11, 3], [0, 0, 0, 0, 0]),
    ];

    [TestMethod, TestCategory("RealData")]
    public void Crash_site_exterior_combatants_and_their_gear()
    {
        var module = Environment.GetEnvironmentVariable("ARC_AREA_MODULE");
        if (string.IsNullOrEmpty(module)) Assert.Inconclusive("Opt-in original Area inputs are required.");
        using var owner = AreaOriginalValidator.OpenFiles(module, Required("ARC_AREA_RESOURCE1"), Required("ARC_AREA_RESOURCE2"),
            Required("ARC_AREA_MOBILE"), Required("ARC_AREA_PROTOTYPES"));
        var snapshot = owner.LoadStrict();
        var read = CombatFieldReader.ReadArea(snapshot);
        Assert.AreEqual(CombatFieldFailure.None, read.Failure, $"refused at {read.SourceIndex}/{read.Detail}");
        var scope = snapshot.Objects.Where(o => o.Key.Kind == AreaSourceKind.Mobile).ToDictionary(o => o.Key.SourceIndex, o => o.Scope);
        var objects = read.Objects!.ToDictionary(o => o.SourceIndex);
        Print(read.Objects!);

        // Creatures: NPCs with a monster damage table. Only normal damage is set.
        var creatures = read.Objects!.Where(o => o.Fields.Npc?.Damage.Any(d => d.Maximum != 0) == true).ToArray();
        Assert.AreEqual(ExteriorCreatures.Sum(c => c.Core + c.Context), creatures.Length);
        foreach (var expected in ExteriorCreatures)
        {
            var rows = creatures.Where(c => c.Prototype == expected.Prototype).ToArray();
            Assert.AreEqual((expected.Core, expected.Context), (rows.Count(r => scope[r.SourceIndex] == AreaScope.Core), rows.Count(r => scope[r.SourceIndex] != AreaScope.Core)), expected.Prototype.ToString());
            foreach (var row in rows)
            {
                var (critter, npc) = (row.Fields.Critter!, row.Fields.Npc!);
                Assert.AreEqual((expected.Ai, expected.Min, expected.Max), (npc.AiData, npc.Damage[0].Minimum, npc.Damage[0].Maximum), row.SourceIndex.ToString());
                Assert.IsTrue(npc.Damage.Skip(1).All(d => d == default), row.SourceIndex.ToString());
                CollectionAssert.AreEqual(expected.Stats, new[] { critter.BaseStats[0], critter.BaseStats[1], critter.BaseStats[2], critter.BaseStats[5], critter.BaseStats[17] }, row.SourceIndex.ToString());
                CollectionAssert.AreEqual(expected.Resistances, row.Fields.Object.Resistances.ToArray(), row.SourceIndex.ToString());
                Assert.IsTrue(critter.BasicSkills.All(s => s == 0), row.SourceIndex.ToString());
            }
        }

        // Gear: two weapons, four armour items, no ammunition.
        Assert.AreEqual((2, 4, 0), (read.Objects!.Count(o => o.Fields.Weapon is not null), read.Objects!.Count(o => o.Fields.Armor is not null),
            read.Objects!.Count(o => o.Fields.Ammo is not null)));
        var inventory = AreaInventoryReader.Read(snapshot).Inventory!;

        // The armed creature 6460 wields weapon 70: melee 2-6, speed 6, range 1, minimum strength 3, technological 5.
        Assert.IsTrue(inventory.TryGetHolder(6460, out var armed));
        CollectionAssert.AreEqual(new[] { 70 }, armed.Items.ToArray());
        var dagger = objects[70].Fields;
        Assert.AreEqual((6198, WieldedWeapon, -5), (dagger.Object.Prototype, dagger.Item!.InventoryLocation, dagger.Item.MagickTechComplexity));
        var w = dagger.Weapon!;
        Assert.AreEqual(CombatWeaponFlags.Silent | CombatWeaponFlags.HandCountFixed | CombatWeaponFlags.Throwable, w.Flags);
        Assert.AreEqual((CombatWeaponSkill.Melee, 6, 1, 3, 0, CombatFieldReader.NoAmmo, 2, 0), (w.Skill, w.SpeedFactor, w.Range, w.MinimumStrength,
            w.BonusToHit, w.AmmoType, w.CriticalHitChart, w.CriticalMissChart));
        CollectionAssert.AreEqual(new CombatDamageRange[] { new(2, 6), default, default, default, default }, w.Damage.ToArray());

        // Virgil (2907): AI row 5, dodge, melee and heal 1; a carried (not wielded) weapon with fatigue damage, worn armour.
        var virgil = objects[2907].Fields;
        Assert.AreEqual((17102, 5), (virgil.Object.Prototype, virgil.Npc!.AiData));
        CollectionAssert.AreEqual(new[] { 0, 1, 1, 0, 0, 0, 0, 0, 0, 0, 1, 0 }, virgil.Critter!.BasicSkills.ToArray());
        Assert.IsTrue(virgil.Npc.Damage.All(d => d == default));
        var staff = objects[2421].Fields;
        Assert.AreEqual((6057, 1), (staff.Object.Prototype, staff.Item!.InventoryLocation));
        Assert.AreEqual((CombatWeaponSkill.Melee, 8, 5, 1, 1), (staff.Weapon!.Skill, staff.Weapon.SpeedFactor, staff.Weapon.MinimumStrength,
            staff.Weapon.CriticalHitChart, staff.Weapon.CriticalMissChart));
        CollectionAssert.AreEqual(new CombatDamageRange[] { new(1, 4), default, default, default, new(2, 9) }, staff.Weapon.Damage.ToArray());
        var armour = objects[2908].Fields;
        Assert.AreEqual((8114, WornArmor, 3, CombatArmorFlags.SizeMedium), (armour.Object.Prototype, armour.Item!.InventoryLocation,
            armour.Armor!.ArmorClassAdjustment, armour.Armor.Flags));
        CollectionAssert.AreEqual(new[] { 3, -5, 0, 0, 0 }, armour.Armor.ResistanceAdjustment.ToArray());
        Assert.AreEqual(CombatFieldFailure.None, CombatFieldReader.ReadArea(owner.LoadStrict()).Failure, "repeatable");
    }

    [TestMethod, TestCategory("RealData")]
    public void Crash_site_cave_creatures_firearm_and_ammunition()
    {
        var cave = Environment.GetEnvironmentVariable("ARC_CAVE_MOBILE");
        if (string.IsNullOrEmpty(cave)) Assert.Inconclusive("Opt-in original inputs and ARC_CAVE_MOBILE are required.");
        using var owner = AreaOriginalValidator.OpenCaveFiles(Required("ARC_AREA_MODULE"), Required("ARC_AREA_RESOURCE1"), Required("ARC_AREA_RESOURCE2"),
            cave, Required("ARC_AREA_PROTOTYPES"));
        var read = CombatFieldReader.ReadArea(owner.LoadStrict());
        Assert.AreEqual(CombatFieldFailure.None, read.Failure, $"refused at {read.SourceIndex}/{read.Detail}");
        var objects = read.Objects!.ToDictionary(o => o.SourceIndex);
        Print(read.Objects!);

        // Six creatures of one prototype: AI row 19, normal damage 1-3, ST 2, DX 7, CN 3, PE 9.
        var creatures = read.Objects!.Where(o => o.Fields.Npc?.Damage.Any(d => d.Maximum != 0) == true).ToArray();
        CollectionAssert.AreEqual(new[] { 4, 8, 17, 25, 26, 30 }, creatures.Select(c => c.SourceIndex).Order().ToArray());
        foreach (var c in creatures)
        {
            Assert.AreEqual((28373, 19, new CombatDamageRange(1, 3)), (c.Prototype, c.Fields.Npc!.AiData, c.Fields.Npc.Damage[0]));
            var s = c.Fields.Critter!.BaseStats;
            Assert.AreEqual((2, 7, 3, 9), (s[0], s[1], s[2], s[5]));
        }

        // A firearm (11): bullets, one per shot, range 5, to-hit −10, normal 1-5; five melee weapons of prototype 6067 (1-2).
        var gun = objects[11].Fields;
        Assert.AreEqual((6102, -5), (gun.Object.Prototype, gun.Item!.MagickTechComplexity));
        Assert.AreEqual((CombatWeaponSkill.Firearms, (int)CombatAmmoType.Bullet, 1, 5, -10, 2, 5, 2, 3), (gun.Weapon!.Skill, gun.Weapon.AmmoType,
            gun.Weapon.AmmoConsumption, gun.Weapon.Range, gun.Weapon.BonusToHit, gun.Weapon.SpeedFactor, gun.Weapon.MinimumStrength,
            gun.Weapon.CriticalHitChart, gun.Weapon.CriticalMissChart));
        Assert.AreEqual(new CombatDamageRange(1, 5), gun.Weapon.Damage[0]);
        var clubs = read.Objects!.Where(o => o.Fields.Weapon is not null && o.SourceIndex != 11).ToArray();
        CollectionAssert.AreEqual(new[] { 12, 18, 24, 35, 38 }, clubs.Select(c => c.SourceIndex).ToArray());
        Assert.IsTrue(clubs.All(c => c.Prototype == 6067 && c.Fields.Weapon!.Skill == CombatWeaponSkill.Melee && c.Fields.Weapon.Damage[0] == new CombatDamageRange(1, 2)));

        // Two ammunition stacks of 15: arrows (1) and bullets (14).
        Assert.AreEqual(new CombatAmmoFields(0, 15, (int)CombatAmmoType.Arrow), objects[1].Fields.Ammo);
        Assert.AreEqual(new CombatAmmoFields(0, 15, (int)CombatAmmoType.Bullet), objects[14].Fields.Ammo);
        Assert.AreEqual(2, read.Objects!.Count(o => o.Fields.Ammo is not null));
    }

    [TestMethod, TestCategory("RealData")]
    public void Every_installed_item_and_critter_prototype_reads()
    {
        var root = Environment.GetEnvironmentVariable("ARC_AREA_PROTOTYPES");
        if (string.IsNullOrEmpty(root)) Assert.Inconclusive("ARC_AREA_PROTOTYPES (the installation's data/proto) is required.");
        var lookup = PremadeHeroReader.DirectoryPrototypes(root);
        var numbers = Directory.EnumerateFiles(root, "*.pro").Select(p => Path.GetFileName(p)).Where(n => n.Length > 6 && n[..6].All(char.IsAsciiDigit))
            .Select(n => int.Parse(n[..6], System.Globalization.CultureInfo.InvariantCulture)).Distinct().Order().ToArray();
        var reads = numbers.Select(n => (Number: n, Read: CombatFieldReader.ReadPrototype(lookup, n))).ToArray();
        var refused = reads.Where(r => r.Read.Failure is not (CombatFieldFailure.None or CombatFieldFailure.TypeUnsupported)).ToArray();
        Assert.AreEqual(0, refused.Length, string.Join(", ", refused.Select(r => $"{r.Number}:{r.Read.Failure}/{r.Read.Detail}")));
        var byType = reads.Where(r => r.Read.Fields is not null).GroupBy(r => r.Read.Fields!.Object.Type).ToDictionary(g => g.Key, g => g.Count());
        Console.WriteLine(JsonSerializer.Serialize(byType.OrderBy(p => p.Key)));
        // Weapons 150, ammunition 4, armour 251, PCs 13, NPCs 497 (first observation); walls, portals, containers, scenery,
        // projectiles and traps are not combat items.
        Assert.AreEqual((150, 4, 251, 13, 497), (byType[5], byType[6], byType[7], byType[15], byType[16]));
        Assert.AreEqual(1308, byType.Values.Sum());
        Assert.IsTrue(reads.Where(r => r.Read.Failure == CombatFieldFailure.TypeUnsupported).All(r => r.Read.Detail is (>= 0 and <= 4) or 17));
        var skills = reads.Select(r => r.Read.Fields?.Weapon?.Skill).OfType<CombatWeaponSkill>().GroupBy(s => s).ToDictionary(g => g.Key, g => g.Count());
        Assert.AreEqual((89, 12, 8, 41), (skills[CombatWeaponSkill.Melee], skills[CombatWeaponSkill.Bow], skills[CombatWeaponSkill.Throwing], skills[CombatWeaponSkill.Firearms]));
        // One ammunition prototype per type, stack 10.
        CollectionAssert.AreEqual(new[] { (7058, 0), (7059, 1), (7060, 2), (7061, 3) },
            reads.Where(r => r.Read.Fields?.Ammo is { Quantity: 10 }).Select(r => (r.Number, r.Read.Fields!.Ammo!.AmmoType)).ToArray());
    }

    private static void Print(IEnumerable<CombatAreaObject> objects) =>
        Console.WriteLine(JsonSerializer.Serialize(objects.Where(o => o.Fields.Critter is not null || o.Fields.Weapon is not null || o.Fields.Ammo is not null || o.Fields.Armor is not null)
            .Select(o => new { o.SourceIndex, o.Prototype, o.Fields.Weapon, o.Fields.Ammo, o.Fields.Armor, o.Fields.Npc?.AiData, o.Fields.Npc?.Damage })));

    private static string Required(string name) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } value ? value : throw new AssertInconclusiveException(name + " is required.");
}
