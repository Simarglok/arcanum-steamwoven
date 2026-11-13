using Arcanum.Data.Area;
using Arcanum.Data.Characters;
using Arcanum.Data.Objects;
using Arcanum.Session;
using Arcanum.Simulation.Movement;

namespace Arcanum.World;

/// <summary>
/// ARC-DEV-359 (E1-c, TM4): the critters the session created on a map, as rows the world reads. A created critter has no map file, so its row is built from its prototype by
/// <see cref="CreatedObjectRows.Critter"/> and each critter is given a one-row <see cref="AreaInventoryMap"/> (one NPC holder with an empty list at T0; ARC-DEV-386: what the original's creation generates, the restock body 0x463C60, is the session's list added to it, and the map carries the
/// prototype's inventory source as the holder's restock fact) that <see cref="RecordResolver.SetCreated"/> answers by identity, and an <see cref="AreaAiNpc"/> that <see cref="AreaAiHost.SyncCreated"/> runs. The session is the owner of what exists
/// (<see cref="GameSessionOwner.QueryCreatedObjects"/>); this is derived data: a row is built once per entity (an entity is never given again) and a prototype that does not read as a critter is
/// reported once (<see cref="Refused"/>) and left out, the critter staying in the session. Caller-serialized; no Godot.
/// </summary>
public sealed class AreaCreatedCritters
{
    private readonly GameSessionOwner _session;
    private readonly Func<SessionCreatedObject, CreatedObjectRowResult> _row;

    /// <param name="prototypes">The game's prototype reader (<see cref="Arcanum.Data.Content.ContentPrototypes.Read"/>), the file lookup the row builder takes.</param>
    /// <param name="template">ARC-WORLD-1526: the base row of an object by entity and home map (the record resolver's), which a monster generator's copy is built from; null: every copy is built from its prototype.</param>
    public AreaCreatedCritters(GameSessionOwner session, Func<int, PrototypeFile> prototypes, Func<string, int, AreaObjectRecord?>? template = null)
        : this(session, made => RowOf(session, prototypes ?? throw new ArgumentNullException(nameof(prototypes)), template, made)) { }

    /// <summary>
    /// ARC-WORLD-1526 (ledger GS5): the row of a created critter. A monster generator's copy (the session holds its slot mark and its template, the generator's entity, <see cref="SessionCreatedObject.Summoner"/>) is a copy of the
    /// generator OBJECT in the original, so its row is built from the generator's row (<see cref="CreatedObjectRows.CritterFrom"/>: its instance fields, scripts, name, stats and flags over its prototype, unmarked); when the
    /// generator's row is not known (an older save whose copy names none, the generator's map not held, a template that is no critter's) the copy falls back to its prototype's row, as before this change. Every other created
    /// critter is built from its prototype. The row is built once per entity (and again after a load or a map change), so a copy built while its generator's row was unknown keeps the prototype row until then (a limit).
    /// </summary>
    internal static CreatedObjectRowResult RowOf(GameSessionOwner session, Func<int, PrototypeFile> prototypes, Func<string, int, AreaObjectRecord?>? template, SessionCreatedObject made)
    {
        if (template is not null && made.Summoner is { } source && (made.SpellFlags & SessionCreatedObject.SummonedFlag) == 0 && session.QueryGeneratorCopy(made.Entity) is not null &&
            template(source, made.Map.RuntimeMap) is { } generator)
        {
            var copy = CreatedObjectRows.CritterFrom(generator, made.Entity, made.Map.RuntimeMap, made.Cell.X, made.Cell.Y);
            if (copy.Row is not null) return copy;
        }
        return CreatedObjectRows.Critter(prototypes, made.Prototype, made.Entity, made.Map.RuntimeMap, made.Cell.X, made.Cell.Y, SummonOf(session, template, made));
    }

    // ARC-MAGIC-053 (SUM5): a critter a Summon made (spell flag 0x4000) has its experience cleared and takes the alignment of its summoner (0x4B0490 / 0x4B0980, stat 19); ARC-MAGIC-1169 (E1-f): an NPC summoner's alignment is its
    // row's base stat 19 (the session keeps no NPC alignment and no stat effects are read here) and its faction (field 292: a script's set faction, else its row's) is copied too; a PC summoner leaves the prototype's faction.
    // The row is built once, when the critter is first handed to the world, so the values are the summoner's of that moment (a load builds it again from the summoner's then).
    internal static CreatedCritterSummon? SummonOf(GameSessionOwner session, Func<string, int, AreaObjectRecord?>? template, SessionCreatedObject made)
    {
        if ((made.SpellFlags & SessionCreatedObject.SummonedFlag) == 0) return null;
        if (made.Summoner == SessionSpellCaster.Hero) return new(session.QueryHero()?.Alignment);
        if (made.Summoner is not { } summoner || template?.Invoke(summoner, made.Map.RuntimeMap) is not { Type: CreatedObjectRows.NpcType } row || Arcanum.Data.Combat.CombatFieldReader.Read(row).Fields is not { Critter: { } critter, Npc: { } npc }) return new(null);
        int? alignment = critter.BaseStats.Count > CreatedObjectRows.AlignmentStat ? critter.BaseStats[CreatedObjectRows.AlignmentStat] : null;
        var faction = session.QueryMapChange(made.Map, MapChangeKey.ForEntity(summoner, AreaUseScriptHost.FactionProperty)) is { } set ? unchecked((int)set) : npc.Faction;
        return new(alignment, faction);
    }

