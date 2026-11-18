using Arcanum.Simulation.Character;
using Arcanum.Simulation;

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
    /// aptitude (16) are the effective values with the life's poison and crippled legs (<c>HeroEffectiveStat</c>); the level (17) and the
    /// alignment (19) are the stored values with the changes of that stat in the effect list laid over them (<see cref="HeroLevelAlignmentStat"/>).
    /// </summary>
    public int? QueryHeroScriptStat(int stat)
    {
        ThrowIfDisposed();
        if (_hero is not { } hero) return null;
        return stat switch
        {
            >= 0 and <= 7 => HeroEffectiveStat((CharacterStat)stat),
            ScriptStatAptitude => HeroEffectiveStat(CharacterStat.MagickTechAptitude),
            ScriptStatLevel or ScriptStatAlignment => HeroLevelAlignmentStat(stat, _effects.Entries, _effects.Definitions),
            _ => null,
        };
    }

    /// <summary>
    /// ARC-AI-2750 (exe read of the stat getter 0x4B0490 for stats 17 and 19, the PC included): the hero's level (17) or alignment (19) as the getter gives
    /// it: the stored value, then every change of that stat in the effect list as <see cref="EffectModifiers.Apply"/> aggregates them (the innate entries of
    /// the race and gender stand first, then the entries of <paramref name="entries"/>), clamped to the stat's range. A number without a rule has no changes.
    /// Null without a hero or for another stat.
    /// </summary>
    private int? HeroLevelAlignmentStat(int stat, IReadOnlyList<SessionEffect> entries, IReadOnlyDictionary<int, SessionEffectDefinition> definitions)
    {
        if (_hero is not { } hero || stat is not (ScriptStatLevel or ScriptStatAlignment)) return null;
        var level = stat == ScriptStatLevel;
        var changes = EffectDomain.StatChanges(entries, definitions, stat, HeroInnateRules());
        return Math.Clamp(EffectModifiers.Apply(level ? hero.State.Level : hero.State.Alignment, changes),
            level ? SessionHeroLimits.MinLevel : SessionHeroLimits.MinAlignment, level ? SessionHeroLimits.MaxLevel : SessionHeroLimits.MaxAlignment);
    }

    /// <summary>Whether the hero's background makes the dumb test (0x45FC00, ledger BG5) pass without a low intelligence: background 55, 74 or 75;
    /// false without a hero.</summary>
    [OriginalFunction(0x45FC00, OriginalFunctionScope.Partial)]
    public bool QueryHeroDumbBackground()
    {
        ThrowIfDisposed();
        return _hero?.State.Background is 55 or 74 or 75;
    }
}
