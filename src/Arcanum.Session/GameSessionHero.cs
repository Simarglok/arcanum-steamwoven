using System.Collections.Immutable;
using Arcanum.Scripting.Dialog;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Skills;

namespace Arcanum.Session;

/// <summary>
/// Stored ranges of the hero's non-stat fields, as the source's stat tables bound them (the stat
/// minimum and maximum tables; ledger XP2 checks level, experience and points: level 0..51, experience 0..2,000,000,000, alignment −1000..1000, unspent
/// points 0..56).
/// </summary>
public static class SessionHeroLimits
{
    public const int MinLevel = 0, MaxLevel = 51;
    public const int MinExperience = 0, MaxExperience = ExperienceRules.MaxExperience;
    public const int MinAlignment = -1000, MaxAlignment = 1000;
    public const int MinUnspentPoints = 0, MaxUnspentPoints = 56;
    /// <summary>ARC-WORLD-069: background ids (<c>rules\backgrnd.mes</c> blocks; 0 none). The installed file has fewer than 100.</summary>
    public const int MinBackground = 0, MaxBackground = 999;

    /// <summary>The stats the session stores for the hero, in this order: the eight primary stats, then magick and tech
    /// points. Derived stats are formulas of <see cref="CharacterStatsOwner"/> and are never stored.</summary>
    public static ImmutableArray<CharacterStat> StoredStats { get; } =
    [
        CharacterStat.Strength, CharacterStat.Dexterity, CharacterStat.Constitution, CharacterStat.Beauty,
        CharacterStat.Intelligence, CharacterStat.Perception, CharacterStat.Willpower, CharacterStat.Charisma,
        CharacterStat.MagickPoints, CharacterStat.TechPoints,
    ];
}

/// <summary>
/// One item of the hero's starting kit: its saved kind (type, prototype wire number, numeric name class, description
/// number). Only types a transfer admits are kit items (weapon, armour, food, scroll, written, generic); gold is
/// <see cref="SessionHeroDefinition.Gold"/>, not an item.
/// </summary>
public sealed record SessionHeroKitItem(SessionItemKind Kind)
{
    /// <summary>ARC-WORLD-085: the item's weight facts (its prototype's in a pre-made hero's kit); none weighs 0.</summary>
    public SessionItemWeight Weight { get; init; }
}

/// <summary>
/// New-game input for the player character (ARC-WORLD-015), built by the host from its data: for a pre-made hero the PC
/// prototype, its stored race and female effects and its inventory source set (ARC-RES-027). <see cref="Stats"/> are
/// the stored base values in <see cref="SessionHeroLimits.StoredStats"/> order; <see cref="RaceEffect"/> and
/// <see cref="GenderEffect"/> are the innate effects already reduced to the stats the character owner models (null: no
/// modelled change). <see cref="Kit"/> enters the PC's list in order, before any item the PC takes from the world.
/// <see cref="Experience"/> (ARC-WORLD-030) is the stored experience; a pre-made hero has none. <see cref="Advancement"/>
/// (ARC-WORLD-036) holds its skills, colleges, degrees, bought points and auto-level scheme (null: none bought, no scheme); with
/// scheme data the session runs that scheme at creation, as a new PC's level set does. <see cref="InnateSkills"/>
/// (ARC-SKILLS-002) are the skill changes of the same race and gender effects (null: none), applied whenever a skill level is
/// read and never written into the stored points.
/// </summary>
public sealed record SessionHeroDefinition(int Prototype, CharacterRace Race, CharacterGender Gender, IReadOnlyList<int> Stats,
    int Level, int UnspentPoints, int Alignment, StatEffectDefinition? RaceEffect, StatEffectDefinition? GenderEffect, int Gold,
    IReadOnlyList<SessionHeroKitItem> Kit, int Experience = 0, SessionHeroAdvancement? Advancement = null,
    IReadOnlyList<SessionInnateSkillChange>? InnateSkills = null, int Background = 0);

/// <summary>
/// Saved hero state: prototype, race, gender, stored stats in <see cref="SessionHeroLimits.StoredStats"/> order, level,
/// experience (ARC-WORLD-030), unspent points, alignment, the advancement fields (ARC-WORLD-036) and the applied innate effects with their modifiers (race first, then gender; an innate
/// effect without a modelled change is not listed), the innate effects' skill changes (ARC-SKILLS-002) and the fate points with
/// the armed fates (ARC-WORLD-054). Gold and the kit items live in <see cref="SessionInventoryState"/>.
/// </summary>
public sealed class SessionHeroState
{
    internal SessionHeroState(int prototype, CharacterRace race, CharacterGender gender, int[] stats, int level, int unspentPoints,
        int alignment, StatEffectDefinition? raceEffect, StatEffectDefinition? genderEffect, int experience = 0,
        SessionHeroAdvancement? advancement = null, ImmutableArray<SessionInnateSkillChange> innateSkills = default, FateState fate = default,
        int background = 0)
    {
        Background = background;
        Prototype = prototype; Race = race; Gender = gender; Stats = Array.AsReadOnly(stats); Level = level;
        UnspentPoints = unspentPoints; Alignment = alignment; RaceEffect = raceEffect; GenderEffect = genderEffect;
        Experience = experience; Advancement = advancement ?? SessionHeroAdvancement.None;
        InnateSkills = innateSkills.IsDefault ? [] : innateSkills;
        Fate = fate;
    }

