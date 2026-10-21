using Arcanum.Data.Lighting;
using Arcanum.Session;
using Arcanum.Simulation.Clock;
using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-RENDER-010: day/night in crash-site-area; the diagnostic palette can switch the ambient
// multiplier off to show unlit source pixels. The active sector follows the actor's committed cell.
// ARC-WORLD-010: the clock is the game session's. It flows by itself each frame (GameTimeFlow, 8x
// admitted host time), holds a Menu pause while the palette or a dialog is open, and the palette's
// "+1 hour" is an explicit wait that works during that pause. Automated runs (self-test, native smoke)
// get no live flow; they drive the same tick with literal host times.
// ARC-RENDER-012: each map (exterior, cave) brings its own ambient source and stationary lights; a transfer
// or a quick load across maps rebinds them on the same session clock.
public partial class TerrainViewerApp
{
    private AreaAmbientDriver? _areaAmbient;
    // ARC-WORLD-039: the event world the clock's pump asks; it reads the viewer's current combat and conversation hosts.
    private AreaEventWorld? _areaEventWorld;
    private AreaEventWorld AreaEvents => _areaEventWorld ??= new AreaEventWorld(() => _combatHost, () => _conversationHost, VisitorCallHost);

    private void StartAreaAmbient(AreaLoadResult area)
    {
        StopAreaAmbient();
        if (area.Lighting is not { } lighting || _areaSession is null) return;
        _areaAmbient = AreaAmbientDriver.Create(lighting.Source, _areaSession, ActiveAreaSector(), AreaEvents);
        PushAreaAmbient(); BindAreaAmbientPalette();
        GD.Print("ARC-RENDER-010 lighting-ready " + AreaAmbientReceipt());
    }

    // ARC-WORLD-011 quick load replaced the session: the day/night driver binds to the loaded clock (its time,
    // pause and the lighting it carries) and keeps the viewer's lighting toggle. The sector is the last selected
    // one until the host re-applies for the loaded actor (FollowAreaAmbient after the actor is placed).
    private void RebindAreaAmbient(GameSessionOwner session)
    {
        if (_areaAmbient is not { } previous || (_exteriorArea ?? _readyArea)?.Lighting is not { } lighting) return;
        // The source of the map shown so far (the exterior's or, after ARC-RENDER-012, the cave's); the map of the
        // loaded actor is bound once it is placed (RebindAreaMapAmbient).
        _areaAmbient = AreaAmbientDriver.Create(previous.Source, session, previous.Selection.Sector, AreaEvents);
        _areaAmbient.Enabled = previous.Enabled;
        PushAreaAmbient(); BindAreaAmbientPalette();
        GD.Print("ARC-WORLD-010 clock-rebound " + AreaAmbientReceipt());
    }

    private void StopAreaAmbient()
    {
        _areaAmbient = null;
        _canvas?.SetAreaAmbient(null); BindAreaAmbientPalette();
    }

    // The sector the lighting follows: the actor's, on the shown map (each map has its own source).
    private long AmbientSector() => ActiveAreaSector();

    // The sector under the actor's committed cell on the shown map's own chart (the exterior spawn
    // before the first session exists).
    private long ActiveAreaSector()
    {
        var cell = _worldActor.Session is AreaGridSession session ? session.Chart.ToWorld(session.State.CommittedCell)
            : _readyArea is { } area ? area.Plan.Chart.ToWorld(area.Plan.Chart.Spawn) : AreaGridSession.Cell(AreaGridSession.Spawn);
        return AreaAmbientDriver.SectorOf(cell.X, cell.Y);
    }

    // ARC-RENDER-031: animation time of the lights since the map loaded (ledger LT6); it stands still while the game pauses.
    private long _lightAnimationMs;
    private string? _lightAnimationKey;

    private AreaLocalLighting? LightsNow(AreaLocalLighting? local, AreaAmbientDriver driver)
    {
        if (local is null) return null;
        var hour = local.AtHour(driver.Selection.Hour);
        _lightAnimationKey = hour.AnimationKey(_lightAnimationMs);
        return hour.AtAnimation(_lightAnimationMs);
    }

