using Arcanum.Data.Lighting;
using Arcanum.Data.Terrain;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

public partial class TerrainViewerApp
{
    // ARC-DEV-353 (V7d second part, ledger LT10, LT12) native observation (stage "critter-light", only when named, on a lit map where an NPC carries
    // a saved light): the light of an NPC is not one of the sector's fixed lights; it is carried with the NPC, so
    //  - the canvas holds it at the NPC's T0 place, and a terrain pixel near it reads back as the merged lattices predict;
    //  - once the AI's mover stands the NPC on another cell, the light stands there and no longer at the T0 place.
    // The mover and the clock are put back (the clock moves to a night hour when the ambient is too bright).
    private async Task RunAreaCritterLightSmokeAsync()
    {
        if (_areaSession is null || _canvas is null || _areaAmbient is not { } driver || _readyArea is not { Lighting: { Local: { } all } lighting } area)
        { GD.Print("ARC-DEV-353 native-smoke critter-light NONE map=" + _readyMapName + " (needs a lit map)"); return; }
        // A map with no NPC that carries a light has nothing to observe; the AI host is built only for one that has.
        if (all.CritterLights.Count == 0)
        { GD.Print($"ARC-DEV-353 native-smoke critter-light NONE map={_readyMapName} (no critter of this map carries a light)"); return; }
        _aiForSmoke = true;
        AreaAiHost? built = null;
        for (var i = 0; i < 120 && (built = AreaAiHostNow()) is null; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (built is not { } host || !ReferenceEquals(host.Session, _areaSession))
        { _aiForSmoke = false; GD.Print("ARC-DEV-353 native-smoke critter-light NONE map=" + _readyMapName + " (no AI host: needs --hero)"); return; }
        static void Require(bool value, string why)
        {
            if (value) return;
            GD.Print("ARC-DEV-353 critter-light smoke FAIL: " + why);
            throw new InvalidOperationException("AREA_CRITTER_LIGHT_SMOKE_FAILED");
        }
        var light = all.CritterLights.FirstOrDefault(l => host.Scene.Npcs.Any(n => n.Row.Key == l.Object));
        if (light is null)
        { _aiForSmoke = false; RetireAiHost(); _aiHostStamp = null; GD.Print($"ARC-DEV-353 native-smoke critter-light NONE map={_readyMapName} (no NPC of the AI host carries a light; {all.CritterLights.Count} critter lights)"); return; }
        var npc = host.Scene.Npcs.First(n => n.Row.Key == light.Object);
        Require(all.Field.Source.Lights.All(l => l.Owner != light.Object), "the NPC's light is not a fixed light of the sector");
        var wasEnabled = driver.Enabled;
        var before = host.Movers.Of(npc.Entity);
        try
        {
            driver.Enabled = true;
            for (var hours = 0; hours < 24 && Math.Max(driver.Effective.Outdoor.R, Math.Max(driver.Effective.Outdoor.G, driver.Effective.Outdoor.B)) > 140; hours++)
            { driver.AdvanceHours(1, AmbientSector()); LogAreaEvents(driver); }
            PushAreaAmbient();
            var pair = driver.Effective;
            async Task<AreaLocalLighting?> Look(CellCoord cell)
            {
                // The camera is held on the cell so the NPC's light is within reach of the view.
                var tile = TerrainGeometry.Project(checked((int)(cell.X - all.Chart.OriginX)), checked((int)(cell.Y - all.Chart.OriginY)));
                _camera!.ResetWorld(new GamepadLab.Core.Vec2(tile.X + 40d, tile.Y + 20d), WorldViewport, 1);
                _canvas.SetAreaMovingBodies(AreaMovingCommands());
                for (var i = 0; i < 3; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                TickCarriedLight();
                for (var i = 0; i < 2; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                return _canvas.AreaCarriedForSelfTest;
            }
            var home = new CellCoord(checked((int)light.X), checked((int)light.Y));
            var lattice = (await WithAreaSmokeCameraHeld(() => Look(home)))?.CarriedLattice(pair);
            Require(lattice is not null, "the canvas holds the NPC's carried light at its T0 place");
            // A terrain pixel near it reads back against the map's lattice and the carried one.
            var basis = LightsNow(all, driver) ?? throw new InvalidOperationException("AREA_CRITTER_LIGHT_NO_LIGHTING");
            var mapLattice = basis.View(area.Plan, lighting.Channels, pair).Lattice;
            var anchor = TerrainGeometry.Project(checked((int)(light.X - all.Chart.OriginX)), checked((int)(light.Y - all.Chart.OriginY)));
            var centre = (X: anchor.X + light.OffsetX + 40d, Y: anchor.Y + light.OffsetY + 20d);
            var observed = false; var tried = 0;
            foreach (var command in area.Plan.Commands.Where(c => c.Identity is null && lighting.Channels[c.Visual] != AreaAmbientChannel.Unlit)
                .OrderBy(c => Math.Abs(c.Destination.X + c.Destination.Width / 2d - centre.X) + Math.Abs(c.Destination.Y + c.Destination.Height / 2d - centre.Y)).Take(24))
            {
                tried++;
                var ambient = AreaLocalLighting.Ambient(lighting.Channels[command.Visual], pair);
                (double R, double G, double B) Merged(Vector2 world) => StationaryLightField.TerrainPixel(mapLattice, lattice, ambient, world.X, world.Y);
                if (!await WithAreaSmokeCameraHeld(() => ObserveTerrainLitAsync(area, command, ambient, Merged))) continue;
                observed = true; break;
            }
            Require(observed, $"a terrain pixel near the NPC shows its light ({tried} candidates tried)");

            // The light's anchor lies inside the carried lattice (the hero's own light and other critters' only widen it).
            bool Holds(StationaryLightLattice carriedLattice, CellCoord cell)
            {
                var anchorTile = TerrainGeometry.Project(checked((int)(cell.X - all.Chart.OriginX)), checked((int)(cell.Y - all.Chart.OriginY)));
                double ax = anchorTile.X + light.OffsetX + 40d, ay = anchorTile.Y + light.OffsetY + 20d;
                return ax >= 40d * carriedLattice.FirstColumn && ax <= 40d * (carriedLattice.FirstColumn + carriedLattice.Columns)
                    && ay >= 20d * carriedLattice.FirstRow && ay <= 20d * (carriedLattice.FirstRow + carriedLattice.Rows);
            }
            Require(Holds(lattice!, home), "the carried lattice holds the NPC's light at its T0 place");

            // The mover stands the NPC on another cell: the light goes with it.
            var elsewhere = new CellCoord(npc.Origin.X + 12, npc.Origin.Y);
            host.Movers.Restore(npc.Entity, npc.Row.Key, npc.Origin, elsewhere, host.Movers.FacingOf(npc.Entity, NeighborDirection.XPlus));
            var moved = (await WithAreaSmokeCameraHeld(() => Look(elsewhere)))?.CarriedLattice(pair);
            Require(moved is not null && Holds(moved, elsewhere), "the carried lattice holds the NPC's light where the mover stands it");
            host.Movers.Restore(npc.Entity, npc.Row.Key, npc.Origin, npc.Origin, host.Movers.FacingOf(npc.Entity, NeighborDirection.XPlus));
            var here = (await WithAreaSmokeCameraHeld(() => Look(home)))?.CarriedLattice(pair);
            Require(here is not null && Holds(here, home), "and back at its place when the mover stands it there");
            // ARC-MAGIC-1561 (ledger RM3c, IE5): a magick point write makes the session recalculate the NPC's light (the rewield 0x465530 runs 0x467E80). The NPC that carries a saved light stays lit at its place whether the recalculation
            // keeps the saved light (no light item worn now or at load), gives the size of its worn items, or clears it (a size of None: the light goes with the item); the stage boundary loads the baseline again (ARC-DEV-408).
            var map = new Arcanum.Session.SessionMapId(host.Scene.RuntimeMap);
            var written = _areaSession.WriteCritterStat(_areaSession.Version, map, npc.Entity, 22, 1);
            Require(written.Disposition != Arcanum.Session.GameSessionDisposition.Rejected, "the magick point write of the NPC is accepted: " + written.Failure);
            var recalculated = AreaEvents.NpcLight(map, npc.Entity);
            var after = (await WithAreaSmokeCameraHeld(() => Look(home)))?.CarriedLattice(pair);
            if (recalculated != CarriedLightSize.None) Require(after is not null && Holds(after, home), $"the NPC's light stands at its place after the recalculation (size {recalculated?.ToString() ?? "as saved"})");
            GD.Print("ARC-MAGIC-1561 critter-light recalculated=" + (recalculated?.ToString() ?? "as-saved"));
            GD.Print("ARC-DEV-353 native-smoke critter-light PASS " + System.Text.Json.JsonSerializer.Serialize(new { Map = _readyMapName, CritterLights = all.CritterLights.Count, Tried = tried, Hour = driver.Clock.Hour }));
        }
        finally
        {
            if (before is { } was) host.Movers.Restore(npc.Entity, npc.Row.Key, was.Origin, was.Cell, was.Facing); else host.Movers.Clear();
            driver.Enabled = wasEnabled; PushAreaAmbient();
            _aiForSmoke = false; RetireAiHost(); _aiHostStamp = null;
            _worldFitView = false; _camera!.ResetWorld(WorldAnchor, WorldViewport, _worldPreferredZoom); PublishWorldSelection();
            _canvas.SetAreaMovingBodies(AreaMovingCommands());
        }
    }
}
