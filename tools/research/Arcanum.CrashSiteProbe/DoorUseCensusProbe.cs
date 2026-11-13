using System.Text.Json;
using Arcanum.Data.Content;
using Arcanum.Data.Scripts;

namespace Arcanum.CrashSiteProbe;

// ARC-WORLD-1805 research only: which portal (door or window, object type 1) use scripts (attachment point 1) of the installed module can ask for a conversation
// (script action 4, start dialog) or a teleport (44, 102), so the use node 0x428A10 would have something to start. It decodes and follows calls like the other script
// censuses (the same live set, CsLiveSet), it never executes. Placed objects and prototype defaults are counted apart: a placed site is an object on a map that holds the
// script, a prototype default is the script an object inherits when it carries none of its own. Only numbers leave the probe (no names, no text, no payload).
internal static partial class Program
{
    private const int DoorUsePoint = 1, DoorUsePortalType = 1;

    /// <summary>The use sites of portals among the attachment hits.</summary>
    internal static IEnumerable<SaHit> DuPortalUseHits(IEnumerable<SaHit> hits) => hits.Where(h => h.Sap == DoorUsePoint && h.Type == DoorUsePortalType && h.Script != 0);

    /// <summary>For one root script (run from line 0, calls followed): how many reached clauses start a dialog, teleport or fade and teleport.</summary>
    internal static (int Dialog, int Teleport, int FadeTeleport) DuReached(IReadOnlyDictionary<int, WorldScript> scripts, int root)
    {
        var live = CsLiveSet(scripts, [root]);
        int dialog = 0, teleport = 0, fade = 0;
        foreach (var (number, reach) in live.Reach)
        {
            var script = scripts[number];
            for (var line = 0; line < script.Lines.Count; line++)
                foreach (var (taken, clause) in new[] { (reach.True[line], script.Lines[line].True), (reach.False[line], script.Lines[line].False) })
                {
                    if (!taken) continue;
                    if (clause.Type == SatDialog) dialog++;
                    else if (clause.Type == SatTeleport) teleport++;
                    else if (clause.Type == SatFadeTeleport) fade++;
                }
        }
        return (dialog, teleport, fade);
    }

    /// <summary>A clause as numbers: its type, then each operand as kind:value.</summary>
    internal static string DuClause(WorldClause clause) => clause.Type + "(" + string.Join(",", clause.Values.Select((v, i) => clause.Kinds[i] + ":" + v)) + ")";

    private static int ProbeDoorUseCensus(string[] args)
    {
        const string usage = "DOOR_USE_USAGE --door-use-census --game-root PATH [--module NAME]";
        if (args.Length is not (2 or 4) || args[0] != "--game-root" || args.Length == 4 && args[2] != "--module") throw new ArgumentException(usage);
        var discovery = ContentMountPlan.Discover(args[1], args.Length == 4 ? args[3] : ContentMountPlan.DefaultModule);
        if (discovery.Status != ContentMountStatus.Ready) throw new InvalidDataException("DOOR_USE_MOUNT_" + discovery.Status);
        var opened = ContentStack.Open(discovery.Plan!);
        if (opened.Status != ContentStackStatus.Ready) throw new InvalidDataException("DOOR_USE_STACK_" + opened.Status);
        using var stack = opened.Stack!;
        var keys = CensusKeys(stack);
        var refusals = new CensusTally();
        byte[]? Read(ContentCandidate candidate, string role)
        {
            var result = stack.Read(candidate);
            if (result.Status == ContentReadStatus.Read) return result.Bytes!;
            refusals.Add($"{role}|DOOR_USE_READ_{result.Status}");
            return null;
        }
        var byNumber = keys.Keys.Where(k => k.StartsWith("scr/", StringComparison.Ordinal) && k.Count(c => c == '/') == 1 && k.EndsWith(".scr", StringComparison.Ordinal))
            .Select(k => (Key: k, Number: ScriptNumberFromFile(k[4..]))).Where(p => p.Number is > 0)
            .GroupBy(p => p.Number!.Value).ToDictionary(g => g.Key, g => g.Select(p => p.Key).Order(StringComparer.Ordinal).Last());
        var scripts = new SortedDictionary<int, WorldScript>(); var decodeFailures = new CensusTally();
        foreach (var (number, key) in byNumber)
        {
            if (Read(keys[key], "scr") is not { } bytes) { decodeFailures.Add("read"); continue; }
            try { scripts[number] = DecodeWorldScript(bytes); } catch (InvalidDataException e) { decodeFailures.Add(e.Message); }
        }
        var walk = SaWalk(keys, Read, s => s != 0, refusals);
        var hits = DuPortalUseHits(walk.Hits).ToArray();
        var kinds = hits.GroupBy(h => h.Kind).OrderBy(g => g.Key, StringComparer.Ordinal).ToDictionary(g => g.Key, g => new { sites = g.Count(), scripts = g.Select(h => h.Script).Distinct().Count() });
        var rows = hits.Select(h => h.Script).Distinct().Order().Where(scripts.ContainsKey).Select(s => (Script: s, Reached: DuReached(scripts, s))).ToArray();
        var asking = rows.Where(r => r.Reached.Dialog + r.Reached.Teleport + r.Reached.FadeTeleport > 0).ToArray();
        var askingSet = asking.Select(r => r.Script).ToHashSet();
        var report = new
        {
            module = stack.Plan.ModuleName, scripts = scripts.Count, script_decode_failures = decodeFailures.Snapshot(),
            scanned = new { maps = walk.Maps, sectors = walk.Sectors, mobiles = walk.Mobiles, aggregates = walk.Aggregates, terrain_templates = walk.Templates, prototypes = walk.Prototypes.Count },
            portal_use_sites = hits.Length, portal_use_scripts = hits.Select(h => h.Script).Distinct().Count(), missing_script_files = hits.Select(h => h.Script).Distinct().Count(s => !scripts.ContainsKey(s)),
            by_kind = kinds,
            scripts_asking = asking.Length, sites_asking = hits.Count(h => askingSet.Contains(h.Script)),
            asking = asking.Select(r => new
            {
                script = r.Script, dialog = r.Reached.Dialog, teleport = r.Reached.Teleport, fade_teleport = r.Reached.FadeTeleport,
                sites = hits.Where(h => h.Script == r.Script).GroupBy(h => h.Kind).OrderBy(g => g.Key, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count()),
                maps = hits.Where(h => h.Script == r.Script && h.Map.Length > 0).Select(h => h.Map).Distinct().Order(StringComparer.Ordinal).ToArray(),
                lines = scripts[r.Script].Lines.Select(l => DuClause(l.Condition) + " ? " + DuClause(l.True) + " : " + DuClause(l.False)).ToArray(),
            }).ToArray(),
            refusals = refusals.Snapshot(),
        };
        Console.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }

