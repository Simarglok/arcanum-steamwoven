using Arcanum.Data.Area;
using Arcanum.Data.Content;
using Arcanum.Data.Maps;
using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>Opt-in ARC-WORLD-037 check (DAT_ROOT = the game folder): every dialog NPC of every interior gets its follow
/// facts from the game's data.</summary>
[TestClass]
public sealed class AreaMapCompanionOriginalTests
{
    [TestMethod, TestCategory("RealData")]
    public void Every_interior_dialog_npc_has_follow_facts_from_the_data()
    {
        var root = Environment.GetEnvironmentVariable("DAT_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Inconclusive("DAT_ROOT is required for the opt-in companion facts check; this is not a synthetic pass.");
        var data = AreaCompanionData.Open(Path.Combine(root, "modules", "Arcanum.dat"));
        Assert.AreEqual(AreaCompanionDataStatus.Ready, data.Status, data.Detail);
        using var stack = ContentStack.Open(ContentMountPlan.Discover(root).Plan!).Stack!;
        var catalog = WorldCatalog.Open(stack);
        var speakers = 0; var refused = new List<string>(); var levels = new SortedDictionary<int, int>(); var rows = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var map in catalog.Maps.Where(m => m.Name != "arcanum1-024-fixed"))
        {
            AreaSnapshot snapshot;
            using (var owner = AreaMapPlans.Create(stack, catalog, map.Name).Plan!.Open()) snapshot = owner.LoadStrict();
            foreach (var speaker in AreaConversationHost.FindSpeakers(snapshot))
            {
                speakers++;
                if (data.Data!.Facts(speaker, out var refusal) is not { } facts) { refused.Add($"{map.Name} script {speaker.ScriptId}: {refusal}"); continue; }
                levels[facts.Level] = levels.GetValueOrDefault(facts.Level) + 1;
                var key = $"{facts.Thresholds.DislikeReaction}/{facts.Thresholds.MaxAlignmentAbove}/{facts.Thresholds.MaxAlignmentBelow}/{facts.Thresholds.LevelGate}";
                rows[key] = rows.GetValueOrDefault(key) + 1;
            }
        }
        Console.WriteLine("levels " + string.Join(", ", levels.Select(l => $"{l.Key}={l.Value}")));
        // 185 dialog NPCs (the unlisted interior's included) all read; ten distinct follow rows (dislike/above/below/gate), most
        // NPCs on 20/1001/1000/2, four on a row whose dislike of 1000 refuses every PC.
        Assert.AreEqual(0, refused.Count, string.Join("; ", refused));
        Assert.AreEqual(185, speakers);
        Assert.AreEqual("-1000/2001/2001/30=1, -1001/1001/1001/20=1, 1000/1000/1000/2=4, 15/2001/355/2=1, 15/2001/510/2=1, 15/400/2001/2=4, "
            + "20/1001/1000/2=163, 20/1001/1000/8=1, 20/20/20/2=8, 25/150/2001/5=1", string.Join(", ", rows.Select(r => $"{r.Key}={r.Value}")));
    }
}
