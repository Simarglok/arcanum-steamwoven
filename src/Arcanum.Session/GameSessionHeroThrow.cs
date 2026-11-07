using Arcanum.Simulation;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Equipment;
using Arcanum.Simulation.Magic;
using Arcanum.Simulation.Movement;

namespace Arcanum.Session;

/// <summary>
/// ARC-COMBAT-023: one throw of one of the hero's items at a location (<see cref="GameSessionOwner.ThrowHeroItem"/>), as the inventory
/// throws it: the item held on the game cursor and a left press in the game view (ledger TT rows). <see cref="Item"/> is a held item,
/// listed or worn; <see cref="Facts"/> its kind, no-drop flag and script points (a worn item's are the session's own worn record's);
/// <see cref="Target"/> the pressed tile, the throw goal's location. <see cref="Combat"/> is a listed weapon's combat facts (its weapon
/// block, or another item's facts for its uber flag; a worn item's are the session's) and <see cref="Throwable"/> its weapon flag 0x10, which keeps its own
/// damage (WB8). The critter the line of fire to the location names (TW4: a blocker before it, or the critter on its tile) is
/// <see cref="Struck"/> on <see cref="StruckCell"/>, with <see cref="LineOfFire"/> to it and its <see cref="StruckWorn"/> items;
/// without one the item flies to the location unchecked. <see cref="HeroWorn"/>, <see cref="DropMap"/> and <see cref="ItemTriggers"/>
/// are <see cref="SessionAttack"/>'s; the triggers also run the removal's spell steps.
/// </summary>
public sealed record SessionHeroThrow(string Item, SessionGridMoveItem Facts, CellCoord Target)
{
    /// <summary>ARC-UI-086: the units of an ammunition stack the throw splits off (the lift's quantity question, QW1, QW5); required for an
    /// ammunition <see cref="Item"/> (without it the throw is refused as before, <see cref="GameSessionFailure.AttackUnsupported"/>).</summary>
    public int? Count { get; init; }
    /// <summary>ARC-UI-086: the kind of the stack the split makes (the host names it, as <see cref="GameSessionOwner.DropHeroAmmo"/>'s).</summary>
    public SessionItemKind? SplitKind { get; init; }
    public EquipmentItem? Combat { get; init; }
    public bool Throwable { get; init; }
    public SessionCritterCombat? Struck { get; init; }
    public CellCoord? StruckCell { get; init; }
    public SessionLineOfFire? LineOfFire { get; init; }
    public IReadOnlyDictionary<EquipmentSlot, SessionWearItem>? HeroWorn { get; init; }
    public IReadOnlyDictionary<EquipmentSlot, SessionWearItem>? StruckWorn { get; init; }
    public Arcanum.Simulation.Items.IDropMap? DropMap { get; init; }
    public ISessionItemTriggers? ItemTriggers { get; init; }
}

/// <summary>
/// ARC-COMBAT-023: what <see cref="GameSessionOwner.ThrowHeroItem"/> did: the attack on the struck critter (its resolution, the shot in
/// flight, a miss's scatter; null when refused), or without a struck critter the shot alone (no resolution); <see cref="Destination"/>
/// is the tile a shot without a target flies to and ends on leaving (<see cref="ProjectileStrikeKind.DestinationEnd"/>: the location,
/// or a miss's scattered tile; null for a shot at its struck critter). <see cref="RemovalSpells"/> are the removal's spell steps with
/// <see cref="SessionHeroThrow.ItemTriggers"/> (a worn item's unwear, then the removal's, as the drop's).
/// </summary>
public sealed record SessionHeroThrowResult(GameSessionDisposition Disposition, GameSessionFailure Failure, SessionAttackResult? Attack,
    GameSessionVersion Version)
{
    public IReadOnlyList<SessionShot> Shots => Attack?.Shots ?? [];
    public CellCoord? Destination { get; init; }
    private readonly IReadOnlyList<SessionItemTriggerResult>? _removalSpells;
    public IReadOnlyList<SessionItemTriggerResult> RemovalSpells { get => _removalSpells ?? []; init => _removalSpells = value; }
}

