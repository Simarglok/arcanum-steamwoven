using Arcanum.Data.Traversal;
using Arcanum.Session;
using Arcanum.Simulation.Movement;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-DEV-408: the state a smoke stage leaves behind must not decide the next stage. Stages of one process run in one session,
// and a stage that passes alone failed after others because of what its predecessors left: the hero still in combat mode (worldmap:
// "leave combat mode first"), the session's roll sequence further along (floaters: the NPC's first critical hit knocked it out instead
// of leaving it able to strike back) and the hero standing where the last stage put it (npc-door: the cell its NPC must walk into).
// TerrainViewerSmokeStageSet calls SmokeStageBoundary at every change of stage; the first call takes the baseline (what a stage run
// alone starts from) and every later one puts these back. A new kind of leak is one more line in the reset, not a change to one stage.
// Not reset here: world objects a stage changed (doors, items, NPC bodies) and the viewer's windows; those are the stage's own to restore.
public partial class TerrainViewerApp
{
    private (GameSessionOwner Session, int Map, AreaStepCell Hero, SessionRandomState Random)? _smokeBaseline;

    private void SmokeStageBoundary(string? previous, string next)
    {
        if (System.Environment.GetEnvironmentVariable("ARC_SMOKE_STAGE_RESET") == "0") return; // for telling a leak from a stage's own failure: the run as before ARC-DEV-408
        if (_areaSession is not { } session || session.QueryHero() is null || _worldActor.Session is not AreaGridSession grid || _readyArea is null) return;
        var hero = grid.Chart.ToWorld(grid.State.CommittedCell);
        // The first stage of a session (or of the session a stage's load replaced) starts from what is there now.
        if (_smokeBaseline is not { } baseline || !ReferenceEquals(baseline.Session, session))
        {
            _smokeBaseline = (session, CurrentAreaMap, hero, session.QueryRandom());
            GD.Print($"ARC-DEV-408 stage-baseline before {next}: map={CurrentAreaMap} hero={hero.X},{hero.Y} draws={session.QueryRandom().Draws}");
            return;
        }
        var reset = new List<string>();
        if (HeroInCombatMode)
        {
            session.SetCombatMode(session.Version, SessionCombatant.Hero, false, SessionCombatModeContext.RealTime);
            CancelAreaAttack("stage boundary");
            reset.Add("combat-mode(off)");
        }
        var random = session.QueryRandom();
        if (random != baseline.Random && session.RestoreRandom(baseline.Random)) reset.Add($"random({random.Draws}->{baseline.Random.Draws})");
        // The hero goes back to where the process started when that cell is on the shown map (a window the stages moved may no longer hold it).
        if (CurrentAreaMap == baseline.Map && hero != baseline.Hero
            && AreaQuickSave.InShownSectors(_readyArea.Snapshot.Sectors.Where(s => s.Scope == Arcanum.Data.Area.AreaScope.Core && s.Present).Select(s => s.Coordinate),
                new(checked((int)baseline.Hero.X), checked((int)baseline.Hero.Y))))
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
        if (reset.Count > 0) GD.Print($"ARC-DEV-408 stage-reset {previous ?? "-"} -> {next}: {string.Join(", ", reset)}");
    }
}
