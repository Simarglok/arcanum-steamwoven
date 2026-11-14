using Arcanum.Data.Area;
using Arcanum.Session;
using Arcanum.Simulation.Locks;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

public partial class TerrainViewerApp
{
    /// <summary>
    /// ARC-SCRIPT-1778 (ledger AD1, AD2, LK10, LK13, LW2) native observation (part of the stage "object-art", only when named, with --hero, on a map window with the inventory active): a container and a door of the shown map take another art id the
    /// way a script's change art number writes it (the number of another container's or door's art in its own id, frame 0), and the view follows the session: the container is drawn closed and open from the new art (and from its own art when it
    /// has an open frame), the door's art facts and frames are the new art's (the open frame its steps end on), it is drawn from the new art closed and open, and the id of the map data returns both to their T0 bodies. The map's own state is left as it
    /// was. The script path (the plan, the frame reset to 0, the lock fields kept) is the host's unit tests'. ARC_AREA_OPENABLE_ART_CAPTURE=PREFIX saves the frames the view draws as PREFIX-NAME.png (never committed).
    /// </summary>
    private async Task RunAreaOpenableArtStageAsync()
    {
        if (_areaSession is not { } session || session.QueryHero() is null || _readyArea is not { } area || !InventoryActive || ExtraArt() is not { } art || _areaPortals is not { } portals)
        { GD.Print("ARC-SCRIPT-1778 native-smoke object-art-openable SKIP (needs --hero, an area session, the inventory, the portals and the module's content)"); return; }
        static void Require(bool value, string why)
        {
            if (value) return;
            GD.Print("ARC-SCRIPT-1778 object-art-openable smoke FAIL: " + why);
            throw new InvalidOperationException("AREA_OPENABLE_ART_SMOKE_FAILED");
        }
        async Task Settle()
        {
            for (var i = 0; i < 3; i++) { await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); SyncAreaInventory(); }
        }
        await Settle();
        var map = new SessionMapId(area.Snapshot.RuntimeMap);
        var overlay = _areaOverlay ?? throw new InvalidOperationException("AREA_OVERLAY_REQUIRED");
        var capture = System.Environment.GetEnvironmentVariable("ARC_AREA_OPENABLE_ART_CAPTURE");
        void Save(string name, AreaExtraFrame? found)
        {
            if (string.IsNullOrWhiteSpace(capture) || found is null) return;
            using var image = Image.CreateFromData(found.Frame.Width, found.Frame.Height, false, Image.Format.Rgba8, found.Frame.Rgba);
            if (image.SavePng($"{capture}-{name}.png") != Error.Ok) throw new InvalidOperationException("AREA_OPENABLE_ART_CAPTURE_FAILED");
        }
        static uint Framed(uint aid, int frame) => (aid & ~(31U << 14)) | ((uint)frame << 14);
        int? containerLoaded = null, doorLoaded = null;

