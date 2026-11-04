using Arcanum.Simulation.Hotkeys;
using Arcanum.Simulation.Magic;

namespace Arcanum.Session;

/// <summary>ARC-UI-022: the hero's hotkey bar: the ten entries (keys 1..0) and the two recent actions (the latest first, on the key A).</summary>
public sealed class SessionHotkeyState
{
    public static SessionHotkeyState Empty { get; } = new([.. Enumerable.Repeat(HotkeyEntry.Empty, HotkeyRules.Entries)],
        [HotkeyEntry.Empty, HotkeyEntry.Empty]);

    internal SessionHotkeyState(HotkeyEntry[] entries, HotkeyEntry[] recent)
    {
        Entries = Array.AsReadOnly(entries);
        Recent = Array.AsReadOnly(recent);
    }

    /// <summary>The ten entries, entry 0 on the key 1 and entry 9 on the key 0.</summary>
    public IReadOnlyList<HotkeyEntry> Entries { get; }
    /// <summary>The latest recent action, then the one before it (only spells and skill-bar buttons: the spell pane and the skill bar write them).</summary>
    public IReadOnlyList<HotkeyEntry> Recent { get; }
}

public sealed partial class GameSessionOwner
{
    private HotkeyEntry[] _hotkeys = [.. SessionHotkeyState.Empty.Entries];
    private HotkeyEntry[] _recentActions = [.. SessionHotkeyState.Empty.Recent];
    // Where each entry's item was after the last change (its wear slot, its grid slot), so that a move of the item empties the entry.
    private (Arcanum.Simulation.Equipment.EquipmentSlot? Worn, int? Grid)[] _hotkeyPlaces = new (Arcanum.Simulation.Equipment.EquipmentSlot?, int?)[HotkeyRules.Entries];

    /// <summary>ARC-UI-022: the hotkey bar's entries and recent actions.</summary>
    public SessionHotkeyState QueryHotkeys()
    {
        ThrowIfDisposed();
        return new([.. _hotkeys], [.. _recentActions]);
    }

    /// <summary>
    /// ARC-UI-022: a drop on hotkey entry <paramref name="slot"/> from outside the bar (ledger IP9, HK3): a spell from the spell pane, a
    /// skill-bar button, a spell of an item's pane, or an item from the inventory (0x57E8D0). The drop overwrites the entry; an item the old
    /// entry named goes back to the grid (the first free slot for its size, ARC-UI-019). An item entry's item leaves the grid for the location
    /// 2000 + entry. Refused before any effect, in order: input (<see cref="GameSessionFailure.InvalidHotkey"/>: a slot outside 0..9, an empty or
    /// malformed entry, a spell outside the record numbers), version, no hero (<see cref="GameSessionFailure.HotkeysUnavailable"/>), an item
    /// the hero does not hold, an item entry's item the hero wears or another item entry already holds
    /// (<see cref="GameSessionFailure.ItemNotAvailable"/>; the original takes a worn item off the doll onto the bar: not ready); for an item entry, ammunition or gold (<see cref="GameSessionFailure.InvalidHotkey"/>)
    /// and an item of a prototype another item entry holds (<see cref="GameSessionFailure.ItemNotAvailable"/>; the original's message 4000).
    /// The same entry again is NoChange. An item that moves onto the bar or off it empties every other entry naming it (HK6). One revision.
    /// </summary>
    public GameSessionMutationResult AssignHotkey(GameSessionVersion expected, int slot, HotkeyEntry entry)
    {
        ThrowIfDisposed();
        if (slot is < 0 or >= HotkeyRules.Entries || entry is null || entry.IsEmpty || !ValidHotkey(entry)) return Reject(GameSessionFailure.InvalidHotkey);
        if (!IsCurrent(expected)) return Reject(GameSessionFailure.StaleVersion);
        if (_hero is null) return Reject(GameSessionFailure.HotkeysUnavailable);
        if (entry.Item is { } item && (HeldKind(item) is null || entry.Kind == HotkeyKind.Item &&
            (_equipment.SlotOf(item) is not null || Enumerable.Range(0, HotkeyRules.Entries).Any(i => i != slot && _hotkeys[i].Kind == HotkeyKind.Item && _hotkeys[i].Item == item))))
            return Reject(GameSessionFailure.ItemNotAvailable);
        if (_hotkeys[slot] == entry) return new(GameSessionDisposition.NoChange, GameSessionFailure.None, Version);
        if (entry.Kind == HotkeyKind.Item && HeldKind(entry.Item!) is { } kind)
        {
            if (kind.Type is SessionItemType.Ammo or SessionItemType.Gold) return Reject(GameSessionFailure.InvalidHotkey);
            if (Enumerable.Range(0, HotkeyRules.Entries).Any(i => i != slot && _hotkeys[i].Kind == HotkeyKind.Item && HeldKind(_hotkeys[i].Item!)?.Prototype == kind.Prototype))
                return Reject(GameSessionFailure.ItemNotAvailable);
        }
        if (_revision == long.MaxValue) return Reject(GameSessionFailure.RevisionExhausted);
        var before = ItemEntrySlots();
        _hotkeys[slot] = entry;
        _hotkeyPlaces[slot] = HotkeyPlace(entry.Item);
        EmptyEntriesOfMovedItems(before);
        Revise();
        return new(GameSessionDisposition.Changed, GameSessionFailure.None, Version);
    }

