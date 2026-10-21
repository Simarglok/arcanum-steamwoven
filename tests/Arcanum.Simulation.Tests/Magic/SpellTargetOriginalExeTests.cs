using System.Reflection;
using System.Text.Json;
using Arcanum.Simulation.Magic;
using Arcanum.Simulation.Tests.Character;
using Arcanum.Tests.Shared;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Simulation.Tests.Magic.SpellResistanceOriginalExeTests;

namespace Arcanum.Simulation.Tests.Magic;

/// <summary>
/// ARC-MAGIC-007: the area test (<see cref="SpellTargetRules.Test"/>, 0x4F2D20) against the retail 1.0.7.4 executable on the
/// function oracle's seeded inputs (<c>tools/original-oracle</c>, model <c>spell-area-test</c>): one context (mask, flags,
/// radius; candidate, self, source, other; locations and tiles) with the candidate's facts, the leaders, the candidate tile's
/// objects (a list the stub writes), the blocked flag, the path and the sector kinds. The Original setting must match on every
/// input, the failure reason included; the Steamwoven setting may differ only where MG25's summoned test meets another flags
/// word. Opt-in like <see cref="ExperienceOriginalExeTests"/>.
/// </summary>
[TestClass]
public sealed class SpellTargetOriginalExeTests
{
    public const string Category = ExperienceOriginalExeTests.Category;
    private const int Untouched = 0x7FFF0001;

    public TestContext TestContext { get; set; } = null!;

    private sealed record Mutant(string Name, Func<Row, string> Compute);

    [TestMethod, TestCategory(Category)]
    public void AreaTestMatchesTheOriginal()
    {
        var file = OracleFile.Load("4F2D20", "spell-area-test", TestContext);
        string Real(Row c, MagicAnomalyProfile profile) => Describe(SpellTargetRules.Test(Context(c), new World(c), profile));
        string Original(Row c) => $"{c["result"]} {c["failedLow"]} {c["failedHigh"]}";
        bool Mg25(Row c) => (c["hi"] & 0x4000) != 0 && c["candidate"] != 0 && ((c["lo"] & 0x40) != 0 || (c["lo"] & 0x40000) != 0 || (c["hi"] & 0x180) != 0);
        var mutants = new Mutant[]
        {
            new("the not-dead test needs the critter bit", c => Describe(SpellTargetRules.Test(Context(c), new World(c), MagicAnomalyProfile.Original), noDeadOnlyForCritters: true, c)),
            new("a mask without object and tile bits fails", c => (c["lo"] & 0x15) == 0 && (c["hi"] & 0x8000) == 0 && (c["lo"] | c["hi"]) != 0
                ? $"0 {Untouched} {Untouched}" : Real(c, MagicAnomalyProfile.Original)),
            new("the radius is strict", c => Describe(SpellTargetRules.Test(Context(c) with { Area = Context(c).Area with { Radius = Context(c).Area.Radius - 1 } }, new World(c), MagicAnomalyProfile.Original))),
            new("the summoned test reads the spell flags", c => Real(c, MagicAnomalyProfile.Original with { SummonedTestReadsLastFlags = false })),
        };
        Run(file, c => Real(c, MagicAnomalyProfile.Original), Original, ("steamwoven", "MG25", Mg25, c => Real(c, MagicAnomalyProfile.Steamwoven)), mutants);
    }

