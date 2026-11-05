using Arcanum.Simulation.ChangeOfPlace;
using Arcanum.Simulation.Movement;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Simulation.ChangeOfPlace.ChangeOfPlaceFlags;
using static Arcanum.Simulation.ChangeOfPlace.ChangeOfPlaceStep;

namespace Arcanum.Simulation.Tests.ChangeOfPlace;

// ARC-DEV-112 (MIG11 S1): the request slot and the step order of 0x4D3380, 0x4D3330 and 0x4D3460. Maps, cells and names are invented.
[TestClass]
public sealed class ChangeOfPlaceRulesTests
{
    private static readonly ChangeOfPlaceWorld World = new(3, map => map is 3 or 7 or 9);

    private static ChangeOfPlaceRequest Hero(int map = 7, ChangeOfPlaceFlags flags = None, int seconds = 0, ChangeOfPlaceCaller caller = ChangeOfPlaceCaller.JumpPoint) =>
        new("hero", caller, map, new CellCoord(10, 20), flags, seconds, IsPlayerCharacter: true, IsLocalPlayer: true);

    private static ChangeOfPlaceRequest Npc(int map = 7, ChangeOfPlaceFlags flags = None) =>
        new("npc-1", ChangeOfPlaceCaller.NpcWaypoint, map, new CellCoord(4, 5), flags, 0, IsPlayerCharacter: false, IsLocalPlayer: false);

    private static ChangeOfPlaceSubmission Stored(ChangeOfPlaceSlot slot, ChangeOfPlaceRequest request, ChangeOfPlaceSubmitStatus expected = ChangeOfPlaceSubmitStatus.Stored)
    {
        var result = ChangeOfPlaceRules.Submit(slot, request, World);
        Assert.AreEqual(expected, result.Status);
        return result;
    }

    private static ChangeOfPlaceStep[] Steps(ChangeOfPlaceRequest request, bool player)
    {
        var plan = ChangeOfPlaceRules.Plan(request, World, player);
        Assert.AreEqual(ChangeOfPlaceStatus.Planned, plan.Status);
        return [.. plan.Steps];
    }

    [TestMethod]
    public void Validate_accepts_a_whole_request_and_names_each_refusal()
    {
        Assert.AreEqual(ChangeOfPlaceFailure.None, ChangeOfPlaceRules.Validate(Hero(flags: FadeOut | FadeIn | AdvanceClock, seconds: ChangeOfPlaceRules.MaxAdvanceSeconds)));
        Assert.AreEqual(ChangeOfPlaceFailure.None, ChangeOfPlaceRules.Validate(Hero(map: -1)));
        Assert.AreEqual(ChangeOfPlaceFailure.EmptyEntity, ChangeOfPlaceRules.Validate(Hero() with { Entity = "" }));
        Assert.AreEqual(ChangeOfPlaceFailure.InvalidDestinationMap, ChangeOfPlaceRules.Validate(Hero(map: -2)));
        Assert.AreEqual(ChangeOfPlaceFailure.InvalidDestinationCell, ChangeOfPlaceRules.Validate(Hero() with { Destination = new(-1, 0) }));
        Assert.AreEqual(ChangeOfPlaceFailure.InvalidDestinationCell, ChangeOfPlaceRules.Validate(Hero() with { Destination = new(0, -1) }));
        Assert.AreEqual(ChangeOfPlaceFailure.LocalPlayerFlagSetByCaller, ChangeOfPlaceRules.Validate(Hero(flags: LocalPlayer)));
        Assert.AreEqual(ChangeOfPlaceFailure.UnknownFlags, ChangeOfPlaceRules.Validate(Hero(flags: (ChangeOfPlaceFlags)0x80)));
        Assert.AreEqual(ChangeOfPlaceFailure.UnknownFlags, ChangeOfPlaceRules.Validate(Hero(flags: (ChangeOfPlaceFlags)0x200)));
        Assert.AreEqual(ChangeOfPlaceFailure.InvalidAdvance, ChangeOfPlaceRules.Validate(Hero(seconds: -1)));
        Assert.AreEqual(ChangeOfPlaceFailure.InvalidAdvance, ChangeOfPlaceRules.Validate(Hero(seconds: ChangeOfPlaceRules.MaxAdvanceSeconds + 1)));
        Assert.AreEqual(ChangeOfPlaceFailure.LocalPlayerIsNotCharacter, ChangeOfPlaceRules.Validate(Hero() with { IsPlayerCharacter = false }));
    }