        // ---- a container ----
        var containers = ContainerFacts(area).Where(c => c.Art >> 28 == 7 && overlay.Bodies.ContainsKey(c.Row.Key) && session.QueryOpenable(c.Openable, c.Baseline).Frame == c.Baseline.Frame).ToArray();
        var containerNumbers = containers.Select(c => (c.Art >> 19) & 511).Distinct().Take(40).ToArray();
        (AreaObjectRecord Row, SessionOpenable Openable, SessionOpenableBaseline Baseline, uint Art)? pickedContainer = null; uint nextContainer = 0; int nextFrames = 0;
        foreach (var candidate in containers.Take(300))
        {
            foreach (var number in containerNumbers)
            {
                var next = ObjectArtNumber.Changed(candidate.Art, checked((int)number), out var outOfRange);
                if (outOfRange || ObjectArtNumber.WithFrameZero(candidate.Art) == next || !art.Exists(next) || art.Load(next).Frame is not { FramesPerGroup: > 1 } loaded
                    || art.Load(Framed(next, 1)).Frame is null) continue;
                pickedContainer = candidate; nextContainer = next; nextFrames = loaded.FramesPerGroup;
                break;
            }
            if (pickedContainer is not null) break;
        }
        if (pickedContainer is { } container)
        {
            var key = container.Row.Key; var entity = container.Openable.Entity; var t0 = overlay.Bodies[key];
            var ownFrames = overlay.Visuals.TryGetValue(t0.Visual, out var ownVisual) ? ownVisual.FrameCount : 0;
            try
            {
                Require(session.QueryObjectArt(map, entity) is null && !_groundBodies.ContainsKey(key), "the container is drawn from its T0 body before the change");
                // The map's own art open: the lid frame is drawn (an ART of one frame keeps frame 0).
                if (ownFrames > 1 && art.Load(Framed(container.Art, 1)).Frame is { } ownOpen)
                {
                    Save("container-old-closed", art.Load(Framed(container.Art, container.Baseline.Frame)).Frame); Save("container-old-open", ownOpen);
                    Require(session.SetOpenableFrame(session.Version, container.Openable, container.Baseline, 1).Disposition == GameSessionDisposition.Changed, "the container opens in the session");
                    await Settle();
                    Require(_groundBodies.TryGetValue(key, out var own) && own.Frame == ownOpen.Key && own.Frame != t0.Frame, "the open container is drawn from frame 1 of its own art");
                    Require(session.SetOpenableFrame(session.Version, container.Openable, container.Baseline, container.Baseline.Frame).Disposition == GameSessionDisposition.Changed, "the container closes in the session");
                    await Settle();
                    Require(!_groundBodies.ContainsKey(key), "the closed container is drawn from its T0 body again");
                }
                // A script's change art number: the new id with frame 0, the container closed (the session's frame is the open state).
                Require(session.SetObjectArt(session.Version, map, entity, ObjectArtNumber.WithFrameZero(nextContainer), container.Art).Disposition == GameSessionDisposition.Changed, "the session keeps the art");
                await Settle();
                var closedFrame = art.Load(nextContainer).Frame!;
                Require(_groundBodies.TryGetValue(key, out var closed) && closed.Frame == closedFrame.Key && closed.Frame != t0.Frame, "the view draws the new art closed where the T0 body stood");
                Require(closed!.MapX == t0.MapX && closed.MapY == t0.MapY, "at the object's cell");
                Require(ContainerFrameCount(container.Row) == nextFrames, $"the container's frame count is the new art's ({ContainerFrameCount(container.Row)} vs {nextFrames})");
                Save("container-new-closed", closedFrame);
                // The open step reads that count: the lid opens to frame 1 and the new art's open body is drawn.
                var opened = _inventoryHost?.Map(CurrentAreaMap) is { } inventoryMap ? AreaContainerLoot.Open(session, inventoryMap, entity, ContainerFrameCount(container.Row), AreaEvents, null) : null;
                GD.Print($"ARC-SCRIPT-1778 object-art-openable container open entity={entity} outcome={opened?.Outcome.ToString() ?? "-"} {opened?.Detail}");
                if (opened?.Outcome == AreaContainerOpenOutcome.Opened) Require(session.QueryOpenable(container.Openable, container.Baseline).Frame == 1, "the open step takes the container to frame 1 of the new art");
                else session.SetOpenableFrame(session.Version, container.Openable, container.Baseline, 1);
                await Settle();
                var openFrame = art.Load(Framed(nextContainer, 1)).Frame!;
                Require(_groundBodies.TryGetValue(key, out var open) && open.Frame == openFrame.Key && open.Frame != closed.Frame, "the view draws the new art's open body");
                Save("container-new-open", openFrame);
                containerLoaded = nextFrames;
            }
            finally
            {
                if (session.QueryObjectArt(map, entity) is not null) session.ClearMapChange(session.Version, map, SessionObjectArt.Key(entity));
                session.SetOpenableFrame(session.Version, container.Openable, container.Baseline, container.Baseline.Frame);
                await Settle();
            }
            Require(!_groundBodies.ContainsKey(key) && session.QueryObjectArt(map, entity) is null, "the container is drawn from its T0 body again");
        }
        else GD.Print($"ARC-SCRIPT-1778 native-smoke object-art-openable NONE (container part: {containers.Length} containers with art, none takes another's number into an ART of two frames)");

