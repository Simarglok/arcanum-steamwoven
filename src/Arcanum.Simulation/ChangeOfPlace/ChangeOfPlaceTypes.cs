using System.Collections.Immutable;
using Arcanum.Simulation.Movement;

namespace Arcanum.Simulation.ChangeOfPlace;

/// <summary>Who asks for a change of place. The original's request carries no caller; the caller decides the flags it sets and whether
/// it empties the sector cache first (<see cref="ChangeOfPlaceRules.EmptiesCacheBeforeRequest"/>).</summary>
public enum ChangeOfPlaceCaller
{
    /// <summary>The hero enters a jump point.</summary>
    JumpPoint,
    /// <summary>A script's teleport.</summary>
    Script,
    /// <summary>A teleport spell (sets <see cref="ChangeOfPlaceFlags.NoWaitOrders"/>, empties the cache first).</summary>
    TeleportSpell,
    /// <summary>A non-player object sent to another place, a former follower going home (GR15).</summary>
    NpcWaypoint,
    /// <summary>The landing from the world map (blanks the screen, empties the cache first).</summary>
    WorldMapLanding,
    /// <summary>The first placement of a new game (RL29).</summary>
    NewGame,
}

/// <summary>The request's flag word (0x4D3380 record, ledger RL21). Bit 0x80000000 is set by the request itself for the local player
/// character and is refused in a caller's word.</summary>
[Flags]
public enum ChangeOfPlaceFlags : uint
{
    None = 0,
    /// <summary>0x1: the first movie.</summary>
    FirstMovie = 0x1,
    /// <summary>0x2: fade out before the change (for the local player the request starts it at once).</summary>
    FadeOut = 0x2,
    /// <summary>0x4: fade in at the end of a change that ran to its end.</summary>
    FadeIn = 0x4,
    /// <summary>0x8: advance the clocks by the request's seconds.</summary>
    AdvanceClock = 0x8,
    /// <summary>0x10: a sound.</summary>
    SoundEffect = 0x10,
    /// <summary>0x20: blank the screen (for the local player the request does it at once; the ping gives the draw back).</summary>
    BlankScreen = 0x20,
    /// <summary>0x40: the second movie.</summary>
    SecondMovie = 0x40,
    /// <summary>0x100: no wait orders for unconscious followers (the teleport spells').</summary>
    NoWaitOrders = 0x100,
    /// <summary>0x80000000: set by the request for the local player character.</summary>
    LocalPlayer = 0x80000000,
}

/// <summary>Why a <see cref="ChangeOfPlaceRequest"/> is refused. The original checks none of these; each one is a technical bound.</summary>
public enum ChangeOfPlaceFailure
{
    None,
    /// <summary>The mover's identity is empty.</summary>
    EmptyEntity,
    /// <summary>The destination map is below −1 (−1 is "the current map").</summary>
    InvalidDestinationMap,
    /// <summary>A destination cell coordinate is negative.</summary>
    InvalidDestinationCell,
    /// <summary>The flag word has a bit outside the nine the original reads.</summary>
    UnknownFlags,
    /// <summary>The caller set <see cref="ChangeOfPlaceFlags.LocalPlayer"/>, which only the request sets.</summary>
    LocalPlayerFlagSetByCaller,
    /// <summary>The clock advance is negative or past <see cref="ChangeOfPlaceRules.MaxAdvanceSeconds"/>; the original multiplies the
    /// seconds by 1000 in 32 bits and would wrap.</summary>
    InvalidAdvance,
    /// <summary>The request is for the local player but not for a player character.</summary>
    LocalPlayerIsNotCharacter,
}

/// <summary>
/// One change of place (the 0x60-byte record of 0x4D3380, ledger RL21): the object, its destination map (−1: the current map) and cell,
/// the flags and the seconds of clock to advance. <paramref name="IsPlayerCharacter"/> is the mover's type test (0x40DA20) and
/// <paramref name="IsLocalPlayer"/> the local-player test (0x40D9F0) the request makes itself.
/// </summary>
public sealed record ChangeOfPlaceRequest(
    string Entity,
    ChangeOfPlaceCaller Caller,
    int DestinationMap,
    CellCoord Destination,
    ChangeOfPlaceFlags Flags,
    int AdvanceSeconds,
    bool IsPlayerCharacter,
    bool IsLocalPlayer)
{
    /// <summary>The flag word as the slot holds it: the caller's flags plus the local-player bit.</summary>
    public ChangeOfPlaceFlags StoredFlags => IsLocalPlayer ? Flags | ChangeOfPlaceFlags.LocalPlayer : Flags;

    /// <summary>The clock advance in milliseconds (the original's 32-bit seconds × 1000, bounded here).</summary>
    public long AdvanceMilliseconds => AdvanceSeconds * 1000L;

    /// <summary>True when the request is the local player's.</summary>
    public bool HasLocalPlayerFlag => (StoredFlags & ChangeOfPlaceFlags.LocalPlayer) != 0;
}

/// <summary>What the request does at once for the local player, before the next ping (0x4D3380).</summary>
[Flags]
public enum ChangeOfPlaceRequestEffects
{
    None = 0,
    /// <summary>The fade-out starts now.</summary>
    StartFadeOut = 1,
    /// <summary>The map draw goes off now.</summary>
    BlankScreen = 2,
}

