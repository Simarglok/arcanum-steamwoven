using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Combat;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Simulation.Tests.Ai;

/// <summary>
/// ARC-WORLD-080 (AI slice I4): target validity (0x4AB990), kill-on-sight (0x4AE120), protect (0x4AE3A0), upset (0x4ADCC0) with
/// HS9, the scan (0x4AB460), choose target (0x4AB0B0), the enemy-list pick (0x4AFA90), the danger update (0x4A92D0) with SP16 and
/// FO8, the reaction to an attack (0x4AA620), the attack event (0x4A9650), the noise squares (0x4A9E10) with AW16 and the witness
/// rule (0x4A9F10), over an invented world: positions on one row, perception and factions set per test.
/// </summary>
[TestClass]
public sealed class HostilityRulesTests
{
    private static readonly CombatAnomalyProfile Steamwoven = CombatAnomalyProfile.Steamwoven, Original = CombatAnomalyProfile.Original;

    /// <summary>A synthetic world: critters on one row (distance = |x − x'|), perception pairs, factions and danger records.</summary>
    private sealed class World : IHostilityWorld
    {
        public readonly Dictionary<string, HostilityCritter> Critters = [];
        public readonly Dictionary<string, int> X = [];
        public readonly HashSet<(string, string)> Seen = [], Factions = [], Vetoes = [];
        public readonly Dictionary<(string, string), Loudness> Heard = [];
        public readonly Dictionary<string, HostilityDanger> Dangers = [];
        public readonly Dictionary<(string, string), int> Reactions = [];
        public readonly Dictionary<string, string> Controllers = [];
        public readonly Dictionary<string, IReadOnlyList<string>> Squares = [];
        public readonly List<(string Npc, int HalfSize)> Queries = [];
        public HostilityParameters Row = new(FleeDistance: 5, HostileReaction: 20, HostileAlignmentGap: 2001, GoodFollowerStop: 50);

        public HostilityCritter Add(string id, HostilityKind kind, int x, Func<HostilityCritter, HostilityCritter>? edit = null)
        {
            var critter = new HostilityCritter { Id = id, Kind = kind, Perception = 5, Intelligence = 8, Level = 1 };
            Critters[id] = edit?.Invoke(critter) ?? critter;
            X[id] = x;
            return Critters[id];
        }

        public void Edit(string id, Func<HostilityCritter, HostilityCritter> edit) => Critters[id] = edit(Critters[id]);
        public void Sees(string a, string b) => Seen.Add((a, b));
        public void Same(string a, string b) { Factions.Add((a, b)); Factions.Add((b, a)); }

        public HostilityCritter? Find(string id) => Critters.GetValueOrDefault(id);
        public int Distance(string from, string to) => Math.Abs(X[from] - X[to]);
        public bool FactionSame(string a, string b) => Factions.Contains((a, b));
        public int HostilityReaction(string npc, string pc) => Reactions.GetValueOrDefault((npc, pc), 50);
        public bool AllowsKillOnSight(string npc, string candidate) => !Vetoes.Contains((npc, candidate));
        public HostilityParameters Parameters(string npc) => Row;
        public AwarenessResult See(string observer, string target) =>
            Seen.Contains((observer, target)) ? new(0, AwarenessReason.Perceived) : new(AwarenessRules.NotPerceived, AwarenessReason.Distance);
        public AwarenessResult Hear(string observer, string target, Loudness loudness) =>
            Heard.TryGetValue((observer, target), out var at) && at == loudness ? new(0, AwarenessReason.Perceived) : new(AwarenessRules.NotPerceived, AwarenessReason.Distance);
        public HostilityDanger Danger(string npc) => Dangers.GetValueOrDefault(npc, HostilityDanger.None);
        public string? MindController(string npc) => Controllers.GetValueOrDefault(npc);
        public IReadOnlyList<string> Square(string npc, int halfSize)
        {
            Queries.Add((npc, halfSize));
            return Squares.GetValueOrDefault(npc, []);
        }
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

    private static HostilityDanger Fighting(string focus) => new(DangerState.CombatFocus, focus, focus, null, []);

    // A wolf at 0 and a PC at 3; the wolf sees the PC.
    private static World Basic()
    {
        var world = new World();
        world.Add("wolf", HostilityKind.Npc, 0);
        world.Add("pc", HostilityKind.Pc, 3);
        world.Sees("wolf", "pc");
        return world;
    }

