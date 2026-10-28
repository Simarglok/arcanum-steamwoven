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
/// rolled and applied. Shots in flight are not saved: a snapshot taken while one flies holds what the shot already spent and no
/// projectile (Steamwoven; the original's projectile is an object of the map), and a restored session has none.
/// ARC-COMBAT-017: a thrown weapon (<see cref="SessionAttack.Throw"/>) leaves the hero at the shot and lies hidden on the hero's cell
/// while it flies (0x4B3C00: 0x4415C0, no decay); its shot ends when the item lands (<see cref="LandThrownItem"/>, after its strike
/// if it struck) or when the host gives the flight up (<see cref="EndShot"/>: the item stays on the hero's cell).
/// </summary>
public sealed partial class GameSessionOwner
{
    /// <summary>The most shots in flight at once (a shot flies as one projectile, a bow's at expert training as two, ARC-COMBAT-018).</summary>
    public const int MaxShotsInFlight = 16;

    /// <summary>A shot in flight: the hero's weapon and skill at the shot (the projectile's parent weapon, field 0x50), the decided
    /// check, whether it hit, and its target: an NPC's entity, or a non-critter object's key (<see cref="ShotObjectKey"/>).</summary>
    private sealed record PendingShot(SessionHeroAttack Hero, AttackResolution Check, bool Hit, string? Entity, string? Object)
    {
        /// <summary>ARC-COMBAT-017: the thrown item and its thrown range; null for a shot of a weapon that stays in the hand.</summary>
        public SessionThrownShot? Thrown { get; init; }
        /// <summary>ARC-COMBAT-017: the thrown shot struck already and waits for its item's landing.</summary>
        public bool Struck { get; init; }
    }

    private readonly Dictionary<SessionShot, PendingShot> _shots = [];
    private long _nextShot;

    /// <summary>The shots in flight, oldest first.</summary>
    public IReadOnlyList<SessionShot> QueryShotsInFlight()
    {
        ThrowIfDisposed();
        return _shots.Keys.OrderBy(shot => shot.Id).ToArray();
    }

    /// <summary>ARC-COMBAT-017: the thrown item of <paramref name="shot"/> and its thrown range; null for a shot not in flight or one
    /// whose weapon stays in the hand.</summary>
    public SessionThrownShot? QueryThrownShot(SessionShot shot)
    {
        ThrowIfDisposed();
        return _shots.TryGetValue(shot, out var pending) ? pending.Thrown : null;
    }

    /// <summary>ARC-COMBAT-017: whether <paramref name="item"/> is a thrown item still in flight (hidden: it cannot be picked up and is
    /// not drawn, as the original's item carries object flag 0x2 until it lands).</summary>
    public bool QueryItemInFlight(string item)
    {
        ThrowIfDisposed();
        return ItemInFlight(item);
    }

