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
// TerrainViewerSmokeStageSet calls SmokeStageBoundary at every change of stage. ARC-DEV-1187: the default reset puts the whole session back:
// the first call saves the baseline into a private save folder (the production save path) and every later call loads it back through the production
// quick-load path (hero, combat mode, NPCs and their life, party, doors, containers, clocks), so no stage inherits a killed or wounded NPC, a companion
// a stage took into the party, a used door or a looted container; then the session's roll position is put back too (a load does not restore it).
// ARC_SMOKE_STAGE_RESET=narrow is the older narrow reset (the hero's combat mode, the hero's cell when it is on the shown map, the party's members a stage took
// in, the roll position; ARC-DEV-408) and also the fallback when the load is not possible (a stage left the shown map, the quick-load is busy, the baseline
// could not be saved). =0 switches the reset off, to tell a leak from a stage's own failure.
// Limits: what the viewer holds outside the session is not reset (its windows, caches, the world map's open state); a loaded session is not the fresh one
// in what the viewer computes per frame (the town map's tile under the hero is revealed once per session instance, so the baseline takes it before it is saved).
public partial class TerrainViewerApp
{
    private string? _smokeBaselineName;
    private (int Map, AreaStepCell Hero, SessionRandomState Random, IReadOnlySet<string> Party)? _smokeBaseline;
    private GameSaveLibrary? _smokeBaselineLibrary;

    private void SmokeStageBoundary(string? previous, string next)
    {
        var mode = System.Environment.GetEnvironmentVariable("ARC_SMOKE_STAGE_RESET");
        if (mode == "0") return;
        var full = mode != "narrow";
        // Before a hero and a session exist there is nothing to reset: the baseline is taken at the first boundary where they do.
        if (_areaSession is not { } session || session.QueryHero() is null || _worldActor.Session is not AreaGridSession grid || _readyArea is null) return;
        var hero = grid.Chart.ToWorld(grid.State.CommittedCell);
        // The first stage of the process starts from what is there now; it is the baseline for every later one, also when a stage
        // replaced the session by a load of its own (what that stage left is what the reset removes).
        if (_smokeBaseline is not { } baseline)
        {
            // The town map's tile under the hero is revealed by the viewer once per frame (WM78); a baseline taken before it was would give every loaded session
            // one more revision on its first frame (inv-arrange counts the revisions of a press), so the baseline has it already.
            RevealWalkedTownMapCell();
            var saved = full && SmokeSaveBaseline();
            _smokeBaseline = (CurrentAreaMap, hero, session.QueryRandom(), PartyLedByPc(session));
            GD.Print($"ARC-DEV-408 stage-baseline before {next}: map={CurrentAreaMap} hero={hero.X},{hero.Y} draws={session.QueryRandom().Draws} saved={saved}");
            return;
        }
        var reset = new List<string>();
        var baselineCell = new CellCoord(checked((int)baseline.Hero.X), checked((int)baseline.Hero.Y));
        var onShownMap = CurrentAreaMap == baseline.Map
            && AreaQuickSave.InShownSectors(_readyArea.Snapshot.Sectors.Where(s => s.Scope == Arcanum.Data.Area.AreaScope.Core && s.Present).Select(s => s.Coordinate), baselineCell);
        if (full && onShownMap && _smokeBaselineLibrary is not null && _smokeBaselineName is not null && SmokeLoadBaseline())
        {
            session = _areaSession!;
            // The loaded session is a new instance: the viewer's once-per-session town map reveal (WM78) runs now, not in the first frame of the next stage (a revision there
            // would count in a stage that counts them); the town files are open by the second boundary.
            RevealWalkedTownMapCell();
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
        // ARC-DEV-1187: an NPC a stage took into the party (the barter merchant joins in life and opening) is no longer free for the stages that need
        // a free speaker (companion-click and ai-follower exit with NO-FOLLOWER, pickpocket with no candidate): it leaves the party as the stages
        // that join one end theirs (PartyLeave Force), and the companion bodies and the selection are rebuilt.
        var joined = PartyLedByPc(session).Where(entity => !baseline.Party.Contains(entity)).Order(StringComparer.Ordinal).ToArray();
        if (joined.Length > 0)
        {
            var refused = new List<string>();
            foreach (var entity in joined)
                if (session.LeaveParty(session.Version, entity, new PartyLeave(Force: true)) is { Disposition: GameSessionDisposition.Rejected } left) refused.Add($"{entity[..Math.Min(12, entity.Length)]}:{left.Failure}");
            BindAreaCompanions();
            PublishWorldSelection();
            reset.Add(refused.Count == 0 ? $"party-left({joined.Length})" : $"party-left({joined.Length - refused.Count}, REFUSED {string.Join(" ", refused)})");
        }
        var random = session.QueryRandom();
        if (random != baseline.Random && session.RestoreRandom(baseline.Random)) reset.Add($"random({random.Draws}->{baseline.Random.Draws})");
        if (reset.Count > 0) GD.Print($"ARC-DEV-408 stage-reset {previous ?? "-"} -> {next}: {string.Join(", ", reset)}");
    }

    // The NPCs the hero leads now: followers (also those the party's Forced flag marks) and waiting ones.
    private static HashSet<string> PartyLedByPc(GameSessionOwner session)
    {
        var party = session.QueryParty();
        return party.Followers.Concat(party.Others).Where(c => c.LedByPc).Select(c => c.Entity).ToHashSet(StringComparer.Ordinal);
    }

    private bool SmokeSaveBaseline()
    {
        var folder = System.IO.Directory.CreateTempSubdirectory("arc-dev-408-").FullName;
        // Best effort: the baseline folder goes with the process (the smoke runner removes only its settings folder).
        AppDomain.CurrentDomain.ProcessExit += (_, _) => { try { System.IO.Directory.Delete(folder, recursive: true); } catch (System.Exception e) when (e is System.IO.IOException or UnauthorizedAccessException) { } };
        _smokeBaselineLibrary = new(folder);
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
