using System.Buffers.Binary;
using System.Security.Cryptography;
using Arcanum.Data;
using Arcanum.Data.Area;
using Arcanum.Data.Terrain;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

// ARC-WORLD-111: the walk, run and stand ARTs of a critter walking off its T0 cell (a following companion, an NPC the AI walks),
// their facing groups and mirroring, the cycle frame and its clock, and the AI movers' gait. Archives and ART are synthetic.
[TestClass]
public sealed class AreaBodyMoveTests
{
    [TestMethod]
    public void TheMoveArtsAreTheBodysNameWithTheGaitLetters()
    {
        WithArchive([("art/critter/hmm/hmmuwsba.art", Art(1, 10, 4, 1)), ("art/critter/hmm/hmmuwxbb.art", Art(3, 10, 4, 1)),
            ("art/critter/hmm/hmmuwxag.art", Art(2, 20, 4, 1))], (path, identity) =>
        {
            var moves = AreaBodyMoveArt.Load(Visual(identity(0)), path);
            Assert.AreEqual(AreaBodyMoveStatus.Ready, moves.Status, moves.Detail);
            Assert.AreEqual("art/critter/hmm/hmmuwxbb.art", moves.Walk!.Name, "walk clears the shield, keeps the weapon");
            Assert.AreEqual("art/critter/hmm/hmmuwxag.art", moves.Run!.Name, "run clears shield and weapon");
            Assert.AreEqual("art/critter/hmm/hmmuwsba.art", moves.Stand!.Name, "stand keeps both");
            Assert.AreEqual((1, 6, 0), (moves.Walk.Animation, moves.Run.Animation, moves.Stand.Animation));
            Assert.AreEqual((3, 10, 20), (moves.Walk.FramesPerGroup, moves.Walk.FramesPerSecond, moves.Run.FramesPerSecond));
            Assert.AreSame(moves.Run, moves.For(ActorDiagnosticAction.Run));
            Assert.AreSame(moves.Walk, moves.For(ActorDiagnosticAction.Vault));
            Assert.AreSame(moves.Stand, moves.For(ActorDiagnosticAction.Idle));
            Assert.IsNull(moves.For(ActorDiagnosticAction.Attack));
        });
    }

    [TestMethod]
    public void AMissingRunIsTheWalkAndAMissingArmedWalkTheUnarmedOne()
    {
        WithArchive([("art/critter/hmm/hmmuwsda.art", Art(1, 10, 4, 1)), ("art/critter/hmm/hmmuwxab.art", Art(4, 10, 4, 1))], (path, identity) =>
        {
            var moves = AreaBodyMoveArt.Load(Visual(identity(0)), path);
            Assert.AreEqual(AreaBodyMoveStatus.Ready, moves.Status, moves.Detail);
            Assert.AreEqual("art/critter/hmm/hmmuwxab.art", moves.Walk!.Name);
            Assert.IsNull(moves.Run);
            Assert.AreSame(moves.Walk, moves.For(ActorDiagnosticAction.Run), "the missing-art repair draws the walk");
        });
        WithArchive([("art/critter/hmm/hmmuwsba.art", Art(1, 10, 4, 1)), ("art/critter/hmm/hmmuwxag.art", Art(2, 20, 4, 1))], (path, identity) =>
        {
            var visual = Visual(identity(0));
            Assert.AreEqual(AreaBodyMoveStatus.WalkMissing, AreaBodyMoveArt.Load(visual, path).Status);
            Assert.AreEqual(AreaBodyMoveStatus.EntryMismatch, AreaBodyMoveArt.Load(visual with { Entry = visual.Entry with { Offset = 9 } }, path).Status);
            Assert.AreEqual(AreaBodyMoveStatus.SourceUnavailable, AreaBodyMoveArt.Load(visual, null).Status);
            Assert.AreEqual(AreaBodyMoveStatus.SourceUnavailable, AreaBodyMoveArt.Load(visual, path + ".absent").Status);
            Assert.AreEqual(AreaBodyMoveStatus.NotACritterArt, AreaBodyMoveArt.Load(visual with { RawArt = 0x40000000 }, path).Status);
            Assert.AreEqual(AreaBodyMoveStatus.NotACritterArt, AreaBodyMoveArt.Load(visual with { Policy = "original-badart-v1" }, path).Status);
        });
    }