    [TestMethod]
    public void ValidityRunsItsTestsInTheOriginalsOrder()
    {
        TargetValidity Case(Func<HostilityCritter, HostilityCritter> candidate, Func<HostilityCritter, HostilityCritter>? npc = null)
        {
            var world = Basic();
            world.Add("boss", HostilityKind.Npc, 1);
            world.Edit("pc", candidate);
            if (npc is not null) world.Edit("wolf", npc);
            return HostilityRules.Validate(world, "wolf", "pc", Steamwoven);
        }

        Assert.AreEqual(TargetValidity.Valid, Case(c => c));
        Assert.AreEqual(TargetValidity.Missing, HostilityRules.Validate(Basic(), "wolf", null, Steamwoven));
        Assert.AreEqual(TargetValidity.Missing, HostilityRules.Validate(Basic(), "wolf", "nobody", Steamwoven));
        Assert.AreEqual(TargetValidity.Gone, Case(c => c with { Invulnerable = true, Dead = true }), "object flags before death");
        Assert.AreEqual(TargetValidity.Gone, Case(c => c with { Destroyed = true }));
        Assert.AreEqual(TargetValidity.Self, HostilityRules.Validate(Basic(), "wolf", "wolf", Steamwoven));
        Assert.AreEqual(TargetValidity.Dead, Case(c => c with { Dead = true, Stoned = true }));
        Assert.AreEqual(TargetValidity.Unconscious, Case(c => c with { Unconscious = true }, n => n with { NonLethal = true }));
        Assert.AreEqual(TargetValidity.Leader, Case(c => c with { Leader = "boss" }, n => n with { Leader = "pc" }));
        Assert.AreEqual(TargetValidity.FellowFollower, Case(c => c with { Leader = "boss" }, n => n with { Leader = "boss" }));
        Assert.AreEqual(TargetValidity.Follower, Case(c => c with { Leader = "wolf" }));
        Assert.AreEqual(TargetValidity.Stoned, Case(c => c with { Stoned = true, InvisibleSpell = true }));
        Assert.AreEqual(TargetValidity.Invisible, Case(c => c with { InvisibleSpell = true }));
        Assert.AreEqual(TargetValidity.Valid, Case(c => c with { InvisibleSpell = true, InCombatMode = true }));
        Assert.AreEqual(TargetValidity.Decoy, Case(c => c with { Decoy = true }), "intelligence 8 sees through the decoy");
        Assert.AreEqual(TargetValidity.Valid, Case(c => c with { Decoy = true }, n => n with { Intelligence = 4 }));
        Assert.AreEqual(TargetValidity.Valid, Case(c => c with { Concealed = true, MasterProwler = true }), "seen: a concealed master prowler stays valid");
        var hidden = Basic();
        hidden.Seen.Clear();
        hidden.Edit("pc", c => c with { Concealed = true, MasterProwler = true });
        Assert.AreEqual(TargetValidity.UnseenProwler, HostilityRules.Validate(hidden, "wolf", "pc", Steamwoven));

        var far = Basic();
        far.X["pc"] = 21;
        Assert.AreEqual(TargetValidity.TooFar, HostilityRules.Validate(far, "wolf", "pc", Steamwoven));
        far.X["pc"] = 20;
        Assert.AreEqual(TargetValidity.Valid, HostilityRules.Validate(far, "wolf", "pc", Steamwoven), "20 tiles is within the leash");
        far.Add("boss", HostilityKind.Npc, -1);
        far.Edit("wolf", n => n with { Leader = "boss" });
        Assert.AreEqual(TargetValidity.TooFarFromLeader, HostilityRules.Validate(far, "wolf", "pc", Steamwoven));

        var objects = Basic();
        objects.Add("wall", HostilityKind.Wall, 1);
        objects.Add("door", HostilityKind.Object, 1, d => d with { Busted = true, Dead = true });
        objects.Add("chest", HostilityKind.Object, 1, d => d with { Stoned = true });
        Assert.AreEqual(TargetValidity.Wall, HostilityRules.Validate(objects, "wolf", "wall", Steamwoven));
        Assert.AreEqual(TargetValidity.Busted, HostilityRules.Validate(objects, "wolf", "door", Steamwoven), "critter tests skip objects");
        Assert.AreEqual(TargetValidity.Valid, HostilityRules.Validate(objects, "wolf", "chest", Steamwoven));
    }

    [TestMethod]
    public void AnUnconsciousTargetStaysValidOnlyWhileTheScanFindsNoOther()
    {
        var world = Basic();
        world.Edit("pc", c => c with { Unconscious = true });
        world.Edit("wolf", n => n with { KillOnSight = true });
        world.Squares["wolf"] = ["pc", "wolf"];
        Assert.AreEqual(TargetValidity.Valid, HostilityRules.Validate(world, "wolf", "pc", Steamwoven), "the scan finds the PC itself");
        Assert.AreEqual(("wolf", 5), world.Queries.Single(), "the nested scan's square: clamp(Perception, 1, 10)");

        world.Add("sheep", HostilityKind.Npc, 2);
        world.Sees("wolf", "sheep");
        world.Squares["wolf"] = ["pc", "sheep"];
        Assert.AreEqual(TargetValidity.Unconscious, HostilityRules.Validate(world, "wolf", "pc", Steamwoven), "the conscious sheep comes first");
        // Inside a scan no nested scan runs: the unconscious PC alone is the scan's target.
        world.Edit("sheep", s => s with { Dead = true });
        Assert.AreEqual("pc", HostilityRules.FindTarget(world, "wolf", Steamwoven).Target);
    }

