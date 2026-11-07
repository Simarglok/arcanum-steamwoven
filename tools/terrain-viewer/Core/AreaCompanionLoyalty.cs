using Arcanum.Session;
using Arcanum.Simulation.Party;
using Arcanum.World;

namespace Arcanum.TerrainViewer.Core;

/// <summary>
/// ARC-WORLD-045: the AREA viewer's side of the follower loyalty step and the wait timer. The step runs on a companion's AI
/// tick (<see cref="AreaCompanionGroup.Loyalty"/>) with the facts of <see cref="Facts"/>; its lines are named by their message
/// range only (no game text is loaded or kept).
/// </summary>
public static class AreaCompanionLoyalty
{
    public const string LogPrefix = "ARC-WORLD-045";

    /// <summary>The original's AI tick delay (ledger CL8): <c>250 + 4750 × d / 30</c> ms with integer division, the distance
    /// to the PC counted as 30 when negative or above 30.</summary>
    public static long AiTickDelayMs(long distance) => 250 + 4750 * (distance is < 0 or > 30 ? 30 : distance) / 30;

    /// <summary>The longest AI tick delay (a distance of 30 or more).</summary>
    public const long MaxAiTickDelayMs = 5000;

    /// <summary>
    /// The loyalty step's facts for <paramref name="companion"/>: the PC side (the session hero, else the diagnostic PC) against its follow facts, the PC's
    /// alignment of the conversation state, its effective reaction under that state's facts and its level now
    /// (<see cref="AreaAdvancement.Level"/>). Null when the session cannot answer its reaction.
    /// </summary>
    public static PartyConsentFacts? Facts(GameSessionOwner session, AreaCompanion companion, AreaConversationState state)
    {
        ArgumentNullException.ThrowIfNull(session); ArgumentNullException.ThrowIfNull(companion); ArgumentNullException.ThrowIfNull(state);
        if (session.QueryReaction(companion.Entity, state.NpcFacts(), state.PcFacts).View is not { } reaction) return null;
        return AreaCompanionRoster.Facts(companion.Facts, state.Alignment, reaction.Effective,
            AreaAdvancement.Level(session, companion.Entity, companion.Facts.Level), session.QueryHeroPartySide());
    }

    /// <summary>What a step showed, for the HUD and the log: the refusal line range of a follower that left or the warning line
    /// range; null when it showed nothing.</summary>
    public static string? Shown(LoyaltyStepResult step) =>
        step.RefusalLines is { } refusal ? $"left, refusal lines {refusal.First}-{refusal.Last} ({step.Consent})"
        : step.WarningLines is { } warning ? $"warning lines {warning.First}-{warning.Last} ({step.Warning})"
        : null;

    /// <summary>The log line of a step that changed the session or was refused.</summary>
    public static string Log(AreaCompanion companion, SessionLoyaltyResult result) =>
        $"{LogPrefix} loyalty script={companion.Speaker.ScriptId} disposition={result.Disposition} failure={result.Failure}"
        + (result.Step is { } step ? $" step={step.Kind} consent={step.Consent?.ToString() ?? "none"} leaveRefused={step.LeaveRefused}"
            + $" warning={step.Warning} marks={step.Loyalty}" + (Shown(step) is { } shown ? $" shown=\"{shown}\"" : "") : "");

    /// <summary>The log line of a wait timer that came due.</summary>
    public static string Log(SessionWaitFiring firing) =>
        $"{LogPrefix} wait expired entity={firing.Entity} map={firing.Event.ScopeMap} charisma={firing.LeaderCharisma}"
        + $" reaction={(firing.Reaction is { } r ? $"{r.Skip}:{r.Delta}" : "none")} failure={firing.ReactionFailure}";
}
