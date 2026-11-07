using Arcanum.Session;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Movement;

namespace Arcanum.World;

/// <summary>
/// ARC-DEV-144 (MIG13 S9c, docs/architecture/goal-table.md, GT94–GT97): the burn of a burning object (the fire damage 0x42A720 of goal 77 and of the eye candy 75/76) for the scenery and eye-candy hosts: the round they gate on and the damage to the critters
/// on the burning object's tile through the cast host's damage step (<see cref="SessionSpellCaster.ApplyBurn"/>: resistance and armour as for any damage, the life step starts the stun, the fall or the death). <b>Not ready:</b> the items lying on the
/// tile (the map's objects take no damage in the viewer, so they neither burn nor count for the round gate), the source's hostility, witnesses and experience (goal 77 has no source), the notified AI pass of a surviving NPC, and the eye candy's source
/// stat bonus.
/// </summary>
public sealed class AreaFireBurner(GameSessionOwner session, Func<SessionSpellCaster?> caster, Func<CellCoord, IReadOnlyList<string>> crittersOn, Func<string, CellCoord?> ownerTile)
{
    private readonly GameSessionOwner _session = session ?? throw new ArgumentNullException(nameof(session));
    private readonly Func<SessionSpellCaster?> _caster = caster ?? throw new ArgumentNullException(nameof(caster));
    private readonly Func<CellCoord, IReadOnlyList<string>> _crittersOn = crittersOn ?? throw new ArgumentNullException(nameof(crittersOn));
    private readonly Func<string, CellCoord?> _ownerTile = ownerTile ?? throw new ArgumentNullException(nameof(ownerTile));

    /// <summary>The burns asked for so far (every call of a burning goal's damage node that passed the round gate).</summary>
    public long Burns { get; private set; }

    /// <summary>The victims the burns reached (a critter on the tile, dead ones included).</summary>
    public long Reached { get; private set; }

    /// <summary>The victims whose life took the damage.</summary>
    public long Hurt { get; private set; }

    /// <summary>The hit points the burns took in all.</summary>
    public long HitPointsTaken { get; private set; }

    /// <summary>The id of the turn-based round in progress (<see cref="SessionCombatRound.Turn"/>), or null outside a round.</summary>
    public int? Round => _session.QueryCombatRound()?.Turn;

    /// <summary>The tile of a burning owner (an eye candy's object), or null when the host knows none.</summary>
    public CellCoord? TileOf(string owner) => _ownerTile(owner);

    /// <summary>Burns the critters on <paramref name="tile"/> (null: nothing); answers how many were reached.</summary>
    public int Burn(CellCoord? tile, bool normal, bool doubled)
    {
        Burns++;
        if (tile is not { } cell) return 0;
        var victims = _crittersOn(cell);
        // No cast host for this session (between reloads): nothing reaches the damage path, so the round gate is not spent.
        if (_caster() is not { } spells) return 0;
        var damage = FireDamageRules.Damage(normal, doubled);
        foreach (var victim in victims)
        {
            Reached++;
            var hit = spells.ApplyBurn(victim, damage);
            if (!hit.Applied) continue;
            Hurt++;
            HitPointsTaken += hit.HitPointDamage;
        }
        return victims.Count;
    }
}
