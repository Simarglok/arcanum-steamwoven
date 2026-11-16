using Arcanum.Data.Characters;
using System.Buffers.Binary;
using Arcanum.Data.Scripts;
using Arcanum.Scripting.Dialog;
using Arcanum.Scripting.Scr;
using Arcanum.Simulation.Locks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.HeroStateTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// ARC-SCRIPT-2335 (ledger IV1-IV8): the display-case shape of the temple items' insert-item scripts (a loop for over the containers of the vicinity, a name test on the looped object, change art number on it) under the
// session's item set, with the host's world part answering the vicinity of the item, the names and the art plan. Script numbers, names and art ids are invented.
[TestClass]
public sealed class ItemWorldScriptTests
{
    private const byte Counter = 0, Triggerer = 0, Attachee = 1, LocalVariable = 2, Number = 3, Looped = 10, LocalObject = 11, EveryContainer = 17, AnyContainer = 18, EveryFollower = 2, AnyFollower = 3, EveryScenery = 15, AnyScenery = 16, Everyone = 8, Anyone = 9;
    private const int Every = 810, Any = 811, Twice = 812, EveryFollowerScript = 813, AnyFollowerScript = 814, ToggleSelf = 815, ToggleSelfTwice = 816, ToggleHero = 817, Quest = 820, Typed = 821, Assigned = 822, DeadGated = 823, GeneratorGated = 824, GeneratorSlot = 7, ScenerySweep = 850, ScenerySweepAny = 851, CritterCalls = 852, CritterCallsAny = 853, Callee = 3500, CalleeLine = 4, ScenerySwitch = 854, CaseName = 1205, NewArt = 4, BaselineArt = 100, QuestNumber = 1005, ContainerType = 2,
        SetQuest = 830, CompleteQuest = 831, GiveSmall = 832, GiveBig = 833, GiveTwice = 834, SetThenRead = 835, SetOnItem = 836, SetInvalid = 837, CompleteBig = 838, SetThenGive = 839, GiveThenStat = 840, StatThenGive = 841, GiveThenRatio = 842, RatioThenGive = 843, SetCallRead = 844, GiveThenXp = 845, XpThenGive = 846, SetThenStop = 847, XpEffect = 60;
    private const int StoryRaise = 880, StoryReadBack = 881, RumorOfHero = 882, RumorOfItem = 883, RumorOutOfRange = 884, RenameSelf = 885, RenameSelfTwice = 886, RenameHero = 887, RumorNumber = 1500, RenamedTo = 2805, DataName = 3100;
    private static readonly SessionInsertCheck Accept = _ => SessionInsertVerdict.Accepted;

    private sealed record Clause(int Type, (byte Kind, int Value)[] Operands);
    private static Clause C(int type, params (byte, int)[] operands) => new(type, operands);
    private static (Clause, Clause, Clause) Do(Clause action) => (C(0), action, C(0));
    private static (Clause, Clause, Clause) If(Clause condition, Clause action) => (condition, action, C(0));
    private static (byte, int) N(int value) => (Number, value);

