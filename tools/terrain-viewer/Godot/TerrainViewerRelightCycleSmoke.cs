using Arcanum.Data.Lighting;
using Arcanum.Data.Terrain;
using Arcanum.Experiments.RelightLab;
using Arcanum.Session;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Equipment;
using Arcanum.Simulation.Goals;
using Arcanum.Simulation.Movement;
using Arcanum.World;
using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

public partial class TerrainViewerApp
{
    // ARC-RES-2000 (follow-up of ARC-RES-1978, FR-LIGHT-013 and FR-PRODUCT-007): stage "relight" with ARC_RELIGHT_SCENE=cycle, the main map at the Crash Site, with --hero. At the night hour, the night-weighted relit look at the shipped strength, three measurements the first experiment left open:
    //  1. walk-cycle flicker (the hero carries no light; two lamps stand beside it, lit in the relit list only): the hero's own Walk frames 0..N-1 of every facing drawn one after the other through the production draw path (the pose is forced in place, so the camera and the
    //     light stand still and only the art frame changes), once Legacy and once relit; per facing the relit term (relit minus Legacy luminance over the hero's crop) is compared from one
    //     frame to the next with the Legacy picture's own frame-to-frame change; the raw frames are written under ARC_RELIGHT_CAPTURE for the lab's `shimmer` command (the same frames, the
    //     normals' own shimmer under a fixed light);
    //  2. frame times of Legacy and of the relit look with exactly 1, 5 and 20 lights (only the smoke's own lights, ringed round the hero: no carried or map light), at the window sizes of ARC_RELIGHT_SIZES (default 1280x800, 1920x1200, 2560x1600: the stand-in for a
    //     weaker GPU, the pixel load grows 1x, 2.25x, 4x while the scene stays the same), vsync off;
    //  3. the Godot-native comparison on the same pixels: the viewer's frame is captured and drawn as a CanvasTexture (diffuse: the capture, normal: the lab generator's map of it) with the
    //     map's visible sprite frames and PointLight2D nodes at the very lights the shader loop used, timed with 0, 1, 5 and 20 lights at the same sizes.
    // ARC_RELIGHT_CAPTURE (an absolute directory) keeps the PNGs; they are derived from the player's art and stay out of git.
    private async Task RunAreaRelightCycleAsync(TerrainLoadResult? mapMode)
    {
        if (_areaSession is not { } session || session.QueryHero() is null || _worldActor.Session is not AreaGridSession || _canvas is null
            || _areaAmbient is not { } driver || _readyArea is not { Lighting: { Local: { } all, CarriedFrame: { } } })
        { GD.Print("ARC-RES-2000 native-smoke relight-cycle NONE map=" + _readyMapName + " (needs a hero on a lit map)"); return; }
        static void Require(bool value, string why)
        {
            if (value) return;
            GD.Print("ARC-RES-2000 relight-cycle FAIL: " + why);
            throw new InvalidOperationException("AREA_RELIGHT_CYCLE_SMOKE_FAILED");
        }
        if (session.QueryEquipment().Worn.FirstOrDefault(w => w.Slot == EquipmentSlot.Weapon) is not { } wielded
            || session.QueryHeroKit().Where(k => k.Entity == wielded.Entity).Select(k => (SessionKitItem?)k).FirstOrDefault() is not { } kit)
        { GD.Print("ARC-RES-2000 native-smoke relight-cycle NONE map=" + _readyMapName + " (the hero wields no kit item to light)"); return; }
        bool Wear(EquipmentItem item)
        {
            if (session.Unequip(session.Version, EquipmentSlot.Weapon, _ => SessionInsertVerdict.Accepted).Disposition == GameSessionDisposition.Rejected) return false;
            return session.Equip(session.Version, wielded.Entity, EquipmentSlot.Weapon, new(kit.Kind, [], false, item), (_, _) => true,
                _ => SessionInsertVerdict.Accepted).Disposition == GameSessionDisposition.Changed;
        }
        var captureDir = OS.GetEnvironment("ARC_RELIGHT_CAPTURE");
        var keep = !string.IsNullOrEmpty(captureDir) && captureDir.StartsWith('/');
        if (keep) { DirAccess.MakeDirRecursiveAbsolute(captureDir); DirAccess.MakeDirRecursiveAbsolute(captureDir + "/cycle"); }
        var wasEnabled = driver.Enabled; var wasMode = AreaRelight.DeveloperMode; var wasStrength = AreaRelight.Strength; var wasVsync = DisplayServer.WindowGetVsyncMode();
        var wasWindow = DisplayServer.WindowGetSize(); var wasProfile = _canvas.VisualProfile; var wasSource = _canvas.HdSource; var wasBudget = _canvas.HdBudget;
        var simulatedFocus = _worldFocusSimulationForSelfTest; var focused = _worldFocused; var testTime = _worldTestTime; var modal = WorldModal; var hudVisible = _hud?.Visible ?? false;
        var map = mapMode is { Area: { } mapArea, AreaStepSnapshot: { } mapSteps }
            ? new AreaMapRuntime(mapArea.Snapshot, mapSteps, mapArea.Plan.Chart) : _areaTransitions!.Current;
        var home = ((AreaGridSession)_worldActor.Session!).State.CommittedCell;
        var worn = false; CanvasLayer? probeLayer = null;
        var viewport = GetViewport();
        try
        {
            _worldFocusSimulationForSelfTest = true; _worldFocused = true; SelectSimulatedPadForSelfTest();
            driver.Enabled = true;
            _canvas.SetVisualProfile(VisualProfile.Legacy, wasSource, wasBudget); SyncRelitLook();
            AreaSmokePlace(map, AreaSmokeStandable(map, home, 2));
            for (var h = 0; h < 24 && driver.Clock.Hour != 22; h++) { driver.AdvanceHours(1, AmbientSector()); LogAreaEvents(driver); }
            AreaRelight.SetMode(AreaRelight.Mode.Off); PushAreaAmbient();
            _worldFitView = false; _camera!.ResetWorld(WorldAnchor, WorldViewport, _worldPreferredZoom); PublishWorldSelection();
            _worldPanel?.Hide(); if (_hud is not null) _hud.Visible = false;
            async Task Frames(int count = 4)
            {
                for (var i = 0; i < count; i++) { AreaSmokeFrame(); await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
                TickCarriedLight(); await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }
            async Task<Image> Capture(string? name = null)
            {
                await Frames();
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                var image = viewport.GetTexture().GetImage();
                if (keep && name is not null) image.SavePng($"{captureDir}/{name}.png");
                return image;
            }
            await Frames();
            await Frames();
            AreaRelight.Strength = AreaRelight.LookStrength; // the shipped look's energy, night-weighted

            // The hero's own world position (the lights below stand beside it).
            Vector2 HeroPoint()
            {
                var p = _canvas.AreaActorPlacementNow;
                return p is null ? new Vector2(0, 0)
                    : new Vector2(TerrainGeometry.Project(checked((int)(p.MapX ?? 0)), checked((int)(p.MapY ?? 0))).X + (p.InstanceOffsetX ?? 0) + 40,
                        TerrainGeometry.Project(checked((int)(p.MapX ?? 0)), checked((int)(p.MapY ?? 0))).Y + (p.InstanceOffsetY ?? 0) + 20);
            }
            // ---- 1. Walk-cycle flicker --------------------------------------------------------------------------------------------------------------------------------------------
            var walkFrames = _worldActor.Profile.Resource(ActorDiagnosticAction.Walk).FramesPerGroup;
            const int Facings = 8;
            var legacyCrops = new Image[Facings, walkFrames]; var relitCrops = new Image[Facings, walkFrames]; var raw = new Image?[Facings, walkFrames]; var rawAt = new Vector2I[Facings, walkFrames];
            var size = viewport.GetTexture().GetSize();
            var crop = new Rect2I((int)size.X / 2 - 80, (int)size.Y / 2 - 150, 160, 220).Intersection(new Rect2I(Vector2I.Zero, (Vector2I)size));
            _canvas.SetActorVisibilityForReadback(false); AreaRelight.SetMode(AreaRelight.Mode.Off); PushAreaAmbient();
            var backgroundCrop = (await Capture()).GetRegion(crop); // the picture without the hero: where a frame differs from it is the hero
            _canvas.SetActorVisibilityForReadback(true);
            foreach (var mode in new[] { AreaRelight.Mode.Off, AreaRelight.Mode.NightAdd })
            {
                // The hero carries no light here: the night weight reads the item's own Legacy light, which a carried torch would raise at the very anchor it lights (the relit term of the
                // light's carrier then nearly vanishes), so two lamps stand beside the hero, one warm on its right and one cool on its left, lit only in the relit list (the Legacy lattice never sees them).
                AreaRelight.SetMode(mode); AreaRelight.Extra.Clear(); AreaRelight.OnlyExtra = mode != AreaRelight.Mode.Off;
                if (mode != AreaRelight.Mode.Off)
                {
                    var hero = HeroPoint();
                    AreaRelight.Extra.Add(new AreaRelight.Light(hero.X + 70, hero.Y - 10, 160f, new Color(1f, .75f, .45f)));
                    AreaRelight.Extra.Add(new AreaRelight.Light(hero.X - 90, hero.Y + 10, 160f, new Color(.55f, .7f, 1f)));
                }
                PushAreaAmbient(); await Frames(4);
                for (var facing = 0; facing < Facings; facing++)
                    for (var frame = 0; frame < walkFrames; frame++)
                    {
                        _worldActor.AttackPose = (ActorDiagnosticAction.Walk, facing, frame); PublishWorldSelection();
                        var image = await Capture();
                        (mode == AreaRelight.Mode.Off ? legacyCrops : relitCrops)[facing, frame] = image.GetRegion(crop);
                        if (mode == AreaRelight.Mode.Off && _canvas.HeroFrame() is { } heroFrame)
                        {
                            var heroImage = heroFrame.GetImage(); if (_readyActor!.FlipX) heroImage.FlipX(); // as drawn: a mirrored facing is the group's frame flipped
                            raw[facing, frame] = heroImage;
                            // Where the frame's top left lies from the anchor: a walk cycle's frames are cut to their own size, so the lab compares them on one canvas by this offset.
                            rawAt[facing, frame] = new Vector2I((int)(_readyActor!.Destination.X - _readyActor.AnchorX), (int)(_readyActor.Destination.Y - _readyActor.AnchorY));
                        }
                    }
            }
            _worldActor.AttackPose = null; PublishWorldSelection();
            AreaRelight.OnlyExtra = false; AreaRelight.Extra.Clear(); AreaRelight.SetMode(AreaRelight.Mode.Off); PushAreaAmbient();
            // The frames of one facing on a common canvas (transparent padding, each frame at its offset from the anchor) for the lab's `shimmer`: same-texel pairs then compare the same body part.
            for (var facing = 0; facing < Facings; facing++)
            {
                int left = int.MaxValue, top = int.MaxValue, right = int.MinValue, bottom = int.MinValue;
                for (var frame = 0; frame < walkFrames; frame++)
                    if (raw[facing, frame] is { } r) { left = Math.Min(left, rawAt[facing, frame].X); top = Math.Min(top, rawAt[facing, frame].Y); right = Math.Max(right, rawAt[facing, frame].X + r.GetWidth()); bottom = Math.Max(bottom, rawAt[facing, frame].Y + r.GetHeight()); }
                if (right <= left) continue;
                for (var frame = 0; frame < walkFrames; frame++)
                {
                    if (raw[facing, frame] is not { } r) continue;
                    var canvas = Image.CreateEmpty(right - left, bottom - top, false, Image.Format.Rgba8);
                    r.Convert(Image.Format.Rgba8); canvas.BlitRect(r, new Rect2I(Vector2I.Zero, r.GetSize()), new Vector2I(rawAt[facing, frame].X - left, rawAt[facing, frame].Y - top));
                    raw[facing, frame] = canvas;
                    if (keep) { DirAccess.MakeDirRecursiveAbsolute($"{captureDir}/cycle-frames/f{facing}"); canvas.SavePng($"{captureDir}/cycle-frames/f{facing}/{frame:00}.png"); }
                }
            }
            static float Lum(Color c) => 0.299f * c.R + 0.587f * c.G + 0.114f * c.B;
            static bool SameImage(Image? a, Image? b) => a is not null && b is not null && a.GetSize() == b.GetSize() && a.GetData().AsSpan().SequenceEqual(b.GetData());
            var cycleReport = new List<object>(); double sumTerm = 0, sumPlain = 0, sumHeroTerm = 0, sumHeroPlain = 0, sumHeroLevel = 0, worstTermMax = 0, worstHeroMax = 0; var heroFramesSeen = 0; var distinctTotal = 0;
            bool OnHero(int x, int y, Image legacy) { var a = legacy.GetPixel(x, y); var b = backgroundCrop.GetPixel(x, y); return Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B) > .004f; }
            for (var facing = 0; facing < Facings; facing++)
            {
                double term = 0, plain = 0, termMax = 0, heroTerm = 0, heroPlain = 0, heroMax = 0, heroLevel = 0; var pairs = 0; var distinct = 1;
                for (var frame = 0; frame < walkFrames; frame++)
                {
                    var next = (frame + 1) % walkFrames; // the cycle loops: the last frame is followed by the first
                    if (raw[facing, frame] is not null) heroFramesSeen++;
                    if (frame + 1 < walkFrames && !SameImage(raw[facing, frame], raw[facing, frame + 1])) distinct++;
                    double t = 0, p = 0, ht = 0, hp = 0, hl = 0; int n = 0, hn = 0;
                    for (var y = 0; y < crop.Size.Y; y++)
                        for (var x = 0; x < crop.Size.X; x++)
                        {
                            float l0 = Lum(legacyCrops[facing, frame].GetPixel(x, y)), l1 = Lum(legacyCrops[facing, next].GetPixel(x, y));
                            float t0 = Lum(relitCrops[facing, frame].GetPixel(x, y)) - l0, t1 = Lum(relitCrops[facing, next].GetPixel(x, y)) - l1;
                            var change = Math.Abs(t1 - t0); t += change; termMax = Math.Max(termMax, change); p += Math.Abs(l1 - l0); n++;
                            // The hero's own pixels (where either frame differs from the picture without the hero): the lit body, not the ground round it.
                            if (OnHero(x, y, legacyCrops[facing, frame]) || OnHero(x, y, legacyCrops[facing, next])) { ht += change; heroMax = Math.Max(heroMax, change); hp += Math.Abs(l1 - l0); hl += t0; hn++; }
                        }
                    term += t / n; plain += p / n; if (hn > 0) { heroTerm += ht / hn; heroPlain += hp / hn; heroLevel += hl / hn; } pairs++;
                }
                distinctTotal += distinct; sumTerm += term / pairs; sumPlain += plain / pairs; worstTermMax = Math.Max(worstTermMax, termMax);
                sumHeroTerm += heroTerm / pairs; sumHeroPlain += heroPlain / pairs; sumHeroLevel += heroLevel / pairs; worstHeroMax = Math.Max(worstHeroMax, heroMax);
                cycleReport.Add(new { Facing = facing, Frames = walkFrames, DistinctSpriteFrames = distinct, MeanTermStep = Math.Round(term / pairs, 5), MeanLegacyStep = Math.Round(plain / pairs, 5),
                    Ratio = plain == 0 ? 0 : Math.Round(term / plain, 3), MaxTermStep = Math.Round(termMax, 3),
                    HeroTermLevel = Math.Round(heroLevel / pairs, 4), HeroTermStep = Math.Round(heroTerm / pairs, 5), HeroLegacyStep = Math.Round(heroPlain / pairs, 5),
                    HeroRatio = heroPlain == 0 ? 0 : Math.Round(heroTerm / heroPlain, 3), HeroMaxTermStep = Math.Round(heroMax, 3) });
            }
            GD.Print("ARC-RES-2000 relight-cycle walk " + System.Text.Json.JsonSerializer.Serialize(new { Hour = driver.Clock.Hour, Strength = AreaRelight.LookStrength, CropW = crop.Size.X, CropH = crop.Size.Y,
                WalkFrames = walkFrames, HeroFramesSeen = heroFramesSeen, DistinctSpriteFrames = distinctTotal, MeanRatio = sumPlain == 0 ? 0 : Math.Round(sumTerm / sumPlain, 3),
                MaxTermStep = Math.Round(worstTermMax, 3), HeroTermLevel = Math.Round(sumHeroLevel / Facings, 4), HeroRatio = sumHeroPlain == 0 ? 0 : Math.Round(sumHeroTerm / sumHeroPlain, 3),
                HeroMaxTermStep = Math.Round(worstHeroMax, 3), Facings = cycleReport }));
            Require(sumHeroLevel / Facings > .005, "the hero's own pixels take a relit term from the lamps beside it");
            Require(distinctTotal > Facings, "the walk cycle's frames are not all one picture (the hero's walk frames animate)");
            if (keep)
            {
                // The composite's strips: Legacy and relit crops of facing 2 (a side view) and facing 5, enlarged 3x nearest, one row per mode.
                foreach (var facing in new[] { 2, 5 })
                    for (var frame = 0; frame < walkFrames; frame++)
                    {
                        legacyCrops[facing, frame].SavePng($"{captureDir}/cycle/crop-f{facing}-{frame:00}-legacy.png");
                        relitCrops[facing, frame].SavePng($"{captureDir}/cycle/crop-f{facing}-{frame:00}-relit.png");
                    }
            }

            Require(Wear(wielded.Item with { Light = EquipmentLight.Large }), "the wielded item is worn again with a large light");
            worn = true;
            await Frames();
            // ---- 2. and 3. Frame times, shader loop against Godot's own lights ----------------------------------------------------------------------------------------------
            var spriteFrames = _canvas.RelightFrames().Where(f => f.Kind is AreaRelight.KindObject or AreaRelight.KindCritter).Take(24)
                .Select(f => (f.Kind, Image: f.Texture.GetImage())).ToArray();
            DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
            var rid = viewport.GetViewportRid();
            RenderingServer.ViewportSetMeasureRenderTime(rid, true);
            // Two ways: "frame" is the smoke's own frame (the area step and the viewer's per-frame work, as the first experiment timed it); "bare" only waits for the next frame, so the
            // CPU side is as small as the viewer lets it be and the GPU's share shows (the OpenGL compatibility renderer has no GPU timer and its GPU column stays 0; on macOS the viewer now runs Metal, whose column is read).
            async Task<(double Mean, double P95, double Gpu, double Cpu)> Time90(bool bare)
            {
                for (var i = 0; i < 6; i++) { if (!bare) AreaSmokeFrame(); await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
                var times = new List<double>(); double gpu = 0, cpu = 0; var last = Time.GetTicksUsec();
                for (var i = 0; i < 120; i++)
                {
                    if (!bare) AreaSmokeFrame();
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                    var now = Time.GetTicksUsec(); times.Add((now - last) / 1000d); last = now;
                    gpu += RenderingServer.ViewportGetMeasuredRenderTimeGpu(rid); cpu += RenderingServer.ViewportGetMeasuredRenderTimeCpu(rid);
                }
                times.Sort();
                return (Math.Round(times.Average(), 2), Math.Round(times[(int)(times.Count * .95)], 2), Math.Round(gpu / 120, 3), Math.Round(cpu / 120, 3));
            }
            static double Median(IEnumerable<double> values) { var sorted = values.OrderBy(v => v).ToArray(); return sorted[sorted.Length / 2]; }
            // Three rounds of each, the median of the rounds' means (the run-to-run noise of one round is about half a millisecond).
            async Task<object> Both()
            {
                var f = new List<(double Mean, double P95, double Gpu, double Cpu)>(); var b = new List<(double Mean, double P95, double Gpu, double Cpu)>();
                for (var round = 0; round < 3; round++) { f.Add(await Time90(false)); b.Add(await Time90(true)); }
                return new { Frame = Math.Round(Median(f.Select(t => t.Mean)), 2), FrameP95 = Math.Round(Median(f.Select(t => t.P95)), 2), Bare = Math.Round(Median(b.Select(t => t.Mean)), 2),
                    BareP95 = Math.Round(Median(b.Select(t => t.P95)), 2), Cpu = Math.Round(Median(b.Select(t => t.Cpu)), 3), Gpu = Math.Round(Median(b.Select(t => t.Gpu)), 3) };
            }
            void PlaceExtra(int extra)
            {
                AreaRelight.Extra.Clear();
                var hero = HeroPoint();
                for (var i = 0; i < extra; i++)
                {
                    var angle = i * Math.Tau / Math.Max(extra, 1); var radius = 90f + 70f * (i % 3);
                    AreaRelight.Extra.Add(new AreaRelight.Light(hero.X + (float)Math.Cos(angle) * radius, hero.Y + (float)Math.Sin(angle) * radius * .5f, 110f, new Color(1f, .75f, .45f)));
                }
            }
            var sizes = (OS.GetEnvironment("ARC_RELIGHT_SIZES") is { Length: > 0 } sizeText ? sizeText : "1280x800,1920x1200,2560x1600").Split(',')
                .Select(s => s.Split('x')).Where(a => a.Length == 2 && int.TryParse(a[0], out _) && int.TryParse(a[1], out _)).Select(a => new Vector2I(int.Parse(a[0]), int.Parse(a[1]))).ToArray();
            var gradient = new Gradient { Offsets = [0f, .25f, .5f, .75f, 1f], Colors = [new Color(1, 1, 1), new Color(.5625f, .5625f, .5625f), new Color(.25f, .25f, .25f), new Color(.0625f, .0625f, .0625f), new Color(0, 0, 0)] };
            var lightTexture = new GradientTexture2D { Gradient = gradient, Fill = GradientTexture2D.FillEnum.Radial, FillFrom = new Vector2(.5f, .5f), FillTo = new Vector2(1f, .5f), Width = 256, Height = 256 };
            // The Godot-native way to a night look is the art's own colours times an ambient plus lights, so the native backdrop is the day frame (the Legacy picture with the day ambient: the
            // art's colours nearly untinted) under a CanvasModulate of the night/day ratio of a dark patch far from any light, and the lights come from PointLight2D: no lattice, no per-pixel light mask.
            ImageTexture Normal(Image image, SpriteKind kind)
            {
                var rgba = image.Duplicate() as Image ?? image; rgba.Convert(Image.Format.Rgba8);
                var map = NormalMapGenerator.Generate(rgba.GetData(), rgba.GetWidth(), rgba.GetHeight(), NormalMapOptions.For(kind));
                return ImageTexture.CreateFromImage(Image.CreateFromData(rgba.GetWidth(), rgba.GetHeight(), false, Image.Format.Rgba8, map.Rgba));
            }
            for (var h = 0; h < 24 && driver.Clock.Hour != 14; h++) { driver.AdvanceHours(1, AmbientSector()); LogAreaEvents(driver); }
            AreaRelight.SetMode(AreaRelight.Mode.Off); PushAreaAmbient(); await Frames();
            var dayImage = await Capture(keep ? "cycle/day-legacy" : null);
            for (var h = 0; h < 24 && driver.Clock.Hour != 22; h++) { driver.AdvanceHours(1, AmbientSector()); LogAreaEvents(driver); }
            PushAreaAmbient(); await Frames();
            var nightImage = await Capture(keep ? "cycle/night-legacy" : null);
            var patch = new Rect2I(20, nightImage.GetHeight() - 200, 200, 150).Intersection(new Rect2I(Vector2I.Zero, nightImage.GetSize()));
            Color PatchMean(Image image) { double r = 0, g = 0, bl = 0; var n = patch.Size.X * patch.Size.Y; for (var y = patch.Position.Y; y < patch.End.Y; y++) for (var x = patch.Position.X; x < patch.End.X; x++) { var c = image.GetPixel(x, y); r += c.R; g += c.G; bl += c.B; } return new Color((float)(r / n), (float)(g / n), (float)(bl / n)); }
            var dayMean = PatchMean(dayImage); var nightMean = PatchMean(nightImage);
            var nightAmbient = new Color(Math.Clamp(nightMean.R / Math.Max(dayMean.R, .01f), 0f, 1f), Math.Clamp(nightMean.G / Math.Max(dayMean.G, .01f), 0f, 1f), Math.Clamp(nightMean.B / Math.Max(dayMean.B, .01f), 0f, 1f));
            GD.Print($"ARC-RES-2000 relight-cycle native ambient (night/day of a dark patch) {nightAmbient}");
            var backdropDiffuse = ImageTexture.CreateFromImage(dayImage); var backdropNormal = Normal(dayImage, SpriteKind.Ground);
            AreaRelight.SetMode(AreaRelight.Mode.NightAdd); AreaRelight.OnlyExtra = false; AreaRelight.Extra.Clear(); PushAreaAmbient(); await Frames(6);
            var realLights = AreaRelight.UploadedLights.ToArray();
            AreaRelight.SetMode(AreaRelight.Mode.Off); PushAreaAmbient();
            var report = new List<object>(); var seen = new HashSet<Vector2I>();
            foreach (var wanted in sizes)
            {
                DisplayServer.WindowSetSize(wanted);
                for (var i = 0; i < 12; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                var actual = DisplayServer.WindowGetSize();
                if (!seen.Add(actual)) { GD.Print($"ARC-RES-2000 relight-cycle size {wanted.X}x{wanted.Y} gave {actual.X}x{actual.Y} (already measured, skipped)"); continue; }
                _worldFitView = false; _camera!.ResetWorld(WorldAnchor, WorldViewport, _worldPreferredZoom); PublishWorldSelection();
                await Frames(8);
                var pixels = (double)actual.X * actual.Y;
                var shader = new Dictionary<string, object>();
                // Legacy, then the relit look with exactly 1, 5 and 20 lights: the smoke lights only its own extra lights (no carried or map light), ringed round the hero.
                AreaRelight.SetMode(AreaRelight.Mode.Off); AreaRelight.Extra.Clear(); PushAreaAmbient();
                shader["legacy"] = await Both();
                AreaRelight.SetMode(AreaRelight.Mode.NightAdd); AreaRelight.OnlyExtra = true;
                var lightSets = new Dictionary<int, AreaRelight.Light[]>();
                foreach (var target in new[] { 1, 5, 20 })
                {
                    PlaceExtra(target); PushAreaAmbient(); await Frames(6);
                    lightSets[target] = AreaRelight.UploadedLights.ToArray();
                    Require(AreaRelight.UploadedCount == target, $"the relit run lights exactly {target} lights ({AreaRelight.UploadedCount})");
                    shader[$"relit-{target}"] = await Both();
                    if (keep && target == 5) await Capture($"cycle/viewer-{actual.X}x{actual.Y}-relit-5");
                }
                AreaRelight.OnlyExtra = false;
                if (keep) { AreaRelight.SetMode(AreaRelight.Mode.Off); AreaRelight.Extra.Clear(); PushAreaAmbient(); await Capture($"cycle/viewer-{actual.X}x{actual.Y}-legacy"); }
                AreaRelight.Extra.Clear();

                // The Godot-native scene on the same pixels: the Legacy frame as a CanvasTexture with the lab generator's normal map of it, the visible sprite frames with theirs.
                AreaRelight.SetMode(AreaRelight.Mode.Off); PushAreaAmbient();
                var logical = viewport.GetVisibleRect().Size;
                var generate = System.Diagnostics.Stopwatch.StartNew();
                probeLayer = new CanvasLayer { Layer = 80 };
                var root = new Node2D(); probeLayer.AddChild(root);
                probeLayer.AddChild(new CanvasModulate { Color = nightAmbient });
                var backdrop = new Sprite2D { Centered = false, Texture = new CanvasTexture { DiffuseTexture = backdropDiffuse, NormalTexture = backdropNormal },
                    Scale = new Vector2(logical.X / dayImage.GetWidth(), logical.Y / dayImage.GetHeight()) };
                root.AddChild(backdrop);
                var spriteIndex = 0; var spriteHolder = new Node2D(); root.AddChild(spriteHolder);
                foreach (var (kind, image) in spriteFrames)
                {
                    var column = spriteIndex % 8; var row = spriteIndex / 8; spriteIndex++;
                    spriteHolder.AddChild(new Sprite2D { Centered = false, Position = new Vector2(70 + column * 140, 70 + row * 130),
                        Texture = new CanvasTexture { DiffuseTexture = ImageTexture.CreateFromImage(image), NormalTexture = Normal(image, kind == AreaRelight.KindCritter ? SpriteKind.Critter : SpriteKind.Object) } });
                }
                generate.Stop();
                GD.Print($"ARC-RES-2000 relight-cycle native scene {actual.X}x{actual.Y}: backdrop {dayImage.GetWidth()}x{dayImage.GetHeight()}, {spriteIndex} sprite frames, normals generated in {generate.ElapsedMilliseconds} ms");
                AddChild(probeLayer); _canvas.Visible = false;
                var native = new Dictionary<string, object>();
                // Energy: the look's strength times about 0.9 (the night weight at this hour, 1 minus the ambient's luminance).
                var lightEnergy = AreaRelight.LookStrength * .9f;
                var zoom = viewport.CanvasTransform.Scale; var origin = viewport.CanvasTransform.Origin;
                var lights = new List<PointLight2D>();
                void SetLights(AreaRelight.Light[] set)
                {
                    foreach (var light in lights) { root.RemoveChild(light); light.Free(); }
                    lights.Clear();
                    foreach (var l in set)
                    {
                        var node = new PointLight2D { Texture = lightTexture, Color = l.Colour, Energy = lightEnergy, Height = AreaRelight.Height, ShadowEnabled = false, RangeLayerMin = probeLayer!.Layer, RangeLayerMax = probeLayer.Layer, // a light reaches the canvas layers in its range only (default 0..0)
                            TextureScale = 2f * l.Radius * zoom.X / 256f, Position = new Vector2(l.X * zoom.X + origin.X, l.Y * zoom.Y + origin.Y) };
                        root.AddChild(node); lights.Add(node);
                    }
                    if (set.Length > 0) GD.Print($"ARC-RES-2000 relight-cycle native lights {set.Length}: first world {set[0].X:0.#},{set[0].Y:0.#} r={set[0].Radius:0.#} -> screen {lights[0].Position} scale {lights[0].TextureScale:0.###} canvas origin {origin} zoom {zoom} visible {viewport.GetVisibleRect()}");
                }
                SetLights([]);
                native["native-0"] = await Both();
                spriteHolder.Visible = false; var native0Image = await Capture(keep ? $"cycle/native-{actual.X}x{actual.Y}-0" : null); spriteHolder.Visible = true;
                foreach (var (target, set) in lightSets.OrderBy(p => p.Key))
                {
                    SetLights(set);
                    native[$"native-{target}"] = await Both();
                    if (target == 5)
                    {
                        spriteHolder.Visible = false; var native5Image = await Capture(keep ? $"cycle/native-{actual.X}x{actual.Y}-5" : null); spriteHolder.Visible = true;
                        var a = native0Image.GetData(); var b = native5Image.GetData(); var differing = 0;
                        for (var i = 0; i + 3 < Math.Min(a.Length, b.Length); i += 4) if (a[i] != b[i] || a[i + 1] != b[i + 1] || a[i + 2] != b[i + 2]) differing++;
                        GD.Print($"ARC-RES-2000 relight-cycle native 5 lights change {differing} pixels of {actual.X * actual.Y}");
                        Require(differing > 2000, "the five PointLight2D lights light the native scene");
                    }
                }
                if (keep && report.Count == 0)
                {
                    // The two looks side by side for the composite: the viewer's relit look with the real lights and five more, and the native scene with the same lights.
                    var look = realLights.Concat(lightSets[5]).ToArray();
                    SetLights(look); spriteHolder.Visible = false; await Capture($"cycle/native-look-{actual.X}x{actual.Y}"); spriteHolder.Visible = true; SetLights([]);
                    probeLayer.Visible = false; _canvas.Visible = true;
                    AreaRelight.SetMode(AreaRelight.Mode.NightAdd); AreaRelight.OnlyExtra = false; PlaceExtra(5); PushAreaAmbient();
                    await Capture($"cycle/viewer-look-{actual.X}x{actual.Y}");
                    AreaRelight.Extra.Clear(); AreaRelight.SetMode(AreaRelight.Mode.Off); PushAreaAmbient();
                    probeLayer.Visible = true; _canvas.Visible = false;
                }
                SetLights([]);
                RemoveChild(probeLayer); probeLayer.QueueFree(); probeLayer = null; _canvas.Visible = true;
                await Frames(4);
                report.Add(new { Window = $"{actual.X}x{actual.Y}", Megapixels = Math.Round(pixels / 1e6, 2), Shader = shader, Native = native });
            }
            RenderingServer.ViewportSetMeasureRenderTime(rid, false);
            AreaRelight.Extra.Clear(); AreaRelight.SetMode(AreaRelight.Mode.Off); PushAreaAmbient();
            DisplayServer.WindowSetVsyncMode(wasVsync);
            GD.Print("ARC-RES-2000 relight-cycle timing " + System.Text.Json.JsonSerializer.Serialize(report));
            Require(report.Count > 0, "at least one window size was measured");
            GD.Print("ARC-RES-1978 native-smoke relight PASS ARC-RES-2000 cycle " + System.Text.Json.JsonSerializer.Serialize(new { Map = _readyMapName, Hour = driver.Clock.Hour, Gpu = RenderingServer.GetVideoAdapterName(),
                Driver = RenderingServer.GetCurrentRenderingDriverName(), Sizes = report.Count, Frames = walkFrames, DistinctSpriteFrames = distinctTotal }));
        }
        finally
        {
            if (probeLayer is not null) { if (probeLayer.GetParent() is not null) RemoveChild(probeLayer); probeLayer.QueueFree(); }
            _canvas.Visible = true;
            _worldActor.AttackPose = null;
            AreaRelight.OnlyExtra = false; AreaRelight.Extra.Clear(); AreaRelight.SetMode(wasMode); AreaRelight.Strength = wasStrength;
            RenderingServer.ViewportSetMeasureRenderTime(viewport.GetViewportRid(), false);
            DisplayServer.WindowSetVsyncMode(wasVsync);
            DisplayServer.WindowSetSize(wasWindow);
            for (var i = 0; i < 8; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); // the window settles before the camera is framed again
            _canvas.SetVisualProfile(wasProfile, wasSource, wasBudget); SyncRelitLook();
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
