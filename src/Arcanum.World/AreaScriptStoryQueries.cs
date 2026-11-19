using Arcanum.Scripting.Scr;
using Arcanum.Session;

namespace Arcanum.World;

/// <summary>
/// ARC-SCRIPT-1165 (E5-c, ledger SQ1–SQ12): the answers of <see cref="IScrStoryQueries"/> that the viewer's script hosts share, read from the session: the game clock, the quest's game-wide state, the PC's
/// rumors and the game-wide quelled set, and the PC's blessings and curses. Only the PC has rumors, blessings and curses (the original tests the object's type, BC3 and 0x4C58D0); any other object holds none.
/// </summary>
public static class AreaScriptStoryQueries
{
    /// <summary>The session's game clock as the story commands read it; null for a snapshot the session cannot give (none in practice).</summary>
    public static ScrGameTime? Time(GameSessionOwner session)
    {
        ArgumentNullException.ThrowIfNull(session);
        var clock = session.QueryClockSnapshot();
        return new ScrGameTime(clock.DayIndex, clock.Hour, clock.Minute, clock.MillisecondOfDay, clock.ProfileBinding.StartHour);
    }

    /// <summary>A quest's game-wide state (2 until a PC completes or botches it); 0 for a number outside the quest range, which the session holds no state for.</summary>
    public static int GlobalQuestState(GameSessionOwner session, int quest)
    {
        ArgumentNullException.ThrowIfNull(session);
        return SessionStoryLimits.IsQuest(quest) ? (int)session.QueryGlobalQuest(quest) : 0;
    }

    /// <summary>Whether the object is the PC and has learned the rumor (a learned time of 0 reads as unknown in the original, 0x4C58D0; no game starts at time 0); false for a rumor number outside the rumor range.</summary>
    public static bool RumorKnown(GameSessionOwner session, bool isPc, int rumor)
    {
        ArgumentNullException.ThrowIfNull(session);
        return isPc && SessionStoryLimits.IsRumor(rumor) && session.QueryRumor(rumor) is { } learned && learned != 0;
    }

    /// <summary>Whether the rumor is quelled (game-wide); false for a rumor number outside the rumor range.</summary>
    public static bool RumorQuelled(GameSessionOwner session, int rumor)
    {
        ArgumentNullException.ThrowIfNull(session);
        return SessionStoryLimits.IsRumor(rumor) && session.QueryRumorQuelled(rumor);
    }

    /// <summary>Whether the object is the PC and holds the blessing or curse (a session without a hero holds none).</summary>
    public static bool HasBlessing(GameSessionOwner session, bool isPc, int number, bool curse)
    {
        ArgumentNullException.ThrowIfNull(session);
        return isPc && session.QueryHeroBlessings(curse ? SessionBlessingKind.Curse : SessionBlessingKind.Blessing)?.Any(held => held.Number == number) == true;
    }
}
