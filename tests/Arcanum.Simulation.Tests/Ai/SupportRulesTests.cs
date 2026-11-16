using System.Collections.Immutable;
using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Combat;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Simulation.Tests.Ai;

/// <summary>
/// ARC-WORLD-088 (AI slice I8): the global lists' sort (0x4CC380, 0x58A548), a critter's list build (0x4CC580) and the cache of the
/// Original setting (SP11), the chooser (0x4ABF10, SP18), the eligibility test (0x4AE720), the heal ladder (0x4A8940, P4, SP17),
/// the spell choice (0x4ABC20, P7), the item search (0x4A8E70, P1), the grenade (0x4AC910, SP15) and the redirect (0x4A9560, P3),
/// over an invented world: critters on one row, invented spells and items.
/// </summary>
[TestClass]
public sealed class SupportRulesTests
{
    private static readonly CombatAnomalyProfile Steamwoven = CombatAnomalyProfile.Steamwoven, Original = CombatAnomalyProfile.Original;

    /// <summary>A synthetic world for every support rule.</summary>
    private sealed class World : IAiSupportWorld, IAiItemSearchWorld, IAiGrenadeWorld, IAiRedirectWorld
    {
        public readonly Dictionary<string, HostilityCritter> Critters = [];
        public readonly Dictionary<string, AiSupportCritter> Facts = [];
        public readonly Dictionary<string, int> X = [];
        public readonly Dictionary<string, HostilityDanger> Dangers = [];
        public readonly Dictionary<string, DangerFlags> Bits = [];
        public readonly Dictionary<string, List<string>> FollowerLists = [];
        public readonly Dictionary<string, List<string>> Inventories = [];
        public readonly Dictionary<string, AiListItem> Items = [];
        public readonly Dictionary<int, AiSpellFacts> SpellFacts = [];
        public readonly HashSet<(string, int)> Known = [], Influenced = [];
        public readonly HashSet<string> Unusable = [];
        public readonly Dictionary<(string, int), int> FailChances = [];
        public readonly Dictionary<(string, string), AiLineOfFire> Lines = [];
        public readonly Dictionary<string, string> Hexed = [];
        public readonly Dictionary<(string, AiActionList), ImmutableArray<AiListEntry>> FixedLists = [];
        public readonly List<string> Asked = [];
        public AiSupportParameters Row = new(DefensiveSkip: 0, HealOthersChance: 100, BackOffDistance: 0);
        public AiGlobalLists Global = AiGlobalLists.Build(Enumerable.Repeat(AiSpellValues.None, AiGlobalLists.Spells).ToArray());
        public int Cost = 1, FreeSlots = 1, College = 1;
        public string? HealItem;
        public bool HealAccepts = true;

        public HostilityCritter Add(string id, HostilityKind kind, int x, Func<HostilityCritter, HostilityCritter>? edit = null)
        {
            var critter = new HostilityCritter { Id = id, Kind = kind, Perception = 8, Intelligence = 10, Level = 1, HealthPercent = 100 };
            Critters[id] = edit?.Invoke(critter) ?? critter;
            Facts[id] = new AiSupportCritter { FatiguePercent = 100, CurrentFatigue = 50, Willpower = 10, MagickPoints = 10 };
            X[id] = x;
            return Critters[id];
        }

        public void Edit(string id, Func<HostilityCritter, HostilityCritter> edit) => Critters[id] = edit(Critters[id]);
        public void EditFacts(string id, Func<AiSupportCritter, AiSupportCritter> edit) => Facts[id] = edit(Facts[id]);

        // Hostility.
        public HostilityCritter? Find(string id) => Critters.GetValueOrDefault(id);
        public int Distance(string from, string to) => Math.Abs(X[from] - X[to]);
        public bool FactionSame(string a, string b) => false;
        public int HostilityReaction(string npc, string pc) => 50;
        public bool AllowsKillOnSight(string npc, string candidate) => true;
        public HostilityParameters Parameters(string npc) => new(5, 20, 2001, 50);
        public AwarenessResult See(string observer, string target) => new(0, AwarenessReason.Perceived);
        public AwarenessResult Hear(string observer, string target, Loudness loudness) => new(0, AwarenessReason.Perceived);
        public HostilityDanger Danger(string npc) => Dangers.GetValueOrDefault(npc, HostilityDanger.None);
        public string? MindController(string npc) => null;
        public IReadOnlyList<string> Square(string npc, int halfSize) => [];

        // Support.
        public AiSupportCritter Support(string id) => Facts[id];
        public IReadOnlyList<string> Followers(string id) => FollowerLists.GetValueOrDefault(id, []);
        public AiSupportParameters SupportParameters(string npc) => Row;
        public bool NpcFighting { get; set; } = true;
        public AiGlobalLists GlobalLists => Global;
        public ImmutableArray<AiListEntry> List(string critter, AiActionList list)
        {
            Asked.Add($"{critter}:{list}");
            return FixedLists.TryGetValue((critter, list), out var fixedList) ? fixedList : AiActionLists.Build(this, critter, list);
        }
        public string? HexedWeapon(string critter) => Hexed.GetValueOrDefault(critter);
        public IReadOnlyList<string> Inventory(string critter) => Inventories.GetValueOrDefault(critter, []);
        public AiListItem Item(string item) => Items[item];
        public bool CanUseItem(string critter, string item) => !Unusable.Contains(item);
        public bool Knows(string critter, int spell) => Known.Contains((critter, spell));
        public AiSpellFacts Spell(int spell) => SpellFacts.GetValueOrDefault(spell);
        public int CollegeLevel(string critter, int college) => College;
        public int CastCost(string critter, int spell) => Cost;
        public int FreeMaintainSlots(string critter) => FreeSlots;
        public bool UnderInfluence(string target, int spell) => Influenced.Contains((target, spell));
        public int FailChance(string critter, string target, int spell) { Asked.Add($"fail:{spell}"); return FailChances.GetValueOrDefault((target, spell)); }
        public int ItemFailChance(string critter, string item, string target, int spell) { Asked.Add($"itemfail:{spell}"); return FailChances.GetValueOrDefault((target, spell)); }
        public AiLineOfFire LineOfFire(string critter, string target) => Lines.GetValueOrDefault((critter, target));
        public string? HealingItem(string npc) => HealItem;
        public bool HealSkillAccepts(string npc, string item, string target) => HealAccepts;

