using Arcanum.Simulation;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Equipment;
using Arcanum.Simulation.Magic;
using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Randomness;

namespace Arcanum.Session;

/// <summary>
/// ARC-WORLD-092: one melee attack of an NPC (the AI's attempt-attack hit step, 0x42A630 → 0x4B3BB0 → 0x4B3170) on the hero
/// (<see cref="Target"/> null) or on another NPC, for <see cref="GameSessionOwner.AttackByNpc"/>. <see cref="Distance"/> is the tile
/// distance (within the weapon's reach); <see cref="AttackerWorn"/> and <see cref="TargetWorn"/> are the two sides' wear-slot items with
/// their durability baselines for the attack's wear (the hero's must be exactly the items it wears; null: that side's items take no
/// wear); <see cref="AttackerCell"/> and <see cref="TargetCell"/> the NPCs' cells where a critical drops their items (null: not
/// applied); <see cref="DropMap"/> the shown map for the drop's placement. The attack is never aimed (the AI's goal stores none).
/// </summary>
public sealed record SessionNpcAttack(SessionCritterCombat Attacker)
{
    public SessionCritterCombat? Target { get; init; }
    public int Distance { get; init; } = 1;
    public IReadOnlyDictionary<EquipmentSlot, SessionWearItem>? AttackerWorn { get; init; }
    public IReadOnlyDictionary<EquipmentSlot, SessionWearItem>? TargetWorn { get; init; }
    public CellCoord? AttackerCell { get; init; }
    public CellCoord? TargetCell { get; init; }
    public Arcanum.Simulation.Items.IDropMap? DropMap { get; init; }
    /// <summary>ARC-DEV-273 (RA4): the line of fire from the attacker to the target, for a bow or firearms attack: a blocked line fails the check, else its cost is the cover. Read by an attack whose weapon reaches beyond one tile (a bow, firearms or a reach weapon, RA1), as the hero's.</summary>
    public SessionLineOfFire? LineOfFire { get; init; }
    /// <summary>ARC-DEV-261 (GT198–GT199): the host's taking-damage script (point 0x17) of whoever takes the damage, as <see cref="SessionAttack.TakingDamage"/>: the recipient in (the target, or the attacker on a critical self-hit), whether the damage goes on out.</summary>
    public Func<DamageRecipient, bool>? TakingDamage { get; init; }
    /// <summary>ARC-SCRIPT-1483 (CP1–CP3): the host's combat-point scripts (hit 7, critter hits 26, critical hit 34) of a resolved hit, as <see cref="SessionAttack.HitScripts"/>; an NPC's shot that flies asks at its strike.</summary>
    public Action<SessionHitScriptEvent>? HitScripts { get; init; }
    /// <summary>ARC-DEV-282 (PF1, PF2): the host can fly a shot. A bow or firearms attack whose wielded weapon has a projectile art (<see cref="SessionWearItem.MissileArt"/>) is then decided now and its hit's damage waits for the strike (<see cref="GameSessionOwner.LandNpcShot"/>); the result names the shots in flight. Without it (or without the art) the attack lands at once, as ARC-DEV-273's.</summary>
    public bool Flight { get; init; }
    /// <summary>ARC-AI-1756 (0x4B3170, SC16): the target is the occupant the attack pulled out of a bed it struck (the record's target replaced), so the check carries -30 (<see cref="AttackRequest.TargetWasInBed"/>).</summary>
    public bool TargetWasInBed { get; init; }
    /// <summary>ARC-MAGIC-1188: the trigger host the damage step's item events fire through (as <see cref="SessionAttack.ItemTriggers"/>, ARC-MAGIC-023): the attacker's worn items fire "damages opponent", "target hit" and the knockdown events, the victim's (an NPC's, or the hero's from the session) "parent hit" and the others (0x4B4390 for any attacker, IC2); a host of this session. Null: they fire nothing, as before ARC-MAGIC-1188.</summary>
    public Arcanum.Session.ISessionItemTriggers? ItemTriggers { get; init; }
    /// <summary>ARC-COMBAT-1929 (0x45F9D0): the target's facing and awareness of the attacker, for a backstab of a melee attack with a wielded weapon (the wielded weapon's art subtype comes with <see cref="AttackerWorn"/>). Null: the host knows none, and the attack is no backstab.</summary>
    public SessionBackstabFacts? Backstab { get; init; }
}

/// <summary>
/// ARC-WORLD-113: one throw of an NPC's item (the throw goal 37's throw, 0x42AB90 → 0x4B3C00, ledger TW rows) for
/// <see cref="GameSessionOwner.ThrowByNpc"/>: the <see cref="Thrower"/> on <see cref="ThrowerCell"/> throws <see cref="Item"/>, an item
/// of its list (in this slice the weapon it wields), at the goal's location <see cref="Target"/>; <see cref="Weight"/> is the item's
/// effective weight (0x461370: its stored weight plus its magic adjustment, the host's, as for the hero's throw), which sets its
/// thrown range. The critter the line of fire to the location names (TW4: a blocker before it, or the critter on its tile) is
/// <see cref="Struck"/> (an NPC) or the hero (<see cref="StruckHero"/>), standing on <see cref="StruckCell"/>; with neither the item
/// lands on the location unchecked. <see cref="LineOfFire"/> is the line to the struck critter (the check's cover); the worn items and
/// the drop map are <see cref="SessionNpcAttack"/>'s.
/// </summary>
public sealed record SessionNpcThrow(SessionCritterCombat Thrower, string Item, int Weight, CellCoord ThrowerCell, CellCoord Target)
{
    public SessionCritterCombat? Struck { get; init; }
    public bool StruckHero { get; init; }
    public CellCoord? StruckCell { get; init; }
    public SessionLineOfFire? LineOfFire { get; init; }
    /// <summary>ARC-COMBAT-2523 (ledger TW11): the light level 0..255 (255: full light) at <see cref="Target"/>, a term of the roll of a throw that names no struck critter (0x4C8430); null reads as full light.</summary>
    public int? TargetLight { get; init; }
    public IReadOnlyDictionary<EquipmentSlot, SessionWearItem>? ThrowerWorn { get; init; }
    public IReadOnlyDictionary<EquipmentSlot, SessionWearItem>? StruckWorn { get; init; }
    public Arcanum.Simulation.Items.IDropMap? DropMap { get; init; }
    /// <summary>ARC-DEV-261: as <see cref="SessionNpcAttack.TakingDamage"/>.</summary>
    public Func<DamageRecipient, bool>? TakingDamage { get; init; }
    /// <summary>ARC-SCRIPT-1483: as <see cref="SessionNpcAttack.HitScripts"/>.</summary>
    public Action<SessionHitScriptEvent>? HitScripts { get; init; }
    /// <summary>ARC-MAGIC-1188: as <see cref="SessionNpcAttack.ItemTriggers"/>, for the attack on the struck critter (the thrown item is the event's weapon; the thrower's worn items exclude it).</summary>
    public ISessionItemTriggers? ItemTriggers { get; init; }
    /// <summary>ARC-COMBAT-1717 (PF1, PF6, PF7, PF10; 0x4B3C00 → 0x4B3170): the host can fly the thrown weapon. The throw is then decided now (the check and a miss's scatter, rolled as ever) and the weapon leaves
    /// the NPC to lie hidden on its cell while its projectile flies to the struck critter (a hit) or to the scatter tile (a miss); the hit's damage waits for the strike
    /// (<see cref="GameSessionOwner.LandNpcShot"/>) and the item lands where the flight ends (<see cref="GameSessionOwner.LandThrownItem"/>). A throw at a location with no struck critter flies unchecked
    /// as a generic item does. Without it the throw lands at once, as before.</summary>
    public bool Flight { get; init; }
    /// <summary>ARC-COMBAT-1761 (0x4B3C00, 0x466E00 → 0x467860, 0x441980): the shown map's item script host with the thrower's life target and stored stats (<see cref="SessionNpcWieldScripts"/>, as an NPC's wield gets them), which
    /// runs the item's removal scripts (wield off 25 for a worn item, remove-item 28) and its throw script (6), the thrower as the triggerer. Null: an item with a throw or remove-item script, or a wield-off script while worn, is refused, as before (an item with only a wield-off script that is not worn has nothing to run).</summary>
    public SessionNpcWieldScripts? Scripts { get; init; }
}