    [TestMethod]
    public void InvisibilityAndTheDecoyFollowTheirFlags()
    {
        var npc = new HostilityCritter { Id = "n", Kind = HostilityKind.Npc, Intelligence = 4 };
        var pc = new HostilityCritter { Id = "p", Kind = HostilityKind.Pc, InvisibleSpell = true };
        Assert.IsFalse(HostilityRules.CanTargetInvisible(npc, pc));
        Assert.IsTrue(HostilityRules.CanTargetInvisible(npc with { DetectsInvisible = true }, pc with { ObjectInvisible = true }));
        Assert.IsFalse(HostilityRules.CanTargetInvisible(npc, pc with { ObjectInvisible = true, InCombatMode = true }));
        Assert.IsTrue(HostilityRules.CanTargetInvisible(npc, pc with { AttemptingAttack = true }));
        Assert.IsTrue(HostilityRules.CanTargetInvisible(npc, pc with { Kind = HostilityKind.Npc }));
        Assert.IsTrue(HostilityRules.CanTargetInvisible(npc, pc with { InvisibleSpell = false, ObjectInvisible = true }));

        var decoy = new HostilityCritter { Id = "d", Kind = HostilityKind.Npc, Decoy = true };
        Assert.IsTrue(HostilityRules.DecoyFor(npc, decoy));
        Assert.IsFalse(HostilityRules.DecoyFor(npc with { Intelligence = 5 }, decoy));
        Assert.IsFalse(HostilityRules.DecoyFor(npc, decoy with { Dead = true }));
    }

    [TestMethod]
    public void KillOnSightTakesItsRowsInOrder()
    {
        var world = Basic();
        world.Edit("wolf", n => n with { KillOnSight = true });
        Assert.AreEqual(KillOnSight.Faction, HostilityRules.KillOnSight(world, "wolf", "pc", Steamwoven));
        world.Vetoes.Add(("wolf", "pc"));
        Assert.AreEqual(KillOnSight.No, HostilityRules.KillOnSight(world, "wolf", "pc", Steamwoven), "the will-kos script vetoes");
        world.Vetoes.Clear();
        world.Dangers["wolf"] = new(DangerState.Surrender, "pc", null, null, []);
        Assert.AreEqual(KillOnSight.No, HostilityRules.KillOnSight(world, "wolf", "pc", Steamwoven), "surrendered to it");
        world.Dangers.Clear();
        world.Edit("wolf", n => n with { Undead = true });
        world.Edit("pc", c => c with { Enshrouded = true });
        Assert.AreEqual(KillOnSight.No, HostilityRules.KillOnSight(world, "wolf", "pc", Steamwoven), "an enshrouded PC is safe from undead KOS");
        world.Edit("wolf", n => n with { Undead = false, KosOverride = true });
        world.Reactions[("wolf", "pc")] = 20;
        Assert.AreEqual(KillOnSight.Reaction, HostilityRules.KillOnSight(world, "wolf", "pc", Steamwoven), "the override skips FACTION");
        world.Reactions[("wolf", "pc")] = 21;
        Assert.AreEqual(KillOnSight.No, HostilityRules.KillOnSight(world, "wolf", "pc", Steamwoven));
        world.Row = world.Row with { HostileAlignmentGap = 100 };
        world.Edit("pc", c => c with { Alignment = -60 });
        world.Edit("wolf", n => n with { Alignment = 40 });
        Assert.AreEqual(KillOnSight.Alignment, HostilityRules.KillOnSight(world, "wolf", "pc", Steamwoven));
        world.Row = world.Row with { HostileAlignmentGap = 2001 };
        world.Edit("wolf", n => n with { Intelligence = 3 });
        world.Edit("pc", c => c with { Decoy = true });
        Assert.AreEqual(KillOnSight.Decoy, HostilityRules.KillOnSight(world, "wolf", "pc", Steamwoven));
        // With a PC leader only GUARD can fire, and it needs an NPC candidate.
        world.Edit("wolf", n => n with { PcLeader = "pc2", Intelligence = 8 });
        world.Edit("pc", c => c with { Decoy = false });
        Assert.AreEqual(KillOnSight.No, HostilityRules.KillOnSight(world, "wolf", "pc", Steamwoven));
    }

    [TestMethod]
    public void GuardFiresOnAnNpcFightingSomeoneTheNpcProtects()
    {
        var world = Basic();
        world.Add("bandit", HostilityKind.Npc, 2);
        world.Add("villager", HostilityKind.Npc, 4);
        world.Dangers["bandit"] = Fighting("villager");
        Assert.AreEqual(KillOnSight.No, HostilityRules.KillOnSight(world, "wolf", "bandit", Steamwoven), "no protection");
        world.Same("wolf", "villager");
        Assert.AreEqual(KillOnSight.Guard, HostilityRules.KillOnSight(world, "wolf", "bandit", Steamwoven));
        world.Same("wolf", "bandit");
        Assert.AreEqual(KillOnSight.No, HostilityRules.KillOnSight(world, "wolf", "bandit", Steamwoven), "upset: one faction");

        // A leaderless guard against a leaderless monster: HS9 makes both leaderless critters "party" in the Original setting.
        var guard = Basic();
        guard.Edit("wolf", n => n with { Guard = true });
        guard.Add("troll", HostilityKind.Npc, 2, t => t with { Monstrous = true });
        guard.Add("farmer", HostilityKind.Npc, 4);
        guard.Dangers["troll"] = Fighting("farmer");
        Assert.AreEqual(KillOnSight.Guard, HostilityRules.KillOnSight(guard, "wolf", "troll", Steamwoven));
        Assert.AreEqual(KillOnSight.No, HostilityRules.KillOnSight(guard, "wolf", "troll", Original));
    }

