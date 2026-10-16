using System.Text;
using Arcanum.Data;
using Arcanum.Scripting.Dialog;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Movement;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// The session's player character (ARC-WORLD-015). Prototype, effect, item and name numbers are invented.
[TestClass]
public sealed class HeroStateTests
{
    private static readonly SessionInsertCheck Accept = _ => SessionInsertVerdict.Accepted;

    internal static readonly StatEffectDefinition HalfElfEffect = new(67, [
        new(CharacterStat.Dexterity, StatModifierOperator.Add, 1), new(CharacterStat.Beauty, StatModifierOperator.Add, 1),
        new(CharacterStat.Constitution, StatModifierOperator.Add, -1), new(CharacterStat.MagickPoints, StatModifierOperator.Add, 1)]);
    internal static readonly StatEffectDefinition HalfOgreEffect = new(71, [
        new(CharacterStat.Strength, StatModifierOperator.Add, 4), new(CharacterStat.Intelligence, StatModifierOperator.Add, -4)]);
    internal static readonly StatEffectDefinition FemaleEffect = new(330, [
        new(CharacterStat.Strength, StatModifierOperator.Add, -1), new(CharacterStat.Constitution, StatModifierOperator.Add, 1)]);

    internal static readonly SessionHeroKitItem[] Kit =
    [
        new(new(SessionItemType.Weapon, 6049, 4021, 6029)),
        new(new(SessionItemType.Armor, 8188, 2850, 8168)),
        new(new(SessionItemType.Armor, 8069, 2851, 8049)),
    ];

    internal static int[] Defaults() => [8, 8, 8, 8, 8, 8, 8, 8, 0, 0];

    internal static SessionHeroDefinition HalfElf(int[]? stats = null) => new(16089, CharacterRace.HalfElf, CharacterGender.Male,
        stats ?? Defaults(), 1, 5, 0, HalfElfEffect, null, 385, Kit);

    internal static GameSessionOwner CreateWith(SessionHeroDefinition hero, GameSessionLimits? limits = null)
    {
        var result = GameSessionOwner.Create(Descriptor(limits) with { Hero = hero });
        Assert.AreEqual(GameSessionFailure.None, result.Failure);
        return result.Owner!;
    }

    private static CharacterStatsSnapshot Stats(GameSessionOwner owner) => owner.QueryHeroStats(new(0))!.Value.Snapshot!;

    [TestMethod]
    public void ANewSessionStartsWithTheHeroItsGoldAndItsKit()
    {
        using var owner = CreateWith(HalfElf());
        Assert.AreEqual(new GameSessionVersion(owner.Identity, 0), owner.Version, "the hero is part of the new game");
        var hero = owner.QueryHero()!;
        Assert.AreEqual((16089, CharacterRace.HalfElf, CharacterGender.Male, 1, 5, 0),
            (hero.Prototype, hero.Race, hero.Gender, hero.Level, hero.UnspentPoints, hero.Alignment));
        CollectionAssert.AreEqual(Defaults(), hero.Stats.ToArray());
        Assert.AreEqual(HalfElfEffect, hero.RaceEffect);
        Assert.IsNull(hero.GenderEffect);

        var stats = Stats(owner);
        Assert.AreEqual((8, 9), (stats.GetBase(CharacterStat.Dexterity), stats.GetEffective(CharacterStat.Dexterity)));
        Assert.AreEqual((8, 7), (stats.GetBase(CharacterStat.Constitution), stats.GetEffective(CharacterStat.Constitution)));
        Assert.AreEqual(1, stats.GetEffective(CharacterStat.MagickPoints));
        Assert.AreEqual(4000, stats.GetEffective(CharacterStat.CarryWeight), "derived from effective strength 8");
        Assert.AreEqual(EffectCause.Race, stats.AppliedEffects.Single().Cause);
        Assert.AreEqual(CharacterStatsFailure.InvalidContext, owner.QueryHeroStats(new(101))!.Value.Failure);

        Assert.AreEqual(385, owner.QueryGold());
        CollectionAssert.AreEqual(new[]
        {
            new SessionKitItem("kit:1", Kit[0].Kind), new SessionKitItem("kit:2", Kit[1].Kind), new SessionKitItem("kit:3", Kit[2].Kind),
        }, owner.QueryHeroKit().ToArray());
        Assert.AreEqual(0, owner.QueryPcInventory().Count, "nothing taken from the world yet");
        Assert.AreEqual(new DialogPcFacts(DialogPcGender.Male, 8, false, 3), owner.QueryHeroDialogFacts());
    }

