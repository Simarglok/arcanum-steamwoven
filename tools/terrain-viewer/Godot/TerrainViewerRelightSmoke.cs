using Arcanum.Data.Lighting;
using Arcanum.Simulation.Goals;
using Arcanum.Data.Terrain;
using Arcanum.Session;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Equipment;
using Arcanum.Simulation.Movement;
using Arcanum.World;
using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

public partial class TerrainViewerApp
{
    // ARC-RES-1978 (FR-LIGHT-013 experiment) native observation (stage "relight", only when named, with --hero, on a lit map: the Crash Site cave):
    // the relit look (AreaRelight) against Legacy on the same frames, through the production draw path.
    //  - Legacy first: with the relit look off two captures of one frame are byte-identical, and after the look has been switched on and off
    //    again they are still identical to the first (the hook leaves the Legacy programs and uniforms alone while it is off);
    //  - the hero wears a large light (its torch), the clock is moved to a day hour and a night hour; at each, one frame in each of the modes
    //    off / add / night / delight is captured (set ARC_RELIGHT_CAPTURE to an absolute directory to keep the PNGs; they are derived from the
    //    player's art and stay out of git) and the relit frames must differ from Legacy near the light (a pixel count) while a day frame in the
    //    night-weighted modes stays close to Legacy;
    //  - frame time: the mean and the 95th percentile of 90 frames with the look off and on, with the real lights only and with 5 and 20 lights
    //    more (synthetic, ringed round the hero), vsync off, and the normal-map memory the visible frames would take as full-size RGBA8.
    // The clock is left at the night hour and the hero's item is put back.
    private async Task RunAreaRelightSmokeAsync(TerrainLoadResult? mapMode = null)
    {
        // ARC-RES-2007: the night-city variant of this stage (Tarant, Ashbury), in its own file.
        if (OS.GetEnvironment("ARC_RELIGHT_SCENE") == "city") { await RunAreaRelightCityAsync(mapMode); return; }
        // ARC-RES-2000: the walk-cycle flicker, frame-time and Godot-native comparison variant of this stage, in its own file.
        if (OS.GetEnvironment("ARC_RELIGHT_SCENE") == "cycle") { await RunAreaRelightCycleAsync(mapMode); return; }
        if (_areaSession is not { } session || session.QueryHero() is null || _worldActor.Session is not AreaGridSession || _canvas is null
            || _areaAmbient is not { } driver || _readyArea is not { Lighting: { Local: { } all, CarriedFrame: { } } } area)
        { GD.Print("ARC-RES-1978 native-smoke relight NONE map=" + _readyMapName + " (needs a hero on a lit map)"); return; }
        static void Require(bool value, string why)
        {
            if (value) return;
            GD.Print("ARC-RES-1978 relight smoke FAIL: " + why);
            throw new InvalidOperationException("AREA_RELIGHT_SMOKE_FAILED");
        }
        if (session.QueryEquipment().Worn.FirstOrDefault(w => w.Slot == EquipmentSlot.Weapon) is not { } wielded
            || session.QueryHeroKit().Where(k => k.Entity == wielded.Entity).Select(k => (SessionKitItem?)k).FirstOrDefault() is not { } kit)
        { GD.Print("ARC-RES-1978 native-smoke relight NONE map=" + _readyMapName + " (the hero wields no kit item to light)"); return; }
        bool Wear(EquipmentItem item)
        {
            if (session.Unequip(session.Version, EquipmentSlot.Weapon, _ => SessionInsertVerdict.Accepted).Disposition == GameSessionDisposition.Rejected) return false;
            return session.Equip(session.Version, wielded.Entity, EquipmentSlot.Weapon, new(kit.Kind, [], false, item), (_, _) => true,
                _ => SessionInsertVerdict.Accepted).Disposition == GameSessionDisposition.Changed;
        }
        var captureDir = OS.GetEnvironment("ARC_RELIGHT_CAPTURE");
        var keep = !string.IsNullOrEmpty(captureDir) && captureDir.StartsWith('/');
        if (keep) DirAccess.MakeDirRecursiveAbsolute(captureDir);
        var simulatedFocus = _worldFocusSimulationForSelfTest; var focused = _worldFocused; var testTime = _worldTestTime; var modal = WorldModal; var hudVisible = _hud?.Visible ?? false;
        _worldFocusSimulationForSelfTest = true;
        long hostMs = 31_000_000L;
        var wasEnabled = driver.Enabled; var wasMode = AreaRelight.DeveloperMode; var wasVsync = DisplayServer.WindowGetVsyncMode();
        var map = mapMode is { Area: { } mapArea, AreaStepSnapshot: { } mapSteps }
            ? new AreaMapRuntime(mapArea.Snapshot, mapSteps, mapArea.Plan.Chart) : _areaTransitions!.Current;
        var home = _worldActor.Session is AreaGridSession grid ? grid.State.CommittedCell : map.Chart.Spawn;
        var worn = false; object? report = null;
        try
        {
            _worldFocused = true; SelectSimulatedPadForSelfTest();
            driver.Enabled = true;
            // ARC-RENDER-2076: the Legacy baselines below need Legacy (an HD profile would turn the look on).
            _canvas.SetVisualProfile(VisualProfile.Legacy, _canvas.HdSource, _canvas.HdBudget); SyncRelitLook();
            AreaRelight.SetMode(AreaRelight.Mode.Off); PushAreaAmbient();
            // Stand the hero near the map's first lit light when it has a standable cell there, else where the map starts.
            var start = home;
            if (all.Field.Placements.FirstOrDefault(p => (p.Light.Flags & (StationaryLightFlags.Off | StationaryLightFlags.Dark)) == 0).Light is { } lamp)
            {
                try { start = AreaSmokeStandable(map, new CellCoord((int)lamp.X + 2, (int)lamp.Y + 2), 6); } catch (InvalidOperationException) { start = AreaSmokeStandable(map, home, 2); }
            }
            else start = AreaSmokeStandable(map, home, 2);
            AreaSmokePlace(map, start);
            _worldFitView = false; _camera!.ResetWorld(WorldAnchor, WorldViewport, _worldPreferredZoom); PublishWorldSelection();
            _worldPanel?.Hide(); if (_hud is not null) _hud.Visible = false;
            async Task Frames(int count = 4)
            {
                for (var i = 0; i < count; i++) { AreaSmokeFrame(); await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
                TickCarriedLight(); await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }
            async Task<Image> Capture(string name)
            {
                await Frames();
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                var image = GetViewport().GetTexture().GetImage();
                if (keep) image.SavePng($"{captureDir}/{name}.png");
                return image;
            }
            static int Differing(Image a, Image b, Rect2I? region = null)
            {
                var r = region ?? new Rect2I(Vector2I.Zero, a.GetSize());
                var n = 0;
                for (var y = r.Position.Y; y < r.End.Y; y++)
                    for (var x = r.Position.X; x < r.End.X; x++) if (a.GetPixel(x, y) != b.GetPixel(x, y)) n++;
                return n;
            }
            await Frames();

            // The hero's torch: a large light, worn like the worn-light stage does.
            Require(Wear(wielded.Item with { Light = EquipmentLight.Large }), "the wielded item is worn again with a large light");
            worn = true;
            await Frames();

            // Legacy is Legacy: two captures identical, and still identical after the relit look has been on.
            var legacyA = await Capture("legacy-a"); var legacyB = await Capture("legacy-b");
            Require(Differing(legacyA, legacyB) == 0, "two Legacy captures of one frame are byte-identical");
            AreaRelight.SetMode(AreaRelight.Mode.Add); PushAreaAmbient(); await Frames(); AreaRelight.SetMode(AreaRelight.Mode.Off); PushAreaAmbient();
            var legacyC = await Capture("legacy-c");
            var legacyDiff = Differing(legacyA, legacyC);
            Require(legacyDiff == 0, $"Legacy after the relit look was on and off again is byte-identical ({legacyDiff} pixels differ)");

            // Day and night, each in every mode.
            var size = legacyA.GetSize();
            var near = new Rect2I(size.X / 2 - 240, size.Y / 2 - 150, 480, 300).Intersection(new Rect2I(Vector2I.Zero, size));
            var counts = new Dictionary<string, int>();
            foreach (var (label, hour) in new[] { ("day", 14), ("night", 2) })
            {
                for (var h = 0; h < 24 && driver.Clock.Hour != hour; h++) { driver.AdvanceHours(1, AmbientSector()); LogAreaEvents(driver); }
                PushAreaAmbient(); await Frames();
                AreaRelight.SetMode(AreaRelight.Mode.Off); PushAreaAmbient();
                var legacy = await Capture($"{label}-1-legacy");
                foreach (var (name, mode) in new[] { ("2-add", AreaRelight.Mode.Add), ("3-night", AreaRelight.Mode.NightAdd), ("4-delight", AreaRelight.Mode.DelightAdd) })
                {
                    AreaRelight.SetMode(mode); PushAreaAmbient();
                    var relit = await Capture($"{label}-{name}");
                    counts[$"{label}-{name}"] = Differing(legacy, relit, near);
                }
                AreaRelight.SetMode(AreaRelight.Mode.Off); PushAreaAmbient();
            }
            GD.Print("ARC-RES-1978 relight pixels differing from Legacy near the hero " + System.Text.Json.JsonSerializer.Serialize(counts));
            Require(counts["night-2-add"] > 0, "the relit night frame differs from Legacy near the hero's light");
            Require(counts["day-3-night"] <= counts["day-2-add"] && counts["night-3-night"] <= counts["night-2-add"], "the night-weighted mode changes no more pixels than add");

            // ARC-RENDER-2298: a light's own carrier. The night weight must not read the carried torch as the Legacy light that already lit the hero: with the same two lamps (lit in the relit list
            // only) the hero's own pixels take a relit term with the torch worn too, not only without it. The hero's pixels are those that differ from the picture with the hero hidden.
            var carrierReport = new Dictionary<string, object>();
            {
                var wasStrength0 = AreaRelight.Strength;
                var crop = new Rect2I(size.X / 2 - 80, size.Y / 2 - 150, 160, 220).Intersection(new Rect2I(Vector2I.Zero, size));
                Vector2 HeroPoint()
                {
                    var p = _canvas.AreaActorPlacementNow;
                    if (p is null) return Vector2.Zero;
                    var projected = TerrainGeometry.Project(checked((int)(p.MapX ?? 0)), checked((int)(p.MapY ?? 0)));
                    return new Vector2(projected.X + (p.InstanceOffsetX ?? 0) + 40, projected.Y + (p.InstanceOffsetY ?? 0) + 20);
                }
                async Task<(int Pixels, int Lit, double Mean)> HeroTerm(EquipmentLight light, string name)
                {
                    Require(Wear(wielded.Item with { Light = light }), $"the wielded item is worn with light {light}");
                    AreaRelight.SetMode(AreaRelight.Mode.Off); AreaRelight.OnlyExtra = false; AreaRelight.Extra.Clear(); PushAreaAmbient();
                    _canvas.SetActorVisibilityForReadback(false);
                    var hidden = (await Capture($"carrier-{name}-1-no-hero")).GetRegion(crop);
                    _canvas.SetActorVisibilityForReadback(true);
                    var legacyFrame = (await Capture($"carrier-{name}-2-legacy")).GetRegion(crop);
                    var hero = HeroPoint();
                    AreaRelight.Extra.Add(new AreaRelight.Light(hero.X + 70, hero.Y - 10, 160f, new Color(1f, .75f, .45f)));
                    AreaRelight.Extra.Add(new AreaRelight.Light(hero.X - 90, hero.Y + 10, 160f, new Color(.55f, .7f, 1f)));
                    AreaRelight.OnlyExtra = true; AreaRelight.SetMode(AreaRelight.Mode.NightAdd); AreaRelight.Strength = AreaRelight.LookStrength; PushAreaAmbient();
                    var relitFrame = (await Capture($"carrier-{name}-3-relit")).GetRegion(crop);
                    AreaRelight.OnlyExtra = false; AreaRelight.Extra.Clear(); AreaRelight.SetMode(AreaRelight.Mode.Off); AreaRelight.Strength = wasStrength0; PushAreaAmbient();
                    int pixels = 0, lit = 0; double sum = 0;
                    for (var y = 0; y < legacyFrame.GetHeight(); y++)
                        for (var x = 0; x < legacyFrame.GetWidth(); x++)
                        {
                            if (legacyFrame.GetPixel(x, y) == hidden.GetPixel(x, y)) continue;
                            pixels++;
                            var term = relitFrame.GetPixel(x, y).Luminance - legacyFrame.GetPixel(x, y).Luminance;
                            if (term > 0.004f) lit++;
                            sum += Math.Max(term, 0f);
                        }
                    return (pixels, lit, pixels == 0 ? 0 : sum / pixels);
                }
                var withTorch = await HeroTerm(EquipmentLight.Large, "torch");
                var withoutTorch = await HeroTerm(EquipmentLight.None, "bare");
                GD.Print($"ARC-RENDER-2298 relit term on the hero's pixels: torch worn {withTorch.Pixels} px, {withTorch.Lit} lit, mean {withTorch.Mean:0.0000}; no torch {withoutTorch.Pixels} px, {withoutTorch.Lit} lit, mean {withoutTorch.Mean:0.0000}");
                Require(withTorch.Pixels > 50 && withoutTorch.Pixels > 50, "the hero's own pixels are found in both captures");
                Require(withoutTorch.Lit > 0, "without a torch the hero's pixels take a relit term (the lamps reach the hero)");
                Require(withTorch.Lit * 2 >= withoutTorch.Lit && withTorch.Mean >= withoutTorch.Mean * 0.5, "the torch's carrier takes a relit term, within a factor of two of the bare hero");
                carrierReport["Torch"] = withTorch; carrierReport["Bare"] = withoutTorch;
                Require(Wear(wielded.Item with { Light = EquipmentLight.Large }), "the hero's torch is put back");
                await Frames();
            }


            // ARC-RENDER-2060, ARC-RENDER-2076: the player's relit look is a part of HD, at the night hour the loop above left. With HD Off it is Legacy,
            // byte for byte; with it On the look is the night-weighted mode at the look's strength (the same pixels as the developer mode set to that), and Off again gives Legacy back.
            var settingReport = new Dictionary<string, object>();
            var wasProfile = _canvas.VisualProfile; var wasSource = _canvas.HdSource; var wasBudget = _canvas.HdBudget;
            try
            {
                _canvas.SetVisualProfile(VisualProfile.Legacy, wasSource, wasBudget); SyncRelitLook(); PushAreaAmbient();
                Require(!RelitLookWanted(VisualProfile.Legacy) && RelitLookWanted(VisualProfile.Hd) && !RelitLookWanted(null), "the relit look is wanted exactly with HD");
                Require(!AreaRelight.Look && !AreaRelight.Active, "HD Off puts the look out of force");
                var nightLegacy = await Capture("setting-1-legacy");
                _canvas.SetVisualProfile(VisualProfile.Hd, wasSource, wasBudget); SyncRelitLook(); PushAreaAmbient();
                Require(AreaRelight.Look && AreaRelight.Current == AreaRelight.Mode.NightAdd, "HD On is the night-weighted look");
                var lookImage = await Capture("setting-2-on-hd");
                var lookDiff = Differing(nightLegacy, lookImage, near);
                Require(lookDiff > 0, "the relit look differs from Legacy near the hero's light at night");
                // ARC-RENDER-2207: the relit light list follows the animation poses of the map's animated lights (a light that steps to another frame or offset moves in the list too).
                var wasAnimationMs = _lightAnimationMs;
                try
                {
                    _lightAnimationMs = 0; PushAreaAmbient();
                    var still = AreaRelight.MapLightList.ToArray();
                    var hourForm = all.AtHour(driver.Selection.Hour);
                    var posedAtZero = hourForm.AtAnimation(0).PosedPlacements.Where(p => (p.Light.Flags & (StationaryLightFlags.Off | StationaryLightFlags.Dark)) == 0).Count();
                    Require(still.Length == posedAtZero, $"the relit map lights are the lit placements ({still.Length} of {posedAtZero})");
                    var movedAt = -1;
                    static bool Lit(StationaryLight light) => (light.Flags & (StationaryLightFlags.Off | StationaryLightFlags.Dark)) == 0;
                    var litAtZero = hourForm.AtAnimation(0).PosedPlacements.Where(p => Lit(p.Light)).Select(p => p.Rect).ToArray();
                    if (hourForm.Field.Animated.Count > 0)
                        for (var ms = 50; ms <= 20000 && movedAt < 0; ms += 50)
                            if (!hourForm.AtAnimation(ms).PosedPlacements.Where(p => Lit(p.Light)).Select(p => p.Rect).SequenceEqual(litAtZero)) movedAt = ms;
                    // The installed data has next to no animated lights (one, on the void map), so on most maps this stays unexercised here; the unit test of the posed placements is the coverage then.
                    if (movedAt > 0)
                    {
                        _lightAnimationMs = movedAt; PushAreaAmbient();
                        Require(!AreaRelight.MapLightList.SequenceEqual(still), $"an animated light moved in the relit list at {movedAt} ms");
                    }
                    settingReport["AnimatedLights"] = hourForm.Field.Animated.Count; settingReport["AnimatedMovedAtMs"] = movedAt;
                }
                finally { _lightAnimationMs = wasAnimationMs; PushAreaAmbient(); }
                _canvas.SetVisualProfile(VisualProfile.Legacy, wasSource, wasBudget); SyncRelitLook(); // the developer mode alone from here, so the per-frame sync does not turn the look back on
                AreaRelight.SetMode(AreaRelight.Mode.NightAdd); var wasStrength = AreaRelight.Strength; AreaRelight.Strength = AreaRelight.LookStrength; PushAreaAmbient();
                var developerImage = await Capture("setting-3-developer-night");
                AreaRelight.Strength = wasStrength; AreaRelight.SetMode(AreaRelight.Mode.Off);
                var lookVsDeveloper = Differing(lookImage, developerImage);
                Require(lookVsDeveloper == 0, $"the look is the night-weighted mode at the look's strength ({lookVsDeveloper} pixels differ)");
                PushAreaAmbient();
                Require(!AreaRelight.Look, "HD Off puts the look out of force");
                var settingOff = Differing(nightLegacy, await Capture("setting-4-off-again"));
                Require(settingOff == 0, $"HD Off again is byte-identical to Legacy ({settingOff} pixels differ)");
                settingReport["Strength"] = AreaRelight.LookStrength; settingReport["NearDiffering"] = lookDiff; settingReport["Lights"] = AreaRelight.UploadedCount;
            }
            finally
            {
                _canvas.SetVisualProfile(wasProfile, wasSource, wasBudget); SyncRelitLook(); PushAreaAmbient();
            }

            // A walk with the torch: the same held-stick walk twice, Legacy then relit (the simulation is deterministic, so frame i is the same
            // frame in both), the hero's crop captured at every step. The relit term is the relit frame minus the Legacy one; its change from
            // one frame to the next, against the Legacy frame's own change, says whether the derived normals shimmer along a walk cycle.
            (int X, int Y)? direction = null;
            foreach (var (dx, dy) in new[] { (6, 0), (0, 6), (-6, 0), (0, -6), (5, 5), (-5, -5), (5, -5), (-5, 5), (3, 0), (0, 3), (-3, 0), (0, -3) })
            {
                AreaSmokePlace(map, start); hostMs = 31_000_000L; TickAreaClock(hostMs);
                var origin = ((AreaGridSession)_worldActor.Session!).State.CommittedCell;
                HeroRequests.Apply(new WalkRequest(new CellCoord(origin.X + dx, origin.Y + dy), Ctrl: false));
                for (var f = 0; f < 80 && ((AreaGridSession)_worldActor.Session!).State.CommittedCell == origin && HeroRunsHost()?.WalkRun is not null; f++) { TickAreaClock(hostMs += 100); AreaSmokeFrame(); await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
                if (HeroRunsHost()?.WalkRun is not null && ((AreaGridSession)_worldActor.Session!).State.CommittedCell != origin) { direction = (dx, dy); GD.Print($"ARC-RES-1978 relight walk probe {dx},{dy} moved from {origin} to {((AreaGridSession)_worldActor.Session!).State.CommittedCell}"); break; }
                HeroRunsHost()?.Interrupt(GoalInterruptRules.OrdinaryActions, "SMOKE_NEXT_TARGET"); StopWorldRoute("SMOKE_NEXT_TARGET");
                for (var f = 0; f < 10; f++) AreaSmokeFrame();
            }
            double walkTermStep = 0, walkLegacyStep = 0; var walkSteps = 0; var walkTermMax = 0d;
            if (direction is { } dir)
            {
                // The same walk twice, Legacy then relit (the simulation is deterministic without the AI host, so frame i is the same frame in both).
                const int Steps = 60;
                void Clean() { HeroRunsHost()?.Interrupt(GoalInterruptRules.OrdinaryActions, "SMOKE_NEXT_TARGET"); StopWorldRoute("SMOKE_NEXT_TARGET"); for (var f = 0; f < 10; f++) AreaSmokeFrame(); }
                Clean();
                var heroFrames = new List<Image>();
                async Task<List<Image>> Walk(AreaRelight.Mode mode)
                {
                    if (mode == AreaRelight.Mode.Off) heroFrames.Clear();
                    AreaRelight.SetMode(mode); PushAreaAmbient();
                    AreaSmokePlace(map, start); hostMs = 31_000_000L; TickAreaClock(hostMs);
                    var origin = ((AreaGridSession)_worldActor.Session!).State.CommittedCell;
                    HeroRequests.Apply(new WalkRequest(new CellCoord(origin.X + dir.X, origin.Y + dir.Y), Ctrl: false));
                    var shots = new List<Image>();
                    for (var f = 0; f < Steps; f++)
                    {
                        TickAreaClock(hostMs += 100); AreaSmokeFrame();
                        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                        shots.Add(GetViewport().GetTexture().GetImage());
                        if (mode == AreaRelight.Mode.Off && _canvas.HeroFrame() is { } heroFrame && keep && f % 4 == 0) heroFrames.Add(heroFrame.GetImage());
                    }
                    GD.Print($"ARC-RES-1978 relight walk {mode} from {origin} to {((AreaGridSession)_worldActor.Session!).State.CommittedCell}");
                    Clean();
                    return shots;
                }
                var legacyWalk = await Walk(AreaRelight.Mode.Off);
                var relitWalk = await Walk(AreaRelight.Mode.NightAdd);
                Rect2I Region() => new Rect2I(160, 100, 960, 600).Intersection(new Rect2I(Vector2I.Zero, legacyWalk[0].GetSize()));
                static float Lum(Color c) => 0.299f * c.R + 0.587f * c.G + 0.114f * c.B;
                for (var f = 0; f + 1 < Steps; f++)
                {
                    double term = 0, plain = 0; var n = 0;
                    var region = Region();
                    for (var y = region.Position.Y; y < region.End.Y; y += 2)
                        for (var x = region.Position.X; x < region.End.X; x += 2)
                        {
                            var t0 = Lum(relitWalk[f].GetPixel(x, y)) - Lum(legacyWalk[f].GetPixel(x, y));
                            var t1 = Lum(relitWalk[f + 1].GetPixel(x, y)) - Lum(legacyWalk[f + 1].GetPixel(x, y));
                            var change = Math.Abs(t1 - t0); term += change; walkTermMax = Math.Max(walkTermMax, change);
                            plain += Math.Abs(Lum(legacyWalk[f + 1].GetPixel(x, y)) - Lum(legacyWalk[f].GetPixel(x, y))); n++;
                        }
                    walkTermStep += term / n; walkLegacyStep += plain / n; walkSteps++;
                }
                if (keep) for (var i = 0; i < heroFrames.Count; i++) heroFrames[i].SavePng($"{captureDir}/hero-walk-{i:00}.png");
                if (keep)
                    for (var f = 0; f < Steps; f += 10)
                    {
                        legacyWalk[f].SavePng($"{captureDir}/walk-legacy-{f:00}.png");
                        relitWalk[f].SavePng($"{captureDir}/walk-relit-{f:00}.png");
                    }
            }
            Require(walkSteps > 0, "a walk of the hero was found and recorded");
            AreaRelight.SetMode(AreaRelight.Mode.Off); PushAreaAmbient();
            AreaSmokePlace(map, start); _worldFitView = false; _camera!.ResetWorld(WorldAnchor, WorldViewport, _worldPreferredZoom); PublishWorldSelection();
            var walkReport = new { Direction = direction is { } w ? $"{w.X},{w.Y}" : "none", Steps = walkSteps, MeanTermStep = Math.Round(walkSteps > 0 ? walkTermStep / walkSteps : 0, 5),
                MeanLegacyStep = Math.Round(walkSteps > 0 ? walkLegacyStep / walkSteps : 0, 5), MaxTermStep = Math.Round(walkTermMax, 4) };
            GD.Print("ARC-RES-1978 relight walk " + System.Text.Json.JsonSerializer.Serialize(walkReport));

            // Real sprite frames (RGBA, from the player's art: kept only under ARC_RELIGHT_CAPTURE) for the lab's generator and bench.
            if (keep)
            {
                var n = 0;
                foreach (var (kind, texture) in _canvas.RelightFrames().Where(f => f.Kind is AreaRelight.KindObject or AreaRelight.KindCritter).Take(24))
                    texture.GetImage().SavePng($"{captureDir}/sprite-{(kind == AreaRelight.KindCritter ? "critter" : "object")}-{n++:00}.png");
            }

            // Frame time: Legacy and relit, with the real lights, 5 and 20 more. Vsync off so the frame is not rounded to the display's.
            DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
            async Task<(double Mean, double P95, int Lights)> Time90(AreaRelight.Mode mode, int extra)
            {
                AreaRelight.SetMode(mode); AreaRelight.Extra.Clear();
                for (var i = 0; i < extra; i++)
                {
                    var angle = i * Math.Tau / Math.Max(extra, 1);
                    var radius = 90f + 70f * (i % 3);
                    var centre = all.Field.Placements.Count > 0 ? all.Field.Placements[0].Rect : default;
                    var p = _canvas.AreaActorPlacementNow;
                    var hero = p is null ? new Vector2(centre.X + centre.Width / 2f, centre.Y + centre.Height / 2f)
                        : new Vector2(TerrainGeometry.Project(checked((int)(p.MapX ?? 0)), checked((int)(p.MapY ?? 0))).X + (p.InstanceOffsetX ?? 0) + 40,
                            TerrainGeometry.Project(checked((int)(p.MapX ?? 0)), checked((int)(p.MapY ?? 0))).Y + (p.InstanceOffsetY ?? 0) + 20);
                    AreaRelight.Extra.Add(new AreaRelight.Light(hero.X + (float)Math.Cos(angle) * radius, hero.Y + (float)Math.Sin(angle) * radius * .5f, 110f,
                        new Color(1f, .75f, .45f)));
                }
                PushAreaAmbient(); await Frames(6);
                var times = new List<double>();
                var last = Time.GetTicksUsec();
                for (var i = 0; i < 90; i++)
                {
                    AreaSmokeFrame(); await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                    var now = Time.GetTicksUsec(); times.Add((now - last) / 1000d); last = now;
                }
                times.Sort();
                return (times.Average(), times[(int)(times.Count * .95)], AreaRelight.UploadedCount);
            }
            var timing = new Dictionary<string, object>();
            foreach (var (name, mode, extra) in new[] { ("legacy", AreaRelight.Mode.Off, 0), ("relit-real", AreaRelight.Mode.NightAdd, 0), ("relit-plus5", AreaRelight.Mode.NightAdd, 5), ("relit-plus20", AreaRelight.Mode.NightAdd, 20),
                ("legacy-again", AreaRelight.Mode.Off, 0) })
            {
                var (mean, p95, lights) = await Time90(mode, extra);
                timing[name] = new { MeanMs = Math.Round(mean, 2), P95Ms = Math.Round(p95, 2), Lights = lights };
            }
            AreaRelight.Extra.Clear(); AreaRelight.SetMode(AreaRelight.Mode.Off); PushAreaAmbient();
            DisplayServer.WindowSetVsyncMode(wasVsync);

            // The normal maps a full-size RGBA8 texture per visible sprite frame would cost (terrain chunks are drawn from composed textures and not counted here).
            var sprites = _canvas.AreaVisibleCommands.Where(c => c.Identity is not null).GroupBy(c => c.Frame).Select(g => g.First()).ToArray();
            long bytes = sprites.Sum(c => (long)c.Source.Width * c.Source.Height * 4);
            var frames = sprites;
            report = new { Map = _readyMapName, Carrier = carrierReport, Setting = settingReport, Walk = walkReport, Hour = driver.Clock.Hour, Window = new { size.X, size.Y }, Differing = counts, Timing = timing,
                VisibleSpriteFrames = frames.Length, NormalBytesFullRgba8 = bytes, Lights = AreaRelight.UploadedCount };
            GD.Print("ARC-RES-1978 native-smoke relight PASS " + System.Text.Json.JsonSerializer.Serialize(report));
        }
        finally
        {
            AreaRelight.Extra.Clear(); AreaRelight.SetMode(wasMode);
            DisplayServer.WindowSetVsyncMode(wasVsync);
            if (worn) Wear(wielded.Item);
            driver.Enabled = wasEnabled; PushAreaAmbient();
            _worldFocusSimulationForSelfTest = simulatedFocus; _worldFocused = focused; _worldTestTime = testTime;
            try { AreaSmokePlace(map, home); } catch (InvalidOperationException) { }
            _worldFitView = false; _camera!.ResetWorld(WorldAnchor, WorldViewport, _worldPreferredZoom); PublishWorldSelection();
            if (modal) _worldPanel?.Show(); else _worldPanel?.Hide();
            if (_hud is not null) _hud.Visible = hudVisible;
        }
    }
}
