using Arcanum.Simulation.Items;
using Arcanum.Simulation.Movement;

namespace Arcanum.Session;

/// <summary>ARC-DEV-347 (E1-a): what a created object is. <see cref="Scenery"/> is a scenery object made from a prototype of object type 3 (a wall of fire's piece, a
/// flash's marker): it is drawn and picked as scenery. <see cref="EyeCandy"/> is an object made only to be seen (an effect's art): it is drawn and never picked. The exact kind of
/// each spell's creation is the research slice E1-r's (ledger rows), so the frame is generic by prototype.</summary>
public enum SessionCreatedKind { Scenery, EyeCandy, Critter }

/// <summary>
/// ARC-DEV-347 (E1-a, TM4): a scenery or eye-candy object the session created from a prototype on a map, a created object of the directory
/// (<c>scenery:n</c> or <c>eyecandy:n</c>; origins <see cref="SessionObjectOrigin.Scenery"/> and <see cref="SessionObjectOrigin.EyeCandy"/>): its prototype number (the host
/// reads the art and flags from the game's data by it), its map, its cell and pixel offsets, and the game time it was created. It is map-local state: it belongs to <see cref="Map"/>
/// ARC-RES-101 (SUM2, SUM4, SUM7): the object also keeps what a summon writes on its creation and a rebuild needs: the spell flags (<see cref="SpellFlags"/>), the summoner (<see cref="Summoner"/>, the entity
/// whose goal runs' source slot the summon writes; the fire wall's burn spares it and its followers) and the spell run that owns it (<see cref="Run"/>, the run whose summoned list holds it). It has
/// no life of its own: it leaves only by a destroy (the record's own Destroy line at the callback or END, SUM-hand-off), singly or with its run's others in one step (<see cref="GameSessionOwner.DestroyCreatedObjects"/>).
/// It lies in the sector of its cell, so the open map lists it among its residents and the sector's fold reaches it; the other maps' objects stay in the save until their map opens
/// (nothing here reads the game's data). Saved (schema 85) and never given its number again.
/// </summary>
public sealed record SessionCreatedObject(string Entity, SessionCreatedKind Kind, int Prototype, SessionMapId Map, CellCoord Cell, int OffsetX, int OffsetY, long CreatedMs,
    uint SpellFlags = 0, string? Summoner = null, int? Run = null)
{
    /// <summary>The spell flag <c>OSF_SUMMONED</c> (field spell flags, bit 0x4000) the Summon component sets on every object it makes (ARC-RES-101, SUM2); the caller passes it in <see cref="SpellFlags"/>.</summary>
    public const uint SummonedFlag = 0x4000;
    /// <summary>The created objects the session holds at once (project bound; the original has none but the object table's).</summary>
    public const int MaxObjects = 4096;
    /// <summary>The highest prototype number a created object may name (a prototype file's number has six digits, the data reader's own bound).</summary>
    public const int MaxPrototype = 999_999;
    /// <summary>The largest pixel offset in either direction (a cell is under 100 pixels wide: a larger offset is another cell).</summary>
    public const int MaxOffset = 256;
    /// <summary>The entity prefix of a created scenery object.</summary>
    public const string SceneryPrefix = "scenery:";
    /// <summary>The entity prefix of a created eye-candy object.</summary>
    public const string EyeCandyPrefix = "eyecandy:";

    /// <summary>The entity prefix of a created critter (ARC-DEV-359, E1-c). It starts with the mobile map object's prefix <c>oid:</c> on purpose: every per-object state of the session (damage, the off
    /// and destroyed flags, a walking NPC's place, the effect list, the faction, the script attachments) is keyed by an <c>oid:</c> entity, so a created critter has all of it with no second vocabulary;
    /// the directory still reads its own origin (<see cref="SessionObjectOrigin.Critter"/>) from the longer prefix.</summary>
    public const string CritterPrefix = "oid:critter:";

    /// <summary>ARC-DEV-371 (PG1): the critter flag (field 218, bit 0x20000) the script action create object writes on a critter it made: an encounter creature, which the AI's tick destroys
    /// when it is left without a next tick outside a round (<c>SchedulerRules</c>, <c>IAiSchedulerWorld.IsEncounterCreature</c>). The action writes it on type 16 only, and on nothing else.</summary>
    public const int EncounterCritterFlag = 0x20000;

    /// <summary>The entity prefix of a kind.</summary>
    public static string PrefixOf(SessionCreatedKind kind) => kind switch { SessionCreatedKind.Scenery => SceneryPrefix, SessionCreatedKind.EyeCandy => EyeCandyPrefix, _ => CritterPrefix };

    /// <summary>The object's number in its entity, or null when the entity is not a created object's.</summary>
    public static int? Number(string? entity) => Number(entity, SceneryPrefix) ?? Number(entity, EyeCandyPrefix) ?? Number(entity, CritterPrefix);

    /// <summary>The number of a <paramref name="prefix"/> entity, or null.</summary>
    public static int? Number(string? entity, string prefix) =>
        entity is not null && entity.StartsWith(prefix, StringComparison.Ordinal) &&
        int.TryParse(entity.AsSpan(prefix.Length), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var n) &&
        n >= 1 && entity == prefix + n.ToString(System.Globalization.CultureInfo.InvariantCulture) ? n : null;

    /// <summary>The kind an entity names (null when it is no created object's).</summary>
    public static SessionCreatedKind? KindOf(string? entity) =>
        Number(entity, SceneryPrefix) is not null ? SessionCreatedKind.Scenery : Number(entity, EyeCandyPrefix) is not null ? SessionCreatedKind.EyeCandy
            : Number(entity, CritterPrefix) is not null ? SessionCreatedKind.Critter : null;
}