    [TestMethod]
    public void The_clock_advance_is_seconds_times_1000_without_wrapping()
    {
        Assert.AreEqual(2_147_483_000L, Hero(seconds: ChangeOfPlaceRules.MaxAdvanceSeconds).AdvanceMilliseconds);
        Assert.AreEqual(90_000L, Hero(seconds: 90).AdvanceMilliseconds);
    }

    [TestMethod]
    public void The_local_player_request_carries_the_local_bit_and_starts_fade_and_blank_at_once()
    {
        var result = Stored(ChangeOfPlaceSlot.Empty, Hero(flags: FadeOut | BlankScreen));
        Assert.AreEqual(ChangeOfPlaceRequestEffects.StartFadeOut | ChangeOfPlaceRequestEffects.BlankScreen, result.Effects);
        Assert.IsTrue(result.Slot.Pending!.HasLocalPlayerFlag);
        Assert.AreEqual(FadeOut | BlankScreen | LocalPlayer, result.Slot.Pending.StoredFlags);
        Assert.IsFalse(result.Slot.Processing);

        // Another object's request has no effect at once and no local bit.
        var other = Stored(ChangeOfPlaceSlot.Empty, Npc(flags: FadeOut | BlankScreen));
        Assert.AreEqual(ChangeOfPlaceRequestEffects.None, other.Effects);
        Assert.IsFalse(other.Slot.Pending!.HasLocalPlayerFlag);
        // A local request with neither flag does nothing at once.
        Assert.AreEqual(ChangeOfPlaceRequestEffects.None, Stored(ChangeOfPlaceSlot.Empty, Hero(flags: FadeIn)).Effects);
        Assert.AreEqual(ChangeOfPlaceRequestEffects.StartFadeOut, Stored(ChangeOfPlaceSlot.Empty, Hero(flags: FadeOut)).Effects);
    }

    [TestMethod]
    public void A_pending_local_player_request_refuses_every_other_request()
    {
        var first = Stored(ChangeOfPlaceSlot.Empty, Hero(map: 7, flags: FadeOut));
        foreach (var next in new[] { Npc(), Hero(map: 9) })
        {
            var refused = Stored(first.Slot, next, ChangeOfPlaceSubmitStatus.RefusedLocalPlayerPending);
            Assert.AreSame(first.Slot, refused.Slot);
            Assert.AreEqual(ChangeOfPlaceRequestEffects.None, refused.Effects);
            Assert.IsFalse(refused.Accepted);
        }
    }

    [TestMethod]
    public void Any_other_pending_request_is_overwritten_and_the_old_one_lost()
    {
        var first = Stored(ChangeOfPlaceSlot.Empty, Npc(map: 7));
        var second = Stored(first.Slot, Npc(map: 9), ChangeOfPlaceSubmitStatus.Overwrote);
        Assert.AreEqual(9, second.Slot.Pending!.DestinationMap);
        Assert.AreSame(first.Slot.Pending, second.Replaced);
        Assert.IsTrue(second.Accepted);
        // The local player's request may overwrite a pending request of another object.
        var hero = Stored(second.Slot, Hero(), ChangeOfPlaceSubmitStatus.Overwrote);
        Assert.IsTrue(hero.Slot.Pending!.HasLocalPlayerFlag);
    }