/// <summary>ARC-WORLD-113: what <see cref="GameSessionOwner.ThrowByNpc"/> did: the attack on the struck critter (null without one or
/// when refused before it) and the cell the item landed on (null: it was not thrown).</summary>
public sealed record SessionNpcThrowResult(GameSessionDisposition Disposition, GameSessionFailure Failure, SessionAttackResult? Attack,
    CellCoord? Landed, GameSessionVersion Version)
{
    /// <summary>ARC-DEV-258 (MIG13 S14n): the shot of a generic item (a grenade) the NPC threw: it flies to <see cref="Destination"/> as a projectile the host
    /// launches and lands (<see cref="GameSessionOwner.LandThrownItem"/>); empty for a thrown weapon, which lands at once.</summary>
    public IReadOnlyList<SessionShot> Shots { get; init; } = [];
    /// <summary>ARC-DEV-258: the tile a thrown item flies to: the throw goal's location for a generic item or an unstruck weapon; ARC-COMBAT-1717: for a weapon thrown at a critter the struck critter's cell (a hit) or the scatter tile (a miss, a flight
    /// without a target); null when it did not fly.</summary>
    public CellCoord? Destination { get; init; }
    /// <summary>ARC-COMBAT-1761 (0x4B3C00): the item's throw script (point 6) answered skip default, so nothing was thrown: the item went back to the thrower's list unworn (the original puts it on the thrower's tile and inserts it again, 0x4415C0, 0x4617F0),
    /// the removal's and the throw script's own changes kept. The result is <see cref="GameSessionDisposition.Changed"/> with no attack, shot or landing.</summary>
    public bool ScriptDeclined { get; init; }
    /// <summary>ARC-COMBAT-2415 (TW8, 0x4B65D0): the thrown weapon's ammunition check refused the throw: the item left the thrower and lies on its tile switched off, with no shot and no landing (<see cref="GameSessionOwner.QueryItemOff"/>); the host's reaction is 0x4ADFF0 (<see cref="AttackRefusal.OutOfAmmunition"/>).
    /// The result is Changed.</summary>
    public bool AmmunitionShort { get; init; }
    /// <summary>ARC-COMBAT-1761: the lines the removal and throw scripts printed, in order, for the host to post as it posts any script's printed line (<see cref="InventoryMutationResult.Messages"/>).</summary>
    public IReadOnlyList<SessionScriptMessage> Messages { get; init; } = [];
    /// <summary>ARC-COMBAT-1761: the eye candy requests of those scripts, in order, for the host to play.</summary>
    public IReadOnlyList<Arcanum.Scripting.Scr.ScrEyeCandy> EyeCandies { get; init; } = [];
    /// <summary>ARC-COMBAT-1761: the script runs that were made (removal, then throw), as the dispatcher answered them.</summary>
    public IReadOnlyList<SessionItemScriptRun> Scripts { get; init; } = [];
}

/// <summary>ARC-DEV-282: an NPC's arrow in flight (<see cref="GameSessionOwner.QueryNpcShot"/>): its shooter's entity, whether the check decided at the shot was a hit, and the weapon's range.</summary>
public readonly record struct SessionNpcShotFacts(string Shooter, bool Hit, int Range, string Target)
{
    /// <summary>ARC-COMBAT-1717: the projectile art of the weapon an NPC threw (its missile art field, a scenery art faced in the flight's direction), as the shot carried it; null for an arrow and for a weapon without one (the viewer flies the item's own art).</summary>
    public uint? ThrownArt { get; init; }

    /// <summary>The <see cref="Target"/> of a shot aimed at the hero.</summary>
    public const string HeroTarget = "\0hero";

    /// <summary>ARC-COMBAT-2523 (TW11): the <see cref="Target"/> of a thrown weapon aimed at a location with no struck critter: a hit strikes nothing, a miss strikes the critters it passes with a forced hit.</summary>
    public const string LocationTarget = "\0location";
}

public sealed partial class GameSessionOwner
{
    // ARC-WORLD-113: a throw's facts for the NPC attack: the item, its weight, the goal's location, the thrower's cell, the struck
    // critter's cell and the line to it.
    private sealed record NpcThrowing(string Item, int Weight, CellCoord Target, CellCoord From, CellCoord Struck, SessionLineOfFire? Line);