    private bool ItemInFlight(string item) => _shots.Values.Any(pending => pending.Thrown?.Item == item);

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
        if (!_shots.TryGetValue(shot, out var pending) || pending.Thrown is not { } thrown || !DropCellFits(cell))
            return Reject(GameSessionFailure.InvalidAttack);
        if (!IsCurrent(expected)) return Reject(GameSessionFailure.StaleVersion);
        if (_revision == long.MaxValue) return Reject(GameSessionFailure.RevisionExhausted);
        _shots.Remove(shot);
        var index = _inventory.Placed.FindIndex(placed => placed.Entity == thrown.Item);
        if (index < 0 || _inventory.Placed[index].Cell == cell) return NoChange();
        _inventory.Placed[index] = _inventory.Placed[index] with { Cell = cell };
        _revision++;
        return new(GameSessionDisposition.Changed, GameSessionFailure.None, Version);
    }

    /// <summary>
    /// ARC-COMBAT-016: the flight of <paramref name="shot"/> ends without a strike (it flew beyond its range, left the map, or its host
    /// gave it up: the map changed); a hit it carried is lost, as the original's projectile that never meets its target. False when the
    /// shot is not in flight. Nothing of the session's saved state changes: a thrown item (ARC-COMBAT-017) stays on the hero's cell where
    /// it was thrown from, shown again (its landing is <see cref="LandThrownItem"/>).
    /// </summary>
    public bool EndShot(SessionShot shot)
    {
        ThrowIfDisposed();
        return _shots.Remove(shot);
    }

    /// <summary>
    /// ARC-COMBAT-017 (ledger RH2, TW rows): the hero's attack with a wielded weapon the host names as "default throws"
    /// (<paramref name="requested"/>) becomes a throw: the throwing skill's level and training, and the weapon's range the thrown range of
    /// its weight under the hero's effective strength (<see cref="ThrowRules.Range"/>); the reach test before it stays the weapon's
    /// (RH1). A weapon the remove check refuses (no drop, 0x466DA0) stays in the hand and attacks as without the flag (0x434B00).
    /// Refused (<see cref="GameSessionFailure.InvalidAttack"/>): a throw that does not fly, a weight outside 0..<see cref="ThrowRules.MaxWeight"/>,
    /// no wielded weapon, or one whose skill is not melee (the only installed "default throws" weapon, the throwing dagger 6072, is a
    /// melee weapon; a bow's ammunition or a boomerang's return would follow other rules, TW7).
    /// </summary>
    private GameSessionFailure ThrowingHero(ref SessionHeroAttack hero, SessionThrow? requested, bool flight, out string? thrown)
    {
        thrown = null;
        if (requested is null) return GameSessionFailure.None;
        if (!flight || requested.Weight is < 0 or > ThrowRules.MaxWeight || hero.Weapon.Kind != WeaponKind.Weapon || hero.Skill != CombatSkill.Melee
            || !_equipment.Worn.TryGetValue(Arcanum.Simulation.Equipment.EquipmentSlot.Weapon, out var wielded)) return GameSessionFailure.InvalidAttack;
        if (Arcanum.Simulation.Equipment.EquipmentRules.CheckRemove(wielded.Item) != Arcanum.Simulation.Equipment.EquipmentRefusal.None)
            return GameSessionFailure.None;
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

    /// <summary>ARC-COMBAT-017: the thrown weapon leaves the hero's hand and lies hidden on the hero's cell while it flies (0x434B00 →
    /// 0x466E00, then 0x4B3C00 → 0x4415C0, which schedules no decay), one revision; the hero's worn items for the attack's later steps
    /// lose the weapon slot (the critical miss's weapon effects find the hand empty, CW3).</summary>
    private IReadOnlyDictionary<Arcanum.Simulation.Equipment.EquipmentSlot, SessionWearItem>? Throw(string item,
        IReadOnlyDictionary<Arcanum.Simulation.Equipment.EquipmentSlot, SessionWearItem>? worn)
    {
        PlaceHeroItem(item, _actor.Cell, decays: false);
        return worn?.Where(pair => pair.Key != Arcanum.Simulation.Equipment.EquipmentSlot.Weapon).ToDictionary(pair => pair.Key, pair => pair.Value);
    }

    /// <summary>A struck shot ends, except a thrown one: it waits for its item's landing (<see cref="LandThrownItem"/>).</summary>
    private void EndStrike(SessionShot shot, PendingShot pending)
    {
        if (pending.Thrown is null) _shots.Remove(shot);
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
    /// the hero's cell) in <paramref name="rotation"/>, each step the map's (<paramref name="map"/>; without one any cell in
    /// 0 .. 2^24 − 1). The shot loses its target and flies free from the start.
    /// </summary>
    private Arcanum.Simulation.Movement.CellCoord ScatterThrow(Arcanum.Simulation.Movement.CellCoord origin, int throwingLevel, int rotation,
        Arcanum.Simulation.Items.IDropMap? map)
    {
        var tiles = ThrowRules.ScatterTiles(throwingLevel, ProjectileRules.Distance(_actor.Cell, origin));
        return ThrowRules.Scatter(origin, rotation, tiles, map is not null ? map.Step
            : (cell, r) => Arcanum.Simulation.Navigation.OriginalPathRequest.Neighbor(cell, r) is var next && DropCellFits(next) ? next : null);
    }

    /// <summary>The identity of an object attack's struck object, to match a strike against the shot's own target.</summary>
    private static string ShotObjectKey(SessionObjectAttack attack) =>
        attack.Wall ? "wall" : attack.Target is { } target ? $"object:{target.Map.RuntimeMap}:{target.Entity}" : $"item:{attack.Item?.Entity}";
}