    /// <summary>Per frame: the animation time runs unless the clock pauses; a light that changed its frame or offsets pushes
    /// a new view.</summary>
    private void TickLightAnimation(double delta)
    {
        // Automated runs drive the animation time themselves, as they drive the clock (TickAreaClock).
        if (SelfTestBootstrap || _nativeSmoke) return;
        if (_areaAmbient is not { } driver || _readyArea?.Lighting?.Local is not { } local || local.Field.Animated.Count == 0) return;
        if (!driver.Clock.PauseDisposition.Paused) _lightAnimationMs += (long)Math.Round(delta * 1000);
        if (local.AtHour(driver.Selection.Hour).AnimationKey(_lightAnimationMs) != _lightAnimationKey) PushAreaAmbient();
    }

    private void TickAreaClock()
    {
        if (_areaAmbient is null || SelfTestBootstrap || _nativeSmoke) return;
        TickAreaClock((long)Time.GetTicksMsec());
    }

    // One production tick at the given host time; the frame loop passes the monotonic engine clock.
    private void TickAreaClock(long hostMilliseconds)
    {
        if (_areaAmbient is not { } driver) return;
        var paused = driver.ModalPaused;
        var hourChanged = driver.Tick(hostMilliseconds, WorldModal, AmbientSector());
        LogAreaEvents(driver);
        if (hourChanged) PushAreaAmbient();
        if (hourChanged || paused != driver.ModalPaused)
        {
            BindAreaAmbientPalette();
            GD.Print("ARC-WORLD-010 clock " + AreaAmbientReceipt());
        }
    }

    // FR-LIGHT-007: re-select when the actor commits into another sector, at the same hour.
    private void FollowAreaAmbient()
    {
        if (_areaAmbient is null) return;
        if (_areaAmbient.Apply(ActiveAreaSector())) { PushAreaAmbient(); GD.Print("ARC-RENDER-010 lighting-sector " + AreaAmbientReceipt()); }
    }

    // ARC-RENDER-012: a map transfer (or a quick load onto the other map) switches to the shown map's ambient
    // source and lights on the same clock (the cave has its own map default scheme and stationary lights).
    private void RebindAreaMapAmbient()
    {
        if (_areaAmbient is null) return;
        // A map without lighting inputs is drawn unlit; the driver never stays bound to the other map.
        if (_readyArea?.Lighting is not { } lighting) { StopAreaAmbient(); GD.Print("ARC-RENDER-012 lighting-map none (unlit)"); return; }
        _areaAmbient.Rebind(lighting.Source, ActiveAreaSector());
        PushAreaAmbient(); BindAreaAmbientPalette();
        GD.Print("ARC-RENDER-012 lighting-map " + AreaAmbientReceipt());
    }

    // Off sends no ambient at all (identical white pixels), so unlit views keep terrain chunks
    // even where a chunk mixes indoor and outdoor tiles. Local lights are part of the lit view only.
    private void PushAreaAmbient() => _canvas?.SetAreaAmbient(_areaAmbient is { Enabled: true } driver && _readyArea?.Lighting is { } lighting
        ? new AreaAmbientView(driver.Effective, lighting.Channels)
        {
            // ARC-RENDER-030: the hour picks the night or the day form of the object lights; ARC-RENDER-031: the animated
            // lights stand where the animation time puts them.
            Local = LightsNow(lighting.Local, driver)?.View(_readyArea.Plan, lighting.Channels, driver.Effective),
            // ARC-WORLD-017: companion bodies are lit where they stand on the shown map, by their own visual. One
            // delegate reads the current pair and map at draw time, so equal views stay equal.
            Moving = _companionLight ??= command => _areaAmbient is { Enabled: true } current && _readyArea?.Lighting is { } shown
                ? _areaCompanions?.Light(command, current.Effective, LightsNow(shown.Local, current)) : null,
        } : null);
    private Func<AreaDrawCommand, AreaLitColors?>? _companionLight;

