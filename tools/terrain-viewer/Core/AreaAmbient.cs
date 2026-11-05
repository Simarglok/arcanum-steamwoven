using System.Collections.ObjectModel;
using Arcanum.Data.Area;
using Arcanum.Data.Lighting;
using Arcanum.Session;
using Arcanum.Simulation.Clock;
using Arcanum.World;

namespace Arcanum.TerrainViewer.Core;

/// <summary>Which ambient channel tints a drawn ART (FR-LIGHT-006).</summary>
public enum AreaAmbientChannel { Outdoor, Indoor, Unlit }

/// <summary>
/// ART-type rule of the source's palette callback (ambient tint per art type, docs/rendering/area-lighting.md): tiles and
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
            // ARC-RENDER-018, ARC-RENDER-026: an additive, multiply or constant-alpha body is drawn with its base palette.
            result[command.Visual] = command.Unlit ? AreaAmbientChannel.Unlit : Channel(art);
        }
        return new ReadOnlyDictionary<AreaVisualKey, AreaAmbientChannel>(result);
    }
}

/// <summary>Area lighting attached to one load generation: the detached source and per-command channels.</summary>
public sealed record AreaLightingPlan(AreaLightingSource Source, IReadOnlyDictionary<AreaVisualKey, AreaAmbientChannel> Channels)
{
    /// <summary>ARC-RENDER-012 stationary lights and tile channels of this map; absent for fixtures without them.</summary>
    public AreaLocalLighting? Local { get; init; }
    /// <summary>ARC-RENDER-030: why the object lights are not part of <see cref="Local"/> (None when they are, or the map has
    /// none).</summary>
    public LightingFailure ObjectLightsFailure { get; init; }
}

/// <summary>
/// Viewer wiring of day/night over the session clock (ARC-WORLD-010): the lighting reads and advances the
/// <see cref="GameSessionOwner"/>'s clock, never one of its own, so lighting, HUD and snapshot see one time.
/// <see cref="Tick"/> lets time flow from host milliseconds through the world runtime's frame step (<see cref="FrameStep"/>,
/// MIG12 S4: the session owns the three clocks and admits the frame, the step spends it at the event deadlines) and holds a
/// Menu pause while the Esc menu (or a save or load list it opens) is open, releasing only a pause it holds (a Menu pause already on the
/// session when the driver binds, as after a restore, counts as held); <see cref="AdvanceHours"/> is an explicit wait that also works
/// while paused. After every clock change the Data hourly selection for the active sector is re-applied
/// and recorded in the session, so a capture carries the time and the scheme selected for it. The session is
/// owned by the caller and outlives this driver. Caller-serialized like the session.
/// </summary>
public sealed class AreaAmbientDriver
{
    public const string ClockPolicy = "steamwoven-normalized-clock-v1";
    public const string ClockSourceRevision = GameClockOwner.RetailExecutableSha256;
    public const long HourMilliseconds = 3_600_000;
    private readonly GameSessionOwner _session;
    private AmbientLightingState _state;
    private AreaLightingSource _source;
    private bool _holdsPause;
    private readonly bool _runsEvents;

    private AreaAmbientDriver(AreaLightingSource source, GameSessionOwner session, AmbientLightingState state, FrameStep frame)
    {
        _source = source; _session = session; _state = state; Frame = frame; _runsEvents = frame.RunsEvents;
        _holdsPause = session.QueryClockSnapshot().PauseDisposition == new PauseDisposition(true, PauseReason.Menu);
    }

    public ClockSnapshot Clock => _session.QueryClockSnapshot();
    /// <summary>MIG12 S4: the frame step that moves the session's clocks (and its event passes and participants); it always exists.</summary>
    public FrameStep Frame { get; }
    /// <summary>ARC-WORLD-039: the frame step when it runs the session's event queue, or null when time moves without
    /// event passes (a host that runs no gameplay events).</summary>
    public FrameStep? Events => _runsEvents ? Frame : null;
    /// <summary>The lighting source currently bound (ARC-RENDER-012: the shown map's).</summary>
    public AreaLightingSource Source => _source;
    public AmbientSelection Selection => _state.Current ?? throw new InvalidOperationException("AREA_AMBIENT_NOT_APPLIED");
    private long _revisionBase;
    public long Revision => _revisionBase + _state.Revision;
    /// <summary>Viewer toggle: off shows the unlit source pixels (neutral pair). Not session state.</summary>
    public bool Enabled { get; set; } = true;
    public AmbientPair Effective => Enabled ? Selection.Pair : AmbientPair.Neutral;
    /// <summary>True while this driver holds the session clock in a Menu pause for the Esc menu.</summary>
    public bool MenuPaused => _holdsPause && Clock.PauseDisposition == new PauseDisposition(true, PauseReason.Menu);

    /// <summary>The clock profile of the area lighting source's module and startup declaration.</summary>
    public static ProfileBinding ClockProfile(AreaLightingSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return ClockProfile(source.ModuleSha256, source.Startup);
    }

    /// <summary>ARC-WORLD-057: the same profile from the module's identity and start clock alone (an unlit map's session),
    /// equal to a lit map's of that module, so a session carries between them.</summary>
    public static ProfileBinding ClockProfile(string moduleSha256, StartupClockDeclaration startup)
    {
        ArgumentNullException.ThrowIfNull(moduleSha256); ArgumentNullException.ThrowIfNull(startup);
        return new(1, ClockPolicy, ClockSourceRevision, moduleSha256, startup.PayloadSha256, startup.Year, startup.Hour);
    }

