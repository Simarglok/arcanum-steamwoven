using Arcanum.Scripting.Scr;
using Arcanum.Session;

namespace Arcanum.TerrainViewer.Core;

/// <summary>
/// ARC-SCRIPT-019: the story effects of a viewer script run applied to the session: the raised story-state number (SS1), then the
/// quest, area, rumor and reputation requests in action order (SS2–SS5), each for a PC only (the original's functions do nothing
/// for any other object; the viewer's one PC is the local player). The quest's giver reaction changes of the original (a reaction
/// raised to 41 at state 2, 10 more at state 4) are not applied. A request the session refuses (a number outside its ranges, where
/// the original stores it unchecked) stops the later ones; the earlier stay, as the original applies as it goes.
/// </summary>
public static class AreaScriptStory
{
    public static bool Apply(GameSessionOwner session, ScrEffects effects, Func<ScrObjectRef, bool> isPc)
    {
        ArgumentNullException.ThrowIfNull(session); ArgumentNullException.ThrowIfNull(effects); ArgumentNullException.ThrowIfNull(isPc);
        if (effects.StoryState is { } story && session.RaiseStoryState(session.Version, story).Disposition == GameSessionDisposition.Rejected) return false;
        foreach (var request in effects.States)
        {
            if (!isPc(request.Object)) continue;
            GameSessionMutationResult? result = request.Kind switch
            {
                ScrStateKind.QuestState => request.Value is >= 0 and <= (int)SessionQuestState.Botched
                    ? session.SetQuestState(session.Version, request.Number, (SessionQuestState)request.Value)
                    : null,
                ScrStateKind.KnownArea => session.DiscoverArea(session.Version, request.Number),
                ScrStateKind.Rumor => session.LearnRumor(session.Version, request.Number),
                ScrStateKind.AddReputation => session.AddReputation(session.Version, request.Number),
                ScrStateKind.RemoveReputation => session.RemoveReputation(session.Version, request.Number),
                _ => null,
            };
            if (result is not { Disposition: not GameSessionDisposition.Rejected }) return false;
        }
        return true;
    }
}
