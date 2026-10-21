using Arcanum.Data.Characters;
using Arcanum.Data.Content;
using Arcanum.Data.Magic;
using Arcanum.Data.Messages;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Data.Tests.Magic;

/// <summary>ARC-MAGIC-001/002 opt-in check of the installed original (DAT_ROOT = the game folder): every spell record against the
/// combat-domain data census (docs/research/combat-data-census.md §3) and the component census (docs/formats/spell-records.md).
/// Counts only; no game text.</summary>
[TestClass]
public sealed class SpellRecordRealDataTests
{
    [TestMethod, TestCategory("RealData")]
    public void The_installed_spell_records_match_the_census()
    {
        var root = Environment.GetEnvironmentVariable("DAT_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Inconclusive("DAT_ROOT is required for the opt-in spell record check; this is not a synthetic pass.");
        using var stack = ContentStack.Open(ContentMountPlan.Discover(root).Plan!).Stack!;
        var like = SpellData.Open(stack, SpellRecordMode.LikeOriginal);
        Assert.AreEqual(SpellDataStatus.Read, like.Status, $"{like.Refusal}");
        var table = like.Data!.Records;
        var records = table.Records;
        Assert.AreEqual(SpellRecord.Count, records.Count);

        // Lines +0..+4 (ARC-MAGIC-001).
        Assert.AreEqual("223/223/10/160/105", string.Join('/', Enumerable.Range(0, 5).Select(o => records.Count(r => r.LinesPresent[o]))));
        string Ranges(string key) =>
            $"{records.Take(80).Count(r => r.KeysRead.Contains(key))}/{records.Skip(80).Take(60).Count(r => r.KeysRead.Contains(key))}/" +
            $"{records.Skip(140).Count(r => r.KeysRead.Contains(key))}";
        var expected = new Dictionary<string, string>
        {
            ["IQ:"] = "0/0/0", ["Cost:"] = "80/36/41", ["Maintain:"] = "53/7/6", ["Duration:"] = "2/12/37", ["DurationStatInfo:"] = "0/0/9",
            ["DurationTriggerCount:"] = "2/3/6", ["Resist:"] = "26/6/5", ["Range:"] = "0/8/42", ["ChargesFromCells:"] = "0/0/3",
            ["ChargeBeginCost:"] = "1/0/1", ["Info:"] = "37/43/77", ["Disabled:"] = "0/0/0", ["No_Resist:"] = "0/0/2", ["No_Reflect:"] = "2/0/0",
        };
        foreach (var (key, counts) in expected) Assert.AreEqual(counts, Ranges(key), key);
        Assert.AreEqual("stat_constitution=22, stat_dexterity=2, stat_strength=1, stat_willpower=12", string.Join(", ",
            records.Where(r => r.ResistStat >= 0).GroupBy(r => SpellNameTables.StatKeys[r.ResistStat]).OrderBy(g => g.Key, StringComparer.Ordinal)
                .Select(g => $"{g.Key}={g.Count()}")));
        Assert.AreEqual((82, 75, 0, 0), (records.Count(r => r.Info == SpellInfo.Aggressive), records.Count(r => r.Info == SpellInfo.Friendly),
            records.Count(r => r.Info == SpellInfo.MayBeAggressive), records.Count(r => !r.Enabled)));
        Assert.AreEqual((2, 2, 83), (records.Count(r => r.NoResist), records.Count(r => r.NoReflect), records.Count(r => r.IsTechnology)));
        Assert.AreEqual("7,16,25,32,7,3,1,3,1,2", string.Join(',', Enumerable.Range(0, 10).Select(a => records.Count(r => r.AiPriorities[a] != -1))));
        var willpower = AutoLevelTables.ReadSpellWillpower(Read(stack, SpellRecordTable.Path));
        CollectionAssert.AreEqual(willpower.Minima!.ToArray(), records.Take(80).Select(r => r.MinimumWillpower).ToArray());
        Assert.AreEqual((25, 16, 80), (like.Data.ComponentTypeNames.Count, like.Data.Texts.CollegeNames.Count(n => n is not null),
            like.Data.Texts.SpellDescriptions.Count(n => n is not null)));

        // Components (ARC-MAGIC-002): 1,009 lines, all parsed; per type the count in Begin/Maintain/End/Callback/EndCallback.
        var components = records.SelectMany(r => r.Components).ToList();
        Assert.AreEqual((1009, 1009), (components.Count, records.Sum(r => r.ComponentLines.Count)));
        Assert.AreEqual("0=5, 1-4=123, 5-9=86, 10-13=9", string.Join(", ", records.GroupBy(r => r.Components.Count switch
        {
            0 => "0", < 5 => "1-4", < 10 => "5-9", _ => "10-13",
        }).OrderBy(g => g.Min(r => r.Components.Count)).Select(g => $"{g.Key}={g.Count()}")));
        Assert.AreEqual("NoOp=4/0/0/0/0; AGoal=17/0/0/0/0; AIRedirect=1/1/0/0/0; Cast=1/0/0/0/0; ChargeNBranch=2/1/0/0/0; Damage=51/7/7/3/0; " +
            "Destroy=17/0/1/15/24; Dispel=2/0/0/0/0; Effect=75/0/50/0/1; EyeCandy=261/3/163/0/2; Heal=34/6/0/5/0; Identify=3/0/0/0/0; " +
            "Movement=3/0/0/0/0; ObjFlag=103/0/59/27/0; Recharge=1/0/0/0/0; Summon=42/0/0/0/0; Terminate=1/0/0/0/0; TestNBranch=0/1/0/0/0; " +
            "Trait=9/0/5/0/0; Use=0/0/1/0/0", string.Join("; ", components.GroupBy(c => c.Type).OrderBy(g => g.Key)
                .Select(g => $"{g.Key}=" + string.Join('/', Enumerable.Range(0, 5).Select(a => g.Count(c => (int)c.Action == a))))));
        Assert.AreEqual(0, components.Count(c => c.Type == SpellComponentType.Interrupt), "MG7: no record uses Interrupt");
        Assert.AreEqual((52, 54), (records.Count(r => r.HasDamage), records.Count(r => r.SpellFlagsSet != 0)));
        Assert.AreEqual(3, records.Count(r => r.Areas.All(a => (a.Targets & SpellComponentTables.NotInvulnerableBit) == 0)));

        // Records 80..139: what the non-college magic records hold.
        Assert.AreEqual("NoOp=2, AGoal=5, Damage=15, Destroy=5, Effect=28, EyeCandy=69, Heal=16, ObjFlag=38, Recharge=1, Summon=5, Trait=3",
            string.Join(", ", records.Skip(80).Take(60).SelectMany(r => r.Components).GroupBy(c => c.Type).OrderBy(g => g.Key).Select(g => $"{g.Key}={g.Count()}")));
        Assert.AreEqual((57, 16, 23), (records.Skip(80).Take(60).Count(r => r.Components.Count > 0),
            records.Skip(80).Take(60).Count(r => r.AiPriorities.Any(p => p != -1)), records.Skip(80).Take(60).Count(r => r.ItemTriggers != 0)));

        // Damage and heal flags, damage types.
        var damage = components.Where(c => c.Data is DamageData).Select(c => (c.Type, Data: (DamageData)c.Data!)).ToList();
        Assert.AreEqual("Full=16, Resurrect=7, Reanimate=2, Death=4, Stunned=3, Scars=2, Scaled=16, Weap_Dropped=1, Crippling=2",
            string.Join(", ", Enumerable.Range(1, 9).Select(i => $"{SpellComponentTables.DamageFlags[i]}=" +
                damage.Count(d => (d.Data.Flags & SpellComponentTables.DamageFlagValues[i]) == SpellComponentTables.DamageFlagValues[i]))));
        Assert.AreEqual((24, 12), (damage.Count(d => d.Type == SpellComponentType.Damage && d.Data.Flags != 0), damage.Count(d => d.Type == SpellComponentType.Heal && d.Data.Flags != 0)));
        Assert.AreEqual("Damage/0=32, Damage/1=10, Damage/2=9, Damage/3=6, Damage/4=10, Damage/5=1, Heal/0=32, Heal/1=8, Heal/4=5",
            string.Join(", ", damage.GroupBy(d => (d.Type, d.Data.DamageType)).OrderBy(g => g.Key).Select(g => $"{g.Key.Type}/{g.Key.DamageType}={g.Count()}")));

        // Summons (MG8, MG9): list, faction, palette; Proto is the basic prototype number, whose file is number + 20.
        var summons = components.Select(c => c.Data).OfType<SummonData>().ToList();
        Assert.AreEqual("42 lists -1:38,0:1,3:1,4:1,5:1 faction 0 palette 0 none 4", $"{summons.Count} lists " +
            string.Join(",", summons.GroupBy(s => s.List).OrderBy(g => g.Key).Select(g => $"{g.Key}:{g.Count()}")) +
            $" faction {summons.Count(s => s.ClearFaction != 0)} palette {summons.Count(s => s.Palette != 0)} none {summons.Count(s => s.Prototype <= 0)}");
        Assert.IsTrue(summons.Where(s => s.Prototype <= 0).All(s => s.List >= 0), "the summons without a prototype are the ones with a list");
        var prototypes = ContentPrototypes.Open(stack);
        var numbers = summons.Where(s => s.Prototype > 0).Select(s => s.Prototype).Distinct().ToList();
        Assert.AreEqual((20, 20, 13), (numbers.Count, numbers.Count(n => prototypes.Read(n + 20).Status == PrototypeFileStatus.Found),
            numbers.Count(n => prototypes.Read(n).Status == PrototypeFileStatus.Found)));

        // Stacking and cancel sets; No_Resist with maintenance or duration (sizes MG19).
        Assert.AreEqual((109, 34, 12, 0, 3, 15, 6), (records.Count(r => r.NoStack == 1), records.Count(r => r.NoStack == 0 && r.KeysRead.Contains("No_Stack:")),
            records.Count(r => r.CancelsSpellFlags != 0), records.Count(r => r.CancelsEnvironmentSpellFlags != 0), records.Count(r => r.DisallowedCasterSpellFlags != 0),
            records.Count(r => r.DisallowedTargetSpellFlags != 0), records.Count(r => r.DisallowedTargetCritterFlags != 0)));
        Assert.AreEqual(0, records.Count(r => r.NoResist && (r.MaintainPeriod != 0 || r.DurationSeconds != 0 || r.DurationTriggerCount != 0)));

        // MG18: the not-dead-critter target is named by 105 records (100 line +0 areas, 15 components); adding the critter bit
        // would change only the masks without it: record 33's five areas and record 168's two components.
        const ulong mg18 = 0x80000000UL;
        Assert.AreEqual((105, 100, 15), (
            records.Count(r => r.Areas.Any(a => (a.Targets & mg18) != 0) || r.Components.Any(c => ((c.Area.Targets | c.ApplyTargets) & mg18) != 0)),
            records.Count(r => r.Areas.Any(a => (a.Targets & mg18) != 0)), components.Count(c => ((c.Area.Targets | c.ApplyTargets) & mg18) != 0)));
        static bool Changed(ulong mask) => (mask & mg18) != 0 && (mask & SpellComponentTables.CritterTargetBit) == 0;
        Assert.AreEqual("33:5/0, 168:0/2", string.Join(", ", records.Select(r => (r.Number, Areas: r.Areas.Count(a => Changed(a.Targets)),
                Components: r.Components.Count(c => Changed(c.Area.Targets) || Changed(c.ApplyTargets))))
            .Where(r => r.Areas + r.Components > 0).Select(r => $"{r.Number}:{r.Areas}/{r.Components}")));

        // Text the original never reads: 43 words after a Destroy (40) or Identify (3) component, one repeated Dmg_Flags.
        Assert.AreEqual("SkippedKey=1, UnknownKey=43", string.Join(", ", table.Unread.GroupBy(u => u.Status).OrderBy(g => g.Key.ToString(), StringComparer.Ordinal)
            .Select(g => $"{g.Key}={g.Count()}")));
        Assert.AreEqual("Damage=1, Destroy=40, Identify=3", string.Join(", ", table.Unread.Select(u => Owner(records, u.Entry!.Value).Type)
            .GroupBy(t => t).OrderBy(g => g.Key.ToString(), StringComparer.Ordinal).Select(g => $"{g.Key}={g.Count()}")));
        var strict = SpellData.Open(stack, SpellRecordMode.Strict);
        Assert.AreEqual((SpellDataStatus.UnknownKey, (int?)table.Unread[0].Entry), (strict.Status, strict.Refusal!.Entry), "strict mode refuses the first");
    }

    /// <summary>ARC-MAGIC-012: the installed Damage and Heal components (types and flags the components read) and data fix DF4: the
    /// Steamwoven read differs from the original's only in record 221's Damage component, which gets <c>Scaled</c> back.</summary>
    [TestMethod, TestCategory("RealData")]
    public void The_installed_damage_and_heal_components_match_the_census_and_DF4_restores_one_flag()
    {
        var root = Environment.GetEnvironmentVariable("DAT_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Inconclusive("DAT_ROOT is required for the opt-in spell record check; this is not a synthetic pass.");
        using var stack = ContentStack.Open(ContentMountPlan.Discover(root).Plan!).Stack!;
        var original = SpellData.Open(stack, SpellRecordMode.LikeOriginal, fixes: Arcanum.Data.Fixes.DataFixProfile.Original).Data!.Records.Records;
        var steamwoven = SpellData.Open(stack, SpellRecordMode.LikeOriginal, fixes: Arcanum.Data.Fixes.DataFixProfile.Steamwoven).Data!.Records.Records;
        var life = original.SelectMany(r => r.Components).Where(c => c.Type is SpellComponentType.Damage or SpellComponentType.Heal).ToList();
        string Types(SpellComponentType type) => string.Join(" ", life.Where(c => c.Type == type).GroupBy(c => ((DamageData)c.Data!).DamageType)
            .OrderBy(g => g.Key).Select(g => $"{g.Key}:{g.Count()}"));
        string Flags(SpellComponentType type) => string.Join(" ", new uint[] { 0x1, 0x2, 0x4, 0x40, 0x200, 0x400, 0x800, 0x1000, 0x2000, 0x400000, 0x800000 }
            .Select(bit => $"{bit:X}:{life.Count(c => c.Type == type && (((DamageData)c.Data!).Flags & bit) == bit)}"));
        Assert.AreEqual("0:32 1:10 2:9 3:6 4:10 5:1", Types(SpellComponentType.Damage));
        Assert.AreEqual("0:32 1:8 4:5", Types(SpellComponentType.Heal));
        Assert.AreEqual("1:7 2:0 4:4 40:3 200:0 400:0 800:0 1000:0 2000:1 400000:0 800000:13", Flags(SpellComponentType.Damage));
        Assert.AreEqual("1:9 2:7 4:0 40:0 200:2 400:2 800:2 1000:2 2000:0 400000:2 800000:3", Flags(SpellComponentType.Heal));
        var changed = Enumerable.Range(0, SpellRecord.Count).Where(r => !original[r].Components.SequenceEqual(steamwoven[r].Components)).ToArray();
        CollectionAssert.AreEqual(new[] { 221 }, changed, "DF4 changes one record");
        var before = (DamageData)original[221].Components.Single(c => c.Type == SpellComponentType.Damage).Data!;
        var after = (DamageData)steamwoven[221].Components.Single(c => c.Type == SpellComponentType.Damage).Data!;
        Assert.AreEqual((0x5u, 0x800005u, 0, 0), (before.Flags, after.Flags, after.Minimum, after.Maximum),
            "a full death hit with a 0-0 range: the restored Scaled changes no amount");
    }

    private static SpellComponent Owner(IReadOnlyList<SpellRecord> records, int entry)
    {
        var record = records.Last(r => SpellRecordTable.FirstMessage(r.Number) <= entry && entry < SpellRecordTable.FirstMessage(r.Number) + 50);
        return record.Components[entry - SpellRecordTable.FirstMessage(record.Number) - SpellRecordTable.FirstComponentLine];
    }

    private static MesFile Read(ContentStack stack, string path) =>
        MesFile.Parse(stack.Read(ContentPhase.Candidate(stack, path, ContentLoadPhase.GameStart)!).Bytes!).File!;
}
