using Arcanum.Simulation;
using Arcanum.Simulation.Character;

namespace Arcanum.Session;

/// <summary>
/// ARC-MAGIC-1510 (the TraitIdx component's base stat write, 0x4B0980, for the two targets whose stats are not the map's stored fields): the hero, whose stats are the character owner's and the hero state's, and an NPC whose stats the
/// follower's advancement record holds (<see cref="QueryNpcAdvancement"/>). The bounds and guards of the setter are the caller's (<c>SessionSpellCaster.TraitIndexComponent</c>); these members read the stored value and store the clamped one.
/// </summary>
public sealed partial class GameSessionOwner
{
    /// <summary>The original's stat numbers whose stored value the hero's state holds and a write can store: the eight primary stats (0..7), the level (17), experience (18), alignment (19), fate points (20), unspent points (21) and (ARC-MAGIC-1628) magick points (22) and tech points (23), which the state keeps after the primary stats.</summary>
    public static bool HeroHoldsStat(int index) => index is >= 0 and <= 7 or >= 17 and <= 23;

    /// <summary>The stat numbers an advancement record holds in its sheet (the primary stats 0..7, level 17, unspent points 21, magick points 22, tech points 23 and, ARC-MAGIC-1628, the race 27); the others (18, 19, 20, 25, 26) stay the NPC's stored fields.</summary>
    public static bool SheetHoldsStat(int index) => index is >= 0 and <= 7 or 17 or 21 or 22 or 23 or 27;

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
            21 => state.UnspentPoints,
            22 => state.Stats[8],
            _ => state.Stats[9],
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

    /// <summary>Whether the hero's wear holds no script the rewield would run.</summary>
    /// <remarks>
    /// ARC-MAGIC-1628 (the rewield 0x465530 the setter runs after storing magick points (22) or tech points (23)): it walks the wear slots 1000 to 1008 and for each worn item recalculates the wearer's light (0x467E80) and runs the item's
    /// script points 25 (wield off) and 24 (wield on) with the wearer as the initiator. True when no worn item of the hero has either point: the runs then do nothing (a run of a point with no script returns at once) and the hero's light is
    /// derived from the wear (<see cref="QueryHeroLight"/>), not stored. False with a worn wield script (ARC-MAGIC-1744: <see cref="PlanHeroRewield"/> runs them, so this answers only whether there is anything to run) and without a hero.
    /// </remarks>
    public bool HeroRewieldReady()
    {
        ThrowIfDisposed();
        return _hero is not null && !_equipment.Worn.Values.Any(worn => worn.ScriptPoints.Contains(SessionScriptPoint.WieldOn) || worn.ScriptPoints.Contains(SessionScriptPoint.WieldOff));
    }

    /// <summary>Stores the hero's experience and runs the level recalculation, as the stat setter does for a PC.</summary>
    /// <remarks>
    /// ARC-MAGIC-1628 (case 0x12 of the stat setter 0x4B0980, then 0x4A69C0): the experience is stored and the recalculation runs on the stored value, as the experience award's does (<see cref="AwardHeroExperience"/>) but without the difficulty,
    /// the experience effects and the dead-hero check, which belong to the award alone: every level the experience reaches is written with its points (unspent points capped), the hero's auto-level scheme spends them, and the last-level branch
    /// writes its table threshold as the experience (ledger XP5). The caller has kept the value within the stat's range. <see cref="SessionHeroExperienceWrite.Award"/> is the recalculation's award when it wrote a level or stepped the followers
    /// (the level-up messages, the scheme's purchases and the followers' step are the host's, as for an award), else null. Refused, in order: no hero (<see cref="GameSessionFailure.InvalidHero"/>), no experience data
    /// (<see cref="GameSessionFailure.ExperienceUnavailable"/>), a value outside the range (<see cref="GameSessionFailure.InvalidChangeKey"/>), the version, the revision. A write that changes nothing is <see cref="GameSessionDisposition.NoChange"/>.
    /// </remarks>
    public SessionHeroExperienceWrite WriteHeroExperienceStat(GameSessionVersion expected, int value)
    {
        ThrowIfDisposed();
        if (_hero is not { } hero) return new(Reject(GameSessionFailure.InvalidHero), null);
        if (_experience.Rules is null) return new(Reject(GameSessionFailure.ExperienceUnavailable), null);
        if (CharacterStatRules.StoredRange(hero.State.Race, 18) is not { } range || !range.Contains(value)) return new(Reject(GameSessionFailure.InvalidChangeKey), null);
        if (!IsCurrent(expected)) return new(Reject(GameSessionFailure.StaleVersion), null);
        if (_revision == long.MaxValue) return new(Reject(GameSessionFailure.RevisionExhausted), null);
        var recalculated = RecalculateHero(value, value - hero.State.Experience);
        var award = recalculated.Award.LevelsGained > 0 || recalculated.Award.Followers is not null ? recalculated.Award : null;
        if (!recalculated.Changed(hero.State)) return new(NoChange(), award);
        hero.Apply(recalculated.Hero);
        return new(Changed(), award);
    }