        // ---- a door or window ----
        var rows = PortalRows(area);
        var doors = portals.All.Where(p => p.ArtId is null && p.Frames.Count > 1 && !p.UnlockScript && session.QueryOpenable(portals.Openable(p), p.Baseline).Frame == p.Baseline.Frame
            && p.Baseline.Frame == 0 && _areaDoors?.GoalOf(p) is null && rows.ContainsKey(p.Key) && Number(rows[p.Key], CurrentArtSlot) is { } own && own >> 28 == 3
            && overlay.Bodies.ContainsKey(p.Key)).OrderBy(p => p.Entity, StringComparer.Ordinal).ToArray();
        var doorNumbers = doors.Select(p => (Number(rows[p.Key], CurrentArtSlot)!.Value >> 19) & 511).Distinct().Take(40).ToArray();
        AreaPortal? pickedDoor = null; uint nextDoor = 0; PortalArt nextArt = default; List<AreaVisualRecord>? nextFramesList = null;
        foreach (var candidate in doors.Take(200))
        {
            var own = Number(rows[candidate.Key], CurrentArtSlot)!.Value;
            foreach (var number in doorNumbers)
            {
                var next = ObjectArtNumber.Changed(own, checked((int)number), out var outOfRange);
                if (outOfRange || ObjectArtNumber.WithFrameZero(own) == next || !art.Exists(next) || !PortalArtFacts(next, out var facts, out var frames, out _)
                    || frames.Count < 2 || PortalFrameRules.Toggled(facts, 0) == 0) continue;
                pickedDoor = candidate; nextDoor = next; nextArt = facts; nextFramesList = frames;
                break;
            }
            if (pickedDoor is not null) break;
        }
        if (pickedDoor is { } door && nextFramesList is not null)
        {
            var key = door.Key; var entity = door.Entity; var openable = portals.Openable(door); var oldFrames = door.Frames.Count; var own = Number(rows[key], CurrentArtSlot)!.Value;
            try
            {
                Require(_portalBodies.TryGetValue(key, out var _) == false && door.ArtId is null, "the door is drawn from its T0 body before the change");
                Save("door-old-closed", art.Load(Framed(own, 0)).Frame);
                Save("door-old-open", art.Load(Framed(own, door.OpenFrame)).Frame);
                Require(session.SetObjectArt(session.Version, map, entity, nextDoor, own).Disposition == GameSessionDisposition.Changed, "the session keeps the door's art");
                session.SetOpenableFrame(session.Version, openable, door.Baseline, 0);
                await Settle();
                Require(portals.ByEntity(entity) is { ArtId: { } changed } now && changed == nextDoor && now.Frames.Count == nextFramesList.Count && now.Art == nextArt, "the portal has the new art's facts and frames");
                var now2 = portals.ByEntity(entity)!;
                var closedKey = AreaFrameKey.From(now2.Visual(0)!);
                Require(_portalBodies.TryGetValue(key, out var closed) && closed.Frame == closedKey, "the view draws the new art's frame 0");
                Require(now2.OpenFrame != 0 && now2.OpenFrame < now2.Frames.Count && now2.OpenFrame == PortalFrameRules.Toggled(nextArt, 0), $"the open frame is the new art's ({now2.OpenFrame} of {now2.Frames.Count})");
                Save("door-new-closed", ExtraArtFrame(nextDoor, "smoke"));
                Require(session.SetOpenableFrame(session.Version, openable, now2.Baseline, now2.OpenFrame).Disposition == GameSessionDisposition.Changed, "the door opens in the session");
                await Settle();
                var openKey = AreaFrameKey.From(now2.Visual(now2.OpenFrame)!);
                Require(_portalBodies.TryGetValue(key, out var open) && open.Frame == openKey && open.Frame != closed!.Frame, "the view draws the new art's open frame");
                Save("door-new-open", ExtraArtFrame(Framed(nextDoor, now2.OpenFrame), "smoke"));
                doorLoaded = nextFramesList.Count;
            }
            finally
            {
                if (session.QueryObjectArt(map, entity) is not null) session.ClearMapChange(session.Version, map, SessionObjectArt.Key(entity));
                session.SetOpenableFrame(session.Version, openable, door.Baseline, door.Baseline.Frame);
                await Settle();
            }
            Require(portals.ByEntity(entity) is { ArtId: null } back && back.Frames.Count == oldFrames && !_portalBodies.ContainsKey(key), "the door has its own art and frames and is drawn from its T0 body again");
        }
        else GD.Print($"ARC-SCRIPT-1778 native-smoke object-art-openable NONE (door part: {doors.Length} doors with art, none takes another's number into an ART of two frames)");

        if (containerLoaded is null && doorLoaded is null) return;
        GD.Print("ARC-SCRIPT-1778 native-smoke object-art-openable PASS " + System.Text.Json.JsonSerializer.Serialize(new { Map = area.Snapshot.RuntimeMap, ContainerFrames = containerLoaded, DoorFrames = doorLoaded }));
    }
}
