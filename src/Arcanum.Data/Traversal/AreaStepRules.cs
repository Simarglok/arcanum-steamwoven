using System.Text;
using Arcanum.Data.Characters;
using Arcanum.Data.Messages;

namespace Arcanum.Data.Traversal;

/// <summary>
/// T0 rule set from docs/research/area-passability-closure.md (F1, F6). Independently authored from that
/// written specification, not translated code. Original text is
/// parsed into flags/numbers only and never retained or surfaced.
/// </summary>
internal static class AreaStepRules
{
    internal const string ProfileId = "CRASH_SITE_CORE_T0_V1";
    internal const int MaxMesRecords = 4096;
    internal const int MaxMesBytes = 1 << 20;
    internal const int EffectTableSize = 400;
    internal const int StatCount = 28, StatStrength = 0, StatWillpower = 6, StatLevel = 17, StatPoison = 24;

    internal sealed record EffectChange(string Attribute, char Operator, int Parameter);
    internal sealed record PatchTarget(byte[] TextOrder, byte[] MixedEndian);
    internal sealed record Critter(int[] StatBase, IReadOnlyList<int> EffectIndices, long HpPoints, long HpAdjustment, long HpDamage);
    internal enum Life { Alive, Dead, Unknown }

    private static readonly MesReaderOptions MesOptions = new() { MaxBytes = MaxMesBytes, MaxMessages = MaxMesRecords };

    /// <summary>A rule MES table in the original grammar (<see cref="MesTable"/>, ARC-DATA-027). Refusals are
    /// <see cref="AreaStepInvocationCode.MalformedSource"/>, limits <see cref="AreaStepInvocationCode.LimitExceeded"/>.</summary>
    internal static SortedDictionary<int, string> ParseMes(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length > MaxMesBytes) throw new AreaStepInvocationException(AreaStepInvocationCode.LimitExceeded, AreaStepInvocationStage.Rules);
        var status = MesTable.Read(bytes, MesOptions, out var table);
        if (status == MesParseStatus.Parsed) return table;
        throw new AreaStepInvocationException(MesTable.IsLimit(status) ? AreaStepInvocationCode.LimitExceeded : AreaStepInvocationCode.MalformedSource,
            AreaStepInvocationStage.Rules);
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

    /// <summary>ARC-DATA-035: an effect read by the executable-checked effect grammar (<see cref="EffectRules"/>, the original's
    /// parser 0x4E9C20, ledger LM9), keeping the changes the hit-point rule reads: strength, willpower, level, poison level and
    /// maximum hit points. Any other attribute (speed, a skill, a resistance…) has no part in it and is ignored. Null when the
    /// grammar refuses the text, or a kept change multiplies, divides or names the bound −1 (which the original reads as no
    /// bound, ledger EF6): the step's life rule models only add, percent and bounds.</summary>
    internal static IReadOnlyList<EffectChange>? ParseEffect(string text)
    {
        if (EffectRules.Parse(Encoding.Latin1.GetBytes(text), out var parsed) != EffectRuleStatus.Parsed) return null;
        var changes = new List<EffectChange>();
        foreach (var change in parsed)
        {
            var attribute = (change.Kind, change.Attribute) switch
            {
                (EffectAttributeKind.Stat, 0) => "st", (EffectAttributeKind.Stat, 6) => "wp", (EffectAttributeKind.Stat, 17) => "level",
                (EffectAttributeKind.Stat, 24) => "poisonlevel", (EffectAttributeKind.Special, 0) => "maxhps", _ => null,
            };
            if (attribute is null) continue;
            var @operator = change.Operator switch
            {
                EffectChangeOperator.Add => '+', EffectChangeOperator.Percent => '%',
                EffectChangeOperator.Minimum when change.Value != -1 => 'n', EffectChangeOperator.Maximum when change.Value != -1 => 'x', _ => '\0',
            };
            if (@operator == '\0') return null;
            changes.Add(new(attribute, @operator, change.Value));
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
