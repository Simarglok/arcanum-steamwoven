using Arcanum.Simulation.Movement;

namespace Arcanum.Session;

/// <summary>ARC-DEV-086 (MIG6c): where a created made item lands.</summary>
internal enum SessionCreatePlace
{
    /// <summary>The end of the hero's made items.</summary>
    HeroList,
    /// <summary>The end of an NPC's added items.</summary>
    NpcList,
    /// <summary>The end of a container's added items.</summary>
    ContainerList,
    /// <summary>The ground of a map, as a placed item.</summary>
    Ground,
}

/// <summary>
/// ARC-DEV-086 (MIG6c): the request of <see cref="GameSessionOwner.CreateMadeItem"/>: an entity from <see cref="GameSessionOwner.NextObjectEntity"/>
/// (or one an assembly planned), its kind, its weight facts, its complexity (what the item keeps in its list or its ground record) and the
/// place: a holder for the lists, a map, a cell and the placed item's source for the ground.
/// </summary>
internal readonly record struct SessionMadeCreation(string Entity, SessionItemKind Kind, SessionCreatePlace Place, SessionItemWeight Weight, int Complexity = 0,
    string? Holder = null, SessionMapId Map = default, CellCoord Cell = default, SessionPlacedSource Source = SessionPlacedSource.Created);

public sealed partial class GameSessionOwner
{
    // ---- Create (TM4, GR4) ----

    /// <summary>
    /// ARC-DEV-086 (MIG6c): the entity of the next object of a made, junk or pool origin, or null when the origin's numbers run out
    /// (<see cref="int.MaxValue"/> is reserved, so no number reaches the bound of an entity). It reserves nothing: the object that takes
    /// the entity is made by <see cref="CreateMadeItem"/>, <see cref="CreateJunkPile"/> or <see cref="CreateBloodPool"/> before another
    /// is asked for.
    /// </summary>
    internal string? NextObjectEntity(SessionObjectOrigin origin)
    {
        // A made number is valid up to int.MaxValue itself; the junk and pool numbers keep the one below it (their allocator's bound).
        if (origin == SessionObjectOrigin.Made) return NextMadeNumber() is var made && made <= int.MaxValue ? SessionMadeItem.EntityOf((int)made) : null;
        var number = NextObjectNumber(origin);
        if (number == int.MaxValue) return null;
        return origin switch
        {
            SessionObjectOrigin.JunkPile => SessionJunkPile.EntityPrefix + number.ToString(System.Globalization.CultureInfo.InvariantCulture),
            SessionObjectOrigin.BloodPool => SessionBloodPool.EntityPrefix + number.ToString(System.Globalization.CultureInfo.InvariantCulture),
            SessionObjectOrigin.Missile => SessionMissile.EntityPrefix + number.ToString(System.Globalization.CultureInfo.InvariantCulture),
            SessionObjectOrigin.Projectile => SessionProjectile.EntityPrefix + number.ToString(System.Globalization.CultureInfo.InvariantCulture),
            SessionObjectOrigin.Scenery => SessionCreatedObject.SceneryPrefix + number.ToString(System.Globalization.CultureInfo.InvariantCulture),
            SessionObjectOrigin.EyeCandy => SessionCreatedObject.EyeCandyPrefix + number.ToString(System.Globalization.CultureInfo.InvariantCulture),
            SessionObjectOrigin.Critter => SessionCreatedObject.CritterPrefix + number.ToString(System.Globalization.CultureInfo.InvariantCulture),
            _ => throw new ArgumentOutOfRangeException(nameof(origin)),
        };
    }

