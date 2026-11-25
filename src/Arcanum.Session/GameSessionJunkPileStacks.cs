using Arcanum.Simulation.Items;

namespace Arcanum.Session;

public sealed partial class GameSessionOwner
{
    /// <summary>
    /// ARC-UI-1328: moves <paramref name="count"/> coins of the gold pile <paramref name="item"/> that lies in the junk pile
    /// <paramref name="pile"/> of the actor's map to the hero: the quantity window's move out of the loot window (the gold move
    /// <see cref="TakeGoldFromNpc"/> makes for a body or a container, with the pile given, NT1). A pile with exactly that many coins is destroyed and
    /// leaves the junk pile; a larger one stays in it with the rest, so the junk pile still holds the same items and its dissolve rules
    /// (<see cref="CloseJunkPile"/>) are unchanged: the window shows the pile while it is taken from, so even a pile left with no item
    /// stands until the window closes (JP5). The gold item is a pile the hero dropped (a placed item) or a map's loose pile that joined
    /// the junk pile. The refusals are <see cref="TakeGoldFromNpc"/>'s: an invalid input, the version, a pile the session does not know
    /// or one of another map (<see cref="GameSessionFailure.UnknownHolder"/>), an item the junk pile does not hold or that is no gold
    /// pile (<see cref="GameSessionFailure.ItemNotAvailable"/>), no hero (<see cref="GameSessionFailure.GoldUnavailable"/>), fewer coins
    /// (<see cref="GameSessionFailure.GoldShort"/>), a budget or the revision. A count of 0 changes nothing. One revision.
    /// </summary>
    public InventoryMutationResult TakeGoldFromJunkPile(GameSessionVersion expected, string pile, string item, int count, ISessionItemScriptHost? scripts = null)
    {
        ThrowIfDisposed();
        if (!SessionValidation.Entity(pile) || !SessionValidation.Entity(item) || count < 0) return RejectItem(GameSessionFailure.InvalidItem);
        if (!IsCurrent(expected)) return RejectItem(GameSessionFailure.StaleVersion);
        if (JunkPileStack(pile, item, SessionItemType.Gold, out var found, out var facts, out var failure) is null) return RejectItem(failure);
        if (_hero is null) return RejectItem(GameSessionFailure.GoldUnavailable);
        if (count == 0) return new(GameSessionDisposition.NoChange, GameSessionFailure.None, null, null, Version);
        var coins = _inventory.Piles.TryGetValue(item, out var tracked) ? tracked : facts?.Quantity ?? 0;
        var outer = _lootedPile;
        _lootedPile = found.Entity;
        try
        {
            var result = TakeGold((item, coins, facts), count, pile, scripts);
            // The pile the move emptied is destroyed (it left the junk pile), so its decay ends (the whole loot does the same).
            if (result.Disposition == GameSessionDisposition.Changed && coins == count) CancelDecay(item);
            return result;
        }
        finally { _lootedPile = outer; }
    }

    /// <summary>
    /// ARC-UI-1328: moves <paramref name="count"/> units of the ammunition stack <paramref name="item"/> in the junk pile
    /// <paramref name="pile"/> of the actor's map to the hero (the ammunition move <see cref="TakeAmmoFromNpc"/> makes for a
    /// body or a container, with the stack given, AM4): a stack with exactly that many is deleted and leaves the junk pile, a larger one stays in it with the
    /// rest; the hero's first stack of the type grows or a new <c>made:n</c> stack of <paramref name="newStackKind"/> enters its list after the host's
    /// insert check. The junk pile's items and dissolve rules are those of <see cref="TakeGoldFromJunkPile"/>. The refusals are
    /// <see cref="TakeAmmoFromNpc"/>'s with the junk pile as the holder. A count of 0 changes nothing. One revision.
    /// </summary>
    public InventoryMutationResult TakeAmmoFromJunkPile(GameSessionVersion expected, string pile, string item, int count, SessionItemKind newStackKind,
        SessionInsertCheck insertCheck, ISessionItemScriptHost? scripts = null)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(insertCheck);
        if (!SessionValidation.Entity(pile) || !SessionValidation.Entity(item)) return RejectItem(GameSessionFailure.InvalidItem);
        if (count < 0 || newStackKind.Type != SessionItemType.Ammo) return RejectItem(GameSessionFailure.InvalidAmmo);
        if (!IsCurrent(expected)) return RejectItem(GameSessionFailure.StaleVersion);
        if (JunkPileStack(pile, item, SessionItemType.Ammo, out var found, out var facts, out var failure) is not { } kind) return RejectItem(failure);
        if (AmmoOf(item, facts ?? InventoryDomain.MadeFacts(kind)) is not { } source) return RejectItem(GameSessionFailure.ItemNotAvailable);
        if (count == 0) return new(GameSessionDisposition.NoChange, GameSessionFailure.None, null, null, Version);
        if (source.Units < count) return RejectItem(GameSessionFailure.AmmoShort);
        // ARC-SCRIPT-2855 (AM4): the stack's insert-item script (21) runs first with the hero as the triggerer.
        var insert = RunPileInsert(scripts, source.Entity, SessionEventKinds.PlayerEntity, npcTriggerer: false, source.Units == count ? pile : null);
        if (insert.Failure != GameSessionFailure.None) return insert.Refused(RejectItem(insert.Failure));
        var into = HeroStackOfType(source.AmmoType);
        var outer = _lootedPile;
        _lootedPile = found.Entity;
        try
        {
            var report = new CreatedScripts { Defer = true };
            var result = MoveUnits(source, count, into, into is null ? () => NewHeroStack(newStackKind, source.AmmoType, count, insertCheck, scripts, report) : null, insert, report);
            if (result.Disposition == GameSessionDisposition.Changed && source.Units == count) CancelDecay(item);
            return result;
        }
        finally { _lootedPile = outer; }
    }

    // The stack item of a junk pile of the actor's map as the loot window lists it: the pile and the item's kind (null with the failure
    // when the pile is unknown or of another map, the pile does not hold the item, or the item is not of the wanted type). A placed
    // item must lie on the pile's cell; a map's loose item (a seed item) must still lie there and not be taken (LootJunkPileItem's checks).
    private SessionItemKind? JunkPileStack(string pile, string item, SessionItemType type, out SessionJunkPile found, out SessionItemFacts? facts, out GameSessionFailure failure)
    {
        facts = null;
        found = null!;
        if (_inventory.Objects.Pile(pile) is not { } listed || listed.Map != _actor.Map)
        {
            failure = GameSessionFailure.UnknownHolder;
            return null;
        }
        found = listed;
        failure = GameSessionFailure.ItemNotAvailable;
        if (listed.Items.All(entry => entry.Entity != item)) return null;
        SessionItemKind kind;
        if (_inventory.PlacedItem(item) is { } placed)
        {
            if (placed.Map != listed.Map || placed.Cell != listed.Cell) return null;
            kind = placed.Kind;
        }
        else
        {
            if (!_inventory.Seeds.TryGetValue(_actor.Map, out var seed))
            {
                failure = GameSessionFailure.InventoryUnavailable;
                return null;
            }
            if (!seed.Items.TryGetValue(item, out var row) || _inventory.Taken.Contains(item) || LooseCell(_actor.Map, seed, item) != listed.Cell) return null;
            facts = row.Facts;
            kind = row.Facts.Kind;
        }
        if (kind.Type != type) return null;
        failure = GameSessionFailure.None;
        return kind;
    }
}