    [TestMethod, TestCategory(Category)]
    public void CollectionMatchesTheOriginal()
    {
        var file = OracleFile.Load("4F40B0", "spell-target-collection", TestContext);
        string Real(Row c, MagicAnomalyProfile profile) => Digest(c, SpellTargetRules.Collect(CollectContext(c), new CollectWorld(c), profile));
        string Original(Row c) => $"{c["entries"]} {c["digest"]} {c["built"]} {c["builtDigest"]}";
        bool Mg25(Row c) => (c["hi"] & 0x4000) != 0 && ((c["lo"] & 0x40) != 0 || (c["lo"] & 0x40000) != 0 || (c["hi"] & 0x180) != 0);
        Run(file, c => Real(c, MagicAnomalyProfile.Original), Original, ("steamwoven", "MG25", Mg25, c => Real(c, MagicAnomalyProfile.Steamwoven)),
            new Mutant("the count is not used", c => Collected(c, MagicAnomalyProfile.Original, area => area with { Count = -1 })),
            new("the radius is one smaller", c => Collected(c, MagicAnomalyProfile.Original, area => area with { Radius = area.Radius - 1 })),
            new("no-self keeps the self", c => Collected(c, MagicAnomalyProfile.Original, area => area with { Targets = area.Targets & ~0x10000000UL })),
            new("the built list keeps the gathered order", c => Collected(c, MagicAnomalyProfile.Original, null, reverseBuilt: true)));
    }

    private static string Collected(Row c, MagicAnomalyProfile profile, Func<SpellTargetArea, SpellTargetArea>? change, bool reverseBuilt = false)
    {
        var context = CollectContext(c);
        if (change is not null) context = context with { Area = change(context.Area) };
        var result = SpellTargetRules.Collect(context, new CollectWorld(c), profile);
        if (reverseBuilt && result.BuiltRunList is { } built) result = result with { BuiltRunList = built.Reverse().ToArray() };
        return Digest(c, result);
    }

    private const int WorldObjects = 8;

    private static string Id(int index) => index < WorldObjects ? $"w{index}" : $"pc{index}";

    private static SpellTargetObject WorldObject(Row c, int i) => new(Id(i), c[$"o{i}Type"], new(unchecked((uint)c[$"o{i}X"]), unchecked((uint)c[$"o{i}Y"])),
        c[$"o{i}ObjectFlags"], c[$"o{i}SpellFlags"], c[$"o{i}CritterFlags"], c[$"o{i}CritterFlags2"], c[$"o{i}HitPoints"], c[$"o{i}Damage"],
        c[$"o{i}Alignment"], c[$"o{i}Poison"], c[$"o{i}Material"], c[$"o{i}Weight"], c[$"o{i}PortalFlags"], c[$"o{i}ContainerFlags"],
        c[$"o{i}Leader"] < 0 ? null : Id(c[$"o{i}Leader"]), c[$"o{i}Occupant"] < 0 ? null : Id(c[$"o{i}Occupant"]),
        c[$"o{i}Owner"] < 0 ? null : Id(c[$"o{i}Owner"]));

    private static SpellTargetContext CollectContext(Row c)
    {
        SpellTargetObject? RoleObject(string role) => c[role] < 0 ? null : WorldObject(c, c[role]);
        IReadOnlyList<SpellTargetObject>? Stored(string count, string prefix) =>
            c[count] < 0 ? null : Enumerable.Range(0, c[count]).Select(k => WorldObject(c, c[$"{prefix}{k}"])).ToArray();
        var area = new SpellTargetArea(unchecked((uint)c["lo"]) | (ulong)unchecked((uint)c["hi"]) << 32, unchecked((uint)c["required"]),
            unchecked((uint)c["forbidden"]), c["radius"], c["count"]);
        return new(area, RoleObject("self"), RoleObject("source"), At(c, "originX", "originY"), RoleObject("candidate"), default, RoleObject("target"),
            At(c, "targetTileX", "targetTileY"), RoleObject("other"), RoleObject("summoned"), Stored("summonedList", "summoned"), Stored("runList", "run"));
    }