        // Item search and grenade.
        public DangerFlags DangerBits(string npc) => Bits.GetValueOrDefault(npc);
        public void WriteDangerBits(string npc, DangerFlags set, DangerFlags clear)
        {
            Asked.Add($"bits:-{clear}");
            Bits[npc] = (DangerBits(npc) | set) & ~clear;
        }
        public bool Blind;
        public bool Blinded(string npc) => Blind;
        public bool InterfaceHoldsObject { get; set; }
        public readonly Dictionary<AiGroundKind, List<AiGroundItem>> Loose = [];
        public readonly List<string> Piles = [];
        public readonly Dictionary<(string, AiGroundKind), AiGroundItem> PileItems = [];
        public readonly HashSet<string> RefusedPickups = [];
        public readonly List<(AiGroundKind? Kind, int HalfSize)> Queries = [];
        public IReadOnlyList<AiGroundItem> LooseItems(string npc, AiGroundKind kind, int halfSize)
        {
            Queries.Add((kind, halfSize));
            return Loose.GetValueOrDefault(kind, []);
        }
        public IReadOnlyList<string> JunkPiles(string npc, int halfSize) { Queries.Add((null, halfSize)); return Piles; }
        public AiGroundItem? FirstInPile(string pile, AiGroundKind kind) => PileItems.GetValueOrDefault((pile, kind));
        public bool StartPickup(string npc, string item) { Asked.Add($"pickup:{item}"); return !RefusedPickups.Contains(item); }
        public bool Throwing = true;
        public string? GrenadeItem = "grenade";
        public int Radius = 2;
        public string? Blocker;
        public readonly List<string> Blast = [];
        public bool HasThrowingPoints(string npc) => Throwing;
        public string? Grenade(string npc) => GrenadeItem;
        public int BlastRadius(string grenade) => Radius;
        public string? LineOfFireBlocker(string npc, string target) => Blocker;
        public IReadOnlyList<string> SquareAround(string center, int halfSize, bool withPcs) =>
            Blast.Where(id => withPcs || Critters[id].Kind == HostilityKind.Npc).ToList();

        // Redirect.
        public readonly List<string> Near = [];
        public readonly Dictionary<string, uint> Flags = [];
        public IReadOnlyList<string> Vicinity(string target) => Near;
        public uint CritterFlags(string id) => Flags.GetValueOrDefault(id);
    }

    /// <summary>Rolls from a queue, recording each request.</summary>
    private sealed class Rolls(params int[] values) : IAiRollSource
    {
        private readonly Queue<int> _values = new(values);
        public readonly List<(AiRollPurpose Purpose, int Min, int Max)> Asked = [];

        public int Roll(AiRollPurpose purpose, int min, int max)
        {
            Asked.Add((purpose, min, max));
            return _values.Count > 0 ? _values.Dequeue() : min;
        }
    }

    private static AiSpellValues Values(AiActionList list, int value, int second = 0)
    {
        var values = Enumerable.Repeat(-1, AiGlobalLists.Lists).ToArray();
        values[(int)list] = value;
        return new(values, second);
    }

    private static AiGlobalLists Lists(params (int Spell, AiSpellValues Values)[] spells)
    {
        var all = Enumerable.Repeat(AiSpellValues.None, AiGlobalLists.Spells).ToArray();
        foreach (var (spell, values) in spells) all[spell] = values;
        return AiGlobalLists.Build(all);
    }

    private static ImmutableArray<AiListEntry> Entries(params (int Spell, string? Item)[] entries) => [.. entries.Select(e => new AiListEntry(e.Spell, e.Item))];

    // A mage at 0 that knows spells 10..12 of the offensive list, and an orc at 4.
    private static World Duel()
    {
        var w = new World();
        w.Add("mage", HostilityKind.Npc, 0);
        w.Add("orc", HostilityKind.Npc, 4);
        w.Global = Lists((10, Values(AiActionList.Offensive, 30)), (11, Values(AiActionList.Offensive, 50)), (12, Values(AiActionList.Offensive, 40)));
        foreach (var spell in new[] { 10, 11, 12 }) w.Known.Add(("mage", spell));
        return w;
    }

    // ---- sort and lists ----

    [TestMethod]
    public void QuicksortIsTheRuntimesAlgorithm()
    {
        // Short parts: the first maximum moves to the end, so equal values leave in the selection sort's order.
        int[] Sort(int[] values)
        {
            var spells = Enumerable.Range(0, values.Length).ToArray();
            AiActionLists.Quicksort(spells, (a, b) => AiActionLists.CompareValues(values[a], values[b]));
            return spells;
        }
        CollectionAssert.AreEqual(new[] { 1, 0 }, Sort([5, 5]));
        CollectionAssert.AreEqual(new[] { 1, 2, 0 }, Sort([5, 5, 5]));
        CollectionAssert.AreEqual(new[] { 2, 1, 0 }, Sort([1, 2, 3]), "highest first");
        // A long part partitions around its middle element; the values come out sorted whatever the ties.
        var values = Enumerable.Range(0, 40).Select(i => i % 7).ToArray();
        var sorted = Sort(values);
        CollectionAssert.AreEquivalent(Enumerable.Range(0, 40).ToArray(), sorted);
        CollectionAssert.AreEqual(values.OrderByDescending(v => v).ToArray(), sorted.Select(s => values[s]).ToArray());
        Assert.AreEqual((1, 0, -1), (AiActionLists.CompareValues(1, 2), AiActionLists.CompareValues(2, 2), AiActionLists.CompareValues(3, 2)));
    }

    [TestMethod]
    public void GlobalListsHoldValuedSpellsHighestFirst()
    {
        var lists = Lists((3, Values(AiActionList.Summon, 10)), (7, Values(AiActionList.Summon, 90)), (9, Values(AiActionList.Defensive, 20, second: 1)));
        CollectionAssert.AreEqual(new[] { 7, 3 }, lists[AiActionList.Summon].ToArray());
        CollectionAssert.AreEqual(new[] { 9 }, lists[AiActionList.Defensive].ToArray());
        Assert.AreEqual(0, lists[AiActionList.Flee].Length);
        Assert.AreEqual((90, -1, 1, 0, -1), (lists.Value(7, AiActionList.Summon), lists.Value(7, AiActionList.Flee), lists.DefensiveSecond(9),
            lists.DefensiveSecond(-1), lists.Value(300, AiActionList.Summon)));
        Assert.ThrowsExactly<ArgumentException>(() => AiGlobalLists.Build(new AiSpellValues[3]));
    }

