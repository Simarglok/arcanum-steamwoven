using Arcanum.Simulation;
using Arcanum.Simulation.Combat;

namespace Arcanum.Session;

/// <summary>ARC-COMBAT-016: a shot of the hero in flight (a projectile, ledger PF rows), named by the session for its strike or its end.</summary>
public readonly record struct SessionShot(long Id);

/// <summary>
/// ARC-COMBAT-017: the host's facts of the wielded weapon when it carries the "default throws" flag (weapon flag 0x200, ledger RH2):
/// its effective weight (0x461370: the stored weight plus its magic adjustment), which sets the thrown range (<see cref="ThrowRules.Range"/>,
/// RH4). The session keeps no item weights (the inventory weight is ARC-WORLD-085's), so the host reads it from the item's data.
/// </summary>
public sealed record SessionThrow(int Weight)
{
    /// <summary>ARC-COMBAT-018: the throw's target location (the throw goal's, TW1: the clicked target's tile at the shot, which a
    /// line-of-fire redirect does not change, PF2), from which a missed throw scatters (RH2); null: the attack's
    /// <see cref="SessionAttack.TargetCell"/>.</summary>
    public Arcanum.Simulation.Movement.CellCoord? Target { get; init; }
}

/// <summary>ARC-COMBAT-017: the thrown item of a shot in flight (<see cref="Item"/>, its entity) and the thrown range at which its
/// flight ends (<see cref="Range"/>, ledger PF9).</summary>
public readonly record struct SessionThrownShot(string Item, int Range);

/// <summary>
/// ARC-COMBAT-016: the hero's shots in flight. A shot that flies (<see cref="SessionAttack.Flight"/>,
/// <see cref="SessionObjectAttack.Flight"/>) is decided at once, as the original's dispatcher decides it (the check, the dodge, the
/// fate, the ammunition and a critical miss's turn on the hero), and its projectile carries the outcome until it strikes
/// (<see cref="LandShot"/>, <see cref="LandShotOnObject"/>) or its flight ends (<see cref="EndShot"/>): only then is a hit's damage
/// rolled and applied. ARC-DEV-288 (schema 80): the shots in flight are saved with their projectiles (the <c>flights</c> section), as the
/// original saves its projectile objects with their fields 77–80 (SV5, SV6): a restored session goes on with them.
/// ARC-COMBAT-017: a thrown weapon (<see cref="SessionAttack.Throw"/>) leaves the hero at the shot and lies hidden on the hero's cell
/// while it flies (0x4B3C00: 0x4415C0, no decay); its shot ends when the item lands (<see cref="LandThrownItem"/>, after its strike
/// if it struck) or when the host gives the flight up (<see cref="EndShot"/>: the item stays on the hero's cell).
/// ARC-COMBAT-019: a boomerang (a throwing-skill weapon, weapon flag 0x40) stays in the hand; its projectile marks the hero (field
/// 219 bit 0x4, <see cref="QueryBoomerangInFlight"/>: no attack starts meanwhile), turns back at its first end (its strike, or
/// <see cref="ReturnShot"/> at the end of its range or the destination of a shot without a target) and flies back striking nothing,
/// until its flight ends at the tile it flies back to (<see cref="EndShot"/>, which clears the mark). The mark is the shot's, saved with it.
/// </summary>
public sealed partial class GameSessionOwner
{
    /// <summary>The most shots in flight at once (a shot flies as one projectile, a bow's at expert training as two, ARC-COMBAT-018).</summary>
    public const int MaxShotsInFlight = 16;

    /// <summary>ARC-DEV-288: what of the hero's attack a shot carries to its strike (the projectile's parent weapon, field 0x50, the skill it was shot with and the weapon's reach,
    /// which the strike's item events read); the strike takes the hero's own combatant as it is then.</summary>
    private sealed record ShotFacts(CombatWeapon Weapon, CombatSkill Skill, int SkillLevel, SkillTraining Training, int Range)
    {
        public static ShotFacts Of(SessionHeroAttack hero) => new(hero.Weapon, hero.Skill, hero.SkillLevel, hero.Training, hero.Range);
    }

