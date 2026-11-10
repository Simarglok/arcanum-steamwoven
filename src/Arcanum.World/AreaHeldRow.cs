namespace Arcanum.World;

/// <summary>ARC-DEV-314 (B01): the object row and weight of an item the hero holds, kept by the inventory host across a window move of the main map
/// (<see cref="AreaInventoryHost.HeldRows"/>, <see cref="AreaInventoryHost.Retain"/>); the row names the item and reads its facts.</summary>
public sealed record AreaHeldRow(Arcanum.Data.Area.AreaObjectRecord Row, int? Weight);
