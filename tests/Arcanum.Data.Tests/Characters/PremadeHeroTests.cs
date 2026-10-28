using System.Buffers.Binary;
using System.Text;
using Arcanum.Data.Characters;
using Arcanum.Data.Messages;
using Arcanum.Data.StaticObjects;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Data.Tests.Characters;

// ARC-WORLD-015 rule parsers and pre-made hero reader on invented inputs: numbers, names and texts are synthetic.
[TestClass]
public sealed class PremadeHeroTests
{
    private static MesFile Mes(string text)
    {
        var parsed = MesFile.Parse(Encoding.ASCII.GetBytes(text));
        Assert.AreEqual(MesParseStatus.Parsed, parsed.Status);
        return parsed.File!;
    }

    private static EffectRuleStatus Parse(string text, out IReadOnlyList<EffectChange> changes) =>
        EffectRules.Parse(Encoding.ASCII.GetBytes(text), out changes);

    [TestMethod]
    public void EffectEntriesParseEveryCollectionAndOperator()
    {
        Assert.AreEqual(EffectRuleStatus.Parsed, Parse("dx +1 BE +1,cn -1 magicpts +2,,age +15", out var changes));
        CollectionAssert.AreEqual(new EffectChange[]
        {
            new(EffectAttributeKind.Stat, 1, EffectChangeOperator.Add, 1), new(EffectAttributeKind.Stat, 3, EffectChangeOperator.Add, 1),
            new(EffectAttributeKind.Stat, 2, EffectChangeOperator.Add, -1), new(EffectAttributeKind.Stat, 22, EffectChangeOperator.Add, 2),
            new(EffectAttributeKind.Stat, 25, EffectChangeOperator.Add, 15),
        }, changes.ToArray());

        Assert.AreEqual(EffectRuleStatus.Parsed, Parse("heal +2, armtrap -1, resistpoison +10% expertisetherapeutics max 4 xpgain MIN -3 race +0",
            out changes));
        CollectionAssert.AreEqual(new EffectChange[]
        {
            new(EffectAttributeKind.BasicSkill, 10, EffectChangeOperator.Add, 2), new(EffectAttributeKind.TechSkill, 3, EffectChangeOperator.Add, -1),
            new(EffectAttributeKind.Resistance, 3, EffectChangeOperator.Percent, 10),
            new(EffectAttributeKind.TechDiscipline, 7, EffectChangeOperator.Maximum, 4), new(EffectAttributeKind.Special, 9, EffectChangeOperator.Minimum, -3),
            new(EffectAttributeKind.Stat, 27, EffectChangeOperator.Add, 0),
        }, changes.ToArray());
        // Decision 7 (ARC-WORLD-035): a factor is the number after the sign, as the original reads it.
        Assert.AreEqual(EffectRuleStatus.Parsed, Parse("goodreactionadj *2 badreactionadj /2 st *-1", out changes));
        CollectionAssert.AreEqual(new EffectChange[]
        {
            new(EffectAttributeKind.Special, 8, EffectChangeOperator.Multiply, 2), new(EffectAttributeKind.Special, 7, EffectChangeOperator.Divide, 2),
            new(EffectAttributeKind.Stat, 0, EffectChangeOperator.Multiply, -1),
        }, changes.ToArray());
        Assert.AreEqual(EffectRuleStatus.Parsed, Parse(" , ", out changes));
        Assert.AreEqual(0, changes.Count);
        Assert.AreEqual(EffectRuleStatus.Parsed, Parse(string.Join(" ", Enumerable.Repeat("st +1", 7)) + " bow +1", out changes));
        Assert.AreEqual(8, changes.Count, "seven per collection");
    }

