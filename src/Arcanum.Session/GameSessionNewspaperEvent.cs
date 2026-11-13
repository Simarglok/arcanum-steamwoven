using Arcanum.Simulation;
using Arcanum.Data.Written;
using Arcanum.Simulation.Events;

namespace Arcanum.Session;

/// <summary>
/// ARC-UI-1739: the pass world of the Newspapers event (kind 27, ledger NQ4). The host answers the newspaper texts the module loaded; the session pops the queue and draws the default issue.
/// A world without it (or without texts) arms no chain, and a chain already armed pops with a default issue of 5000 (no numbered default texts to draw among).
/// </summary>
public interface ISessionNewspaperWorld : ISessionEventWorld
{
    /// <summary>The written texts of the module (the newspaper file's headlines); null: none loaded, so no event is armed.</summary>
    WrittenTexts? NewspaperTexts { get; }
}

public sealed partial class GameSessionOwner
{
    /// <summary>The pending Newspapers events' deadlines (game ms), in queue order: one in a running chain.</summary>
    public IReadOnlyList<long> QueryNewspaperDeadlines() { ThrowIfDisposed(); return _events.QueryPending(SessionEventKinds.Newspaper).Select(e => e.DeadlineMs).ToArray(); }

    /// <summary>
    /// ARC-UI-1739 (0x4BF270 as the new game's start adds it, ledger NQ4): arms the Newspapers chain when the host has the newspaper texts and none is pending, to the next midnight (a day less the time of day on, as the new game's
    /// start does with the clock's start time). The original adds the first event when a new game starts; here it is the first event pass of a host that has the texts (a build's old save has none pending). One revision when it armed.
    /// </summary>
    private void ArmNewspaper()
    {
        if (PassWorldAs<ISessionNewspaperWorld>() is not { NewspaperTexts: not null } || _events.Any(SessionEventKinds.Newspaper)) return;
        var clock = _clock.QueryClockSnapshot();
        if (!ScheduleNewspaper(clock.GameTimestampMs, SessionEventKinds.NewspaperDayMs - clock.MillisecondOfDay) || _revision == long.MaxValue) return;
        Revise();
    }

    // The next event after delayMs; beyond the event queue's bound the chain ends (project bound).
    private bool ScheduleNewspaper(long now, long delayMs)
    {
        if (_events.Count >= _events.Limits.MaxPending || _events.NextSequence == long.MaxValue || now > long.MaxValue - delayMs) return false;
        return _events.ScheduleAfter(SessionEventKinds.Newspaper, GameEventPayload.Of([], now), delayMs, EventClocks(now)).Disposition != GameEventScheduleDisposition.Rejected;
    }

    /// <summary>
    /// ARC-UI-1739 (the handler 0x4BF270, the pop 0x4BF2C0, the default issue 0x4BF330; ledger NQ4): the daily event came due. The pop removes the head (the network flag that can skip it is not applicable), a default issue takes the head
    /// when none is left behind it (5000 + 10 × a number drawn from 0 to <see cref="WrittenTexts.DefaultIssueChoices"/>; equal bounds draw nothing), and the event sets itself up again a day on. A session whose generator is exhausted keeps the queue as it is.
    /// The re-armed event and the new queue are one revision.
    /// </summary>
    [OriginalFunction(0x4BF270, OriginalFunctionScope.Full)]
    [OriginalFunction(0x4BF2C0, OriginalFunctionScope.Full)]
    [OriginalFunction(0x4BF330, OriginalFunctionScope.Full)]
    internal GameEventHandlerOutcome FireNewspaper(GameEventFiring firing)
    {
        ScheduleNewspaper(firing.NowMs, SessionEventKinds.NewspaperDayMs);
        var popped = _newspaper.Popped();
        if (popped.NeedsDefault)
        {
            var choices = PassWorldAs<ISessionNewspaperWorld>()?.NewspaperTexts?.DefaultIssueChoices(_rules.Written) ?? 0;
            // 0x4CEF00 of 0 and the count: equal bounds return without a draw (the session's generator does the same).
            if (_random.TryNext(0, choices, out var draw)) popped = popped.WithDefault((draw + 500) * 10);
            else popped = _newspaper;
        }
        _newspaper = popped;
        if (_revision < long.MaxValue) Revise();
        return GameEventHandlerOutcome.Completed;
    }
}
