using System.Buffers.Binary;
using Arcanum.Data.Characters;
using Arcanum.Data.Scripts;
using Arcanum.Scripting.Scr;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Equipment;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.HeroStateTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// ARC-SCRIPT-042: the item events of the hero's equipment moves (ledger IE3–IE6, WE1–WE7). The kit is the half-elf fixture's (kit:2 and
// kit:3 armour, worn here as rings); script numbers, programs, effects and values are invented.
[TestClass]
public sealed class WieldEventScriptTests
{
    private const byte Counter = 0, Triggerer = 0, Attachee = 1, Local = 2, Number = 3;
    private const int WieldOn = 700, WieldOff = 701, Record = 702, Charm = 50;
    private static readonly SessionInsertCheck Accept = _ => SessionInsertVerdict.Accepted;
    private static readonly SessionWearArtCheck Drawn = (_, _) => true;
    // A charm: strength +1 (stat 0).
    private static readonly SessionEffectDefinition CharmEffect = new(Charm, [new(EffectAttributeKind.Stat, 0, EffectChangeOperator.Add, 1)]);

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

    // The installed shape (census): wield on scales 10 by the power ratio into counter 0 and adds the charm that many times; from line 6,
    // the part wield off reaches through call-ex, it removes it counter 0 times.
    private static readonly ScrProgram Scaled = Program(
        Do(C(ScrCatalogue.Action.MagickTechAdjustment, N(10), (Attachee, 0), (Triggerer, 0), (Counter, 0))),
        Do(C(ScrCatalogue.Action.Assign, (Local, 0), (Counter, 0))),
        If(C(ScrCatalogue.Condition.Equal, (Local, 0), N(0)), C(ScrCatalogue.Action.ReturnRunDefault)),
        Do(C(ScrCatalogue.Action.AddEffect, (Triggerer, 0), N(Charm), N(5))),
        Do(C(ScrCatalogue.Action.Subtract, (Local, 0), (Local, 0), N(1))),
        Do(C(ScrCatalogue.Action.Goto, N(2))),
        Do(C(ScrCatalogue.Action.Assign, (Local, 0), (Counter, 0))),
        If(C(ScrCatalogue.Condition.Equal, (Local, 0), N(0)), C(ScrCatalogue.Action.ReturnRunDefault)),
        Do(C(ScrCatalogue.Action.RemoveEffect, (Triggerer, 0), N(Charm))),
        Do(C(ScrCatalogue.Action.Subtract, (Local, 0), (Local, 0), N(1))),
        Do(C(ScrCatalogue.Action.Goto, N(7))));
    private static readonly ScrProgram CallOff = Program(Do(C(ScrCatalogue.Action.CallScriptEx, (Attachee, 0), N(24), N(6), (Triggerer, 0))),
        Do(C(ScrCatalogue.Action.ReturnRunDefault)));
    private static readonly ScrProgram Plain = Program(Do(C(ScrCatalogue.Action.ReturnRunDefault)));

    /// <summary>A host with slots per (entity, point), the charm's rule and the identified answers; it reads the session's flags.</summary>
    private sealed class Host(GameSessionOwner session) : ISessionItemScriptHost
    {
        public Dictionary<(string, int), int> Slots { get; } = [];
        public Dictionary<int, ScrProgram> Programs { get; } = new() { [WieldOn] = Scaled, [WieldOff] = CallOff, [Record] = Plain };
        public Dictionary<int, SessionEffectDefinition> Effects { get; } = new() { [Charm] = CharmEffect };
        public Dictionary<string, bool> Identified { get; } = [];

        public ScrSlot? Slot(ScrObjectRef attachee, int point) => Slots.TryGetValue((attachee.Code, point), out var script) ? new ScrSlot(script, 0, 0) : null;
        public ScrScriptLookup Script(int number) => Programs.TryGetValue(number, out var program) ? ScrScriptLookup.Of(program) : ScrScriptLookup.Missing;
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
        public SessionEffectDefinition? EffectDefinition(int number) => Effects.TryGetValue(number, out var rule) ? rule : null;
        public bool? IsIdentified(ScrObjectRef obj) => Identified.TryGetValue(obj.Code, out var identified) ? identified : null;
    }

    private static EquipmentItem Ring(int ac, int complexity = 0) => new()
    {
        Kind = EquipmentItemKind.Armor, MagickTechComplexity = complexity, Armor = new EquipmentArmor { Coverage = ArmorCoverage.Ring, AcAdjustment = ac },
    };