    [TestMethod]
    public void EffectEntriesTheProjectDoesNotAdmitAreTypedRefusals()
    {
        foreach (var (text, status) in new[]
        {
            ("st /0", EffectRuleStatus.ZeroDivisor), ("goodreactionadj /-0", EffectRuleStatus.ZeroDivisor), ("st *", EffectRuleStatus.InvalidToken),
            ("st *x", EffectRuleStatus.InvalidToken), ("st /2x", EffectRuleStatus.InvalidToken),
            ("strength +1", EffectRuleStatus.InvalidToken), ("st 1", EffectRuleStatus.InvalidToken), ("st +1x", EffectRuleStatus.InvalidToken),
            ("st +", EffectRuleStatus.InvalidToken), ("st +%", EffectRuleStatus.InvalidToken), ("st +1234567890", EffectRuleStatus.InvalidToken),
            ("st min x", EffectRuleStatus.InvalidToken), ("st =1", EffectRuleStatus.InvalidToken), ("st", EffectRuleStatus.MissingOperand),
            ("st max", EffectRuleStatus.MissingOperand), (string.Join(" ", Enumerable.Repeat("dx +1", 8)), EffectRuleStatus.TooManyChanges),
            ("st +1é", EffectRuleStatus.InvalidText), ("dx +1\tbe +1", EffectRuleStatus.InvalidText),
            ("dx +1\nbe +1", EffectRuleStatus.InvalidText),
        })
        {
            var bytes = Encoding.Latin1.GetBytes(text);
            Assert.AreEqual(status, EffectRules.Parse(bytes, out var changes), text);
            Assert.AreEqual(0, changes.Count, text);
        }

        var table = Mes("{64}{}{67}{dx +1}{68}{st /0}");
        Assert.AreEqual(new EffectRuleRead(EffectRuleStatus.OutOfRange, null), EffectRules.Read(table, 49));
        Assert.AreEqual(new EffectRuleRead(EffectRuleStatus.OutOfRange, null), EffectRules.Read(table, 400));
        Assert.AreEqual((true, 0), (EffectRules.Read(table, 64).Rule!.Defined, EffectRules.Read(table, 64).Rule!.Changes.Count));
        Assert.AreEqual((false, 0), (EffectRules.Read(table, 65).Rule!.Defined, EffectRules.Read(table, 65).Rule!.Changes.Count), "absent: an empty effect");
        Assert.AreEqual(1, EffectRules.Read(table, 67).Rule!.Changes.Count);
        Assert.AreEqual(new EffectRuleRead(EffectRuleStatus.ZeroDivisor, null), EffectRules.Read(table, 68));
    }

    private static readonly MesFile Sources = Mes(
        "{1}{Empty: 0,0}{2}{Hero kit: 385,385 100,6029 100,8168}{3}{Purse: 5,50 50,6029}{4}{No colon 1,1}{5}{: 1,1}" +
        "{6}{Bad pair: 1,1 100}{7}{Letters: 1,1 a,6029}{8}{Triple: 1,1 1,2,3}{9}{Only name:}{10}{" + new string('n', 70) + ": 1,1}" +
        "{11}{" + new string('n', 69) + ": 1,1}{12}{Many: 1,1" + string.Concat(Enumerable.Repeat(" 1,1", 301)) + "}" +
        "{13}{Bought: 1,1}{14}{Last: 1,1}{16}{After the gap: 1,1}");

    private static bool Any(int basic) => true;