    [TestMethod]
    public void AFacingDrawsItsGroupAndRotationsOneToThreeMirrorTheHotspot()
    {
        WithArchive([("art/critter/hmm/hmmuwxaa.art", Art(1, 10, 6, 2)), ("art/critter/hmm/hmmuwxab.art", Art(3, 10, 6, 2))], (path, identity) =>
        {
            var walk = AreaBodyMoveArt.Load(Visual(identity(0)), path).Walk!;
            var straight = walk.Pose(5, 4);
            Assert.AreEqual((5, 1, false, 2), (straight.Key.Group, straight.Key.Frame, straight.FlipX, straight.HotX), "frame 4 of a 3-frame cycle is frame 1");
            Assert.AreEqual(AreaBodyMoveArt.Policy, straight.Key.Policy);
            Assert.AreEqual(1, straight.Key.Entry.Ordinal);
            var mirrored = walk.Pose(2, 0);
            Assert.AreEqual((6, 0, true, 6 - 2 - 1), (mirrored.Key.Group, mirrored.Key.Frame, mirrored.FlipX, mirrored.HotX), "rotation 2 is group 6 mirrored");
            Assert.AreEqual((7, true), (walk.Pose(1, 0).Key.Group, walk.Pose(1, 0).FlipX));
            Assert.AreEqual((0, false), (walk.Pose(0, 0).Key.Group, walk.Pose(0, 0).FlipX));
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => walk.Pose(8, 0));
        });
    }

    [TestMethod]
    public void AWalkingBodyIsDrawnWithItsCycleFrameAtItsAnchor()
    {
        WithArchive([("art/critter/hmm/hmmuwxaa.art", Art(1, 10, 6, 2)), ("art/critter/hmm/hmmuwxab.art", Art(4, 10, 6, 2))], (path, identity) =>
        {
            var body = AreaCombatTests.Body();
            var visual = Visual(identity(0));
            var command = AreaScenePlan.Create([], [body], [visual]).Commands.Single();
            var moves = AreaBodyMoveArt.Load(visual, path);
            var anchorX = command.Destination.X + visual.HotX; var anchorY = command.Destination.Y + visual.HotY;

            var (standing, none) = AreaBodyMoveArt.Animate(command, body, visual, moves, ActorDiagnosticAction.Idle, visual.NominalFacing, 0);
            Assert.AreSame(command, standing, "standing with its T0 facing it keeps its T0 frame");
            Assert.IsNull(none);

            // 10 fps: 250 ms in is frame 2.
            var (walking, pose) = AreaBodyMoveArt.Animate(command, body, visual, moves, ActorDiagnosticAction.Walk, 5, 250);
            Assert.AreEqual((2, 5), (pose!.Key.Frame, pose.Key.Group));
            Assert.AreEqual(pose.Key, walking.Frame);
            Assert.AreEqual((anchorX - 2, anchorY - 1, 6, 2), (walking.Destination.X, walking.Destination.Y, walking.Destination.Width, walking.Destination.Height));
            Assert.AreEqual((command.MapX, command.MapY, command.Identity, command.Visual), (walking.MapX, walking.MapY, walking.Identity, walking.Visual));
            Assert.IsFalse(walking.FlipX);

            var (turned, mirror) = AreaBodyMoveArt.Animate(command, body, visual, moves, ActorDiagnosticAction.Walk, 3, 0);
            Assert.AreEqual((5, 0, true), (mirror!.Key.Group, mirror.Key.Frame, turned.FlipX));
            Assert.AreEqual(anchorX - (6 - 2 - 1), turned.Destination.X, "a mirrored frame is placed by its mirrored hotspot");

            var (stood, stand) = AreaBodyMoveArt.Animate(command, body, visual, moves, ActorDiagnosticAction.Idle, 1, 900);
            Assert.AreEqual((0, 0, 7, true), (stand!.Key.Entry.Ordinal, stand.Key.Frame, stand.Key.Group, stood.FlipX), "turned, it stands on frame 0 of its stand ART");

            var refused = moves with { Status = AreaBodyMoveStatus.Unreadable };
            Assert.AreSame(command, AreaBodyMoveArt.Animate(command, body, visual, refused, ActorDiagnosticAction.Walk, 5, 250).Command);

            var (back, again) = AreaBodyMoveArt.Animate(command, body, visual, moves, ActorDiagnosticAction.Idle, visual.NominalFacing, 0, moved: true);
            Assert.AreEqual((0, 5), (again!.Key.Frame, again.Key.Group), "once moved it stands on its stand ART with its T0 facing too");
            Assert.AreEqual(again.Key, back.Frame);

            // The object's own flip (blit bit 1) stays: it flips a straight pose and unflips a mirrored one.
            var flipped = command with { FlipX = true };
            Assert.IsTrue(AreaBodyMoveArt.Animate(flipped, body, visual, moves, ActorDiagnosticAction.Walk, 5, 0).Command.FlipX);
            Assert.IsFalse(AreaBodyMoveArt.Animate(flipped, body, visual, moves, ActorDiagnosticAction.Walk, 2, 0).Command.FlipX);
            var mirroredT0 = visual with { Mirrored = true };
            // The T0 plan draws a mirrored frame flipped; that flip is the frame's, not the object's.
            Assert.IsFalse(AreaBodyMoveArt.Animate(command with { Visual = mirroredT0.Key, FlipX = true }, body, mirroredT0, moves, ActorDiagnosticAction.Walk, 5, 0).Command.FlipX,
                "a mirrored T0 frame is no object flip");
        });
    }

    [TestMethod]
    public void BytesAfterTheLastFrameAreNoPartOfTheArtAndAOneRotationArtMirrors()
    {
        var walk = Art(2, 10, 6, 2, groups: 1);
        WithArchive([("art/critter/hmm/hmmuwxaa.art", Art(1, 10, 6, 2)), ("art/critter/hmm/hmmuwxab.art", [.. walk, 7, 7, 7])], (path, identity) =>
        {
            var moves = AreaBodyMoveArt.Load(Visual(identity(0)), path);
            Assert.AreEqual(AreaBodyMoveStatus.Ready, moves.Status, moves.Detail);
            var straight = moves.Walk!.Pose(5, 1);
            Assert.AreEqual((0, 1, false, walk.Length), (straight.Key.Group, straight.Key.Frame, straight.FlipX, straight.Key.ViewLength));
            var mirrored = moves.Walk.Pose(3, 1);
            Assert.AreEqual((0, true, 6 - 2 - 1), (mirrored.Key.Group, mirrored.FlipX, mirrored.HotX), "rotation 3 mirrors group 0, as the T0 body");
        });
    }

    [TestMethod]
    public void TheGaitClockRunsThroughAWalkAndRestartsOnAChange()
    {
        Assert.AreEqual(0, AreaBodyMoveArt.CycleFrame(0, 17, 10));
        Assert.AreEqual(1, AreaBodyMoveArt.CycleFrame(59, 17, 10));
        Assert.AreEqual(7, AreaBodyMoveArt.CycleFrame(1000, 17, 10), "17 frames a second, a 10-frame cycle");
        Assert.AreEqual(0, AreaBodyMoveArt.CycleFrame(500, 0, 10));
        var cycle = new AreaBodyCycle();
        Assert.AreEqual(0, cycle.Observe(ActorDiagnosticAction.Idle, 100));
        Assert.IsFalse(cycle.Moved);
        Assert.AreEqual(0, cycle.Observe(ActorDiagnosticAction.Walk, 200));
        Assert.AreEqual(350, cycle.Observe(ActorDiagnosticAction.Walk, 550), "the walk's steps share one cycle");
        Assert.AreEqual(0, cycle.Observe(ActorDiagnosticAction.Run, 600), "a new gait restarts it");
        Assert.AreEqual(0, cycle.Observe(ActorDiagnosticAction.Idle, 700));
        Assert.AreEqual(0, cycle.Observe(ActorDiagnosticAction.Run, 800), "a new walk restarts it");
        Assert.AreEqual(0, cycle.Observe(ActorDiagnosticAction.Run, 100), "a clock set back restarts it");
        Assert.AreEqual(50, cycle.Observe(ActorDiagnosticAction.Run, 150));
        Assert.IsTrue(cycle.Moved);
    }

    [TestMethod]
    public void AnAiMoverStepsWithItsGaitUntilTheStepEnds()
    {
        var movers = new AreaAiMovers();
        var key = AreaCombatTests.Body().Key;
        movers.Step("npc", key, new(5, 5), new(6, 5), 1000, 500, running: true);
        var mover = movers.Of("npc")!;
        Assert.IsTrue(mover.Running);
        Assert.AreEqual(NeighborDirection.XPlus, mover.Facing);
        Assert.IsTrue(mover.Stepping(1000) && mover.Stepping(1499));
        Assert.IsFalse(mover.Stepping(1500));
        movers.Settle(1500);
        Assert.IsFalse(movers.Of("npc")!.Stepping(1500));
        movers.Step("npc", key, new(5, 5), new(7, 5), 1500, 500);
        Assert.IsFalse(movers.Of("npc")!.Running, "a walked step");
    }

    private static AreaVisualRecord Visual(AreaEntryIdentity entry) =>
        AreaCombatTests.Visual(AreaCombatTests.Body(), entry, width: 6, height: 2, hotX: 2, hotY: 1);

    private static void WithArchive(IReadOnlyList<(string Name, byte[] Payload)> entries, Action<string, Func<int, AreaEntryIdentity>> test)
    {
        var path = Path.Combine(Path.GetTempPath(), $"arc-world-111-{Guid.NewGuid():N}.dat");
        try
        {
            File.WriteAllBytes(path, AreaCombatTests.Dat(entries));
            AreaEntryIdentity Identity(int ordinal)
            {
                using var archive = DatArchive.Open(path);
                var entry = archive.Entries.Single(e => e.Ordinal == ordinal);
                return new(new(AreaSourceRole.Resource1), entry.Ordinal, entry.Flags, entry.StoredSize, entry.DeclaredOutputSize, entry.Offset,
                    Convert.ToHexString(SHA256.HashData(entry.RawNameBytes.ToArray())).ToLowerInvariant());
            }
            test(path, Identity);
        }
        finally { File.Delete(path); }
    }

    // A minimal 8-bit ART: `groups` (8, or 1 with the one-rotation flag) groups of `frames` width×height frames with hotspot
    // (hotX, height − 1), palette 0 white.
    private static byte[] Art(int frames, int fps, int width, int hotX, int groups = 8)
    {
        const int headerBytes = 132, paletteBytes = 1024, frameHeaderBytes = 28, height = 2;
        var total = groups * frames; var size = width * height;
        var table = headerBytes + paletteBytes;
        var payload = table + total * frameHeaderBytes;
        var bytes = new byte[payload + total * size];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, groups == 1 ? 1u : 0u);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), fps);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8), 8);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), 1);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(32), frames);
        bytes[headerBytes + 4] = 255; bytes[headerBytes + 5] = 255; bytes[headerBytes + 6] = 255; bytes[headerBytes + 7] = 255;
        for (var index = 0; index < total; index++)
        {
            var frame = table + index * frameHeaderBytes;
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(frame), width);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(frame + 4), height);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(frame + 8), size);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(frame + 12), hotX);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(frame + 16), height - 1);
            bytes.AsSpan(payload + index * size, size).Fill(1);
        }
        return bytes;
    }
}