    [TestMethod]
    public void ListBuildTakesSpellsOrItemsByFatigueAndCombat()
    {
        var w = Duel();
        w.Items["wand"] = new AiListItem { ManaStore = 5, Identified = true, Spells = [12, -1, 11, -1, -1] };
        w.Items["salve"] = new AiListItem { ValidAiAction = true, AiAction = (int)AiActionList.Offensive };
        w.Inventories["mage"] = ["wand", "salve"];
        // Out of combat: spells in the global order; items only when no spell results.
        CollectionAssert.AreEqual(Entries((11, null), (12, null), (10, null)).ToArray(), AiActionLists.Build(w, "mage", AiActionList.Offensive).ToArray());
        // In combat mode at or below 50 % fatigue: the items in inventory order, slot order, then the AI-action entry.
        w.Edit("mage", c => c with { InCombatMode = true });
        w.EditFacts("mage", f => f with { FatiguePercent = 50 });
        CollectionAssert.AreEqual(Entries((12, "wand"), (11, "wand"), (-1, "salve")).ToArray(), AiActionLists.Build(w, "mage", AiActionList.Offensive).ToArray());
        // No magick points: no spells; an unusable or unidentified item of a PC follower is skipped (its AI action stays).
        w.Edit("mage", c => c with { InCombatMode = false, PcLeader = "pc" });
        w.EditFacts("mage", f => f with { MagickPoints = 0 });
        w.Items["wand"] = w.Items["wand"] with { Identified = false };
        CollectionAssert.AreEqual(Entries((-1, "salve")).ToArray(), AiActionLists.Build(w, "mage", AiActionList.Offensive).ToArray());
        w.Items["wand"] = w.Items["wand"] with { IdentificationExempt = true };
        w.Unusable.Add("wand");
        CollectionAssert.AreEqual(Entries((-1, "salve")).ToArray(), AiActionLists.Build(w, "mage", AiActionList.Offensive).ToArray());
        // A hexed wielded weapon is the only item looked at.
        w.Unusable.Clear();
        w.Hexed["mage"] = "wand";
        CollectionAssert.AreEqual(Entries((12, "wand"), (11, "wand")).ToArray(), AiActionLists.Build(w, "mage", AiActionList.Offensive).ToArray());
        // Not a critter: nothing.
        w.Add("door", HostilityKind.Object, 1);
        Assert.AreEqual(0, AiActionLists.Build(w, "door", AiActionList.Offensive).Length);
    }

    [TestMethod]
    public void ListBuildStopsAtTwoHundredEntries()
    {
        var w = Duel();
        for (var i = 0; i < 210; i++)
        {
            w.Items[$"i{i}"] = new AiListItem { ValidAiAction = true, AiAction = (int)AiActionList.Offensive };
            w.Inventories.TryAdd("mage", []);
            w.Inventories["mage"].Add($"i{i}");
        }
        w.EditFacts("mage", f => f with { MagickPoints = 0 });
        var list = AiActionLists.Build(w, "mage", AiActionList.Offensive);
        Assert.AreEqual((AiActionLists.MaxEntries, "i199"), (list.Length, list[^1].Item));
    }

    [TestMethod]
    public void OriginalCacheKeepsTheFirstListAndSteamwovenBuildsFresh()
    {
        var w = Duel();
        w.Global = Lists((10, Values(AiActionList.Offensive, 30)), (20, Values(AiActionList.Defensive, 10)));
        w.Known.Add(("mage", 20));
        var cache = AiListCache.Empty;
        var first = cache.Request(w, "mage", AiActionList.Offensive, Original);
        CollectionAssert.AreEqual(Entries((10, null)).ToArray(), first.Entries.ToArray());
        // Learning another spell leaves the frozen list as it was; another list is built fresh each time and never stored.
        w.Global = Lists((10, Values(AiActionList.Offensive, 30)), (11, Values(AiActionList.Offensive, 50)), (20, Values(AiActionList.Defensive, 10)));
        var again = first.Cache.Request(w, "mage", AiActionList.Offensive, Original);
        CollectionAssert.AreEqual(Entries((10, null)).ToArray(), again.Entries.ToArray(), "frozen");
        Assert.AreSame(first.Cache, again.Cache);
        var other = again.Cache.Request(w, "mage", AiActionList.Defensive, Original);
        CollectionAssert.AreEqual(Entries((20, null)).ToArray(), other.Entries.ToArray());
        Assert.AreEqual(0, other.Cache.Table.Single(s => s is not null)!.Lists[(int)AiActionList.Defensive].Length, "built for the caller only");
        // Steamwoven builds fresh and leaves the cache alone.
        var fresh = other.Cache.Request(w, "mage", AiActionList.Offensive, Steamwoven);
        CollectionAssert.AreEqual(Entries((11, null), (10, null)).ToArray(), fresh.Entries.ToArray());
        Assert.AreSame(other.Cache, fresh.Cache);
        // An inventory change clears the slot.
        var cleared = other.Cache.Clear("mage");
        CollectionAssert.AreEqual(Entries((11, null), (10, null)).ToArray(), cleared.Request(w, "mage", AiActionList.Offensive, Original).Entries.ToArray());
        Assert.AreSame(cleared, cleared.Clear("nobody"));
    }

    [TestMethod]
    public void CacheTakesFreeSlotsThenReusesRoundRobinAndDropsExhaustedItems()
    {
        var w = Duel();
        w.Items["wand"] = new AiListItem { ManaStore = 5, Identified = true, Spells = [10, -1, -1, -1, -1] };
        var cache = AiListCache.Empty;
        for (var i = 0; i < AiListCache.Slots; i++)
        {
            w.Add($"n{i}", HostilityKind.Npc, 1);
            cache = cache.Request(w, $"n{i}", AiActionList.Offensive, Original).Cache;
        }
        Assert.AreEqual((0, $"n{AiListCache.Slots - 1}"), (cache.NextReuse, cache.Table[^1]!.Critter));
        cache = cache.Request(w, "mage", AiActionList.Offensive, Original).Cache;
        Assert.AreEqual(("mage", 1), (cache.Table[0]!.Critter, cache.NextReuse), "slot 0 reused");
        cache = cache.Request(w, "orc", AiActionList.Offensive, Original).Cache;
        Assert.AreEqual(("orc", 2), (cache.Table[1]!.Critter, cache.NextReuse));
        // An exhausted item drops the slot only when one of its lists holds it.
        w.Inventories["mage"] = ["wand"];
        w.Edit("mage", c => c with { InCombatMode = true });
        w.EditFacts("mage", f => f with { FatiguePercent = 10 });
        var held = cache.Clear("mage").Request(w, "mage", AiActionList.Offensive, Original);
        Assert.AreSame(held.Cache, held.Cache.ItemExhausted("mage", "other"));
        Assert.IsNull(held.Cache.ItemExhausted("mage", "wand").Table.FirstOrDefault(s => s?.Critter == "mage"));
        Assert.AreSame(AiListCache.Empty, AiListCache.Empty.ItemExhausted("mage", "wand"));
    }