    [TestMethod]
    public void InventorySourceSetsParseWithTheSourceNumberingAndAdmission()
    {
        var hero = InventorySourceRules.Read(Sources, Mes("{2}{  }"), 2, Any);
        Assert.AreEqual(InventorySourceStatus.Parsed, hero.Status);
        Assert.AreEqual((2, 385, 385), (hero.Set!.Number, hero.Set.MinCoins, hero.Set.MaxCoins));
        CollectionAssert.AreEqual(new[] { new InventorySourceEntry(100, 6029), new InventorySourceEntry(100, 8168) }, hero.Set.Items.ToArray());
        Assert.AreEqual((5, 50, 1), (InventorySourceRules.Read(Sources, null, 3, Any).Set!.MinCoins, InventorySourceRules.Read(Sources, null, 3, Any).Set!.MaxCoins,
            InventorySourceRules.Read(Sources, null, 3, Any).Set!.Items.Count));
        Assert.AreEqual(0, InventorySourceRules.Read(Sources, null, 1, Any).Set!.Items.Count);
        Assert.AreEqual(InventorySourceStatus.Parsed, InventorySourceRules.Read(Sources, null, 11, Any).Status, "a 69-character name");

        foreach (var (set, status) in new[]
        {
            (0, InventorySourceStatus.Unreachable), (-1, InventorySourceStatus.Unreachable), (15, InventorySourceStatus.Unreachable),
            (16, InventorySourceStatus.Unreachable), (4, InventorySourceStatus.Malformed), (5, InventorySourceStatus.Malformed),
            (6, InventorySourceStatus.Emptied), (7, InventorySourceStatus.Malformed), (8, InventorySourceStatus.Emptied),
            (9, InventorySourceStatus.Malformed), (10, InventorySourceStatus.Emptied), (12, InventorySourceStatus.Emptied),
        })
            // Set 12 fails on its first item number (1 is outside the basic ranges): 301 in-range items cannot fit the 2000-character
            // line, so the source's 301st-item failure and the project's item cap never decide a set.
            Assert.AreEqual(new InventorySourceRead(status, null), InventorySourceRules.Read(Sources, null, set, Any), set.ToString());
        // ARC-WORLD-093: the lookup takes 1..count (0x4BF5D0, BT9), so the last of two entries is reachable.
        Assert.AreEqual(InventorySourceStatus.Parsed, InventorySourceRules.Read(Mes("{1}{a: 1,1}{2}{b: 1,1}"), null, 2, Any).Status);
        Assert.AreEqual(InventorySourceStatus.Parsed, InventorySourceRules.Read(Sources, null, 14, Any).Status, "the last set before the gap");
        Assert.AreEqual(InventorySourceStatus.Unreachable, InventorySourceRules.Read(Mes("{0}{z: 1,1}{1}{a: 1,1}"), null, 1, Any).Status,
            "message 1 must be the first message, as the buy rules' set loop reads it (BT8)");
        // A buy line that parses is read; a failing one empties the whole set (BT8).
        Assert.AreEqual(InventorySourceStatus.Parsed, InventorySourceRules.Read(Sources, Mes("{13}{ALL}"), 13, Any).Status);
        Assert.AreEqual(InventorySourceStatus.Parsed, InventorySourceRules.Read(Sources, Mes("{2}{6029 8168}"), 2, Any).Status);
        Assert.AreEqual(InventorySourceStatus.Emptied, InventorySourceRules.Read(Sources, Mes("{13}{ \t }"), 13, Any).Status,
            "a tab is a (failing) buy token in the source");
        Assert.AreEqual(InventorySourceStatus.Emptied, InventorySourceRules.Read(Sources, Mes("{2}{6029 8168}"), 2, basic => basic != 8168).Status,
            "a buy number without a prototype");
        Assert.AreEqual(InventorySourceStatus.Emptied, InventorySourceRules.Read(Sources, null, 2, basic => basic != 8168).Status,
            "a loot item without a prototype empties the set in the source");
        Assert.AreEqual(InventorySourceStatus.InvalidText,
            InventorySourceRules.Read(Mes("{1}{a: 385,385\n100,6029}{2}{b: 1,1}"), null, 1, Any).Status, "the source would read one token 385, LF, 100");
        Assert.AreEqual(InventorySourceStatus.InvalidText,
            InventorySourceRules.Read(MesFile.Parse(Encoding.Latin1.GetBytes("{1}{aé: 1,1}{2}{b: 1,1}")).File!, null, 1, Any).Status);
    }

    // ARC-WORLD-096: the coin pair an emptied set keeps (RK6): the loot line's first pair; none when the line failed before it.
    [TestMethod]
    public void AnEmptiedSetKeepsTheCoinPairItsLootLineReadFirst()
    {
        foreach (var (set, coins) in new (int, (int, int)?)[] { (6, (1, 1)), (8, (1, 1)), (12, (1, 1)), (3, (5, 50)), (10, null), (4, null), (5, null), (9, null), (0, null), (15, null) })
            Assert.AreEqual(coins, InventorySourceRules.LootCoins(Sources, set), set.ToString());
        Assert.IsNull(InventorySourceRules.LootCoins(Mes("{1}{a: 385,385\n100,6029}"), 1), "text the source would read otherwise");
    }

