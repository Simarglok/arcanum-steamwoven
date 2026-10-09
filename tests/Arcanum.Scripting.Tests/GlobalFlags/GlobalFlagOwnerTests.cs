using Arcanum.Scripting.GlobalFlags;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Scripting.Tests.GlobalFlags.GlobalFlagFixture;

namespace Arcanum.Scripting.Tests.GlobalFlags;

[TestClass]
public sealed class GlobalFlagOwnerTests
{
    [TestMethod]
    public void Explicit_bootstrap_known_true_false_and_missing_unknown_are_not_mutating_reads()
    {
        using var owner = Owner();
        Assert.AreEqual(new GlobalFlagReadResult(RequestId, Version(0), new FlagId(3), GlobalFlagReadStatus.Known, false, Version(0), GlobalFlagReason.None),
            owner.Read(new(RequestId, Version(0), new(3))));
        Assert.AreEqual(new GlobalFlagReadResult(RequestId, Version(0), new FlagId(4), GlobalFlagReadStatus.Known, true, Version(0), GlobalFlagReason.None),
            owner.Read(new(RequestId, Version(0), new(4))));
        Assert.AreEqual(new GlobalFlagReadResult(RequestId, Version(0), new FlagId(9), GlobalFlagReadStatus.Unknown, null, Version(0), GlobalFlagReason.MissingBootstrapValue),
            owner.Read(new(RequestId, Version(0), new(9))));
        State(owner, 0, false, true, false, false, false, null);
    }

    [TestMethod]
    public void Changed_false_true_equal_and_unknown_writes_have_literal_receipts_and_whole_state()
    {
        using var owner = Owner();
        Assert.AreEqual(new GlobalFlagWriteReceipt(RequestId, GlobalFlagOperation.Write, Version(0), new FlagId(4), false, Version(1), GlobalFlagWriteDisposition.Applied, GlobalFlagReason.None),
            owner.Write(new(RequestId, Version(0), new(4), false)));
        State(owner, 1, false, false, false, false, false, null);
        Assert.AreEqual(new GlobalFlagWriteReceipt(RequestId, GlobalFlagOperation.Write, Version(1), new FlagId(3), true, Version(2), GlobalFlagWriteDisposition.Applied, GlobalFlagReason.None),
            owner.Write(new(RequestId, Version(1), new(3), true)));
        State(owner, 2, true, false, false, false, false, null);
        Assert.AreEqual(new GlobalFlagWriteReceipt(RequestId, GlobalFlagOperation.Write, Version(2), new FlagId(3), true, Version(2), GlobalFlagWriteDisposition.NoChange, GlobalFlagReason.None),
            owner.Write(new(RequestId, Version(2), new(3), true)));
        State(owner, 2, true, false, false, false, false, null);
        Assert.AreEqual(new GlobalFlagWriteReceipt(RequestId, GlobalFlagOperation.Write, Version(2), new FlagId(9), false, Version(3), GlobalFlagWriteDisposition.Applied, GlobalFlagReason.None),
            owner.Write(new(RequestId, Version(2), new(9), false)));
        State(owner, 3, true, false, false, false, false, false);
    }