    /// <summary>ARC-UI-022: a drag from entry <paramref name="from"/> to entry <paramref name="to"/> (IP9): the two entries trade places (an
    /// empty target leaves the source empty); items stay off the grid. The original puts a target item back into the inventory and leaves the
    /// source an entry without its object (HK5, a technical slip fixed in both modes). Refused for a slot outside 0..9 or an empty source
    /// (<see cref="GameSessionFailure.InvalidHotkey"/>), a stale version, no hero; the same slot is NoChange. One revision.</summary>
    public GameSessionMutationResult MoveHotkey(GameSessionVersion expected, int from, int to)
    {
        ThrowIfDisposed();
        if (from is < 0 or >= HotkeyRules.Entries || to is < 0 or >= HotkeyRules.Entries) return Reject(GameSessionFailure.InvalidHotkey);
        if (!IsCurrent(expected)) return Reject(GameSessionFailure.StaleVersion);
        if (_hero is null) return Reject(GameSessionFailure.HotkeysUnavailable);
        if (_hotkeys[from].IsEmpty) return Reject(GameSessionFailure.InvalidHotkey);
        if (from == to) return new(GameSessionDisposition.NoChange, GameSessionFailure.None, Version);
        if (_revision == long.MaxValue) return Reject(GameSessionFailure.RevisionExhausted);
        var before = ItemEntrySlots();
        (_hotkeys[from], _hotkeys[to]) = (_hotkeys[to], _hotkeys[from]);
        (_hotkeyPlaces[from], _hotkeyPlaces[to]) = (_hotkeyPlaces[to], _hotkeyPlaces[from]);
        EmptyEntriesOfMovedItems(before);
        Revise();
        return new(GameSessionDisposition.Changed, GameSessionFailure.None, Version);
    }

    /// <summary>ARC-UI-022: entry <paramref name="slot"/> dragged off the bar: it empties, and an item entry's item goes back to the grid. An
    /// empty entry is NoChange. Refused for a slot outside 0..9, a stale version, no hero. One revision.</summary>
    public GameSessionMutationResult ClearHotkey(GameSessionVersion expected, int slot)
    {
        ThrowIfDisposed();
        if (slot is < 0 or >= HotkeyRules.Entries) return Reject(GameSessionFailure.InvalidHotkey);
        if (!IsCurrent(expected)) return Reject(GameSessionFailure.StaleVersion);
        if (_hero is null) return Reject(GameSessionFailure.HotkeysUnavailable);
        if (_hotkeys[slot].IsEmpty) return new(GameSessionDisposition.NoChange, GameSessionFailure.None, Version);
        if (_revision == long.MaxValue) return Reject(GameSessionFailure.RevisionExhausted);
        var before = ItemEntrySlots();
        _hotkeys[slot] = HotkeyEntry.Empty;
        EmptyEntriesOfMovedItems(before);
        Revise();
        return new(GameSessionDisposition.Changed, GameSessionFailure.None, Version);
    }