    // ---------------------------------------------------------------- synthetic v119 prototypes
    private const int PcType = 15;

    /// <summary>A v119 prototype: every slot of the type in order. Values: int for an int32 slot, a key→value map for an
    /// int32 array or a script array (attachment point → script number), an int count for an object-id array, true for a present object id; every other slot is absent.</summary>
    internal static byte[] Prototype(int number, int type, IReadOnlyDictionary<int, object>? values = null)
    {
        values ??= new Dictionary<int, object>();
        var bytes = new List<byte>();
        I32(bytes, 119); Id(bytes, -1, 0); Id(bytes, 1, number); I32(bytes, type);
        for (var word = 0; word < StaticObjectLoader.MaskWords(type); word++) I32(bytes, 0);
        var kinds = StaticObjectLoader.FieldsFor(type);
        for (var slot = 0; slot < kinds.Length; slot++)
        {
            values.TryGetValue(slot, out var value);
            switch (kinds[slot])
            {
                case StaticObjectLoader.WireKind.I32: I32(bytes, value is int i ? i : 0); break;
                case StaticObjectLoader.WireKind.OptObjectId:
                    if (value is true) { bytes.Add(1); Id(bytes, 2, 77); } else bytes.Add(0);
                    break;
                case StaticObjectLoader.WireKind.ArrI32 or StaticObjectLoader.WireKind.ArrU32 when value is IReadOnlyDictionary<int, int> map:
                    Array(bytes, 4, map.OrderBy(pair => pair.Key).Select(pair => (pair.Key, BitConverter.GetBytes(pair.Value))).ToArray());
                    break;
                case StaticObjectLoader.WireKind.ArrScript when value is IReadOnlyDictionary<int, int> scripts:
                    Array(bytes, 12, scripts.OrderBy(pair => pair.Key)
                        .Select(pair => (pair.Key, (byte[])[.. new byte[8], .. BitConverter.GetBytes(pair.Value)])).ToArray());
                    break;
                case StaticObjectLoader.WireKind.ArrObjectId when value is int count:
                    Array(bytes, 24, Enumerable.Range(0, count).Select(key => (key, IdBytes(2, 90 + key))).ToArray());
                    break;
                default: bytes.Add(0); break;
            }
        }
        return bytes.ToArray();
    }

    private static void Array(List<byte> bytes, int width, (int Key, byte[] Value)[] elements)
    {
        bytes.Add(1); I32(bytes, width); I32(bytes, elements.Length); I32(bytes, 0);
        foreach (var element in elements) bytes.AddRange(element.Value);
        var words = Math.Max(1, (elements.Select(e => e.Key).DefaultIfEmpty(0).Max() / 32) + 1);
        I32(bytes, words);
        for (var word = 0; word < words; word++)
        {
            var bits = 0u;
            foreach (var element in elements.Where(e => e.Key / 32 == word)) bits |= 1u << (element.Key % 32);
            bytes.AddRange(BitConverter.GetBytes(bits));
        }
    }

    private static byte[] IdBytes(short kind, int number)
    {
        var raw = new byte[24];
        BinaryPrimitives.WriteInt16LittleEndian(raw, kind);
        BinaryPrimitives.WriteInt32LittleEndian(raw.AsSpan(8), number);
        return raw;
    }

    private static void Id(List<byte> bytes, short kind, int number) => bytes.AddRange(IdBytes(kind, number));
    private static void I32(List<byte> bytes, int value) => bytes.AddRange(BitConverter.GetBytes(value));

    // A half-elf man (race 3, gender 1) with alignment 5, age 35, the race effect stored, set 2, portrait 1032, scheme 78,
    // background 41.
    private static Dictionary<int, object> Pc(int race = 3, int gender = 1) => new()
    {
        [38] = new Dictionary<int, int> { [0] = 8, [17] = 1, [19] = 5, [21] = 5, [25] = 35, [26] = gender, [27] = race },
        [46] = new Dictionary<int, int> { [0] = 64 + race }, [47] = new Dictionary<int, int> { [0] = 0 },
        [49] = 1032, [57] = 2, [63] = 78, [73] = 41,
    };

