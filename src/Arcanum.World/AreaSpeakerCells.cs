using Arcanum.Simulation.Movement;

namespace Arcanum.World;

/// <summary>
/// ARC-SCRIPT-1601 (exe 0x406DA0, field 2 = the object's live location): where a speaker stands now, for the <c>d:</c> and <c>x:</c> replies' directions (<see cref="AreaDialogDirections"/>).
/// A following companion stands on its committed cell, any other NPC the shown map's AI host holds on the cell that host has it on now (where its walker moved it, else its own origin),
/// and anything else on its T0 cell.
/// </summary>
public static class AreaSpeakerCells
{
    /// <param name="companion">A following companion's committed world cell; null for a speaker that is none.</param>
    /// <param name="npc">The AI host's current cell of an NPC by entity id (<see cref="AreaAiHost.NpcCell"/>); null for an entity the host does not hold.</param>
    public static Func<AreaSpeaker, (long X, long Y)> Live(Func<AreaSpeaker, (long X, long Y)?>? companion, Func<string, CellCoord?>? npc) => speaker =>
    {
        if (companion?.Invoke(speaker) is { } following) return following;
        if (npc?.Invoke(AreaConversationState.EntityId(speaker.Row)) is { } cell) return (cell.X, cell.Y);
        return (speaker.WorldX, speaker.WorldY);
    };
}
