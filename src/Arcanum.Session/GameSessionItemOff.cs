namespace Arcanum.Session;

/// <summary>
/// ARC-SCRIPT-2346 (ledger IV9–IV11): the off flag (object flag 0x2) of an item as a script's toggle state (56) flips it, kept as the object field <c>obj.off</c> of the item's directory entry: 1 is off, 0 is on, and no record is what the item's map data says
/// (the same field an NPC's toggle writes, and the residency reads it for the items lying on the ground). A mobile object's field has one record by the object's identity (a placed item's <c>oid:</c> entity or a made item's <c>made:n</c>), is carried
/// with the item and saved in the snapshot's objects section (a save made before the action had none and loads as it was: no schema step).
/// </summary>
public static class SessionItemOff
{
    /// <summary>The map change key of an item's off flag.</summary>
    public static MapChangeKey Key(string entity) => MapChangeKey.ForEntity(entity, ObjectStateVocabulary.OffProperty);
}

public sealed partial class GameSessionOwner
{
    /// <summary>
    /// ARC-SCRIPT-2346 (IV9): whether the item <paramref name="entity"/> is switched off now: the record of its off flag when a script toggled it, else what its map data stores (<paramref name="dataOff"/>). A read only; the readers that
    /// list an item (the viewer's inventory windows) ask it with the data's bit.
    /// ARC-SCRIPT-2386 (IV15): a thrown item in flight is switched off too: the throw sets the flag (0x4B3C00) and the projectile's end clears it (0x4B2690), which is
    /// the shot's own state here (<see cref="QueryItemInFlight"/>), so no record is written for it.
    /// </summary>
    public bool QueryItemOff(SessionMapId map, string entity, bool dataOff)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(entity);
        if (ItemInFlight(entity)) return true;
        return QueryObjectChange(map, SessionItemOff.Key(entity)) is { } value ? value == 1 : dataOff;
    }

    /// <summary>
    /// ARC-SCRIPT-2324 (ledger OT8): whether the static object <paramref name="entity"/> (a scenery of a map's data) is switched off now: the record of its off flag when a script's toggle state (56) wrote one
    /// (<see cref="SessionItemOff.Key"/>, the one field an item's and an NPC's toggle also use; 1 off, 0 on over a data that stores it off), else what its map data stores (<paramref name="dataOff"/>).
    /// </summary>
    public bool QueryObjectOff(SessionMapId map, string entity, bool dataOff)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(entity);
        return QueryObjectChange(map, SessionItemOff.Key(entity)) is { } value ? value == 1 : dataOff;
    }
}