    private static readonly MesFile Effects = Mes("{64}{}{67}{dx +1 be +1 cn -1 magicpts +1 repair -1 age +15}{71}{st /0}{330}{st -1 cn +1}");

    private static PremadeHeroSources Inputs(Dictionary<int, PrototypeFile> files, MesFile? effects = null, MesFile? buy = null) =>
        new(number => files.TryGetValue(number, out var file) ? file : new(PrototypeFileStatus.Missing, null), effects ?? Effects, Sources, buy);

    private static Dictionary<int, PrototypeFile> Files(Dictionary<int, object>? pc = null, int pcType = PcType) => new()
    {
        [16089] = new(PrototypeFileStatus.Found, Prototype(16089, pcType, pc ?? Pc())),
        [6049] = new(PrototypeFileStatus.Found, Prototype(6049, 5, new Dictionary<int, object> { [21] = 4021, [22] = 6029 })),
        [8188] = new(PrototypeFileStatus.Found, Prototype(8188, 7, new Dictionary<int, object> { [21] = 2850, [22] = 8168 })),
    };

    [TestMethod]
    public void APreMadeHeroStartsWithItsInnateEffectsGoldAndKit()
    {
        var read = PremadeHeroReader.Read(Inputs(Files()), 16069);
        Assert.AreEqual(PremadeHeroFailure.None, read.Failure);
        var hero = read.Hero!;
        Assert.AreEqual((16069, 16089, 3, 1, 5, 35, 1032, 41, 78, 2, 385),
            (hero.BasicPrototype, hero.Prototype, hero.Race, hero.Gender, hero.Alignment, hero.Age, hero.Portrait, hero.Background,
                hero.AutoLevelScheme, hero.InventorySource, hero.Gold));
        Assert.AreEqual((67, 6), (hero.RaceEffect.Number, hero.RaceEffect.Changes.Count));
        Assert.IsNull(hero.GenderEffect);
        CollectionAssert.AreEqual(new[] { new PremadeHeroItem(6029, 6049, 5, 4021, 6029), new PremadeHeroItem(8168, 8188, 7, 2850, 8168) },
            hero.Items.ToArray());

        // A woman also gets effect 330 (cause gender, stored or not); a set-less prototype has no coins and no items.
        var pc = Pc(gender: 0);
        pc[57] = 0;
        var woman = PremadeHeroReader.Read(Inputs(Files(pc)), 16069).Hero!;
        Assert.AreEqual((330, 2, 0, 0), (woman.GenderEffect!.Number, woman.GenderEffect.Changes.Count, woman.Gold, woman.Items.Count));
        var stored = Pc(gender: 0);
        stored[46] = new Dictionary<int, int> { [0] = 67, [1] = 330 };
        stored[47] = new Dictionary<int, int> { [0] = 0, [1] = 9 };
        Assert.AreEqual(PremadeHeroFailure.None, PremadeHeroReader.Read(Inputs(Files(stored)), 16069).Failure);
    }

