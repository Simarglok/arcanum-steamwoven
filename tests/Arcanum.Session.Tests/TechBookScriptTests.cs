using System.Buffers.Binary;
using Arcanum.Data.Characters;
using Arcanum.Data.Scripts;
using Arcanum.Scripting.Dialog;
using Arcanum.Scripting.Scr;
using Arcanum.Simulation.Character;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.HeroStateTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// ARC-DEV-366 (ledger XA9, GS1, GS3): the tech books' insert and remove scripts. The programs have the installed books' shape (get stat of
// counter 3 into counter 0, divide by counter 2 into a local, add effect counter 1 with the item cause that many times; from line 6, the part
// the remove script reaches through call-ex, remove it counter 0 times); script numbers, the effect and the values are invented.
[TestClass]
public sealed class TechBookScriptTests
{
    private const byte Counter = 0, Triggerer = 0, Attachee = 1, Local = 2, Number = 3;
    private const int Insert = 800, Remove = 801, Expertise = 168, Discipline = 3, Raise = 60, Strength = 61;
    private const int Intelligence = 4;
    private static readonly SessionInsertCheck Accept = _ => SessionInsertVerdict.Accepted;
    // The book's effect: +1 expertise in a discipline; a raise: intelligence +3; a boost: strength +1.
    private static readonly SessionEffectDefinition BookEffect = new(Expertise, [new(EffectAttributeKind.TechDiscipline, Discipline, EffectChangeOperator.Add, 1)]);
    private static readonly SessionEffectDefinition RaiseEffect = new(Raise, [new(EffectAttributeKind.Stat, Intelligence, EffectChangeOperator.Add, 3)]);
    private static readonly SessionEffectDefinition BoostEffect = new(Strength, [new(EffectAttributeKind.Stat, 0, EffectChangeOperator.Add, 1)]);

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

    // The loop of lines 2 to 5 adds, the one of lines 7 to 10 removes; both count the local variable down from the divided value.
    private static (Clause, Clause, Clause)[] Loops(Clause read) =>
    [
        Do(read),
        Do(C(ScrCatalogue.Action.Divide, (Local, 0), (Counter, 0), (Counter, 2))),
        If(C(ScrCatalogue.Condition.Equal, (Local, 0), N(0)), C(ScrCatalogue.Action.ReturnRunDefault)),
        Do(C(ScrCatalogue.Action.AddEffect, (Triggerer, 0), (Counter, 1), N(5))),
        Do(C(ScrCatalogue.Action.Subtract, (Local, 0), (Local, 0), N(1))),
        Do(C(ScrCatalogue.Action.Goto, N(2))),
        Do(C(ScrCatalogue.Action.Divide, (Local, 0), (Counter, 0), (Counter, 2))),
        If(C(ScrCatalogue.Condition.Equal, (Local, 0), N(0)), C(ScrCatalogue.Action.ReturnRunDefault)),
        Do(C(ScrCatalogue.Action.RemoveEffect, (Triggerer, 0), (Counter, 1))),
        Do(C(ScrCatalogue.Action.Subtract, (Local, 0), (Local, 0), N(1))),
        Do(C(ScrCatalogue.Action.Goto, N(7))),
    ];

    private static readonly ScrProgram BookInsert = Program(Loops(C(ScrCatalogue.Action.GetStat, (Counter, 3), (Triggerer, 0), (Counter, 0))));
    private static readonly ScrProgram BookRemove = Program(Do(C(ScrCatalogue.Action.CallScriptEx, (Attachee, 0), N(SessionScriptPoint.InsertItem), N(6), (Triggerer, 0))),
        Do(C(ScrCatalogue.Action.ReturnRunDefault)));

    // The book's counters as the prototype names them: counter 0 free, counter 1 the effect, counter 2 the divisor (1), counter 3 the stat (4).
    private static uint Counters(int count = 0) => (uint)count | (uint)Expertise << 8 | 1u << 16 | (uint)Intelligence << 24;

