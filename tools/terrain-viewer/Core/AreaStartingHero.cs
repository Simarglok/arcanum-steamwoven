using Arcanum.Data.Characters;
using Arcanum.Data.Combat;
using Arcanum.Data.Content;
using Arcanum.Data.Messages;
using Arcanum.Scripting.GlobalFlags;
using Arcanum.Session;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Skills;

namespace Arcanum.TerrainViewer.Core;

/// <summary>One of the twelve pre-made heroes the original main menu offers: the project's own name (race and gender,
/// unique among the twelve) and its basic prototype 16067..16078 (ARC-RES-025, the table of built-in PC prototypes).
/// <see cref="Index"/> is the main menu's hero index minus one, which picks its global flag.</summary>
public sealed record AreaPremadeHero(string Name, int BasicPrototype)
{
    public int Index => BasicPrototype - AreaStartingHero.FirstPremadePrototype;
}

public enum AreaHeroStatus
{
    Ready,
    /// <summary>The module archive is not <c>&lt;game&gt;/modules/&lt;Name&gt;.dat</c>, or the stack does not mount it.</summary>
    ModuleOutsideGameRoot,
    MountFailed,
    StackFailed,
    /// <summary>A rule or menu table is absent, unreadable or not a valid MES file (<see cref="AreaHeroLoad.Detail"/> names it).</summary>
    TableUnavailable,
    /// <summary>The pre-made hero reader refused the prototype (<see cref="AreaHeroLoad.Read"/>).</summary>
    HeroRefused,
    /// <summary>The hero's main-menu flag message is absent or not a decimal flag number of the new-game flag domain.</summary>
    FlagInvalid,
    /// <summary>ARC-UI-007: an experience table entry is not a number, or the level table is not strictly increasing from 0
    /// (<see cref="AreaHeroLoad.Detail"/> names it).</summary>
    ExperienceInvalid,
    /// <summary>ARC-WORLD-036: a scheme rule the grammar refuses, or a spell willpower that is not a number
    /// (<see cref="AreaHeroLoad.Detail"/> names it).</summary>
    AutoLevelInvalid,
    /// <summary>ARC-WORLD-034: a kit item's prototype does not read as combat fields, or a weapon, armour or generic kit item has
    /// values outside the wear rules' bounds (<see cref="AreaHeroLoad.Detail"/> names the prototype).</summary>
    KitUnreadable,
}

/// <param name="Definition">The session's hero, or null unless <see cref="AreaHeroStatus.Ready"/>.</param>
/// <param name="Flag">The hero's global flag (<c>MainMenu.mes</c> 551 + index), set true at the start; null unless ready.</param>
/// <param name="DroppedChanges">Effect changes on attributes the character owner does not model (see <see cref="AreaStartingHero.Definition"/>).</param>
/// <param name="Experience">ARC-UI-007: the session's experience data (<c>rules\xp_level.mes</c>, <c>rules\xp_quest.mes</c>,
/// <c>rules\gamequest.mes</c>), or null unless ready.</param>
/// <param name="AutoLevel">ARC-WORLD-036: the session's scheme data (<c>rules\gamelevel.mes</c>, <c>rules\userlevel.mes</c>,
/// <c>rules\spelllist.mes</c>), or null unless ready.</param>
/// <param name="Effects">ARC-WORLD-036: the effect rule of a number (<c>rules\effect.mes</c> 50..399, the module's
/// <c>rules\gameeffect.mes</c> 0..49), for the followers' effects; null unless ready.</param>
/// <param name="KitWear">ARC-WORLD-034: the wear facts of each kit item in kit order (null for an item that is worn nowhere), which
/// <see cref="AreaTransitionCoordinator.CreateSession"/> gives the session's best-items call; null unless ready.</param>
public sealed record AreaHeroLoad(AreaHeroStatus Status, AreaPremadeHero Hero, SessionHeroDefinition? Definition, int? Flag,
    int DroppedChanges, PremadeHeroRead? Read, string Detail, SessionExperienceRules? Experience = null,
    SessionAutoLevelRules? AutoLevel = null, Func<int, EffectRule?>? Effects = null, IReadOnlyList<SessionWearableItem?>? KitWear = null)
{
    /// <summary>ARC-SKILLS-005: the kit's healing items, by kit index (null for anything else), from the same prototype fields.</summary>
    public IReadOnlyList<SessionHealingItem?>? KitHealing { get; init; }

    /// <summary>ARC-SKILLS-006: the kit's lockpicks, by kit index (null for anything else), from the same prototype fields.</summary>
    public IReadOnlyList<SessionLockpick?>? KitLockpicks { get; init; }

    /// <summary>ARC-COMBAT-008: the kit's durability values for the attack's wear, by kit index (null for a prototype that does not
    /// read or an item outside the durability bounds), from the same prototype fields (<see cref="SessionWearItem.FromCombatFields"/>).</summary>
    public IReadOnlyList<SessionWearItem?>? KitDurability { get; init; }

    /// <summary>ARC-COMBAT-016: each kit weapon's projectile art (field 127, -1 for none), by kit index; null for a prototype that does
    /// not read or is no weapon. A shot on the ranged path with an art flies as a projectile.</summary>
    public IReadOnlyList<int?>? KitMissileArt { get; init; }

    /// <summary>ARC-RENDER-033: each kit item's ground ART id (its prototype's current art, field 0), by kit index; null for a
    /// prototype that does not read. The viewer draws a placed kit item with it.</summary>
    public IReadOnlyList<uint?>? KitArt { get; init; }
}