    /// <param name="row">Builds the row of a created critter (a test gives synthetic rows).</param>
    /// <param name="inventorySource">ARC-DEV-386: the inventory source (field 0xEF) of a created critter's row; null reads it from the row's critter fields (a test gives synthetic rows no fields).</param>
    public AreaCreatedCritters(GameSessionOwner session, Func<SessionCreatedObject, CreatedObjectRowResult> row, Func<Arcanum.Data.Area.AreaObjectRecord, int>? inventorySource = null)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _row = row ?? throw new ArgumentNullException(nameof(row));
        _source = inventorySource ?? (record => Arcanum.Data.Combat.CombatFieldReader.Read(record).Fields?.Critter?.InventorySource ?? 0);
    }

    private readonly Func<Arcanum.Data.Area.AreaObjectRecord, int> _source;

    private readonly Dictionary<string, (AreaInventoryMap Map, AreaAiNpc Npc)> _built = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CreatedRowFailure> _refused = new(StringComparer.Ordinal);

    /// <summary>The created critters whose prototype did not build a row, with the reason (each said once by the caller, via <see cref="TakeNewRefusals"/>).</summary>
    public IReadOnlyDictionary<string, CreatedRowFailure> Refused => _refused;

    private readonly HashSet<string> _reported = new(StringComparer.Ordinal);

    /// <summary>The refusals not yet taken, in entity order (so a caller prints each once).</summary>
    public IReadOnlyList<(string Entity, CreatedRowFailure Failure)> TakeNewRefusals()
    {
        var fresh = _refused.Where(pair => _reported.Add(pair.Key)).OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => (pair.Key, pair.Value)).ToArray();
        return fresh;
    }

    /// <summary>The one-row maps of the critters created on <paramref name="map"/> now, in the order they were created (the resolver's <see cref="RecordResolver.SetCreated"/>).</summary>
    public IReadOnlyList<AreaInventoryMap> Maps(SessionMapId map) => Rows(map).Select(row => row.Map).ToArray();

    /// <summary>ARC-DEV-371: the base record of the created critter <paramref name="entity"/> of <paramref name="map"/>, or null when the session holds none under it or its prototype did not build a row.</summary>
    public AreaObjectRecord? Row(SessionMapId map, string entity) =>
        _session.QueryCreatedObject(entity) is not { Kind: SessionCreatedKind.Critter } made || made.Map != map ? null
        : _built.TryGetValue(entity, out var known) ? (known.Map.TryGetRow(entity, out var row) ? row : null)
        : Rows(map).Select(row => row.Map.TryGetRow(entity, out var found) ? found : null).FirstOrDefault(found => found is not null);

    /// <summary>The created critters of <paramref name="map"/> as the AI host's NPCs, in the order they were created (<see cref="AreaAiHost.SyncCreated"/>).</summary>
    public IReadOnlyList<AreaAiNpc> Npcs(SessionMapId map) => Rows(map).Select(row => row.Npc).ToArray();

    private IEnumerable<(AreaInventoryMap Map, AreaAiNpc Npc)> Rows(SessionMapId map)
    {
        var rows = new List<(AreaInventoryMap, AreaAiNpc)>();
        var live = new HashSet<string>(StringComparer.Ordinal);
        foreach (var made in _session.QueryCreatedObjects(map))
        {
            if (made.Kind != SessionCreatedKind.Critter) continue;
            live.Add(made.Entity);
            // ARC-MAGIC-050: a critter that followed the PC to another map is built again there (its row names the map and cell it stands on).
            if (_built.TryGetValue(made.Entity, out var stale) && stale.Npc.Row.Key.RuntimeMap != made.Map.RuntimeMap) { _built.Remove(made.Entity); _refused.Remove(made.Entity); _reported.Remove(made.Entity); }
            if (!_built.TryGetValue(made.Entity, out var built))
            {
                if (_refused.ContainsKey(made.Entity)) continue;
                var result = _row(made);
                if (result.Row is not { } row) { _refused[made.Entity] = result.Failure; continue; }
                var seed = new SessionMapInventorySeed([new SessionSeedHolder(made.Entity, SessionHolderKind.Npc, false, false, [], [])], []);
                // ARC-DEV-386 (E1-c4, RK3): the restock facts of the holder are its prototype's inventory source (field 0xEF); a prototype that does not read as a critter's has none.
                var source = _source(row);
                var restock = source == 0 ? null : new Dictionary<string, AreaRestockSeed>(StringComparer.Ordinal) { [made.Entity] = new(source, 0) };
                var one = new AreaInventoryMap(made.Map.RuntimeMap, seed, new Dictionary<string, Arcanum.Data.Area.AreaObjectRecord>(StringComparer.Ordinal) { [made.Entity] = row }, restock: restock);
                built = (one, new AreaAiNpc(made.Entity, row, made.Cell));
                _built[made.Entity] = built;
            }
            rows.Add(built);
        }
        // What the session no longer holds is forgotten (its number is never given again, so nothing later asks for it).
        foreach (var gone in _built.Keys.Where(entity => !live.Contains(entity) && _session.QueryCreatedObject(entity) is null).ToArray()) _built.Remove(gone);
        return rows;
    }
}
