using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Equipment;
using Arcanum.Simulation.Movement;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.HeroStateTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// ARC-COMBAT-017: a wielded weapon that "default throws" leaves the hand at the shot, is checked with the throwing skill and its thrown
// range, flies hidden on the hero's cell and lands where its projectile ends (ledger RH2, RH4, TW rows). The half-elf wields an
// invented throwing dagger (melee skill, range 15, as the installed 6072); the rats are AttackStateTests' small animal. Values are
// invented.
[TestClass]
public sealed class ThrowStateTests
{
    private static readonly SessionInsertCheck Accept = _ => SessionInsertVerdict.Accepted;
    // The throw's target location (a missed throw scatters from it, ARC-COMBAT-018): as many cells from the hero's start cell as the
    // rats' distance.
    private static readonly SessionThrow Dagger = new(15) { Target = new CellCoord(Start.Cell.X + 3, Start.Cell.Y) };
    private static readonly SessionThrow Heavy = new(ThrowRules.MaxWeight) { Target = new CellCoord(Start.Cell.X + 2, Start.Cell.Y) };

    internal static GameSessionOwner Owner()
    {
        var owner = GameSessionOwner.Create(Descriptor() with { Hero = HalfElf() }).Owner!;
        var dagger = new EquipmentItem
        {
            Kind = EquipmentItemKind.Weapon, HitPoints = 20, MaxHitPoints = 20,
            Weapon = new EquipmentWeapon { Skill = CombatSkill.Melee, Damage = DamageRanges.None with { Normal = new(2, 6) }, SpeedFactor = 10, Range = 15 },
        };
        Assert.AreEqual(GameSessionFailure.None, owner.Equip(owner.Version, "kit:1", EquipmentSlot.Weapon, new(Kit[0].Kind, [], false, dagger),
            (_, _) => true, Accept).Failure);
        return owner;
    }

    internal static SessionCritterCombat Rat(string entity = "oid:rat") => SessionCritterCombat.From(entity, AttackStateTests.Npc(hpAdjustment: 5000)).Combat!;

    private static int Hp(GameSessionOwner owner, SessionCritterCombat rat) => owner.QueryLife(rat.Target).View!.Status.HitPoints;

    internal static bool Wields(GameSessionOwner owner) => owner.QueryEquipment().Worn.Any(w => w.Slot == EquipmentSlot.Weapon);

    // Throws until the outcome is a hit (or a miss), picking the dagger up and wielding it again after each other throw.
    internal static SessionAttackResult ThrowUntil(GameSessionOwner owner, SessionCritterCombat rat, bool hit, int distance = 3)
    {
        for (var i = 0; i < 60; i++)
        {
            var shot = owner.AttackCritter(owner.Version, new(rat) { Distance = distance, Flight = true, Throw = Dagger });
            Assert.AreEqual(GameSessionFailure.None, shot.Failure);
            var hits = shot.Resolution!.Outcome is AttackOutcome.Hit or AttackOutcome.CriticalHit;
            if (hits == hit && shot.Resolution.Outcome != AttackOutcome.CriticalMiss) return shot;
            Assert.IsTrue(owner.EndShot(shot.Shot!.Value));
            Rewield(owner);
        }
        Assert.Fail("no such throw");
        return null!;
    }

    private static void Rewield(GameSessionOwner owner)
    {
        Assert.AreEqual(GameSessionDisposition.Changed, owner.PickUpItem(owner.Version, "kit:1", Accept).Disposition);
        var dagger = new EquipmentItem
        {
            Kind = EquipmentItemKind.Weapon, HitPoints = 20, MaxHitPoints = 20,
            Weapon = new EquipmentWeapon { Skill = CombatSkill.Melee, Damage = DamageRanges.None with { Normal = new(2, 6) }, SpeedFactor = 10, Range = 15 },
        };
        Assert.AreEqual(GameSessionFailure.None, owner.Equip(owner.Version, "kit:1", EquipmentSlot.Weapon, new(Kit[0].Kind, [], false, dagger),
            (_, _) => true, Accept).Failure);
    }

