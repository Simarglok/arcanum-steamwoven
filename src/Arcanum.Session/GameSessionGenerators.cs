using Arcanum.Simulation;
using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Items;
using Arcanum.Simulation.Movement;

namespace Arcanum.Session;

/// <summary>ARC-WORLD-1157 (schema 91, ledger PO1, PO7, PO8): one slot of the generators' counter table that holds something: the number of live creatures of the slot (0..31) and the
/// "blocked" bit (script action 110 toggles it, condition 46 reads it). A slot that is zero is not saved.</summary>
public sealed record SessionGeneratorSlot(int Slot, int Live, bool Blocked);

/// <summary>ARC-WORLD-1157 (PO1): the count of creatures a generator has made so far (bits 0 to 6 of its dword, 0..127), when the session has written one over the map's stored dword.</summary>
public sealed record SessionGeneratorMade(string Entity, int Made);

/// <summary>ARC-WORLD-1157 (PO5, PO7): a created critter a generator made. It keeps the slot of its template's dword, which is how its death finds the counter; the mark leaves only when the
/// critter is destroyed (the original's flag 0x100000 stays on a body, so a resurrected one releases again at its next death).</summary>
public sealed record SessionGeneratorCopy(string Entity, int Slot);

/// <summary>
/// ARC-WORLD-1157 (E2-a, schema 91, the <c>generators</c> section; ledger PO1 to PO8): what the original saves for the monster generators besides the generator object's own dword (the map
/// file keeps that): the 256-byte counter table of all maps (the live count of each slot and its blocked bit, 0x4BA460 and 0x4BA490), here as the slots that are not zero; the creatures made so
/// far of the generators this session ran (the dword's low bits, which the original writes back into the object); and the slot mark of every critter a generator made.
/// </summary>
public sealed record SessionGeneratorState(IReadOnlyList<SessionGeneratorSlot> Slots, IReadOnlyList<SessionGeneratorMade> Made, IReadOnlyList<SessionGeneratorCopy> Copies)
{
    /// <summary>No slot used, nothing made, no copy: a new game's table (cleared when a game is reset, 0x4BA400).</summary>
    public static SessionGeneratorState Empty { get; } = new([], [], []);

    /// <summary>The bounds of the saved lists (project bounds: a slot is one of 256, a copy is a created critter, a generator is a map object).</summary>
    public const int MaxMade = 65536;
}

/// <summary>What the host tells the session about one generator's tick (<see cref="GameSessionOwner.RunGenerator"/>): the generator, where it stands, what it copies and how it is set.</summary>
public sealed record SessionGeneratorRun
{
    /// <summary>The generator NPC's entity.</summary>
    public required string Entity { get; init; }
    public required SessionMapId Map { get; init; }
    /// <summary>The generator's own tile (the spot of a cap 1 generator, the centre of the others' tries).</summary>
    public required CellCoord Cell { get; init; }
    /// <summary>The prototype file number of the object the generator copies (its own prototype).</summary>
    public required int Prototype { get; init; }
    /// <summary>The generator's stored dword (field 0x12D, <see cref="GeneratorSettings"/>) as its map file has it; a made count the session wrote is read over it.</summary>
    public required uint Packed { get; init; }
    /// <summary>The rate (bits 21 to 23 of the NPC flag word): the next tick's delay.</summary>
    public required int Rate { get; init; }
    /// <summary>The map the placement reads (0x4F4E40).</summary>
    public required IDropMap DropMap { get; init; }
    /// <summary>With the off-screen option: the generator's drawn rectangle is off the view now (SZ6). Without the option it is not asked.</summary>
    public bool OffView { get; init; } = true;
    /// <summary>With the off-screen option: a spot has the generator's tile class and a rectangle moved to it off the view (the host's test; null: every spot).</summary>
    public Func<CellCoord, bool>? SpotAccepted { get; init; }
}

/// <summary>The outcome of <see cref="GameSessionOwner.RunGenerator"/>: the critters made (possibly none; the spawner answers success then too), and the delay of the generator's next tick.</summary>
public sealed record SessionGeneratorResult(GameSessionDisposition Disposition, GameSessionFailure Failure, IReadOnlyList<SessionCreatedObject> Made, long DelayMs, GameSessionVersion Version);

/// <summary>The original's 256-byte counter table (0x59AD58 module) and the generators' written counts, in the session's one mutable place.</summary>
internal sealed class GeneratorBook
{
    private const byte LiveMask = 0x1F, BlockedBit = 0x80;
    private readonly byte[] _table = new byte[GeneratorSettings.SlotCount];
    private readonly SortedDictionary<string, int> _made = new(StringComparer.Ordinal);
    private readonly SortedDictionary<string, int> _copies = new(StringComparer.Ordinal);

