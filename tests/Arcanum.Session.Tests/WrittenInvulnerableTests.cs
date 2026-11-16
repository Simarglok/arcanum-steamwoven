using Arcanum.Simulation.Combat;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.HeroStateTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

/// <summary>
/// ARC-COMBAT-1832 (ledger CQ4/CQ8, exe damage step 0x4B4390): the object flag 0x400000 (invulnerable, field 0x13) that a script's toggle writes on the hero or an NPC lies over the map data's flag in the
/// damage paths: the life's damage, the combatant the damage step reads and the hostility facts. Entities and values are invented.
/// </summary>
[TestClass]
public sealed class WrittenInvulnerableTests
{
    private const int Flag = 0x400000;
    private const string Wolf = "oid:wolf";

    private static GameSessionOwner Owner()
    {
        var result = GameSessionOwner.Create(Descriptor() with { Hero = HalfElf([8, 15, 8, 8, 8, 8, 8, 8, 10, 0]), Experience = ExperienceStateTests.Rules() });
        Assert.AreEqual(GameSessionFailure.None, result.Failure);
        return result.Owner!;
    }

    private static SessionCritterCombat Npc(GameSessionOwner owner, uint objectFlags = 0)
    {
        var build = SessionCritterCombat.From(Wolf, AttackStateTests.Npc(stats: AttackStateTests.Stats(dexterity: 15, constitution: 3), objectFlags: objectFlags));
        Assert.AreEqual(SessionCritterCombatFailure.None, build.Failure);
        return owner.WithNpcEffects(build.Combat!);
    }

    private static CombatantConditions Conditions(GameSessionOwner owner, SessionCritterCombat npc) =>
        npc.Combatant(LifeRules.Evaluate(npc.Loaded, npc.Facts), npc.Loaded, owner.Rules.Combat).Conditions;

    private static void Write(GameSessionOwner owner, string who, bool on, bool dataHas = false) =>
        Assert.AreNotEqual(GameSessionDisposition.Rejected,
            owner.WriteObjectFlag(owner.Version, who, SessionObjectFlagField.Object, Flag, on, dataHas).Disposition);

    [TestMethod]
    public void A_written_flag_makes_an_NPC_lose_no_hit_points_and_clearing_it_gives_the_damage_back()
    {
        using var owner = Owner();
        var npc = Npc(owner).Target;
        Write(owner, Wolf, true);
        var before = owner.QueryLife(npc).View!.Record.State;
        owner.ApplyDamage(owner.Version, npc, new LifeDamage { HitPoints = 3 });
        Assert.AreEqual(before.HitPointDamage, owner.QueryLife(npc).View!.Record.State.HitPointDamage, "invulnerable: nothing lost");
        Write(owner, Wolf, false);
        owner.ApplyDamage(owner.Version, npc, new LifeDamage { HitPoints = 3 });
        Assert.AreEqual(before.HitPointDamage + 3, owner.QueryLife(npc).View!.Record.State.HitPointDamage);
    }

    [TestMethod]
    public void A_written_flag_reaches_the_combatant_the_damage_step_reads_over_the_datas_value()
    {
        using var owner = Owner();
        Assert.IsFalse(Conditions(owner, Npc(owner)).HasFlag(CombatantConditions.Invulnerable));
        Write(owner, Wolf, true);
        Assert.IsTrue(Conditions(owner, Npc(owner)).HasFlag(CombatantConditions.Invulnerable), "written on");
        // A data flag the session cleared is off; the data's own flag stays on without a write.
        Assert.IsTrue(Conditions(owner, Npc(owner, Flag)).HasFlag(CombatantConditions.Invulnerable), "the data's flag, the write agrees");
        Write(owner, Wolf, false);
        Assert.IsFalse(Conditions(owner, Npc(owner)).HasFlag(CombatantConditions.Invulnerable), "written off");
        Write(owner, Wolf, false, dataHas: true);
        Assert.IsFalse(Conditions(owner, Npc(owner, Flag)).HasFlag(CombatantConditions.Invulnerable), "the data's flag cleared by the session");
    }

    [TestMethod]
    public void A_written_flag_makes_the_hero_lose_no_hit_points()
    {
        using var owner = Owner();
        var hero = SessionLifeTarget.Hero;
        var start = owner.QueryHeroLife()!.Record.State.HitPointDamage;
        Write(owner, SessionSpellCaster.Hero, true);
        owner.ApplyDamage(owner.Version, hero, new LifeDamage { HitPoints = 2 });
        Assert.AreEqual(start, owner.QueryHeroLife()!.Record.State.HitPointDamage);
        Assert.IsTrue(owner.QueryHeroAttack()!.Combatant.Conditions.HasFlag(CombatantConditions.Invulnerable));
        Write(owner, SessionSpellCaster.Hero, false);
        owner.ApplyDamage(owner.Version, hero, new LifeDamage { HitPoints = 2 });
        Assert.AreEqual(start + 2, owner.QueryHeroLife()!.Record.State.HitPointDamage);
    }
}
