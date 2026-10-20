using System.Globalization;
using System.Text.Json;
using Arcanum.Data.Area;
using Arcanum.Data.Characters;
using Arcanum.Data.Combat;
using Arcanum.Data.Content;
using Arcanum.Data.Maps;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Skills;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Session.Tests;

/// <summary>
/// Opt-in census of ARC-SKILLS-001 on the installed original: the twelve basic and four tech skill fields of every PC and NPC
/// prototype (<c>ARC_AREA_PROTOTYPES</c>, the installation's <c>data/proto</c>) and of every PC and NPC placed on any of the
/// installed maps (<c>DAT_ROOT</c>: each map's mobile objects with their effective fields, the instance's own over its
/// prototype's) read through <see cref="SkillRules"/> with the critter's own base stats, monstrous flags and character level
/// (effects are not applied). Answers the census items of the skills research (section 11): any points above five, any
/// training without the level its tier needs, any point the key stat would not allow. Only aggregates are compared and
/// printed, never original text.
/// </summary>
[TestClass]
public sealed class SkillRulesRealDataTests
{
    // The critter flags of the source's monstrous test (ledger FA9): undead, animal, monster, mechanical.
    private const uint MonstrousFlags = 0x4 | 0x8 | 0x8000 | 0x20000000;
    private const int CharacterLevelStat = 17;

    [TestMethod, TestCategory("RealData")]
    public void Every_critter_prototypes_skills_read_through_the_skill_rules()
    {
        var root = Environment.GetEnvironmentVariable("ARC_AREA_PROTOTYPES");
        if (string.IsNullOrEmpty(root)) Assert.Inconclusive("ARC_AREA_PROTOTYPES (the installation's data/proto) is required.");
        var lookup = PremadeHeroReader.DirectoryPrototypes(root);
        var numbers = Directory.EnumerateFiles(root, "*.pro").Select(Path.GetFileName).Where(n => n!.Length > 6 && n[..6].All(char.IsAsciiDigit))
            .Select(n => int.Parse(n![..6], CultureInfo.InvariantCulture)).Distinct().Order().ToArray();
        var critters = numbers.Select(n => CombatFieldReader.ReadPrototype(lookup, n).Fields?.Critter).OfType<CombatCritterFields>().ToArray();

        var census = Census(critters);
        Console.WriteLine("prototypes " + JsonSerializer.Serialize(census));
        Assert.AreEqual(510, critters.Length, "13 PC and 497 NPC prototypes (ARC-DATA-021)");
        // First observation 2026-10-20: no prototype stores a skill value; only the monstrous melee (280 prototypes) gives a
        // level and a derived training.
        Assert.AreEqual((280, 0, 0, 280), (census.Monstrous, census.WithPoints, census.StoredTiers.Skip(1).Sum(), census.LevelsAboveZero));
        CollectionAssert.AreEqual(new[] { 0, 82, 69, 25 }, census.Tiers);
    }