/// <summary>The outcome of <see cref="GameSessionOwner.CreateObject"/> and <see cref="GameSessionOwner.DestroyCreatedObject"/>: the object created or destroyed (Changed), or none
/// (Rejected, <see cref="Failure"/> says why; NoChange for a destroy of an object the session does not hold).</summary>
public sealed record SessionCreatedObjectResult(GameSessionDisposition Disposition, GameSessionFailure Failure, SessionCreatedObject? Object, GameSessionVersion Version);

/// <summary>The outcome of <see cref="GameSessionOwner.DestroyCreatedObjects"/>: the objects destroyed.</summary>
public sealed record SessionCreatedObjectsResult(GameSessionDisposition Disposition, GameSessionFailure Failure, IReadOnlyList<SessionCreatedObject> Objects, GameSessionVersion Version);

public sealed partial class GameSessionOwner
{
    /// <summary>
    /// ARC-DEV-347 (E1-a): why <see cref="CreateObject"/> would refuse a creation before any effect (an invalid kind, prototype, map, cell or offset, or the bound of
    /// <see cref="SessionCreatedObject.MaxObjects"/>, or the numbers running out), or null when it would not. The host checks a run's creations with it before it applies anything.
    /// </summary>
    public GameSessionFailure? QueryCreateObjectRefusal(SessionCreatedKind kind, int prototype, SessionMapId map, CellCoord cell, int offsetX = 0, int offsetY = 0, string? summoner = null, int? run = null,
        int count = 1)
    {
        ThrowIfDisposed();
        return CreateObjectRefusal(kind, prototype, map, cell, offsetX, offsetY, summoner, run, count);
    }

    private GameSessionFailure? CreateObjectRefusal(SessionCreatedKind kind, int prototype, SessionMapId map, CellCoord cell, int offsetX, int offsetY, string? summoner = null, int? run = null, int count = 1)
    {
        if (summoner is not null && (!SessionValidation.Entity(summoner) || summoner == SessionEventKinds.AbsentRole) || run is < 0) return GameSessionFailure.InvalidCreatedObject;
        if (!Enum.IsDefined(kind) || prototype is < 1 or > SessionCreatedObject.MaxPrototype || !SessionValidation.Map(map) || !DropCellFits(cell) ||
            Math.Abs((long)offsetX) > SessionCreatedObject.MaxOffset || Math.Abs((long)offsetY) > SessionCreatedObject.MaxOffset)
            return GameSessionFailure.InvalidCreatedObject;
        if (count < 1 || _inventory.Objects.Created.Count + (long)count > SessionCreatedObject.MaxObjects || NextObjectEntity(CreatedOrigin(kind)) is null) return GameSessionFailure.BudgetExceeded;
        return null;
    }

    private static SessionObjectOrigin CreatedOrigin(SessionCreatedKind kind) => kind switch { SessionCreatedKind.Scenery => SessionObjectOrigin.Scenery, SessionCreatedKind.EyeCandy => SessionObjectOrigin.EyeCandy, _ => SessionObjectOrigin.Critter };