    // The oracle's record: the entry count, an FNV-1a digest of the entries (an object's world index; −1, x, y for a tile) and
    // of the built run list (world indices in list order).
    private static string Digest(Row c, SpellTargetCollection collection)
    {
        static int Index(SpellTargetObject o) => int.Parse(o.Id.AsSpan(1));
        var entries = collection.Entries.SelectMany(e => e.Object is { } o ? new[] { Index(o) } : new[] { -1, (int)e.Tile.X, (int)e.Tile.Y }).ToList();
        var built = collection.BuiltRunList?.Select(Index).ToList() ?? [];
        return $"{collection.Entries.Count} {Fnv(entries)} {built.Count} {Fnv(built)}";
    }

    private static int Fnv(IEnumerable<int> values)
    {
        var hash = 0x811C9DC5u;
        foreach (var value in values)
            foreach (var b in BitConverter.GetBytes(value)) hash = (hash ^ b) * 0x01000193u;
        return unchecked((int)hash);
    }

    // The oracle's world: tile answers from the tile alone, queries in world order.
    private static (bool Blocked, bool Path, int Kind) TileFacts(SpellLocation tile) =>
        ((tile.X * 7L + tile.Y * 3L) % 5 == 0, (tile.X + (long)tile.Y) % 3 != 0, (int)((tile.X ^ tile.Y) & 1));

    private sealed class CollectWorld(Row c) : ISpellTargetWorld
    {
        private IEnumerable<SpellTargetObject> All() => Enumerable.Range(0, WorldObjects).Select(i => WorldObject(c, i));

        public SpellTargetObject? Object(string id) => int.TryParse(id.AsSpan(1), out var i) && id[0] == 'w' ? WorldObject(c, i) : null;

        public IReadOnlyList<SpellTargetObject> ObjectsInSquare(SpellLocation centre, int radius, int typeMask) =>
            All().Where(o => Math.Abs((long)o.Location.X - centre.X) <= radius && Math.Abs((long)o.Location.Y - centre.Y) <= radius
                && (typeMask & (1 << o.Type)) != 0 && (long)o.Location.X >= (long)centre.X - radius && (long)o.Location.Y >= (long)centre.Y - radius).ToArray();

        public IReadOnlyList<SpellTargetObject> ObjectsAt(SpellLocation tile, int typeMask) =>
            All().Where(o => o.Location == tile && (typeMask & (1 << o.Type)) != 0).ToArray();

        public bool TileBlocked(SpellLocation tile) => TileFacts(tile).Blocked;

        public bool PathExists(SpellTargetObject source, SpellLocation tile) => TileFacts(tile).Path;

        public int SectorKind(SpellLocation location) => TileFacts(location).Kind;

        public int Direction(SpellLocation from, SpellLocation to) => c["direction"];

        public IReadOnlyList<SpellTargetObject> Party(SpellTargetObject source) =>
            Enumerable.Range(0, c["party"]).Select(k => WorldObject(c, c[$"party{k}"])).ToArray();
    }

    private static readonly string[] RunRoles = ["parent", "source", "runTarget", "summoned", "other"];

    private static SpellTargetObject? RunRole(Row c, int index) => index < 0 || c[$"{RunRoles[index]}Present"] == 0 ? null
        : new(RunRoles[index], c[$"{RunRoles[index]}Type"], At(c, $"{RunRoles[index]}X", $"{RunRoles[index]}Y"));

    private static int RunIndex(string? id) => id is null ? -1 : Array.IndexOf(RunRoles, id);

