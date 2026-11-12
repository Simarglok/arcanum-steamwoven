using System.Collections.ObjectModel;
using Arcanum.Data.Area;
using Arcanum.Data.Characters;
using Arcanum.Data.Objects;

namespace Arcanum.Data.Combat;

/// <summary>
/// ARC-DATA-021: the Godot-free reader of the combat-relevant stored fields of weapons, ammunition, armour, other items and
/// critters (ARC-RES-030 open question O6; FR-ITEM-006 data side; field list in docs/research/combat-domain-load-map.md
/// section 5). Two inputs: a loose prototype file by number, or an Area object's effective fields, where an instance's own
/// fields replace its prototype's whole field. Slots are the v119 layout of the StaticObjects loader: common block 0..35,
/// item 36..58 with weapon, ammunition, armour or generic from 59, critter 36..68 with NPC from 69 (contract
/// docs/formats/combat-fields.md). Sparse int32 arrays read like the source where it is safe: a missing key is 0; a key
/// outside the array's range, which the source would ignore, is refused (fail-closed). Read-only and fail-closed; no stat, effect, aptitude scaling or wielder rule is applied.
/// NOT READY: portals, containers and other attackable non-items; critter ammunition pools (object handles); the spell
/// and AI tables the stored numbers point to.
/// </summary>
public static class CombatFieldReader
{
    /// <summary>The stored ammunition type of a weapon that uses none.</summary>
    public const int NoAmmo = 10000;
    public const int PortalType = 1, ContainerType = 2, SceneryType = 3, TrapType = 17;
    public const int WeaponType = 5, AmmoType = 6, ArmorType = 7, GoldType = 8, WrittenType = 13, GenericType = 14, PcType = 15, NpcType = 16;
    public const int DamageTypes = 5, Resistances = 5, ItemSpells = 5, BaseStats = 28, BasicSkills = 12, TechSkills = 4, SpellTech = 25;

    // ARC-COMBAT-008: the first field of a portal, container, scenery or trap block (its flags), a scenery's respawn delay and the
    // common destroyed ART.
    private const int TypeFlagsSlot = 36, RespawnDelaySlot = 38, DestroyedArtSlot = 24;
    // Common block.
    private const int CurrentArtSlot = 0, ScriptsSlot = 31, LastScriptPoint = 35, FlagsSlot = 18, SpellFlagsSlot = 19, NameSlot = 21, DescriptionSlot = 22, AcSlot = 25, HpSlot = 26, HpAdjustmentSlot = 27,
        HpDamageSlot = 28, MaterialSlot = 29, ResistanceSlot = 30;
    // Item block (36..58).
    private const int EffectsDescriptionSlot = 48, GoldQuantitySlot = 60;
    private const int ItemFlagsSlot = 36, WeightSlot = 38, MagicWeightSlot = 39, WorthSlot = 40, ManaStoreSlot = 41, InventoryArtSlot = 42, InventoryLocationSlot = 43,
        UseArtFragmentSlot = 44, ComplexitySlot = 45,
        DisciplineSlot = 46, FirstSpellSlot = 49, SpellManaStoreSlot = 54, AiActionSlot = 55;
    // Weapon block (59..85).
    private const int WeaponFlagsSlot = 59, BonusToHitSlot = 61, MagicHitSlot = 62, DamageLowerSlot = 63, DamageUpperSlot = 64, MagicDamageSlot = 65,
        SpeedSlot = 66, MagicSpeedSlot = 67, RangeSlot = 68, MagicRangeSlot = 69, MinStrengthSlot = 70, MagicMinStrengthSlot = 71,
        WeaponAmmoTypeSlot = 72, AmmoConsumptionSlot = 73, MissileArtSlot = 74, CritHitChartSlot = 76, MagicCritHitChanceSlot = 77, MagicCritHitEffectSlot = 78,
        CritMissChartSlot = 79, MagicCritMissChanceSlot = 80, MagicCritMissEffectSlot = 81;
    // Ammunition block (59..65).
    private const int AmmoFlagsSlot = 59, AmmoQuantitySlot = 60, AmmoTypeSlot = 61;
    // Armour block (59..70).
    private const int ArmorFlagsSlot = 59, AcAdjustmentSlot = 61, MagicAcAdjustmentSlot = 62, ResistanceAdjustmentSlot = 63,
        MagicResistanceAdjustmentSlot = 64, SilentMoveSlot = 65, MagicSilentMoveSlot = 66, UnarmedBonusSlot = 67;
    // Written block (59..66, ARC-TECHNOLOGY-003): flags, subtype, first and last text line, then pads.
    private const int WrittenFlagsSlot = 59, WrittenSubtypeSlot = 60, WrittenStartLineSlot = 61, WrittenEndLineSlot = 62;
    // Generic block (59..63).
    private const int GenericFlagsSlot = 59, UsageBonusSlot = 60, UsageCountSlot = 61;
    // Critter block (36..68).
    private const int CritterFlagsSlot = 36, CritterFlags2Slot = 37, StatSlot = 38, BasicSkillSlot = 39, TechSkillSlot = 40, SpellTechSlot = 41,
        FatigueSlot = 42, FatigueAdjustmentSlot = 43, FatigueDamageSlot = 44, CritterCritHitChartSlot = 45, EffectSlot = 46, CauseSlot = 47,
        InventorySourceSlot = 57, UnknownDescriptionSlot = 58, SchemeSlot = 63;
    /// <summary>The longest effect list read (project policy; the session's own lists hold at most 64 entries).</summary>
    public const int MaxEffects = 256;
    // NPC block (69..93).
    private const int NpcFlagsSlot = 69, AiDataSlot = 71, ExperienceWorthSlot = 74, ExperiencePoolSlot = 75, FactionSlot = 81, RetailPriceMultiplierSlot = 82,
        ReactionBaseSlot = 84, SocialClassSlot = 85, WaitSlot = 89, GeneratorDataSlot = 90, NpcDamageSlot = 92;

