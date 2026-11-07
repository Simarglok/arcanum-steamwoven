using Arcanum.Scripting.Scr;
using Arcanum.Session;

namespace Arcanum.TerrainViewer.Core;

/// <summary>
/// ARC-SCRIPT-019: the story effects of a viewer script run applied to the session: the raised story-state number (SS1), then the
/// quest, area, rumor and reputation requests in action order (SS2–SS5), each for a PC only (the original's functions do nothing
/// for any other object; the viewer's one PC is the local player), then (ARC-DEV-205) the town maps to reveal whole in action order (UF1:
/// game-wide, no object; a number outside the session's range is skipped). The quest's giver reaction changes of the original (a reaction
/// raised to 41 at state 2, 10 more at state 4) are not applied. A request the session refuses (a number outside its ranges, where
/// the original stores it unchecked) stops the later ones; the earlier stay, as the original applies as it goes.
/// </summary>
public static class AreaScriptStory
{
    /// <param name="award">Takes the experience a completed quest gave (the host passes its follower step and level-up messages on,
    /// as for a dialog's quest command).</param>
    public static bool Apply(GameSessionOwner session, ScrEffects effects, Func<ScrObjectRef, bool> isPc, Action<SessionExperienceAward> award)
    {
        ArgumentNullException.ThrowIfNull(session); ArgumentNullException.ThrowIfNull(effects); ArgumentNullException.ThrowIfNull(isPc);
        ArgumentNullException.ThrowIfNull(award);
        if (effects.StoryState is { } story && session.RaiseStoryState(session.Version, story).Disposition == GameSessionDisposition.Rejected) return false;
        foreach (var request in effects.States)
        {
            if (!isPc(request.Object)) continue;
            SessionExperienceAward? gained = null;
            GameSessionMutationResult? result = request.Kind switch
            {
                // A state outside 0..6 is refused by the session (the original stores it unchecked), and stops the later steps.
                ScrStateKind.QuestState => session.SetQuestState(session.Version, request.Number, (SessionQuestState)request.Value, out gained),
                ScrStateKind.KnownArea => session.DiscoverArea(session.Version, request.Number),
                ScrStateKind.Rumor => session.LearnRumor(session.Version, request.Number),
                ScrStateKind.AddReputation => session.AddReputation(session.Version, request.Number),
                ScrStateKind.RemoveReputation => session.RemoveReputation(session.Version, request.Number),
                _ => null,
            };
            if (gained is not null) award(gained);
            if (result is not { Disposition: not GameSessionDisposition.Rejected }) return false;
        }
        // UF1, UF2: the original does nothing for a number it holds no town map for, so a number the session cannot keep is skipped, not refused.
        foreach (var townMap in effects.TownMaps)
            if (SessionStoryLimits.IsTownMap(townMap) && session.RevealTownMap(session.Version, townMap).Disposition == GameSessionDisposition.Rejected) return false;
        return true;
    }
}