    // ARC-AI-2402 (BC10, SP11): a worn store (field 92) a payment drained to 0 calls 0x4CCC40, so the Original setting's frozen list is rebuilt from the
    // current state at the next request; without the call the frozen list stays as it was. (The list build reads the item's charges, field 105, not the
    // worn store: the call only forces the rebuild.) Steamwoven builds fresh either way.
    [TestMethod]
    public void AnEmptiedStoreDropsTheFrozenListOnlyThroughTheExhaustedCall()
    {
        var w = Duel();
        w.Items["wand"] = new AiListItem { ManaStore = 5, Identified = true, Spells = [10, -1, -1, -1, -1] };
        w.Inventories["mage"] = ["wand"];
        w.Edit("mage", c => c with { InCombatMode = true });
        w.EditFacts("mage", f => f with { FatiguePercent = 10 });
        var frozen = AiListCache.Empty.Request(w, "mage", AiActionList.Offensive, Original);
        Assert.IsTrue(frozen.Entries.Any(e => e.Item == "wand"), "the wand is on the first list");

        // The wand's listing changes after the list froze (here: its spell is gone).
        w.Items["wand"] = w.Items["wand"] with { Spells = [-1, -1, -1, -1, -1] };
        Assert.IsTrue(frozen.Cache.Request(w, "mage", AiActionList.Offensive, Original).Entries.Any(e => e.Item == "wand"),
            "no call: the Original setting's slot keeps the old list (SP11)");
        Assert.IsTrue(frozen.Cache.ItemExhausted("mage", "other").Request(w, "mage", AiActionList.Offensive, Original).Entries.Any(e => e.Item == "wand"),
            "a call for an item no list holds drops nothing");
        Assert.IsFalse(frozen.Cache.ItemExhausted("mage", "wand").Request(w, "mage", AiActionList.Offensive, Original).Entries.Any(e => e.Item == "wand"),
            "the call dropped the slot: the rebuilt list follows the current state");
        Assert.IsFalse(frozen.Cache.Request(w, "mage", AiActionList.Offensive, Steamwoven).Entries.Any(e => e.Item == "wand"), "Steamwoven builds fresh");
    }

    [TestMethod]
    public void UsesItemFollowsTheManaStoreAndTheFirstSpell()
    {
        AiSpellFacts Facts(int spell) => new(ItemTriggers: spell == 5);
        Assert.IsFalse(AiActionLists.UsesItem(new AiListItem { ManaStore = 0, ValidAiAction = true }, Facts), "no mana store");
        Assert.IsTrue(AiActionLists.UsesItem(new AiListItem { ManaStore = 1 }, Facts), "first slot empty");
        Assert.IsTrue(AiActionLists.UsesItem(new AiListItem { ManaStore = 1, Spells = [5, -1, -1, -1, -1] }, Facts), "item triggers");
        Assert.IsFalse(AiActionLists.UsesItem(new AiListItem { ManaStore = 1, Spells = [6, -1, -1, -1, -1] }, Facts));
        Assert.IsTrue(AiActionLists.UsesItem(new AiListItem { ManaStore = 1, Spells = [6, -1, -1, -1, -1], ValidAiAction = true }, Facts));
    }

    // ---- the chooser ----

    [TestMethod]
    public void ChooserStartsAtTheTopOrAtRandomAndWalksCircularly()
    {
        var w = Duel();
        var list = Entries((11, null), (12, null), (10, null));
        var top = new Rolls(79);
        var chosen = AiSupportRules.Choose(w, "mage", "orc", AiChooserMode.PassTarget, list, null, top);
        Assert.AreEqual((AiChooserOutcome.Chosen, 11, "orc", AiPassAction.Cast), (chosen.Outcome, chosen.Choice!.Spell, chosen.Choice.Target, chosen.Choice.Action));
        Assert.AreEqual(1, top.Asked.Count);
        // 80 or more: a random start; the walk wraps from the last entry.
        w.Influenced.Add(("orc", 10));
        var random = new Rolls(80, 2);
        var wrapped = AiSupportRules.Choose(w, "mage", "orc", AiChooserMode.PassTarget, list, null, random);
        Assert.AreEqual(11, wrapped.Choice!.Spell);
        CollectionAssert.AreEqual(new[] { 2, 0 }, wrapped.Checks.Select(c => c.Index).ToArray());
        Assert.IsTrue(wrapped.Checks[0].UnderInfluence);
        Assert.AreEqual((AiRollPurpose.ChooserEntry, 0, 2), random.Asked[1]);
        // Nothing passes; an empty list draws nothing.
        w.Influenced.UnionWith([("orc", 11), ("orc", 12)]);
        Assert.AreEqual(AiChooserOutcome.NoneAccepted, AiSupportRules.Choose(w, "mage", "orc", AiChooserMode.PassTarget, list, null, new Rolls()).Outcome);
        var none = new Rolls();
        Assert.AreEqual(AiChooserOutcome.Empty, AiSupportRules.Choose(w, "mage", "orc", AiChooserMode.PassTarget, [], null, none).Outcome);
        Assert.AreEqual(0, none.Asked.Count);
    }