    [TestMethod]
    public void ProtectAndUpsetFollowTheirTables()
    {
        var world = Basic();
        world.Add("boss", HostilityKind.Npc, 1);
        world.Add("other", HostilityKind.Npc, 2, o => o with { Origin = 7 });
        Assert.AreEqual(Protection.Self, HostilityRules.Protect(world, "wolf", "wolf"));
        world.Edit("wolf", n => n with { Leader = "boss", MindControlled = true, Guard = true, Origin = 7 });
        Assert.AreEqual(Protection.Group, HostilityRules.Protect(world, "wolf", "boss"));
        world.Same("wolf", "other");
        Assert.AreEqual(Protection.No, HostilityRules.Protect(world, "wolf", "other"), "mind control after GROUP");
        world.Edit("wolf", n => n with { MindControlled = false });
        Assert.AreEqual(Protection.Faction, HostilityRules.Protect(world, "wolf", "other"));
        world.Factions.Clear();
        Assert.AreEqual(Protection.Origin, HostilityRules.Protect(world, "wolf", "other"));
        world.Edit("wolf", n => n with { Guard = false });
        Assert.AreEqual(Protection.No, HostilityRules.Protect(world, "wolf", "other"));

        // Upset: a target in the leader's party, too good for a good follower, of one origin or faction.
        world.Edit("other", o => o with { Leader = "boss", Alignment = 60 });
        Assert.AreEqual(UpsetReason.Party, HostilityRules.Upset(world, "wolf", "other", "boss", Steamwoven));
        world.Edit("other", o => o with { Leader = null });
        world.Edit("wolf", n => n with { Alignment = 10, Origin = 0 });
        Assert.AreEqual(UpsetReason.Good, HostilityRules.Upset(world, "wolf", "other", "boss", Steamwoven));
        Assert.AreEqual(UpsetReason.None, HostilityRules.Upset(world, "wolf", "other", null, Steamwoven), "GOOD needs a leader; HS9 fixed");
        Assert.AreEqual(UpsetReason.Party, HostilityRules.Upset(world, "wolf", "other", null, Original), "HS9: two null leaders match");
        world.Edit("wolf", n => n with { Origin = 7 });
        Assert.AreEqual(UpsetReason.Origin, HostilityRules.Upset(world, "wolf", "other", null, Steamwoven));
        world.Same("wolf", "pc");
        Assert.AreEqual(UpsetReason.Faction, HostilityRules.Upset(world, "wolf", "pc", null, Steamwoven));
        world.Add("door", HostilityKind.Object, 1);
        Assert.AreEqual(UpsetReason.None, HostilityRules.Upset(world, "wolf", "door", null, Original), "not a critter");

        // A mind-controlled NPC obeys its controller, or (no controller found) its PC leader.
        world.Edit("wolf", n => n with { MindControlled = true });
        world.Controllers["wolf"] = "boss";
        Assert.AreEqual(UpsetReason.None, HostilityRules.Upset(world, "wolf", "pc", "boss", Steamwoven));
        Assert.AreEqual(UpsetReason.Faction, HostilityRules.Upset(world, "wolf", "pc", "pc", Steamwoven), "another leader: the table runs");
        world.Controllers.Clear();
        world.Edit("wolf", n => n with { PcLeader = "pc" });
        Assert.AreEqual(UpsetReason.None, HostilityRules.Upset(world, "wolf", "pc", "anyone", Steamwoven));
    }

    [TestMethod]
    public void TheScanSortsTheFirstFiftyAndTakesTheFirstKillOnSight()
    {
        var world = Basic();
        world.Edit("wolf", n => n with { KillOnSight = true, Intelligence = 3, Perception = 15 });
        world.Add("far", HostilityKind.Pc, 9);
        world.Add("asleep", HostilityKind.Pc, 1, c => c with { Unconscious = true });
        world.Add("decoy", HostilityKind.Npc, 8, d => d with { Decoy = true });
        world.Same("wolf", "decoy");
        foreach (var id in new[] { "far", "asleep", "decoy" }) world.Sees("wolf", id);
        world.Squares["wolf"] = ["far", "asleep", "pc", "decoy", "wolf"];
        var scan = HostilityRules.FindTarget(world, "wolf", Steamwoven);
        Assert.AreEqual(("wolf", 10), world.Queries.Single(), "perception 15 clamps to 10");
        Assert.AreEqual(new ScanResult("decoy", ScanStep.KillOnSight, KillOnSight.Decoy, null), scan, "the decoy sorts first (key 0)");
        world.Edit("decoy", d => d with { Dead = true });
        Assert.AreEqual("pc", HostilityRules.FindTarget(world, "wolf", Steamwoven).Target, "distance 3 before 9; unconscious last");

        // Only the first 50 critters of the query are kept, before sorting.
        var crowd = Basic();
        crowd.Edit("wolf", n => n with { KillOnSight = true });
        var ids = Enumerable.Range(0, 50).Select(i => $"ally{i}").ToList();
        foreach (var id in ids) { crowd.Add(id, HostilityKind.Npc, 1); crowd.Same("wolf", id); }
        crowd.Squares["wolf"] = [.. ids, "pc"];
        Assert.AreEqual(new ScanResult(null, ScanStep.None, KillOnSight.No, null), HostilityRules.FindTarget(crowd, "wolf", Steamwoven));

        var refused = Basic();
        refused.Edit("wolf", n => n with { NighInvulnerable = true });
        Assert.AreEqual(ScanStep.Refused, HostilityRules.FindTarget(refused, "wolf", Steamwoven).Step);
        Assert.AreEqual(0, refused.Queries.Count);
    }