    /// <summary>
    /// ARC-DEV-086 (MIG6c): the one creation of a made item (<c>made:n</c>): the item joins the hero's made items, an NPC's or container's
    /// added items or the ground of a map, is recorded as taken (no seed can list it, except in the hero's own list where its record
    /// already says so) and keeps its weight facts. The caller made the checks (budgets, the host's insert check, the revision) and keeps
    /// the revision and its own records (ammunition units, gold coins, decay). A created item has no map row, so its place is read from
    /// the stores by <see cref="QueryObject"/>.
    /// </summary>
    internal SessionMadeItem CreateMadeItem(in SessionMadeCreation creation)
    {
        var item = new SessionMadeItem(creation.Entity, creation.Kind, creation.Complexity);
        switch (creation.Place)
        {
            case SessionCreatePlace.HeroList:
                _inventory.Add(item);
                break;
            case SessionCreatePlace.NpcList:
                _inventory.AddToNpc(creation.Holder ?? throw new ArgumentException("HOLDER_REQUIRED", nameof(creation)), item);
                _inventory.Taken.Add(creation.Entity);
                // ARC-WORLD-1284 (VPG1): the insert 0x466640 sets NPC flag 0x40 on the receiver (a new object never merges here).
                MarkCheckWield(creation.Holder);
                break;
            case SessionCreatePlace.ContainerList:
                _inventory.AddToContainer(creation.Holder ?? throw new ArgumentException("HOLDER_REQUIRED", nameof(creation)), item);
                _inventory.Taken.Add(creation.Entity);
                break;
            case SessionCreatePlace.Ground:
                _inventory.Hero.AddPlaced(new(creation.Entity, creation.Kind, creation.Source, null, creation.Map, creation.Cell, creation.Complexity));
                _inventory.Taken.Add(creation.Entity);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(creation));
        }
        _inventory.SetWeight(creation.Entity, creation.Weight);
        return item;
    }

    /// <summary>
    /// ARC-DEV-086 (MIG6c): the one creation of a junk pile: it takes the next pile number (the high-water mark moves to it, so the
    /// number is never given again, TM1) and stands on <paramref name="cell"/> of <paramref name="map"/> with <paramref name="items"/>.
    /// Null, and nothing written, beyond the bounds of piles and pile items or when the pile numbers run out. The caller keeps the revision.
    /// </summary>
    internal SessionJunkPile? CreateJunkPile(SessionMapId map, CellCoord cell, IReadOnlyList<SessionJunkPileItem> items)
    {
        if (_inventory.Objects.Piles.Count >= SessionJunkPile.MaxPiles || _inventory.Objects.PileItems + items.Count > SessionJunkPile.MaxItems) return null;
        if (NextObjectEntity(SessionObjectOrigin.JunkPile) is null) return null;
        var number = AllocateObjectNumber(SessionObjectOrigin.JunkPile);
        var pile = new SessionJunkPile(SessionJunkPile.EntityPrefix + number.ToString(System.Globalization.CultureInfo.InvariantCulture), map, cell,
            Array.AsReadOnly(items.ToArray()));
        _inventory.Objects.AddPile(pile);
        return pile;
    }

    /// <summary>
    /// ARC-DEV-086 (MIG6c): the one creation of a pool of blood: the pool takes the next pool number (never given again) and is
    /// recorded. The caller made the checks and keeps the revision; the pool's facts are the caller's (<see cref="LeaveBloodPool"/>).
    /// </summary>
    internal SessionBloodPool CreateBloodPool(string critter, long deathTimeMs, SessionMapId map, CellCoord cell, int offsetX, int offsetY, long createdMs)
    {
        var number = AllocateObjectNumber(SessionObjectOrigin.BloodPool);
        var pool = new SessionBloodPool(SessionBloodPool.EntityPrefix + number.ToString(System.Globalization.CultureInfo.InvariantCulture),
            critter, deathTimeMs, map, cell, offsetX, offsetY, createdMs);
        _inventory.Objects.AddPool(pool);
        return pool;
    }

    // ---- Destroy (TM4, GR4, B04) ----

    /// <summary>
    /// ARC-DEV-086 (MIG6c): the one destruction of an object of the session's stores, whichever its origin.
    /// <list type="bullet">
    /// <item><b>An item</b> (the deletion of ARC-WORLD-053): out of its wear slot, the hero's and NPCs' lists, the ground and its junk pile,
    /// hidden from every seed (taken), its condition, uses, charges, units, coins, given and identified records, kept script slots and
    /// weight facts dropped, recorded as deleted and its decay cancelled. Then the pile it left is checked as the original's removal does
    /// (JP5, JP7): with no container shown in the loot window, a pile left with one item or none dissolves at once
    /// (<see cref="DissolveJunkPile"/>; the last item is dropped again with a decay two days on). While the window shows the pile the
    /// pile stays, and <see cref="CloseJunkPile"/> dissolves it on closing; the window's own loot (<see cref="LootJunkPile"/>) never
    /// dissolves it. This is the check every destroyer shares, so a pile an item left by any deletion goes the same way (B04: an emptied
    /// pile no longer stays by a path that skipped the check).</item>
    /// <item><b>A junk pile</b>: the pile's record only; its items stay on the ground where they lie.</item>
    /// <item><b>A pool of blood</b>: the pool's record (a decayed pool is purged here; its number is never given again).</item>
    /// <item><b>A created scenery or eye-candy object</b> (ARC-DEV-347): its record; its number is never given again. <b>A created critter</b> (ARC-DEV-359, ARC-DEV-371): its record and its written flags.</item>
    /// </list>
    /// <paramref name="windowShowsContainer"/> is a caller that knows the host's loot window shows a container (the check then waits for
    /// the close); the session also reads it from the event world of the pass it runs in (the decay), and outside a pass it knows none, so
    /// a destroy there checks the pile at once (a limit: the host's window state is not session state).
    /// No revision (the caller's change carries it). Not modelled: the destroy script of point 2, which may refuse (JP7; hosts run it
    /// before they ask), an NPC's leave of its leader and the spell runs that name the object (TM4: the session's NPCs are records of
    /// the window's host, MIG7–MIG9).
    /// </summary>
    internal void DestroyObject(string entity, bool windowShowsContainer = false)
    {
        switch (ObjectOriginOf(entity))
        {
            case SessionObjectOrigin.JunkPile:
                _inventory.Objects.RemovePile(entity);
                return;
            case SessionObjectOrigin.BloodPool:
                _inventory.Objects.RemovePool(entity);
                return;
            case SessionObjectOrigin.Scenery:
            case SessionObjectOrigin.EyeCandy:
                _inventory.Objects.RemoveCreated(entity);
                return;
            case SessionObjectOrigin.Critter:
                // ARC-DEV-386 (E1-c4): what the critter holds is destroyed with it (the original destroys an object's inventory with it, 0x43CCA0 on each entry), and its holder leaves the seed.
                _destroyingHolder = true;
                try { foreach (var held in _inventory.AddedTo(entity).Select(item => item.Entity).ToArray()) Discard(held); }
                finally { _destroyingHolder = false; }
                _inventory.NpcWorn.Remove(entity);
                _inventory.HolderSlots.Remove(entity);
                foreach (var seed in _inventory.Seeds.Values) if (seed.RemoveCreatedHolder(entity)) _inventory.SeedGeneration++;
                _inventory.Objects.RemoveCreated(entity);
                // ARC-MAGIC-053 (E1-e): a summoned creature the mind control made a follower leaves the party with its destruction (the record would otherwise name an object that is gone).
                if (_party.Get(entity) is var following && (following.Standing != CompanionStanding.Free || following.WaitFlag))
                {
                    _party.Put(following with { Standing = CompanionStanding.Free, Forced = false, WaitFlag = false }, false);
                    _combat.Concealed.Remove(entity);
                    ClearNoAttackOnLeave(entity);
                    if (following.WaitBit) CancelWaitTimers(entity);
                }
                // ARC-DEV-371: the flags written on it (the encounter flag every script-made critter carries) go with it, so a long run of created critters does not fill the written-flags bound; its other state records stay (E1-c1).
                _objectFlags = _objectFlags.Remove(entity);
                // ARC-MAGIC-040: the monster body a Trait component put on it goes too (the same bound).
                _traitArt = _traitArt.Remove(entity);
                // ARC-WORLD-1157 (PO7): the slot mark goes with the critter; a destroy releases nothing (the count of its slot stays).
                _generators.DropCopy(entity);
                // ARC-WORLD-1158 (0x45EC80): a pending Decay Dead Bodies event of the critter goes before its destroy.
                CancelBodyDecay(entity);
                return;
            case SessionObjectOrigin.Missile:
                _missileObjects.Remove(entity);
                return;
            case SessionObjectOrigin.Projectile:
                _projectileObjects.Remove(entity);
                return;
        }
        DestroyItem(entity, windowShowsContainer);
    }

    // True while a created critter's list is destroyed with it: the NPC is going, so its items' removals mark nothing.
    private bool _destroyingHolder;

    private void DestroyItem(string entity, bool windowShowsContainer)
    {
        var pile = _inventory.Objects.PileOf(entity);
        // ARC-WORLD-1284 (AM5): the destroy 0x43CCA0 takes an item an NPC holds out of its list first (0x467860), which sets NPC flag 0x40 on the NPC; read before the records go.
        if (!_destroyingHolder) MarkCheckWield(NpcHolderOfItem(entity));
        RemoveItemRecords(entity);
        // ARC-WORLD-063 (JP5, JP7): the pile the item left is checked unless a container window is open (the counter 0x5E8800).
        if (pile is not null && _lootedPile != pile.Entity && !windowShowsContainer && !LootWindowShowsContainer() && _inventory.Objects.Pile(pile.Entity) is { } left)
            DissolveJunkPile(left);
    }

    // The pile the host's loot window is taking an item from right now (LootJunkPile): the window shows it, so no destroy inside the
    // loot dissolves it (the original's open-window counter, JP5).
    private string? _lootedPile;

    private bool LootWindowShowsContainer() => PassWorldAs<ISessionLootWindowWorld>() is { LootWindowShowsContainer: true };

    /// <summary>The deletion of an item: out of its wear slot and the PC's lists, hidden from every seed, its record dropped.</summary>
    private void RemoveItemRecords(string entity)
    {
        if (_equipment.SlotOf(entity) is { } slot) _equipment.Worn.Remove(slot);
        _inventory.Hero.Remove(entity);
        _inventory.RemoveFromNpc(entity);
        // ARC-WORLD-103: a deleted item an NPC wore is worn no more.
        _inventory.Unwear(entity);
        // ARC-WORLD-066: a deleted ammunition stack is gone with its units; ARC-WORLD-071: a gold pile with its coins, a given item with
        // its origin.
        _inventory.Ammo.Remove(entity);
        _inventory.Piles.Remove(entity);
        _inventory.Hero.TakeGiven(entity);
        // ARC-WORLD-090: and its identified flag.
        _inventory.Identified.Remove(entity);
        // ARC-SCRIPT-1279: and the fields a script wrote on it.
        _inventory.ItemFields.Remove(entity);
        // ARC-RES-1441: and the slot an arrangement gave it (the save refuses a slot of a deleted item).
        _inventory.ForgetSlot(entity);
        // ARC-SCRIPT-042: its kept script slots go with it.
        foreach (var key in _inventory.ScriptSlots.Keys.Where(key => key.Entity == entity).ToArray()) _inventory.ScriptSlots.Remove(key);
        // ARC-WORLD-085: its weight facts go with it.
        _inventory.Weights.Remove(entity);
        // ARC-WORLD-063: a deleted item leaves its junk pile.
        _inventory.Objects.LeaveItem(entity);
        // Taken hides an item from every seed view and from pickup and loot.
        _inventory.Taken.Add(entity);
        _durability.Records.Remove(entity);
        _durability.Uses.Remove(entity);
        _durability.Charges.Remove(entity);
        _durability.Destroyed.Add(entity);
        // ARC-WORLD-062: a deleted item no longer decays.
        CancelDecay(entity);
    }
}
