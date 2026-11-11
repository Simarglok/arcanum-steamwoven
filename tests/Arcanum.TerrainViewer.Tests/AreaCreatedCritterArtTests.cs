using Arcanum.Data.Area;
using Arcanum.Simulation.Movement;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

// ARC-DEV-370 (E1-c2): the body of a created critter from its prototype's art id: the file the original names, found in the window's art archives, its stand pose, facing and mirrored hotspot, and the move
// ARTs next to it. Archives and ART are synthetic.
[TestClass]
public sealed class AreaCreatedCritterArtTests
{
    private const uint HumanFemaleStand = 0x20000000u;
    private static string? Name(AreaMappingRole role, int key) => null;

    private static void WithArchives(IReadOnlyList<IReadOnlyList<(string Name, byte[] Payload)>> archives, Action<IReadOnlyDictionary<AreaInputKey, string>> test)
    {
        var paths = new List<string>();
        try
        {
            foreach (var entries in archives)
            {
                var path = Path.Combine(Path.GetTempPath(), $"arc-dev-370-{Guid.NewGuid():N}.dat");
                File.WriteAllBytes(path, AreaCombatTests.Dat(entries));
                paths.Add(path);
            }
            test(paths.Select((p, i) => (p, i)).ToDictionary(x => new AreaInputKey(x.i == 0 ? AreaSourceRole.Resource1 : AreaSourceRole.Resource2), x => x.p));
        }
        finally { foreach (var path in paths) File.Delete(path); }
    }

    [TestMethod]
    public void TheBodyIsTheNamedFileOfTheFirstArchiveThatHoldsItWithItsStandPose()
    {
        var stand = AreaBodyMoveTests.Art(2, 10, 4, 1);
        WithArchives([[("art/other/x.art", AreaBodyMoveTests.Art(1, 10, 4, 1))], [("art/critter/hmf/hmfuwxaa.art", stand), ("art/critter/hmf/hmfuwxab.art", AreaBodyMoveTests.Art(3, 10, 4, 1))]], sources =>
        {
            var aid = HumanFemaleStand | (4u << 11);
            var load = AreaCreatedCritterArt.Load(aid, sources, Name);
            Assert.AreEqual(AreaCreatedCritterArtStatus.Ready, load.Status, load.Detail);
            var visual = load.Visual!; var pose = load.Pose!;
            Assert.AreEqual(new AreaInputKey(AreaSourceRole.Resource2), visual.Entry.Source, "the second archive holds the file");
            Assert.AreEqual((aid, 4, false, AreaCreatedCritterArt.Policy), (visual.RawArt, visual.NominalFacing, visual.Mirrored, visual.Policy));
            Assert.AreEqual((4, 2, 1, 1, false), (visual.Width, visual.Height, visual.HotX, visual.HotY, pose.FlipX));
            Assert.AreEqual(AreaCreatedCritterArt.VisualKey(aid), visual.Key);
            Assert.AreEqual(AreaFrameKey.From(visual).Entry, pose.Key.Entry);
            // The move ARTs are found next to it through the visual's entry, as for a map NPC's body.
            var moves = AreaBodyMoveArt.Load(visual, sources[visual.Entry.Source]);
            Assert.AreEqual(AreaBodyMoveStatus.Ready, moves.Status, moves.Detail);
            Assert.AreEqual("art/critter/hmf/hmfuwxab.art", moves.Walk!.Name);
        });
    }

    [TestMethod]
    public void AMirroredFacingIsTheUnmirroredGroupFlippedWithTheHotspotMirrored()
    {
        WithArchives([[("art/critter/hmf/hmfuwxaa.art", AreaBodyMoveTests.Art(1, 10, 4, 1))]], sources =>
        {
            var load = AreaCreatedCritterArt.Load(HumanFemaleStand | (1u << 11), sources, Name);
            Assert.AreEqual(AreaCreatedCritterArtStatus.Ready, load.Status, load.Detail);
            Assert.IsTrue(load.Pose!.FlipX);
            Assert.AreEqual(4 - 1 - 1, load.Pose.HotX, "width - hotX - 1");
            Assert.AreEqual(7, load.Pose.Key.Group, "rotation 1 draws group 7");
            Assert.IsTrue(load.Visual!.Mirrored, "the visual is a map NPC's: mirrored for a facing of 1 to 3, with the unmirrored hotspot");
            Assert.AreEqual(1, load.Visual.HotX);
            Assert.AreEqual((1, 7), (load.Visual.NominalFacing, load.Visual.SourceGroup), "source group 8 - 1");
        });
    }

    [TestMethod]
    public void AMissingFileAnotherClassAndAnUnnamedMonsterAreRefusedWithTheirReason()
    {
        WithArchives([[("art/critter/hmf/hmfuwxaa.art", AreaBodyMoveTests.Art(1, 10, 4, 1))]], sources =>
        {
            // A male body: its file is in no archive.
            var missing = AreaCreatedCritterArt.Load((2u << 28) | (1u << 27), sources, Name);
            Assert.AreEqual((AreaCreatedCritterArtStatus.MissingFile, "art/critter/HMM/HMMUWXAa.art"), (missing.Status, missing.Detail));
            Assert.AreEqual(AreaCreatedCritterArtStatus.NotACritterArt, AreaCreatedCritterArt.Load(0x40000000u, sources, Name).Status);
            Assert.AreEqual(AreaCreatedCritterArtStatus.NameUnresolved, AreaCreatedCritterArt.Load((12u << 28) | (9u << 23), sources, Name).Status);
            Assert.AreEqual(AreaCreatedCritterArtStatus.MissingFile, AreaCreatedCritterArt.Load(HumanFemaleStand, new Dictionary<AreaInputKey, string>(), Name).Status, "no archive at all");
        });
    }

    [TestMethod]
    public void AFileHeldTwiceByOneArchiveIsAmbiguousAndNeverChosen()
    {
        WithArchives([[("art/critter/hmf/hmfuwxaa.art", AreaBodyMoveTests.Art(1, 10, 4, 1)), ("art/critter/HMF/hmfuwxaa.art", AreaBodyMoveTests.Art(1, 10, 4, 1))]], sources =>
            Assert.AreEqual(AreaCreatedCritterArtStatus.Unreadable, AreaCreatedCritterArt.Load(HumanFemaleStand, sources, Name).Status));
    }
}