    [TestMethod, TestCategory(Category)]
    public void ReAimMatchesTheOriginal()
    {
        var file = OracleFile.Load("453D40", "spell-reaim", TestContext);
        string ReAim(Row c, Func<ulong, ulong>? change = null, bool offscreenAlways = false)
        {
            var apply = unchecked((uint)c["applyLow"]) | (ulong)unchecked((uint)c["applyHigh"]) << 32;
            if (change is not null) apply = change(apply);
            var (target, tile) = SpellTargetRules.ReAim(apply, RunRole(c, c["current"]), At(c, "tileX", "tileY"), RunRole(c, 0), RunRole(c, 1), RunRole(c, 3),
                At(c, "runTileX", "runTileY"), RunRole(c, 2));
            if (offscreenAlways && (apply >> 32 & 0x8000) != 0 && target is not null) tile = target.Location;
            return $"{RunIndex(target?.Id)} {(int)tile.X} {(int)tile.Y}";
        }
        Run(file, c => ReAim(c), c => $"{c["targetAfter"]} {c["tileAfterX"]} {c["tileAfterY"]}",
            new Mutant("source before self", c => ReAim(c, a => (a & 3) == 3 ? a & ~2UL : a)),
            new("the tile follows the target", c => ReAim(c, null, offscreenAlways: true)),
            new("no summoned re-aim", c => ReAim(c, a => a & ~(0x4000UL << 32))));
    }

    [TestMethod, TestCategory(Category)]
    public void AiNoticeMatchesTheOriginal()
    {
        var file = OracleFile.Load("453EE0", "spell-ai-notice", TestContext);
        string Notice(Row c, bool everyAction = false, bool always = false)
        {
            var target = RunRole(c, c["current"]);
            var last = c["last"] < 0 ? null : RunRoles[c["last"]];
            var (notices, remembered) = SpellTargetRules.AiNotices(c["aggressive"] != 0, everyAction ? 0 : c["action"], target?.Id, RunRole(c, 0)?.Id,
                RunRole(c, 4)?.Id, always ? null : last);
            var flat = notices.SelectMany(n => new[] { RunIndex(n.Attacker), RunIndex(n.Target) }).Concat(Enumerable.Repeat(-2, 4 - 2 * notices.Count));
            return $"{notices.Count} {string.Join(' ', flat)} {RunIndex(remembered)}";
        }
        Run(file, c => Notice(c), c => $"{c["notices"]} {c["attacker0"]} {c["victim0"]} {c["attacker1"]} {c["victim1"]} {c["lastAfter"]}",
            new Mutant("END is told too", c => Notice(c, everyAction: true)),
            new("no deduplication", c => Notice(c, always: true)));
    }

    private void Run(OracleFile file, Func<Row, string> real, Func<Row, string> original, params Mutant[] mutants) =>
        Run(file, real, original, ("steamwoven", NoSwitch, _ => false, real), mutants);

    // No rule-mode switch touches the function: Steamwoven is the Original setting (the receipt has no other mode).
    private const string NoSwitch = "none";

    private static string Describe(SpellTargetTest test) => Describe(test, false, null);

    // The answer as the oracle records it: result, then the failure-bit global (low, high) or the untouched marker.
    private static string Describe(SpellTargetTest test, bool noDeadOnlyForCritters, Row? c)
    {
        var passes = test.Passes;
        // A planted variant: the not-dead bit ignored without the critter bit (the reading the data census assumed for MG18).
        if (noDeadOnlyForCritters && c is not null && !passes && (c["lo"] & 0x40) == 0 && (c["lo"] & unchecked((int)0x80000000)) != 0 && c["hitPoints"] <= 0)
            passes = true;
        var (low, high) = test.Reason switch
        {
            0 => (Untouched, Untouched),
            var r => (unchecked((int)(uint)r), unchecked((int)(uint)(r >> 32))),
        };
        return $"{(passes ? 1 : 0)} {low} {high}";
    }

    private static SpellLocation At(Row c, string x, string y) => new(unchecked((uint)c[x]), unchecked((uint)c[y]));

    private static SpellTargetObject? Role(Row c, string role) => role switch
    {
        "self" => c["self"] == 0 ? null : c["candidate"] == 2 ? Candidate(c, "self") : new("self", 16, SpellLocation.None, PcLeader: Leader(c, c["selfLeader"])),
        "source" => c["source"] == 0 ? null : c["candidate"] == 3 ? Candidate(c, "source") : new("source", 16, At(c, "sourceX", "sourceY")),
        "other" => c["other"] == 0 ? null : new("other", 16, SpellLocation.None, PcLeader: Leader(c, c["otherLeader"])),
        _ => throw new ArgumentOutOfRangeException(nameof(role)),
    };