/// <summary>
/// ARC-WORLD-027: the AREA viewer's start as an original pre-made hero (<c>--hero</c>). Reads the hero with
/// <see cref="PremadeHeroReader"/> (ARC-WORLD-015) from the game folder that holds the module archive
/// (<see cref="AreaContentRoot.Discover"/>, the rule of the description tables and scripts): the prototypes the original reads
/// (<see cref="ContentPrototypes"/>, ARC-DATA-026), <c>rules\effect.mes</c>,
/// <c>rules\InvenSource.mes</c>, <c>rules\InvenSourceBuy.mes</c> and the hero's flag from <c>mes\MainMenu.mes</c>; since ARC-UI-007
/// also the experience tables the session awards with and the character sheet reads the next level from (ARC-WORLD-030).
/// Each table comes from the sources of its load phase (<see cref="ContentPhase"/>, ARC-DATA-027). Godot-free; a typed status, never an exception for data.
/// </summary>
public static class AreaStartingHero
{
    public const int FirstPremadePrototype = 16067;
    /// <summary>Main-menu message of the first hero's global flag; hero <c>i</c> reads message <c>551 + i</c> (the original
    /// 0x545B60, checked in the executable).</summary>
    public const int FirstFlagMessage = 551;
    public const string EffectsPath = "rules/effect.mes", SourcesPath = "rules/InvenSource.mes", SourcesBuyPath = "rules/InvenSourceBuy.mes",
        MainMenuPath = "mes/MainMenu.mes", LevelsPath = "rules/xp_level.mes", QuestExperiencePath = "rules/xp_quest.mes",
        QuestLevelsPath = "rules/gamequest.mes", ModuleEffectsPath = "rules/gameeffect.mes";

    /// <summary>The twelve heroes in main-menu order. Keep in step with <c>PREMADE_HEROES</c> in
    /// <c>tools/local_dev/launch.py</c> (tools/local_dev/test_launcher_sync.py fails on any drift).</summary>
    public static IReadOnlyList<AreaPremadeHero> Premade { get; } = Array.AsReadOnly(new AreaPremadeHero[]
    {
        new("halfling-male", 16067), new("gnome-male", 16068), new("half-elf-male", 16069), new("half-ogre-male", 16070),
        new("elf-male", 16071), new("human-male", 16072), new("half-orc-male", 16073), new("dwarf-male", 16074),
        new("human-female", 16075), new("elf-female", 16076), new("half-orc-female", 16077), new("half-elf-female", 16078),
    });