    [TestMethod]
    public void A_request_made_while_a_non_player_request_is_processed_is_lost_and_the_one_in_flight_stays()
    {
        var stored = Stored(ChangeOfPlaceSlot.Empty, Npc(map: 7));
        var begin = ChangeOfPlaceRules.Begin(stored.Slot);
        Assert.IsTrue(begin.Slot.Processing);
        var lost = Stored(begin.Slot, Hero(map: 9), ChangeOfPlaceSubmitStatus.LostWhileProcessing);
        Assert.IsFalse(lost.Accepted);
        Assert.AreSame(begin.Slot, lost.Slot);
        Assert.AreEqual(ChangeOfPlaceRequestEffects.None, lost.Effects);
        var end = ChangeOfPlaceRules.End(lost.Slot);
        Assert.AreSame(ChangeOfPlaceSlot.Empty, end.Slot);
        Assert.IsFalse(end.RestoreDraw);
    }

    [TestMethod]
    public void A_request_made_while_the_local_player_request_is_processed_is_refused()
    {
        var begin = ChangeOfPlaceRules.Begin(Stored(ChangeOfPlaceSlot.Empty, Hero()).Slot);
        Stored(begin.Slot, Npc(), ChangeOfPlaceSubmitStatus.RefusedLocalPlayerPending);
    }

    [TestMethod]
    public void The_ping_is_not_re_entered_and_an_empty_slot_gives_nothing()
    {
        Assert.IsNull(ChangeOfPlaceRules.Begin(ChangeOfPlaceSlot.Empty).Request);
        var begin = ChangeOfPlaceRules.Begin(Stored(ChangeOfPlaceSlot.Empty, Npc()).Slot);
        Assert.IsNotNull(begin.Request);
        var again = ChangeOfPlaceRules.Begin(begin.Slot);
        Assert.IsNull(again.Request);
        Assert.AreSame(begin.Slot, again.Slot);
        // End on a slot that is not processed changes nothing.
        var idle = Stored(ChangeOfPlaceSlot.Empty, Npc()).Slot;
        Assert.AreSame(idle, ChangeOfPlaceRules.End(idle).Slot);
        Assert.IsFalse(ChangeOfPlaceRules.End(idle).RestoreDraw);
    }

    [TestMethod]
    public void The_ping_gives_the_draw_back_only_for_a_local_request_that_blanked()
    {
        ChangeOfPlaceEnd Ended(ChangeOfPlaceRequest request) =>
            ChangeOfPlaceRules.End(ChangeOfPlaceRules.Begin(Stored(ChangeOfPlaceSlot.Empty, request).Slot).Slot);
        Assert.IsTrue(Ended(Hero(flags: BlankScreen)).RestoreDraw);
        Assert.IsFalse(Ended(Hero(flags: FadeOut)).RestoreDraw);
        Assert.IsFalse(Ended(Npc(flags: BlankScreen)).RestoreDraw);
        Assert.AreSame(ChangeOfPlaceSlot.Empty, Ended(Hero(flags: BlankScreen)).Slot);
        // After the end a new request is stored, not refused.
        Stored(Ended(Hero(flags: BlankScreen)).Slot, Hero());
    }

    [TestMethod]
    public void A_refused_request_leaves_the_slot_untouched_and_does_nothing_at_once()
    {
        var pending = Stored(ChangeOfPlaceSlot.Empty, Npc(map: 7)).Slot;
        var invalid = ChangeOfPlaceRules.Submit(pending, Hero(map: -5, flags: FadeOut | BlankScreen), World);
        Assert.AreEqual(ChangeOfPlaceSubmitStatus.Invalid, invalid.Status);
        Assert.AreEqual(ChangeOfPlaceFailure.InvalidDestinationMap, invalid.Failure);
        Assert.AreSame(pending, invalid.Slot);
        Assert.AreEqual(ChangeOfPlaceRequestEffects.None, invalid.Effects);
        // GI7: an unlisted destination is refused before the fade starts, so the screen is never left blanked.
        var unlisted = ChangeOfPlaceRules.Submit(pending, Hero(map: 42, flags: FadeOut | BlankScreen), World);
        Assert.AreEqual(ChangeOfPlaceSubmitStatus.DestinationNotListed, unlisted.Status);
        Assert.AreSame(pending, unlisted.Slot);
        Assert.AreEqual(ChangeOfPlaceRequestEffects.None, unlisted.Effects);
        Assert.IsFalse(unlisted.Accepted);
    }