    [TestMethod]
    public void ChooserAimsByModeAndUsesAiActionItemsAtOnce()
    {
        var w = Duel();
        w.Global = Lists((10, Values(AiActionList.Defensive, 30, second: 1)), (11, Values(AiActionList.Defensive, 20, second: 0)));
        Assert.AreEqual("mage", AiSupportRules.Choose(w, "mage", "orc", AiChooserMode.Self, Entries((10, null)), null, new Rolls(1)).Target);
        Assert.AreEqual("orc", AiSupportRules.Choose(w, "mage", "orc", AiChooserMode.PerSpell, Entries((10, null)), null, new Rolls(1)).Target);
        Assert.AreEqual("mage", AiSupportRules.Choose(w, "mage", "orc", AiChooserMode.PerSpell, Entries((11, null)), null, new Rolls(1)).Target);
        Assert.AreEqual("ally", AiSupportRules.Choose(w, "mage", "orc", AiChooserMode.Requested, Entries((10, null)), "ally", new Rolls(1)).Target);
        // An item AI-action entry is used at once, unchecked; in mode 4 on the NPC itself (SP18, both settings).
        w.Influenced.Add(("orc", 10));
        var item = AiSupportRules.Choose(w, "mage", "orc", AiChooserMode.PerSpell, Entries((-1, "potion"), (10, null)), null, new Rolls(1));
        Assert.AreEqual((AiChooserOutcome.ItemUsedAtOnce, "mage", "potion"), (item.Outcome, item.Target, item.Entry!.Value.Item));
        Assert.IsNull(item.Choice, "the pass record is not written");
        Assert.IsTrue(item.Found);
        Assert.AreEqual("orc", AiSupportRules.Choose(w, "mage", "orc", AiChooserMode.PassTarget, Entries((-1, "potion")), null, new Rolls(1)).Target);
        // An item spell is a use-item action when the item passes the item-use test, else a cast with the item.
        w.Items["wand"] = new AiListItem { ManaStore = 3, Spells = [11, -1, -1, -1, -1] };
        w.Items["rod"] = new AiListItem { ManaStore = 3, Spells = [-1, 11, -1, -1, -1] };
        var cast = AiSupportRules.Choose(w, "mage", "orc", AiChooserMode.PassTarget, Entries((11, "wand")), null, new Rolls(1)).Choice!;
        Assert.AreEqual((AiPassAction.Cast, "wand"), (cast.Action, cast.Item));
        Assert.AreEqual(AiPassAction.UseItem, AiSupportRules.Choose(w, "mage", "orc", AiChooserMode.PassTarget, Entries((11, "rod")), null, new Rolls(1)).Choice!.Action);
        // The flee step's choice: the flee list on the NPC itself.
        w.FixedLists[("mage", AiActionList.Flee)] = Entries((10, null));
        var flee = AiSupportRules.ChooseFleeAction(w, "mage", new Rolls(1));
        Assert.AreEqual((AiChooserOutcome.Chosen, "mage"), (flee.Outcome, flee.Target));
    }

    // ---- eligibility ----

    [TestMethod]
    public void EligibilityRefusesInTheOriginalsOrder()
    {
        var w = Duel();
        AiCastRefusal Check(int spell = 10, string? item = null, string? target = "orc") => AiSupportRules.Eligibility(w, "mage", item, target, spell);
        Assert.AreEqual(AiCastRefusal.Unknown, Check(30));
        Assert.AreEqual(AiCastRefusal.Accepted, Check());
        w.EditFacts("mage", f => f with { FatiguePercent = 19 });
        Assert.AreEqual(AiCastRefusal.Tired, Check(), "an NPC without a PC leader below 20 %");
        w.Edit("mage", c => c with { PcLeader = "pc" });
        Assert.AreEqual(AiCastRefusal.Accepted, Check(), "a PC follower may cast tired");
        w.SpellFacts[10] = new(Maintained: true, MinimumIntelligence: 11, MinimumWillpower: 11);
        w.FreeSlots = 0;
        Assert.AreEqual(AiCastRefusal.Maintenance, Check());
        w.FreeSlots = -1;
        w.Cost = 50;
        Assert.AreEqual(AiCastRefusal.Tired, Check(), "a negative free count passes; the cost reaches the current fatigue");
        w.Cost = 49;
        Assert.AreEqual(AiCastRefusal.Intelligence, Check());
        w.Edit("mage", c => c with { Intelligence = 11 });
        Assert.AreEqual(AiCastRefusal.Willpower, Check());
        w.EditFacts("mage", f => f with { Willpower = 11, CastHighest = true });
        w.College = 2;
        Assert.AreEqual(AiCastRefusal.NotHighest, Check(), "college 2 level 2 is spell 11");
        Assert.AreEqual(AiCastRefusal.Accepted, Check(11));
        w.College = 1;
        Assert.AreEqual(AiCastRefusal.Accepted, Check(target: null));
        w.FailChances[("orc", 10)] = 38;
        Assert.AreEqual(AiCastRefusal.FailChance, Check());
        w.FailChances[("orc", 10)] = 37;
        w.Lines[("mage", "orc")] = new(100, null);
        Assert.AreEqual(AiCastRefusal.LineOfFire, Check());
        w.Lines[("mage", "orc")] = new(99, "orc");
        Assert.AreEqual(AiCastRefusal.Accepted, Check(), "the target itself may stop the line");
        w.Lines[("mage", "orc")] = new(0, "tree");
        Assert.AreEqual(AiCastRefusal.LineOfFire, Check());
        w.Lines[("mage", "mage")] = new(500, "tree");
        Assert.AreEqual(AiCastRefusal.Accepted, Check(target: "mage"), "no line to oneself");
    }

    [TestMethod]
    public void EligibilityOfItemSpellsAndTheHealingCollege()
    {
        var w = Duel();
        w.Items["wand"] = new AiListItem { ManaStore = 0, Spells = [61, -1, -1, -1, -1] };
        w.EditFacts("mage", f => f with { FatiguePercent = 50 });
        AiCastRefusal Check(int spell) => AiSupportRules.Eligibility(w, "mage", "wand", "orc", spell);
        Assert.AreEqual(AiCastRefusal.Tired, Check(61), "an empty mana store; the item skips knowing the spell");
        w.Items["wand"] = w.Items["wand"] with { ManaStore = 2 };
        w.FailChances[("orc", 61)] = 50;
        w.FailChances[("orc", 30)] = 50;
        Assert.AreEqual(AiCastRefusal.FailChance, Check(61));
        Assert.AreEqual(AiCastRefusal.FailChance, Check(30));
        // SP8: a healing-college item spell skips the predictor for a user at 80 % fatigue or more, a cast spell only for a hurt target.
        w.EditFacts("mage", f => f with { FatiguePercent = 80 });
        w.Asked.Clear();
        Assert.AreEqual(AiCastRefusal.Accepted, Check(61));
        Assert.AreEqual(AiCastRefusal.Accepted, AiSupportRules.Eligibility(w, "mage", "wand", "chest", 61), "the fatigue exemption needs no critter target");
        Assert.AreEqual(AiCastRefusal.FailChance, Check(30), "not the healing college");
        Assert.IsFalse(w.Asked.Contains("itemfail:61"));
        w.Known.Add(("mage", 61));
        Assert.AreEqual(AiCastRefusal.FailChance, AiSupportRules.Eligibility(w, "mage", null, "orc", 61), "a cast spell has no fatigue exemption");
        w.Edit("orc", c => c with { HealthPercent = 30 });
        Assert.AreEqual(AiCastRefusal.Accepted, AiSupportRules.Eligibility(w, "mage", null, "orc", 61));
        w.FreeSlots = 0;
        w.SpellFacts[61] = new(Maintained: true);
        Assert.AreEqual(AiCastRefusal.Maintenance, Check(61));
    }