    public int Prototype { get; }
    public CharacterRace Race { get; }
    public CharacterGender Gender { get; }
    public IReadOnlyList<int> Stats { get; }
    public int Level { get; }
    /// <summary>The stored experience (ARC-WORLD-030).</summary>
    public int Experience { get; }
    public int UnspentPoints { get; }
    public int Alignment { get; }
    public StatEffectDefinition? RaceEffect { get; }
    public StatEffectDefinition? GenderEffect { get; }
    /// <summary>The skills, colleges, degrees, bought points and auto-level scheme (ARC-WORLD-036).</summary>
    public SessionHeroAdvancement Advancement { get; }
    /// <summary>The race and gender effects' skill changes (ARC-SKILLS-002), race first, in the effects' order.</summary>
    public ImmutableArray<SessionInnateSkillChange> InnateSkills { get; }
    /// <summary>The fate points (0..100) and the armed fates (ARC-WORLD-054).</summary>
    public FateState Fate { get; }
    /// <summary>ARC-WORLD-069: the PC's background id (the original's PC field 0x101, written by character creation; a pre-made hero's
    /// comes from its prototype); 0 for none.</summary>
    public int Background { get; }

    /// <summary>The same hero with a new level, experience and unspent points (an experience award).</summary>
    internal SessionHeroState WithExperience(int level, int experience, int unspentPoints) =>
        new(Prototype, Race, Gender, [.. Stats], level, unspentPoints, Alignment, RaceEffect, GenderEffect, experience, Advancement, InnateSkills, Fate, Background);

    /// <summary>The same hero with new stored stats, level, experience, unspent points and advancement (a scheme's purchases).</summary>
    internal SessionHeroState With(int[] stats, int level, int experience, int unspentPoints, SessionHeroAdvancement advancement) =>
        new(Prototype, Race, Gender, stats, level, unspentPoints, Alignment, RaceEffect, GenderEffect, experience, advancement, InnateSkills, Fate, Background);

    /// <summary>The same hero with a new stored alignment (ARC-WORLD-067).</summary>
    internal SessionHeroState WithAlignment(int alignment) =>
        new(Prototype, Race, Gender, [.. Stats], Level, UnspentPoints, alignment, RaceEffect, GenderEffect, Experience, Advancement, InnateSkills, Fate, Background);

    /// <summary>The same hero with new fate points and armed fates (ARC-WORLD-054).</summary>
    internal SessionHeroState WithFate(FateState fate) =>
        new(Prototype, Race, Gender, [.. Stats], Level, UnspentPoints, Alignment, RaceEffect, GenderEffect, Experience, Advancement, InnateSkills, fate, Background);
}

/// <summary>The hero domain of one owner: its saved fields and the character owner built from them.</summary>
internal sealed class HeroDomain
{
    /// <summary>Entity ids of kit items: <c>kit:1</c>, <c>kit:2</c>, … in kit order. A seed item with such an id would be
    /// an <see cref="GameSessionFailure.InventoryMismatch"/> like any other item the PC already holds.</summary>
    internal const string KitPrefix = "kit:";

    private HeroDomain(SessionHeroState state, CharacterStatsOwner stats)
    {
        State = state;
        Stats = stats;
    }

    /// <summary>Replaced by <see cref="Apply"/> (an experience award, ARC-WORLD-030, or a scheme's purchases, ARC-WORLD-036).</summary>
    internal SessionHeroState State { get; private set; }
    internal CharacterStatsOwner Stats { get; }

    /// <summary>Takes a new state of the same hero: the character owner's stored stats follow it. The caller has built the
    /// state from admitted values, so a refused stat is a broken invariant.</summary>
    internal void Apply(SessionHeroState next)
    {
        for (var index = 0; index < next.Stats.Count; index++)
            if (next.Stats[index] != State.Stats[index] &&
                Stats.SetBase(SessionHeroLimits.StoredStats[index], next.Stats[index]).Disposition == CharacterStatsDisposition.Rejected)
                throw new InvalidOperationException("Hero stat refused after admission.");
        State = next;
    }