    private static void DoorUseCensusSelfTest()
    {
        static void Check(bool condition, string label) { if (!condition) throw new InvalidOperationException("door-use-census self-test failed: " + label); }
        static byte[] Script(params (int Cond, int[] CondOps, int Yes, int[] YesOps, int No, int[] NoOps)[] lines)
        {
            var bytes = new byte[WorldScriptHeaderBytes + lines.Length * WorldScriptLineBytes];
            System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(52), lines.Length);
            System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(56), lines.Length);
            for (var i = 0; i < lines.Length; i++)
            {
                var at = WorldScriptHeaderBytes + i * WorldScriptLineBytes;
                foreach (var (offset, type, ops) in new[] { (0, lines[i].Cond, lines[i].CondOps), (44, lines[i].Yes, lines[i].YesOps), (88, lines[i].No, lines[i].NoOps) })
                {
                    System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(at + offset), type);
                    for (var k = 0; k < ops.Length; k++) { bytes[at + offset + 4 + k] = LiteralKind; System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(at + offset + 12 + 4 * k), ops[k]); }
                }
            }
            return bytes;
        }
        // A plain door script, one that starts a dialog, one that teleports behind a call, one whose teleport line is never reached (it returns first).
        var scripts = new Dictionary<int, WorldScript>
        {
            [1] = DecodeWorldScript(Script((0, [], SatReturnRun, [], 0, []))),
            [2] = DecodeWorldScript(Script((0, [], SatDialog, [5], 0, []), (0, [], SatReturnRun, [], 0, []))),
            [3] = DecodeWorldScript(Script((0, [], SatCall, [4, 0], 0, []), (0, [], SatReturnRun, [], 0, []))),
            [4] = DecodeWorldScript(Script((0, [], SatFadeTeleport, [1, 2, 3, 4, 5, 6], 0, []), (0, [], SatReturnRun, [], 0, []))),
            [5] = DecodeWorldScript(Script((0, [], SatReturnRun, [], 0, []), (0, [], SatTeleport, [1, 2, 3], 0, []))),
        };
        Check(DuReached(scripts, 1) == (0, 0, 0), "a plain door script asks for nothing");
        Check(DuReached(scripts, 2) == (1, 0, 0), "a dialog action is counted");
        Check(DuReached(scripts, 3) == (0, 0, 1), "a fade-and-teleport behind a call is counted");
        Check(DuReached(scripts, 5) == (0, 0, 0), "a teleport after a return is not reached");
        var hits = new[]
        {
            new SaHit("m", "object", "f", 0, 1, null, 1, 7, null, null, null, null), new SaHit("m", "object", "f", 0, 2, null, 1, 8, null, null, null, null),
            new SaHit("m", "object", "f", 0, 1, null, 3, 9, null, null, null, null), new SaHit("m", "prototype", "f", 0, 1, null, 1, 10, null, null, null, null),
            new SaHit("m", "object", "f", 0, 1, null, 1, 0, null, null, null, null),
        };
        Check(DuPortalUseHits(hits).Select(h => h.Script).SequenceEqual([7, 10]), "only use-point scripts of portals count");
        Console.WriteLine("door-use-census self-test: PASS (reached dialog and teleports through calls, unreached lines apart, portal use hits)");
    }
}
