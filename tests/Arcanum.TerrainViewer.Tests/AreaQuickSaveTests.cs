using Arcanum.Data.Traversal;
using Arcanum.Session;
using Arcanum.Simulation.Clock;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

[TestClass]
public sealed class AreaQuickSaveTests
{
    private static readonly Guid Module = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly ActorPlacement Outside = new(new(1), new(92910, 82598), NeighborDirection.XPlus);
    private string _directory = null!;

    [TestInitialize]
    public void CreateDirectory() => _directory = Directory.CreateTempSubdirectory("arc-quicksave-").FullName;

    [TestCleanup]
    public void DeleteDirectory()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    private static ProfileBinding Clock(string data = "aa") => new(1, AreaAmbientDriver.ClockPolicy, AreaAmbientDriver.ClockSourceRevision,
        string.Concat(Enumerable.Repeat(data, 32)), new string('c', 64), 1885, 15);

    private static GameSessionOwner Session(Guid? module = null, ProfileBinding? clock = null) =>
        AreaTransitionCoordinator.CreateSession(clock ?? Clock(), Guid.NewGuid(), module ?? Module, new AreaStepCell(92928, 82560), 1).Owner
        ?? throw new AssertFailedException("session");

    private GameSessionSaveSlot Slot() => GameSessionSaveSlot.QuickSave(_directory);

    [TestMethod]
    public void ASaveRecordsTheActorAndALaterRunLoadsItIntoANewSession()
    {
        using var first = Session();
        var cave = new ActorPlacement(new(79), new(2090, 2020), NeighborDirection.YMinus);
        var saved = AreaQuickSave.Save(first, cave, Slot());
        Assert.IsTrue(saved.Saved, saved.Status);
        Assert.AreEqual(cave, first.QueryActor(), "The save placed the actor in the session first.");
        Assert.AreEqual("quick saved: map 79 cell (2090,2020), session rev 1", saved.Status);

        // A new run: another world id for its new game, the same module and clock profile.
        using var second = Session();
        Assert.AreNotEqual(first.Flags.Binding.Identity.World, second.Flags.Binding.Identity.World);
        var asked = new List<ActorPlacement>();
        var loaded = AreaQuickSave.Load(Slot(), second, placement => { asked.Add(placement); return null; });
        using var owner = loaded.Owner ?? throw new AssertFailedException(loaded.Status);
        CollectionAssert.AreEqual(new[] { cave }, asked);
        Assert.AreEqual(cave, owner.QueryActor());
        Assert.AreEqual(1, owner.Version.Revision);
        Assert.AreEqual(first.Flags.Binding.Identity, owner.Flags.Binding.Identity, "The load continues the saved playthrough.");
        Assert.AreNotSame(second.Identity, owner.Identity);
        Assert.AreEqual(Outside.Map, second.QueryActor().Map, "The running session is untouched; the caller adopts the new owner.");
        Assert.AreEqual("quick loaded: map 79 cell (2090,2020), session rev 1", loaded.Status);
    }

    [TestMethod]
    public void AnInvalidPlacementIsRefusedAndNothingIsWritten()
    {
        using var session = Session();
        var saved = AreaQuickSave.Save(session, Outside with { Map = new(0) }, Slot());
        Assert.AreEqual((false, GameSessionFailure.InvalidActor), (saved.Saved, saved.PlaceFailure));
        Assert.IsFalse(Slot().Exists);
    }

    [TestMethod]
    public void LoadFailuresLeaveNoOwnerAndSayWhy()
    {
        using var current = Session();
        AreaQuickLoadResult Load(GameSessionOwner on, Func<ActorPlacement, string?>? refusal = null)
        {
            var result = AreaQuickSave.Load(Slot(), on, refusal ?? (_ => null));
            Assert.IsNull(result.Owner, result.Status);
            return result;
        }

        Assert.AreEqual("quick load FAILED: no quick save yet (F7 saves)", Load(current).Status);

        using (var saver = Session()) Assert.IsTrue(AreaQuickSave.Save(saver, Outside, Slot()).Saved);
        var unloaded = Load(current, _ => "map not loaded in this run");
        Assert.AreEqual("quick load REFUSED: saved actor on map 1 cell (92910,82598): map not loaded in this run", unloaded.Status);

        using (var otherModule = Session(Guid.NewGuid()))
        {
            var refused = Load(otherModule);
            Assert.AreEqual(GameSessionFailure.WrongModule, refused.RestoreFailure);
            StringAssert.Contains(refused.Status, "another module");
        }
        using (var otherData = Session(clock: Clock("bb")))
            Assert.AreEqual(GameSessionFailure.IncompatibleClockProfile, Load(otherData).RestoreFailure);

        File.WriteAllText(Slot().Path, "{\"format\":");
        var damaged = Load(current);
        Assert.AreEqual(SaveSlotFailure.Corrupt, damaged.Read!.Failure);
        Assert.AreEqual("quick load FAILED: the save file is damaged (MalformedJson at $)", damaged.Status);
        Assert.AreEqual(0, current.Version.Revision, "No failed load touched the running session.");
    }
}