    [TestMethod]
    public void PrototypesAndStateTheReaderDoesNotModelAreTypedRefusals()
    {
        PremadeHeroRead Read(Dictionary<int, PrototypeFile> files, MesFile? effects = null, MesFile? buy = null) =>
            PremadeHeroReader.Read(Inputs(files, effects, buy), 16069);
        void Refused(PremadeHeroFailure failure, Dictionary<int, PrototypeFile> files, int? detail = null, MesFile? effects = null, MesFile? buy = null)
        {
            var read = Read(files, effects, buy);
            Assert.AreEqual((failure, (PremadeHero?)null), (read.Failure, read.Hero));
            if (detail is not null) Assert.AreEqual(detail, read.Detail);
        }
        Dictionary<int, PrototypeFile> With(int number, PrototypeFile file)
        {
            var files = Files();
            files[number] = file;
            return files;
        }
        Dictionary<int, PrototypeFile> Changed(Action<Dictionary<int, object>> change)
        {
            var pc = Pc();
            change(pc);
            return Files(pc);
        }
        var good = Files()[16089].Bytes!;

        Refused(PremadeHeroFailure.PrototypeMissing, With(16089, new(PrototypeFileStatus.Missing, null)), 16089);
        Refused(PremadeHeroFailure.PrototypeAmbiguous, With(16089, new(PrototypeFileStatus.Ambiguous, null)));
        Refused(PremadeHeroFailure.PrototypeInvalid, With(16089, new(PrototypeFileStatus.TooLarge, null)));
        Refused(PremadeHeroFailure.PrototypeInvalid, With(16089, new(PrototypeFileStatus.Found, [.. good, 0])), 16089);
        Refused(PremadeHeroFailure.PrototypeInvalid, With(16089, new(PrototypeFileStatus.Found, good[..^1])));
        Refused(PremadeHeroFailure.PrototypeInvalid, With(16089, new(PrototypeFileStatus.Found, Prototype(16090, PcType, Pc()))));
        Refused(PremadeHeroFailure.PrototypeInvalid, With(16089, new(PrototypeFileStatus.Found, new byte[PremadeHeroReader.MaxPrototypeBytes + 1])));
        Refused(PremadeHeroFailure.NotPcPrototype, Files(pcType: 16));
        Refused(PremadeHeroFailure.InvalidRace, Files(Pc(race: 11)), 11);
        Refused(PremadeHeroFailure.InvalidGender, Files(Pc(gender: 2)), 2);
        Refused(PremadeHeroFailure.UnsupportedPrototypeState, Changed(pc => pc[39] = new Dictionary<int, int> { [2] = 4 }));
        Refused(PremadeHeroFailure.UnsupportedPrototypeState, Changed(pc => pc[40] = new Dictionary<int, int> { [0] = 1 }));
        Refused(PremadeHeroFailure.UnsupportedPrototypeState, Changed(pc => ((Dictionary<int, int>)pc[38])[18] = 10));
        Refused(PremadeHeroFailure.UnsupportedPrototypeState, Changed(pc => pc[55] = 1));
        Refused(PremadeHeroFailure.UnsupportedPrototypeState, Changed(pc => pc[56] = 1));
        Refused(PremadeHeroFailure.UnsupportedPrototypeState, Changed(pc => pc[50] = true));
        Refused(PremadeHeroFailure.UnsupportedPrototypeState, Changed(pc => pc[87] = 100));
        Refused(PremadeHeroFailure.UnsupportedPrototypeState, Changed(pc =>
        {
            pc[46] = new Dictionary<int, int> { [0] = 67, [1] = 101 };
            pc[47] = new Dictionary<int, int> { [0] = 0, [1] = 1 };
        }), 101);
        Refused(PremadeHeroFailure.UnsupportedPrototypeState, Changed(pc => pc[46] = new Dictionary<int, int> { [0] = 67, [1] = 102 }), 102);
        Refused(PremadeHeroFailure.UnsupportedPrototypeState, Changed(pc =>
        {
            pc[46] = new Dictionary<int, int> { [0] = 67, [1] = 67 };
            pc[47] = new Dictionary<int, int> { [0] = 0, [1] = 0 };
        }), 67);
        Assert.AreEqual(PremadeHeroFailure.None, Read(Changed(pc => ((Dictionary<int, int>)pc[38])[0] = 12)).Failure,
            "stored primary stats are replaced by the level-1 defaults");

        var effect = Read(Files(Pc(race: 7)));
        Assert.AreEqual((PremadeHeroFailure.EffectRefused, 71, (EffectRuleStatus?)EffectRuleStatus.ZeroDivisor),
            (effect.Failure, effect.Detail, effect.EffectStatus));
        Refused(PremadeHeroFailure.EffectRefused, Files(Pc(gender: 0)), 330, Mes("{67}{dx +1}{330}{st +1x}"));
        var set = Read(Changed(pc => pc[57] = 4));
        Assert.AreEqual((PremadeHeroFailure.InventorySourceRefused, 4, (InventorySourceStatus?)InventorySourceStatus.Malformed),
            (set.Failure, set.Detail, set.SourceStatus));
        // ARC-WORLD-093: a buy line that parses is allowed; a failing one empties the set (BT8).
        Assert.AreEqual(PremadeHeroFailure.None, Read(Files(), buy: Mes("{2}{6029}")).Failure);
        var emptied = Read(Files(), buy: Mes("{2}{1}"));
        Assert.AreEqual((PremadeHeroFailure.InventorySourceRefused, 2, (InventorySourceStatus?)InventorySourceStatus.Emptied),
            (emptied.Failure, emptied.Detail, emptied.SourceStatus));
        Refused(PremadeHeroFailure.RandomInventorySource, Changed(pc => pc[57] = 3), 3);
        // A kit item without a prototype empties the set in the source (ARC-WORLD-093, BT9).
        var missing = Read(With(8188, new(PrototypeFileStatus.Missing, null)));
        Assert.AreEqual((PremadeHeroFailure.InventorySourceRefused, 2, (InventorySourceStatus?)InventorySourceStatus.Emptied),
            (missing.Failure, missing.Detail, missing.SourceStatus));
        Refused(PremadeHeroFailure.ItemPrototypeAmbiguous, With(8188, new(PrototypeFileStatus.Ambiguous, null)), 8188);
        Refused(PremadeHeroFailure.ItemPrototypeInvalid, With(8188, new(PrototypeFileStatus.Found, [1, 2, 3])), 8188);
        Refused(PremadeHeroFailure.ItemTypeUnsupported, With(8188, new(PrototypeFileStatus.Found, Prototype(8188, 8))), 8188);
        Refused(PremadeHeroFailure.ItemTypeUnsupported, With(8188, new(PrototypeFileStatus.Found, Prototype(8188, 3))), 8188);
        foreach (var point in new[] { 4, 21 })
            Refused(PremadeHeroFailure.ItemScriptUnsupported, With(8188, new(PrototypeFileStatus.Found,
                Prototype(8188, 7, new Dictionary<int, object> { [31] = new Dictionary<int, int> { [point] = 30001 } }))), 8188);
        Assert.AreEqual(PremadeHeroFailure.None, Read(With(8188, new(PrototypeFileStatus.Found,
            Prototype(8188, 7, new Dictionary<int, object> { [31] = new Dictionary<int, int> { [0] = 30001, [4] = 0 } })))).Failure,
            "an examine script, and an attachment without a script number, do not run on the transfer");
        Refused(PremadeHeroFailure.PrototypeMissing, With(16089, null!));
        Assert.AreEqual(PremadeHeroFailure.PrototypeMissing, PremadeHeroReader.Read(Inputs(Files()), int.MaxValue).Failure);
        Assert.AreEqual(PremadeHeroFailure.PrototypeMissing, PremadeHeroReader.Read(Inputs(Files()), -21).Failure);
    }