    /// <summary>0x4BA590: the low five bits of a slot's byte.</summary>
    [OriginalFunction(0x4BA590, OriginalFunctionScope.Full)]
    public int Live(int slot) => _table[slot] & LiveMask;

    /// <summary>0x4BABA0: bit 0x80 of a slot's byte (condition 46 reads it).</summary>
    [OriginalFunction(0x4BABA0, OriginalFunctionScope.Full)]
    public bool Blocked(int slot) => (_table[slot] & BlockedBit) != 0;
    public int? Made(string entity) => _made.TryGetValue(entity, out var made) ? made : null;
    public int? CopySlot(string entity) => _copies.TryGetValue(entity, out var slot) ? slot : null;
    public int CopyCount => _copies.Count;

    /// <summary>0x4BA6D0: the live count, refused above 31 or below 0 (the table keeps its value then).</summary>
    [OriginalFunction(0x4BA6D0, OriginalFunctionScope.Full)]
    public void SetLive(int slot, int live)
    {
        if (live is < 0 or > LiveMask) return;
        _table[slot] = (byte)((_table[slot] & BlockedBit) | live);
    }

    /// <summary>The blocked bit's clear (0x4BABD0) and set (0x4BABF0).</summary>
    [OriginalFunction(0x4BABD0, OriginalFunctionScope.Full)]
    [OriginalFunction(0x4BABF0, OriginalFunctionScope.Full)]
    public void SetBlocked(int slot, bool blocked) => _table[slot] = (byte)((_table[slot] & LiveMask) | (blocked ? BlockedBit : 0));

    public void SetMade(string entity, int made) => _made[entity] = made;
    public void AddCopy(string entity, int slot) => _copies[entity] = slot;
    public void DropCopy(string entity) => _copies.Remove(entity);

    /// <summary>The table as the save keeps it (0x4BA460 writes the 256 bytes as one blob; here the slots that are not zero, with the counts made and the copies' marks).</summary>
    [OriginalFunction(0x4BA460, OriginalFunctionScope.Partial)]
    public SessionGeneratorState Capture()
    {
        var slots = new List<SessionGeneratorSlot>();
        for (var slot = 0; slot < _table.Length; slot++)
            if (_table[slot] != 0) slots.Add(new(slot, _table[slot] & LiveMask, (_table[slot] & BlockedBit) != 0));
        return new(slots.ToArray(), _made.Select(pair => new SessionGeneratorMade(pair.Key, pair.Value)).ToArray(), _copies.Select(pair => new SessionGeneratorCopy(pair.Key, pair.Value)).ToArray());
    }

    /// <summary>The table read back from the save (0x4BA490).</summary>
    [OriginalFunction(0x4BA490, OriginalFunctionScope.Partial)]
    public static GeneratorBook From(SessionGeneratorState state)
    {
        var book = new GeneratorBook();
        foreach (var slot in state.Slots) { book.SetLive(slot.Slot, slot.Live); book.SetBlocked(slot.Slot, slot.Blocked); }
        foreach (var made in state.Made) book._made[made.Entity] = made.Made;
        foreach (var copy in state.Copies) book._copies[copy.Entity] = copy.Slot;
        return book;
    }
}

public sealed partial class GameSessionOwner
{
    private GeneratorBook _generators = new();
    private readonly List<string> _generatedPending = [];
    // A host that never takes them (a test, a headless run) does not grow the list without end.
    private const int MaxGeneratedPending = 1024;

    /// <summary>ARC-WORLD-1157 (PO1): the live count (0..31) of a counter slot and whether the slot is blocked (script condition 46), or null for a slot outside 0..255.</summary>
    public (int Live, bool Blocked)? QueryGeneratorSlot(int slot)
    {
        ThrowIfDisposed();
        return slot is < 0 or >= GeneratorSettings.SlotCount ? null : (_generators.Live(slot), _generators.Blocked(slot));
    }

    /// <summary>ARC-WORLD-1157 (PO6): the creatures a generator made so far as the session wrote it, or null when it wrote none (the map's stored dword then counts).</summary>
    public int? QueryGeneratorMade(string entity)
    {
        ThrowIfDisposed();
        return SessionValidation.Entity(entity) ? _generators.Made(entity) : null;
    }

    /// <summary>ARC-WORLD-1157 (PO5, PO7): the slot of the generator a created critter was made by, or null when it is no generator's.</summary>
    public int? QueryGeneratorCopy(string entity)
    {
        ThrowIfDisposed();
        return SessionValidation.Entity(entity) ? _generators.CopySlot(entity) : null;
    }

