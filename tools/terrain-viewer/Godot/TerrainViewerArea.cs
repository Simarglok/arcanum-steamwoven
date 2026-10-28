using Arcanum.Data.Area;
using Arcanum.Data.Terrain;
using Arcanum.TerrainViewer.Core;
using GamepadLab.Core;
using Godot;

namespace Arcanum.TerrainViewer;

public partial class TerrainViewerApp
{
    private int _readyAreaNotApplicable;
    private void PublishAreaReady(TerrainViewerArguments arguments, TerrainLoadResult result)
    {
        var area = result.Area ?? throw new InvalidOperationException("AREA_RESULT_REQUIRED");
        // Whole-core step evidence bound to this generation is the only admitted movement source;
        // the bounded single-sector snapshot is WORLD-only.
        if (area.Generation != result.Generation || arguments.Mode != TerrainViewerMode.CrashSiteArea ||
            result.ActorPlan?.Profile.Id != ActorDiagnosticProfileId.HumanFixed || result.ActorPlan.Profile.Run is null || result.ActorPlan.Profile.Vault is null ||
            result.TraversalSnapshot is not null || result.AreaStepSnapshot?.EvidenceEpoch != result.Generation)
            throw new InvalidOperationException("AREA_GENERATION_MISMATCH");
        var core = area.Snapshot.Terrain.Where(record => record.Scope == AreaScope.Core).Select(record => record.Key).ToHashSet();
        var drawn = area.Plan.Commands.Where(command => command.Identity is null).Select(command => command.Visual).ToArray();
        if (drawn.Length != core.Count || !core.SetEquals(drawn)) throw new InvalidOperationException("AREA_TERRAIN_COVERAGE");
        var objectRows = area.Plan.Coverage.Where(row => row.Identity is not null).ToArray();
        if (objectRows.Length != area.Snapshot.Objects.Count || objectRows.Select(row => row.Identity).Distinct().Count() != objectRows.Length)
            throw new InvalidOperationException("AREA_OBJECT_COVERAGE");
        _cache.UploadActor(result.ActorTextures);
        // Counted once per publication; the status line is rebuilt every frame.
        _readyAreaNotApplicable = area.Plan.Coverage.Count(row => row.Disposition == Core.AreaDisposition.Suppressed);
        _readyArea = area; _readyMapName = arguments.AreaMapName; _readySourceId = arguments.SourceId; _readyMode = arguments.Mode;
        _readyActorPlan = result.ActorPlan; _readyActor = result.ActorPlan.Current; _readyActorInput = arguments.ActorInput;
        _readyElapsedMilliseconds = result.ElapsedMilliseconds;
        _hud!.SetMode(arguments.Mode); InitializeWorld(); _actorTimeline = null; RetireWorldLifecycle();
        // The plan's chart: the exterior chart for the Crash Site, the whole-map chart for --area-map (ARC-RENDER-017).
        _worldActor.Configure(result.AreaStepSnapshot, WorldTime, chart: area.Plan.Chart);
        NoteMapWindow(result);
        StartAreaTransitions(arguments, result);
        BindAreaConversations(arguments, area);
        BindAreaCompanions();
        BindAreaHover(arguments);
        BindAreaLogbook(arguments);
        BindAreaSkillTexts(arguments);
        BindAreaSpellData(arguments);
        BindAreaLevelTexts(arguments);
        BindAreaSchematics(arguments);
        BindAreaBarter(arguments);
        BindAreaReading(arguments);
        BindAreaWorldMap(arguments);
        StartAreaAmbient(area);
        _worldGridInput = new(); PaletteScenePublished();
        // ARC-RENDER-032: a map loaded as a window (the main world map) has a zoom floor; ARC-RENDER-034: with its terrain overview
        // the view may reach past the window, down to the ordinary 1/8.
        BindAreaOverview(result);
        _camera!.SetAreaPlan(area.Plan, WorldViewport, window: _mapWindowCentre is not null, _mapOverview);
        _canvas!.AreaFailure = error =>
        {
            GD.Print("ARC-RENDER-007 failure stage=VisibleUpload category=" + error.GetType().Name);
            PublishFailure(TerrainViewerMode.CrashSiteArea, new(TerrainViewerErrorCode.RenderFailure, Stage: TerrainViewerFailureStage.Upload));
            if (_nativeSmoke) GetTree().Quit(1);
        };
        // Frame the actor before the first publication: SetAreaPlan Fits the whole
        // envelope at minimum zoom, and publishing there would submit ~32k items.
        _camera.ResetWorld(WorldAnchor, WorldViewport, _worldPreferredZoom);
        // As in WORLD: quarantine first, then enter Follow on the actor, so the
        // safety reset cannot clear the fresh camera's actor tracking.
        QuarantineWorld(); PublishWorldSelection();
        _worldFitView = false; _camera.ResetWorld(WorldAnchor, WorldViewport, _worldPreferredZoom);
        RefreshAreaStatus();
        GD.Print("ARC-RENDER-007 source-ready " + System.Text.Json.JsonSerializer.Serialize(new {
            area.Generation, area.Snapshot.Reconciliation, DrawnTerrain = drawn.Length, DrawnBodies = area.Plan.DrawableBodies,
            NotApplicable = _readyAreaNotApplicable,
            CpuFrames = area.Frames.Count, area.CpuRgbaBytes, area.InitialUsage, area.FinalUsage, area.ElapsedMilliseconds,
            area.AllocatedBytes, area.ManagedBytes, area.ProcessBytes, Parity = "NOT_VERIFIED" }));
        MaybeOpenMainMenu(arguments);
    }
    private void RefreshAreaStatus()
    {
        if (_readyArea is not { } area || _camera is null || _canvas is null) return;
        var r = area.Snapshot.Reconciliation;
        var b = area.Plan.WorldBounds;
        var limited = b.Width * _camera.State.Zoom > WorldViewport.X - 64 || b.Height * _camera.State.Zoom > WorldViewport.Y - 64;
        ShowStatus($"{(_readyMapName is { } map ? $"Map '{map}' ({area.Snapshot.CatalogueMap}/{area.Snapshot.RuntimeMap})" : "Crash Site area")} | SOURCE READY generation {area.Generation} | core sectors {r.CoreSectors} | terrain {r.CoreTerrain} | static {r.Statics} | core mobile {r.CoreMobiles} | bodies {area.Plan.DrawableBodies} | N/A {_readyAreaNotApplicable} | visible commands {_canvas.AreaVisibleCount} / culled {_canvas.AreaCulledCount} | terrain {(_canvas.AreaTerrainChunked ? $"chunks {_canvas.AreaChunkCount}{(_canvas.AreaChunkLevel > 0 ? $" at 1/{1 << _canvas.AreaChunkLevel}" : string.Empty)}, resident {_canvas.AreaChunkResidentCount}/{_canvas.AreaChunkResidentBytes} bytes" : "per command")} | CPU frames {area.Frames.Count}/{area.CpuRgbaBytes} bytes | GPU {_cache.AreaCount}/{_cache.AreaRgbaBytes} estimated RGBA bytes | load {area.ElapsedMilliseconds}ms | zoom {FormatZoom(_camera.State.Zoom)} | context {r.ContextPresent} present/{r.ContextMissing} absent, {(_mapOverview is not null ? "drawn as the terrain overview" : "not drawn")} | {(_readyMapName is not null ? AreaMapMovementStatus() : InCave ? $"movement: cave sector at new-game T0, walls block tile edges | {AreaWallStatus()}; roofs/portals/active composites absent | cave lit by its own scheme and stationary lights" : "movement: whole core (9 sectors) at new-game T0, core edge refused | roofs/walls/portals/active composites absent, not general support")}{(_readyMapName is null ? " | cave transfer by use, no gameplay" : "")} | {(_worldFitView && limited ? _camera.WindowFloored ? "Fit limited by the window zoom floor" : "Fit limited by minimum zoom" : "area envelope")}{AreaTransitionStatus()}{MapStreamStatus()}{HeroStatusText()}{CombatModeStatus()}{TurnBasedStatus()}{AttackStatus()}{SpellStatus()}{PortalStatus()}{QuickSaveStatus()}{AreaAmbientStatus()}{ConversationStatus()}{CompanionStatus()}{AreaInventoryStatus()}{AreaSheetStatus()}{AreaLogbookStatus()}{AreaSchematicsStatus()}{AreaBarterStatus()}{AreaReadingStatus()}{AreaWorldMapStatus()}{AreaSaveListStatus()}{AreaMainMenuStatus()}{AudioStatus()}{SchemeStatus()}{AreaHoverStatus()}{WorldStatus()}");
    }
    private string AreaMapMovementStatus() => "movement: whole map at new-game T0 from the map-list arrival, walls/doors/windows by whole-map step rules | "
        + AreaWallStatus().Replace("cave walls", "walls") + "; active composites absent" + AreaRoofStatus() + " | transitions by use, jump point and tile (unconditional only); companions from the map's data";
    private async Task RunAreaSmokeAsync(TerrainLoadResult result)
    {
        var area = result.Area!;
        // Source-pixel gates below observe unlit source pixels; lighting has its own phase at the end.
        if (_areaAmbient is not null) { _areaAmbient.Enabled = false; PushAreaAmbient(); }
        var viewports = new List<Vector2I>();
        // This is a real native viewport observation. A requested size or a
        // headless success can never substitute for its readback dimensions.
        foreach (var size in new[] { new Vector2I(1280,800), new Vector2I(1440,900), new Vector2I(800,600), new Vector2I(1920,1080) })
        {
            // Process-local automated observation only. This independently
            // probed native path avoids decorated macOS usable-width clamping;
            // ordinary launch and display preferences remain unchanged.
            GetWindow().ContentScaleMode = Window.ContentScaleModeEnum.Disabled;
            GetWindow().Borderless = true; GetWindow().Unresizable = true;
            GetWindow().Position = Vector2I.Zero;
            GetWindow().Size = size;
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            using var image = GetViewport().GetTexture().GetImage();
            if (image.GetWidth() != size.X || image.GetHeight() != size.Y)
                throw new InvalidOperationException("AREA_ACTUAL_VIEWPORT_REQUIRED");
            GD.Print($"ARC-RENDER-007 viewport requested={size.X}x{size.Y} actual={image.GetWidth()}x{image.GetHeight()}");
            viewports.Add(size);
        }
        GetWindow().Size = new(1280,800);
        // ARC-RENDER-029: let the resize settle before the held samples, so it cannot move a sampled view.
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        // Per-sector terrain and per-body-class native pixels, plus the largest
        // admitted scenery. Expectations come from decoded source pixels; the
        // external original occurrence oracle separately ratifies descriptors.
        var terrainSamples = area.Plan.Commands.Where(command => command.Identity is null).GroupBy(command => command.Visual.Sector.Packed)
            .Select(group => group.Reverse().ToArray()).ToList();
        var objects = area.Snapshot.Objects.ToDictionary(body => body.Key);
        var visuals = area.Snapshot.Visuals.ToDictionary(visual => visual.Key);
        var bodySamples = area.Plan.Commands.Where(command => command.Identity is not null && !command.Blended)
            .GroupBy(command => (objects[command.Identity!.Value].Type, ArtClass: visuals[command.Visual].RawArt >> 28))
            .Select(group => group.OrderByDescending(command => (long)command.Destination.Width * command.Destination.Height).ToArray()).ToList();
        var groups = terrainSamples.Concat(bodySamples).ToArray();
        var samples = new List<object>();
        var sampleFrames = new HashSet<AreaFrameKey>();
        // ARC-RENDER-029: the samples hold the camera, as the blend samples do (ARC-RENDER-023).
        await WithAreaSmokeCameraHeld(async () =>
        {
            foreach (var zoom in new[] { 1d, 2d, .5d })
            foreach (var candidates in groups)
            {
                GD.Print($"ARC-RENDER-007 sample-begin kind={candidates[0].Visual.Kind} sector={candidates[0].Visual.Sector.Packed} sourceIndex={candidates[0].Visual.SourceIndex} zoom={zoom}");
                var found = false;
                var diagnostics = new List<object>();
                foreach (var command in candidates.Take(64))
                {
                    _camera!.ResetWorld(new Vec2(command.Destination.X + command.Destination.Width / 2d, command.Destination.Y + command.Destination.Height / 2d), WorldViewport, zoom);
                    var view = _camera.State;
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                    await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                    using var image = GetViewport().GetTexture().GetImage();
                    // The frame must show the view just set.
                    if (_camera.State != view) throw new InvalidOperationException($"AREA_CLASS_CAMERA_MOVED {command.Visual.Kind} {view} {_camera.State}");
                    var admitted = TryAreaPixel(command, area, image, out var sample, out var diagnostic);
                    diagnostics.Add(new { command.Visual, Diagnostic = diagnostic });
                    if (!admitted) continue;
                    samples.Add(sample); sampleFrames.Add(command.Frame); found = true; break;
                }
                if (!found)
                {
                    var path = System.Environment.GetEnvironmentVariable("ARC_AREA_REPORT");
                    if (!string.IsNullOrWhiteSpace(path)) System.IO.File.WriteAllText(path + ".admission-failure.json",
                        System.Text.Json.JsonSerializer.Serialize(new { Schema = "arcanum-area-admission-failure-v1",
                            Outcome = "FAIL", area.Generation, Zoom = zoom, GroupCommands = candidates.Length,
                            CandidatesTried = diagnostics.Count, CapHit = candidates.Length > 64, Diagnostics = diagnostics }));
                    throw new InvalidOperationException("AREA_CLASS_PIXEL_NOT_OBSERVED");
                }
            }
            return samples.Count;
        });
        RefreshAreaStatus();
        var report = new {
            Schema = "arcanum-area-render-coverage-v1", Task = "ARC-RENDER-007", area.Generation, area.Snapshot.Inputs,
            ControlledActor = new { Generation = result.Generation, Profile = result.ActorPlan!.Profile.Id.ToString(),
                Frames = result.ActorTextures.Count, RgbaBytes = result.ActorTextures.Values.Sum(frame => (long)frame.Rgba.Length),
                result.ActorPlan.Current },
            Traversal = new { result.AreaStepSnapshot!.EvidenceEpoch, result.AreaStepSnapshot.ProfileId, AdmissionPolicy = "WholeCoreT0",
                InputKind = result.AreaStepSnapshot.InputIdentity.Kind.ToString(), result.AreaStepSnapshot.EvidenceSha256,
                result.AreaStepSnapshot.AdmittedCells, result.AreaStepSnapshot.TerrainBlockedCells, result.AreaStepSnapshot.TerrainUnknownCells,
                result.AreaStepSnapshot.ObjectBlockerCells, result.AreaStepSnapshot.UnknownObjects },
            area.Snapshot.CatalogueMap, area.Snapshot.RuntimeMap, area.Snapshot.Profile, area.Snapshot.Endpoints,
            Chart = new { OriginX = AreaScenePlan.CameraOriginX, OriginY = AreaScenePlan.CameraOriginY }, Viewports = viewports.Select(v => new { v.X, v.Y }).ToArray(),
            area.Snapshot.Reconciliation, area.Snapshot.Sectors, Terrain = area.Snapshot.Terrain, Objects = area.Snapshot.Objects,
            Visuals = area.Snapshot.Visuals, area.Plan.Coverage, area.Plan.Commands, area.CpuRgbaBytes, Frames = area.Frames.Count,
            area.InitialUsage, area.FinalUsage, area.ElapsedMilliseconds, area.AllocatedBytes, area.ManagedBytes, area.ProcessBytes,
            GpuCreated = _cache.CreatedCount, GpuDisposed = _cache.DisposedCount, GpuLive = _cache.Count, PixelSamples = samples,
            SampleFrameProofs = sampleFrames.Select(key => new { Frame = key, area.Frames[key].Width, area.Frames[key].Height,
                RgbaBase64 = Convert.ToBase64String(area.Frames[key].Rgba) }).ToArray(),
            RendererObserved = true, OriginalRuntimeParity = "NOT_VERIFIED" };
        var reportPath = System.Environment.GetEnvironmentVariable("ARC_AREA_REPORT");
        if (!string.IsNullOrWhiteSpace(reportPath)) System.IO.File.WriteAllText(reportPath, System.Text.Json.JsonSerializer.Serialize(report));
        GD.Print($"ARC-RENDER-007 native-smoke source-coverage and sampled-pixels PASS samples={samples.Count} terrain={area.Snapshot.Reconciliation.CoreTerrain} bodies={area.Plan.DrawableBodies}");
        // One cell west of the nine core sectors (chart x -65): outside the admitted whole-core coverage.
        OriginalPassabilityOverlayCheck("crash-site-area", new(AreaGridSession.MinimumChartCell - 1, 1), blockerReason: "DESTINATION_OBJECT_BLOCKER");
        if (_nativeSmokeScreenshot is not null)
        {
            using var image = GetViewport().GetTexture().GetImage();
            if (image.SavePng(_nativeSmokeScreenshot) != Error.Ok) throw new InvalidOperationException("AREA_CAPTURE_FAILED");
        }
        await RunAreaHdSmokeAsync(area, result);
        await RunAreaLightingSmokeAsync(area);
        if (_areaTransitions is not null) { await RunAreaHoverSmokeAsync(); await RunAreaInventorySmokeAsync(); await RunAreaHeroSmokeAsync(result); await RunAreaLevelMessageSmokeAsync(); await RunAreaLogbookSmokeAsync(); await RunAreaSchematicsSmokeAsync(); await RunAreaReadingSmokeAsync(); await RunAreaNpcCrafterSmokeAsync(); await RunReservedKeyHintSmokeAsync(); await RunAreaTransitionSmokeAsync(); await RunAreaQuickSaveSmokeAsync(); await RunAreaGaitSmokeAsync(); await RunAreaCombatModeSmokeAsync(); await RunAreaAudioSmokeAsync(); await RunAreaAttackSmokeAsync(); await RunAreaCastSmokeAsync(); await RunAreaFollowSmokeAsync(); await RunAreaCompanionCaveSmokeAsync(); await RunAreaVisualToggleSmokeAsync(result); }
        RunAreaClockSmoke();
        // The caller releases its load result, then runs S5 and quits.
    }
    private bool TryAreaPixel(AreaDrawCommand command, AreaLoadResult area, Image image, out object sample, out AreaPixelDiagnostics diagnostics)
        => TryAreaPixel(command, area.Frames, _canvas!.AreaVisibleCommands, image, GetViewport(), _hud!.Rect,
            _readyActor is { } actor ? new Rect2(actor.Destination.X - 5, actor.Destination.Y - 5,
                actor.Destination.Width + 10, actor.Destination.Height + 10) : null,
            _camera!.State.Zoom, out sample, out diagnostics);

