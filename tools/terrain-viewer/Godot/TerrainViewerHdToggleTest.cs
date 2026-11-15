using System.Reflection;
using Arcanum.Data.Area;
using Arcanum.Data.Hd;
using Arcanum.TerrainViewer.Core;
using Arcanum.TerrainViewer.Tests;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

public static partial class TerrainViewerSelfTest
{
    // ARC-UI-006: the toggle protocol on the production AREA compositor with a synthetic tinted 4×
    // pack (HD differs from Legacy in colour at magnification 2, never in coverage). While a switch
    // to HD is prepared every frame draws Legacy only; the commit frame draws every candidate HD at
    // once; a switch back to Legacy commits in the next submission and keeps HD textures retained;
    // a newer request cancels a pending one; the bounded wait commits with frames still pending
    // (they draw Legacy as Pending and switch individually); a pack-less HD commits at once as
    // NoPack; a request on an unpublished canvas commits at once; a map change during a pending
    // switch keeps it pending until the new view is ready. Synthetic pixels only.
    private static async Task AssertHdToggleAsync(Node host)
    {
        var native = DisplayServer.GetName() != "headless";
        var root = System.IO.Directory.CreateTempSubdirectory("arc-ui-006-selftest-").FullName;
        using var cache = new TerrainTextureCache();
        var synthetic = SyntheticActorFixture.Create(human: true);
        cache.UploadActor(synthetic.Result.ActorTextures);
        var viewport = new SubViewport { Size = new(480, 400), TransparentBg = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
        var canvas = new TerrainCanvas { WorldSpritesFromPack = true }; viewport.AddChild(canvas); host.AddChild(viewport);
        var sources = new List<HdFrameSource>();
        var clock = 0L; canvas.VisualClock = () => clock;
        var frames = 0; var preparingFrames = 0;
        static void Require(bool value, string why) { if (!value) throw new InvalidOperationException("ARC-UI-006 toggle self-test: " + why); }
        try
        {
            var (bodyCount, _, areaFrames, plan, placement, actor, bodyFrames, actorFrames) = HdAreaScene(synthetic);
            var candidates = bodyCount + 1;
            HdFrameSource Tinted(string id)
            {
                var open = HdPack.Open(HdNearestPack.Write(System.IO.Path.Combine(root, id), id, 4, bodyFrames.Concat(actorFrames), tintColumn: 1, tint: 0xFF));
                if (open.Status != HdPackOpenStatus.Opened) throw new InvalidOperationException("ARC-UI-006 toggle self-test pack " + open.Status);
                var source = new HdFrameSource(open.Pack); sources.Add(source); return source;
            }
            // Magnification 2: the tinted HD texels are visible; every candidate is in view.
            viewport.CanvasTransform = new Transform2D(0, new Vector2(2, 2), 0, new Vector2(60, 200));
            AreaDrawItem[] Shown() => canvas.GetChildren().OfType<AreaDrawItem>().Where(item => item.Visible).ToArray();
            int HdItems() => Shown().Count(item => item.HdScale != 1);
            async Task<byte[]> Frame()
            {
                frames++;
                await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
                if (!native) return Array.Empty<byte>();
                await host.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                using var image = viewport.GetTexture().GetImage();
                return image.GetData();
            }
            async Task Drain(string label)
            {
                for (var wait = 0; canvas.PendingVisualProfile is not null || (canvas.HdSource?.PendingCount ?? 0) > 0 || canvas.AreaHdStats[HdFallbackReason.Pending] > 0; wait++)
                {
                    Require(wait < 600, "pending forever: " + label);
                    await Frame();
                }
                await Frame();
            }

            canvas.SetStaticVisibilityForReadback(true); canvas.SetActorVisibilityForReadback(true);
            canvas.PublishArea(plan, areaFrames, actor, placement, cache);
            var tinted = Tinted("tinted");
            canvas.SetVisualProfile(VisualProfile.Legacy, tinted, HdDeviceBudget.Ceiling(4));
            await Frame(); var legacy = await Frame();
            Require(Shown().Length >= candidates && HdItems() == 0, "the Legacy view draws Legacy");

            // 1. Legacy → HD: Legacy draws until the commit frame, which draws every candidate HD.
            var generation = canvas.VisualGeneration;
            Require(canvas.RequestVisualProfile(VisualProfile.Hd) == VisualRequestOutcome.Started && canvas.VisualGeneration == generation + 1, "the request starts a switch");
            Require(canvas.VisualProfile == VisualProfile.Legacy && canvas.PendingVisualProfile == VisualProfile.Hd && HdItems() == 0,
                "the request's own submission still draws Legacy (decodes just started)");
            Require(canvas.RequestVisualProfile(VisualProfile.Hd) == VisualRequestOutcome.NoOp && canvas.VisualGeneration == generation + 1, "a repeated request is a no-op");
            var rasters = new List<byte[]>(); var preparing = 0;
            for (var wait = 0; canvas.VisualProfile == VisualProfile.Legacy; wait++)
            {
                Require(wait < 600, "the switch to HD never committed");
                rasters.Add(await Frame());
                if (canvas.VisualProfile == VisualProfile.Legacy)
                {
                    preparing++;
                    Require(HdItems() == 0 && canvas.AreaHdStats.Hd + canvas.AreaHdStats.Fallback == 0, "a preparing frame drew HD");
                }
            }
            preparingFrames = preparing;
            Require(canvas.LastVisualCommit is { Profile: VisualProfile.Hd, TimedOut: false, PendingFrames: 0 } && canvas.VisualCommits == 1,
                "a settled commit: " + canvas.LastVisualCommit);
            Require(HdItems() == candidates && canvas.AreaHdStats.Hd == candidates && canvas.AreaHdStats.Fallback == 0, "the commit frame binds every candidate HD: " + canvas.AreaHdStats.Format());
            // A read-back may lag the submission by a frame: two more frames show the committed view.
            rasters.Add(await Frame()); rasters.Add(await Frame());
            if (native)
            {
                // Every frame is wholly Legacy or wholly the committed HD view: no frame mixes them.
                var hdRaster = rasters[^1];
                Require(rasters.All(raster => raster.AsSpan().SequenceEqual(legacy) || raster.AsSpan().SequenceEqual(hdRaster)), "a frame mixed Legacy and HD");
                // Only colour changes, and it does at magnification 2: the HD textures are on screen.
                var changed = 0;
                for (var at = 0; at < legacy.Length; at += 4)
                {
                    Require(hdRaster[at + 3] == legacy[at + 3], "HD changed coverage");
                    if (hdRaster[at] != legacy[at]) changed++;
                }
                Require(changed > 0, "the committed view shows the HD textures");
            }

            // 2. HD → Legacy: the request's own submission commits; HD textures stay retained.
            Require(canvas.RequestVisualProfile(VisualProfile.Legacy) == VisualRequestOutcome.Started, "the request back to Legacy starts");
            Require(canvas.VisualProfile == VisualProfile.Legacy && canvas.PendingVisualProfile is null && HdItems() == 0, "Legacy commits in the next submission");
            Require(canvas.HdResidentCount == candidates, "HD textures are kept within the retain limit: " + canvas.HdResidentCount);
            if (native) { await Frame(); Require((await Frame()).AsSpan().SequenceEqual(legacy), "the Legacy frame after HD equals Legacy"); }
            // Back to HD with every frame resident: settled at once, no wait.
            Require(canvas.RequestVisualProfile(VisualProfile.Hd) == VisualRequestOutcome.Started && canvas.VisualProfile == VisualProfile.Hd && HdItems() == candidates,
                "resident frames commit in the request's own submission");

            // 3. Cancel: a request for the active profile while HD is pending drops the switch and its decodes.
            var fresh = Tinted("fresh");
            canvas.SetVisualProfile(VisualProfile.Legacy, fresh, HdDeviceBudget.Ceiling(4));
            Require(canvas.HdResidentCount == 0, "a new source releases the previous pack's textures");
            Require(canvas.RequestVisualProfile(VisualProfile.Hd) == VisualRequestOutcome.Started && canvas.PendingVisualProfile == VisualProfile.Hd, "pending again");
            var commits = canvas.VisualCommits;
            Require(canvas.RequestVisualProfile(VisualProfile.Legacy) == VisualRequestOutcome.Cancelled && canvas.PendingVisualProfile is null, "cancelled");
            Require(fresh.PendingCount == 0, "the cancel drops pending decodes");
            for (var i = 0; i < 6; i++) { await Frame(); Require(canvas.VisualProfile == VisualProfile.Legacy && HdItems() == 0, "a cancelled switch drew HD"); }
            Require(canvas.VisualCommits == commits, "a cancelled switch committed");

            // 4. The bounded wait: the clock passes 500 ms within the request's own submission, so the
            //    switch commits with every candidate still pending; they draw Legacy and turn HD one by one.
            var slow = Tinted("slow");
            canvas.SetVisualProfile(VisualProfile.Legacy, slow, HdDeviceBudget.Ceiling(4));
            canvas.VisualClock = () => clock += VisualProfileSwitch.DefaultCommitWaitMilliseconds;
            canvas.RequestVisualProfile(VisualProfile.Hd);
            canvas.VisualClock = () => clock;
            Require(canvas.VisualProfile == VisualProfile.Hd && canvas.LastVisualCommit is { TimedOut: true } last && last.PendingFrames == candidates,
                "the bounded wait committed with frames pending: " + canvas.LastVisualCommit);
            Require(HdItems() == 0 && canvas.AreaHdStats[HdFallbackReason.Pending] == candidates, "pending frames draw Legacy: " + canvas.AreaHdStats.Format());
            await Drain("timed-out");
            Require(HdItems() == candidates && canvas.AreaHdStats.Hd == candidates, "pending frames switched individually: " + canvas.AreaHdStats.Format());

            // 5. A map change while HD is pending: the switch stays pending and commits on the new view.
            var mapChange = Tinted("map-change");
            canvas.SetVisualProfile(VisualProfile.Legacy, mapChange, HdDeviceBudget.Ceiling(4));
            canvas.RequestVisualProfile(VisualProfile.Hd);
            Require(canvas.PendingVisualProfile == VisualProfile.Hd, "pending before the map change");
            // Equal content in new objects: the canvas treats it as another map and releases the old map's HD work.
            var (_, _, otherFrames, otherPlan, otherPlacement, otherActor, _, _) = HdAreaScene(synthetic);
            canvas.PublishArea(otherPlan, otherFrames, otherActor, otherPlacement, cache);
            Require(canvas.PendingVisualProfile == VisualProfile.Hd && canvas.VisualProfile == VisualProfile.Legacy, "the map change keeps the request");
            await Drain("map-change");
            Require(canvas.VisualProfile == VisualProfile.Hd && HdItems() == candidates, "the switch committed on the new view");

            // 6. HD without a pack commits at once: every frame NoPack, drawn Legacy.
            canvas.SetVisualProfile(VisualProfile.Legacy, null, null);
            Require(canvas.RequestVisualProfile(VisualProfile.Hd) == VisualRequestOutcome.Started && canvas.VisualProfile == VisualProfile.Hd, "no pack: committed at once");
            Require(canvas.AreaHdStats[HdFallbackReason.NoPack] == candidates && HdItems() == 0, "no pack: NoPack " + canvas.AreaHdStats.Format());
            if (native) { await Frame(); Require((await Frame()).AsSpan().SequenceEqual(legacy), "NoPack draws the Legacy raster"); }
            Require(canvas.HdStatusText().StartsWith("visual HD no pack:", StringComparison.Ordinal), "HUD " + canvas.HdStatusText());

            // 7. Nothing published: a request commits at once (no submission would ever come).
            canvas.ClearScene();
            canvas.SetVisualProfile(VisualProfile.Legacy, null, null);
            Require(canvas.RequestVisualProfile(VisualProfile.Hd) == VisualRequestOutcome.Started && canvas.VisualProfile == VisualProfile.Hd && canvas.PendingVisualProfile is null,
                "an unpublished canvas commits at once");
            canvas.SetVisualProfile(VisualProfile.Legacy, null, null);
        }
        finally
        {
            canvas.ClearScene(); viewport.QueueFree();
            foreach (var source in sources) source.Dispose();
            System.IO.Directory.Delete(root, true);
        }
        GD.Print($"ARC-UI-006 self-test toggle PASS Legacy-only preparing frames then one all-HD commit frame, Legacy commit keeps HD retained, cancel drops decodes, "
            + $"500 ms bounded wait commits pending frames as Legacy, map change keeps the request, NoPack and unpublished commit at once; frames={frames} preparing={preparingFrames}"
            + (native ? "" : "; pixels NOT_VERIFIED headless"));
    }

    // ARC-UI-006 on the WORLD canvas, whose submissions are its _Draw calls: preparing draws are
    // Legacy only and the commit draw binds the statics and the actor HD together; Legacy commits in
    // the next draw and keeps HD retained; the bounded wait commits with every frame pending; a
    // cancel never commits. A pack recoloured per parent texel makes HD visible at any zoom.
    private static async Task AssertWorldHdToggleAsync(Node host)
    {
        var native = DisplayServer.GetName() != "headless";
        var root = System.IO.Directory.CreateTempSubdirectory("arc-ui-006-world-").FullName;
        var fixture = WorldFixture();
        using var cache = new TerrainTextureCache();
        cache.Upload(fixture.Result.Textures); cache.UploadStatic(fixture.Result.StaticTextures); cache.UploadActor(fixture.Result.ActorTextures);
        var viewport = new SubViewport { Size = new(480, 400), TransparentBg = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
        var canvas = new TerrainCanvas { WorldSpritesFromPack = true }; viewport.AddChild(canvas); host.AddChild(viewport);
        var sources = new List<HdFrameSource>();
        var clock = 0L; canvas.VisualClock = () => clock;
        static void Require(bool value, string why) { if (!value) throw new InvalidOperationException("ARC-UI-006 WORLD toggle self-test: " + why); }
        const int candidates = 6; // the statics and the actor of the fixture (ARC-RENDER-014 WORLD self-test)
        try
        {
            var actor = fixture.Result.ActorPlan!.Current.Destination;
            viewport.CanvasTransform = new Transform2D(0, new Vector2(2, 2), 0, new Vector2(240 - 2 * actor.X, 200 - 2 * actor.Y));
            var frames = fixture.Result.StaticTextures.Values.Select(t => new HdNearestPack.Frame(t.HdQuery!, t.Rgba))
                .Concat(fixture.Result.ActorTextures.Values.Select(t => new HdNearestPack.Frame(t.HdQuery!, t.Rgba))).ToArray();
            HdFrameSource Recoloured(string id)
            {
                var open = HdPack.Open(HdNearestPack.Write(System.IO.Path.Combine(root, id), id, 4, frames, parentTint: (x, y) => (byte)(x * 37 + y * 11 + 64)));
                var source = new HdFrameSource(open.Pack ?? throw new InvalidOperationException("ARC-UI-006 WORLD pack " + open.Status)); sources.Add(source); return source;
            }
            async Task<byte[]> Frame()
            {
                await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
                if (!native) return Array.Empty<byte>();
                await host.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                using var image = viewport.GetTexture().GetImage();
                return image.GetData();
            }
            async Task Drain(string label)
            {
                for (var wait = 0; canvas.PendingVisualProfile is not null || canvas.WorldHdStats[HdFallbackReason.Pending] > 0 || (canvas.HdSource?.PendingCount ?? 0) > 0; wait++)
                {
                    Require(wait < 600, "pending forever: " + label);
                    await Frame();
                }
                await Frame(); await Frame();
            }
            canvas.Publish(fixture.Result.DiagnosticPlan!, cache);
            var source = Recoloured("recoloured");
            canvas.SetVisualProfile(VisualProfile.Legacy, source, HdDeviceBudget.Ceiling(4));
            await Frame(); var legacy = await Frame();

            // Legacy → HD: Legacy draws until the commit draw binds all six together.
            Require(canvas.RequestVisualProfile(VisualProfile.Hd) == VisualRequestOutcome.Started, "the request starts");
            var rasters = new List<byte[]>();
            for (var wait = 0; canvas.VisualProfile == VisualProfile.Legacy; wait++)
            {
                Require(wait < 600, "the WORLD switch to HD never committed");
                rasters.Add(await Frame());
                if (canvas.VisualProfile == VisualProfile.Legacy) Require(canvas.WorldHdStats.Hd + canvas.WorldHdStats.Fallback == 0, "a preparing draw drew HD");
            }
            Require(canvas.LastVisualCommit is { Profile: VisualProfile.Hd, TimedOut: false, PendingFrames: 0 } && canvas.WorldHdStats.Hd == candidates,
                "a settled WORLD commit binds all six: " + canvas.WorldHdStats.Format());
            rasters.Add(await Frame()); rasters.Add(await Frame());
            if (native)
            {
                var hd = rasters[^1];
                Require(!hd.AsSpan().SequenceEqual(legacy), "the committed WORLD view shows the HD textures");
                Require(rasters.All(raster => raster.AsSpan().SequenceEqual(legacy) || raster.AsSpan().SequenceEqual(hd)), "a WORLD frame mixed Legacy and HD");
            }

            // HD → Legacy: commits in the next draw, HD textures retained.
            Require(canvas.RequestVisualProfile(VisualProfile.Legacy) == VisualRequestOutcome.Started, "the request back starts");
            await Frame();
            Require(canvas.VisualProfile == VisualProfile.Legacy && canvas.PendingVisualProfile is null && canvas.WorldHdStats.Hd == 0, "Legacy committed in the next draw");
            Require(canvas.HdResidentCount == candidates, "HD textures retained: " + canvas.HdResidentCount);
            if (native) { await Frame(); Require((await Frame()).AsSpan().SequenceEqual(legacy), "Legacy again"); }

            // Cancel: never commits, decodes dropped.
            var cancel = Recoloured("cancel");
            canvas.SetVisualProfile(VisualProfile.Legacy, cancel, HdDeviceBudget.Ceiling(4));
            await Frame();
            // WORLD submits only in its draw, so the request is pending until the next one; cancel it there.
            var commits = canvas.VisualCommits;
            Require(canvas.RequestVisualProfile(VisualProfile.Hd) == VisualRequestOutcome.Started && canvas.PendingVisualProfile == VisualProfile.Hd, "pending until the next draw");
            Require(canvas.RequestVisualProfile(VisualProfile.Legacy) == VisualRequestOutcome.Cancelled && cancel.PendingCount == 0, "cancelled");
            for (var i = 0; i < 6; i++) { await Frame(); Require(canvas.VisualProfile == VisualProfile.Legacy && canvas.WorldHdStats.Hd == 0 && cancel.PendingCount == 0, "a cancelled WORLD switch drew or decoded HD"); }
            Require(canvas.VisualCommits == commits, "a cancelled WORLD switch committed");

            // The bounded wait: the first draw after the request is already past 500 ms.
            var slow = Recoloured("slow");
            canvas.SetVisualProfile(VisualProfile.Legacy, slow, HdDeviceBudget.Ceiling(4));
            await Frame();
            canvas.RequestVisualProfile(VisualProfile.Hd);
            clock += VisualProfileSwitch.DefaultCommitWaitMilliseconds;
            await Frame();
            Require(canvas.VisualProfile == VisualProfile.Hd && canvas.LastVisualCommit is { TimedOut: true, PendingFrames: > 0 }, "the WORLD bounded wait committed with frames pending: " + canvas.LastVisualCommit);
            await Drain("world-timed-out");
            Require(canvas.WorldHdStats.Hd == candidates, "pending WORLD frames switched individually: " + canvas.WorldHdStats.Format());
        }
        finally
        {
            canvas.ClearScene(); viewport.QueueFree();
            foreach (var source in sources) source.Dispose();
            System.IO.Directory.Delete(root, true);
        }
        GD.Print("ARC-UI-006 self-test WORLD toggle PASS Legacy-only preparing draws then one all-HD commit draw, Legacy commit keeps HD retained, cancel, bounded wait with frames pending"
            + (native ? "" : "; pixels NOT_VERIFIED headless"));
    }

    // ARC-UI-006 in the production app over the strict synthetic AREA owner: the H key route, the
    // palette action, the request saved at once, cancel by a second H, and Session invariance of two
    // walks under Legacy, HD and with switches (dialog, light and map change: native smoke).
    private static async Task AssertVisualToggleAppAsync(Node host)
    {
        using var source = new AreaOwnerFixture(motionBodies: true);
        var read = typeof(TerrainLoadCoordinator).Assembly.GetType("Arcanum.World.AreaLoadOperation")!
            .GetMethod("ReadArea", BindingFlags.NonPublic | BindingFlags.Static)!;
        var area = (AreaLoadResult)read.Invoke(null, [7L, source.Owner, CancellationToken.None, null])!;
        var steps = AreaOwnerFixture.Steps(area.Snapshot, source.Streams[new(AreaSourceRole.Module)].Bytes, 7);
        source.Owner.Dispose();
        var result = SyntheticActorFixture.Create(human: true, area: true).Result with { Generation = 7, Area = area, AreaStepSnapshot = steps };
        var arguments = TerrainViewerArguments.Parse(["--mode", "crash-site-area", "--module", "synthetic-module",
            "--resource", "synthetic-terrain", "--actor-resource", "synthetic-actor", "--prototypes", "synthetic-prototypes",
            "--area-mobile", "synthetic-mobile", "--area-effects", "synthetic-effects", "--area-patch", "synthetic-patch", "--source-id", "synthetic"]);
        var viewport = new SubViewport { Size = new(800, 600), TransparentBg = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
        var app = new TerrainViewerApp { SelfTestBootstrap = true };
        viewport.AddChild(app); host.AddChild(viewport);
        await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
        app.SetProcess(false); app.SetProcessInput(false);
        string summary;
        try
        {
            app.PublishSyntheticForSelfTest(arguments, result);
            await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
            summary = await app.VisualToggleForSelfTestAsync(result);
        }
        finally
        {
            app.ClearSyntheticForSelfTest();
            viewport.Free();
        }
        GD.Print("ARC-UI-006 self-test app PASS " + summary);
    }

    // ARC-UI-006 persistence (ARC-UI-1130: a line of the settings file): the store writes exactly its line to its test path, reads it
    // back, loads anything else as Legacy, reports a failed save, and an automated run without a
    // test path neither reads nor writes a file.
    private static void AssertVisualProfileStore()
    {
        static void Require(bool value, string why) { if (!value) throw new InvalidOperationException("ARC-UI-006 store self-test: " + why); }
        var previous = OS.GetEnvironment(VisualProfileSettingsStore.TestVariable);
        var root = System.IO.Directory.CreateTempSubdirectory("arc-ui-006-store-").FullName;
        try
        {
            var path = System.IO.Path.Combine(root, "visual.cfg");
            OS.SetEnvironment(VisualProfileSettingsStore.TestVariable, path);
            var store = new VisualProfileSettingsStore(automated: true);
            Require(store.Persistent && store.Load() == VisualProfile.Legacy, "missing file loads Legacy");
            Require(store.Save(VisualProfile.Hd) && System.IO.File.ReadAllText(path) == "visual profile=hd\n", "Hd saved as its line");
            Require(new VisualProfileSettingsStore(automated: false).Load() == VisualProfile.Hd, "a new store reads Hd back");
            System.IO.File.WriteAllText(path, "visual profile=ultra\n");
            Require(store.Load() == VisualProfile.Legacy, "an unknown value loads Legacy");
            System.IO.File.WriteAllBytes(path, [0xFF, 0xFE, 0x00]);
            Require(store.Load() == VisualProfile.Legacy, "unreadable text loads Legacy");
            // A settings file under a file (its folder cannot be made) cannot be written.
            System.IO.File.WriteAllText(System.IO.Path.Combine(root, "blocker"), "x");
            OS.SetEnvironment(VisualProfileSettingsStore.TestVariable, System.IO.Path.Combine(root, "blocker", "visual.cfg"));
            Require(!new VisualProfileSettingsStore(automated: true).Save(VisualProfile.Hd), "a failed save is reported");
            OS.UnsetEnvironment(VisualProfileSettingsStore.TestVariable);
            var automated = new VisualProfileSettingsStore(automated: true);
            Require(!automated.Persistent && automated.Load() == VisualProfile.Legacy && !automated.Save(VisualProfile.Hd), "an automated run has no file");
        }
        finally
        {
            if (string.IsNullOrEmpty(previous)) OS.UnsetEnvironment(VisualProfileSettingsStore.TestVariable);
            else OS.SetEnvironment(VisualProfileSettingsStore.TestVariable, previous);
            System.IO.Directory.Delete(root, true);
        }
        GD.Print("ARC-UI-006 self-test store PASS settings line at the test path, read back, unknown/unreadable as Legacy, failed save reported, automated run without a file");
    }
}
