using Arcanum.Session;
using Arcanum.Simulation.Skills;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-SKILLS-008: prowling in the AREA view. The palette's Prowl action stands for the original's skill-bar prowling button: it
// toggles the session hero's concealment and its followers' (no check, no action points). While the hero is concealed its
// steps follow the stealth gait (ledger ST3): below master it never runs, a master runs while it has fatigue. The viewer draws
// the stand and, ARC-RENDER-2539, the stealth walk (animation 3) below prowling expert; the crouch (animation 5) is still not drawn for the hero.
public partial class TerrainViewerApp
{
    private string? _prowlNotice;

    private void PaletteProwlToggle()
    {
        if (!GaitControlsActive || _areaSession is not { } session) { _prowlNotice = "prowl: no area session"; BindGaitPalette(); return; }
        // ARC-UI-022: the skill bar's button press becomes the latest recent action (0x57EFA0).
        RecordRecentAction(Arcanum.Simulation.Hotkeys.HotkeyEntry.ForSkill(Arcanum.Simulation.Hotkeys.SkillBarButton.Prowling));
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

    // ARC-RENDER-2539 (0x42EDC0): the concealment flag is set and the prowling training is below expert (none or apprentice).
    private bool HeroSneaks() => HeroConcealed && _areaSession!.QueryHeroSkills() is { } skills
        && StealthRules.Gait(skills[Skill.Prowling].Training, true).Walk == StealthWalk.StealthWalk;

    private string StealthSummary() => !HeroConcealed ? "" : StealthAllowsRun() ? "; prowling: a master, runs allowed" : "; prowling: walks only";
}
