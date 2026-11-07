using Arcanum.Session;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Movement;

namespace Arcanum.World;

/// <summary>
/// ARC-DEV-144 (MIG13 S9c, docs/architecture/goal-table.md, GT94–GT97): the burn of a burning object (the fire damage 0x42A720 of goal 77 and of the eye candy 75/76) for the scenery and eye-candy hosts: the round they gate on and the damage to the critters
/// on the burning object's tile through the cast host's damage step (<see cref="SessionSpellCaster.ApplyBurn"/>: resistance and armour as for any damage, the life step starts the stun, the fall or the death). Since ARC-COMBAT-025 the items lying on the
/// tile burn too, after the critters (a project choice: each victim is independent, so the order only changes the log; types 5..14 of the same object list, through the item rules of the cast map; an item the cast map cannot damage, one in a container, a pack or a junk pile, is not listed and does not count for the round gate). <b>Not ready:</b> the source's hostility,
/// witnesses and experience (goal 77 has no source), the notified AI pass of a surviving NPC, and the eye candy's source stat bonus.
/// </summary>
public sealed class AreaFireBurner(GameSessionOwner session, Func<SessionSpellCaster?> caster, Func<CellCoord, IReadOnlyList<string>> crittersOn, Func<string, CellCoord?> ownerTile,
    Func<CellCoord, IReadOnlyList<string>>? itemsOn = null, Action? afterBurn = null)
{
    private readonly GameSessionOwner _session = session ?? throw new ArgumentNullException(nameof(session));
    private readonly Func<SessionSpellCaster?> _caster = caster ?? throw new ArgumentNullException(nameof(caster));
    private readonly Func<CellCoord, IReadOnlyList<string>> _crittersOn = crittersOn ?? throw new ArgumentNullException(nameof(crittersOn));
    private readonly Func<string, CellCoord?> _ownerTile = ownerTile ?? throw new ArgumentNullException(nameof(ownerTile));
    private readonly Func<CellCoord, IReadOnlyList<string>> _itemsOn = itemsOn ?? (_ => []);
    private readonly Action? _afterBurn = afterBurn;

    /// <summary>The burns asked for so far (every call of a burning goal's damage node that passed the round gate).</summary>
    public long Burns { get; private set; }

    /// <summary>The victims the burns reached (a critter on the tile, dead ones included, or an item lying there).</summary>
    public long Reached { get; private set; }

    /// <summary>The victims whose life or durability took the damage.</summary>
    public long Hurt { get; private set; }

    /// <summary>The hit points the burns took in all.</summary>
    public long HitPointsTaken { get; private set; }

    /// <summary>The id of the turn-based round in progress (<see cref="SessionCombatRound.Turn"/>), or null outside a round.</summary>
    public int? Round => _session.QueryCombatRound()?.Turn;

    /// <summary>The tile of a burning owner (an eye candy's object), or null when the host knows none.</summary>
    public CellCoord? TileOf(string owner) => _ownerTile(owner);

    /// <summary>Burns the critters and the items lying on <paramref name="tile"/> (null: nothing), the critters first; answers how many were reached.</summary>
    public int Burn(CellCoord? tile, bool normal, bool doubled)
    {
        Burns++;
        if (tile is not { } cell) return 0;
        var victims = _crittersOn(cell).Concat(_itemsOn(cell)).ToArray();
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
        // The host reports and redraws what the burn hit (a broken or deleted item or object), as after a spell or a script's damage.
        if (victims.Length > 0) _afterBurn?.Invoke();
        return victims.Length;
    }
}