    [TestMethod]
    public void Wrong_identity_domain_invalid_defaults_and_stale_versions_reject_atomically()
    {
        using var owner = Owner();
        owner.Write(new(RequestId, Version(0), new(3), true));
        var rows = new (GlobalFlagRequestId Request, GlobalFlagVersion Expected, FlagId Flag, GlobalFlagReason Reason, GlobalFlagReadStatus Status)[]
        {
            (default, Version(1), new(3), GlobalFlagReason.InvalidRequest, GlobalFlagReadStatus.Failed),
            (RequestId, default, new(3), GlobalFlagReason.InvalidIdentity, GlobalFlagReadStatus.Failed),
            (RequestId, Version(1) with { Epoch = default }, new(3), GlobalFlagReason.InvalidEpoch, GlobalFlagReadStatus.Failed),
            (RequestId, Version(-1), new(3), GlobalFlagReason.InvalidRevision, GlobalFlagReadStatus.Failed),
            (RequestId, Version(1), new(-1), GlobalFlagReason.InvalidFlag, GlobalFlagReadStatus.Failed),
            (RequestId, Version(1) with { Identity = Identity with { World = new(Other) } }, new(3), GlobalFlagReason.WrongWorld, GlobalFlagReadStatus.Failed),
            (RequestId, Version(1) with { Identity = Identity with { Module = new(Other) } }, new(3), GlobalFlagReason.WrongModule, GlobalFlagReadStatus.Failed),
            (RequestId, Version(1), new(7), GlobalFlagReason.FlagNotAdmitted, GlobalFlagReadStatus.Failed),
            (RequestId, Version(1) with { Epoch = new(Other) }, new(3), GlobalFlagReason.StaleEpoch, GlobalFlagReadStatus.Stale),
            (RequestId, Version(0), new(3), GlobalFlagReason.StaleRevision, GlobalFlagReadStatus.Stale),
            (RequestId, Version(2), new(3), GlobalFlagReason.StaleRevision, GlobalFlagReadStatus.Stale),
        };
        foreach (var row in rows)
        {
            Assert.AreEqual(new GlobalFlagReadResult(row.Request, row.Expected, row.Flag, row.Status, null, Version(1), row.Reason),
                owner.Read(new(row.Request, row.Expected, row.Flag)));
            Assert.AreEqual(new GlobalFlagWriteReceipt(row.Request, GlobalFlagOperation.Write, row.Expected, row.Flag, false, Version(1), GlobalFlagWriteDisposition.Rejected, row.Reason),
                owner.Write(new(row.Request, row.Expected, row.Flag, false)));
            State(owner, 1, true, true, false, false, false, null);
        }
    }

    [TestMethod]
    public void Independent_worlds_and_modules_with_identical_flags_and_epochs_are_isolated()
    {
        var identities = new[] { Identity, Identity with { World = new(Other) }, Identity with { Module = new(Other) }, new GlobalFlagIdentity(new(Other), new(Other)) };
        var owners = identities.Select(id => GlobalFlagOwner.Create(Bootstrap() with { Identity = id }).Owner!).ToArray();
        try
        {
            for (var index = 0; index < owners.Length; index++)
            {
                var version = new GlobalFlagVersion(identities[index], Epoch, 0);
                Assert.AreEqual(GlobalFlagWriteDisposition.Applied, owners[index].Write(new(RequestId, version, new(3), true)).Disposition);
                for (var other = 0; other < owners.Length; other++)
                {
                    var expectedRevision = other <= index ? 1L : 0L;
                    var expectedVersion = new GlobalFlagVersion(identities[other], Epoch, expectedRevision);
                    Assert.AreEqual(expectedVersion, owners[other].Version);
                    Assert.AreEqual(other <= index, owners[other].Read(new(RequestId, expectedVersion, new(3))).Value);
                }
            }
        }
        finally { foreach (var owner in owners) owner.Dispose(); }
    }