    private static ScrProgram Program(params (Clause Condition, Clause OnTrue, Clause OnFalse)[] lines)
    {
        var bytes = new byte[64 + lines.Length * 132];
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(52), lines.Length);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(56), lines.Length);
        for (var line = 0; line < lines.Length; line++)
            foreach (var (offset, clause) in new[] { (0, lines[line].Condition), (44, lines[line].OnTrue), (88, lines[line].OnFalse) })
            {
                var slot = bytes.AsSpan(64 + line * 132 + offset, 44);
                BinaryPrimitives.WriteInt32LittleEndian(slot, clause.Type);
                for (var i = 0; i < clause.Operands.Length; i++) { slot[4 + i] = clause.Operands[i].Kind; BinaryPrimitives.WriteInt32LittleEndian(slot[(12 + 4 * i)..], clause.Operands[i].Value); }
            }
        return ScrProgram.Read(bytes).Program!;
    }

    // ARC-SCRIPT-2376 (IV25): the dialog loops of the installed scripts: the looped critter that carries the name is sent a deferred call into a dialog script (call script in: script, line, the hero as triggerer, the looped one as attachee, 2 seconds).
    private static ScrProgram CallLoop(byte listKind) => Program(Do(C(ScrCatalogue.Action.LoopFor, (listKind, 0))),
        If(C(ScrCatalogue.Condition.IsNamed, (Looped, 0), N(CaseName)), C(ScrCatalogue.Action.CallScriptIn, N(Callee), N(CalleeLine), (Triggerer, 0), (Looped, 0), N(2))),
        Do(C(ScrCatalogue.Action.LoopEnd)), Do(C(ScrCatalogue.Action.ReturnRunDefault)));

    private static ScrProgram Loop(byte listKind, byte testKind, int times = 1)
    {
        var lines = new List<(Clause, Clause, Clause)> { Do(C(ScrCatalogue.Action.LoopFor, (listKind, 0))) };
        for (var i = 0; i < times; i++) lines.Add(If(C(ScrCatalogue.Condition.IsNamed, (testKind, 0), N(CaseName)), C(ScrCatalogue.Action.ChangeArtNumber, (Looped, 0), N(NewArt))));
        lines.Add(Do(C(ScrCatalogue.Action.LoopEnd)));
        lines.Add(Do(C(ScrCatalogue.Action.ReturnRunDefault)));
        return Program([.. lines]);
    }

    // ARC-SCRIPT-2347 (IV13): the same display case, gated on the hero's quest state, on the looped object's type read into a variable and on the looped object kept in an object variable.
    private static ScrProgram QuestGated(int state) => Program(Do(C(ScrCatalogue.Action.LoopFor, (EveryContainer, 0))),
        If(C(ScrCatalogue.Condition.PcQuestState, (Triggerer, 0), N(QuestNumber), N(state)), C(ScrCatalogue.Action.ChangeArtNumber, (Looped, 0), N(NewArt))),
        Do(C(ScrCatalogue.Action.LoopEnd)), Do(C(ScrCatalogue.Action.ReturnRunDefault)));

    private static ScrProgram TypeGated(int type) => Program(Do(C(ScrCatalogue.Action.LoopFor, (EveryContainer, 0))),
        Do(C(ScrCatalogue.Action.GetObjectType, (Looped, 0), (LocalVariable, 0))),
        If(C(ScrCatalogue.Condition.Equal, (LocalVariable, 0), N(type)), C(ScrCatalogue.Action.ChangeArtNumber, (Looped, 0), N(NewArt))),
        Do(C(ScrCatalogue.Action.LoopEnd)), Do(C(ScrCatalogue.Action.ReturnRunDefault)));

    private static ScrProgram AssignGated() => Program(Do(C(ScrCatalogue.Action.LoopFor, (EveryContainer, 0))),
        Do(C(ScrCatalogue.Action.AssignObject, (LocalObject, 0), (Looped, 0))),
        If(C(ScrCatalogue.Condition.IsNamed, (LocalObject, 0), N(CaseName)), C(ScrCatalogue.Action.ChangeArtNumber, (LocalObject, 0), N(NewArt))),
        Do(C(ScrCatalogue.Action.LoopEnd)), Do(C(ScrCatalogue.Action.ReturnRunDefault)));

    private static ScrProgram Gated(Clause condition) => Program(Do(C(ScrCatalogue.Action.LoopFor, (EveryContainer, 0))),
        If(condition, C(ScrCatalogue.Action.ChangeArtNumber, (Looped, 0), N(NewArt))), Do(C(ScrCatalogue.Action.LoopEnd)), Do(C(ScrCatalogue.Action.ReturnRunDefault)));

    // ARC-SCRIPT-2374 (IV18–IV21): set PC quest state (16) of the triggerer and give quest experience (63), in the shapes the installed scripts use.
    private static ScrProgram SetQuestOf(byte kind, int quest, int state) => Program(Do(C(ScrCatalogue.Action.SetPcQuestState, (kind, 0), N(quest), N(state))), Do(C(ScrCatalogue.Action.ReturnRunDefault)));

    private static ScrProgram Give(int level, int times = 1)
    {
        var lines = new List<(Clause, Clause, Clause)>();
        for (var i = 0; i < times; i++) lines.Add(Do(C(ScrCatalogue.Action.GiveQuestExperience, (Triggerer, 0), N(level))));
        lines.Add(Do(C(ScrCatalogue.Action.ReturnRunDefault)));
        return Program([.. lines]);
    }

    // The write, then the same quest read back in the same run: the later read sees the earlier write (the original applies as it goes).
    private static ScrProgram SetAndRead(int quest, int state) => Program(Do(C(ScrCatalogue.Action.SetPcQuestState, (Triggerer, 0), N(quest), N(state))),
        Do(C(ScrCatalogue.Action.LoopFor, (EveryContainer, 0))),
        If(C(ScrCatalogue.Condition.PcQuestState, (Triggerer, 0), N(quest), N(state)), C(ScrCatalogue.Action.ChangeArtNumber, (Looped, 0), N(NewArt))),
        Do(C(ScrCatalogue.Action.LoopEnd)), Do(C(ScrCatalogue.Action.ReturnRunDefault)));

    // ARC-SCRIPT-2410 (IV19): the write is a call-ex callee's (line 5 of the same script), the read is the caller's after it: the callee's write is applied as the run goes.
    private static ScrProgram SetCallAndRead(int quest, int state) => Program(Do(C(ScrCatalogue.Action.CallScriptEx, (Attachee, 0), N(SessionScriptPoint.InsertItem), N(5), (Triggerer, 0))),
        Do(C(ScrCatalogue.Action.LoopFor, (EveryContainer, 0))),
        If(C(ScrCatalogue.Condition.PcQuestState, (Triggerer, 0), N(quest), N(state)), C(ScrCatalogue.Action.ChangeArtNumber, (Looped, 0), N(NewArt))),
        Do(C(ScrCatalogue.Action.LoopEnd)), Do(C(ScrCatalogue.Action.ReturnRunDefault)),
        Do(C(ScrCatalogue.Action.SetPcQuestState, (Triggerer, 0), N(quest), N(state))), Do(C(ScrCatalogue.Action.ReturnRunDefault)));

    // ARC-SCRIPT-2410 (IV20): an award and an experience gain effect of the hero's list in one run, in either order (the award reads the list as the actions before it left it).
    private static ScrProgram GiveThenAddXp() => Program(Do(C(ScrCatalogue.Action.GiveQuestExperience, (Triggerer, 0), N(1))),
        Do(C(ScrCatalogue.Action.AddEffect, (Triggerer, 0), N(XpEffect), N(5))), Do(C(ScrCatalogue.Action.ReturnRunDefault)));

    // ARC-SCRIPT-2410: a quest write, then a call-ex of a point outside the script points: the dispatcher ends the run with its effects dropped.
    private static ScrProgram SetThenCallOutsidePoints() => Program(Do(C(ScrCatalogue.Action.SetPcQuestState, (Triggerer, 0), N(1004), N(4))),
        Do(C(ScrCatalogue.Action.CallScriptEx, (Attachee, 0), N(99), N(0), (Triggerer, 0))), Do(C(ScrCatalogue.Action.ReturnRunDefault)));

    private static ScrProgram AddXpThenGive() => Program(Do(C(ScrCatalogue.Action.AddEffect, (Triggerer, 0), N(XpEffect), N(5))),
        Do(C(ScrCatalogue.Action.GiveQuestExperience, (Triggerer, 0), N(1))), Do(C(ScrCatalogue.Action.ReturnRunDefault)));

    private static ScrProgram SetAndGive(int quest, int state, int level) => Program(Do(C(ScrCatalogue.Action.SetPcQuestState, (Triggerer, 0), N(quest), N(state))),
        Do(C(ScrCatalogue.Action.GiveQuestExperience, (Triggerer, 0), N(level))), Do(C(ScrCatalogue.Action.ReturnRunDefault)));

    // ARC-SCRIPT-2394: the hero's level (get stat 17) read after / before a quest experience award of the same run.
    private static ScrProgram GiveAndStat(int level, bool statFirst)
    {
        var give = Do(C(ScrCatalogue.Action.GiveQuestExperience, (Triggerer, 0), N(level)));
        var read = Do(C(ScrCatalogue.Action.GetStat, (Number, GameSessionOwner.ScriptStatLevel), (Triggerer, 0), (Counter, 0)));
        return statFirst ? Program(read, give, Do(C(ScrCatalogue.Action.ReturnRunDefault))) : Program(give, read, Do(C(ScrCatalogue.Action.ReturnRunDefault)));
    }

    // ARC-SCRIPT-2394: the power ratio (aptitude) read after / before the award: an auto-level scheme may buy the magick and tech points the aptitude derives from.
    private static ScrProgram GiveAndRatio(int level, bool ratioFirst)
    {
        var give = Do(C(ScrCatalogue.Action.GiveQuestExperience, (Triggerer, 0), N(level)));
        var read = Do(C(ScrCatalogue.Action.MagickTechAdjustment, N(10), (Attachee, 0), (Triggerer, 0), (Counter, 0)));
        return ratioFirst ? Program(read, give, Do(C(ScrCatalogue.Action.ReturnRunDefault))) : Program(give, read, Do(C(ScrCatalogue.Action.ReturnRunDefault)));
    }

    // ARC-SCRIPT-2346: toggle state (56) of one object, `times` times.
    private static ScrProgram Toggle(byte kind, int times = 1)
    {
        var lines = new List<(Clause, Clause, Clause)>();
        for (var i = 0; i < times; i++) lines.Add(Do(C(ScrCatalogue.Action.ToggleState, (kind, 0))));
        lines.Add(Do(C(ScrCatalogue.Action.ReturnRunDefault)));
        return Program([.. lines]);
    }

    // ARC-SCRIPT-2375 (IV28-IV30): the story-state, set rumor and rename shapes of the installed point 21 scripts (1094, 1103, 1566, 1573), with invented numbers.
    private static ScrProgram StoryRaiseProgram() => Program(Do(C(ScrCatalogue.Action.GetStoryState, (LocalVariable, 0))), Do(C(ScrCatalogue.Action.SetStoryState, N(19))), Do(C(ScrCatalogue.Action.ReturnRunDefault)));

    // A raise the same run reads back, then a rumor gated on what it read (5).
    private static ScrProgram StoryReadBackProgram() => Program(Do(C(ScrCatalogue.Action.SetStoryState, N(5))), Do(C(ScrCatalogue.Action.GetStoryState, (LocalVariable, 0))),
        If(C(ScrCatalogue.Condition.Equal, (LocalVariable, 0), N(5)), C(ScrCatalogue.Action.SetRumor, N(RumorNumber), (Triggerer, 0))), Do(C(ScrCatalogue.Action.ReturnRunDefault)));

    private static ScrProgram RumorProgram(byte who, int rumor = RumorNumber) => Program(Do(C(ScrCatalogue.Action.SetRumor, N(rumor), (who, 0))), Do(C(ScrCatalogue.Action.ReturnRunDefault)));

    private static ScrProgram RenameProgram(byte who, int times = 1)
    {
        var lines = new List<(Clause, Clause, Clause)>();
        for (var i = 0; i < times; i++) lines.Add(Do(C(ScrCatalogue.Action.Rename, (who, 0), N(RenamedTo + i))));
        lines.Add(Do(C(ScrCatalogue.Action.ReturnRunDefault)));
        return Program([.. lines]);
    }

    private sealed class Host(int script) : ISessionItemScriptHost, ISessionItemWorldHost
    {
        public Dictionary<int, ScrProgram> Programs { get; } = new() { [Every] = Loop(EveryContainer, Looped), [Any] = Loop(EveryContainer, AnyContainer), [Twice] = Loop(EveryContainer, Looped, 2),
            [Quest] = QuestGated(2), [Typed] = TypeGated(ContainerType), [Assigned] = AssignGated(),
            [DeadGated] = Gated(C(ScrCatalogue.Condition.IsDead, (Looped, 0))), [GeneratorGated] = Gated(C(ScrCatalogue.Condition.MonsterGenDisabled, N(GeneratorSlot))),
            [ScenerySweep] = Loop(EveryScenery, Looped), [ScenerySwitch] = Program(Do(C(ScrCatalogue.Action.LoopFor, (EveryScenery, 0))), Do(C(ScrCatalogue.Action.ToggleState, (Looped, 0))), Do(C(ScrCatalogue.Action.LoopEnd)), Do(C(ScrCatalogue.Action.ReturnRunDefault))), [ScenerySweepAny] = Loop(EveryScenery, AnyScenery), [CritterCalls] = CallLoop(Everyone), [CritterCallsAny] = CallLoop(Anyone),
            [EveryFollowerScript] = Loop(EveryFollower, Looped), [AnyFollowerScript] = Loop(EveryFollower, AnyFollower),
            [SetQuest] = SetQuestOf(Triggerer, 1004, 2), [CompleteQuest] = SetQuestOf(Triggerer, 1004, 4), [GiveSmall] = Give(1), [GiveBig] = Give(5), [GiveTwice] = Give(1, 2),
            [SetThenRead] = SetAndRead(1004, 2), [SetOnItem] = SetQuestOf(Attachee, 1004, 4), [SetInvalid] = SetQuestOf(Triggerer, 5, 2), [CompleteBig] = SetQuestOf(Triggerer, 1003, 4), [SetThenGive] = SetAndGive(1004, 4, 1), [GiveThenStat] = GiveAndStat(5, false), [StatThenGive] = GiveAndStat(5, true), [GiveThenRatio] = GiveAndRatio(5, false), [RatioThenGive] = GiveAndRatio(5, true), [SetCallRead] = SetCallAndRead(1004, 2), [GiveThenXp] = GiveThenAddXp(), [XpThenGive] = AddXpThenGive(), [SetThenStop] = SetThenCallOutsidePoints(),
            [ToggleSelf] = Toggle(Attachee), [ToggleSelfTwice] = Toggle(Attachee, 2), [ToggleHero] = Toggle(Triggerer),
            [StoryRaise] = StoryRaiseProgram(), [StoryReadBack] = StoryReadBackProgram(), [RumorOfHero] = RumorProgram(Triggerer), [RumorOfItem] = RumorProgram(Attachee), [RumorOutOfRange] = RumorProgram(Triggerer, 3000),
            [RenameSelf] = RenameProgram(Attachee), [RenameSelfTwice] = RenameProgram(Attachee, 2), [RenameHero] = RenameProgram(Triggerer) };
        public Dictionary<string, int> Names { get; set; } = new() { ["case"] = CaseName, ["other"] = 3000 };
        public string[]? Vicinity { get; set; } = ["case", "other"];
        public bool? DeadCase { get; set; } = true;
        public int? QuestStateOfHero { get; set; } = 2;
        public int? TypeOfContainers { get; set; } = ContainerType;
        public List<(ScrObjectRef Object, int Quest)> QuestReads { get; } = [];
        public bool Plans { get; set; } = true;
        public bool PlanBaseline { get; set; }
        public bool? DataOff { get; set; } = false;
        public int? NameInData { get; set; } = DataName;
        public SessionOpenableBaseline? Openable { get; set; }
        public List<ScrObjectRef> Centres { get; } = [];
        public string[]? Followers { get; set; } = ["case", "other"];
        public string[]? Scenery { get; set; } = ["case", "other"];
        public string[]? Critters { get; set; } = ["case", "other"];
        public List<ScrObjectRef> SceneryCentres { get; } = [];
        public List<ScrObjectRef> CritterCentres { get; } = [];
        public List<ScrObjectRef> Leaders { get; } = [];

        public ScrSlot? Slot(ScrObjectRef attachee, int point) => attachee.Code == "oid:tome" && point == SessionScriptPoint.InsertItem ? new ScrSlot(script, 0, 0) : null;
        public ScrScriptLookup Script(int number) => Programs.TryGetValue(number, out var program) ? ScrScriptLookup.Of(program) : ScrScriptLookup.Missing;
        public bool GlobalFlag(int index) => throw new InvalidOperationException("the session reads flags itself");
        public int StoryState() => throw new InvalidOperationException("not admitted");
        public bool IsMonsterGenerator(ScrObjectRef attachee) => false;
        public ScrDialogState DialogState(ScrObjectRef attachee) => ScrDialogState.Normal;
        public ScrTrapCheck CheckTrapBeforeUse(ScrObjectRef triggerer, ScrObjectRef attachee) => ScrTrapCheck.NoTrap;
        public bool IsTrapObject(ScrObjectRef attachee) => false;
        public ScrObjectRef? Player() => new ScrObjectRef(Exterior.RuntimeMap, SessionEventKinds.PlayerEntity);
        public ScrObjectRef? Leader(ScrObjectRef critter) => null;
        public bool IsJilted(ScrObjectRef critter) => false;
        public bool IsPc(ScrObjectRef critter) => critter.Code == SessionEventKinds.PlayerEntity;
        public SessionPrintedLine PrintedLine(int script, int line) => new(SessionPrintedLineStatus.Shown, $"line {line}", "test");
        public SessionEffectDefinition? EffectDefinition(int number) => number == XpEffect ? XpGainEffect : null;

        public IReadOnlyList<ScrObjectRef>? ContainersInVicinity(ScrObjectRef item)
        {
            Centres.Add(item);
            return Vicinity?.Select(code => new ScrObjectRef(item.Map, code)).ToArray();
        }

        public IReadOnlyList<ScrObjectRef>? SceneryInVicinity(ScrObjectRef item)
        {
            SceneryCentres.Add(item);
            return Scenery?.Select(code => new ScrObjectRef(item.Map, code)).ToArray();
        }

        public IReadOnlyList<ScrObjectRef>? CrittersInVicinity(ScrObjectRef item)
        {
            CritterCentres.Add(item);
            return Critters?.Select(code => new ScrObjectRef(item.Map, code)).ToArray();
        }

        public IReadOnlyList<ScrObjectRef>? FollowersOf(ScrObjectRef leader)
        {
            Leaders.Add(leader);
            return Followers?.Select(code => new ScrObjectRef(leader.Map, code)).ToArray();
        }

        public int? NameOf(ScrObjectRef obj) => Names.TryGetValue(obj.Code, out var name) ? name : null;
        public bool? IsMindControlled(ScrObjectRef obj) => Names.ContainsKey(obj.Code) ? false : null;

        public int? QuestState(ScrObjectRef obj, int quest)
        {
            QuestReads.Add((obj, quest));
            return QuestStateOfHero;
        }

        // "case" is the dead one (the host decides what its objects are), "other" is alive.
        public bool? IsDead(ScrObjectRef obj) => obj.Code == "case" ? DeadCase : obj.Code == "other" ? false : null;

        // The other container ("other") has a different type, which the typed script must leave alone.
        public int? ObjectType(ScrObjectRef obj) => obj.Code == "case" ? TypeOfContainers : obj.Code == "other" ? ContainerType + 1 : null;

        public bool? ItemOffInData(ScrObjectRef item) => DataOff;
        public int? ItemNameInData(ScrObjectRef item) => NameInData;

        public SessionObjectArtPlan? ArtPlan(ScrObjectRef obj, int number) => !Plans ? null
            : new(new SessionMapId(obj.Map), obj.Code, PlanBaseline ? BaselineArt : (uint)number, BaselineArt, Openable is null ? null : new SessionOpenable(new SessionMapId(obj.Map), obj.Code), Openable);
    }

    // withExperience: the session holds experience tables (ExperienceStateTests.Rules: level 2 at 1010 points, quest experience level 1 gives 700 and level 5 1300, quest 1003 is level 5, every other quest level 1).
    private static GameSessionOwner Owner(bool withExperience = false, SessionExperienceRules? rules = null, int experience = 0)
    {
        var created = GameSessionOwner.Create(Descriptor() with { Hero = HalfElf([8, 8, 8, 8, 14, 8, 8, 8, 0, 0]) with { Experience = experience }, Experience = rules ?? (withExperience ? ExperienceStateTests.Rules() : null) });
        Assert.AreEqual(GameSessionFailure.None, created.Failure);
        var owner = created.Owner!;
        var seed = InventoryStateTests.Seed();
        var tome = new SessionSeedItem("oid:tome", InventoryStateTests.Facts(SessionItemType.Generic, 5700, 0, 0, null, SessionScriptPoint.InsertItem), new Arcanum.Simulation.Movement.CellCoord(300, 200));
        Assert.AreEqual(GameSessionDisposition.Changed, owner.AttachMapInventory(Exterior, seed with { Items = [.. seed.Items, tome] }).Disposition);
        return owner;
    }

    private static uint? Art(GameSessionOwner owner, string entity) => owner.QueryObjectArt(Exterior, entity);

    [TestMethod]
    public void The_pickup_changes_the_art_of_the_named_container_around_the_item_and_leaves_the_others()
    {
        using var owner = Owner();
        var host = new Host(Every);
        var picked = owner.PickUpItem(owner.Version, "oid:tome", Accept, host);
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None), (picked.Disposition, picked.Failure));
        Assert.AreEqual(new ScrObjectRef(Exterior.RuntimeMap, "oid:tome"), host.Centres.Single(), "the vicinity is asked of the item, the attachee");
        Assert.AreEqual(NewArt, (int?)Art(owner, "case"));
        Assert.IsNull(Art(owner, "other"), "a container with another name keeps its art");
        using var restored = Restore(Decode(GameSessionSnapshotCodec.Encode(owner.Capture())));
        Assert.AreEqual(NewArt, (int?)Art(restored, "case"), "the written art survives a save");
    }

    [TestMethod]
    public void An_any_container_condition_is_true_when_one_container_in_the_vicinity_passes()
    {
        using var owner = Owner();
        Assert.AreEqual(GameSessionDisposition.Changed, owner.PickUpItem(owner.Version, "oid:tome", Accept, new Host(Any)).Disposition);
        // The list condition (kind 18, "any") is true because "case" is named, so every looped container changes, "other" too.
        Assert.AreEqual((NewArt, NewArt), ((int?)Art(owner, "case"), (int?)Art(owner, "other")));
    }

    // ARC-SCRIPT-2376 (IV25, PG3): the scenery kinds 15 and 16 list the scenery around the item, asked of the host's world part.
    [TestMethod]
    public void Every_scenery_loop_changes_the_art_of_the_named_scenery_around_the_item()
    {
        using var owner = Owner();
        var host = new Host(ScenerySweep);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.PickUpItem(owner.Version, "oid:tome", Accept, host).Disposition);
        Assert.AreEqual(new ScrObjectRef(Exterior.RuntimeMap, "oid:tome"), host.SceneryCentres.Single(), "the scenery is asked of the item, the attachee");
        Assert.AreEqual(NewArt, (int?)Art(owner, "case"));
        Assert.IsNull(Art(owner, "other"), "scenery with another name keeps its art");
        Assert.AreEqual(0, host.CritterCentres.Count + host.Centres.Count);
    }

    [TestMethod]
    public void An_any_scenery_condition_is_true_when_one_scenery_in_the_vicinity_passes()
    {
        using var owner = Owner();
        Assert.AreEqual(GameSessionDisposition.Changed, owner.PickUpItem(owner.Version, "oid:tome", Accept, new Host(ScenerySweepAny)).Disposition);
        Assert.AreEqual((NewArt, NewArt), ((int?)Art(owner, "case"), (int?)Art(owner, "other")));
    }

    // ARC-SCRIPT-2376 (IV25, TG4): everyone and anyone in the vicinity list the PCs and NPCs around the item; the looped critter that passes the name test gets a deferred call into a dialog script (call script in), kept in the event queue.
    [TestMethod]
    public void The_critter_loop_schedules_a_dialog_call_for_the_named_critter_around_the_item()
    {
        foreach (var script in new[] { CritterCalls, CritterCallsAny })
        {
            using var owner = Owner();
            var host = new Host(script);
            var picked = owner.PickUpItem(owner.Version, "oid:tome", Accept, host);
            Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None), (picked.Disposition, picked.Failure));
            Assert.AreEqual(new ScrObjectRef(Exterior.RuntimeMap, "oid:tome"), host.CritterCentres.Single(), "the critters are asked of the item, the attachee");
            var call = owner.QueryScriptCalls().Single();
            Assert.AreEqual(new SessionScriptCall(Callee, CalleeLine, new(Exterior, SessionEventKinds.PlayerEntity), new(Exterior, "case")), call.Call, "only the named critter is called, with the hero as triggerer and itself as attachee");
            using var restored = Restore(Decode(GameSessionSnapshotCodec.Encode(owner.Capture())));
            Assert.AreEqual(call, restored.QueryScriptCalls().Single(), "the call survives a save");
        }
    }

    // Script 1239 toggles the looped scenery: the item set admits the loop, but the session keeps the off flag of an item of the move only, so the move is refused whole (IV25).
    [TestMethod]
    public void A_toggle_of_the_looped_scenery_is_refused_and_changes_nothing()
    {
        using var owner = Owner();
        var before = owner.Version;
        var refused = owner.PickUpItem(before, "oid:tome", Accept, new Host(ScenerySwitch));
        Assert.AreEqual((GameSessionDisposition.Rejected, GameSessionFailure.ItemScriptUnsupported, before), (refused.Disposition, refused.Failure, owner.Version));
    }

    [TestMethod]
    public void A_critter_loop_without_a_named_critter_schedules_nothing()
    {
        using var owner = Owner();
        var picked = owner.PickUpItem(owner.Version, "oid:tome", Accept, new Host(CritterCalls) { Critters = ["other"] });
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None, 0), (picked.Disposition, picked.Failure, owner.QueryScriptCalls().Count));
        using var empty = Owner();
        Assert.AreEqual(GameSessionDisposition.Changed, empty.PickUpItem(empty.Version, "oid:tome", Accept, new Host(CritterCalls) { Critters = [] }).Disposition);
        Assert.AreEqual(0, empty.QueryScriptCalls().Count);
    }

    // ARC-SCRIPT-2336 (FK8): the follower focus kinds 2 and 3 of a point 21 script list the triggerer's direct followers, asked of the host's world part.
    [TestMethod]
    public void Every_follower_loop_changes_the_art_of_the_named_follower_of_the_triggerer()
    {
        using var owner = Owner();
        var host = new Host(EveryFollowerScript);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.PickUpItem(owner.Version, "oid:tome", Accept, host).Disposition);
        Assert.AreEqual(new ScrObjectRef(Exterior.RuntimeMap, SessionEventKinds.PlayerEntity), host.Leaders.Single(), "the followers are the triggerer's, the hero's for a pickup");
        Assert.AreEqual(NewArt, (int?)Art(owner, "case"));
        Assert.IsNull(Art(owner, "other"), "a follower with another name keeps its art");
    }

    [TestMethod]
    public void An_any_follower_condition_is_true_when_one_follower_passes()
    {
        using var owner = Owner();
        Assert.AreEqual(GameSessionDisposition.Changed, owner.PickUpItem(owner.Version, "oid:tome", Accept, new Host(AnyFollowerScript)).Disposition);
        Assert.AreEqual((NewArt, NewArt), ((int?)Art(owner, "case"), (int?)Art(owner, "other")), "kind 3 is true because \"case\" is named, so every looped follower changes");
    }

    [TestMethod]
    public void A_triggerer_without_followers_leaves_the_loop_empty()
    {
        using var owner = Owner();
        Assert.AreEqual(GameSessionDisposition.Changed, owner.PickUpItem(owner.Version, "oid:tome", Accept, new Host(EveryFollowerScript) { Followers = [] }).Disposition);
        Assert.IsNull(Art(owner, "case"));
    }

    [TestMethod]
    public void A_container_plan_resets_the_closed_frame_with_the_new_art()
    {
        using var owner = Owner();
        var baseline = new SessionOpenableBaseline(OpenableKind.Container, 0, 0, 3);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.PickUpItem(owner.Version, "oid:tome", Accept, new Host(Every) { Openable = baseline }).Disposition);
        Assert.AreEqual(0, owner.QueryOpenable(new SessionOpenable(Exterior, "case"), baseline).Frame, "the action closes the container");
    }

    [TestMethod]
    public void A_quest_state_condition_reads_the_hero_through_the_host()
    {
        using var owner = Owner();
        var host = new Host(Quest);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.PickUpItem(owner.Version, "oid:tome", Accept, host).Disposition);
        Assert.AreEqual((NewArt, NewArt), ((int?)Art(owner, "case"), (int?)Art(owner, "other")), "the state equals the one the script names, so every looped container changes");
        Assert.IsTrue(host.QuestReads.All(read => read.Quest == QuestNumber && read.Object.Code == SessionEventKinds.PlayerEntity), "the hero is the triggerer of a pickup");

        using var other = Owner();
        var different = new Host(Quest) { QuestStateOfHero = 3 };
        Assert.AreEqual(GameSessionDisposition.Changed, other.PickUpItem(other.Version, "oid:tome", Accept, different).Disposition);
        Assert.IsNull(Art(other, "case"), "another state changes nothing");
    }

    [TestMethod]
    public void Get_object_type_reads_the_type_of_the_looped_object_and_assign_object_keeps_it()
    {
        using var typed = Owner();
        Assert.AreEqual(GameSessionDisposition.Changed, typed.PickUpItem(typed.Version, "oid:tome", Accept, new Host(Typed)).Disposition);
        Assert.AreEqual((NewArt, (int?)null), ((int?)Art(typed, "case"), (int?)Art(typed, "other")), "only the container of the named type changes");

        using var assigned = Owner();
        Assert.AreEqual(GameSessionDisposition.Changed, assigned.PickUpItem(assigned.Version, "oid:tome", Accept, new Host(Assigned)).Disposition);
        Assert.AreEqual((NewArt, (int?)null), ((int?)Art(assigned, "case"), (int?)Art(assigned, "other")), "the object variable holds the looped object the name test and the art change read");
    }

    [TestMethod]
    public void The_dead_condition_reads_the_host_and_the_generator_condition_reads_the_session_table()
    {
        using var dead = Owner();
        Assert.AreEqual(GameSessionDisposition.Changed, dead.PickUpItem(dead.Version, "oid:tome", Accept, new Host(DeadGated)).Disposition);
        Assert.AreEqual((NewArt, (int?)null), ((int?)Art(dead, "case"), (int?)Art(dead, "other")), "only the container the host calls dead changes");

        using var open = Owner();
        Assert.AreEqual(GameSessionDisposition.Changed, open.PickUpItem(open.Version, "oid:tome", Accept, new Host(GeneratorGated)).Disposition);
        Assert.IsNull(Art(open, "case"), "an unblocked generator slot does not pass the condition");

        using var blocked = Owner();
        Assert.AreEqual(GameSessionDisposition.Changed, blocked.ToggleGeneratorBlock(blocked.Version, GeneratorSlot).Disposition);
        Assert.AreEqual(GameSessionDisposition.Changed, blocked.PickUpItem(blocked.Version, "oid:tome", Accept, new Host(GeneratorGated)).Disposition);
        Assert.AreEqual((NewArt, NewArt), ((int?)Art(blocked, "case"), (int?)Art(blocked, "other")), "a blocked slot passes it for every looped container");
    }

    // The experience gain special (9) with +50: an award of 100 gives 150 under it.
    private static readonly SessionEffectDefinition XpGainEffect = new(XpEffect, [new(EffectAttributeKind.Special, 9, EffectChangeOperator.Add, 50)]);

    private static int Points(GameSessionOwner owner) => owner.QueryHeroExperience()!.Experience;

    // ARC-SCRIPT-2374 (IV18, IV19): set PC quest state of the hero in a pickup's insert-item run changes the quest log in the move's own revision, survives a save, and a completion pays the quest's experience.
    [TestMethod]
    public void Set_pc_quest_state_writes_the_hero_quest_in_the_moves_own_revision()
    {
        using var owner = Owner(withExperience: true);
        var before = owner.Version.Revision;
        Assert.AreEqual(GameSessionDisposition.Changed, owner.PickUpItem(owner.Version, "oid:tome", Accept, new Host(SetQuest)).Disposition);
        Assert.AreEqual((SessionQuestState.Accepted, before + 1), (owner.QueryQuest(1004)!.Value.Effective, owner.Version.Revision), "one revision for the pickup and the quest write");
        Assert.AreEqual(0, Points(owner));
        using var restored = Restore(Decode(GameSessionSnapshotCodec.Encode(owner.Capture())), RestoreOptions() with { Experience = ExperienceStateTests.Rules() });
        Assert.AreEqual(SessionQuestState.Accepted, restored.QueryQuest(1004)!.Value.Effective, "the quest survives a save");

        using var done = Owner(withExperience: true);
        Assert.AreEqual(GameSessionDisposition.Changed, done.PickUpItem(done.Version, "oid:tome", Accept, new Host(CompleteQuest)).Disposition);
        Assert.AreEqual((SessionQuestState.Completed, SessionQuestState.Completed, 700), (done.QueryQuest(1004)!.Value.Effective, done.QueryGlobalQuest(1004), Points(done)), "a completion completes it for everyone and pays the quest's experience");
    }

    // ARC-SCRIPT-2410 (IV19): the original applies a quest write as the run goes, so a read after it in the same run (a call-ex callee's write included) answers the written state, not the one the move began with.
    [TestMethod]
    public void A_quest_read_after_a_quest_write_of_the_same_run_answers_the_written_state()
    {
        foreach (var script in new[] { SetThenRead, SetCallRead })
        {
            using var owner = Owner();
            var host = new Host(script) { QuestStateOfHero = 0 };
            Assert.AreEqual(GameSessionDisposition.Changed, owner.PickUpItem(owner.Version, "oid:tome", Accept, host).Disposition, $"script {script}");
            Assert.AreEqual(SessionQuestState.Accepted, owner.QueryQuest(1004)!.Value.Effective);
            Assert.AreEqual((NewArt, NewArt), ((int?)Art(owner, "case"), (int?)Art(owner, "other")), $"script {script}: the read saw the write, so the art changed");
            Assert.AreEqual(0, host.QuestReads.Count, "the planned state answers, the host is not asked");
        }
    }

    [TestMethod]
    public void A_run_ended_with_its_effects_dropped_after_a_quest_write_refuses_the_move()
    {
        using var owner = Owner(withExperience: true);
        var before = owner.Version;
        var refused = owner.PickUpItem(before, "oid:tome", Accept, new Host(SetThenStop));
        Assert.AreEqual((GameSessionDisposition.Rejected, GameSessionFailure.ItemScriptUnsupported, before, 0), (refused.Disposition, refused.Failure, owner.Version, Points(owner)));
        Assert.IsNull(owner.QueryQuest(1004));
    }

    [TestMethod]
    public void A_quest_write_names_the_hero_only_and_a_botched_or_ended_quest_stays()
    {
        using var item = Owner();
        Assert.AreEqual(GameSessionDisposition.Changed, item.PickUpItem(item.Version, "oid:tome", Accept, new Host(SetOnItem)).Disposition);
        Assert.IsNull(item.QueryQuest(1004), "an item is no PC: the original's function does nothing for it");
        Assert.AreEqual(SessionQuestState.Accepted, item.QueryGlobalQuest(1004));

        using var ended = Owner();
        Assert.AreEqual(GameSessionDisposition.Changed, ended.SetQuestState(ended.Version, 1004, SessionQuestState.Botched).Disposition);
        var revision = ended.Version.Revision;
        Assert.AreEqual(GameSessionDisposition.Changed, ended.PickUpItem(ended.Version, "oid:tome", Accept, new Host(CompleteQuest)).Disposition);
        Assert.AreEqual((SessionQuestState.Botched, revision + 1), (ended.QueryQuest(1004)!.Value.Effective, ended.Version.Revision), "a botched quest takes no completion; the pickup is the move's one revision");
    }

    [TestMethod]
    public void Give_quest_experience_gives_the_level_experience_and_a_second_award_in_the_move_follows()
    {
        using var small = Owner(withExperience: true);
        Assert.AreEqual(GameSessionDisposition.Changed, small.PickUpItem(small.Version, "oid:tome", Accept, new Host(GiveSmall)).Disposition);
        Assert.AreEqual(700, Points(small));

        // Without tables the award is nothing (the use host skips it): the pickup goes on.
        using var none = Owner();
        Assert.AreEqual(GameSessionDisposition.Changed, none.PickUpItem(none.Version, "oid:tome", Accept, new Host(GiveSmall)).Disposition);

        // Two awards in a move (700 and 700) apply in order over the hero as the first left it: the second passes the level (1010), the move's last award (ARC-SCRIPT-2410, ARC-SCRIPT-2394).
        foreach (var script in new[] { GiveTwice, SetThenGive })
        {
            using var owner = Owner(withExperience: true);
            Assert.AreEqual(GameSessionDisposition.Changed, owner.PickUpItem(owner.Version, "oid:tome", Accept, new Host(script)).Disposition, $"script {script}");
            Assert.AreEqual((1400, 2, 1), (Points(owner), owner.QueryHero()!.Level, owner.TakeItemScriptAwards().Count), $"script {script}: both awards are given, the level written once");
        }
    }

    // ARC-SCRIPT-2410 (IV20): a second award is planned over the hero as the first left it, level-free awards apply in order; the second reads the hero as the first left it (it may raise the level, as the move's last award), and none is planned over a level-raising one.
    [TestMethod]
    public void A_second_award_in_one_move_applies_in_order_and_none_follows_one_that_raises_the_level()
    {
        var rules = ExperienceStateTests.SmallQuestRules();
        foreach (var script in new[] { GiveTwice, SetThenGive })
        {
            using var owner = Owner(rules: rules);
            var before = owner.Version.Revision;
            Assert.AreEqual(GameSessionDisposition.Changed, owner.PickUpItem(owner.Version, "oid:tome", Accept, new Host(script)).Disposition, $"script {script}");
            Assert.AreEqual((200, 1, before + 1), (Points(owner), owner.QueryHero()!.Level, owner.Version.Revision), $"script {script}: two awards of 100, one revision");
        }
        using var done = Owner(rules: rules);
        Assert.AreEqual(GameSessionDisposition.Changed, done.PickUpItem(done.Version, "oid:tome", Accept, new Host(SetThenGive)).Disposition);
        Assert.AreEqual(SessionQuestState.Completed, done.QueryQuest(1004)!.Value.Effective);

        // At 900 points the first award (to 1000) is level-free and the second (to 1100) raises the level, the move's last award: applied, and queued for the host (ARC-SCRIPT-2394).
        using var edge = Owner(rules: rules, experience: 900);
        Assert.AreEqual(GameSessionDisposition.Changed, edge.PickUpItem(edge.Version, "oid:tome", Accept, new Host(GiveTwice)).Disposition);
        Assert.AreEqual((1100, 2, 1), (Points(edge), edge.QueryHero()!.Level, edge.TakeItemScriptAwards().Count), "the second award read the hero as the first left it (1000) and wrote the level");

        // At 950 the first award raises the level: a further award is not planned over it (the scheme's purchases read the stats the first changed), refused with nothing applied.
        using var raised = Owner(rules: rules, experience: 950);
        var version = raised.Version;
        var refused = raised.PickUpItem(version, "oid:tome", Accept, new Host(GiveTwice));
        Assert.AreEqual((GameSessionDisposition.Rejected, GameSessionFailure.ItemScriptUnsupported, version, 950), (refused.Disposition, refused.Failure, raised.Version, Points(raised)));
        using var single = Owner(rules: rules, experience: 900);
        Assert.AreEqual(GameSessionDisposition.Changed, single.PickUpItem(single.Version, "oid:tome", Accept, new Host(GiveSmall)).Disposition);
        Assert.AreEqual(1000, Points(single), "one award alone stays under the level");
    }

    // ARC-SCRIPT-2410 (IV20): the award reads the hero's experience gain effect as the actions before it left the list: an effect change that follows the award in the run leaves it alone, one that precedes it in the same run is refused.
    [TestMethod]
    public void An_award_reads_the_experience_gain_effect_in_action_order()
    {
        var rules = ExperienceStateTests.SmallQuestRules();
        using var after = Owner(rules: rules);
        Assert.AreEqual(GameSessionDisposition.Changed, after.PickUpItem(after.Version, "oid:tome", Accept, new Host(GiveThenXp)).Disposition);
        Assert.AreEqual(100, Points(after), "the award came before the effect: no gain bonus");
        Assert.IsTrue(after.QueryHeroEffects()!.Effects.Any(e => e.Number == XpEffect));

        using var before = Owner(rules: rules);
        var version = before.Version;
        var refused = before.PickUpItem(version, "oid:tome", Accept, new Host(XpThenGive));
        Assert.AreEqual((GameSessionDisposition.Rejected, GameSessionFailure.ItemScriptUnsupported, version, 0), (refused.Disposition, refused.Failure, before.Version, Points(before)), "the effect comes before the award in the same run: the run's collected change is not known to the award");
        Assert.IsFalse(before.QueryHeroEffects()!.Effects.Any(e => e.Number == XpEffect));
    }

    // ARC-SCRIPT-2394 (IV20): an award that raises the hero's level is applied with the level written and queued for the host (the level-up messages, the followers' step); one that does not raise a level queues its follower step too.
    [TestMethod]
    public void An_award_that_raises_a_level_is_applied_and_queued_for_the_host()
    {
        foreach (var script in new[] { GiveBig, CompleteBig })
        {
            using var owner = Owner(withExperience: true);
            var before = owner.Version.Revision;
            var moved = owner.PickUpItem(owner.Version, "oid:tome", Accept, new Host(script));
            Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None, before + 1), (moved.Disposition, moved.Failure, owner.Version.Revision), $"script {script}: the pickup is the move's one revision");
            var hero = owner.QueryHeroExperience()!;
            Assert.AreEqual((1300, 2), (hero.Experience, hero.Level), $"script {script}: the experience is added and the level written");
            var awards = owner.TakeItemScriptAwards();
            Assert.AreEqual(1, awards.Count);
            Assert.IsTrue(awards[0].LevelsGained == 1 && awards[0].Followers is not null && awards[0].LevelUps is { Count: 1 }, "the level step and the level-up message reach the host");
            Assert.AreEqual(0, owner.TakeItemScriptAwards().Count, "taken once");
            using var restored = Restore(Decode(GameSessionSnapshotCodec.Encode(owner.Capture())), RestoreOptions() with { Experience = ExperienceStateTests.Rules() });
            Assert.AreEqual(2, restored.QueryHeroExperience()!.Level, "the level survives a save");
        }
        using var small = Owner(withExperience: true);
        Assert.AreEqual(GameSessionDisposition.Changed, small.PickUpItem(small.Version, "oid:tome", Accept, new Host(GiveSmall)).Disposition);
        Assert.IsTrue(small.TakeItemScriptAwards().All(award => award.LevelsGained == 0 && award.LevelUps is null), "an award that writes no level carries no message");
    }

    [TestMethod]
    public void A_refused_or_unchanged_move_queues_no_award()
    {
        // The first award (950 + 100) raises the level: a further award is not planned over it and the move is refused.
        using var owner = Owner(rules: ExperienceStateTests.SmallQuestRules(), experience: 950);
        Assert.AreEqual(GameSessionFailure.ItemScriptUnsupported, owner.PickUpItem(owner.Version, "oid:tome", Accept, new Host(GiveTwice)).Failure);
        Assert.AreEqual(0, owner.TakeItemScriptAwards().Count);
    }

    [TestMethod]
    public void A_stat_read_after_a_level_raising_award_refuses_the_move_and_one_before_it_passes()
    {
        using var after = Owner(withExperience: true);
        var before = after.Version;
        var refused = after.PickUpItem(before, "oid:tome", Accept, new Host(GiveThenStat));
        Assert.AreEqual((GameSessionDisposition.Rejected, GameSessionFailure.ItemScriptUnsupported, before, 0), (refused.Disposition, refused.Failure, after.Version, Points(after)), "the run read the level the award has raised in the original");
        Assert.AreEqual(0, after.TakeItemScriptAwards().Count);

        using var first = Owner(withExperience: true);
        var passed = first.PickUpItem(first.Version, "oid:tome", Accept, new Host(StatThenGive));
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None, 2), (passed.Disposition, passed.Failure, first.QueryHeroExperience()!.Level), "a read before the award sees the level the session holds");

        // The power ratio is the aptitude, which the points an auto-level scheme buys move: the same order rule.
        using var ratio = Owner(withExperience: true);
        var ratioBefore = ratio.Version;
        var ratioRefused = ratio.PickUpItem(ratioBefore, "oid:tome", Accept, new Host(GiveThenRatio));
        Assert.AreEqual((GameSessionDisposition.Rejected, GameSessionFailure.ItemScriptUnsupported, ratioBefore), (ratioRefused.Disposition, ratioRefused.Failure, ratio.Version));
        using var ratioFirst = Owner(withExperience: true);
        var ratioPassed = ratioFirst.PickUpItem(ratioFirst.Version, "oid:tome", Accept, new Host(RatioThenGive));
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None), (ratioPassed.Disposition, ratioPassed.Failure), "a ratio read before the award is the move's order");
    }

    [TestMethod]
    public void A_quest_number_outside_the_log_refuses_the_move()
    {
        using var owner = Owner();
        var before = owner.Version;
        var refused = owner.PickUpItem(before, "oid:tome", Accept, new Host(SetInvalid));
        Assert.AreEqual((GameSessionDisposition.Rejected, GameSessionFailure.ItemScriptUnsupported, before), (refused.Disposition, refused.Failure, owner.Version));
    }

    [TestMethod]
    public void A_quest_state_object_type_or_death_the_host_cannot_answer_refuses_the_move()
    {
        foreach (var host in new Host[] { new(Typed) { TypeOfContainers = null }, new(DeadGated) { DeadCase = null }, new(Quest) { QuestStateOfHero = null } })
        {
            using var owner = Owner();
            var before = owner.Version;
            var refused = owner.PickUpItem(before, "oid:tome", Accept, host);
            Assert.AreEqual((GameSessionDisposition.Rejected, GameSessionFailure.ItemScriptUnsupported, before), (refused.Disposition, refused.Failure, owner.Version));
            Assert.IsNull(Art(owner, "case"));
        }
    }

    [TestMethod]
    public void A_move_the_host_cannot_answer_is_refused_and_changes_nothing()
    {
        foreach (var host in new Host[] { new(Every) { Vicinity = null }, new(ScenerySweep) { Scenery = null }, new(CritterCalls) { Critters = null }, new(EveryFollowerScript) { Followers = null }, new(Every) { Plans = false }, new(Every) { Names = [] }, new(Twice) })
        {
            using var owner = Owner();
            var before = owner.Version;
            var refused = owner.PickUpItem(before, "oid:tome", Accept, host);
            Assert.AreEqual((GameSessionDisposition.Rejected, GameSessionFailure.ItemScriptUnsupported), (refused.Disposition, refused.Failure));
            Assert.AreEqual(before, owner.Version);
            Assert.IsNull(Art(owner, "case"));
        }
    }

    [TestMethod]
    public void A_host_without_a_world_part_refuses_the_script()
    {
        using var owner = Owner();
        var before = owner.Version;
        var refused = owner.PickUpItem(before, "oid:tome", Accept, new WorldlessHost(new Host(Every)));
        Assert.AreEqual((GameSessionFailure.ItemScriptUnsupported, before), (refused.Failure, owner.Version));
        Assert.IsNull(Art(owner, "case"));
    }

    [TestMethod]
    public void A_plan_that_returns_the_art_its_map_data_gives_writes_no_record()
    {
        using var owner = Owner();
        Assert.AreEqual(GameSessionDisposition.Changed, owner.PickUpItem(owner.Version, "oid:tome", Accept, new Host(Every) { PlanBaseline = true }).Disposition);
        Assert.IsNull(Art(owner, "case"), "the baseline id clears the record, as SetObjectArt does");
    }

    private static bool Off(GameSessionOwner owner, bool dataOff = false) => owner.QueryItemOff(Exterior, "oid:tome", dataOff);

    [TestMethod]
    public void A_toggle_state_of_the_attachee_switches_the_item_off_in_the_moves_revision_and_the_save_keeps_it()
    {
        using var owner = Owner();
        var before = owner.Version;
        var picked = owner.PickUpItem(before, "oid:tome", Accept, new Host(ToggleSelf));
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None), (picked.Disposition, picked.Failure));
        Assert.AreEqual(before.Revision + 1, owner.Version.Revision, "one revision for the whole move");
        Assert.IsTrue(Off(owner));
        Assert.AreEqual(1, owner.QueryObjectChange(Exterior, SessionItemOff.Key("oid:tome")));
        using var restored = Restore(Decode(GameSessionSnapshotCodec.Encode(owner.Capture())));
        Assert.IsTrue(Off(restored), "the off flag survives a save");
    }

    [TestMethod]
    public void Two_toggles_in_one_run_leave_the_item_as_it_was_and_write_no_record()
    {
        using var owner = Owner();
        Assert.AreEqual(GameSessionFailure.None, owner.PickUpItem(owner.Version, "oid:tome", Accept, new Host(ToggleSelfTwice)).Failure);
        Assert.IsFalse(Off(owner));
        Assert.IsNull(owner.QueryObjectChange(Exterior, SessionItemOff.Key("oid:tome")));
    }

    [TestMethod]
    public void A_toggle_of_an_item_the_data_stores_off_switches_it_on_with_an_explicit_record()
    {
        using var owner = Owner();
        Assert.IsTrue(Off(owner, dataOff: true), "no record: the data's bit");
        Assert.AreEqual(GameSessionFailure.None, owner.PickUpItem(owner.Version, "oid:tome", Accept, new Host(ToggleSelf) { DataOff = true }).Failure);
        Assert.IsFalse(Off(owner, dataOff: true));
        Assert.AreEqual(0, owner.QueryObjectChange(Exterior, SessionItemOff.Key("oid:tome")));
    }

    [TestMethod]
    public void A_toggle_the_session_cannot_keep_is_refused_and_changes_nothing()
    {
        // The host has no row for the item, or the object is the hero (the item set keeps no flag of a critter).
        foreach (var host in new Host[] { new(ToggleSelf) { DataOff = null }, new(ToggleHero) })
        {
            using var owner = Owner();
            var before = owner.Version;
            var refused = owner.PickUpItem(before, "oid:tome", Accept, host);
            Assert.AreEqual((GameSessionDisposition.Rejected, GameSessionFailure.ItemScriptUnsupported, before), (refused.Disposition, refused.Failure, owner.Version));
            Assert.IsFalse(Off(owner));
        }
    }

    // The stubs of the run's host adapter throw for what the item set must never ask; a widening of the set has to answer them first.
    [TestMethod]
    public void The_item_set_keeps_to_the_questions_the_session_answers()
    {
        var set = SessionItemScripts.Admission;
        foreach (var action in new[] { ScrCatalogue.Action.Random, ScrCatalogue.Action.DistanceBetween, ScrCatalogue.Action.GetLocation, ScrCatalogue.Action.FloatLine })
            Assert.IsFalse(set.Actions.Contains(action), $"action {action}");
        foreach (var condition in new[] { ScrCatalogue.Condition.HasItemNamed, ScrCatalogue.Condition.WithinRange })
            Assert.IsFalse(set.Conditions.Contains(condition), $"condition {condition}");
        // The adapter answers IScrGroupQueries for the follower kinds only (ARC-SCRIPT-2336): the group kinds 13 and 14 (followers plus the triggerer) stay out until a slice reads them for the item host.
        foreach (var kind in new[] { ScrFocusKind.EveryoneInGroup, ScrFocusKind.AnyoneInGroup })
            Assert.IsFalse(set.FocusKinds.Contains(kind), $"focus {kind}");
        // ARC-SCRIPT-2376 (IV25): the scenery and critter vicinity kinds are answered by the host's world part now, and call script in stays admitted.
        foreach (var kind in new[] { ScrFocusKind.EverySceneryInVicinity, ScrFocusKind.AnySceneryInVicinity, ScrFocusKind.EveryoneInVicinity, ScrFocusKind.AnyoneInVicinity })
            Assert.IsTrue(set.FocusKinds.Contains(kind), $"focus {kind}");
        Assert.IsTrue(set.Actions.Contains(ScrCatalogue.Action.CallScriptIn));
        // ARC-SCRIPT-2347 (IV13): the quest state condition, assign object and get object type are answered by the host's world part now.
        Assert.IsTrue(set.Conditions.Contains(ScrCatalogue.Condition.PcQuestState) && set.Actions.Contains(ScrCatalogue.Action.AssignObject) && set.Actions.Contains(ScrCatalogue.Action.GetObjectType));
        Assert.IsTrue(set.IsSubsetOf(ScrAdmissionSet.S65));
    }

    // ARC-SCRIPT-2375 (IV28): get and set story state of a point 21 script.
    [TestMethod]
    public void A_set_story_state_raises_the_number_in_the_moves_revision_and_the_save_keeps_it()
    {
        using var owner = Owner();
        var before = owner.Version;
        var picked = owner.PickUpItem(before, "oid:tome", Accept, new Host(StoryRaise));
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None), (picked.Disposition, picked.Failure));
        Assert.AreEqual(before.Revision + 1, owner.Version.Revision, "one revision for the whole move");
        Assert.AreEqual(19, owner.QueryStoryState());
        using var restored = Restore(Decode(GameSessionSnapshotCodec.Encode(owner.Capture())));
        Assert.AreEqual(19, restored.QueryStoryState());
    }

    [TestMethod]
    public void A_set_story_state_below_the_current_number_changes_nothing_and_the_number_never_falls()
    {
        using var owner = Owner();
        Assert.AreNotEqual(GameSessionDisposition.Rejected, owner.RaiseStoryState(owner.Version, 25).Disposition);
        Assert.AreEqual(GameSessionFailure.None, owner.PickUpItem(owner.Version, "oid:tome", Accept, new Host(StoryRaise)).Failure);
        Assert.AreEqual(25, owner.QueryStoryState());
    }

    [TestMethod]
    public void A_run_reads_the_story_state_it_raised_and_a_rumor_follows_for_the_hero()
    {
        using var owner = Owner();
        Assert.AreEqual(GameSessionFailure.None, owner.PickUpItem(owner.Version, "oid:tome", Accept, new Host(StoryReadBack)).Failure);
        Assert.AreEqual(5, owner.QueryStoryState());
        Assert.IsNotNull(owner.QueryRumor(RumorNumber), "the condition that read 5 held");
    }

    // ARC-SCRIPT-2375 (IV29): set rumor (SS4): the hero learns it at the game time; any other object's request does nothing.
    [TestMethod]
    public void A_set_rumor_for_the_hero_is_learned_in_the_moves_revision_and_the_save_keeps_it()
    {
        using var owner = Owner();
        var before = owner.Version;
        Assert.IsNull(owner.QueryRumor(RumorNumber));
        Assert.AreEqual(GameSessionFailure.None, owner.PickUpItem(before, "oid:tome", Accept, new Host(RumorOfHero)).Failure);
        Assert.AreEqual(before.Revision + 1, owner.Version.Revision, "one revision for the whole move");
        var learned = owner.QueryRumor(RumorNumber);
        Assert.IsNotNull(learned);
        using var restored = Restore(Decode(GameSessionSnapshotCodec.Encode(owner.Capture())));
        Assert.AreEqual(learned, restored.QueryRumor(RumorNumber));
    }

    [TestMethod]
    public void A_set_rumor_for_an_object_that_is_not_the_hero_learns_nothing_and_does_not_refuse_the_move()
    {
        using var owner = Owner();
        var picked = owner.PickUpItem(owner.Version, "oid:tome", Accept, new Host(RumorOfItem));
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None), (picked.Disposition, picked.Failure));
        Assert.IsNull(owner.QueryRumor(RumorNumber));
    }

    [TestMethod]
    public void A_set_rumor_outside_the_sessions_range_refuses_the_move_and_changes_nothing()
    {
        using var owner = Owner();
        var before = owner.Version;
        var refused = owner.PickUpItem(before, "oid:tome", Accept, new Host(RumorOutOfRange));
        Assert.AreEqual((GameSessionDisposition.Rejected, GameSessionFailure.ItemScriptUnsupported, before), (refused.Disposition, refused.Failure, owner.Version));
    }

    // ARC-SCRIPT-2375 (IV30): rename (92, CQ5) writes the name number of an item of the move.
    private static long? Renamed(GameSessionOwner owner) => owner.QueryObjectChange(Exterior, SessionItemName.Key("oid:tome"));

    [TestMethod]
    public void A_rename_of_the_attachee_item_writes_its_name_number_in_the_moves_revision_and_the_save_keeps_it()
    {
        using var owner = Owner();
        var before = owner.Version;
        var picked = owner.PickUpItem(before, "oid:tome", Accept, new Host(RenameSelf));
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None), (picked.Disposition, picked.Failure));
        Assert.AreEqual(before.Revision + 1, owner.Version.Revision, "one revision for the whole move");
        Assert.AreEqual(RenamedTo, (int?)Renamed(owner));
        using var restored = Restore(Decode(GameSessionSnapshotCodec.Encode(owner.Capture())));
        Assert.AreEqual(RenamedTo, (int?)Renamed(restored), "the name number survives a save");
    }

    [TestMethod]
    public void A_rename_to_the_number_the_data_gives_writes_no_record_and_a_second_rename_follows_the_first()
    {
        using var owner = Owner();
        Assert.AreEqual(GameSessionFailure.None, owner.PickUpItem(owner.Version, "oid:tome", Accept, new Host(RenameSelf) { NameInData = RenamedTo }).Failure);
        Assert.IsNull(Renamed(owner), "the data's number clears the record");
        using var twice = Owner();
        Assert.AreEqual(GameSessionFailure.None, twice.PickUpItem(twice.Version, "oid:tome", Accept, new Host(RenameSelfTwice)).Failure);
        Assert.AreEqual(RenamedTo + 1, (int?)Renamed(twice), "the last rename of the run stands");
    }

    [TestMethod]
    public void A_rename_the_session_cannot_keep_is_refused_and_changes_nothing()
    {
        // The host reads no data name for the item (a made item has no row), or the object is the hero (its name number is not modelled).
        foreach (var host in new Host[] { new(RenameSelf) { NameInData = null }, new(RenameHero) })
        {
            using var owner = Owner();
            var before = owner.Version;
            var refused = owner.PickUpItem(before, "oid:tome", Accept, host);
            Assert.AreEqual((GameSessionDisposition.Rejected, GameSessionFailure.ItemScriptUnsupported, before), (refused.Disposition, refused.Failure, owner.Version));
            Assert.IsNull(Renamed(owner));
        }
    }

    [TestMethod]
    public void The_story_rumor_and_rename_commands_are_in_the_item_set_and_the_dialog_stays_out()
    {
        var set = SessionItemScripts.Admission;
        foreach (var action in new[] { ScrCatalogue.Action.GetStoryState, ScrCatalogue.Action.SetStoryState, ScrCatalogue.Action.SetRumor, ScrCatalogue.Action.Rename })
            Assert.IsTrue(set.Actions.Contains(action), $"action {action}");
        Assert.IsFalse(set.Actions.Contains(ScrCatalogue.Action.Dialog));
    }

    // The same host with the world part hidden: the question is not answerable.
    private sealed class WorldlessHost(Host inner) : ISessionItemScriptHost
    {
        public ScrSlot? Slot(ScrObjectRef attachee, int point) => inner.Slot(attachee, point);
        public ScrScriptLookup Script(int number) => inner.Script(number);
        public bool GlobalFlag(int index) => inner.GlobalFlag(index);
        public int StoryState() => inner.StoryState();
        public bool IsMonsterGenerator(ScrObjectRef attachee) => false;
        public ScrDialogState DialogState(ScrObjectRef attachee) => ScrDialogState.Normal;
        public ScrTrapCheck CheckTrapBeforeUse(ScrObjectRef triggerer, ScrObjectRef attachee) => ScrTrapCheck.NoTrap;
        public bool IsTrapObject(ScrObjectRef attachee) => false;
        public ScrObjectRef? Player() => inner.Player();
        public ScrObjectRef? Leader(ScrObjectRef critter) => null;
        public bool IsJilted(ScrObjectRef critter) => false;
        public bool IsPc(ScrObjectRef critter) => inner.IsPc(critter);
        public SessionPrintedLine PrintedLine(int script, int line) => inner.PrintedLine(script, line);
        public SessionEffectDefinition? EffectDefinition(int number) => null;
    }
}
