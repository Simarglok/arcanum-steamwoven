using System.Globalization;
using System.Text;

namespace Arcanum.Data.Traversal;

/// <summary>
/// T0 rule set from docs/research/area-passability-closure.md (F1, F6). Independently authored from that
/// written specification; CE pin a7ff41b3 is attributed lineage, not translated code. Original text is
/// parsed into flags/numbers only and never retained or surfaced.
/// </summary>
internal static class AreaStepRules
{
    internal const string ProfileId = "CRASH_SITE_CORE_T0_V1";
    internal const int MaxMesRecords = 4096;
    internal const int MaxMesBytes = 1 << 20;
    internal const int EffectTableSize = 400;
    internal const int StatCount = 28, StatStrength = 0, StatWillpower = 6, StatLevel = 17, StatPoison = 24;
    // Attribute tokens checked against the pinned source symbol tables; others fail closed.
    private static readonly HashSet<string> EffectVocabulary = new(StringComparer.OrdinalIgnoreCase)
    {
        "st", "dx", "cn", "be", "in", "pe", "wp", "ch", "level", "poisonlevel", "age", "magicpts", "techpts",
        "melee", "dodge", "prowling", "haggle", "repair", "firearms", "picklock", "armtrap",
        "resistdamage", "resistpoison", "maxhps", "crithitchance", "badreactionadj",
    };

    internal sealed record EffectChange(string Attribute, char Operator, int Parameter);
    internal sealed record PatchTarget(byte[] TextOrder, byte[] MixedEndian);
    internal sealed record Critter(int[] StatBase, IReadOnlyList<int> EffectIndices, long HpPoints, long HpAdjustment, long HpDamage);
    internal enum Life { Alive, Dead, Unknown }

    /// <summary>Bounded braced-record reader. Text between records is ignored; duplicates and broken braces fail.</summary>
    internal static SortedDictionary<int, string> ParseMes(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length > MaxMesBytes) throw new AreaStepInvocationException(AreaStepInvocationCode.LimitExceeded, AreaStepInvocationStage.Rules);
        var text = Encoding.Latin1.GetString(bytes); var result = new SortedDictionary<int, string>(); var cursor = 0;
        while ((cursor = text.IndexOf('{', cursor)) >= 0)
        {
            var keyEnd = text.IndexOf('}', cursor + 1);
            if (keyEnd < 0 || !int.TryParse(text.AsSpan(cursor + 1, keyEnd - cursor - 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var key))
                throw Malformed();
            cursor = keyEnd + 1; while (cursor < text.Length && char.IsWhiteSpace(text[cursor])) cursor++;
            if (cursor == text.Length || text[cursor] != '{') throw Malformed();
            var valueEnd = text.IndexOf('}', cursor + 1);
            if (valueEnd < 0 || text.IndexOf('{', cursor + 1, valueEnd - cursor - 1) >= 0 || !result.TryAdd(key, text[(cursor + 1)..valueEnd])) throw Malformed();
            if (result.Count > MaxMesRecords) throw new AreaStepInvocationException(AreaStepInvocationCode.LimitExceeded, AreaStepInvocationStage.Rules);
            cursor = valueEnd + 1;
        }
        return result;
        static AreaStepInvocationException Malformed() => new(AreaStepInvocationCode.MalformedSource, AreaStepInvocationStage.Rules);
    }

    /// <summary>Flags run from the first '/' to the first space; 'b'/'f' block; other characters are ignored.
    /// A bare value needs three characters (null otherwise).</summary>
    internal static bool? TileNameBlocks(string value)
    {
        var slash = value.IndexOf('/');
        if (slash < 0) return value.Length >= 3 ? false : null;
        for (var i = slash + 1; i < value.Length && value[i] != ' '; i++)
            if (value[i] is 'b' or 'f') return true;
        return false;
    }

    /// <summary>Per category (outdoor+flippable, outdoor, indoor+flippable, indoor), the blocking flag by material
    /// index. Null when an anchor key 0/100/200/300 is missing or any bare value in 0..399 is too short.</summary>
    internal static bool[][]? TileNameTable(SortedDictionary<int, string> records)
    {
        if (!records.ContainsKey(0) || !records.ContainsKey(100) || !records.ContainsKey(200) || !records.ContainsKey(300)) return null;
        var flags = records.Where(r => r.Key is >= 0 and < 400).Select(r => (r.Key, Blocks: TileNameBlocks(r.Value))).ToArray();
        if (flags.Any(f => f.Blocks is null)) return null;
        return [.. Enumerable.Range(0, 4).Select(c => flags.Where(f => f.Key / 100 == c).Select(f => f.Blocks!.Value).ToArray())];
    }

