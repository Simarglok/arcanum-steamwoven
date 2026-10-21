using Arcanum.Simulation.Magic;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Simulation.Tests.Magic;

// ARC-MAGIC-006: the invocation check (ledger IV1–IV12). Objects, records and the world double are invented.
[TestClass]
public sealed class SpellInvocationRulesTests
{
    private static readonly SpellLocation Here = new(100, 100);
    private static readonly SpellObjectFacts Mage = new("mage", SpellObjectFacts.PcType, Here, Aptitude: 40, Intelligence: 10);
    private static readonly SpellObjectFacts Orc = new("orc", SpellObjectFacts.NpcType, new(103, 98), Aptitude: 0, Intelligence: 8);
    private static readonly SpellInvocationRecord Bolt = new(false, 10, 0, 0, 0);

    private static SpellInvocation Cast(SpellObjectFacts? parent = null, SpellObjectFacts? target = null, int spell = 33) =>
        new(spell, parent ?? Mage, parent ?? Mage, target ?? Orc, SpellLocation.None);

    private sealed class World : ISpellInvocationWorld
    {
        public SpellLineOfFire Line { get; init; } = new(0, null);
        public bool Matches { get; init; } = true;
        public bool Tiles { get; init; } = true;
        public SpellLocation Fallback { get; init; } = SpellLocation.None;
        public int LineCalls { get; private set; }
        public int? Radius { get; private set; }

        public SpellLineOfFire LineOfFire(SpellObjectFacts parent, SpellLocation tile)
        {
            LineCalls++;
            return Line;
        }

        public bool TargetMatchesBeginArea(SpellInvocation invocation) => Matches;

        public bool BeginAreaTakesTiles(int spell) => Tiles;

        public SpellLocation FirstTileInBeginArea(SpellInvocation invocation, int radius)
        {
            Radius = radius;
            return Fallback;
        }
    }

    private sealed class Rolls(params int[] values) : ISpellRollSource
    {
        private int _next;

        public List<(SpellRollPurpose, int, int)> Made { get; } = [];

        public int Roll(SpellRollPurpose purpose, int min, int max)
        {
            Made.Add((purpose, min, max));
            return values[_next++];
        }
    }

    private static SpellInvocationResult Check(SpellInvocation invocation, SpellInvocationRecord? record = null, World? world = null,
        Rolls? rolls = null) => SpellInvocationRules.Check(invocation, record ?? Bolt, world ?? new World(), rolls ?? new Rolls());

    [TestMethod]
    public void AnOrdinaryCastPassesAndEachEarlyStepRefusesInOrder()
    {
        Assert.IsTrue(Check(Cast()).Allowed);
        Assert.AreEqual(SpellInvocationRefusal.NotTheirTurn, Check(Cast() with { TurnBased = true, TurnBasedActor = "orc", Spell = -1 }).Refusal, "the turn first");
        Assert.IsTrue(Check(Cast() with { TurnBased = true, TurnBasedActor = "mage" }).Allowed);
        Assert.AreEqual(SpellInvocationRefusal.NotTheirTurn, Check(Cast() with { TurnBased = true }).Refusal, "no current critter matches no critter");
        Assert.IsTrue(Check(Cast() with { TurnBasedActor = "orc" }).Allowed, "outside turn-based combat the actor is not asked");
        var wand = new SpellObjectFacts("wand", 6, Here);
        Assert.IsTrue(Check(new SpellInvocation(33, wand, wand, Orc, SpellLocation.None, TurnBased: true, TurnBasedActor: "orc")).Allowed,
            "only a critter parent waits for its turn");
        Assert.AreEqual(SpellInvocationRefusal.InvalidSpell, Check(Cast(spell: SpellInvocationRules.Spells)).Refusal);
        Assert.AreEqual(SpellInvocationRefusal.InvalidSpell, Check(Cast(spell: SpellInvocationRules.Spells), Bolt with { Range = 0 }).Refusal,
            "the spell number before the range");
        Assert.AreEqual(SpellInvocationRefusal.OutOfRange, Check(Cast(), Bolt with { Range = 2 }, new World { Line = new(100, null) }).Refusal,
            "the range before the line of fire");
        Assert.AreEqual(SpellInvocationRefusal.NoLineOfFire, Check(Cast(), Bolt with { DisallowedTargetSpellFlags = 1 },
            new World { Line = new(100, null) }).Refusal, "the line of fire before the flags");
    }

