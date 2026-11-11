using Arcanum.Data.Traversal;
using Arcanum.Session;
using Arcanum.Simulation.Movement;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-DEV-408: the state a smoke stage leaves behind must not decide the next stage. Stages of one process run in one session, and a
// stage that passes alone failed after others because of what its predecessors left: the hero in combat mode (worldmap: "leave combat
// mode first"), the session's roll sequence further along (floaters: the NPC's critical hit knocked it out), the hero standing where the
// last stage put it (npc-door: the cell its NPC must walk into), an NPC a stage killed (placement-census) ...
// TerrainViewerSmokeStageSet calls SmokeStageBoundary at every change of stage, and the default reset puts back three things: the
// hero's combat mode, the hero's cell (when it is on the shown map) and the session's roll position, each to what the first stage of the
// process started with.
// Not reset: world objects a stage changed (a killed NPC, a used door, a looted container) and what the viewer holds outside the session.
// ARC_SMOKE_STAGE_RESET=0 switches it off, to tell a leak from a stage's own failure. =full is the wider experiment: the first call saves the
// baseline into a private save folder and every later call loads it back through the production quick-load path (hero, combat, NPCs,
// doors, containers, clocks); it fixes the killed-NPC leaks, but a loaded session is not a fresh one (inv-arrange counts revisions
// and sees one more), so it is not the default. When a load is not possible the narrower reset runs.
public partial class TerrainViewerApp
{
    private string? _smokeBaselineName;
    private (int Map, AreaStepCell Hero, SessionRandomState Random)? _smokeBaseline;
    private GameSaveLibrary? _smokeBaselineLibrary;

    private void SmokeStageBoundary(string? previous, string next)
    {
        var mode = System.Environment.GetEnvironmentVariable("ARC_SMOKE_STAGE_RESET");
        if (mode == "0") return;
        // Before a hero and a session exist there is nothing to reset: the baseline is taken at the first boundary where they do.
        if (_areaSession is not { } session || session.QueryHero() is null || _worldActor.Session is not AreaGridSession grid || _readyArea is null) return;
        var hero = grid.Chart.ToWorld(grid.State.CommittedCell);
        // The first stage of the process starts from what is there now; it is the baseline for every later one, also when a stage
        // replaced the session by a load of its own (what that stage left is what the reset removes).
        if (_smokeBaseline is not { } baseline)
        {
            var saved = mode == "full" && SmokeSaveBaseline();
            _smokeBaseline = (CurrentAreaMap, hero, session.QueryRandom());
            GD.Print($"ARC-DEV-408 stage-baseline before {next}: map={CurrentAreaMap} hero={hero.X},{hero.Y} draws={session.QueryRandom().Draws} saved={saved}");
            return;
        }
        var reset = new List<string>();
        var baselineCell = new CellCoord(checked((int)baseline.Hero.X), checked((int)baseline.Hero.Y));
        var onShownMap = CurrentAreaMap == baseline.Map
            && AreaQuickSave.InShownSectors(_readyArea.Snapshot.Sectors.Where(s => s.Scope == Arcanum.Data.Area.AreaScope.Core && s.Present).Select(s => s.Coordinate), baselineCell);
        if (mode == "full" && onShownMap && _smokeBaselineLibrary is not null && _smokeBaselineName is not null && SmokeLoadBaseline())
        {
            session = _areaSession!;
            reset.Add("session(loaded from the baseline)");
            grid = (AreaGridSession)_worldActor.Session!;
            hero = grid.Chart.ToWorld(grid.State.CommittedCell);
        }
        else
        {
            if (HeroInCombatMode)
            {
                // The viewer's own context, as a map change leaves combat mode: a turn-based round ends with it.
                var left = session.SetCombatMode(session.Version, SessionCombatant.Hero, false, CombatModeContext());
                CancelAreaAttack("stage boundary");
                reset.Add(HeroInCombatMode ? $"combat-mode(STILL ON: {left.Disposition} {left.Refusal})" : "combat-mode(off)");
            }
            // The hero goes back to where the process started when that cell is on the shown map (a window the stages moved may no longer hold it).
            if (hero != baseline.Hero && onShownMap)
            {
                var simulatedFocus = _worldFocusSimulationForSelfTest; var focused = _worldFocused;
                _worldFocusSimulationForSelfTest = true; _worldFocused = true;
                try
                {
                    AreaSmokePlace(new AreaMapRuntime(_readyArea.Snapshot, grid.Snapshot, grid.Chart), grid.Chart.ToChart(baseline.Hero));
                    reset.Add($"hero({hero.X},{hero.Y}->{baseline.Hero.X},{baseline.Hero.Y})");
                }
                catch (InvalidOperationException refused) { GD.Print($"ARC-DEV-408 stage-reset hero placement refused: {refused.Message} ({AreaSmokeGateWhy()})"); }
                finally { _worldFocusSimulationForSelfTest = simulatedFocus; _worldFocused = focused; }
            }
        }
        var random = session.QueryRandom();
        if (random != baseline.Random && session.RestoreRandom(baseline.Random)) reset.Add($"random({random.Draws}->{baseline.Random.Draws})");
        if (reset.Count > 0) GD.Print($"ARC-DEV-408 stage-reset {previous ?? "-"} -> {next}: {string.Join(", ", reset)}");
    }

    private bool SmokeSaveBaseline()
    {
        _smokeBaselineLibrary = new(System.IO.Directory.CreateTempSubdirectory("arc-dev-408-").FullName);
        var previous = _saveLibraryForSelfTest;
        _saveLibraryForSelfTest = _smokeBaselineLibrary;
        try
        {
            if (SaveGame(null, "smoke baseline", null, "smoke ") is { } name) { _smokeBaselineName = name; return true; }
            _smokeBaselineLibrary = null;
            GD.Print($"ARC-DEV-408 stage-baseline save refused: {_quickSaveStatus}");
            return false;
        }
        finally { _saveLibraryForSelfTest = previous; }
    }

    private bool SmokeLoadBaseline()
    {
        if (QuickBusyReason() != QuickBusy.None) { GD.Print($"ARC-DEV-408 stage-reset load skipped: {Describe(QuickBusyReason())}"); return false; }
        var focused = _worldFocused; var simulatedFocus = _worldFocusSimulationForSelfTest;
        var loaded = LoadGame(_smokeBaselineLibrary!.Slot(_smokeBaselineName!), "smoke ");
        _worldFocused = focused; _worldFocusSimulationForSelfTest = simulatedFocus;
        if (!loaded) GD.Print($"ARC-DEV-408 stage-reset load refused: {_quickSaveStatus}");
        return loaded;
    }
}
