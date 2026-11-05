using System.Runtime.CompilerServices;
using Arcanum.Session;
using Arcanum.World;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>MIG12 S4 (ARC-DEV-123): a synthetic host clock for the tests of the frame step. The host stamp of each session is kept here
/// (the first call primes it, as the first frame of a host admits nothing), and a span of host milliseconds is spent as frames of at
/// most 250 ms, so the 250 ms cut never loses any of it. Game time is 8 times the host time (TL14), so <see cref="AdvanceGame"/> takes
/// a multiple of 8.</summary>
internal static class FrameStepTestClock
{
    private sealed class Stamp { public long HostMs; }

    private static readonly ConditionalWeakTable<GameSessionOwner, Stamp> Stamps = new();

    /// <summary>Runs frames until <paramref name="hostMs"/> host milliseconds have passed (the first call of a session primes its stamp
    /// with a frame that admits nothing); the report is the sum of the frames', its clocks and queue those of the last.</summary>
    public static FrameStepReport AdvanceHost(this FrameStep step, long hostMs)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(hostMs);
        var stamp = Stamps.TryGetValue(step.Session, out var known) ? known : Stamps.GetValue(step.Session, _ => new Stamp());
        if (!Primed.TryGetValue(step.Session, out _)) { Primed.Add(step.Session, new object()); step.Run(stamp.HostMs); }
        FrameStepReport? sum = null;
        do
        {
            var chunk = Math.Min(hostMs, 250);
            stamp.HostMs += chunk;
            hostMs -= chunk;
            sum = Merge(sum, step.Run(stamp.HostMs));
        }
        while (hostMs > 0);
        return sum;
    }

    private static readonly ConditionalWeakTable<GameSessionOwner, object> Primed = new();

    /// <summary><paramref name="gameMs"/> game milliseconds as host time (a multiple of 8).</summary>
    public static FrameStepReport AdvanceGame(this FrameStep step, long gameMs)
    {
        if (gameMs % 8 != 0) throw new ArgumentException("Game time is 8 times host time: pass a multiple of 8.", nameof(gameMs));
        return step.AdvanceHost(gameMs / 8);
    }

    private static FrameStepReport Merge(FrameStepReport? sum, FrameStepReport next) => sum is null ? next : next with
    {
        Steps = sum.Steps + next.Steps, Passes = sum.Passes + next.Passes, Fired = sum.Fired + next.Fired, Dropped = sum.Dropped + next.Dropped,
        ScriptCalls = sum.ScriptCalls + next.ScriptCalls, ScriptCallsRun = sum.ScriptCallsRun + next.ScriptCallsRun, Coalesced = sum.Coalesced || next.Coalesced,
        Waits = [.. sum.Waits, .. next.Waits], Decays = [.. sum.Decays, .. next.Decays], Restocks = [.. sum.Restocks, .. next.Restocks],
        HourChanged = sum.HourChanged || next.HourChanged, Participants = next.Participants,
    };
}
