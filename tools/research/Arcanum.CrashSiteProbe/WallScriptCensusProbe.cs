using System.Text.Json;
using Arcanum.Data.Content;

namespace Arcanum.CrashSiteProbe;

// ARC-COMBAT-1973 research only: which attachment points of a wall (object type 0) carry a script in the installed module, so the hero's hit on a wall knows whether
// the weapon-hit and taking-damage points the original offers on it (ledger RH5) have anything to run. Placed sites and prototype defaults are counted apart. Only numbers leave the probe.
internal static partial class Program
{
    private const int WallObjectType = 0;

    /// <summary>The attachment hits of walls (object type 0) with a script.</summary>
    internal static IEnumerable<SaHit> WsWallHits(IEnumerable<SaHit> hits) => hits.Where(h => h.Type == WallObjectType && h.Script != 0);

    /// <summary>The weapon hit (7) and critical hit (34) attachments of weapon items (object type 5) with a script.</summary>
    internal static IEnumerable<SaHit> WsWeaponHitHits(IEnumerable<SaHit> hits) => hits.Where(h => h.Type == 5 && h.Sap is 7 or 34 && h.Script != 0);

    private static int ProbeWallScriptCensus(string[] args)
    {
        const string usage = "WALL_SCRIPT_USAGE --wall-script-census --game-root PATH [--module NAME]";
        if (args.Length is not (2 or 4) || args[0] != "--game-root" || args.Length == 4 && args[2] != "--module") throw new ArgumentException(usage);
        var discovery = ContentMountPlan.Discover(args[1], args.Length == 4 ? args[3] : ContentMountPlan.DefaultModule);
        if (discovery.Status != ContentMountStatus.Ready) throw new InvalidDataException("WALL_SCRIPT_MOUNT_" + discovery.Status);
        var opened = ContentStack.Open(discovery.Plan!);
        if (opened.Status != ContentStackStatus.Ready) throw new InvalidDataException("WALL_SCRIPT_STACK_" + opened.Status);
        using var stack = opened.Stack!;
        var keys = CensusKeys(stack);
        var refusals = new CensusTally();
        byte[]? Read(ContentCandidate candidate, string role)
        {
            var result = stack.Read(candidate);
            if (result.Status == ContentReadStatus.Read) return result.Bytes!;
            refusals.Add($"{role}|WALL_SCRIPT_READ_{result.Status}");
            return null;
        }
        var walk = SaWalk(keys, Read, s => s != 0, refusals);
        var hits = WsWallHits(walk.Hits).ToArray();
        var report = new
        {
            module = stack.Plan.ModuleName,
            scanned = new { maps = walk.Maps, sectors = walk.Sectors, mobiles = walk.Mobiles, aggregates = walk.Aggregates, terrain_templates = walk.Templates, prototypes = walk.Prototypes.Count },
            wall_script_sites = hits.Length,
            by_point_and_kind = hits.GroupBy(h => (h.Sap, h.Kind)).OrderBy(g => g.Key.Sap).ThenBy(g => g.Key.Kind, StringComparer.Ordinal)
                .Select(g => new { point = g.Key.Sap, kind = g.Key.Kind, sites = g.Count(), scripts = g.Select(h => h.Script).Distinct().Order().ToArray() }).ToArray(),
            // The weapon's hit (7) and critical hit (34) scripts, the other scripts a hit on a wall offers (item type 5).
            weapon_hit_sites = WsWeaponHitHits(walk.Hits).GroupBy(h => (h.Sap, h.Kind)).OrderBy(g => g.Key.Sap).ThenBy(g => g.Key.Kind, StringComparer.Ordinal)
                .Select(g => new { point = g.Key.Sap, kind = g.Key.Kind, sites = g.Count(), scripts = g.Select(h => h.Script).Distinct().Order().ToArray() }).ToArray(),
            refusals = refusals.Snapshot(),
        };
        Console.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }

    private static void WallScriptCensusSelfTest()
    {
        var hits = new[]
        {
            new SaHit("m", "object", "f", 0, 0, null, 7, 11, null, null, null, null), new SaHit("m", "object", "f", 0, 1, null, 7, 12, null, null, null, null),
            new SaHit("m", "prototype", "f", 0, 0, null, 23, 13, null, null, null, null), new SaHit("m", "object", "f", 0, 0, null, 7, 0, null, null, null, null),
        };
        if (!WsWallHits(hits).Select(h => h.Script).SequenceEqual([11, 13])) throw new InvalidOperationException("wall-script-census self-test failed: only wall sites with a script count");
        Console.WriteLine("wall-script-census self-test: PASS (wall sites with a script only)");
    }
}