public sealed partial class GameSessionOwner
{
    // ARC-COMBAT-023: a pack item's throw for the critter attack: the item, the throw's location and the hero as it throws.
    private sealed record PackThrow(string Item, CellCoord Target, SessionHeroAttack Hero)
    {
        /// <summary>ARC-UI-086: the units and kind of the ammunition split the throw makes at the moment the item leaves.</summary>
        public (int Count, SessionItemKind Kind)? Split { get; init; }
    }

    /// <summary>ARC-UI-086 (QW5, 0x4658E0, 0x465A40): the refusal of a split of <paramref name="count"/> units off the hero's ammunition stack
    /// <paramref name="stack"/> into a new stack of <paramref name="kind"/> in the hero's list, as <see cref="DropHeroAmmo"/> checks it, or None.</summary>
    private GameSessionFailure SplitAmmoRefusal(string stack, int count, SessionItemKind? kind)
    {
        if (kind is not { Type: SessionItemType.Ammo } || count <= 0) return GameSessionFailure.InvalidAmmo;
        if (!_inventory.Ammo.TryGetValue(stack, out var source)) return GameSessionFailure.ItemNotAvailable;
        if (source.Units < count) return GameSessionFailure.AmmoShort;
        var emptied = source.Units == count;
        if (emptied && !_durability.Records.ContainsKey(source.Entity) && _durability.Count >= _limits.MaxDurabilityRecords) return GameSessionFailure.BudgetExceeded;
        var freed = emptied ? 1 : 0;
        if (_inventory.Ammo.Count - freed + 1 > _limits.MaxAmmoRecords || _inventory.Held - freed + 1 > _limits.MaxPcItems) return GameSessionFailure.BudgetExceeded;
        return NextObjectEntity(SessionObjectOrigin.Made) is null ? GameSessionFailure.BudgetExceeded : GameSessionFailure.None;
    }

    /// <summary>ARC-UI-086: the split itself (checked by <see cref="SplitAmmoRefusal"/>): the units leave the stack (exactly the count deletes
    /// it, with no remove-item event: the stack path never reaches 0x461A70, QW5) and a new made stack of <paramref name="kind"/> carries them in
    /// the hero's list; the caller then throws it. Returns its entity.</summary>
    private string SplitAmmoForThrow(string stack, int count, SessionItemKind kind)
    {
        var source = _inventory.Ammo[stack];
        var entity = NextObjectEntity(SessionObjectOrigin.Made)!;
        if (source.Units == count) DestroyObject(source.Entity);
        else _inventory.Ammo[source.Entity] = source with { Units = source.Units - count };
        var item = CreateMadeItem(new(entity, kind, SessionCreatePlace.HeroList, CreatedWeight(kind.Prototype)));
        _inventory.Ammo[item.Entity] = new(item.Entity, source.AmmoType, count);
        return item.Entity;
    }

