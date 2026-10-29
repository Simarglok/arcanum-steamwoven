using System.Globalization;
using Arcanum.Data.Messages;
using Arcanum.Data.Objects;
using Arcanum.Data.StaticObjects;

namespace Arcanum.Data.Characters;

public enum PrototypeFileStatus { Found, Missing, Ambiguous, TooLarge, Unreadable }

/// <summary>A prototype file read by number (the file name's six digits, the basic prototype number + 20).</summary>
public sealed record PrototypeFile(PrototypeFileStatus Status, byte[]? Bytes);

/// <summary>Inputs of <see cref="PremadeHeroReader"/>: a prototype lookup and the three rule tables (effects
/// <c>rules\effect.mes</c>, inventory sources <c>rules\InvenSource.mes</c> and, when present, <c>rules\InvenSourceBuy.mes</c>).</summary>
public sealed record PremadeHeroSources(Func<int, PrototypeFile> Prototype, MesFile Effects, MesFile InventorySources,
    MesFile? InventorySourcesBuy);

public enum PremadeHeroFailure
{
    None,
    PrototypeMissing,
    PrototypeAmbiguous,
    /// <summary>Too large, unreadable, not a complete v119 prototype record, or its number is not the file's.</summary>
    PrototypeInvalid,
    NotPcPrototype,
    InvalidRace,
    InvalidGender,
    /// <summary>State a new PC drops or that is not modelled: skills, experience, inventory, a gold object, bank money,
    /// an effect whose cause is neither race nor gender (<see cref="PremadeHeroRead.Detail"/> names the effect), or more
    /// than one stored effect of one of those causes (the stat setter removes only one per cause).</summary>
    UnsupportedPrototypeState,
    /// <summary>A race or gender effect the effect rules refuse (<see cref="PremadeHeroRead.EffectStatus"/>).</summary>
    EffectRefused,
    /// <summary>The inventory source set is refused (<see cref="PremadeHeroRead.SourceStatus"/>).</summary>
    InventorySourceRefused,
    /// <summary>A coin range (minimum ≠ maximum) or an item rate other than 100. A range or a rate 1..99 needs gameplay
    /// randomness, which is not modelled (FR-CONTENT-007); rate 0 never creates the item and is refused with them.</summary>
    RandomInventorySource,
    /// <summary>Kept for compatibility: since ARC-WORLD-093 a kit item without a prototype file empties the set
    /// (<see cref="InventorySourceRefused"/> with <see cref="InventorySourceStatus.Emptied"/>), so this is not reached.</summary>
    ItemPrototypeMissing,
    ItemPrototypeAmbiguous,
    ItemPrototypeInvalid,
    /// <summary>A kit item whose prototype attaches a <c>SAP_GET</c> or <c>SAP_INSERT_ITEM</c> script, which the source's
    /// transfer to the PC runs and this reader does not.</summary>
    ItemScriptUnsupported,
    /// <summary>A listed prototype that is not an item (types 5..14) or is gold, which the PC holds as coins.</summary>
    ItemTypeUnsupported,
}

/// <summary>A starting-kit item: basic prototype, prototype (file) number, object type (5..14), numeric name class
/// (<c>OBJ_F_NAME</c>) and description number, all from the item prototype.</summary>
public sealed record PremadeHeroItem(int BasicPrototype, int Prototype, int Type, int Name, int Description)
{
    /// <summary>ARC-WORLD-085: the prototype's weight (field 0x59), magic weight adjustment (field 0x5A) and magick/tech complexity
    /// (field 0x60), which the kit item, created from it, keeps.</summary>
    public int Weight { get; init; }
    public int MagicWeight { get; init; }
    public int Complexity { get; init; }
}