    private static SessionWearableItem Wearable(int kit, EquipmentItem item, params int[] scripts) => new(Kit[kit - 1].Kind, scripts, false, item);

    private static (GameSessionOwner, Host) Owner(SessionHeroDefinition? hero = null)
    {
        var owner = CreateWith(hero ?? HalfElf());
        var host = new Host(owner);
        foreach (var kit in new[] { "kit:2", "kit:3" })
        {
            host.Slots[(kit, SessionScriptPoint.WieldOn)] = WieldOn;
            host.Slots[(kit, SessionScriptPoint.WieldOff)] = WieldOff;
        }
        return (owner, host);
    }

    private static int Charms(GameSessionOwner owner) => owner.QueryHeroEffects()!.Effects.Count(effect => effect == new SessionEffect(Charm, EffectCause.Item));
    private static (int, string)[] Runs(EquipmentMutationResult result) => result.Scripts.Select(run => (run.Point, run.Attachee)).ToArray();

    [TestMethod]
    public void Wield_on_adds_the_scaled_effects_and_wield_off_takes_them_away_through_call_ex()
    {
        var (owner, host) = Owner();
        using var _ = owner;
        var ring = Wearable(3, Ring(1), SessionScriptPoint.WieldOn, SessionScriptPoint.WieldOff);
        // Without a host the scripted ring is refused, as before.
        Assert.AreEqual(GameSessionFailure.ItemScriptUnsupported, owner.Equip(owner.Version, "kit:3", EquipmentSlot.Ring1, ring, Drawn, Accept).Failure);

        var worn = owner.Equip(owner.Version, "kit:3", EquipmentSlot.Ring1, ring, Drawn, Accept, host);
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None, 1L), (worn.Disposition, worn.Failure, owner.Version.Revision));
        CollectionAssert.AreEqual(new[] { (24, "kit:3") }, Runs(worn), "the ring carries no remove-item or insert-item script");
        Assert.AreEqual(10, Charms(owner), "a neutral item's ratio is 100");
        Assert.AreEqual(new SessionItemScriptSlot("kit:3", 24, WieldOn, 0, 10), owner.QueryItemScriptSlots().Single(), "counter 0 keeps the count");
        CollectionAssert.AreEqual(new[] { 24, 25 }, owner.QueryEquipment().Worn.Single().ScriptPoints.ToArray());

        // The kept slot and the worn item's points survive the snapshot.
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        StringAssert.Contains(text, "\"scriptSlots\":[{\"entity\":\"kit:3\",\"point\":24,\"script\":700,\"localFlags\":0,\"counters\":10}]");
        StringAssert.Contains(text, "\"scripts\":[24,25],\"itemSpells\":false}");
        using var restored = Restore(Decode(text));
        Assert.AreEqual(owner.QueryItemScriptSlots().Single(), restored.QueryItemScriptSlots().Single());
        CollectionAssert.AreEqual(owner.QueryEquipment().Worn.ToArray(), restored.QueryEquipment().Worn.ToArray());

        // Without a host the scripted ring cannot come off; with one, wield off reads the kept counter and removes the ten charms.
        Assert.AreEqual(GameSessionFailure.ItemScriptUnsupported, restored.Unequip(restored.Version, EquipmentSlot.Ring1, Accept).Failure);
        var off = restored.Unequip(restored.Version, EquipmentSlot.Ring1, Accept, new Host(restored) { Slots = { [("kit:3", 25)] = WieldOff, [("kit:3", 24)] = 0 } });
        Assert.AreEqual((GameSessionDisposition.Changed, "kit:3"), (off.Disposition, off.Displaced));
        CollectionAssert.AreEqual(new[] { (25, "kit:3") }, Runs(off));
        Assert.AreEqual((0, 0), (Charms(restored), restored.QueryEquipment().Worn.Count));
    }

    [TestMethod]
    public void The_magick_tech_adjustment_reads_the_hero_s_power_ratio()
    {
        var (owner, host) = Owner();
        using var _ = owner;
        var aptitude = owner.QueryHeroStats(new(0))!.Value.Snapshot!.GetEffective(CharacterStat.MagickTechAptitude);
        var ratio = EquipmentRules.EffectivePowerRatio(40, Math.Clamp(aptitude, EquipmentLimits.MinAptitude, EquipmentLimits.MaxAptitude));
        Assert.AreEqual(GameSessionDisposition.Changed, owner.Equip(owner.Version, "kit:3", EquipmentSlot.Ring1,
            Wearable(3, Ring(1, complexity: 40), SessionScriptPoint.WieldOn), Drawn, Accept, host).Disposition);
        Assert.AreEqual((ratio * 10 + 50) / 100, Charms(owner));
        Assert.AreNotEqual(10, Charms(owner), "a magickal item the hero cannot fully power gives fewer charms");
    }

    [TestMethod]
    public void A_swap_runs_both_moves_in_the_original_s_order_and_a_refused_occupant_rolls_both_back()
    {
        int[] all = [SessionScriptPoint.InsertItem, SessionScriptPoint.WieldOn, SessionScriptPoint.WieldOff, SessionScriptPoint.RemoveItem];
        var (owner, host) = Owner();
        using var _ = owner;
        foreach (var kit in new[] { "kit:2", "kit:3" })
        foreach (var point in all) host.Slots[(kit, point)] = Record;
        Assert.AreEqual(GameSessionDisposition.Changed, owner.Equip(owner.Version, "kit:2", EquipmentSlot.Ring1, Wearable(2, Ring(2), all), Drawn, Accept, host).Disposition);
        var refused = owner.Equip(owner.Version, "kit:3", EquipmentSlot.Ring1, Wearable(3, Ring(1), all), Drawn, _ => SessionInsertVerdict.NoRoom, host);
        CollectionAssert.AreEqual(new[] { (28, "kit:3"), (25, "kit:2"), (28, "kit:2"), (21, "kit:3"), (24, "kit:2"), (21, "kit:2") }, Runs(refused));
        Assert.AreEqual((GameSessionDisposition.NoChange, GameSessionFailure.None, true, SessionInsertVerdict.NoRoom, (string?)null, 1L),
            (refused.Disposition, refused.Failure, refused.RolledBack, refused.Verdict, refused.Displaced, owner.Version.Revision));
        Assert.AreEqual("kit:2", owner.QueryEquipment().Worn.Single().Entity);

        var swapped = owner.Equip(owner.Version, "kit:3", EquipmentSlot.Ring1, Wearable(3, Ring(1), all), Drawn, Accept, host);
        CollectionAssert.AreEqual(new[] { (28, "kit:3"), (25, "kit:2"), (28, "kit:2"), (21, "kit:2"), (24, "kit:3"), (21, "kit:3") }, Runs(swapped));
        Assert.AreEqual((GameSessionDisposition.Changed, "kit:2", false), (swapped.Disposition, swapped.Displaced, swapped.RolledBack));
        Assert.AreEqual("kit:3", owner.QueryEquipment().Worn.Single().Entity);

        // A ring moved to the other ring slot is unwielded first and wielded again.
        var moved = owner.Equip(owner.Version, "kit:3", EquipmentSlot.Ring2, Wearable(3, Ring(1), all), Drawn, Accept, host);
        CollectionAssert.AreEqual(new[] { (25, "kit:3"), (28, "kit:3"), (24, "kit:3"), (21, "kit:3") }, Runs(moved));
    }

    [TestMethod]
    public void A_refused_occupant_applies_what_the_rollback_runs_changed()
    {
        var (owner, host) = Owner();
        using var _ = owner;
        int[] wield = [SessionScriptPoint.WieldOn, SessionScriptPoint.WieldOff];
        Assert.AreEqual(GameSessionDisposition.Changed, owner.Equip(owner.Version, "kit:2", EquipmentSlot.Ring1, Wearable(2, Ring(2), wield), Drawn, Accept, host).Disposition);
        // The refused swap unwields the worn ring (wield off removes the ten charms its kept counter names) and wields it again (ten
        // more): both are applied, in one revision.
        var refused = owner.Equip(owner.Version, "kit:3", EquipmentSlot.Ring1, Wearable(3, Ring(1), wield), Drawn, _ => SessionInsertVerdict.TooHeavy, host);
        Assert.AreEqual((GameSessionDisposition.Changed, true, 2L), (refused.Disposition, refused.RolledBack, owner.Version.Revision));
        CollectionAssert.AreEqual(new[] { (25, "kit:2"), (24, "kit:2") }, Runs(refused));
        Assert.AreEqual(10, Charms(owner), "ten removed, ten added");
    }

    [TestMethod]
    public void A_run_the_session_cannot_apply_refuses_the_move_with_nothing_changed()
    {
        var (owner, host) = Owner();
        using var _ = owner;
        var ring = Wearable(3, Ring(1), SessionScriptPoint.WieldOn);
        foreach (var program in new[]
        {
            // An effect the host has no rule for, an effect on the ring itself, and the identified test of an object the host does not know.
            Program(Do(C(ScrCatalogue.Action.AddEffect, (Triggerer, 0), N(51), N(5)))),
            Program(Do(C(ScrCatalogue.Action.AddEffect, (Attachee, 0), N(Charm), N(5)))),
            Program(If(C(ScrCatalogue.Condition.Identified, (Attachee, 0)), C(ScrCatalogue.Action.ReturnRunDefault))),
            // A cause outside the original's numbers, and the race cause the session's setters own.
            Program(Do(C(ScrCatalogue.Action.AddEffect, (Triggerer, 0), N(Charm), N(10)))),
            Program(Do(C(ScrCatalogue.Action.AddEffect, (Triggerer, 0), N(Charm), N(0)))),
        })
        {
            host.Programs[WieldOn] = program;
            var refused = owner.Equip(owner.Version, "kit:3", EquipmentSlot.Ring1, ring, Drawn, Accept, host);
            Assert.AreEqual(GameSessionDisposition.Rejected, refused.Disposition);
            Assert.AreEqual(1, refused.Scripts.Count);
            Assert.AreEqual((0L, 0, 0), (owner.Version.Revision, owner.QueryEquipment().Worn.Count, owner.QueryItemScriptSlots().Count));
        }
        // The identified test the host answers, with the eye candy for the host to show.
        host.Programs[WieldOn] = Program(If(C(ScrCatalogue.Condition.Identified, (Attachee, 0)), C(ScrCatalogue.Action.PlayScriptEyeCandy, N(30), (Triggerer, 0))),
            Do(C(ScrCatalogue.Action.ReturnRunDefault)));
        host.Identified["kit:3"] = true;
        var shown = owner.Equip(owner.Version, "kit:3", EquipmentSlot.Ring1, ring, Drawn, Accept, host);
        Assert.AreEqual(GameSessionDisposition.Changed, shown.Disposition);
        Assert.AreEqual(30, shown.Scripts.Single().Run.Effects.EyeCandies.Single().Number);
    }

    [TestMethod]
    public void A_later_run_that_cannot_be_applied_drops_what_the_earlier_runs_did()
    {
        int[] all = [SessionScriptPoint.InsertItem, SessionScriptPoint.WieldOn, SessionScriptPoint.WieldOff, SessionScriptPoint.RemoveItem];
        var (owner, host) = Owner();
        using var _ = owner;
        Assert.AreEqual(GameSessionDisposition.Changed, owner.Equip(owner.Version, "kit:2", EquipmentSlot.Ring1, Wearable(2, Ring(2), all), Drawn, Accept, host).Disposition);
        var before = (owner.Version, Charms(owner), owner.QueryItemScriptSlots().ToArray());
        // The new ring's remove-item run sets a flag, adds a charm and changes its slot; the occupant's remove-item run then asks for an
        // effect the host has no rule for.
        const int Changing = 703, Failing = 704;
        host.Programs[Changing] = Program(Do(C(ScrCatalogue.Action.SetGlobalFlag, N(3))), Do(C(ScrCatalogue.Action.AddEffect, (Triggerer, 0), N(Charm), N(5))),
            Do(C(ScrCatalogue.Action.SetLocalFlag, N(1))), Do(C(ScrCatalogue.Action.ReturnRunDefault)));
        host.Programs[Failing] = Program(Do(C(ScrCatalogue.Action.AddEffect, (Triggerer, 0), N(51), N(5))));
        host.Slots[("kit:3", SessionScriptPoint.RemoveItem)] = Changing;
        host.Slots[("kit:2", SessionScriptPoint.RemoveItem)] = Failing;
        var refused = owner.Equip(owner.Version, "kit:3", EquipmentSlot.Ring1, Wearable(3, Ring(1), all), Drawn, Accept, host);
        Assert.AreEqual((GameSessionDisposition.Rejected, GameSessionFailure.ItemScriptUnsupported), (refused.Disposition, refused.Failure));
        CollectionAssert.AreEqual(new[] { (28, "kit:3"), (25, "kit:2"), (28, "kit:2") }, Runs(refused));
        Assert.AreEqual(before.Version, owner.Version);
        Assert.AreEqual(before.Item2, Charms(owner));
        CollectionAssert.AreEqual(before.Item3, owner.QueryItemScriptSlots().ToArray());
        Assert.AreEqual(false, owner.Flags.Read(new(new(Guid.NewGuid()), owner.Flags.Version, new(3))).Value);
        Assert.AreEqual("kit:2", owner.QueryEquipment().Worn.Single().Entity);
    }

    [TestMethod]
    public void A_ratio_read_after_an_effect_change_of_the_aptitude_in_the_same_run_refuses_the_move()
    {
        // A sharpening charm raises the hero's magick/tech aptitude by 50 (stat 16); the run adds it and then reads the ratio, which the
        // original would read under the new aptitude.
        const int Sharp = 52;
        var (owner, host) = Owner();
        using var _ = owner;
        host.Effects[Sharp] = new(Sharp, [new(EffectAttributeKind.Stat, 16, EffectChangeOperator.Add, 50)]);
        host.Programs[WieldOn] = Program(Do(C(ScrCatalogue.Action.AddEffect, (Triggerer, 0), N(Sharp), N(5))),
            Do(C(ScrCatalogue.Action.MagickTechAdjustment, N(10), (Attachee, 0), (Triggerer, 0), (Local, 0))), Do(C(ScrCatalogue.Action.ReturnRunDefault)));
        var ring = Wearable(3, Ring(1, complexity: 40), SessionScriptPoint.WieldOn);
        var refused = owner.Equip(owner.Version, "kit:3", EquipmentSlot.Ring1, ring, Drawn, Accept, host);
        Assert.AreEqual((GameSessionDisposition.Rejected, GameSessionFailure.ItemScriptUnsupported, 0L), (refused.Disposition, refused.Failure, owner.Version.Revision));
        // The other order (read, then add) is what the installed scripts do: accepted.
        host.Programs[WieldOn] = Program(Do(C(ScrCatalogue.Action.MagickTechAdjustment, N(10), (Attachee, 0), (Triggerer, 0), (Local, 0))),
            Do(C(ScrCatalogue.Action.AddEffect, (Triggerer, 0), N(Sharp), N(5))), Do(C(ScrCatalogue.Action.ReturnRunDefault)));
        Assert.AreEqual(GameSessionDisposition.Changed, owner.Equip(owner.Version, "kit:3", EquipmentSlot.Ring1, ring, Drawn, Accept, host).Disposition);
    }

    [TestMethod]
    public void A_slot_change_of_an_object_outside_the_move_and_a_refused_take_off_check_run_nothing_applied()
    {
        var (owner, host) = Owner();
        using var _ = owner;
        // Wield on call-exes the PC's own wield-on slot, which changes its local flag: the PC's slot has no home.
        const int PcScript = 705;
        host.Programs[PcScript] = Program(Do(C(ScrCatalogue.Action.SetLocalFlag, N(1))), Do(C(ScrCatalogue.Action.ReturnRunDefault)));
        host.Slots[(SessionEventKinds.PlayerEntity, SessionScriptPoint.WieldOn)] = PcScript;
        host.Programs[WieldOn] = Program(Do(C(ScrCatalogue.Action.CallScriptEx, (Triggerer, 0), N(24), N(0), (Triggerer, 0))), Do(C(ScrCatalogue.Action.ReturnRunDefault)));
        var ring = Wearable(3, Ring(1), SessionScriptPoint.WieldOn, SessionScriptPoint.WieldOff);
        Assert.AreEqual((GameSessionDisposition.Rejected, GameSessionFailure.ItemScriptUnsupported),
            (owner.Equip(owner.Version, "kit:3", EquipmentSlot.Ring1, ring, Drawn, Accept, host) is var refused ? (refused.Disposition, refused.Failure) : default));
        // A take-off the insert check refuses runs no script.
        host.Programs[WieldOn] = Scaled;
        Assert.AreEqual(GameSessionDisposition.Changed, owner.Equip(owner.Version, "kit:3", EquipmentSlot.Ring1, ring, Drawn, Accept, host).Disposition);
        var kept = owner.Unequip(owner.Version, EquipmentSlot.Ring1, _ => SessionInsertVerdict.NoRoom, host);
        Assert.AreEqual((GameSessionDisposition.Rejected, GameSessionFailure.InsertRefused, 0), (kept.Disposition, kept.Failure, kept.Scripts.Count));
        Assert.AreEqual(10, Charms(owner));
        // Giving the worn scripted ring to an NPC is refused: that path runs no item events.
        Assert.AreEqual(GameSessionFailure.None, owner.AttachMapInventory(Exterior, InventoryStateTests.Seed()).Failure);
        Assert.AreEqual(GameSessionFailure.ItemScriptUnsupported, owner.GiveItemToNpc(owner.Version, "virgil", "kit:3", Accept).Failure);
    }

    [TestMethod]
    public void Equip_best_leaves_a_scripted_worn_item_in_its_slot()
    {
        var (owner, host) = Owner();
        using var _ = owner;
        Assert.AreEqual(GameSessionDisposition.Changed, owner.Equip(owner.Version, "kit:3", EquipmentSlot.Ring1,
            Wearable(3, Ring(1), SessionScriptPoint.WieldOn, SessionScriptPoint.WieldOff), Drawn, Accept, host).Disposition);
        var ring = Wearable(3, Ring(1), SessionScriptPoint.WieldOn, SessionScriptPoint.WieldOff);
        var best = owner.EquipBest(owner.Version, entity => entity switch
        {
            "kit:2" => Wearable(2, Ring(9)) with { Worth = 900 },
            "kit:3" => ring with { Worth = 2 },
            _ => null,
        }, Drawn, Accept);
        Assert.AreEqual("kit:3", owner.QueryEquipment().Worn.Single(worn => worn.Slot == EquipmentSlot.Ring1).Entity);
        Assert.AreEqual(10, Charms(owner));
        var step = best.Steps.Single(step => step.Slot == EquipmentSlot.Ring1);
        Assert.AreEqual(("kit:3", GameSessionFailure.ItemScriptUnsupported, (string?)null), (step.Unsupported, step.Failure, step.TakenOff));
    }

    [TestMethod]
    public void Decode_and_restore_refuse_malformed_script_slots_and_worn_points()
    {
        var (owner, host) = Owner();
        using var _ = owner;
        Assert.AreEqual(GameSessionDisposition.Changed, owner.Equip(owner.Version, "kit:3", EquipmentSlot.Ring1,
            Wearable(3, Ring(1), SessionScriptPoint.WieldOn, SessionScriptPoint.WieldOff), Drawn, Accept, host).Disposition);
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        const string Slot = "{\"entity\":\"kit:3\",\"point\":24,\"script\":700,\"localFlags\":0,\"counters\":10}";
        void Fails(string from, string to, SnapshotDecodeFailure failure, string path)
        {
            var decoded = GameSessionSnapshotCodec.TryDecode(text.Replace(from, to, StringComparison.Ordinal));
            Assert.AreEqual((failure, path), (decoded.Failure, decoded.Path));
        }
        Fails(Slot, Slot + "," + Slot, SnapshotDecodeFailure.NotCanonical, "$.inventory.scriptSlots[1].point");
        Fails("\"point\":24", "\"point\":36", SnapshotDecodeFailure.OutOfRange, "$.inventory.scriptSlots[0].point");
        Fails("\"script\":700", "\"script\":100000", SnapshotDecodeFailure.OutOfRange, "$.inventory.scriptSlots[0].script");
        Fails("\"scripts\":[24,25]", "\"scripts\":[25,24]", SnapshotDecodeFailure.NotCanonical, "$.equipment[0].scripts[1]");
        Fails("\"scripts\":[24,25]", "\"scripts\":[1]", SnapshotDecodeFailure.OutOfRange, "$.equipment[0].scripts[0]");
        Fails("\"scripts\":[24,25]", "\"scripts\":[22]", SnapshotDecodeFailure.NotCanonical, "$.equipment[0].scripts[0]");
        // The dialog-override point decodes but is no slot of an item event.
        var overridden = GameSessionSnapshotCodec.TryDecode(text.Replace("\"point\":24", "\"point\":31", StringComparison.Ordinal));
        Assert.AreEqual(GameSessionFailure.InvalidSnapshot, GameSessionOwner.Restore(overridden.Snapshot!, RestoreOptions()).Failure);
    }
}
