using Arcanum.Simulation.Ai;

namespace Arcanum.Session;

public sealed partial class GameSessionOwner
{
    /// <summary>
    /// ARC-WORLD-1284 (NT3, VP1, VP3, VP5, VPG1): the item insert (0x466640) and the removal (0x467860) set NPC flag 0x40 (ONF_CHECK_WIELD, <see cref="DangerFlags.CheckWield"/>)
    /// on a critter that is an NPC (a container and the hero set none), so the NPC's next AI gate re-wields its best items (0x4A8570, <see cref="GameSessionOwner.RewieldNpc"/>) and clears the bit.
    /// The bit is written with the caller's move, in its revision, on the NPC's danger record (saved with it). An insert that merges a gold or ammunition item into a held stack returns before
    /// the flag, so its callers do not call this. A dead NPC is not marked (no AI pass runs on a body, and a looted body's record would fill the danger record budget for good; the original sets the bit on it too, unobservable unless it is raised again). A record that would be new at the record budget is not written (as <see cref="FocusOnKiller"/>'s).
    /// </summary>
    private void MarkCheckWield(string? npc)
    {
        if (npc is null || NpcDead(npc)) return;
        var held = _danger.Get(npc);
        if (held.Flags.HasFlag(DangerFlags.CheckWield)) return;
        if (_danger.Records.Count >= _limits.MaxDangerRecords && !_danger.Records.ContainsKey(npc)) return;
        _danger.Put(held with { Flags = held.Flags | DangerFlags.CheckWield });
    }

    /// <summary>
    /// The NPC whose current list holds <paramref name="item"/> (a made item the session added to it, or a seed item still in its list), or null: a container's, the hero's,
    /// a placed or deleted item, an item no list holds. The holder the removal 0x467860 of a destroyed item (0x43CCA0) acts on.
    /// </summary>
    private string? NpcHolderOfItem(string item)
    {
        if (_inventory.NpcOf(item) is { } made) return KnownNpc(made) ? made : null;
        if (_inventory.Taken.Contains(item) || Dropped(item)) return null;
        foreach (var seed in _inventory.Seeds.Values)
            if (seed.HolderOf.TryGetValue(item, out var holder) && seed.Holders.TryGetValue(holder, out var row) && row.Kind == SessionHolderKind.Npc) return holder;
        return null;
    }
}
