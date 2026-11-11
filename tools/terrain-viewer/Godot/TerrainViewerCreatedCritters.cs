using Arcanum.Session;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

public partial class TerrainViewerApp
{
    // ARC-DEV-359 (E1-c): the critters the session created on the shown map are rows the world reads by identity (the record resolver, the combat host) and NPCs the AI host runs, wherever they stand:
    // a created critter has no window to give its row, so it is built from its prototype (the module's prototype reader, set with the extra art) and handed to both after every change of the
    // session. Derived data: a load or a rebuilt host reads the session's critters again. Drawing, hover and the smoke stage are E1-c2's.
    private AreaCreatedCritters? _createdCritters;
    private GameSessionOwner? _createdCrittersSession;
    private AreaAiHost? _createdCrittersHost;
    private long _createdCrittersRevision = -1;
    private int _createdCrittersMap;

    /// <summary>Hands the shown map's created critters to the resolver and the AI host when the session, the host or the map changed since the last call (the session's revision moves with every change, so
    /// an idle frame is one comparison). A critter whose prototype does not read as a critter is said once and left out; it stays in the session.</summary>
    private void SyncCreatedCritters(GameSessionOwner session, AreaAiHost host)
    {
        if (_createdPrototypeRead is not { } read || _inventoryHost is not { } inventory) return;
        if (_createdCritters is null || !ReferenceEquals(_createdCrittersSession, session)) { _createdCritters = new(session, read); _createdCrittersSession = session; _createdCrittersRevision = -1; }
        var revision = session.Version.Revision;
        if (revision == _createdCrittersRevision && ReferenceEquals(_createdCrittersHost, host) && _createdCrittersMap == CurrentAreaMap) return;
        _createdCrittersRevision = revision; _createdCrittersHost = host; _createdCrittersMap = CurrentAreaMap;
        var map = new SessionMapId(CurrentAreaMap);
        inventory.Resolver.SetCreated(_createdCritters.Maps(map));
        var added = host.SyncCreated(_createdCritters.Npcs(map));
        foreach (var (entity, failure) in _createdCritters.TakeNewRefusals()) GD.Print($"ARC-DEV-359 created-critter entity={entity} not hosted: {failure}");
        if (added > 0) GD.Print($"ARC-DEV-359 created-critters map={CurrentAreaMap} hosted={added} npcs={host.Scene.Npcs.Count}");
    }
}
