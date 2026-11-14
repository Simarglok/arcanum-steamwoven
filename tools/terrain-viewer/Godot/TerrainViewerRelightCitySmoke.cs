using Arcanum.Data.Lighting;
using Arcanum.Data.Terrain;
using Arcanum.Session;
using Arcanum.Simulation.Equipment;
using Arcanum.Simulation.Goals;
using Arcanum.Simulation.Movement;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

public partial class TerrainViewerApp
{
    // ARC-RES-2007 (follow-up of ARC-RES-1978, FR-LIGHT-013): the relit look in a night city. Stage "relight" with ARC_RELIGHT_SCENE=city, on a main-map window of
    // Tarant (--sector 969,1026) or Ashbury (--sector 504,975), with --hero. At hour 22 with the city's own lights only (the hero has no torch):
    //  - a survey of the window's lights (fixed lights by kind, how many reach the view, how many the 32-light upload keeps);
    //  - three spots (the densest cluster of lit lights, one far from it, a street lamp) captured in Legacy and in add / night / de-light at strength 0.6 and 1.6;
    //  - a moving light: the hero wears an extra-large torch (cold white, the colour of a will-o'-wisp stand-in; it is the original's carried-light mechanism, LT10 / LT12,
    //    which Legacy draws as well) and walks; the same walk once Legacy, once per relit mode, captured at the start and after about three seconds;
    //  - frame time of Legacy and relit over 90 frames with the city's real lights in view.
    // ARC_RELIGHT_CAPTURE (an absolute directory) keeps the PNGs (derived from the player's art: outside git); ARC_RELIGHT_CITY names the files.
    private async Task RunAreaRelightCityAsync(TerrainLoadResult? mapMode)
    {
        if (_areaSession is not { } session || session.QueryHero() is null || _worldActor.Session is not AreaGridSession || _canvas is null
            || _areaAmbient is not { } driver || _readyArea is not { Lighting: { Local: { } all, CarriedFrame: { } } })
        { GD.Print("ARC-RES-2007 native-smoke relight-city NONE map=" + _readyMapName + " (needs a hero on a lit map)"); return; }
        static void Require(bool value, string why)
        {
            if (value) return;
            GD.Print("ARC-RES-2007 relight-city FAIL: " + why);
            throw new InvalidOperationException("AREA_RELIGHT_CITY_SMOKE_FAILED");
        }
        if (session.QueryEquipment().Worn.FirstOrDefault(w => w.Slot == EquipmentSlot.Weapon) is not { } wielded
            || session.QueryHeroKit().Where(k => k.Entity == wielded.Entity).Select(k => (SessionKitItem?)k).FirstOrDefault() is not { } kit)
        { GD.Print("ARC-RES-2007 native-smoke relight-city NONE map=" + _readyMapName + " (the hero wields no kit item to light)"); return; }
        bool Wear(EquipmentItem item)
        {
            if (session.Unequip(session.Version, EquipmentSlot.Weapon, _ => SessionInsertVerdict.Accepted).Disposition == GameSessionDisposition.Rejected) return false;
            return session.Equip(session.Version, wielded.Entity, EquipmentSlot.Weapon, new(kit.Kind, [], false, item), (_, _) => true,
                _ => SessionInsertVerdict.Accepted).Disposition == GameSessionDisposition.Changed;
        }
        var captureDir = OS.GetEnvironment("ARC_RELIGHT_CAPTURE");
        var keep = !string.IsNullOrEmpty(captureDir) && captureDir.StartsWith('/');
        if (keep) DirAccess.MakeDirRecursiveAbsolute(captureDir);
        var city = string.IsNullOrWhiteSpace(OS.GetEnvironment("ARC_RELIGHT_CITY")) ? "city" : OS.GetEnvironment("ARC_RELIGHT_CITY");
        var wasEnabled = driver.Enabled; var wasMode = AreaRelight.DeveloperMode; var wasStrength = AreaRelight.Strength; var wasVsync = DisplayServer.WindowGetVsyncMode();
        var simulatedFocus = _worldFocusSimulationForSelfTest; var focused = _worldFocused; var testTime = _worldTestTime; var modal = WorldModal; var hudVisible = _hud?.Visible ?? false;
        var map = mapMode is { Area: { } mapArea, AreaStepSnapshot: { } mapSteps }
            ? new AreaMapRuntime(mapArea.Snapshot, mapSteps, mapArea.Plan.Chart) : _areaTransitions!.Current;
        var home = ((AreaGridSession)_worldActor.Session!).State.CommittedCell;
        var worn = false;
        try
        {
            _worldFocusSimulationForSelfTest = true; _worldFocused = true; SelectSimulatedPadForSelfTest();
            driver.Enabled = true;
            AreaSmokePlace(map, home);
            for (var h = 0; h < 24 && driver.Clock.Hour != 22; h++) { driver.AdvanceHours(1, AmbientSector()); LogAreaEvents(driver); }
            AreaRelight.SetMode(AreaRelight.Mode.Off); PushAreaAmbient();
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
                if (keep) image.SavePng($"{captureDir}/{city}-{name}.png");
                return image;
            }
            static int Differing(Image a, Image b)
            {
                var da = a.GetData(); var db = b.GetData(); var n = 0;
                var bytes = Math.Min(da.Length, db.Length);
                for (var i = 0; i + 3 < bytes; i += 4) if (da[i] != db[i] || da[i + 1] != db[i + 1] || da[i + 2] != db[i + 2]) n++;
                return n;
            }
            void Look(CellCoord cell)
            {
                AreaSmokePlace(map, cell);
                _worldFitView = false; _camera!.ResetWorld(WorldAnchor, WorldViewport, _worldPreferredZoom); PublishWorldSelection();
            }
            await Frames();

            // The survey: the lights the window's night lighting holds.
            var lit = all.Field.Placements.Where(p => (p.Light.Flags & (StationaryLightFlags.Off | StationaryLightFlags.Dark)) == 0).ToArray();
            var survey = new
            {
                Map = _readyMapName, Hour = driver.Clock.Hour, Placements = all.Field.Placements.Count, Lit = lit.Length,
                Dark = all.Field.Placements.Count(p => (p.Light.Flags & StationaryLightFlags.Dark) != 0),
                Off = all.Field.Placements.Count(p => (p.Light.Flags & StationaryLightFlags.Off) != 0),
                LitByTint = lit.GroupBy(p => (p.Light.Flags & (StationaryLightFlags.Indoor | StationaryLightFlags.Outdoor)).ToString()).ToDictionary(g => g.Key, g => g.Count()),
                LitRadii = lit.GroupBy(p => Math.Max(p.Rect.Width, p.Rect.Height) / 2 / 10 * 10).OrderBy(g => g.Key).ToDictionary(g => g.Key.ToString(), g => g.Count()),
                CritterLights = all.CritterLights.Count,
            };
            GD.Print("ARC-RES-2007 relight-city survey " + System.Text.Json.JsonSerializer.Serialize(survey));
            // ARC-RENDER-2064: the wisp shots stand at named tiles and need no lit light (a wilderness window of dark object lights only is fine).
            Require(lit.Length > 0 || OS.GetEnvironment("ARC_RELIGHT_WISP_SHOTS") == "1", "the window holds lit lights");

            // Three spots: the densest cluster of lit lights (most other lit lights within 350 px), and the densest one at least 800 px from it.
            Vector2 Centre((StationaryLight Light, TerrainRect Rect) p) => new(p.Rect.X + p.Rect.Width / 2f, p.Rect.Y + p.Rect.Height / 2f);
            int Density((StationaryLight Light, TerrainRect Rect) p) => lit.Count(q => Centre(q).DistanceTo(Centre(p)) < 350f);
            var spotA = lit.Length > 0 ? lit.OrderByDescending(Density).First() : default;
            var spotB = lit.Where(p => Centre(p).DistanceTo(Centre(spotA)) >= 800f).OrderByDescending(Density).Select(p => ((StationaryLight Light, TerrainRect Rect)?)p).FirstOrDefault();
            var spots = new List<(string Name, (StationaryLight Light, TerrainRect Rect) Lamp)> { ("a", spotA) };
            if (spotB is { } b) spots.Add(("b", b));
            // And a street spot: the densest outdoor-tinted light (the emitters that take the hour's outdoor ambient: street lamps and the like) away from the others.
            var street = lit.Where(p => (p.Light.Flags & StationaryLightFlags.Outdoor) != 0 && spots.All(spot => Centre(p).DistanceTo(Centre(spot.Lamp)) >= 800f))
                .OrderByDescending(Density).Select(p => ((StationaryLight Light, TerrainRect Rect)?)p).FirstOrDefault();
            if (street is { } c) spots.Add(("c", c));
            CellCoord Stand((StationaryLight Light, TerrainRect Rect) lamp)
            {
                var at = map.Chart.ToChart(new(lamp.Light.X, lamp.Light.Y));
                foreach (var radius in new[] { 4, 8, 14 })
                    try { return AreaSmokeStandable(map, new CellCoord(at.X + 2, at.Y + 2), radius); } catch (InvalidOperationException) { }
                GD.Print($"ARC-RES-2007 relight-city spot fallback: no standable cell near the lamp at {lamp.Light.X},{lamp.Light.Y}; standing at the window's start");
                return AreaSmokeStandable(map, home, 4);
            }
            // ARC-RES-2050: "shots" mode (ARC_RELIGHT_SCENE=city plus ARC_RELIGHT_SHOTS=1): full-frame captures only. Up to three spots at least 700 px apart, each with at least four lit lights
            // within 350 px, each captured at every hour of ARC_RELIGHT_HOURS (default 20,23,3) in Legacy, night 1.0 and night 1.6. Names: city-shot-<spot>-h<hour>-<variant>.png.
            if (OS.GetEnvironment("ARC_RELIGHT_SHOTS") == "1")
            {
                var shotSpots = new List<(string Name, (StationaryLight Light, TerrainRect Rect) Lamp)>();
                // ARC-RES-2061: ARC_RELIGHT_AT="x,y;x,y" names world tiles to stand at instead of the densest light clusters (the spot names are at1, at2 ...).
                var atTiles = (OS.GetEnvironment("ARC_RELIGHT_AT") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(t => t.Split(',')).Select(p => (X: long.Parse(p[0]), Y: long.Parse(p[1]))).ToArray();
                foreach (var candidate in lit.OrderByDescending(Density))
                {
                    if (atTiles.Length > 0 || shotSpots.Count == 3 || Density(candidate) < 4) break;
                    if (shotSpots.All(x => Centre(x.Lamp).DistanceTo(Centre(candidate)) >= 700f)) shotSpots.Add(((shotSpots.Count + 1).ToString(), candidate));
                }
                Require(atTiles.Length > 0 || shotSpots.Count > 0, "the window has a spot with at least four lit lights within reach");
                var hours = (OS.GetEnvironment("ARC_RELIGHT_HOURS") is { Length: > 0 } list ? list : "20,23,3").Split(',').Select(h => int.TryParse(h.Trim(), out var value) && value is >= 0 and <= 23 ? value : -1).ToArray();
                Require(hours.Length > 0 && hours.All(h => h >= 0), "ARC_RELIGHT_HOURS is a comma-separated list of hours 0..23");
                var shotLog = new List<object>();
                foreach (var hour in hours)
                {
                    for (var h = 0; h < 24 && driver.Clock.Hour != hour; h++) { driver.AdvanceHours(1, AmbientSector()); LogAreaEvents(driver); }
                    Require(driver.Clock.Hour == hour, $"the clock reached hour {hour}");
                    var places = shotSpots.Select(x => (x.Name, Cell: (CellCoord?)null, x.Lamp)).Concat(atTiles.Select((t, i) => ($"at{i + 1}", (CellCoord?)map.Chart.ToChart(new(t.X, t.Y)), default((StationaryLight Light, TerrainRect Rect))))).ToArray();
                    foreach (var (spotName, tileCell, lamp) in places)
                    {
                        if (tileCell is { } tile)
                        {
                            CellCoord stand = home; var found = false;
                            foreach (var radius in new[] { 2, 5, 9 })
                                try { stand = AreaSmokeStandable(map, tile, radius); found = true; break; } catch (InvalidOperationException) { }
                            if (!found) GD.Print($"ARC-RES-2061 relight-city tile {spotName}: no standable cell near it; standing at the window's start");
                            Look(stand);
                        }
                        else Look(Stand(lamp));
                        PushAreaAmbient(); await Frames();
                        // ARC-RENDER-2064: ARC_RELIGHT_WISP_SHOTS=1 captures the wisps' glow instead: Legacy with the glow on and off (they must be identical), the relit look (night weighted at the
                        // player's strength) without the glow and with it, and logs the wisp lights standing in reach and the lights the relit upload kept.
                        if (OS.GetEnvironment("ARC_RELIGHT_WISP_SHOTS") == "1")
                        {
                            var wasWisps = WispGlowOn;
                            try
                            {
                                AreaRelight.SetMode(AreaRelight.Mode.Off); SetWispGlow(true); PushAreaAmbient();
                                var legacyOn = await Capture($"shot-{spotName}-h{hour:00}-1-legacy");
                                SetWispGlow(false); var legacyOff = await Capture($"shot-{spotName}-h{hour:00}-1b-legacy-glow-off");
                                AreaRelight.SetMode(AreaRelight.Mode.NightAdd); AreaRelight.Strength = AreaRelight.LookStrength; PushAreaAmbient();
                                var without = await Capture($"shot-{spotName}-h{hour:00}-2-relit-no-glow");
                                SetWispGlow(true); var withGlow = await Capture($"shot-{spotName}-h{hour:00}-3-relit-wisp-glow");
                                Require(Differing(legacyOn, legacyOff) == 0, "Legacy (the relit look off) is identical with the wisp glow on and off");
                                shotLog.Add(new { Spot = spotName, Hour = hour, LegacyGlowOnOffDiffering = Differing(legacyOn, legacyOff), RelitGlowDiffering = Differing(without, withGlow),
                                    WispLights = WispRelitLights().Count(), InView = AreaRelight.InViewCount, CarriedInView = AreaRelight.CarriedInViewCount, Uploaded = AreaRelight.UploadedCount });
                            }
                            finally { SetWispGlow(wasWisps); }
                        }
                        else
                        foreach (var (variant, mode, strength) in new[] { ("1-legacy", AreaRelight.Mode.Off, wasStrength), ("2-night-10", AreaRelight.Mode.NightAdd, 1.0f), ("3-night-16", AreaRelight.Mode.NightAdd, 1.6f) })
                        {
                            AreaRelight.SetMode(mode); AreaRelight.Strength = strength; PushAreaAmbient();
                            await Capture($"shot-{spotName}-h{hour:00}-{variant}");
                            if (mode == AreaRelight.Mode.NightAdd && strength > 1.5f) shotLog.Add(new { Spot = spotName, Hour = hour, Density = tileCell is null ? Density(lamp) : -1, InView = AreaRelight.InViewCount });
                        }
                        AreaRelight.SetMode(AreaRelight.Mode.Off); AreaRelight.Strength = wasStrength; PushAreaAmbient();
                    }
                }
                GD.Print("ARC-RES-2050 relight-city shots " + System.Text.Json.JsonSerializer.Serialize(new { City = city, Spots = shotSpots.Count + atTiles.Length, Hours = hours, Scenes = shotLog }));
                GD.Print("ARC-RES-1978 native-smoke relight PASS ARC-RES-2050 shots " + city);
                return;
            }
            var variants = new (string Name, AreaRelight.Mode Mode, float Strength)[]
            {
                ("2-add-06", AreaRelight.Mode.Add, 0.6f), ("3-night-06", AreaRelight.Mode.NightAdd, 0.6f), ("4-delight-06", AreaRelight.Mode.DelightAdd, 0.6f),
                ("5-add-16", AreaRelight.Mode.Add, 1.6f), ("6-night-16", AreaRelight.Mode.NightAdd, 1.6f), ("7-delight-16", AreaRelight.Mode.DelightAdd, 1.6f),
            };
            var counts = new Dictionary<string, int>(); var lightsReport = new Dictionary<string, object>();
            foreach (var (spotName, lamp) in spots)
            {
                Look(Stand(lamp)); await Frames();
                AreaRelight.SetMode(AreaRelight.Mode.Off); PushAreaAmbient();
                var legacy = await Capture($"spot-{spotName}-1-legacy");
                foreach (var (name, mode, strength) in variants)
                {
                    AreaRelight.SetMode(mode); AreaRelight.Strength = strength; PushAreaAmbient();
                    var relit = await Capture($"spot-{spotName}-{name}");
                    counts[$"{spotName}-{name}"] = Differing(legacy, relit);
                    if (name == "3-night-06") lightsReport[spotName] = new { InView = AreaRelight.InViewCount, Uploaded = AreaRelight.UploadedCount };
                }
                AreaRelight.SetMode(AreaRelight.Mode.Off); AreaRelight.Strength = wasStrength; PushAreaAmbient();
            }
            GD.Print("ARC-RES-2007 relight-city pixels differing from Legacy " + System.Text.Json.JsonSerializer.Serialize(counts));
            GD.Print("ARC-RES-2007 relight-city lights " + System.Text.Json.JsonSerializer.Serialize(lightsReport));

            // The moving light: the hero's extra-large torch, a walk from spot a; the same walk once per mode, captured at the start and after about three seconds.
            Require(Wear(wielded.Item with { Light = EquipmentLight.ExtraLarge }), "the wielded item is worn with an extra-large light");
            worn = true;
            var start = Stand(spots[0].Lamp);
            (int X, int Y)? direction = null;
            foreach (var (dx, dy) in new[] { (8, 0), (0, 8), (-8, 0), (0, -8), (6, 6), (-6, -6), (6, -6), (-6, 6) })
            {
                AreaSmokePlace(map, start);
                long probeMs = 31_000_000L; TickAreaClock(probeMs);
                var origin = ((AreaGridSession)_worldActor.Session!).State.CommittedCell;
                HeroRequests.Apply(new WalkRequest(new CellCoord(origin.X + dx, origin.Y + dy), Ctrl: false));
                for (var f = 0; f < 80 && ((AreaGridSession)_worldActor.Session!).State.CommittedCell == origin && HeroRunsHost()?.WalkRun is not null; f++) { TickAreaClock(probeMs += 100); AreaSmokeFrame(); await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
                var moved = HeroRunsHost()?.WalkRun is not null && ((AreaGridSession)_worldActor.Session!).State.CommittedCell != origin;
                HeroRunsHost()?.Interrupt(GoalInterruptRules.OrdinaryActions, "SMOKE_NEXT_TARGET"); StopWorldRoute("SMOKE_NEXT_TARGET");
                for (var f = 0; f < 10; f++) AreaSmokeFrame();
                if (moved) { direction = (dx, dy); break; }
            }
            Require(direction is not null, "a walk of the hero from the densest spot was found");
            var dir = direction!.Value;
            var walkCounts = new Dictionary<string, int>();
            async Task<List<Image>> Walk(string label, AreaRelight.Mode mode, float strength)
            {
                AreaRelight.SetMode(mode); AreaRelight.Strength = strength; PushAreaAmbient();
                Look(start);
                long hostMs = 31_000_000L; TickAreaClock(hostMs);
                var origin = ((AreaGridSession)_worldActor.Session!).State.CommittedCell;
                HeroRequests.Apply(new WalkRequest(new CellCoord(origin.X + dir.X, origin.Y + dir.Y), Ctrl: false));
                var shots = new List<Image>();
                for (var f = 0; f < 30; f++)
                {
                    TickAreaClock(hostMs += 100); AreaSmokeFrame();
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                    if (f is 2 or 29)
                    {
                        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                        var image = GetViewport().GetTexture().GetImage(); shots.Add(image);
                        if (keep) image.SavePng($"{captureDir}/{city}-walk-{label}-{(f == 2 ? "0-start" : "1-later")}.png");
                    }
                }
                HeroRunsHost()?.Interrupt(GoalInterruptRules.OrdinaryActions, "SMOKE_NEXT_TARGET"); StopWorldRoute("SMOKE_NEXT_TARGET");
                for (var f = 0; f < 10; f++) AreaSmokeFrame();
                return shots;
            }
            var legacyWalk = await Walk("1-legacy", AreaRelight.Mode.Off, wasStrength);
            foreach (var (name, mode, strength) in new[] { ("2-add-06", AreaRelight.Mode.Add, 0.6f), ("3-night-06", AreaRelight.Mode.NightAdd, 0.6f), ("4-delight-06", AreaRelight.Mode.DelightAdd, 0.6f), ("6-night-16", AreaRelight.Mode.NightAdd, 1.6f) })
            {
                var relitWalk = await Walk(name, mode, strength);
                walkCounts[name] = relitWalk.Select((img, i) => Differing(legacyWalk[i], img)).Sum();
            }
            AreaRelight.SetMode(AreaRelight.Mode.Off); AreaRelight.Strength = wasStrength; PushAreaAmbient();
            GD.Print($"ARC-RES-2007 relight-city walk direction={dir.X},{dir.Y} pixels differing from Legacy (both captures) " + System.Text.Json.JsonSerializer.Serialize(walkCounts));

            // Frame time at spot a: Legacy and relit (night weighted, 0.6) with the city's real lights in view, 90 frames each, vsync off.
            Look(Stand(spots[0].Lamp)); await Frames();
            DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
            async Task<object> Time90(AreaRelight.Mode mode)
            {
                AreaRelight.SetMode(mode); PushAreaAmbient(); await Frames(6);
                var times = new List<double>(); var last = Time.GetTicksUsec();
                for (var i = 0; i < 90; i++)
                {
                    AreaSmokeFrame(); await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                    var now = Time.GetTicksUsec(); times.Add((now - last) / 1000d); last = now;
                }
                times.Sort();
                // The counts are those of the last upload, which only a relit mode makes (the hero's torch is in the list during this run).
                var relit = mode != AreaRelight.Mode.Off;
                return new { MeanMs = Math.Round(times.Average(), 2), P95Ms = Math.Round(times[(int)(times.Count * .95)], 2), Uploaded = relit ? AreaRelight.UploadedCount : (int?)null, InView = relit ? AreaRelight.InViewCount : (int?)null };
            }
            var timing = new Dictionary<string, object>();
            foreach (var (name, mode) in new[] { ("legacy", AreaRelight.Mode.Off), ("relit-night", AreaRelight.Mode.NightAdd), ("relit-add", AreaRelight.Mode.Add), ("legacy-again", AreaRelight.Mode.Off) })
                timing[name] = await Time90(mode);
            AreaRelight.SetMode(AreaRelight.Mode.Off); PushAreaAmbient();
            DisplayServer.WindowSetVsyncMode(wasVsync);
            GD.Print("ARC-RES-2007 relight-city timing " + System.Text.Json.JsonSerializer.Serialize(timing));
            Require(counts.Values.Any(c => c > 0), "a relit capture differs from Legacy");
            GD.Print("ARC-RES-1978 native-smoke relight PASS " + System.Text.Json.JsonSerializer.Serialize(new { City = city, Map = _readyMapName, Hour = driver.Clock.Hour, Survey = survey, Differing = counts, Lights = lightsReport, Walk = walkCounts, Timing = timing }));
        }
        finally
        {
            AreaRelight.SetMode(wasMode); AreaRelight.Strength = wasStrength;
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
