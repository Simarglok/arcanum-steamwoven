using Arcanum.Simulation;
using Arcanum.Simulation.Character;

namespace Arcanum.Session;

/// <summary>
/// ARC-MAGIC-1510 (the TraitIdx component's base stat write, 0x4B0980, for the two targets whose stats are not the map's stored fields): the hero, whose stats are the character owner's and the hero state's, and an NPC whose stats the
/// follower's advancement record holds (<see cref="QueryNpcAdvancement"/>). The bounds and guards of the setter are the caller's (<c>SessionSpellCaster.TraitIndexComponent</c>); these members read the stored value and store the clamped one.
/// </summary>
public sealed partial class GameSessionOwner
{
    /// <summary>The original's stat numbers whose stored value the hero's state holds and a write can store: the eight primary stats (0..7), the level (17), experience (18), alignment (19), fate points (20) and unspent points (21).</summary>
    public static bool HeroHoldsStat(int index) => index is >= 0 and <= 7 or >= 17 and <= 21;

    /// <summary>The stat numbers an advancement record holds in its sheet (the primary stats 0..7, level 17, unspent points 21, magick points 22 and tech points 23); the others (18, 19, 20, 25) stay the NPC's stored fields.</summary>
    public static bool SheetHoldsStat(int index) => index is >= 0 and <= 7 or 17 or 21 or 22 or 23;

    /// <summary>The hero's stored value of base stat <paramref name="index"/> (see <see cref="HeroHoldsStat"/>) as the stat getter's stored array gives it, or null without a hero or for another number. Effects are not read.</summary>
    public int? QueryHeroStoredStat(int index)
    {
        ThrowIfDisposed();
        if (_hero is not { } hero || !HeroHoldsStat(index)) return null;
        var state = hero.State;
        return index switch
        {
            <= 7 => state.Stats[index],
            17 => state.Level,
            18 => state.Experience,
            19 => state.Alignment,
            20 => state.Fate.Points,
            _ => state.UnspentPoints,
        };
    }

    /// <summary>The hero's effective primary stat if its base were the given value (a setter guard's read).</summary>
    /// <remarks>
    /// ARC-MAGIC-1510 (the guards of the stat setter read the stat after the write, 0x4B0490): the hero's effective primary stat <paramref name="stat"/> if its base were <paramref name="baseValue"/> (clamped to the race's range),
    /// with the poison and crippled legs <see cref="HeroEffectiveStat"/> reads. Null without a hero or for a derived stat. The base is put back before returning.
    /// </remarks>
    public int? QueryHeroEffectiveStatAt(CharacterStat stat, int baseValue)
    {
        ThrowIfDisposed();
        if (_hero is not { } hero || !CharacterStatRules.IsPrimary(stat) || QueryHeroLife() is not { } life) return null;
        var old = hero.State.Stats[(int)stat];
        var range = CharacterStatRules.Range(hero.State.Race, stat);
        if (hero.Stats.SetBase(stat, Math.Clamp(baseValue, range.Minimum, range.Maximum)).Disposition == CharacterStatsDisposition.Rejected) return null;
        try
        {
            var state = life.Record.State;
            return QueryHeroStats(HeroStatContext(Math.Clamp(state.PoisonLevel, 0, CharacterStatRules.MaxPoisonLevel), state.Conditions.HasFlag(Arcanum.Simulation.Combat.LifeConditions.CrippledLegsBoth)))?.Snapshot?.GetEffective(stat);
        }
        finally { hero.Stats.SetBase(stat, old); }
    }

    /// <summary>Whether the hero with the given level and experience would reach a level.</summary>
    /// <remarks>
    /// ARC-MAGIC-1510 (the recalculation 0x4A69C0 the setter runs for a PC after storing the level or experience): whether the hero with <paramref name="level"/> and <paramref name="experience"/> would reach a level, which the session
    /// runs only through an experience award (its messages, scheme and followers' step); null when the session has no experience table to tell.
    /// </remarks>
    public bool? HeroLevelUpPending(int level, int experience)
    {
        ThrowIfDisposed();
        if (_hero is not { } hero || _experience.Rules is not { } rules || level is < 0 or > ExperienceRules.MaxLevel || experience is < 0 or > ExperienceRules.MaxExperience) return null;
        return ExperienceRules.Advance(rules.Levels, level, experience, hero.State.UnspentPoints).LevelsGained > 0;
    }