    // ---- heal ----

    // A healer at 0 with a leader at 1 and a follower at 2; heavy healing spell 61 known.
    private static World Party()
    {
        var w = new World();
        w.Add("healer", HostilityKind.Npc, 0, c => c with { Leader = "chief" });
        w.Add("chief", HostilityKind.Npc, 1);
        w.Add("squire", HostilityKind.Npc, 2, c => c with { Leader = "chief" });
        w.FollowerLists["chief"] = ["healer", "squire"];
        w.Global = Lists((61, Values(AiActionList.HealingHeavy, 50)), (62, Values(AiActionList.FatigueRecover, 50)), (63, Values(AiActionList.Resurrect, 50)),
            (64, Values(AiActionList.HealingLight, 50)));
        foreach (var spell in new[] { 61, 62, 63, 64 }) w.Known.Add(("healer", spell));
        w.Dangers["healer"] = new(DangerState.CombatFocus, "orc", "orc", null, []);
        return w;
    }

    [TestMethod]
    public void HealTriesSelfLeaderThenFollowers()
    {
        var w = Party();
        w.Edit("squire", c => c with { HealthPercent = 20 });
        var heal = AiSupportRules.Heal(w, "healer", new Rolls(1, 1), Steamwoven);
        Assert.IsTrue(heal.Chose);
        CollectionAssert.AreEqual(new[] { "healer", "chief", "squire" }, heal.Attempts.Select(a => a.Target).ToArray());
        Assert.AreEqual((AiHealStep.HealingHeavy, "squire", 61, AiPassAction.Cast), (heal.Attempts[^1].Step, heal.Choice!.Target, heal.Choice.Spell, heal.Choice.Action));
        // Asleep or fleeing: nobody.
        w.Edit("healer", c => c with { Asleep = true });
        Assert.AreEqual(AiHealStop.Asleep, AiSupportRules.Heal(w, "healer", new Rolls(), Steamwoven).Stop);
        w.Edit("healer", c => c with { Asleep = false });
        w.Dangers["healer"] = new(DangerState.Flee, "orc", null, null, []);
        Assert.AreEqual(AiHealStop.Fleeing, AiSupportRules.Heal(w, "healer", new Rolls(), Steamwoven).Stop);
    }

    [TestMethod]
    public void HealLadderRungsMechanicalDeadAndTheCombatArgument()
    {
        var w = Party();
        w.Edit("healer", c => c with { InCombatMode = true });
        w.Row = w.Row with { HealOthersChance = 20 };
        w.Edit("chief", c => c with { HealthPercent = 60 });
        // d100 = 21 > 20: targets above 30 % are left alone.
        var rolls = new Rolls(21);
        var calm = AiSupportRules.Heal(w, "healer", rolls, Steamwoven);
        Assert.IsFalse(calm.Chose);
        Assert.IsTrue(calm.Attempts.All(a => a.Step == AiHealStep.NotHurtEnough));
        Assert.AreEqual((AiRollPurpose.HealCareRoll, 1, 100), rolls.Asked[0]);
        // d100 = 20: the chief at 60 % gets light healing (below 70 in danger).
        var cared = AiSupportRules.Heal(w, "healer", new Rolls(20, 1), Steamwoven);
        Assert.AreEqual((AiHealStep.HealingLight, "chief"), (cared.Attempts[^1].Step, cared.Choice!.Target));
        // A mechanical leader is never healed (P4); a dead one gets the resurrect list.
        w.Facts["chief"] = w.Facts["chief"] with { Mechanical = true };
        Assert.AreEqual(AiHealStep.Mechanical, AiSupportRules.Heal(w, "healer", new Rolls(20), Steamwoven).Attempts[1].Step);
        w.Facts["chief"] = w.Facts["chief"] with { Mechanical = false };
        w.Edit("chief", c => c with { Dead = true });
        var raise = AiSupportRules.Heal(w, "healer", new Rolls(21, 1), Steamwoven);
        Assert.AreEqual((AiHealStep.Resurrect, 63), (raise.Attempts[^1].Step, raise.Choice!.Spell), "the argument does not filter resurrection");
    }

    [TestMethod]
    public void HealSkillOutOfDangerAndTheFatigueRungSp17()
    {
        var w = Party();
        w.Dangers["healer"] = HostilityDanger.None;
        w.Edit("healer", c => c with { HealthPercent = 85 });
        w.HealItem = "bandage";
        var skill = AiSupportRules.Heal(w, "healer", new Rolls(), Steamwoven);
        Assert.AreEqual(new AiPassChoice(AiPassAction.UseSkill, -1, AiPassChoice.HealSkill, "bandage", "healer"), skill.Choice);
        w.HealAccepts = false;
        Assert.AreEqual(AiHealStep.HealingLight, AiSupportRules.Heal(w, "healer", new Rolls(1), Steamwoven).Attempts[^1].Step, "below 90 out of danger");
        // In danger with the healer below 20 % fatigue: the fatigue rung for itself; for the chief only in the Original setting.
        w.Dangers["healer"] = new(DangerState.CombatFocus, "orc", "orc", null, []);
        w.Edit("healer", c => c with { HealthPercent = 100 });
        w.EditFacts("healer", f => f with { FatiguePercent = 10 });
        w.Edit("chief", c => c with { PcLeader = null });
        w.Edit("healer", c => c with { PcLeader = "pc", Leader = null });
        w.Add("pc", HostilityKind.Pc, 3);
        w.FollowerLists["pc"] = ["healer"];
        var self = AiSupportRules.Heal(w, "healer", new Rolls(1), Steamwoven);
        Assert.AreEqual((AiHealStep.FatigueRecover, "healer"), (self.Attempts[0].Step, self.Choice!.Target));
        w.Influenced.Add(("healer", 62));
        Assert.IsFalse(AiSupportRules.Heal(w, "healer", new Rolls(1, 1), Steamwoven).Chose, "Steamwoven: not for the PC leader");
        var other = AiSupportRules.Heal(w, "healer", new Rolls(1, 1), Original);
        Assert.AreEqual((AiHealStep.FatigueRecover, "pc"), (other.Attempts[^1].Step, other.Choice!.Target));
    }