    [TestMethod]
    public void TheDirectoryLookupFindsOneFilePerNumber()
    {
        var root = Path.Combine(Path.GetTempPath(), $"premade-hero-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllBytes(Path.Combine(root, "016089 - PC.PRO"), [1, 2]);
            File.WriteAllBytes(Path.Combine(root, "006049 - Weapon.pro"), [3]);
            File.WriteAllBytes(Path.Combine(root, "006049 - Other.pro"), [4]);
            File.WriteAllBytes(Path.Combine(root, "008188 - Armor.txt"), [5]);
            File.WriteAllBytes(Path.Combine(root, "008189 - Big.pro"), new byte[PremadeHeroReader.MaxPrototypeBytes + 1]);
            var lookup = PremadeHeroReader.DirectoryPrototypes(root);
            var found = lookup(16089);
            Assert.AreEqual(PrototypeFileStatus.Found, found.Status);
            CollectionAssert.AreEqual(new byte[] { 1, 2 }, found.Bytes);
            Assert.AreEqual(PrototypeFileStatus.Ambiguous, lookup(6049).Status);
            Assert.AreEqual(PrototypeFileStatus.Missing, lookup(8188).Status);
            Assert.AreEqual(PrototypeFileStatus.TooLarge, lookup(8189).Status);
            Assert.AreEqual(PrototypeFileStatus.Missing, lookup(1_000_000).Status);
            Assert.AreEqual(PrototypeFileStatus.Unreadable, PremadeHeroReader.DirectoryPrototypes(Path.Combine(root, "absent"))(1).Status);
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
