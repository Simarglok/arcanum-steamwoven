using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Equipment;

namespace Arcanum.Session;

/// <summary>
/// ARC-SCRIPT-1483 (script trigger audit slice 5, ledger CP1–CP5): what the session tells the host's combat-point scripts of a hit that is resolved and about to deal its damage: the original's hit step
/// (0x4B3770 for a swing or an arrow's strike, 0x4B2F60 for a projectile's) runs the scripts before the damage step. <see cref="Weapon"/> is the weapon item of the attack's context (the wielded weapon,
/// the thrown item, the projectile's weapon; null for bare hands or a critter that holds none): its hit (7) and critical-hit (34) scripts run when it is not null, the attacker's own critter-hits script (26)
/// runs whether or not it is. <see cref="Critical"/> is the context's critical flag (the critical-hit point, 34). <see cref="Projectile"/> marks the projectile path (0x4B2F60, a shot that flew), whose critical-hit point asks
/// that the weapon is a weapon item. <see cref="Thrown"/>: the weapon is an item thrown (the hero's throw lands through the thrown-item host, which runs the hit point of its own, ARC-MAGIC-042). The attacker and the victim are the host's own (it built the attack). The answers of the scripts are not read (the call sites ignore them).
/// ARC-SCRIPT-2841 (TQ31, 0x4B6410, 0x4B3170): <see cref="SelfHit"/> marks the hit a critical miss turned on the attacker (the context's hit flag 0x2 set, the target := the attacker, the critical flag 0x4 half of the time, which is <see cref="Critical"/> here):
/// the victim is the attacker itself, so the host passes the attacker as the extra object of the weapon's points and as the triggerer of its own critter-hits point (26).
/// ARC-SCRIPT-2724 (0x4B2F60): <see cref="Generic"/> marks a projectile whose parent item is a generic item (an NPC's thrown grenade: no weapon block, <see cref="Arcanum.Simulation.Equipment.WeaponKind.ThrownItem"/>), not a weapon item (object type 5):
/// the projectile path's critical-hit point (34) asks for a weapon item, so the host runs it for no generic item.
/// </summary>
public readonly record struct SessionHitScriptEvent(string? Weapon, bool Critical, bool Projectile, bool Thrown = false, bool SelfHit = false, bool Generic = false)
{
    /// <summary>
    /// ARC-SCRIPT-2724 (0x4B2F60, ledger CP2): whether the weapon's critical-hit script (point 34) runs for the hit: a critical hit runs it; on the projectile path only when the parent item is a weapon item
    /// (<paramref name="parentIsWeapon"/>: the host's reading of the item's type, true for an NPC's wielded weapon) and not a generic item (<see cref="Generic"/>, a thrown grenade).
    /// </summary>
    public bool RunsCriticalHitPoint(bool parentIsWeapon) => Critical && (!Projectile || parentIsWeapon && !Generic);
}

/// <summary>
/// ARC-COMBAT-1719 (ledger PF9, PS5; 0x4B2870): what the session tells the host's weapon scripts at an end of a projectile's flight, asked while the shot is still in flight: <see cref="Attacker"/> is the shooter (the hero's
/// <see cref="SessionEventKinds.PlayerEntity"/>, or an NPC's entity) that the original passes as the triggerer, <see cref="Weapon"/> the projectile's parent item (field 0x50: the thrown item, or the weapon wielded at the shot) whose
/// scripts run, <see cref="Critical"/> the shot's critical flag (the check decided at the shot was a critical hit or a critical miss; an NPC's thrown item keeps no check, so it is false), <see cref="Thrown"/> that the item was thrown
/// (the hero's thrown item runs its hit and miss scripts in the thrown-item host, ARC-MAGIC-042) and <see cref="Target"/> the shot's target the original passes as the extra object (null for a shot with no target, a scattered throw, and
/// for a target the session cannot name). <see cref="IsWeapon"/> is whether the parent item is a weapon (object type 5), which the critical points ask (0x4B2870): read by the session before the end, while a thrown item still
/// lies hidden on the hero's cell (a landing that transforms it, as a creature item's, removes the row the host would read afterwards); an item the session does not list counts as a weapon, and so does an NPC's.
/// </summary>
public readonly record struct SessionShotEndScripts(string Attacker, string Weapon, bool Critical, bool Thrown, string? Target, bool IsWeapon = true);