    // ---- spell choice ----

    [TestMethod]
    public void SpellChoiceChanceAndStronger()
    {
        var w = Duel();
        Assert.AreEqual(25, AiSupportRules.Chance(w, "mage", "orc"));
        w.Row = w.Row with { BackOffDistance = 2 };
        Assert.AreEqual(50, AiSupportRules.Chance(w, "mage", "orc"));
        w.Add("lord", HostilityKind.Npc, 1, c => c with { HealthPercent = 51 });
        w.Edit("mage", c => c with { Leader = "lord", HealthPercent = 51, Level = 10 });
        Assert.AreEqual(0, AiSupportRules.Chance(w, "mage", "orc"), "calm under a healthy leader");
        w.Edit("orc", c => c with { Level = 19 });
        Assert.AreEqual(0, AiSupportRules.Chance(w, "mage", "orc"), "level 19 is not 10 + 10");
        w.Edit("orc", c => c with { Level = 20 });
        Assert.AreEqual(50, AiSupportRules.Chance(w, "mage", "orc"), "level 20 is at least 10 + 5: a stronger target");
        Assert.IsTrue(AiSupportRules.Stronger(20, 15));
        Assert.IsFalse(AiSupportRules.Stronger(19, 10));
        Assert.IsTrue(AiSupportRules.Stronger(20, 10));
        Assert.IsTrue(AiSupportRules.Stronger(40, 40));
        Assert.IsFalse(AiSupportRules.Stronger(39, 35));
    }

    [TestMethod]
    public void SpellChoiceTriesSummonDefensiveThenOffensive()
    {
        var w = Duel();
        w.Global = Lists((10, Values(AiActionList.Offensive, 30)), (20, Values(AiActionList.Summon, 30)), (30, Values(AiActionList.Defensive, 30, second: 0)));
        w.Known.UnionWith([("mage", 20), ("mage", 30)]);
        // No followers: the summon list on itself.
        var summon = AiSupportRules.ChooseSpell(w, "mage", "orc", new Rolls(25, 1));
        Assert.AreEqual((AiSpellChoiceStop.Summon, 20, "mage"), (summon.Stop, summon.Choice!.Spell, summon.Choice.Target));
        // With followers: the defensive list when parameter 13 < d100 (P7), else the offensive one.
        w.FollowerLists["mage"] = ["imp"];
        w.Row = w.Row with { DefensiveSkip = 50 };
        Assert.AreEqual(AiSpellChoiceStop.Defensive, AiSupportRules.ChooseSpell(w, "mage", "orc", new Rolls(1, 51, 1)).Stop);
        var rolls = new Rolls(1, 50, 1);
        var offensive = AiSupportRules.ChooseSpell(w, "mage", "orc", rolls);
        Assert.AreEqual((AiSpellChoiceStop.Offensive, "orc"), (offensive.Stop, offensive.Choice!.Target));
        CollectionAssert.AreEqual(new[] { AiRollPurpose.SpellChoiceRoll, AiRollPurpose.DefensiveListRoll, AiRollPurpose.ChooserStart },
            rolls.Asked.Select(a => a.Purpose).ToArray());
        // Above the chance, invulnerable, or the switch off: nothing.
        Assert.AreEqual(AiSpellChoiceStop.NoRoll, AiSupportRules.ChooseSpell(w, "mage", "orc", new Rolls(26)).Stop);
        w.NpcFighting = false;
        var off = new Rolls();
        Assert.AreEqual(AiSpellChoiceStop.FightingOff, AiSupportRules.ChooseSpell(w, "mage", "orc", off).Stop);
        Assert.AreEqual(0, off.Asked.Count);
        w.NpcFighting = true;
        w.Edit("mage", c => c with { NighInvulnerable = true });
        Assert.AreEqual(AiSpellChoiceStop.Invulnerable, AiSupportRules.ChooseSpell(w, "mage", "orc", new Rolls()).Stop);
        w.Edit("mage", c => c with { NighInvulnerable = false });
        w.Influenced.Add(("orc", 10));
        Assert.AreEqual(AiSpellChoiceStop.NothingFound, AiSupportRules.ChooseSpell(w, "mage", "orc", new Rolls(1, 1, 1)).Stop);
    }

    // ---- item search ----

    [TestMethod]
    public void ItemSearchClearsEachBitAndPicksTheNearest()
    {
        var w = Duel();
        w.Bits["mage"] = DangerFlags.LookForAmmo | DangerFlags.LookForArmor | DangerFlags.Fighting;
        w.Loose[AiGroundKind.Ammunition] = [new("far", 5), new("hidden", 1, NoDisplay: true), new("near", 2), new("tie", 2), new("cheap", 1, Worthless: true)];
        var found = AiGroundRules.SearchItems(w, "mage");
        Assert.AreEqual((AiItemSearchStop.PickupStarted, "near", DangerFlags.LookForAmmo), (found.Stop, found.Item, found.Cleared));
        Assert.AreEqual(DangerFlags.LookForArmor | DangerFlags.Fighting, w.Bits["mage"], "later bits stay");
        Assert.AreEqual((AiGroundKind.Ammunition, 3), (w.Queries[0].Kind!.Value, w.Queries[0].HalfSize), "clamp(8 − 5, 1, 10)");
        // The armour search finds nothing; the bit is cleared all the same.
        var none = AiGroundRules.SearchItems(w, "mage");
        Assert.AreEqual((AiItemSearchStop.NothingStarted, DangerFlags.LookForArmor), (none.Stop, none.Cleared));
        Assert.AreEqual(DangerFlags.Fighting, w.Bits["mage"]);
        // A refused pickup falls back to the junk piles; an off pile item is skipped.
        w.Bits["mage"] = DangerFlags.LookForAmmo;
        w.RefusedPickups.Add("near");
        w.Piles.AddRange(["pile1", "pile2"]);
        w.PileItems[("pile1", AiGroundKind.Ammunition)] = new("lost", 0, Off: true);
        w.PileItems[("pile2", AiGroundKind.Ammunition)] = new("arrows", 0);
        var pile = AiGroundRules.SearchItems(w, "mage");
        Assert.AreEqual(("arrows", "pile2"), (pile.Item, pile.Pile));
        // Monstrous critters clear the bit and search nothing; a sleeping, blinded or busy one does not even clear it.
        w.Bits["mage"] = DangerFlags.LookForWeapon;
        w.Edit("mage", c => c with { Monstrous = true });
        var monster = AiGroundRules.SearchItems(w, "mage");
        Assert.AreEqual((AiItemSearchStop.NothingStarted, DangerFlags.LookForWeapon, DangerFlags.None), (monster.Stop, monster.Cleared, w.Bits["mage"]));
        w.Edit("mage", c => c with { Monstrous = false });
        w.Bits["mage"] = DangerFlags.LookForWeapon;
        w.Blind = true;
        Assert.AreEqual((AiItemSearchStop.Blinded, DangerFlags.LookForWeapon), (AiGroundRules.SearchItems(w, "mage").Stop, w.Bits["mage"]));
        w.Blind = false;
        w.InterfaceHoldsObject = true;
        Assert.AreEqual(AiItemSearchStop.InterfaceBusy, AiGroundRules.SearchItems(w, "mage").Stop);
    }

