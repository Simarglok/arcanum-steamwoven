using Arcanum.Data.Area;
using Arcanum.Session;
using Arcanum.Simulation.Animation;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-RENDER-1781: the loop part of the "object-art" stage (ledger AAR1-AAR6). On the shown map a still scenery near the hero takes the art of an animated scenery of the map (several frames): it gets the loop, its run on the
// table, and the canvas bodies show three distinct frames of the new art; an animated scenery takes the art of a still one (one frame): its run is killed, its loop gone and its body stays on that frame; the data's id returns both.
// The frame clock is ticked by hand (50 ms per tick, as the door smokes do) so the run steps without a rendered frame. ARC_AREA_OBJECT_ART_CAPTURE=PREFIX also saves the new art's frames as PREFIX-loop-N.png (never committed).
public partial class TerrainViewerApp
{
    private async Task<object> ObserveSceneryArtLoopAsync(GameSessionOwner session, SessionMapId map, AreaExtraArt art)
    {
        static void Require(bool value, string why)
        {
            if (value) return;
            GD.Print("ARC-RENDER-1781 scenery-art-loop smoke FAIL: " + why);
            throw new InvalidOperationException("AREA_SCENERY_ART_LOOP_SMOKE_FAILED");
        }
        if (_readyArea is not { } area || HeroWorldCell() is not { } pc) return "skip";
        var cleanup = new List<(string Entity, uint Baseline)>();
        PortalSmokeClockStart();
        try
        {
            ProcessAreaSceneryAnimation();
            if (_sceneryAnimation is not { } animation || _sceneryHost is not { } host || _areaOverlay is not { } overlay) return "skip";
            long Distance(long x, long y) => Math.Max(Math.Abs(x - pc.X), Math.Abs(y - pc.Y));
            // The donor: an animated scenery of the map with several frames, the nearest to the hero. The recipient: a still scenery near the hero with a one-frame art (its own art is the one-frame art the donor takes).
            var donor = animation.Loops.Where(l => l.Frames.Count >= 3 && !_sceneryArt.ContainsKey(l.Entity) && overlay.Bodies.ContainsKey(l.Key)).OrderBy(l => Distance(l.Loop.X, l.Loop.Y)).FirstOrDefault();
            if (donor is null) { GD.Print("ARC-RENDER-1781 native-smoke scenery-art-loop NONE (the map has no animated scenery of three frames)"); return "none"; }
            var looping = animation.Loops.Select(l => l.Entity).ToHashSet(StringComparer.Ordinal);
            AreaObjectRecord? recipient = null; uint still = 0;
            foreach (var row in area.Snapshot.Objects.Where(r => r.Type == SceneryType && r.WorldX is not null && overlay.Bodies.ContainsKey(r.Key)
                && !looping.Contains(AreaUseScriptHost.EntityId(r)) && Number(r, CurrentArtSlot) is { } aid && aid >> 28 == 4)
                .OrderBy(r => Distance(r.WorldX!.Value, r.WorldY!.Value)).Take(40))
            {
                var own = Number(row, CurrentArtSlot)!.Value;
                if (!SceneryAnimationRules.Near(row.WorldX!.Value, row.WorldY!.Value, pc) || art.Load(SceneryArtFrameId(own, 0)).Frame is not { FramesPerGroup: 1 }) continue;
                recipient = row; still = own; break;
            }
            if (recipient is null) { GD.Print("ARC-RENDER-1781 native-smoke scenery-art-loop NONE (no still scenery with a one-frame art near the hero)"); return "none"; }
            var donorArt = SceneryArtFrameId(donor.Frames[0].RawArt, 0);
            var donorBase = unchecked((uint)Number(donor.Row, CurrentArtSlot)!.Value);
            var donorCount = donor.Frames.Count;
            var recipientEntity = AreaUseScriptHost.EntityId(recipient);
            var recipientKey = recipient.Key;
            var capture = System.Environment.GetEnvironmentVariable("ARC_AREA_OBJECT_ART_CAPTURE");
            async Task Settle() { for (var i = 0; i < 3; i++) { ScanSceneryArtNow(); TickAreaClock(_portalSmokeHostMs += 50); ProcessAreaSceneryAnimation(); await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); } }
            void Write(string entity, uint now, uint baseline)
            {
                var set = session.SetObjectArt(session.Version, map, entity, now, baseline);
                Require(set.Disposition == GameSessionDisposition.Changed, $"the session keeps the art of {entity}: {set.Disposition} {set.Failure}");
                if (!cleanup.Any(c => c.Entity == entity)) cleanup.Add((entity, baseline));
            }

            // 1. The still scenery takes the several-frame art: loop, run, three distinct frames drawn.
            Require(host.LoopOf(recipientEntity) is null && host.GoalOf(recipientEntity) is null, "the recipient has no loop before the change");
            Write(recipientEntity, donorArt, still);
            await Settle();
            var haveChanged = _sceneryArt.TryGetValue(recipientEntity, out var changed);
            Require(haveChanged && changed!.Frames.Count == donorCount, $"the new art's {donorCount} frames are loaded: {changed?.Frames.Count ?? 0}");
            var loop = host.LoopOf(recipientEntity);
            Require(loop is not null && loop.Frames.Count == donorCount && host.GoalOf(recipientEntity) is not null, "the changed scenery has a loop of the new art with a run");
            Require(animation.Loops.Contains(loop!), "the animation list holds the loop");
            var drawnFrames = new List<int>();
            for (var ticks = 0; drawnFrames.Distinct().Count() < 3 && ticks < 600; ticks++)
            {
                TickAreaClock(_portalSmokeHostMs += 50);
                ProcessAreaSceneryAnimation();
                if (!_sceneryBodies.TryGetValue(recipientKey, out var command)) continue;
                var index = changed!.Frames.ToList().FindIndex(f => AreaFrameKey.From(f) == command.Frame);
                Require(index >= 0, $"the drawn body is a frame of the new art: {command.Frame.Frame}");
                if (drawnFrames.Count == 0 || drawnFrames[^1] != index) drawnFrames.Add(index);
            }
            Require(drawnFrames.Distinct().Count() >= 3, "three distinct frames of the new art are drawn: " + string.Join(",", drawnFrames));
            if (!string.IsNullOrWhiteSpace(capture))
                for (var i = 0; i < changed!.Frames.Count; i++)
                    if (art.Load(SceneryArtFrameId(donorArt, i)).Frame is { } saved)
                    {
                        using var image = Image.CreateFromData(saved.Frame.Width, saved.Frame.Height, false, Image.Format.Rgba8, saved.Frame.Rgba);
                        if (image.SavePng($"{capture}-loop-{i}.png") != Error.Ok) throw new InvalidOperationException("AREA_OBJECT_ART_CAPTURE_FAILED");
                    }

            // 2. The animated scenery takes the one-frame art: its run is killed, its loop gone, its body stays on that frame.
            Require(host.GoalOf(donor.Entity) is not null, "the donor runs before the change");
            Write(donor.Entity, still, donorBase);
            await Settle();
            Require(host.LoopOf(donor.Entity) is null && host.GoalOf(donor.Entity) is null && !animation.Loops.Any(l => l.Entity == donor.Entity), "the old art's loop stops: no loop, no run");
            var haveStill = _sceneryArt.TryGetValue(donor.Entity, out var stilled);
            var haveHeld = _sceneryBodies.TryGetValue(donor.Key, out var held);
            Require(haveStill && haveHeld && stilled!.Frames.Count == 1 && held!.Frame == AreaFrameKey.From(stilled.Frames[0]), "the donor's body is the one-frame art");
            for (var ticks = 0; ticks < 100; ticks++) { TickAreaClock(_portalSmokeHostMs += 50); ProcessAreaSceneryAnimation(); }
            var haveStill2 = _sceneryBodies.TryGetValue(donor.Key, out var still2);
            Require(haveStill2 && still2!.Frame == held!.Frame, "a scenery with a one-frame art is still");

            // 3. The data's id returns both: the donor's own loop and run come back, the recipient has none.
            Write(donor.Entity, donorBase, donorBase);
            Write(recipientEntity, still, still);
            await Settle();
            Require(!_sceneryArt.ContainsKey(donor.Entity) && !_sceneryArt.ContainsKey(recipientEntity), "no art field is drawn any more");
            var backLoop = host.LoopOf(donor.Entity);
            Require(backLoop is not null && backLoop.Frames.Count == donorCount && host.GoalOf(donor.Entity) is not null, "the donor has its own loop and run again");
            Require(host.LoopOf(recipientEntity) is null && host.GoalOf(recipientEntity) is null, "the recipient's art has one frame: no loop, no run");
            var report = new { Donor = donor.Entity, Recipient = recipientEntity, Frames = donorCount, Drawn = string.Join(",", drawnFrames), donor.Loop.PauseMs };
            GD.Print("ARC-RENDER-1781 scenery-art-loop " + System.Text.Json.JsonSerializer.Serialize(report));
            return report;
        }
        finally
        {
            foreach (var (entity, baseline) in cleanup)
                if (session.QueryObjectArt(map, entity) is not null) session.ClearMapChange(session.Version, map, SessionObjectArt.Key(entity));
            ProcessAreaSceneryAnimation();
            PortalSmokeClockEnd();
        }
    }
}
