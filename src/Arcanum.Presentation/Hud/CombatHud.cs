namespace Arcanum.Presentation.Hud;

/// <summary>What one lamp of the action-point panel shows (ledger CH2).</summary>
public enum ActionPointLamp
{
    Off,
    /// <summary>The points left after the previewed action.</summary>
    Left,
    /// <summary>The previewed action's cost, as far as the points cover it.</summary>
    Cost,
    /// <summary>The part of the cost the points do not cover.</summary>
    Shortfall,
}

/// <summary>
/// ARC-UI-1114 (slice 2 of docs/research/combat-hud.md; checked in the 1.0.7.4 executable, ledger CH2–CH6): the combat interface's
/// action-point lamps. Twenty lamps in four groups of five, lit from the left in three shares: the points left after the previewed
/// action, then its cost, then the part of the cost the points do not cover. More than 20 of any show only what fits. On another
/// participant's subturn the panel shows three running lights instead. Engine-free; the host draws.
/// </summary>
public static class ActionPointRules
{
    /// <summary>The lamps of the panel (CH2).</summary>
    public const int LampCount = 20;
    /// <summary>The lamps of one group (CH2).</summary>
    public const int GroupSize = 5;
    /// <summary>The distance between two lamps in the original's 800 × 600 screen (CH2).</summary>
    public const int LampPitch = 13;
    /// <summary>The running lights of another participant's subturn and the lamps between two of them (CH5).</summary>
    public const int CueLights = 3, CueSpacing = 7;
    /// <summary>A running light steps one lamp every this many milliseconds, so a round of the 20 lamps takes 2 s (CH5).</summary>
    public const int CueStepMs = 100;

    /// <summary>How many lamps each share lights, left to right in this order (<see cref="Left"/>, <see cref="Cost"/>, <see cref="Shortfall"/>).</summary>
    public readonly record struct Shares(int Left, int Cost, int Shortfall)
    {
        public int Lit => Left + Cost + Shortfall;
    }

    /// <summary>
    /// The shares for <paramref name="points"/> action points and the pending cost <paramref name="pending"/> (the last action-point
    /// check's, CH4). With the points covering the cost the points left come first (up to 20) and the cost fills what remains of the
    /// 20; with the cost beyond the points the cost's lamps (as many as there are points) come first and the shortfall fills what
    /// remains. No points light nothing. A negative pending cost counts as none.
    /// </summary>
    [OriginalFunction(0x56F430, OriginalFunctionScope.Partial)]
    public static Shares LampShares(int points, int pending)
    {
        if (points <= 0) return default;
        pending = Math.Max(0, pending);
        if (points >= pending)
        {
            var left = (int)Math.Min((long)points - pending, LampCount);
            return new(left, Math.Min(pending, LampCount - left), 0);
        }
        var cost = Math.Min(points, LampCount);
        return new(0, cost, (int)Math.Min((long)pending - points, LampCount - cost));
    }

    /// <summary>The 20 lamps left to right for <see cref="LampShares"/>.</summary>
    public static ActionPointLamp[] Lamps(int points, int pending)
    {
        var shares = LampShares(points, pending);
        var lamps = new ActionPointLamp[LampCount];
        for (var i = 0; i < lamps.Length; i++)
            lamps[i] = i < shares.Left ? ActionPointLamp.Left : i < shares.Left + shares.Cost ? ActionPointLamp.Cost
                : i < shares.Lit ? ActionPointLamp.Shortfall : ActionPointLamp.Off;
        return lamps;
    }

    /// <summary>The step of the running lights <paramref name="elapsedMs"/> after the panel was built (it starts at 0, CH5).</summary>
    public static int CueIndex(long elapsedMs) => (int)(Math.Max(0, elapsedMs) / CueStepMs % LampCount);

    /// <summary>The three lamps the running lights light at step <paramref name="index"/>: i, i − 7 and i + 7 round the 20 (CH5).</summary>
    public static int[] CueLamps(int index)
    {
        var i = ((index % LampCount) + LampCount) % LampCount;
        return [i, (i - CueSpacing + LampCount) % LampCount, (i + CueSpacing) % LampCount];
    }

    /// <summary>The 20 lamps of the running lights at step <paramref name="index"/>: the three lit are <see cref="ActionPointLamp.Left"/>'s art (CH5).</summary>
    public static ActionPointLamp[] CueLampStates(int index)
    {
        var lamps = new ActionPointLamp[LampCount];
        foreach (var lamp in CueLamps(index)) lamps[lamp] = ActionPointLamp.Left;
        return lamps;
    }

    /// <summary>The lamp's x offset from the panel's first lamp in the original's screen: 13 pixels a lamp, a group of five apart by <paramref name="groupGap"/> more.</summary>
    public static int LampX(int lamp, int groupGap)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(lamp);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(lamp, LampCount);
        return lamp * LampPitch + lamp / GroupSize * groupGap;
    }
}

/// <summary>
/// ARC-UI-1114: the list of a round's participants with the current one marked, a Steamwoven addition approved by the owner on
/// 2026-10-28 (the original's combat interface shows no turn order, ledger CH6): shown in the Steamwoven rule mode and hidden in
/// Original. A round may have up to a thousand participants, so a long list shows a window of the order and counts the rest.
/// </summary>
public static class TurnOrderRules
{
    /// <summary>The most rows the list shows.</summary>
    public const int MaxRows = 8;

    /// <summary>A row: the participant's index in the round order and whether it is the one acting.</summary>
    public readonly record struct Row(int Index, bool Current);

    /// <summary>
    /// The rows for a round of <paramref name="count"/> participants whose <paramref name="current"/> one acts: all of them in round
    /// order when they fit, else <paramref name="maxRows"/> of them from the current one on, wrapping round the order, and the count
    /// of those left out.
    /// </summary>
    public static (IReadOnlyList<Row> Rows, int Hidden) Window(int count, int current, int maxRows = MaxRows)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxRows, 1);
        if (count == 0) return ([], 0);
        ArgumentOutOfRangeException.ThrowIfNegative(current);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(current, count);
        if (count <= maxRows) return (Enumerable.Range(0, count).Select(i => new Row(i, i == current)).ToArray(), 0);
        return (Enumerable.Range(0, maxRows).Select(k => new Row((current + k) % count, k == 0)).ToArray(), count - maxRows);
    }
}
