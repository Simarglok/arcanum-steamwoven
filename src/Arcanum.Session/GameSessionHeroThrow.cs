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
    /// <summary>ARC-WORLD-1149: the light level 0..255 (255: full light) at the struck critter's cell (as <see cref="SessionAttack.TargetLight"/>); null: full light.</summary>
    public int? StruckLight { get; init; }
    public Arcanum.Simulation.Items.IDropMap? DropMap { get; init; }
    public ISessionItemTriggers? ItemTriggers { get; init; }
    /// <summary>ARC-COMBAT-1955 (0x466E00 → 0x467860, 0x4B3C00 → 0x441980): the host that runs the item's removal scripts (wield off 25 for a worn item, remove-item 28) and its throw script (point 6) before the throw; without it an
    /// item with any of them is refused (<see cref="GameSessionFailure.ItemScriptUnsupported"/>).</summary>
    public ISessionItemScriptHost? Scripts { get; init; }
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
    /// <summary>ARC-COMBAT-1955 (0x4B3C00): the item's throw script (point 6) answered skip default, so nothing was thrown: the item is back in the hero's list unworn (the original puts it on the hero's tile and inserts it again, 0x4415C0,
    /// 0x4617F0), the removal's and the throw script's own changes kept. The result is <see cref="GameSessionDisposition.Changed"/> with no attack and no shot.</summary>
    public bool ScriptDeclined { get; init; }
    /// <summary>ARC-COMBAT-1955: the lines the removal and throw scripts printed, in order, for the host to post as it posts any script's printed line.</summary>
    public IReadOnlyList<SessionScriptMessage> Messages { get; init; } = [];
    /// <summary>ARC-COMBAT-1955: the eye candy requests of those scripts, in order, for the host to play.</summary>
    public IReadOnlyList<Arcanum.Scripting.Scr.ScrEyeCandy> EyeCandies { get; init; } = [];
    /// <summary>ARC-COMBAT-1955: the script runs that were made (removal, then throw), as the dispatcher answered them.</summary>
    public IReadOnlyList<SessionItemScriptRun> Scripts { get; init; } = [];
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

    /// <summary>ARC-UI-094 (QW1, QW5, IW18): the refusal of a split of <paramref name="count"/> coins off the hero's gold pile into a new made gold
    /// item of <paramref name="kind"/> in the hero's list, as <see cref="DropHeroGold"/> checks it, or None.</summary>
    private GameSessionFailure SplitGoldRefusal(int count, SessionItemKind? kind)
    {
        if (kind is not { Type: SessionItemType.Gold } || count <= 0) return GameSessionFailure.InvalidItem;
        if (_inventory.Gold < count) return GameSessionFailure.GoldShort;
        if (_inventory.Piles.Count >= _limits.MaxGoldRecords || _inventory.Held + 1 > _limits.MaxPcItems) return GameSessionFailure.BudgetExceeded;
        return NextObjectEntity(SessionObjectOrigin.Made) is null ? GameSessionFailure.BudgetExceeded : GameSessionFailure.None;
    }

    /// <summary>ARC-UI-094: the split itself (checked by <see cref="SplitGoldRefusal"/>): the coins leave the counter (the last coin takes the pile
    /// with it, HG1) and a new made gold item of <paramref name="kind"/> carries them in the hero's list; the caller then throws it. Returns its entity.</summary>
    private string SplitGoldForThrow(int count, SessionItemKind kind)
    {
        var entity = NextObjectEntity(SessionObjectOrigin.Made)!;
        var weight = CreatedWeight(kind.Prototype);
        _inventory.Gold -= count;
        var item = CreateMadeItem(new(entity, kind, SessionCreatePlace.HeroList, weight));
        _inventory.Piles[item.Entity] = count;
        return item.Entity;
    }

    // The split a throw makes when the item leaves: an ammunition stack's units or the gold pile's coins.
    private string SplitForThrow(string item, int count, SessionItemKind kind) =>
        item == SessionHeroGrid.GoldEntity ? SplitGoldForThrow(count, kind) : SplitAmmoForThrow(item, count, kind);

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
    /// is the thrown item (its weight, range and damage are those of its units); no count refuses as before. ARC-UI-094: the hero's gold pile (<see cref="SessionHeroGrid.GoldEntity"/>) throws the same way: the coins
    /// split off into a new made gold item of <see cref="SessionHeroThrow.SplitKind"/> (a coin weighs nothing, HG1), and the item lands as the pile to
    /// pick up (<see cref="TakePlaced"/>). ARC-COMBAT-1955 (TT1, TT8, TW3): with <see cref="SessionHeroThrow.Scripts"/> the removal's wield-off script (25, a worn item) and remove-item script (28), then the item's throw script (point 6, the
    /// hero as the triggerer, no other object) run first (<see cref="RunHeroThrowScripts"/>); a throw script that answers skip default throws nothing and puts the item back in the hero's list unworn
    /// (<see cref="SessionHeroThrowResult.ScriptDeclined"/>). ARC-COMBAT-2182 (TW3, QW5): a split stack's throw script runs for the stack the split would make (no removal script: the stack path runs none), before anything is split; an answer of skip default throws
    /// nothing and the units stay in the stack, the run's flag, store and message changes kept. ARC-COMBAT-2413: for the whole ammunition stack the source is destroyed and the made stack stays in its place (the lift's new object, merged into nothing, <see cref="GameSessionOwner.RemakeWholeAmmoStack"/>), keeping the slots and calls the runs left on it. NOT READY: a throw script together with an insert-item script (refused,
    /// <see cref="GameSessionFailure.ItemScriptUnsupported"/>; so is any of the scripts without the host), a non-critter blocker
    /// (the host names none), the struck critter's alert and the landing's item event (PF10). Steamwoven differences: the item leaves
    /// at the throw, not at the lift, so a worn item's own values (its armour, its effects on the hero's strength) still count for the
    /// throw's range and check; a critical miss's weapon and ammunition steps read the weapon the hero still wields (as CW3; not read for
    /// a pack throw). A struck critter that is dead is thrown at like a living one (ARC-COMBAT-2392, TW8: the roll is against the corpse, no dodge, no damage). ARC-COMBAT-2390: once item scripts ran, a struck critter whose attack is refused (a refusal the scripts' runs brought about) is thrown at as the location by the session itself, so the scripts do not run twice.
    /// Refused before any effect, in order: no hero (<see cref="GameSessionFailure.AttackUnavailable"/>), invalid item facts
    /// (<see cref="GameSessionFailure.InvalidItem"/>), a location or struck cell outside 0 .. 2^24 − 1, a struck critter without its cell
    /// or the reverse, a line-of-fire cost outside 0 .. 100,000 (<see cref="GameSessionFailure.InvalidAttack"/>), an item the hero does not
    /// hold (<see cref="GameSessionFailure.ItemNotAvailable"/>), a kind other than the saved one (<see cref="GameSessionFailure.InventoryMismatch"/>),
    /// invalid combat facts (<see cref="GameSessionFailure.InvalidItem"/>: a weapon without its weapon block, a weapon block on another
    /// kind), or gold or ammunition without a count (<see cref="GameSessionFailure.AttackUnsupported"/>), a worn ammunition stack
    /// (<see cref="GameSessionFailure.InvalidItem"/>), a bad count or split kind (<see cref="GameSessionFailure.InvalidAmmo"/>, gold: <see cref="GameSessionFailure.InvalidItem"/>), fewer units or coins
    /// (<see cref="GameSessionFailure.AmmoShort"/>, <see cref="GameSessionFailure.GoldShort"/>), the budgets of the split (<see cref="GameSessionFailure.BudgetExceeded"/>), the scripts above
    /// (<see cref="GameSessionFailure.ItemScriptUnsupported"/>, and a run the session cannot apply), the no-drop flag (the remove check: <see cref="GameSessionFailure.ItemNotDroppable"/>),
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
        // ARC-COMBAT-1955: the scripts the throw ran (null: none): a refusal after they took effect (the location throw's own checks) is a changed session with the failure named, a refusal before that carries the runs only.
        ItemScriptRuns? runs = null;
        var applied = false;
        SessionHeroThrowResult Refused(GameSessionFailure failure)
        {
            // ARC-COMBAT-2182: the slots and deferred calls a split stack's throw script left on an item the throw then never made (the stack still holds its units) go.
            if (runs is not null && applied && NextObjectEntity(SessionObjectOrigin.Made) is { } stray) ForgetNeverMadeThrowItem(stray);
            return runs is null ? new(GameSessionDisposition.Rejected, failure, null, Version)
                : With(new(applied ? GameSessionDisposition.Changed : GameSessionDisposition.Rejected, failure, null, Version));
        }
        SessionHeroThrowResult With(SessionHeroThrowResult made) => runs is null ? made
            : made with { Scripts = runs.Runs, Messages = applied || made.Disposition != GameSessionDisposition.Rejected ? runs.Messages : [], EyeCandies = applied || made.Disposition != GameSessionDisposition.Rejected ? runs.EyeCandies : [] };
        if (_hero is null) return Refused(GameSessionFailure.AttackUnavailable);
        var facts = request.Facts;
        if (!SessionValidation.Entity(request.Item) || facts.ScriptPoints is null || !Enum.IsDefined(facts.Kind.Type) ||
            facts.ScriptPoints.Any(point => point is < 0 or > SessionScriptPoint.Last)) return Refused(GameSessionFailure.InvalidItem);
        var struck = request.Struck is not null;
        if (!DropCellFits(request.Target) || struck != request.StruckCell.HasValue || request.StruckCell is { } at && !DropCellFits(at) ||
            request.LineOfFire is { Cost: < 0 or > AttackResolver.MaxMagnitude }) return Refused(GameSessionFailure.InvalidAttack);
        var gold = request.Item == SessionHeroGrid.GoldEntity;
        if ((gold ? _inventory.Gold > 0 ? SessionHeroGrid.GoldKind : (SessionItemKind?)null : HeldKind(request.Item)) is not { } kind) return Refused(GameSessionFailure.ItemNotAvailable);
        if (kind != facts.Kind) return Refused(GameSessionFailure.InventoryMismatch);
        // ARC-SCRIPT-2386 (IV15, throw node 0x42AB90): an item that is switched off (a script's toggle; the map data's own bit is not read here) is not thrown; the window does not offer it either.
        if (!gold && QueryItemOff(_actor.Map, request.Item, dataOff: false)) return Refused(GameSessionFailure.ItemNotAvailable);
        var worn = _equipment.SlotOf(request.Item) is { } slot ? _equipment.Worn[slot] : null;
        var combat = worn?.Item ?? request.Combat;
        var weapon = kind.Type == SessionItemType.Weapon;
        if (worn is null && request.Combat is not null && !EquipmentRules.IsValid(request.Combat) ||
            weapon != (combat is { Kind: EquipmentItemKind.Weapon, Weapon: not null })) return Refused(GameSessionFailure.InvalidItem);
        // The lift's quantity split (0x4658E0) takes gold and ammunition another way: the hero's gold pile (ARC-UI-090) and an ammunition stack split off
        // the count the lift asked for (ARC-UI-094, ARC-UI-086), another stack is thrown whole.
        var ammoSplit = kind.Type == SessionItemType.Ammo;
        var split = ammoSplit || gold;
        if (split && request.Count is null) return Refused(GameSessionFailure.AttackUnsupported);
        if (ammoSplit && worn is not null) return Refused(GameSessionFailure.InvalidItem);
        if (split && (gold ? SplitGoldRefusal(request.Count!.Value, request.SplitKind) : SplitAmmoRefusal(request.Item, request.Count!.Value, request.SplitKind)) is var splitFailure
            && splitFailure != GameSessionFailure.None) return Refused(splitFailure);
        var points = worn?.ScriptPoints ?? facts.ScriptPoints;
        // ARC-COMBAT-1955: the removal's scripts (0x467860: wield off for a worn item, then remove-item; the stack path of the split runs none, QW5) and the throw script (point 6) run under the host. ARC-COMBAT-2182: the throw script of a split
        // stack runs for the stack the split makes (its entity is planned before the scripts, RunHeroThrowScripts). NOT READY (refused): a throw script together with an insert-item script (the give-back of a refusing throw would run it, 0x4617F0).
        var removal = worn is not null && points.Contains(SessionScriptPoint.WieldOff) || !split && points.Contains(SessionScriptPoint.RemoveItem);
        // A worn item's own record keeps only the wear events' points (21, 24, 25, 28), so the throw and insert-item points are read from the host's facts too.
        var throwScript = points.Contains(SessionScriptPoint.Throw) || facts.ScriptPoints.Contains(SessionScriptPoint.Throw);
        if ((removal || throwScript) && request.Scripts is null ||
            throwScript && (points.Contains(SessionScriptPoint.InsertItem) || facts.ScriptPoints.Contains(SessionScriptPoint.InsertItem)))
            return Refused(GameSessionFailure.ItemScriptUnsupported);
        if (!split && (facts.NoDrop || worn?.Item.NoDrop == true)) return Refused(GameSessionFailure.ItemNotDroppable);
        var triggers = request.ItemTriggers;
        if (triggers is null && (worn?.ItemSpells == true || HasItemRuns(request.Item))) return Refused(GameSessionFailure.ItemEffectUnsupported);
        // A wielded boomerang away on its own shot is not in the hand to throw (its shot still names it).
        if (_shots.Values.Any(pending => pending.Boomerang && pending.WeaponItem == request.Item)) return Refused(GameSessionFailure.WeaponInFlight);

        if (removal || throwScript)
        {
            // ARC-COMBAT-2176: with a struck critter the scripts run only when the attack on it would not refuse the throw, judged on the hero as it is before them.
            var scripted = RunHeroThrowScripts(expected, request, worn, points, split, kind, throwScript,
                struck ? ThrownHero(request, ammoSplit, weapon, combat, out _) : null);
            if (scripted.Done is { } done) return done;
            (runs, applied) = (scripted.Runs, scripted.Applied);
            if (applied) expected = Version;
        }

        var hero = ThrownHero(request, ammoSplit, weapon, combat, out var range);
        var wornBefore = worn is not null;
        SessionHeroThrowResult? result = null;
        if (struck)
        {
            // ARC-DEV-261: no TakingDamage here: the throw always flies and its hit is deferred, so the script is asked at the landing (LandShot with the host's SessionAttack).
            var attack = HeroThrowAttack(request, triggers);
            var attacked = CritterAttack(expected, attack, null, new PackThrow(request.Item, request.Target, hero) { Split = split ? (request.Count!.Value, request.SplitKind!.Value) : null });
            // ARC-COMBAT-1955: a struck critter that is dead no longer refuses the attack (ARC-COMBAT-2392); the host would throw again without it, but the scripts ran already and must not run twice, so after them the session throws at the location itself.
            // ARC-COMBAT-2390: so does any other refusal of the attack that only the scripts' runs could bring about (the preflight judged the throw before them): the original's throw flies in any case (its hit is
            // deferred, ARC-DEV-261), so the item is thrown at the location below, whose own checks (worn items, an inactive hero, the budget, the version, the revision) refuse what binds a throw of any kind.
            var late = runs is not null && attacked.Disposition is GameSessionDisposition.NoChange or GameSessionDisposition.Rejected;
            if (attacked.Disposition == GameSessionDisposition.Rejected && runs is null) return Refused(attacked.Failure);
            if (late) struck = false;
            else
            {
                result = new(attacked.Disposition, GameSessionFailure.None, attacked, Version) { Destination = attacked.Scatter };
                if (attacked.Shots.Count == 0) return With(result);
            }
        }
        if (result is null)
        {
            // The hero's own checks, as the attack's: its worn items; an inactive hero throws nothing (the press puts it back, 0x54E021).
            if (!ValidWear(new SessionWearSide { Life = SessionLifeTarget.Hero, Worn = request.HeroWorn }, new SessionWearSide(), targetBroken: false, wear: null))
                return Refused(GameSessionFailure.InvalidWear);
            var life = _life.Hero!.Value.State;
            if (!LifeRules.Evaluate(life, HeroLifeFacts(life)).IsActive) return With(new(GameSessionDisposition.NoChange, GameSessionFailure.None, null, Version));
            if (ShotsInFlight > MaxShotsInFlight - 1) return Refused(GameSessionFailure.BudgetExceeded);
            if (!IsCurrent(expected)) return Refused(GameSessionFailure.StaleVersion);
            if (_revision == long.MaxValue) return Refused(GameSessionFailure.RevisionExhausted);
            var thrownItem = split ? SplitForThrow(request.Item, request.Count!.Value, request.SplitKind!.Value) : request.Item;
            Throw(thrownItem, null);
            // A shot without a target: any critter it strikes on the way takes a forced hit (LandShot), its item lands where it ends.
            var shots = Launch(new PendingShot(ShotFacts.Of(hero), AttackResolver.ForcedHit(false), false, null, null)
                { Thrown = new SessionThrownShot(thrownItem, range), WeaponItem = thrownItem }, 1);
            result = new(GameSessionDisposition.Changed, GameSessionFailure.None,
                new(GameSessionDisposition.Changed, GameSessionFailure.None, null, null, null, null, null, GameSessionFailure.None, Version) { Shots = shots },
                Version) { Destination = request.Target };
        }
        if (triggers is null || split) return With(result);
        return With(result with { RemovalSpells = RemovalSpellSteps(triggers, request.Item, wornBefore), Version = Version });
    }

    // The hero as it throws: the throwing skill, the item's thrown range (the check's and the flight's, PF9) and, as the attack's weapon range (0x465E30: a weapon's range field and magic adjustment, anything else 1), its reach.
    private SessionHeroAttack ThrownHero(SessionHeroThrow request, bool ammoSplit, bool weapon, EquipmentItem? combat, out int range)
    {
        var kind = request.Facts.Kind;
        var carry = HeroCarryFacts();
        var weight = carry is { } held ? Math.Clamp(HeroItemWeight(held, ammoSplit ? CarriedUnits(request.Item, request.Count!.Value) : Carried(request.Item, kind.Type)), 0, ThrowRules.MaxWeight) : 0;
        var hero = QueryHeroAttack()!;
        var throwing = HeroSkills()[Arcanum.Simulation.Skills.Skill.Throwing];
        range = ThrowRules.Range(hero.Combatant.Strength, throwing.Training, weight);
        var aptitude = HeroAptitude();
        var reach = weapon ? EquipmentRules.Weapon(new Dictionary<EquipmentSlot, EquipmentItem> { [EquipmentSlot.Weapon] = combat! }, aptitude).Range : 1;
        return hero with
        {
            Weapon = ThrowRules.ThrownWeapon(combat, request.Throwable, weight, aptitude, range), Skill = CombatSkill.Throwing,
            SkillLevel = throwing.Level, Training = throwing.Training, Range = reach, Supported = true,
        };
    }

    // The attack on the struck critter that the throw makes (ARC-DEV-261: no TakingDamage here: the throw always flies and its hit is deferred, so the script is asked at the landing, LandShot with the host's SessionAttack).
    private SessionAttack HeroThrowAttack(SessionHeroThrow request, ISessionItemTriggers? triggers) => new(request.Struck!)
    {
        Flight = true,
        Distance = ProjectileRules.Distance(_actor.Cell, request.StruckCell!.Value),
        LineOfFire = request.LineOfFire,
        HeroWorn = request.HeroWorn,
        TargetWorn = request.StruckWorn,
        TargetCell = request.StruckCell,
        TargetLight = request.StruckLight,
        DropMap = request.DropMap,
        ItemTriggers = triggers,
    };

    // The removal's spell steps of a hero's item leaving its list or slot (ARC-COMBAT-023): a worn item's unwear executor, then the removal's.
    private static IReadOnlyList<SessionItemTriggerResult> RemovalSpellSteps(ISessionItemTriggers triggers, string item, bool worn)
    {
        var spells = new List<SessionItemTriggerResult>();
        if (worn) spells.Add(triggers.Withdraw(ItemSpellTriggerRules.Unwear, item, SessionEventKinds.PlayerEntity));
        spells.Add(triggers.Remove(item, SessionSpellCaster.Hero));
        return spells.AsReadOnly();
    }

    /// <summary>ARC-COMBAT-023: the thrown range of one of the hero's items now (<see cref="ThrowRules.Range"/>: its weight with the hero as
    /// holder under the hero's effective strength and throwing training), for the host's cursor and the flight's end; null without a hero
    /// or for an item the hero does not hold.</summary>
    public int? QueryHeroThrowRange(string item, int? units = null)
    {
        ThrowIfDisposed();
        if (!SessionValidation.Entity(item)) throw new ArgumentException("Invalid entity id.", nameof(item));
        if (_hero is null || (item == SessionHeroGrid.GoldEntity ? _inventory.Gold > 0 ? SessionHeroGrid.GoldKind : (SessionItemKind?)null : HeldKind(item)) is not { } kind) return null;
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
