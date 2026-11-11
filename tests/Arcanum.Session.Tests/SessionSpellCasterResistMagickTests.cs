using Arcanum.Data.Characters;
using Arcanum.Data.Combat;
using Arcanum.Data.Magic;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Magic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.SessionSpellRecordsTests;

namespace Arcanum.Session.Tests;

// ARC-MAGIC-033 (owner decision D6c of the magic audit ARC-RES-103, ledger RE2): the magic resistance a spell's save reads counts the target's resistmagic effect changes (Resist Magick, spell 40,
// effect 56), for the hero and for an NPC, in both modes; the original reads the stored resistance alone. Entities and values are invented.
[TestClass]
public sealed class SessionSpellCasterResistMagickTests
{
    private const string Target = "oid:target";
    private const string Hero = SessionSpellCaster.Hero;
    private static readonly SpellLocation HeroTile = new(5, 5), TargetTile = new(8, 5);
    // Resist Magick: the magic resistance (attribute 4) +25; a curse: −50.
    private static readonly SessionEffectDefinition ResistMagick = new(56, [new(EffectAttributeKind.Resistance, 4, EffectChangeOperator.Add, 25)]);
    private static readonly SessionEffectDefinition Curse = new(57, [new(EffectAttributeKind.Resistance, 4, EffectChangeOperator.Add, -50)]);

    private sealed class Map(GameSessionOwner owner, SpellCastCritter critter) : ISpellCastMap
    {
        public SpellTargetObject? Object(string id) => id switch
        {
            Hero => new(id, SpellTargetObject.Pc, HeroTile, HitPoints: owner.QueryHeroLife()!.Status.HitPoints),
            Target => new(id, SpellTargetObject.Npc, TargetTile, HitPoints: 5),
            _ => null,
        };
        public IReadOnlyList<SpellTargetObject> ObjectsInSquare(SpellLocation centre, int radius, int typeMask) => [];
        public IReadOnlyList<SpellTargetObject> ObjectsAt(SpellLocation tile, int typeMask) => [];
        public bool TileBlocked(SpellLocation tile) => false;
        public bool PathExists(SpellTargetObject source, SpellLocation tile) => true;
        public int SectorKind(SpellLocation location) => 0;
        public int Direction(SpellLocation from, SpellLocation to) => 0;
        public IReadOnlyList<SpellTargetObject> Party(SpellTargetObject source) => [];
        public SpellLineOfFire LineOfFire(SpellObjectFacts parent, SpellLocation tile) => new(0, null);
        public SpellCastCritter? Critter(string entity) => entity == Target ? critter : null;
        public CombatWeapon WieldedWeapon(string entity) => CombatWeapon.Unarmed();
        public IReadOnlyList<SpellAcidItem> WornItems(string entity) => [];
        public (Combatant Combatant, AttackTargetKind Kind)? ObjectCombat(string id) => null;
        public bool DamageObject(string id, AttackDamage damage) => false;
    }

    private sealed class Events : ISessionEventWorld
    {
        public LifeFacts? CritterFacts(SessionMapId map, string entity) => null;
    }

    private sealed class NoCombat : ISessionCombatWorld
    {
        public IReadOnlyList<SessionCombatCandidate> ScanCombatants(SessionMapId map) => [];
        public SessionCombatantFacts? CombatantFacts(SessionMapId map, string entity) => null;
    }

    private static SpellCastCritter TargetCritter(int stored, uint critterFlags)
    {
        var fields = AttackStateTests.Npc(stats: AttackStateTests.Stats(willpower: 9), critterFlags: critterFlags);
        return new(SessionCritterCombat.From(Target, fields).Combat!, stored, 0, 0, 0, 0);
    }

    // Any one-component spell will do: the host's resistance question is asked of the run and its target, whatever the spell does.
    private static SessionSpellRecord Plain() =>
        SessionSpellRecords.Translate(Record(number: 94, beginTargets: 0x4, info: SpellInfo.Neutral,
            components: [new(SpellAction.Begin, SpellComponentType.ObjFlag, new(0x4, 0, 0, 0, -1), 0, 0, new ObjectFlagData(SessionSpellCaster.SpellFlagsField, 0x400, true))])
            with { Cost = 10, MaintainCost = 3, MaintainPeriod = 10, Range = 99, DurationSeconds = 0, DurationStat = -1, DurationTriggerCount = 0, ResistStat = -1, NoStack = 0, NoReflect = true,
                DisallowedCasterSpellFlags = 0, DisallowedTargetSpellFlags = 0, DisallowedTargetCritterFlags = 0, CancelsSpellFlags = 0, SpellFlagsSet = 0x400,
                Areas = [new(0x4, 0, 0, 0, -1), new(0, 0, 0, 0, -1), new(0x4, 0, 0, 0, -1), new(0, 0, 0, 0, -1), new(0, 0, 0, 0, -1)] });

