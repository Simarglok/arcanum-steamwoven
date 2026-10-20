using Arcanum.Simulation.Locks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Simulation.Locks.LockRules;

namespace Arcanum.Simulation.Tests.Locks;

// ARC-WORLD-050: the lock rules of ledger LK2–LK10. Flags, keys and hours are invented.
[TestClass]
public sealed class LockRulesTests
{
    private static readonly int[] NoKeys = [];

    [TestMethod]
    public void Locked_reads_busted_never_locked_and_the_day_and_night_bits_at_the_hour()
    {
        Assert.IsTrue(IsLocked(LockedBit, 3));
        Assert.IsFalse(IsLocked(0, 3));
        Assert.IsFalse(IsLocked(LockedBit | BustedBit, 12));
        Assert.IsFalse(IsLocked(LockedBit | NeverLockedBit, 12));
        // Locked by day holds 7..21, locked by night outside it.
        foreach (var (hour, day, night) in new[] { (6, false, true), (7, true, false), (21, true, false), (22, false, true), (0, false, true) })
        {
            Assert.AreEqual(day, IsLocked(LockedBit | LockedByDayBit, hour), $"day {hour}");
            Assert.AreEqual(night, IsLocked(LockedBit | LockedByNightBit, hour), $"night {hour}");
        }
        // Always locked is not read by the locked query; the locked bit decides.
        Assert.IsFalse(IsLocked(AlwaysLockedBit, 12));
    }

    [TestMethod]
    public void Only_unlocking_a_locked_object_and_jamming_a_free_one_schedule_the_lock_event()
    {
        Assert.AreEqual(new LockWrite(0x40, true, false), SetLocked(LockedBit | 0x40, false, 12));
        Assert.AreEqual(new LockWrite(0x40, false, false), SetLocked(0x40, false, 12));
        Assert.AreEqual(new LockWrite(LockedBit | 0x40, false, false), SetLocked(0x40, true, 12)); // locked by night, unlocked by day
        Assert.AreEqual(new LockWrite(LockedBit | JammedBit, true, true), SetJammed(LockedBit, true, 12));
        Assert.AreEqual(new LockWrite(LockedBit | JammedBit, false, true), SetJammed(LockedBit | JammedBit, true, 12));
        Assert.AreEqual(new LockWrite(LockedBit, false, true), SetJammed(LockedBit | JammedBit, false, 12));
        // The event relocks and frees the object whatever scheduled it.
        Assert.AreEqual(LockedBit | NeverLockedBit, AfterReset(JammedBit | NeverLockedBit));
    }

    [TestMethod]
    public void Key_one_is_a_master_value_on_either_side_and_zero_never_fits()
    {
        Assert.IsTrue(KeyFits(7, 7));
        Assert.IsFalse(KeyFits(7, 8));
        Assert.IsTrue(KeyFits(1, 8));
        Assert.IsTrue(KeyFits(7, 1));
        Assert.IsFalse(KeyFits(0, 1));
        Assert.IsFalse(KeyFits(1, 0));
    }

    [TestMethod]
    public void A_portal_attempt_checks_in_the_original_order()
    {
        OpenAttempt Portal(int flags, OpenAttemptWalker walker = OpenAttemptWalker.Player, bool script = false, bool leaving = false,
            int[]? keys = null, int hour = 12) => Attempt(new(OpenableKind.Portal, flags, hour, walker, script, 5, keys ?? NoKeys, leaving));
        Assert.AreEqual(OpenAttempt.Opens, Portal(BustedBit | JammedBit, OpenAttemptWalker.NotCritter, script: true));
        Assert.AreEqual(OpenAttempt.NotCritter, Portal(0, OpenAttemptWalker.NotCritter, script: true));
        Assert.AreEqual(OpenAttempt.ScriptRefused, Portal(JammedBit, script: true));
        Assert.AreEqual(OpenAttempt.Jammed, Portal(JammedBit | MagicallyHeldBit));
        Assert.AreEqual(OpenAttempt.MagicallyHeld, Portal(MagicallyHeldBit, OpenAttemptWalker.FreeNpc, leaving: true));
        // An NPC without a PC leader and a step leaving a building pass any lock, unless the portal is always locked.
        Assert.AreEqual(OpenAttempt.Opens, Portal(LockedBit, OpenAttemptWalker.FreeNpc));
        Assert.AreEqual(OpenAttempt.Opens, Portal(LockedBit, leaving: true));
        Assert.AreEqual(OpenAttempt.Locked, Portal(LockedBit, OpenAttemptWalker.LedNpc));
        Assert.AreEqual(OpenAttempt.Opens, Portal(LockedBit, OpenAttemptWalker.LedNpc, leaving: true));
        Assert.AreEqual(OpenAttempt.Locked, Portal(LockedBit | AlwaysLockedBit, OpenAttemptWalker.FreeNpc, leaving: true));
        Assert.AreEqual(OpenAttempt.Opens, Portal(LockedBit | AlwaysLockedBit, keys: [9, 5]));
        Assert.AreEqual(OpenAttempt.Opens, Portal(LockedBit | LockedByNightBit, hour: 12));
        Assert.AreEqual(OpenAttempt.Locked, Portal(LockedBit | LockedByNightBit, hour: 23));
    }

