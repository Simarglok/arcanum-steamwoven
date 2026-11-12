using Arcanum.Scripting.Scr;
using Arcanum.Session;
using Arcanum.Simulation.Clock;

namespace Arcanum.World;

/// <summary>
/// ARC-SCRIPT-1241 (<c>docs/research/bink-movies.md</c> MV5): one script fade (action 103) resolved for its host. <see cref="AdvanceMs"/> is the game-clock jump
/// (0 when the seconds are not above 0); <see cref="Movie"/> is the movie number (0: none), <see cref="Track"/> the movie's audio track id (0: the default track, the
/// sound operand only when it is above 0), <see cref="Sound"/> the sound the action plays when the movie is 0 (0: none) and <see cref="Flag"/> the operand that picks how
/// the screen comes back (0: at once, else after that many seconds; the viewer brings it back at once).
/// </summary>
public readonly record struct AreaScriptFade(int AdvanceMs, int Movie, int Track, int Sound, int Flag)
{
    /// <summary>Whether the fade asks the viewer for anything to show or play.</summary>
    public bool HasContent => Movie != 0 || Sound > 0;
}

/// <summary>
/// ARC-SCRIPT-1241 (script action 103, 0x447220 case 0x67; the whole action is skipped in a network host game, which this single-player host never is): the fade effects of a script
/// run applied to the session, in action order. The original fades the screen out, jumps the game clock by the first operand × 1000 ms when that is above 0 (0x45C200, tests nothing,
/// fires nothing at once: the next ping sees it, TL26), plays the movie by number with the sound operand as its audio track (when above 0) or, with no movie, plays the sound, and
/// brings the screen back at once or after the last operand's seconds. The movie and the sound are the viewer's: this applies the clock jump and hands the rest back.
/// </summary>
public static class AreaScriptFades
{
    /// <summary>The fade as the host asks for it (a pure reading of the four operands).</summary>
    public static AreaScriptFade Resolve(ScrFade fade) => new(
        // The original multiplies in 32 bits and jumps only when the seconds are above 0 (a very large count wraps, as there).
        fade.Seconds > 0 ? unchecked(fade.Seconds * 1000) : 0,
        fade.Movie,
        fade.Movie != 0 && fade.Sound > 0 ? fade.Sound : 0,
        fade.Movie == 0 && fade.Sound > 0 ? fade.Sound : 0,
        fade.Flag);

    /// <summary>
    /// Applies each fade's clock jump in action order and adds the fades that ask for a movie or a sound to <paramref name="asked"/>. False when the session's clock refuses a jump
    /// (a stale stamp, an overflowing calendar): the later steps stop, the earlier stay, as for any refused step of a script run. A jump that wrapped below 0 is skipped (the
    /// original's helper would take it as a duration; no installed script has one).
    /// </summary>
    public static bool Apply(GameSessionOwner session, ScrEffects effects, List<AreaScriptFade> asked)
    {
        ArgumentNullException.ThrowIfNull(session); ArgumentNullException.ThrowIfNull(effects); ArgumentNullException.ThrowIfNull(asked);
        foreach (var fade in effects.Fades)
        {
            var resolved = Resolve(fade);
            if (resolved.AdvanceMs > 0 && session.JumpClocks(session.QueryClockSnapshot().Token, resolved.AdvanceMs).Disposition == ClockMutationDisposition.Rejected) return false;
            if (resolved.HasContent) asked.Add(resolved);
        }
        return true;
    }
}
