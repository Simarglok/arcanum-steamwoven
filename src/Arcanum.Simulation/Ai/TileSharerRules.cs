using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Navigation;

namespace Arcanum.Simulation.Ai;

/// <summary>
/// One critter standing on a tile, as the tile-sharer finder (0x436720) and the step apart (0x4364D0) read it. <paramref name="Living"/> is the dead test's
/// opposite (0x45D8D0), <paramref name="Busy"/> the busy-run test (0x44E8C0: a run whose bottom goal is not carried), <paramref name="Order"/> the stand-in for the
/// original's object handle: the original compares handle values (signed, high word first), which are run-time pool handles that differ from run to run and are
/// never saved (OM1), so the world gives a stable order instead and "the larger handle" is the larger <paramref name="Order"/>.
/// </summary>
public readonly record struct AiTileCritter(string Id, bool Pc, bool Living, bool Busy, long Order);

/// <summary>The finder's answer: the two critters in the order the please-move starter (0x4363E0) takes them (the finder's second and first out parameters).</summary>
public readonly record struct AiTileSharerPair(AiTileCritter First, AiTileCritter Second);

/// <summary>Which critter 0x4363E0 asks to step aside and whom it steps away from.</summary>
public readonly record struct AiPleaseMoveChoice(AiTileCritter Mover, AiTileCritter AwayFrom);