    [TestMethod]
    public void TheRangeIsChebyshevAndAnInventoryIsAlwaysInRange()
    {
        Assert.IsTrue(SpellInvocationRules.InRange(Here, new(110, 95), null, 10));
        Assert.IsFalse(SpellInvocationRules.InRange(Here, new(111, 100), null, 10));
        Assert.IsTrue(SpellInvocationRules.InRange(SpellLocation.None, SpellLocation.None, null, 0), "a parent without a location");
        Assert.IsFalse(SpellInvocationRules.InRange(Here, SpellLocation.None, null, 10), "no tile");
        Assert.IsTrue(SpellInvocationRules.InRange(Here, new(500, 500), Orc, 3), "the object's location replaces the tile");
        var carried = Orc with { Location = SpellLocation.None, ObjectFlags = SpellObjectFacts.InInventory };
        Assert.IsTrue(SpellInvocationRules.InRange(Here, SpellLocation.None, carried, 0));
        Assert.IsFalse(SpellInvocationRules.InRange(Here, Here, null, -1), "a negative range admits nothing");
        Assert.AreEqual(0xFFFFFFFFL, SpellLocation.Distance(new(0, 1), new(uint.MaxValue, 1)), "differences are not wrapped");
    }

    [TestMethod]
    public void TheLineOfFireAsksTheWorldUnlessTheTargetIsTheParentOrCarried()
    {
        var world = new World { Line = new(40, new("tree", 1, new(101, 100))) };
        Assert.AreEqual(SpellInvocationRefusal.NoLineOfFire, Check(Cast(), world: world).Refusal);
        Assert.IsTrue(Check(Cast(), world: new World { Line = new(40, new("smoke", 1, Here, ObjectFlags: SpellObjectFacts.ShootThrough)) }).Allowed);
        Assert.IsTrue(Check(Cast(), world: new World { Line = new(99, Orc) }).Allowed, "the target itself blocks nothing");
        var self = new World { Line = new(100, null) };
        Assert.IsTrue(Check(Cast(target: Mage), world: self).Allowed);
        Assert.AreEqual(0, self.LineCalls, "a self-cast skips the line");
        var potion = new SpellObjectFacts("potion", 8, SpellLocation.None, ObjectFlags: SpellObjectFacts.InInventory);
        Assert.IsTrue(Check(Cast(target: potion), world: self).Allowed, "an item in an inventory is visible");
        Assert.IsTrue(SpellInvocationRules.HasLineOfFire(new(33, Mage, Mage, null, SpellLocation.None), self), "no tile and no object");
        var tileCast = new SpellInvocation(33, Mage, Mage, null, new(104, 100));
        Assert.IsFalse(SpellInvocationRules.HasLineOfFire(tileCast, self));
    }

    [TestMethod]
    public void ForbiddenFlagsAndBondsOfMagick()
    {
        var bound = Mage with { SpellFlags = SpellObjectFacts.BondsOfMagick };
        Assert.AreEqual(SpellInvocationRefusal.ForbiddenFlags, Check(Cast(bound), Bolt with { DisallowedCasterSpellFlags = SpellObjectFacts.BondsOfMagick }).Refusal);
        Assert.AreEqual(SpellInvocationRefusal.BondsOfMagick, Check(Cast(bound)).Refusal);
        var ring = new SpellObjectFacts("ring", 12, SpellLocation.None, ObjectFlags: SpellObjectFacts.InInventory);
        Assert.IsTrue(Check(new SpellInvocation(33, bound, ring, Orc, SpellLocation.None)).Allowed, "an item still works");
        Assert.AreEqual(SpellInvocationRefusal.ForbiddenFlags, Check(Cast(target: Orc with { CritterFlags = 0x40 }), Bolt with { DisallowedTargetCritterFlags = 0x40 }).Refusal);
        Assert.IsTrue(SpellInvocationRules.FlagsAllow(Mage, new("door", 2, Here, CritterFlags: 0x40), Bolt with { DisallowedTargetCritterFlags = 0x40 }),
            "critter flags only of a PC or NPC");
        Assert.IsTrue(SpellInvocationRules.FlagsAllow(null, Orc with { SpellFlags = 1 }, Bolt with { DisallowedTargetSpellFlags = 1 }),
            "no parent tests nothing");
    }