    /// <summary>The hero of that name (ordinal, exact), or null.</summary>
    public static AreaPremadeHero? Find(string name) => Premade.FirstOrDefault(hero => string.Equals(hero.Name, name, StringComparison.Ordinal));

    /// <summary>Reads <paramref name="hero"/> from the game folder of <paramref name="modulePath"/> and maps it to the session.</summary>
    public static AreaHeroLoad Load(string modulePath, AreaPremadeHero hero)
    {
        ArgumentNullException.ThrowIfNull(modulePath);
        ArgumentNullException.ThrowIfNull(hero);
        var found = AreaContentRoot.Discover(modulePath);
        if (found.Status != AreaContentRootStatus.Ready)
            return Refused(found.Status == AreaContentRootStatus.MountFailed ? AreaHeroStatus.MountFailed : AreaHeroStatus.ModuleOutsideGameRoot, hero, found.Detail);
        var opened = ContentStack.Open(found.Plan!);
        if (opened.Status != ContentStackStatus.Ready) return Refused(AreaHeroStatus.StackFailed, hero, opened.Status.ToString());
        using var stack = opened.Stack!;
        // ARC-DATA-027: each table from the sources of the phase the original reads it in (exe-checked): the effect,
        // inventory-source, main-menu, level and quest-experience tables at game start (base sources only), the
        // module's quest table at module load.
        const ContentLoadPhase start = ContentLoadPhase.GameStart, module = ContentLoadPhase.ModuleLoaded;
        var effects = Table(stack, EffectsPath, start, required: true, out var detail);
        var sources = effects is null ? null : Table(stack, SourcesPath, start, required: true, out detail);
        var buy = sources is null ? null : Table(stack, SourcesBuyPath, start, required: false, out detail);
        var menu = sources is null || detail is not null ? null : Table(stack, MainMenuPath, start, required: true, out detail);
        var levels = menu is null ? null : Table(stack, LevelsPath, start, required: true, out detail);
        var quests = levels is null ? null : Table(stack, QuestExperiencePath, start, required: true, out detail);
        var questLevels = quests is null ? null : Table(stack, QuestLevelsPath, module, required: true, out detail);
        if (effects is null || sources is null || menu is null || questLevels is null || detail is not null)
            return Refused(AreaHeroStatus.TableUnavailable, hero, detail ?? "table");
        if (Experience(levels!, quests!, questLevels, out detail) is not { } experience) return Refused(AreaHeroStatus.ExperienceInvalid, hero, detail!);
        // ARC-WORLD-036: the scheme rules and spell records at game start, the module's own effects at module load.
        var game = Table(stack, AreaAdvancement.GameSchemesPath, start, required: true, out detail);
        var user = game is null ? null : Table(stack, AreaAdvancement.UserSchemesPath, start, required: true, out detail);
        var spells = user is null ? null : Table(stack, AreaAdvancement.SpellsPath, start, required: true, out detail);
        var moduleEffects = spells is null ? null : Table(stack, ModuleEffectsPath, module, required: false, out detail);
        if (spells is null || detail is not null) return Refused(AreaHeroStatus.TableUnavailable, hero, detail ?? "table");
        if (AreaAdvancement.Rules(game!, user!, spells, out detail) is not { } autoLevel) return Refused(AreaHeroStatus.AutoLevelInvalid, hero, detail!);
        EffectRule? Effect(int number)
        {
            if (number >= EffectRules.FirstNumber) return EffectRules.Read(effects!, number).Rule;
            if (moduleEffects?.Find(number) is not { } message || EffectRules.Parse(message.Bytes.Span, out var changes) != EffectRuleStatus.Parsed) return null;
            return new(number, true, changes);
        }
        // ARC-DATA-026: prototypes from the content stack's base sources, as the original reads them.
        var prototypes = ContentPrototypes.Open(stack);
        var read = PremadeHeroReader.Read(new(prototypes.Read, effects, sources, buy), hero.BasicPrototype);
        if (read.Failure != PremadeHeroFailure.None || read.Hero is null)
            return new(AreaHeroStatus.HeroRefused, hero, null, null, 0, read, $"{read.Failure} {read.Detail}");
        if (Flag(menu, hero) is not { } flag) return new(AreaHeroStatus.FlagInvalid, hero, null, null, 0, read, $"message {FirstFlagMessage + hero.Index}");
        var definition = Definition(read.Hero, out var dropped);
        if (KitWear(prototypes.Read, definition, out detail) is not { } wear) return new(AreaHeroStatus.KitUnreadable, hero, null, null, 0, read, detail!);
        return new(AreaHeroStatus.Ready, hero, definition, flag, dropped, read, "hero ready", experience, autoLevel, Effect, wear)
        {
            KitHealing = KitHealing(prototypes.Read, definition),
            KitLockpicks = KitLockpicks(prototypes.Read, definition),
            KitDurability = KitDurability(prototypes.Read, definition),
            KitArt = KitArt(prototypes.Read, definition),
            KitMissileArt = KitMissileArt(prototypes.Read, definition),
        };
    }