    // ---- grenade ----

    [TestMethod]
    public void GrenadeStepsInTheOriginalsOrderAndSp15()
    {
        var w = Duel();
        w.Bits["mage"] = DangerFlags.CheckGrenade | DangerFlags.BackingOff;
        Assert.AreEqual(AiGrenadeStop.BackingOff, AiGroundRules.Grenade(w, "mage", "orc", Steamwoven).Stop);
        Assert.IsTrue(w.Bits["mage"].HasFlag(DangerFlags.CheckGrenade), "kept while backing off");
        w.Bits["mage"] = DangerFlags.CheckGrenade;
        w.Throwing = false;
        Assert.AreEqual(AiGrenadeStop.NoThrowingSkill, AiGroundRules.Grenade(w, "mage", "orc", Steamwoven).Stop);
        Assert.AreEqual(AiGrenadeStop.NoCheck, AiGroundRules.Grenade(w, "mage", "orc", Steamwoven).Stop, "one consideration per fight start");
        w.Throwing = true;
        AiGrenadeStop Throw(CombatAnomalyProfile profile)
        {
            w.Bits["mage"] = DangerFlags.CheckGrenade;
            return AiGroundRules.Grenade(w, "mage", "orc", profile).Stop;
        }
        w.GrenadeItem = null;
        Assert.AreEqual(AiGrenadeStop.NoGrenade, Throw(Steamwoven));
        w.GrenadeItem = "grenade";
        w.Radius = 4;
        Assert.AreEqual(AiGrenadeStop.TooClose, Throw(Steamwoven));
        Assert.AreEqual(AiGrenadeStop.TooClose, Throw(Original), "the radius reading is the same in both modes (ARC-DEV-373)");
        w.Radius = 3;
        w.Blocker = "orc";
        Assert.AreEqual(AiGrenadeStop.LineBlocked, Throw(Steamwoven), "any blocker");
        w.Blocker = null;
        Assert.AreEqual(AiGrenadeStop.Throw, Throw(Steamwoven));
        // A PC the NPC protects (its leader) stops a Steamwoven throw only; a dead NPC never does.
        w.Add("pc", HostilityKind.Pc, 5);
        w.Add("corpse", HostilityKind.Npc, 5, c => c with { Dead = true });
        w.Edit("mage", c => c with { Leader = "pc" });
        w.Blast.AddRange(["corpse", "orc", "pc"]);
        Assert.AreEqual(AiGrenadeStop.Throw, Throw(Original));
        w.Bits["mage"] = DangerFlags.CheckGrenade;
        var guarded = AiGroundRules.Grenade(w, "mage", "orc", Steamwoven);
        Assert.AreEqual((AiGrenadeStop.FriendInBlast, "pc"), (guarded.Stop, guarded.Protected));
    }

    // ARC-DEV-364 (TH1), ARC-DEV-373: the original's inverted slot test is a bug the owner fixed in both modes.
    [TestMethod]
    public void GrenadeBlastRadiusIsTheSlotsSpellRadiusInBothModes()
    {
        int Radius(int spell) => spell == 175 ? 3 : spell == 16 ? 1 : 0;
        // A grenade with a spell reads that spell's Begin radius; an item with no spell (10000, low byte 16) reads 0, never Stone Throw's radius.
        Assert.AreEqual(3, AiGroundRules.BlastRadius(175, Radius));
        Assert.AreEqual(0, AiGroundRules.BlastRadius(10000, Radius));
        // A record-less spell and a negative radius read 0.
        Assert.AreEqual(0, AiGroundRules.BlastRadius(222, _ => -1));
        Assert.AreEqual(3, AiGroundRules.BlastRadius(175 + 256, Radius), "the spell is the slot's low byte (0x4CB790)");
    }

    // ---- redirect ----

    [TestMethod]
    public void RedirectP3TestsTheTargetOrEachCandidate()
    {
        var w = Duel();
        w.Add("dim", HostilityKind.Npc, 1, c => c with { Intelligence = 3 });
        w.Add("bright", HostilityKind.Npc, 2, c => c with { Intelligence = 15 });
        w.Add("pc", HostilityKind.Pc, 3, c => c with { Intelligence = 3 });
        w.Add("dead", HostilityKind.Npc, 3, c => c with { Dead = true });
        w.Near.AddRange(["orc", "dim", "bright", "pc", "dead"]);
        w.Edit("orc", c => c with { Intelligence = 12 });
        var component = new AiRedirectComponent(0, 10);
        CollectionAssert.AreEqual(new[] { "dim" }, AiGroundRules.Redirect(w, "orc", component, Steamwoven).ToArray(), "each candidate; PCs get nothing");
        Assert.AreEqual(0, AiGroundRules.Redirect(w, "orc", component, Original).Length, "the target's 12 exceeds 10: nobody");
        w.Edit("orc", c => c with { Intelligence = 10 });
        CollectionAssert.AreEqual(new[] { "dim", "bright" }, AiGroundRules.Redirect(w, "orc", component, Original).ToArray(), "everybody");
        // No threshold: everybody; a mask keeps only candidates with one of its critter flags.
        CollectionAssert.AreEqual(new[] { "dim", "bright" }, AiGroundRules.Redirect(w, "orc", new(0, -1), Steamwoven).ToArray());
        w.Flags["bright"] = 0x8;
        CollectionAssert.AreEqual(new[] { "bright" }, AiGroundRules.Redirect(w, "orc", new(0x8 | 0x4, -1), Steamwoven).ToArray());
    }
}