    /// <summary>ARC-UI-022: a press of the spell pane (a spell) or the skill bar (a button) becomes the latest recent action
    /// (<see cref="HotkeyRules.RecordAction"/>, 0x57EFA0); the same action as the latest is NoChange. Refused for another kind or a
    /// malformed action (<see cref="GameSessionFailure.InvalidHotkey"/>), a stale version, no hero. One revision.</summary>
    public GameSessionMutationResult RecordHotkeyAction(GameSessionVersion expected, HotkeyEntry action)
    {
        ThrowIfDisposed();
        if (action is null || action.Kind is not (HotkeyKind.Skill or HotkeyKind.Spell) || !ValidHotkey(action)) return Reject(GameSessionFailure.InvalidHotkey);
        if (!IsCurrent(expected)) return Reject(GameSessionFailure.StaleVersion);
        if (_hero is null) return Reject(GameSessionFailure.HotkeysUnavailable);
        var recent = HotkeyRules.RecordAction(_recentActions, action);
        if (ReferenceEquals(recent, _recentActions)) return new(GameSessionDisposition.NoChange, GameSessionFailure.None, Version);
        if (_revision == long.MaxValue) return Reject(GameSessionFailure.RevisionExhausted);
        _recentActions = [.. recent];
        Revise();
        return new(GameSessionDisposition.Changed, GameSessionFailure.None, Version);
    }

