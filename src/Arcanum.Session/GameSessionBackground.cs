namespace Arcanum.Session;

/// <summary>
/// ARC-WORLD-069: the background ids the session's rules read (<c>rules\backgrnd.mes</c> blocks, ledger BG rows). Only a PC has a
/// background (the original's PC field 0x101; its reader 0x4C2690 answers 0 for any other object), so an NPC is never refused.
/// </summary>
public static class SessionBackgrounds
{
    /// <summary>No significant background.</summary>
    public const int None = 0;
    /// <summary>Magick Allergy: cannot wear a magickal item (the wear check 0x464D20, code 18, <c>mes\item.mes</c> 118).</summary>
    public const int MagickAllergy = 69;
    /// <summary>Technophobia: cannot carry a technological item (the insert check 0x466510, code 15, <c>mes\item.mes</c> 115).</summary>
    public const int Technophobia = 72;

    /// <summary>The insert check's background part (0x466510, after the weight and before the room): a technophobe refuses an item of
    /// negative magick/tech complexity.</summary>
    public static bool RefusesCarry(int background, int complexity) => background == Technophobia && complexity < 0;
}

public sealed partial class GameSessionOwner
{
    /// <summary>The hero's background id (0 for none), or null without a hero (the background reader 0x4C2690 for the PC).</summary>
    [Arcanum.Simulation.OriginalFunction(0x4C2690, Arcanum.Simulation.OriginalFunctionScope.Partial)]
    public int? QueryHeroBackground()
    {
        ThrowIfDisposed();
        return _hero?.State.Background;
    }

    /// <summary>
    /// The insert check's answer for the hero (0x466510): the weight first, then the background, then the room. ARC-WORLD-085: for an
    /// item entering the hero's list from outside it (<paramref name="incoming"/>), the session answers the weight itself
    /// (<see cref="SessionInsertVerdict.TooHeavy"/> when the hero's total plus the item would pass its carry weight, ledger WE4); a host's
    /// weight refusal counts as well. Between weight and room the session refuses a technological item for a Technophobia hero
    /// (<see cref="SessionInsertVerdict.TechnologyRefused"/>); any other host answer (the room) yields to both refusals. A wear move
    /// passes no incoming item: the item is in the hero's list already, and the original's weight test on an unwield is not read yet
    /// (not ready). ARC-UI-019: <paramref name="room"/> false (the hero's grid has no free slot for the item, ledger IW11) refuses an
    /// accepted item as <see cref="SessionInsertVerdict.NoRoom"/>; the wear and grid moves answer it, items entering from outside leave the
    /// room to the host (not ready).
    /// </summary>
    [Arcanum.Simulation.OriginalFunction(0x466510, Arcanum.Simulation.OriginalFunctionScope.Partial)]
    private SessionInsertVerdict HeroInsertVerdict(SessionInsertVerdict host, int complexity, Arcanum.Simulation.Items.CarriedItem? incoming = null, bool room = true)
    {
        if (host == SessionInsertVerdict.TooHeavy || incoming is { } item && !HeroFits(item)) return SessionInsertVerdict.TooHeavy;
        if (_hero is not null && SessionBackgrounds.RefusesCarry(_hero.State.Background, complexity)) return SessionInsertVerdict.TechnologyRefused;
        return host == SessionInsertVerdict.Accepted && !room ? SessionInsertVerdict.NoRoom : host;
    }
}