    /// <summary>Class-0 tile: bit 8 outdoor, bit 7 flippable, bits 22..27 material index. Null when out of range.</summary>
    internal static bool? TileBlocks(uint raw, bool[][] table)
    {
        var category = ((raw >> 8) & 1, (raw >> 7) & 1) switch { (1, 1) => 0, (1, 0) => 1, (0, 1) => 2, _ => 3 };
        var material = (int)((raw >> 22) & 63);
        return material < table[category].Length ? table[category][material] : null;
    }

    /// <summary>Space/comma tokens; admitted operators are signed add, signed percent and min/max (not -1).
    /// Multiply/divide (factor read as zero by the pinned parser) and other forms return null.</summary>
    internal static IReadOnlyList<EffectChange>? ParseEffect(string text)
    {
        var tokens = text.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Any(t => t.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)))) return null;
        var changes = new List<EffectChange>();
        for (var i = 0; i < tokens.Length; i++)
        {
            if (!EffectVocabulary.Contains(tokens[i]) || i + 1 >= tokens.Length) return null;
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

    /// <summary>Scale by the summed percentage (truncating), add the summed offsets, raise to the largest floor,
    /// lower to the smallest ceiling.</summary>
    internal static long ApplyEffects(IReadOnlyList<IReadOnlyList<EffectChange>> effects, string attribute, long value)
    {
        var relevant = effects.SelectMany(e => e).Where(c => c.Attribute == attribute).ToArray();
        var percent = relevant.Where(c => c.Operator == '%').Sum(c => (long)c.Parameter);
        var result = checked((percent == 0 ? value : value * (100 + percent) / 100) + relevant.Where(c => c.Operator == '+').Sum(c => (long)c.Parameter));
        if (relevant.Where(c => c.Operator == 'n').Select(c => (long?)c.Parameter).Max() is { } floor) result = Math.Max(result, floor);
        if (relevant.Where(c => c.Operator == 'x').Select(c => (long?)c.Parameter).Min() is { } ceiling) result = Math.Min(result, ceiling);
        return result;
    }

    /// <summary>Current HP = WP + 2*(ST + Level) + 4 + HpAdjustment + 4*HpPoints - HpDamage for an NPC (no background);
    /// ST first loses min(3, poison/100). ST/WP are exact inside [1,20] (every race maximum is at least 20) and an
    /// interval above; Level is clamped exactly to [0,51]. A max-HP effect is not admitted.</summary>
    internal static (Life Life, long Minimum, long Maximum, string Reason) Evaluate(Critter critter, IReadOnlyDictionary<int, IReadOnlyList<EffectChange>?> table)
    {
        if (critter.StatBase.Length != StatCount) return (Life.Unknown, 0, 0, "CRITTER_STATS_UNRESOLVED");
        var effects = new List<IReadOnlyList<EffectChange>>();
        foreach (var index in critter.EffectIndices)
        {
            if (index is < 0 or >= EffectTableSize) return (Life.Unknown, 0, 0, "CRITTER_EFFECT_INDEX_UNSUPPORTED");
            if (!table.TryGetValue(index, out var parsed)) continue; // An undefined slot carries no changes.
            if (parsed is null) return (Life.Unknown, 0, 0, "CRITTER_EFFECT_UNSUPPORTED");
            effects.Add(parsed);
        }
        if (effects.SelectMany(e => e).Any(c => c.Attribute == "maxhps")) return (Life.Unknown, 0, 0, "CRITTER_MAX_HP_EFFECT_UNSUPPORTED");
        var poison = Math.Clamp(ApplyEffects(effects, "poisonlevel", critter.StatBase[StatPoison]), 0, 1000);
        var strength = Bound(ApplyEffects(effects, "st", critter.StatBase[StatStrength] - Math.Min(3, poison / 100)));
        var willpower = Bound(ApplyEffects(effects, "wp", critter.StatBase[StatWillpower]));
        var level = Math.Clamp(ApplyEffects(effects, "level", critter.StatBase[StatLevel]), 0, 51);
        var fixedPart = checked(critter.HpAdjustment + 4 * critter.HpPoints + 4 - critter.HpDamage + 2 * level);
        var minimum = checked(willpower.Min + 2 * strength.Min + fixedPart);
        var maximum = checked(willpower.Max + 2 * strength.Max + fixedPart);
        return minimum > 0 ? (Life.Alive, minimum, maximum, "CRITTER_ALIVE") :
            maximum <= 0 ? (Life.Dead, minimum, maximum, "CRITTER_DEAD") : (Life.Unknown, minimum, maximum, "CRITTER_LIFE_INTERVAL_UNRESOLVED");
        static (long Min, long Max) Bound(long value) => value < 1 ? (1, 1) : value <= 20 ? (value, value) : (20, value);
    }

    /// <summary>`G_` GUID lines in both byte orders (text order and mixed-endian layout). Other non-comment lines are counted malformed.</summary>
    internal static IReadOnlyList<PatchTarget> PatchTargets(string text, out int malformed)
    {
        var targets = new List<PatchTarget>(); malformed = 0;
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
}
