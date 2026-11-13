namespace Arcanum.Session;

/// <summary>
/// ARC-SCRIPT-1483 (script trigger audit slice 5, ledger CP1–CP5): what the session tells the host's combat-point scripts of a hit that is resolved and about to deal its damage: the original's hit step
/// (0x4B3770 for a swing or an arrow's strike, 0x4B2F60 for a projectile's) runs the scripts before the damage step. <see cref="Weapon"/> is the weapon item of the attack's context (the wielded weapon,
/// the thrown item, the projectile's weapon; null for bare hands or a critter that holds none): its hit (7) and critical-hit (34) scripts run when it is not null, the attacker's own critter-hits script (26)
/// runs whether or not it is. <see cref="Critical"/> is the context's critical flag (the critical-hit point, 34). <see cref="Projectile"/> marks the projectile path (0x4B2F60, a shot that flew), whose critical-hit point asks
/// that the weapon is a weapon item. <see cref="Thrown"/>: the weapon is an item thrown (the hero's throw lands through the thrown-item host, which runs the hit point of its own, ARC-MAGIC-042). The attacker and the victim are the host's own (it built the attack). The answers of the scripts are not read (the call sites ignore them).
/// </summary>
public readonly record struct SessionHitScriptEvent(string? Weapon, bool Critical, bool Projectile, bool Thrown = false);

/// <summary>
/// ARC-COMBAT-1719 (ledger PF9, PS5; 0x4B2870): what the session tells the host's weapon scripts at an end of a projectile's flight, asked while the shot is still in flight: <see cref="Attacker"/> is the shooter (the hero's
/// <see cref="SessionEventKinds.PlayerEntity"/>, or an NPC's entity) that the original passes as the triggerer, <see cref="Weapon"/> the projectile's parent item (field 0x50: the thrown item, or the weapon wielded at the shot) whose
/// scripts run, <see cref="Critical"/> the shot's critical flag (the check decided at the shot was a critical hit or a critical miss; an NPC's thrown item keeps no check, so it is false), <see cref="Thrown"/> that the item was thrown
/// (the hero's thrown item runs its hit and miss scripts in the thrown-item host, ARC-MAGIC-042) and <see cref="Target"/> the shot's target the original passes as the extra object (null for a shot with no target, a scattered throw, and
/// for a target the session cannot name).
/// </summary>
public readonly record struct SessionShotEndScripts(string Attacker, string Weapon, bool Critical, bool Thrown, string? Target);