    [TestMethod, TestCategory("RealData")]
    public void Every_placed_critters_skills_read_through_the_skill_rules()
    {
        var root = Environment.GetEnvironmentVariable("DAT_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Inconclusive("DAT_ROOT is required.");
        var discovery = ContentMountPlan.Discover(root);
        Assert.AreEqual(ContentMountStatus.Ready, discovery.Status, discovery.Detail);
        using var stack = ContentStack.Open(discovery.Plan!).Stack!;
        var catalog = WorldCatalog.Open(stack);
        var prototypes = new Dictionary<int, WorldObject>();
        foreach (var number in catalog.PrototypeNumbers)
            if (catalog.ReadPrototype(number).Prototype is { } prototype) prototypes[number] = prototype;
        var critters = new List<CombatCritterFields>();
        int maps = 0, missingPrototype = 0, refused = 0;
        foreach (var map in catalog.Maps)
        {
            var mobiles = catalog.ReadMobiles(map).Objects;
            var placed = 0;
            for (var i = 0; i < mobiles.Count; i++)
            {
                var o = mobiles[i];
                if (o.Type is not (CombatFieldReader.PcType or CombatFieldReader.NpcType)) continue;
                if (o.PrototypeNumber is not { } p || !prototypes.TryGetValue(p, out var prototype)) { missingPrototype++; continue; }
                if (CombatFieldReader.Read(Effective(o, prototype, i)).Fields?.Critter is { } critter) { critters.Add(critter); placed++; }
                else refused++;
            }
            if (placed > 0) maps++;
        }
        var census = Census(critters);
        Console.WriteLine($"placed maps={maps} missingPrototype={missingPrototype} refused={refused} " + JsonSerializer.Serialize(census));
        Assert.AreEqual((0, 0), (missingPrototype, refused));
        // First observation 2026-10-20: 3,792 placed critters on 76 maps, 1,049 with skill points. Stored values exceed the
        // purchase rules (up to 16 points; 779 values with more points than the key stat's base value allows, 5 tiers without
        // their level); the reads keep them and the level's clamp and cap still hold, as in the original.
        Assert.AreEqual((76, 3792, 2581, 1049, 1496), (maps, census.Critters, census.Monstrous, census.WithPoints, census.Trained));
        CollectionAssert.AreEqual(new[] { 382, 835, 926, 298, 31, 31, 154, 47, 219, 195, 301, 64, 69, 232, 40, 17 }, census.PerSkill);
        CollectionAssert.AreEqual(new[] { 60196, 229, 193, 54 }, census.StoredTiers);
        Assert.AreEqual((16, 6, 779, 5, 0), (census.MaxPoints, census.AboveFive, census.PointsAboveCap, census.TrainingWithoutLevel, census.OtherBits));
    }

    private sealed record SkillCensus(int Critters, int Monstrous, int WithPoints, int Trained, int[] PerSkill, int MaxPoints, int[] StoredTiers,
        int[] Tiers, int AboveFive, int PointsAboveCap, int TrainingWithoutLevel, int OtherBits, int LevelsAboveZero);

    private static SkillCensus Census(IReadOnlyList<CombatCritterFields> critters)
    {
        int withPoints = 0, trained = 0, monstrous = 0, aboveFive = 0, trainingWithoutLevel = 0, pointsAboveCap = 0, otherBits = 0, levelsAboveZero = 0;
        var perSkill = new int[SkillRules.Count];
        var perTier = new int[4];
        var storedTiers = new int[4];
        var maxPoints = 0;
        foreach (var critter in critters)
        {
            Assert.AreEqual((SkillRules.BasicCount, SkillRules.TechCount), (critter.BasicSkills.Count, critter.TechSkills.Count));
            var isMonstrous = (critter.Flags & MonstrousFlags) != 0;
            if (isMonstrous) monstrous++;
            var any = false;
            var anyTrained = false;
            foreach (var skill in Enum.GetValues<Skill>())
            {
                var stored = SkillRules.IsTech(skill) ? critter.TechSkills[SkillRules.FamilyIndex(skill)] : critter.BasicSkills[(int)skill];
                Assert.IsTrue(stored is >= 0 and <= SkillRules.MaxValue, $"stored {stored}");
                var keyStat = critter.BaseStats[(int)SkillRules.KeyStat(skill)];
                var points = SkillRules.Points(stored);
                var level = SkillRules.Level(skill, stored, keyStat, isMonstrous);
                var meleeLevel = SkillRules.Level(Skill.Melee, critter.BasicSkills[(int)Skill.Melee], critter.BaseStats[(int)SkillRules.KeyStat(Skill.Melee)], isMonstrous);
                var training = SkillRules.Training(skill, stored, isMonstrous, meleeLevel, critter.BaseStats[CharacterLevelStat]);
                if (points > 0) { any = true; perSkill[(int)skill]++; }
                maxPoints = Math.Max(maxPoints, points);
                storedTiers[(int)SkillRules.StoredTraining(stored)]++;
                if (level > 0) levelsAboveZero++;
                if (training > SkillTraining.None) { anyTrained = true; perTier[(int)training]++; }
                if (points > SkillRules.MaxPoints) aboveFive++;
                if (SkillRules.LevelPerPoint * points > SkillRules.KeyStatCap(keyStat)) pointsAboveCap++;
                if (level < SkillRules.MinimumLevel(training)) trainingWithoutLevel++;
                if ((stored & ~0xFF) != 0) otherBits++;
            }
            if (any) withPoints++;
            if (anyTrained) trained++;
        }
        return new(critters.Count, monstrous, withPoints, trained, perSkill, maxPoints, storedTiers, perTier, aboveFive, pointsAboveCap,
            trainingWithoutLevel, otherBits, levelsAboveZero);
    }

    /// <summary>An instance's effective fields as an Area record (its own field, else its prototype's whole field), as the
    /// combat census reads catalogue mobiles (ARC-RES-041).</summary>
    private static AreaObjectRecord Effective(WorldObject instance, WorldObject prototype, int index)
    {
        static AreaFieldValue Value(int slot, WorldFieldValue v, AreaValueOrigin origin) =>
            new(slot, (AreaPresence)((int)v.Presence + 1), origin, v.Number, v.Elements.Select(e => new AreaSparseElement(e.Key, e.Words)).ToArray(), v.Fingerprint);
        var effective = instance.Fields.Keys.Union(prototype.Fields.Keys).Order()
            .Select(slot => instance.Fields.TryGetValue(slot, out var own) ? Value(slot, own, AreaValueOrigin.Instance) : Value(slot, prototype.Fields[slot], AreaValueOrigin.Prototype))
            .ToArray();
        return new(new("", 0, AreaSourceKind.Mobile, null, index, 0, instance.OwnIdSha256, 0), 119, instance.Type, instance.PrototypeNumber ?? 0,
            "", "", "", "", AreaScope.Unknown, null, null, null, AreaDisposition.ContextOnly, [], effective, true, true, true);
    }
}