    // Palette actions (ARC-INPUT-001 route: diagnostics live in the palette, not on game keys).
    private void PaletteGameHour()
    {
        if (_areaAmbient is null) { _worldPanel?.SetActionStatus("Game time unavailable: no area lighting loaded"); return; }
        _areaAmbient.AdvanceHours(1, AmbientSector());
        LogAreaEvents(_areaAmbient);
        AreaAmbientChanged($"Waited 1 hour: {_areaAmbient.Clock.Hour:00}:00");
    }
    private void PaletteLightingToggle()
    {
        if (_areaAmbient is null) { _worldPanel?.SetActionStatus("Ambient lighting unavailable: no area lighting loaded"); return; }
        _areaAmbient.Enabled = !_areaAmbient.Enabled;
        AreaAmbientChanged(_areaAmbient.Enabled ? "Ambient lighting ON" : "Ambient lighting OFF (unlit source pixels)");
    }
    private void AreaAmbientChanged(string action)
    {
        PushAreaAmbient(); BindAreaAmbientPalette(); _worldPanel?.SetActionStatus(action); RefreshReadyStatus();
        GD.Print("ARC-RENDER-010 lighting-input " + AreaAmbientReceipt());
    }
    private void BindAreaAmbientPalette()
    {
        if (_worldPanel is null || !GodotObject.IsInstanceValid(_worldPanel) || !_worldPanel.IsInsideTree()) return;
        _worldPanel.SetLightingStatus(_areaAmbient is null ? "Lighting: not loaded for this mode (unlit)." : "Lighting:" + AreaAmbientStatus().TrimStart(' ', '|'));
    }

    private string AreaAmbientStatus()
    {
        if (_areaAmbient is not { } driver) return " | lighting: not loaded (unlit)";
        var clock = driver.Clock; var selection = driver.Selection;
        var flow = driver.ModalPaused ? "PAUSED (dialog/palette/inventory open)" : clock.PauseDisposition.Paused ? $"PAUSED ({clock.PauseDisposition.Reason})"
            : $"time flows {GameTimeFlow.GameMillisecondsPerHostMillisecond}x";
        return $" | {clock.Hour:00}:{clock.Minute:00} day {clock.DayIndex + 1} {(clock.IsDay ? "DAY" : "NIGHT")} {flow} | ambient scheme {selection.SchemeId}"
            + $"{(selection.FromMapDefault ? " (map default)" : " (sector)")} hour {selection.Hour} {(driver.Enabled ? "ON" : "OFF (unlit source)")}"
            + $" | lights {_readyArea?.Lighting?.Local?.LightCount ?? 0} (object {_readyArea?.Lighting?.Local?.ObjectLightCount ?? 0})"
            + " | palette: Game time +1 hour (wait), Ambient lighting on/off | hourly discrete, 8x rate and original night/visual parity NOT_VERIFIED"
            + (driver.Events is { } events ? " | " + events.Last.Status(clock.GameTimestampMs) : "");
    }

    // ARC-WORLD-039: a line per pump call that took events (identifiers and counts only).
    private int _loggedCallRefusals;

    private void LogAreaEvents(AreaAmbientDriver driver)
    {
        if (driver.Events?.Last is not { } last || last.Fired + last.Dropped + last.ScriptCalls == 0) return;
        ReportCompanionWaits(last.Waits);
        var refused = (_areaEventWorld?.CallRefusals ?? 0) > _loggedCallRefusals;
        _loggedCallRefusals = _areaEventWorld?.CallRefusals ?? 0;
        GD.Print($"ARC-WORLD-039 events steps={last.Steps} passes={last.Passes} fired={last.Fired} dropped={last.Dropped} calls={last.ScriptCallsRun}/{last.ScriptCalls}"
            + $" late={last.Coalesced} budgetLeft={last.BudgetLeft} pending={last.Pending}"
            + (refused ? $" refusal={_areaEventWorld!.LastCallRefusal}" : ""));
    }