    // A run of the spell by the hero at the NPC and the resistance facts the host answers for the NPC and for the hero (ResistTarget of the run host).
    private static (GameSessionOwner Owner, SessionSpellCaster Caster, SpellRun Run, SessionLifeTarget Npc) Session(int stored = 40, uint critterFlags = 0, GameRules? rules = null)
    {
        var record = Plain();
        var owner = HeroStateTests.CreateWith(HeroStateTests.HalfElf());
        var critter = TargetCritter(stored, critterFlags);
        var caster = new SessionSpellCaster(owner, n => n == 94 ? record : null, new Map(owner, critter), new Events());
        var hero = caster.Facts(Hero)!;
        var at = caster.Facts(Target)!;
        var started = owner.StartSpellRun(owner.Version, new(new(94, hero, hero, at, at.Location, ParentIsLocalPlayer: true)), record.Run, caster.InvocationWorld(), owner.SpellRolls);
        Assert.AreEqual(GameSessionDisposition.Changed, started.Disposition, $"{started.Failure} {started.Creation?.Status} {started.Creation?.Check}");
        caster.StartGoal(started.Creation!.Run!.Id, new NoCombat());
        return (owner, caster, owner.QuerySpellRuns()[started.Creation.Run.Id]!, critter.Combat.Target);
    }

    private static int NpcResistance(SessionSpellCaster caster, SpellRun run) => caster.ResistTarget(run, new(caster.RunObject(Target), TargetTile)).StoredMagicResistance;

    private static int HeroResistance(SessionSpellCaster caster, SpellRun run) => caster.ResistTarget(run, new(caster.RunObject(Hero), HeroTile)).StoredMagicResistance;

    [TestMethod]
    public void An_NPC_under_Resist_Magick_saves_with_its_stored_resistance_raised_and_loses_it_with_the_spell()
    {
        var (owner, caster, run, target) = Session(stored: 40);
        using var _ = owner;
        Assert.AreEqual(40, NpcResistance(caster, run), "no effect: the stored value");
        Assert.AreEqual(GameSessionDisposition.Changed, owner.AddNpcEffect(owner.Version, target, ResistMagick, EffectCause.Spell).Disposition);
        Assert.AreEqual(65, NpcResistance(caster, run), "Resist Magick adds 25");
        Assert.AreEqual(GameSessionDisposition.Changed, owner.AddNpcEffect(owner.Version, target, Curse, EffectCause.Spell).Disposition);
        Assert.AreEqual(15, NpcResistance(caster, run), "a curse of −50 joins it");
        Assert.AreEqual(GameSessionDisposition.Changed, owner.RemoveNpcEffect(owner.Version, target, 57).Disposition);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.RemoveNpcEffect(owner.Version, target, 56).Disposition);
        Assert.AreEqual(40, NpcResistance(caster, run), "the spell's END removes it");
    }

    [TestMethod]
    public void An_NPC_with_an_immune_stored_value_keeps_it_under_a_blessing_and_a_monstrous_one_is_not_cut_at_95()
    {
        var (owner, caster, run, target) = Session(stored: 100);
        using var _ = owner;
        Assert.AreEqual(GameSessionDisposition.Changed, owner.AddNpcEffect(owner.Version, target, ResistMagick, EffectCause.Spell).Disposition);
        Assert.AreEqual(100, NpcResistance(caster, run), "the cut at 95 never lowers an immune NPC");

        var (monsterOwner, monsterCaster, monsterRun, monster) = Session(stored: 100, critterFlags: 0x8000);
        using var __ = monsterOwner;
        Assert.AreEqual(GameSessionDisposition.Changed, monsterOwner.AddNpcEffect(monsterOwner.Version, monster, ResistMagick, EffectCause.Spell).Disposition);
        Assert.AreEqual(125, NpcResistance(monsterCaster, monsterRun), "a monstrous NPC keeps its sum");
    }

    [TestMethod]
    public void The_hero_under_Resist_Magick_saves_with_the_effect_over_a_stored_value_of_none()
    {
        var (owner, caster, run, _) = Session();
        using var _ = owner;
        Assert.AreEqual(0, HeroResistance(caster, run));
        Assert.AreEqual(GameSessionDisposition.Changed, owner.AddHeroEffect(owner.Version, ResistMagick, EffectCause.Spell, 4).Disposition);
        Assert.AreEqual(95, HeroResistance(caster, run), "four entries of 25 are cut at 95 for a PC");
        Assert.AreEqual(GameSessionDisposition.Changed, owner.RemoveHeroEffect(owner.Version, 56, 3).Disposition);
        Assert.AreEqual(25, HeroResistance(caster, run));
        Assert.AreEqual(GameSessionDisposition.Changed, owner.AddHeroEffect(owner.Version, Curse, EffectCause.Spell).Disposition);
        Assert.AreEqual(0, HeroResistance(caster, run), "a curse is cut at 0");
        Assert.AreEqual(0, owner.HeroSaveMagicResistance());
    }
}