    [TestMethod]
    public void Snapshot_and_bootstrap_are_detached_immutable_and_historical_reads_survive_lifecycle()
    {
        var domain = new[] { new FlagId(3), new FlagId(4) };
        var bootstrap = new[] { new GlobalFlagBootstrapValue(new(3), false) };
        using var owner = GlobalFlagOwner.Create(new(Identity, Epoch, 0, domain, bootstrap)).Owner!;
        var old = owner.CaptureSnapshot();
        domain[0] = new(99);
        bootstrap[0] = new(new(3), true);
        Assert.AreEqual(GlobalFlagWriteDisposition.Applied, owner.Write(new(RequestId, Version(0), new(3), true)).Disposition);
        owner.MarkFailed();
        Assert.AreEqual(GlobalFlagReadStatus.Failed, owner.Read(new(RequestId, Version(1), new(3))).Status);
        owner.Dispose();
        Assert.AreEqual(GlobalFlagReadStatus.Unavailable, owner.Read(new(RequestId, Version(1), new(3))).Status);
        Assert.AreEqual(new GlobalFlagReadResult(RequestId, Version(0), new FlagId(3), GlobalFlagReadStatus.Known, false, Version(0), GlobalFlagReason.None),
            old.Read(new(RequestId, Version(0), new(3))));
        Assert.AreEqual(GlobalFlagReadStatus.Unknown, old.Read(new(RequestId, Version(0), new(4))).Status);
        Assert.AreEqual(GlobalFlagReason.StaleRevision, old.Read(new(RequestId, Version(1), new(3))).Reason);
        Assert.AreEqual(GlobalFlagReason.StaleEpoch, old.Read(new(RequestId, Version(0) with { Epoch = new(Other) }, new(3))).Reason);
        Assert.AreEqual(GlobalFlagReason.WrongWorld, old.Read(new(RequestId, Version(0) with { Identity = Identity with { World = new(Other) } }, new(3))).Reason);
        Assert.AreEqual(GlobalFlagReason.FlagNotAdmitted, old.Read(new(RequestId, Version(0), new(99))).Reason);
        Assert.AreEqual(Version(1), owner.Version);
        CollectionAssert.AreEqual(new[] { new GlobalFlagEntry(new(3), false), new(new(4), null) }, old.Entries.ToArray());
        CollectionAssert.AreEqual(new[] { new GlobalFlagEntry(new(3), true), new(new(4), null) }, owner.CaptureSnapshot().Entries.ToArray());
        try { ((IList<GlobalFlagEntry>)old.Entries)[0] = new(new(3), true); Assert.Fail("Snapshot was mutable."); }
        catch (NotSupportedException) { }
        try { ((IList<FlagId>)owner.Binding.Domain)[0] = new(99); Assert.Fail("Binding was mutable."); }
        catch (NotSupportedException) { }
    }

    [TestMethod]
    public void Failed_and_disposed_owners_reject_writes_without_state_or_revision_changes()
    {
        using var owner = Owner();
        owner.MarkFailed();
        Assert.AreEqual(new GlobalFlagReadResult(RequestId, Version(0), new FlagId(3), GlobalFlagReadStatus.Failed, null, Version(0), GlobalFlagReason.OwnerFailed), owner.Read(new(RequestId, Version(0), new(3))));
        Assert.AreEqual(new GlobalFlagWriteReceipt(RequestId, GlobalFlagOperation.Write, Version(0), new FlagId(3), true, Version(0), GlobalFlagWriteDisposition.Rejected, GlobalFlagReason.OwnerFailed), owner.Write(new(RequestId, Version(0), new(3), true)));
        State(owner, 0, false, true, false, false, false, null);
        owner.Dispose();
        owner.Dispose();
        Assert.AreEqual(new GlobalFlagReadResult(RequestId, Version(0), new FlagId(3), GlobalFlagReadStatus.Unavailable, null, Version(0), GlobalFlagReason.OwnerDisposed), owner.Read(new(RequestId, Version(0), new(3))));
        Assert.AreEqual(new GlobalFlagWriteReceipt(RequestId, GlobalFlagOperation.Write, Version(0), new FlagId(3), true, Version(0), GlobalFlagWriteDisposition.Rejected, GlobalFlagReason.OwnerDisposed), owner.Write(new(RequestId, Version(0), new(3), true)));
        State(owner, 0, false, true, false, false, false, null);
    }

