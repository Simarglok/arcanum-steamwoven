using System.Collections.Immutable;

namespace Arcanum.Simulation.ChangeOfPlace;

/// <summary>
/// ARC-DEV-112 (MIG11 slice S1, TM12, GR16): the original's one change-of-place request as pure rules, read in the executable at
/// 0x4D3380 (the request), 0x4D3330 (the ping that processes it) and 0x4D3460 (the process); the architecture note
/// <c>docs/architecture/change-of-place.md</c> lists each reading. The rules hold the single slot, the effects the request has at once
/// for the local player, and the ordered steps of a change for the cases the process distinguishes. They move nothing and have no
/// caller yet: the session executes the plan in the next slice.
/// </summary>
public static class ChangeOfPlaceRules
{
    /// <summary>The longest clock advance: the original multiplies the seconds by 1000 in 32 bits, so more would wrap.</summary>
    public const int MaxAdvanceSeconds = int.MaxValue / 1000;

    /// <summary>
    /// The flag word of a teleport spell's request (the helper 0x452650 stores 0x120, ledger CP10): no wait orders for the followers and the
    /// screen blanked for the local player; no fade and no movie.
    /// </summary>
    public const ChangeOfPlaceFlags TeleportSpellFlags = ChangeOfPlaceFlags.NoWaitOrders | ChangeOfPlaceFlags.BlankScreen;

    private const ChangeOfPlaceFlags Known =
        ChangeOfPlaceFlags.FirstMovie | ChangeOfPlaceFlags.FadeOut | ChangeOfPlaceFlags.FadeIn | ChangeOfPlaceFlags.AdvanceClock |
        ChangeOfPlaceFlags.SoundEffect | ChangeOfPlaceFlags.BlankScreen | ChangeOfPlaceFlags.SecondMovie | ChangeOfPlaceFlags.NoWaitOrders;