    /// <summary>
    /// ARC-DEV-347 (E1-a, TM4, GR4): creates a scenery or eye-candy object by <paramref name="prototype"/> on <paramref name="map"/> at <paramref name="cell"/> with the pixel offsets from
    /// the tile's centre, and returns it (the one create path of these kinds, <see cref="SessionObjectOrigin.Scenery"/> and <see cref="SessionObjectOrigin.EyeCandy"/>; the id is the
    /// next number of the shared counter, never given again). The map need not be the open one: the object is map-local and waits for its map. One revision. The session reads no game
    /// data, so the prototype is the host's to have checked (the host reads its art, type and flags when it draws it). Refused before any effect: the version, an invalid input,
    /// <see cref="SessionCreatedObject.MaxObjects"/>, the numbers running out, the revision bound.
    /// </summary>
    public SessionCreatedObjectResult CreateObject(GameSessionVersion expected, SessionCreatedKind kind, int prototype, SessionMapId map, CellCoord cell, int offsetX = 0, int offsetY = 0,
        uint spellFlags = 0, string? summoner = null, int? run = null)
    {
        ThrowIfDisposed();
        SessionCreatedObjectResult Reject(GameSessionFailure failure) => new(GameSessionDisposition.Rejected, failure, null, new(_identity, _revision));
        if (CreateObjectRefusal(kind, prototype, map, cell, offsetX, offsetY, summoner, run) is { } refused) return Reject(refused);
        if (!IsCurrent(expected)) return Reject(GameSessionFailure.StaleVersion);
        if (_revision == long.MaxValue) return Reject(GameSessionFailure.RevisionExhausted);
        var made = CreateCreatedObject(kind, prototype, map, cell, offsetX, offsetY, _clock.QueryClockSnapshot().GameTimestampMs, spellFlags, summoner, run);
        Revise();
        return new(GameSessionDisposition.Changed, GameSessionFailure.None, made, new(_identity, _revision));
    }

    /// <summary>ARC-DEV-371: why <see cref="CreateCritterNear"/> would refuse <paramref name="count"/> creations before any draw (the checks of <see cref="QueryCreateObjectRefusal"/> and the bound of critters with
    /// written flags, which the encounter flag each one carries counts against), or null when it would not; the host checks a whole run with it before it applies anything.</summary>
    public GameSessionFailure? QueryCreateCritterRefusal(int prototype, SessionMapId map, CellCoord near, int count = 1)
    {
        ThrowIfDisposed();
        return CreateCritterRefusal(prototype, map, near, count);
    }

    private GameSessionFailure? CreateCritterRefusal(int prototype, SessionMapId map, CellCoord near, int count) =>
        CreateObjectRefusal(SessionCreatedKind.Critter, prototype, map, near, 0, 0, null, null, count)
        ?? (count < 1 || (long)_objectFlags.Count + count > MaxObjectFlagCritters ? GameSessionFailure.BudgetExceeded : null);