    private sealed class Host : ISessionItemScriptHost
    {
        public Dictionary<int, ScrProgram> Programs { get; } = new() { [Insert] = BookInsert, [Remove] = BookRemove };
        public Dictionary<int, SessionEffectDefinition> Effects { get; } = new() { [Expertise] = BookEffect, [Raise] = RaiseEffect, [Strength] = BoostEffect };

        public ScrSlot? Slot(ScrObjectRef attachee, int point) => attachee.Code is "tome" or "tome-held" ? point switch
        {
            SessionScriptPoint.InsertItem => new ScrSlot(Insert, 0, Counters()),
            SessionScriptPoint.RemoveItem => new ScrSlot(Remove, 0, Counters()),
            _ => null,
        } : null;

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
        public SessionEffectDefinition? EffectDefinition(int number) => Effects.TryGetValue(number, out var rule) ? rule : null;
    }

    // The hero of the half-elf fixture with the Crash Site seed and a tome on the ground that carries the insert-item and remove-item scripts.
    private static GameSessionOwner Owner()
    {
        var owner = CreateWith(HalfElf([8, 8, 8, 8, 14, 8, 8, 8, 0, 0]));
        var seed = InventoryStateTests.Seed();
        var tome = new SessionSeedItem("tome", InventoryStateTests.Facts(SessionItemType.Generic, 5700, 0, 0, null, SessionScriptPoint.InsertItem, SessionScriptPoint.RemoveItem),
            new Arcanum.Simulation.Movement.CellCoord(300, 200));
        // The same book in the dead body's list, for the loot path (the removal's script runs before the insertion's).
        var held = new SessionSeedItem("tome-held", InventoryStateTests.Facts(SessionItemType.Generic, 5701, 0, 0, null, SessionScriptPoint.InsertItem, SessionScriptPoint.RemoveItem), null);
        var holders = seed.Holders.Select(holder => holder.Entity == "corpse" ? holder with { Items = [.. holder.Items, "tome-held"] } : holder).ToArray();
        Assert.AreEqual(GameSessionDisposition.Changed, owner.AttachMapInventory(Exterior, seed with { Holders = holders, Items = [.. seed.Items, tome, held] }).Disposition);
        return owner;
    }

    private static int Books(GameSessionOwner owner) => owner.QueryHeroEffects()!.Effects.Count(effect => effect == new SessionEffect(Expertise, EffectCause.Item));
    private static int HeroIntelligence(GameSessionOwner owner) => owner.QueryHeroScriptStat(Intelligence)!.Value;
    private static int DisciplineExpertise(GameSessionOwner owner) => owner.QueryHeroTechnology()!.Value[Discipline].Expertise;