    /// <summary>Skill points of a stored basic or tech skill value (bits 0..5).</summary>
    public static int SkillPoints(int stored) => stored & 0x3F;

    /// <summary>Training level of a stored basic or tech skill value (bits 6..7): 0 none .. 3 master.</summary>
    public static int SkillTraining(int stored) => (stored >> 6) & 0x3;

    /// <summary>The prototype file <paramref name="prototype"/> (its six-digit number, the basic prototype number + 20).
    /// The lookup is the one the pre-made hero reader takes: in play <see cref="Content.ContentPrototypes.Read"/> (ARC-DATA-026).</summary>
    public static CombatFieldRead ReadPrototype(Func<int, PrototypeFile> lookup, int prototype)
    {
        ArgumentNullException.ThrowIfNull(lookup);
        if (prototype is < 0 or > 999_999) return new(CombatFieldFailure.PrototypeMissing, null, prototype);
        var status = PrototypeRecords.Read(lookup, prototype, out var record);
        if (status != PrototypeFileStatus.Found)
            return new(status == PrototypeFileStatus.Missing ? CombatFieldFailure.PrototypeMissing
                : status == PrototypeFileStatus.Ambiguous ? CombatFieldFailure.PrototypeAmbiguous : CombatFieldFailure.PrototypeInvalid, null, prototype);
        var fields = record!.Fields;
        return Read(CombatFieldOrigin.Prototype, record.Type, prototype,
            slot => fields.TryGetValue(slot, out var value) ? new Field((Presence)(int)value.Presence, value.Number, value.Elements.Select(e => (e.Key, e.Words)).ToArray()) : null);
    }

    /// <summary>One Area object by its effective fields (instance over prototype).</summary>
    public static CombatFieldRead Read(AreaObjectRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        // First value wins for a caller-built record that repeats a slot, as in the inventory view.
        var fields = new Dictionary<int, AreaFieldValue>();
        foreach (var value in record.EffectiveFields) fields.TryAdd(value.Slot, value);
        return Read(CombatFieldOrigin.AreaObject, record.Type, record.PrototypeNumber, slot =>
            fields.TryGetValue(slot, out var value) && value.Presence != AreaPresence.Absent
                ? new Field((Presence)((int)value.Presence - 1), value.Number, value.Elements.Select(e => (e.Key, e.Words)).ToArray())
                : null);
    }