    /// <summary>A shot in flight: the hero's weapon and skill at the shot (the projectile's parent weapon, field 0x50), the decided
    /// check, whether it hit, and its target: an NPC's entity, or a non-critter object's key (<see cref="ShotObjectKey"/>).</summary>
    private sealed record PendingShot(ShotFacts Facts, AttackResolution Check, bool Hit, string? Entity, string? Object)
    {
        /// <summary>ARC-COMBAT-017: the thrown item and its thrown range; null for a shot of a weapon that stays in the hand.</summary>
        public SessionThrownShot? Thrown { get; init; }
        /// <summary>ARC-MAGIC-023: the projectile's item (field 0x50, the strike's context weapon): the thrown item, or the weapon the
        /// hero wielded at the shot; null bare-handed.</summary>
        public string? WeaponItem { get; init; }
        /// <summary>ARC-COMBAT-017: the thrown shot struck already and waits for its item's landing.</summary>
        public bool Struck { get; init; }
        /// <summary>ARC-COMBAT-019: the shot of a boomerang that stays in the hand (attack flag 0x1000, 0x4B24F0).</summary>
        public bool Boomerang { get; init; }
        /// <summary>ARC-COMBAT-019: the boomerang turned back (flag 0x2000) and strikes nothing more.</summary>
        public bool Returning { get; init; }
    }

    private readonly Dictionary<SessionShot, PendingShot> _shots = [];
    private long _nextShot;

    /// <summary>ARC-DEV-258 (MIG13 S14n): an NPC's thrown generic item in flight (a grenade): the thrower's entity and the thrown item with its range. Saved with the shots (ARC-DEV-288).</summary>
    private sealed record NpcShot(string Thrower, SessionThrownShot Thrown);

    private readonly Dictionary<SessionShot, NpcShot> _npcShots = [];

    /// <summary>The shots in flight against <see cref="MaxShotsInFlight"/>: the hero's and the NPCs' together.</summary>
    private int ShotsInFlight => _shots.Count + _npcShots.Count + _npcArrows.Count;

    /// <summary>The shots in flight, oldest first.</summary>
    public IReadOnlyList<SessionShot> QueryShotsInFlight()
    {
        ThrowIfDisposed();
        return _shots.Keys.Concat(_npcShots.Keys).Concat(_npcArrows.Keys).OrderBy(shot => shot.Id).ToArray();
    }

    /// <summary>ARC-COMBAT-017: the thrown item of <paramref name="shot"/> and its thrown range; null for a shot not in flight or one
    /// whose weapon stays in the hand.</summary>
    public SessionThrownShot? QueryThrownShot(SessionShot shot)
    {
        ThrowIfDisposed();
        return _shots.TryGetValue(shot, out var pending) ? pending.Thrown : _npcShots.TryGetValue(shot, out var npc) ? npc.Thrown : null;
    }

    /// <summary>ARC-COMBAT-017: whether <paramref name="item"/> is a thrown item still in flight (hidden: it cannot be picked up and is
    /// not drawn, as the original's item carries object flag 0x2 until it lands).</summary>
    public bool QueryItemInFlight(string item)
    {
        ThrowIfDisposed();
        return ItemInFlight(item);
    }

    private bool ItemInFlight(string item) => _shots.Values.Any(pending => pending.Thrown?.Item == item) || _npcShots.Values.Any(npc => npc.Thrown.Item == item);

    /// <summary>ARC-COMBAT-019: whether a boomerang of the hero is away (a boomerang's shot in flight, out or back: the hero's field 219
    /// bit 0x4 that 0x4B24F0 sets and the second end of 0x4B2690 clears): no attack starts (<see cref="GameSessionFailure.WeaponInFlight"/>,
    /// 0x434B00) and the real-time attack does not repeat (RT7).</summary>
    public bool QueryBoomerangInFlight()
    {
        ThrowIfDisposed();
        return HeroBoomerangAway();
    }

    /// <summary>ARC-COMBAT-019: whether <paramref name="shot"/> is a boomerang's that will turn back at its first end (in flight, not
    /// returning yet).</summary>
    public bool QueryShotReturns(SessionShot shot)
    {
        ThrowIfDisposed();
        return _shots.TryGetValue(shot, out var pending) && pending.Boomerang && !pending.Returning;
    }

    private bool HeroBoomerangAway() => _shots.Values.Any(pending => pending.Boomerang);