/// <summary>
/// One pre-made hero at the moment the new game starts (ARC-RES-027). Race, gender, alignment and age are the prototype's
/// stored values, which <c>level_set(pc, 1)</c> keeps; every other stat is the source default it writes: the eight primary
/// stats 8, level 1, 5 unspent points, magick and tech points 0 (<see cref="StartPrimaryStats"/> etc.). The auto-level
/// scheme (<see cref="AutoLevelScheme"/>) that then spends the points is NOT applied. <see cref="RaceEffect"/> is effect
/// 64 + race and <see cref="GenderEffect"/> effect 330 for a woman, the innate effects the stat setter re-adds. Gold and
/// items come from the inventory source set; the source then transfers each item to the PC, whose insert check (weight,
/// room) is NOT evaluated here. The stored background is display data and adds no effect. The pre-made hero global flags
/// the main menu sets (2350..2361, ARC-RES-025 CC-07) are not read: the host sets them.
/// </summary>
public sealed record PremadeHero(int BasicPrototype, int Prototype, int Race, int Gender, int Alignment, int Age, int Portrait,
    int Background, int AutoLevelScheme, EffectRule RaceEffect, EffectRule? GenderEffect, int InventorySource, int Gold,
    IReadOnlyList<PremadeHeroItem> Items)
{
    public const int StartPrimaryStat = 8, StartLevel = 1, StartUnspentPoints = 5;
    /// <summary>ARC-WORLD-101: the prototype's player name (field 0x10E, the PC's only string field, v119 slot 86) as its stored bytes up
    /// to the first zero (where the original's string ends); empty when the field is unset. The generated-once seed reads it (0x441B60,
    /// ledger RK10).</summary>
    public IReadOnlyList<byte> PlayerName { get; init; } = Array.Empty<byte>();

    /// <summary>ARC-UI-066: the prototype's background description number (field 0x102, v119 slot 74; BG1), the <c>mes\gameback.mes</c>
    /// text the pre-made browser shows (the built-in heroes carry 1039..1050); 0 when unset.</summary>
    public int BackgroundDescription { get; init; }
}

/// <param name="Detail">The effect or prototype number the failure is about, when it has one.</param>
public sealed record PremadeHeroRead(PremadeHeroFailure Failure, PremadeHero? Hero, int? Detail = null,
    EffectRuleStatus? EffectStatus = null, InventorySourceStatus? SourceStatus = null);

/// <summary>
/// ARC-WORLD-015: the Godot-free reader of one pre-made PC prototype into its new-game start (ARC-RES-027 proposal 2).
/// Field slots are the v119 layout of <see cref="StaticObjectLoader"/> (the ARC-RES-027 probe's slot map): stat array 38,
/// basic and tech skills 39/40, effects and causes 46/47, portrait 49, gold handle 50, inventory count/list/source 55/56/57,
/// auto-level scheme 63, background 73, background description 74 (ARC-UI-066), player name 86 (ARC-WORLD-101), bank money 87. Read-only and fail-closed; it creates no object and runs no script.
/// NOT READY: the auto-level scheme replay, background effects, the new-character (non-pre-made) path, item wielding.
/// </summary>
public static class PremadeHeroReader
{
    public const int PcType = 15, PrototypeOffset = 20, MaxPrototypeBytes = PrototypeRecords.MaxPrototypeBytes;
    public const int GoldType = 8;
    private const int StatSlot = 38, BasicSkillSlot = 39, TechSkillSlot = 40, EffectSlot = 46, CauseSlot = 47, PortraitSlot = 49,
        GoldSlot = 50, InventoryCountSlot = 55, InventoryListSlot = 56, InventorySourceSlot = 57, SchemeSlot = 63,
        BackgroundSlot = 73, BackgroundDescriptionSlot = 74, PlayerNameSlot = 86, BankSlot = 87, NameSlot = 21, DescriptionSlot = 22, ScriptsSlot = 31;
    // ARC-WORLD-085: the item slots of the weight facts (as the area inventory reads them).
    private const int ItemWeightSlot = 38, ItemMagicWeightSlot = 39, ItemComplexitySlot = 45;
    private const int GetScript = 4, InsertItemScript = 21;
    private const int ExperienceStat = 18, AlignmentStat = 19, AgeStat = 25, GenderStat = 26, RaceStat = 27;
    private const int RaceCause = 0, GenderCause = 9, RaceCount = 11;

    public static PremadeHeroRead Read(PremadeHeroSources sources, int basicPrototype)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(sources.Prototype);
        ArgumentNullException.ThrowIfNull(sources.Effects);
        ArgumentNullException.ThrowIfNull(sources.InventorySources);
        if (basicPrototype is < 0 or > 999_999 - PrototypeOffset) return new(PremadeHeroFailure.PrototypeMissing, null, basicPrototype);
        var number = basicPrototype + PrototypeOffset;
        var file = Prototype(sources, number, out var record);
        if (file != PrototypeFileStatus.Found)
            return new(file == PrototypeFileStatus.Missing ? PremadeHeroFailure.PrototypeMissing
                : file == PrototypeFileStatus.Ambiguous ? PremadeHeroFailure.PrototypeAmbiguous : PremadeHeroFailure.PrototypeInvalid, null, number);
        if (record!.Type != PcType) return new(PremadeHeroFailure.NotPcPrototype, null, number);
        var f = record.Fields;

