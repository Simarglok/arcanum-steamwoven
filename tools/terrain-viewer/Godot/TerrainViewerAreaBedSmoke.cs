using Arcanum.Data.Area;
using Arcanum.Data.Traversal;
using Arcanum.Session;
using Arcanum.Simulation.Combat;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

public partial class TerrainViewerApp
{
    // ARC-WORLD-1162 (E3-d) native observation (stage "bed", only when named, with --hero, on the Crash Site cave): the beds of the shown map (scenery of art type 4, sub-type 0x16) on the real data. A bed whose use slot holds no
    // script refuses the hero with message 8 of sleepUI.mes (the script entry point's "run default", SWB5) and seats nobody; an occupied bed refuses with message 10; a bed whose script ends "skip default" (the cave's script
    // 1893) seats the hero and opens the sleep window (eight buttons, no town-map test), a one-hour rest in it heals exactly twice what the same hour on the ground heals (heal kind 2 against 1), and the close empties the bed.
    // A click on a bed is a use target (hover and click, IsAreaTrigger) and a use from outside the range reports it. The cave's beds are counted by script number first, numbers only.
    private async Task RunAreaBedSmokeAsync()
    {
        if (_areaSession is not { } session || session.QueryHero() is null || !SleepActive || session.QueryHeroLife() is null || _mapTransfers is not { } transfers || _readyArea is not { } area)
        { GD.Print("ARC-WORLD-1162 native-smoke bed NONE map=" + _readyMapName + " (needs --hero and a map mode session)"); return; }
        static void Require(bool value, string why)
        {
            if (value) return;
            GD.Print("ARC-WORLD-1162 bed smoke FAIL: " + why);
            throw new InvalidOperationException("AREA_BED_SMOKE_FAILED");
        }
        if (UseScriptHost(area.Snapshot) is not { } host) { GD.Print("ARC-WORLD-1162 native-smoke bed NONE map=" + _readyMapName + " (no script host)"); return; }
        var beds = transfers.Beds.Select(key => (Key: key, Row: transfers.Object(key))).Select(bed => (bed.Key, bed.Row, Script: host.UseScriptOf(bed.Row))).ToArray();
        GD.Print($"ARC-WORLD-1162 bed census map={_readyMapName} beds={beds.Length} scripted={beds.Count(b => b.Script != 0)} scripts={string.Join(",", beds.Where(b => b.Script != 0).Select(b => b.Script).Distinct().Order())}");
        if (beds.Length == 0) { GD.Print("ARC-WORLD-1162 native-smoke bed NONE map=" + _readyMapName + " (no bed on the map)"); return; }
        // ARC-WORLD-1162 slice 2: the AI host's scene gets the same beds, each with its world cell (an NPC at its standpoint looks for one there).
        var placed = PlacedBeds();
        Require(placed.Count == beds.Length && placed.All(p => beds.Any(b => b.Key == p.Key && b.Row.WorldX == p.Cell.X && b.Row.WorldY == p.Cell.Y)), $"the AI host's bed list is the map's beds with their cells: {placed.Count} of {beds.Length}");
        var unscripted = beds.Where(b => b.Script == 0).Select(b => ((AreaObjectKey, AreaObjectRecord, int)?)b).FirstOrDefault();
        var scripted = beds.Where(b => b.Script != 0).Select(b => ((AreaObjectKey, AreaObjectRecord, int)?)b).FirstOrDefault();
        long host0 = 90_000_000L;
        var hostMs = host0;
        void Tick(long at) { hostMs = at; TickSleepForSelfTest(at); }
        int HpDamage() => session.QueryHeroLife()!.Record.State.HitPointDamage;
                void Wound(int hitPoints, int fatigue)
        {
            var wounded = session.ApplyDamage(session.Version, SessionLifeTarget.Hero, new LifeDamage { HitPoints = hitPoints, Fatigue = fatigue });
            Require(wounded.Disposition != GameSessionDisposition.Rejected && HpDamage() >= hitPoints && session.QueryHeroLife()!.Status.IsDead == false, $"the hero is wounded: {wounded.Disposition} {wounded.Failure} damage {HpDamage()}");
        }
        var occupancy = BedOccupancyForSelfTest;
        Tick(hostMs);
        Require(!SleepOpenForSelfTest && occupancy.Count == 0, "no window and no occupant before the use");

        // ---- every bed is a use target, and a use from out of range says so ----
        Require(beds.All(b => IsAreaTrigger(b.Key)), "every bed is a use target (hover and click)");
        Require(!transfers.IsBed(new AreaObjectKey("none", 0, AreaSourceKind.Static, null, 0, 0, "none", 0)), "an object that is no bed is not one");
        var far = new AreaStepCell(beds[0].Row.WorldX!.Value + 40, beds[0].Row.WorldY!.Value + 40);
        ApplyBedUse(beds[0].Key, far);
        Require(!SleepOpenForSelfTest && _areaUseStatus.StartsWith("too far", StringComparison.Ordinal), $"a use from 40 cells away is out of range: {_areaUseStatus}");

        // ---- a bed with no use script refuses the hero with message 8 and seats nobody ----
        var refusals = 0;
        if (unscripted is { } bare)
        {
            OpenSleepForSelfTest(bare.Item1);
            Require(!SleepOpenForSelfTest && occupancy.Occupant(bare.Item1) is null && RestForSelfTest is null, "a bed with no script refuses: no window, no occupant");
            Require(SleepStatusForSelfTest == SleepTexts().Text(AreaRestMessages.ScriptRefused), $"the refusal is message 8 of sleepUI.mes: {SleepStatusForSelfTest}");
            refusals++;
        }

        if (scripted is not { } good)
        {
            GD.Print($"ARC-WORLD-1162 native-smoke bed PASS {System.Text.Json.JsonSerializer.Serialize(new { Map = _readyMapName, Beds = beds.Length, Scripted = 0, Refusals = refusals })}");
            return;
        }

        // ---- an occupied bed refuses with message 10 (the script has answered first) ----
        Require(occupancy.Seat(good.Item1, "npc:other"), "another sleeper takes the bed");
        OpenSleepForSelfTest(good.Item1);
        Require(!SleepOpenForSelfTest && SleepStatusForSelfTest == SleepTexts().Text(AreaRestMessages.BedOccupied) && occupancy.Occupant(good.Item1) == "npc:other", $"an occupied bed refuses with message 10 and keeps its occupant: {SleepStatusForSelfTest}");
        Require(occupancy.Unseat(good.Item1, "npc:other"), "the other sleeper leaves");

        // ---- the reference: one hour of the ground's heal (kind 1) given straight to the session. (The cave is a town-map sector without the wait marker: the ground window itself is refused with message 20, which a bed is not.) ----
        Wound(9, 14);
        OpenSleepForSelfTest();
        Require(!SleepOpenForSelfTest && SleepStatusForSelfTest == SleepTexts().Text(AreaRestMessages.WaitRefused), $"the key is refused in this town-map sector (message 20): {SleepStatusForSelfTest}");
        var groundBefore = HpDamage();
        session.RestHeal(session.Version, SessionLifeTarget.Hero, AreaRestRules.GroundHealKind);
        var groundDrop = groundBefore - HpDamage();

        // ---- the scripted bed seats the hero: the sleep window, the occupant, kind-2 heal, and the close empties the bed ----
        Wound(9, 14);
        var hpBefore = HpDamage();
        OpenSleepForSelfTest(good.Item1);
        Require(SleepOpenForSelfTest && RestForSelfTest is { Wait: false }, $"the scripted bed (script {good.Item3}) opens the sleep window: {SleepStatusForSelfTest}");
        Require(SleepPanelForSelfTest!.ButtonCountForSelfTest == 8, "the bed's window has eight buttons");
        Require(occupancy.Occupant(good.Item1) == Arcanum.World.AreaScriptHost.PcCode && _restBed == good.Item1, "the hero is the bed's occupant while the window is open");
        // ARC-WORLD-1162 slice 3 (SWB15): the occupied bed is drawn at the next frame of its ART (the plan holds every frame of a bed, whatever its scenery flags), and the empty one at its own.
        Require(area.Plan.SceneryFrames.TryGetValue(good.Item1, out var bedFrames) && bedFrames.Count > 1, $"the plan holds the other frames of a bed's ART: {(area.Plan.SceneryFrames.TryGetValue(good.Item1, out var f) ? f.Count : 0)}");
        ProcessAreaSceneryAnimation();
        var plain = _areaOverlay!.Value.Bodies[good.Item1];
        Require(_sceneryBodies.TryGetValue(good.Item1, out var occupiedBed) && occupiedBed.Frame.Frame == AreaBeds.OccupiedFrame(plain.Frame.Frame, bedFrames!.Count) && occupiedBed.Frame.Frame != plain.Frame.Frame,
            $"the occupied bed is drawn at its next frame: {(_sceneryBodies.TryGetValue(good.Item1, out var shown) ? shown.Frame.Frame : -1)} against {plain.Frame.Frame}");
        // A second open (the key) closes the window and empties the bed; a third lies down again.
        OpenSleepForSelfTest(good.Item1);
        Require(!SleepOpenForSelfTest && occupancy.Occupant(good.Item1) is null && _restBed is null, "the toggle closes the window and takes the hero out of the bed");
        ProcessAreaSceneryAnimation();
        Require(!_sceneryBodies.ContainsKey(good.Item1), "the emptied bed is drawn at its own frame again");
        OpenSleepForSelfTest(good.Item1);
        Require(SleepOpenForSelfTest && occupancy.Occupant(good.Item1) == Arcanum.World.AreaScriptHost.PcCode, "and lying down again seats him again");
        Tick(hostMs + 10);
        SleepPressedForSelfTest(0);
        Tick(hostMs + AreaRestRun.FirstStepMs);
        Require(!SleepOpenForSelfTest && occupancy.Occupant(good.Item1) is null && _restBed is null, "a one-hour rest in the bed ends and empties it");
        var bedDrop = hpBefore - HpDamage();
        GD.Print($"ARC-WORLD-1162 bed heal ground hp={groundDrop} bed hp={bedDrop}");
        Require(bedDrop > 0, $"the bed heals: hit points {bedDrop}");
        // Exactly twice the ground's hour (the hit-point heal of the same wound; fatigue also recovers by itself inside the jump).
        Require(groundDrop > 0 && (HpDamage() == 0 ? bedDrop >= groundDrop : bedDrop == 2 * groundDrop), $"a bed heals twice the ground: {bedDrop} vs {groundDrop}");
        Tick(hostMs + AreaRestFade.DurationMs + 100);
        Require(WindowPanelsMatchStack, "the stack and the panels agree at the end");
        await Task.CompletedTask;

        GD.Print("ARC-WORLD-1162 native-smoke bed PASS " + System.Text.Json.JsonSerializer.Serialize(new
        {
            Map = _readyMapName, Beds = beds.Length, Scripted = beds.Count(b => b.Script != 0), Refusals = refusals, GroundDrop = groundDrop, BedDrop = bedDrop, Script = good.Item3,
        }));
    }
}