    /// <summary>
    /// ARC-WORLD-034: the wear facts of each kit item, from its prototype's combat fields (ARC-DATA-021) through
    /// <see cref="SessionEquipmentFacts.Wearable"/>; null for food, scrolls and the like. A prototype that does not read, or a weapon,
    /// armour or generic item whose values the wear rules refuse, is null with the reason.
    /// </summary>
    public static IReadOnlyList<SessionWearableItem?>? KitWear(Func<int, PrototypeFile> prototypes, SessionHeroDefinition definition, out string? detail)
    {
        ArgumentNullException.ThrowIfNull(prototypes);
        ArgumentNullException.ThrowIfNull(definition);
        detail = null;
        var wear = new List<SessionWearableItem?>();
        foreach (var item in definition.Kit)
        {
            var read = CombatFieldReader.ReadPrototype(prototypes, item.Kind.Prototype);
            if (read.Failure != CombatFieldFailure.None || read.Fields is not { } fields)
            {
                detail = $"kit prototype {item.Kind.Prototype} {read.Failure} {read.Detail}";
                return null;
            }
            var wearable = item.Kind.Type is SessionItemType.Weapon or SessionItemType.Armor or SessionItemType.Generic
                ? SessionEquipmentFacts.Wearable(item.Kind, fields) : null;
            if (wearable is null && item.Kind.Type is SessionItemType.Weapon or SessionItemType.Armor or SessionItemType.Generic)
            {
                detail = $"kit prototype {item.Kind.Prototype} wear values out of bounds";
                return null;
            }
            wear.Add(wearable);
        }
        return wear.AsReadOnly();
    }

    /// <summary>ARC-SKILLS-005: each kit item's healing-item facts (<see cref="AreaHealingItems.From"/>) under its kit entity, null for an
    /// item that is not a healing item or whose prototype does not read.</summary>
    public static IReadOnlyList<SessionHealingItem?> KitHealing(Func<int, PrototypeFile> prototypes, SessionHeroDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(prototypes);
        ArgumentNullException.ThrowIfNull(definition);
        return definition.Kit.Select((item, index) => CombatFieldReader.ReadPrototype(prototypes, item.Kind.Prototype).Fields is { } fields
            ? AreaHealingItems.From("kit:" + (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), fields) : null).ToArray();
    }

    /// <summary>ARC-SKILLS-006: each kit item's lockpick facts (<see cref="AreaLockpicks.From"/>) under its kit entity, null for an
    /// item that is not lockpicks or whose prototype does not read.</summary>
    public static IReadOnlyList<SessionLockpick?> KitLockpicks(Func<int, PrototypeFile> prototypes, SessionHeroDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(prototypes);
        ArgumentNullException.ThrowIfNull(definition);
        return definition.Kit.Select((item, index) => CombatFieldReader.ReadPrototype(prototypes, item.Kind.Prototype).Fields is { } fields
            ? AreaLockpicks.From("kit:" + (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), fields) : null).ToArray();
    }