    /// <summary>Stores base stat <paramref name="index"/> of the hero in its state.</summary>
    /// <remarks>
    /// ARC-MAGIC-1510 (0x4B0980 for the PC, after the setter's clamp and guards): the hero's base stat <paramref name="index"/> (<see cref="HeroHoldsStat"/>) is <paramref name="value"/>. Strength changes the encumbrance level as
    /// every revision compares it (<c>SettleEncumbrance</c>, 0x45F820); alignment arms the followers' re-check as a script's change does (OL17, <see cref="ChangeHeroAlignment"/>); the callbacks after the write are the interface's. The caller
    /// has kept the value within the stat's range, and for magick and tech points has planned the rewield's runs over the new value (<see cref="PlanHeroRewield"/>, applied after this write by <c>CommitRewield</c>, ARC-MAGIC-1744). Experience (18) is always refused here (<see cref="GameSessionFailure.InvalidChangeKey"/>): it runs the level recalculation, so use <see cref="WriteHeroExperienceStat"/>. Refused, in order: no hero (<see cref="GameSessionFailure.InvalidHero"/>), a number the hero does not hold or a value outside its
    /// range (<see cref="GameSessionFailure.InvalidChangeKey"/>), the version, the revision. The same value is <see cref="GameSessionDisposition.NoChange"/>.
    /// </remarks>
    public GameSessionMutationResult WriteHeroStat(GameSessionVersion expected, int index, int value)
    {
        ThrowIfDisposed();
        if (_hero is not { } hero) return Reject(GameSessionFailure.InvalidHero);
        // Experience runs the level recalculation after its store: WriteHeroExperienceStat.
        if (!HeroHoldsStat(index) || index == 18 || CharacterStatRules.StoredRange(hero.State.Race, index) is not { } range || !range.Contains(value)) return Reject(GameSessionFailure.InvalidChangeKey);
        if (!IsCurrent(expected)) return Reject(GameSessionFailure.StaleVersion);
        var state = hero.State;
        if (QueryHeroStoredStat(index) == value) return NoChange();
        if (_revision == long.MaxValue) return Reject(GameSessionFailure.RevisionExhausted);
        if (index == 19) SetHeroAlignment(value);
        else
        {
            var stats = state.Stats.ToArray();
            if (index <= 7) stats[index] = value;
            else if (index is 22 or 23) stats[index - 14] = value;
            hero.Apply(index switch
            {
                <= 7 or 22 or 23 => state.With(stats, state.Level, state.Experience, state.UnspentPoints, state.Advancement),
                17 => state.WithExperience(value, state.Experience, state.UnspentPoints),
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
            27 => (int)sheet.Race,
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
            23 => record.Sheet with { TechPoints = value },
            _ => record.Sheet with { Race = (CharacterRace)value },
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

/// <summary>ARC-MAGIC-1628: what <see cref="GameSessionOwner.WriteHeroExperienceStat"/> did: the mutation and the recalculation's award (null when it wrote no level and stepped no follower).</summary>
public sealed record SessionHeroExperienceWrite(GameSessionMutationResult Result, SessionExperienceAward? Award);
