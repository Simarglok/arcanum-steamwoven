using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Skills;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Simulation.Tests.Ai;

/// <summary>ARC-WORLD-078 (AI slice I2): sight, hearing and the sound line (0x4AF240, 0x45EFF0, 0x4AF260, 0x4AF470, 0x4AF640)
/// over a synthetic map, with the worked numbers of the awareness study and the FR-AI-002 matrix.</summary>
[TestClass]
public sealed class AwarenessRulesTests
{
    private const uint Opaque = 0, Cover = LineOfFireRules.ShootThrough | LineOfFireRules.SeeThrough | LineOfFireRules.ProvidesCover;
    private static readonly CombatAnomalyProfile Steamwoven = CombatAnomalyProfile.Steamwoven, Original = CombatAnomalyProfile.Original;

    private sealed class Map(int size = 64) : IAwarenessMap<string>
    {
        public readonly Dictionary<(CellCoord, int), List<LineOfFirePiece<string>>> Pieces = [];
        public readonly Dictionary<CellCoord, List<LineOfFireObject<string>>> Things = [];
        public readonly HashSet<CellCoord> Quiet = [];
        private static readonly (int X, int Y)[] Offsets = [(-1, -1), (-1, 0), (-1, 1), (0, 1), (1, 1), (1, 0), (1, -1), (0, -1)];

        public bool Contains(CellCoord tile) => tile.X >= 0 && tile.Y >= 0 && tile.X < size && tile.Y < size;
        public CellCoord? Step(CellCoord from, int rotation)
        {
            var next = new CellCoord(from.X + Offsets[rotation].X, from.Y + Offsets[rotation].Y);
            return Contains(next) ? next : null;
        }
        public IEnumerable<LineOfFirePiece<string>> Edge(CellCoord tile, int edge) => Pieces.GetValueOrDefault((tile, edge)) ?? [];
        public IEnumerable<LineOfFireObject<string>> Objects(CellCoord tile) => Things.GetValueOrDefault(tile) ?? [];
        public bool Soundproof(CellCoord tile) => Quiet.Contains(tile);

        public Map With(CellCoord cell, string name, uint flags)
        {
            if (!Things.TryGetValue(cell, out var list)) Things[cell] = list = [];
            list.Add(new(name, false, flags));
            return this;
        }

        public Map WithPiece(CellCoord cell, int edge, string name, LineOfFirePieceKind kind, uint flags = 0)
        {
            if (!Pieces.TryGetValue((cell, edge), out var list)) Pieces[(cell, edge)] = list = [];
            list.Add(new(name, kind, flags));
            return this;
        }

        public Map WithQuiet(CellCoord cell)
        {
            Quiet.Add(cell);
            return this;
        }
    }

    // The observer stands at (10, 10) facing +y (rotation 3); targets lie along +y, so every step of the line is an edge step.
    private static readonly CellCoord Observer = new(10, 10);
    private static CellCoord Ahead(int tiles) => new(10, 10 + tiles);
    private static readonly SightFacts Eyes = new() { Perception = 8, Rotation = 3 };
    private static readonly HearingFacts Ears = new() { Perception = 8 };

    private static AwarenessResult See(Map map, SightFacts facts, CellCoord target, CombatAnomalyProfile? profile = null) =>
        AwarenessRules.See(facts, Observer, 0, 0, target, map, profile ?? Steamwoven);

    private static AwarenessResult Hear(Map map, HearingFacts facts, CellCoord target, Loudness loudness = Loudness.Normal) =>
        AwarenessRules.Hear(facts, Observer, 0, 0, target, loudness, map, Steamwoven);

    // The first tile the line from the observer to the target enters, and the edge it crosses there.
    private static LineWalkStep FirstStep(CellCoord target) => LineOfFireRules.Walk(Observer, 0, 0, target, _ => true)![0];

