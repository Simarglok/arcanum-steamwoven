using Arcanum.Simulation.Character;

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

    /// <summary>The description number stored with the hero's background (0 for none), or null without a hero (the description reader
    /// 0x4C26D0, field 0x102; ARC-WORLD-097).</summary>
    [Arcanum.Simulation.OriginalFunction(0x4C26D0, Arcanum.Simulation.OriginalFunctionScope.Partial)]
    public int? QueryHeroBackgroundDescription()
    {
        ThrowIfDisposed();
        return _hero?.State.BackgroundDescription;
    }

    /// <summary>
    /// ARC-WORLD-097: <c>background_set</c> (0x4C25E0, ledger BD5, BD9) for the hero: stores the background and its description number and
    /// adds one entry of the choice's effect with the background cause, in one revision. It neither clears an earlier background nor
    /// removes its effect: the creation screen clears first (<see cref="ClearHeroBackground"/>, BD4), and a second set without a clear
    /// keeps both effects, as in the original. <paramref name="effect"/> is the rule of <see cref="Arcanum.Data.Characters.BackgroundChoice.Effect"/>
    /// (an effect with no modelled change still enters the list). Refused, in order: no hero (<see cref="GameSessionFailure.InvalidHero"/>);
    /// a background outside <see cref="SessionHeroLimits.MinBackground"/>..<see cref="SessionHeroLimits.MaxBackground"/> or an effect rule of
    /// another number (<see cref="GameSessionFailure.InvalidBackground"/>); then the refusals of <see cref="AddHeroEffect"/>.
    /// </summary>
    [Arcanum.Simulation.OriginalFunction(0x4C25E0, Arcanum.Simulation.OriginalFunctionScope.Partial)]
    public SessionEffectResult SetHeroBackground(GameSessionVersion expected, Arcanum.Data.Characters.BackgroundChoice choice,
        SessionEffectDefinition effect)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(choice);
        ArgumentNullException.ThrowIfNull(effect);
        if (_hero is not { } hero) return RejectEffect(GameSessionFailure.InvalidHero);
        if (choice.Background is < SessionHeroLimits.MinBackground or > SessionHeroLimits.MaxBackground || effect.Number != choice.Effect)
            return RejectEffect(GameSessionFailure.InvalidBackground);
        return AddEffects(expected, effect, EffectCause.Background, 1, null,
            _ => hero.Apply(hero.State.WithBackground(choice.Background, choice.Description)));
    }

    /// <summary>
    /// ARC-WORLD-097: the background clear (0x4C2650, ledger BD9) for the hero: zeroes the background and its description number and removes
    /// the first effect entry with the background cause (0x4EA2E0), whatever its number, in one revision. Other background-caused entries
    /// stay. No change when both numbers are 0 and no entry has that cause. Refused without a hero
    /// (<see cref="GameSessionFailure.InvalidHero"/>), on a stale version and with the other refusals of an effect change (the revision).
    /// </summary>
    [Arcanum.Simulation.OriginalFunction(0x4C2650, Arcanum.Simulation.OriginalFunctionScope.Partial)]
    public SessionEffectResult ClearHeroBackground(GameSessionVersion expected)
    {
        ThrowIfDisposed();
        if (_hero is not { } hero) return RejectEffect(GameSessionFailure.InvalidHero);
        var stored = hero.State.Background != SessionBackgrounds.None || hero.State.BackgroundDescription != 0;
        return RemoveEffects(expected, null, 1, EffectCause.Background,
            apply: stored ? _ => hero.Apply(hero.State.WithBackground(SessionBackgrounds.None, 0)) : null);
    }

    /// <summary>
    /// The insert check's answer for the hero (0x466510): the weight first, then the background, then the room. ARC-WORLD-085: for an
    /// item entering the hero's list from outside it (<paramref name="incoming"/>), the session answers the weight itself
    /// (<see cref="SessionInsertVerdict.TooHeavy"/> when the hero's total plus the item would pass its carry weight, ledger WE4); a host's
    /// weight refusal counts as well. Between weight and room the session refuses a technological item for a Technophobia hero
    /// (<see cref="SessionInsertVerdict.TechnologyRefused"/>); any other host answer (the room) yields to both refusals. A move inside
    /// the hero's inventory passes no incoming item (the item is in the list already) and answers the weight with its own load test
    /// (ARC-UI-019, IW12). ARC-UI-019: <paramref name="room"/> false (the hero's grid has no free slot for the item, ledger IW11) refuses an
    /// accepted item as <see cref="SessionInsertVerdict.NoRoom"/>; the wear and grid moves and, since ARC-DEV-333 (B48), every take
    /// from outside (pickup, loot, take from an NPC, new ammunition stack, assembly product: <see cref="HeroTakeRoom"/>) answer it.
    /// </summary>
    [Arcanum.Simulation.OriginalFunction(0x466510, Arcanum.Simulation.OriginalFunctionScope.Partial)]
    private SessionInsertVerdict HeroInsertVerdict(SessionInsertVerdict host, int complexity, Arcanum.Simulation.Items.CarriedItem? incoming = null, bool room = true)
    {
        if (host == SessionInsertVerdict.TooHeavy || incoming is { } item && !HeroFits(item)) return SessionInsertVerdict.TooHeavy;
        if (_hero is not null && SessionBackgrounds.RefusesCarry(_hero.State.Background, complexity)) return SessionInsertVerdict.TechnologyRefused;
        return host == SessionInsertVerdict.Accepted && !room ? SessionInsertVerdict.NoRoom : host;
    }
}