    /// <summary>
    /// ARC-DEV-371 (E1-c3, ledger PG1, PG2, PG12): a script's create object (action 52, 0x447220 case 0x34) of a critter prototype next to the object on <paramref name="near"/>: the tile is the action's
    /// (up to ten tries of a distance 1..3 and a direction over the host's <paramref name="dropMap"/>, <see cref="DropPlacementRules.PlaceCreated"/>, drawn from the session's generator, the same tile
    /// search a created item's), the critter an <c>oid:critter:n</c> (<see cref="SessionCreatedKind.Critter"/>) with the critter flag 0x20000 written on it
    /// (<see cref="SessionCreatedObject.EncounterCritterFlag"/>, the only thing the action adds to a created type 16 object), both in one revision. <paramref name="prototype"/> is the prototype file's number
    /// (the script's number + 20). The session reads no game data, so the prototype being a critter is the host's to have checked (<see cref="Arcanum.Data.Objects.CreatedObjectRows"/>). Refused before any effect: an
    /// invalid input, the bounds of <see cref="SessionCreatedObject.MaxObjects"/> and of the critters with written flags, the version, a generator at its end
    /// (<see cref="GameSessionFailure.RandomExhausted"/>), a tile outside the ground store's range, the revision bound.
    /// </summary>
    public SessionCreatedObjectResult CreateCritterNear(GameSessionVersion expected, int prototype, SessionMapId map, CellCoord near, IDropMap dropMap, DropOwner owner = DropOwner.Object)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(dropMap);
        if (!Enum.IsDefined(owner)) throw new ArgumentOutOfRangeException(nameof(owner));
        SessionCreatedObjectResult Reject(GameSessionFailure failure) => new(GameSessionDisposition.Rejected, failure, null, new(_identity, _revision));
        if (CreateCritterRefusal(prototype, map, near, 1) is { } refused) return Reject(refused);
        if (!IsCurrent(expected)) return Reject(GameSessionFailure.StaleVersion);
        if (_revision == long.MaxValue) return Reject(GameSessionFailure.RevisionExhausted);
        var revision = _revision;
        CellCoord cell;
        try { cell = DropPlacementRules.PlaceCreated(near, dropMap, new SessionDropRolls(_random), owner); }
        catch (SessionDropRolls.ExhaustedException) { return Reject(GameSessionFailure.RandomExhausted); }
        ThrowIfDisposed();
        if (_revision != revision) return Reject(GameSessionFailure.StaleVersion);
        if (!DropCellFits(cell)) return Reject(GameSessionFailure.InvalidCreatedObject);
        var made = CreateCreatedObject(SessionCreatedKind.Critter, prototype, map, cell, 0, 0, _clock.QueryClockSnapshot().GameTimestampMs);
        _objectFlags = _objectFlags.SetItem(made.Entity, new SessionObjectFlags(0, 0, SessionCreatedObject.EncounterCritterFlag, 0));
        Revise();
        return new(GameSessionDisposition.Changed, GameSessionFailure.None, made, new(_identity, _revision));
    }

    /// <summary>
    /// ARC-DEV-347 (E1-a, TM4): destroys a created scenery or eye-candy object (the one destroy path, <see cref="DestroyObject"/>; the number stays used). NoChange for an entity the
    /// session holds no such object under; Rejected for a stale version. One revision. The run that created it (a spell's END, a dispel) calls this.
    /// </summary>
    public SessionCreatedObjectResult DestroyCreatedObject(GameSessionVersion expected, string entity)
    {
        ThrowIfDisposed();
        SessionCreatedObjectResult Reject(GameSessionFailure failure) => new(GameSessionDisposition.Rejected, failure, null, new(_identity, _revision));
        if (!SessionValidation.Entity(entity) || SessionCreatedObject.KindOf(entity) is null) return Reject(GameSessionFailure.InvalidCreatedObject);
        if (!IsCurrent(expected)) return Reject(GameSessionFailure.StaleVersion);
        if (_inventory.Objects.CreatedObject(entity) is not { } held) return new(GameSessionDisposition.NoChange, GameSessionFailure.None, null, new(_identity, _revision));
        if (_revision == long.MaxValue) return Reject(GameSessionFailure.RevisionExhausted);
        DestroyObject(entity);
        Revise();
        return new(GameSessionDisposition.Changed, GameSessionFailure.None, held, new(_identity, _revision));
    }

    /// <summary>
    /// ARC-DEV-347 (ARC-RES-101 hand-off): destroys the created objects <paramref name="entities"/> names in one step, one revision (a caller that removes a list at once; the spell's Destroy component, ARC-DEV-352,
    /// destroys one object per list entry, as the original's component does, so a wall's five pieces go in one action with five revisions). An entity that is no object the session holds is skipped (a piece already gone, the original's skip of destroyed objects).
    /// Changed with the objects destroyed (in the order given) when any was; NoChange with none; Rejected for a stale version, a list over <see cref="SessionCreatedObject.MaxObjects"/>
    /// or an entity that is no created object's name at all (nothing destroyed then).
    /// </summary>
    public SessionCreatedObjectsResult DestroyCreatedObjects(GameSessionVersion expected, IReadOnlyList<string> entities)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(entities);
        SessionCreatedObjectsResult Reject(GameSessionFailure failure) => new(GameSessionDisposition.Rejected, failure, [], new(_identity, _revision));
        if (entities.Count > SessionCreatedObject.MaxObjects || entities.Any(entity => !SessionValidation.Entity(entity) || SessionCreatedObject.KindOf(entity) is null)) return Reject(GameSessionFailure.InvalidCreatedObject);
        if (!IsCurrent(expected)) return Reject(GameSessionFailure.StaleVersion);
        var held = entities.Distinct(StringComparer.Ordinal).Select(entity => _inventory.Objects.CreatedObject(entity)).OfType<SessionCreatedObject>().ToArray();
        if (held.Length == 0) return new(GameSessionDisposition.NoChange, GameSessionFailure.None, [], new(_identity, _revision));
        if (_revision == long.MaxValue) return Reject(GameSessionFailure.RevisionExhausted);
        foreach (var made in held) DestroyObject(made.Entity);
        Revise();
        return new(GameSessionDisposition.Changed, GameSessionFailure.None, Array.AsReadOnly(held), new(_identity, _revision));
    }

    /// <summary>ARC-DEV-347: the created scenery and eye-candy objects lying on <paramref name="map"/> now, in the order they were created.</summary>
    public IReadOnlyList<SessionCreatedObject> QueryCreatedObjects(SessionMapId map)
    {
        ThrowIfDisposed();
        return Array.AsReadOnly(_inventory.Objects.Created.Where(made => made.Map == map).ToArray());
    }

    /// <summary>ARC-DEV-347: a created object of the session by its entity, or null.</summary>
    public SessionCreatedObject? QueryCreatedObject(string entity)
    {
        ThrowIfDisposed();
        return SessionValidation.Entity(entity) ? _inventory.Objects.CreatedObject(entity) : null;
    }

    /// <summary>
    /// ARC-DEV-347 (TM4): the one creation of a scenery or eye-candy object: it takes the next number of the shared counter (the high-water mark moves to it, so the number is never
    /// given again) and is recorded. The caller made the checks (<see cref="CreateObjectRefusal"/>) and keeps the revision.
    /// </summary>
    internal SessionCreatedObject CreateCreatedObject(SessionCreatedKind kind, int prototype, SessionMapId map, CellCoord cell, int offsetX, int offsetY, long createdMs,
        uint spellFlags = 0, string? summoner = null, int? run = null)
    {
        var number = AllocateObjectNumber(CreatedOrigin(kind));
        var made = new SessionCreatedObject(SessionCreatedObject.PrefixOf(kind) + number.ToString(System.Globalization.CultureInfo.InvariantCulture), kind, prototype, map, cell, offsetX, offsetY, createdMs, spellFlags, summoner, run);
        _inventory.Objects.AddCreated(made);
        // ARC-DEV-386 (E1-c4): a created critter is at once a holder of its map's seed (when one is attached; the next attach adds it otherwise).
        if (kind == SessionCreatedKind.Critter && _inventory.Seeds.TryGetValue(map, out var seed) && seed.AddCreatedHolder(made.Entity)) _inventory.SeedGeneration++;
        return made;
    }

    // The saved created objects within the bounds, each a scenery or eye-candy entity once, by the prefix of its kind, of a prototype in range, on a valid map and cell, created not after the saved clock.
    private static GameSessionFailure ImportCreatedObjects(SessionObjectRecords saved, long clockMs, InventoryDomain inventory)
    {
        if (saved.Created.Count > SessionCreatedObject.MaxObjects) return GameSessionFailure.BudgetExceeded;
        if (saved.LastCreatedNumber < 0) return GameSessionFailure.InvalidSnapshot;
        var entities = new HashSet<string>(StringComparer.Ordinal);
        foreach (var made in saved.Created)
        {
            if (made is null || !Enum.IsDefined(made.Kind) || SessionCreatedObject.KindOf(made.Entity) != made.Kind || !entities.Add(made.Entity) ||
                made.Prototype is < 1 or > SessionCreatedObject.MaxPrototype || !SessionValidation.Map(made.Map) || !DropCellFits(made.Cell) ||
                Math.Abs((long)made.OffsetX) > SessionCreatedObject.MaxOffset || Math.Abs((long)made.OffsetY) > SessionCreatedObject.MaxOffset ||
                made.CreatedMs < 0 || made.CreatedMs > clockMs || made.Run is < 0 ||
                made.Summoner is not null && (!SessionValidation.Entity(made.Summoner) || made.Summoner == SessionEventKinds.AbsentRole))
                return GameSessionFailure.InvalidSnapshot;
            inventory.Objects.AddCreated(made);
        }
        inventory.Objects.LastCreatedNumber = Math.Max(saved.LastCreatedNumber, saved.Created.Select(made => SessionCreatedObject.Number(made.Entity) ?? 0).DefaultIfEmpty(0).Max());
        return GameSessionFailure.None;
    }
}