    /// <summary>
    /// ARC-COMBAT-008: a portal, container, scenery or trap of an Area snapshot (static or mobile) by its effective fields: the common
    /// block and the fields its break reads (<see cref="CombatBreakableFields"/>). Another type is
    /// <see cref="CombatFieldFailure.TypeUnsupported"/>; a field that is not a plain number refuses it, fail-closed.
    /// </summary>
    public static CombatBreakableRead ReadBreakable(AreaObjectRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (record.Type is not (PortalType or ContainerType or SceneryType or TrapType)) return new(CombatFieldFailure.TypeUnsupported, null, record.Type);
        var fields = new Dictionary<int, AreaFieldValue>();
        foreach (var value in record.EffectiveFields) fields.TryAdd(value.Slot, value);
        Field? Lookup(int slot) => fields.TryGetValue(slot, out var value) && value.Presence != AreaPresence.Absent
            ? new Field((Presence)((int)value.Presence - 1), value.Number, value.Elements.Select(e => (e.Key, e.Words)).ToArray())
            : null;
        try
        {
            var common = Common(record.Type, record.PrototypeNumber, Lookup);
            var scenery = record.Type == SceneryType;
            return new(CombatFieldFailure.None, new(common, unchecked((uint)Number(Lookup, TypeFlagsSlot)), scenery ? Number(Lookup, RespawnDelaySlot) : 0,
                scenery || record.Type == TrapType ? unchecked((uint)Number(Lookup, DestroyedArtSlot)) : CombatBreakableFields.NoArt));
        }
        catch (Refusal refusal) { return new(refusal.Failure, null, refusal.Slot); }
    }