/// <summary>
/// ARC-SCRIPT-2828 (ledger TQ23; 0x4B4390, 0x4B47F8): what the session tells the host's armour scripts of a critical damage step on a living critter: the original's damage apply runs, after the victim's taking-damage script
/// (point 23) and before the hit points are written, the critical-hit script (point 34, <c>0x22</c>; the hit flag of the damage is set, also on a critical self-hit that a critical miss turned on the attacker, 0x4B6410) of each worn
/// armour piece (object type 7, slots 1000–1008, in slot order) with the attacker as the triggerer and the victim as the extra object; the answers are ignored. <see cref="Recipient"/> says who took the damage (the attacker on a critical
/// self-hit) and <see cref="Worn"/> lists the entities that critter wears in slot order, whatever their type: the host keeps the armour pieces. The attacker and the victim are the host's own (it built the attack).
/// </summary>
public readonly record struct SessionArmourCriticalEvent(DamageRecipient Recipient, IReadOnlyList<string> Worn);

public sealed partial class GameSessionOwner
{
    /// <summary>
    /// ARC-SCRIPT-2841 (0x4B3170 → 0x4B6410 → 0x4B3770, ranged without projectile art 0x4B2F60): the combat-point event of a resolved attack, or null when none runs. A hit or critical hit runs when <paramref name="hitLands"/> (a shot that flies asks at its strike instead);
    /// a critical miss turned on the attacker runs for its self-hit when <paramref name="selfHitRuns"/>: the original's self-hit has a hit step only where the attack is resolved at once (a swing, a shot with no projectile art); a projectile that flies, a throw included,
    /// ends at once on the attacker's own tile and runs no hit step at all (ARC-SCRIPT-2841 exe read), so those callers pass false. The event's <see cref="SessionHitScriptEvent.Critical"/> is the self-hit's critical flag (point 34). A plain miss, a dodge and a refusal run nothing here.
    /// </summary>
    private static SessionHitScriptEvent? HitScriptEventOf(AttackResolution resolution, bool hitLands, bool selfHitRuns, SessionHitScriptEvent hit)
    {
        if (resolution.Refusal != AttackRefusal.None) return null;
        return resolution.Outcome switch
        {
            AttackOutcome.Hit or AttackOutcome.CriticalHit when hitLands => hit with { Critical = resolution.Outcome == AttackOutcome.CriticalHit },
            AttackOutcome.CriticalMiss when selfHitRuns && resolution.Damage is { Recipient: DamageRecipient.Attacker } => hit with { Critical = resolution.CriticalSelfHit, SelfHit = true },
            _ => null,
        };
    }

    /// <summary>
    /// ARC-SCRIPT-2828 (0x4B4390, the loop at 0x4B47F8, GT201): tells <paramref name="scripts"/> of the armour a critical damage step reaches, right after the victim's taking-damage script let the damage go on. The step is critical when
    /// the damage is a critical hit's on the target or a critical self-hit's on the attacker (context flag 4); it runs for a living victim only (hit points above 0 before the damage: the original returns for a dead critter before the loop) and
    /// whatever its worn slots hold (<paramref name="worn"/>, null or empty: nothing). A prevented damage never gets here (the gates before the loop returned).
    /// </summary>
    private static void NotifyArmourCritical(Action<SessionArmourCriticalEvent>? scripts, AttackResolution resolution, IReadOnlyDictionary<EquipmentSlot, SessionWearItem>? worn, LifeStatus? status)
    {
        if (scripts is null || resolution.Damage is not { } damage || worn is not { Count: > 0 } || status is not { HitPoints: > 0 }) return;
        var critical = damage.Recipient == DamageRecipient.Target ? resolution.Outcome == AttackOutcome.CriticalHit : resolution.CriticalSelfHit;
        if (!critical) return;
        scripts(new(damage.Recipient, worn.OrderBy(pair => pair.Key).Select(pair => pair.Value.Entity).ToArray()));
    }
}
