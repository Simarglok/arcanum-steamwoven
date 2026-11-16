using Arcanum.Scripting.Scr;

namespace Arcanum.Session;

/// <summary>
/// ARC-SCRIPT-2335 (ledger IV1–IV8): what a host adds for the item set's world commands, the container vicinity focus kinds (17, 18), the name test on a looped object (condition 20) and change art number
/// (action 59) of a point 21 (insert item) script. A host that does not implement it leaves each such question unanswerable, and the session then refuses the move as for any other thing it cannot answer
/// (<see cref="GameSessionFailure.ItemScriptUnsupported"/>). It reads only.
/// </summary>
public interface ISessionItemWorldHost
{
    /// <summary>
    /// The containers in the vicinity of <paramref name="item"/>, the attachee of the run: the scan around the item's own location field, which the original does not write at an insertion (IV2). Null when the host does not
    /// know that location (an item made at run time, one the map data does not place): the session then refuses the move.
    /// </summary>
    IReadOnlyList<ScrObjectRef>? ContainersInVicinity(ScrObjectRef item);

    /// <summary>
    /// ARC-SCRIPT-2336 (FK8): the direct followers of <paramref name="leader"/>, the triggerer of the run (the focus kinds every follower and any follower, 0x44B4E0 over 0x4411C0): the hero's party followers for the hero, an NPC leader's session followers
    /// for an NPC the host knows, none for any other object. Null when the host cannot answer (no shown map): the session then refuses the move.
    /// </summary>
    IReadOnlyList<ScrObjectRef>? FollowersOf(ScrObjectRef leader);

    /// <summary>An object's name number (condition 20), or null for an object the host does not know.</summary>
    int? NameOf(ScrObjectRef obj);

    /// <summary>Whether the object is mind-controlled (condition 20 never takes such an object as named), or null for an object the host does not know.</summary>
    bool? IsMindControlled(ScrObjectRef obj);

    /// <summary>The plan of a change art number of the object to <paramref name="number"/> (the host's reading of the art files and of the object's lock identity); null when the host cannot plan it (the move is refused).</summary>
    SessionObjectArtPlan? ArtPlan(ScrObjectRef obj, int number);
}

/// <summary>
/// ARC-SCRIPT-2335 (IV5): what a change art number does to one object: the art id to write (<see cref="Art"/>; null: nothing changes) against the id its map data gives (<see cref="Baseline"/>, equal ids clear
/// the record), and for a door, window or container the closed frame the action resets with the new id (<see cref="Openable"/>, <see cref="OpenableBaseline"/>).
/// </summary>
public sealed record SessionObjectArtPlan(SessionMapId Map, string Entity, uint? Art, uint Baseline, SessionOpenable? Openable = null, SessionOpenableBaseline? OpenableBaseline = null);