    /// <summary>
    /// ARC-COMBAT-023 (ledger TT rows, TW rows, WB8): the hero throws one of its items at <see cref="SessionHeroThrow.Target"/>. In the
    /// original the item leaves the hero when the inventory lifts it onto the game cursor (0x5788C0 → its message 0x51: the remove check
    /// 0x466DA0, then the removal 0x466E00 with its wield-off and remove-item events), and the left press in the game view starts the
    /// throw goal 37 (0x54E033 → 0x434F80) whose throw (0x4B3C00) leaves a PC's item where it is, so the session takes it out at the
    /// throw itself. The throw checks with the throwing skill's level and training (skill 3) and the item's thrown range
    /// (<see cref="ThrowRules.Range"/> of its weight under the hero's effective strength, the session's weight row with the hero as holder,
    /// 0x461370); there is no reach test (the press throws at any tile). With a struck critter it is <see cref="AttackCritter"/>'s attack
    /// at the struck critter's distance with the line of fire's cover, a flight (<see cref="SessionAttack.Flight"/>) whose item strikes
    /// with <see cref="ThrowRules.ThrownWeapon"/> (WB8: a throwable weapon its own damage, anything else the damage of its weight), a
    /// missed throw scattering from the location (RH2); no ammunition of a wielded weapon is spent and a wielded boomerang still away does
    /// not refuse it. Without one the item flies to the location: nothing is rolled (NOT READY: the original's dispatcher still runs the
    /// shared check without a target, 0x4B34B9, so a failed throw at an empty tile scatters, TT3). Either way the item leaves its list or
    /// slot in a revision of its own and lies hidden on the hero's cell (0x4415C0, no decay) until the host lands it
    /// (<see cref="LandThrownItem"/>) or gives the flight up (<see cref="EndShot"/>). No action points here: the turn-based host charges
    /// <see cref="TurnBasedRules.ThrowActionCost"/>. ARC-UI-086: an ammunition stack throws the <see cref="SessionHeroThrow.Count"/> units the
    /// lift asked for (QW1, QW5): at the moment the item leaves they are split off into a new made stack of <see cref="SessionHeroThrow.SplitKind"/>
    /// (the stack path runs no remove check and no remove-item event, so the no-drop flag and the remove-item script do not refuse it), and that stack
    /// is the thrown item (its weight, range and damage are those of its units); no count refuses as before. NOT READY: gold (the hero's coins are a
    /// counter here, <see cref="GameSessionFailure.AttackUnsupported"/>), the item's throw script (point 6) and its removal's wield-off and remove-item
    /// scripts (an item with any of them is refused, <see cref="GameSessionFailure.ItemScriptUnsupported"/>), a non-critter blocker
    /// (the host names none), the struck critter's alert and the landing's item event (PF10). Steamwoven differences: the item leaves
    /// at the throw, not at the lift, so a worn item's own values (its armour, its effects on the hero's strength) still count for the
    /// throw's range and check; a critical miss's weapon and ammunition steps read the weapon the hero still wields (as CW3; not read for
    /// a pack throw). A struck critter that is dead refuses the attack (NoChange, the item stays): the host throws again without one, as
    /// the original's press throws at the location anyway.
    /// Refused before any effect, in order: no hero (<see cref="GameSessionFailure.AttackUnavailable"/>), invalid item facts
    /// (<see cref="GameSessionFailure.InvalidItem"/>), a location or struck cell outside 0 .. 2^24 − 1, a struck critter without its cell
    /// or the reverse, a line-of-fire cost outside 0 .. 100,000 (<see cref="GameSessionFailure.InvalidAttack"/>), an item the hero does not
    /// hold (<see cref="GameSessionFailure.ItemNotAvailable"/>), a kind other than the saved one (<see cref="GameSessionFailure.InventoryMismatch"/>),
    /// invalid combat facts (<see cref="GameSessionFailure.InvalidItem"/>: a weapon without its weapon block, a weapon block on another
    /// kind), gold, or ammunition without a count (<see cref="GameSessionFailure.AttackUnsupported"/>), a worn ammunition stack
    /// (<see cref="GameSessionFailure.InvalidItem"/>), a bad count or split kind (<see cref="GameSessionFailure.InvalidAmmo"/>), fewer units
    /// (<see cref="GameSessionFailure.AmmoShort"/>), the budgets of the split (<see cref="GameSessionFailure.BudgetExceeded"/>), the scripts above
    /// (<see cref="GameSessionFailure.ItemScriptUnsupported"/>), the no-drop flag (the remove check: <see cref="GameSessionFailure.ItemNotDroppable"/>),
    /// an item that is the source of a spell run or a worn item with the item spells mark without triggers
    /// (<see cref="GameSessionFailure.ItemEffectUnsupported"/>), a wielded boomerang whose shot is away (<see cref="GameSessionFailure.WeaponInFlight"/>),
    /// then <see cref="AttackCritter"/>'s refusals (with a struck critter) or the hero's worn items, the flight budget, the version and the
    /// revision. An inactive hero throws nothing (NoChange; the original puts the item back, 0x575770).
    /// </summary>
    public SessionHeroThrowResult ThrowHeroItem(GameSessionVersion expected, SessionHeroThrow request)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Facts);
        SessionHeroThrowResult Refused(GameSessionFailure failure) => new(GameSessionDisposition.Rejected, failure, null, Version);
        if (_hero is null) return Refused(GameSessionFailure.AttackUnavailable);
        var facts = request.Facts;
        if (!SessionValidation.Entity(request.Item) || facts.ScriptPoints is null || !Enum.IsDefined(facts.Kind.Type) ||
            facts.ScriptPoints.Any(point => point is < 0 or > SessionScriptPoint.Last)) return Refused(GameSessionFailure.InvalidItem);
        var struck = request.Struck is not null;
        if (!DropCellFits(request.Target) || struck != request.StruckCell.HasValue || request.StruckCell is { } at && !DropCellFits(at) ||
            request.LineOfFire is { Cost: < 0 or > AttackResolver.MaxMagnitude }) return Refused(GameSessionFailure.InvalidAttack);
        if (HeldKind(request.Item) is not { } kind) return Refused(GameSessionFailure.ItemNotAvailable);
        if (kind != facts.Kind) return Refused(GameSessionFailure.InventoryMismatch);
        var worn = _equipment.SlotOf(request.Item) is { } slot ? _equipment.Worn[slot] : null;
        var combat = worn?.Item ?? request.Combat;
        var weapon = kind.Type == SessionItemType.Weapon;
        if (worn is null && request.Combat is not null && !EquipmentRules.IsValid(request.Combat) ||
            weapon != (combat is { Kind: EquipmentItemKind.Weapon, Weapon: not null })) return Refused(GameSessionFailure.InvalidItem);
        // The lift's quantity split (0x4658E0) takes gold and ammunition another way: gold is the hero's pile in the grid (ARC-UI-090) whose lift and throw are ARC-UI-091's, and an
        // ammunition stack splits off the units the lift asked for (ARC-UI-086), another stack that is thrown whole.
        if (kind.Type == SessionItemType.Gold || kind.Type == SessionItemType.Ammo && request.Count is null) return Refused(GameSessionFailure.AttackUnsupported);
        var split = kind.Type == SessionItemType.Ammo;
        if (split && worn is not null) return Refused(GameSessionFailure.InvalidItem);
        if (split && SplitAmmoRefusal(request.Item, request.Count!.Value, request.SplitKind) is var splitFailure && splitFailure != GameSessionFailure.None) return Refused(splitFailure);
        var points = worn?.ScriptPoints ?? facts.ScriptPoints;
        // The stack path of the split runs no remove check and no remove-item event (QW5): only the throw script keeps it refused.
        if (points.Any(point => point is SessionScriptPoint.Throw || !split && point == SessionScriptPoint.RemoveItem || worn is not null && point == SessionScriptPoint.WieldOff))
            return Refused(GameSessionFailure.ItemScriptUnsupported);
        if (!split && (facts.NoDrop || worn?.Item.NoDrop == true)) return Refused(GameSessionFailure.ItemNotDroppable);
        var triggers = request.ItemTriggers;
        if (triggers is null && (worn?.ItemSpells == true || HasItemRuns(request.Item))) return Refused(GameSessionFailure.ItemEffectUnsupported);
        // A wielded boomerang away on its own shot is not in the hand to throw (its shot still names it).
        if (_shots.Values.Any(pending => pending.Boomerang && pending.WeaponItem == request.Item)) return Refused(GameSessionFailure.WeaponInFlight);

        // The hero as it throws: the throwing skill, the item's thrown range (the check's and the flight's, PF9) and, as the attack's
        // weapon range (0x465E30: a weapon's range field and magic adjustment, anything else 1), its reach.
        var carry = HeroCarryFacts();
        var weight = carry is { } held ? Math.Clamp(HeroItemWeight(held, split ? CarriedUnits(request.Item, request.Count!.Value) : Carried(request.Item, kind.Type)), 0, ThrowRules.MaxWeight) : 0;
        var hero = QueryHeroAttack()!;
        var throwing = HeroSkills()[Arcanum.Simulation.Skills.Skill.Throwing];
        var range = ThrowRules.Range(hero.Combatant.Strength, throwing.Training, weight);
        var aptitude = HeroAptitude();
        var reach = weapon ? EquipmentRules.Weapon(new Dictionary<EquipmentSlot, EquipmentItem> { [EquipmentSlot.Weapon] = combat! }, aptitude).Range : 1;
        hero = hero with
        {
            Weapon = ThrowRules.ThrownWeapon(combat, request.Throwable, weight, aptitude, range), Skill = CombatSkill.Throwing,
            SkillLevel = throwing.Level, Training = throwing.Training, Range = reach, Supported = true,
        };
        var wornBefore = worn is not null;
        SessionHeroThrowResult result;
        if (struck)
        {
            var attack = new SessionAttack(request.Struck!)
            {
                Flight = true,
                Distance = ProjectileRules.Distance(_actor.Cell, request.StruckCell!.Value),
                LineOfFire = request.LineOfFire,
                HeroWorn = request.HeroWorn,
                TargetWorn = request.StruckWorn,
                TargetCell = request.StruckCell,
                DropMap = request.DropMap,
                ItemTriggers = triggers,
            };
            var attacked = CritterAttack(expected, attack, null, new PackThrow(request.Item, request.Target, hero) { Split = split ? (request.Count!.Value, request.SplitKind!.Value) : null });
            if (attacked.Disposition == GameSessionDisposition.Rejected) return Refused(attacked.Failure);
            result = new(attacked.Disposition, GameSessionFailure.None, attacked, Version) { Destination = attacked.Scatter };
            if (attacked.Shots.Count == 0) return result;
        }
        else
        {
            // The hero's own checks, as the attack's: its worn items; an inactive hero throws nothing (the press puts it back, 0x54E021).
            if (!ValidWear(new SessionWearSide { Life = SessionLifeTarget.Hero, Worn = request.HeroWorn }, new SessionWearSide(), targetBroken: false, wear: null))
                return Refused(GameSessionFailure.InvalidWear);
            var life = _life.Hero!.Value.State;
            if (!LifeRules.Evaluate(life, HeroLifeFacts(life)).IsActive) return new(GameSessionDisposition.NoChange, GameSessionFailure.None, null, Version);
            if (_shots.Count > MaxShotsInFlight - 1) return Refused(GameSessionFailure.BudgetExceeded);
            if (!IsCurrent(expected)) return Refused(GameSessionFailure.StaleVersion);
            if (_revision == long.MaxValue) return Refused(GameSessionFailure.RevisionExhausted);
            var thrownItem = split ? SplitAmmoForThrow(request.Item, request.Count!.Value, request.SplitKind!.Value) : request.Item;
            Throw(thrownItem, null);
            // A shot without a target: any critter it strikes on the way takes a forced hit (LandShot), its item lands where it ends.
            var shots = Launch(new PendingShot(hero, AttackResolver.ForcedHit(false), false, null, null)
                { Thrown = new SessionThrownShot(thrownItem, range), WeaponItem = thrownItem }, 1);
            result = new(GameSessionDisposition.Changed, GameSessionFailure.None,
                new(GameSessionDisposition.Changed, GameSessionFailure.None, null, null, null, null, null, GameSessionFailure.None, Version) { Shots = shots },
                Version) { Destination = request.Target };
        }
        if (triggers is null || split) return result;
        var spells = new List<SessionItemTriggerResult>();
        if (wornBefore) spells.Add(triggers.Withdraw(ItemSpellTriggerRules.Unwear, request.Item, SessionEventKinds.PlayerEntity));
        spells.Add(triggers.Remove(request.Item, SessionSpellCaster.Hero));
        return result with { RemovalSpells = spells.AsReadOnly(), Version = Version };
    }

    /// <summary>ARC-COMBAT-023: the thrown range of one of the hero's items now (<see cref="ThrowRules.Range"/>: its weight with the hero as
    /// holder under the hero's effective strength and throwing training), for the host's cursor and the flight's end; null without a hero
    /// or for an item the hero does not hold.</summary>
    public int? QueryHeroThrowRange(string item, int? units = null)
    {
        ThrowIfDisposed();
        if (!SessionValidation.Entity(item)) throw new ArgumentException("Invalid entity id.", nameof(item));
        if (_hero is null || HeldKind(item) is not { } kind) return null;
        // ARC-UI-086: of an ammunition stack only the units the throw would split off count.
        var weight = HeroCarryFacts() is { } held ? Math.Clamp(HeroItemWeight(held, units is { } n && kind.Type == SessionItemType.Ammo ? CarriedUnits(item, Math.Max(0, n)) : Carried(item, kind.Type)), 0, ThrowRules.MaxWeight) : 0;
        return ThrowRules.Range(QueryHeroAttack()!.Combatant.Strength, HeroSkills()[Arcanum.Simulation.Skills.Skill.Throwing].Training, weight);
    }

    /// <summary>ARC-COMBAT-023: what a throw costs the hero in a round under the session's rule mode (<see cref="TurnBasedRules.ThrowActionCost"/>).</summary>
    public int QueryHeroThrowCost()
    {
        ThrowIfDisposed();
        return TurnBasedRules.ThrowActionCost(localPc: true, _rules.Combat);
    }
}