    [TestMethod]
    public void DialogFactsUseTheEffectiveIntelligenceGenderAndSourceRace()
    {
        using var ogre = CreateWith(HalfElf() with { Race = CharacterRace.HalfOgre, RaceEffect = HalfOgreEffect });
        Assert.AreEqual(new DialogPcFacts(DialogPcGender.Male, 4, false, 7), ogre.QueryHeroDialogFacts());
        using var woman = CreateWith(HalfElf() with { Gender = CharacterGender.Female, GenderEffect = FemaleEffect });
        Assert.AreEqual(new DialogPcFacts(DialogPcGender.Female, 8, false, 3), woman.QueryHeroDialogFacts());
        Assert.AreEqual(EffectCause.Gender, Stats(woman).AppliedEffects[1].Cause);
        Assert.AreEqual(7, Stats(woman).GetEffective(CharacterStat.Strength));
        Assert.AreEqual(8, Stats(woman).GetEffective(CharacterStat.Constitution), "race −1, female +1");
        using var dwarf = CreateWith(HalfElf() with { Race = CharacterRace.Dwarf, RaceEffect = null });
        Assert.AreEqual(1, dwarf.QueryHeroDialogFacts()!.Race);

        using var none = Create();
        Assert.IsNull(none.QueryHero());
        Assert.IsNull(none.QueryHeroStats(new(0)));
        Assert.IsNull(none.QueryHeroDialogFacts());
        Assert.AreEqual(0, none.QueryGold());
        Assert.AreEqual(0, none.QueryHeroKit().Count);
        Assert.IsNull(none.Capture().Hero);
    }

    [TestMethod]
    public void TheHerosFactsDecideWhichRepliesADialogOffers()
    {
        // Node 10: a reply for IQ 5 and up, one for IQ 4 and below, one for women, and one for half-elves (RA 4 = race 3).
        var document = DlgReader.Read(Encoding.Latin1.GetBytes(
            Record("10", "?")
            + Record("11", "Smart.", iq: "5", response: "0")
            + Record("12", "Dumb.", iq: "-4", response: "0")
            + Record("13", "Lady.", alternate: "0", iq: "1", response: "0")
            + Record("14", "Half-elf.", iq: "1", condition: "RA 4", response: "0")));
        string[] Offered(SessionHeroDefinition hero)
        {
            using var owner = CreateWith(hero);
            var commands = new NoCommands();
            var talk = DialogConversation.Start(document,
                new DialogConversationRequest(new DialogNodeId(10), new DialogParticipants(new DialogId(700), new(800), new(900)),
                    new DialogScriptLine(0), owner.QueryHeroDialogFacts()!, new DialogSpeakerNames("Ada", "Virgil")),
                new DialogConversationPorts(commands, commands, null), new DialogConversationOptions());
            return talk.Options.Select(option => option.Text).ToArray();
        }

        CollectionAssert.AreEqual(new[] { "Smart.", "Half-elf." }, Offered(HalfElf()));
        CollectionAssert.AreEqual(new[] { "Dumb." }, Offered(HalfElf() with { Race = CharacterRace.HalfOgre, RaceEffect = HalfOgreEffect }));
        CollectionAssert.AreEqual(new[] { "Smart.", "Lady.", "Half-elf." }, Offered(HalfElf() with { Gender = CharacterGender.Female }));
        CollectionAssert.AreEqual(new[] { "Dumb.", "Half-elf." }, Offered(HalfElf(stats: [8, 8, 8, 8, 4, 8, 8, 8, 0, 0])), "base intelligence 4");
    }

