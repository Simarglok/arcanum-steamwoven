using Arcanum.Data.Magic;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Magic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.HeroStateTests;
using static Arcanum.Session.Tests.InventoryStateTests;
using static Arcanum.Session.Tests.SessionFixtures;
using static Arcanum.Session.Tests.SessionSpellRecordsTests;
using static Arcanum.Simulation.Magic.ItemSpellTriggerRules;

namespace Arcanum.Session.Tests;

// ARC-WORLD-108 (NPC AI slice I10, part 3e): an NPC's items through the cast host (contract docs/world/ai-host.md "Item use"). The
// seed's living NPC "virgil" is given a wand (made:1, a weapon with 20 charges), a scroll (made:2) and a potion (made:3, food whose
// slot-0 record fires on Item_Used at the other object). Every record's BEGIN deals 2 damage at the run's target, the hero. Records
// and numbers are invented.
[TestClass]
public sealed class NpcItemUseTests
{
    private const string Npc = "virgil", Wand = "made:1", Scroll = "made:2", Potion = "made:3";
    private static readonly SpellLocation HeroTile = new(5, 5), NpcTile = new(7, 5);

    private sealed class Map(GameSessionOwner owner) : ISpellCastMap
    {
        public SpellTargetObject? Object(string id) => id switch
        {
            SessionSpellCaster.Hero => new(id, SpellTargetObject.Pc, HeroTile, HitPoints: owner.QueryHeroLife()!.Status.HitPoints),
            Npc => new(id, SpellTargetObject.Npc, NpcTile),
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
        public SpellCastCritter? Critter(string entity) => null;
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

    private static SessionSpellRecord Use(int number, uint triggers = 0) =>
        SessionSpellRecords.Translate(Record(number: number, beginTargets: 0x4, info: SpellInfo.Friendly,
            components: [new(SpellAction.Begin, SpellComponentType.Damage, new(0x4, 0, 0, 0, -1), 0, triggers, new DamageData(0, 2, 2, 0))])
        with { Areas = [.. Enumerable.Repeat(new SpellArea(0x4, 0, 0, 0, -1), 5)], Cost = 5, MaintainCost = 0, MaintainPeriod = 0, DurationSeconds = 0,
            DurationStat = -1, DurationTriggerCount = 0, ResistStat = -1, NoStack = 0, DisallowedCasterSpellFlags = 0, DisallowedTargetSpellFlags = 0,
            DisallowedTargetCritterFlags = 0, ItemTriggers = triggers });

    private static (GameSessionOwner Owner, SessionSpellCaster Caster, SessionItemTriggers Triggers) Open(RuleMode mode = RuleMode.Steamwoven)
    {
        var records = new[] { Use(160), Use(161, ItemUsed | Other) }.ToDictionary(r => r.Data.Number);
        var owner = CreateWith(HalfElf());
        owner.SelectRules(mode);
        Assert.AreEqual(GameSessionFailure.None, owner.AttachMapInventory(Exterior, Seed()).Failure);
        foreach (var kind in new SessionItemKind[] { new(SessionItemType.Weapon, 9600, 1, 2), new(SessionItemType.Scroll, 9700, 3, 4), new(SessionItemType.Food, 9800, 5, 6) })
            Assert.AreEqual(GameSessionDisposition.Changed, owner.AddItemToNpc(owner.Version, Npc, kind, _ => SessionInsertVerdict.Accepted).Disposition);
        var caster = new SessionSpellCaster(owner, n => records.GetValueOrDefault(n), new Map(owner), new Events(),
            id => id switch
            {
                Wand => new SessionItemSpells(Wand, Npc, 5, [160, 10000, 10000, 10000, 10000], 20, 10),
                Scroll => new SessionItemSpells(Scroll, Npc, SessionItemSpells.ScrollType, [160, 10000, 10000, 10000, 10000], 1, 10),
                Potion => new SessionItemSpells(Potion, Npc, SessionItemSpells.FoodType, [161, 10000, 10000, 10000, 10000], 1, 10),
                _ => null,
            });
        var triggers = new SessionItemTriggers(caster, new NoCombat());
        caster.ItemTriggers = triggers;
        return (owner, caster, triggers);
    }

    private static int HitPoints(GameSessionOwner owner) => owner.QueryHeroLife()!.Status.HitPoints;

    private static string[] Held(GameSessionOwner owner) =>
        owner.QueryMapInventory(Exterior)!.Holders.Single(h => h.Entity == Npc).Items.Select(item => item.Entity).ToArray();

    [TestMethod]
    public void An_npc_s_potion_fires_item_used_at_the_target_and_is_spent_out_of_its_list()
    {
        var (owner, _, triggers) = Open();
        using var _ = owner;
        var hp = HitPoints(owner);
        var use = triggers.UseItem(Potion, SessionSpellCaster.Hero);
        Assert.AreEqual((SessionItemUseOutcome.Used, SessionItemUseCast.Triggered), (use.Outcome, use.Cast), "slot 0's record has item triggers");
        var fired = use.ItemUsed!.Spells.Single();
        Assert.AreEqual((161, SessionItemSpellOutcome.Started), (fired.Firing.Spell, fired.Outcome));
        Assert.AreEqual(Npc, fired.Result!.Creation!.Run!.Parent!.Id, "the NPC, the potion's holder, is the run's parent");
        Assert.AreEqual(hp - 2, HitPoints(owner));
        Assert.IsTrue(use.Spent);
        Assert.IsTrue(owner.IsItemDestroyed(Potion));
        CollectionAssert.DoesNotContain(Held(owner), Potion, "the potion left the NPC's list");
    }

    [TestMethod]
    public void Nu2_an_npc_s_use_of_a_wand_casts_nothing_from_slot_zero_and_spends_no_charge()
    {
        // 0x57CE30 → 0x57BC70: the activation's caster is the item's holder, and 0x40DA20 (type 15) refuses any caster but a PC.
        var (owner, _, triggers) = Open();
        using var _ = owner;
        var hp = HitPoints(owner);
        var use = triggers.UseItem(Wand, SessionSpellCaster.Hero);
        Assert.AreEqual((SessionItemUseOutcome.Used, SessionItemUseCast.Nothing, false), (use.Outcome, use.Cast, use.Spent));
        Assert.IsNull(use.Attempt);
        Assert.AreEqual(0, use.ItemUsed!.Spells.Count, "record 160 has no item triggers");
        Assert.AreEqual(hp, HitPoints(owner));
        Assert.AreEqual(0, owner.QuerySpellRuns().Runs.Count());
        Assert.IsNull(owner.QueryItemCharges(Wand), "no charge was spent");
    }

    [TestMethod]
    public void The_ai_s_cast_from_a_scroll_runs_with_the_scroll_as_source_then_destroys_it_and_clears_the_sources()
    {
        var (owner, caster, _) = Open();
        using var _ = owner;
        var attempt = caster.CastByAi(Npc, 160, SessionSpellCaster.Hero, Scroll);
        Assert.AreEqual(SpellCastAttemptOutcome.Started, attempt.Outcome);
        var run = owner.QuerySpellRuns()[attempt.RunId!.Value]!;
        Assert.AreEqual(Npc, run.Parent!.Id);
        Assert.IsNull(run.Source, "0x4574D0 cleared the scroll as the run's source");
        Assert.IsTrue(owner.IsItemDestroyed(Scroll));
        CollectionAssert.DoesNotContain(Held(owner), Scroll);
    }

    [TestMethod]
    public void Mg47_the_ai_s_cast_from_a_wand_clears_its_source_in_the_original_so_the_wand_pays_no_charge()
    {
        foreach (var mode in new[] { RuleMode.Original, RuleMode.Steamwoven })
        {
            var (owner, caster, _) = Open(mode);
            using var _ = owner;
            var hp = HitPoints(owner);
            var attempt = caster.CastByAi(Npc, 160, SessionSpellCaster.Hero, Wand);
            Assert.AreEqual(SpellCastAttemptOutcome.Started, attempt.Outcome, $"{mode}");
            var run = attempt.RunId!.Value;
            Assert.AreEqual(mode == RuleMode.Original ? null : Wand, owner.QuerySpellRuns()[run]!.Source?.Id, $"{mode}: the run's source");
            Assert.AreEqual(SpellCastGoalOutcome.Started, caster.StartGoal(run, new NoCombat()).Outcome, $"{mode}");
            Assert.IsTrue(caster.Release(run) is { Disposition: GameSessionDisposition.Changed }, $"{mode}: the release began the run");
            Assert.AreEqual(hp - 2, HitPoints(owner), $"{mode}: the spell took effect");
            Assert.AreEqual(mode == RuleMode.Original ? null : 19, owner.QueryItemCharges(Wand), $"{mode}: the wand's charges");
            Assert.IsFalse(owner.IsItemDestroyed(Wand), $"{mode}: a wand is not spent");
        }
    }

    [TestMethod]
    public void The_ai_s_item_cast_needs_an_item_the_caster_holds_and_a_refused_check_spends_nothing()
    {
        var (owner, caster, _) = Open();
        using var _ = owner;
        Assert.AreEqual(SpellCastAttemptOutcome.Nothing, caster.CastByAi(SessionSpellCaster.Hero, 160, Npc, Scroll).Outcome, "the hero does not hold it");
        Assert.AreEqual(SpellCastAttemptOutcome.Nothing, caster.CastByAi(Npc, 160, SessionSpellCaster.Hero, "made:9").Outcome, "an unknown item");
        Assert.AreEqual(SpellCastAttemptOutcome.Nothing, caster.CastByAi(Npc, 999, SessionSpellCaster.Hero, Scroll).Outcome, "no record");
        Assert.IsFalse(owner.IsItemDestroyed(Scroll));
        Assert.AreEqual(0, owner.QuerySpellRuns().Runs.Count());
    }

    [TestMethod]
    public void The_item_answers_of_the_support_world_read_the_item_lookup()
    {
        var (owner, caster, _) = Open();
        using var _ = owner;
        Assert.IsTrue(caster.AiCanUseItem(Npc, Wand), "an NPC without critter facts has no aptitude to refuse");
        Assert.IsFalse(caster.AiCanUseItem(Npc, "made:9"), "an unknown item");
        Assert.AreEqual(0, caster.AiItemFailChance(Npc, Wand, SessionSpellCaster.Hero, 160), "without the magical flag");
        Assert.IsTrue(caster.SpendUsedItem(Potion));
        Assert.IsFalse(caster.SpendUsedItem(Wand), "a weapon is not spent as used");
        Assert.IsFalse(owner.IsItemDestroyed(Wand));
    }
}
