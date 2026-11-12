using Arcanum.Session;
using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Movement;

namespace Arcanum.World;

/// <summary>
/// ARC-MAGIC-1230 (UO3-UO6 of ARC-RES-1109): the items lying on the shown map's ground as pushed bodies for the knockback host. Unseen Force (spells 2 and 133) pushes an item of weight below 5 through the same goal-43 run as an NPC:
/// the starter tests no type (UO3), the slide reads only the object's location and art (UO4), and the object move 0x43E770 writes the location field and nothing else (UO5). So this body is the item's cell in the session
/// (<see cref="GameSessionOwner.MoveGroundItem"/>); the terrain and critter test of each step is the AI host's (an item blocks as anything the walls, doors, objects and living critters stop); a blocked step bumps the item through the
/// damage step with no attacker, as any non-critter object (<c>OD1</c>-<c>OD4</c>): the normal damage rolled 1..the line steps left (the fatigue half is drawn too, the object branch ignores it) is a script-style damage the cast host applies to the
/// item's durability, so a bump can break it (UO4).
/// </summary>
public sealed class AreaKnockbackItems(GameSessionOwner session, Func<string, CellCoord, CellCoord, KnockbackBlock> block, Func<SessionSpellCaster?> caster)
{
    /// <summary>Whether <paramref name="target"/> lies on the ground of the hero's map now (loose, not held, not in flight, not in a pile).</summary>
    public bool IsItem(string target)
    {
        try { return session.QueryGroundItem(target) is not null; }
        catch (ArgumentException) { return false; }
    }

    /// <summary>The item's cell now, or null when it does not lie on the map's ground.</summary>
    public CellCoord? Tile(string target)
    {
        try { return session.QueryGroundItem(target) is { } lying ? lying.Cell : null; }
        catch (ArgumentException) { return null; }
    }

    /// <summary>The block test of a step: the AI host's terrain test of a walker's step and the living critter on the tile (the pushed id names no critter), given by the viewer.</summary>
    public KnockbackBlock Block(string target, CellCoord left, CellCoord tested) => block(target, left, tested);

    /// <summary>Puts the item on the tile: its cell is the session's record at once (no sub-tile slide, as an NPC's, a tile a tick). False when the session refuses it.</summary>
    public bool Slide(string target, CellCoord tile) => session.MoveGroundItem(session.Version, target, tile).Disposition != GameSessionDisposition.Rejected;

    /// <summary>The collision damage of a blocked step: the two draws of a critter's bump, the normal one applied to the item as damage with no attacker; nothing without a cast host (the item is then undamaged).</summary>
    public void Bump(string target, int maximum)
    {
        var normal = session.AiRolls.Roll(AiRollPurpose.KnockbackBump, 1, Math.Max(maximum, 1));
        _ = session.AiRolls.Roll(AiRollPurpose.KnockbackBump, 1, Math.Max(maximum, 1));
        caster()?.ApplyScriptDamage(target, normal, 0, unresistable: false);
    }
}