    /// <summary>Every mobile item and critter of a strict Area snapshot, by source index; the first refusal stops the read. An
    /// item or critter from a sector file (not the mobile aggregate) is refused, as the inventory view refuses it.</summary>
    public static CombatAreaRead ReadArea(AreaSnapshot snapshot, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!snapshot.StrictlyPublished) return new(CombatFieldFailure.SnapshotNotStrict, null);
        if (snapshot.Objects.FirstOrDefault(o => o.Key.Kind != AreaSourceKind.Mobile && Supported(o.Type)) is { } stray)
            return new(CombatFieldFailure.StaticObjectUnsupported, null, stray.Key.SourceIndex, stray.Type);
        var objects = new List<CombatAreaObject>();
        foreach (var record in snapshot.Objects.Where(o => o.Key.Kind == AreaSourceKind.Mobile && Supported(o.Type)).OrderBy(o => o.Key.SourceIndex))
        {
            token.ThrowIfCancellationRequested();
            var read = Read(record);
            if (read.Fields is null) return new(read.Failure, null, record.Key.SourceIndex, read.Detail);
            objects.Add(new(record.Key.SourceIndex, record.PrototypeNumber, read.Fields));
        }
        return new(CombatFieldFailure.None, objects.AsReadOnly());
    }

    /// <summary>The source's weapon skill of a weapon (0x465D80): boomerangs throw; a technological weapon
    /// with range at least 3 and bullets, power cells or fuel is a firearm; arrows are bows; everything else is melee.
    /// A non-weapon item is thrown. Only the weapon branch is implemented here (ledger IF11).</summary>
    [OriginalFunction(0x465D80, OriginalFunctionScope.Partial)]
    public static CombatWeaponSkill WeaponSkill(CombatWeaponFlags flags, int magickTechComplexity, int range, int ammoType)
    {
        if ((flags & CombatWeaponFlags.Boomerangs) != 0) return CombatWeaponSkill.Throwing;
        if (magickTechComplexity < 0 && range >= 3 && ammoType is (int)CombatAmmoType.Bullet or (int)CombatAmmoType.PowerCell or (int)CombatAmmoType.Fuel)
            return CombatWeaponSkill.Firearms;
        return ammoType == (int)CombatAmmoType.Arrow ? CombatWeaponSkill.Bow : CombatWeaponSkill.Melee;
    }

    private static bool Supported(int type) => type is >= WeaponType and <= NpcType;

    private enum Presence { Null, Value, Empty, Array }
    private sealed record Field(Presence Presence, long? Number, (int Key, IReadOnlyList<long> Words)[] Elements);

    private sealed class Refusal(CombatFieldFailure failure, int slot) : Exception
    {
        public CombatFieldFailure Failure { get; } = failure;
        public int Slot { get; } = slot;
    }

    private static CombatFieldRead Read(CombatFieldOrigin origin, int type, int prototype, Func<int, Field?> field)
    {
        if (!Supported(type)) return new(CombatFieldFailure.TypeUnsupported, null, type);
        try { return new(CombatFieldFailure.None, Build(origin, type, prototype, field)); }
        catch (Refusal refusal) { return new(refusal.Failure, null, refusal.Slot); }
    }

    // A plain number of a slot (a 32-bit word, signed or unsigned), else the slot refuses the object.
    private static int Number(Func<int, Field?> field, int slot) => field(slot) is { Presence: Presence.Value, Number: { } n } && n is >= int.MinValue and <= uint.MaxValue
        ? unchecked((int)n) : throw new Refusal(CombatFieldFailure.FieldUnresolved, slot);

    // The common block every object carries.
    private static CombatObjectFields Common(int type, int prototype, Func<int, Field?> field)
    {
        int Int(int slot) => Number(field, slot);
        uint Flags(int slot) => unchecked((uint)Int(slot));
        return new CombatObjectFields(type, prototype, Flags(FlagsSlot), Flags(SpellFlagsSlot), Int(NameSlot), Int(DescriptionSlot), Int(AcSlot),
            Int(HpSlot), Int(HpAdjustmentSlot), Int(HpDamageSlot), Int(MaterialSlot), Values(field(ResistanceSlot), ResistanceSlot, Resistances), Flags(CurrentArtSlot))
        { ScriptPoints = ScriptPoints(field(ScriptsSlot)), ScriptAttachments = ScriptAttachments(field(ScriptsSlot)) };
    }

    private static CombatFields Build(CombatFieldOrigin origin, int type, int prototype, Func<int, Field?> field)
    {
        int Int(int slot) => Number(field, slot);
        uint Flags(int slot) => unchecked((uint)Int(slot));
        // ARC-UI-108: a number a record may lack (read 0), not a refusal.
        int Optional(int slot) => field(slot) is { Presence: Presence.Value, Number: { } n } && n is >= int.MinValue and <= uint.MaxValue ? unchecked((int)n) : 0;
        IReadOnlyList<int> Array(int slot, int count) => Values(field(slot), slot, count);
        IReadOnlyList<CombatDamageRange> Ranges(IReadOnlyList<int> lower, IReadOnlyList<int> upper) =>
            System.Array.AsReadOnly(Enumerable.Range(0, DamageTypes).Select(i => new CombatDamageRange(lower[i], upper[i])).ToArray());

        var common = Common(type, prototype, field);
        if (type is PcType or NpcType)
        {
            var critter = new CombatCritterFields(Flags(CritterFlagsSlot), Flags(CritterFlags2Slot), Array(StatSlot, BaseStats),
                Array(BasicSkillSlot, BasicSkills), Array(TechSkillSlot, TechSkills), Array(SpellTechSlot, SpellTech), Int(FatigueSlot),
                Int(FatigueAdjustmentSlot), Int(FatigueDamageSlot), Int(CritterCritHitChartSlot), Int(InventorySourceSlot), Int(SchemeSlot),
                EffectList(field(EffectSlot), field(CauseSlot)));
            CombatNpcFields? npc = null;
            if (type == NpcType)
            {
                // Monster damage: minimum at 2 × damage type, maximum at 2 × damage type + 1.
                var damage = Array(NpcDamageSlot, 2 * DamageTypes);
                npc = new(Flags(NpcFlagsSlot), Int(AiDataSlot), Int(ExperienceWorthSlot), Int(ExperiencePoolSlot), Int(FactionSlot),
                    Int(ReactionBaseSlot), Int(SocialClassSlot),
                    Ranges(damage.Where((_, i) => i % 2 == 0).ToArray(), damage.Where((_, i) => i % 2 == 1).ToArray()), Int(WaitSlot),
                    Int(RetailPriceMultiplierSlot))
                {
                    // ARC-UI-097: optional (a record without the field reads 0, which names nobody in the kill log).
                    UnknownDescription = field(UnknownDescriptionSlot) is { Presence: Presence.Value, Number: { } unknown } && unknown is >= int.MinValue and <= int.MaxValue
                        ? (int)unknown : 0,
                    // ARC-WORLD-1157: the monster generator's packed settings (field 0x12D, slot 90): optional (a record without the field reads 0, so only a generator's own record needs it).
                    GeneratorData = field(GeneratorDataSlot) is { Presence: Presence.Value, Number: { } generator } && generator is >= int.MinValue and <= uint.MaxValue ? unchecked((uint)generator) : 0,
                };
            }
            return new(origin, common, null, null, null, null, null, critter, npc);
        }

        var item = new CombatItemFields(Flags(ItemFlagsSlot), Int(WeightSlot), Int(WorthSlot), Int(ManaStoreSlot), Int(ComplexitySlot),
            Int(DisciplineSlot), Int(InventoryLocationSlot),
            System.Array.AsReadOnly(Enumerable.Range(FirstSpellSlot, ItemSpells).Select(Int).ToArray()), Int(SpellManaStoreSlot), Int(AiActionSlot),
            Flags(InventoryArtSlot), Flags(UseArtFragmentSlot))
        {
            MagicWeight = Int(MagicWeightSlot),
            // ARC-UI-108: optional, read 0 when absent (the item window's effects description 0x63 and a gold pile's quantity 0xA5).
            EffectsDescription = Optional(EffectsDescriptionSlot),
            GoldQuantity = type == GoldType ? Optional(GoldQuantitySlot) : 0,
        };
        CombatWeaponFields? weapon = null;
        CombatAmmoFields? ammo = null;
        CombatArmorFields? armor = null;
        CombatGenericFields? generic = null;
        if (type == WeaponType)
        {
            var flags = (CombatWeaponFlags)Flags(WeaponFlagsSlot);
            var range = Int(RangeSlot);
            var ammoType = Int(WeaponAmmoTypeSlot);
            weapon = new(flags, WeaponSkill(flags, item.MagickTechComplexity, range, ammoType), Int(BonusToHitSlot), Int(MagicHitSlot),
                Ranges(Array(DamageLowerSlot, DamageTypes), Array(DamageUpperSlot, DamageTypes)), Array(MagicDamageSlot, DamageTypes),
                Int(SpeedSlot), Int(MagicSpeedSlot), range, Int(MagicRangeSlot), Int(MinStrengthSlot), Int(MagicMinStrengthSlot), ammoType,
                Int(AmmoConsumptionSlot), Int(CritHitChartSlot), Int(MagicCritHitChanceSlot), Int(MagicCritHitEffectSlot), Int(CritMissChartSlot),
                Int(MagicCritMissChanceSlot), Int(MagicCritMissEffectSlot)) { MissileArt = Int(MissileArtSlot) };
        }
        else if (type == AmmoType)
            ammo = new(Flags(AmmoFlagsSlot), Int(AmmoQuantitySlot), Int(AmmoTypeSlot));
        else if (type == ArmorType)
            armor = new((CombatArmorFlags)Flags(ArmorFlagsSlot), Int(AcAdjustmentSlot), Int(MagicAcAdjustmentSlot),
                Array(ResistanceAdjustmentSlot, Resistances), Array(MagicResistanceAdjustmentSlot, Resistances), Int(SilentMoveSlot),
                Int(MagicSilentMoveSlot), Int(UnarmedBonusSlot));
        else if (type == GenericType)
            generic = new(Flags(GenericFlagsSlot), Int(UsageBonusSlot), Int(UsageCountSlot));
        CombatWrittenFields? written = type == WrittenType
            ? new(Flags(WrittenFlagsSlot), Int(WrittenSubtypeSlot), Int(WrittenStartLineSlot), Int(WrittenEndLineSlot)) : null;
        return new(origin, common, item, weapon, ammo, armor, generic, null, null) { Written = written };
    }

    // The two parallel effect arrays: keys 0..n−1 in both, one word each; an absent or null array is empty. Anything else is
    // a malformed array, as the source would read a number without its cause (or past the list).
    private static IReadOnlyList<CombatEffectEntry> EffectList(Field? numbers, Field? causes)
    {
        static (int Key, long Word)[] Entries(Field? field, int slot)
        {
            if (field is null || field.Presence is Presence.Null or Presence.Empty) return [];
            if (field.Presence == Presence.Value) throw new Refusal(CombatFieldFailure.FieldUnresolved, slot);
            return [.. field.Elements.Select(e => e.Words.Count == 1 && e.Words[0] is >= 0 and <= uint.MaxValue ? (e.Key, e.Words[0])
                : throw new Refusal(CombatFieldFailure.ArrayMalformed, slot)).OrderBy(e => e.Key)];
        }
        var n = Entries(numbers, EffectSlot);
        var c = Entries(causes, CauseSlot);
        if (n.Length != c.Length || n.Length > MaxEffects) throw new Refusal(CombatFieldFailure.ArrayMalformed, EffectSlot);
        for (var i = 0; i < n.Length; i++)
            if (n[i].Key != i || c[i].Key != i) throw new Refusal(CombatFieldFailure.ArrayMalformed, EffectSlot);
        return System.Array.AsReadOnly(n.Select((e, i) => new CombatEffectEntry(unchecked((int)(uint)e.Word), unchecked((int)(uint)c[i].Word))).ToArray());
    }

    // ARC-WORLD-034: the attachment points of the script array (12-byte elements keyed by the point; the third word is the script
    // number, 0 for none), in key order. A key outside 0..35 or an element of another width is malformed.
    private static IReadOnlyList<int> ScriptPoints(Field? field)
    {
        if (field is null || field.Presence == Presence.Value) throw new Refusal(CombatFieldFailure.FieldUnresolved, ScriptsSlot);
        if (field.Presence != Presence.Array) return [];
        var points = new SortedSet<int>();
        foreach (var (key, words) in field.Elements)
        {
            if (key is < 0 or > LastScriptPoint || words.Count != 3) throw new Refusal(CombatFieldFailure.ArrayMalformed, ScriptsSlot);
            if (words[2] != 0) points.Add(key);
        }
        return System.Array.AsReadOnly(points.ToArray());
    }

    // ARC-DEV-097 (MIG8, SA1): the elements of the script array as the 12-byte records they are: word 0 the local flags, word 1 the counters, word 2 the
    // script number, in key order. The key and the width are the ones ScriptPoints checks; a word beyond 32 bits is malformed.
    private static IReadOnlyList<CombatScriptAttachment> ScriptAttachments(Field? field)
    {
        if (field is null || field.Presence != Presence.Array) return [];
        var found = new SortedDictionary<int, CombatScriptAttachment>();
        foreach (var (key, words) in field.Elements)
        {
            if (key is < 0 or > LastScriptPoint || words.Count != 3 || words.Any(word => word is < 0 or > uint.MaxValue)) throw new Refusal(CombatFieldFailure.ArrayMalformed, ScriptsSlot);
            found[key] = new(key, unchecked((int)(uint)words[2]), (uint)words[0], (uint)words[1]);
        }
        return System.Array.AsReadOnly(found.Values.ToArray());
    }

    // A dense copy of a sparse int32 array: a null or empty array, or a missing key, is 0; a 32-bit word is reinterpreted
    // as signed (the source stores −1 in unsigned arrays).
    private static IReadOnlyList<int> Values(Field? field, int slot, int count)
    {
        var values = new int[count];
        if (field is null || field.Presence == Presence.Value) throw new Refusal(CombatFieldFailure.FieldUnresolved, slot);
        if (field.Presence == Presence.Array)
            foreach (var (key, words) in field.Elements)
            {
                if (key < 0 || key >= count || words.Count != 1 || words[0] is < 0 or > uint.MaxValue)
                    throw new Refusal(CombatFieldFailure.ArrayMalformed, slot);
                values[key] = unchecked((int)(uint)words[0]);
            }
        return System.Array.AsReadOnly(values);
    }
}
