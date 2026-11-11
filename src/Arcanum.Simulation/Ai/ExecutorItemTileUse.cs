using Arcanum.Simulation.Magic;

namespace Arcanum.Simulation.Ai;

/// <summary>
/// ARC-DEV-365 (T-g, ledger IA12): what the use of an item on a tile (0x462FC0) reads and starts beyond the object use: the player's host
/// (the viewer's item cursor and goal 41) runs it through this world.
/// </summary>
public interface IItemUseTileWorld : IItemUseWorld
{
    /// <summary>A trap device's use on a tile (0x4BC480, ledger IA13): the marker, the spent device, or a refusal that keeps the device.</summary>
    void UseTrapDeviceOnTile(string user, string item, SpellLocation tile);
    /// <summary>The item's slot-0 spell cast at the tile (0x4F27F0, 0x4CB7A0, 0x4605E0) and the item-used trigger with no target (0x4CBF70).</summary>
    void UseItemMagicOnTile(string user, string item, SpellLocation tile);
}

public static partial class AiExecutorRules
{
    /// <summary>
    /// The item use on a tile (0x462FC0, ledger IA12), which corrects the earlier reading "the object use without the targeting step": it
    /// has no scroll level test (so no message 113), runs no use script and cannot be stopped by one, and has no lockpick, heal or food-message
    /// branch. A trap device goes to the tile form (0x4BC480). An item with spell charges or the magical flag casts its slot-0 spell at the
    /// tile and sends the item-used trigger with no target; a food or scroll is then spent. Any other item charges 4 action points (the
    /// consume, 0x4B7CD0: only in a round, on the user's subturn), reads a written item, spends a food or a scroll, and does nothing else.
    /// </summary>
    [OriginalFunction(0x462FC0, OriginalFunctionScope.Partial)]
    public static AiItemUseResult UseItemOnTileNow(IItemUseTileWorld world, string user, string item, SpellLocation tile)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(item);
        var facts = world.UsedItem(user, item);
        if (facts.TrapDevice) { world.UseTrapDeviceOnTile(user, item, tile); return new(AiItemUseStep.TrapDevice, false, false); }
        if (facts.Charges != 0 || facts.Magical)
        {
            world.UseItemMagicOnTile(user, item, tile);
            return new(AiItemUseStep.Magic, false, Spend(world, facts.Kind, item));
        }
        var charged = Charge(world, user);
        switch (facts.Kind)
        {
            case AiItemKind.Written:
                world.ReadWritten(user, item);
                return new(AiItemUseStep.Read, charged, false);
            case AiItemKind.Food or AiItemKind.Scroll:
                return new(AiItemUseStep.Spent, charged, Spend(world, facts.Kind, item));
            default:
                return new(AiItemUseStep.Nothing, charged, false);
        }
    }
}