    [TestMethod]
    public void AnNpcsFailedTargetIsReplacedByATileInRadiusTwo()
    {
        var npc = Orc with { Id = "shaman", Location = Here };
        var target = Mage with { Location = new(104, 100) };
        var world = new World { Matches = false, Fallback = new(105, 101) };
        var result = Check(Cast(npc, target), world: world);
        Assert.AreEqual((SpellInvocationRefusal.None, new SpellLocation(105, 101)), (result.Refusal, result.ReplacedTile));
        Assert.AreEqual(SpellInvocationRules.FallbackRadius, world.Radius);
        Assert.AreEqual(SpellInvocationRefusal.NoValidTarget, Check(Cast(npc, target), world: new World { Matches = false }).Refusal);
        Assert.AreEqual(SpellInvocationRefusal.NoValidTarget, Check(Cast(npc, target), world: new World { Matches = false, Tiles = false, Fallback = Here }).Refusal);
        Assert.AreEqual(SpellInvocationRefusal.NoValidTarget, Check(new SpellInvocation(33, npc, npc, null, new(104, 100)),
            world: new World { Matches = false, Fallback = Here }).Refusal, "a tile target is not replaced");
        Assert.IsTrue(Check(Cast(), world: new World { Matches = false }).Allowed, "a PC skips the fallback");
    }

    [TestMethod]
    public void ATechnologistCannotUseAPowerlessMagicItemAndMg5NeverRefusesButRolls()
    {
        var tech = Mage with { Aptitude = -60 };
        var wand = new SpellObjectFacts("wand", 6, SpellLocation.None, ObjectFlags: SpellObjectFacts.InInventory);
        var powerless = Check(new SpellInvocation(33, tech, wand, Orc, SpellLocation.None, SourceItemPower: 0));
        Assert.AreEqual((SpellInvocationRefusal.ItemAptitude, true), (powerless.Refusal, powerless.PlaysResisted));
        Assert.IsTrue(Check(new SpellInvocation(33, tech, wand, Orc, SpellLocation.None, SourceItemPower: 3)).Allowed);
        Assert.IsTrue(Check(new SpellInvocation(33, tech, wand, Orc, SpellLocation.None), Bolt with { Technology = true }).Allowed,
            "a technology record skips the aptitude");

        var rolls = new Rolls(1);
        Assert.IsTrue(Check(Cast(target: Orc with { Aptitude = -100 }), rolls: rolls).Allowed, "MG5: a d100 of 1 is above (100 − 40) × −100 / 100");
        CollectionAssert.AreEqual(new[] { (SpellRollPurpose.InvocationAptitudeD100, 1, 100) }, rolls.Made);
        var none = new Rolls();
        Assert.IsTrue(Check(Cast(target: Orc with { Aptitude = 30 }), rolls: none).Allowed);
        Assert.AreEqual(0, none.Made.Count, "no d100 for a target that is not a technologist");
        Assert.AreEqual(SpellInvocationRefusal.TargetAptitude, Check(Cast(target: Orc with { Aptitude = -100 }), rolls: new Rolls(-70)).Refusal,
            "a roll source answering below the chance (impossible for a d100) refuses, as the original's comparison would");

        Assert.IsTrue(SpellInvocationRules.HolderCanUseItem(null, 50, () => throw new AssertFailedException("not asked")));
        Assert.IsTrue(SpellInvocationRules.HolderCanUseItem(-50, 0, () => throw new AssertFailedException("not asked")));
        Assert.IsTrue(SpellInvocationRules.HolderCanUseItem(10, 50, () => throw new AssertFailedException("not asked")));
        Assert.IsFalse(SpellInvocationRules.HolderCanUseItem(-50, 50, () => 0));
        Assert.IsTrue(SpellInvocationRules.HolderCanUseItem(-50, 50, () => 1));
    }