    /// <summary>
    /// ARC-COMBAT-019 (the projectile's end 0x4B2690, ledger BR rows): a boomerang's shot that ends without a strike (beyond its range,
    /// or leaving the destination of a shot without a target) turns back to the hero instead (0x435A00) and strikes nothing more; a
    /// strike turns it back itself (<see cref="LandShot"/>, <see cref="LandShotOnObject"/>). The host flies it back
    /// (<see cref="Arcanum.Simulation.Combat.ProjectileRules.TurnBack"/>) and ends it where it arrives (<see cref="EndShot"/>). Nothing
    /// saved changes. False when the shot is not in flight, not a boomerang's, or returning already.
    /// </summary>
    public bool ReturnShot(SessionShot shot)
    {
        ThrowIfDisposed();
        if (!_shots.TryGetValue(shot, out var pending) || !pending.Boomerang || pending.Returning) return false;
        _shots[shot] = pending with { Returning = true };
        return true;
    }

    /// <summary>
    /// ARC-COMBAT-017: the thrown item of <paramref name="shot"/> lands on <paramref name="cell"/> of the map it was thrown on (the
    /// projectile's end 0x4B2690, flag 0x40: the item moves to the projectile's tile through 0x4EDF20 and is shown again; no decay is
    /// scheduled, ledger TW rows), and the shot ends. The host calls it when the shot has struck (the struck object's tile) or flown
    /// beyond its thrown range or off the map (the last tile). One revision. Refused: a shot not in flight or not thrown, or a cell
    /// outside 0 .. 2^24 − 1 (<see cref="GameSessionFailure.InvalidAttack"/>), the version, the revision. NoChange when the item no longer
    /// lies where it was thrown (the shot still ends).
    /// </summary>
    [OriginalFunction(0x4B2690, OriginalFunctionScope.Partial)]
    public GameSessionMutationResult LandThrownItem(GameSessionVersion expected, SessionShot shot, Arcanum.Simulation.Movement.CellCoord cell)
    {
        ThrowIfDisposed();
        // ARC-DEV-258: an NPC's thrown item lies on the cell where the flight ends (the map change a throw's landing writes), in one revision.
        if (_npcShots.TryGetValue(shot, out var npcShot))
        {
            if (!DropCellFits(cell)) return Reject(GameSessionFailure.InvalidAttack);
            if (!IsCurrent(expected)) return Reject(GameSessionFailure.StaleVersion);
            if (_revision == long.MaxValue) return Reject(GameSessionFailure.RevisionExhausted);
            _npcShots.Remove(shot);
            var landed = LandNpcItem(npcShot.Thrown.Item, cell);
            return landed == GameSessionFailure.None ? new(GameSessionDisposition.Changed, GameSessionFailure.None, Version) : Reject(landed);
        }
        if (!_shots.TryGetValue(shot, out var pending) || pending.Thrown is not { } thrown || !DropCellFits(cell))
            return Reject(GameSessionFailure.InvalidAttack);
        if (!IsCurrent(expected)) return Reject(GameSessionFailure.StaleVersion);
        if (_revision == long.MaxValue) return Reject(GameSessionFailure.RevisionExhausted);
        _shots.Remove(shot);
        if (_inventory.PlacedItem(thrown.Item) is not { } placed || placed.Cell == cell) return NoChange();
        _inventory.Hero.ReplacePlaced(placed with { Cell = cell });
        Revise();
        return new(GameSessionDisposition.Changed, GameSessionFailure.None, Version);
    }

    /// <summary>
    /// ARC-COMBAT-016: the flight of <paramref name="shot"/> ends without a strike (it flew beyond its range, left the map, or its host
    /// gave it up: the map changed); a hit it carried is lost, as the original's projectile that never meets its target. False when the
    /// shot is not in flight. Nothing of the session's saved state changes: a thrown item (ARC-COMBAT-017) stays on the hero's cell where
    /// it was thrown from, shown again (its landing is <see cref="LandThrownItem"/>). ARC-COMBAT-019: a boomerang is back in the hand
    /// (the second end of 0x4B2690: also a flight given up before it turned, as the flight goal's own end passes no turn), which ends
    /// the hero's mark (<see cref="QueryBoomerangInFlight"/>).
    /// </summary>
    public bool EndShot(SessionShot shot)
    {
        ThrowIfDisposed();
        return _shots.Remove(shot) || _npcShots.Remove(shot) || _npcArrows.Remove(shot);
    }

