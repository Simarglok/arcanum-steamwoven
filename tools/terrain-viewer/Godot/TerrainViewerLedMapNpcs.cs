using Arcanum.Session;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-AI-1622: an NPC of the shown map that has no dialog script and that the hero leads (a Dominate Will target: SessionSpellCasterSocial.MindControlled -> ForceJoinParty) is a
// follower of the party like any other (the original's follower array holds any critter, GT/FO notes of ARC-RES-076). The viewer builds companions from the dialog NPCs' templates
// and the AI scene leaves every led NPC out of the host's own rows, so without a body template of its own such an NPC was neither the host's NPC nor a companion: the host counted it as
// gone and it never followed, fought or caught up. Its template is built from its map row (AreaCompanionTemplate.ForLedMapNpcs) as a created critter's is, and the group is rebuilt
// when the set of such NPCs changes (a Dominate cast, a leave, a death, a load).
public partial class TerrainViewerApp
{
    // The set of led map NPCs the last rebuild of the companions looked at (entities in order).
    private (GameSessionOwner Session, int Map, string Entities)? _ledMapNpcLooked;

    /// <summary>The living NPCs of the shown map's rows that the hero leads and that no dialog template of the map covers: the entities in order.</summary>
    private IReadOnlySet<string> LedMapNpcEntities(GameSessionOwner session)
    {
        if (_inventoryHost?.Maps.FirstOrDefault(m => m.RuntimeMap == CurrentAreaMap) is not { } map) return new HashSet<string>(StringComparer.Ordinal);
        var own = _ownCompanionTemplates.Select(t => t.Entity).ToHashSet(StringComparer.Ordinal);
        return session.QueryParty().Followers.Select(f => f.Entity)
            .Where(entity => !own.Contains(entity) && SessionCreatedObject.KindOf(entity) != SessionCreatedKind.Critter && map.TryGetRow(entity, out var row) && row.Type == 16
                && session.QueryCompanion(entity).Standing != CompanionStanding.Free && _combatHost?.Target(session, entity) is not { Dead: true })
            .ToHashSet(StringComparer.Ordinal);
    }

    private static string LedMapNpcSignature(IReadOnlySet<string> entities) => string.Join(",", entities.Order(StringComparer.Ordinal));

    /// <summary>The body templates of the shown map's led NPCs without a dialog (empty until the module's companion data reads); the set they were built for is recorded as looked at.</summary>
    private IReadOnlyList<AreaCompanionTemplate> LedMapNpcTemplates(GameSessionOwner session)
    {
        var entities = LedMapNpcEntities(session);
        if (_readyArea is not { } area || _companionData?.Data is null) return [];
        _ledMapNpcLooked = (session, CurrentAreaMap, LedMapNpcSignature(entities));
        var templates = AreaCompanionTemplate.ForLedMapNpcs(area.Snapshot.Objects, area.Snapshot.Visuals, entities, area.Plan, area.Plan.Chart, new(area.Snapshot.RuntimeMap), MapCompanionFacts);
        foreach (var missing in entities.Where(e => templates.All(t => t.Entity != e)))
            GD.Print($"ARC-AI-1622 led-map-npc entity={missing} has no body template (the scene does not draw it, or its AI row or fields do not read)");
        return templates.Where(t => !SpeakerDead(t.Speaker)).ToArray();
    }

    /// <summary>Rebuilds the companions when the led map NPCs are not the ones the group was last built for (a Dominate Will cast, one that left, a death). The group's own walk state is recorded first.</summary>
    private void SyncLedMapNpcCompanions(GameSessionOwner session)
    {
        if (_readyMapName is null || _readyArea is null || _areaCompanions is null || _companionData?.Data is null) return;
        var wanted = LedMapNpcSignature(LedMapNpcEntities(session));
        var before = _ledMapNpcLooked is { } looked && ReferenceEquals(looked.Session, session) && looked.Map == CurrentAreaMap ? looked.Entities : string.Empty;
        if (before == wanted) return;
        _ledMapNpcLooked = (session, CurrentAreaMap, wanted);
        RecordAreaCompanions("led map NPCs changed");
        BindAreaCompanions();
    }
}
