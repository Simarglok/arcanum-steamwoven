using Arcanum.Data.Lighting;
using Arcanum.Data.Terrain;
using Arcanum.Session;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Equipment;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

public partial class TerrainViewerApp
{
    // ARC-DEV-335 (V7d, B50) native observation (stage "worn-light", only when named, with --hero, on a lit map): a light worn by the hero
    // lights the area around it (ledger EQ12, LT12). The hero's wielded item is worn again with a large light, as the ranged stage lends a
    // bow's values, and the light is followed through the production path (the session's worn light, the carried-light tick, the canvas):
    //  - without a worn light the canvas draws none; after the item is worn the canvas holds one carried light of the large size, the hero's
    //    colour and the terrain's lattice differ from the map's alone, and a terrain pixel of the lit area in the viewport reads back as the
    //    merged lattices predict (the map's and the carried one, which the shader adds before it composes the node);
    //  - the light goes with the hero: after the hero stands elsewhere the carried lattice stands elsewhere too;
    //  - taking the item off, or turning the lighting off, takes the light away again.
    // The clock is moved to a night hour when the ambient is too bright for a light to show (it is not put back); the item and the hero's
    // place are restored for the later stages.
    private async Task RunAreaWornLightSmokeAsync(TerrainLoadResult? mapMode = null)
    {
        if (_areaSession is not { } session || session.QueryHero() is null || _worldActor.Session is not AreaGridSession || _canvas is null
            || _areaAmbient is not { } driver || _readyArea is not { Lighting: { Local: { } all, CarriedFrame: { } } lighting } area)
        { GD.Print("ARC-DEV-335 native-smoke worn-light NONE map=" + _readyMapName + " (needs a hero on a lit map)"); return; }
        static void Require(bool value, string why)
        {
            if (value) return;
            GD.Print("ARC-DEV-335 worn-light smoke FAIL: " + why);
            throw new InvalidOperationException("AREA_WORN_LIGHT_SMOKE_FAILED");
        }
        if (session.QueryEquipment().Worn.FirstOrDefault(w => w.Slot == EquipmentSlot.Weapon) is not { } wielded
            || session.QueryHeroKit().Where(k => k.Entity == wielded.Entity).Select(k => (SessionKitItem?)k).FirstOrDefault() is not { } kit)
        { GD.Print("ARC-DEV-335 native-smoke worn-light NONE map=" + _readyMapName + " (the hero wields no kit item to light)"); return; }
        bool Wear(EquipmentItem item)
        {
            if (session.Unequip(session.Version, EquipmentSlot.Weapon, _ => SessionInsertVerdict.Accepted).Disposition == GameSessionDisposition.Rejected) return false;
            return session.Equip(session.Version, wielded.Entity, EquipmentSlot.Weapon, new(kit.Kind, [], false, item), (_, _) => true,
                _ => SessionInsertVerdict.Accepted).Disposition == GameSessionDisposition.Changed;
        }
        var simulatedFocus = _worldFocusSimulationForSelfTest; var focused = _worldFocused; var testTime = _worldTestTime;
        _worldFocusSimulationForSelfTest = true;
        var wasEnabled = driver.Enabled;
        var map = mapMode is { Area: { } mapArea, AreaStepSnapshot: { } mapSteps }
            ? new AreaMapRuntime(mapArea.Snapshot, mapSteps, mapArea.Plan.Chart) : _areaTransitions!.Current;
        var home = _worldActor.Session is AreaGridSession grid ? grid.State.CommittedCell : map.Chart.Spawn;
        var worn = false; object? report = null;
        try
        {
            _worldFocused = true; SelectSimulatedPadForSelfTest();
            driver.Enabled = true;
            // A night ambient, so a light's red shows (a bright day saturates it).
            for (var hours = 0; hours < 24 && Math.Max(driver.Effective.Outdoor.R, Math.Max(driver.Effective.Outdoor.G, driver.Effective.Outdoor.B)) > 140; hours++)
            { driver.AdvanceHours(1, AmbientSector()); LogAreaEvents(driver); }
            PushAreaAmbient();
            var start = AreaSmokeStandable(map, home, 2);
            AreaSmokePlace(map, start);
            async Task Frames(int count = 3) { for (var i = 0; i < count; i++) { AreaSmokeFrame(); await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); } TickCarriedLight(); await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
            await Frames();
            var pair = driver.Effective;

            // Nothing worn that lights: the canvas holds no carried light of the hero (the critters' lights near it, if any, are the baseline).
            Require(session.QueryHeroLight() == LightEmission.None, "the hero's own kit gives no light before the test");
            var baseline = _canvas.AreaCarriedForSelfTest?.CarriedLightCount ?? 0;

            // Worn: a large light.
            Require(Wear(wielded.Item with { Light = EquipmentLight.Large }), "the wielded item is worn again with a large light");
            worn = true;
            Require(session.QueryHeroLight() == LightEmission.Large, "the session's hero light is large");
            await Frames();
            var carried = _canvas.AreaCarriedForSelfTest;
            Require(carried is not null && carried.CarriedLightCount == baseline + 1, "the canvas holds the hero's carried light after the item is worn");
            var placement = _canvas.AreaActorPlacementForSelfTest ?? throw new InvalidOperationException("AREA_WORN_LIGHT_NO_PLACEMENT");
            var basis = LightsNow(all, driver) ?? throw new InvalidOperationException("AREA_WORN_LIGHT_NO_LIGHTING");
            var withoutLight = basis.Actor(placement, pair);
            var withLight = carried!.Actor(placement, pair);
            Require(withLight.R > withoutLight.R || withLight.G > withoutLight.G || withLight.B > withoutLight.B, $"the hero is lit by its own light ({withoutLight} -> {withLight})");
            var lattice = carried.CarriedLattice(pair) ?? throw new InvalidOperationException("AREA_WORN_LIGHT_NO_LATTICE");
            var plan = area.Plan;
            var mapLattice = basis.View(plan, lighting.Channels, pair).Lattice;

            // A terrain pixel of the lit area reads back as the merged lattices predict.
            var chart = all.Chart;
            var hero = TerrainGeometry.Project(checked((int)placement.MapX!.Value), checked((int)placement.MapY!.Value));
            var centre = (X: hero.X + (placement.InstanceOffsetX ?? 0) + 40d, Y: hero.Y + (placement.InstanceOffsetY ?? 0) + 20d);
            var observed = 0; var tried = 0;
            foreach (var command in plan.Commands.Where(c => c.Identity is null && lighting.Channels[c.Visual] != AreaAmbientChannel.Unlit)
                .OrderBy(c => Math.Abs(c.Destination.X + c.Destination.Width / 2d - centre.X) + Math.Abs(c.Destination.Y + c.Destination.Height / 2d - centre.Y)).Take(24))
            {
                tried++;
                var ambient = AreaLocalLighting.Ambient(lighting.Channels[command.Visual], pair);
                (double R, double G, double B) Merged(Vector2 world) => StationaryLightField.TerrainPixel(mapLattice, lattice, ambient, world.X, world.Y);
                // The camera is held where the sample puts it (the open input gate would follow the hero back).
                if (!await WithAreaSmokeCameraHeld(() => ObserveTerrainLitAsync(area, command, ambient, Merged))) continue;
                observed++; break;
            }
            Require(observed == 1, $"a terrain pixel near the hero shows the carried light ({tried} candidates tried)");
            // The pixel observation moved the camera: put it back where the hero is.
            await Frames();

            // The light goes with the hero.
            CellCoord? far = null;
            foreach (var (dx, dy) in new[] { (14, 0), (-14, 0), (0, 14), (0, -14) })
            {
                try { if (AreaSmokeStandable(map, new CellCoord(start.X + dx, start.Y + dy), 5) is var cell && cell != start) { far = cell; break; } }
                catch (InvalidOperationException) { }
            }
            Require(far is not null, "a cell the hero can stand on about 14 cells from its start");
            AreaSmokePlace(map, far!.Value);
            await Frames();
            var moved = _canvas.AreaCarriedForSelfTest?.CarriedLattice(pair) ?? throw new InvalidOperationException("AREA_WORN_LIGHT_NO_LATTICE_MOVED");
            Require((moved.FirstColumn, moved.FirstRow) != (lattice.FirstColumn, lattice.FirstRow), "the carried lattice stands elsewhere after the hero stands elsewhere");
            AreaSmokePlace(map, start);
            await Frames();

            // The lighting off draws none; on again, it is back; taken off, it is gone.
            driver.Enabled = false; PushAreaAmbient(); await Frames();
            Require(_canvas.AreaCarriedForSelfTest is null, "the carried light is not drawn with the lighting off");
            driver.Enabled = true; PushAreaAmbient(); await Frames();
            Require((_canvas.AreaCarriedForSelfTest?.CarriedLightCount ?? 0) == baseline + 1, "the carried light is back with the lighting on");
            Require(Wear(wielded.Item), "the original item is worn again");
            worn = false;
            Require(session.QueryHeroLight() == LightEmission.None, "the hero's light is gone");
            await Frames();
            Require((_canvas.AreaCarriedForSelfTest?.CarriedLightCount ?? 0) == baseline, "no carried light of the hero is drawn once the item is off");
            report = new { Map = _readyMapName, Observed = observed, Tried = tried, Lattice = new { lattice.FirstColumn, lattice.FirstRow, lattice.Columns, lattice.Rows }, Hour = driver.Clock.Hour };
            GD.Print("ARC-DEV-335 native-smoke worn-light PASS " + System.Text.Json.JsonSerializer.Serialize(report));
        }
        finally
        {
            if (worn) Wear(wielded.Item);
            driver.Enabled = wasEnabled; PushAreaAmbient();
            _worldFocusSimulationForSelfTest = simulatedFocus; _worldFocused = focused; _worldTestTime = testTime;
            try { AreaSmokePlace(map, home); } catch (InvalidOperationException) { }
            _worldFitView = false; _camera!.ResetWorld(WorldAnchor, WorldViewport, _worldPreferredZoom); PublishWorldSelection();
        }
    }
}
