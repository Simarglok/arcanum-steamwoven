using Arcanum.Scripting.Scr;
using Arcanum.Session;

namespace Arcanum.World;

/// <summary>
/// ARC-SCRIPT-019: the story effects of a viewer script run applied to the session: the raised story-state number (SS1), then the
/// quest, area, rumor and reputation requests in action order (SS2–SS5), each for a PC only (the original's functions do nothing
/// for any other object; the viewer's one PC is the local player), then (ARC-DEV-205) the town maps to reveal whole in action order (UF1:
/// game-wide, no object; a number outside the session's range is skipped). The quest's giver reaction changes of the original (a reaction
/// raised to 41 at state 2, 10 more at state 4) are not applied. A request the session refuses (a number outside its ranges, where
/// the original stores it unchecked) stops the later ones; the earlier stay, as the original applies as it goes.
/// ARC-SCRIPT-1165 (E5-c, SQ7–SQ12): the same list carries set quest global state (game-wide), quell rumor, quest experience, the fate point, set PC quest unbotched and the blessing and curse
/// changes in action order. A session without a hero (or without experience tables) gives nothing for the PC-only ones and the later requests go on; a blessing or curse needs
/// <c>blessingEffect</c>, the host's rule of the number's effect (the module's blessing and curse tables and the effect tables), and without one it is refused (the later requests stop). The "received"
/// message and the logbook mark of the original are the host's.
/// </summary>
public static class AreaScriptStory
{
    /// <summary>Whether every add blessing or add curse request that would change the hero has its effect rule, asked before anything of the run applies, so a missing rule refuses the run whole and not partway (a held number needs no rule, BC4).</summary>
    public static bool BlessingRulesResolve(GameSessionOwner session, ScrEffects effects, Func<ScrObjectRef, bool> isPc, Func<SessionBlessingKind, int, SessionEffectDefinition?>? blessingEffect)
    {
        ArgumentNullException.ThrowIfNull(session); ArgumentNullException.ThrowIfNull(effects); ArgumentNullException.ThrowIfNull(isPc);
        foreach (var request in effects.States)
        {
            if (request.Kind is not (ScrStateKind.AddBlessing or ScrStateKind.AddCurse) || request.Object is not { } who || !isPc(who)) continue;
            var kind = request.Kind is ScrStateKind.AddBlessing ? SessionBlessingKind.Blessing : SessionBlessingKind.Curse;
            if (session.QueryHeroBlessings(kind) is not { } held || held.Any(b => b.Number == request.Number)) continue;
            if (blessingEffect?.Invoke(kind, request.Number) is null) return false;
        }
        return true;
    }

    /// <param name="award">Takes the experience a completed quest gave (the host passes its follower step and level-up messages on,
    /// as for a dialog's quest command).</param>
    /// <param name="blessingEffect">The effect rule of a blessing or curse number (<c>BlessingRules.EffectOf</c> over the tables, then the effect table); null when the host has none.</param>
    public static bool Apply(GameSessionOwner session, ScrEffects effects, Func<ScrObjectRef, bool> isPc, Action<SessionExperienceAward> award,
        Func<SessionBlessingKind, int, SessionEffectDefinition?>? blessingEffect = null)
    {
        ArgumentNullException.ThrowIfNull(session); ArgumentNullException.ThrowIfNull(effects); ArgumentNullException.ThrowIfNull(isPc);
        ArgumentNullException.ThrowIfNull(award);
        if (effects.StoryState is { } story && session.RaiseStoryState(session.Version, story).Disposition == GameSessionDisposition.Rejected) return false;
        foreach (var request in effects.States)
        {
            // Every request is for a PC but the game-wide ones (set quest global state).
            if (request.Kind != ScrStateKind.GlobalQuestState && (request.Object is not { } who || !isPc(who))) continue;
            switch (request.Kind)
            {
                case ScrStateKind.QuestExperience:
                {
                    var result = session.AwardQuestExperience(session.Version, request.Number);
                    if (result.Disposition == GameSessionDisposition.Rejected && result.Failure != GameSessionFailure.ExperienceUnavailable) return false;
                    if (result.Award is { } given) award(given);
                    continue;
                }
                case ScrStateKind.FatePoint:
                {
                    var result = session.GrantHeroFatePoint(session.Version);
                    if (result.Disposition == GameSessionDisposition.Rejected && result.Failure != GameSessionFailure.FateUnavailable) return false;
                    continue;
                }
                case ScrStateKind.AddBlessing or ScrStateKind.AddCurse or ScrStateKind.RemoveBlessing or ScrStateKind.RemoveCurse:
                {
                    var kind = request.Kind is ScrStateKind.AddBlessing or ScrStateKind.RemoveBlessing ? SessionBlessingKind.Blessing : SessionBlessingKind.Curse;
                    var adding = request.Kind is ScrStateKind.AddBlessing or ScrStateKind.AddCurse;
                    SessionEffectResult result;
                    if (adding)
                    {
                        if (session.QueryHeroBlessings(kind) is not { } held) continue;
                        // A held number is NoChange whatever its effect (BC4), so the rule is asked for only when it is needed.
                        if (held.Any(b => b.Number == request.Number)) continue;
                        if (blessingEffect?.Invoke(kind, request.Number) is not { } rule) return false;
                        result = session.AddHeroBlessing(session.Version, kind, request.Number, rule);
                    }
                    else result = session.RemoveHeroBlessing(session.Version, kind, request.Number);
                    if (result.Disposition == GameSessionDisposition.Rejected && result.Failure != GameSessionFailure.EffectsUnavailable) return false;
                    continue;
                }
            }
            SessionExperienceAward? gained = null;
            GameSessionMutationResult? mutation = request.Kind switch
            {
                // A state outside 0..6 is refused by the session (the original stores it unchecked), and stops the later steps.
                ScrStateKind.QuestState => session.SetQuestState(session.Version, request.Number, (SessionQuestState)request.Value, out gained),
                ScrStateKind.KnownArea => session.DiscoverArea(session.Version, request.Number),
                ScrStateKind.Rumor => session.LearnRumor(session.Version, request.Number),
                ScrStateKind.AddReputation => session.AddReputation(session.Version, request.Number),
                ScrStateKind.RemoveReputation => session.RemoveReputation(session.Version, request.Number),
                ScrStateKind.GlobalQuestState => session.SetGlobalQuestState(session.Version, request.Number, (SessionQuestState)request.Value),
                ScrStateKind.QuellRumor => session.QuellRumor(session.Version, request.Number),
                ScrStateKind.QuestUnbotched => session.UnbotchQuest(session.Version, request.Number),
                _ => null,
            };
            if (gained is not null) award(gained);
            if (mutation is not { Disposition: not GameSessionDisposition.Rejected }) return false;
        }
        // UF1, UF2: the original does nothing for a number it holds no town map for, so a number the session cannot keep is skipped, not refused.
        foreach (var townMap in effects.TownMaps)
            if (SessionStoryLimits.IsTownMap(townMap) && session.RevealTownMap(session.Version, townMap).Disposition == GameSessionDisposition.Rejected) return false;
        return true;
    }
}