    /// <summary>ARC-UI-022: the item a type-1 entry acts on (0x462930 as the activation calls it): the first other item the hero holds with the
    /// entry item's prototype, in the hero's list order (kit, taken, made), or null.</summary>
    public string? QueryHotkeySameKind(string item)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(item);
        if (HeldKind(item) is not { } kind) return null;
        return HeroHeld().Where(held => held.Entity != item && held.Kind.Prototype == kind.Prototype).Select(held => held.Entity).FirstOrDefault();
    }

    /// <summary>ARC-UI-027: the count item entry <paramref name="slot"/> prints on the bar (0x462B40 as the bar's drop and load call it,
    /// ledger HK12): the items the hero holds with the entry item's prototype, the entry's own included. Null for a slot outside 0..9, an
    /// entry of another kind or an item the hero no longer holds. (The original counts 1 for the prototype 15085, which 0x49B290 numbers
    /// 0x3AD9 and the session's same-kind finder does not single out either.)</summary>
    [Arcanum.Simulation.OriginalFunction(0x462B40, Arcanum.Simulation.OriginalFunctionScope.Partial)]
    public int? QueryHotkeyCount(int slot)
    {
        ThrowIfDisposed();
        if (slot is < 0 or >= HotkeyRules.Entries || _hotkeys[slot] is not { Kind: HotkeyKind.Item, Item: { } item } || HeldKind(item) is not { } kind) return null;
        return HeroHeld().Count(held => held.Kind.Prototype == kind.Prototype);
    }

    // An entry fits its kind; a spell lies below the empty spell field (10000).
    private static bool ValidHotkey(HotkeyEntry entry) => entry.IsWellFormed && (entry.Item is null || SessionValidation.Entity(entry.Item))
        && (entry.Kind is not (HotkeyKind.Spell or HotkeyKind.ItemSpell) || entry.Index < ItemSpellPaneRules.NoSpell);

    // The item entries' items by entry: their locations 2000 + entry.
    private Dictionary<string, int> ItemEntrySlots() => Enumerable.Range(0, HotkeyRules.Entries).Where(i => _hotkeys[i].Kind == HotkeyKind.Item)
        .ToDictionary(i => _hotkeys[i].Item!, i => i, StringComparer.Ordinal);

    // A drop or drag on the bar moves the items whose location 2000 + entry changed (onto the bar, between entries, back into the inventory),
    // and each move runs the inventory callback (HK6): every other entry naming such an item empties; the item entry now holding it stays.
    private void EmptyEntriesOfMovedItems(Dictionary<string, int> before)
    {
        var after = ItemEntrySlots();
        var moved = before.Keys.Concat(after.Keys).Where(item => before.GetValueOrDefault(item, -1) != after.GetValueOrDefault(item, -1))
            .ToHashSet(StringComparer.Ordinal);
        for (var i = 0; i < _hotkeys.Length; i++)
            if (_hotkeys[i].Item is { } item && moved.Contains(item) && !(_hotkeys[i].Kind == HotkeyKind.Item && after.GetValueOrDefault(item, -1) == i))
                _hotkeys[i] = HotkeyEntry.Empty;
    }

    // The items of the item entries: off the grid, in the locations 2000..2009.
    private IEnumerable<string> HotkeyItems() => _hotkeys.Where(entry => entry.Kind == HotkeyKind.Item).Select(entry => entry.Item!);

    /// <summary>
    /// After every change (the inventory callback 0x5570D0, which every removal of an item from its place runs, a wear move and a grid move
    /// included, ledger IP6, HK6): an entry whose item the hero no longer holds or whose item moved (its wear slot or grid slot changed since
    /// the last change) empties, whatever its type.
    /// </summary>
    private void SettleHotkeys()
    {
        for (var i = 0; i < _hotkeys.Length; i++)
            if (_hotkeys[i].Item is { } item && (HeldKind(item) is null || HotkeyPlace(item) != _hotkeyPlaces[i]))
                _hotkeys[i] = HotkeyEntry.Empty;
    }

    // After the grid settled: each entry's item's place, the mark the next change compares with.
    private void MarkHotkeyPlaces()
    {
        for (var i = 0; i < _hotkeys.Length; i++) _hotkeyPlaces[i] = HotkeyPlace(_hotkeys[i].Item);
    }

    private (Arcanum.Simulation.Equipment.EquipmentSlot? Worn, int? Grid) HotkeyPlace(string? item) => item is null ? default
        : (_equipment.SlotOf(item), _inventory.Grid.TryGetValue(item, out var placed) ? placed.Slot : null);

    // A saved bar: ten entries and two recent actions, each fitting its kind; an entry's item one the hero holds (an item entry's not worn, and
    // on one entry only); the recent actions only spells and skill-bar buttons. The original's load sets the recent actions through 0x57EFA0
    // in saved order, which turns the two around (HK7); here they come back as saved.
    private static GameSessionFailure ImportHotkeys(SessionHotkeyState saved, bool hasHero, InventoryDomain inventory, EquipmentDomain equipment,
        out HotkeyEntry[] entries, out HotkeyEntry[] recent)
    {
        entries = [.. SessionHotkeyState.Empty.Entries];
        recent = [.. SessionHotkeyState.Empty.Recent];
        if (saved.Entries.Count != HotkeyRules.Entries || saved.Recent.Count != HotkeyRules.RecentActions) return GameSessionFailure.InvalidHotkey;
        var held = inventory.Hero.PackKinds.Select(item => item.Entity)
            .ToHashSet(StringComparer.Ordinal);
        var onEntries = new HashSet<string>(StringComparer.Ordinal);
        var prototypes = new HashSet<int>();
        var kinds = inventory.Hero.PackKinds.ToDictionary(pair => pair.Entity, pair => pair.Kind, StringComparer.Ordinal);
        foreach (var entry in saved.Entries)
        {
            if (entry is null || !entry.IsEmpty && (!hasHero || !ValidHotkey(entry))) return GameSessionFailure.InvalidHotkey;
            if (entry.Item is { } item && (!held.Contains(item) || entry.Kind == HotkeyKind.Item && (equipment.SlotOf(item) is not null || !onEntries.Add(item)
                || kinds[item].Type is SessionItemType.Ammo or SessionItemType.Gold || !prototypes.Add(kinds[item].Prototype))))
                return GameSessionFailure.InvalidHotkey;
        }
        foreach (var action in saved.Recent)
            if (action is null || !action.IsEmpty && (!hasHero || action.Kind is not (HotkeyKind.Skill or HotkeyKind.Spell) || !ValidHotkey(action)))
                return GameSessionFailure.InvalidHotkey;
        entries = [.. saved.Entries];
        recent = [.. saved.Recent];
        return GameSessionFailure.None;
    }
}
