using System.Globalization;
using System.Text;
using System.Text.Json;
using Arcanum.Data;
using Arcanum.Data.Area;
using Arcanum.Data.Terrain;

namespace Arcanum.CrashSiteProbe;

// Research-only closure probe for the whole-exterior step frontier. Independently authored from the
// semantics recorded in docs/research/area-passability-closure.md (CE pin a7ff41b3, read as a
// specification, not translated). It emits aggregate counts only: no original text, names, payload
// bytes or input paths. It is not a production admission API and grants no movement permission.
internal static partial class Program
{
    private const string ClosureSchema = "AREA_PASSABILITY_CLOSURE_RESEARCH_V1";
    private const long ClosureSpawnX = 92934, ClosureSpawnY = 82561;
    private const int ClosureFlagsSlot = 18, ClosureHpPtsSlot = 26, ClosureHpAdjSlot = 27, ClosureHpDamageSlot = 28;
    private const int ClosureStatBaseSlot = 38, ClosureEffectsSlot = 46;
    private const int ClosureStatStrength = 0, ClosureStatWillpower = 6, ClosureStatLevel = 17, ClosureStatPoison = 24, ClosureStatCount = 28;
    private const int ClosureEffectTableSize = 400;
    // Semantic direction order 0..7 (XMinusYMinus .. YMinus), as in the area traversal contract.
    private static readonly (int Dx, int Dy)[] ClosureDeltas = [(-1, -1), (-1, 0), (-1, 1), (0, 1), (1, 1), (1, 0), (1, -1), (0, -1)];
    // Effect attribute tokens admitted by this probe. Each was checked against the pinned CE symbol tables
    // (effect.c 87-185); any other token makes the effect fail closed instead of guessing parser recovery.
    private static readonly HashSet<string> ClosureEffectVocabulary = new(StringComparer.OrdinalIgnoreCase)
    {
        "st", "dx", "cn", "be", "in", "pe", "wp", "ch", "level", "poisonlevel", "age", "magicpts", "techpts",
        "melee", "dodge", "prowling", "haggle", "repair", "firearms", "picklock", "armtrap",
        "resistdamage", "resistpoison", "maxhps", "crithitchance", "badreactionadj",
    };

    internal enum ClosureTerrain { Missing, Open, Blocked }
    internal enum ClosureStep { Open, TerrainBlocked, ObjectBlocked, NeighborMissing }
    internal enum ClosureLife { Alive, Dead, Unknown }
    internal sealed record ClosureEffectChange(string Attribute, char Operator, int Parameter);

    // Flags run from the first '/' to the first space; 'b' and 'f' block, other characters are ignored.
    // A value without '/' needs at least three characters (shorter aborts tile-name initialization: null).
    internal static bool? ClosureTileNameBlocks(string value)
    {
        var slash = value.IndexOf('/');
        if (slash < 0) return value.Length >= 3 ? false : null;
        for (var i = slash + 1; i < value.Length && value[i] != ' '; i++)
            if (value[i] is 'b' or 'f') return true;
        return false;
    }

    // Per-category arrays are filled in ascending key order and restart only at keys 0/100/200/300,
    // so rank-within-category equals the material index only when all four anchors exist. A bare
    // name shorter than three characters anywhere in 0..399 aborts the whole table: also null.
    internal static string[][]? ClosureTileNameCategories(SortedDictionary<int, string> records)
    {
        if (!records.ContainsKey(0) || !records.ContainsKey(100) || !records.ContainsKey(200) || !records.ContainsKey(300)) return null;
        if (records.Where(r => r.Key is >= 0 and < 400).Any(r => ClosureTileNameBlocks(r.Value) is null)) return null;
        return [.. Enumerable.Range(0, 4).Select(c => records.Where(r => r.Key >= c * 100 && r.Key < (c + 1) * 100).Select(r => r.Value).ToArray())];
    }

    // Packed class-0 tile: bit 8 outdoor, bit 7 flippable, bits 22..27 material index.
    internal static bool? ClosureTileBlocks(uint raw, string[][] categories)
    {
        var category = ((raw >> 8) & 1, (raw >> 7) & 1) switch { (1, 1) => 0, (1, 0) => 1, (0, 1) => 2, _ => 3 };
        var material = (int)((raw >> 22) & 63);
        return material < categories[category].Length ? ClosureTileNameBlocks(categories[category][material]) : null;
    }

