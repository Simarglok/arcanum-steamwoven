using Arcanum.Simulation;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Equipment;
using Arcanum.Simulation.Skills;

namespace Arcanum.Session;

/// <summary>
/// ARC-UI-108: what the item window reads of the local PC (0x554F10 with the observer 0x57CCF0 gives it, ledger OH12–OH18): the PC's
/// effective magick/tech aptitude (stat 16, clamped to the equipment rules' range as the attack does), its training in the skill of each
/// weapon kind (melee, bow, throwing, firearms, for the weapon speed 0x465CF0), and the count of the key numbers on its key ring (0x463110).
/// </summary>
public sealed record SessionItemViewer(int Aptitude, IReadOnlyList<SkillTraining> WeaponTraining, int KeyCount)
{
    /// <summary>The PC's training in the weapon skill with <paramref name="weaponSkill"/> 0 melee, 1 bow, 2 throwing, 3 firearms
    /// (<c>CombatWeaponSkill</c>), none for another number.</summary>
    public SkillTraining TrainingOf(int weaponSkill) =>
        weaponSkill >= 0 && weaponSkill < WeaponTraining.Count ? WeaponTraining[weaponSkill] : SkillTraining.None;
}

public sealed partial class GameSessionOwner
{
    /// <summary>ARC-UI-108: the local PC as the item window sees it; null without a hero (the original's 0x57CD60 describes nothing for an
    /// invalid PC, so the viewer then keeps the window's name only).</summary>
    public SessionItemViewer? QueryItemViewer()
    {
        ThrowIfDisposed();
        if (_hero is null) return null;
        var skills = HeroSkills();
        var training = new[] { CombatSkill.Melee, CombatSkill.Bow, CombatSkill.Throwing, CombatSkill.Firearms }
            .Select(skill => skills[SkillRules.Of(skill)].Training).ToArray();
        return new(HeroAptitude(), Array.AsReadOnly(training), _story.HeroKeys.Count);
    }
}
