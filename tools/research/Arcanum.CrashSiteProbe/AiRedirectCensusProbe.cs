using System.Text.Json;
using Arcanum.Data.Content;
using Arcanum.Data.Magic;

namespace Arcanum.CrashSiteProbe;

/// <summary>
/// ARC-WORLD-088 (NPC AI slice I8): which installed spells carry an AI redirect component, with its critter-flag mask and
/// <c>MinIQ:</c> threshold (the data question of P3). Reads the spell records as the original does (<see cref="SpellRecordMode.LikeOriginal"/>)
/// and prints spell numbers, actions and values only, no text.
/// </summary>
internal static partial class Program
{
    private static int ProbeAiRedirectCensus(string[] args)
    {
        if (args.Length is not (2 or 4) || args[0] != "--game-root" || (args.Length == 4 && args[2] != "--module"))
            throw new ArgumentException("AI_REDIRECT_USAGE --ai-redirect-census --game-root PATH [--module NAME]");
        var discovery = ContentMountPlan.Discover(args[1], args.Length == 4 ? args[3] : ContentMountPlan.DefaultModule);
        if (discovery.Status != ContentMountStatus.Ready) throw new InvalidDataException($"AI_REDIRECT_MOUNT_{discovery.Status}");
        var opened = ContentStack.Open(discovery.Plan!);
        if (opened.Status != ContentStackStatus.Ready) throw new InvalidDataException($"AI_REDIRECT_STACK_{opened.Status}");
        using var stack = opened.Stack!;
        var loaded = SpellData.Open(stack, SpellRecordMode.LikeOriginal);
        if (loaded.Status != SpellDataStatus.Read) throw new InvalidDataException($"AI_REDIRECT_SPELLS_{loaded.Status}");
        Console.WriteLine(JsonSerializer.Serialize(AiRedirectCensus(loaded.Data!.Records.Records), new JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }

    /// <summary>Every AI redirect component in record and line order, and the counts by threshold.</summary>
    internal static object AiRedirectCensus(IEnumerable<SpellRecord> records) =>
        AiRedirectCensus(records.SelectMany(r => r.Components.Where(c => c.Data is AiRedirectData).Select(c => (r.Number, c.Action, (AiRedirectData)c.Data!))));

    internal static object AiRedirectCensus(IEnumerable<(int Number, SpellAction Action, AiRedirectData Data)> components)
    {
        var rows = components.ToList();
        return new
        {
            redirect_components = rows.Count,
            spells = rows.Select(r => r.Number).Distinct().Count(),
            with_threshold = rows.Count(r => r.Data.MinimumIntelligence > 0),
            thresholds = rows.GroupBy(r => r.Data.MinimumIntelligence).OrderBy(g => g.Key).ToDictionary(g => g.Key.ToString(), g => g.Count()),
            components = rows.Select(r => new { spell = r.Number, action = r.Action.ToString(), critter_mask = $"0x{r.Data.CritterFlag:X}", min_iq = r.Data.MinimumIntelligence }).ToArray(),
        };
    }

    private static void AiRedirectCensusSelfTest()
    {
        static void Check(bool condition, string what)
        {
            if (!condition) throw new InvalidOperationException($"ai-redirect-census self-test failed: {what}");
        }
        var json = JsonSerializer.Serialize(AiRedirectCensus(Array.Empty<SpellRecord>()));
        Check(json.Contains("\"redirect_components\":0", StringComparison.Ordinal) && json.Contains("\"thresholds\":{}", StringComparison.Ordinal), "an empty table");
        var three = JsonSerializer.Serialize(AiRedirectCensus([(7, SpellAction.Begin, new AiRedirectData(0x8, -1)), (7, SpellAction.Maintain, new AiRedirectData(0x8, -1)),
            (9, SpellAction.Begin, new AiRedirectData(0, 12))]));
        Check(three.Contains("\"redirect_components\":3,\"spells\":2,\"with_threshold\":1", StringComparison.Ordinal), "the counts");
        Check(three.Contains("\"thresholds\":{\"-1\":2,\"12\":1}", StringComparison.Ordinal), "the thresholds by value");
        Check(three.Contains("\"critter_mask\":\"0x8\"", StringComparison.Ordinal) && three.Contains("\"action\":\"Maintain\"", StringComparison.Ordinal), "a component row");
        Console.WriteLine("ai-redirect-census self-test: PASS");
    }
}