    [TestMethod]
    public void TheThrownDaggerLeavesTheHandAndLiesHiddenOnTheHerosCellWithoutDecay()
    {
        using var owner = Owner();
        var rat = Rat();
        var revision = owner.Version.Revision;
        var shot = owner.AttackCritter(owner.Version, new(rat) { Distance = 3, Flight = true, Throw = Dagger });
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None), (shot.Disposition, shot.Failure));
        Assert.IsTrue(owner.Version.Revision > revision);
        Assert.IsFalse(Wields(owner), "the dagger left the hand");
        var placed = owner.QueryPlacedItems().Single();
        Assert.AreEqual(("kit:1", owner.QueryActor().Cell), (placed.Entity, placed.Cell), "it lies on the hero's cell while it flies");
        Assert.AreEqual(0, owner.QueryItemDecays().Count, "the throw's placement schedules no decay");
        Assert.AreEqual("kit:1", owner.QueryThrownShot(shot.Shot!.Value)!.Value.Item);
        Assert.IsTrue(owner.QueryItemInFlight("kit:1"));
        Assert.AreEqual(GameSessionFailure.ItemNotAvailable, owner.PickUpItem(owner.Version, "kit:1", Accept).Failure, "it is hidden until it lands");
        Assert.AreEqual(GameSessionFailure.InvalidAttack, owner.AttackCritter(owner.Version, new(rat) { Distance = 3, Flight = true, Throw = Dagger }).Failure,
            "nothing is wielded to throw now");
    }

    // ARC-SCRIPT-2386 (IV15, 0x4B3C00 sets the off flag of the thrown item and 0x4B2690 clears it): the flight is the flag; the flight given up clears it too.
    [TestMethod]
    public void AThrownItemIsSwitchedOffWhileItFliesAndOnAgainWhenTheShotEnds()
    {
        using var owner = Owner();
        var map = owner.QueryActor().Map;
        Assert.IsFalse(owner.QueryItemOff(map, "kit:1", dataOff: false));
        var shot = owner.AttackCritter(owner.Version, new(Rat()) { Distance = 3, Flight = true, Throw = Dagger });
        Assert.IsTrue(owner.QueryItemOff(map, "kit:1", dataOff: false), "in flight: off");
        Assert.IsNull(owner.QueryObjectChange(map, SessionItemOff.Key("kit:1")), "no record is written: the shot is the state");
        Assert.IsTrue(owner.EndShot(shot.Shot!.Value));
        Assert.IsFalse(owner.QueryItemOff(map, "kit:1", dataOff: false), "the end clears it");
    }

    // ARC-SCRIPT-2386 (IV15, throw node 0x42AB90 refuses an item whose flags & 3 is not 0): a switched-off weapon is not thrown, it stays in the hand.
    [TestMethod]
    public void ASwitchedOffWeaponIsNotThrown()
    {
        using var owner = Owner();
        var map = owner.QueryActor().Map;
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ApplyChanges(owner.Version, null, [new MapChangeEdit(map, SessionItemOff.Key("kit:1"), 1)]).Disposition);
        var shot = owner.AttackCritter(owner.Version, new(Rat()) { Distance = 3, Flight = true, Throw = Dagger });
        Assert.AreEqual(GameSessionFailure.None, shot.Failure);
        Assert.IsTrue(Wields(owner), "the weapon stays in the hand");
        Assert.AreEqual(0, owner.QueryPlacedItems().Count, "nothing lies on the ground");
        Assert.IsFalse(owner.QueryItemInFlight("kit:1"));
    }

    // ARC-MAGIC-025 (IC12, IC14): the thrown item is the projectile's item of its location event; its strike is its end.
    [TestMethod]
    public void AThrownShotsLocationEventNamesTheThrownItemUntilItStrikes()
    {
        using var owner = Owner();
        var rat = Rat();
        var shot = ThrowUntil(owner, rat, hit: true);
        var tile = new CellCoord(Start.Cell.X + 3, Start.Cell.Y);
        Assert.AreEqual("kit:1", owner.QueryShotEndItemEvents(shot.Shot!.Value, tile).Single().Item);
        // ARC-COMBAT-1719: the thrown item's end scripts name the hero, the item and the target; the hero's thrown item's hit and miss are the thrown-item host's (Thrown).
        var ends = owner.QueryShotEndScripts(shot.Shot.Value)!.Value;
        Assert.AreEqual((SessionEventKinds.PlayerEntity, "kit:1", true), (ends.Attacker, ends.Weapon, ends.Thrown));
        Assert.IsTrue(ends.IsWeapon, "ARC-COMBAT-1766: the thrown dagger is a weapon, read by the session while it lies hidden on the hero's cell");
        Assert.AreEqual(GameSessionFailure.None, owner.LandShot(owner.Version, shot.Shot.Value, new(rat)).Failure);
        Assert.IsNull(owner.QueryShotEndScripts(shot.Shot.Value), "struck: the strike ran the scripts, the landing the session splits off runs none");
        Assert.AreEqual(0, owner.QueryShotEndItemEvents(shot.Shot.Value, tile).Count, "struck: the landing the session splits off fires nothing more");
        Assert.AreEqual(GameSessionDisposition.Changed, owner.LandThrownItem(owner.Version, shot.Shot.Value, tile).Disposition);
    }

    [TestMethod]
    public void ThrowIsCheckedWithTheThrowingSkillAndTheThrownRange()
    {
        using var owner = Owner();
        var rat = Rat();
        var strength = owner.QueryHeroAttack()!.Combatant.Strength;
        Assert.AreEqual(ThrowRules.Range(strength, SkillTraining.None, 15), owner.AttackCritter(owner.Version, new(rat) { Distance = 3, Flight = true, Throw = Dagger }) is var thrown
            && owner.QueryThrownShot(thrown.Shot!.Value) is { } facts ? facts.Range : -1, "the flight ends at the thrown range");
        Assert.AreEqual(0, thrown.Resolution!.Difficulty!.Range, "within the thrown range and half the Perception: no range penalty");
        owner.EndShot(thrown.Shot!.Value);
        Rewield(owner);
        // A weight that thrown range 1 cannot carry two cells: the check is impossible, the throw misses and still flies.
        var heavy = owner.AttackCritter(owner.Version, new(rat) { Distance = 2, Flight = true, Throw = Heavy });
        Assert.AreEqual(1, owner.QueryThrownShot(heavy.Shot!.Value)!.Value.Range);
        Assert.IsTrue(heavy.Resolution!.Difficulty!.Range >= 1_000_000);
        Assert.IsFalse(heavy.Resolution.Outcome is AttackOutcome.Hit or AttackOutcome.CriticalHit);
        Assert.IsFalse(Wields(owner), "a missed throw leaves the hand too");
    }

    [TestMethod]
    public void AHitStrikesThenTheItemLandsOnTheStruckCellAndCanBePickedUp()
    {
        using var owner = Owner();
        var rat = Rat();
        var before = Hp(owner, rat);
        var shot = ThrowUntil(owner, rat, hit: true);
        var flying = shot.Shot!.Value;
        Assert.AreEqual(before, Hp(owner, rat), "the damage waits for the arrival");
        var struck = owner.LandShot(owner.Version, flying, new(rat));
        Assert.AreEqual((GameSessionFailure.None, shot.Resolution!.Outcome), (struck.Failure, struck.Resolution!.Outcome));
        Assert.AreEqual(before - struck.Life!.HitPointDamage, Hp(owner, rat));
        CollectionAssert.AreEqual(new[] { flying }, owner.QueryShotsInFlight().ToArray(), "the thrown shot waits for its item's landing");
        Assert.AreEqual(GameSessionFailure.InvalidAttack, owner.LandShot(owner.Version, flying, new(rat)).Failure, "a shot strikes once");
        Assert.AreEqual(GameSessionFailure.InvalidAttack, owner.LandThrownItem(owner.Version, flying, new CellCoord(-1, 5)).Failure);
        var cell = new CellCoord(owner.QueryActor().Cell.X + 3, owner.QueryActor().Cell.Y);
        var revision = owner.Version.Revision;
        var landed = owner.LandThrownItem(owner.Version, flying, cell);
        Assert.AreEqual((GameSessionDisposition.Changed, revision + 1), (landed.Disposition, landed.Version.Revision));
        Assert.AreEqual(cell, owner.QueryPlacedItems().Single().Cell);
        Assert.AreEqual((0, 0, false), (owner.QueryShotsInFlight().Count, owner.QueryItemDecays().Count, owner.QueryItemInFlight("kit:1")),
            "the landing ends the shot and schedules no decay");
        Assert.AreEqual(GameSessionFailure.InvalidAttack, owner.LandThrownItem(owner.Version, flying, cell).Failure, "it lands once");
        Assert.AreEqual(GameSessionDisposition.Changed, owner.PickUpItem(owner.Version, "kit:1", Accept).Disposition);
        Assert.IsFalse(Wields(owner), "no weapon is wielded again by itself");
    }

    [TestMethod]
    public void AGivenUpThrowLeavesTheItemOnTheHerosCellAndARestoredSessionKeepsIt()
    {
        using var owner = Owner();
        var rat = Rat();
        var shot = owner.AttackCritter(owner.Version, new(rat) { Distance = 3, Flight = true, Throw = Dagger }).Shot!.Value;
        using (var restored = GameSessionOwner.Restore(owner.Capture(), RestoreOptions()).Owner!)
        {
            Assert.AreEqual((0, "kit:1", false), (restored.QueryShotsInFlight().Count, restored.QueryPlacedItems().Single().Entity,
                restored.QueryItemInFlight("kit:1")), "a snapshot keeps the item where it was thrown from, without the flight");
        }
        Assert.IsTrue(owner.EndShot(shot));
        Assert.AreEqual(owner.QueryActor().Cell, owner.QueryPlacedItems().Single().Cell);
        Assert.AreEqual(GameSessionFailure.InvalidAttack, owner.LandThrownItem(owner.Version, shot, new CellCoord(1, 1)).Failure);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.PickUpItem(owner.Version, "kit:1", Accept).Disposition);
    }

    [TestMethod]
    public void AThrownMissFliesOnAndAThrowAtAWallLandsToo()
    {
        using var owner = Owner();
        var rat = Rat();
        var miss = owner.AttackCritter(owner.Version, new(rat) { Distance = 2, Flight = true, Throw = Heavy });
        Assert.IsNotNull(miss.Scatter, "a missed throw scatters (ARC-COMBAT-018)");
        // The scatter cleared its target (PF6): the rat it missed may take a stray strike on the way, as any critter.
        var stray = owner.LandShot(owner.Version, miss.Shot!.Value, new(rat));
        Assert.AreEqual((GameSessionFailure.None, AttackOutcome.Hit), (stray.Failure, stray.Resolution!.Outcome), "a stray strike is a forced hit");
        Assert.AreEqual(GameSessionDisposition.Changed, owner.LandThrownItem(owner.Version, miss.Shot.Value, new CellCoord(7, 7)).Disposition);
        Rewield(owner);
        var wall = owner.AttackObject(owner.Version, new() { Wall = true, Distance = 2, Flight = true, Throw = Dagger });
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None), (wall.Disposition, wall.Failure));
        Assert.IsFalse(Wields(owner));
        var flying = wall.Shot!.Value;
        Assert.AreEqual(GameSessionFailure.None, owner.LandShotOnObject(owner.Version, flying, new() { Wall = true }).Failure);
        Assert.AreEqual(GameSessionFailure.InvalidAttack, owner.LandShotOnObject(owner.Version, flying, new() { Wall = true }).Failure, "a shot strikes once");
        Assert.AreEqual(GameSessionDisposition.Changed, owner.LandThrownItem(owner.Version, flying, new CellCoord(9, 9)).Disposition);
    }

    // A no-drop weapon would stay in the hand (0x466DA0), but the session never wields one: Equip refuses it (EQ rows).
    [TestMethod]
    public void AThrowMustFlyAndCarryAWeight()
    {
        var rat = Rat();
        using var owner = Owner();
        Assert.AreEqual(GameSessionFailure.InvalidAttack, owner.AttackCritter(owner.Version, new(rat) { Distance = 3, Throw = Dagger }).Failure);
        Assert.AreEqual(GameSessionFailure.InvalidAttack, owner.AttackCritter(owner.Version, new(rat) { Distance = 3, Flight = true, Throw = new(-1) }).Failure);
        Assert.AreEqual(GameSessionFailure.InvalidAttack, owner.AttackObject(owner.Version, new() { Wall = true, Distance = 2, Throw = Dagger }).Failure);
        Assert.AreEqual(GameSessionFailure.InvalidAttack, owner.AttackCritter(owner.Version, new(rat) { Distance = 16, Flight = true, Throw = Dagger }).Failure,
            "the reach before a throw is the weapon's (RH1)");
        Assert.IsTrue(Wields(owner), "a refused throw keeps the dagger");
    }

    [TestMethod]
    public void OnlyAMeleeWeaponIsThrownAndARefusedAttackKeepsIt()
    {
        using var owner = Owner();
        var rat = Rat();
        // The resolver refuses an attack on a dead target before any throw.
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ApplyDamage(owner.Version, rat.Target, new LifeDamage { HitPoints = 100_000 }).Disposition);
        var corpse = owner.AttackCritter(owner.Version, new(rat) { Distance = 3, Flight = true, Throw = Dagger });
        Assert.AreEqual((GameSessionDisposition.NoChange, AttackRefusal.TargetDead, (SessionShot?)null), (corpse.Disposition, corpse.Resolution!.Refusal, corpse.Shot));
        Assert.IsTrue(Wields(owner), "a refused attack throws nothing");

        using var archer = GameSessionOwner.Create(Descriptor() with { Hero = HalfElf() }).Owner!;
        var bow = new EquipmentItem
        {
            Kind = EquipmentItemKind.Weapon, HitPoints = 20, MaxHitPoints = 20,
            Weapon = new EquipmentWeapon { Skill = CombatSkill.Bow, Damage = DamageRanges.None with { Normal = new(2, 8) }, SpeedFactor = 10, Range = 10 },
        };
        Assert.AreEqual(GameSessionFailure.None, archer.Equip(archer.Version, "kit:1", EquipmentSlot.Weapon, new(Kit[0].Kind, [], false, bow),
            (_, _) => true, Accept).Failure);
        Assert.AreEqual(GameSessionFailure.InvalidAttack, archer.AttackCritter(archer.Version, new(Rat()) { Distance = 3, Flight = true, Throw = Dagger }).Failure,
            "a bow is not thrown");
    }

    [TestMethod]
    public void AStruckThrownShotStillCountsAgainstTheLimitUntilItLands()
    {
        using var owner = Owner();
        var rat = Rat();
        var shot = ThrowUntil(owner, rat, hit: true).Shot!.Value;
        Assert.AreEqual(GameSessionFailure.None, owner.LandShot(owner.Version, shot, new(rat)).Failure);
        Assert.AreEqual(1, owner.QueryShotsInFlight().Count);
        Assert.IsNotNull(owner.QueryThrownShot(shot));
        Assert.AreEqual(GameSessionFailure.InvalidAttack, owner.LandShotOnObject(owner.Version, shot, new() { Wall = true }).Failure, "a shot strikes once");
        Assert.AreEqual(GameSessionDisposition.Changed, owner.LandThrownItem(owner.Version, shot, new CellCoord(3, 3)).Disposition);
        Assert.AreEqual(0, owner.QueryShotsInFlight().Count);
    }

    // ARC-COMBAT-018 (0x4B39B0, ledger RH2, PS rows): a missed throw flies from its target location (20 − level) / 5 + 1 tiles, at
    // most its distance, in one of eight directions the session draws, and carries no target.
    [TestMethod]
    public void AMissedThrowScattersFromItsTargetLocation()
    {
        using var owner = Owner();
        var rat = Rat();
        var here = owner.QueryActor().Cell;
        var level = owner.QueryHeroSkills()![Arcanum.Simulation.Skills.Skill.Throwing].Level;
        var target = new CellCoord(here.X + 3, here.Y);
        var miss = ThrowUntil(owner, rat, hit: false);
        var tiles = ThrowRules.ScatterTiles(level, 3);
        Assert.IsTrue(tiles >= 1);
        var reachable = Enumerable.Range(0, ThrowRules.ScatterDirections)
            .Select(r => ThrowRules.Scatter(target, r, tiles, (c, d) => Arcanum.Simulation.Navigation.OriginalPathRequest.Neighbor(c, d))).ToArray();
        CollectionAssert.Contains(reachable, miss.Scatter!.Value, "one of the eight directions, the tiles of the thrower's level");
        Assert.AreEqual("kit:1", owner.QueryThrownShot(miss.Shot!.Value)!.Value.Item);

        // A hit keeps its target and does not scatter: it draws only the resolver's rolls, a miss one more.
        Assert.IsTrue(owner.EndShot(miss.Shot.Value));
        Rewield(owner);
        for (var i = 0; i < 60; i++)
        {
            var draws = owner.QueryRandom().Draws;
            var shot = owner.AttackCritter(owner.Version, new(rat) { Distance = 3, Flight = true, Throw = Dagger });
            var hits = shot.Resolution!.Outcome is AttackOutcome.Hit or AttackOutcome.CriticalHit;
            Assert.AreEqual((hits, draws + shot.Resolution.Rolls.Count + (hits ? 0 : 1)), (shot.Scatter is null, owner.QueryRandom().Draws),
                $"throw {i}: {shot.Resolution.Outcome}");
            Assert.IsTrue(owner.EndShot(shot.Shot!.Value));
            Rewield(owner);
            if (hits) return;
        }
        Assert.Fail("no hit");
    }

    // ARC-COMBAT-1223 (PS5, PF10; 0x4B2870): a missed throw has no target, and its end on the scattered tile fires the thrown item's attacks-a-location event there, not once it struck.
    [TestMethod]
    public void AMissedThrowStillAsksItsItemEventAtTheScatteredTile()
    {
        using var owner = Owner();
        var rat = Rat();
        var miss = ThrowUntil(owner, rat, hit: false);
        var scattered = miss.Scatter!.Value;
        var events = owner.QueryShotEndItemEvents(miss.Shot!.Value, scattered);
        Assert.AreEqual(1, events.Count, "the miss's end fires the item's location event");
        Assert.AreEqual(("kit:1", SessionEventKinds.PlayerEntity), (events[0].Item, events[0].Parent));
        var missEnds = owner.QueryShotEndScripts(miss.Shot.Value)!.Value;
        Assert.AreEqual((SessionEventKinds.PlayerEntity, "kit:1", true, null), (missEnds.Attacker, missEnds.Weapon, missEnds.Thrown, missEnds.Target), "a scattered throw has no target");
        Assert.AreEqual(GameSessionDisposition.Changed, owner.LandThrownItem(owner.Version, miss.Shot.Value, scattered).Disposition);
        Assert.AreEqual(0, owner.QueryShotEndItemEvents(miss.Shot.Value, scattered).Count, "landed: the shot is gone");
    }

    // A miss at a critter next to the hero may scatter back onto the hero's own cell (the tiles are capped at the distance 1).
    [TestMethod]
    public void AScatterAtANeighbourMayLandOnTheHerosOwnCell()
    {
        using var owner = Owner();
        var rat = Rat();
        var here = owner.QueryActor().Cell;
        var near = new SessionThrow(ThrowRules.MaxWeight) { Target = new CellCoord(here.X + 1, here.Y) };
        var seen = new HashSet<CellCoord>();
        for (var i = 0; i < 80 && !seen.Contains(here); i++)
        {
            var miss = owner.AttackCritter(owner.Version, new(rat) { Distance = 1, Flight = true, Throw = near });
            Assert.AreEqual(GameSessionFailure.None, miss.Failure);
            var at = miss.Scatter!.Value;
            Assert.AreEqual(1, Math.Max(Math.Abs(at.X - here.X - 1), Math.Abs(at.Y - here.Y)), "one tile from the target location");
            seen.Add(at);
            Assert.IsTrue(owner.EndShot(miss.Shot!.Value));
            Rewield(owner);
        }
        CollectionAssert.Contains(seen.ToArray(), here);
    }

    [TestMethod]
    public void TheScatterStopsAtTheMapsEdgeAndNeedsATargetLocation()
    {
        using var owner = Owner();
        var rat = Rat();
        var target = new CellCoord(Start.Cell.X + 2, Start.Cell.Y);
        var walled = owner.AttackCritter(owner.Version, new(rat) { Distance = 2, Flight = true, Throw = Heavy, DropMap = new NoSteps() });
        Assert.AreEqual((GameSessionFailure.None, target), (walled.Failure, walled.Scatter), "every step leaves the map: it stays on the target location");
        Assert.IsTrue(owner.EndShot(walled.Shot!.Value));
        Rewield(owner);
        // Without the throw's location the attack's target cell is the origin; with neither the throw is refused before any roll.
        var draws = owner.QueryRandom().Draws;
        Assert.AreEqual(GameSessionFailure.InvalidAttack, owner.AttackCritter(owner.Version, new(rat) { Distance = 2, Flight = true, Throw = new(15) }).Failure);
        Assert.AreEqual(GameSessionFailure.InvalidAttack, owner.AttackCritter(owner.Version,
            new(rat) { Distance = 2, Flight = true, Throw = new(15) { Target = new CellCoord(-1, 0) } }).Failure);
        Assert.AreEqual(draws, owner.QueryRandom().Draws);
        Assert.IsTrue(Wields(owner));
        var fromCell = owner.AttackCritter(owner.Version, new(rat) { Distance = 2, Flight = true, Throw = new(ThrowRules.MaxWeight), TargetCell = target, DropMap = new NoSteps() });
        Assert.AreEqual((GameSessionFailure.None, target), (fromCell.Failure, fromCell.Scatter));
    }

    // ARC-COMBAT-1968 (0x4B3C00 and 0x4B2870): the original clears the context's backstab flags (0x4000, 0x8000) after the builder 0x4B2210 for a
    // throw and for a shot, so neither is a backstab even from behind with a dagger-subtype weapon. The same facts and weapon make a melee strike
    // from behind one (the control), so the throw's and the shot's equal damage is the rule and not a missing fact.
    private static GameSessionOwner BackstabOwner(CombatSkill skill, int range)
    {
        var basic = new int[12];
        basic[(int)Arcanum.Simulation.Skills.Skill.Backstab] = 3 | 1 << 6;
        var hero = HalfElf() with { Advancement = SessionHeroAdvancement.None with { BasicSkills = [.. basic] } };
        var owner = GameSessionOwner.Create(Descriptor() with { Hero = hero }).Owner!;
        var weapon = new EquipmentItem
        {
            Kind = EquipmentItemKind.Weapon, HitPoints = 20, MaxHitPoints = 20,
            Weapon = new EquipmentWeapon { Skill = skill, Damage = DamageRanges.None with { Normal = new(4, 4) }, SpeedFactor = 10, Range = range },
        };
        Assert.AreEqual(GameSessionFailure.None, owner.Equip(owner.Version, "kit:1", EquipmentSlot.Weapon, new(Kit[0].Kind, [], false, weapon),
            (_, _) => true, Accept).Failure);
        Assert.IsTrue(owner.QueryHeroAttack()!.Combatant.BackstabLevel > 0, "the setup gives the hero backstab points");
        return owner;
    }

    private static readonly IReadOnlyDictionary<EquipmentSlot, SessionWearItem> DaggerSubtype =
        new Dictionary<EquipmentSlot, SessionWearItem> { [EquipmentSlot.Weapon] = new("kit:1", new(1000, 0, 0, false)) { ArtSubtype = 2 } };

    private static readonly SessionBackstabFacts FromBehindUnseen = new(() => false, () => false);

    private static int MeleeNormal(SessionBackstabFacts? facts)
    {
        using var owner = BackstabOwner(CombatSkill.Melee, 1);
        var rat = Rat();
        for (var attempt = 0; attempt < 400; attempt++)
        {
            var result = owner.AttackCritter(owner.Version, new(rat) { HeroWorn = DaggerSubtype, Backstab = facts });
            Assert.AreEqual(GameSessionFailure.None, result.Failure);
            if (result.Resolution is { Refusal: AttackRefusal.None, Outcome: AttackOutcome.Hit or AttackOutcome.CriticalHit, Damage.Recipient: DamageRecipient.Target } resolution)
                return resolution.Damage.Rolled.Normal;
        }
        Assert.Fail("no hit within the bounded run");
        return 0;
    }

    // The damage a flying shot (a throw or an arrow) deals at its landing, through the facts and the worn dagger-subtype weapon.
    private static int LandedDamage(CombatSkill skill, bool thrown, SessionBackstabFacts? facts)
    {
        using var owner = BackstabOwner(skill, 15);
        var rat = Rat();
        for (var attempt = 0; attempt < 60; attempt++)
        {
            var shot = owner.AttackCritter(owner.Version, new(rat)
            {
                Distance = 3, Flight = true, Throw = thrown ? Dagger : null, HeroWorn = DaggerSubtype, Backstab = facts,
            });
            Assert.AreEqual(GameSessionFailure.None, shot.Failure);
            if (shot.Resolution!.Outcome is AttackOutcome.Hit or AttackOutcome.CriticalHit)
            {
                var struck = owner.LandShot(owner.Version, shot.Shot!.Value, new(rat) { HeroWorn = thrown ? null : DaggerSubtype, Backstab = facts });
                Assert.AreEqual(GameSessionFailure.None, struck.Failure);
                return struck.Life!.HitPointDamage;
            }
            Assert.IsTrue(owner.EndShot(shot.Shot!.Value));
            if (thrown) Rewield(owner);
        }
        Assert.Fail("no hit within the bounded run");
        return 0;
    }

    [TestMethod]
    public void AThrownDaggerFromBehindIsNoBackstab()
    {
        Assert.IsTrue(MeleeNormal(FromBehindUnseen) > MeleeNormal(null), "control: a melee strike from behind adds the backstab level");
        Assert.AreEqual(LandedDamage(CombatSkill.Melee, thrown: true, null), LandedDamage(CombatSkill.Melee, thrown: true, FromBehindUnseen),
            "a thrown dagger from behind deals the plain damage (0x4B3C00 clears the backstab flags)");
    }

    [TestMethod]
    public void AnArrowFromBehindIsNoBackstab()
    {
        Assert.AreEqual(LandedDamage(CombatSkill.Bow, thrown: false, null), LandedDamage(CombatSkill.Bow, thrown: false, FromBehindUnseen),
            "a shot from behind deals the plain damage (0x4B2870 drops the builder's backstab flags)");
    }

    // A map where every step leaves it.
    private sealed class NoSteps : Arcanum.Simulation.Items.IDropMap
    {
        public CellCoord? Step(CellCoord from, int rotation) => null;
        public Arcanum.Simulation.Items.DropPath Path(CellCoord from, CellCoord to, Arcanum.Simulation.Items.DropOwner owner) => new(0, 0, []);
        public bool HasCritter(CellCoord cell) => false;
    }
}
