using Arcanum.Session;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;

namespace Arcanum.TerrainViewer;

// ARC-DEV-144 (MIG13 S9c, docs/architecture/goal-table.md GT94-GT97): the burn of the burning goals (77 and the eye candy 75/76) for the shown session: the critters on a burning object's tile come from the AI host, the damage goes
// through the cast host's damage step. One burner per session, shared by the scenery and eye-candy hosts, reading the AI host and the cast host as they are now (both are rebuilt on a reload).
public partial class TerrainViewerApp
{
    private AreaFireBurner? _fireBurner;
    private GameSessionOwner? _fireBurnerSession;

    private AreaFireBurner FireBurnerFor(GameSessionOwner session)
    {
        if (_fireBurner is { } burner && ReferenceEquals(_fireBurnerSession, session)) return burner;
        _fireBurnerSession = session;
        return _fireBurner = new(session,
            () => _spellCaster is { } caster && ReferenceEquals(caster.Owner, session) ? caster : null,
            cell => _aiHost?.CrittersOn(cell) ?? [],
            owner => _aiHost?.Tile(owner));
    }
}