    // The exact production observation path, with value inputs for public
    // native fixtures. The fixture must still publish via the real compositor.
    internal static bool TryAreaPixel(AreaDrawCommand command, IReadOnlyDictionary<AreaFrameKey, DecodedAreaFrame> frames,
        IReadOnlyList<AreaDrawCommand> ordered, Image image, Viewport viewport, Rect2? hud, Rect2? actor,
        double zoom, out object sample, out AreaPixelDiagnostics diagnostics, Arcanum.Data.Lighting.AmbientColor? ambient = null,
        Func<Vector2, (double R, double G, double B)>? tintAt = null)
    {
        sample = null!;
        var frame = frames[command.Frame]; var destination = command.Destination;
        diagnostics = new();
        var index = ordered.ToList().IndexOf(command);
        if (index < 0) { diagnostics.Reject("not-visible"); return false; }
        var transform = AreaRasterTransform(viewport); var inverse = transform.AffineInverse();
        diagnostics.RawTransform = viewport.GetCanvasTransform().ToString();
        diagnostics.RasterTransform = transform.ToString();
        var xAxis = new AreaNearestAxis(transform.Origin.X, transform.X.X, destination.X, destination.Width,
            command.Source.X, command.Source.Width, command.FlipX);
        var yAxis = new AreaNearestAxis(transform.Origin.Y, transform.Y.Y, destination.Y, destination.Height,
            command.Source.Y, command.Source.Height, command.FlipY);
        var visited = new HashSet<(int,int)>();
        for (var y = 0; y < command.Source.Height; y++)
        for (var x = 0; x < command.Source.Width; x++)
        {
            var world = new Vector2((float)(destination.X + (x + .5) * destination.Width / command.Source.Width),
                (float)(destination.Y + (y + .5) * destination.Height / command.Source.Height));
            var screen = transform * world; var sx = (int)Math.Floor(screen.X); var sy = (int)Math.Floor(screen.Y);
            if (!visited.Add((sx,sy))) continue;
            diagnostics.UniqueScreenCenters++;
            if (sx < 0 || sy < 0 || sx >= image.GetWidth() || sy >= image.GetHeight()) { diagnostics.Reject("bounds"); continue; }
            if (hud is { } h && h.HasPoint(new(sx,sy))) { diagnostics.Reject("hud"); continue; }

            // A later sprite's transparent bounding rectangle is not an
            // occluder. Consult its sampled source alpha at this screen point;
            // otherwise small original ground items under canopy bounds can
            // never acquire a valid full-scene observation even through holes.
            var px = xAxis.SourceIndex(sx);
            var py = yAxis.SourceIndex(sy);
            if (px < 0 || py < 0) { diagnostics.Reject("crop"); continue; }
            var at = checked((py * frame.Width + px) * 4);
            if (frame.Rgba[at + 3] != 255) { diagnostics.Reject("target-alpha"); continue; }
            var actualWorld = inverse * new Vector2(sx + .5f, sy + .5f);
            // ARC-RENDER-012: a per-pixel tint (local light) counts only where it differs visibly from the
            // plain ambient, so a renderer without the lights cannot pass.
            var local = tintAt?.Invoke(actualWorld);
            if (local is { } pixelTint && ambient is { } plain && Math.Max(Math.Abs(frame.Rgba[at] * (pixelTint.R - plain.R) / 255d), Math.Max(
                    Math.Abs(frame.Rgba[at + 1] * (pixelTint.G - plain.G) / 255d), Math.Abs(frame.Rgba[at + 2] * (pixelTint.B - plain.B) / 255d))) <= 6)
            { diagnostics.Reject("light-not-observable"); continue; }
            // A lit observation counts only where the tint is visible beyond the tolerance.
            if (local is null && ambient is { } lit && Math.Max(Math.Abs(frame.Rgba[at] * lit.R / 255d - frame.Rgba[at]), Math.Max(
                    Math.Abs(frame.Rgba[at + 1] * lit.G / 255d - frame.Rgba[at + 1]), Math.Abs(frame.Rgba[at + 2] * lit.B / 255d - frame.Rgba[at + 2]))) <= 3)
            { diagnostics.Reject("tint-not-observable"); continue; }
            // The compositor uses exact CPU integer index rows and texelFetch.
            // Sparse halfzoom targets no longer need a uniform neighboring block.
            diagnostics.TargetOnlyEligible++;
            var occluder = ordered.Skip(index + 1).FirstOrDefault(later => AreaAlphaAt(later, frames, transform, sx, sy) != 0);
            if (occluder is not null) { diagnostics.LastOccluder = occluder.Visual; diagnostics.Reject("later-alpha"); continue; }
            if (actor is { } a && a.HasPoint(actualWorld)) { diagnostics.Reject("actor-guide"); continue; }
            var observed = image.GetPixel(sx, sy);
            // Lit observation (ARC-RENDER-010): source channel times the applied ambient channel / 255.
            var tint = ambient ?? Arcanum.Data.Lighting.AmbientColor.Neutral;
            var (tr, tg, tb) = local ?? (tint.R, tint.G, tint.B);
            var expected = new[] { frame.Rgba[at] * tr / 255d, frame.Rgba[at + 1] * tg / 255d, frame.Rgba[at + 2] * tb / 255d };
            sample = new { command.Visual, command.Identity, command.Frame, command.Destination, Zoom = zoom,
                command.Source, command.FlipX, command.FlipY, RasterTransform = transform.ToString(),
                SourceU = xAxis.CoordinateRatio(sx), SourceV = yAxis.CoordinateRatio(sy), SamplingDomain = "exact-binary-rational-center-discrete-reflection-v1",
                TransformBits = new[] { BitConverter.SingleToUInt32Bits(transform.X.X), BitConverter.SingleToUInt32Bits(transform.Y.Y),
                    BitConverter.SingleToUInt32Bits(transform.Origin.X), BitConverter.SingleToUInt32Bits(transform.Origin.Y) },
                RawTransform = viewport.GetCanvasTransform().ToString(), Snap = viewport.Snap2DTransformsToPixel,
                RawTransformBits = new[] { BitConverter.SingleToUInt32Bits(viewport.GetCanvasTransform().X.X),
                    BitConverter.SingleToUInt32Bits(viewport.GetCanvasTransform().Y.Y),
                    BitConverter.SingleToUInt32Bits(viewport.GetCanvasTransform().Origin.X),
                    BitConverter.SingleToUInt32Bits(viewport.GetCanvasTransform().Origin.Y) },
                SourceX = px, SourceY = py, ScreenX = sx, ScreenY = sy,
                Expected = ambient is null ? new[] { (int)frame.Rgba[at], frame.Rgba[at+1], frame.Rgba[at+2], 255 }
                    : new[] { (int)Math.Round(expected[0]), (int)Math.Round(expected[1]), (int)Math.Round(expected[2]), 255 },
                Actual = new[] { (int)Math.Round(observed.R * 255), (int)Math.Round(observed.G * 255), (int)Math.Round(observed.B * 255), (int)Math.Round(observed.A * 255) } };
            if (Math.Abs(observed.R * 255 - expected[0]) > 2 || Math.Abs(observed.G * 255 - expected[1]) > 2 || Math.Abs(observed.B * 255 - expected[2]) > 2 || Math.Abs(observed.A * 255 - 255) > 2)
            {
                // Detailed original pixels remain private, never in public logs.
                var path = System.Environment.GetEnvironmentVariable("ARC_AREA_REPORT");
                if (!string.IsNullOrWhiteSpace(path)) System.IO.File.WriteAllText(path + ".failure.json", System.Text.Json.JsonSerializer.Serialize(sample));
                throw new InvalidOperationException("AREA_SOURCE_PIXEL_MISMATCH");
            }
            diagnostics.Admitted++;
            return true;
        }
        return false;
    }

    private static byte AreaAlphaAt(AreaDrawCommand command, IReadOnlyDictionary<AreaFrameKey, DecodedAreaFrame> frames,
        Transform2D transform, int screenX, int screenY)
    {
        var d = command.Destination; var s = command.Source;
        var x = new AreaNearestAxis(transform.Origin.X, transform.X.X, d.X, d.Width, s.X, s.Width, command.FlipX).SourceIndex(screenX);
        var y = new AreaNearestAxis(transform.Origin.Y, transform.Y.Y, d.Y, d.Height, s.Y, s.Height, command.FlipY).SourceIndex(screenY);
        if (x < 0 || y < 0) return 0;
        var frame = frames[command.Frame];
        return frame.Rgba[(y * frame.Width + x) * 4 + 3];
    }
    private static Transform2D AreaRasterTransform(Viewport viewport)
    {
        return AreaDrawItem.RasterTransform(viewport);
    }
}
