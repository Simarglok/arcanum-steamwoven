using Arcanum.Data.Characters;
using Arcanum.Data.Content;
using Arcanum.Data.Magic;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Data.Tests.Magic;

/// <summary>ARC-MAGIC-001 opt-in check of the installed original (DAT_ROOT = the game folder): every spell record against the
/// combat-domain data census (docs/research/combat-data-census.md §3).</summary>
[TestClass]
public sealed class SpellRecordRealDataTests
{
    [TestMethod, TestCategory("RealData")]
    public void The_installed_spell_records_match_the_census_in_both_modes()
    {
        var root = Environment.GetEnvironmentVariable("DAT_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Inconclusive("DAT_ROOT is required for the opt-in spell record check; this is not a synthetic pass.");
        using var stack = ContentStack.Open(ContentMountPlan.Discover(root).Plan!).Stack!;
        // Strict mode reads the installation: no unknown or skipped key, no missing line or text, no dropped value.
        var strict = SpellData.Open(stack, SpellRecordMode.Strict);
        Assert.AreEqual(SpellDataStatus.Read, strict.Status, $"{strict.Refusal}");
        var like = SpellData.Open(stack, SpellRecordMode.LikeOriginal);
        Assert.AreEqual(SpellDataStatus.Read, like.Status, $"{like.Refusal}");
        var records = strict.Data!.Records.Records;
        CollectionAssert.AreEqual(records.ToArray(), like.Data!.Records.Records.ToArray(), "both modes read the same records");
        Assert.AreEqual(SpellRecord.Count, records.Count);

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
        // The AI action list sizes of the census: flee, summon, defensive, offensive, three healing, cure poison, fatigue, resurrect.
        Assert.AreEqual("7,16,25,32,7,3,1,3,1,2", string.Join(',', Enumerable.Range(0, 10).Select(a => records.Count(r => r.AiPriorities[a] != -1))));
        Assert.AreEqual("0=5, 1-4=123, 5-9=86, 10-13=9", string.Join(", ", records.GroupBy(r => r.ComponentLines.Count switch
        {
            0 => "0", < 5 => "1-4", < 10 => "5-9", _ => "10-13",
        }).OrderBy(g => g.Min(r => r.ComponentLines.Count)).Select(g => $"{g.Key}={g.Count()}")));
        Assert.IsTrue(records.All(r => r.ComponentLines.Count <= 13));

        // No record names its own willpower, so the auto-level read keeps the rank defaults and agrees with the record reader.
        var willpower = AutoLevelTables.ReadSpellWillpower(Read(stack, SpellRecordTable.Path));
        CollectionAssert.AreEqual(willpower.Minima!.ToArray(), records.Take(80).Select(r => r.MinimumWillpower).ToArray());
        Assert.AreEqual((25, 16, 80), (strict.Data.ComponentTypeNames.Count, strict.Data.Texts.CollegeNames.Count(n => n is not null),
            strict.Data.Texts.SpellDescriptions.Count(n => n is not null)));
    }

    private static Arcanum.Data.Messages.MesFile Read(ContentStack stack, string path)
    {
        var read = stack.Read(ContentPhase.Candidate(stack, path, ContentLoadPhase.GameStart)!);
        return Arcanum.Data.Messages.MesFile.Parse(read.Bytes!).File!;
    }
}
