using Arcanum.Data.Combat;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Equipment;
using Arcanum.Simulation.Items;
using Arcanum.Simulation.Movement;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.AttackStateTests;
using static Arcanum.Session.Tests.InventoryStateTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// ARC-WORLD-113: an NPC throws the "default throws" weapon it wields (the throw goal 37's throw, 0x4B3C00, ledger TW rows): the
// thrower is AttackStateTests' invented small animal wielding a throwing dagger, listed in a synthetic seed of the exterior; the hero
// stands at the session's start cell (100, 200), the thrower ten cells west of it. Entities and numbers are invented.
[TestClass]
public sealed class NpcThrowStateTests
{
    private const string Thrower = "thrower", Dagger = "dagger", Other = "other";
    private static readonly CellCoord ThrowerCell = new(90, 200), HeroCell = Start.Cell;

    private static CombatFields DaggerFields() =>
        new(CombatFieldOrigin.AreaObject, new CombatObjectFields(5, 5000, 0, 0, 0, 0, 0, 10, 0, 0, 4, [0, 0, 0, 0, 0], 0),
            new CombatItemFields(0, 15, 1, 0, 0, 0, (int)EquipmentSlot.Weapon, [10000, 10000, 10000, 10000, 10000], 0, 0, CombatItemArt.ItemArtType << 28, 0),
            new CombatWeaponFields(CombatWeaponFlags.DefaultThrows, CombatWeaponSkill.Melee, 0, 0, [new(2, 6), new(0, 0), new(0, 0), new(0, 0), new(0, 0)],
                [0, 0, 0, 0, 0], 10, 0, 1, 0, 0, 0, 10000, 0, 0, 0, 0, 0, 0, 0), null, null, null, null, null);

    private static SessionCritterCombat Combat(string entity = Thrower, bool armed = true, int throwing = 5)
    {
        var skills = new int[12];
        skills[(int)BasicCombatSkill.Throwing] = throwing;
        var build = SessionCritterCombat.From(entity, Npc(skills: skills), armed ? [DaggerFields()] : null);
        Assert.AreEqual(SessionCritterCombatFailure.None, build.Failure);
        return build.Combat!;
    }

    private static SessionMapInventorySeed Seed(params int[] scripts) => new(
        [new(Thrower, SessionHolderKind.Npc, false, true, [], [Dagger]), new(Other, SessionHolderKind.Npc, false, true, [], [])],
        [new(Dagger, Facts(SessionItemType.Weapon, 2001, location: 1004, scripts: scripts), null)]);

    private static GameSessionOwner Owner(ulong seed = 0, params int[] scripts)
    {
        var owner = GameSessionOwner.Create(Descriptor() with { Hero = HeroStateTests.HalfElf(), RandomSeed = seed }).Owner!;
        Assert.AreEqual(GameSessionFailure.None, owner.AttachMapInventory(Exterior, Seed(scripts)).Failure);
        return owner;
    }

    private static Dictionary<EquipmentSlot, SessionWearItem> Wielded(bool noDrop = false) =>
        new() { [EquipmentSlot.Weapon] = new SessionWearItem(Dagger, new(20, 0, 0, false)) { NoDrop = noDrop } };

    private static SessionNpcThrow AtHero(int throwing = 5) => new(Combat(throwing: throwing), Dagger, 15, ThrowerCell, HeroCell)
    {
        StruckHero = true, StruckCell = HeroCell, ThrowerWorn = Wielded(),
    };

    private static CellCoord? Lying(GameSessionOwner owner, string item) =>
        owner.QueryMapInventory(Exterior)!.Ground.FirstOrDefault(ground => ground.Item.Entity == item)?.Cell;