    [TestMethod]
    public void AParentlessCastSkipsTheParentStepsButNotTheFallbackOrMg5()
    {
        var target = Orc with { Aptitude = -50, Location = new(9000, 9000), SpellFlags = 1 };
        var world = new World { Line = new(100, null), Matches = false, Fallback = new(9001, 9000) };
        var rolls = new Rolls(-50);
        var record = Bolt with { Range = 0, DisallowedTargetSpellFlags = 1, DisallowedCasterSpellFlags = 1 };
        var result = Check(new SpellInvocation(33, null, null, target, SpellLocation.None, TurnBased: true), record, world, rolls);
        Assert.AreEqual((SpellInvocationRefusal.None, new SpellLocation(9001, 9000)), (result.Refusal, result.ReplacedTile),
            "no range, line, flags or turn without a parent; the fallback still runs and clears the technologist target");
        Assert.AreEqual(0, rolls.Made.Count, "the replaced target has no aptitude: no d100");
        var kept = new Rolls(-50);
        Assert.AreEqual(SpellInvocationRefusal.TargetAptitude,
            Check(new SpellInvocation(33, null, null, target, SpellLocation.None), record, new World(), kept).Refusal,
            "without a parent the chance is the target's aptitude itself (a roll source below it refuses)");
        CollectionAssert.AreEqual(new[] { (SpellRollPurpose.InvocationAptitudeD100, 1, 100) }, kept.Made);
    }

    [TestMethod]
    public void TheItemRefusalAndTechnologyRecordsDrawNoD100()
    {
        var tech = Mage with { Aptitude = -60 };
        var wand = new SpellObjectFacts("wand", 6, SpellLocation.None, ObjectFlags: SpellObjectFacts.InInventory);
        var technologist = Orc with { Aptitude = -80 };
        var rolls = new Rolls();
        Assert.AreEqual(SpellInvocationRefusal.ItemAptitude,
            Check(new SpellInvocation(33, tech, wand, technologist, SpellLocation.None, SourceItemPower: -2), rolls: rolls).Refusal);
        Assert.IsTrue(Check(Cast(target: technologist), Bolt with { Technology = true }, rolls: rolls).Allowed);
        Assert.AreEqual(0, rolls.Made.Count);
        Assert.ThrowsExactly<ArgumentException>(() => Check(new SpellInvocation(33, tech, wand, Orc, SpellLocation.None)),
            "a technologist's item spell without the item power is not guessed");
    }

    [TestMethod]
    public void IntelligenceFiveAndAntiTeleportComeLast()
    {
        var dull = Mage with { Intelligence = 4 };
        Assert.AreEqual(SpellInvocationRefusal.Intelligence, Check(Cast(dull), Bolt with { Technology = true }).Refusal, "for technology records too");
        var scroll = new SpellObjectFacts("scroll", 10, SpellLocation.None, ObjectFlags: SpellObjectFacts.InInventory);
        Assert.IsTrue(Check(new SpellInvocation(33, dull, scroll, Orc, SpellLocation.None)).Allowed, "an item's spell needs no intelligence");
        var teleport = new SpellInvocation(SpellInvocationRules.Teleportation, Mage, Mage, null, new(105, 100), TeleportAllowed: false,
            ParentIsLocalPlayer: true);
        var refused = Check(teleport);
        Assert.AreEqual((SpellInvocationRefusal.AntiTeleport, true), (refused.Refusal, refused.ShowsTeleportMessage));
        Assert.IsFalse(Check(teleport with { ParentIsLocalPlayer = false }).ShowsTeleportMessage, "an NPC is refused silently");
        Assert.IsTrue(Check(teleport with { TeleportAllowed = true }).Allowed);
        Assert.IsTrue(Check(teleport with { Spell = 5 }).Allowed);
    }
}