    [TestMethod]
    public void The_current_map_needs_no_listing_and_minus_one_means_the_current_map()
    {
        var unlisted = new ChangeOfPlaceWorld(5, _ => false);
        Assert.AreEqual(ChangeOfPlaceSubmitStatus.Stored, ChangeOfPlaceRules.Submit(ChangeOfPlaceSlot.Empty, Hero(map: 5), unlisted).Status);
        Assert.AreEqual(ChangeOfPlaceSubmitStatus.Stored, ChangeOfPlaceRules.Submit(ChangeOfPlaceSlot.Empty, Hero(map: -1), unlisted).Status);
        Assert.AreEqual(ChangeOfPlaceSubmitStatus.DestinationNotListed, ChangeOfPlaceRules.Submit(ChangeOfPlaceSlot.Empty, Hero(map: 6), unlisted).Status);
        var plan = ChangeOfPlaceRules.Plan(Hero(map: -1), unlisted, true);
        Assert.IsTrue(plan.SameMap);
        Assert.AreEqual(5, plan.Destination);
    }

    [TestMethod]
    public void Only_the_spell_helper_and_the_world_map_landing_empty_the_cache_first()
    {
        foreach (var caller in Enum.GetValues<ChangeOfPlaceCaller>())
            Assert.AreEqual(caller is ChangeOfPlaceCaller.TeleportSpell or ChangeOfPlaceCaller.WorldMapLanding,
                ChangeOfPlaceRules.EmptiesCacheBeforeRequest(caller), $"{caller}");
    }

    [TestMethod]
    public void A_player_change_to_another_map_runs_the_full_order()
    {
        CollectionAssert.AreEqual(new[]
        {
            FadeOutScreen, PlaySound, PlayFirstMovie, PlaySecondMovie, AdvanceClocks, WaitOrders, BuildMoveList, ResetWallCheck, ResetMagicTechAi, CarryEvents,
            WriteTravellers, KeepPlayerId, FlushOldMap, CloseOldMap, OpenDestination, MoveView, RebindPlayer, PreCache, FadeInScreen,
        }, Steps(Hero(map: 7, flags: FadeOut | SoundEffect | FirstMovie | SecondMovie | AdvanceClock | FadeIn, seconds: 3600), player: true));
        var plan = ChangeOfPlaceRules.Plan(Hero(map: 7), World, true);
        Assert.IsFalse(plan.SameMap);
        Assert.AreEqual(7, plan.Destination);
    }

    [TestMethod]
    public void The_effects_of_the_request_come_in_the_original_order_whatever_the_bit_order()
    {
        CollectionAssert.AreEqual(new[] { FadeOutScreen, PlaySound, PlayFirstMovie, PlaySecondMovie, AdvanceClocks },
            Steps(Hero(map: 3, flags: AdvanceClock | SecondMovie | FirstMovie | SoundEffect | FadeOut), true).Take(5).ToArray());
        // No flag, no effect step; the first step is the wait orders.
        Assert.AreEqual(WaitOrders, Steps(Hero(map: 3), true)[0]);
    }

    [TestMethod]
    public void A_same_map_change_relocates_keeps_the_sectors_and_does_no_flush_or_open()
    {
        CollectionAssert.AreEqual(new[] { WaitOrders, BuildMoveList, ResetWallCheck, RelocateObjects, KeepPlayerId, PreCache, FadeInScreen },
            Steps(Hero(map: 3, flags: FadeIn), true));
        CollectionAssert.AreEqual(Steps(Hero(map: 3, flags: FadeIn), true), Steps(Hero(map: -1, flags: FadeIn), true));
        var plan = ChangeOfPlaceRules.Plan(Hero(map: 3), World, true);
        Assert.IsTrue(plan.SameMap);
        Assert.AreEqual(3, plan.Destination);
    }

