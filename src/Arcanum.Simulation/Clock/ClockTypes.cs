namespace Arcanum.Simulation.Clock;

/// <summary>Explicit startup declarations. Bootstrap validates support, not provenance authenticity.</summary>
public readonly record struct ProfileBinding(int ClockSchemaVersion, string PolicyId, string SourceRevision,
    string SourceDataSha256, string StartupResourceSha256, int StartYear, int StartHour);

/// <summary>Immutable process-local reference identity, distinct for each successfully constructed owner.</summary>
public sealed class ClockOwnerIdentity
{
    internal ClockOwnerIdentity() { }
}

public readonly record struct ClockToken(ClockOwnerIdentity? OwnerIdentity, ProfileBinding ProfileBinding,
    long Generation, long Revision);
public enum PauseReason { None, User, Menu, TurnBased }
public readonly record struct PauseDisposition(bool Paused, PauseReason Reason);
public enum ClockAdvanceReason { OrdinaryAdvance }
public enum ClockBootstrapDisposition { Ready, Rejected }
public enum ClockMutationDisposition { Changed, NoChange, Rejected }
public enum ClockFailure
{
    None, InvalidSchema, InvalidProfile, InvalidState, InvalidDuration, InvalidReason,
    StaleToken, TimeOverflow, CalendarOverflow, RevisionExhausted,
}

/// <summary>Detached value. Copy edits cannot alter the owner; historical values survive disposal.</summary>
public readonly record struct ClockSnapshot(int ClockSchemaVersion, ClockOwnerIdentity OwnerIdentity,
    ProfileBinding ProfileBinding, long GameTimestampMs, PauseDisposition PauseDisposition,
    long Generation, long Revision, long DayIndex, long MillisecondOfDay, int Hour, int Minute,
    int Second, int DayOfMonth, int Month, long Year, bool IsDay)
{
    public ClockToken Token => new(OwnerIdentity, ProfileBinding, Generation, Revision);
}

public readonly record struct ClockBootstrapResult(ClockBootstrapDisposition Disposition, ClockFailure Failure,
    GameClockOwner? Owner, ClockSnapshot? Snapshot);
public readonly record struct ClockMutationResult(ClockMutationDisposition Disposition, ClockFailure Failure,
    ClockSnapshot Snapshot);

// Friend construction is bounded and validated through the same production construction path.
internal readonly record struct ClockDescriptor(ProfileBinding ProfileBinding, long GameTimestampMs,
    PauseDisposition PauseDisposition, long Generation, long Revision);
internal readonly record struct ClockCalendarFields(long DayIndex, long MillisecondOfDay, int Hour,
    int Minute, int Second, int DayOfMonth, int Month, long Year, bool IsDay);
