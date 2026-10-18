using Arcanum.Data.Area;
using Arcanum.Data.Characters;
using Arcanum.Data.Combat;
using Arcanum.Data.Content;
using Arcanum.Data.Maps;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Data.Tests.Characters;

/// <summary>ARC-DATA-033 opt-in check of the installed original (DAT_ROOT = the game folder): the AI parameter table and
/// the rows the NPCs of every interior name.</summary>
[TestClass]
public sealed class AiParameterTableRealDataTests
{
    [TestMethod, TestCategory("RealData")]
    public void The_installed_table_and_every_interior_npcs_row()
    {
        var root = Environment.GetEnvironmentVariable("DAT_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Inconclusive("DAT_ROOT is required for the opt-in AI table check; this is not a synthetic pass.");
        using var stack = ContentStack.Open(ContentMountPlan.Discover(root).Plan!).Stack!;
        var load = AiParameterTable.Open(stack);
        Assert.AreEqual(AiParameterTableStatus.Read, load.Status, load.Detail);
        var table = load.Table!;
        // 41 engine rows (entries 0..40) and the module's one row (100).
        Assert.AreEqual((41, 1), (table.BaseFilled, table.ModuleFilled));
        var virgil = table.Row(5)!;
        Assert.AreEqual((-1000, 2001, 2001, 30), (virgil.DislikeReaction, virgil.MaxAlignmentAbove, virgil.MaxAlignmentBelow, virgil.LevelGate));

        var catalog = WorldCatalog.Open(stack);
        var rows = new SortedDictionary<int, int>(); var npcs = 0; var unread = 0;
        foreach (var map in catalog.Maps.Where(m => m.Name != "arcanum1-024-fixed"))
        {
            AreaSnapshot snapshot;
            using (var owner = AreaMapPlans.Create(stack, catalog, map.Name).Plan!.Open()) snapshot = owner.LoadStrict();
            foreach (var row in snapshot.Objects.Where(r => r.Type == CombatFieldReader.NpcType && r.Scope == AreaScope.Core))
            {
                npcs++;
                var read = CombatFieldReader.Read(row);
                if (read.Fields?.Npc is not { } npc) { unread++; continue; }
                rows[npc.AiData] = rows.GetValueOrDefault(npc.AiData) + 1;
            }
        }
        // Every core NPC of every interior reads; each names one of 21 filled rows, the most common 19, 0, 2, 10 and 15.
        Assert.AreEqual((2236, 0), (npcs, unread));
        Assert.AreEqual("0=559, 2=157, 4=10, 5=1, 6=1, 7=1, 8=1, 9=1, 10=113, 12=19, 13=8, 14=36, 15=75, 16=6, 18=1, 19=1240, 23=3, 24=1, 26=1, 28=1, 33=1",
            string.Join(", ", rows.Select(r => $"{r.Key}={r.Value}")));
        Assert.IsTrue(rows.Keys.All(index => index < table.BaseFilled), "every row an NPC names was filled by an entry");
    }
}