    /// <summary>ARC-COMBAT-008: each kit item's durability values for the attack's wear under its kit entity
    /// (<see cref="SessionWearItem.FromCombatFields"/>), null where the prototype does not read.</summary>
    public static IReadOnlyList<SessionWearItem?> KitDurability(Func<int, PrototypeFile> prototypes, SessionHeroDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(prototypes);
        ArgumentNullException.ThrowIfNull(definition);
        return definition.Kit.Select((item, index) => CombatFieldReader.ReadPrototype(prototypes, item.Kind.Prototype).Fields is { } fields
            ? SessionWearItem.FromCombatFields("kit:" + (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), fields) : null).ToArray();
    }

    /// <summary>ARC-RENDER-033: each kit item's ground ART id from its prototype's combat fields, null where it does not read.</summary>
    public static IReadOnlyList<uint?> KitArt(Func<int, PrototypeFile> prototypes, SessionHeroDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(prototypes);
        ArgumentNullException.ThrowIfNull(definition);
        return definition.Kit.Select(item => CombatFieldReader.ReadPrototype(prototypes, item.Kind.Prototype).Fields is { } fields
            ? (uint?)fields.Object.CurrentArt : null).ToArray();
    }

    /// <summary>ARC-COMBAT-016: each kit weapon's projectile art from its prototype's combat fields, null where it does not read or is
    /// no weapon.</summary>
    public static IReadOnlyList<int?> KitMissileArt(Func<int, PrototypeFile> prototypes, SessionHeroDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(prototypes);
        ArgumentNullException.ThrowIfNull(definition);
        return definition.Kit.Select(item => CombatFieldReader.ReadPrototype(prototypes, item.Kind.Prototype).Fields?.Weapon is { } weapon
            ? (int?)weapon.MissileArt : null).ToArray();
    }

    /// <summary>The session's experience data from the three tables (ARC-WORLD-030 readers), or null with the reason.</summary>
    public static SessionExperienceRules? Experience(MesFile levels, MesFile questExperience, MesFile questLevels, out string? detail)
    {
        ArgumentNullException.ThrowIfNull(levels);
        ArgumentNullException.ThrowIfNull(questExperience);
        ArgumentNullException.ThrowIfNull(questLevels);
        var thresholds = ExperienceTables.ReadLevels(levels);
        var quest = ExperienceTables.ReadValues(questExperience);
        var questLevel = ExperienceTables.ReadQuestLevels(questLevels);
        detail = thresholds.Status != ExperienceTableStatus.Read ? $"{LevelsPath} {thresholds.Status} {thresholds.Number}"
            : quest.Status != ExperienceTableStatus.Read ? $"{QuestExperiencePath} {quest.Status} {quest.Number}"
            : questLevel.Status != ExperienceTableStatus.Read ? $"{QuestLevelsPath} {questLevel.Status} {questLevel.Number}" : null;
        if (detail is not null) return null;
        var rules = SessionExperienceRules.Create(ExperienceTable.Create(thresholds.Thresholds), quest.Values, questLevel.Values);
        if (rules is null) detail = $"{LevelsPath} not strictly increasing from 0, or a quest value or number out of range";
        return rules;
    }

    /// <summary>The original new-game flags (0..3199 known false, ARC-SCRIPT-006) with the chosen hero's flag set true, as the
    /// main menu does before the game starts (it also writes 0 to the other heroes' flags, which a new game already holds).</summary>
    public static GlobalFlagOwnerDescriptor NewGameFlags(GlobalFlagIdentity identity, OwnerEpoch epoch, int? heroFlag)
    {
        var flags = OriginalGlobalFlags.NewGame(identity, epoch);
        if (heroFlag is not { } chosen) return flags;
        if (chosen is < 0 or >= OriginalGlobalFlags.Count) throw new ArgumentOutOfRangeException(nameof(heroFlag));
        return flags with { Bootstrap = Array.AsReadOnly(flags.Bootstrap.Select(value => value.Flag.Value == chosen ? value with { Value = true } : value).ToArray()) };
    }