    // Numbers only: scheme id, hour, sector and digests; never colour values.
    private string AreaAmbientReceipt()
    {
        var driver = _areaAmbient!; var selection = driver.Selection;
        return System.Text.Json.JsonSerializer.Serialize(new {
            driver.Clock.GameTimestampMs, driver.Clock.Hour, driver.Clock.IsDay, ClockRevision = driver.Clock.Revision,
            Paused = driver.Clock.PauseDisposition.Paused, PauseReason = driver.Clock.PauseDisposition.Reason.ToString(),
            selection.SchemeId, selection.FromMapDefault, selection.Sector, AmbientRevision = driver.Revision, driver.Enabled,
            Map = _readyArea?.Snapshot.CatalogueMap, StationaryLights = (_readyArea?.Lighting?.Local?.LightCount ?? 0) - (_readyArea?.Lighting?.Local?.ObjectLightCount ?? 0), ObjectLights = _readyArea?.Lighting?.Local?.ObjectLightCount ?? 0,
            TerrainChunks = _canvas?.AreaChunkCount ?? 0 });
    }

    // Native smoke: ordinary source-pixel gates run unlit; this phase then checks lit pixels
    // against source * ambient / 255 at several whole hours, through the same compositor path.
    private async Task RunAreaLightingSmokeAsync(AreaLoadResult area)
    {
        if (_areaAmbient is not { } driver || area.Lighting is not { } lighting) throw new InvalidOperationException("AREA_LIGHTING_REQUIRED");
        var capture = System.Environment.GetEnvironmentVariable("ARC_AREA_LIGHTING_CAPTURE");
        // Spread terrain candidates across the whole chart (fixed stride): edge cells can be
        // uniformly black, where no tint is observable.
        var lit = area.Plan.Commands.Where(command => command.Identity is null && lighting.Channels[command.Visual] != AreaAmbientChannel.Unlit).ToArray();
        var terrain = lit.Where((_, index) => index % Math.Max(1, lit.Length / 64) == 0).Take(64).ToArray();
        var bodies = area.Plan.Commands.Where(command => command.Identity is not null && !command.Blended
            && lighting.Channels[command.Visual] != AreaAmbientChannel.Unlit)
            .OrderByDescending(command => (long)command.Destination.Width * command.Destination.Height).Take(64).ToArray();
        driver.Enabled = true;
        var hours = new List<int>(); var tinted = new int[2]; var neutral = new List<string>();
        // A channel this close to white changes source pixels by at most 15/255, often within the
        // observation tolerance; such an hour is recorded as near-neutral for that group if no
        // tinted pixel is found, instead of passing vacuously. Darker hours must be observed.
        static bool NearNeutral(AmbientColor color) => color.R >= 240 && color.G >= 240 && color.B >= 240;
        // ARC-RENDER-029: the samples hold the camera, as the blend samples do (ARC-RENDER-023).
        await WithAreaSmokeCameraHeld(async () =>
        {
            foreach (var advance in new[] { 0, 6, 9, 6 })
            {
                if (advance > 0) driver.AdvanceHours(advance, ActiveAreaSector());
                var selection = driver.Selection;
                if (selection.Hour != driver.Clock.Hour || selection.Pair != lighting.Source.Schemes[selection.SchemeId].At(driver.Clock.Hour))
                    throw new InvalidOperationException("AREA_LIGHTING_HOURLY_SELECTION");
                PushAreaAmbient();
                foreach (var (group, groupIndex) in new[] { terrain, bodies }.Select((group, index) => (group, index)))
                {
                    var found = false; var rejections = new List<Dictionary<string, int>>();
                    foreach (var command in group)
                    {
                        _camera!.ResetWorld(new GamepadLab.Core.Vec2(command.Destination.X + command.Destination.Width / 2d,
                            command.Destination.Y + command.Destination.Height / 2d), WorldViewport, 1);
                        var view = _camera.State;
                        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                        using var image = GetViewport().GetTexture().GetImage();
                        if (_camera.State != view) throw new InvalidOperationException($"AREA_LIGHTING_CAMERA_MOVED {view} {_camera.State}");
                        var channel = lighting.Channels[command.Visual];
                        var ambient = channel == AreaAmbientChannel.Indoor ? selection.Pair.Indoor : selection.Pair.Outdoor;
                        if (!TryAreaPixel(command, area.Frames, _canvas!.AreaVisibleCommands, image, GetViewport(), _hud!.Rect,
                            _readyActor is { } actor ? new Rect2(actor.Destination.X - 5, actor.Destination.Y - 5, actor.Destination.Width + 10, actor.Destination.Height + 10) : null,
                            1, out _, out var diagnostics, ambient)) { rejections.Add(diagnostics.Rejections); continue; }
                        found = true; break;
                    }
                    if (found) { tinted[groupIndex]++; continue; }
                    var channels = group.Select(command => lighting.Channels[command.Visual]).Distinct()
                        .Select(channel => channel == AreaAmbientChannel.Indoor ? selection.Pair.Indoor : selection.Pair.Outdoor);
                    if (!channels.All(NearNeutral)) GD.Print("ARC-RENDER-010 lighting-rejections " + System.Text.Json.JsonSerializer.Serialize(rejections
                        .SelectMany(row => row).GroupBy(pair => pair.Key).ToDictionary(group => group.Key, group => group.Sum(pair => pair.Value))));
                    if (!channels.All(NearNeutral)) throw new InvalidOperationException($"AREA_LIGHTING_PIXEL_NOT_OBSERVED_{(groupIndex == 0 ? "TERRAIN" : "BODY")}_HOUR{driver.Clock.Hour:00}");
                    neutral.Add($"{(groupIndex == 0 ? "terrain" : "body")}@{driver.Clock.Hour:00}");
                }
                hours.Add(driver.Clock.Hour);
                if (!string.IsNullOrWhiteSpace(capture))
                {
                    _camera!.ResetWorld(WorldAnchor, WorldViewport, _worldPreferredZoom); RefreshAreaStatus();
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                    await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                    using var shot = GetViewport().GetTexture().GetImage();
                    if (shot.SavePng(System.IO.Path.Combine(capture, $"area-lighting-hour{driver.Clock.Hour:00}.png")) != Error.Ok)
                        throw new InvalidOperationException("AREA_LIGHTING_CAPTURE_FAILED");
                }
            }
            return hours.Count;
        });
        if (tinted[0] == 0 || tinted[1] == 0) throw new InvalidOperationException("AREA_LIGHTING_TINT_NEVER_OBSERVED");
        GD.Print($"ARC-RENDER-010 native-smoke lighting PASS hours={string.Join(',', hours)} tinted terrain={tinted[0]} body={tinted[1]} neutral=[{string.Join(',', neutral)}] " + AreaAmbientReceipt());
    }