    /// <summary>Binds to a session whose clock was started from this source's profile and applies the
    /// current hour for the active sector. Every frame goes through a <see cref="FrameStep"/>; with <paramref name="events"/> it runs the
    /// event passes over that world (ARC-WORLD-039), through the cast host <paramref name="spells"/>
    /// answers when it answers one (ARC-MAGIC-015); <paramref name="placePing"/> is the change-of-place ping that opens each frame (TM12).</summary>
    public static AreaAmbientDriver Create(AreaLightingSource source, GameSessionOwner session, long activeSector, ISessionEventWorld? events = null,
        Func<SessionSpellCaster?>? spells = null, Action? placePing = null)
    {
        ArgumentNullException.ThrowIfNull(source); ArgumentNullException.ThrowIfNull(session);
        if (session.QueryClockSnapshot().ProfileBinding != ClockProfile(source)) throw new InvalidOperationException("AREA_AMBIENT_CLOCK_PROFILE");
        // A new driver's first frame admits nothing, as the driver's own admission did before the session owned it (MIG12 S3).
        session.ResetFrameReference();
        var driver = new AreaAmbientDriver(source, session, new AmbientLightingState(source), new FrameStep(session, events, spells: spells, placePing: placePing));
        driver.Apply(activeSector);
        return driver;
    }

    public static long SectorOf(long worldX, long worldY) => new AreaSectorCoordinate(worldX / 64, worldY / 64).Packed;

    /// <summary>One host frame: hold or release the Menu pause for <paramref name="menuOpen"/> (MIG12 S7, GQ3 A: only the Esc menu and the lists it opens
    /// pause; a dialog, inventory, sheet, barter, logbook, world map, the palette and the bug report do not), then run the frame step at the host
    /// stamp (MIG12 S4: the session admits the interval, the step spends it on real, game and animation time together at the event
    /// deadlines, a paused clock admits no game or animation time, so a resume has no catch-up). <paramref name="frozen"/> is a frame in
    /// which the original does not ping (TL25). Returns true when the applied selection changed. A pause of another reason is left alone.</summary>
    public bool Tick(long hostMilliseconds, bool menuOpen, long activeSector, bool frozen = false)
    {
        var resolve = _source.TryResolveScheme(activeSector, out _, out _);
        if (resolve != LightingFailure.None) throw new InvalidOperationException("AREA_AMBIENT_" + resolve);
        var clock = Clock;
        if (menuOpen && !clock.PauseDisposition.Paused) { clock = Mutate(_session.SetPaused(clock.Token, new(true, PauseReason.Menu))); _holdsPause = true; }
        else if (!menuOpen && MenuPaused) { clock = Mutate(_session.SetPaused(clock.Token, default)); _holdsPause = false; }
        else if (!MenuPaused) _holdsPause = false;
        return Frame.Run(hostMilliseconds, frozen).HourChanged && Apply(activeSector);
    }

    /// <summary>Explicit wait of whole game hours (FR-LIGHT-002), also while paused; the selection follows the
    /// new hour at once. The sector is checked first, so a refusal leaves clock and selection unchanged together.</summary>
    public bool AdvanceHours(int hours, long activeSector)
    {
        if (hours <= 0) throw new ArgumentOutOfRangeException(nameof(hours));
        return AdvanceMilliseconds(checked(hours * HourMilliseconds), activeSector);
    }

    /// <summary>A jump of game time in milliseconds (ARC-UI-048, a world-map travel step), also while paused: as
    /// <see cref="AdvanceHours"/>, with the events that came due fired once, late; the selection follows the new hour.</summary>
    public bool AdvanceMilliseconds(long gameMs, long activeSector)
    {
        if (gameMs <= 0) throw new ArgumentOutOfRangeException(nameof(gameMs));
        var resolve = _source.TryResolveScheme(activeSector, out _, out _);
        if (resolve != LightingFailure.None) throw new InvalidOperationException("AREA_AMBIENT_" + resolve);
        Frame.Jump(gameMs);
        return Apply(activeSector);
    }

    /// <summary>Re-select for the active sector at the current hour (FR-LIGHT-007 sector change) and record
    /// the applied scheme in the session.</summary>
    public bool Apply(long activeSector)
    {
        var failure = _state.Apply(Clock.Hour, activeSector, out var changed);
        if (failure != LightingFailure.None) throw new InvalidOperationException("AREA_AMBIENT_" + failure);
        var recorded = _session.RecordLighting(new(Selection.SchemeId, Selection.Hour));
        if (recorded.Disposition == GameSessionDisposition.Rejected) throw new InvalidOperationException("AREA_AMBIENT_RECORD_" + recorded.Failure);
        return changed;
    }

    /// <summary>ARC-RENDER-012: switch to another map's lighting source (a map transfer or a quick load
    /// across maps) on the same session clock, selecting and recording its scheme for the active sector at the
    /// current hour. The source must share the clock's profile (module and startup declaration); on a refusal
    /// the previous source stays.</summary>
    public void Rebind(AreaLightingSource source, long activeSector)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (ReferenceEquals(source, _source)) { Apply(activeSector); return; }
        if (ClockProfile(source) != Clock.ProfileBinding) throw new InvalidOperationException("AREA_AMBIENT_SOURCE_MISMATCH");
        var state = new AmbientLightingState(source);
        var failure = state.Apply(Clock.Hour, activeSector, out _);
        if (failure != LightingFailure.None) throw new InvalidOperationException("AREA_AMBIENT_" + failure);
        _revisionBase = Revision; _source = source; _state = state;
        Apply(activeSector);
    }

    private static ClockSnapshot Mutate(ClockMutationResult result) => result.Disposition == ClockMutationDisposition.Rejected
        ? throw new InvalidOperationException("AREA_AMBIENT_CLOCK_" + result.Failure) : result.Snapshot;
}
