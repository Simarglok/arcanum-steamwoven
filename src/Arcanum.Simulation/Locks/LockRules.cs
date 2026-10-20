namespace Arcanum.Simulation.Locks;

/// <summary>The two kinds of object that carry a lock (ledger LK1): portals (doors and windows) and containers.</summary>
public enum OpenableKind { Portal = 1, Container = 2 }

/// <summary>
/// Result of an open attempt (ledger LK7, LK8), in the original's check order. Values are Steamwoven identifiers, not the
/// original's result codes.
/// </summary>
public enum OpenAttempt
{
    /// <summary>The walker may open it (busted, unlocked, leaving a building, an NPC without a PC leader, or the key).</summary>
    Opens,
    /// <summary>Locked, and the party holds no key that fits.</summary>
    Locked,
    Jammed,
    MagicallyHeld,
    /// <summary>The object's unlock script (SAP 3) refused.</summary>
    ScriptRefused,
    /// <summary>The walker is not a PC or an NPC.</summary>
    NotCritter,
}

/// <summary>The walker of an open attempt: the player, an NPC with a PC leader, an NPC without one, or no critter.</summary>
public enum OpenAttemptWalker { Player, LedNpc, FreeNpc, NotCritter }

/// <summary>
/// Explicit inputs of one open attempt: the object's kind and flags, the game hour, whether its unlock script refused, the
/// lock's key id, the key ids the walker's party holds (keys and key rings, ledger LK9), and for a portal whether the
/// step leaves a building as both of the original's attempts see it (ledger LV1–LV5).
/// </summary>
public readonly record struct OpenAttemptInput(OpenableKind Kind, int Flags, int Hour, OpenAttemptWalker Walker, bool ScriptRefused,
    int LockKey, IReadOnlyCollection<int> PartyKeys, bool Leaving = false, bool JunkPile = false);

/// <summary>A lock or jam write: the new flags, whether the original schedules its Lock event now, and the locked state after it.</summary>
public readonly record struct LockWrite(int Flags, bool SchedulesReset, bool Locked);

/// <summary>
/// ARC-WORLD-050: the lock rules of portals and containers as the retail 1.0.7.4 executable applies them (ledger
/// LK1–LK9). Pure functions over the object's lock flags (portal field 46, container field 56: the same bits for both),
/// the game hour and the caller's facts.
/// </summary>
public static class LockRules
{
    public const int LockedBit = 0x1, JammedBit = 0x2, MagicallyHeldBit = 0x4, NeverLockedBit = 0x8, AlwaysLockedBit = 0x10,
        LockedByDayBit = 0x20, LockedByNightBit = 0x40, BustedBit = 0x80;

    /// <summary>The Lock event after an unlock of a locked object: one game hour (ledger LK3).</summary>
    public const long UnlockResetDelayMs = 3_600_000;
    /// <summary>The Lock event after a jam of an object that was not jammed: one game day (ledger LK4).</summary>
    public const long JamResetDelayMs = 86_400_000;

    /// <summary>Whether the object counts as locked at <paramref name="hour"/> (ledger LK2). Always locked is not read here.</summary>
    [OriginalFunction(0x441D40, OriginalFunctionScope.Full)]
    public static bool IsLocked(int flags, int hour)
    {
        if ((flags & (BustedBit | NeverLockedBit)) != 0) return false;
        if ((flags & LockedByDayBit) != 0 && hour is < 7 or > 21) return false;
        if ((flags & LockedByNightBit) != 0 && hour is >= 7 and <= 21) return false;
        return (flags & LockedBit) != 0;
    }

    /// <summary>Writes the locked bit (ledger LK3); unlocking a locked object schedules the Lock event an hour later.</summary>
    [OriginalFunction(0x441DD0, OriginalFunctionScope.Full)]
    public static LockWrite SetLocked(int flags, bool locked, int hour)
    {
        var schedules = (flags & LockedBit) != 0 && !locked;
        var written = (flags & ~LockedBit) | (locked ? LockedBit : 0);
        return new(written, schedules, IsLocked(written, hour));
    }

    /// <summary>Writes the jammed bit (ledger LK4); jamming an object that was not jammed schedules the Lock event a day later.</summary>
    [OriginalFunction(0x441F10, OriginalFunctionScope.Full)]
    public static LockWrite SetJammed(int flags, bool jammed, int hour)
    {
        var schedules = (flags & JammedBit) == 0 && jammed;
        var written = (flags & ~JammedBit) | (jammed ? JammedBit : 0);
        return new(written, schedules, IsLocked(written, hour));
    }

    /// <summary>The flags after a Lock event (ledger LK5): locked and not jammed; it schedules nothing. The caller also closes
    /// the object (<see cref="PortalFrameRules.Closed"/>, or a container whose frame is exactly 1 goes to 0).</summary>
    [OriginalFunction(0x441E90, OriginalFunctionScope.Partial)]
    public static int AfterReset(int flags) => (flags | LockedBit) & ~JammedBit;

    /// <summary>Whether a key of <paramref name="itemKey"/> fits a lock of <paramref name="lockKey"/> (ledger LK9): 0 never
    /// fits, a lock of 1 takes any key, a key of 1 opens any lock, otherwise they must be equal.</summary>
    [OriginalFunction(0x463340, OriginalFunctionScope.Full)]
    public static bool KeyFits(int lockKey, int itemKey) =>
        lockKey != 0 && itemKey != 0 && (lockKey == 1 || itemKey == 1 || lockKey == itemKey);

