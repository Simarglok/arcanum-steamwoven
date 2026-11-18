using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Items;

namespace Arcanum.Session;

/// <summary>
/// The result of <see cref="GameSessionOwner.DamageItemUnattacked"/> (ARC-COMBAT-025): the disposition and failure, the item's durability result
/// (null when nothing was applied) and what the host does (<see cref="Host"/>: the trap, the bust script, the sounds; the witnesses of an
/// attacker are not in it, there is no attacker).
/// </summary>
public sealed record SessionItemDamageResult(GameSessionDisposition Disposition, GameSessionFailure Failure, ItemDurabilityResult? Item,
    SessionObjectHostSteps Host, GameSessionVersion Version);

public sealed partial class GameSessionOwner
{
    /// <summary>
    /// ARC-COMBAT-025 (ledger OD1–OD4, OD12, DU1–DU4, DH9): the combat values and kind the damage step reads for an item (types 5..14), lying on the
    /// ground or carried: its stored material and resistances (clamped 0..95 as the object resistance getter 0x43D6D0 does), invulnerable at the item's
    /// object flag. The kind is <see cref="AttackTargetKind.OtherObject"/>: only portals and containers take a material reduction (OD3). Null for an
    /// invalid item or facts (anything but an item's).
    /// </summary>
    public (Combatant Combatant, AttackTargetKind Kind)? QueryItemCombat(SessionWearItem item, SessionObjectBaseline facts)
    {
        ThrowIfDisposed();
        if (!ValidItemDamage(item, facts)) return null;
        return (ObjectDefender(facts, facts.Invulnerable || item.Invulnerable), AttackTargetKind.OtherObject);
    }

    /// <summary>
    /// ARC-COMBAT-025: damage that is no attack of the hero (a script's damage, a spell, an acid, a burn, a trap) reaches an item (OD1–OD4, OD12): the
    /// damage step's hit-point total <paramref name="amount"/> (<see cref="QueryItemCombat"/> gave it the defender) is added to the item's hit-point
    /// damage as armour and weapon wear add theirs (<see cref="DamageItem"/>); at current hit points 0 or less the item breaks (first broken, then
    /// deleted; a worn item leaves its slot at the first break). A negative amount counts as 0. The item is either lying on the ground
    /// (<paramref name="onGround"/>: the seed's loose items, not taken, deleted or in a junk pile or container) or carried by a critter the session lists, as the
    /// caller says it is: the session only refuses an item it does not track (<see cref="GameSessionFailure.ItemNotAvailable"/>).
    /// The state is the item's durability records, so the snapshot needs no schema step. <see cref="SessionItemDamageResult.Host"/> names the trap
    /// (a trapped item that broke), the bust script and the sounds the host owns. Not modelled: the item's taking-damage script (point 0x17), and the
    /// witnesses (the hit has no attacker). Refused with <see cref="GameSessionFailure.InvalidItem"/> for an invalid item or facts,
    /// <see cref="GameSessionFailure.ObjectUnavailable"/> for a deleted item or one no longer on the ground, and the version, revision and record
    /// budget failures.
    /// </summary>
    public SessionItemDamageResult DamageItemUnattacked(GameSessionVersion expected, SessionWearItem item, SessionObjectBaseline facts, int amount, bool onGround)
    {
        ThrowIfDisposed();
        if (!ValidItemDamage(item, facts)) return RejectItemDamage(GameSessionFailure.InvalidItem);
        if (_durability.Destroyed.Contains(item.Entity) || onGround && !OnGround(item.Entity)) return RejectItemDamage(GameSessionFailure.ObjectUnavailable);
        var result = DamageItem(expected, item.Entity, item.Baseline, Math.Clamp(amount, 0, ItemDurabilityLimits.MaxDamageStep), destroyPoint: true);
        if (result.Disposition == GameSessionDisposition.Rejected) return RejectItemDamage(result.Failure);
        if (result.Disposition == GameSessionDisposition.NoChange) return new(GameSessionDisposition.NoChange, GameSessionFailure.None, result, SessionObjectHostSteps.None, Version);
        var host = SessionObjectHostSteps.None;
        if (result.Break != ItemBreakStep.None && facts.Trapped) host |= SessionObjectHostSteps.TrapFires;
        if (result.DestroyKept) host |= SessionObjectHostSteps.DestroyKept;
        host |= result.Break switch
        {
            ItemBreakStep.Broken => SessionObjectHostSteps.BustScript | SessionObjectHostSteps.BustSound,
            ItemBreakStep.Deleted => SessionObjectHostSteps.DestroySound,
            _ => SessionObjectHostSteps.None,
        };
        return new(GameSessionDisposition.Changed, GameSessionFailure.None, result, host, Version);
    }

    private static bool ValidItemDamage(SessionWearItem? item, SessionObjectBaseline? facts) =>
        item is not null && facts is { Kind: DamageableObjectKind.Item } && ValidFacts(facts) && SessionValidation.Entity(item.Entity) &&
        ItemDurabilityRules.IsValid(item.Baseline);

    private SessionItemDamageResult RejectItemDamage(GameSessionFailure failure) =>
        new(GameSessionDisposition.Rejected, failure, null, SessionObjectHostSteps.None, Version);
}
