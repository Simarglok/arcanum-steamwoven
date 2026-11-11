namespace Arcanum.Simulation.Clock;

/// <summary>
/// Caller-serialized clock authority. Serialize all calls including Dispose; no concurrency guarantee.
/// Durations are already-admitted game milliseconds, never host elapsed or Movement time.
/// </summary>
public sealed class GameClockOwner : IDisposable
{
    /// <summary>The source a clock profile binds to (<see cref="ProfileBinding.SourceRevision"/>): the SHA-256 of the retail
    /// 1.0.7.4 <c>Arcanum.exe</c> whose clock functions were checked (verification ledger, second reading; ARC-DEV-009).</summary>
    public const string RetailExecutableSha256 = "9bd80511f8663a41a25a20a701959e394e0065e4ffc68a8dc69fe19c23678d52";

    private ClockSnapshot state;
    private bool disposed;

    private GameClockOwner(ClockSnapshot initial) { state = initial; }

    [OriginalFunction(0x45AD20, OriginalFunctionScope.Partial)]
    public static ClockBootstrapResult Bootstrap(ProfileBinding profile)
    {
        var failure = ValidateProfile(profile);
        if (failure != ClockFailure.None) return RejectBootstrap(failure);
        return FromDescriptor(new(profile, checked((long)profile.StartHour * 3_600_000), default, 1, 0));
    }

    internal static ClockBootstrapResult FromDescriptor(ClockDescriptor descriptor)
    {
        var failure = ValidateProfile(descriptor.ProfileBinding);
        if (failure != ClockFailure.None) return RejectBootstrap(failure);
        if (descriptor.GameTimestampMs < 0 || descriptor.Generation != 1 || descriptor.Revision < 0 ||
            !IsValidPause(descriptor.PauseDisposition)) return RejectBootstrap(ClockFailure.InvalidState);
        failure = ClockCalendar.TryDerive(descriptor.GameTimestampMs, descriptor.ProfileBinding.StartYear, out var calendar);
        if (failure != ClockFailure.None) return RejectBootstrap(failure);
        var initial = Snapshot(new ClockOwnerIdentity(), descriptor.ProfileBinding, descriptor.GameTimestampMs,
            descriptor.PauseDisposition, descriptor.Revision, calendar);
        var owner = new GameClockOwner(initial);
        return new(ClockBootstrapDisposition.Ready, ClockFailure.None, owner, initial);
    }

    public ClockSnapshot QueryClockSnapshot()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        return state;
    }

    [OriginalFunction(0x45AA10, OriginalFunctionScope.Partial)]
    public ClockMutationResult AdvanceGameTime(ClockToken token, long durationGameMs, ClockAdvanceReason reason)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (durationGameMs < 0) return Reject(ClockFailure.InvalidDuration);
        if (reason is not (ClockAdvanceReason.OrdinaryAdvance or ClockAdvanceReason.ExplicitWait)) return Reject(ClockFailure.InvalidReason);
        if (token != state.Token) return Reject(ClockFailure.StaleToken);
        if (durationGameMs == 0 || (state.PauseDisposition.Paused && reason == ClockAdvanceReason.OrdinaryAdvance)) return NoChange();
        long timestamp;
        try { timestamp = checked(state.GameTimestampMs + durationGameMs); }
        catch (OverflowException) { return Reject(ClockFailure.TimeOverflow); }
        var failure = ClockCalendar.TryDerive(timestamp, state.ProfileBinding.StartYear, out var calendar);
        if (failure != ClockFailure.None) return Reject(failure);
        if (state.Revision == long.MaxValue) return Reject(ClockFailure.RevisionExhausted);
        var next = Snapshot(state.OwnerIdentity, state.ProfileBinding, timestamp, state.PauseDisposition,
            checked(state.Revision + 1), calendar);
        state = next;
        return new(ClockMutationDisposition.Changed, ClockFailure.None, next);
    }

    [OriginalFunction(0x45B340, OriginalFunctionScope.Partial)]
    [OriginalFunction(0x45B320, OriginalFunctionScope.Partial)]
    public ClockMutationResult SetPaused(ClockToken token, PauseDisposition disposition)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!IsValidPause(disposition)) return Reject(ClockFailure.InvalidReason);
        if (token != state.Token) return Reject(ClockFailure.StaleToken);
        if (disposition == state.PauseDisposition) return NoChange();
        if (state.Revision == long.MaxValue) return Reject(ClockFailure.RevisionExhausted);
        var next = state with { PauseDisposition = disposition, Revision = checked(state.Revision + 1) };
        state = next;
        return new(ClockMutationDisposition.Changed, ClockFailure.None, next);
    }

    public void Dispose() { disposed = true; }

    private ClockMutationResult Reject(ClockFailure failure) => new(ClockMutationDisposition.Rejected, failure, state);
    private ClockMutationResult NoChange() => new(ClockMutationDisposition.NoChange, ClockFailure.None, state);
    private static ClockBootstrapResult RejectBootstrap(ClockFailure failure) => new(ClockBootstrapDisposition.Rejected, failure, null, null);

    private static ClockSnapshot Snapshot(ClockOwnerIdentity identity, ProfileBinding profile, long timestamp,
        PauseDisposition pause, long revision, ClockCalendarFields calendar) =>
        new(profile.ClockSchemaVersion, identity, profile, timestamp, pause, 1, revision, calendar.DayIndex,
            calendar.MillisecondOfDay, calendar.Hour, calendar.Minute, calendar.Second, calendar.DayOfMonth,
            calendar.Month, calendar.Year, calendar.IsDay);

    private static bool IsValidPause(PauseDisposition pause) => pause.Paused
        ? pause.Reason is PauseReason.User or PauseReason.Menu or PauseReason.TurnBased
        : pause.Reason == PauseReason.None;

    private static ClockFailure ValidateProfile(ProfileBinding profile)
    {
        if (profile.ClockSchemaVersion != 1) return ClockFailure.InvalidSchema;
        if (!string.Equals(profile.PolicyId, "steamwoven-normalized-clock-v1", StringComparison.Ordinal) ||
            !string.Equals(profile.SourceRevision, RetailExecutableSha256, StringComparison.Ordinal) ||
            !IsFingerprint(profile.SourceDataSha256) || !IsFingerprint(profile.StartupResourceSha256) ||
            profile.StartYear < 0 || profile.StartHour is < 0 or > 23) return ClockFailure.InvalidProfile;
        return ClockFailure.None;
    }

    private static bool IsFingerprint(string? value)
    {
        if (value is not { Length: 64 }) return false;
        foreach (var c in value)
            if (c is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')) return false;
        return true;
    }
}