    [TestMethod]
    public void AnInvalidHeroIsRefusedBeforeAnyComponentIsBuilt()
    {
        void Refused(SessionHeroDefinition hero, GameSessionLimits? limits = null)
        {
            var result = GameSessionOwner.Create(Descriptor(limits) with { Hero = hero });
            Assert.AreEqual((null, GameSessionFailure.InvalidHero), (result.Owner, result.Failure));
        }
        var hero = HalfElf();
        Refused(hero with { Prototype = 0 });
        Refused(hero with { Race = (CharacterRace)11 });
        Refused(hero with { Gender = (CharacterGender)2 });
        Refused(hero with { Stats = null! });
        Refused(hero with { Stats = [8, 8, 8, 8, 8, 8, 8, 8, 0] });
        Refused(HalfElf(stats: [8, 22, 8, 8, 8, 8, 8, 8, 0, 0]), null);
        Refused(HalfElf(stats: [0, 8, 8, 8, 8, 8, 8, 8, 0, 0]));
        Refused(HalfElf(stats: [8, 8, 8, 8, 8, 8, 8, 8, 211, 0]));
        Refused(hero with { Level = -1 });
        Refused(hero with { Level = 52 });
        Refused(hero with { UnspentPoints = -1 });
        Refused(hero with { UnspentPoints = 57 });
        Refused(hero with { Alignment = 1001 });
        Refused(hero with { Alignment = -1001 });
        Refused(hero with { Gold = -1 });
        Refused(hero with { RaceEffect = new(67, []) });
        Refused(hero with { RaceEffect = new(-1, HalfElfEffect.Modifiers) });
        Refused(hero with { GenderEffect = new(330, [new(CharacterStat.Strength, StatModifierOperator.Divide, 0)]) });
        Refused(hero with { RaceEffect = new(67, [.. Enumerable.Repeat(new StatModifier(CharacterStat.Strength, StatModifierOperator.Add, 1), 8)]) });
        Refused(hero with { Kit = null! });
        Refused(hero with { Kit = [null!] });
        foreach (var type in new[] { SessionItemType.Gold, SessionItemType.Ammo, SessionItemType.Key, SessionItemType.KeyRing, (SessionItemType)4 })
            Refused(hero with { Kit = [new(new(type, 9076, 1, 9056))] });
        Refused(hero, new GameSessionLimits(maxPcItems: 2));

        using var edge = CreateWith(HalfElf(stats: [8, 21, 8, 8, 8, 8, 8, 8, 210, 210]) with
        {
            Level = 51, UnspentPoints = 56, Alignment = -1000, Gold = int.MaxValue, Kit = [],
        }, new GameSessionLimits(maxPcItems: 1));
        Assert.AreEqual(21, Stats(edge).GetEffective(CharacterStat.Dexterity), "the half-elf maximum, effect clamped");
        Assert.AreEqual(0, edge.QueryHeroKit().Count);
    }

    [TestMethod]
    public void TheDefinitionIsCopiedSoLaterChangesCannotReachTheSession()
    {
        var stats = Defaults();
        var kit = Kit.ToList();
        using var owner = CreateWith(HalfElf(stats) with { Kit = kit });
        stats[0] = 3;
        kit.Clear();
        Assert.AreEqual(8, owner.QueryHero()!.Stats[0]);
        Assert.AreEqual(8, Stats(owner).GetBase(CharacterStat.Strength));
        Assert.AreEqual(3, owner.QueryHeroKit().Count);
    }

    [TestMethod]
    public void KitItemsCountTowardTheBudgetAndASeedCannotListThem()
    {
        using var owner = CreateWith(HalfElf(), new GameSessionLimits(maxPcItems: 4));
        Assert.AreEqual(GameSessionDisposition.Changed, owner.AttachMapInventory(Exterior, InventoryStateTests.Seed()).Disposition);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.PickUpItem(owner.Version, "apple", Accept).Disposition);
        CollectionAssert.AreEqual(new[] { "apple" }, owner.QueryPcInventory().Select(item => item.Entity).ToArray());
        Assert.AreEqual(3, owner.QueryHeroKit().Count);
        Assert.AreEqual(GameSessionFailure.BudgetExceeded, owner.PickUpItem(owner.Version, "cog", Accept).Failure, "three kit items and the apple");