    // ARC-WORLD-010 native smoke: the production tick at literal host times (no live flow in automation).
    // Time flows to the next hour boundary at 8x and the scheme follows; the palette and a dialog each hold a
    // Menu pause with no catch-up on resume; the palette's +1 hour waits during that pause; a capture carries
    // the time and applied scheme and a restore of it (a separate owner, the live session untouched) agrees.
    private void RunAreaClockSmoke()
    {
        if (_areaAmbient is not { } driver || _areaSession is not { } session || _conversationPanel is null) throw new InvalidOperationException("AREA_CLOCK_REQUIRED");
        if (WorldModal) ToggleWorldSettings();
        // After the quick-load phase the session is a restored one: the driver must be bound to it.
        if (!ReferenceEquals(driver.Clock.OwnerIdentity, session.QueryClockSnapshot().OwnerIdentity)) throw new InvalidOperationException("AREA_CLOCK_DRIVER_NOT_REBOUND");
        var host = 10_000_000L;
        TickAreaClock(host);
        var start = driver.Clock;
        if (start.PauseDisposition.Paused) throw new InvalidOperationException("AREA_CLOCK_PAUSED_AT_START");
        var remaining = AreaAmbientDriver.HourMilliseconds - start.MillisecondOfDay % AreaAmbientDriver.HourMilliseconds;
        var frames = 0;
        while (driver.Clock.Hour == start.Hour)
        {
            if (++frames > 2_000) throw new InvalidOperationException("AREA_CLOCK_DID_NOT_FLOW");
            TickAreaClock(host += 250);
        }
        var flowed = driver.Clock;
        if (frames != (remaining + 1_999) / 2_000 || flowed.GameTimestampMs != start.GameTimestampMs + frames * 2_000L
            || driver.Selection.Hour != flowed.Hour || session.QueryLighting() != new SessionLightingState(driver.Selection.SchemeId, flowed.Hour))
            throw new InvalidOperationException($"AREA_CLOCK_HOUR_FLOW frames={frames} remaining={remaining}");

        ToggleWorldSettings();
        if (!PaletteOpen) throw new InvalidOperationException("AREA_CLOCK_PALETTE_NOT_OPEN");
        for (var i = 0; i < 400; i++) TickAreaClock(host += 250);
        var palette = driver.Clock;
        if (!driver.ModalPaused || palette.GameTimestampMs != flowed.GameTimestampMs) throw new InvalidOperationException("AREA_CLOCK_PALETTE_NOT_PAUSED");
        PaletteGameHour();
        var waited = driver.Clock;
        if (waited.GameTimestampMs != flowed.GameTimestampMs + AreaAmbientDriver.HourMilliseconds || !driver.ModalPaused
            || session.QueryLighting() != new SessionLightingState(driver.Selection.SchemeId, waited.Hour)) throw new InvalidOperationException("AREA_CLOCK_WAIT");
        ToggleWorldSettings();
        TickAreaClock(host += 100);
        var resumed = driver.Clock;
        if (driver.ModalPaused || resumed.GameTimestampMs != waited.GameTimestampMs + 800) throw new InvalidOperationException("AREA_CLOCK_PALETTE_CATCHUP");

        _conversationPanel.Open("ARC-WORLD-010 smoke", null, "[clock pause check]");
        for (var i = 0; i < 400; i++) TickAreaClock(host += 250);
        var dialog = driver.Clock;
        if (!ConversationOpen || !driver.ModalPaused || dialog.GameTimestampMs != resumed.GameTimestampMs) throw new InvalidOperationException("AREA_CLOCK_DIALOG_NOT_PAUSED");
        EndConversation();
        TickAreaClock(host += 100);
        var after = driver.Clock;
        if (driver.ModalPaused || after.GameTimestampMs != resumed.GameTimestampMs + 800) throw new InvalidOperationException("AREA_CLOCK_DIALOG_CATCHUP");

        var snapshot = session.Capture();
        var text = GameSessionSnapshotCodec.Encode(snapshot);
        var decoded = GameSessionSnapshotCodec.TryDecode(text);
        if (decoded.Snapshot is not { } saved || saved.Clock.GameTimestampMs != after.GameTimestampMs || saved.Lighting != session.QueryLighting())
            throw new InvalidOperationException("AREA_CLOCK_SNAPSHOT " + decoded.Failure);
        var restored = GameSessionOwner.Restore(saved, new(saved.Identity, after.ProfileBinding, new(Guid.NewGuid())));
        using (var owner = restored.Owner ?? throw new InvalidOperationException("AREA_CLOCK_RESTORE_" + restored.Failure))
            if (owner.QueryClockSnapshot().GameTimestampMs != after.GameTimestampMs || owner.QueryLighting() != saved.Lighting)
                throw new InvalidOperationException("AREA_CLOCK_RESTORE_STATE");
        GD.Print("ARC-WORLD-010 native-smoke clock PASS " + System.Text.Json.JsonSerializer.Serialize(new {
            Start = start.GameTimestampMs, FlowFrames = frames, Flowed = flowed.GameTimestampMs, FlowedHour = flowed.Hour,
            PaletteWait = waited.GameTimestampMs, Resumed = resumed.GameTimestampMs, DialogHeld = dialog.GameTimestampMs,
            After = after.GameTimestampMs, saved.Lighting, SnapshotSchema = saved.SchemaVersion, SnapshotChars = text.Length }));
    }
}