    [TestMethod]
    public void TheScanHearsConcealedCritterAtSilentAndTurnsTowardAConcealedPc()
    {
        var world = Basic();
        world.Seen.Clear();
        world.Edit("pc", c => c with { Concealed = true });
        world.Squares["wolf"] = ["pc"];
        world.Heard[("wolf", "pc")] = Loudness.Normal;
        Assert.AreEqual(new ScanResult(null, ScanStep.None, KillOnSight.No, null), HostilityRules.FindTarget(world, "wolf", Steamwoven),
            "a concealed PC is heard only at Silent");
        world.Heard[("wolf", "pc")] = Loudness.Silent;
        Assert.AreEqual(new ScanResult(null, ScanStep.None, KillOnSight.No, "pc"), HostilityRules.FindTarget(world, "wolf", Steamwoven));
    }

    [TestMethod]
    public void AlliesInDangerAndDeadAlliesHandOverTheirEnemies()
    {
        var world = Basic();
        world.Add("sheep", HostilityKind.Npc, 1);
        world.Add("bandit", HostilityKind.Npc, 6);
        world.Sees("wolf", "sheep");
        world.Squares["wolf"] = ["sheep"];
        world.Dangers["sheep"] = new(DangerState.Flee, "bandit", null, null, []);
        Assert.IsNull(HostilityRules.FindTarget(world, "wolf", Steamwoven).Target, "no protection");
        world.Same("wolf", "sheep");
        Assert.IsNull(HostilityRules.FindTarget(world, "wolf", Steamwoven).Target, "the source is not perceived");
        world.Heard[("wolf", "bandit")] = Loudness.Normal;
        Assert.AreEqual(new ScanResult("bandit", ScanStep.AllyHelp, KillOnSight.No, null), HostilityRules.FindTarget(world, "wolf", Steamwoven));

        // HS9: a leaderless wolf helping against a leaderless bandit is "party" in the Original setting.
        Assert.IsNull(HostilityRules.FindTarget(world, "wolf", Original).Target);

        // Avenge: a dead ally out of sight still points at its killer, who must be perceived.
        world.Seen.Clear();
        world.Edit("sheep", s => s with { Dead = true });
        world.Dangers["sheep"] = new(DangerState.None, null, "bandit", null, []);
        Assert.AreEqual(new ScanResult("bandit", ScanStep.Avenge, KillOnSight.No, null), HostilityRules.FindTarget(world, "wolf", Original));
    }

    [TestMethod]
    public void ChooseTargetRollsOnlyWhenTheRowsDoNotDecide()
    {
        var world = Basic();
        world.Add("near", HostilityKind.Npc, 2, c => c with { Level = 1 });
        world.Add("strong", HostilityKind.Npc, 6, c => c with { Level = 9 });
        var none = new Rolls();
        Assert.AreEqual("near", HostilityRules.ChooseTarget(world, "wolf", null, "near", none));
        Assert.AreEqual("near", HostilityRules.ChooseTarget(world, "wolf", "near", null, none));
        world.Edit("strong", c => c with { Unconscious = true });
        Assert.AreEqual("near", HostilityRules.ChooseTarget(world, "wolf", "near", "strong", none));
        world.Edit("strong", c => c with { Unconscious = false });
        world.X["strong"] = 21;
        Assert.AreEqual("near", HostilityRules.ChooseTarget(world, "wolf", "near", "strong", none));
        Assert.AreEqual(0, none.Asked.Count);

        world.X["strong"] = 6;
        var byLevel = new Rolls(50);
        Assert.AreEqual("strong", HostilityRules.ChooseTarget(world, "wolf", "near", "strong", byLevel), "9 − 6 > 1 − 2");
        Assert.AreEqual((AiRollPurpose.ChooseTargetScore, 1, 100), byLevel.Asked.Single());
        Assert.AreEqual("near", HostilityRules.ChooseTarget(world, "wolf", "near", "strong", new Rolls(51)), "nearness");
        Assert.AreEqual("near", HostilityRules.ChooseTarget(world, "wolf", "near", "near", new Rolls(50)), "a tie keeps the current");
    }

    [TestMethod]
    public void TheEnemyListPickStartsAtARandomEntryAndDrawsAlsoForAnEmptyList()
    {
        var empty = new Rolls();
        Assert.IsNull(HostilityRules.PickEnemy(Array.Empty<string>(), _ => true, empty));
        Assert.AreEqual((AiRollPurpose.EnemyListStart, -1, 0), empty.Asked.Single(), "random(0, −1) swapped");
        var one = new Rolls();
        Assert.AreEqual("a", HostilityRules.PickEnemy(new[] { "a" }, _ => true, one));
        Assert.AreEqual((AiRollPurpose.EnemyListStart, 0, 0), one.Asked.Single());
        Assert.AreEqual("a", HostilityRules.PickEnemy(new[] { "a", "b", "c" }, e => e != "c", new Rolls(2)), "wraps past the end");
        Assert.IsNull(HostilityRules.PickEnemy(new[] { "a", "b" }, _ => false, new Rolls(1)));
    }