    private static string? Leader(Row c, int code) => code switch
    {
        0 => null,
        1 => c["self"] != 0 ? Id(c, "self") : null,
        2 => c["other"] != 0 ? "other" : null,
        3 => "pc-a",
        4 => "pc-b",
        5 => CandidateId(c),
        _ => throw new ArgumentOutOfRangeException(nameof(code)),
    };

    private static string Id(Row c, string role) => role;

    private static string? CandidateId(Row c) => c["candidate"] switch
    {
        1 => "candidate",
        2 => c["self"] != 0 ? "self" : null,
        3 => c["source"] != 0 ? "source" : null,
        _ => null,
    };

    // The oracle answers the candidate's handle with the candidate's facts and location, also when it is the self or the source.
    private static SpellTargetObject Candidate(Row c, string id) => new(id, c["type"], At(c, "candidateX", "candidateY"),
        c["objectFlags"], c["spellFlags"], c["critterFlags"], c["critterFlags2"], c["hitPoints"], c["damage"], c["alignment"], c["poison"],
        c["material"], c["weight"], c["portalFlags"], c["containerFlags"], Leader(c, c["leader"]));

    private static SpellTargetContext Context(Row c)
    {
        var self = Role(c, "self");
        var source = Role(c, "source");
        var candidate = c["candidate"] switch { 1 => Candidate(c, "candidate"), 2 => self, 3 => source, _ => null };
        var area = new SpellTargetArea(unchecked((uint)c["lo"]) | (ulong)unchecked((uint)c["hi"]) << 32, unchecked((uint)c["required"]),
            unchecked((uint)c["forbidden"]), c["radius"]);
        return new(area, c["candidate"] == 2 && self is not null ? candidate : self, c["candidate"] == 3 && source is not null ? candidate : source,
            At(c, "originX", "originY"), candidate, At(c, "tileX", "tileY"), null, At(c, "targetTileX", "targetTileY"), Role(c, "other"));
    }

    private sealed class World(Row c) : ISpellTargetWorld
    {
        public SpellTargetObject? Object(string id) => throw new AssertFailedException("the test asks no object by id");

        public IReadOnlyList<SpellTargetObject> ObjectsInSquare(SpellLocation centre, int radius, int typeMask) => throw new AssertFailedException("no square query");

        public IReadOnlyList<SpellTargetObject> ObjectsAt(SpellLocation tile, int typeMask)
        {
            Assert.AreEqual(At(c, "tileX", "tileY"), tile);
            return Enumerable.Range(0, c["tileObjects"]).Where(i => (typeMask & (1 << c[$"object{i}Type"])) != 0)
                .Select(i => new SpellTargetObject($"tile-{i}", c[$"object{i}Type"], tile, c[$"object{i}Flags"])).ToArray();
        }

        public bool TileBlocked(SpellLocation tile) => c["blocked"] != 0;

        public bool PathExists(SpellTargetObject source, SpellLocation tile) => c["path"] != 0;

        public int SectorKind(SpellLocation location) => location == At(c, "originX", "originY") ? c["originKind"] : c["tileKind"];

        public int Direction(SpellLocation from, SpellLocation to) => throw new AssertFailedException("no direction");

        public IReadOnlyList<SpellTargetObject> Party(SpellTargetObject source) => throw new AssertFailedException("no party");
    }