    [TestMethod]
    public void A_picked_up_book_adds_one_expertise_per_point_of_intelligence_and_keeps_the_count_in_its_counter()
    {
        using var owner = Owner();
        var intelligence = HeroIntelligence(owner);
        Assert.IsTrue(intelligence > 8, "the fixture's hero is clever enough for the test to see the difference");
        var before = DisciplineExpertise(owner);
        // Without a host the book is refused, as before.
        Assert.AreEqual(GameSessionFailure.ItemScriptUnsupported, owner.PickUpItem(owner.Version, "tome", Accept).Failure);

        var picked = owner.PickUpItem(owner.Version, "tome", Accept, new Host());
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None, "tome"), (picked.Disposition, picked.Failure, picked.Item));
        CollectionAssert.AreEqual(new[] { (SessionScriptPoint.InsertItem, "tome") }, picked.Scripts.Select(run => (run.Point, run.Attachee)).ToArray());
        Assert.AreEqual(intelligence, Books(owner));
        Assert.AreEqual(before + intelligence, DisciplineExpertise(owner), "+1 expertise in the book's discipline for each point");
        Assert.AreEqual(new SessionItemScriptSlot("tome", SessionScriptPoint.InsertItem, Insert, 0, Counters(intelligence)), owner.QueryItemScriptSlot("tome", SessionScriptPoint.InsertItem),
            "counter 0 keeps the intelligence the insertion read");

        // The list and the counter survive a save.
        using var restored = Restore(Decode(GameSessionSnapshotCodec.Encode(owner.Capture())));
        Assert.AreEqual((intelligence, before + intelligence), (Books(restored), DisciplineExpertise(restored)));
    }

    [TestMethod]
    public void Moving_the_book_removes_what_the_insertion_added_and_adds_what_the_intelligence_is_now()
    {
        using var owner = Owner();
        var host = new Host();
        var intelligence = HeroIntelligence(owner);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.PickUpItem(owner.Version, "tome", Accept, host).Disposition);
        Assert.AreEqual(intelligence, Books(owner));
        // The intelligence rises by 3 while the book is carried: the effects the insertion added stay (they count the old intelligence).
        Assert.AreEqual(GameSessionDisposition.Changed, owner.AddHeroEffect(owner.Version, RaiseEffect, EffectCause.Spell, 1).Disposition);
        Assert.AreEqual((intelligence + 3, intelligence), (HeroIntelligence(owner), Books(owner)));

        // A move in the grid runs the remove-item script (call-ex to the insert part, which removes the kept count) and then the insert-item
        // script again (which reads the intelligence now).
        var held = owner.QueryPcInventory().Single(item => item.Entity == "tome");
        var moved = owner.MoveHeroItem(owner.Version, "tome", 77, new SessionGridMoveItem(held.Kind, false, [SessionScriptPoint.InsertItem, SessionScriptPoint.RemoveItem]), Accept, host);
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None), (moved.Disposition, moved.Failure));
        CollectionAssert.AreEqual(new[] { (SessionScriptPoint.RemoveItem, "tome"), (SessionScriptPoint.InsertItem, "tome") }, moved.Scripts.Select(run => (run.Point, run.Attachee)).ToArray());
        Assert.AreEqual(intelligence + 3, Books(owner), "the old count is gone and the new one is in");
        Assert.AreEqual(Counters(intelligence + 3), owner.QueryItemScriptSlot("tome", SessionScriptPoint.InsertItem)!.Counters);
    }

    [TestMethod]
    public void A_book_looted_from_a_body_runs_its_removal_first_and_takes_nothing_from_the_holder()
    {
        using var owner = Owner();
        var intelligence = HeroIntelligence(owner);
        var before = DisciplineExpertise(owner);
        // The loot fires the body's transfer, the book's removal (30018 from the holder: counter 0 is still 0, so its loop removes nothing) and
        // then the insertion (30017), which adds the effects.
        var looted = owner.LootItem(owner.Version, "corpse", "tome-held", Accept, new Host());
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None), (looted.Disposition, looted.Failure));
        CollectionAssert.AreEqual(new[] { (SessionScriptPoint.Transfer, SessionEventKinds.PlayerEntity), (SessionScriptPoint.RemoveItem, "tome-held"), (SessionScriptPoint.InsertItem, "tome-held") },
            looted.Scripts.Select(run => (run.Point, run.Attachee)).ToArray());
        Assert.AreEqual((intelligence, before + intelligence), (Books(owner), DisciplineExpertise(owner)));
    }

    [TestMethod]
    public void A_stat_the_session_does_not_hold_and_a_read_after_a_change_of_the_stat_refuse_the_move()
    {
        using var owner = Owner();
        // GS3: the secondary stats (here 12) are not modelled; the run ends as not modelled and nothing is applied.
        var host = new Host();
        host.Programs[Insert] = Program(Loops(C(ScrCatalogue.Action.GetStat, (Number, 12), (Triggerer, 0), (Counter, 0))));
        var refused = owner.PickUpItem(owner.Version, "tome", Accept, host);
        Assert.AreEqual((GameSessionDisposition.Rejected, GameSessionFailure.ItemScriptUnsupported, 0L), (refused.Disposition, refused.Failure, owner.Version.Revision));
        Assert.AreEqual(0, Books(owner));

        // A run that adds an effect that changes the stat and reads the stat after it would read the changed stat in the original (the session
        // applies a run's changes after it): refused; the same read before the change is the book's order and passes.
        host.Programs[Insert] = Program(
            Do(C(ScrCatalogue.Action.AddEffect, (Triggerer, 0), N(Raise), N(5))),
            Do(C(ScrCatalogue.Action.GetStat, (Number, Intelligence), (Triggerer, 0), (Counter, 0))),
            Do(C(ScrCatalogue.Action.ReturnRunDefault)));
        Assert.AreEqual(GameSessionFailure.ItemScriptUnsupported, owner.PickUpItem(owner.Version, "tome", Accept, host).Failure);
        host.Programs[Insert] = Program(
            Do(C(ScrCatalogue.Action.GetStat, (Number, Intelligence), (Triggerer, 0), (Counter, 0))),
            Do(C(ScrCatalogue.Action.AddEffect, (Triggerer, 0), N(Raise), N(5))),
            Do(C(ScrCatalogue.Action.ReturnRunDefault)));
        var passed = owner.PickUpItem(owner.Version, "tome", Accept, host);
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None), (passed.Disposition, passed.Failure));
        Assert.AreEqual(new SessionItemScriptSlot("tome", SessionScriptPoint.InsertItem, Insert, 0, Counters(14)), owner.QueryItemScriptSlot("tome", SessionScriptPoint.InsertItem));
    }

    [TestMethod]
    public void More_than_sixty_four_stat_changing_entries_reach_the_character_owner_and_a_book_still_runs()
    {
        using var owner = Owner();
        var strength = owner.QueryHeroScriptStat(0)!.Value;
        // 70 strength effects (the character owner's own default bound was 64), then the book's run reads the stat under them.
        Assert.AreEqual(GameSessionDisposition.Changed, owner.AddHeroEffect(owner.Version, BoostEffect, EffectCause.Spell, 70).Disposition);
        Assert.AreEqual(Math.Min(strength + 70, owner.QueryHeroScriptStat(0)!.Value), owner.QueryHeroScriptStat(0)!.Value);
        Assert.AreEqual(70, owner.QueryHeroEffects()!.Effects.Count(entry => entry.Number == Strength));
        var picked = owner.PickUpItem(owner.Version, "tome", Accept, new Host());
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None), (picked.Disposition, picked.Failure));
        Assert.AreEqual(HeroIntelligence(owner), Books(owner));
    }

    [TestMethod]
    public void The_hero_s_list_holds_far_more_than_sixty_four_entries_for_the_books()
    {
        using var owner = Owner();
        var host = new Host();
        var baseline = DisciplineExpertise(owner);
        // 200 entries from one run (the loop of four lines, 800 of the run's 1000 lines).
        host.Programs[Insert] = Program(Loops(C(ScrCatalogue.Action.Assign, (Counter, 0), N(200))));
        var picked = owner.PickUpItem(owner.Version, "tome", Accept, host);
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None), (picked.Disposition, picked.Failure));
        Assert.AreEqual(200, Books(owner));
        Assert.AreEqual(baseline + 200, DisciplineExpertise(owner));

        using var restored = Restore(Decode(GameSessionSnapshotCodec.Encode(owner.Capture())));
        Assert.AreEqual(200, Books(restored));
        // The bound is the list's, not the run's: 1024 entries in all, and the one past it is refused whole.
        Assert.AreEqual(GameSessionDisposition.Changed, owner.AddHeroEffect(owner.Version, BookEffect, EffectCause.Item, SessionEffectLimits.MaxHeroEffects - 200).Disposition);
        Assert.AreEqual(GameSessionFailure.EffectCapacityExceeded, owner.AddHeroEffect(owner.Version, BookEffect, EffectCause.Item, 1).Failure);
    }
}