    [TestMethod]
    public void Revision_exhaustion_is_atomic_but_equal_receipt_remains_successful()
    {
        using var owner = Owner(9_223_372_036_854_775_806);
        Assert.AreEqual(new GlobalFlagWriteReceipt(RequestId, GlobalFlagOperation.Write, Version(9_223_372_036_854_775_806), new FlagId(3), true, Version(9_223_372_036_854_775_807), GlobalFlagWriteDisposition.Applied, GlobalFlagReason.None),
            owner.Write(new(RequestId, Version(9_223_372_036_854_775_806), new(3), true)));
        foreach (var flag in new[] { new FlagId(3), new FlagId(9) })
        {
            Assert.AreEqual(new GlobalFlagWriteReceipt(RequestId, GlobalFlagOperation.Write, Version(9_223_372_036_854_775_807), flag, false, Version(9_223_372_036_854_775_807), GlobalFlagWriteDisposition.Rejected, GlobalFlagReason.RevisionExhausted),
                owner.Write(new(RequestId, Version(9_223_372_036_854_775_807), flag, false)));
            State(owner, 9_223_372_036_854_775_807, true, true, false, false, false, null);
        }
        Assert.AreEqual(new GlobalFlagWriteReceipt(RequestId, GlobalFlagOperation.Write, Version(9_223_372_036_854_775_807), new FlagId(3), true, Version(9_223_372_036_854_775_807), GlobalFlagWriteDisposition.NoChange, GlobalFlagReason.None),
            owner.Write(new(RequestId, Version(9_223_372_036_854_775_807), new(3), true)));
        var adapter = new GlobalFlagAdapter(owner.Binding, owner);
        Assert.AreEqual(new GlobalFlagWriteReceipt(RequestId, GlobalFlagOperation.Write, Version(9_223_372_036_854_775_807), new FlagId(3), true, Version(9_223_372_036_854_775_807), GlobalFlagWriteDisposition.NoChange, GlobalFlagReason.None),
            adapter.Write(RequestId, Version(9_223_372_036_854_775_807), Descriptor("3", "1")));
        Assert.AreEqual(new GlobalFlagWriteReceipt(RequestId, GlobalFlagOperation.Write, Version(9_223_372_036_854_775_807), new FlagId(3), false, Version(9_223_372_036_854_775_807), GlobalFlagWriteDisposition.Rejected, GlobalFlagReason.RevisionExhausted),
            adapter.Write(RequestId, Version(9_223_372_036_854_775_807), Descriptor("3", "0")));
        State(owner, 9_223_372_036_854_775_807, true, true, false, false, false, null);
    }

    [TestMethod]
    public void Bootstrap_validation_rejects_each_bad_descriptor_without_publishing_owner()
    {
        var rows = new (GlobalFlagOwnerDescriptor? Descriptor, GlobalFlagReason Reason)[]
        {
            (null, GlobalFlagReason.InvalidDescriptor),
            (Bootstrap() with { Identity = default }, GlobalFlagReason.InvalidIdentity),
            (Bootstrap() with { Epoch = default }, GlobalFlagReason.InvalidEpoch),
            (Bootstrap(-1), GlobalFlagReason.InvalidRevision),
            (Bootstrap() with { Domain = null! }, GlobalFlagReason.InvalidDomain),
            (Bootstrap() with { Domain = Array.Empty<FlagId>() }, GlobalFlagReason.InvalidDomain),
            (Bootstrap() with { Bootstrap = null! }, GlobalFlagReason.InvalidBootstrap),
            (Bootstrap() with { Domain = new[] { new FlagId(-1) } }, GlobalFlagReason.InvalidFlag),
            (Bootstrap() with { Domain = new[] { new FlagId(3), new FlagId(3) } }, GlobalFlagReason.DuplicateFlag),
            (Bootstrap() with { Bootstrap = new[] { new GlobalFlagBootstrapValue(new(7), false) } }, GlobalFlagReason.InvalidBootstrap),
            (Bootstrap() with { Bootstrap = new[] { new GlobalFlagBootstrapValue(new(3), false), new(new(3), true) } }, GlobalFlagReason.DuplicateBootstrap),
            (Bootstrap() with { Domain = Enumerable.Range(0, 4097).Select(n => new FlagId(n)).ToArray() }, GlobalFlagReason.BudgetExceeded),
            (Bootstrap() with { Bootstrap = Enumerable.Repeat(new GlobalFlagBootstrapValue(new(3), false), 4097).ToArray() }, GlobalFlagReason.BudgetExceeded),
        };
        foreach (var row in rows) Assert.AreEqual(new GlobalFlagCreationResult(null, row.Reason), GlobalFlagOwner.Create(row.Descriptor));
    }