        var stats = Elements(f[StatSlot]);
        var race = stats.GetValueOrDefault(RaceStat);
        var gender = stats.GetValueOrDefault(GenderStat);
        if (race is < 0 or >= RaceCount) return new(PremadeHeroFailure.InvalidRace, null, race);
        if (gender is not (0 or 1)) return new(PremadeHeroFailure.InvalidGender, null, gender);
        // A new PC keeps no skills, experience, items, gold object or bank money from its prototype here: any of them is
        // state this reader would have to model.
        if (Elements(f[BasicSkillSlot]).Values.Any(v => v != 0) || Elements(f[TechSkillSlot]).Values.Any(v => v != 0) ||
            stats.GetValueOrDefault(ExperienceStat) != 0 || Int(f[InventoryCountSlot]) != 0 || f[InventoryListSlot].Elements.Count != 0 ||
            f[GoldSlot].Presence != ObjectWirePresence.Null || Int(f[BankSlot]) != 0)
            return new(PremadeHeroFailure.UnsupportedPrototypeState, null, number);
        var effects = Elements(f[EffectSlot]);
        var causes = Elements(f[CauseSlot]);
        var innate = new HashSet<int>();
        foreach (var (index, effect) in effects)
            if (causes.GetValueOrDefault(index, -1) is not ((RaceCause or GenderCause) and var cause) || !innate.Add(cause))
                return new(PremadeHeroFailure.UnsupportedPrototypeState, null, effect);

        var raceEffect = EffectRules.Read(sources.Effects, EffectRules.RaceEffectBase + race);
        if (raceEffect.Rule is null) return new(PremadeHeroFailure.EffectRefused, null, EffectRules.RaceEffectBase + race, raceEffect.Status);
        EffectRule? genderEffect = null;
        if (gender == 0)
        {
            var read = EffectRules.Read(sources.Effects, EffectRules.FemaleEffect);
            if (read.Rule is null) return new(PremadeHeroFailure.EffectRefused, null, EffectRules.FemaleEffect, read.Status);
            genderEffect = read.Rule;
        }

        var setNumber = Int(f[InventorySourceSlot]);
        var gold = 0;
        var items = new List<PremadeHeroItem>();
        if (setNumber != 0)
        {
            // ARC-WORLD-093: a loot or buy number without a prototype file empties the set in the source (BT8, BT9); an ambiguous or
            // invalid file counts as existing here, so a kit item keeps its own refusal below and a buy number passes.
            var set = InventorySourceRules.Read(sources.InventorySources, sources.InventorySourcesBuy, setNumber,
                basic => basic is > 0 and <= 999_999 - PrototypeOffset && Prototype(sources, basic + PrototypeOffset, out _) != PrototypeFileStatus.Missing);
            if (set.Set is null) return new(PremadeHeroFailure.InventorySourceRefused, null, setNumber, SourceStatus: set.Status);
            if (set.Set.MinCoins != set.Set.MaxCoins || set.Set.Items.Any(item => item.Rate < 100))
                return new(PremadeHeroFailure.RandomInventorySource, null, setNumber);
            gold = set.Set.MinCoins;
            foreach (var entry in set.Set.Items)
            {
                var failure = ReadItem(sources.Prototype, entry.BasicPrototype, out var item, out var itemNumber);
                if (failure != PremadeHeroFailure.None) return new(failure, null, itemNumber);
                items.Add(item!);
            }
        }

