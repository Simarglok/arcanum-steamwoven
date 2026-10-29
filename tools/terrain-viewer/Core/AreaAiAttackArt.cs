using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Equipment;

namespace Arcanum.TerrainViewer.Core;

/// <summary>
/// ARC-RENDER-038: the attack ART an NPC's attempt-attack wants (node 0x42B9C0, ledger WA4–WA5): the attack animation
/// (<see cref="WeaponArtRules.Animation"/>: 14, 20 or 21) and the weapon part and shield bit its body shows in combat mode
/// (<see cref="WeaponArtRules.WieldedArt"/>, WA1).
/// </summary>
public sealed record AreaAiAttackArtRequest(int Animation, int Weapon, int Shield);

/// <summary>
/// ARC-RENDER-038: the attack ART an NPC's body has for a request: the animation, weapon part and shield of the file read (after the
/// missing-art repair, MA1, and the viewer's fallbacks) and its frames, action frame and own frame rate.
/// </summary>
public sealed record AreaAiAttackArt(int Animation, int Weapon, int Shield, int Frames, int ActionFrame, int RawFramesPerSecond);

/// <summary>
/// ARC-RENDER-038: one NPC attack animation as the AI host started it: at <see cref="StartMs"/> on the host's animation clock, for
/// <see cref="Wanted"/>, with the ART read (<see cref="Art"/>; null: the fixed human attack timing, nothing drawn) and the clip's plan
/// (<see cref="RealTimeCombatRules.Schedule"/>).
/// </summary>
public sealed record AreaAiAttackPlay(string Npc, long StartMs, AreaAiAttackArtRequest Wanted, AreaAiAttackArt? Art, IReadOnlyList<AttackMoment> Moments)
{
    /// <summary>When the play ends (its last update) on the host's clock.</summary>
    public long EndMs => StartMs + (Moments.Count == 0 ? 0 : Moments[^1].AtMs);

    /// <summary>The frame shown at <paramref name="nowMs"/> (frame 0 until the first update, then each update's), or null once the
    /// play ended or before it began.</summary>
    public int? Frame(long nowMs)
    {
        if (nowMs < StartMs || nowMs >= EndMs) return null;
        var frame = 0;
        foreach (var moment in Moments)
        {
            if (StartMs + moment.AtMs > nowMs) break;
            frame = moment.Frame;
        }
        return frame;
    }
}

/// <summary>
/// ARC-RENDER-038: the weapon facts of an NPC's attack art: what the critter shows (0x465020, WA1) and the attack animation (0x42B9C0,
/// WA4). Godot-free.
/// </summary>
public static class AreaAiAttackArtRules
{
    /// <summary>The current animation the attack's low-attack test reads for a target lying down (fall-down, 7: WA4's 7..11).</summary>
    public const int LyingAnimation = 7;

    /// <summary>
    /// The request of an attack: the weapon part (<see cref="WeaponArtRules.WeaponPart"/>) and shield bit
    /// (<see cref="WeaponArtRules.ShieldPart"/>) in combat mode, and <see cref="WeaponArtRules.Animation"/> from the wielded weapon's
    /// flags and the target's object flags, current animation and art.
    /// </summary>
    public static AreaAiAttackArtRequest Request(uint? weaponUseArt, uint weaponFlags, int? shieldItemType, uint targetObjectFlags, int targetAnimation,
        uint targetArt) =>
        new(WeaponArtRules.Animation(weaponFlags, targetObjectFlags, targetAnimation, targetArt), WeaponArtRules.WeaponPart(true, weaponUseArt),
            WeaponArtRules.ShieldPart(true, shieldItemType));

    /// <summary>
    /// The attack's frame rate: the low-attack (21) and attack (20) rates of the drawn ART's weapon slot
    /// (<see cref="RealTimeCombatRules.AttackFramesPerSecond"/>); the throw (14) is drawn without a weapon (MA2) and so plays at the ART's
    /// own rate (the original's rate table for animation 14 is not read: F0).
    /// </summary>
    public static int FramesPerSecond(AreaAiAttackArt art, int speed)
    {
        ArgumentNullException.ThrowIfNull(art);
        var animation = art.Animation == WeaponArtRules.LowAttackAnimation ? AttackAnimation.AttackLow : AttackAnimation.Attack;
        var weapon = art.Animation == WeaponArtRules.ThrowAnimation ? AttackArtWeapon.NoWeapon : (AttackArtWeapon)Math.Clamp(art.Weapon, 0, WeaponArtRules.WeaponParts - 1);
        return RealTimeCombatRules.AttackFramesPerSecond(animation, weapon, Math.Clamp(speed, 1, RealTimeCombatRules.MaxStat),
            Math.Clamp(art.RawFramesPerSecond, 1, RealTimeCombatRules.MaxStat));
    }
}
