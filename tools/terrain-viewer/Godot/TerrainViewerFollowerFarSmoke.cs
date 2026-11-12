using Arcanum.Presentation.Hud;
using Arcanum.Session;
using Arcanum.Simulation.Magic;
using Arcanum.Simulation.Party;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

public partial class TerrainViewerApp
{
    // ARC-DEV-170 (B05, smoke plan SM01) native observation (stage "follower-far", only when named, with --hero and --debug-mode): the report's own path. A speaker of the start
    // map joins the hero (the follow smoke's join); the hero's window moves to a far area of the main map (the debug travel's Teleport carries the session; the companion stood in the
    // window the hero leaves); the companion is in the party, drawn and next to the hero there; F7, a step away and F8 (the report's quick load in the town) bring the same hero and
    // the same companion back, drawn, within the follow distance, after ten seconds of frames. (Not with `debug-travel` in one list: that stage leaves the hero away from the speakers' map; name it alone.)
    // ARC-MAGIC-1147 (MT16, FM6, ledger TP6-TP9) native observation (stage "teleport-follow", only when named, with --hero, no debug mode): the same companion, the window moved by the
    // spell's own path: a Teleportation cast opens the world map in its teleport mode, the pick of a known far area lands the hero (the landing 0x561860 with flag 0x20, so the followers go
    // too and are ordered to wait), the companion is in the party and within the follow distance of the hero there, and the window of that landing has no Travel button.
    private async Task RunAreaFollowerFarSmokeAsync(bool viaSpell = false)
    {
        var tag = viaSpell ? "ARC-MAGIC-1147 teleport-follow" : "ARC-DEV-170 follower-far";
        void Require(bool value, string why)
        {
            if (value) return;
            GD.Print(tag + " smoke FAIL: " + why);
            throw new InvalidOperationException(viaSpell ? "AREA_TELEPORT_FOLLOW_FAILED" : "AREA_FOLLOWER_FAR_FAILED");
        }
        if (!viaSpell && !DebugMode) { GD.Print("ARC-DEV-170 native-smoke follower-far NONE (the stage needs --debug-mode)"); return; }
        if (_areaSession is null) { GD.Print(tag.Replace(" ", " native-smoke ") + " NONE (no area session)"); return; }
        if (_smokeCompanion is null) await RunAreaMapCompanionSmokeAsync();
        if (_smokeCompanion is not { } entity || _areaCompanions?.Companions.Any(c => c.Entity == entity) != true)
        { GD.Print("ARC-DEV-170 native-smoke follower-far NONE (no companion joined on this map)"); return; }
        for (var i = 0; i < 1200 && _worldMapData is null; i++) { await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); AdoptWorldMapData(); }
        Require(_worldMapData is { Status: AreaWorldMapDataStatus.Ready, Data: not null }, "world map data " + _worldMapData?.Status + " " + _worldMapData?.Detail);
        var data = _worldMapData!.Data!;
        var session = _areaSession;
        var simulatedFocus = _worldFocusSimulationForSelfTest; var focused = _worldFocused;
        _worldFocusSimulationForSelfTest = true; _worldFocused = true;
        var saves = System.IO.Directory.CreateTempSubdirectory("arc-dev-170-").FullName;
        _saveLibraryForSelfTest = new(saves);
        try
        {
            var fromMap = _readyArea!.Snapshot.RuntimeMap;
            // The far area, as the debug travel's smoke picks it: the farthest placed area from the actor's sector.
            int target;
            if (!viaSpell)
            {
                InventoryKey(Key.W);
                Require(WorldMapOpen && _worldMap is not null, "W opened the world map: " + _worldMapStatus);
                var map = _worldMap!;
                var here = map.OnStartMap ? Arcanum.Simulation.Travel.WorldTravelGeometry.Location(map.Position, data.Width) : data.TravelAreas[1].Location;
                target = Enumerable.Range(1, data.TravelAreas.Count - 1).Where(a => data.TravelAreas[a].Location != default)
                    .OrderBy(a => Arcanum.Simulation.Travel.WorldTravelGeometry.Distance(data.TravelAreas[a].Location, here)).Last();
                _worldMapPanel!.TeleportForSelfTest(target);
                Require(!WorldMapOpen && _mapHandoff is not null, "Teleport started the load: " + _worldMapPanel.MessageForSelfTest);
            }
            else
            {
                // The spell's own path (ARC-MAGIC-038): the cast opens the teleport mode, the pick lands. The farthest area the ban test allows is made known (a pick finds known areas only).
                var cell = ((AreaGridSession)_worldActor.Session!).Chart.ToWorld(((AreaGridSession)_worldActor.Session!).State.CommittedCell);
                var here = new Arcanum.Simulation.Travel.TravelLocation(checked((uint)cell.X), checked((uint)cell.Y));
                var host = NewSpellTeleportHost();
                var caster = SpellCasterNow() ?? throw new InvalidOperationException("AREA_TELEPORT_FOLLOW_NO_CAST_HOST");
                var hero = caster.RunObject(SessionSpellCaster.Hero) ?? throw new InvalidOperationException("AREA_TELEPORT_FOLLOW_NO_HERO");
                target = Enumerable.Range(1, data.TravelAreas.Count - 1).Where(a => data.TravelAreas[a].Location != default
                        && host.Allowed(SessionSpellCaster.Hero, new(unchecked((uint)data.TravelAreas[a].Location.X), unchecked((uint)data.TravelAreas[a].Location.Y))))
                    .OrderBy(a => Arcanum.Simulation.Travel.WorldTravelGeometry.Distance(data.TravelAreas[a].Location, here)).Last();
                Require(session.DiscoverArea(session.Version, target).Disposition != GameSessionDisposition.Rejected, "the target area was made known");
                // ARC_AREA_TELEPORT_CAPTURE=PREFIX saves the ordinary W window (PREFIX-before.png: what the teleport window looked like before this task) and the teleport window (PREFIX-after.png) as readbacks of the viewport, for the owner's side-by-side look; nothing is committed.
                var capture = System.Environment.GetEnvironmentVariable("ARC_AREA_TELEPORT_CAPTURE");
                // Real frames pass here with the stage's simulated focus off (a world frame while the map is open would fault its observation).
                async Task Shot(string name)
                {
                    var simulated = _worldFocusSimulationForSelfTest; var focus = _worldFocused;
                    _worldFocusSimulationForSelfTest = false; _worldFocused = false;
                    try
                    {
                        for (var i = 0; i < 3; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                        using var image = GetViewport().GetTexture().GetImage();
                        Require(image.SavePng($"{capture}-{name}.png") == Error.Ok, "the capture is saved");
                    }
                    finally { _worldFocusSimulationForSelfTest = simulated; _worldFocused = focus; }
                }
                if (capture is { Length: > 0 })
                {
                    InventoryKey(Key.W);
                    Require(WorldMapOpen && _teleportMode is null, "W opened the ordinary window for the capture: " + _worldMapStatus);
                    await Shot("before");
                    CloseWorldMapByPlayer();
                    Require(!WorldMapOpen, "the ordinary window closed again");
                }
                var spot = new SpellLocation(checked((uint)cell.X), checked((uint)cell.Y));
                var log = "";
                for (var component = 0; component < 4; component++)
                {
                    var start = caster.Log.Count;
                    caster.Apply(new SpellRun(0, 4, SpellRunAction.Begin, hero, hero, spot, hero, spot, null, SpellRunFlags.Active), component, new(hero, spot));
                    log += string.Join(",", caster.Log.Skip(start).Where(e => e.Kind is SpellCastEventKind.TeleportMap or SpellCastEventKind.Message).Select(e => $"{e.Kind}:{e.Value}"));
                    FlushSpellLog(null);
                }
                Require(log == $"{SpellCastEventKind.TeleportMap}:4" && WorldMapOpen && _teleportMode is { Spell: 4 }, $"the cast opened the world map in its teleport mode: [{log}] {_worldMapStatus}");
                Require(!(_worldMapPanel?.TravelShownForSelfTest ?? true), "the teleport mode's window has no Travel button (WM62, TP6)");
                if (capture is { Length: > 0 }) await Shot("after");
                var fatigue = caster.CurrentFatigue(hero);
                Require(TeleportPick(data.TravelAreas[target].Location) && !WorldMapOpen && _mapHandoff is not null, "the pick started the landing: " + _worldMapStatus);
                Require(_teleportsForSelfTest.Any(e => e.StartsWith($"area {target} landing", StringComparison.Ordinal)), "the pick landed on the target area: " + string.Join(";", _teleportsForSelfTest));
                Require(caster.CurrentFatigue(hero) <= fatigue, "the spell's fatigue is taken at the pick, never given back");
            }
            for (var i = 0; i < 36000 && (_mapHandoff is not null || !ReferenceEquals(_areaSession, session)); i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Require(_mapHandoff is null && ReferenceEquals(_areaSession, session), "the far area loaded with the session carried");
            Require(session.QueryActor().Map.RuntimeMap == data.StartRuntime, "the hero is on the main map");
            var there = await FollowerNearHeroAsync(entity, "after the window moved");
            if (viaSpell)
            {
                // The companion was carried by the landing: led by the hero, in the shown map's group, within the follow distance, drawn (FollowerNearHeroAsync).
                GD.Print("ARC-MAGIC-1147 native-smoke teleport-follow PASS " + System.Text.Json.JsonSerializer.Serialize(new { From = fromMap, Target = target, Entity = entity, AfterMove = there }));
            }
            else
            {
                // F7, a step away, F8: the report's quick load.
                var savedCell = ((AreaGridSession)_worldActor.Session!).State.CommittedCell;
                QuickKey(Key.F7);
                Require(_quickSaveStatus?.StartsWith("F7 quick saved", StringComparison.Ordinal) == true, "F7 saved: " + _quickSaveStatus);
                for (var i = 0; i < 3; i++) AreaSmokeFrame();
                var running = _areaSession;
                QuickKey(Key.F8);
                Require(_mapHandoff is not null || _quickSaveStatus?.StartsWith("F8 quick loaded", StringComparison.Ordinal) == true, "F8 loaded: " + _quickSaveStatus);
                if (_mapHandoff is not null) await (_lastLoad ?? Task.CompletedTask);
                for (var i = 0; i < 36000 && _mapHandoff is not null; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                Require(_mapHandoff is null && !ReferenceEquals(running, _areaSession) && _areaSession is not null, "the load published a new session: " + _quickSaveStatus);
                Require(((AreaGridSession)_worldActor.Session!).State.CommittedCell == savedCell && _areaSession!.QueryActor().Map.RuntimeMap == data.StartRuntime, "the hero stands on the saved cell");
                var back = await FollowerNearHeroAsync(entity, "after the quick load");
                GD.Print("ARC-DEV-170 native-smoke follower-far PASS " + System.Text.Json.JsonSerializer.Serialize(new { From = fromMap, Target = target, Entity = entity, AfterMove = there, AfterLoad = back }));
            }
        }
        finally
        {
            _saveLibraryForSelfTest = null; _quickKeys.Reset();
            try { System.IO.Directory.Delete(saves, recursive: true); } catch (System.IO.IOException) { }
            if (WorldMapOpen) EndWorldMap();
            _worldFocusSimulationForSelfTest = simulatedFocus; _worldFocused = focused;
        }

        // The companion is led by the hero, bound to the shown map, drawn and within the follow distance after ten seconds of frames (a hundred frames of a tenth of a second).
        async Task<object> FollowerNearHeroAsync(string who, string when)
        {
            Require(_areaSession!.QueryCompanion(who).LedByPc, $"the companion is in the party {when}");
            Require(_areaCompanions?.Companions.Any(c => c.Entity == who) == true, $"the shown map's group holds the companion before any placement {when}: {_areaCompanions?.Status()}");
            // The new grid starts its clock at 0: the hero is placed on its own cell again (the smoke clock restarts) before the frames.
            var grid = (AreaGridSession)_worldActor.Session!;
            AreaSmokePlace(new AreaMapRuntime(_readyArea!.Snapshot, grid.Snapshot, grid.Chart), grid.State.CommittedCell);
            var frames = 0;
            for (; frames < 100 || (_areaCompanions?.Companions.SingleOrDefault(c => c.Entity == who) is not { } c0 || FollowDistanceRules.Distance(c0.Grid.State.CommittedCell, ((AreaGridSession)_worldActor.Session!).State.CommittedCell) > FollowDistanceRules.Range) && frames < 600; frames++) AreaSmokeFrame();
            _camera!.ResetWorld(WorldAnchor, WorldViewport, _worldPreferredZoom);
            PublishWorldSelection();
            for (var frame = 0; frame < 3; frame++)
            {
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            }
            var companion = _areaCompanions?.Companions.SingleOrDefault(c => c.Entity == who);
            Require(companion is not null, $"the shown map's group holds the companion {when}: {_areaCompanions?.Status()}");
            var hero = ((AreaGridSession)_worldActor.Session!).State.CommittedCell;
            var distance = FollowDistanceRules.Distance(companion!.Grid.State.CommittedCell, hero);
            Require(distance <= FollowDistanceRules.Range, $"it stands within {FollowDistanceRules.Range} of the hero {when}: {distance} cells after {frames} frames");
            var world = ((AreaGridSession)_worldActor.Session!).Chart.ToWorld(companion.Grid.State.CommittedCell);
            var drawn = _canvas!.AreaVisibleCommands.Any(c => c.Identity == companion.Key && c.MapX == world.X && c.MapY == world.Y) || _canvas.AreaRoofCovers(companion.Key);
            Require(drawn, $"its body is drawn on {world} {when}");
            // ARC-DEV-337 (B52): the follower's portrait shows its health and fatigue (CH9) after the window move and after the load: the record of a companion
            // of another window of the main map resolves, and the bars are not left at the empty values a first look without them gave.
            RefreshFollowerPortraits();
            Require(_followerPortraits?.EntitiesForSelfTest.Contains(who) == true, $"the companion's portrait is shown {when}");
            var life = CompanionLife(companion);
            Require(life is { Health: > 0, MaxHealth: > 0, MaxFatigue: > 0 }, $"the host has life values for the companion {when}: {life}");
            var bars = _followerPortraits!.BarsForSelfTest(who);
            Require(bars is { } shown && shown.Health == LifeBarRules.PortraitFill(LifeBarRules.Percent(life!.Value.Health, life.Value.MaxHealth), AreaFollowerPortraits.BarWidth)
                && shown.Fatigue == LifeBarRules.PortraitFill(LifeBarRules.Percent(life.Value.Fatigue, life.Value.MaxFatigue), AreaFollowerPortraits.BarWidth)
                && shown.Health > 0, $"the portrait's bars match the life {when}: {bars} for {life}");
            return new { Distance = distance, Frames = frames, At = world, Bars = bars is { } b ? new { b.Health, b.Fatigue } : null };
        }
    }
}
