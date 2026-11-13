using Arcanum.Data.Characters;
using Arcanum.Data.Combat;
using Arcanum.Data.Content;
using Arcanum.Data.Messages;
using Arcanum.Scripting.GlobalFlags;
using Arcanum.Session;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Equipment;
using Arcanum.Simulation.Skills;
using Arcanum.Simulation;

namespace Arcanum.World;

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

    /// <summary>ARC-COMBAT-017: the stored weight of each kit weapon that "default throws" (weapon flag 0x200, the throwing dagger), by
    /// kit index; null for any other item or a prototype that does not read. An attack with it throws it (ledger RH2).</summary>
    public IReadOnlyList<int?>? KitThrowWeight { get; init; }

    /// <summary>ARC-RENDER-033: each kit item's ground ART id (its prototype's current art, field 0), by kit index; null for a
    /// prototype that does not read. The viewer draws a placed kit item with it.</summary>
    public IReadOnlyList<uint?>? KitArt { get; init; }

    /// <summary>ARC-UI-016: each kit item's spell facts (its spell fields, charges, complexity and item flags) under its kit entity, by
    /// kit index; null for a prototype that does not read or is no item. The cast host knows the hero's items through them.</summary>
    public IReadOnlyList<SessionItemSpells?>? KitItemSpells { get; init; }

    /// <summary>ARC-UI-019: each kit item's facts for a move in the hero's grid, by kit index; null for a prototype that does not read.</summary>
    public IReadOnlyList<SessionGridMoveItem?>? KitGridMove { get; init; }

    /// <summary>ARC-UI-025: each kit item's use facts under its kit entity, by kit index; null for a prototype that does not read or is no
    /// item.</summary>
    public IReadOnlyList<AreaItemUseFacts?>? KitUse { get; init; }

    /// <summary>ARC-UI-030: for <see cref="AreaStartingHero.NewCharacter"/>, the background table the new-character screen browses
    /// (<see cref="BackgroundTable"/>, ARC-DATA-044); null for a pre-made hero or when it does not read
    /// (<see cref="BackgroundsDetail"/> says why).</summary>
    public BackgroundTable? Backgrounds { get; init; }

    /// <summary>ARC-UI-030: why <see cref="Backgrounds"/> is null for the new character, else null.</summary>
    public string? BackgroundsDetail { get; init; }

    /// <summary>ARC-UI-036: for <see cref="AreaStartingHero.NewCharacter"/>, the prototype files (by file number) the opening shop's kit
    /// can make, read once from the base sources: every background's items, the clothes, the jackets, the lockpicks and the bandages
    /// (<see cref="OpeningKitRules"/>); any other number answers missing. Null for a pre-made hero.</summary>
    public Func<int, PrototypeFile>? KitPrototypes { get; init; }

    /// <summary>ARC-UI-033: for <see cref="AreaStartingHero.NewCharacter"/>, the portraits the new-character screen browses
    /// (<see cref="PortraitCatalogue"/>, ARC-DATA-045); null for a pre-made hero or when it does not read (<see cref="PortraitsDetail"/>).</summary>
    public PortraitCatalogue? Portraits { get; init; }

    /// <summary>ARC-UI-033: why <see cref="Portraits"/> is null for the new character, else null.</summary>
    public string? PortraitsDetail { get; init; }
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
    /// <summary>ARC-UI-030: the generic PC prototype the new-character screen makes its PC from (human, male, background 0; ledger NW1).</summary>
    public const int GenericPrototype = 16066;
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

    /// <summary>ARC-UI-030: the new character's start: the generic prototype, read as a pre-made hero is (no main-menu flag, no kit);
    /// the screen then sets its race, gender and background (<see cref="NewCharacterHero"/>). Not one of <see cref="Premade"/>.</summary>
    public static AreaPremadeHero NewCharacter { get; } = new("new-character", GenericPrototype);

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
        var opened = ContentStack.OpenShared(found.Plan!);
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
        // ARC-UI-030: the new character has no main-menu flag (only the pre-made step sets one, 0x545B60) and brings the background
        // table its screen browses, read at game start from the base sources (BD1).
        int? flag = null;
        BackgroundTable? backgrounds = null;
        string? backgroundsDetail = null;
        PortraitCatalogue? portraits = null;
        string? portraitsDetail = null;
        Func<int, PrototypeFile>? kitPrototypes = null;
        if (hero.BasicPrototype == GenericPrototype)
        {
            var table = BackgroundTable.Load(stack);
            backgrounds = table.Table;
            if (backgrounds is null) backgroundsDetail = $"{table.Status} {table.File}";
            // ARC-UI-033: and the portraits its portrait buttons browse, read at game start from the base sources (PO1).
            var portraitLoad = PortraitCatalogue.Load(stack);
            portraits = portraitLoad.Catalogue;
            if (portraits is null) portraitsDetail = $"{portraitLoad.Status} {portraitLoad.File}";
            // ARC-UI-036: and the prototypes its opening shop's kit makes, read now (the stack closes with this load).
            kitPrototypes = KitPrototypeCache(prototypes.Read, backgrounds);
        }
        else if (Flag(menu, hero) is not { } premadeFlag) return new(AreaHeroStatus.FlagInvalid, hero, null, null, 0, read, $"message {FirstFlagMessage + hero.Index}");
        else flag = premadeFlag;
        var definition = Definition(read.Hero, out var dropped);
        if (KitWear(prototypes.Read, definition, out detail) is not { } wear) return new(AreaHeroStatus.KitUnreadable, hero, null, null, 0, read, detail!);
        return new(AreaHeroStatus.Ready, hero, definition, flag, dropped, read, "hero ready", experience, autoLevel, Effect, wear)
        {
            KitHealing = KitHealing(prototypes.Read, definition),
            KitLockpicks = KitLockpicks(prototypes.Read, definition),
            KitDurability = KitDurability(prototypes.Read, definition),
            KitArt = KitArt(prototypes.Read, definition),
            KitMissileArt = KitMissileArt(prototypes.Read, definition),
            KitThrowWeight = KitThrowWeight(prototypes.Read, definition),
            KitItemSpells = KitItemSpells(prototypes.Read, definition),
            KitGridMove = KitGridMove(prototypes.Read, definition),
            KitUse = KitUse(prototypes.Read, definition),
            Backgrounds = backgrounds,
            BackgroundsDetail = backgroundsDetail,
            Portraits = portraits,
            PortraitsDetail = portraitsDetail,
            KitPrototypes = kitPrototypes,
        };
    }

    /// <summary>ARC-UI-036: the files <see cref="AreaHeroLoad.KitPrototypes"/> answers, read through <paramref name="read"/> while the
    /// stack is open: the items of every block of <paramref name="backgrounds"/> that reads (none when the table did not read), the six
    /// clothes, the three jackets, the lockpicks and the bandages.</summary>
    private static Func<int, PrototypeFile> KitPrototypeCache(Func<int, PrototypeFile> read, BackgroundTable? backgrounds)
    {
        var basics = new SortedSet<int> { OpeningKitRules.Lockpicks, OpeningKitRules.Bandages };
        foreach (var size in new[] { ArmorSizes.Small, ArmorSizes.Medium, ArmorSizes.Large })
        {
            basics.Add(OpeningKitRules.Clothes(CharacterGender.Male, size)!.Value);
            basics.Add(OpeningKitRules.Clothes(CharacterGender.Female, size)!.Value);
            basics.Add(OpeningKitRules.Jacket(size)!.Value);
        }
        if (backgrounds is not null)
            for (var block = 0; block < backgrounds.Count; block++)
                if (backgrounds.Block(block).Block is { } listed) basics.UnionWith(listed.Items);
        var files = new Dictionary<int, PrototypeFile>();
        foreach (var basic in basics)
            if (basic is >= 0 and <= 999_999 - PremadeHeroReader.PrototypeOffset) files[basic + PremadeHeroReader.PrototypeOffset] = read(basic + PremadeHeroReader.PrototypeOffset);
        var missing = new PrototypeFile(PrototypeFileStatus.Missing, null);
        return number => files.GetValueOrDefault(number, missing);
    }

    /// <summary>
    /// ARC-UI-036: the new character's hero (<see cref="NewCharacterHero"/>) with the opening shop's kit: <paramref name="gold"/> and the
    /// items of <paramref name="items"/> (basic prototype numbers in creation order, <see cref="OpeningKitRules.Items"/>) read from
    /// <see cref="AreaHeroLoad.KitPrototypes"/>, with the kit's tables (wear, healing, lockpicks, durability, art, item spells, grid,
    /// use) built from the same files. An item whose prototype is missing is skipped, as the original's failed creation skips it
    /// (<paramref name="skipped"/> counts them); any other refusal of the item reader fails it. Null with the reason when the load is not
    /// the new character's or an item is refused.
    /// </summary>
    public static AreaHeroLoad? WithKit(AreaHeroLoad hero, int gold, IReadOnlyList<int> items, out int skipped, out string? detail)
    {
        ArgumentNullException.ThrowIfNull(hero);
        ArgumentNullException.ThrowIfNull(items);
        skipped = 0;
        detail = null;
        if (hero is not { Status: AreaHeroStatus.Ready, Definition: { } definition, KitPrototypes: { } prototypes } || hero.Hero.BasicPrototype != GenericPrototype)
        {
            detail = "not the new character's ready load";
            return null;
        }
        if (gold < 0) { detail = $"gold {gold} is negative"; return null; }
        var kit = new List<SessionHeroKitItem>();
        foreach (var basic in items)
        {
            var failure = PremadeHeroReader.ReadItem(prototypes, basic, out var item, out var number);
            if (failure == PremadeHeroFailure.ItemPrototypeMissing) { skipped++; continue; }
            if (failure != PremadeHeroFailure.None) { detail = $"kit prototype {number} {failure}"; return null; }
            kit.Add(new(new((SessionItemType)item!.Type, item.Prototype, item.Name, item.Description)) { Weight = new(item.Weight, item.MagicWeight, item.Complexity) });
        }
        return Tables(hero, prototypes, definition with { Gold = gold, Kit = kit.AsReadOnly() }, out detail);
    }

    /// <summary>
    /// ARC-UI-036: the new character's tables for the kit items a session knows (<see cref="GameSessionOwner.QueryKitItemsAnywhere"/>:
    /// held, placed on the ground or given), by kit number: a map transfer or a load reads the generic start again, whose kit is empty,
    /// so its tables are rebuilt from the session's own kit items (their prototypes from <see cref="AreaHeroLoad.KitPrototypes"/>). A
    /// number not listed (a destroyed item) has no facts. The load itself when it is not the new character's or the list is empty; null
    /// with the reason when an item's facts do not read.
    /// </summary>
    public static AreaHeroLoad? WithSessionKit(AreaHeroLoad hero, IReadOnlyList<SessionKitItem> kit, out string? detail)
    {
        ArgumentNullException.ThrowIfNull(hero);
        ArgumentNullException.ThrowIfNull(kit);
        detail = null;
        if (hero is not { Status: AreaHeroStatus.Ready, Definition: { } definition, KitPrototypes: { } prototypes } || kit.Count == 0) return hero;
        var numbered = new Dictionary<int, SessionItemKind>();
        foreach (var item in kit)
        {
            if (KitIndex(item.Entity) is not { } index || index >= SessionKitLimit) { detail = $"kit item {item.Entity} has no kit number"; return null; }
            numbered[index] = item.Kind;
        }
        var count = numbered.Keys.Max() + 1;
        // A number the session no longer holds takes another item's kind, so the tables keep their positions; its facts are dropped.
        var filler = numbered[numbered.Keys.Min()];
        var full = Enumerable.Range(0, count).Select(i => new SessionHeroKitItem(numbered.GetValueOrDefault(i, filler))).ToArray();
        if (Tables(hero, prototypes, definition with { Kit = full }, out detail) is not { } tables) return null;
        IReadOnlyList<T?>? Held<T>(IReadOnlyList<T?>? list) => list?.Select((value, i) => numbered.ContainsKey(i) ? value : default).ToArray();
        return tables with
        {
            KitWear = Held(tables.KitWear), KitHealing = Held(tables.KitHealing), KitLockpicks = Held(tables.KitLockpicks),
            KitDurability = Held(tables.KitDurability), KitArt = Held(tables.KitArt), KitMissileArt = Held(tables.KitMissileArt),
            KitThrowWeight = Held(tables.KitThrowWeight), KitItemSpells = Held(tables.KitItemSpells), KitGridMove = Held(tables.KitGridMove),
            KitUse = Held(tables.KitUse),
        };
    }

    // The most kit numbers a session's kit is rebuilt for (the pre-made kits and the opening shop's hold a handful).
    private const int SessionKitLimit = 4096;

    private static AreaHeroLoad? Tables(AreaHeroLoad hero, Func<int, PrototypeFile> prototypes, SessionHeroDefinition definition, out string? detail)
    {
        if (KitWear(prototypes, definition, out detail) is not { } wear) return null;
        return hero with
        {
            Definition = definition, KitWear = wear,
            KitHealing = KitHealing(prototypes, definition),
            KitLockpicks = KitLockpicks(prototypes, definition),
            KitDurability = KitDurability(prototypes, definition),
            KitArt = KitArt(prototypes, definition),
            KitMissileArt = KitMissileArt(prototypes, definition),
            KitThrowWeight = KitThrowWeight(prototypes, definition),
            KitItemSpells = KitItemSpells(prototypes, definition),
            KitGridMove = KitGridMove(prototypes, definition),
            KitUse = KitUse(prototypes, definition),
        };
    }

    // kit:1 is the first kit item.
    internal static int? KitIndex(string entity) =>
        entity.StartsWith("kit:", StringComparison.Ordinal) && int.TryParse(entity.AsSpan(4), System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture, out var number) && number >= 1 ? number - 1 : null;

    /// <summary>
    /// ARC-UI-030: the new character as the screen leaves it: the generic hero's load (<see cref="NewCharacter"/>, ready) with the
    /// chosen race and gender, whose setters replace the race effect with 64 + race and add the female effect 330 for a woman
    /// (ledger NW3), as the pre-made reader reads them. Null with the reason when the load is not the new character's, the pair is not
    /// one the screen makes (<see cref="CharacterCreationRules.Allows"/>) or an effect does not read.
    /// </summary>
    /// <param name="portrait">ARC-UI-033: the portrait the screen chose (field 0xE7); null keeps the generic prototype's.</param>
    public static AreaHeroLoad? NewCharacterHero(AreaHeroLoad generic, CharacterRace race, CharacterGender gender, out string? detail, int? portrait = null)
    {
        ArgumentNullException.ThrowIfNull(generic);
        detail = null;
        if (generic is not { Status: AreaHeroStatus.Ready, Read.Hero: { } read, Effects: { } effects } || read.BasicPrototype != GenericPrototype)
        {
            detail = "not the new character's ready load";
            return null;
        }
        if (!CharacterCreationRules.Allows(race, gender)) { detail = $"{race} {gender} is not offered"; return null; }
        if (effects(EffectRules.RaceEffectBase + (int)race) is not { } raceEffect) { detail = $"effect {EffectRules.RaceEffectBase + (int)race} does not read"; return null; }
        EffectRule? genderEffect = null;
        if (gender == CharacterGender.Female && (genderEffect = effects(EffectRules.FemaleEffect)) is null) { detail = $"effect {EffectRules.FemaleEffect} does not read"; return null; }
        if (portrait < 0) { detail = $"portrait {portrait} is negative"; return null; }
        var hero = read with { Race = (int)race, Gender = gender == CharacterGender.Female ? 0 : 1, RaceEffect = raceEffect, GenderEffect = genderEffect,
            Portrait = portrait ?? read.Portrait };
        return generic with { Definition = Definition(hero, out var dropped), DroppedChanges = dropped };
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

    /// <summary>ARC-UI-016: each kit item's spell facts held by the hero under its kit entity
    /// (<see cref="SessionItemSpells.FromCombatFields"/>), null where the prototype does not read or is no item.</summary>
    public static IReadOnlyList<SessionItemSpells?> KitItemSpells(Func<int, PrototypeFile> prototypes, SessionHeroDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(prototypes);
        ArgumentNullException.ThrowIfNull(definition);
        return definition.Kit.Select((item, index) => CombatFieldReader.ReadPrototype(prototypes, item.Kind.Prototype).Fields is { } fields
            ? SessionItemSpells.FromCombatFields("kit:" + (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), SessionSpellCaster.Hero, fields)
            : null).ToArray();
    }

    /// <summary>ARC-UI-019: each kit item's facts for a move in the hero's grid (its kind, the no-drop item flag 0x20 and its script points)
    /// from its prototype's combat fields, null where it does not read.</summary>
    public static IReadOnlyList<SessionGridMoveItem?> KitGridMove(Func<int, PrototypeFile> prototypes, SessionHeroDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(prototypes);
        ArgumentNullException.ThrowIfNull(definition);
        return definition.Kit.Select(item => CombatFieldReader.ReadPrototype(prototypes, item.Kind.Prototype).Fields is { } fields
            ? AreaInventoryHost.GridMoveFacts(item.Kind, fields) : null).ToArray();
    }

    /// <summary>ARC-UI-025: each kit item's use facts (<see cref="AreaItemUseFacts.From"/>) under its kit entity from its prototype's combat
    /// fields, null where it does not read or is no item.</summary>
    public static IReadOnlyList<AreaItemUseFacts?> KitUse(Func<int, PrototypeFile> prototypes, SessionHeroDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(prototypes);
        ArgumentNullException.ThrowIfNull(definition);
        return definition.Kit.Select((item, index) => CombatFieldReader.ReadPrototype(prototypes, item.Kind.Prototype).Fields is { } fields
            ? AreaItemUseFacts.From("kit:" + (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), fields) : null).ToArray();
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

    /// <summary>ARC-COMBAT-017: each kit weapon's stored weight when it carries the "default throws" flag, null otherwise (the magic weight
    /// adjustment is not read, as for <see cref="SessionEquipmentFacts.Wearable"/>).</summary>
    public static IReadOnlyList<int?> KitThrowWeight(Func<int, PrototypeFile> prototypes, SessionHeroDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(prototypes);
        ArgumentNullException.ThrowIfNull(definition);
        return definition.Kit.Select(item => CombatFieldReader.ReadPrototype(prototypes, item.Kind.Prototype).Fields is { Weapon: { } weapon, Item: { } stored }
            && weapon.Flags.HasFlag(CombatWeaponFlags.DefaultThrows) ? (int?)stored.Weight : null).ToArray();
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
    [OriginalFunction(0x545B60, OriginalFunctionScope.Partial)]
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

    private static StatModifierOperator Operator(EffectChangeOperator op) => op switch
    {
        EffectChangeOperator.Add => StatModifierOperator.Add, EffectChangeOperator.Percent => StatModifierOperator.Percent,
        EffectChangeOperator.Minimum => StatModifierOperator.Minimum, EffectChangeOperator.Maximum => StatModifierOperator.Maximum,
        EffectChangeOperator.Multiply => StatModifierOperator.Multiply, EffectChangeOperator.Divide => StatModifierOperator.Divide,
        _ => throw new ArgumentOutOfRangeException(nameof(op)),
    };

    /// <summary>ARC-UI-068: an effect rule's changes on the stats the character owner models (as <see cref="Definition"/> keeps a
    /// pre-made hero's innate effects), or null when it has none: the stat part of a background's effect.</summary>
    public static StatEffectDefinition? StatEffect(EffectRule? rule)
    {
        if (rule is null) return null;
        StatModifier[] modifiers = [.. rule.Changes.Where(change => change.Kind == EffectAttributeKind.Stat && Stat(change.Attribute) is not null)
            .Select(change => new StatModifier(Stat(change.Attribute)!.Value, Operator(change.Operator), change.Value))];
        return modifiers.Length == 0 ? null : new(rule.Number, [.. modifiers]);
    }

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
    /// skills too, as the hero's innate skill changes; since ARC-MAGIC-1700 the resistance changes are read from the attached innate rules
    /// (not dropped); the others (tech disciplines, special values, level, alignment, age …) are dropped and counted in <paramref name="dropped"/>.
    /// </summary>
    public static SessionHeroDefinition Definition(PremadeHero hero, out int dropped)
    {
        ArgumentNullException.ThrowIfNull(hero);
        var count = 0;
        var skills = new List<SessionInnateSkillChange>();
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
                // ARC-MAGIC-1700: a resistance change of an innate effect is read from the attached rule of the effect when the hero's resistances are (GameSessionOwner.AttachInnateEffects), so it is not dropped.
                if (change.Kind == EffectAttributeKind.Resistance && change.Attribute is >= 0 and <= 4) continue;
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
            InnateSkills: skills, Background: hero.Background is >= SessionHeroLimits.MinBackground and <= SessionHeroLimits.MaxBackground ? hero.Background : 0)
        {
            // ARC-UI-033: the prototype's portrait (field 0xE7); a negative one (never installed) starts as 0, as the portrait code refuses it (PO2).
            Portrait = Math.Max(hero.Portrait, 0),
            // ARC-UI-068: the prototype's background description (field 0x102, MW19): a pre-made hero's story.
            BackgroundDescription = hero.BackgroundDescription,
            // ARC-UI-069: the prototype's player name (field 0x10E, MW21), the name the browser shows and the save info keeps; one the
            // session would refuse (longer than its bound, never installed) starts empty rather than refusing the hero.
            PlayerName = SessionHeroLimits.ValidPlayerName(hero.PlayerName) ? hero.PlayerName : [],
            // ARC-MAGIC-1658: the prototype's age (stat 25), which the TraitIdx component adds to; a negative one (never installed) is not kept.
            Age = hero.Age >= 0 ? hero.Age : null,
        };
    }

    /// <summary>A one-line HUD summary of the session's hero (no original text), or "diagnostic PC" without one.</summary>
    public static string Summary(GameSessionOwner session)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (session.QueryHero() is not { } hero) return "hero: none (diagnostic PC profile)";
        return $"hero: {Name(hero)} level {hero.Level} ({hero.UnspentPoints} points unspent), gold {session.QueryGold()}, kit {session.QueryHeroKit().Count}, worn {session.QueryEquipment().Worn.Count}";
    }

    /// <summary>The session hero's player name when it has one (ARC-UI-033), else the project's name of the pre-made hero whose
    /// prototype it has, else its prototype number.</summary>
    public static string Name(SessionHeroState hero)
    {
        ArgumentNullException.ThrowIfNull(hero);
        // ARC-UI-033: the player name the new-character window stored, decoded as the game's texts are.
        if (!hero.PlayerName.IsEmpty && MesText.Decode(hero.PlayerName.AsSpan(), AreaHoverIndex.TextProfile) is { Status: MesDecodeStatus.Decoded, Text: { } name })
            return name;
        return Premade.FirstOrDefault(p => p.BasicPrototype + PremadeHeroReader.PrototypeOffset == hero.Prototype)?.Name ?? $"prototype {hero.Prototype}";
    }
}