/// <summary>
/// Critters that share a tile (ARC-AI-1315, ledger VA4, VAG4, read twice in the exports of the retail 1.0.7.4 executable): the tile-sharer finder 0x436720, the please-move
/// starter 0x4363E0 of goal 68 and the step apart 0x4364D0. The attack starter asks the finder before its interrupt and the step apart after every refused start.
/// Multiplayer-only branches (the step apart's second pass over the player characters runs only when both multiplayer bits of the mode word 0x62AC6C are set) are not
/// built: a single-player game has one player character.
/// </summary>
public static class AiTileSharerRules
{
    /// <summary>
    /// The finder (0x436720) for <paramref name="source"/> among <paramref name="onTile"/> (the critters of both kinds on the source's tile, the source among them): null
    /// when the source is busy, or fewer than two living, not busy critters share the tile, or none of the others is an NPC (a player character is never the blocker).
    /// Otherwise the first living, not busy NPC other than the source in the tile's list order is the blocker, and the pair is (blocker, source), swapped to (source,
    /// blocker) when both are NPCs and the source has the smaller handle: the starter then asks the second of the pair to move, which is the larger handle between two NPCs
    /// and the NPC when the source is a player character.
    /// </summary>
    [OriginalFunction(0x436720, OriginalFunctionScope.Partial)]
    public static AiTileSharerPair? Find(string source, IReadOnlyList<AiTileCritter> onTile)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(onTile);
        if (onTile.FirstOrDefault(c => c.Id == source) is not { Id: not null } self || self.Busy) return null;
        if (onTile.Count(c => c.Living && !c.Busy) < 2) return null;
        foreach (var other in onTile)
        {
            if (!other.Living || other.Busy || other.Pc || other.Id == source) continue;
            return !self.Pc && self.Order < other.Order ? new(self, other) : new(other, self);
        }
        return null;
    }

    /// <summary>
    /// The choice of the please-move starter (0x4363E0) for the pair (<paramref name="first"/>, <paramref name="second"/>): the second moves away from the first, except
    /// that when the second is a player character the first moves when it is not one or has the larger handle. Null for the same critter twice.
    /// </summary>
    public static AiPleaseMoveChoice? Choose(AiTileCritter first, AiTileCritter second)
    {
        if (first.Id is null || second.Id is null || first.Id == second.Id) return null;
        return second.Pc && (!first.Pc || second.Order < first.Order) ? new(first, second) : new(second, first);
    }

    /// <summary>
    /// The please-move starter (0x4363E0): the critter <see cref="Choose"/> names moves away from the other when the start gate with cost 0 passes (0x4348E0) and it is not
    /// busy; then goal 68 with the other critter stored starts (<see cref="IAiPerformWorld.StartPleaseMove"/>: the interrupt at the goal's priority and the run). True when
    /// the run started.
    /// </summary>
    [OriginalFunction(0x4363E0, OriginalFunctionScope.Partial)]
    public static bool PleaseMove(IAiPerformWorld world, AiTileCritter first, AiTileCritter second)
    {
        ArgumentNullException.ThrowIfNull(world);
        if (Choose(first, second) is not { } choice) return false;
        if (!AiExecutorRules.StartGate(world, choice.Mover.Id, 0)) return false;
        return !choice.Mover.Busy && world.StartPleaseMove(choice.Mover.Id, choice.AwayFrom.Id);
    }

    /// <summary>
    /// The attack starter's tile-sharer branch (0x434B00 → 0x436720 → 0x4363E0): true when the finder found a blocker, whether or not the please-move start succeeded; the
    /// attack then does not start and no step apart follows.
    /// </summary>
    public static bool AskBlocker(IAiPerformWorld world, string attacker)
    {
        ArgumentNullException.ThrowIfNull(world);
        if (Find(attacker, world.TileCritters(attacker)) is not { } pair) return false;
        PleaseMove(world, pair.First, pair.Second);
        return true;
    }

    /// <summary>
    /// The step apart (0x4364D0) of the critters on <paramref name="id"/>'s tile: with fewer than two living, not busy critters nothing happens; otherwise one stays (the
    /// player character with the smallest handle, else the NPC with the smallest) and every other NPC is asked to move away from it (<see cref="PleaseMove"/>), the
    /// last listed first. Returns how many asked.
    /// </summary>
    [OriginalFunction(0x4364D0, OriginalFunctionScope.Partial)]
    public static int StepApart(IAiPerformWorld world, string id)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(id);
        var free = world.TileCritters(id).Where(c => c.Living && !c.Busy).ToList();
        if (free.Count < 2) return 0;
        var npcs = free.Where(c => !c.Pc).ToList();
        var players = free.Where(c => c.Pc).ToList();
        var stay = players.Count > 0 ? players.MinBy(c => c.Order) : npcs.MinBy(c => c.Order);
        // The original builds the NPC list by prepending, so it asks them last to first.
        var asked = 0;
        for (var i = npcs.Count - 1; i >= 0; i--)
        {
            if (npcs[i].Id == stay.Id) continue;
            if (PleaseMove(world, stay, npcs[i])) asked++;
        }
        return asked;
    }

    /// <summary>
    /// Node 1 of goal 68 (0x424E00): the tile to step to, tried over the eight rotations from a random one (<see cref="AiRollPurpose.PleaseMoveStart"/>, 0 to 8: 8 starts
    /// at rotation 0) and wrapping: the first neighbour whose step is clear (<see cref="IAiGoalWorld.StepClear"/>, the original's blocked-step test 0x425760 with the
    /// mover's flags, the tile test and the object traversal). Null when none is clear: the goal then pops. The original's second pass over the occupied neighbours
    /// (asking the critter standing there to move and waiting 1000 ms) never finds one: the traversal test's blocker parameter is null and the occupant records stay
    /// zero, so the please-move start is called with no object (ledger VAG4).
    /// </summary>
    [OriginalFunction(0x424E00, OriginalFunctionScope.Partial)]
    public static CellCoord? StepTile(IAiGoalWorld world, string owner, IAiRollSource rolls)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(rolls);
        if (world.Tile(owner) is not { } at) return null;
        var start = rolls.Roll(AiRollPurpose.PleaseMoveStart, 0, 8);
        for (var i = 0; i < 8; i++)
        {
            var next = OriginalPathRequest.Neighbor(at, (start + i) % 8);
            if (world.StepClear(owner, next)) return next;
        }
        return null;
    }
}
