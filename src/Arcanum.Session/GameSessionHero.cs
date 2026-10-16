using System.Collections.Immutable;
using Arcanum.Scripting.Dialog;
using Arcanum.Simulation.Character;

namespace Arcanum.Session;

/// <summary>
/// Stored ranges of the hero's non-stat fields, as the source's stat tables bound them (pinned CE <c>stat.c</c>
/// <c>stat_min_values</c>/<c>stat_max_values</c>: level 0..51, alignment −1000..1000, unspent points 0..56).
/// </summary>
public static class SessionHeroLimits
{
    public const int MinLevel = 0, MaxLevel = 51;
    public const int MinAlignment = -1000, MaxAlignment = 1000;
    public const int MinUnspentPoints = 0, MaxUnspentPoints = 56;

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
public sealed record SessionHeroKitItem(SessionItemKind Kind);

/// <summary>
/// New-game input for the player character (ARC-WORLD-015), built by the host from its data: for a pre-made hero the PC
/// prototype, its stored race and female effects and its inventory source set (ARC-RES-027). <see cref="Stats"/> are
/// the stored base values in <see cref="SessionHeroLimits.StoredStats"/> order; <see cref="RaceEffect"/> and
/// <see cref="GenderEffect"/> are the innate effects already reduced to the stats the character owner models (null: no
/// modelled change). <see cref="Kit"/> enters the PC's list in order, before any item the PC takes from the world.
/// </summary>
public sealed record SessionHeroDefinition(int Prototype, CharacterRace Race, CharacterGender Gender, IReadOnlyList<int> Stats,
    int Level, int UnspentPoints, int Alignment, StatEffectDefinition? RaceEffect, StatEffectDefinition? GenderEffect, int Gold,
    IReadOnlyList<SessionHeroKitItem> Kit);

/// <summary>
/// Saved hero state: prototype, race, gender, stored stats in <see cref="SessionHeroLimits.StoredStats"/> order, level,
/// unspent points, alignment and the applied innate effects with their modifiers (race first, then gender; an innate
/// effect without a modelled change is not listed). Gold and the kit items live in <see cref="SessionInventoryState"/>.
/// </summary>
public sealed class SessionHeroState
{
    internal SessionHeroState(int prototype, CharacterRace race, CharacterGender gender, int[] stats, int level, int unspentPoints,
        int alignment, StatEffectDefinition? raceEffect, StatEffectDefinition? genderEffect)
    {
        Prototype = prototype; Race = race; Gender = gender; Stats = Array.AsReadOnly(stats); Level = level;
        UnspentPoints = unspentPoints; Alignment = alignment; RaceEffect = raceEffect; GenderEffect = genderEffect;
    }

    public int Prototype { get; }
    public CharacterRace Race { get; }
    public CharacterGender Gender { get; }
    public IReadOnlyList<int> Stats { get; }
    public int Level { get; }
    public int UnspentPoints { get; }
    public int Alignment { get; }
    public StatEffectDefinition? RaceEffect { get; }
    public StatEffectDefinition? GenderEffect { get; }
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

    internal SessionHeroState State { get; }
    internal CharacterStatsOwner Stats { get; }

    internal static string KitEntity(int index) => KitPrefix + (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// Builds the hero from saved or new fields; null when any field is outside its range or an effect is not one the
    /// character owner admits (no modifiers, an unknown stat or operator, a magnitude above its limit). The character owner
    /// holds only this hero's race and gender effects; the session never calls its race or gender setters.
    /// </summary>
    internal static HeroDomain? Build(int prototype, CharacterRace race, CharacterGender gender, IReadOnlyList<int>? stats,
        int level, int unspentPoints, int alignment, StatEffectDefinition? raceEffect, StatEffectDefinition? genderEffect)
    {
        if (prototype < 1 || stats is null || stats.Count != SessionHeroLimits.StoredStats.Length) return null;
        if (level is < SessionHeroLimits.MinLevel or > SessionHeroLimits.MaxLevel ||
            unspentPoints is < SessionHeroLimits.MinUnspentPoints or > SessionHeroLimits.MaxUnspentPoints ||
            alignment is < SessionHeroLimits.MinAlignment or > SessionHeroLimits.MaxAlignment) return null;
        var races = raceEffect is null ? ImmutableDictionary<CharacterRace, StatEffectDefinition>.Empty
            : ImmutableDictionary<CharacterRace, StatEffectDefinition>.Empty.Add(race, raceEffect);
        var genders = genderEffect is null ? ImmutableDictionary<CharacterGender, StatEffectDefinition>.Empty
            : ImmutableDictionary<CharacterGender, StatEffectDefinition>.Empty.Add(gender, genderEffect);
        var created = CharacterStatsOwner.Create(new(race, gender, new(races, genders), CharacterStatLimits.Default));
        if (created.Owner is not { } owner) return null;
        var values = stats.ToArray();
        for (var index = 0; index < values.Length; index++)
            if (owner.SetBase(SessionHeroLimits.StoredStats[index], values[index]).Disposition == CharacterStatsDisposition.Rejected) return null;
        return new(new(prototype, race, gender, values, level, unspentPoints, alignment, raceEffect, genderEffect), owner);
    }

    /// <summary>The DLG reply-eligibility facts of this hero (see <see cref="GameSessionOwner.QueryHeroDialogFacts"/>).</summary>
    internal DialogPcFacts DialogFacts()
    {
        var snapshot = Stats.Query(new(0)).Snapshot!;
        var gender = State.Gender == CharacterGender.Female ? DialogPcGender.Female : DialogPcGender.Male;
        return new(gender, snapshot.GetEffective(CharacterStat.Intelligence), false, SourceRace(State.Race));
    }

    /// <summary>The source's zero-based race index (CE <c>RACE_*</c>), the value DLG race selectors compare.</summary>
    internal static int SourceRace(CharacterRace race) => race switch
    {
        CharacterRace.Human => 0, CharacterRace.Dwarf => 1, CharacterRace.Elf => 2, CharacterRace.HalfElf => 3,
        CharacterRace.Gnome => 4, CharacterRace.Halfling => 5, CharacterRace.HalfOrc => 6, CharacterRace.HalfOgre => 7,
        CharacterRace.DarkElf => 8, CharacterRace.Ogre => 9, CharacterRace.Orc => 10,
        _ => throw new ArgumentOutOfRangeException(nameof(race)),
    };
}