    [TestMethod]
    public void TheDangerUpdateTakesFocusThenScanThenHitByThenTheEnemyList()
    {
        var world = Basic();
        world.Add("bear", HostilityKind.Npc, 2);
        world.Add("fox", HostilityKind.Npc, 4);
        world.Squares["wolf"] = [];
        world.Dangers["wolf"] = new(DangerState.CombatFocus, "pc", "pc", "bear", ["fox"]);
        var focus = HostilityRules.DangerUpdate(world, "wolf", new Rolls(), Steamwoven);
        Assert.AreEqual((DangerUpdatePath.Focus, "pc", (DangerState?)DangerState.CombatFocus, false), (focus.Path, focus.Target, focus.Request, focus.Unlocks));

        world.Edit("pc", c => c with { Dead = true });
        var hitBy = HostilityRules.DangerUpdate(world, "wolf", new Rolls(), Steamwoven);
        Assert.AreEqual((DangerUpdatePath.HitBy, "bear", true), (hitBy.Path, hitBy.Target, hitBy.Unlocks));
        world.Edit("bear", c => c with { Dead = true });
        var rolls = new Rolls();
        var listed = HostilityRules.DangerUpdate(world, "wolf", rolls, Steamwoven);
        Assert.AreEqual((DangerUpdatePath.EnemyList, "fox"), (listed.Path, listed.Target));
        Assert.AreEqual(AiRollPurpose.EnemyListStart, rolls.Asked.Single().Purpose);
        // SP16: the Original setting passes the pass record's target, which the spell choice may have made the NPC itself.
        Assert.AreEqual("fox", HostilityRules.FocusTarget(listed, "wolf", Steamwoven));
        Assert.AreEqual("wolf", HostilityRules.FocusTarget(listed, "wolf", Original));
        Assert.AreEqual("pc", HostilityRules.FocusTarget(focus, "wolf", Original), "other paths pass the found target");
        world.Edit("fox", c => c with { Dead = true });
        var calm = HostilityRules.DangerUpdate(world, "wolf", new Rolls(), Steamwoven);
        Assert.AreEqual((DangerUpdatePath.CalmDown, (string?)null, (DangerState?)DangerState.None), (calm.Path, calm.Target, calm.Request));
        Assert.IsNull(HostilityRules.FocusTarget(calm, "wolf", Original));

        world.Edit("fox", c => c with { Dead = false });
        world.Edit("wolf", n => n with { KillOnSight = true });
        world.Squares["wolf"] = ["fox"];
        world.Sees("wolf", "fox");
        var rescan = HostilityRules.DangerUpdate(world, "wolf", new Rolls(), Steamwoven);
        Assert.AreEqual((DangerUpdatePath.Rescan, "fox", true), (rescan.Path, rescan.Target, rescan.Unlocks));

        world.Dangers["wolf"] = HostilityDanger.None;
        var scan = HostilityRules.DangerUpdate(world, "wolf", new Rolls(), Steamwoven);
        Assert.AreEqual((DangerUpdatePath.Scan, "fox", false), (scan.Path, scan.Target, scan.Unlocks));
        world.Edit("wolf", n => n with { Asleep = true });
        Assert.AreEqual(DangerUpdatePath.Asleep, HostilityRules.DangerUpdate(world, "wolf", new Rolls(), Steamwoven).Path);
    }

    [TestMethod]
    public void ASurrenderedNpcRollsToCalmDownOnlyAtEightyPercent()
    {
        var world = Basic();
        world.Dangers["wolf"] = new(DangerState.Surrender, "pc", null, null, []);
        world.Edit("wolf", n => n with { HealthPercent = 79 });
        var low = new Rolls(1);
        Assert.AreEqual(DangerUpdatePath.SurrenderStays, HostilityRules.DangerUpdate(world, "wolf", low, Steamwoven).Path);
        Assert.AreEqual(0, low.Asked.Count);
        world.Edit("wolf", n => n with { HealthPercent = 80 });
        var calm = new Rolls(1);
        Assert.AreEqual(DangerUpdatePath.SurrenderCalms, HostilityRules.DangerUpdate(world, "wolf", calm, Steamwoven).Path);
        Assert.AreEqual((AiRollPurpose.SurrenderCalm, 1, 500), calm.Asked.Single());
        Assert.AreEqual(DangerUpdatePath.SurrenderStays, HostilityRules.DangerUpdate(world, "wolf", new Rolls(2), Steamwoven).Path);
        world.Dangers["wolf"] = new(DangerState.Flee, "pc", null, null, []);
        Assert.AreEqual(DangerUpdatePath.Flee, HostilityRules.DangerUpdate(world, "wolf", new Rolls(), Steamwoven).Path);
    }

    [TestMethod]
    public void Fo8DropsTheChosenActionOnlyWhenRuleFourRefusesTheFight()
    {
        var refused = DangerRules.Set(DangerFlags.NoAttack, DangerState.CombatFocus, default, Steamwoven);
        Assert.IsFalse(HostilityRules.KeepsChosenAction(refused, Steamwoven));
        Assert.IsTrue(HostilityRules.KeepsChosenAction(refused, Original));
        var off = DangerRules.Set(DangerFlags.None, DangerState.CombatFocus, new(NpcFightingOff: true), Steamwoven);
        Assert.IsFalse(HostilityRules.KeepsChosenAction(off, Steamwoven));
        Assert.IsTrue(HostilityRules.KeepsChosenAction(DangerRules.Set(DangerFlags.None, DangerState.CombatFocus, default, Steamwoven), Steamwoven));
    }