    // The original reads the message with atoi and writes any result that is not negative (flag 0 included, checked in
    // the executable at 0x545B60, docs/world/starting-hero-runtime.md). Project admission: one to five ASCII digits, optionally space-padded, inside the
    // new-game domain; an absent or other message is refused instead of atoi's partial or zero value.
    private static int? Flag(MesFile menu, AreaPremadeHero hero)
    {
        if (menu.Find(FirstFlagMessage + hero.Index) is not { } message) return null;
        var text = message.Bytes.Span.Trim((byte)' ');
        if (text.Length is 0 or > 5) return null;
        var value = 0;
        foreach (var b in text)
        {
            if (b is < (byte)'0' or > (byte)'9') return null;
            value = value * 10 + (b - '0');
        }
        return value < OriginalGlobalFlags.Count ? value : null;
    }

    /// <summary>One rule table from the candidate of its load phase; a table only another phase's sources hold is "NotInPhase".</summary>
    internal static MesFile? Table(ContentStack stack, string path, ContentLoadPhase phase, bool required, out string? detail)
    {
        detail = null;
        var resolution = stack.Resolve(path);
        if (ContentPhase.Candidate(resolution, phase) is not { } winner)
        {
            if (required) detail = $"{path} {(resolution.Status == ContentResolutionStatus.Resolved ? "NotInPhase" : resolution.Status)}";
            return null;
        }
        var bytes = stack.Read(winner);
        if (bytes.Status != ContentReadStatus.Read || bytes.Bytes is null) { detail = $"{path} {bytes.Status}"; return null; }
        var parsed = MesFile.Parse(bytes.Bytes);
        if (parsed.Status != MesParseStatus.Parsed || parsed.File is null) { detail = $"{path} {parsed.Status}"; return null; }
        return parsed.File;
    }

    private static AreaHeroLoad Refused(AreaHeroStatus status, AreaPremadeHero hero, string detail) => new(status, hero, null, null, 0, null, detail);

    private static readonly CharacterRace[] Races =
    [
        CharacterRace.Human, CharacterRace.Dwarf, CharacterRace.Elf, CharacterRace.HalfElf, CharacterRace.Gnome, CharacterRace.Halfling,
        CharacterRace.HalfOrc, CharacterRace.HalfOgre, CharacterRace.DarkElf, CharacterRace.Ogre, CharacterRace.Orc,
    ];

    // Source stat index → owner stat: 0..7 primary, 8..16 derived, 22 magick points, 23 tech points.
    private static CharacterStat? Stat(int source) => source switch
    {
        0 => CharacterStat.Strength, 1 => CharacterStat.Dexterity, 2 => CharacterStat.Constitution, 3 => CharacterStat.Beauty,
        4 => CharacterStat.Intelligence, 5 => CharacterStat.Perception, 6 => CharacterStat.Willpower, 7 => CharacterStat.Charisma,
        8 => CharacterStat.CarryWeight, 9 => CharacterStat.DamageBonus, 10 => CharacterStat.AcAdjustment, 11 => CharacterStat.Speed,
        12 => CharacterStat.HealRate, 13 => CharacterStat.PoisonRecovery, 14 => CharacterStat.ReactionModifier,
        15 => CharacterStat.MaxFollowers, 16 => CharacterStat.MagickTechAptitude, 22 => CharacterStat.MagickPoints,
        23 => CharacterStat.TechPoints, _ => null,
    };

