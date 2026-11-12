using Arcanum.Data.Area;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using GamepadLab.Core;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-UI-057 native observation (map mode, the "hover" stage, or the map battery): with the interface bars drawn, hovering each kind of
// pickable non-critter object of the map (an item, a container, a portal, a scenery) draws it a second time additively, so its pixel under
// the pointer is not darker and at least one channel is brighter unless already full, and describes it in the message window (an item's
// window, or the object's image and name) without the floating tooltip; leaving it takes the highlight and the window away. A
// click-through body under a point is never the pick. ARC_AREA_HOVER_WINDOW_CAPTURE saves in-app readbacks to the caller's ignored path.
public partial class TerrainViewerApp
{
    private async Task RunAreaHoverWindowSmokeAsync()
    {
        if (_areaSession is null || _readyArea is not { } area || _canvas is not { } canvas)
        { GD.Print($"ARC-UI-057 native-smoke hover-window NONE map={_readyMapName} (needs an area session)"); return; }
        static void Require(bool value, string why)
        {
            if (value) return;
            GD.Print("ARC-UI-057 hover-window smoke FAIL: " + why);
            throw new InvalidOperationException("AREA_HOVER_WINDOW_SMOKE_FAILED");
        }
        var capture = System.Environment.GetEnvironmentVariable("ARC_AREA_HOVER_WINDOW_CAPTURE");
        async Task<Image> Frame()
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            return GetViewport().GetTexture().GetImage();
        }
        var simulatedFocus = _worldFocusSimulationForSelfTest; var focused = _worldFocused; var bars = _gameBarForSelfTest;
        _worldFocusSimulationForSelfTest = true; _gameBarForSelfTest = true;
        try
        {
            _worldFocused = true; SelectSimulatedPadForSelfTest();
            for (var i = 0; i < 600 && _hoverNames is null; i++) { await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); AdoptHoverNames(); }
            for (var i = 0; i < 120 && _gameBarPanel is not { Visible: true }; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Require(_gameBarPanel is { Visible: true } && !_messageLog.PaneMode, "the bars show in the message mode: " + _gameBarNotice);
            var index = HoverIndex()!;
            var seen = new List<string>();
            // The body framed, then the pixels (every other one, outside the HUD and the bars' strips) where it is the pick.
            async Task<List<Vector2>> PickPoints(AreaDrawCommand body)
            {
                PublishWorldSelection();
                _camera!.ResetWorld(new Vec2(body.Destination.X + body.Destination.Width / 2d, body.Destination.Y + body.Destination.Height / 2d), WorldViewport, 1);
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                var hud = _hud!.Rect;
                var top = _gameBarPanel!.StripRectForSelfTest(AreaBarStrip.Top); var bottom = _gameBarPanel.StripRectForSelfTest(AreaBarStrip.Bottom);
                var found = new List<Vector2>();
                if (canvas.AreaBodyRect(body.Identity!.Value) is not { } rect) return found;
                // The body's rectangle on the screen, inside the viewport.
                var screenRect = (AreaDrawItem.RasterTransform(GetViewport()) * rect).Grow(2).Intersection(GetViewport().GetVisibleRect());
                for (var y = (int)screenRect.Position.Y; y < (int)screenRect.End.Y && found.Count < 400; y += 2)
                for (var x = (int)screenRect.Position.X; x < (int)screenRect.End.X && found.Count < 400; x += 2)
                {
                    var p = new Vector2(x + .5f, y + .5f);
                    if (!hud.HasPoint(p) && !top.HasPoint(p) && !bottom.HasPoint(p) && AreaObjectAtScreen(p) == body.Identity) found.Add(p);
                }
                return found;
            }
            foreach (var kind in new[] { AreaHoverWindowKind.Item, AreaHoverWindowKind.Container, AreaHoverWindowKind.Portal, AreaHoverWindowKind.Scenery })
            {
                List<Vector2> points = []; AreaDrawCommand? body = null; var last = false;
                // A body a roof covers (or that is hidden) is picked but not drawn, so it has no highlight to observe; and a body with only a few
                // picked pixels (a thin or small one) may leave them all already full, so the candidate with the most pixels of the first six
                // is taken (the first with at least MinPoints stops the search).
                const int MinPoints = 16;
                foreach (var candidate in index.Bodies.Where(c => c.Identity is { } id && index.Row(id) is var row && AreaHoverWindow.Kind(row.Type) == kind
                    && AreaHoverWindow.Highlighted(row) && !string.IsNullOrEmpty(index.Name(id)) && !AreaTakenByPc(c) && !canvas.AreaHiddenOf(c))
                    // An animated scenery changes frame between the two readbacks, so the still bodies come first.
                    .OrderBy(c => _sceneryBodies.ContainsKey(c.Identity!.Value)).Take(6))
                {
                    HoverAtForSelfTest(null);
                    var found = await PickPoints(candidate);
                    last = found.Count > points.Count;
                    if (last) { body = candidate; points = found; }
                    if (points.Count >= MinPoints) break;
                }
                if (body is null) { seen.Add($"{kind}=none"); continue; }
                // The camera is on the last candidate framed: frame the chosen one again when it is not that one.
                if (!last) points = await PickPoints(body);
                Require(points.Count > 0, $"{kind}: the chosen body is picked again after framing it");
                var at = points[points.Count / 2];
                var key = body.Identity!.Value; var row = index.Row(key);
                // Under the pointer without the highlight first (the pointer off the window), then hovered.
                HoverAtForSelfTest(null);
                Require(canvas.AreaHighlight is null && canvas.AreaHighlightDrawnForSelfTest is null, $"{kind}: no highlight without a hover");
                using var before = await Frame();
                HoverAtForSelfTest(at);
                Require(HoverKeyForSelfTest == key, $"{kind}: the body is the pick");
                using var after = await Frame();
                // The body's frame: the index's, or for an animated scenery (ARC-RENDER-035) the frame its animation shows now.
                Require(canvas.AreaHighlightDrawnForSelfTest is { Additive: true, Unlit: true, Alpha: 255, Multiply: false, WadeRows: 0 } drawn
                    && drawn.Identity == key && drawn.Frame == (_sceneryBodies.TryGetValue(key, out var animated) ? animated.Frame : body.Frame),
                    $"{kind}: the highlight is the body's frame drawn additive: {canvas.AreaHighlightDrawnForSelfTest} (body frame {body.Frame.Frame}, animated frame {(_sceneryBodies.TryGetValue(key, out var animatedShown) ? animatedShown.Frame.Frame : null)})");
                // Additive: no pixel of the body gets darker and some get brighter (an opaque black source pixel adds nothing).
                const float Step = 1.5f / 255;
                var pairs = points.Select(p => (Before: before.GetPixel((int)p.X, (int)p.Y), After: after.GetPixel((int)p.X, (int)p.Y))).ToArray();
                var darker = pairs.Count(c => c.After.R < c.Before.R - Step || c.After.G < c.Before.G - Step || c.After.B < c.Before.B - Step);
                var brighter = pairs.Count(c => c.After.R > c.Before.R + Step || c.After.G > c.Before.G + Step || c.After.B > c.Before.B + Step);
                Require(darker == 0 && brighter > 0, $"{kind}: of {pairs.Length} picked pixels {brighter} brighter and {darker} darker");
                var window = HoverWindowForSelfTest;
                var log = _gameBarPanel!.MessageLog;
                if (kind == AreaHoverWindowKind.Item)
                {
                    // ARC-UI-108 (OH12–OH18): with the local PC known the window also draws the lines after the name; the weight line is always there.
                    Require(window is { Kind: AreaMessageKind.Item } && log.ShownKindForSelfTest == AreaMessageKind.Item && log.DrawnTextsForSelfTest == ItemWindowTextsForSelfTest
                        && ItemWindowLinesForSelfTest is { Weight.Length: > 0 },
                        $"{kind}: the item window: {window} drawn {log.ShownKindForSelfTest}/{log.DrawnTextsForSelfTest} expected {ItemWindowTextsForSelfTest}");
                    GD.Print($"ARC-UI-108 native-smoke hover-window item-lines name='{window?.Title}' {ItemWindowLinesForSelfTest}");
                }
                else
                {
                    // ARC-UI-107 (OH6, OH10): the fourth line, expected from the session's own facts of the hovered object: a portal's and
                    // container's lock line, and the hit points "current/maximum" right-aligned in red (not for an invulnerable container or scenery).
                    string expectedHitPoints = "";
                    var lockLine = false;
                    if (_areaSession is { } hoverSession && ObjectTargets()?.Target(hoverSession, key, AreaTrapped)?.Object is { } hoverObject)
                    {
                        var hp = hoverSession.QueryObject(hoverObject).HitPoints;
                        var max = hoverObject.Baseline.Points + hoverObject.Baseline.Adjustment;
                        if (AreaHoverWindow.ShowsHitPoints(kind, hoverObject.Baseline.Invulnerable)) expectedHitPoints = string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{hp}/{max}");
                        lockLine = AreaHoverWindow.ShowsLock(kind) && hoverObject.Baseline.Openable is not null;
                    }
                    var lines = 1 + (expectedHitPoints.Length > 0 ? 1 : 0) + (lockLine ? 1 : 0);
                    Require(window is { Kind: AreaMessageKind.Object } && window.Title == index.Name(key) && window.Icon == AreaHoverWindow.Image(row)
                        && log.ShownKindForSelfTest == AreaMessageKind.Object && log.IconForSelfTest == window.Icon && log.DrawnTextsForSelfTest == lines
                        && window.NameBlock == (kind == AreaHoverWindowKind.Scenery) && window.Value == expectedHitPoints && (window.Line.Length > 0) == lockLine,
                        $"{kind}: the object window: {window} icon {log.IconForSelfTest} texts {log.DrawnTextsForSelfTest} (expected {lines}, hit points '{expectedHitPoints}', lock line {lockLine})");
                    GD.Print($"ARC-UI-107 native-smoke hover-window fourth-line kind={kind} name='{window?.Title}' lock='{window?.Line}' hit-points='{window?.Value}'");
                }
                Require(HoverTextForSelfTest is null, $"{kind}: no floating tooltip with the bars");
                if (!string.IsNullOrWhiteSpace(capture)) Require(after.SavePng($"{capture}-{kind}.png") == Error.Ok, "capture saved");
                HoverAtForSelfTest(null);
                Require(canvas.AreaHighlight is null && HoverWindowForSelfTest is null && log.ShownKindForSelfTest is not (AreaMessageKind.Item or AreaMessageKind.Object),
                    $"{kind}: leaving takes the highlight and the window away");
                seen.Add($"{kind}={row.PrototypeNumber}:{brighter}/{pairs.Length}");
            }
            // ARC-UI-1117 (OH2, OH4): a hovered NPC gets the rings: both arts by the hero's combat mode, the colour of its reaction category, drawn
            // additive (the pixels of their rectangles are not darker and some are brighter), animated at the art's rate; leaving takes them away.
            var ringSeen = "none";
            // The reaction colour needs the AI host, which an automated run leaves off: it is on for this pass (as the AI stages do) and given its frames to be built.
            var aiBefore = _aiForSmoke;
            _aiForSmoke = true;
            try
            {
                for (var i = 0; i < 300 && _aiHost is null; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                List<Vector2> points = []; AreaDrawCommand? body = null;
                foreach (var candidate in index.Bodies.Where(c => c.Identity is { } id && index.Row(id).Type == AreaHoverRingRules.NpcType && !string.IsNullOrEmpty(index.Name(id))
                    && !AreaTakenByPc(c) && !canvas.AreaHiddenOf(c))
                    // An NPC with a known reaction first, a living one before a corpse: its reaction colour is the one worth seeing.
                    .OrderBy(c => HoverEntity(c.Identity!.Value) is { } who && _aiHost?.StandardReaction(who) is not null ? 0 : 1)
                    .ThenBy(c => HoverLife(c.Identity!.Value) is { Life.Health: > 0 } ? 0 : 1).Take(6))
                {
                    HoverAtForSelfTest(null);
                    var found = await PickPoints(candidate);
                    if (found.Count > points.Count) { body = candidate; points = found; }
                    if (points.Count >= 16) break;
                }
                if (body is not null)
                {
                    // The camera is on the last candidate framed: frame the chosen one again.
                    points = await PickPoints(body);
                    Require(points.Count > 0, "ring: the chosen NPC is picked again after framing it");
                    var at = points[points.Count / 2]; var key = body.Identity!.Value;
                    HoverAtForSelfTest(null);
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                    Require(canvas.AreaRing is null && canvas.AreaRingDrawnForSelfTest is (null, null), "ring: none without a hover");
                    using var before = await Frame();
                    HoverAtForSelfTest(at);
                    Require(HoverKeyForSelfTest == key, "ring: the NPC is the pick");
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                    using var after = await Frame();
                    var content = HoverRingContent();
                    var arts = AreaHoverRingRules.Arts(HeroInCombatMode);
                    Require(HoverRingArtsForSelfTest == arts, $"ring: the arts follow the combat mode: {HoverRingArtsForSelfTest} expected {arts}");
                    if (content?.Art(arts.Under) is null || content.Art(arts.Over) is null)
                    {
                        // The folder does not read a ring art: no ring from it, said in the log, never a failure.
                        ringSeen = $"unread-arts({content?.Detail})";
                    }
                    else
                    {
                        var entity = HoverEntity(key);
                        var colour = AreaHoverRingRules.Colour(AreaHoverRingRules.NpcType, entity is null ? null : _aiHost?.StandardReaction(entity), content.Colours);
                        Require(canvas.AreaRing is { } ring && ring.Target == key && ring.Colour == colour, $"ring: the colour is the reaction category's: {canvas.AreaRing?.Colour} expected {colour}");
                        var drawn = canvas.AreaRingDrawnForSelfTest;
                        Require(drawn.Under is not null && drawn.Over is not null, "ring: both rings are drawn for the hovered NPC (its body has a hotspot)");
                        var under = drawn.Under!; var over = drawn.Over!;
                        Require(under.Additive && under.Unlit && over.Additive && over.Unlit && under.Identity is null && over.Identity is null, "ring: additive, unlit, no object identity");
                        Require(under.Visual.SourceIndex / 64 == arts.Under && over.Visual.SourceIndex / 64 == arts.Over, "ring: the frames are of the arts chosen");
                        // Additive: no pixel of a ring's rectangle gets darker and some get brighter (not for a black tint, which adds nothing).
                        var hud = _hud!.Rect; var top = _gameBarPanel!.StripRectForSelfTest(AreaBarStrip.Top); var bottom = _gameBarPanel.StripRectForSelfTest(AreaBarStrip.Bottom);
                        var screenRect = (AreaDrawItem.RasterTransform(GetViewport()) * new Rect2(under.Destination.X, under.Destination.Y, under.Destination.Width, under.Destination.Height))
                            .Intersection(GetViewport().GetVisibleRect());
                        const float Step = 1.5f / 255;
                        int darker = 0, brighter = 0, total = 0;
                        for (var y = (int)screenRect.Position.Y; y < (int)screenRect.End.Y; y += 2)
                        for (var x = (int)screenRect.Position.X; x < (int)screenRect.End.X; x += 2)
                        {
                            var p = new Vector2(x + .5f, y + .5f);
                            if (hud.HasPoint(p) || top.HasPoint(p) || bottom.HasPoint(p)) continue;
                            var b = before.GetPixel(x, y); var a = after.GetPixel(x, y); total++;
                            if (a.R < b.R - Step || a.G < b.G - Step || a.B < b.B - Step) darker++;
                            if (a.R > b.R + Step || a.G > b.G + Step || a.B > b.B + Step) brighter++;
                        }
                        Require(darker == 0 && (brighter > 0 || colour == (0, 0, 0)), $"ring: of {total} pixels of the ring's rectangle {brighter} brighter and {darker} darker");
                        // The rings step through their frames at the art's rate (an art of one frame stays).
                        var frames = content.Art(arts.Under)!.Frames.Count; var first = canvas.AreaRing!.Under!.Key;
                        var stepped = frames <= 1;
                        for (var i = 0; i < 400 && !stepped; i++) { await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); stepped = canvas.AreaRing?.Under?.Key != first; }
                        Require(stepped, $"ring: the under ring stepped past frame {first.Frame} of {frames}");
                        if (!string.IsNullOrWhiteSpace(capture)) Require(before.SavePng($"{capture}-critter-before.png") == Error.Ok && after.SavePng($"{capture}-critter-ring.png") == Error.Ok, "ring capture saved");
                        var reaction = entity is null ? null : _aiHost?.StandardReaction(entity);
                        ringSeen = $"{index.Row(key).PrototypeNumber}:arts={arts.Under}/{arts.Over}:reaction={reaction}:colour={colour.Item1},{colour.Item2},{colour.Item3}:brighter={brighter}/{total}:frames={frames}";
                        GD.Print($"ARC-UI-1117 native-smoke hover-ring name='{index.Name(key)}' {ringSeen}");
                    }
                    HoverAtForSelfTest(null);
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                    Require(canvas.AreaRing is null && canvas.AreaRingDrawnForSelfTest is (null, null), "ring: leaving the NPC takes both rings away");
                }
            }
            finally
            {
                if (!aiBefore) { _aiForSmoke = false; RetireAiHost(); _aiHostStamp = null; }
            }
            // A window with no hoverable body of any kind (the generated wilderness has none) has nothing to observe: NONE, not a failure.
            if (seen.All(s => s.EndsWith("=none", StringComparison.Ordinal)) && !index.Bodies.Any(c => c.Identity is { } id && AreaHoverWindow.Highlighted(index.Row(id))
                && !string.IsNullOrEmpty(index.Name(id)) && !AreaTakenByPc(c) && !canvas.AreaHiddenOf(c)))
            { GD.Print($"ARC-UI-057 native-smoke hover-window NONE map={_readyMapName} (no hoverable object body in the window)"); return; }
            Require(seen.Any(s => !s.EndsWith("=none", StringComparison.Ordinal)), "at least one kind of object is hovered: " + string.Join(" ", seen));
            // A click-through body (OF_CLICK_THROUGH) is never the pick, so it is neither highlighted nor described (OH1).
            var through = area.Plan.Commands.FirstOrDefault(c => c.Identity is { } id && index.Contains(id) && AreaHoverIndex.ClickThrough(index.Row(id)));
            Require(through is null || index.Bodies.All(c => c.Identity != through.Identity), "click-through bodies are not pickable");
            GD.Print($"ARC-UI-057 native-smoke hover-window PASS map={_readyMapName} {string.Join(" ", seen)} click-through={(through is null ? "none" : "skipped")} ring={ringSeen}");
        }
        finally
        {
            HoverReleaseForSelfTest(); HoverAtForSelfTest(null); HoverReleaseForSelfTest();
            _messageLog.ClearHover();
            _gameBarForSelfTest = bars; ReleaseSimulatedPadForSelfTest();
            _worldFocusSimulationForSelfTest = simulatedFocus; _worldFocused = focused;
            PublishWorldSelection(); _camera?.ResetWorld(WorldAnchor, WorldViewport, _worldPreferredZoom);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
    }
}