    internal static string KitEntity(int index) => KitPrefix + (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>The number n of a kit entity <c>kit:n</c> (1-based), or null for any other entity.</summary>
    internal static int? KitNumber(string entity) =>
        entity.StartsWith(KitPrefix, StringComparison.Ordinal) && int.TryParse(entity.AsSpan(KitPrefix.Length), System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture, out var n) && n >= 1 && entity == KitEntity(n - 1) ? n : null;

    /// <summary>
    /// Builds the hero from saved or new fields; null when any field is outside its range or an effect is not one the
    /// character owner admits (no modifiers, an unknown stat or operator, a magnitude above its limit), or an innate skill change
    /// is not one <see cref="SessionInnateSkillChange.IsValid"/> admits, more than <see cref="SessionInnateSkillChange.MaxPerEffect"/> of
    /// one cause, or a race change after a gender change (the effects' order, which the aggregation of bounds reads), or a fate state
    /// outside 0..100 points and the eleven armed bits.
    /// The character owner holds only this hero's race and gender effects; the session never calls its race or gender setters.
    /// </summary>
    internal static HeroDomain? Build(int prototype, CharacterRace race, CharacterGender gender, IReadOnlyList<int>? stats,
        int level, int unspentPoints, int alignment, StatEffectDefinition? raceEffect, StatEffectDefinition? genderEffect, int experience,
        SessionHeroAdvancement? advancement, IReadOnlyList<SessionInnateSkillChange>? innateSkills, FateState fate = default, int background = 0)
    {
        if (background is < SessionHeroLimits.MinBackground or > SessionHeroLimits.MaxBackground) return null;
        advancement ??= SessionHeroAdvancement.None;
        if (!fate.IsValid) return null;
        if (!advancement.IsValid()) return null;
        ImmutableArray<SessionInnateSkillChange> skills = [.. innateSkills ?? []];
        if (skills.Length > SessionInnateSkillChange.MaxChanges || !skills.All(SessionInnateSkillChange.IsValid)) return null;
        var raceSkills = skills.Count(change => change.Cause == EffectCause.Race);
        if (raceSkills > SessionInnateSkillChange.MaxPerEffect || skills.Length - raceSkills > SessionInnateSkillChange.MaxPerEffect ||
            skills.Skip(raceSkills).Any(change => change.Cause == EffectCause.Race)) return null;
        // Checked on the copy, so a list whose count and contents disagree cannot pass.
        var values = stats?.ToArray();
        if (prototype < 1 || values is null || values.Length != SessionHeroLimits.StoredStats.Length) return null;
        if (level is < SessionHeroLimits.MinLevel or > SessionHeroLimits.MaxLevel ||
            unspentPoints is < SessionHeroLimits.MinUnspentPoints or > SessionHeroLimits.MaxUnspentPoints ||
            experience is < SessionHeroLimits.MinExperience or > SessionHeroLimits.MaxExperience ||
            alignment is < SessionHeroLimits.MinAlignment or > SessionHeroLimits.MaxAlignment) return null;
        var races = raceEffect is null ? ImmutableDictionary<CharacterRace, StatEffectDefinition>.Empty
            : ImmutableDictionary<CharacterRace, StatEffectDefinition>.Empty.Add(race, raceEffect);
        var genders = genderEffect is null ? ImmutableDictionary<CharacterGender, StatEffectDefinition>.Empty
            : ImmutableDictionary<CharacterGender, StatEffectDefinition>.Empty.Add(gender, genderEffect);
        var created = CharacterStatsOwner.Create(new(race, gender, new(races, genders), CharacterStatLimits.Default));
        if (created.Owner is not { } owner) return null;
        for (var index = 0; index < values.Length; index++)
            if (owner.SetBase(SessionHeroLimits.StoredStats[index], values[index]).Disposition == CharacterStatsDisposition.Rejected) return null;
        return new(new(prototype, race, gender, values, level, unspentPoints, alignment, raceEffect, genderEffect, experience, advancement, skills, fate, background), owner);
    }

    /// <summary>The DLG reply-eligibility facts of this hero (see <see cref="GameSessionOwner.QueryHeroDialogFacts"/>).</summary>
    internal DialogPcFacts DialogFacts()
    {
        var snapshot = Stats.Query(new(0)).Snapshot!;
        var gender = State.Gender == CharacterGender.Female ? DialogPcGender.Female : DialogPcGender.Male;
        return new(gender, snapshot.GetEffective(CharacterStat.Intelligence), false, SourceRace(State.Race));
    }

    /// <summary>The source's zero-based race index (human 0 .. ogre 10), the value DLG race selectors compare.</summary>
    internal static int SourceRace(CharacterRace race) => race switch
    {
        CharacterRace.Human => 0, CharacterRace.Dwarf => 1, CharacterRace.Elf => 2, CharacterRace.HalfElf => 3,
        CharacterRace.Gnome => 4, CharacterRace.Halfling => 5, CharacterRace.HalfOrc => 6, CharacterRace.HalfOgre => 7,
        CharacterRace.DarkElf => 8, CharacterRace.Ogre => 9, CharacterRace.Orc => 10,
        _ => throw new ArgumentOutOfRangeException(nameof(race)),
    };
}
