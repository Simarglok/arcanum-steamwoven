using Arcanum.Simulation.Magic;
using Arcanum.Simulation.Technology;

namespace Arcanum.Session;

/// <summary>Who spends ammunition (ARC-WORLD-066): the hero, or an NPC by its entity (its seed list and the items the session
/// added to it).</summary>
public readonly record struct SessionAmmoHolder(string? Npc)
{
    public static SessionAmmoHolder Hero => new(null);
    public bool IsHero => Npc is null;
}

/// <summary>
/// The outcome of spending ammunition (ARC-WORLD-066, the unit use 0x4658E0 without a destination): the stack spent from, its
/// units after, and whether it was removed (its last units spent: the stack is deleted as the durability deletion deletes an item).
/// </summary>
public readonly record struct SessionAmmoUse(GameSessionDisposition Disposition, GameSessionFailure Failure, string? Stack, int UnitsAfter,
    bool Removed, GameSessionVersion Version);

/// <summary>
/// ARC-WORLD-066: ammunition in the session (contract docs/world/inventory-runtime.md, ledger "Ammunition"). An ammunition item is
/// an item of the hero's or an NPC's list like any other, and the session keeps its ammunition type and unit count
/// (<see cref="SessionAmmoRecord"/>): every one the hero holds or placed, and any other whose units it changed. An item inserted
/// into a holder that already holds a stack of the same prototype merges into it (its units added, the item deleted), as the
/// original's insertion does (0x466640); spending takes units from the holder's stack of an ammunition type and deletes the stack
/// when its last units go (0x4658E0).
/// </summary>
public sealed partial class GameSessionOwner
{
    /// <summary>The ammunition type and units of an ammunition item the session knows: a tracked stack, or an attached seed's
    /// ammunition item that is still there (its seed units); null for anything else.</summary>
    public SessionAmmoRecord? QueryAmmo(string entity)
    {
        ThrowIfDisposed();
        if (!SessionValidation.Entity(entity)) return null;
        if (_inventory.Ammo.TryGetValue(entity, out var stack)) return stack;
        if (_inventory.Taken.Contains(entity)) return null;
        foreach (var seed in _inventory.Seeds.Values)
            if (seed.Items.TryGetValue(entity, out var item) && item.Facts.AmmoType is { } type && item.Facts.Quantity is { } units)
                return new(entity, type, units);
        return null;
    }

    /// <summary>The hero's ammunition stacks in its list order (kit, taken, made); empty without a hero.</summary>
    public IReadOnlyList<SessionAmmoRecord> QueryHeroAmmo()
    {
        ThrowIfDisposed();
        return _hero is null ? [] : Array.AsReadOnly(HeroHeld().Where(item => item.Kind.Type == SessionItemType.Ammo)
            .Select(item => _inventory.Ammo[item.Entity]).ToArray());
    }

    /// <summary>
    /// Spends <paramref name="count"/> units of ammunition type <paramref name="ammoType"/> from a holder (the unit use 0x4658E0 with
    /// no destination, as a shot, a cell payment, an assembly component or a script takes them): the holder's first stack of that
    /// type in its list order (the original finds the one stack its critter field names; a holder has one stack a type unless two
    /// prototypes share a type, which no installed prototype does). Fewer units than the count spend nothing
    /// (<see cref="GameSessionFailure.AmmoShort"/>, also when the holder has no stack); exactly the count deletes the stack (the
    /// durability deletion); more leave the rest. A count of 0 changes nothing. Refused before any effect: an invalid input
    /// (<see cref="GameSessionFailure.InvalidAmmo"/>: a negative count, which the original would add to the source, or a type out of
    /// 0..3), no hero (<see cref="GameSessionFailure.InvalidLifeTarget"/>) or an NPC the session does not know
    /// (<see cref="GameSessionFailure.UnknownHolder"/>), the version, the record and durability budgets
    /// (<see cref="GameSessionFailure.BudgetExceeded"/>), the revision. One revision.
    /// </summary>
    public SessionAmmoUse SpendAmmo(GameSessionVersion expected, SessionAmmoHolder holder, int ammoType, int count)
    {
        ThrowIfDisposed();
        if (count < 0 || ammoType is < 0 or >= SessionAmmoRecord.AmmoTypes || holder.Npc is { } bad && (!SessionValidation.Entity(bad) ||
            bad.StartsWith(SessionMadeItem.Prefix, StringComparison.Ordinal))) return RejectAmmo(GameSessionFailure.InvalidAmmo);
        if (holder.IsHero ? _hero is null : !KnownNpc(holder.Npc!)) return RejectAmmo(holder.IsHero ? GameSessionFailure.InvalidLifeTarget : GameSessionFailure.UnknownHolder);
        if (!IsCurrent(expected)) return RejectAmmo(GameSessionFailure.StaleVersion);
        var stack = holder.IsHero ? HeroStackOfType(ammoType) : NpcStackOfType(holder.Npc!, ammoType);
        if (count == 0) return new(GameSessionDisposition.NoChange, GameSessionFailure.None, stack?.Entity, stack?.Units ?? 0, false, Version);
        if (stack is not { } found || found.Units < count) return new(GameSessionDisposition.Rejected, GameSessionFailure.AmmoShort, stack?.Entity, stack?.Units ?? 0, false, Version);
        var removed = found.Units == count;
        if (removed ? !_durability.Records.ContainsKey(found.Entity) && _durability.Count >= _limits.MaxDurabilityRecords
            : !_inventory.Ammo.ContainsKey(found.Entity) && _inventory.Ammo.Count >= _limits.MaxAmmoRecords) return RejectAmmo(GameSessionFailure.BudgetExceeded);
        if (_revision == long.MaxValue) return RejectAmmo(GameSessionFailure.RevisionExhausted);
        if (removed) Delete(found.Entity);
        else _inventory.Ammo[found.Entity] = found with { Units = found.Units - count };
        _revision++;
        return new(GameSessionDisposition.Changed, GameSessionFailure.None, found.Entity, found.Units - count, removed, Version);
    }