    // Tokens are separated by spaces and commas only. Admitted operators: signed add, signed percent,
    // and min/max bounds. Multiply/divide tokens are refused because the pinned parser reads their
    // factor as zero; a bound of -1 is refused because -1 is the source's "unset" marker.
    internal static IReadOnlyList<ClosureEffectChange>? ClosureParseEffect(string text)
    {
        var tokens = text.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Any(t => t.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)))) return null;
        var changes = new List<ClosureEffectChange>();
        for (var i = 0; i < tokens.Length; i++)
        {
            if (!ClosureEffectVocabulary.Contains(tokens[i]) || i + 1 >= tokens.Length) return null;
            var attribute = tokens[i].ToLowerInvariant(); var op = tokens[++i];
            if (op.Equals("min", StringComparison.OrdinalIgnoreCase) || op.Equals("max", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= tokens.Length || !int.TryParse(tokens[++i], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var bound) || bound == -1) return null;
                changes.Add(new(attribute, char.ToLowerInvariant(op[1]) == 'i' ? 'n' : 'x', bound));
            }
            else if (op[0] is '+' or '-')
            {
                var percent = op.EndsWith('%');
                if (!int.TryParse(percent ? op[..^1] : op, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var amount)) return null;
                changes.Add(new(attribute, percent ? '%' : '+', amount));
            }
            else return null;
        }
        return changes;
    }

    // Admitted changes compose as: scale by the summed percentage (integer, truncating), add the
    // summed offsets, then raise to the largest floor and lower to the smallest ceiling.
    internal static int ClosureApplyEffects(IEnumerable<IReadOnlyList<ClosureEffectChange>> effects, string attribute, int value)
    {
        var relevant = effects.SelectMany(e => e).Where(c => c.Attribute == attribute).ToArray();
        var percent = relevant.Where(c => c.Operator == '%').Sum(c => c.Parameter);
        var result = (percent == 0 ? value : value * (100 + percent) / 100) + relevant.Where(c => c.Operator == '+').Sum(c => c.Parameter);
        if (relevant.Where(c => c.Operator == 'n').Select(c => (int?)c.Parameter).Max() is { } floor) result = Math.Max(result, floor);
        if (relevant.Where(c => c.Operator == 'x').Select(c => (int?)c.Parameter).Min() is { } ceiling) result = Math.Min(result, ceiling);
        return result;
    }

    // The engine clamps to [floor, raceMaximum]; every race maximum is at least the common ceiling,
    // so a value above the ceiling is only bounded, never guessed.
    internal static (long Min, long Max) ClosureClampInterval(int value, int floor, int commonCeiling) =>
        value < floor ? (floor, floor) : value <= commonCeiling ? (value, value) : (commonCeiling, value);

    internal sealed record ClosureCritter(int[] StatBase, IReadOnlyList<int> EffectIndices, long HpPts, long HpAdj, long HpDamage);

    // Current HP = maxhps-effects(WP + 2*(ST + Level) + 4 + HpAdj + 4*HpPts) - HpDamage for an NPC
    // (no background); ST first loses min(3, poison/100). A max-HP effect is not admitted here.
    internal static (ClosureLife Life, long Min, long Max, string Reason) ClosureCritterLife(ClosureCritter c,
        IReadOnlyDictionary<int, IReadOnlyList<ClosureEffectChange>?> table)
    {
        if (c.StatBase.Length != ClosureStatCount) return (ClosureLife.Unknown, 0, 0, "STAT_BASE_SHAPE");
        var effects = new List<IReadOnlyList<ClosureEffectChange>>();
        foreach (var index in c.EffectIndices)
        {
            if (index is < 0 or >= ClosureEffectTableSize) return (ClosureLife.Unknown, 0, 0, "EFFECT_INDEX_RANGE");
            if (!table.TryGetValue(index, out var parsed)) continue; // Undefined slots carry no changes.
            if (parsed is null) return (ClosureLife.Unknown, 0, 0, "EFFECT_GRAMMAR");
            effects.Add(parsed);
        }
        if (effects.SelectMany(e => e).Any(x => x.Attribute == "maxhps")) return (ClosureLife.Unknown, 0, 0, "MAX_HP_EFFECT");
        var poison = Math.Clamp(ClosureApplyEffects(effects, "poisonlevel", c.StatBase[ClosureStatPoison]), 0, 1000);
        var strength = c.StatBase[ClosureStatStrength] - Math.Min(3, poison / 100);
        var st = ClosureClampInterval(ClosureApplyEffects(effects, "st", strength), 1, 20);
        var wp = ClosureClampInterval(ClosureApplyEffects(effects, "wp", c.StatBase[ClosureStatWillpower]), 1, 20);
        var exactLevel = Math.Clamp(ClosureApplyEffects(effects, "level", c.StatBase[ClosureStatLevel]), 0, 51); // Race-independent bound.
        var level = (Min: (long)exactLevel, Max: (long)exactLevel);
        var fixedPart = c.HpAdj + 4 * c.HpPts + 4 - c.HpDamage;
        var min = wp.Min + 2 * (st.Min + level.Min) + fixedPart;
        var max = wp.Max + 2 * (st.Max + level.Max) + fixedPart;
        return min > 0 ? (ClosureLife.Alive, min, max, "HP_POSITIVE") : max <= 0 ? (ClosureLife.Dead, min, max, "HP_NONPOSITIVE") : (ClosureLife.Unknown, min, max, "HP_INTERVAL_STRADDLES_ZERO");
    }

    // Destination terrain first; then object-only legs. A map diagonal checks both shoulders and the
    // final cell for object blockers but never consults shoulder terrain.
    internal static ClosureStep ClosureStepVerdict(Func<long, long, ClosureTerrain> terrain, IReadOnlySet<(long, long)> blockers, long x, long y, int direction)
    {
        var (dx, dy) = ClosureDeltas[direction];
        long tx = x + dx, ty = y + dy;
        var final = terrain(tx, ty);
        if (final == ClosureTerrain.Missing) return ClosureStep.NeighborMissing;
        if (final == ClosureTerrain.Blocked) return ClosureStep.TerrainBlocked;
        if ((direction & 1) == 1) return blockers.Contains((tx, ty)) ? ClosureStep.ObjectBlocked : ClosureStep.Open;
        var ccw = ClosureDeltas[(direction + 7) % 8]; var cw = ClosureDeltas[direction + 1];
        (long, long) left = (x + ccw.Dx, y + ccw.Dy), right = (x + cw.Dx, y + cw.Dy);
        // A shoulder in an unloaded/generated sector has no closed object registry here.
        if (terrain(left.Item1, left.Item2) == ClosureTerrain.Missing || terrain(right.Item1, right.Item2) == ClosureTerrain.Missing) return ClosureStep.NeighborMissing;
        return blockers.Contains(left) || blockers.Contains(right) || blockers.Contains((tx, ty)) ? ClosureStep.ObjectBlocked : ClosureStep.Open;
    }

    internal static (HashSet<(long, long)> Reached, HashSet<(long, long)> Exits) ClosureReach(Func<long, long, ClosureTerrain> terrain,
        IReadOnlySet<(long, long)> blockers, (long, long) start)
    {
        var reached = new HashSet<(long, long)> { start }; var exits = new HashSet<(long, long)>(); var queue = new Queue<(long, long)>([start]);
        while (queue.Count != 0)
        {
            var (x, y) = queue.Dequeue();
            for (var d = 0; d < 8; d++)
            {
                var step = ClosureStepVerdict(terrain, blockers, x, y, d);
                var next = (x + ClosureDeltas[d].Dx, y + ClosureDeltas[d].Dy);
                if (step == ClosureStep.NeighborMissing)
                {
                    // Record the absent cells themselves: the final cell or a diagonal shoulder.
                    foreach (var probe in new[] { next, (x + ClosureDeltas[(d + 7) % 8].Dx, y + ClosureDeltas[(d + 7) % 8].Dy), (x + ClosureDeltas[(d + 1) % 8].Dx, y + ClosureDeltas[(d + 1) % 8].Dy) }
                        .Take((d & 1) == 0 ? 3 : 1))
                        if (terrain(probe.Item1, probe.Item2) == ClosureTerrain.Missing) exits.Add(probe);
                }
                else if (step == ClosureStep.Open && reached.Add(next)) queue.Enqueue(next);
            }
        }
        return (reached, exits);
    }

    internal static SortedDictionary<int, string> ClosureMes(byte[] bytes)
    {
        // Latin-1 keeps every byte; text stays in memory and never reaches output.
        var text = Encoding.Latin1.GetString(bytes); var result = new SortedDictionary<int, string>(); var cursor = 0;
        while ((cursor = text.IndexOf('{', cursor)) >= 0)
        {
            var keyEnd = text.IndexOf('}', cursor + 1);
            if (keyEnd < 0 || !int.TryParse(text.AsSpan(cursor + 1, keyEnd - cursor - 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var key))
                throw new InvalidDataException("CLOSURE_MES_KEY");
            cursor = keyEnd + 1; while (cursor < text.Length && char.IsWhiteSpace(text[cursor])) cursor++;
            if (cursor == text.Length || text[cursor] != '{') throw new InvalidDataException("CLOSURE_MES_VALUE");
            var valueEnd = text.IndexOf('}', cursor + 1);
            if (valueEnd < 0 || !result.TryAdd(key, text[(cursor + 1)..valueEnd])) throw new InvalidDataException("CLOSURE_MES_RECORD");
            cursor = valueEnd + 1;
        }
        return result;
    }

    // Object IDs named by map.patch lines ("G_xxxxxxxx_xxxx_xxxx_xxxx_xxxxxxxxxxxx: ..."). Both byte orders are
    // kept: the pinned string parser stores the digits in text order, while the observed map file stores
    // GUIDs in the mixed-endian Windows layout. Any other non-comment line is malformed (fail closed).
    internal sealed record ClosurePatchTarget(byte[] TextOrder, byte[] MixedEndian);
    internal static IReadOnlyList<ClosurePatchTarget> ClosurePatchTargets(string text, out int malformed)
    {
        var targets = new List<ClosurePatchTarget>(); malformed = 0;
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == '#') continue;
            var colon = line.IndexOf(':');
            var digits = colon > 2 && line.StartsWith("G_", StringComparison.Ordinal) ? line[2..colon] : "";
            if (digits.Length != 36 || !Guid.TryParseExact(digits.Replace('_', '-'), "D", out var guid)) { malformed++; continue; }
            targets.Add(new(Convert.FromHexString(digits.Replace("_", "")), guid.ToByteArray()));
        }
        return targets;
    }

    internal static (bool TextOrder, bool MixedEndian) ClosurePatchHits(ReadOnlySpan<byte> record, ClosurePatchTarget target) =>
        (record.IndexOf(target.TextOrder) >= 0, record.IndexOf(target.MixedEndian) >= 0);

    private static int ProbeAreaPassabilityClosure(string[] args)
    {
        string[] single = ["--module", "--resource1", "--resource2", "--resource3", "--resource4", "--tig", "--patch", "--mobile", "--prototype-root", "--oracle"];
        var options = new Dictionary<string, string>(); var looseRoots = new List<string>();
        for (var i = 0; i < args.Length; i += 2)
        {
            if (i + 1 >= args.Length) throw new ArgumentException("CLOSURE_ARGUMENTS");
            if (args[i] == "--loose-root") looseRoots.Add(args[i + 1]);
            else if (!single.Contains(args[i]) || !options.TryAdd(args[i], args[i + 1])) throw new ArgumentException("CLOSURE_ARGUMENTS");
        }
        if (options.Count != single.Length || looseRoots.Count == 0) throw new ArgumentException("CLOSURE_ARGUMENTS");
        var oracleBefore = InputFingerprint.Create(options["--oracle"]);
        if (oracleBefore.Sha256 != "bfe9770d75a1c1922ca8817fd0c239e589c3de48020022f7de6bd450ef2d2b60") throw new InvalidDataException("ORACLE_IDENTITY");
        using var oracle = JsonDocument.Parse(File.ReadAllBytes(options["--oracle"]));
        var paths = new List<(AreaInputKey Key, string Path)>
        {
            (new(AreaSourceRole.Module), options["--module"]), (new(AreaSourceRole.Resource1), options["--resource1"]),
            (new(AreaSourceRole.Resource2), options["--resource2"]), (new(AreaSourceRole.NormalGameMobile), options["--mobile"])
        };
        foreach (var p in oracle.RootElement.GetProperty("prototypes").EnumerateArray())
        {
            var n = p.GetProperty("own_number").GetInt32();
            var matches = Directory.EnumerateFiles(options["--prototype-root"], $"{n:D6} - *.pro").Take(2).ToArray();
            if (matches.Length != 1) throw new InvalidDataException("PROTOTYPE_SELECTION");
            paths.Add((new(AreaSourceRole.Prototype, n), matches[0]));
        }
        string[] archiveRoles = ["--module", "--resource1", "--resource2", "--resource3", "--resource4", "--tig", "--patch"];
        var guarded = paths.Select(p => p.Path).Concat(archiveRoles.Select(r => options[r])).Append(options["--oracle"]).Distinct().ToArray();
        var before = guarded.Select(InputFingerprint.Create).ToArray();
        var fingerprints = guarded.Zip(before).ToDictionary(p => p.First, p => p.Second);

        using var owner = AreaOriginalValidator.Open(new(paths.Select(p => AreaInput.FromFile(p.Key, p.Path)).ToArray()));
        var snapshot = owner.LoadStrict();
        var mobileBytes = File.ReadAllBytes(options["--mobile"]);
        if (AreaHash(mobileBytes) != snapshot.Inputs.Single(i => i.Key.Role == AreaSourceRole.NormalGameMobile).Sha256) throw new InvalidDataException("MOBILE_IDENTITY");

        // Each consumed resource must occur exactly once across every supplied archive: no precedence is inferred.
        var archives = archiveRoles.ToDictionary(r => r, r => DatArchive.Open(options[r]));
        try
        {
            string[] wanted = ["art/tile/tilename.mes", "rules/effect.mes", "rules/gameeffect.mes", "rules/map.patch"];
            var resources = new Dictionary<string, (string Role, byte[] Bytes)>();
            foreach (var name in wanted)
            {
                var hits = archives.SelectMany(a => a.Value.Entries.Where(e => ClosureEntryName(e) == name).Select(e => (a.Key, a.Value, e))).ToArray();
                if (hits.Length != 1) throw new InvalidDataException("RESOURCE_NOT_UNIQUE");
                resources[name] = (hits[0].Key.TrimStart('-'), hits[0].Item2.ReadEntry(hits[0].e));
            }
            string[] looseNames = ["tilename.mes", "effect.mes", "gameeffect.mes", "map.patch"];
            var looseOverrides = looseRoots.Sum(root => Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                .Count(f => looseNames.Contains(Path.GetFileName(f), StringComparer.OrdinalIgnoreCase) ||
                    f.EndsWith(".dif", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".mdy", StringComparison.OrdinalIgnoreCase) ||
                    f.EndsWith(".sec", StringComparison.OrdinalIgnoreCase)));
            if (looseOverrides != 0) throw new InvalidDataException("LOOSE_OVERRIDE_PRESENT");
            foreach (var sector in snapshot.Sectors.Where(x => x.Present))
                if (archives.Sum(a => a.Value.Entries.Count(e => ClosureEntryName(e) == $"maps/arcanum1-024-fixed/{sector.Key.Packed}.sec")) != 1)
                    throw new InvalidDataException("SECTOR_NOT_UNIQUE");

            var nameRecords = ClosureMes(resources["art/tile/tilename.mes"].Bytes);
            var categories = ClosureTileNameCategories(nameRecords) ?? throw new InvalidDataException("TILE_NAME_ANCHORS");
            var effectTable = new Dictionary<int, IReadOnlyList<ClosureEffectChange>?>();
            foreach (var (key, value) in ClosureMes(resources["rules/effect.mes"].Bytes).Where(r => r.Key is >= 50 and < 400)) effectTable[key] = ClosureParseEffect(value);
            foreach (var (key, value) in ClosureMes(resources["rules/gameeffect.mes"].Bytes).Where(r => r.Key is >= 0 and < 50)) effectTable[key] = ClosureParseEffect(value);

            // Terrain under the selected destination rule, against the production conservative components.
            var module = archives["--module"];
            var nameSource = new TerrainTileNameSource(archives["--resource2"], "R2", "art/tile/tilename.mes", TerrainPathProfile.CeNonWindowsMacOs);
            var terrain = new Dictionary<(long, long), ClosureTerrain>(); var sectorRows = new List<object>(); var conservativeUnknownTotal = 0;
            foreach (var s in snapshot.Sectors.Where(s => s.Present).OrderBy(s => s.Coordinate.Y).ThenBy(s => s.Coordinate.X))
            {
                var entry = module.Entries.Single(e => e.Ordinal == s.DatOrdinal);
                if (AreaHash(module.ReadEntry(entry)) != s.PayloadSha256) throw new InvalidDataException("SECTOR_IDENTITY");
                var selector = new TerrainSectorSelector(Encoding.ASCII.GetString(entry.RawNameBytes.ToArray()[..^1]), "Arcanum1-024-fixed",
                    checked((int)s.Coordinate.X), checked((int)s.Coordinate.Y), s.Key.Packed, checked((int)s.Coordinate.OriginX), checked((int)s.Coordinate.OriginY), 64, 64,
                    new(entry.Ordinal, entry.Flags, entry.DeclaredOutputSize));
                var report = TerrainObstructionInspector.InspectSector(module, s.Key.SourceSha256, selector, nameSource);
                int mask = 0, facadeBlocked = 0, facadeOpen = 0, nameBlocked = 0, nameOpen = 0, conservativeUnknown = 0, resolvedByRule = 0;
                foreach (var t in snapshot.Terrain.Where(t => t.Key.Sector == s.Key))
                {
                    var c = report.GetCell(t.LocalX, t.LocalY);
                    var maskBlocked = c.SerializedBlockMask.Disposition == TerrainObstructionDisposition.Blocked;
                    // A sector serialized before block version 0xAA0004 keeps the zero-initialized cache mask.
                    if ((c.SerializedBlockMask.Supportedness == TerrainObstructionSupportedness.Supported) != (s.Placeholder >= 0xAA0004))
                        throw new InvalidDataException("MASK_VERSION_MISMATCH");
                    var unknownBefore = new[] { c.SerializedBlockMask, c.FacadeWalkabilityBit, c.TileNameRule }
                        .Any(r => r.Supportedness != TerrainObstructionSupportedness.Supported || r.Applicability == TerrainObstructionApplicability.Unknown);
                    bool blocked;
                    if (maskBlocked) { blocked = true; mask++; }
                    else if (t.RawArt >> 28 == 11) { blocked = (t.RawArt & 1) == 0; if (blocked) facadeBlocked++; else facadeOpen++; }
                    else if (t.RawArt >> 28 == 0)
                    {
                        blocked = ClosureTileBlocks(t.RawArt, categories) ?? throw new InvalidDataException("TILE_NAME_UNRESOLVED");
                        if (blocked) nameBlocked++; else nameOpen++;
                    }
                    else throw new InvalidDataException("TILE_ART_CLASS");
                    if (unknownBefore) { conservativeUnknown++; resolvedByRule++; conservativeUnknownTotal++; }
                    terrain[(t.WorldX, t.WorldY)] = blocked ? ClosureTerrain.Blocked : ClosureTerrain.Open;
                }
                sectorRows.Add(new { s.Coordinate.X, s.Coordinate.Y, Scope = s.Scope.ToString(), Placeholder = $"0x{s.Placeholder:X}",
                    MaskSerialized = s.Placeholder >= 0xAA0004, MaskBlocked = mask, FacadeBlocked = facadeBlocked, FacadeOpen = facadeOpen,
                    NameBlocked = nameBlocked, NameOpen = nameOpen, ConservativeUnknown = conservativeUnknown, ResolvedBySelectedRule = resolvedByRule });
            }

            // Destination contributors at the post-fold initial state; every mobile record re-verified in place.
            var blockers = new HashSet<(long, long)>(); var lifeRows = new List<(ClosureLife Life, long Min, long Max, string Reason, AreaScope Scope)>();
            int itemsExcluded = 0, offOrDestroyed = 0, inventory = 0, noBlock = 0, mobileVerified = 0, wallsPortals = 0;
            var blockerTypes = new SortedDictionary<string, int>(StringComparer.Ordinal);
            foreach (var o in snapshot.Objects.Where(o => o.Scope is AreaScope.Core or AreaScope.Context))
            {
                if (o.Key.Kind == AreaSourceKind.Mobile)
                {
                    if (o.Key.Offset < 0 || o.Key.Offset + o.Key.Length > mobileBytes.Length ||
                        AreaHash(mobileBytes.AsSpan((int)o.Key.Offset, o.Key.Length).ToArray()) != o.RawSha256) throw new InvalidDataException("MOBILE_RECORD_IDENTITY");
                    mobileVerified++;
                }
                var flags = ClosureNumber(o, ClosureFlagsSlot) ?? throw new InvalidDataException("FLAGS_UNRESOLVED");
                if (o.WorldX is not { } wx || o.WorldY is not { } wy) throw new InvalidDataException("LOCATION_UNRESOLVED");
                if (o.Type is 0 or 1) { wallsPortals++; continue; }
                if ((flags & 0x1000) != 0) { inventory++; continue; }
                if ((flags & 0x3) != 0) { offOrDestroyed++; continue; }
                if (o.Type is >= 5 and <= 14) { itemsExcluded++; continue; }
                if (o.Type is 15 or 16)
                {
                    if (o.Type == 15) throw new InvalidDataException("PC_RECORD_UNSUPPORTED");
                    var life = ClosureCritterLife(ClosureCritterOf(o), effectTable);
                    lifeRows.Add((life.Life, life.Min, life.Max, life.Reason, o.Scope));
                    if (life.Life == ClosureLife.Unknown) throw new InvalidDataException("CRITTER_LIFE_UNKNOWN");
                    if (life.Life == ClosureLife.Alive) { blockers.Add((wx, wy)); Count(blockerTypes, "critter"); }
                    continue;
                }
                if ((flags & 0x400) != 0) { noBlock++; continue; }
                blockers.Add((wx, wy)); Count(blockerTypes, $"type{o.Type}");
            }
            if (wallsPortals != 0) throw new InvalidDataException("WALL_OR_PORTAL_PRESENT");

            var patchText = Encoding.Latin1.GetString(resources["rules/map.patch"].Bytes);
            var patchTargets = ClosurePatchTargets(patchText, out var malformedPatchLines);
            if (malformedPatchLines != 0) throw new InvalidDataException("PATCH_LINE_UNSUPPORTED");
            var areaRecords = snapshot.Objects.Where(o => o.Key.Kind == AreaSourceKind.Mobile && o.Scope is AreaScope.Core or AreaScope.Context).ToArray();
            int Hits(Func<(bool TextOrder, bool MixedEndian), bool> pick) =>
                areaRecords.Count(o => patchTargets.Any(t => pick(ClosurePatchHits(mobileBytes.AsSpan((int)o.Key.Offset, o.Key.Length), t))));
            var areaTextHits = Hits(h => h.TextOrder); var areaMixedHits = Hits(h => h.MixedEndian);
            var fileTextHits = patchTargets.Count(t => ClosurePatchHits(mobileBytes, t).TextOrder);
            var fileMixedHits = patchTargets.Count(t => ClosurePatchHits(mobileBytes, t).MixedEndian);
            if (areaTextHits != 0 || areaMixedHits != 0) throw new InvalidDataException("PATCH_TARGETS_AREA_RECORD");

            ClosureTerrain Terrain(long x, long y) => terrain.TryGetValue((x, y), out var t) ? t : ClosureTerrain.Missing;
            var spawn = (ClosureSpawnX, ClosureSpawnY);
            var (reached, exits) = ClosureReach(Terrain, blockers, spawn);
            var sectors = snapshot.Sectors.ToDictionary(s => (s.Coordinate.X, s.Coordinate.Y));
            string SectorClass((long X, long Y) c) => sectors.TryGetValue((c.X >> 6, c.Y >> 6), out var s) ? s.Present ? s.Scope.ToString() : "SurveyedAbsent" : "Unsurveyed";
            var components = new List<int>(); var seen = new HashSet<(long, long)>();
            foreach (var cell in terrain.Where(t => t.Value == ClosureTerrain.Open && !blockers.Contains(t.Key)).Select(t => t.Key))
                if (!seen.Contains(cell)) { var part = ClosureReach(Terrain, blockers, cell).Reached; seen.UnionWith(part); components.Add(part.Count); }
            (int Dir, string Expected)[] frozen = [(1, "ObjectBlocked"), (5, "Open"), (0, "ObjectBlocked"), (2, "ObjectBlocked"), (4, "Open"), (6, "Open")];
            var frozenRows = frozen.Select(f => new { Direction = f.Dir, f.Expected, Observed = ClosureStepVerdict(Terrain, blockers, ClosureSpawnX, ClosureSpawnY, f.Dir).ToString() })
                .Append(new { Direction = 5, Expected = "Open", Observed = ClosureStepVerdict(Terrain, blockers, ClosureSpawnX - 1, ClosureSpawnY, 5).ToString() }).ToArray();

            var after = guarded.Select(InputFingerprint.Create).ToArray();
            if (!before.SequenceEqual(after)) throw new InvalidDataException("INPUT_CHANGED");
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                Schema = ClosureSchema,
                SourcePin = AreaTraversalPin,
                Inputs = snapshot.Inputs.Where(i => i.Key.Role != AreaSourceRole.Prototype).Select(i => new { Role = i.Key.Role.ToString(), i.Length, i.Sha256 })
                    .Concat(archiveRoles.Skip(3).Select(r => new { Role = r.TrimStart('-'), Length = fingerprints[options[r]].Bytes, fingerprints[options[r]].Sha256 })).ToArray(),
                PrototypeInputs = snapshot.Inputs.Count(i => i.Key.Role == AreaSourceRole.Prototype),
                Resources = resources.Select(r => new { Name = r.Key, Archive = r.Value.Role, Bytes = r.Value.Bytes.Length, Sha256 = AreaHash(r.Value.Bytes) }).ToArray(),
                LooseRoots = looseRoots.Count, LooseOverrideCandidates = looseOverrides,
                TileName = new { Categories = categories.Select(c => c.Length).ToArray(), Records = nameRecords.Count },
                Effects = new { Defined = effectTable.Count, OutsideGrammar = effectTable.Count(e => e.Value is null) },
                Sectors = sectorRows,
                Terrain = new { Cells = terrain.Count, Blocked = terrain.Count(t => t.Value == ClosureTerrain.Blocked),
                    ConservativeUnknown = conservativeUnknownTotal },
                Objects = new { MobileRecordsVerified = mobileVerified, WallsOrPortals = wallsPortals, InventoryExcluded = inventory, OffOrDestroyedExcluded = offOrDestroyed,
                    ItemTypeExcluded = itemsExcluded, NoBlockScenery = noBlock, BlockerCells = blockers.Count, BlockerTypes = blockerTypes },
                Critters = lifeRows.GroupBy(r => (r.Scope, r.Life)).OrderBy(g => g.Key.ToString()).Select(g => new { Scope = g.Key.Scope.ToString(), Life = g.Key.Life.ToString(),
                    Count = g.Count(), MinHp = g.Min(r => r.Min), MaxHp = g.Max(r => r.Max) }).ToArray(),
                MapPatch = new { TargetLines = patchTargets.Count, MalformedLines = malformedPatchLines, TargetsFoundInMobileFile = new { TextOrder = fileTextHits, MixedEndian = fileMixedHits },
                    AreaRecordsHit = new { TextOrder = areaTextHits, MixedEndian = areaMixedHits } },
                Reachability = new
                {
                    Spawn = new[] { ClosureSpawnX, ClosureSpawnY },
                    Reached = reached.Count,
                    BySectorScope = reached.GroupBy(c => SectorClass(c)).OrderBy(g => g.Key, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count()),
                    BySector = reached.GroupBy(c => $"{c.Item1 >> 6},{c.Item2 >> 6}").OrderBy(g => g.Key, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count()),
                    Exits = exits.GroupBy(c => (Sector: $"{c.Item1 >> 6},{c.Item2 >> 6}", Class: SectorClass(c))).OrderBy(g => g.Key.Sector, StringComparer.Ordinal)
                        .Select(g => new { g.Key.Sector, g.Key.Class, Cells = g.Count(), MinX = g.Min(c => c.Item1), MaxX = g.Max(c => c.Item1), MinY = g.Min(c => c.Item2), MaxY = g.Max(c => c.Item2) }).ToArray(),
                    Components = components.Count, LargestComponents = components.OrderByDescending(c => c).Take(5).ToArray(),
                },
                FrozenCrossCheck = frozenRows,
                FrozenCrossCheckPass = frozenRows.All(r => r.Expected == r.Observed),
                Readiness = "RESEARCH_ONLY_NOT_PRODUCTION_ADMISSION",
            }));
            return frozenRows.All(r => r.Expected == r.Observed) ? 0 : 1;
        }
        finally { foreach (var a in archives.Values) a.Dispose(); }

        static void Count(SortedDictionary<string, int> map, string key) => map[key] = map.GetValueOrDefault(key) + 1;
    }

    private static string ClosureEntryName(DatEntry e) => Encoding.ASCII.GetString(e.RawNameBytes.ToArray()).TrimEnd('\0').Replace('\\', '/').ToLowerInvariant();

    private static long? ClosureNumber(AreaObjectRecord o, int slot) =>
        o.EffectiveFields.FirstOrDefault(f => f.Slot == slot) is { Presence: AreaPresence.Value, Number: { } n } ? n : null;

    private static ClosureCritter ClosureCritterOf(AreaObjectRecord o)
    {
        var stats = o.EffectiveFields.FirstOrDefault(f => f.Slot == ClosureStatBaseSlot);
        if (stats is not { Presence: AreaPresence.Array } || stats.Elements.Count != ClosureStatCount ||
            !stats.Elements.Select(e => e.Key).SequenceEqual(Enumerable.Range(0, ClosureStatCount)) || stats.Elements.Any(e => e.Words.Count != 1))
            throw new InvalidDataException("STAT_BASE_UNRESOLVED");
        var effects = o.EffectiveFields.FirstOrDefault(f => f.Slot == ClosureEffectsSlot);
        IReadOnlyList<int> indices = effects switch
        {
            null or { Presence: AreaPresence.Null or AreaPresence.Empty } => [],
            { Presence: AreaPresence.Array } a when a.Elements.All(e => e.Words.Count == 1) &&
                a.Elements.Select(e => e.Key).SequenceEqual(Enumerable.Range(0, a.Elements.Count)) => [.. a.Elements.Select(e => unchecked((int)e.Words[0]))],
            _ => throw new InvalidDataException("EFFECTS_UNRESOLVED")
        };
        return new([.. stats.Elements.Select(e => unchecked((int)e.Words[0]))], indices,
            ClosureNumber(o, ClosureHpPtsSlot) ?? throw new InvalidDataException("HP_UNRESOLVED"),
            ClosureNumber(o, ClosureHpAdjSlot) ?? throw new InvalidDataException("HP_UNRESOLVED"),
            ClosureNumber(o, ClosureHpDamageSlot) ?? throw new InvalidDataException("HP_UNRESOLVED"));
    }

    private static void AreaPassabilityClosureSelfTest()
    {
        var checks = 0;
        void Check(bool condition, string id) { if (!condition) throw new InvalidOperationException($"closure self-test failed: {id}"); checks++; }

        // C1 tile-name flags: multi-slash, ignored characters, sound tails, short and long bare names.
        Check(ClosureTileNameBlocks("aaa/s/n 4") == false, "C1_MULTI_SLASH_OPEN");
        Check(ClosureTileNameBlocks("aaa/b/p 0") == true, "C1_MULTI_SLASH_BLOCK");
        Check(ClosureTileNameBlocks("aaa/n2") == false && ClosureTileNameBlocks("aaa/f") == true, "C1_FLAG_TAIL");
        Check(ClosureTileNameBlocks("aaa/n b") == false, "C1_SPACE_ENDS_FLAGS");
        Check(ClosureTileNameBlocks("aaa") == false && ClosureTileNameBlocks("abcd 3") == false && ClosureTileNameBlocks("ab") is null, "C1_BARE_NAMES");
        var names = new SortedDictionary<int, string> { [0] = "oo0/b", [1] = "oo1", [100] = "nn0", [102] = "nn1/f", [200] = "ii0", [300] = "xx0" };
        var categories = ClosureTileNameCategories(names)!;
        Check(categories[1].Length == 2 && ClosureTileBlocks((1u << 22) | 0x100, categories) == true, "C1_RANK_WITHIN_CATEGORY");
        Check(ClosureTileBlocks(0x180, categories) == true && ClosureTileBlocks((1u << 22) | 0x180, categories) == false, "C1_OUTDOOR_FLIPPABLE");
        Check(ClosureTileBlocks((5u << 22) | 0x80, categories) is null, "C1_MATERIAL_OUT_OF_RANGE");
        names[399] = "ab"; Check(ClosureTileNameCategories(names) is null, "C1_SHORT_BARE_NAME_ABORTS_TABLE");
        names.Remove(399); names.Remove(200); Check(ClosureTileNameCategories(names) is null, "C1_ANCHOR_REQUIRED");

        // C2 effect grammar and stacking order.
        var parsed = ClosureParseEffect("st +2, maxhps +15%, wp min 3, ST -1, level max 9")!;
        Check(parsed.Count == 5 && parsed[1].Operator == '%' && parsed[2].Operator == 'n' && parsed[3].Attribute == "st" && parsed[4].Operator == 'x', "C2_GRAMMAR");
        Check(ClosureParseEffect("unknowntoken +1") is null && ClosureParseEffect("st") is null && ClosureParseEffect("st ?3") is null, "C2_FAIL_CLOSED");
        Check(ClosureParseEffect("st *3") is null && ClosureParseEffect("st /2") is null, "C2_MULTIPLY_DIVIDE_REFUSED");
        Check(ClosureParseEffect("st min -1") is null && ClosureParseEffect("st\t+1") is null && ClosureParseEffect("st +1\nwp +1") is null, "C2_SENTINEL_AND_SEPARATORS");
        Check(ClosureParseEffect("") is { Count: 0 }, "C2_EMPTY");
        IReadOnlyList<ClosureEffectChange>[] stack = [[new("st", '%', 50), new("st", '+', 1)], [new("st", '+', 2), new("st", 'x', 20)]];
        Check(ClosureApplyEffects(stack, "st", 4) == 9 && ClosureApplyEffects(stack, "st", 13) == 20 && ClosureApplyEffects(stack, "wp", 4) == 4, "C2_ORDER_AND_BOUND");
        Check(ClosureApplyEffects([[new("st", '%', -50)]], "st", -3) == -1, "C2_TRUNCATING_PERCENT");
        Check(ClosureApplyEffects([[new("st", '+', -9), new("st", 'n', 2), new("st", 'n', 5), new("st", 'x', 7), new("st", 'x', 6)]], "st", 4) == 5, "C2_STRONGEST_BOUNDS");

        // C3 HP intervals: exact inside common bounds, conservative above them, fail closed on max-HP effects.
        int[] Stats(int st, int wp, int level, int poison = 0) { var s = new int[ClosureStatCount]; s[0] = st; s[6] = wp; s[17] = level; s[24] = poison; return s; }
        var table = new Dictionary<int, IReadOnlyList<ClosureEffectChange>?> { [70] = [new("st", '+', 4)], [71] = [new("maxhps", '+', 100)], [72] = null };
        var alive = ClosureCritterLife(new(Stats(8, 8, 1), [70], 0, 2, 10), table);
        Check(alive.Life == ClosureLife.Alive && alive.Min == 8 + 2 * (12 + 1) + 4 + 2 - 10, "C3_EXACT_ALIVE");
        Check(ClosureCritterLife(new(Stats(8, 8, 1), [], 0, 0, 32000), table).Life == ClosureLife.Dead, "C3_DEAD");
        var high = ClosureCritterLife(new(Stats(25, 8, 1), [], 0, 0, 0), table);
        Check(high.Life == ClosureLife.Alive && high.Min == 8 + 2 * (20 + 1) + 4 && high.Max == 8 + 2 * (25 + 1) + 4, "C3_RACE_CEILING_INTERVAL");
        var floor = ClosureCritterLife(new(Stats(0, 0, 0), [], 0, -7, 0), table);
        Check(floor.Life == ClosureLife.Dead && floor.Min == 0 && floor.Max == 0, "C3_FLOOR_CLAMP");
        Check(ClosureCritterLife(new(Stats(8, 8, 1), [71], 0, 0, 0), table).Reason == "MAX_HP_EFFECT", "C3_MAX_HP_FAIL_CLOSED");
        Check(ClosureCritterLife(new(Stats(8, 8, 1), [72], 0, 0, 0), table).Reason == "EFFECT_GRAMMAR", "C3_GRAMMAR_FAIL_CLOSED");
        Check(ClosureCritterLife(new(Stats(8, 8, 1), [400], 0, 0, 0), table).Reason == "EFFECT_INDEX_RANGE", "C3_INDEX_RANGE");
        Check(ClosureCritterLife(new(Stats(8, 8, 1, 250), [], 0, 0, 0), table).Min == 8 + 2 * (6 + 1) + 4, "C3_POISON_PENALTY");
        Check(ClosureCritterLife(new(Stats(8, 8, 1, 900), [], 0, 0, 0), table).Min == 8 + 2 * (5 + 1) + 4, "C3_POISON_CAP");
        Check(ClosureCritterLife(new(new int[3], [], 0, 0, 0), table).Reason == "STAT_BASE_SHAPE", "C3_SHAPE");

        // C4 step rule: destination terrain only, diagonal object shoulders, missing neighbors.
        var open = new HashSet<(long, long)>();
        for (var x = 0; x < 5; x++) for (var y = 0; y < 5; y++) open.Add((x, y));
        var walls = new HashSet<(long, long)> { (2, 1) };
        ClosureTerrain T(long x, long y) => !open.Contains((x, y)) ? ClosureTerrain.Missing : walls.Contains((x, y)) ? ClosureTerrain.Blocked : ClosureTerrain.Open;
        var objects = new HashSet<(long, long)> { (1, 3) };
        Check(ClosureStepVerdict(T, objects, 1, 1, 5) == ClosureStep.TerrainBlocked, "C4_DEST_TERRAIN");
        Check(ClosureStepVerdict(T, objects, 2, 1, 1) == ClosureStep.Open, "C4_SOURCE_TERRAIN_IGNORED");
        Check(ClosureStepVerdict(T, objects, 1, 1, 4) == ClosureStep.Open && ClosureStepVerdict(T, objects, 2, 2, 6) == ClosureStep.Open, "C4_SHOULDER_TERRAIN_IGNORED");
        Check(ClosureStepVerdict(T, objects, 0, 3, 4) == ClosureStep.ObjectBlocked && ClosureStepVerdict(T, objects, 1, 2, 2) == ClosureStep.ObjectBlocked, "C4_SHOULDER_OBJECT");
        Check(ClosureStepVerdict(T, objects, 1, 2, 3) == ClosureStep.ObjectBlocked && ClosureStepVerdict(T, objects, 0, 2, 4) == ClosureStep.ObjectBlocked, "C4_FINAL_OBJECT");
        Check(ClosureStepVerdict(T, objects, 4, 4, 5) == ClosureStep.NeighborMissing && ClosureStepVerdict(T, objects, 4, 3, 3) == ClosureStep.Open, "C4_MISSING");
        Check(ClosureStepVerdict(T, objects, 4, 3, 4) == ClosureStep.NeighborMissing, "C4_MISSING_DIAGONAL");
        open.Remove((3, 0)); Check(ClosureStepVerdict(T, objects, 3, 1, 6) == ClosureStep.NeighborMissing, "C4_MISSING_SHOULDER");

        // C5 reachability, exits and determinism.
        var (reached, exits) = ClosureReach(T, objects, (0, 0));
        Check(reached.Count == open.Count - walls.Count - objects.Count, "C5_REACH_COUNT");
        Check(exits.All(e => !open.Contains(e)) && exits.Contains((5, 0)) && exits.Contains((3, 0)), "C5_EXITS");
        Check(ClosureReach(T, objects, (0, 0)).Reached.SetEquals(reached), "C5_DETERMINISTIC");

        // C6 MES and map.patch structure.
        var mes = ClosureMes(Encoding.ASCII.GetBytes("// note\n{0}{a/b}\nloose text {5}  {x y}\n"));
        Check(mes.Count == 2 && mes[5] == "x y", "C6_MES");
        var rejected = false; try { ClosureMes(Encoding.ASCII.GetBytes("{1}{a}{1}{b}")); } catch (InvalidDataException) { rejected = true; }
        Check(rejected, "C6_MES_DUPLICATE");
        var targets = ClosurePatchTargets("# c\nG_01234567_89AB_CDEF_0123_456789ABCDEF: obj_flag OF_OFF\n", out var bad);
        Check(targets.Count == 1 && bad == 0 && Convert.ToHexString(targets[0].TextOrder) == "0123456789ABCDEF0123456789ABCDEF" &&
            Convert.ToHexString(targets[0].MixedEndian) == "67452301AB89EFCD0123456789ABCDEF", "C6_PATCH_BYTE_ORDERS");
        byte[] record = [0x77, 0, .. targets[0].TextOrder, 0x10];
        Check(ClosurePatchHits(record, targets[0]) == (true, false), "C6_PATCH_TEXT_ORDER_HIT");
        record = [.. targets[0].MixedEndian, 0x20];
        Check(ClosurePatchHits(record, targets[0]) == (false, true), "C6_PATCH_MIXED_ENDIAN_HIT");
        _ = ClosurePatchTargets("A_00000001: origin 0\nbad\nG_0123: x\n", out bad);
        Check(bad == 3, "C6_PATCH_OTHER_ID_FORMS_MALFORMED");
        Console.WriteLine($"area-passability-closure-self-test: PASS ({checks} synthetic assertions; rules only, no original data)");
    }
}