    // ARC-COMBAT-1930 (0x4B2810): no weapon object is normal; weapon flag 0x1 is loud, flag 0x2 silent (loud first), other bits change nothing.
    [TestMethod]
    public void WeaponLoudnessReadsTheLoudAndSilentFlags()
    {
        Assert.AreEqual(Loudness.Normal, AwarenessRules.WeaponLoudness(null));
        Assert.AreEqual(Loudness.Normal, AwarenessRules.WeaponLoudness(0));
        Assert.AreEqual(Loudness.Loud, AwarenessRules.WeaponLoudness(0x1));
        Assert.AreEqual(Loudness.Silent, AwarenessRules.WeaponLoudness(0x2));
        Assert.AreEqual(Loudness.Loud, AwarenessRules.WeaponLoudness(0x3));
        Assert.AreEqual(Loudness.Normal, AwarenessRules.WeaponLoudness(0x4 | 0x10 | 0x100));
        Assert.AreEqual(Loudness.Silent, AwarenessRules.WeaponLoudness(0xF0000002));
    }

    [TestMethod]
    public void PerceptionDistanceClampsToOneThroughTen()
    {
        // Study section 1: Perception 8 → 8 for sight, 4 for hearing, 3 for the item search; 14 → 10, 10, 9.
        Assert.AreEqual((8, 4, 3), (AwarenessRules.PerceptionDistance(8), AwarenessRules.PerceptionDistance(8 - 4), AwarenessRules.PerceptionDistance(8 - 5)));
        Assert.AreEqual((10, 10, 9), (AwarenessRules.PerceptionDistance(14), AwarenessRules.PerceptionDistance(14 - 4), AwarenessRules.PerceptionDistance(14 - 5)));
        Assert.AreEqual((1, 1), (AwarenessRules.PerceptionDistance(0), AwarenessRules.PerceptionDistance(int.MinValue)));
        Assert.AreEqual((2, 8, 15), (AwarenessRules.NoiseRadius(Loudness.Silent), AwarenessRules.NoiseRadius(Loudness.Normal), AwarenessRules.NoiseRadius(Loudness.Loud)));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => AwarenessRules.NoiseRadius((Loudness)3));
    }

    [TestMethod]
    public void AnObserverFacesFiveOfEightDirectionsAndTheSameTile()
    {
        // The target at +y is direction 3; the observer's rotation r faces it unless (3 − r + 8) mod 8 is 3, 4 or 5 (r = 0, 7, 6).
        var faced = Enumerable.Range(0, 8).Where(r => AwarenessRules.Faces(Observer, r, Ahead(4))).ToArray();
        CollectionAssert.AreEqual(new[] { 1, 2, 3, 4, 5 }, faced);
        // The same tile is direction 5: faced unless the rotation is 0, 1 or 2.
        CollectionAssert.AreEqual(new[] { 3, 4, 5, 6, 7 }, Enumerable.Range(0, 8).Where(r => AwarenessRules.Faces(Observer, r, Observer)).ToArray());
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => AwarenessRules.Faces(Observer, 8, Ahead(1)));
    }

    [TestMethod]
    public void SightRefusesStatesInvisibilityAndTheBackWithAThousand()
    {
        var map = new Map();
        foreach (var facts in new[] { Eyes with { Stunned = true }, Eyes with { Blinded = true }, Eyes with { Sleeping = true }, Eyes with { Unconscious = true } })
            Assert.AreEqual(new AwarenessResult(1000, AwarenessReason.State), See(map, facts, Ahead(1)));
        Assert.AreEqual(new AwarenessResult(1000, AwarenessReason.Invisible), See(map, Eyes with { TargetInvisible = true }, Ahead(1)));
        Assert.AreEqual(new AwarenessResult(0, AwarenessReason.Perceived), See(map, Eyes with { TargetInvisible = true, ExtraordinaryPerception = true }, Ahead(1)));
        Assert.AreEqual(new AwarenessResult(0, AwarenessReason.Perceived), See(map, Eyes with { TargetInvisible = true, DetectsInvisible = true }, Ahead(1)));
        Assert.AreEqual(new AwarenessResult(1000, AwarenessReason.Facing), See(map, Eyes with { Rotation = 7 }, Ahead(1)));
        // More than 1000 tiles: refused before the line is traced.
        var far = new Map(3000);
        Assert.AreEqual(new AwarenessResult(1000, AwarenessReason.Distance), AwarenessRules.See(Eyes, Observer, 0, 0, new(10, 1011), far, Steamwoven));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => See(map, Eyes with { ProwlingMargin = 101 }, Ahead(1)));
    }

    [TestMethod]
    public void SightIsTheExcessOverThePerceptionDistancePlusOneForABlocker()
    {
        var map = new Map();
        // Study section 2: Perception 8, facing, nothing on the line: seen at 6, out by 4 at 12.
        Assert.AreEqual(new AwarenessResult(0, AwarenessReason.Perceived), See(map, Eyes, Ahead(6)));
        Assert.AreEqual(new AwarenessResult(4, AwarenessReason.Range), See(map, Eyes, Ahead(12)));
        // A blocker adds 1, also within range; the cover of a see-through object is discarded.
        var wall = new Map().With(Ahead(2), "crate", Opaque);
        Assert.AreEqual(new AwarenessResult(1, AwarenessReason.Blocked), See(wall, Eyes, Ahead(4)));
        Assert.AreEqual(new AwarenessResult(5, AwarenessReason.Blocked), See(wall, Eyes, Ahead(12)));
        var glass = new Map().With(Ahead(2), "fence", Cover).With(Ahead(3), "hedge", Cover);
        Assert.AreEqual(new AwarenessResult(0, AwarenessReason.Perceived), See(glass, Eyes, Ahead(4)));
    }

    [TestMethod]
    public void AnExpertProwlerTakesItsMarginFromTheSharedSkillCheck()
    {
        // Study section 2: an expert prowler of level 12 against Perception 8 behind one see-through cover object: E = 65, D = −20,
        // M = 85, P = 2 → seen at 2, out by 2 at 4. Behind a closed door: D = −100, M = 100, P = 0 → R = 1, and the door adds 1.
        var cover = new Map().With(Ahead(1), "fence", Cover);
        Assert.AreEqual(85, SightMargin(cover, Ahead(2)));
        Assert.AreEqual(new AwarenessResult(0, AwarenessReason.Perceived), See(cover, Eyes with { ProwlingMargin = SightMargin(cover, Ahead(2)) }, Ahead(2)));
        Assert.AreEqual(new AwarenessResult(2, AwarenessReason.Range), See(cover, Eyes with { ProwlingMargin = SightMargin(cover, Ahead(4)) }, Ahead(4)));

        var step = FirstStep(Ahead(2));
        var door = new Map().WithPiece(step.From, step.Rotation, "door", LineOfFirePieceKind.ClosedPortal, Opaque);
        Assert.AreEqual(100, SightMargin(door, Ahead(2)));
        Assert.AreEqual(new AwarenessResult(2, AwarenessReason.Blocked), See(door, Eyes with { ProwlingMargin = 100 }, Ahead(2)));
    }

    // The margin the shared check gives the prowler over the observer: effectiveness on the opposed table, the sight difficulty with
    // the line traced from the observer to the prowler's tile.
    private static int SightMargin(Map map, CellCoord prowler)
    {
        var effectiveness = SkillCheckRules.Effectiveness(Skill.Prowling, 12, new SkillEffectivenessFacts { OpponentValue = Eyes.Perception });
        Assert.AreEqual(65, effectiveness);
        var line = LineOfFireRules.Trace(Observer, 0, 0, prowler, map, Steamwoven);
        var facts = AwarenessRules.WithProwlerLine(new SkillDifficultyFacts { Target = true, TargetCritter = true, SeeingCheck = true }, line);
        var difficulty = SkillCheckRules.Difficulty(Skill.Prowling, SkillTraining.Expert, effectiveness, facts);
        return StealthRules.Margin(effectiveness, difficulty.Total);
    }

    [TestMethod]
    public void HearingRangeFollowsThePerceptionTable()
    {
        // Study section 3, the range before the sound cost: P ≤ 5 → 0/3/7, 8 → 2/5/8, 10 → 3/6/9, ≥ 14 → 5/8/11.
        var map = new Map();
        foreach (var (perception, ranges) in new[] { (5, new[] { 0, 3, 7 }), (8, new[] { 2, 5, 8 }), (10, new[] { 3, 6, 9 }), (14, new[] { 5, 8, 11 }) })
            for (var loudness = Loudness.Silent; loudness <= Loudness.Loud; loudness++)
            {
                var range = ranges[(int)loudness];
                var facts = Ears with { Perception = perception };
                Assert.IsTrue(Hear(map, facts, Ahead(range), loudness).Perceived, $"P {perception} {loudness} at {range}");
                Assert.AreEqual(new AwarenessResult(1, AwarenessReason.Range), Hear(map, facts, Ahead(range + 1), loudness), $"P {perception} {loudness}");
            }
        // The same tile is always heard (range 0, no sound cost).
        Assert.IsTrue(Hear(map, Ears with { Perception = 1 }, Observer, Loudness.Silent).Perceived);
    }

    [TestMethod]
    public void HearingIgnoresFacingBlindnessAndInvisibilityButNotStunOrSleep()
    {
        var map = new Map();
        Assert.AreEqual(new AwarenessResult(1000, AwarenessReason.State), Hear(map, Ears with { Stunned = true }, Ahead(1)));
        Assert.AreEqual(new AwarenessResult(1000, AwarenessReason.State), Hear(map, Ears with { Unconscious = true }, Ahead(1)));
        // Behind the observer (a hearing check has no facing): heard at 5 with Perception 8.
        Assert.IsTrue(AwarenessRules.Hear(Ears, Observer, 0, 0, new(10, 5), Loudness.Normal, map, Steamwoven).Perceived);
        // Asleep: Perception 4 → normal 3, loud 7.
        Assert.IsTrue(Hear(map, Ears with { Sleeping = true }, Ahead(3)).Perceived);
        Assert.AreEqual(1, Hear(map, Ears with { Sleeping = true }, Ahead(4)).Excess);
        Assert.IsTrue(Hear(map, Ears with { Sleeping = true }, Ahead(7), Loudness.Loud).Perceived);
        var far = new Map(3000);
        Assert.AreEqual(new AwarenessResult(1000, AwarenessReason.Distance), AwarenessRules.Hear(Ears, Observer, 0, 0, new(10, 1011), Loudness.Loud, far, Steamwoven));
    }

    [TestMethod]
    public void AClosedDoorTakesOneOffHearingAndAMovingSilentlyProwlerItsMargin()
    {
        // Study section 3: Perception 8 with a closed door on the line: normal 4 (heard at 4, out by 2 at 6), loud 7, silent 1.
        var step = FirstStep(Ahead(4));
        var door = new Map().WithPiece(step.From, step.Rotation, "door", LineOfFirePieceKind.ClosedPortal, Opaque);
        Assert.IsTrue(Hear(door, Ears, Ahead(4)).Perceived);
        Assert.AreEqual(new AwarenessResult(2, AwarenessReason.Range), Hear(door, Ears, Ahead(6)));
        Assert.IsTrue(Hear(door, Ears, Ahead(7), Loudness.Loud).Perceived);
        Assert.AreEqual(1, Hear(door, Ears, Ahead(8), Loudness.Loud).Excess);
        Assert.IsTrue(Hear(door, Ears, Ahead(1), Loudness.Silent).Perceived);
        Assert.AreEqual(1, Hear(door, Ears, Ahead(2), Loudness.Silent).Excess);

        // An expert prowler of level 12 in armour with a silent-move adjustment of −10 (halved: difficulty +5): M = 65 − 5 = 60,
        // P = 8 − 4 = 4 → normal range 3.
        var effectiveness = SkillCheckRules.Effectiveness(Skill.Prowling, 12, new SkillEffectivenessFacts { OpponentValue = 8 });
        var difficulty = SkillCheckRules.Difficulty(Skill.Prowling, SkillTraining.Expert, effectiveness,
            new SkillDifficultyFacts { Target = true, TargetCritter = true, HearingCheck = true, ArmorSilentMoves = [-10] });
        var margin = StealthRules.Margin(effectiveness, difficulty.Total);
        Assert.AreEqual(60, margin);
        var open = new Map();
        Assert.IsTrue(Hear(open, Ears with { ProwlingMargin = margin }, Ahead(3)).Perceived);
        Assert.AreEqual(1, Hear(open, Ears with { ProwlingMargin = margin }, Ahead(4)).Excess);
    }

    [TestMethod]
    public void TheSoundLinePricesWallsDoorsAndSoundproofTiles()
    {
        var target = Ahead(3);
        var steps = LineOfFireRules.Walk(Observer, 0, 0, target, _ => true)!;
        Assert.AreEqual(3, steps.Count);
        Assert.IsTrue(steps.All(s => s.Rotation == 3), "edge steps along +y");
        Assert.AreEqual(0, AwarenessRules.SoundCost(Observer, 0, 0, Observer, new Map(), Steamwoven), "same tile");
        Assert.AreEqual(0, AwarenessRules.SoundCost(Observer, 0, 0, target, new Map(), Steamwoven), "open line");

        // Study section 4: one wall edge, then a soundproof tile entered by an edge step: 2 + 8 = 10.
        var map = new Map().WithPiece(steps[0].From, 3, "wall", LineOfFirePieceKind.Solid).WithQuiet(steps[1].To);
        Assert.AreEqual(10, AwarenessRules.SoundCost(Observer, 0, 0, target, map, Steamwoven));
        // Every wall piece costs 2 on a sound line, also a window or doorway piece and a wall on the destination's opposite edge;
        // a closed door 1, an open or broken one nothing; objects never cost or block.
        var pieces = new Map()
            .WithPiece(steps[0].From, 3, "window", LineOfFirePieceKind.Window)
            .WithPiece(steps[0].To, 7, "doorway", LineOfFirePieceKind.Doorway)
            .WithPiece(steps[1].From, 3, "door", LineOfFirePieceKind.ClosedPortal)
            .WithPiece(steps[1].To, 7, "open door", LineOfFirePieceKind.OpenPortal)
            .WithPiece(steps[2].From, 3, "passwall", LineOfFirePieceKind.PassWalled, Cover)
            .With(steps[2].To, "boulder", Opaque);
        Assert.AreEqual(2 + 2 + 1 + 0 + 2, AwarenessRules.SoundCost(Observer, 0, 0, target, pieces, Steamwoven));
        // Normal hearing at Perception 8 (5) less a cost of 10 is negative: not heard two tiles away.
        Assert.AreEqual(new AwarenessResult(7, AwarenessReason.Range), Hear(map, Ears, Ahead(2)));
    }

    [TestMethod]
    public void ASoundLineTooLongCostsAHundredAndAStepOffTheMapOne()
    {
        // About 25 tiles along a tile axis exceed 1000 pixels (AW17).
        var far = new Map(200);
        Assert.IsNull(LineOfFireRules.Walk(Observer, 0, 0, new(10, 40), far.Contains));
        Assert.AreEqual(100, AwarenessRules.SoundCost(Observer, 0, 0, new(10, 40), far, Steamwoven));
        // A step whose neighbour is off the map costs 1, dropping what the source tile's walls added.
        var edge = new Map(11).WithPiece(Observer, 3, "wall", LineOfFirePieceKind.Solid);
        Assert.AreEqual(1, AwarenessRules.SoundStepCost(Observer, 3, edge, Steamwoven));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => AwarenessRules.SoundStepCost(Observer, 8, edge, Steamwoven));
    }

    [TestMethod]
    public void ACornerStepCountsTheSoundproofCornerTileOnceOrTwiceInTheOriginalSetting()
    {
        // Rotation 4 from (10, 10): side tiles (10, 11) by rotation 3 and (11, 10) by rotation 5, corner tile (11, 11).
        var corner = new Map().WithQuiet(new(11, 11));
        Assert.AreEqual(8, AwarenessRules.SoundStepCost(Observer, 4, corner, Steamwoven));
        Assert.AreEqual(16, AwarenessRules.SoundStepCost(Observer, 4, corner, Original), "AW13: both routes");
        // A side tile and every crossed edge count once in both settings.
        var side = new Map().WithQuiet(new(10, 11)).WithPiece(new(11, 10), 3, "wall", LineOfFirePieceKind.Solid);
        Assert.AreEqual(10, AwarenessRules.SoundStepCost(Observer, 4, side, Steamwoven));
        Assert.AreEqual(10, AwarenessRules.SoundStepCost(Observer, 4, side, Original));
        // A side tile off the map: 1, the clockwise one from (0, 5) and the counter-clockwise one from (5, 0), whatever the walls add.
        Assert.AreEqual(1, AwarenessRules.SoundStepCost(new(0, 5), 0, new Map(), Steamwoven));
        var walled = new Map().WithPiece(new(5, 0), 1, "wall", LineOfFirePieceKind.Solid).WithQuiet(new(4, 0));
        Assert.AreEqual(1, AwarenessRules.SoundStepCost(new(5, 0), 0, walled, Steamwoven));
    }

    [TestMethod]
    public void TheObserversPixelOffsetShiftsTheLine()
    {
        // AW18: a pixel offset of one tile step in +x (−40, +20 on screen) moves the walk off the tiles straight ahead, past the crates
        // on them; the same offset with its axes swapped walks other tiles.
        var crates = new Map().With(Ahead(1), "crate", Opaque).With(Ahead(2), "crate", Opaque);
        Assert.AreEqual(new AwarenessResult(1, AwarenessReason.Blocked), See(crates, Eyes, Ahead(3)));
        var shifted = LineOfFireRules.Walk(Observer, -40, 20, Ahead(3), crates.Contains)!;
        Assert.IsTrue(shifted.Count > 0 && shifted.All(s => s.To.X == 11), string.Join(" ", shifted));
        Assert.IsFalse(LineOfFireRules.Walk(Observer, 20, -40, Ahead(3), crates.Contains)!.All(s => s.To.X == 11));
        Assert.AreEqual(new AwarenessResult(0, AwarenessReason.Perceived), AwarenessRules.See(Eyes, Observer, -40, 20, Ahead(3), crates, Steamwoven));
        var walls = new Map().WithPiece(Observer, 3, "wall", LineOfFirePieceKind.Solid);
        Assert.AreEqual(2, AwarenessRules.SoundCost(Observer, 0, 0, Ahead(3), walls, Steamwoven));
        Assert.AreEqual(shifted.Sum(s => AwarenessRules.SoundStepCost(s.From, s.Rotation, walls, Steamwoven)),
            AwarenessRules.SoundCost(Observer, -40, 20, Ahead(3), walls, Steamwoven));
    }

    [TestMethod]
    public void APerceptionOutsideZeroToAThousandIsRefused()
    {
        var map = new Map();
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => See(map, Eyes with { Perception = -1 }, Ahead(1)));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => Hear(map, Ears with { Perception = 1001 }, Ahead(1)));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => Hear(map, Ears with { Perception = int.MinValue }, Ahead(1)));
        Assert.AreEqual(new AwarenessResult(0, AwarenessReason.Perceived), Hear(map, Ears with { Perception = 0 }, Observer, Loudness.Silent));
    }

    [TestMethod]
    public void AClearShotNeedsCoverBelowTwentySixAndNoOtherBlocker()
    {
        var target = Ahead(4);
        var one = LineOfFireRules.Trace(Observer, 0, 0, target, new Map().With(Ahead(2), "fence", Cover), Steamwoven);
        var two = LineOfFireRules.Trace(Observer, 0, 0, target, new Map().With(Ahead(1), "fence", Cover).With(Ahead(2), "hedge", Cover), Steamwoven);
        var crate = LineOfFireRules.Trace(Observer, 0, 0, target, new Map().With(Ahead(2), "crate", Opaque), Steamwoven);
        Assert.AreEqual((20, 40), (one.Cost, two.Cost));
        Assert.IsTrue(LineOfFireRules.ClearShot(one, "target"));
        Assert.IsFalse(LineOfFireRules.ClearShot(two, "target"));
        Assert.IsFalse(LineOfFireRules.ClearShot(crate, "target"));
        Assert.IsTrue(LineOfFireRules.ClearShot(crate, "crate"), "the target itself may stop the line");
    }

    /// <summary>
    /// FR-AI-002 acceptance for slice I2: one row per state, invisibility, facing, distance, blocker, concealment and loudness case;
    /// each gives its sight and hearing result once. The transitions these results feed (target choice, witnesses, the danger
    /// setter) are slices I3–I4.
    /// </summary>
    [TestMethod]
    public void TheSightHearingAndConcealmentMatrix()
    {
        var open = new Map();
        var crate = new Map().With(Ahead(2), "crate", Opaque);
        var step = FirstStep(Ahead(3));
        var door = new Map().WithPiece(step.From, step.Rotation, "door", LineOfFirePieceKind.ClosedPortal, Opaque);
        var rows = new (string Case, Map Map, SightFacts Sight, HearingFacts Hearing, int Tiles, Loudness Loudness, AwarenessResult Seen, AwarenessResult Heard)[]
        {
            ("awake, open, near", open, Eyes, Ears, 3, Loudness.Normal, new(0, AwarenessReason.Perceived), new(0, AwarenessReason.Perceived)),
            ("awake, open, at sight range", open, Eyes, Ears, 8, Loudness.Normal, new(0, AwarenessReason.Perceived), new(3, AwarenessReason.Range)),
            ("awake, open, beyond both", open, Eyes, Ears, 9, Loudness.Loud, new(1, AwarenessReason.Range), new(1, AwarenessReason.Range)),
            ("loud weapon heard where sight ends", open, Eyes, Ears, 8, Loudness.Loud, new(0, AwarenessReason.Perceived), new(0, AwarenessReason.Perceived)),
            ("silent weapon", open, Eyes, Ears, 3, Loudness.Silent, new(0, AwarenessReason.Perceived), new(1, AwarenessReason.Range)),
            ("crate blocks sight, not sound", crate, Eyes, Ears, 3, Loudness.Normal, new(1, AwarenessReason.Blocked), new(0, AwarenessReason.Perceived)),
            ("closed door", door, Eyes, Ears, 3, Loudness.Normal, new(1, AwarenessReason.Blocked), new(0, AwarenessReason.Perceived)),
            ("back turned", open, Eyes with { Rotation = 7 }, Ears, 3, Loudness.Normal, new(1000, AwarenessReason.Facing), new(0, AwarenessReason.Perceived)),
            ("blinded", open, Eyes with { Blinded = true }, Ears, 3, Loudness.Normal, new(1000, AwarenessReason.State), new(0, AwarenessReason.Perceived)),
            ("asleep", open, Eyes with { Sleeping = true }, Ears with { Sleeping = true }, 3, Loudness.Normal, new(1000, AwarenessReason.State), new(0, AwarenessReason.Perceived)),
            ("asleep, quiet", open, Eyes with { Sleeping = true }, Ears with { Sleeping = true }, 1, Loudness.Silent, new(1000, AwarenessReason.State), new(1, AwarenessReason.Range)),
            ("stunned", open, Eyes with { Stunned = true }, Ears with { Stunned = true }, 1, Loudness.Loud, new(1000, AwarenessReason.State), new(1000, AwarenessReason.State)),
            ("unconscious", open, Eyes with { Unconscious = true }, Ears with { Unconscious = true }, 1, Loudness.Loud, new(1000, AwarenessReason.State), new(1000, AwarenessReason.State)),
            ("invisible target", open, Eyes with { TargetInvisible = true }, Ears, 3, Loudness.Normal, new(1000, AwarenessReason.Invisible), new(0, AwarenessReason.Perceived)),
            ("invisible, detected", open, Eyes with { TargetInvisible = true, DetectsInvisible = true }, Ears, 3, Loudness.Normal, new(0, AwarenessReason.Perceived), new(0, AwarenessReason.Perceived)),
            ("concealed, margin 50", open, Eyes with { ProwlingMargin = 50 }, Ears with { ProwlingMargin = 50 }, 4, Loudness.Normal, new(0, AwarenessReason.Perceived), new(1, AwarenessReason.Range)),
            ("concealed, margin 50, farther", open, Eyes with { ProwlingMargin = 50 }, Ears with { ProwlingMargin = 50 }, 5, Loudness.Normal, new(1, AwarenessReason.Range), new(2, AwarenessReason.Range)),
            ("concealed, full margin", open, Eyes with { ProwlingMargin = 100 }, Ears with { ProwlingMargin = 100 }, 2, Loudness.Normal, new(1, AwarenessReason.Range), new(0, AwarenessReason.Perceived)),
            ("concealed, full margin, silent", open, Eyes with { ProwlingMargin = 100 }, Ears with { ProwlingMargin = 100 }, 1, Loudness.Silent, new(0, AwarenessReason.Perceived), new(1, AwarenessReason.Range)),
        };
        foreach (var row in rows)
        {
            Assert.AreEqual(row.Seen, See(row.Map, row.Sight, Ahead(row.Tiles)), $"{row.Case}: sight");
            Assert.AreEqual(row.Heard, Hear(row.Map, row.Hearing, Ahead(row.Tiles), row.Loudness), $"{row.Case}: hearing");
        }
    }
}