    [TestMethod]
    public void TheReactionToAnAttackWeighsByState()
    {
        var world = Basic();
        world.Add("bear", HostilityKind.Npc, 9);
        var flee = false;
        AttackReaction React(Rolls rolls) => HostilityRules.ReactToAttack(world, "wolf", "pc", () => flee, rolls);

        Assert.AreEqual(new AttackReaction(AttackReactionStep.Weigh, DangerState.CombatFocus, "pc", true), React(new Rolls()));
        flee = true;
        Assert.AreEqual(new AttackReaction(AttackReactionStep.Weigh, DangerState.Flee, "pc", true), React(new Rolls()));
        flee = false;

        world.Dangers["wolf"] = Fighting("bear");
        Assert.AreEqual(AttackReactionStep.Weigh, React(new Rolls(50)).Step, "level 1 − 3 > 1 − 9");
        world.Dangers["wolf"] = Fighting("pc");
        var noRoll = new Rolls();
        Assert.AreEqual(AttackReactionStep.Weigh, React(noRoll).Step);
        Assert.AreEqual(0, noRoll.Asked.Count, "the attacker is the focus: no choice");
        world.X["bear"] = 1;
        world.Dangers["wolf"] = Fighting("bear");
        Assert.AreEqual(new AttackReaction(AttackReactionStep.KeepFocus, null, null, true), React(new Rolls(51)));

        world.Dangers["wolf"] = new(DangerState.Flee, "bear", null, null, []);
        Assert.AreEqual(AttackReactionStep.KeepFleeing, React(new Rolls()).Step, "within 5 tiles of what it flees");
        world.X["bear"] = 6;
        Assert.AreEqual(AttackReactionStep.Weigh, React(new Rolls()).Step);
        world.Dangers["wolf"] = new(DangerState.Flee, "pc", null, null, []);
        Assert.AreEqual(AttackReactionStep.KeepFleeing, React(new Rolls()).Step, "it already flees the attacker");

        world.X["bear"] = 1;
        world.Dangers["wolf"] = new(DangerState.Surrender, "bear", null, null, []);
        Assert.AreEqual(AttackReactionStep.FleeGoal, React(new Rolls(51)).Step);
        world.Edit("wolf", n => n with { Dead = true });
        Assert.AreEqual(new AttackReaction(AttackReactionStep.Dead, null, null, false), React(new Rolls()));
    }

    [TestMethod]
    public void TheAttackEventStopsWhereTheOriginalStops()
    {
        var world = Basic();
        world.Add("boss", HostilityKind.Npc, 1);
        world.Add("door", HostilityKind.Object, 2);
        world.Add("trap", HostilityKind.Object, 2);
        Assert.AreEqual(AttackEventStop.Missing, HostilityRules.AttackEvent(world, null, "wolf", AttackEventFlags.None).Stop);
        world.Edit("wolf", n => n with { Asleep = true, Off = true });
        var gone = HostilityRules.AttackEvent(world, "pc", "wolf", AttackEventFlags.None);
        Assert.AreEqual((AttackEventStop.VictimGone, true), (gone.Stop, gone.WakesVictim), "woken before the object flags");
        world.Edit("wolf", n => n with { Off = false, Dead = true });
        Assert.AreEqual(AttackEventStop.VictimDead, HostilityRules.AttackEvent(world, "pc", "wolf", AttackEventFlags.None).Stop);
        world.Edit("wolf", n => n with { Dead = false, Asleep = false });
        Assert.AreEqual(AttackEventStop.Self, HostilityRules.AttackEvent(world, "wolf", "wolf", AttackEventFlags.None).Stop);
        var trap = HostilityRules.AttackEvent(world, "trap", "wolf", AttackEventFlags.None);
        Assert.AreEqual((AttackEventStop.ObjectAttacker, true), (trap.Stop, trap.VictimFightsObject));
        var ordered = HostilityRules.AttackEvent(world, "wolf", "pc", AttackEventFlags.Ordered);
        Assert.AreEqual((AttackEventStop.Ordered, (string?)null, false), (ordered.Stop, ordered.OrderedLeader, ordered.TellsAttackerFollowers));
        world.Edit("wolf", n => n with { Leader = "boss" });
        Assert.AreEqual("boss", HostilityRules.AttackEvent(world, "wolf", "pc", AttackEventFlags.Ordered | AttackEventFlags.NoFanOut).OrderedLeader);
        var door = HostilityRules.AttackEvent(world, "pc", "door", AttackEventFlags.None);
        Assert.AreEqual((AttackEventStop.ObjectVictim, true), (door.Stop, door.TellsAttackerFollowers));

        world.Edit("pc", c => c with { Concealed = true });
        var onPc = HostilityRules.AttackEvent(world, "wolf", "pc", AttackEventFlags.None);
        Assert.AreEqual(new AttackEventPlan
        {
            Stop = AttackEventStop.PcVictim, TellsAttackerFollowers = true, LosesConcealment = "pc", PcVictimResponds = true,
            TellsVictimFollowers = true, RaisesNoise = true,
        }, onPc);
        var scripted = HostilityRules.AttackEvent(world, "wolf", "pc", AttackEventFlags.NoFanOut);
        Assert.IsFalse(scripted.TellsAttackerFollowers || scripted.PcVictimResponds || scripted.TellsVictimFollowers || scripted.RaisesNoise);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => HostilityRules.AttackEvent(world, "wolf", "pc", (AttackEventFlags)8));
    }