/// <summary>What became of a submitted request.</summary>
public enum ChangeOfPlaceSubmitStatus
{
    /// <summary>The slot was empty and holds the request.</summary>
    Stored,
    /// <summary>The slot held a pending request of another object and now holds this one; the old one is lost (RL21).</summary>
    Overwrote,
    /// <summary>The pending request is the local player's: this one is refused (RL21).</summary>
    RefusedLocalPlayerPending,
    /// <summary>A request that is not the local player's is being processed: this one is dropped when the ping ends (RL21, an
    /// observation of the original, which overwrites the record still being read; see the architecture note).</summary>
    LostWhileProcessing,
    /// <summary>The request fails <see cref="ChangeOfPlaceRules.Validate"/>; nothing was written.</summary>
    Invalid,
    /// <summary>The destination map is not one the host can open; nothing was written (GI7).</summary>
    DestinationNotListed,
}

/// <summary>The single request slot (0x601858 with the pending flag 0x601844 and the in-progress flag 0x60184C).</summary>
public sealed record ChangeOfPlaceSlot(ChangeOfPlaceRequest? Pending, bool Processing)
{
    /// <summary>No request, nothing in progress.</summary>
    public static ChangeOfPlaceSlot Empty { get; } = new(null, false);
}

/// <summary>The result of <see cref="ChangeOfPlaceRules.Submit"/>.</summary>
public sealed record ChangeOfPlaceSubmission(
    ChangeOfPlaceSlot Slot,
    ChangeOfPlaceSubmitStatus Status,
    ChangeOfPlaceFailure Failure,
    ChangeOfPlaceRequestEffects Effects,
    ChangeOfPlaceRequest? Replaced)
{
    /// <summary>True when the slot now holds the request.</summary>
    public bool Accepted => Status is ChangeOfPlaceSubmitStatus.Stored or ChangeOfPlaceSubmitStatus.Overwrote;
}

/// <summary>The start of one ping's work: the request to process (null when the slot is empty or a request is already being processed).</summary>
public sealed record ChangeOfPlaceBegin(ChangeOfPlaceSlot Slot, ChangeOfPlaceRequest? Request);

/// <summary>The end of one ping's work: the cleared slot and whether the map draw is given back (the ping does it after the process,
/// whatever its result, for a local-player request that blanked the screen).</summary>
public sealed record ChangeOfPlaceEnd(ChangeOfPlaceSlot Slot, bool RestoreDraw);

/// <summary>What the host answers for a plan: the map the request starts on and which destination maps it can open.</summary>
public sealed record ChangeOfPlaceWorld(int CurrentMap, Func<int, bool> DestinationListed);

/// <summary>One step of a change, in the original's order (0x4D3460, ledger RL22–RL24). The host carries each step out.</summary>
public enum ChangeOfPlaceStep
{
    FadeOutScreen,
    PlaySound,
    PlayFirstMovie,
    PlaySecondMovie,
    /// <summary>Advance the game and animation clocks by the request's seconds, on the old map.</summary>
    AdvanceClocks,
    /// <summary>Order each direct follower that is unconscious to wait (the move list rules of ARC-DEV-073).</summary>
    WaitOrders,
    /// <summary>Build the move list: the mover, its followers recursively and every inventory item.</summary>
    BuildMoveList,
    /// <summary>A player character is in the list: reset the wall-check state.</summary>
    ResetWallCheck,
    /// <summary>A player character moves to another map: reset the magic and technology AI table.</summary>
    ResetMagicTechAi,
    /// <summary>A player character moves to another map: carry the events and runs whose participants all travel, park the rest.</summary>
    CarryEvents,
    /// <summary>Same map: relocate each listed object (items stay with their holders).</summary>
    RelocateObjects,
    /// <summary>Another map: write the listed objects to the destination and leave them extinct on this map.</summary>
    WriteTravellers,
    /// <summary>A player character is in the list: remember its identity, every handle is about to die.</summary>
    KeepPlayerId,
    /// <summary>Another map, player in the list: flush the old map.</summary>
    FlushOldMap,
    /// <summary>Another map, player in the list: close the old map.</summary>
    CloseOldMap,
    /// <summary>Another map, player in the list: open the destination.</summary>
    OpenDestination,
    /// <summary>Another map, player in the list: move the view to the destination cell.</summary>
    MoveView,
    /// <summary>Another map, player in the list: find the local player again by its remembered identity.</summary>
    RebindPlayer,
    /// <summary>Player in the list: the 3 × 3 sector pre-cache at the destination.</summary>
    PreCache,
    /// <summary>The fade-in; only a change that ran to its end reaches it.</summary>
    FadeInScreen,
}

/// <summary>Whether a plan can run.</summary>
public enum ChangeOfPlaceStatus
{
    /// <summary>The steps are in <see cref="ChangeOfPlacePlan.Steps"/>.</summary>
    Planned,
    /// <summary>The request fails <see cref="ChangeOfPlaceRules.Validate"/>; no steps.</summary>
    InvalidRequest,
    /// <summary>The destination is not listed; no steps (the original would run the effects first and then stop, RL30).</summary>
    DestinationNotListed,
}

/// <summary>The ordered steps of one change of place.</summary>
public sealed record ChangeOfPlacePlan(
    ChangeOfPlaceStatus Status,
    ChangeOfPlaceFailure Failure,
    bool SameMap,
    int Destination,
    ImmutableArray<ChangeOfPlaceStep> Steps)
{
    internal static ChangeOfPlacePlan Without(ChangeOfPlaceStatus status, ChangeOfPlaceFailure failure) =>
        new(status, failure, false, -1, []);
}
