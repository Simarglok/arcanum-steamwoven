using System.Text.Json;
using Arcanum.Data.Combat;
using Arcanum.Data.Content;
using Arcanum.Data.Maps;

namespace Arcanum.CrashSiteProbe;

// ARC-COMBAT-009: read-only census of the data the original's critical-effect procedures read (0x4B5F40, 0x4B6410): every
// critter's critical-hit body type (field 227, slot 0x3E of the critter block) and every weapon's critical-hit and
// critical-miss charts, over the module content stack, for prototypes and for every map's mobiles by their effective fields.
// Aggregates only: counts by value; never text or payload bytes.
internal static partial class Program
{
    // Critter flags the critical-hit procedure tests: undead skips stun, knockout and the location effects; fatigue-immune
    // skips the knockout.
    private const uint CrUndead = 0x4, CrFatigueImmune = 0x4000000;

    private static int ProbeCriticalCensus(string[] args)
    {
        if (args.Length is not (2 or 4) || args[0] != "--game-root" || (args.Length == 4 && args[2] != "--module"))
            throw new ArgumentException("CRITICAL_CENSUS_USAGE --critical-census --game-root PATH [--module NAME]");
        var discovery = ContentMountPlan.Discover(args[1], args.Length == 4 ? args[3] : ContentMountPlan.DefaultModule);
        if (discovery.Status != ContentMountStatus.Ready) throw new InvalidDataException($"CRITICAL_CENSUS_MOUNT_{discovery.Status}");
        var opened = ContentStack.Open(discovery.Plan!);
        if (opened.Status != ContentStackStatus.Ready) throw new InvalidDataException($"CRITICAL_CENSUS_STACK_{opened.Status}");
        using var stack = opened.Stack!;
        var catalog = WorldCatalog.Open(stack);
        var prototypes = new Dictionary<int, WorldObject>();
        var prototypeFields = new List<CombatFields>();
        var refused = new CensusTally();
        foreach (var number in catalog.PrototypeNumbers.Order())
        {
            var read = catalog.ReadPrototype(number);
            if (read.Prototype is null) { refused.Add($"prototype|{read.Issue?.Code}"); continue; }
            prototypes[number] = read.Prototype;
            var fields = CombatFieldReader.ReadPrototype(catalog.Prototypes.Read, number);
            if (fields.Fields is not null) prototypeFields.Add(fields.Fields);
            else refused.Add($"prototype_fields|{fields.Failure}");
        }
        var instanceFields = new List<CombatFields>();
        foreach (var map in catalog.Maps)
        {
            var mobiles = catalog.ReadMobiles(map);
            for (var i = 0; i < mobiles.Objects.Count; i++)
            {
                var o = mobiles.Objects[i];
                if (o.Type is not (CombatFieldReader.WeaponType or CombatFieldReader.PcType or CombatFieldReader.NpcType)) continue;
                if (o.PrototypeNumber is not { } p || !prototypes.TryGetValue(p, out var prototype)) { refused.Add("instance|missing_prototype"); continue; }
                var read = CombatFieldReader.Read(CcEffective(o, prototype, i));
                if (read.Fields is not null) instanceFields.Add(read.Fields);
                else refused.Add($"instance|{read.Failure}");
            }
        }
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            prototypes = CriticalCensus(prototypeFields),
            instances = CriticalCensus(instanceFields),
            refused = refused.Snapshot(),
        }, new JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }

    /// <summary>Counts by critical-hit body type (PCs and NPCs apart, with how many NPCs of each are undead or fatigue-immune) and
    /// by weapon chart.</summary>
    internal static object CriticalCensus(IReadOnlyCollection<CombatFields> fields)
    {
        static SortedDictionary<int, int> By<T>(IEnumerable<T> rows, Func<T, int> key)
        {
            var counts = new SortedDictionary<int, int>();
            foreach (var row in rows) counts[key(row)] = counts.GetValueOrDefault(key(row)) + 1;
            return counts;
        }
        var critters = fields.Where(f => f.Critter is not null).ToArray();
        var npcs = critters.Where(f => f.Object.Type == CombatFieldReader.NpcType).ToArray();
        var weapons = fields.Where(f => f.Weapon is not null).Select(f => f.Weapon!).ToArray();
        return new
        {
            pc_body_types = By(critters.Where(f => f.Object.Type == CombatFieldReader.PcType), f => f.Critter!.CriticalHitChart),
            npc_body_types = By(npcs, f => f.Critter!.CriticalHitChart),
            npc_undead_by_body_type = By(npcs.Where(f => (f.Critter!.Flags & CrUndead) != 0), f => f.Critter!.CriticalHitChart),
            npc_fatigue_immune_by_body_type = By(npcs.Where(f => (f.Critter!.Flags & CrFatigueImmune) != 0), f => f.Critter!.CriticalHitChart),
            weapon_hit_charts = By(weapons, w => w.CriticalHitChart),
            weapon_miss_charts = By(weapons, w => w.CriticalMissChart),
        };
    }

    private static void CriticalCensusSelfTest()
    {
        static void Check(bool condition, string what) { if (!condition) throw new InvalidOperationException($"critical-census self-test failed: {what}"); }
        var report = JsonSerializer.SerializeToElement(CriticalCensus([]));
        Check(report.GetProperty("npc_body_types").EnumerateObject().Count() == 0, "an empty census counts nothing");
        Check(report.EnumerateObject().Select(p => p.Name).SequenceEqual(["pc_body_types", "npc_body_types", "npc_undead_by_body_type",
            "npc_fatigue_immune_by_body_type", "weapon_hit_charts", "weapon_miss_charts"]), "the report keys");
        Console.WriteLine("critical-census self-test: PASS");
    }
}