    /// <summary>Stores base stat <paramref name="index"/> of the hero in its state.</summary>
    /// <remarks>
    /// ARC-MAGIC-1510 (0x4B0980 for the PC, after the setter's clamp and guards): the hero's base stat <paramref name="index"/> (<see cref="HeroHoldsStat"/>) is <paramref name="value"/>. Strength changes the encumbrance level as
    /// every revision compares it (<c>SettleEncumbrance</c>, 0x45F820); alignment arms the followers' re-check as a script's change does (OL17, <see cref="ChangeHeroAlignment"/>); the callbacks after the write are the interface's. The caller
    /// has kept the value within the stat's range and has refused a level or experience that would reach a level. Refused, in order: no hero (<see cref="GameSessionFailure.InvalidHero"/>), a number the hero does not hold or a value outside its
    /// range (<see cref="GameSessionFailure.InvalidChangeKey"/>), the version, the revision. The same value is <see cref="GameSessionDisposition.NoChange"/>.
    /// </remarks>
    public GameSessionMutationResult WriteHeroStat(GameSessionVersion expected, int index, int value)
    {
        ThrowIfDisposed();
        if (_hero is not { } hero) return Reject(GameSessionFailure.InvalidHero);
        if (!HeroHoldsStat(index) || CharacterStatRules.StoredRange(hero.State.Race, index) is not { } range || !range.Contains(value)) return Reject(GameSessionFailure.InvalidChangeKey);
        if (!IsCurrent(expected)) return Reject(GameSessionFailure.StaleVersion);
        var state = hero.State;
        if (QueryHeroStoredStat(index) == value) return NoChange();
        if (_revision == long.MaxValue) return Reject(GameSessionFailure.RevisionExhausted);
        if (index == 19) SetHeroAlignment(value);
        else
        {
            var stats = state.Stats.ToArray();
            if (index <= 7) stats[index] = value;
            hero.Apply(index switch
            {
                <= 7 => state.With(stats, state.Level, state.Experience, state.UnspentPoints, state.Advancement),
                17 => state.WithExperience(value, state.Experience, state.UnspentPoints),
                18 => state.WithExperience(state.Level, value, state.UnspentPoints),
                20 => state.WithFate(state.Fate with { Points = value }),
                _ => state.WithExperience(state.Level, state.Experience, value),
            });
        }
        return Changed();
    }

    /// <summary>The value the advancement record's sheet holds for stat <paramref name="index"/> (<see cref="SheetHoldsStat"/>), or null for another number.</summary>
    public static int? SheetStat(AdvancementSheet sheet, int index)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        return index switch
        {
            >= 0 and <= 7 => sheet.Stats[index],
            17 => sheet.Level,
            21 => sheet.UnspentPoints,
            22 => sheet.MagickPoints,
            23 => sheet.TechPoints,
            _ => null,
        };
    }

    /// <summary>Writes a sheet stat of a follower's advancement record and the indexed base stat beside it.</summary>
    /// <remarks>
    /// ARC-MAGIC-1510 (0x4B0980 for an NPC whose stats live in its advancement record): stat <paramref name="index"/> (<see cref="SheetHoldsStat"/>) of the record is <paramref name="value"/>, and so is the indexed base stat the map's
    /// stored fields are read through (<see cref="WriteCritterStat"/>: the combat values and the seed of a later record start from it), in one step. The bounds and guards are the caller's. Refused, in order: an NPC with no record, a
    /// number the sheet does not hold or a value the sheet does not admit (<see cref="GameSessionFailure.InvalidAdvancement"/>), the version, the revision and the refusals of <see cref="WriteCritterStat"/>. The same value on both is
    /// <see cref="GameSessionDisposition.NoChange"/>.
    /// </remarks>
    public GameSessionMutationResult WriteNpcAdvancementStat(GameSessionVersion expected, SessionMapId map, string entity, int index, int value)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(entity);
        if (!SessionValidation.Entity(entity) || !_advancement.Npcs.TryGetValue(entity, out var record) || SheetStat(record.Sheet, index) is not { } held) return Reject(GameSessionFailure.InvalidAdvancement);
        var sheet = index switch
        {
            <= 7 => record.Sheet with { Stats = record.Sheet.Stats.SetItem(index, value) },
            17 => record.Sheet with { Level = value },
            21 => record.Sheet with { UnspentPoints = value },
            22 => record.Sheet with { MagickPoints = value },
            _ => record.Sheet with { TechPoints = value },
        };
        if (!AutoLevelRules.IsValid(sheet)) return Reject(GameSessionFailure.InvalidAdvancement);
        if (!IsCurrent(expected)) return Reject(GameSessionFailure.StaleVersion);
        if (_revision == long.MaxValue) return Reject(GameSessionFailure.RevisionExhausted);
        // The record is set first so the one revision of the indexed write (or of the step below, when only the record changes) covers both; a refusal puts it back.
        if (held != value) _advancement.Npcs[entity] = record with { Sheet = sheet };
        var written = WriteCritterStat(expected, map, entity, index, value);
        if (written.Disposition == GameSessionDisposition.Rejected) _advancement.Npcs[entity] = record;
        return written.Disposition == GameSessionDisposition.NoChange && held != value ? Changed() : written;
    }
}