    [TestMethod]
    public void Finite_domain_boundary_zero_maximum_sparse_and_4096_entries_are_admitted()
    {
        using var sparse = GlobalFlagOwner.Create(new(Identity, Epoch, 0, new[] { new FlagId(2_147_483_647), new FlagId(0) }, Array.Empty<GlobalFlagBootstrapValue>())).Owner!;
        Assert.IsNotNull(sparse);
        CollectionAssert.AreEqual(new[] { new FlagId(0), new(2_147_483_647) }, sparse.Binding.Domain.ToArray());
        Assert.AreEqual(GlobalFlagReadStatus.Unknown, sparse.Read(new(RequestId, Version(0), new(0))).Status);
        Assert.AreEqual(GlobalFlagReason.FlagNotAdmitted, sparse.Read(new(RequestId, Version(0), new(1))).Reason);
        Assert.AreEqual(GlobalFlagWriteDisposition.Applied, sparse.Write(new(RequestId, Version(0), new(2_147_483_647), true)).Disposition);
        using var full = GlobalFlagOwner.Create(new(Identity, Epoch, 0, Enumerable.Range(0, 4096).Select(n => new FlagId(n)).ToArray(), Array.Empty<GlobalFlagBootstrapValue>())).Owner!;
        Assert.AreEqual(4096, full.CaptureSnapshot().Entries.Count);
        Assert.AreEqual(GlobalFlagWriteDisposition.Applied, full.Write(new(RequestId, Version(0), new(4095), false)).Disposition);
        Assert.AreEqual(4095, full.CaptureSnapshot().Entries.Count(e => e.Value is null));
        using var known = GlobalFlagOwner.Create(new(Identity, Epoch, 0,
            Enumerable.Range(0, 4096).Select(n => new FlagId(n)).ToArray(),
            Enumerable.Range(0, 4096).Select(n => new GlobalFlagBootstrapValue(new(n), n % 2 == 1)).ToArray())).Owner!;
        Assert.IsNotNull(known);
        Assert.AreEqual(0, known.CaptureSnapshot().Entries.Count(e => e.Value is null));
        Assert.AreEqual(false, known.Read(new(RequestId, Version(0), new(0))).Value);
        Assert.AreEqual(true, known.Read(new(RequestId, Version(0), new(4095))).Value);
        Assert.AreEqual(Version(0), known.Version);
    }

    [TestMethod]
    public void Deterministic_independent_state_machine_oracle_checks_many_serialized_writes()
    {
        using var owner = Owner();
        var expected = new Dictionary<int, bool?> { [3] = false, [4] = true, [5] = false, [6] = false, [8] = false, [9] = null };
        var random = new Random(431);
        long revision = 0;
        var flags = new[] { 3, 4, 5, 6, 8, 9 };
        for (var step = 0; step < 256; step++)
        {
            var flag = flags[random.Next(flags.Length)];
            var value = random.Next(2) == 1;
            var oldRevision = revision;
            var changed = expected[flag] != value;
            if (changed) { expected[flag] = value; revision++; }
            Assert.AreEqual(new GlobalFlagWriteReceipt(RequestId, GlobalFlagOperation.Write, Version(oldRevision), new FlagId(flag), value, Version(revision),
                changed ? GlobalFlagWriteDisposition.Applied : GlobalFlagWriteDisposition.NoChange, GlobalFlagReason.None),
                owner.Write(new(RequestId, Version(oldRevision), new(flag), value)));
            State(owner, revision, flags.Select(f => expected[f]).ToArray());
        }
    }
}