    private void Run(OracleFile file, Func<Row, string> real, Func<Row, string> original,
        (string Mode, string Ledger, Func<Row, bool> Applies, Func<Row, string> Compute) other, params Mutant[] mutants)
    {
        Assert.IsTrue(mutants.Length >= 2, "at least two planted mutants per function");
        var unexpected = file.Cases.Where(c => real(c) != original(c)).ToList();
        var caught = mutants.Where(m => file.Cases.Any(c => m.Compute(c) != original(c))).Select(m => m.Name).ToList();
        var missed = mutants.Select(m => m.Name).Except(caught).ToList();
        var ledgerInputs = file.Cases.Count(other.Applies);
        var otherDifferences = file.Cases.Where(c => other.Compute(c) != original(c)).ToList();
        var outsideLedger = otherDifferences.Where(c => !other.Applies(c)).ToList();
        var pass = unexpected.Count == 0 && missed.Count == 0 && outsideLedger.Count == 0;
        var method = ExperienceOriginalExeTests.OriginalMethod(file.Key);
        TestContext.WriteLine($"oracle {file.Function} {file.Name} ({MethodIlHash.Name(method)}): seed {file.Seed}, {file.Cases.Count} inputs, exe {file.ExeSha256}");
        TestContext.WriteLine($"  {(file.Cases[0].Has("result") ? $"passes: {file.Cases.Count(c => c["result"] != 0)}; " : "")}differences: {unexpected.Count} (original mode); {other.Mode} mode: "
            + $"{otherDifferences.Count} differences among the {ledgerInputs} {other.Ledger} inputs, {outsideLedger.Count} outside them");
        TestContext.WriteLine($"  mutants caught: {caught.Count}/{mutants.Length}");
        WriteReceipt(file, method, other.Mode, other.Ledger, ledgerInputs, otherDifferences.Count, outsideLedger.Count, mutants.Length, caught.Count, unexpected.Count, pass);
        if (unexpected.Count > 0)
            Assert.Fail($"{unexpected.Count} differences from the original; first: {unexpected[0]} original {original(unexpected[0])}, Steamwoven (original mode) {real(unexpected[0])}");
        if (outsideLedger.Count > 0)
            Assert.Fail($"the {other.Mode} mode differs outside {other.Ledger}: {outsideLedger[0]} original {original(outsideLedger[0])}");
        Assert.AreEqual(0, missed.Count, "planted mutants not caught (the inputs do not cover them): " + string.Join(", ", missed));
    }

    /// <summary>The oracle receipt (docs/verification/receipts), in the format of <see cref="ExperienceOriginalExeTests"/>.</summary>
    private void WriteReceipt(OracleFile file, MethodInfo method, string mode, string ledger, int ledgerInputs, int otherDifferences, int outsideLedger,
        int planted, int caught, int differences, bool pass)
    {
        var folder = Environment.GetEnvironmentVariable("ARCANUM_ORACLE_RECEIPTS");
        if (string.IsNullOrWhiteSpace(folder)) return;
        Directory.CreateDirectory(folder);
        var receipt = new Dictionary<string, object?>
        {
            ["format"] = 1,
            ["function"] = file.Function,
            ["name"] = file.Name,
            ["method"] = MethodIlHash.Name(method),
            ["seed"] = file.Seed,
            ["inputs"] = file.Cases.Count,
            ["mutantsPlanted"] = planted,
            ["mutantsCaught"] = caught,
            ["differences"] = differences,
            ["otherMode"] = ledger == NoSwitch ? null : new Dictionary<string, object>
            {
                ["mode"] = mode, ["ledger"] = ledger, ["ledgerInputs"] = ledgerInputs, ["differ"] = otherDifferences, ["outsideLedger"] = outsideLedger,
            },
            ["result"] = pass ? "pass" : "fail",
            ["exeSha256"] = file.ExeSha256,
            ["ilSha256"] = MethodIlHash.Compute(method),
            ["configuration"] = MethodIlHash.Configuration(method),
        };
        var path = Path.Combine(folder, file.Key + ".json");
        File.WriteAllText(path, JsonSerializer.Serialize(receipt, new JsonSerializerOptions { WriteIndented = true }).ReplaceLineEndings("\n") + "\n");
        TestContext.WriteLine($"  receipt {path}");
    }
}
