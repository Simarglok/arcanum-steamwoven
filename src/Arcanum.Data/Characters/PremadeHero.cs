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
    /// or an effect whose cause is neither race nor gender (<see cref="PremadeHeroRead.Detail"/> names the effect).</summary>
    UnsupportedPrototypeState,
    /// <summary>A race or gender effect the effect rules refuse (<see cref="PremadeHeroRead.EffectStatus"/>).</summary>
    EffectRefused,
    /// <summary>The inventory source set is refused (<see cref="PremadeHeroRead.SourceStatus"/>).</summary>
    InventorySourceRefused,
    /// <summary>A coin range or an item rate that needs gameplay randomness, which is not modelled (FR-CONTENT-007).</summary>
    RandomInventorySource,
    ItemPrototypeMissing,
    ItemPrototypeAmbiguous,
    ItemPrototypeInvalid,
    /// <summary>A listed prototype that is not an item (types 5..14) or is gold, which the PC holds as coins.</summary>
    ItemTypeUnsupported,
}

/// <summary>A starting-kit item: basic prototype, prototype (file) number, object type (5..14), numeric name class
/// (<c>OBJ_F_NAME</c>) and description number, all from the item prototype.</summary>
public sealed record PremadeHeroItem(int BasicPrototype, int Prototype, int Type, int Name, int Description);

/// <summary>
/// One pre-made hero at the moment the new game starts (ARC-RES-027). Race, gender, alignment and age are the prototype's
/// stored values, which <c>level_set(pc, 1)</c> keeps; every other stat is the source default it writes: the eight primary
/// stats 8, level 1, 5 unspent points, magick and tech points 0 (<see cref="StartPrimaryStats"/> etc.). The auto-level
/// scheme (<see cref="AutoLevelScheme"/>) that then spends the points is NOT applied. <see cref="RaceEffect"/> is effect
/// 64 + race and <see cref="GenderEffect"/> effect 330 for a woman, the innate effects the stat setter re-adds. Gold and
/// items come from the inventory source set; the stored background is display data and adds no effect.
/// </summary>
public sealed record PremadeHero(int BasicPrototype, int Prototype, int Race, int Gender, int Alignment, int Age, int Portrait,
    int Background, int AutoLevelScheme, EffectRule RaceEffect, EffectRule? GenderEffect, int InventorySource, int Gold,
    IReadOnlyList<PremadeHeroItem> Items)
{
    public const int StartPrimaryStat = 8, StartLevel = 1, StartUnspentPoints = 5;
}

/// <param name="Detail">The effect or prototype number the failure is about, when it has one.</param>
public sealed record PremadeHeroRead(PremadeHeroFailure Failure, PremadeHero? Hero, int? Detail = null,
    EffectRuleStatus? EffectStatus = null, InventorySourceStatus? SourceStatus = null);

/// <summary>
/// ARC-WORLD-015: the Godot-free reader of one pre-made PC prototype into its new-game start (ARC-RES-027 proposal 2).
/// Field slots are the v119 layout of <see cref="StaticObjectLoader"/> (the ARC-RES-027 probe's slot map): stat array 38,
/// basic and tech skills 39/40, effects and causes 46/47, portrait 49, gold handle 50, inventory count/list/source 55/56/57,
/// auto-level scheme 63, background 73, bank money 87. Read-only and fail-closed; it creates no object and runs no script.
/// NOT READY: the auto-level scheme replay, background effects, the new-character (non-pre-made) path, item wielding.
/// </summary>
public static class PremadeHeroReader
{
    public const int PcType = 15, PrototypeOffset = 20, MaxPrototypeBytes = 64 * 1024;
    public const int GoldType = 8;
    private const int StatSlot = 38, BasicSkillSlot = 39, TechSkillSlot = 40, EffectSlot = 46, CauseSlot = 47, PortraitSlot = 49,
        GoldSlot = 50, InventoryCountSlot = 55, InventoryListSlot = 56, InventorySourceSlot = 57, SchemeSlot = 63,
        BackgroundSlot = 73, BankSlot = 87, NameSlot = 21, DescriptionSlot = 22;
    private const int ExperienceStat = 18, AlignmentStat = 19, AgeStat = 25, GenderStat = 26, RaceStat = 27;
    private const int RaceCause = 0, GenderCause = 9, RaceCount = 11;