        var seed = InventoryStateTests.Seed();
        var clash = seed with { Items = [.. seed.Items, new("kit:2", InventoryStateTests.Facts(SessionItemType.Generic, 5700), new CellCoord(1, 1))] };
        Assert.AreEqual(GameSessionFailure.InventoryMismatch, owner.AttachMapInventory(Cave, clash).Failure);
    }

    // Independently written canonical text of the golden session with the half-elf hero (and a woman's gender effect).
    internal const string HeroJson =
        ",\"hero\":{\"prototype\":16089,\"race\":\"HalfElf\",\"gender\":\"Female\"," +
        "\"stats\":{\"strength\":8,\"dexterity\":8,\"constitution\":8,\"beauty\":8,\"intelligence\":8,\"perception\":8," +
        "\"willpower\":8,\"charisma\":8,\"magickPoints\":0,\"techPoints\":0},\"level\":1,\"unspentPoints\":5,\"alignment\":0," +
        "\"raceEffect\":{\"effect\":67,\"modifiers\":[{\"stat\":\"Dexterity\",\"operator\":\"Add\",\"value\":1}," +
        "{\"stat\":\"Beauty\",\"operator\":\"Add\",\"value\":1},{\"stat\":\"Constitution\",\"operator\":\"Add\",\"value\":-1}," +
        "{\"stat\":\"MagickPoints\",\"operator\":\"Add\",\"value\":1}]}," +
        "\"genderEffect\":{\"effect\":330,\"modifiers\":[{\"stat\":\"Strength\",\"operator\":\"Add\",\"value\":-1}," +
        "{\"stat\":\"Constitution\",\"operator\":\"Add\",\"value\":1}]}}";
    internal const string KitInventory =
        ",\"inventory\":{\"gold\":385,\"kit\":[{\"entity\":\"kit:1\",\"type\":\"Weapon\",\"prototype\":6049,\"name\":4021,\"description\":6029}," +
        "{\"entity\":\"kit:2\",\"type\":\"Armor\",\"prototype\":8188,\"name\":2850,\"description\":8168}," +
        "{\"entity\":\"kit:3\",\"type\":\"Armor\",\"prototype\":8069,\"name\":2851,\"description\":8049}],\"pc\":[]}";
    internal static readonly string HeroGolden = GoldenJson.Replace(NoHero + EmptyInventory, HeroJson + KitInventory);

    private static GameSessionOwner HeroGoldenSession()
    {
        var owner = CreateWith(HalfElf() with { Gender = CharacterGender.Female, GenderEffect = FemaleEffect });
        Changed(owner.SetMapChange(owner.Version, Exterior, MapChangeKey.ForEntity("npc-1", "hp"), 12), owner, 1);
        Changed(owner.SetMapChange(owner.Version, Exterior, MapChangeKey.ForSector(5, "door.open"), 1), owner, 2);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.RecordLighting(new(1, 15)).Disposition);
        return owner;
    }

    [TestMethod]
    public void TheHeroRoundTripsThroughCanonicalTextAndRestore()
    {
        using var owner = HeroGoldenSession();
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        Assert.AreEqual(HeroGolden, text);
        Assert.AreEqual(HeroGolden, GameSessionSnapshotCodec.Encode(Decode(HeroGolden)));

        using var restored = Restore(Decode(text));
        var hero = restored.QueryHero()!;
        Assert.AreEqual((16089, CharacterRace.HalfElf, CharacterGender.Female), (hero.Prototype, hero.Race, hero.Gender));
        Assert.AreEqual(FemaleEffect.EffectId, hero.GenderEffect!.EffectId);
        CollectionAssert.AreEqual(FemaleEffect.Modifiers.ToArray(), hero.GenderEffect.Modifiers.ToArray());
        Assert.AreEqual(Stats(owner).GetEffective(CharacterStat.Constitution), Stats(restored).GetEffective(CharacterStat.Constitution));
        Assert.AreEqual(owner.QueryHeroDialogFacts(), restored.QueryHeroDialogFacts());
        Assert.AreEqual(385, restored.QueryGold());
        CollectionAssert.AreEqual(owner.QueryHeroKit().ToArray(), restored.QueryHeroKit().ToArray());

        // A world item taken after the start is saved beside the kit.
        Assert.AreEqual(GameSessionDisposition.Changed, restored.AttachMapInventory(Exterior, InventoryStateTests.Seed()).Disposition);
        Assert.AreEqual(GameSessionDisposition.Changed, restored.PickUpItem(restored.Version, "apple", Accept).Disposition);
        using var again = Restore(Decode(GameSessionSnapshotCodec.Encode(restored.Capture())));
        CollectionAssert.AreEqual(new[] { "apple" }, again.QueryPcInventory().Select(item => item.Entity).ToArray());
        CollectionAssert.AreEqual(owner.QueryHeroKit().ToArray(), again.QueryHeroKit().ToArray());
    }

    [TestMethod]
    public void DecodeRefusesAMalformedHeroWithTypedFailureAndPath()
    {
        void Fails(string from, string to, SnapshotDecodeFailure failure, string path)
        {
            Assert.IsTrue(HeroGolden.Contains(from, StringComparison.Ordinal), from);
            var result = GameSessionSnapshotCodec.TryDecode(HeroGolden.Replace(from, to));
            Assert.AreEqual((null, failure, path), (result.Snapshot, result.Failure, result.Path), to);
        }
        Fails("\"race\":\"HalfElf\"", "\"race\":\"halfelf\"", SnapshotDecodeFailure.OutOfRange, "$.hero.race");
        Fails("\"gender\":\"Female\"", "\"gender\":0", SnapshotDecodeFailure.WrongType, "$.hero.gender");
        Fails("\"gender\":\"Female\"", "\"gender\":\"Any\"", SnapshotDecodeFailure.OutOfRange, "$.hero.gender");
        Fails("\"prototype\":16089", "\"prototype\":0", SnapshotDecodeFailure.OutOfRange, "$.hero.prototype");
        Fails(",\"techPoints\":0}", "}", SnapshotDecodeFailure.MissingField, "$.hero.stats.techPoints");
        Fails("\"techPoints\":0}", "\"techPoints\":0,\"age\":20}", SnapshotDecodeFailure.UnknownField, "$.hero.stats.age");
        Fails(",\"alignment\":0", "", SnapshotDecodeFailure.MissingField, "$.hero.alignment");
        Fails("\"level\":1,", "\"level\":1,\"xp\":0,", SnapshotDecodeFailure.UnknownField, "$.hero.xp");
        Fails("\"stat\":\"Beauty\"", "\"stat\":\"Age\"", SnapshotDecodeFailure.OutOfRange, "$.hero.raceEffect.modifiers[1].stat");
        Fails("\"operator\":\"Add\",\"value\":-1}", "\"operator\":\"Times\",\"value\":-1}", SnapshotDecodeFailure.OutOfRange,
            "$.hero.raceEffect.modifiers[2].operator");
        Fails("\"effect\":330", "\"effect\":-1", SnapshotDecodeFailure.OutOfRange, "$.hero.genderEffect.effect");
        Fails("\"genderEffect\":{\"effect\":330,\"modifiers\":", "\"genderEffect\":{\"effect\":330,\"modifiers\":{},\"x\":", SnapshotDecodeFailure.UnknownField,
            "$.hero.genderEffect.x");
        Fails(HeroJson, ",\"hero\":[]", SnapshotDecodeFailure.WrongType, "$.hero");
        Fails("\"gold\":385", "\"gold\":-1", SnapshotDecodeFailure.OutOfRange, "$.inventory.gold");
        Fails("\"gold\":385,", "", SnapshotDecodeFailure.MissingField, "$.inventory.gold");
        Fails("\"entity\":\"kit:1\",", "\"entity\":\"kit:1\",\"map\":79,", SnapshotDecodeFailure.UnknownField, "$.inventory.kit[0].map");
        Fails("\"type\":\"Weapon\"", "\"type\":\"Sword\"", SnapshotDecodeFailure.OutOfRange, "$.inventory.kit[0].type");
        Fails("\"pc\":[]", "\"pc\":[{\"entity\":\"kit:2\",\"map\":79,\"type\":\"Armor\",\"prototype\":1,\"name\":2,\"description\":3}]",
            SnapshotDecodeFailure.NotCanonical, "$.inventory.pc[0].entity");
        Fails(",\"kit\":[", ",\"kits\":[", SnapshotDecodeFailure.UnknownField, "$.inventory.kits");
    }

    [TestMethod]
    public void RestoreRefusesHeroStateNoTransitionCanProduce()
    {
        void Refused(string text, GameSessionFailure failure)
        {
            var result = GameSessionOwner.Restore(Decode(text), RestoreOptions());
            Assert.AreEqual((null, failure), (result.Owner, result.Failure), text);
        }
        Refused(HeroGolden.Replace("\"dexterity\":8", "\"dexterity\":22"), GameSessionFailure.InvalidHero);
        Refused(HeroGolden.Replace("\"level\":1", "\"level\":52"), GameSessionFailure.InvalidHero);
        Refused(HeroGolden.Replace("\"unspentPoints\":5", "\"unspentPoints\":-1"), GameSessionFailure.InvalidHero);
        Refused(HeroGolden.Replace("\"alignment\":0", "\"alignment\":1001"), GameSessionFailure.InvalidHero);
        Refused(HeroGolden.Replace("\"genderEffect\":{\"effect\":330,\"modifiers\":[{\"stat\":\"Strength\",\"operator\":\"Add\",\"value\":-1}," +
            "{\"stat\":\"Constitution\",\"operator\":\"Add\",\"value\":1}]}", "\"genderEffect\":{\"effect\":330,\"modifiers\":[]}"), GameSessionFailure.InvalidHero);
        Refused(HeroGolden.Replace("\"value\":-1}", "\"value\":1001}"), GameSessionFailure.InvalidHero);

        // Gold and kit items come only with a hero; the kit is kit:1, kit:2, … in order.
        var noHero = HeroGolden.Replace(HeroJson, NoHero);
        Refused(noHero, GameSessionFailure.InvalidSnapshot);
        Refused(noHero.Replace("\"gold\":385", "\"gold\":0"), GameSessionFailure.InvalidSnapshot);
        Refused(noHero.Replace(KitInventory, ",\"inventory\":{\"gold\":1,\"kit\":[],\"pc\":[]}"), GameSessionFailure.InvalidSnapshot);
        Restore(Decode(noHero.Replace(KitInventory, EmptyInventory))).Dispose();
        Restore(Decode(HeroGolden.Replace(KitInventory, ",\"inventory\":{\"gold\":0,\"kit\":[],\"pc\":[]}"))).Dispose();
        Refused(HeroGolden.Replace("\"entity\":\"kit:1\"", "\"entity\":\"kit:9\""), GameSessionFailure.InvalidSnapshot);
        Refused(HeroGolden.Replace("\"entity\":\"kit:1\"", "\"entity\":\"kit:0\""), GameSessionFailure.InvalidSnapshot);
        Refused(HeroGolden.Replace("\"type\":\"Weapon\"", "\"type\":\"Gold\""), GameSessionFailure.InvalidItem);
        var small = GameSessionOwner.Restore(Decode(HeroGolden), RestoreOptions(new GameSessionLimits(maxPcItems: 2)));
        Assert.AreEqual((null, GameSessionFailure.BudgetExceeded), (small.Owner, small.Failure), "a kit above the caller's limit");
    }

    private static string Record(string id, string text = "", string alternate = "", string iq = "", string condition = "", string response = "", string action = "") =>
        $"{{{id}}}{{{text}}}{{{alternate}}}{{{iq}}}{{{condition}}}{{{response}}}{{{action}}}";

    private sealed class NoCommands : IDialogStateQueryPort, IDialogCommandPort
    {
        public LocalFlagReadResult Read(LocalFlagQuery query) => throw new InvalidOperationException();
        public DialogCommandResult Execute(LocalFlagWriteCommand command) => throw new InvalidOperationException();
        public DialogCommandResult Execute(QuestStateCommand command) => throw new InvalidOperationException();
        public DialogCommandResult Execute(RumorLogCommand command) => throw new InvalidOperationException();
    }
}