    /// <summary>
    /// ARC-WORLD-066: the engine's charge-cell port for the hero as payer, over the hero's own power-cell stack: each request is
    /// answered by <see cref="ChargeCellRules.Plan"/> under the session's technology rules (TC1), and an applied, paid plan spends
    /// the cells at once (<see cref="SpendAmmo"/>, type 2); an unapplied request writes nothing. <paramref name="itemComplexity"/>
    /// makes the payer an item the hero holds (its parent is the hero). Null without a hero.
    /// </summary>
    public ISpellCellSource? HeroCellSource(int? itemComplexity = null)
    {
        ThrowIfDisposed();
        return _hero is null ? null : new SessionCellSource(this, itemComplexity);
    }

    /// <summary>The hero's power-cell stack as the cell rules see it (null without one).</summary>
    private CellStack? HeroCells() => HeroStackOfType(SessionAmmoRecord.Cells) is { } stack ? new CellStack(stack.Entity, stack.Units) : null;

    private sealed class SessionCellSource(GameSessionOwner owner, int? itemComplexity) : ISpellCellSource
    {
        public CellPlan? LastPlan { get; private set; }

        public SpellCellPayment Pay(SpellCellRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);
            owner.ThrowIfDisposed();
            if (owner._hero is null) { LastPlan = null; return new(false, 0); }
            var payer = itemComplexity is { } complexity
                ? new CellPayer(SpellPayerKind.Item, CellOwnerKind.Critter, owner.HeroCells(), complexity, owner.HeroAptitude())
                : new CellPayer(SpellPayerKind.Critter, CellOwnerKind.Critter, owner.HeroCells());
            var plan = request.Payer != payer.Payer ? new CellPlan(false, CellRefusal.PayerMismatch, 0, null, 0, false, request.Apply)
                : ChargeCellRules.Plan(payer, request.Cost, request.Apply, owner._rules.Technology);
            if (plan.Writes && plan.Need > 0 && owner.SpendAmmo(owner.Version, SessionAmmoHolder.Hero, SessionAmmoRecord.Cells, plan.Need).Disposition != GameSessionDisposition.Changed)
                plan = plan with { Paid = false, Refusal = CellRefusal.TooFewCells };
            LastPlan = plan;
            return new(plan.Paid, plan.Need);
        }
    }

    /// <summary>The hero's held items in list order: kit, taken, made.</summary>
    private IEnumerable<(string Entity, SessionItemKind Kind)> HeroHeld() =>
        _inventory.Kit.Select(item => (item.Entity, item.Kind)).Concat(_inventory.Pc.Select(item => (item.Entity, item.Kind)))
            .Concat(_inventory.Made.Select(item => (item.Entity, item.Kind)));

    /// <summary>The hero's first held stack of an ammunition type, or null.</summary>
    private SessionAmmoRecord? HeroStackOfType(int ammoType) => HeroHeld().Where(item => item.Kind.Type == SessionItemType.Ammo)
        .Select(item => _inventory.Ammo[item.Entity]).Cast<SessionAmmoRecord?>().FirstOrDefault(stack => stack!.Value.AmmoType == ammoType);

    /// <summary>The hero's first held stack of a prototype (the merge target, 0x462930), or null.</summary>
    private SessionAmmoRecord? HeroStackOfPrototype(int prototype) => HeroHeld()
        .Where(item => item.Kind.Type == SessionItemType.Ammo && item.Kind.Prototype == prototype)
        .Select(item => _inventory.Ammo[item.Entity]).Cast<SessionAmmoRecord?>().FirstOrDefault();

    /// <summary>An NPC's current ammunition stacks in its list order: its seed items still in its list (in every attached seed
    /// that lists it), then the items the session added to it, each with its tracked or seed units.</summary>
    private IEnumerable<(SessionAmmoRecord Stack, int Prototype)> NpcStacks(string npc)
    {
        foreach (var seed in _inventory.Seeds.Values)
            if (seed.Holders.TryGetValue(npc, out var holder) && holder.Kind == SessionHolderKind.Npc)
                foreach (var entity in holder.Items)
                    if (!_inventory.Taken.Contains(entity) && !Dropped(entity) && seed.Items[entity].Facts is { AmmoType: { } type, Quantity: { } units } facts)
                        yield return (_inventory.Ammo.TryGetValue(entity, out var tracked) ? tracked : new(entity, type, units), facts.Kind.Prototype);
        foreach (var item in _inventory.AddedTo(npc))
            if (item.Kind.Type == SessionItemType.Ammo) yield return (_inventory.Ammo[item.Entity], item.Kind.Prototype);
    }

    private SessionAmmoRecord? NpcStackOfType(string npc, int ammoType) =>
        NpcStacks(npc).Select(pair => pair.Stack).Cast<SessionAmmoRecord?>().FirstOrDefault(stack => stack!.Value.AmmoType == ammoType);

    /// <summary>The ammunition type and units an item brings into a list: its tracked stack, else its facts' (a seed item); null for
    /// an item that is not ammunition or carries none.</summary>
    private SessionAmmoRecord? AmmoOf(string entity, SessionItemFacts facts) =>
        _inventory.Ammo.TryGetValue(entity, out var tracked) ? tracked
            : facts is { AmmoType: { } type, Quantity: { } units } ? new(entity, type, units) : null;

    private SessionAmmoUse RejectAmmo(GameSessionFailure failure) => new(GameSessionDisposition.Rejected, failure, null, 0, false, Version);
}