    /// <summary>Whether the request is well formed; the original checks none of this (the bounds are the project's).</summary>
    public static ChangeOfPlaceFailure Validate(ChangeOfPlaceRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrEmpty(request.Entity)) return ChangeOfPlaceFailure.EmptyEntity;
        if (request.DestinationMap < -1) return ChangeOfPlaceFailure.InvalidDestinationMap;
        if (request.Destination.X < 0 || request.Destination.Y < 0) return ChangeOfPlaceFailure.InvalidDestinationCell;
        if ((request.Flags & ChangeOfPlaceFlags.LocalPlayer) != 0) return ChangeOfPlaceFailure.LocalPlayerFlagSetByCaller;
        if ((request.Flags & ~Known) != 0) return ChangeOfPlaceFailure.UnknownFlags;
        if (request.AdvanceSeconds is < 0 or > MaxAdvanceSeconds) return ChangeOfPlaceFailure.InvalidAdvance;
        if (request.IsLocalPlayer && !request.IsPlayerCharacter) return ChangeOfPlaceFailure.LocalPlayerIsNotCharacter;
        return ChangeOfPlaceFailure.None;
    }

    /// <summary>
    /// True for the callers that save and unload every unlocked sector before their request (ledger RL21, RL24): the teleport spell's
    /// helper (0x452650) and the world-map landing (0x561860). A cross-map change empties the cache again at its flush; a same-map
    /// change keeps the sectors otherwise.
    /// </summary>
    [OriginalFunction(0x452650, OriginalFunctionScope.Partial)]
    [OriginalFunction(0x561860, OriginalFunctionScope.Partial)]
    public static bool EmptiesCacheBeforeRequest(ChangeOfPlaceCaller caller) =>
        caller is ChangeOfPlaceCaller.TeleportSpell or ChangeOfPlaceCaller.WorldMapLanding;

    /// <summary>
    /// Stores a request in the slot (0x4D3380). The slot refuses it only when a request is pending and is the local player's; any other
    /// pending request is overwritten and lost. A request made while a request that is not the local player's is being processed is
    /// dropped when the ping ends. The project validates the request and its destination before anything is written, which the
    /// original does not (GI7); a refused request leaves the slot and the screen as they were. For the local player the request
    /// starts the fade-out (flag 0x2) and blanks the screen (flag 0x20) at once.
    /// </summary>
    [OriginalFunction(0x4D3380, OriginalFunctionScope.Partial)]
    public static ChangeOfPlaceSubmission Submit(ChangeOfPlaceSlot slot, ChangeOfPlaceRequest request, ChangeOfPlaceWorld world)
    {
        ArgumentNullException.ThrowIfNull(slot);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(world);
        var failure = Validate(request);
        if (failure != ChangeOfPlaceFailure.None) return Refuse(slot, ChangeOfPlaceSubmitStatus.Invalid, failure);
        if (!CanOpen(request, world)) return Refuse(slot, ChangeOfPlaceSubmitStatus.DestinationNotListed, ChangeOfPlaceFailure.None);
        if (slot.Pending is { HasLocalPlayerFlag: true }) return Refuse(slot, ChangeOfPlaceSubmitStatus.RefusedLocalPlayerPending, ChangeOfPlaceFailure.None);
        if (slot.Processing) return Refuse(slot, ChangeOfPlaceSubmitStatus.LostWhileProcessing, ChangeOfPlaceFailure.None);
        var effects = ChangeOfPlaceRequestEffects.None;
        if (request.IsLocalPlayer)
        {
            if ((request.Flags & ChangeOfPlaceFlags.FadeOut) != 0) effects |= ChangeOfPlaceRequestEffects.StartFadeOut;
            if ((request.Flags & ChangeOfPlaceFlags.BlankScreen) != 0) effects |= ChangeOfPlaceRequestEffects.BlankScreen;
        }
        var replaced = slot.Pending;
        return new(new(request, false), replaced is null ? ChangeOfPlaceSubmitStatus.Stored : ChangeOfPlaceSubmitStatus.Overwrote,
            ChangeOfPlaceFailure.None, effects, replaced);
    }

    /// <summary>
    /// The ping takes the pending request and keeps it pending while it processes it (0x4D3330, in-progress flag set). An empty slot, or
    /// a slot already being processed, gives no request: the ping is not re-entered.
    /// </summary>
    [OriginalFunction(0x4D3330, OriginalFunctionScope.Partial)]
    public static ChangeOfPlaceBegin Begin(ChangeOfPlaceSlot slot)
    {
        ArgumentNullException.ThrowIfNull(slot);
        if (slot.Pending is null || slot.Processing) return new(slot, null);
        return new(slot with { Processing = true }, slot.Pending);
    }

    /// <summary>
    /// The ping clears the in-progress and pending flags after the process, whatever its result, and, for a local-player request that
    /// blanked the screen, gives the draw back. A slot that is not being processed is unchanged.
    /// </summary>
    public static ChangeOfPlaceEnd End(ChangeOfPlaceSlot slot)
    {
        ArgumentNullException.ThrowIfNull(slot);
        if (!slot.Processing || slot.Pending is null) return new(slot, false);
        var restore = slot.Pending.HasLocalPlayerFlag && (slot.Pending.Flags & ChangeOfPlaceFlags.BlankScreen) != 0;
        return new(ChangeOfPlaceSlot.Empty, restore);
    }

    /// <summary>
    /// The steps of one change, in the order of the process (0x4D3460): the effects of the request on the old map (fade-out, sound,
    /// movies, clock advance), the wait orders unless flag 0x100, the move list, the resets and the event carry for a player
    /// character, the payload (a relocation within the map, or the travellers written to the destination), then, when a player
    /// character is in the list, the identity kept, for another map the flush, close and open, the view and the rebinding, the
    /// pre-cache (the plan assumes the identity kept for it resolves), and last the fade-in. A change with no player character in the list does not open the destination and goes
    /// straight to the fade-in. <paramref name="moveListHasPlayerCharacter"/> is the answer of the move list walk (false for an object
    /// that is not a player character or for a walk that failed).
    /// </summary>
    [OriginalFunction(0x4D3460, OriginalFunctionScope.Partial)]
    public static ChangeOfPlacePlan Plan(ChangeOfPlaceRequest request, ChangeOfPlaceWorld world, bool moveListHasPlayerCharacter)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(world);
        var failure = Validate(request);
        if (failure != ChangeOfPlaceFailure.None) return ChangeOfPlacePlan.Without(ChangeOfPlaceStatus.InvalidRequest, failure);
        if (!CanOpen(request, world)) return ChangeOfPlacePlan.Without(ChangeOfPlaceStatus.DestinationNotListed, ChangeOfPlaceFailure.None);
        var destination = request.DestinationMap == -1 ? world.CurrentMap : request.DestinationMap;
        var same = destination == world.CurrentMap;
        var flags = request.Flags;
        var steps = ImmutableArray.CreateBuilder<ChangeOfPlaceStep>();
        if ((flags & ChangeOfPlaceFlags.FadeOut) != 0) steps.Add(ChangeOfPlaceStep.FadeOutScreen);
        if ((flags & ChangeOfPlaceFlags.SoundEffect) != 0) steps.Add(ChangeOfPlaceStep.PlaySound);
        if ((flags & ChangeOfPlaceFlags.FirstMovie) != 0) steps.Add(ChangeOfPlaceStep.PlayFirstMovie);
        if ((flags & ChangeOfPlaceFlags.SecondMovie) != 0) steps.Add(ChangeOfPlaceStep.PlaySecondMovie);
        if ((flags & ChangeOfPlaceFlags.AdvanceClock) != 0) steps.Add(ChangeOfPlaceStep.AdvanceClocks);
        if ((flags & ChangeOfPlaceFlags.NoWaitOrders) == 0) steps.Add(ChangeOfPlaceStep.WaitOrders);
        steps.Add(ChangeOfPlaceStep.BuildMoveList);
        if (moveListHasPlayerCharacter) steps.Add(ChangeOfPlaceStep.ResetWallCheck);
        if (!same && request.IsPlayerCharacter)
        {
            steps.Add(ChangeOfPlaceStep.ResetMagicTechAi);
            steps.Add(ChangeOfPlaceStep.CarryEvents);
        }
        steps.Add(same ? ChangeOfPlaceStep.RelocateObjects : ChangeOfPlaceStep.WriteTravellers);
        if (moveListHasPlayerCharacter)
        {
            steps.Add(ChangeOfPlaceStep.KeepPlayerId);
            if (!same)
            {
                steps.Add(ChangeOfPlaceStep.FlushOldMap);
                steps.Add(ChangeOfPlaceStep.CloseOldMap);
                steps.Add(ChangeOfPlaceStep.OpenDestination);
                steps.Add(ChangeOfPlaceStep.MoveView);
                steps.Add(ChangeOfPlaceStep.RebindPlayer);
            }
            steps.Add(ChangeOfPlaceStep.PreCache);
        }
        if ((flags & ChangeOfPlaceFlags.FadeIn) != 0) steps.Add(ChangeOfPlaceStep.FadeInScreen);
        return new(ChangeOfPlaceStatus.Planned, ChangeOfPlaceFailure.None, same, destination, steps.ToImmutable());
    }

    // A destination on another map must be one the host can open; the current map always can.
    private static bool CanOpen(ChangeOfPlaceRequest request, ChangeOfPlaceWorld world) =>
        request.DestinationMap == -1 || request.DestinationMap == world.CurrentMap || world.DestinationListed(request.DestinationMap);

    private static ChangeOfPlaceSubmission Refuse(ChangeOfPlaceSlot slot, ChangeOfPlaceSubmitStatus status, ChangeOfPlaceFailure failure) =>
        new(slot, status, failure, ChangeOfPlaceRequestEffects.None, null);
}
