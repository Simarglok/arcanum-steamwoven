using Arcanum.Data.Characters;
using Arcanum.Data.Objects;
using Arcanum.Session;
using Arcanum.Simulation.Movement;

namespace Arcanum.World;

/// <summary>
/// ARC-DEV-359 (E1-c, TM4): the critters the session created on a map, as rows the world reads. A created critter has no map file, so its row is built from its prototype by
/// <see cref="CreatedObjectRows.Critter"/> and each critter is given a one-row <see cref="AreaInventoryMap"/> (one NPC holder with an empty list: the inventory generation of a created critter is
/// not modelled yet) that <see cref="RecordResolver.SetCreated"/> answers by identity, and an <see cref="AreaAiNpc"/> that <see cref="AreaAiHost.SyncCreated"/> runs. The session is the owner of what exists
/// (<see cref="GameSessionOwner.QueryCreatedObjects"/>); this is derived data: a row is built once per entity (an entity is never given again) and a prototype that does not read as a critter is
/// reported once (<see cref="Refused"/>) and left out, the critter staying in the session. Caller-serialized; no Godot.
/// </summary>
public sealed class AreaCreatedCritters
{
    private readonly GameSessionOwner _session;
    private readonly Func<SessionCreatedObject, CreatedObjectRowResult> _row;

    /// <param name="prototypes">The game's prototype reader (<see cref="Arcanum.Data.Content.ContentPrototypes.Read"/>), the file lookup the row builder takes.</param>
    public AreaCreatedCritters(GameSessionOwner session, Func<int, PrototypeFile> prototypes)
        : this(session, made => CreatedObjectRows.Critter(prototypes ?? throw new ArgumentNullException(nameof(prototypes)), made.Prototype, made.Entity, made.Map.RuntimeMap, made.Cell.X, made.Cell.Y)) { }

    /// <param name="row">Builds the row of a created critter (a test gives synthetic rows).</param>
    public AreaCreatedCritters(GameSessionOwner session, Func<SessionCreatedObject, CreatedObjectRowResult> row)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _row = row ?? throw new ArgumentNullException(nameof(row));
    }

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
            if (!_built.TryGetValue(made.Entity, out var built))
            {
                if (_refused.ContainsKey(made.Entity)) continue;
                var result = _row(made);
                if (result.Row is not { } row) { _refused[made.Entity] = result.Failure; continue; }
                var seed = new SessionMapInventorySeed([new SessionSeedHolder(made.Entity, SessionHolderKind.Npc, false, false, [], [])], []);
                var one = new AreaInventoryMap(made.Map.RuntimeMap, seed, new Dictionary<string, Arcanum.Data.Area.AreaObjectRecord>(StringComparer.Ordinal) { [made.Entity] = row });
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