        return new(PremadeHeroFailure.None, new(basicPrototype, number, race, gender, stats.GetValueOrDefault(AlignmentStat),
            stats.GetValueOrDefault(AgeStat), Int(f[PortraitSlot]), Int(f[BackgroundSlot]), Int(f[SchemeSlot]), raceEffect.Rule, genderEffect,
            setNumber, gold, items.AsReadOnly()) { PlayerName = Array.AsReadOnly((f[PlayerNameSlot].Text ?? []).TakeWhile(b => b != 0).ToArray()),
                BackgroundDescription = Int(f[BackgroundDescriptionSlot]) });
    }

    /// <summary>
    /// One kit item from its basic prototype number (the prototype file is <paramref name="basicPrototype"/> + <see cref="PrototypeOffset"/>,
    /// <paramref name="number"/>): the item's type, name class, description number and weight facts. Refused: a number past the
    /// prototype range or a file that is missing, ambiguous or invalid, a type that is not an item or is gold (the PC holds coins), and an
    /// item with a get or insert-item script, which the transfer to the PC runs and nothing here does. ARC-UI-036: the opening shop's
    /// kit (background items, clothes, lockpicks, bandages) reads its items with this too.
    /// </summary>
    public static PremadeHeroFailure ReadItem(Func<int, PrototypeFile> prototypes, int basicPrototype, out PremadeHeroItem? item, out int number)
    {
        ArgumentNullException.ThrowIfNull(prototypes);
        item = null;
        number = basicPrototype;
        if (basicPrototype is < 0 or > 999_999 - PrototypeOffset) return PremadeHeroFailure.ItemPrototypeMissing;
        number = basicPrototype + PrototypeOffset;
        var status = PrototypeRecords.Read(prototypes, number, out var record);
        if (status != PrototypeFileStatus.Found)
            return status == PrototypeFileStatus.Missing ? PremadeHeroFailure.ItemPrototypeMissing
                : status == PrototypeFileStatus.Ambiguous ? PremadeHeroFailure.ItemPrototypeAmbiguous : PremadeHeroFailure.ItemPrototypeInvalid;
        if (record!.Type is < 5 or > 14 || record.Type == GoldType) return PremadeHeroFailure.ItemTypeUnsupported;
        // An attachment with script number 0 is no script (word 2 of the 12-byte element).
        if (record.Fields[ScriptsSlot].Elements.Any(e => e.Key is GetScript or InsertItemScript && e.Words.Count == 3 && e.Words[2] != 0))
            return PremadeHeroFailure.ItemScriptUnsupported;
        item = new(basicPrototype, number, record.Type, Int(record.Fields[NameSlot]), Int(record.Fields[DescriptionSlot]))
            { Weight = Int(record.Fields[ItemWeightSlot]), MagicWeight = Int(record.Fields[ItemMagicWeightSlot]), Complexity = Int(record.Fields[ItemComplexitySlot]) };
        return PremadeHeroFailure.None;
    }

    /// <summary>
    /// A lookup over one loose prototype directory: file <c>NNNNNN - *.pro</c> by number, ASCII case-insensitive on the
    /// extension. More than one match is <see cref="PrototypeFileStatus.Ambiguous"/>. For synthetic fixtures only: it sees
    /// no archive and no other folder, so game code reads prototypes through <see cref="Content.ContentPrototypes"/>
    /// (ARC-DATA-026), the files the original reads.
    /// </summary>
    public static Func<int, PrototypeFile> DirectoryPrototypes(string directory)
    {
        ArgumentException.ThrowIfNullOrEmpty(directory);
        return number =>
        {
            if (number is < 0 or > 999_999) return new(PrototypeFileStatus.Missing, null);
            var prefix = number.ToString("D6", CultureInfo.InvariantCulture) + " - ";
            string[] matches;
            try
            {
                matches = Directory.EnumerateFiles(directory, prefix + "*")
                    .Where(path => Path.GetFileName(path).EndsWith(".pro", StringComparison.OrdinalIgnoreCase)).ToArray();
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return new(PrototypeFileStatus.Unreadable, null); }
            if (matches.Length == 0) return new(PrototypeFileStatus.Missing, null);
            if (matches.Length > 1) return new(PrototypeFileStatus.Ambiguous, null);
            try
            {
                var info = new FileInfo(matches[0]);
                if (info.Length > MaxPrototypeBytes) return new(PrototypeFileStatus.TooLarge, null);
                return new(PrototypeFileStatus.Found, File.ReadAllBytes(matches[0]));
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return new(PrototypeFileStatus.Unreadable, null); }
        };
    }

    private static PrototypeFileStatus Prototype(PremadeHeroSources sources, int number, out ObjectWireRecord? record) =>
        PrototypeRecords.Read(sources.Prototype, number, out record);

    private static int Int(ObjectWireValue value) => value.Number is { } number ? unchecked((int)number) : 0;

    // Sparse int32 array elements by key; an empty or null array has none.
    private static Dictionary<int, int> Elements(ObjectWireValue value) =>
        value.Elements.ToDictionary(element => element.Key, element => unchecked((int)(uint)element.Words[0]));
}
