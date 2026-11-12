using Arcanum.Data.Area;
using Arcanum.Data.Lighting;
using Arcanum.Data.Terrain;
using Arcanum.Session;
using Arcanum.Simulation.Clock;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-RENDER-010: day/night in crash-site-area; the diagnostic palette can switch the ambient
// multiplier off to show unlit source pixels. The active sector follows the actor's committed cell.
// ARC-WORLD-010: the clock is the game session's. It flows by itself each frame (the world runtime's FrameStep over the session's frame, MIG12 S4: 8x
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
    // ARC-WORLD-092: its AI ticks run through the shown map's AI host; ARC-WORLD-096: its restock events through the restock host.
    private AreaEventWorld AreaEvents => _areaEventWorld ??= new AreaEventWorld(() => _combatHost, () => _conversationHost, AdmitCallAttachee,
        () => InventoryOpen && SessionJunkPile.Number(_inventoryPanel!.Holder) is not null || ContainerLootOpen, () => _aiHost is { } ai && ReferenceEquals(ai.Session, _areaSession) ? ai : null,
        () => _combatHost is not null && _worldActor.Session is AreaGridSession ? AreaCombatWorldNow() : null, RestockHostNow);

    // MIG12 S8: the host of the real-time steps the frame step hands out (the world-map journey's travel step, the turn-based event).
    private AreaRealStepHost? _realStepHost;
    private AreaRealStepHost RealStepHost => _realStepHost ??= new AreaRealStepHost(RunWorldMapStep, RunTurnBasedStepEvent);

    private void StartAreaAmbient(AreaLoadResult area)
    {
        StopAreaAmbient();
        if (area.Lighting is not { } lighting || _areaSession is null) return;
        _areaAmbient = AreaAmbientDriver.Create(lighting.Source, _areaSession, ActiveAreaSector(), AreaEvents, SpellCasterForEvents, PingNpcChangeOfPlace);
        BindFrameParticipants(_areaAmbient);
        ResetLightAnimation(); ResetCarriedLight();
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
        _areaAmbient = AreaAmbientDriver.Create(previous.Source, session, previous.Selection.Sector, AreaEvents, SpellCasterForEvents, PingNpcChangeOfPlace);
        BindFrameParticipants(_areaAmbient);
        _areaAmbient.Enabled = previous.Enabled;
        ResetLightAnimation(); ResetCarriedLight();
        PushAreaAmbient(); BindAreaAmbientPalette();
        GD.Print("ARC-WORLD-010 clock-rebound " + AreaAmbientReceipt());
    }

    // ARC-DEV-125 (MIG12 S6): the viewer's frame participant, registered on the driver's frame step (it runs after the event passes of
    // every frame): it carries the frame's clocks to the systems that read time between frames. ARC-DEV-124 (MIG12 S5): the AI host is the
    // other one, and every frame step opens with the NPCs' change-of-place ping (TM12, the placePing given to the driver).
    private void BindFrameParticipants(AreaAmbientDriver driver) => BindFrameParticipants(driver.Frame);

    private void BindFrameParticipants(FrameStep frame)
    {
        _frameTap = new FrameClockTap();
        frame.Register(_frameTap);
        frame.Register(AiFrameParticipant);
        // MIG12 S8: the host of the real-time steps, the same one for a lit map's driver and an unlit map's.
        frame.RealSteps = RealStepHost;
        // ARC-DEV-134 (MIG13 S4): the goal runs' steps go to the shown map's AI host; ARC-DEV-138 (MIG13 S7): the doors' runs to the shown map's door host.
        frame.GoalSteps = GoalStepRouter;
    }

    private void StopAreaAmbient()
    {
        _areaAmbient = null; _frameTap = null;
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

    // ARC-RENDER-031: animation time of the lights since the map's lighting bound (ledger LT3, LT6: the original starts a
    // light's animation when the light enters its sector list at load). ARC-DEV-125: it is the frame clocks' world milliseconds since
    // the frame in which it bound, so it stands still while the game pauses, is cut with a long frame like every animation and is not
    // moved by a jump (a world-map step or a round's second does not run the lights for minutes).
    private double _lightAnimationMs;
    private string? _lightAnimationKey;
    private long? _lightAnimationBaseMs;

    private void ResetLightAnimation() { _lightAnimationMs = 0; _lightAnimationKey = null; _lightAnimationBaseMs = null; }

    // The hour's form posed at the animation time (AtHour first: a posed view keeps no day form). Pure: the draw-time
    // companion delegate calls it too.
    private AreaLocalLighting? LightsNow(AreaLocalLighting? local, AreaAmbientDriver driver) =>
        local?.AtHour(driver.Selection.Hour).AtAnimation((long)_lightAnimationMs);

    /// <summary>Per frame, after the clock's frame: the animation time follows the frame clocks (<see cref="FrameClockTap"/>); a light
    /// that changed its frame or offsets pushes a new view.</summary>
    private void TickLightAnimation()
    {
        // Automated runs drive the animation time themselves, as they drive the clock (TickAreaClock).
        if (SelfTestBootstrap || _nativeSmoke) return;
        if (_areaAmbient is not { } driver || _readyArea?.Lighting?.Local is not { } local || local.Field.Animated.Count == 0) return;
        if (_frameTap?.Clocks is not { } clocks) return;
        _lightAnimationMs = clocks.WorldMs - (_lightAnimationBaseMs ??= clocks.WorldMs);
        // With the lighting toggled off nothing is drawn lit; the time still runs.
        if (!driver.Enabled) return;
        var key = local.AtHour(driver.Selection.Hour).AnimationKey((long)_lightAnimationMs);
        if (key == _lightAnimationKey) return;
        _lightAnimationKey = key;
        if (_restDark) return; // SWB11: the light is held during a rest; the stop applies it
        PushAreaAmbient();
    }

    private void TickAreaClock()
    {
        if (SelfTestBootstrap || _nativeSmoke) return;
        TickAreaClock((long)Time.GetTicksMsec());
    }

    // ARC-DEV-124 (MIG12 S5): the frame source of a scene with a session and no ambient driver (a map drawn unlit, or a lit map whose driver was stopped by an
    // unlit one and not rebound); it is bound to one session and map (a window move of the same map keeps it, as it keeps a lit map's driver), and the game does not depend on the lighting: the clocks, the event passes,
    // the real-time steps and the AI run on it all the same.
    private (GameSessionOwner Session, int Map, AreaFrameDriver Driver, FrameClockTap? Tap)? _unlitFrame;

    // The unlit driver's frame step is gone with it, and so is its own tap (a stale tap would hold the world time still); a tap a lit driver has bound since stays.
    private void DropUnlitFrame()
    {
        if (_unlitFrame is not { } held) return;
        _unlitFrame = null;
        if (ReferenceEquals(_frameTap, held.Tap)) _frameTap = null;
    }

    // Whether a frame step moves this scene's session clock: a lit map's ambient driver, or the frame driver of a session without one
    // (MIG12 S8's real-time steps, the world-map journey and the turn-based event, run in it either way).
    private bool HasFrameSource => _areaAmbient is not null || _areaSession is not null;

    private void TickUnlitFrame(long hostMilliseconds)
    {
        if (_areaSession is not { } session) { DropUnlitFrame(); return; }
        if (_unlitFrame is not { } held || !ReferenceEquals(held.Session, session) || held.Map != CurrentAreaMap)
        {
            DropUnlitFrame();
            // A new driver's first frame admits nothing, as an ambient driver's does on a new scene.
            var driver = AreaFrameDriver.Create(session, AreaEvents, SpellCasterForEvents, PingNpcChangeOfPlace);
            BindFrameParticipants(driver.Frame);
            _unlitFrame = held = (session, CurrentAreaMap, driver, _frameTap);
        }
        held.Driver.Tick(hostMilliseconds, WorldMenuPause, WorldFrozenFrame);
        LogAreaEvents(held.Driver.Frame);
    }

    // One production tick at the given host time; the frame loop passes the monotonic engine clock.
    private void TickAreaClock(long hostMilliseconds)
    {
        // ARC-WORLD-1160: the sleep window's steps are real-time events, which a frame's pass spends before the game's (TL27); the cover follows the fade.
        _clockHostMs = hostMilliseconds;
        TickSleep(hostMilliseconds);
        if (_areaAmbient is not { } driver) { TickUnlitFrame(hostMilliseconds); return; }
        DropUnlitFrame();
        var paused = driver.MenuPaused;
        var hourChanged = driver.Tick(hostMilliseconds, WorldMenuPause, AmbientSector(), WorldFrozenFrame);
        LogAreaEvents(driver);
        // SWB11: the hourly light apply is off while the rest runs; the stop applies the current hour at once.
        if (hourChanged && !_restDark) PushAreaAmbient();
        if (hourChanged || paused != driver.MenuPaused)
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
        ResetLightAnimation();
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
            // ARC-DEV-335: with the hero's carried light the canvas hands in the lighting that has it (the map's own otherwise).
            Moving = _companionLight ??= (command, carried) => _areaAmbient is { Enabled: true } current && _readyArea?.Lighting is { } shown
                ? _areaCompanions?.Light(command, current.Effective, carried ?? LightsNow(shown.Local, current))
                    // ARC-RENDER-033: a body drawn from extra art is lit where it lies, by its own art.
                    ?? GroundLight(command, current.Effective, carried ?? LightsNow(shown.Local, current)) : null,
        } : null);
    private Func<AreaDrawCommand, AreaLocalLighting?, AreaLitColors?>? _companionLight;

    // ARC-DEV-335 (V7d, ledger EQ12, LT12): the hero's carried light. Each frame the session's worn light is read; what the canvas draws
    // changes only with it, with the shown map's lighting and with the lighting toggle. The light follows the hero because the canvas makes
    // the lighting from the hero's placement at every submission.
    // ARC-MAGIC-1229: the number of live eye candy lights is part of the key, so a glow that starts or ends hands the canvas a source that has it or not.
    private (CarriedLightSize Size, AreaLightingPlan? Lighting, bool Lit, int EyeCandy)? _carriedKey;

    private void ResetCarriedLight() => _carriedKey = null;

    private static CarriedLightSize CarriedSize(Arcanum.Simulation.Combat.LightEmission size) => size switch
    {
        Arcanum.Simulation.Combat.LightEmission.Small => CarriedLightSize.Small,
        Arcanum.Simulation.Combat.LightEmission.Medium => CarriedLightSize.Medium,
        Arcanum.Simulation.Combat.LightEmission.Large => CarriedLightSize.Large,
        Arcanum.Simulation.Combat.LightEmission.ExtraLarge => CarriedLightSize.ExtraLarge,
        _ => CarriedLightSize.None,
    };

    /// <summary>The size of the hero's carried light now (None without a session or a hero).</summary>
    private CarriedLightSize HeroCarriedSize() => _areaSession is { } session ? CarriedSize(session.QueryHeroLight()) : CarriedLightSize.None;

    /// <summary>
    /// ARC-WORLD-1149 (FR-LIGHT-011, ledger LL1–LL3): the light level 0..254 a light-aware check reads for something at the world cell
    /// (<see cref="AreaLocalLighting.LightLevel"/>): the shown map's ambient for the game hour, its sector lights as posed now and the lights
    /// the creatures carry (the hero's worn light, the NPCs' and companions'), whatever the viewer's lighting toggle shows. Full light (255, which
    /// has the penalty of the original's white, 254) on a map without lighting, before one is shown, or for a cell the lighting has no tile for, where the original's
    /// sampler answers 0 and the colour is white.
    /// </summary>
    private int AreaCellLightLevel(long worldX, long worldY)
    {
        const int Full = Arcanum.Simulation.Combat.AttackRules.MaxLightLevel, Reach = 1024;
        if (_cellLightForSelfTest is { } fixedLevel) return fixedLevel;
        if (_areaAmbient is not { } driver || _readyArea?.Lighting is not { Local: { } basis } lighting || LightsNow(basis, driver) is not { } local) return Full;
        // A cell the shown lighting has no tile for (a row of another map's snapshot, a body outside the loaded window) is lit by nothing here: full light.
        if (!local.HasTile(worldX, worldY)) return Full;
        var view = local;
        if (_canvas?.AreaActorPlacementNow is { } placement && CarriedLightSource(HeroCarriedSize(), lighting, true, gameplay: true) is { } carried)
        {
            var tile = Arcanum.Data.Terrain.TerrainGeometry.Project(checked((int)(worldX - local.Chart.OriginX)), checked((int)(worldY - local.Chart.OriginY)));
            view = carried(placement, new TerrainRect(tile.X + 40 - Reach, tile.Y + 20 - Reach, 2 * Reach, 2 * Reach)) ?? local;
        }
        return view.LightLevel(worldX, worldY, driver.Selection.Pair);
    }

    /// <summary>ARC-WORLD-1149 smoke seam: a stage that judges something other than the darkness penalty (the tech-products grenade, in the dark cave)
    /// fixes the level every check reads at full light for its throws; null: the shown lighting's.</summary>
    private int? _cellLightForSelfTest;

    /// <summary>ARC-WORLD-1149: the light level at the cell a critter row stands on now (<see cref="AreaCellLightLevel"/>); full light for a row without a cell.</summary>
    private int AreaRowLightLevel(AreaObjectRecord row)
    {
        if (row.WorldX is null || row.WorldY is null) return Arcanum.Simulation.Combat.AttackRules.MaxLightLevel;
        var (x, y) = LiveWorldCell(row);
        return AreaCellLightLevel(x, y);
    }

    /// <summary>Per frame: gives the canvas the hero's carried light when the worn light, the shown lighting or the toggle changed.</summary>
    private void TickCarriedLight()
    {
        if (_canvas is null) return;
        var lighting = _readyArea?.Lighting;
        var lit = _areaAmbient is { Enabled: true };
        var size = HeroCarriedSize();
        var glows = EyeCandyLightCount();
        if (_carriedKey is { } known && known.Size == size && known.Lit == lit && known.EyeCandy == glows && ReferenceEquals(known.Lighting, lighting)) return;
        _carriedKey = (size, lighting, lit, glows);
        _canvas.SetAreaCarried(CarriedLightSource(size, lighting, lit));
    }

    // ARC-DEV-335 (the hero) and ARC-DEV-353 (V7d second part, the critters): the lights that move with a creature. The hero's is the size of its worn
    // light; a critter's (an NPC's or a companion's) is its saved primary light, which the original writes from its worn items (EQ12, LT12) and
    // moves with it (LT10): it stands where the critter stands now, its T0 cell until the AI or the follower moved it.
    // ARC-WORLD-1149: with gameplay the source answers a light-aware check, not a drawing: it stands whatever the viewer's lighting toggle says.
    private Func<ActorDiagnosticPlacementInput, TerrainRect, AreaLocalLighting?>? CarriedLightSource(CarriedLightSize size, AreaLightingPlan? lighting, bool lit, bool gameplay = false)
    {
        if (!lit || lighting is not { Local: { } local }) return null;
        var hero = size != CarriedLightSize.None && lighting.CarriedFrame is not null;
        // ARC-MAGIC-1229: a lit eye candy's glow is a carried light as well (it moves with its object).
        if (!hero && local.CritterLights.Count == 0 && EyeCandyLightCount() == 0) return null;
        return (placement, reach) =>
        {
            // A source made for another map's lighting (a map change this frame) draws nothing until the tick gives the canvas the new one.
            if (_areaAmbient is not { } driver || !gameplay && !driver.Enabled || !ReferenceEquals(_readyArea?.Lighting, lighting)) return null;
            var frames = new Dictionary<int, StationaryLightFrame>(local.CritterFrames);
            var lights = new List<StationaryLight>();
            // Only a light whose anchor is within reach of the view is built (the view is widened by the largest frame), so one far from the view
            // adds nothing to the carried lattice, which is one rectangle over all its lights.
            bool Near(long cellX, long cellY, int offsetX, int offsetY)
            {
                var tile = Arcanum.Data.Terrain.TerrainGeometry.Project(checked((int)(cellX - local.Chart.OriginX)), checked((int)(cellY - local.Chart.OriginY)));
                long ax = tile.X + offsetX + 40L, ay = tile.Y + offsetY + 20L;
                return ax >= reach.X && ay >= reach.Y && ax <= (long)reach.X + reach.Width && ay <= (long)reach.Y + reach.Height;
            }
            // The hero's light is where the hero stands: its cell, plus its pixel offsets (the object's move and offset change move its lights, LT10).
            if (hero && placement.MapX is { } x && placement.MapY is { } y
                && Near(checked(local.Chart.OriginX + x), checked(local.Chart.OriginY + y), placement.InstanceOffsetX ?? 0, placement.InstanceOffsetY ?? 0))
            {
                lights.Add(CarriedLightRules.ToLight(new CarriedLight(checked(local.Chart.OriginX + x), checked(local.Chart.OriginY + y),
                    placement.InstanceOffsetX ?? 0, placement.InstanceOffsetY ?? 0, size)));
                frames.TryAdd(CarriedLightRules.ArtNumber, lighting.CarriedFrame!);
            }
            var poses = CritterPoses();
            var absent = _areaCompanions?.Absent;
            foreach (var critter in local.CritterLights)
            {
                // A companion that stands on another map takes its light with it (its body is not drawn here).
                if (absent?.Contains(critter.Object) == true) continue;
                if (!poses.TryGetValue(critter.Object, out var pose))
                {
                    // At its T0 place: the cheap test first, so the many far critters of a map cost nothing per submission.
                    if (!Near(critter.X, critter.Y, critter.OffsetX, critter.OffsetY)) continue;
                    lights.Add(AreaCritterLights.Place(critter, null));
                    continue;
                }
                var stood = AreaCritterLights.Place(critter, pose);
                if (Near(stood.X, stood.Y, stood.OffsetX, stood.OffsetY)) lights.Add(stood);
            }
            AddEyeCandyLights(lighting, local, placement, poses, absent, frames, lights, Near);
            return LightsNow(local, driver)?.WithCarried(frames, lights);
        };
    }

    // Where the critters of the shown map that left their T0 place stand now: the AI's walked NPCs, then the companions (a companion's own pose wins).
    private Dictionary<AreaObjectKey, AreaCritterPose> CritterPoses()
    {
        var poses = new Dictionary<AreaObjectKey, AreaCritterPose>();
        if (_aiHost is { } ai && ReferenceEquals(ai.Session, _areaSession) && _areaOverlay is { } overlay && ReferenceEquals(overlay.Area, _readyArea))
            foreach (var pose in ai.Movers.Poses(ai.NowMs)) poses[pose.Key] = pose;
        if (_areaCompanions is { } companions)
            foreach (var pose in companions.Poses()) poses[pose.Key] = pose;
        return poses;
    }

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
        var flow = driver.MenuPaused ? "PAUSED (the menu is open)" : clock.PauseDisposition.Paused ? $"PAUSED ({clock.PauseDisposition.Reason})"
            : $"time flows {GameTimeFlow.GameMillisecondsPerHostMillisecond}x";
        return $" | {clock.Hour:00}:{clock.Minute:00} day {clock.DayIndex + 1} {(clock.IsDay ? "DAY" : "NIGHT")} {flow} | ambient scheme {selection.SchemeId}"
            + $"{(selection.FromMapDefault ? " (map default)" : " (sector)")} hour {selection.Hour} {(driver.Enabled ? "ON" : "OFF (unlit source)")}"
            + $" | lights {_readyArea?.Lighting?.Local?.LightCount ?? 0} (object {_readyArea?.Lighting?.Local?.ObjectLightCount ?? 0})"
            + " | palette: Game time +1 hour (wait), Ambient lighting on/off | hourly discrete, 8x rate and original night/visual parity NOT_VERIFIED"
            + (driver.Events is { } events ? " | " + events.Last.Status(clock.GameTimestampMs) : "");
    }

    // ARC-WORLD-039: a line per pump call that took events (identifiers and counts only).
    private int _loggedCallRefusals;

    private void LogAreaEvents(AreaAmbientDriver driver) => LogAreaEvents(driver.Events);

    private void LogAreaEvents(FrameStep? frame)
    {
        // ARC-SCRIPT-047: a heartbeat's talk, asked for during the pump that just returned.
        OpenHeartbeatConversation();
        if (frame?.Last is not { } last || last.Fired + last.Dropped + last.ScriptCalls == 0) return;
        // ARC-DEV-051: the session events that came due, for the bug report's log.
        BugReportLog.Process.Add(BugReportLineKind.Event, $"session events fired={last.Fired} dropped={last.Dropped} scripts={last.ScriptCallsRun}/{last.ScriptCalls}"
            + $" waits={last.Waits.Count} decays={last.Decays.Count} restocks={last.Restocks.Count}");
        ReportCompanionWaits(last.Waits);
        // ARC-WORLD-062: a decayed item leaves the ground (the map's inventory and its drawn body).
        if (last.Decays.Count > 0)
        {
            GD.Print($"ARC-WORLD-062 item-decay fired={last.Decays.Count} destroyed={last.Decays.Count(d => d.Step == Arcanum.Simulation.Items.ItemDecayStep.Destroy)}"
                + $" retried={last.Decays.Count(d => d.Step == Arcanum.Simulation.Items.ItemDecayStep.Retry)}");
            SyncAreaInventory();
        }
        // ARC-WORLD-096: a restock changes a holder's list (an open loot or barter window is modal, the clock stands while it is open).
        foreach (var restock in last.Restocks)
            GD.Print($"ARC-WORLD-096 restock fired holder={restock.Holder} step={restock.Decision.Step} merchantRegenerates={restock.Decision.MerchantRegenerates}"
                + $" postponed={restock.Postponed} schedule={restock.ScheduleFailure} regenerations="
                + string.Join(",", restock.Regenerations.Select(r => $"{r.Outcome}:removed {r.Removed}:coins {r.Coins}:created {r.Created.Count}:refused {r.Refused}:skipped {r.Skipped}")));
        if (last.Restocks.Any(r => r.Regenerations.Count > 0)) SyncAreaInventory();
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
                        if (!TryAreaPixel(command, AreaFramesOf(area), _canvas!.AreaVisibleCommands, image, GetViewport(), _hud!.Rect,
                            _readyActor is { } actor ? new Rect2(actor.Destination.X - 5, actor.Destination.Y - 5, actor.Destination.Width + 10, actor.Destination.Height + 10) : null,
                            1, out _, out var diagnostics, ambient, covers: _canvas.AreaVisibleCovers)) { rejections.Add(diagnostics.Rejections); continue; }
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
    // Time flows to the next hour boundary at 8x and the scheme follows; the palette and a dialog do not pause it (MIG12 S7);
    // the palette's +1 hour is an explicit wait on top of the flow; a capture carries
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

        // ARC-DEV-126 (MIG12 S7, GQ3 A): the palette and a dialog are not pause raisers; game time flows behind them at the same 8x, and the palette's
        // +1 hour is an explicit wait on top of it. Only the Esc menu pauses (the frame clock smoke opens the menu on the map stage).
        const long behind = 400 * 2_000L;
        ToggleWorldSettings();
        if (!PaletteOpen) throw new InvalidOperationException("AREA_CLOCK_PALETTE_NOT_OPEN");
        for (var i = 0; i < 400; i++) TickAreaClock(host += 250);
        var palette = driver.Clock;
        if (driver.MenuPaused || palette.PauseDisposition.Paused || palette.GameTimestampMs != flowed.GameTimestampMs + behind) throw new InvalidOperationException("AREA_CLOCK_PALETTE_PAUSED");
        PaletteGameHour();
        var waited = driver.Clock;
        if (waited.GameTimestampMs != palette.GameTimestampMs + AreaAmbientDriver.HourMilliseconds || driver.MenuPaused
            || session.QueryLighting() != new SessionLightingState(driver.Selection.SchemeId, waited.Hour)) throw new InvalidOperationException("AREA_CLOCK_WAIT");
        ToggleWorldSettings();
        TickAreaClock(host += 100);
        var resumed = driver.Clock;
        if (driver.MenuPaused || resumed.GameTimestampMs != waited.GameTimestampMs + 800) throw new InvalidOperationException("AREA_CLOCK_PALETTE_FLOW");

        if (!ShowConversationPanel("ARC-WORLD-010 smoke", null, "[clock pause check]")) throw new InvalidOperationException("AREA_CLOCK_DIALOG_NOT_OPENED");
        for (var i = 0; i < 400; i++) TickAreaClock(host += 250);
        var dialog = driver.Clock;
        if (!ConversationOpen || driver.MenuPaused || dialog.GameTimestampMs != resumed.GameTimestampMs + behind) throw new InvalidOperationException("AREA_CLOCK_DIALOG_PAUSED");
        EndConversation();
        TickAreaClock(host += 100);
        var after = driver.Clock;
        if (driver.MenuPaused || after.GameTimestampMs != dialog.GameTimestampMs + 800) throw new InvalidOperationException("AREA_CLOCK_DIALOG_FLOW");

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
            PaletteWait = waited.GameTimestampMs, Resumed = resumed.GameTimestampMs, DialogFlowed = dialog.GameTimestampMs,
            After = after.GameTimestampMs, saved.Lighting, SnapshotSchema = saved.SchemaVersion, SnapshotChars = text.Length }));
    }
}