    /// <summary>
    /// ARC-WORLD-1157 (PO8, script action 110 <c>toggle_monster_generator</c>): toggles the blocked bit of a slot (0..255); the spawner and condition 46 read it. One revision.
    /// The script action itself is not admitted yet (E5-b); this is the session's half of it. A slot outside 0..255 is refused with <see cref="GameSessionFailure.InvalidCreatedObject"/> (the session has no failure of its own for a generator slot; the script action's validation, E5-b, will say it first).
    /// </summary>
    public GameSessionMutationResult ToggleGeneratorBlock(GameSessionVersion expected, int slot)
    {
        ThrowIfDisposed();
        if (slot is < 0 or >= GeneratorSettings.SlotCount) return Reject(GameSessionFailure.InvalidCreatedObject);
        if (!IsCurrent(expected)) return Reject(GameSessionFailure.StaleVersion);
        if (_revision == long.MaxValue) return Reject(GameSessionFailure.RevisionExhausted);
        _generators.SetBlocked(slot, !_generators.Blocked(slot));
        return Changed();
    }

    /// <summary>ARC-WORLD-1157: the entities of the critters generators made since the last call, in order (their inventories are generated by the viewer, as a script-made critter's, ARC-DEV-386). Not saved.</summary>
    public IReadOnlyList<string> TakeGeneratedCritters()
    {
        ThrowIfDisposed();
        var taken = _generatedPending.ToArray();
        _generatedPending.Clear();
        return taken;
    }

