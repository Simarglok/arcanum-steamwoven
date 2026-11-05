using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.HeroStateTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

/// <summary>ARC-DEV-098 (MIG8, GR29, TM9; SA13, SA14): the session keeps the 2000 global variables and the hero's flags and variables, saves them in the
/// <c>scripts</c> section (schema 67) and refuses what the original's stores cannot hold; the 66 to 67 step adds the empty section.</summary>
[TestClass]
public sealed class ScriptStoreStateTests
{
    private static GameSessionOwner Hero()
    {
        var result = GameSessionOwner.Create(Descriptor() with { Hero = HalfElf() });
        Assert.AreEqual(GameSessionFailure.None, result.Failure);
        return result.Owner!;
    }

    [TestMethod]
    public void AGlobalVariableIsZeroUntilWrittenAndZeroAgainRemovesIt()
    {
        using var owner = Create();
        Assert.AreEqual(0, owner.QueryGlobalVariable(0));
        Assert.AreEqual(0, owner.QueryGlobalVariable(1999));
        Changed(owner.SetGlobalVariable(owner.Version, 1999, -7), owner, 1);
        Changed(owner.SetGlobalVariable(owner.Version, 0, int.MaxValue), owner, 2);
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.SetGlobalVariable(owner.Version, 0, int.MaxValue).Disposition, "the same value changes nothing");
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.SetGlobalVariable(owner.Version, 5, 0).Disposition, "a variable never written is already zero");
        Assert.AreEqual(2, owner.Version.Revision);
        Assert.AreEqual((int.MaxValue, 0, -7), (owner.QueryGlobalVariable(0), owner.QueryGlobalVariable(1), owner.QueryGlobalVariable(1999)));
        CollectionAssert.AreEqual(new[] { new SessionScriptCell(0, int.MaxValue), new SessionScriptCell(1999, -7) }, owner.QueryScriptState().GlobalVariables.ToArray());
        Changed(owner.SetGlobalVariable(owner.Version, 0, 0), owner, 3);
        CollectionAssert.AreEqual(new[] { new SessionScriptCell(1999, -7) }, owner.QueryScriptState().GlobalVariables.ToArray(), "a zero is not stored");
    }

    [TestMethod]
    public void AnIndexOutsideTheStoreIsRefusedAndAStaleVersionToo()
    {
        using var owner = Create();
        var stale = owner.Version;
        Changed(owner.SetGlobalVariable(stale, 3, 1), owner, 1);
        Rejected(owner.SetGlobalVariable(stale, 4, 1), owner, GameSessionFailure.StaleVersion, 1);
        Rejected(owner.SetGlobalVariable(owner.Version, 2000, 1), owner, GameSessionFailure.InvalidScriptIndex, 1);
        Rejected(owner.SetGlobalVariable(owner.Version, -1, 1), owner, GameSessionFailure.InvalidScriptIndex, 1);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => owner.QueryGlobalVariable(2000));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => owner.QueryGlobalVariable(-1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => owner.QueryPcVariable(65536));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => owner.QueryPcFlag(-1));
        using var hero = Hero();
        Rejected(hero.SetPcVariable(hero.Version, 65536, 1), hero, GameSessionFailure.InvalidScriptIndex, 0);
        Rejected(hero.SetPcFlag(hero.Version, 65536, true), hero, GameSessionFailure.InvalidScriptIndex, 0);
    }

    [TestMethod]
    public void ThePcStoresBelongToTheHeroAndAreZeroWithout()
    {
        using var none = Create();
        Assert.AreEqual((0, false), (none.QueryPcVariable(7), none.QueryPcFlag(7)), "an object that is not a PC reads 0");
        Rejected(none.SetPcVariable(none.Version, 7, 1), none, GameSessionFailure.PcScriptsUnavailable, 0);
        Rejected(none.SetPcFlag(none.Version, 7, true), none, GameSessionFailure.PcScriptsUnavailable, 0);
        Assert.IsTrue(none.QueryScriptState().IsEmpty);

        using var owner = Hero();
        Changed(owner.SetPcVariable(owner.Version, 65535, 12), owner, 1);
        Changed(owner.SetPcVariable(owner.Version, 0, -1), owner, 2);
        Assert.AreEqual((12, -1, 0), (owner.QueryPcVariable(65535), owner.QueryPcVariable(0), owner.QueryPcVariable(1)));
        CollectionAssert.AreEqual(new[] { new SessionScriptCell(0, -1), new SessionScriptCell(65535, 12) }, owner.QueryScriptState().PcVariables.ToArray());
    }

    [TestMethod]
    public void APcFlagIsABitOfAThirtyTwoBitElementAndItsNeighboursStay()
    {
        using var owner = Hero();
        Changed(owner.SetPcFlag(owner.Version, 0, true), owner, 1);
        Changed(owner.SetPcFlag(owner.Version, 31, true), owner, 2);
        Changed(owner.SetPcFlag(owner.Version, 32, true), owner, 3);
        Changed(owner.SetPcFlag(owner.Version, 65535, true), owner, 4);
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.SetPcFlag(owner.Version, 31, true).Disposition);
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.SetPcFlag(owner.Version, 30, false).Disposition, "clearing a clear flag changes nothing");
        Assert.IsTrue(new[] { 0, 31, 32, 65535 }.All(owner.QueryPcFlag));
        Assert.IsFalse(new[] { 1, 30, 33, 65534 }.Any(owner.QueryPcFlag));
        CollectionAssert.AreEqual(new[] { new SessionScriptCell(0, int.MinValue | 1), new SessionScriptCell(1, 1), new SessionScriptCell(2047, int.MinValue) },
            owner.QueryScriptState().PcFlags.ToArray(), "flag n is bit n mod 32 of element n / 32; flag 31 is the sign bit");
        Changed(owner.SetPcFlag(owner.Version, 31, false), owner, 5);
        Changed(owner.SetPcFlag(owner.Version, 0, false), owner, 6);
        CollectionAssert.AreEqual(new[] { new SessionScriptCell(1, 1), new SessionScriptCell(2047, int.MinValue) }, owner.QueryScriptState().PcFlags.ToArray(),
            "an element left with no flag is dropped");
        Assert.AreEqual(0, owner.QueryPcVariable(0), "the flags and the variables are separate arrays");
    }

    [TestMethod]
    public void TheStoresAreSavedInTheScriptsSectionAndRestoreExactly()
    {
        using var owner = Hero();
        Changed(owner.SetGlobalVariable(owner.Version, 1999, -7), owner, 1);
        Changed(owner.SetGlobalVariable(owner.Version, 2, 40), owner, 2);
        Changed(owner.SetPcVariable(owner.Version, 300, 9), owner, 3);
        Changed(owner.SetPcFlag(owner.Version, 33, true), owner, 4);
        Changed(owner.SetPcFlag(owner.Version, 31, true), owner, 5);
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        StringAssert.Contains(text, "\"scripts\":{\"globalVariables\":[[2,40],[1999,-7]],\"pcFlags\":[[0,-2147483648],[1,2]],\"pcVariables\":[[300,9]]}");
        var restored = Restore(Decode(text));
        using var _ = restored;
        Assert.AreEqual(text, GameSessionSnapshotCodec.Encode(restored.Capture()));
        Assert.AreEqual((40, -7, 9, true, true, false), (restored.QueryGlobalVariable(2), restored.QueryGlobalVariable(1999), restored.QueryPcVariable(300),
            restored.QueryPcFlag(33), restored.QueryPcFlag(31), restored.QueryPcFlag(32)));
        Changed(restored.SetPcFlag(restored.Version, 33, false), restored, 6);
    }

    [TestMethod]
    public void ASaveWithNoStoreWritesTheEmptySectionAndASessionWithoutAHeroRefusesAPcStore()
    {
        using var owner = Create();
        StringAssert.Contains(GameSessionSnapshotCodec.Encode(owner.Capture()), NoScripts);
        using var hero = Hero();
        Changed(hero.SetPcVariable(hero.Version, 1, 5), hero, 1);
        var text = GameSessionSnapshotCodec.Encode(hero.Capture());
        // The same PC store in a save with no hero is damage: nothing owns it.
        var noHero = GameSessionSnapshotCodec.Encode(Create().Capture()).Replace(NoScripts, ",\"scripts\":{\"globalVariables\":[],\"pcFlags\":[],\"pcVariables\":[[1,5]]}");
        Assert.AreEqual(GameSessionFailure.InvalidSnapshot, RestoreFailure(noHero));
        // A global variable needs no hero.
        var global = GameSessionSnapshotCodec.Encode(Create().Capture()).Replace(NoScripts, ",\"scripts\":{\"globalVariables\":[[1,5]],\"pcFlags\":[],\"pcVariables\":[]}");
        using var kept = Restore(Decode(global));
        Assert.AreEqual(5, kept.QueryGlobalVariable(1));
        StringAssert.Contains(text, "\"pcVariables\":[[1,5]]");
    }

    [TestMethod]
    public void TheDecodeRefusesAStoreThatIsNotInTheOneCanonicalSpelling()
    {
        using var owner = Create();
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        void Fails(string scripts, SnapshotDecodeFailure failure, string path)
        {
            var result = GameSessionSnapshotCodec.TryDecode(text.Replace(NoScripts, ",\"scripts\":" + scripts));
            Assert.AreEqual((failure, path), (result.Failure, result.Path));
        }
        Fails("{\"globalVariables\":[[2000,1]],\"pcFlags\":[],\"pcVariables\":[]}", SnapshotDecodeFailure.OutOfRange, "$.scripts.globalVariables[0][0]");
        Fails("{\"globalVariables\":[[-1,1]],\"pcFlags\":[],\"pcVariables\":[]}", SnapshotDecodeFailure.OutOfRange, "$.scripts.globalVariables[0][0]");
        Fails("{\"globalVariables\":[],\"pcFlags\":[[2048,1]],\"pcVariables\":[]}", SnapshotDecodeFailure.OutOfRange, "$.scripts.pcFlags[0][0]");
        Fails("{\"globalVariables\":[],\"pcFlags\":[],\"pcVariables\":[[65536,1]]}", SnapshotDecodeFailure.OutOfRange, "$.scripts.pcVariables[0][0]");
        Fails("{\"globalVariables\":[[3,1],[3,2]],\"pcFlags\":[],\"pcVariables\":[]}", SnapshotDecodeFailure.NotCanonical, "$.scripts.globalVariables[1]");
        Fails("{\"globalVariables\":[[4,1],[3,2]],\"pcFlags\":[],\"pcVariables\":[]}", SnapshotDecodeFailure.NotCanonical, "$.scripts.globalVariables[1]");
        Fails("{\"globalVariables\":[[3,0]],\"pcFlags\":[],\"pcVariables\":[]}", SnapshotDecodeFailure.NotCanonical, "$.scripts.globalVariables[0]");
        Fails("{\"globalVariables\":[[3]],\"pcFlags\":[],\"pcVariables\":[]}", SnapshotDecodeFailure.WrongType, "$.scripts.globalVariables[0]");
        Fails("{\"globalVariables\":[[3,1,1]],\"pcFlags\":[],\"pcVariables\":[]}", SnapshotDecodeFailure.BudgetExceeded, "$.scripts.globalVariables[0]");
        Fails("{\"globalVariables\":[[3,2147483648]],\"pcFlags\":[],\"pcVariables\":[]}", SnapshotDecodeFailure.OutOfRange, "$.scripts.globalVariables[0][1]");
        Fails("{\"globalVariables\":[[3,1.5]],\"pcFlags\":[],\"pcVariables\":[]}", SnapshotDecodeFailure.OutOfRange, "$.scripts.globalVariables[0][1]");
        Fails("{\"globalVariables\":[],\"pcFlags\":[]}", SnapshotDecodeFailure.MissingField, "$.scripts.pcVariables");
        Fails("{\"globalVariables\":[],\"pcFlags\":[],\"pcVariables\":[],\"storyState\":1}", SnapshotDecodeFailure.UnknownField, "$.scripts.storyState");
        var missing = GameSessionSnapshotCodec.TryDecode(text.Replace(NoScripts, "").Replace(NoResidency, ""));
        Assert.AreEqual((SnapshotDecodeFailure.MissingField, "$.scripts"), (missing.Failure, missing.Path));
        // Elements of every store are bounded by the store: 2000 variables cannot be listed twice over.
        var many = string.Join(",", Enumerable.Range(0, 2001).Select(i => $"[{i},1]"));
        Fails("{\"globalVariables\":[" + many + "],\"pcFlags\":[],\"pcVariables\":[]}", SnapshotDecodeFailure.BudgetExceeded, "$.scripts.globalVariables");
    }

    // MIG8 (ARC-DEV-098, schema 67): a schema 66 save has no scripts section; the step adds the empty one (every store was zero: a script that used one
    // was refused), and the text of a save with no store is otherwise the same.
    [TestMethod]
    public void ASchema66SaveGetsTheEmptyScriptsSection()
    {
        using var owner = Create();
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        var older = SnapshotUpgradeTests.Schema66(text);
        StringAssert.Contains(older, "\"schemaVersion\":66");
        Assert.IsFalse(older.Contains("\"scripts\""));
        var upgraded = Decode(older);
        Assert.AreEqual(GameSessionSnapshot.CurrentSchemaVersion, upgraded.SchemaVersion);
        Assert.IsTrue(upgraded.Scripts.IsEmpty);
        Assert.AreEqual(text, GameSessionSnapshotCodec.Encode(upgraded), "the upgraded save is the schema 67 text it came from");
        using var restored = Restore(upgraded);
        Assert.AreEqual(0, restored.QueryGlobalVariable(10));
        // The step does not repair a damaged save: a schema 66 text that already carries the section is refused, a damaged older one keeps its failure.
        var carries = GameSessionSnapshotCodec.TryDecode(older.Replace(",\"lighting\":", NoScripts + ",\"lighting\":"));
        Assert.AreEqual((SnapshotDecodeFailure.UnknownField, "$.scripts"), (carries.Failure, carries.Path));
    }

    // Restore checks the stores itself, apart from the decoder: a snapshot built by hand is held to the same canonical form.
    [TestMethod]
    public void RestoreRefusesAHandBuiltStoreThatTheDecoderWouldRefuse()
    {
        using var owner = Hero();
        var saved = owner.Capture();
        void Refused(SessionScriptState scripts)
        {
            var snapshot = new GameSessionSnapshot(saved.SchemaVersion, saved.Identity, saved.Revision, saved.Clock, saved.Flags, saved.Actor, saved.Maps.ToArray(),
                saved.Story, saved.Party, saved.Hero, saved.Inventory, saved.Equipment, saved.Durability, saved.Schematics, saved.Effects, saved.Experience,
                saved.Advancement, saved.Random, saved.Life, saved.Reactions, saved.Combat, saved.CombatMode, saved.Concealment, saved.Danger, saved.Events,
                saved.Lighting, saved.Sound, saved.Hotkeys, saved.Objects, scripts);
            Assert.AreEqual(GameSessionFailure.InvalidSnapshot, GameSessionOwner.Restore(snapshot, RestoreOptions()).Failure);
        }
        SessionScriptCell[] None = [];
        Refused(new([new(2000, 1)], None, None));
        Refused(new([new(-1, 1)], None, None));
        Refused(new(None, [new(2048, 1)], None));
        Refused(new(None, None, [new(65536, 1)]));
        Refused(new([new(3, 0)], None, None));
        Refused(new([new(4, 1), new(3, 1)], None, None));
        Refused(new([new(3, 1), new(3, 2)], None, None));
    }

    private static GameSessionFailure RestoreFailure(string text) =>
        GameSessionOwner.Restore(GameSessionSnapshotCodec.TryDecode(text).Snapshot, RestoreOptions()).Failure;
}