    [TestMethod]
    public void The_teleport_spell_flag_skips_the_wait_orders()
    {
        var steps = Steps(Hero(map: 3, flags: NoWaitOrders, caller: ChangeOfPlaceCaller.TeleportSpell), true);
        Assert.IsFalse(steps.Contains(WaitOrders));
        Assert.AreEqual(BuildMoveList, steps[0]);
    }

    [TestMethod]
    public void A_non_player_mover_to_another_map_never_opens_it_and_goes_to_the_fade_in()
    {
        CollectionAssert.AreEqual(new[] { WaitOrders, BuildMoveList, WriteTravellers, FadeInScreen }, Steps(Npc(map: 7, flags: FadeIn), player: false));
        CollectionAssert.AreEqual(new[] { WaitOrders, BuildMoveList, WriteTravellers }, Steps(Npc(map: 7), player: false));
    }

    [TestMethod]
    public void A_player_mover_whose_walk_failed_still_resets_and_carries_but_does_not_open()
    {
        CollectionAssert.AreEqual(new[] { WaitOrders, BuildMoveList, ResetMagicTechAi, CarryEvents, WriteTravellers, FadeInScreen },
            Steps(Hero(map: 7, flags: FadeIn), player: false));
    }

    [TestMethod]
    public void A_non_player_mover_within_the_map_is_a_plain_relocation()
    {
        CollectionAssert.AreEqual(new[] { WaitOrders, BuildMoveList, RelocateObjects }, Steps(Npc(map: 3), player: false));
    }

    [TestMethod]
    public void A_non_player_mover_with_a_player_in_its_list_resets_keeps_the_identity_and_pre_caches_but_carries_nothing()
    {
        // The list's player flag decides the resets, the identity and the open; the mover's own type decides the event carry.
        CollectionAssert.AreEqual(new[]
        {
            WaitOrders, BuildMoveList, ResetWallCheck, WriteTravellers, KeepPlayerId, FlushOldMap, CloseOldMap, OpenDestination, MoveView, RebindPlayer, PreCache,
        }, Steps(Npc(map: 7), player: true));
    }

    [TestMethod]
    public void A_refused_or_unlisted_request_has_no_plan()
    {
        var invalid = ChangeOfPlaceRules.Plan(Hero(map: -9), World, true);
        Assert.AreEqual(ChangeOfPlaceStatus.InvalidRequest, invalid.Status);
        Assert.AreEqual(ChangeOfPlaceFailure.InvalidDestinationMap, invalid.Failure);
        Assert.AreEqual(0, invalid.Steps.Length);
        var unlisted = ChangeOfPlaceRules.Plan(Hero(map: 42), World, true);
        Assert.AreEqual(ChangeOfPlaceStatus.DestinationNotListed, unlisted.Status);
        Assert.AreEqual(0, unlisted.Steps.Length);
    }

    [TestMethod]
    public void The_rules_refuse_nulls()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => ChangeOfPlaceRules.Validate(null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => ChangeOfPlaceRules.Submit(null!, Hero(), World));
        Assert.ThrowsExactly<ArgumentNullException>(() => ChangeOfPlaceRules.Submit(ChangeOfPlaceSlot.Empty, null!, World));
        Assert.ThrowsExactly<ArgumentNullException>(() => ChangeOfPlaceRules.Submit(ChangeOfPlaceSlot.Empty, Hero(), null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => ChangeOfPlaceRules.Begin(null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => ChangeOfPlaceRules.End(null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => ChangeOfPlaceRules.Plan(null!, World, true));
        Assert.ThrowsExactly<ArgumentNullException>(() => ChangeOfPlaceRules.Plan(Hero(), null!, true));
    }
}
