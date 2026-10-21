using Arcanum.Data.Traversal;
using Arcanum.Input;
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

    // ARC-SCRIPT-006: dialog consequences are session state, so the quick-save file carries them.
    [TestMethod]
    public void DialogConsequencesComeBackWithAQuickLoad()
    {
        const string npc = "oid:synthetic-npc";
        using var first = Session();
        var state = new AreaConversationState(first, new(1), [npc], 50);
        Assert.AreEqual(Arcanum.Scripting.Dialog.DialogJoinResult.Accepted,
            state.Execute(new Arcanum.Scripting.Dialog.JoinCommand(AreaConversationState.Npc(0), AreaConversationState.PcParticipant)));
        Assert.AreEqual(Arcanum.Scripting.Dialog.DialogCommandResult.Applied,
            state.Execute(new Arcanum.Scripting.Dialog.QuestStateCommand(AreaConversationState.PcParticipant, 1003, 2, AreaConversationState.Npc(0))));
        Assert.AreEqual(GameSessionDisposition.Changed, first.LearnRumor(first.Version, 1001).Disposition);
        Assert.IsTrue(AreaQuickSave.Save(first, Outside, Slot()).Saved);

        using var second = Session();
        using var owner = AreaQuickSave.Load(Slot(), second, _ => null).Owner ?? throw new AssertFailedException("load");
        var loaded = new AreaConversationState(owner, new(1), [npc], 50);
        Assert.IsTrue(loaded.Follows(0));
        Assert.AreEqual(SessionQuestState.Accepted, owner.QueryQuest(1003)!.Value.State);
        Assert.IsNotNull(owner.QueryRumor(1001));
        Assert.IsFalse(new AreaConversationState(second, new(1), [npc], 50).Follows(0), "the running session is untouched");
    }

    // ARC-WORLD-044: the PC's reputations are saved; the module's table is data and comes from the running session.
    [TestMethod]
    public void ReputationsComeBackWithAQuickLoadUnderTheRunningSessionsTable()
    {
        var catalog = Arcanum.Simulation.Reputation.ReputationCatalog.Create([new(1001, [5, 0, 0], [new(-7, 0, 0)])])!;
        using var first = AreaTransitionCoordinator.CreateSession(Clock(), Guid.NewGuid(), Module, new AreaStepCell(92928, 82560), 1, reputations: catalog).Owner!;
        Assert.AreEqual(GameSessionDisposition.Changed, first.AddReputation(first.Version, 1001).Disposition);
        Assert.IsTrue(AreaQuickSave.Save(first, Outside, Slot()).Saved);
        using var second = AreaTransitionCoordinator.CreateSession(Clock(), Guid.NewGuid(), Module, new AreaStepCell(92928, 82560), 1, reputations: catalog).Owner!;
        using var owner = AreaQuickSave.Load(Slot(), second, _ => null).Owner ?? throw new AssertFailedException("load");
        Assert.IsTrue(owner.HeroHasReputation(1001));
        Assert.AreSame(catalog, owner.Reputations);
        Assert.AreEqual(-7, owner.QueryReputationReaction(0, 0));
    }

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
        Assert.AreSame(Arcanum.Session.GameRules.Steamwoven, owner.Rules);
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
    public void QuickLoadKeepsTheRunningSessionsRuleMode()
    {
        using var first = Session();
        Assert.IsTrue(AreaQuickSave.Save(first, Outside, Slot()).Saved);
        using var second = Session();
        Assert.AreEqual(GameSessionDisposition.Changed, second.SelectRules(Arcanum.Session.RuleMode.Original));
        using var owner = AreaQuickSave.Load(Slot(), second, _ => null).Owner ?? throw new AssertFailedException("load");
        Assert.AreSame(Arcanum.Session.GameRules.Original, owner.Rules, "The rule mode is a setting, not part of the save.");
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

    [TestMethod]
    public void TheKeyGateActsOnAReleaseWhosePressAndReleaseWereBothAllowed()
    {
        var gate = new QuickKeyGate();
        QuickKeyAction Press(WorldKey key, bool allowed = true, bool echo = false) => gate.Route(key, true, echo, allowed);
        QuickKeyAction Release(WorldKey key, bool allowed = true) => gate.Route(key, false, false, allowed);

        Assert.AreEqual(QuickKeyAction.NotQuickKey, Press(WorldKey.F9));
        Assert.AreEqual(QuickKeyAction.NotQuickKey, Release(WorldKey.A));

        Assert.AreEqual(QuickKeyAction.None, Press(WorldKey.F7));
        Assert.AreEqual(QuickKeyAction.None, Press(WorldKey.F7, echo: true), "Echo does nothing and keeps the key armed.");
        Assert.AreEqual(QuickKeyAction.Save, Release(WorldKey.F7));
        Assert.AreEqual(QuickKeyAction.None, Release(WorldKey.F7), "One action per press.");

        Assert.AreEqual(QuickKeyAction.None, Press(WorldKey.F8));
        Assert.AreEqual(QuickKeyAction.Load, Release(WorldKey.F8));

        // Press allowed, release under the palette: ignored, and the next release does nothing.
        Press(WorldKey.F7);
        Assert.AreEqual(QuickKeyAction.Ignored, Release(WorldKey.F7, allowed: false));
        Assert.IsNull(gate.Armed);
        // Press under the palette, release allowed: the press was ignored and the release does nothing.
        Assert.AreEqual(QuickKeyAction.Ignored, Press(WorldKey.F8, allowed: false));
        Assert.AreEqual(QuickKeyAction.None, Release(WorldKey.F8));
        // Interleaved: the later press re-arms for its key; the earlier key's release does nothing.
        Press(WorldKey.F7); Press(WorldKey.F8);
        Assert.AreEqual(QuickKeyAction.None, Release(WorldKey.F7));
        Assert.AreEqual(QuickKeyAction.None, Release(WorldKey.F8), "The F7 release disarmed the gate.");
        // A release without a press (focus came back mid-hold) does nothing; Reset forgets an armed key.
        Assert.AreEqual(QuickKeyAction.None, Release(WorldKey.F8));
        Press(WorldKey.F8); gate.Reset();
        Assert.AreEqual(QuickKeyAction.None, Release(WorldKey.F8));
    }
}