    [TestMethod]
    public void A_container_attempt_has_no_walker_or_leaving_exemption()
    {
        OpenAttempt Container(int flags, OpenAttemptWalker walker = OpenAttemptWalker.Player, bool leaving = false, bool junk = false) =>
            Attempt(new(OpenableKind.Container, flags, 12, walker, false, 5, NoKeys, leaving, junk));
        Assert.AreEqual(OpenAttempt.Opens, Container(JammedBit, junk: true));
        Assert.AreEqual(OpenAttempt.Locked, Container(LockedBit, OpenAttemptWalker.FreeNpc, leaving: true));
        Assert.AreEqual(OpenAttempt.Locked, Container(LockedBit, OpenAttemptWalker.NotCritter));
        Assert.AreEqual(OpenAttempt.Jammed, Container(JammedBit));
        Assert.AreEqual(OpenAttempt.Opens, Container(0));
    }

    [TestMethod]
    public void Door_frames_follow_the_rotation_ranges_and_windows_use_frames_0_and_1()
    {
        var east = new PortalArt(true, 3, 7); // rotation 3 → 2: frames 1..3
        var north = new PortalArt(true, 7, 7); // rotation 7 → 6: frames 4..6
        Assert.AreEqual(new PortalStep(1, PortalFlagChange.Set), PortalFrameRules.OpenStep(east, 0));
        Assert.AreEqual(new PortalStep(3, PortalFlagChange.None), PortalFrameRules.OpenStep(east, 2));
        Assert.IsNull(PortalFrameRules.OpenStep(east, 3));
        Assert.AreEqual(new PortalStep(4, PortalFlagChange.Set), PortalFrameRules.OpenStep(north, 0));
        Assert.AreEqual(new PortalStep(4, PortalFlagChange.Set), PortalFrameRules.OpenStep(north, 2)); // outside its range
        Assert.AreEqual(new PortalStep(0, PortalFlagChange.Cleared), PortalFrameRules.CloseStep(north, 4));
        Assert.AreEqual(new PortalStep(6, PortalFlagChange.None), PortalFrameRules.CloseStep(north, 2));
        Assert.AreEqual(new PortalStep(2, PortalFlagChange.None), PortalFrameRules.CloseStep(east, 3));
        Assert.IsNull(PortalFrameRules.CloseStep(east, 0));
        Assert.AreEqual(3, PortalFrameRules.Toggled(east, 0));
        Assert.AreEqual(0, PortalFrameRules.Toggled(north, 5));
        var window = new PortalArt(false, 5, 2);
        Assert.AreEqual(new PortalStep(1, PortalFlagChange.Set), PortalFrameRules.OpenStep(window, 0));
        Assert.IsNull(PortalFrameRules.OpenStep(window, 1));
        Assert.AreEqual(new PortalStep(0, PortalFlagChange.Cleared), PortalFrameRules.CloseStep(window, 1));
        // A one-frame ART never moves.
        Assert.IsNull(PortalFrameRules.OpenStep(new PortalArt(false, 5, 1), 0));
        Assert.IsNull(PortalFrameRules.OpenStep(new PortalArt(true, 3, 1), 0));
    }
}
