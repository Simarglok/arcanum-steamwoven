using Arcanum.Data.Scripts;
using Arcanum.Scripting.Scr;
using Arcanum.Simulation.Equipment;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.HeroStateTests;
using static Arcanum.Session.Tests.InsertItemScriptTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// ARC-DEV-097 (MIG8 slice 2, TM9, GR7, SA2–SA4): an item the session creates from a prototype (a made item) owns no script array and reads its
// prototype's until its first write, which copies every point of the prototype's array onto it. Invented prototype, scripts and values; the made
// ring is worn by the half-elf fixture, so the wield-on script of point 24 is the first writer.
[TestClass]
public sealed class PrototypeScriptInheritanceTests
{
    private const byte Counter = 0;
    private const int RingPrototype = 7700, WieldOn = 710, WieldOff = 711;
    private static readonly SessionItemKind RingKind = new(SessionItemType.Armor, RingPrototype, 7701, 7702);
    private static readonly SessionInsertCheck Accept = _ => SessionInsertVerdict.Accepted;
    private static readonly SessionWearArtCheck Drawn = (_, _) => true;

    // The prototype's array: wield on and wield off with their default flags and counters, and a use point whose script number is 0 (present, SA1).
    private static readonly SessionPrototypeScript[] Array_ =
    [
        new(SessionScriptPoint.Use, 0, 9, 0x0102), new(SessionScriptPoint.WieldOn, WieldOn, 3, 0), new(SessionScriptPoint.WieldOff, WieldOff, 0, 0),
    ];

    // Wield on writes counter 0 (the first write to the ring's array); wield off changes nothing.
    private static readonly ScrProgram Writer = Program(C(ScrCatalogue.Action.Assign, (Counter, 0), (Number, 7)), C(ScrCatalogue.Action.ReturnRunDefault));
    private static readonly ScrProgram Plain = Program(C(ScrCatalogue.Action.ReturnRunDefault));

    private sealed class Host(GameSessionOwner session) : ISessionItemScriptHost
    {
        public ScrSlot? Slot(ScrObjectRef attachee, int point) => null;
        public ScrScriptLookup Script(int number) => number == WieldOn ? ScrScriptLookup.Of(Writer) : number == WieldOff ? ScrScriptLookup.Of(Plain) : ScrScriptLookup.Missing;
        public bool GlobalFlag(int index) => throw new InvalidOperationException("the session reads flags itself");
        public int StoryState() => throw new InvalidOperationException("not admitted");
        public bool IsMonsterGenerator(ScrObjectRef attachee) => false;
        public ScrDialogState DialogState(ScrObjectRef attachee) => ScrDialogState.Normal;
        public ScrTrapCheck CheckTrapBeforeUse(ScrObjectRef triggerer, ScrObjectRef attachee) => ScrTrapCheck.NoTrap;
        public bool IsTrapObject(ScrObjectRef attachee) => false;
        public ScrObjectRef? Player() => new ScrObjectRef(session.QueryActor().Map.RuntimeMap, SessionEventKinds.PlayerEntity);
        public ScrObjectRef? Leader(ScrObjectRef critter) => null;
        public bool IsJilted(ScrObjectRef critter) => false;
        public bool IsPc(ScrObjectRef critter) => critter.Code == SessionEventKinds.PlayerEntity;
        public SessionPrintedLine PrintedLine(int script, int line) => new(SessionPrintedLineStatus.Shown, $"line {line}", "test");
        public SessionEffectDefinition? EffectDefinition(int number) => null;
        public bool? IsIdentified(ScrObjectRef obj) => null;
    }

    private static SessionWearableItem Wearable() => new(RingKind, [SessionScriptPoint.WieldOn, SessionScriptPoint.WieldOff], false, new EquipmentItem
    {
        Kind = EquipmentItemKind.Armor, Armor = new EquipmentArmor { Coverage = ArmorCoverage.Ring, AcAdjustment = 1 },
    });

    // A session with the ring made into the hero's list, the prototype source attached or not.
    private static (GameSessionOwner Owner, string Entity) Owner(bool attach = true)
    {
        var owner = CreateWith(HalfElf());
        if (attach) owner.AttachItemScripts(prototype => prototype == RingPrototype ? Array_ : null);
        var entity = owner.NextObjectEntity(SessionObjectOrigin.Made)!;
        owner.CreateMadeItem(new(entity, RingKind, SessionCreatePlace.HeroList, new SessionItemWeight(1, 0, 0)));
        return (owner, entity);
    }

    [TestMethod]
    public void A_created_item_reads_its_prototypes_array_and_keeps_nothing_of_its_own()
    {
        var (owner, ring) = Owner();
        using var _ = owner;
        Assert.AreEqual(new SessionItemScriptSlot(ring, SessionScriptPoint.WieldOn, WieldOn, 3, 0), owner.QueryItemScriptSlot(ring, SessionScriptPoint.WieldOn));
        Assert.AreEqual(new SessionItemScriptSlot(ring, SessionScriptPoint.Use, 0, 9, 0x0102), owner.QueryItemScriptSlot(ring, SessionScriptPoint.Use), "an element with no script is present");
        Assert.IsNull(owner.QueryItemScriptSlot(ring, SessionScriptPoint.Drop), "a point the prototype has no element at");
        Assert.AreEqual(0, owner.QueryItemScriptSlots().Count, "the read is through the prototype: nothing is copied");
        Assert.IsNull(owner.QueryItemScriptSlot("kit:1", SessionScriptPoint.WieldOn), "a kit item's slot is the host's data");
        Assert.IsNull(owner.QueryItemScriptSlot("made:99", SessionScriptPoint.WieldOn), "an item the session does not hold");
        // The source is not state: detached, the item has no attachment (the older behaviour).
        owner.AttachItemScripts(null);
        Assert.IsNull(owner.QueryItemScriptSlot(ring, SessionScriptPoint.WieldOn));
    }