    /// <summary>The open attempt of a portal (ledger LK7) or a container (LK8) for an unbusted, undestroyed object.</summary>
    [OriginalFunction(0x4AEB70, OriginalFunctionScope.Partial)]
    [OriginalFunction(0x4AED80, OriginalFunctionScope.Partial)]
    public static OpenAttempt Attempt(OpenAttemptInput input)
    {
        ArgumentNullException.ThrowIfNull(input.PartyKeys);
        if (input.Kind is not (OpenableKind.Portal or OpenableKind.Container)) throw new ArgumentOutOfRangeException(nameof(input));
        var portal = input.Kind == OpenableKind.Portal;
        if (!portal && input.JunkPile) return OpenAttempt.Opens;
        if ((input.Flags & BustedBit) != 0) return OpenAttempt.Opens;
        if (portal && input.Walker == OpenAttemptWalker.NotCritter) return OpenAttempt.NotCritter;
        if (input.ScriptRefused) return OpenAttempt.ScriptRefused;
        if ((input.Flags & JammedBit) != 0) return OpenAttempt.Jammed;
        if ((input.Flags & MagicallyHeldBit) != 0) return OpenAttempt.MagicallyHeld;
        if (portal && (input.Flags & AlwaysLockedBit) == 0 && (input.Walker == OpenAttemptWalker.FreeNpc || input.Leaving))
            return OpenAttempt.Opens;
        if (!IsLocked(input.Flags, input.Hour)) return OpenAttempt.Opens;
        return input.PartyKeys.Any(key => KeyFits(input.LockKey, key)) ? OpenAttempt.Opens : OpenAttempt.Locked;
    }
}

/// <summary>The ART facts of a portal that its frames depend on: door or window, raw rotation 0..7 and frame count.</summary>
public readonly record struct PortalArt(bool Door, int Rotation, int FrameCount);

/// <summary>One open or close step of a portal: the new frame, and whether it sets or clears the open object flags.</summary>
public readonly record struct PortalStep(int Frame, PortalFlagChange Flags);

public enum PortalFlagChange { None, Set, Cleared }

/// <summary>
/// ARC-WORLD-050: a portal's open and close steps (ledger LK10). Open means frame ≠ 0. A door of even rotation 0 or 6 (odd
/// rotations drop by one) uses frames 4..6, of 2 or 4 frames 1..3; a window frames 0 and 1. Leaving or entering frame 0 sets
/// or clears the open flags: <see cref="DoorOpenFlags"/> for a door, <see cref="WindowOpenFlags"/> for a window.
/// </summary>
public static class PortalFrameRules
{
    /// <summary>Object flags no block, shoot through and see through (0x400, 0x20, 0x10).</summary>
    public const int DoorOpenFlags = 0x430;
    /// <summary>Object flags no block and shoot through (0x400, 0x20).</summary>
    public const int WindowOpenFlags = 0x420;

    public static bool IsOpen(int frame) => frame != 0;

    /// <summary>The next open step, or null when the portal cannot open further (or at all: one frame).</summary>
    [OriginalFunction(0x4F0950, OriginalFunctionScope.Partial)]
    public static PortalStep? OpenStep(PortalArt art, int frame)
    {
        if (art.FrameCount == 1) return null;
        if (!art.Door) return frame == 1 ? null : new(1, PortalFlagChange.Set);
        var (first, last) = Range(art.Rotation);
        if (frame >= first && frame <= last) return frame == last ? null : new(frame + 1, PortalFlagChange.None);
        return new(first, PortalFlagChange.Set);
    }

    /// <summary>The next close step, or null when the portal is closed (or has one frame).</summary>
    [OriginalFunction(0x4F0A90, OriginalFunctionScope.Partial)]
    public static PortalStep? CloseStep(PortalArt art, int frame)
    {
        if (art.FrameCount == 1 || frame == 0) return null;
        if (!art.Door) return new(0, PortalFlagChange.Cleared);
        var (first, last) = Range(art.Rotation);
        if (frame >= first && frame <= last) return frame == first ? new(0, PortalFlagChange.Cleared) : new(frame - 1, PortalFlagChange.None);
        return new(last, PortalFlagChange.None);
    }

    /// <summary>The frame a toggle leaves (ledger LK10: open or close until the step refuses).</summary>
    [OriginalFunction(0x4F08F0, OriginalFunctionScope.Partial)]
    public static int Toggled(PortalArt art, int frame)
    {
        var opening = !IsOpen(frame);
        for (var steps = 0; steps < 16; steps++)
        {
            if ((opening ? OpenStep(art, frame) : CloseStep(art, frame)) is not { } step) return frame;
            frame = step.Frame;
        }
        throw new InvalidOperationException("A portal toggle did not settle.");
    }

    /// <summary>The frame of a closed portal: 0 (every close sequence ends there).</summary>
    public static int Closed => 0;

    private static (int First, int Last) Range(int rotation)
    {
        if (rotation is < 0 or > 7) throw new ArgumentOutOfRangeException(nameof(rotation));
        var even = rotation & ~1;
        return even is 0 or 6 ? (4, 6) : (1, 3);
    }
}
