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
    private const byte Triggerer = 0, Attachee = 1, LocalVariable = 2, Number = 3, Looped = 10, LocalObject = 11, EveryContainer = 17, AnyContainer = 18, EveryFollower = 2, AnyFollower = 3;
    private const int Every = 810, Any = 811, Twice = 812, EveryFollowerScript = 813, AnyFollowerScript = 814, ToggleSelf = 815, ToggleSelfTwice = 816, ToggleHero = 817, Quest = 820, Typed = 821, Assigned = 822, DeadGated = 823, GeneratorGated = 824, GeneratorSlot = 7, CaseName = 1205, NewArt = 4, BaselineArt = 100, QuestNumber = 1005, ContainerType = 2;
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

    // ARC-SCRIPT-2346: toggle state (56) of one object, `times` times.
    private static ScrProgram Toggle(byte kind, int times = 1)
    {
        var lines = new List<(Clause, Clause, Clause)>();
        for (var i = 0; i < times; i++) lines.Add(Do(C(ScrCatalogue.Action.ToggleState, (kind, 0))));
        lines.Add(Do(C(ScrCatalogue.Action.ReturnRunDefault)));
        return Program([.. lines]);
    }

    private sealed class Host(int script) : ISessionItemScriptHost, ISessionItemWorldHost
    {
        public Dictionary<int, ScrProgram> Programs { get; } = new() { [Every] = Loop(EveryContainer, Looped), [Any] = Loop(EveryContainer, AnyContainer), [Twice] = Loop(EveryContainer, Looped, 2),
            [Quest] = QuestGated(2), [Typed] = TypeGated(ContainerType), [Assigned] = AssignGated(),
            [DeadGated] = Gated(C(ScrCatalogue.Condition.IsDead, (Looped, 0))), [GeneratorGated] = Gated(C(ScrCatalogue.Condition.MonsterGenDisabled, N(GeneratorSlot))),
            [EveryFollowerScript] = Loop(EveryFollower, Looped), [AnyFollowerScript] = Loop(EveryFollower, AnyFollower),
            [ToggleSelf] = Toggle(Attachee), [ToggleSelfTwice] = Toggle(Attachee, 2), [ToggleHero] = Toggle(Triggerer) };
        public Dictionary<string, int> Names { get; set; } = new() { ["case"] = CaseName, ["other"] = 3000 };
        public string[]? Vicinity { get; set; } = ["case", "other"];
        public bool? DeadCase { get; set; } = true;
        public int? QuestStateOfHero { get; set; } = 2;
        public int? TypeOfContainers { get; set; } = ContainerType;
        public List<(ScrObjectRef Object, int Quest)> QuestReads { get; } = [];
        public bool Plans { get; set; } = true;
        public bool PlanBaseline { get; set; }
        public bool? DataOff { get; set; } = false;
        public SessionOpenableBaseline? Openable { get; set; }
        public List<ScrObjectRef> Centres { get; } = [];
        public string[]? Followers { get; set; } = ["case", "other"];
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
        public SessionEffectDefinition? EffectDefinition(int number) => null;

        public IReadOnlyList<ScrObjectRef>? ContainersInVicinity(ScrObjectRef item)
        {
            Centres.Add(item);
            return Vicinity?.Select(code => new ScrObjectRef(item.Map, code)).ToArray();
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

        public SessionObjectArtPlan? ArtPlan(ScrObjectRef obj, int number) => !Plans ? null
            : new(new SessionMapId(obj.Map), obj.Code, PlanBaseline ? BaselineArt : (uint)number, BaselineArt, Openable is null ? null : new SessionOpenable(new SessionMapId(obj.Map), obj.Code), Openable);
    }

    private static GameSessionOwner Owner()
    {
        var owner = CreateWith(HalfElf([8, 8, 8, 8, 14, 8, 8, 8, 0, 0]));
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
        Assert.AreEqual(GameSessionDisposition.Changed, owner.PickUpItem(owner.Version, "tome", Accept, host).Disposition);
        Assert.AreEqual((NewArt, NewArt), ((int?)Art(owner, "case"), (int?)Art(owner, "other")), "the state equals the one the script names, so every looped container changes");
        Assert.IsTrue(host.QuestReads.All(read => read.Quest == QuestNumber && read.Object.Code == SessionEventKinds.PlayerEntity), "the hero is the triggerer of a pickup");

        using var other = Owner();
        var different = new Host(Quest) { QuestStateOfHero = 3 };
        Assert.AreEqual(GameSessionDisposition.Changed, other.PickUpItem(other.Version, "tome", Accept, different).Disposition);
        Assert.IsNull(Art(other, "case"), "another state changes nothing");
    }

    [TestMethod]
    public void Get_object_type_reads_the_type_of_the_looped_object_and_assign_object_keeps_it()
    {
        using var typed = Owner();
        Assert.AreEqual(GameSessionDisposition.Changed, typed.PickUpItem(typed.Version, "tome", Accept, new Host(Typed)).Disposition);
        Assert.AreEqual((NewArt, (int?)null), ((int?)Art(typed, "case"), (int?)Art(typed, "other")), "only the container of the named type changes");

        using var assigned = Owner();
        Assert.AreEqual(GameSessionDisposition.Changed, assigned.PickUpItem(assigned.Version, "tome", Accept, new Host(Assigned)).Disposition);
        Assert.AreEqual((NewArt, (int?)null), ((int?)Art(assigned, "case"), (int?)Art(assigned, "other")), "the object variable holds the looped object the name test and the art change read");
    }

    [TestMethod]
    public void The_dead_condition_reads_the_host_and_the_generator_condition_reads_the_session_table()
    {
        using var dead = Owner();
        Assert.AreEqual(GameSessionDisposition.Changed, dead.PickUpItem(dead.Version, "tome", Accept, new Host(DeadGated)).Disposition);
        Assert.AreEqual((NewArt, (int?)null), ((int?)Art(dead, "case"), (int?)Art(dead, "other")), "only the container the host calls dead changes");

        using var open = Owner();
        Assert.AreEqual(GameSessionDisposition.Changed, open.PickUpItem(open.Version, "tome", Accept, new Host(GeneratorGated)).Disposition);
        Assert.IsNull(Art(open, "case"), "an unblocked generator slot does not pass the condition");

        using var blocked = Owner();
        Assert.AreEqual(GameSessionDisposition.Changed, blocked.ToggleGeneratorBlock(blocked.Version, GeneratorSlot).Disposition);
        Assert.AreEqual(GameSessionDisposition.Changed, blocked.PickUpItem(blocked.Version, "tome", Accept, new Host(GeneratorGated)).Disposition);
        Assert.AreEqual((NewArt, NewArt), ((int?)Art(blocked, "case"), (int?)Art(blocked, "other")), "a blocked slot passes it for every looped container");
    }

    [TestMethod]
    public void A_quest_state_object_type_or_death_the_host_cannot_answer_refuses_the_move()
    {
        foreach (var host in new Host[] { new(Typed) { TypeOfContainers = null }, new(DeadGated) { DeadCase = null }, new(Quest) { QuestStateOfHero = null } })
        {
            using var owner = Owner();
            var before = owner.Version;
            var refused = owner.PickUpItem(before, "tome", Accept, host);
            Assert.AreEqual((GameSessionDisposition.Rejected, GameSessionFailure.ItemScriptUnsupported, before), (refused.Disposition, refused.Failure, owner.Version));
            Assert.IsNull(Art(owner, "case"));
        }
    }

    [TestMethod]
    public void A_move_the_host_cannot_answer_is_refused_and_changes_nothing()
    {
        foreach (var host in new Host[] { new(Every) { Vicinity = null }, new(EveryFollowerScript) { Followers = null }, new(Every) { Plans = false }, new(Every) { Names = [] }, new(Twice) })
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
        foreach (var kind in new[] { ScrFocusKind.EverySceneryInVicinity, ScrFocusKind.AnySceneryInVicinity, ScrFocusKind.EveryoneInVicinity, ScrFocusKind.AnyoneInVicinity, ScrFocusKind.EveryoneInGroup, ScrFocusKind.AnyoneInGroup })
            Assert.IsFalse(set.FocusKinds.Contains(kind), $"focus {kind}");
        // ARC-SCRIPT-2347 (IV13): the quest state condition, assign object and get object type are answered by the host's world part now.
        Assert.IsTrue(set.Conditions.Contains(ScrCatalogue.Condition.PcQuestState) && set.Actions.Contains(ScrCatalogue.Action.AssignObject) && set.Actions.Contains(ScrCatalogue.Action.GetObjectType));
        Assert.IsTrue(set.IsSubsetOf(ScrAdmissionSet.S65));
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
