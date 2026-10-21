using Arcanum.Session;
using Arcanum.Simulation.Skills;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-SKILLS-008: prowling in the AREA view. The palette's Prowl action stands for the original's skill-bar prowling button: it
// toggles the session hero's concealment and its followers' (no check, no action points). While the hero is concealed its
// steps follow the stealth gait (ledger ST3): below master it never runs, a master runs while it has fatigue. The viewer draws
// the ordinary walk and stand (the crouch and the stealth walk animations are not drawn).
public partial class TerrainViewerApp
{
    private string? _prowlNotice;

    private void PaletteProwlToggle()
    {
        if (!GaitControlsActive || _areaSession is not { } session) { _prowlNotice = "prowl: no area session"; BindGaitPalette(); return; }
        var result = session.ToggleHeroConcealment(session.Version);
        _prowlNotice = result.Disposition == GameSessionDisposition.Rejected ? $"prowl refused: {result.Failure}"
            : session.IsConcealed(SessionCombatant.Hero) ? "prowling (concealed)" : "prowling off";
        GD.Print($"ARC-SKILLS-008 prowl disposition={result.Disposition} failure={result.Failure} concealed={session.IsConcealed(SessionCombatant.Hero)} " +
            $"followers={session.QueryConcealment().Npcs.Count}");
        BindGaitPalette(); RefreshReadyStatus();
    }

    private bool HeroConcealed => _areaSession is { } session && session.IsConcealed(SessionCombatant.Hero);

    // A concealed hero runs only as a master of prowling with fatigue left (ST3).
    private bool StealthAllowsRun() => !HeroConcealed || _areaSession!.QueryHeroSkills() is { } skills
        && StealthRules.Gait(skills[Skill.Prowling].Training, HeroFatigue() is > 0).CanRun;

    private string StealthSummary() => !HeroConcealed ? "" : StealthAllowsRun() ? "; prowling: a master, runs allowed" : "; prowling: walks only";
}
