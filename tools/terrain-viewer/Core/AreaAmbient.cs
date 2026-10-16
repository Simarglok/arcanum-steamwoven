using System.Collections.ObjectModel;
using Arcanum.Data.Area;
using Arcanum.Data.Lighting;
using Arcanum.Simulation.Clock;

namespace Arcanum.TerrainViewer.Core;

/// <summary>Which ambient channel tints a drawn ART (FR-LIGHT-006).</summary>
public enum AreaAmbientChannel { Outdoor, Indoor, Unlit }

/// <summary>
/// ART-type rule of the pinned CE palette callback (light.c, ambient tint per art type): tiles and
/// facades take indoor when their type bit is 0 and outdoor otherwise; wall, critter, portal,
/// scenery, container, roof, monster and unique-NPC take outdoor; items only in ground disposition;
/// interface, misc, light and eye-candy are not tinted. Independently written; parity NOT_VERIFIED.
/// </summary>
public static class AreaAmbientPolicy
{
    public static AreaAmbientChannel Channel(uint rawArt) => (rawArt >> 28) switch
    {
        0 => ((rawArt >> 8) & 1) == 0 ? AreaAmbientChannel.Indoor : AreaAmbientChannel.Outdoor,
        11 => ((rawArt >> 25) & 1) == 0 ? AreaAmbientChannel.Indoor : AreaAmbientChannel.Outdoor,
        1 or 2 or 3 or 4 or 7 or 10 or 12 or 13 => AreaAmbientChannel.Outdoor,
        6 => ((rawArt >> 12) & 3) == 0 ? AreaAmbientChannel.Outdoor : AreaAmbientChannel.Unlit,
        _ => AreaAmbientChannel.Unlit,
    };

    /// <summary>Channel of every drawn command, keyed by its visual. Terrain commands carry the
    /// terrain record key; body commands carry their selected visual key.</summary>
    public static IReadOnlyDictionary<AreaVisualKey, AreaAmbientChannel> Channels(AreaSnapshot snapshot, AreaScenePlan plan)
        => Channels(plan, snapshot.Terrain, snapshot.Visuals);

    // Value-input seam shared with synthetic tests.
    public static IReadOnlyDictionary<AreaVisualKey, AreaAmbientChannel> Channels(AreaScenePlan plan,
        IEnumerable<AreaTerrainRecord> terrain, IEnumerable<AreaVisualRecord> visuals)
    {
        var raw = new Dictionary<AreaVisualKey, uint>();
        foreach (var tile in terrain) raw[tile.Key] = tile.RawArt;
        foreach (var visual in visuals) raw.TryAdd(visual.Key, visual.RawArt);
        var result = new Dictionary<AreaVisualKey, AreaAmbientChannel>();
        foreach (var command in plan.Commands)
        {
            if (!raw.TryGetValue(command.Visual, out var art)) throw new InvalidOperationException("AREA_AMBIENT_VISUAL_REQUIRED");
            result[command.Visual] = Channel(art);
        }
        return new ReadOnlyDictionary<AreaVisualKey, AreaAmbientChannel>(result);
    }
}

/// <summary>Area lighting attached to one load generation: the detached source and per-command channels.</summary>
public sealed record AreaLightingPlan(AreaLightingSource Source, IReadOnlyDictionary<AreaVisualKey, AreaAmbientChannel> Channels);

/// <summary>
/// Viewer wiring of day/night: one WORLD-004 clock owner bootstrapped from the module's startup
/// declaration, advanced only by explicit whole-hour requests, and the Data hourly selection for the
/// active sector. Caller-serialized like the clock. No wall-clock flow, scheduler or save state.
/// </summary>
public sealed class AreaAmbientDriver : IDisposable
{
    public const string ClockPolicy = "steamwoven-normalized-clock-v1";
    public const string ClockSourceRevision = "a7ff41b300ef712f0e7d088183a3d08957110cdd";
    public const long HourMilliseconds = 3_600_000;
    private readonly GameClockOwner _clock;
    private readonly AmbientLightingState _state;
    private readonly AreaLightingSource _source;

    private AreaAmbientDriver(AreaLightingSource source, GameClockOwner clock, AmbientLightingState state, ClockSnapshot snapshot)
    { _source = source; _clock = clock; _state = state; Clock = snapshot; }

    public ClockSnapshot Clock { get; private set; }
    public AmbientSelection Selection => _state.Current ?? throw new InvalidOperationException("AREA_AMBIENT_NOT_APPLIED");
    public long Revision => _state.Revision;
    /// <summary>Viewer toggle: off shows the unlit source pixels (neutral pair).</summary>
    public bool Enabled { get; set; } = true;
    public AmbientPair Effective => Enabled ? Selection.Pair : AmbientPair.Neutral;

    public static AreaAmbientDriver Create(AreaLightingSource source, long activeSector)
    {
        ArgumentNullException.ThrowIfNull(source);
        var profile = new ProfileBinding(1, ClockPolicy, ClockSourceRevision, source.ModuleSha256,
            source.Startup.PayloadSha256, source.Startup.Year, source.Startup.Hour);
        var boot = GameClockOwner.Bootstrap(profile);
        if (boot.Disposition != ClockBootstrapDisposition.Ready) throw new InvalidOperationException("AREA_AMBIENT_CLOCK_" + boot.Failure);
        var state = new AmbientLightingState(source);
        var driver = new AreaAmbientDriver(source, boot.Owner!, state, boot.Snapshot!.Value);
        driver.Apply(activeSector);
        return driver;
    }

    public static long SectorOf(long worldX, long worldY) => new AreaSectorCoordinate(worldX / 64, worldY / 64).Packed;

    /// <summary>Advance the clock by whole game hours; the selection follows the new hour at once.
    /// The sector is checked first, so a refusal leaves clock and selection unchanged together.</summary>
    public bool AdvanceHours(int hours, long activeSector)
    {
        if (hours <= 0) throw new ArgumentOutOfRangeException(nameof(hours));
        var resolve = _source.TryResolveScheme(activeSector, out _, out _);
        if (resolve != LightingFailure.None) throw new InvalidOperationException("AREA_AMBIENT_" + resolve);
        var result = _clock.AdvanceGameTime(Clock.Token, checked(hours * HourMilliseconds), ClockAdvanceReason.OrdinaryAdvance);
        if (result.Disposition == ClockMutationDisposition.Rejected) throw new InvalidOperationException("AREA_AMBIENT_CLOCK_" + result.Failure);
        Clock = result.Snapshot;
        return Apply(activeSector);
    }

    /// <summary>Re-select for the active sector at the current hour (FR-LIGHT-007 sector change).</summary>
    public bool Apply(long activeSector)
    {
        var failure = _state.Apply(Clock.Hour, activeSector, out var changed);
        if (failure != LightingFailure.None) throw new InvalidOperationException("AREA_AMBIENT_" + failure);
        return changed;
    }

    public void Dispose() => _clock.Dispose();
}