    /// <summary>
    /// ARC-COMBAT-017 (ledger RH2, TW rows): the hero's attack with a wielded weapon the host names as "default throws"
    /// (<paramref name="requested"/>) becomes a throw: the throwing skill's level and training, and the weapon's range the thrown range of
    /// its weight under the hero's effective strength (<see cref="ThrowRules.Range"/>); the reach test before it stays the weapon's
    /// (RH1). A weapon the remove check refuses (no drop, 0x466DA0) stays in the hand and attacks as without the flag (0x434B00).
    /// Refused (<see cref="GameSessionFailure.InvalidAttack"/>): a throw that does not fly, a weight outside 0..<see cref="ThrowRules.MaxWeight"/>,
    /// no wielded weapon, or one whose skill is not melee (the only installed "default throws" weapon, the throwing dagger 6072, is a
    /// melee weapon; a bow's ammunition or a boomerang's return would follow other rules, TW7). ARC-SCRIPT-042: a weapon worn with item
    /// event scripts is refused (<see cref="GameSessionFailure.ItemScriptUnsupported"/>): the throw's removal would run them.
    /// </summary>
    private GameSessionFailure ThrowingHero(ref SessionHeroAttack hero, SessionThrow? requested, bool flight, out string? thrown)
    {
        thrown = null;
        if (requested is null) return GameSessionFailure.None;
        if (!flight || requested.Weight is < 0 or > ThrowRules.MaxWeight || hero.Weapon.Kind != WeaponKind.Weapon || hero.Skill != CombatSkill.Melee
            || !_equipment.Worn.TryGetValue(Arcanum.Simulation.Equipment.EquipmentSlot.Weapon, out var wielded)) return GameSessionFailure.InvalidAttack;
        if (Arcanum.Simulation.Equipment.EquipmentRules.CheckRemove(wielded.Item) != Arcanum.Simulation.Equipment.EquipmentRefusal.None)
            return GameSessionFailure.None;
        // ARC-SCRIPT-042: the throw's removal (0x466E00) would run the weapon's wield-off and remove-item scripts, which this path does not.
        if (wielded.ScriptPoints.Count > 0) return GameSessionFailure.ItemScriptUnsupported;
        // ARC-MAGIC-022: nor does it run a marked weapon's unwear (0x4CBFC0).
        if (wielded.ItemSpells) return GameSessionFailure.ItemEffectUnsupported;
        var throwing = HeroSkills()[Arcanum.Simulation.Skills.Skill.Throwing];
        var range = ThrowRules.Range(hero.Combatant.Strength, throwing.Training, requested.Weight);
        hero = hero with
        {
            Weapon = hero.Weapon with { Range = range }, Skill = CombatSkill.Throwing, SkillLevel = throwing.Level, Training = throwing.Training,
            Supported = true,
        };
        thrown = wielded.Entity;
        return GameSessionFailure.None;
    }

    /// <summary>
    /// ARC-COMBAT-019 (ledger RH4, BR rows): a wielded boomerang (the throwing skill, 0x465D80) is checked with the thrown range of its
    /// own weight under the hero's effective strength and throwing training (<see cref="ThrowRules.Range"/>: the shared check reads
    /// 0x4622A0 for skill 3, SC8), the weight 0x461370 from the session's row with the hero as holder (none: 0); its reach before it
    /// and its flight's range end (PF9) stay the weapon's range. Any other weapon is left as it is.
    /// </summary>
    private void BoomerangHero(ref SessionHeroAttack hero)
    {
        if (hero.Skill != CombatSkill.Throwing || hero.Weapon.Kind != WeaponKind.Weapon || HeroCarryFacts() is not { } facts
            || !_equipment.Worn.TryGetValue(Arcanum.Simulation.Equipment.EquipmentSlot.Weapon, out var wielded)) return;
        var weight = Math.Clamp(HeroItemWeight(facts, Carried(wielded.Entity, SessionItemType.Weapon)), 0, ThrowRules.MaxWeight);
        hero = hero with { Weapon = hero.Weapon with { Range = ThrowRules.Range(hero.Combatant.Strength, hero.Training, weight) } };
    }

