using System.Text.Json;

namespace Arcanum.Data.Hd;

/// <summary>This device's HD texture limits (ARC-RENDER-013, Budgets, as amended by
/// ARC-DATA-014). The contract's limits are ceilings: a device may lower any of them, 0
/// turns HD off in that domain (every frame there draws Legacy with <c>HdBudget</c>), and
/// nothing may raise one above its ceiling. Frame-count caps are not part of it; they stay
/// the Legacy ones. Legacy budgets are never affected.</summary>
/// <param name="AreaVisibleBytes">HD bytes of the AREA visible set.</param>
/// <param name="AreaRetainedFrames">HD frames kept after they leave the view.</param>
/// <param name="AreaRetainedBytes">HD bytes kept after they leave the view.</param>
/// <param name="WorldBytes">HD bytes of WORLD actor and static textures.</param>
/// <param name="TerrainBytes">HD terrain chunks of the AREA, visible and kept (ARC-RENDER-016).</param>
public sealed record HdDeviceBudget(long AreaVisibleBytes, int AreaRetainedFrames, long AreaRetainedBytes, long WorldBytes, long TerrainBytes)
{
    /// <summary>Legacy limits the HD ceilings shadow (ARC-RENDER-013 table).</summary>
    public const long LegacyAreaVisibleBytes = 81_853_096;
    public const int LegacyAreaRetainedFrames = 512;
    public const long LegacyAreaRetainedBytes = 32L * 1024 * 1024;
    public const long LegacyWorldBytes = 256L * 1024 * 1024;
    /// <summary>The Legacy <c>AreaChunkTextureCache</c> byte limit; at 512² × 4 B it holds
    /// 192 chunks, so the N² ceiling keeps the same 192 chunks resident (768 MiB at 2×,
    /// 3 GiB at 4×).</summary>
    public const long LegacyTerrainBytes = 192L * 1024 * 1024;

    /// <summary>The contract ceilings at scale <paramref name="scale"/>: N² × the AREA and
    /// terrain Legacy byte limits, the Legacy retained frame count, and a WORLD HD limit of
    /// its own (not shared with Legacy) of N²/4 × 256 MiB, i.e. 256 MiB at 2× as
    /// ARC-RENDER-013 set it. At N = 4: 1,309,649,536 B visible, 512 frames / 512 MiB
    /// retained, 1 GiB WORLD, 3 GiB terrain (192 chunks of 16 MiB). These bound the worst
    /// case; the Crash Site exterior's 1,975 distinct frames are about 400 MiB at 4×
    /// (ARC-RES-033 census × 16).</summary>
    public static HdDeviceBudget Ceiling(int scale)
    {
        if (scale is < 2 or > 16) throw new ArgumentOutOfRangeException(nameof(scale));
        long square = scale * scale;
        return new(square * LegacyAreaVisibleBytes, LegacyAreaRetainedFrames, square * LegacyAreaRetainedBytes, square * LegacyWorldBytes / 4,
            square * LegacyTerrainBytes);
    }

    /// <summary>Each value clamped into 0..its ceiling.</summary>
    public HdDeviceBudget Clamped(int scale)
    {
        var c = Ceiling(scale);
        return new(Math.Clamp(AreaVisibleBytes, 0, c.AreaVisibleBytes), Math.Clamp(AreaRetainedFrames, 0, c.AreaRetainedFrames),
            Math.Clamp(AreaRetainedBytes, 0, c.AreaRetainedBytes), Math.Clamp(WorldBytes, 0, c.WorldBytes), Math.Clamp(TerrainBytes, 0, c.TerrainBytes));
    }
}

/// <summary>The device budget file (<c>user://hd_budget_v1.json</c> in the game; the host
/// owns the storage): <c>{"schema":1, "areaVisibleBytes":…, "areaRetainedFrames":…,
/// "areaRetainedBytes":…, "worldBytes":…, "terrainBytes":…}</c>, every limit optional.
/// A missing, unreadable, malformed or unknown-schema file loads as the ceiling; a missing
/// field takes its ceiling; a value above its ceiling is clamped to it.</summary>
public static class HdDeviceBudgetCodec
{
    private static readonly string[] Fields = ["areaVisibleBytes", "areaRetainedFrames", "areaRetainedBytes", "worldBytes", "terrainBytes"];

    public static string Serialize(HdDeviceBudget budget, int scale)
    {
        ArgumentNullException.ThrowIfNull(budget);
        var b = budget.Clamped(scale);
        return JsonSerializer.Serialize(new Dictionary<string, long>
        {
            ["schema"] = 1, ["areaVisibleBytes"] = b.AreaVisibleBytes, ["areaRetainedFrames"] = b.AreaRetainedFrames,
            ["areaRetainedBytes"] = b.AreaRetainedBytes, ["worldBytes"] = b.WorldBytes, ["terrainBytes"] = b.TerrainBytes,
        });
    }

    public static HdDeviceBudget DeserializeOrCeiling(string? json, int scale)
    {
        var ceiling = HdDeviceBudget.Ceiling(scale);
        if (string.IsNullOrWhiteSpace(json) || json.Length > 4096) return ceiling;
        try
        {
            using var document = JsonDocument.Parse(json, HdJson.Options);
            var root = HdJson.Object(document.RootElement, "budget", ["schema"], Fields);
            if (HdJson.Long(root["schema"], "budget.schema", 1, 1) != 1) return ceiling;
            long Value(string name, long fallback) => root.TryGetValue(name, out var v) ? HdJson.Long(v, "budget." + name, 0, long.MaxValue) : fallback;
            return new HdDeviceBudget(Value("areaVisibleBytes", ceiling.AreaVisibleBytes),
                (int)Math.Min(Value("areaRetainedFrames", ceiling.AreaRetainedFrames), int.MaxValue),
                Value("areaRetainedBytes", ceiling.AreaRetainedBytes), Value("worldBytes", ceiling.WorldBytes), Value("terrainBytes", ceiling.TerrainBytes)).Clamped(scale);
        }
        catch (Exception error) when (error is JsonException or HdFormatException or ArgumentException) { return ceiling; }
    }
}
