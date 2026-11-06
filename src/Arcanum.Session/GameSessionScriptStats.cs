using Arcanum.Simulation.Character;

namespace Arcanum.Session;

/// <summary>The hero's stats as the script command get stat (action 33, ARC-DEV-157, ledger GS1–GS4) reads them by the original's stat number.</summary>
public sealed partial class GameSessionOwner
{
    /// <summary>The original's stat numbers get stat reads for the hero (CS4–CS7 of the character ledger): the eight primary stats, the level,
    /// the alignment and the magickal-technological aptitude.</summary>
    public const int ScriptStatLevel = 17, ScriptStatAlignment = 19, ScriptStatAptitude = 16;

    /// <summary>
    /// The hero's stat <paramref name="stat"/> by the original's number as the stat getter (0x4B0490) gives it, or null without a hero or for a
    /// stat the session does not hold yet (GS3: the secondary stats 8..15, experience, fate and the rest). Strength to charisma (0..7) and the
    /// aptitude (16) are the effective values with the life's poison and crippled legs (<c>HeroEffectiveStat</c>); the level (17) is the hero's
    /// stored level and the alignment (19) the stored alignment (effects on them are not read).
    /// </summary>
    public int? QueryHeroScriptStat(int stat)
    {
        ThrowIfDisposed();
        if (_hero is not { } hero) return null;
        return stat switch
        {
            >= 0 and <= 7 => HeroEffectiveStat((CharacterStat)stat),
            ScriptStatAptitude => HeroEffectiveStat(CharacterStat.MagickTechAptitude),
            ScriptStatLevel => hero.State.Level,
            ScriptStatAlignment => hero.State.Alignment,
            _ => null,
        };
    }

    /// <summary>Whether the hero's background makes the dumb test (0x45FC00, ledger BG5) pass without a low intelligence: background 55, 74 or 75;
    /// false without a hero.</summary>
    public bool QueryHeroDumbBackground()
    {
        ThrowIfDisposed();
        return _hero?.State.Background is 55 or 74 or 75;
    }
}