    /// <summary>ARC-COMBAT-019: a shot of <paramref name="hero"/> is a boomerang's when it attacks with the throwing skill and nothing
    /// leaves the hand (0x4B24F0: weapon flag 0x40 sets attack flag 0x1000).</summary>
    private static bool Boomerang(SessionHeroAttack hero, string? thrown) => hero.Skill == CombatSkill.Throwing && thrown is null;

    /// <summary>ARC-COMBAT-017: the thrown weapon leaves the hero's hand and lies hidden on the hero's cell while it flies (0x434B00 →
    /// 0x466E00, then 0x4B3C00 → 0x4415C0, which schedules no decay), one revision; the hero's worn items for the attack's later steps
    /// lose the weapon slot (the critical miss's weapon effects find the hand empty, CW3). ARC-COMBAT-023: a thrown pack item leaves
    /// its list (or the slot it was worn in) the same way; a weapon the hero wields meanwhile stays in the hand.</summary>
    private IReadOnlyDictionary<Arcanum.Simulation.Equipment.EquipmentSlot, SessionWearItem>? Throw(string item,
        IReadOnlyDictionary<Arcanum.Simulation.Equipment.EquipmentSlot, SessionWearItem>? worn)
    {
        PlaceHeroItem(item, _actor.Cell, decays: false);
        return worn?.Where(pair => pair.Value.Entity != item).ToDictionary(pair => pair.Key, pair => pair.Value);
    }

    /// <summary>A struck shot ends, except a thrown one: it waits for its item's landing (<see cref="LandThrownItem"/>); ARC-COMBAT-019:
    /// and a boomerang's, which turns back (0x4B2690 runs before the hit, PF8).</summary>
    private void EndStrike(SessionShot shot, PendingShot pending)
    {
        if (pending.Boomerang) _shots[shot] = pending with { Returning = true };
        else if (pending.Thrown is null) _shots.Remove(shot);
        else _shots[shot] = pending with { Struck = true };
    }

    /// <summary>ARC-COMBAT-018: the shot's projectiles in flight, each a shot of its own with the same decided outcome (0x4B39B0: a bow
    /// at expert training shoots two, each striking on its own).</summary>
    private IReadOnlyList<SessionShot> Launch(PendingShot pending, int projectiles)
    {
        var shots = new SessionShot[projectiles];
        for (var i = 0; i < projectiles; i++)
        {
            shots[i] = new SessionShot(++_nextShot);
            _shots.Add(shots[i], pending);
        }
        return shots;
    }

    /// <summary>ARC-COMBAT-018: the projectiles a shot of <paramref name="hero"/> flies as (<see cref="ProjectileRules.Projectiles"/>).</summary>
    private static int ShotProjectiles(SessionHeroAttack hero) => ProjectileRules.Projectiles(hero.Skill, hero.Training);

    /// <summary>
    /// ARC-COMBAT-018 (0x4B39B0, ledger RH2 and PS rows): where a missed throw flies: from its target location
    /// <paramref name="origin"/>, <see cref="ThrowRules.ScatterTiles"/> tiles (the thrower's throwing level, at most its distance from
    /// the hero's cell; ARC-WORLD-113: from the NPC thrower's <paramref name="thrower"/> cell) in <paramref name="rotation"/>, each step
    /// the map's (<paramref name="map"/>; without one any cell in 0 .. 2^24 − 1). The shot loses its target and flies free from the start.
    /// </summary>
    private Arcanum.Simulation.Movement.CellCoord ScatterThrow(Arcanum.Simulation.Movement.CellCoord origin, int throwingLevel, int rotation,
        Arcanum.Simulation.Items.IDropMap? map, Arcanum.Simulation.Movement.CellCoord? thrower = null)
    {
        var tiles = ThrowRules.ScatterTiles(throwingLevel, ProjectileRules.Distance(thrower ?? _actor.Cell, origin));
        return ThrowRules.Scatter(origin, rotation, tiles, map is not null ? map.Step
            : (cell, r) => Arcanum.Simulation.Navigation.OriginalPathRequest.Neighbor(cell, r) is var next && DropCellFits(next) ? next : null);
    }

    /// <summary>The identity of an object attack's struck object, to match a strike against the shot's own target.</summary>
    private static string ShotObjectKey(SessionObjectAttack attack) =>
        attack.Wall ? "wall" : attack.Target is { } target ? $"object:{target.Map.RuntimeMap}:{target.Entity}" : $"item:{attack.Item?.Entity}";
}