    /// <summary>
    /// The host mapping from a pre-made hero (Arcanum.Data) to the session's hero definition (moved here from the
    /// ARC-WORLD-015 real-data test). Level-1 defaults per <see cref="PremadeHero"/>; effect changes on stats the character
    /// owner models (primary, derived, magick and tech points) are kept, and since ARC-SKILLS-002 the changes on basic and tech
    /// skills too, as the hero's innate skill changes; the others (resistances, tech disciplines, special values, level,
    /// alignment, age …) are dropped and counted in <paramref name="dropped"/>.
    /// </summary>
    public static SessionHeroDefinition Definition(PremadeHero hero, out int dropped)
    {
        ArgumentNullException.ThrowIfNull(hero);
        var count = 0;
        var skills = new List<SessionInnateSkillChange>();
        static StatModifierOperator Operator(EffectChangeOperator op) => op switch
        {
            EffectChangeOperator.Add => StatModifierOperator.Add, EffectChangeOperator.Percent => StatModifierOperator.Percent,
            EffectChangeOperator.Minimum => StatModifierOperator.Minimum, EffectChangeOperator.Maximum => StatModifierOperator.Maximum,
            EffectChangeOperator.Multiply => StatModifierOperator.Multiply, EffectChangeOperator.Divide => StatModifierOperator.Divide,
            _ => throw new ArgumentOutOfRangeException(nameof(op)),
        };
        StatEffectDefinition? Effect(EffectRule? rule, EffectCause cause)
        {
            if (rule is null) return null;
            var modifiers = new List<StatModifier>();
            foreach (var change in rule.Changes)
            {
                if (change.Kind is EffectAttributeKind.BasicSkill or EffectAttributeKind.TechSkill)
                {
                    var tech = change.Kind == EffectAttributeKind.TechSkill;
                    if (change.Attribute >= 0 && change.Attribute < (tech ? SkillRules.TechCount : SkillRules.BasicCount))
                    {
                        skills.Add(new(cause, tech ? SkillRules.Tech(change.Attribute) : SkillRules.Basic(change.Attribute), Operator(change.Operator), change.Value));
                        continue;
                    }
                }
                if (change.Kind != EffectAttributeKind.Stat || Stat(change.Attribute) is not { } stat)
                {
                    count++;
                    continue;
                }
                modifiers.Add(new(stat, Operator(change.Operator), change.Value));
            }
            return modifiers.Count == 0 ? null : new(rule.Number, [.. modifiers]);
        }
        var raceEffect = Effect(hero.RaceEffect, EffectCause.Race);
        var genderEffect = Effect(hero.GenderEffect, EffectCause.Gender);
        dropped = count;
        return new(hero.Prototype, Races[hero.Race], hero.Gender == 0 ? CharacterGender.Female : CharacterGender.Male,
            [.. Enumerable.Repeat(PremadeHero.StartPrimaryStat, 8), 0, 0], PremadeHero.StartLevel, PremadeHero.StartUnspentPoints, hero.Alignment,
            raceEffect, genderEffect, hero.Gold,
            [.. hero.Items.Select(item => new SessionHeroKitItem(new((SessionItemType)item.Type, item.Prototype, item.Name, item.Description))
                { Weight = new(item.Weight, item.MagicWeight, item.Complexity) })],
            Advancement: SessionHeroAdvancement.WithScheme(hero.AutoLevelScheme is >= 0 and <= SessionAdvancementLimits.MaxScheme ? hero.AutoLevelScheme : 0),
            // A scheme or background outside the session's range starts as 0 (none), as the pre-made data never has one (39..50 installed).
            InnateSkills: skills, Background: hero.Background is >= SessionHeroLimits.MinBackground and <= SessionHeroLimits.MaxBackground ? hero.Background : 0);
    }

    /// <summary>A one-line HUD summary of the session's hero (no original text), or "diagnostic PC" without one.</summary>
    public static string Summary(GameSessionOwner session)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (session.QueryHero() is not { } hero) return "hero: none (diagnostic PC profile)";
        return $"hero: {Name(hero)} level {hero.Level} ({hero.UnspentPoints} points unspent), gold {session.QueryGold()}, kit {session.QueryHeroKit().Count}, worn {session.QueryEquipment().Worn.Count}";
    }

    /// <summary>The project's name of a session hero (the pre-made hero whose prototype it has), else its prototype number.
    /// The session holds no player name.</summary>
    public static string Name(SessionHeroState hero)
    {
        ArgumentNullException.ThrowIfNull(hero);
        return Premade.FirstOrDefault(p => p.BasicPrototype + PremadeHeroReader.PrototypeOffset == hero.Prototype)?.Name ?? $"prototype {hero.Prototype}";
    }
}