    [TestMethod]
    public void AThrowAtTheHeroChecksWithThrowingAndLandsTheDaggerOnTheHeroOrItsScatteredTile()
    {
        var hits = 0; var misses = 0;
        var level = Combat().SkillValues(CombatSkill.Throwing, LifeState.Unharmed).Level;
        var scatter = ThrowRules.ScatterTiles(level, ProjectileRules.Distance(ThrowerCell, HeroCell));
        Assert.IsTrue(scatter is > 0 and < 10, $"the scatter is shorter than the distance: {scatter}");
        for (ulong seed = 1; seed <= 200 && (hits == 0 || misses == 0); seed++)
        {
            using var owner = Owner(seed);
            var before = owner.QueryHeroLife()!.Record.State.HitPointDamage;
            var result = owner.ThrowByNpc(owner.Version, AtHero());
            Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None), (result.Disposition, result.Failure));
            var attack = result.Attack!;
            Assert.AreEqual((AttackRefusal.None, GameSessionFailure.None), (attack.Resolution!.Refusal, attack.StepFailure));
            var landed = result.Landed!.Value;
            Assert.AreEqual(landed, Lying(owner, Dagger), "the dagger lies where it landed");
            Assert.IsNull(owner.QueryMapInventory(Exterior)!.Holders.Single(h => h.Entity == Thrower).Items.FirstOrDefault(i => i.Entity == Dagger),
                "and left the thrower's list");
            Assert.IsFalse(owner.QueryItemDecays().Any(decay => decay.Entity == Dagger), "a thrown item gets no decay (TW6, ID9)");
            if (attack.Resolution.Outcome is AttackOutcome.Hit or AttackOutcome.CriticalHit)
            {
                hits++;
                Assert.AreEqual(HeroCell, landed, "a hit lands on the struck critter's tile");
                Assert.IsTrue(owner.QueryHeroLife()!.Record.State.HitPointDamage > before || attack.Life is { HitPointDamage: 0 }, "the hero took the hit");
            }
            else
            {
                misses++;
                // (20 − throwing level) / 5 + 1 tiles from the target location in one direction (no map: every step fits).
                Assert.AreEqual(scatter, ProjectileRules.Distance(landed, HeroCell), $"a miss scatters from the location: {landed}");
            }
            Assert.AreEqual(GameSessionFailure.ItemNotAvailable, owner.ThrowByNpc(owner.Version, AtHero()).Failure, "the dagger is thrown once");
        }
        Assert.IsTrue(hits > 0 && misses > 0, $"both outcomes within the bounded run: {hits} hits, {misses} misses");
    }

    [TestMethod]
    public void TheCheckIsTheThrowingSkillsWithTheThrownRange()
    {
        // The throwing skill's points raise the check's effectiveness (the melee points are the same).
        int Effectiveness(int throwing)
        {
            using var owner = Owner(3);
            return owner.ThrowByNpc(owner.Version, AtHero(throwing)).Attack!.Resolution!.AttackCheck!.Effectiveness;
        }
        Assert.IsTrue(Effectiveness(10) > Effectiveness(0), "throwing, not melee");
        // A weight that makes the thrown range 1 (RH4) makes a throw at 10 tiles impossible: it never hits.
        Assert.AreEqual(1, ThrowRules.Range(Combat().EffectiveStat(Arcanum.Simulation.Character.CharacterStat.Strength,
            LifeState.Unharmed), SkillTraining.None, ThrowRules.MaxWeight));
        for (ulong seed = 1; seed <= 40; seed++)
        {
            using var owner = Owner(seed);
            var attack = owner.ThrowByNpc(owner.Version, AtHero() with { Weight = ThrowRules.MaxWeight }).Attack!;
            Assert.IsFalse(attack.Resolution!.Outcome is AttackOutcome.Hit or AttackOutcome.CriticalHit, $"seed {seed} hit beyond the thrown range");
        }
    }

    [TestMethod]
    public void AThrowAtAnEmptyLocationLandsTheItemThereWithoutARoll()
    {
        using var owner = Owner();
        var draws = owner.QueryRandom().Draws;
        var tile = new CellCoord(98, 203);
        var result = owner.ThrowByNpc(owner.Version, new(Combat(), Dagger, 15, ThrowerCell, tile) { ThrowerWorn = Wielded() });
        Assert.AreEqual((GameSessionDisposition.Changed, (SessionAttackResult?)null, (CellCoord?)tile), (result.Disposition, result.Attack, result.Landed));
        Assert.AreEqual(draws, owner.QueryRandom().Draws);
        Assert.AreEqual(tile, Lying(owner, Dagger));
    }

    [TestMethod]
    public void AThrowAtAnotherNpcStrikesItWithTheThrowersSkill()
    {
        using var owner = Owner(7);
        var victim = Combat(Other, armed: false);
        var cell = new CellCoord(99, 200);
        var result = owner.ThrowByNpc(owner.Version, new(Combat(), Dagger, 15, ThrowerCell, cell)
        {
            Struck = victim, StruckCell = cell, ThrowerWorn = Wielded(), LineOfFire = new(0, false),
        });
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None), (result.Disposition, result.Failure));
        var attack = result.Attack!;
        Assert.AreEqual(AttackRefusal.None, attack.Resolution!.Refusal);
        if (attack.Resolution.Outcome is AttackOutcome.Hit or AttackOutcome.CriticalHit)
        {
            Assert.AreEqual(cell, result.Landed, "a hit lands on the struck NPC's cell");
            Assert.AreEqual(DamageRecipient.Target, attack.Recipient);
        }
        else Assert.AreNotEqual(cell, result.Landed, "a miss scatters away from it");
        Assert.AreEqual(result.Landed, Lying(owner, Dagger));
    }

    [TestMethod]
    public void TheRefusalsComeBeforeAnyRoll()
    {
        using var owner = Owner();
        var version = owner.Version;
        var draws = owner.QueryRandom().Draws;
        void Refused(GameSessionFailure failure, SessionNpcThrow request, string why) =>
            Assert.AreEqual(failure, owner.ThrowByNpc(owner.Version, request).Failure, why);
        Refused(GameSessionFailure.InvalidAttack, AtHero() with { Weight = -1 }, "a weight below 0");
        Refused(GameSessionFailure.InvalidAttack, AtHero() with { StruckCell = null }, "a struck critter without its cell");
        Refused(GameSessionFailure.InvalidAttack, AtHero() with { Struck = Combat(Other, armed: false) }, "both struck kinds");
        Refused(GameSessionFailure.InvalidAttack, AtHero() with { Target = new(-1, 0) }, "a location outside the packing");
        Refused(GameSessionFailure.InvalidAttack, AtHero() with { ThrowerWorn = Wielded(noDrop: true) }, "a no-drop weapon (0x466DA0)");
        Refused(GameSessionFailure.ItemNotAvailable, AtHero() with { Item = "elsewhere" }, "an item not in the thrower's list");
        Refused(GameSessionFailure.AttackUnsupported, AtHero() with { ThrowerWorn = null }, "an item the thrower does not wield");
        Assert.AreEqual(GameSessionFailure.StaleVersion, owner.ThrowByNpc(new(owner.Identity, owner.Version.Revision + 1), AtHero()).Failure);
        Assert.AreEqual((version, draws), (owner.Version, owner.QueryRandom().Draws));

        using var scripted = Owner(0, 6);
        Assert.AreEqual(GameSessionFailure.ItemScriptUnsupported, scripted.ThrowByNpc(scripted.Version, AtHero()).Failure, "the throw script (point 6) is not run");
        using var heroless = GameSessionOwner.Create(Descriptor()).Owner!;
        Assert.AreEqual(GameSessionFailure.AttackUnavailable, heroless.ThrowByNpc(heroless.Version, AtHero()).Failure);
    }
}