    /// <summary>
    /// ARC-WORLD-1157 (E2-a, ledger PO2 to PO6, the spawner 0x4BA790 with the placement 0x4BA910 and the copy 0x4EF920): one tick of a monster generator. The room, the slot's block, the hour, the view
    /// and the budget decide how many creatures are asked for (<see cref="GeneratorRules.Request"/>); each is a copy of the generator's object (here: a created critter of its prototype that keeps the generator as its template,
    /// <see cref="SessionCreatedKind.Critter"/>, <see cref="SessionCreatedObject.Summoner"/>, one number of the shared counter) put on the generator's tile when its cap is 1, else on a spot of up to ten tries
    /// (<see cref="DropPlacementRules.PlaceGenerated"/>, drawn from the session's generator); a failed creation ends the loop with the count so far. The live count of the slot rises by the number made, the count
    /// made too unless the budget is ignored, and each copy keeps the slot (the death releases it, <see cref="ReleaseGeneratorSlot"/>). The answer is the next tick's delay by the rate (PO6) whatever was
    /// made: the original answers success also when nothing was made. One revision when anything was made; nothing is written otherwise. Refused before any effect: an invalid input, the version, the revision bound.
    /// </summary>
    [OriginalFunction(0x4BA790, OriginalFunctionScope.Partial)]
    [OriginalFunction(0x4BA910, OriginalFunctionScope.Partial)]
    [OriginalFunction(0x4BA720, OriginalFunctionScope.Partial)]
    public SessionGeneratorResult RunGenerator(GameSessionVersion expected, SessionGeneratorRun run)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(run);
        SessionGeneratorResult Reject(GameSessionFailure failure) => new(GameSessionDisposition.Rejected, failure, [], 0, new(_identity, _revision));
        if (!SessionValidation.Entity(run.Entity) || !SessionValidation.Map(run.Map) || !DropCellFits(run.Cell) || run.Prototype is < 1 or > SessionCreatedObject.MaxPrototype || run.Rate is < 0 or > 7 ||
            run.DropMap is null || run.Entity == SessionEventKinds.AbsentRole) return Reject(GameSessionFailure.InvalidCreatedObject);
        if (!IsCurrent(expected)) return Reject(GameSessionFailure.StaleVersion);
        var settings = GeneratorSettings.Unpack(run.Packed);
        if (_generators.Made(run.Entity) is { } written) settings = settings with { Made = written };
        var delay = GeneratorRules.NextDelayMs(run.Rate);
        var now = _clock.QueryClockSnapshot().GameTimestampMs;
        var ask = GeneratorRules.Request(settings, _generators.Live(settings.Slot), _generators.Blocked(settings.Slot), now, run.OffView);
        if (ask == 0) return new(GameSessionDisposition.NoChange, GameSessionFailure.None, [], delay, new(_identity, _revision));
        if (_revision == long.MaxValue) return Reject(GameSessionFailure.RevisionExhausted);
        var drawsBefore = _random.Draws;
        var rolls = new SessionDropRolls(_random);
        var accepted = run.SpotAccepted ?? (_ => true);
        var made = new List<SessionCreatedObject>();
        for (var i = 0; i < ask; i++)
        {
            if (CreateObjectRefusal(SessionCreatedKind.Critter, run.Prototype, run.Map, run.Cell, 0, 0) is not null) break;
            CellCoord spot;
            try { spot = settings.Cap == 1 ? run.Cell : DropPlacementRules.PlaceGenerated(run.Cell, run.DropMap, rolls, accepted); }
            catch (SessionDropRolls.ExhaustedException) { break; }
            if (!DropCellFits(spot)) break;
            // ARC-WORLD-1526 (GS5): the copy is of the generator object, not of its prototype: the generator's entity is kept as the copy's template (the created record's source), so the world builds the copy's row from the generator's fields.
            var copy = CreateCreatedObject(SessionCreatedKind.Critter, run.Prototype, run.Map, spot, 0, 0, now, summoner: run.Entity);
            _generators.AddCopy(copy.Entity, settings.Slot);
            made.Add(copy);
        }
        if (made.Count == 0)
        {
            // The placement's draws moved the session's generator: a snapshot of the same revision would differ, so the draws are a revision of their own.
            if (_random.Draws == drawsBefore) return new(GameSessionDisposition.NoChange, GameSessionFailure.None, [], delay, new(_identity, _revision));
            Revise();
            return new(GameSessionDisposition.Changed, GameSessionFailure.None, [], delay, new(_identity, _revision));
        }
        // PO3: the record is written back: the live count rises by the number made, the count made too unless the budget is ignored.
        _generators.SetLive(settings.Slot, _generators.Live(settings.Slot) + made.Count);
        if (settings.BudgetCounts) _generators.SetMade(run.Entity, settings.Made + made.Count);
        foreach (var copy in made) if (_generatedPending.Count < MaxGeneratedPending) _generatedPending.Add(copy.Entity);
        Revise();
        return new(GameSessionDisposition.Changed, GameSessionFailure.None, Array.AsReadOnly(made.ToArray()), delay, new(_identity, _revision));
    }

    /// <summary>
    /// ARC-WORLD-1157 (PO7, the release 0x4BAB30 called from the death 0x45DA20): the death of a critter a generator made lowers the live count of its slot by one, never below 0. The count made is not given back.
    /// Nothing else lowers a live count: a copy destroyed without dying leaves its slot counted. The caller keeps the revision.
    /// </summary>
    [OriginalFunction(0x4BAB30, OriginalFunctionScope.Full)]
    private void ReleaseGeneratorSlot(string entity)
    {
        if (_generators.CopySlot(entity) is { } slot) _generators.SetLive(slot, Math.Max(0, _generators.Live(slot) - 1));
    }

    private SessionGeneratorState CaptureGenerators() => _generators.Capture();

    // The saved generator state within the bounds: slots 0..255 once each with a live count 0..31, a made count 0..127 per generator entity once, each copy a created critter the objects section holds, once, with a slot 0..255.
    private static GameSessionFailure ImportGenerators(SessionGeneratorState saved, SessionObjectRecords objects, out GeneratorBook book)
    {
        book = new GeneratorBook();
        if (saved is null || saved.Slots is null || saved.Made is null || saved.Copies is null || saved.Made.Count > SessionGeneratorState.MaxMade || saved.Copies.Count > SessionCreatedObject.MaxObjects)
            return GameSessionFailure.InvalidSnapshot;
        var slots = new HashSet<int>();
        foreach (var slot in saved.Slots)
        {
            if (slot is null || slot.Slot is < 0 or >= GeneratorSettings.SlotCount || slot.Live is < 0 or > GeneratorSettings.MaxCap || !slots.Add(slot.Slot) || slot.Live == 0 && !slot.Blocked)
                return GameSessionFailure.InvalidSnapshot;
            book.SetLive(slot.Slot, slot.Live);
            book.SetBlocked(slot.Slot, slot.Blocked);
        }
        var generators = new HashSet<string>(StringComparer.Ordinal);
        foreach (var made in saved.Made)
        {
            if (made is null || !SessionValidation.Entity(made.Entity) || made.Made is < 0 or > GeneratorSettings.MaxBudget || !generators.Add(made.Entity)) return GameSessionFailure.InvalidSnapshot;
            book.SetMade(made.Entity, made.Made);
        }
        var created = new HashSet<string>(objects.Created.Where(held => held.Kind == SessionCreatedKind.Critter).Select(held => held.Entity), StringComparer.Ordinal);
        var copies = new HashSet<string>(StringComparer.Ordinal);
        foreach (var copy in saved.Copies)
        {
            if (copy is null || !created.Contains(copy.Entity) || copy.Slot is < 0 or >= GeneratorSettings.SlotCount || !copies.Add(copy.Entity)) return GameSessionFailure.InvalidSnapshot;
            book.AddCopy(copy.Entity, copy.Slot);
        }
        return GameSessionFailure.None;
    }
}
