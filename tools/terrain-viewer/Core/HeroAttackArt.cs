using Arcanum.Data.Area;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Equipment;
using Arcanum.World;

namespace Arcanum.TerrainViewer.Core;

/// <summary>
/// ARC-RENDER-039: which of the hero body's ARTs an attack plays and at which rate (ledger WA4, WA5, WA9, MA1). The attack animation
/// is the original's (<see cref="WeaponArtRules.Animation"/>): the throw for a returning weapon (the session's throwing-skill weapon,
/// flag 0x40, 0x465D80), else the low attack against a shrunk, falling, lying or short target, else the attack. The target's facts are
/// its placed object's: the current art (field 1) and the object flags (field 0x13); a lying target's animation is the host's (the
/// session's knocked-out or killed NPC). Godot-free.
/// </summary>
public static class HeroAttackArt
{
    // AREA field slots: slot = field - 1 (the current art is field 1, the object flags field 0x13).
    private const int ArtSlot = 0, ObjectFlagsSlot = 18;

    /// <summary>The animation (14, 20 or 21) of the hero's attack with a weapon of <paramref name="skill"/> on <paramref name="target"/>.</summary>
    /// <param name="lyingAnimation">The animation the host draws the target in when it differs from its placed art (a fall 7 or a
    /// death 17..19 of an NPC the session knocked out or killed); null: the placed art's (<see cref="WeaponArtRules.AnimationOf"/>).</param>
    public static int Animation(CombatSkill skill, AreaObjectRecord target, int? lyingAnimation)
    {
        ArgumentNullException.ThrowIfNull(target);
        var art = Field(target, ArtSlot);
        return WeaponArtRules.Animation(skill == CombatSkill.Throwing ? WeaponArtRules.ReturningWeaponFlag : 0, Field(target, ObjectFlagsSlot),
            lyingAnimation ?? WeaponArtRules.AnimationOf(art), art);
    }

    /// <summary>
    /// The profile's ART for an attack animation, its drawn action and the rate table it plays at (0x437990 reads the animation of the
    /// art id, which keeps the chosen one when the file is repaired): the throw with the throw ART at that ART's rate (WA9); the low
    /// attack with the low attack ART, or without one the attack ART at the low attack's rate (MA1 repairs the low attack of bare
    /// hands and weapons 2..8, 10 and 13 to the attack); the attack with the attack ART. A low attack or throw ART that is missing or
    /// was left out over the body's key cap counts as missing. Null when a hero body has no such ART: the original's goal fails (WA5).
    /// The fixed human stand-in (a profile that is not a hero body, drawn when the body does not load) plays its attack ART instead,
    /// at the chosen animation's rate.
    /// </summary>
    public static (ActorDiagnosticAction Action, ActorDiagnosticResource Resource, AttackAnimation Rate)? Clip(ActorDiagnosticProfile profile, int animation)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var clip = Own(profile, animation);
        return clip is null && profile.Id != ActorDiagnosticProfileId.HeroBody && profile.Attack is { } standIn
            ? (ActorDiagnosticAction.Attack, standIn, animation == WeaponArtRules.ThrowAnimation ? AttackAnimation.Throw : AttackAnimation.AttackLow)
            : clip;
    }

    private static (ActorDiagnosticAction Action, ActorDiagnosticResource Resource, AttackAnimation Rate)? Own(ActorDiagnosticProfile profile, int animation) =>
        animation switch
        {
            WeaponArtRules.ThrowAnimation => profile.Throw is { } thrown ? (ActorDiagnosticAction.Throw, thrown, AttackAnimation.Throw) : null,
            WeaponArtRules.LowAttackAnimation => profile.LowAttack is { } low ? (ActorDiagnosticAction.LowAttack, low, AttackAnimation.AttackLow)
                : profile.Attack is { } attack && HeroBody.LowAttackRepaired(WeaponArtRules.Weapon(attack.Selector))
                    ? (ActorDiagnosticAction.Attack, attack, AttackAnimation.AttackLow) : null,
            WeaponArtRules.AttackAnimation => profile.Attack is { } attack ? (ActorDiagnosticAction.Attack, attack, AttackAnimation.Attack) : null,
            _ => throw new ArgumentOutOfRangeException(nameof(animation)),
        };

    private static uint Field(AreaObjectRecord row, int slot) =>
        unchecked((uint)(row.EffectiveFields.FirstOrDefault(f => f.Slot == slot && f.Presence == AreaPresence.Value)?.Number ?? 0));
}