    private static readonly StaticObjectLoaderOptions Limits = new() { MaxPrototypeBytes = MaxPrototypeBytes, MaxArrayElements = 4096, MaxStringBytes = 4096 };

    public static PremadeHeroRead Read(PremadeHeroSources sources, int basicPrototype)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(sources.Prototype);
        ArgumentNullException.ThrowIfNull(sources.Effects);
        ArgumentNullException.ThrowIfNull(sources.InventorySources);
        var number = checked(basicPrototype + PrototypeOffset);
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
        foreach (var (index, effect) in effects)
            if (causes.GetValueOrDefault(index, -1) is not (RaceCause or GenderCause))
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
            var set = InventorySourceRules.Read(sources.InventorySources, sources.InventorySourcesBuy, setNumber);
            if (set.Set is null) return new(PremadeHeroFailure.InventorySourceRefused, null, setNumber, SourceStatus: set.Status);
            if (set.Set.MinCoins != set.Set.MaxCoins || set.Set.Items.Any(item => item.Rate < 100))
                return new(PremadeHeroFailure.RandomInventorySource, null, setNumber);
            gold = set.Set.MinCoins;
            foreach (var entry in set.Set.Items)
            {
                var itemNumber = checked(entry.BasicPrototype + PrototypeOffset);
                var status = Prototype(sources, itemNumber, out var item);
                if (status != PrototypeFileStatus.Found)
                    return new(status == PrototypeFileStatus.Missing ? PremadeHeroFailure.ItemPrototypeMissing
                        : status == PrototypeFileStatus.Ambiguous ? PremadeHeroFailure.ItemPrototypeAmbiguous : PremadeHeroFailure.ItemPrototypeInvalid,
                        null, itemNumber);
                if (item!.Type is < 5 or > 14 || item.Type == GoldType) return new(PremadeHeroFailure.ItemTypeUnsupported, null, itemNumber);
                items.Add(new(entry.BasicPrototype, itemNumber, item.Type, Int(item.Fields[NameSlot]), Int(item.Fields[DescriptionSlot])));
            }
        }

        return new(PremadeHeroFailure.None, new(basicPrototype, number, race, gender, stats.GetValueOrDefault(AlignmentStat),
            stats.GetValueOrDefault(AgeStat), Int(f[PortraitSlot]), Int(f[BackgroundSlot]), Int(f[SchemeSlot]), raceEffect.Rule, genderEffect,
            setNumber, gold, items.AsReadOnly()));
    }

    /// <summary>
    /// A lookup over one loose prototype directory (the installation's <c>data/proto</c>): file <c>NNNNNN - *.pro</c> by
    /// number, ASCII case-insensitive on the extension. More than one match is <see cref="PrototypeFileStatus.Ambiguous"/>.
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

    private static PrototypeFileStatus Prototype(PremadeHeroSources sources, int number, out ObjectWireRecord? record)
    {
        record = null;
        var file = sources.Prototype(number);
        if (file is null || file.Status != PrototypeFileStatus.Found)
            return file?.Status is { } status && status != PrototypeFileStatus.Found ? status : PrototypeFileStatus.Missing;
        if (file.Bytes is not { } bytes || bytes.Length > MaxPrototypeBytes) return PrototypeFileStatus.TooLarge;
        try { record = ObjectWireReader.Read(bytes, Limits, prototype: true, maxRecordBytes: MaxPrototypeBytes); }
        catch (StaticObjectException) { return PrototypeFileStatus.Unreadable; }
        if (record.Own.Number != number || record.Length != bytes.Length)
        {
            record = null;
            return PrototypeFileStatus.Unreadable;
        }
        return PrototypeFileStatus.Found;
    }

    private static int Int(ObjectWireValue value) => value.Number is { } number ? unchecked((int)number) : 0;

    // Sparse int32 array elements by key; an empty or null array has none.
    private static Dictionary<int, int> Elements(ObjectWireValue value) =>
        value.Elements.ToDictionary(element => element.Key, element => unchecked((int)(uint)element.Words[0]));
}