    /// <summary>
    /// ARC-WORLD-113 (NPC AI slice I10, the throw goal 37; ledger TW rows, TA rows): an NPC throws an item of its list at a location,
    /// decided and landed at once. In order, as the original's throw (0x4B3C00) and dispatcher (0x4B3170) for a thrower that is not a
    /// PC: with a struck critter the attack is <see cref="AttackByNpc"/>'s with the throwing skill's level and training (skill 3, TW3)
    /// and the item's thrown range (<see cref="ThrowRules.Range"/>, the check's range: impossible beyond it), the line of fire's cover,
    /// no reach test; a hit lands the item on the struck critter's cell, a miss scatters it from the location
    /// (<see cref="ThrowRules.ScatterTiles"/> with the thrower's throwing level, a rotation drawn after the check, RH2, PS3); the
    /// item leaves the NPC (worn no more) and lies on the landing cell in a revision of its own before the damage, moved there without
    /// the drop's placement, so it joins no junk pile and gets no decay (TW6, ID9); the damage, experience, critical and wear steps
    /// follow with the thrower's hand empty (its critical miss finds no weapon). Without a struck critter nothing is rolled and the item
    /// lands on the location. ARC-DEV-258: a generic item the NPC holds (a grenade) is not thrown here but flies (<see cref="ThrowNpcGeneric"/>). ARC-COMBAT-1717 (<see cref="SessionNpcThrow.Flight"/>): the thrown weapon flies
    /// too, decided now and struck later (the strike is <see cref="LandNpcShot"/>'s, a miss's stray strikes PF6, PF7 forced hits, the landing <see cref="LandThrownItem"/>, the item event at its end PF10); the result then names the shot
    /// (<see cref="SessionNpcThrowResult.Shots"/>) and its destination, with no landing and no damage yet. ARC-COMBAT-1761 (TW1, TW3): with <see cref="SessionNpcThrow.Scripts"/> the item's removal scripts (wield off for a worn item, remove-item) and its throw
    /// script (point 6) run first, the thrower as the triggerer (<see cref="RunNpcThrowScripts"/>); a throw script that answers skip default throws nothing and puts the item back with the thrower (<see cref="SessionNpcThrowResult.ScriptDeclined"/>). NOT READY: a non-critter
    /// blocker (the host names none), a thrown item other than the weapon the NPC wields and a generic item (a food, an ammunition, a key) or an item
    /// the session added to the NPC (no seed holds it), an item with a script at another point than those three (the insert-item script of the give-back included) and the unwear of its item spells. Refused before any roll: no hero
    /// (<see cref="GameSessionFailure.AttackUnavailable"/>), a cell outside 0 .. 2^24 − 1, a weight outside 0 ..
    /// <see cref="ThrowRules.MaxWeight"/>, a struck critter without its cell or both struck kinds, a no-drop item
    /// (<see cref="GameSessionFailure.InvalidAttack"/>), an item not in the NPC's seed list (<see cref="GameSessionFailure.ItemNotAvailable"/>),
    /// an item with a script the session does not run (<see cref="GameSessionFailure.ItemScriptUnsupported"/>: another point, or a removal or throw script without a host or one it cannot apply), an item that is not the wielded weapon, or one the NPC attack does not support (a throwing-skill weapon)
    /// (<see cref="GameSessionFailure.AttackUnsupported"/>), then <see cref="AttackByNpc"/>'s refusals (without a struck critter the
    /// thrower's: its life record, its worn items), the version, the revision. A resolver refusal throws nothing (NoChange, as the
    /// hero's), as does an inactive thrower without a struck critter.
    /// </summary>
    [OriginalFunction(0x4B3C00, OriginalFunctionScope.Partial)]
    public SessionNpcThrowResult ThrowByNpc(GameSessionVersion expected, SessionNpcThrow request)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Thrower);
        SessionNpcThrowResult Refused(GameSessionFailure failure) => new(GameSessionDisposition.Rejected, failure, null, null, Version);
        if (_hero is null) return Refused(GameSessionFailure.AttackUnavailable);
        var struck = request.StruckHero || request.Struck is not null;
        if (!SessionValidation.Entity(request.Item) || !DropCellFits(request.ThrowerCell) || !DropCellFits(request.Target) ||
            request.StruckCell is { } at && !DropCellFits(at) || struck != request.StruckCell.HasValue || request.StruckHero && request.Struck is not null ||
            request.Weight is < 0 or > ThrowRules.MaxWeight || request.LineOfFire is { Cost: < 0 or > AttackResolver.MaxMagnitude } || request.TargetLight is < 0 or > AttackRules.MaxLightLevel)
            return Refused(GameSessionFailure.InvalidAttack);
        // An item a critical or an earlier throw dropped, or one destroyed, is no longer the NPC's.
        if (NpcItemSeed(request.Thrower.Entity, request.Item) is not { } listed || Gone(request.Item)) return Refused(GameSessionFailure.ItemNotAvailable);
        // ARC-SCRIPT-2397 (IV15, IV18; throw node 0x42AB90): an item whose flags & 3 is not 0 is refused: a script's toggle or the map data's own off bit (a thrown item in flight is not in a list). Nothing is removed (the grenade path's item stays in the list).
        if (QueryItemOff(_actor.Map, request.Item, listed.Facts.OffInData)) return Refused(GameSessionFailure.ItemNotAvailable);
        // ARC-COMBAT-1761: the scripts the throw runs (the removal's wield off 25 and remove-item 28, the throw's own 6) are run below under a host; a script point the session does not model on a throw (get, drop, hit, insert-item ...) stays refused.
        if (listed.Facts.ScriptPoints.Any(point => point is not (SessionScriptPoint.Throw or SessionScriptPoint.RemoveItem or SessionScriptPoint.WieldOff))) return Refused(GameSessionFailure.ItemScriptUnsupported);
        // ARC-DEV-258 (MIG13 S14n): a generic item (a grenade) is not a wielded weapon: it flies to the location as a projectile.
        var generic = listed.Facts.Kind.Type == SessionItemType.Generic;
        if (!generic)
        {
            if (listed.Facts.Kind.Type != SessionItemType.Weapon || request.ThrowerWorn?.GetValueOrDefault(EquipmentSlot.Weapon) is not { } wielded ||
                wielded.Entity != request.Item) return Refused(GameSessionFailure.AttackUnsupported);
            // The remove check (0x466DA0) keeps a no-drop item: the attack starter never throws it.
            if (wielded.NoDrop) return Refused(GameSessionFailure.InvalidAttack);
        }
        var worn = request.ThrowerWorn?.Values.Any(item => item.Entity == request.Item) == true;
        ItemScriptRuns? runs = null;
        var applied = false;
        if (listed.Facts.HasScript(SessionScriptPoint.Throw) || listed.Facts.HasScript(SessionScriptPoint.RemoveItem) || worn && listed.Facts.HasScript(SessionScriptPoint.WieldOff))
        {
            var scripted = RunNpcThrowScripts(expected, request, listed.Facts, worn, generic, struck);
            if (scripted.Done is { } done) return done;
            (runs, applied) = (scripted.Runs, scripted.Applied);
            expected = Version;
        }
        var result = ThrowNpcPlanned(expected, request, generic, struck);
        if (runs is null) return result;
        // What the scripts did stays whatever the throw came to: a refusal after them is a changed session with the failure named (the original removed the item before it threw).
        // The lines and eye candy of the scripts are the host's to show only once they took effect: a refused throw whose scripts changed nothing shows none.
        var rejected = result.Disposition == GameSessionDisposition.Rejected;
        if (rejected && !applied) return result with { Scripts = runs.Runs };
        return result with
        {
            Disposition = rejected ? GameSessionDisposition.Changed : result.Disposition,
            Messages = runs.Messages, EyeCandies = runs.EyeCandies, Scripts = runs.Runs,
        };
    }

    // The throw once its scripts ran (or it has none): ARC-DEV-258's generic flight, an unstruck weapon's landing or flight, the attack on the struck critter.
    private SessionNpcThrowResult ThrowNpcPlanned(GameSessionVersion expected, SessionNpcThrow request, bool generic, bool struck)
    {
        // ARC-COMBAT-2523 (TW11): a generic item (a grenade) always flies to the location; a weapon that names no struck critter is thrown at the location: both roll as the hero's location throw does.
        if (generic || !struck) return ThrowNpcAtLocation(expected, request, generic, struck);
        var attack = new SessionNpcAttack(request.Thrower)
        {
            Target = request.Struck,
            Distance = ProjectileRules.Distance(request.ThrowerCell, request.StruckCell!.Value),
            AttackerWorn = request.ThrowerWorn,
            TargetWorn = request.StruckWorn,
            AttackerCell = request.ThrowerCell,
            TargetCell = request.StruckHero ? null : request.StruckCell,
            DropMap = request.DropMap,
            TakingDamage = request.TakingDamage,
            HitScripts = request.HitScripts,
            ItemTriggers = request.ItemTriggers,
            Flight = request.Flight,
        };
        var result = NpcAttack(expected, attack,
            new NpcThrowing(request.Item, request.Weight, request.Target, request.ThrowerCell, request.StruckCell.Value, request.LineOfFire), out var landed, null);
        // ARC-COMBAT-2415 (TW8, 0x4B65D0): the dispatcher refused the thrown weapon before any roll (the thrower, led by a PC, has fewer units of its ammunition type than it consumes): the item stays on the thrower's tile switched off.
        if (result.Resolution?.Refusal == AttackRefusal.OutOfAmmunition) return NpcThrowRefusedForAmmunition(request, result);
        // ARC-COMBAT-1717: a weapon that flies goes to the struck critter's cell (a hit) or the scatter tile (a miss).
        return new(result.Disposition, result.Failure, result.Disposition == GameSessionDisposition.Rejected ? null : result, landed, Version)
        {
            Shots = result.Shots,
            Destination = result.Shots.Count > 0 ? result.Scatter ?? request.StruckCell : null,
        };
    }

    /// <summary>
    /// ARC-COMBAT-2415 (ledger TW8): an NPC's throw of its weapon the ammunition check refused (a thrower led by a PC with fewer units of the weapon's ammunition type than its consumption). Nothing is rolled or spent; the item, which the
    /// original took out of the thrower's hand, is not given back: it leaves the NPC (worn no more) and lies on the thrower's tile switched off (the field a script's toggle writes, <see cref="QueryItemOff"/>), in two revisions. The result is Changed with
    /// <see cref="SessionNpcThrowResult.AmmunitionShort"/>; the host's reaction (<see cref="AttackRefusal.OutOfAmmunition"/>) reads it from <see cref="SessionNpcThrowResult.Attack"/>'s resolution when the attack was made, else from the flag.
    /// </summary>
    private SessionNpcThrowResult NpcThrowRefusedForAmmunition(SessionNpcThrow request, SessionAttackResult? attack)
    {
        if (_revision >= long.MaxValue - 1) return new(GameSessionDisposition.Rejected, GameSessionFailure.RevisionExhausted, null, null, Version);
        var leaving = LandNpcItem(request.Item, request.ThrowerCell);
        if (leaving != GameSessionFailure.None) return new(GameSessionDisposition.Rejected, leaving, null, null, Version);
        var off = ApplyChanges(Version, null, [new(_actor.Map, SessionItemOff.Key(request.Item), 1)]);
        return new(GameSessionDisposition.Changed, off.Disposition == GameSessionDisposition.Rejected ? off.Failure : GameSessionFailure.None, attack, null, Version) { AmmunitionShort = true };
    }

    // ARC-COMBAT-2415: the ammunition a thrown weapon takes from an NPC led by a PC (0x4B65D0), and whether the thrower has too few units; null for a weapon without an ammunition type or consumption, or a thrower with no PC up its leader chain (it passes at once).
    private (SessionAmmoHolder Holder, int Type, int Count, bool Short)? NpcThrowAmmunition(string npc, CombatWeapon weapon) =>
        weapon.Kind == WeaponKind.Weapon && weapon.AmmoType is { } type && weapon.AmmoConsumption > 0 && PcLeads(npc)
            ? (new SessionAmmoHolder(npc), type, weapon.AmmoConsumption, type is < 0 or >= SessionAmmoRecord.AmmoTypes || (NpcStackOfType(npc, type)?.Units ?? 0) < weapon.AmmoConsumption) : null;

    /// <summary>
    /// ARC-DEV-258 (MIG13 S14n, GT63): an NPC throws a generic item of its list (a grenade the AI chose, the throw goal 37) at a location: the item
    /// leaves the NPC and lies hidden on the thrower's cell (a revision of its own, as <see cref="LandNpcItem"/>, no decay) while its shot flies to
    /// <see cref="SessionNpcThrowResult.Destination"/> as a projectile; the host lands it (<see cref="LandThrownItem"/>: where the projectile ends) or gives
    /// the flight up (<see cref="EndShot"/>: it stays on the thrower's cell). The thrown range is the item's weight under the thrower's effective strength and
    /// throwing training (<see cref="ThrowRules.Range"/>), the flight's range end. The item's own spell goes off where the flight ends
    /// (<see cref="QueryShotEndItemEvents"/>, the thrower as the event's parent). NOT READY: the throw dispatcher's shared check against a struck critter
    /// (a hit's weight damage, a miss's scatter, TT3) and the line of fire's redirect: the item flies to the location unchecked, and the attack event on
    /// the struck critter is not raised. An inactive thrower throws nothing (NoChange). Refused: the shot budget, the version, the revision, and the
    /// thrower's life record (<see cref="Resolve"/>).
    /// </summary>
    private SessionNpcThrowResult ThrowNpcGeneric(GameSessionVersion expected, SessionNpcThrow request)
    {
        SessionNpcThrowResult Refused(GameSessionFailure failure) => new(GameSessionDisposition.Rejected, failure, null, null, Version);
        var resolved = Resolve(request.Thrower.Target, out var record, out var facts, out _);
        if (resolved != GameSessionFailure.None) return Refused(resolved);
        var status = LifeRules.Evaluate(record.State, facts!);
        if (!status.IsActive) return new(GameSessionDisposition.NoChange, GameSessionFailure.None, null, null, Version);
        Combatant thrower;
        SkillTraining training;
        try
        {
            thrower = Levelled(request.Thrower).Combatant(status, record.State, _rules.Combat);
            training = Levelled(request.Thrower).SkillValues(CombatSkill.Throwing, record.State).Training;
        }
        catch (ArgumentException) { return Refused(GameSessionFailure.InvalidAttack); }
        if (ShotsInFlight > MaxShotsInFlight - 1) return Refused(GameSessionFailure.BudgetExceeded);
        if (!IsCurrent(expected)) return Refused(GameSessionFailure.StaleVersion);
        if (_revision == long.MaxValue) return Refused(GameSessionFailure.RevisionExhausted);
        var failure = LandNpcItem(request.Item, request.ThrowerCell);
        if (failure != GameSessionFailure.None) return Refused(failure);
        var range = ThrowRules.Range(thrower.Strength, training, request.Weight);
        var shot = new SessionShot(++_nextShot);
        _npcShots[shot] = new NpcShot(request.Thrower.Entity!, new SessionThrownShot(request.Item, range));
        return new(GameSessionDisposition.Changed, GameSessionFailure.None, null, null, Version) { Shots = [shot], Destination = request.Target };
    }

    // ARC-WORLD-113: the NPC's thrown seed item lies on the cell (the map change a critical's drop writes), worn no more; TW6: moved
    // there (0x4EDF20 → 0x43E770), not placed by the drop, so it settles into no junk pile and schedules no decay. One revision.
    private GameSessionFailure LandNpcItem(string item, CellCoord cell)
    {
        var written = ApplyChanges(Version, null, [new(_actor.Map, MapChangeKey.ForEntity(item, ItemDroppedProperty), DroppedValue(cell))]);
        if (written.Disposition == GameSessionDisposition.Rejected) return written.Failure;
        _inventory.Unwear(item);
        return GameSessionFailure.None;
    }

    /// <summary>
    /// ARC-WORLD-092: an NPC attacks the hero or another NPC once with its melee attack (<see cref="SessionCritterCombat.AttackValues"/>):
    /// the hit step and resolution take no test of whether the attacker is a PC (ledger EX rows of the execution study), so the steps
    /// are <see cref="AttackCritter"/>'s with the NPC as attacker: the resolution under the active rules and difficulty (rolls
    /// <c>combat.*</c>; the hero's armed "critical miss" fate turns it into a critical miss and is used up), the damage to the target, or to the attacker on a critical self-hit, then on an
    /// NPC target's hit-point damage the pool share, which the pool loses although only a PC attacker gets it (XP10), and on its death
    /// the kill award to the attacker's PC leader, else nothing and the worth stays (XP9), then the critical and wear steps with the NPC
    /// as attacker. Refused before any roll, in order: no hero (<see cref="GameSessionFailure.AttackUnavailable"/>), an attacker or
    /// target whose values do not hold, an attacker that attacks itself (<see cref="GameSessionFailure.InvalidAttack"/>), wear items
    /// that do not hold (<see cref="GameSessionFailure.InvalidWear"/>), a distance below 0 or beyond the reach, or a drop cell outside
    /// the drop's packing (<see cref="GameSessionFailure.InvalidAttack"/>), an attack other than melee
    /// (<see cref="GameSessionFailure.AttackUnsupported"/>), the version, the revision. A resolver refusal is NoChange with the
    /// resolution. Not modelled: the target's taking-damage and dying scripts, the attack event (the host's), backstab and the
    /// awareness the resolver reads (the target always sees the attacker) and the light (every position is lit). ARC-DEV-283: an NPC led by a PC spends its weapon's ammunition from its own stack (<see cref="SessionAttackResult.Ammunition"/>).
    /// </summary>
    public SessionAttackResult AttackByNpc(GameSessionVersion expected, SessionNpcAttack attack) => NpcAttack(expected, attack, null, out _, null);

    /// <summary>
    /// ARC-DEV-282 (PF2, PF3): the strike of an NPC's shot in flight (<see cref="SessionNpcAttack.Flight"/>) on <see cref="SessionNpcAttack.Target"/> (an NPC, or the
    /// hero when null): the damage of the attack decided at the shot, now. The shooter's weapon, skill and Combatant are the shot's (a shooter that died or lost its
    /// weapon since still strikes); the target is the one the projectile reached. The shot's own target takes the carried hit; another object it meets on the way (a stray
    /// strike, PF3) takes a forced hit with the shot's weapon magic. A missed shot meeting its own target is refused (<see cref="GameSessionFailure.InvalidAttack"/>), as a
    /// shot not in flight, one the hero shot, and a shooter other than the shot's. The shot ends with the strike. Distance 1, no cover, no line, as the hero's strike.
    /// Refused as <see cref="AttackByNpc"/> is.
    /// </summary>
    public SessionAttackResult LandNpcShot(GameSessionVersion expected, SessionShot shot, SessionNpcAttack attack) => NpcAttack(expected, attack, null, out _, shot);

    /// <summary>ARC-DEV-282: an NPC's arrow or bullet in flight: the shooter, the attack values and the check decided at the shot, whether it hit, and its target (an NPC's entity; a marker for the hero).</summary>
    private sealed record NpcArrow(string Shooter, SessionNpcAttackValues Values, AttackResolution Check, bool Hit, string Target, string? WeaponItem = null)
    {
        /// <summary>ARC-COMBAT-1717: the thrown weapon's projectile art (not saved: the saved flight carries the art the projectile flies with).</summary>
        public uint? ThrownArt { get; init; }
    }

    private readonly Dictionary<SessionShot, NpcArrow> _npcArrows = [];

    /// <summary>ARC-DEV-282: an NPC's shot in flight as the projectile needs it: whether the attack decided at the shot hit, and the weapon's range that ends the flight (PF9). Null for a shot not an NPC arrow's.</summary>
    public SessionNpcShotFacts? QueryNpcShot(SessionShot shot)
    {
        ThrowIfDisposed();
        return _npcArrows.TryGetValue(shot, out var arrow) ? new(arrow.Shooter, arrow.Hit, arrow.Values.Weapon.Range, arrow.Target) { ThrownArt = arrow.ThrownArt } : null;
    }

    private const string HeroArrowTarget = SessionNpcShotFacts.HeroTarget;

    // The NPC's attack; ARC-WORLD-113: a throw (<paramref name="throwing"/>) checks with the throwing skill and thrown range and lands
    // its item (<paramref name="landed"/>). ARC-DEV-282: <paramref name="strike"/> is the NPC's shot in flight whose strike this is.
    private SessionAttackResult NpcAttack(GameSessionVersion expected, SessionNpcAttack attack, NpcThrowing? throwing, out CellCoord? landed, SessionShot? strike)
    {
        landed = null;
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(attack);
        ArgumentNullException.ThrowIfNull(attack.Attacker);
        if (_hero is null) return RejectAttack(GameSessionFailure.AttackUnavailable);
        var attacker = attack.Attacker;
        var resolved = Resolve(attacker.Target, out var attackerRecord, out var attackerFacts, out _);
        if (resolved != GameSessionFailure.None) return RejectAttack(resolved);
        var target = attack.Target;
        var targetLife = target?.Target ?? SessionLifeTarget.Hero;
        resolved = Resolve(targetLife, out var targetRecord, out var targetFacts, out _);
        if (resolved != GameSessionFailure.None) return RejectAttack(resolved);
        if (target is not null && string.Equals(target.Entity, attacker.Entity, StringComparison.Ordinal)) return RejectAttack(GameSessionFailure.InvalidAttack);
        var attackerSide = new SessionWearSide { Life = attacker.Target, Worn = attack.AttackerWorn };
        var targetSide = new SessionWearSide { Life = targetLife, Worn = attack.TargetWorn };
        if (!ValidWear(attackerSide, targetSide, targetBroken: false, wear: null)) return RejectAttack(GameSessionFailure.InvalidWear);

        var attackerStatus = LifeRules.Evaluate(attackerRecord.State, attackerFacts!);
        // ARC-DEV-282: the strike of an NPC's shot in flight: the shot is the NPC's, its target is not the shooter itself and a missed shot does not strike its own target.
        NpcArrow? arrow = null;
        var targetKey = target?.Entity ?? HeroArrowTarget;
        if (strike is { } shotId && (!_npcArrows.TryGetValue(shotId, out arrow) || arrow.Shooter != attacker.Entity || !arrow.Hit && arrow.Target == targetKey))
            return RejectAttack(GameSessionFailure.InvalidAttack);
        SessionNpcAttackValues values;
        // The items the host says the NPC wears now: a weapon a critical dropped or destroyed no longer strikes.
        if (arrow is not null) values = arrow.Values;
        else
        {
            try { values = Levelled(attacker).AttackValues(attackerStatus, attackerRecord.State, _rules.Combat, attack.AttackerWorn?.Keys); }
            catch (ArgumentException) { return RejectAttack(GameSessionFailure.InvalidAttack); }
        }
        if (attack.Distance < 0 || throwing is null && arrow is null && attack.Distance > values.Range || attack.AttackerCell is { } from && !DropCellFits(from) ||
            attack.TargetCell is { } to && !DropCellFits(to) || attack.LineOfFire is { Cost: < 0 or > AttackResolver.MaxMagnitude }) return RejectAttack(GameSessionFailure.InvalidAttack);
        if (!values.Supported) return RejectAttack(GameSessionFailure.AttackUnsupported);
        // ARC-MAGIC-1188: the parent hit's melee bit reads the weapon's reach (0x4CBAD0, 0x465E30), which a throw keeps (its thrown range is the check's only).
        var reach = values.Weapon.Kind == WeaponKind.Unarmed ? (int?)null : values.Range;
        if (throwing is not null)
        {
            // TW7, ARC-COMBAT-2699 (TW8, 0x42AB90 asks nothing of the weapon's skill, the check is the throwing skill's): any wielded weapon the attack supports (a melee, bow or firearms one) is thrown, so a bow or firearm
            // reaches the ammunition check too; a throwing-skill weapon (a boomerang) is not a supported NPC attack and is refused with the others.
            if (values.Weapon.Kind != WeaponKind.Weapon) return RejectAttack(GameSessionFailure.AttackUnsupported);
            var (level, training) = Levelled(attacker).SkillValues(CombatSkill.Throwing, attackerRecord.State);
            values = values with
            {
                Skill = CombatSkill.Throwing, SkillLevel = level, Training = training,
                Weapon = values.Weapon with { Range = ThrowRules.Range(values.Combatant.Strength, training, throwing.Weight) },
            };
        }

        var targetStatus = LifeRules.Evaluate(targetRecord.State, targetFacts!);
        Combatant defender;
        if (target is null) defender = QueryHeroAttack()!.Combatant;
        else
        {
            try { defender = Levelled(target).Combatant(targetStatus, targetRecord.State, _rules.Combat); }
            catch (ArgumentException) { return RejectAttack(GameSessionFailure.InvalidAttack); }
            // An earlier critical may have dropped or destroyed some of its map data's items (ARC-COMBAT-009).
            if (attack.TargetWorn is { } targetWorn) defender = defender with { Worn = CombatantRules.WornItems(targetWorn.Keys) };
        }
        if (!IsCurrent(expected)) return RejectAttack(GameSessionFailure.StaleVersion);
        // The fate may take a revision before the damage; the ammunition's spend or a throw's landing another.
        if (_revision >= long.MaxValue - 2) return RejectAttack(GameSessionFailure.RevisionExhausted);
        var weapon = values.Weapon;
        // The weapon's art subtype is item data, passed with its baseline, as for the hero's.
        if (arrow is null && attack.AttackerWorn?.TryGetValue(EquipmentSlot.Weapon, out var wielded) == true && weapon.Kind == WeaponKind.Weapon)
            weapon = weapon with { ArtSubtype = wielded.ArtSubtype };
        // ARC-DEV-282 (PF1): a shot on the ranged path with a weapon that has a projectile art flies, and the hit's damage waits for the strike.
        var flies = arrow is null && throwing is null && attack.Flight && values.Range > 1 && weapon.Kind == WeaponKind.Weapon
            && attack.AttackerWorn?.GetValueOrDefault(EquipmentSlot.Weapon)?.MissileArt is not null;
        var projectiles = flies ? ProjectileRules.Projectiles(values.Skill, values.Training) : 0;
        // A full shot budget (the hero's, the NPCs' thrown items and arrows share it) lands the attack at once, as ARC-DEV-273's.
        if (flies && ShotsInFlight > MaxShotsInFlight - projectiles) { flies = false; projectiles = 0; }
        // ARC-COMBAT-1717 (PF1, 0x4B3C00 → 0x4B3170): a thrown weapon on a host that can fly it is one projectile whose hit waits for the strike; a full shot budget lands the throw at once as before.
        var throwFlies = arrow is null && throwing is not null && attack.Flight && ShotsInFlight <= MaxShotsInFlight - 1;
        if (throwFlies) { flies = true; projectiles = 1; }
        // ARC-DEV-283 (RA3, AM4, 0x4B65D0): a shot of an NPC led by a PC (a companion) spends its weapon's ammunition from the NPC's own stack, right after the location roll and before every
        // other roll, all or nothing; too few units refuse the attack (the resolver's out-of-ammunition refusal). An NPC with no PC leader spends nothing. Spent at the shot, not at the strike. ARC-COMBAT-2415 (TW8): a thrown weapon is checked the same (ThrowNpcPlanned leaves the refused one on the thrower's tile).
        var ammunition = arrow is null && weapon.Kind == WeaponKind.Weapon && weapon.AmmoType is { } ammoType && weapon.AmmoConsumption > 0 && PcLeads(attacker.Entity!)
            ? (Holder: new SessionAmmoHolder(attacker.Entity), Type: ammoType, Count: weapon.AmmoConsumption) : ((SessionAmmoHolder Holder, int Type, int Count)?)null;
        var unsupplied = ammunition is { } need && (need.Type is < 0 or >= SessionAmmoRecord.AmmoTypes || (NpcStackOfType(attacker.Entity!, need.Type)?.Units ?? 0) < need.Count);
        var striker = values.Combatant;
        var request = new AttackRequest
        {
            Attacker = striker,
            Target = defender,
            Weapon = weapon,
            Skill = values.Skill,
            SkillLevel = values.SkillLevel,
            Training = values.Training,
            Distance = arrow is null ? attack.Distance : 1,
            CoverRating = arrow is not null ? 0 : throwing?.Line?.Cost ?? (values.Range > 1 ? attack.LineOfFire?.Cost : null) ?? 0,
            LineOfFireBlocked = arrow is null && (throwing?.Line?.Blocked ?? (values.Range > 1 && (attack.LineOfFire?.Blocked ?? false))),
            Difficulty = _difficulty,
            DeferHit = flies,
            AmmunitionShort = unsupplied,
            // ARC-COMBAT-1929: a melee strike (not a shot's landing or a throw) may be a backstab.
            Backstab = arrow is null && throwing is null ? BackstabOf(values.Combatant, values.Skill, weapon, attack.Backstab) : BackstabKind.None,
            TargetWasInBed = attack.TargetWasInBed,
        };
        var rolls = new TrialCombatRolls(new SeededRandom(_random.Seed, _random.Draws));
        AttackResolution resolution;
        int? scatterRotation = null;
        try
        {
            resolution = arrow is null ? AttackResolver.Resolve(request, rolls, OriginalCriticalEffects.Instance, _rules.Combat)
                : AttackResolver.LandHit(request, arrow.Hit && arrow.Target == targetKey ? arrow.Check : AttackResolver.ForcedHit(arrow.Check.WeaponMagicCancelled),
                    rolls, OriginalCriticalEffects.Instance, _rules.Combat);
            // ARC-WORLD-113: a missed throw's direction is drawn after the dispatcher's decisions (0x4B39B0, 0x4CEF00(0, 7)), as the hero's.
            if (throwing is not null && resolution.Refusal == AttackRefusal.None && resolution.Outcome is not (AttackOutcome.Hit or AttackOutcome.CriticalHit))
                scatterRotation = rolls.Roll(CombatRollPurpose.ThrowScatter, 0, ThrowRules.ScatterDirections - 1);
        }
        catch (TrialCombatRolls.ExhaustedException) { return RejectAttack(GameSessionFailure.RandomExhausted); }
        catch (ArgumentException) { return RejectAttack(GameSessionFailure.InvalidAttack); }
        _random = rolls.Trial;
        // ARC-DEV-282: the shot ends with its strike (the hero's EndStrike); a shot that flies is in flight from now on, hit or miss, as the hero's (ARC-COMBAT-018: two for a bow at expert).
        if (strike is { } strikeShot) _npcArrows.Remove(strikeShot);
        // The hero's armed "critical miss" fate turns an NPC's attack into a critical miss (FT6, 0x4B3170): cleared at once, in a
        // revision of its own before the damage steps, as for the hero's own fates.
        if (resolution.Fate != FateUse.None && arrow is null)
        {
            ClearUsedAttackFate(resolution.Fate);
            Revise();
        }
        // ARC-DEV-283: the shot's ammunition goes for a hit or a miss (the original takes it before every roll), once per attack (a bow at expert's second projectile is not charged again).
        SessionAmmoUse? spentAmmo = null;
        if (ammunition is { } spend && resolution.Refusal == AttackRefusal.None)
        {
            spentAmmo = SpendAmmo(Version, spend.Holder, spend.Type, spend.Count);
            if (spentAmmo is { Disposition: GameSessionDisposition.Rejected } refused)
                return new(GameSessionDisposition.Changed, GameSessionFailure.None, resolution, null, null, null, null, refused.Failure, Version) { Ammunition = refused };
        }
        var spentNow = spentAmmo is { Disposition: GameSessionDisposition.Changed };
        // A shot that flies is in flight from now on, hit or miss, after its ammunition went (the hero's order); a rejected spend leaves none.
        IReadOnlyList<SessionShot> flying = [];
        CellCoord? scatter = null;
        if (throwFlies && resolution.Refusal == AttackRefusal.None)
        {
            // ARC-COMBAT-1717 (0x4B3C00: 0x4415C0, no decay): the thrown weapon leaves the NPC's hand and lies hidden on its cell while it flies; a miss's scatter tile is the flight's destination.
            scatter = scatterRotation is { } rotation ? ScatterThrow(throwing!.Target, values.SkillLevel, rotation, attack.DropMap, throwing.From) : null;
            var leaving = LandNpcItem(throwing!.Item, throwing.From);
            if (leaving != GameSessionFailure.None) return new(GameSessionDisposition.Changed, GameSessionFailure.None, resolution, null, null, null, null, leaving, Version);
            attackerSide = attackerSide with { Worn = attack.AttackerWorn?.Where(pair => pair.Key != EquipmentSlot.Weapon).ToDictionary(pair => pair.Key, pair => pair.Value) };
        }
        if (flies && resolution.Refusal == AttackRefusal.None)
        {
            var carried = new NpcArrow(attacker.Entity!, values with { Weapon = weapon }, resolution,
                resolution.Outcome is AttackOutcome.Hit or AttackOutcome.CriticalHit, targetKey,
                throwFlies ? throwing!.Item : attack.AttackerWorn?.GetValueOrDefault(EquipmentSlot.Weapon)?.Entity)
            {
                ThrownArt = throwFlies ? attack.AttackerWorn?.GetValueOrDefault(EquipmentSlot.Weapon)?.MissileArt : null,
            };
            var shots = new SessionShot[projectiles];
            for (var i = 0; i < projectiles; i++)
            {
                shots[i] = new SessionShot(++_nextShot);
                _npcArrows.Add(shots[i], carried);
                if (throwFlies) _npcShots[shots[i]] = new NpcShot(attacker.Entity!, new SessionThrownShot(throwing!.Item, values.Weapon.Range));
            }
            flying = shots;
        }
        // ARC-WORLD-113: the thrown item leaves the NPC's hand and lands at once: on the struck critter's cell for a hit, else on its
        // scattered tile; the thrower's later steps find its hand empty.
        var thrown = throwing is not null && resolution.Refusal == AttackRefusal.None;
        if (thrown && !throwFlies)
        {
            landed = scatterRotation is { } rotation
                ? ScatterThrow(throwing!.Target, values.SkillLevel, rotation, attack.DropMap, throwing.From) : throwing!.Struck;
            var landing = LandNpcItem(throwing.Item, landed.Value);
            if (landing != GameSessionFailure.None)
            {
                landed = null;
                return new(GameSessionDisposition.Changed, GameSessionFailure.None, resolution, null, null, null, null, landing, Version);
            }
            attackerSide = attackerSide with { Worn = attack.AttackerWorn?.Where(pair => pair.Key != EquipmentSlot.Weapon).ToDictionary(pair => pair.Key, pair => pair.Value) };
        }
        // ARC-SCRIPT-1483 (CP1–CP3): the hit's combat-point scripts run before the damage step; a shot that flies asks at its strike.
        if (resolution.Refusal == AttackRefusal.None && !flies && (resolution.Outcome is AttackOutcome.Hit or AttackOutcome.CriticalHit))
            attack.HitScripts?.Invoke(new(arrow?.WeaponItem ?? throwing?.Item ?? attack.AttackerWorn?.GetValueOrDefault(EquipmentSlot.Weapon)?.Entity, resolution.Outcome == AttackOutcome.CriticalHit, arrow is not null || throwing is not null, throwing is not null || strike is { } landing && _npcShots.ContainsKey(landing)));
        // ARC-DEV-261 (GT198): a prevented damage asks the victim's script too (it comes before the invulnerable gate); its answer cannot change the outcome.
        if (resolution.Refusal == AttackRefusal.None && resolution.Damage is { Prevented: not DamagePrevention.None } blocked) attack.TakingDamage?.Invoke(blocked.Recipient);
        if (resolution.Refusal != AttackRefusal.None || LifeDamage.FromAttack(resolution) is not { } damage)
            return new(resolution.Fate != FateUse.None || thrown || spentNow ? GameSessionDisposition.Changed : GameSessionDisposition.NoChange, GameSessionFailure.None,
                resolution, null, null, null, null, GameSessionFailure.None, Version) { Shots = flying, Ammunition = spentAmmo, Scatter = scatter };

        var recipient = resolution.Damage!.Recipient;
        var struck = recipient == DamageRecipient.Target ? target : attacker;
        // ARC-DEV-261 (GT198–GT199): the victim's taking-damage script comes first in the damage apply; a veto ends the whole step.
        if (attack.TakingDamage is { } asks && !asks(recipient))
            return new(resolution.Fate != FateUse.None || thrown || spentNow ? GameSessionDisposition.Changed : GameSessionDisposition.NoChange, GameSessionFailure.None, resolution, recipient, null, null, null, GameSessionFailure.None, Version) { Vetoed = true, Shots = flying, Ammunition = spentAmmo, Scatter = scatter };
        // ARC-DEV-312: the attacker is the killer of a target it kills (a critical self-hit has none).
        var applied = ApplyDamage(Version, recipient == DamageRecipient.Target ? targetLife : attacker.Target, damage, killer: recipient == DamageRecipient.Target ? attacker.Entity : null);
        if (applied.Disposition == GameSessionDisposition.Rejected)
            return new(GameSessionDisposition.Changed, GameSessionFailure.None, resolution, recipient, null, null, null, applied.Failure, Version) { Shots = flying, Ammunition = spentAmmo, Scatter = scatter };
        var outcome = applied.Outcome!;
        SessionExperienceAward? killAward = null;
        var stepFailure = GameSessionFailure.None;
        if (struck is not null && _experience.Rules is not null)
        {
            var struckFacts = recipient == DamageRecipient.Target ? targetFacts! : attackerFacts!;
            var struckStatus = recipient == DamageRecipient.Target ? targetStatus : attackerStatus;
            // XP10: the pool loses the share whoever attacked; only a PC attacker gets it (a critical self-hit has none).
            if (recipient == DamageRecipient.Target && outcome.HitPointDamage > 0 && !struckFacts.Traits.HasFlag(LifeTraits.Stoned))
            {
                var hit = RecordNpcDamage(Version, struck.Experience, Math.Min(outcome.HitPointDamage, LifeLimits.MaxAmount),
                    Math.Clamp(struckStatus.MaxHitPoints, 1, LifeLimits.MaxAmount), byHero: false);
                if (hit.Disposition == GameSessionDisposition.Rejected) stepFailure = hit.Failure;
            }
            // XP9: the killer's PC leader gets the kill award; a self-hit has no killer.
            if (stepFailure == GameSessionFailure.None && recipient == DamageRecipient.Target && outcome.Events.HasFlag(LifeEvents.Died) && !applied.DeathStopped)
            {
                var kill = RecordNpcKill(Version, struck.Experience, creditsHero: PcLeads(attacker.Entity));
                killAward = kill.Award;
                if (kill.Disposition == GameSessionDisposition.Rejected) stepFailure = kill.Failure;
            }
        }
        // ARC-UI-097: the hero's kill and injury log. A death the hero's follower dealt is a kill of the hero (0x4F5330, the killer's PC leader);
        // a hit that scars, blinds or cripples the hero is an injury (0x4F5690) whose cause is the attacker as the hero knows it.
        if (stepFailure == GameSessionFailure.None && recipient == DamageRecipient.Target && struck is not null && outcome.Events.HasFlag(LifeEvents.Died) && !applied.DeathStopped
            && PcLeads(attacker.Entity))
            stepFailure = RecordKillLog(Levelled(struck));
        else if (stepFailure == GameSessionFailure.None && recipient == DamageRecipient.Target && struck is null)
            stepFailure = RecordHeroInjuries(outcome.Events, AttackerCause(attacker));
        SessionAttackWearResult? wear = null;
        var criticals = new List<SessionCriticalStep>();
        // ARC-MAGIC-1188 (IC2, 0x4B4390 serves any attacker): the damage step's item events of an attack on the target, in the hero's order (ARC-MAGIC-023): the parties are the NPC (the attacker
        // and the kill's damage source) and its victim, the hero (its items as the session wears them) or another NPC; a critical self-hit fires none (its life events are the caller's).
        var itemSteps = recipient == DamageRecipient.Target && stepFailure == GameSessionFailure.None && attack.ItemTriggers is not null
            ? ItemSpellTriggerRules.DamageStep(targetFacts!, damage, outcome, attacker: true, killer: true, ItemSpellTriggerRules.MeleeHit(attackEntry: true, reach))
            : [];
        var victim = target?.Entity ?? SessionEventKinds.PlayerEntity;
        var attackerWorn = attackerSide.Worn?.OrderBy(pair => pair.Key).Select(pair => pair.Value.Entity).ToArray() ?? [];
        var victimWorn = target is null ? [.. _equipment.Worn.Values.OrderBy(item => item.Slot).Select(item => item.Entity)]
            : attack.TargetWorn?.OrderBy(pair => pair.Key).Select(pair => pair.Value.Entity).ToArray() ?? [];
        // The weapon of the event's context: the thrown item, an arrow's weapon at the shot (as the hero's pending shot; a flight restored from a save does not carry it), else the wielded one.
        var contextWeapon = throwing?.Item ?? (arrow?.WeaponItem ?? attack.AttackerWorn?.GetValueOrDefault(EquipmentSlot.Weapon)?.Entity);
        var itemSpells = FireDamageStepItems(attack.ItemTriggers, itemSteps.Where(step => BeforeItemSteps(step, outcome)), victim, victimWorn,
            attacker.Entity, attackerWorn, contextWeapon).ToList();
        if (stepFailure == GameSessionFailure.None)
            (wear, stepFailure) = CriticalAndWearSteps(resolution, outcome, attackerSide, targetSide, struck is null ? null : Levelled(struck),
                recipient == DamageRecipient.Target ? attack.TargetCell : attack.AttackerCell, attack.DropMap, criticals, (values.Weapon.AmmoType, values.Weapon.AmmoConsumption));
        if (stepFailure == GameSessionFailure.None)
            itemSpells.AddRange(FireDamageStepItems(attack.ItemTriggers, itemSteps.Where(step => !BeforeItemSteps(step, outcome)), victim,
                StillWorn(victimWorn, wear, criticals), attacker.Entity, StillWorn(attackerWorn, wear, criticals, DamageRecipient.Attacker), contextWeapon));
        return new(GameSessionDisposition.Changed, GameSessionFailure.None, resolution, recipient, outcome, null, killAward, stepFailure, Version)
        {
            Wear = wear,
            Criticals = criticals,
            Ammunition = spentAmmo,
            Shots = flying,
            Scatter = scatter,
            ItemSpells = itemSpells,
        };
    }

    // A follower the session levelled fights with its saved sheet's skills and level (ARC-SKILLS-002), and any NPC under its effect list (ARC-MAGIC-027).
    private SessionCritterCombat Levelled(SessionCritterCombat npc) =>
        WithNpcEffects(_advancement.Npcs.TryGetValue(npc.Entity, out var levelled) ? npc.WithSheet(levelled.Sheet) : npc);

    /// <summary>
    /// ARC-MAGIC-027 (MA-2): the NPC's combat values under its effect list, the session's else the loaded one (<see cref="QueryNpcEffects"/>): the stat changes of the entries reach its stats
    /// (speed, strength and the rest: <see cref="SessionCritterCombat.EffectiveStat"/>, the turn-based action points, the attack pause) and the resistance changes its effective
    /// resistances (0x43D6D0). The NPC itself when its list is empty. Hosts that read an NPC's combat values for the round or a spell call it first.
    /// </summary>
    public SessionCritterCombat WithNpcEffects(SessionCritterCombat npc)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(npc);
        // ARC-DEV-358: the body spell its spell flags hold (the session's written flags) feeds its unarmed attacks.
        npc = npc.WithUnarmedBody(UnarmedBodyOf(npc.Entity, npc.DataSpellFlags));
        // ARC-COMBAT-1832: the invulnerable flag a script's toggle wrote (the written object flags over the data's) reaches the combatant the damage step reads.
        if (QueryObjectFlags(npc.Entity) is { IsEmpty: false } written)
            npc = npc.WithInvulnerable((written.Object(npc.Facts.Traits.HasFlag(LifeTraits.Invulnerable) ? InvulnerableObjectFlag : 0) & InvulnerableObjectFlag) != 0);
        // ARC-MAGIC-056 (MA-9): the speed change of Tempus Fugit, from the spell flags held now.
        npc = npc.WithSpeedShift(TempusFugitShiftOf(npc.Entity, npc.DataSpellFlags));
        // No session list and no host rules for the loaded one: its entries have no changes, nothing to apply (the common case, kept cheap).
        if (!_effects.Critters.ContainsKey(npc.Entity) && NpcEffectRules is null) return npc;
        if (QueryNpcEffects(npc.Target) is not { Entries.Count: > 0 } view) return npc;
        return npc.WithEffects(view.Entries, view.Definitions.ToDictionary(definition => definition.Number));
    }

    /// <summary>
    /// ARC-MAGIC-056 (MA-9, ledger XR6, XR14 (a)): the speed change of Tempus Fugit for a critter, over <paramref name="dataSpellFlags"/> (the flags its map data stores, 0 when unknown) with the
    /// session's written flags: +10 when the critter holds the spell, -10 when it does not but some critter's written flags hold it, else 0 (a bit that only a critter's map data stores starts nothing) (<see cref="CharacterStatRules.TempusFugitShift"/>).
    /// </summary>
    public int TempusFugitShiftOf(string entity, int dataSpellFlags)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(entity);
        var holds = (QueryObjectFlags(entity).Spell(dataSpellFlags) & TempusFugitSpell) != 0;
        return CharacterStatRules.TempusFugitShift(holds, AnyHoldsTempusFugit());
    }

    /// <summary>ARC-MAGIC-056: the speed change of Tempus Fugit for the hero (<see cref="TempusFugitShiftOf"/> for the hero's written flags; the hero has no stored spell flags).</summary>
    public int HeroTempusFugitShift() => TempusFugitShiftOf(SessionSpellCaster.Hero, 0);

    // Whether any critter's written spell flags hold Tempus Fugit: the hero (the original's only test, 0x458A80) or an NPC that cast it or was handed it (the owner's fix of XR14 (a)).
    // The answer is kept for the dictionary it was worked out from (the dictionary is immutable and replaced by every write), so a scene of many NPC builds scans it once per change.
    private bool AnyHoldsTempusFugit()
    {
        var flagsNow = _objectFlags;
        if (ReferenceEquals(flagsNow, _tempusFlagsScanned)) return _tempusAnyHolds;
        var any = false;
        foreach (var flags in flagsNow.Values)
            if ((flags.Spell(0) & TempusFugitSpell) != 0) { any = true; break; }
        (_tempusFlagsScanned, _tempusAnyHolds) = (flagsNow, any);
        return any;
    }

    private object? _tempusFlagsScanned;
    private bool _tempusAnyHolds;

    // ARC-MAGIC-1190 (XR7, TF3): the join tail of 0x45DE90 copies the leader's Tempus Fugit flag by a plain write (no run, no count): a critter that joins a leader holding it holds it from then on.
    // ARC-WORLD-1840: the leader is any critter, the hero by default; an NPC leader's flag is read from the session's written flags (a bit only its map data stores is not seen, TF3).
    // An entity the written flags do not cover (not a mobile object) needs no write.
    private bool JoinCopiesTempusFugit(string follower, string leader = SessionSpellCaster.Hero) => ObjectFlagEntity(follower)
        && (_objectFlags.GetValueOrDefault(leader).Spell(0) & TempusFugitSpell) != 0;

    private void WriteTempusFugitOnJoin(string follower) =>
        _objectFlags = _objectFlags.SetItem(follower, _objectFlags.GetValueOrDefault(follower).With(SessionObjectFlagField.Spell, TempusFugitSpell, true));

    // ARC-MAGIC-1190 (XR7, TF3): every leave (0x45E040 through 0x459EA0, before the entry leaves the follower array) clears the leaver's Tempus Fugit flag, whatever the leader holds. A wait is a leave too
    // (0x4AA7A0 calls 0x45DFC0 and restores the leader after), and so is the first half of every join of a critter that has a leader. Only a flag the session wrote is cleared: a bit that only the NPC's map data stores is not seen here (TF3).
    // The clear is a write over the data, so it leaves a cleared bit behind. The caller's revision covers it.
    private void ClearTempusFugitOnLeave(string follower)
    {
        if (!_objectFlags.TryGetValue(follower, out var old) || (old.Spell(0) & TempusFugitSpell) == 0) return;
        _objectFlags = _objectFlags.SetItem(follower, old.With(SessionObjectFlagField.Spell, TempusFugitSpell, false));
    }

    /// <summary>
    /// ARC-DEV-358 (MA-1; the unarmed damage block 0x4B67F1 of the original, in this order): the body spell a critter's spell flags hold: Body of Earth (+5 normal damage), else Fire (+15 fire), else
    /// Water (+15 fatigue), else Hardened Hands (+2 normal), over <paramref name="dataSpellFlags"/> (the flags the critter's map data stores, 0 when the host does not know them) with the session's
    /// written flags (<see cref="QueryObjectFlags"/>). The damage rule itself is the resolver's (<see cref="UnarmedBodySpell"/>).
    /// </summary>
    public UnarmedBodySpell UnarmedBodyOf(string entity, int dataSpellFlags)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(entity);
        var spell = QueryObjectFlags(entity).Spell(dataSpellFlags);
        return (spell & BodyOfEarthSpell) != 0 ? UnarmedBodySpell.BodyOfEarth : (spell & BodyOfFireSpell) != 0 ? UnarmedBodySpell.BodyOfFire
            : (spell & BodyOfWaterSpell) != 0 ? UnarmedBodySpell.BodyOfWater : (spell & HardenedHandsSpell) != 0 ? UnarmedBodySpell.HardenedHands : UnarmedBodySpell.None;
    }

    private const int BodyOfEarthSpell = 0x8, BodyOfFireSpell = 0x10, BodyOfWaterSpell = 0x20, HardenedHandsSpell = unchecked((int)0x80000000), TempusFugitSpell = 0x4000000;
}