    [TestMethod]
    public void TheAttackEventOnAnNpcRecordsTheAttackerAndChangesTheReaction()
    {
        var world = Basic();
        world.Add("bear", HostilityKind.Npc, 1);
        var deliberate = HostilityRules.AttackEvent(world, "pc", "wolf", AttackEventFlags.None);
        Assert.AreEqual(new AttackEventPlan
        {
            TellsAttackerFollowers = true, ClearsKosOverride = true, TellsVictimFollowers = true, RaisesNoise = true, TurnsVictim = true,
            WritesHitBy = true, AddsAttackerTeam = true, Reaction = AttackReactionChange.Deliberate, ReactsToAttack = true,
        }, deliberate);
        Assert.AreEqual(AttackReactionChange.Accidental, HostilityRules.AttackEvent(world, "pc", "wolf", AttackEventFlags.Accidental).Reaction);
        world.Edit("wolf", n => n with { Leader = "pc", PcLeader = "pc" });
        Assert.AreEqual(AttackReactionChange.FollowerAccidental, HostilityRules.AttackEvent(world, "pc", "wolf", AttackEventFlags.Accidental).Reaction);
        Assert.AreEqual(AttackReactionChange.FollowerDeliberate, HostilityRules.AttackEvent(world, "pc", "wolf", AttackEventFlags.None).Reaction);

        // An NPC attacker also takes the victim's team; an accidental hit between one faction records nothing and is not answered.
        var npc = HostilityRules.AttackEvent(world, "bear", "wolf", AttackEventFlags.Accidental);
        Assert.AreEqual((true, true, AttackReactionChange.None, false), (npc.AddsVictimTeam, npc.WritesHitBy, npc.Reaction, npc.RaisesNoise));
        world.Same("bear", "wolf");
        var friendly = HostilityRules.AttackEvent(world, "bear", "wolf", AttackEventFlags.Accidental);
        Assert.AreEqual((false, true, false), (friendly.WritesHitBy, friendly.AddsAttackerTeam, friendly.ReactsToAttack), "HS19: still listed");
    }

    [TestMethod]
    public void Aw16HandlesAWitnessInBothSquaresOnceInSteamwoven()
    {
        string[] around = ["a", "b"], victim = ["b", "c"];
        CollectionAssert.AreEqual(new[] { "a", "b", "c" }, HostilityRules.NoiseWitnesses(5, around, victim, Steamwoven).ToArray());
        CollectionAssert.AreEqual(new[] { "a", "b", "b", "c" }, HostilityRules.NoiseWitnesses(5, around, victim, Original).ToArray());
        CollectionAssert.AreEqual(new[] { "a", "b" }, HostilityRules.NoiseWitnesses(1, around, victim, Original).ToArray(), "1 tile: the attacker's square only");
        CollectionAssert.AreEqual(new[] { "a", "b", "b", "c" }, HostilityRules.NoiseWitnesses(29, around, victim, Original).ToArray());
        CollectionAssert.AreEqual(new[] { "b", "c" }, HostilityRules.NoiseWitnesses(30, around, victim, Original).ToArray(), "30 tiles: the victim's only");
    }

    [TestMethod]
    public void AWitnessDefendsWhomItProtectsOrFlees()
    {
        var world = Basic();
        world.Add("guard", HostilityKind.Npc, 1, g => g with { Asleep = true });
        world.Add("bandit", HostilityKind.Npc, 6);
        WitnessOutcome Witness() => HostilityRules.Witness(world, "guard", "bandit", "wolf", Loudness.Normal);

        Assert.AreEqual(new WitnessOutcome(WitnessResponse.None, true), Witness(), "a bystander that perceives nothing; woken anyway");
        world.Heard[("guard", "wolf")] = Loudness.Normal;
        Assert.AreEqual(new WitnessOutcome(WitnessResponse.FleeFromAttacker, true), Witness());
        world.Edit("guard", g => g with { NoFlee = true });
        Assert.AreEqual(WitnessResponse.None, Witness().Response);
        world.Same("guard", "wolf");
        Assert.AreEqual(WitnessResponse.ReactToAttacker, Witness().Response);
        world.Factions.Clear();
        world.Same("guard", "bandit");
        Assert.AreEqual(WitnessResponse.None, Witness().Response, "protects the attacker, which it does not perceive");
        world.Sees("guard", "bandit");
        Assert.AreEqual(WitnessResponse.ReactToVictim, Witness().Response);
        world.Edit("guard", g => g with { Leader = "bandit" });
        Assert.AreEqual(new WitnessOutcome(WitnessResponse.None, false), Witness(), "the attacker is its leader: not even woken");
        world.Edit("guard", g => g with { WaitingHere = true });
        Assert.AreEqual(WitnessResponse.ReactToVictim, Witness().Response, "a waiting follower has no leader here");
    }
}