    [TestMethod]
    public void An_array_the_store_cannot_keep_is_none()
    {
        foreach (var bad in new SessionPrototypeScript[][]
        {
            [new(36, 1, 0, 0)], [new(-1, 1, 0, 0)], [new(31, 1, 0, 0)], [new(4, 100_000, 0, 0)], [new(4, -1, 0, 0)], [new(4, 1, 0, 0), new(4, 2, 0, 0)],
        })
        {
            var (owner, ring) = Owner(attach: false);
            using var _ = owner;
            owner.AttachItemScripts(_ => bad);
            Assert.IsNull(owner.QueryItemScriptSlot(ring, 4), "refused whole: " + string.Join(",", bad.Select(element => element.Point)));
        }
    }

    [TestMethod]
    public void The_first_write_copies_every_point_and_the_item_owns_its_array_from_then_on()
    {
        var (owner, ring) = Owner();
        using var _ = owner;
        var host = new Host(owner);
        var worn = owner.Equip(owner.Version, ring, EquipmentSlot.Ring1, Wearable(), Drawn, Accept, host);
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None), (worn.Disposition, worn.Failure));
        CollectionAssert.AreEqual(new[] { (SessionScriptPoint.WieldOn, ring) }, worn.Scripts.Select(run => (run.Point, run.Attachee)).ToArray());
        // The run read the prototype's slot and wrote counter 0; the other points came along with their prototype values.
        CollectionAssert.AreEqual(new[]
        {
            new SessionItemScriptSlot(ring, SessionScriptPoint.Use, 0, 9, 0x0102),
            new SessionItemScriptSlot(ring, SessionScriptPoint.WieldOn, WieldOn, 3, 7),
            new SessionItemScriptSlot(ring, SessionScriptPoint.WieldOff, WieldOff, 0, 0),
        }, owner.QueryItemScriptSlots().ToArray());

        // Owned now: a different prototype source no longer matters, and a point the copy lacks stays empty.
        owner.AttachItemScripts(_ => [new(SessionScriptPoint.Drop, 5, 0, 0)]);
        Assert.IsNull(owner.QueryItemScriptSlot(ring, SessionScriptPoint.Drop));
        Assert.AreEqual(7u, owner.QueryItemScriptSlot(ring, SessionScriptPoint.WieldOn)!.Counters);

        // The array is saved with the item and survives a restore; the restored session reads its own copy whatever source is attached.
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        StringAssert.Contains(text, "\"scriptSlots\":[");
        using var restored = Restore(Decode(text));
        restored.AttachItemScripts(prototype => prototype == RingPrototype ? [new(SessionScriptPoint.Drop, 9, 0, 0)] : null);
        CollectionAssert.AreEqual(owner.QueryItemScriptSlots().ToArray(), restored.QueryItemScriptSlots().ToArray());
        Assert.IsNull(restored.QueryItemScriptSlot(ring, SessionScriptPoint.Drop));
        Assert.AreEqual(text, GameSessionSnapshotCodec.Encode(restored.Capture()), "canonical");
    }

    [TestMethod]
    public void Wield_off_reads_the_copy_and_a_run_with_no_write_copies_nothing()
    {
        var (owner, ring) = Owner();
        using var _ = owner;
        var host = new Host(owner);
        // Wield on, then wield off: wield off's script writes nothing, and the copy made by wield on stays as it is.
        Assert.AreEqual(GameSessionDisposition.Changed, owner.Equip(owner.Version, ring, EquipmentSlot.Ring1, Wearable(), Drawn, Accept, host).Disposition);
        var before = owner.QueryItemScriptSlots().ToArray();
        var off = owner.Unequip(owner.Version, EquipmentSlot.Ring1, Accept, host);
        Assert.AreEqual(GameSessionFailure.None, off.Failure);
        CollectionAssert.AreEqual(new[] { (SessionScriptPoint.WieldOff, ring) }, off.Scripts.Select(run => (run.Point, run.Attachee)).ToArray());
        CollectionAssert.AreEqual(before, owner.QueryItemScriptSlots().ToArray());
    }

    [TestMethod]
    public void A_destroyed_item_loses_its_copy()
    {
        var (owner, ring) = Owner();
        using var _ = owner;
        // The destroy path drops the array with the item.
        Assert.AreEqual(GameSessionDisposition.Changed, owner.Equip(owner.Version, ring, EquipmentSlot.Ring1, Wearable(), Drawn, Accept, new Host(owner)).Disposition);
        Assert.AreEqual(3, owner.QueryItemScriptSlots().Count);
        owner.DestroyObject(ring);
        Assert.AreEqual(0, owner.QueryItemScriptSlots().Count);
        Assert.IsNull(owner.QueryItemScriptSlot(ring, SessionScriptPoint.WieldOn), "a destroyed item holds nothing");
    }
}
