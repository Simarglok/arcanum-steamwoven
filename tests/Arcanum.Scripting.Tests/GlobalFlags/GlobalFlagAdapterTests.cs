using Arcanum.Scripting.GlobalFlags;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Scripting.Tests.GlobalFlags.GlobalFlagFixture;

namespace Arcanum.Scripting.Tests.GlobalFlags;

[TestClass]
public sealed class GlobalFlagAdapterTests
{
    [TestMethod]
    public void C01_post_write_equality_queries_and_C02_both_explicit_writes_use_production_owner()
    {
        using var owner = Owner();
        var calls = new CountingOwner(owner);
        var adapter = new GlobalFlagAdapter(owner.Binding, calls);
        Assert.AreEqual(new GlobalFlagConditionResult(new(RequestId, Version(0), new FlagId(3), GlobalFlagReadStatus.Known, false, Version(0), GlobalFlagReason.None), false),
            adapter.Compare(RequestId, Version(0), Descriptor("3", "1")));
        Assert.AreEqual(true, adapter.Compare(RequestId, Version(0), Descriptor("3", "0")).Matches);
        var first = adapter.Write(RequestId, Version(0), Descriptor("3", "1"));
        Assert.AreEqual(new GlobalFlagWriteReceipt(RequestId, GlobalFlagOperation.Write, Version(0), new FlagId(3), true, Version(1), GlobalFlagWriteDisposition.Applied, GlobalFlagReason.None), first);
        State(owner, 1, true, true, false, false, false, null);
        Assert.AreEqual(true, adapter.Compare(RequestId, Version(1), Descriptor("3", "1")).Matches);
        Assert.AreEqual(false, adapter.Compare(RequestId, Version(1), Descriptor("3", "0")).Matches);
        var second = adapter.Write(new(Other), Version(1), Descriptor("3", "0"));
        Assert.AreEqual(new GlobalFlagWriteReceipt(new(Other), GlobalFlagOperation.Write, Version(1), new FlagId(3), false, Version(2), GlobalFlagWriteDisposition.Applied, GlobalFlagReason.None), second);
        State(owner, 2, false, true, false, false, false, null);
        Assert.AreEqual(false, adapter.Compare(RequestId, Version(2), Descriptor("3", "1")).Matches);
        Assert.AreEqual(true, adapter.Compare(RequestId, Version(2), Descriptor("3", "0")).Matches);
        Assert.AreEqual(6, calls.Reads);
        Assert.AreEqual(2, calls.Writes);
    }

    [TestMethod]
    public void Unknown_failed_unavailable_and_stale_comparisons_never_become_false()
    {
        using var owner = Owner();
        var adapter = new GlobalFlagAdapter(owner.Binding, owner);
        foreach (var token in new[] { "0", "1" })
        {
            Assert.AreEqual(new GlobalFlagConditionResult(new(RequestId, Version(0), new FlagId(9), GlobalFlagReadStatus.Unknown, null, Version(0), GlobalFlagReason.MissingBootstrapValue), null),
                adapter.Compare(RequestId, Version(0), Descriptor("9", token)));
        }
        Assert.AreEqual(new GlobalFlagConditionResult(new(RequestId, Version(0), new FlagId(3), GlobalFlagReadStatus.Unavailable, null, null, GlobalFlagReason.OwnerUnavailable), null),
            new GlobalFlagAdapter(owner.Binding, null).Compare(RequestId, Version(0), Descriptor("3", "1")));
        Assert.AreEqual(new GlobalFlagWriteReceipt(RequestId, GlobalFlagOperation.Write, Version(0), new FlagId(3), null, null, GlobalFlagWriteDisposition.Rejected, GlobalFlagReason.OwnerUnavailable),
            new GlobalFlagAdapter(owner.Binding, null).Write(RequestId, Version(0), Descriptor("3", "1")));
        adapter.Write(RequestId, Version(0), Descriptor("3", "1"));
        Assert.AreEqual(new GlobalFlagConditionResult(new(RequestId, Version(0), new FlagId(3), GlobalFlagReadStatus.Stale, null, Version(1), GlobalFlagReason.StaleRevision), null),
            adapter.Compare(RequestId, Version(0), Descriptor("3", "1")));
        owner.MarkFailed();
        Assert.AreEqual(new GlobalFlagConditionResult(new(RequestId, Version(1), new FlagId(3), GlobalFlagReadStatus.Failed, null, Version(1), GlobalFlagReason.OwnerFailed), null),
            adapter.Compare(RequestId, Version(1), Descriptor("3", "1")));
        owner.Dispose();
        Assert.AreEqual(new GlobalFlagConditionResult(new(RequestId, Version(1), new FlagId(3), GlobalFlagReadStatus.Unavailable, null, Version(1), GlobalFlagReason.OwnerDisposed), null),
            adapter.Compare(RequestId, Version(1), Descriptor("3", "1")));
        State(owner, 1, true, true, false, false, false, null);
    }

    [TestMethod]
    public void Every_invalid_descriptor_is_rejected_before_any_read_or_write_call()
    {
        using var owner = Owner();
        var calls = new CountingOwner(owner);
        var adapter = new GlobalFlagAdapter(owner.Binding, calls);
        var rows = new (GlobalFlagOperationDescriptor? Descriptor, GlobalFlagReason Reason)[]
        {
            (null, GlobalFlagReason.InvalidDescriptor),
            (new(1, null!, new[] { "3", "1" }), GlobalFlagReason.InvalidDescriptor),
            (new(1, "GF", null!), GlobalFlagReason.InvalidDescriptor),
            (new(0, "GF", new[] { "3", "1" }), GlobalFlagReason.UnsupportedSchema),
            (new(2, "GF", new[] { "3", "1" }), GlobalFlagReason.UnsupportedSchema),
            (new(1, "LF", new[] { "3", "1" }), GlobalFlagReason.UnsupportedOperation),
            (new(1, "GFF", new[] { "3", "1" }), GlobalFlagReason.UnsupportedOperation),
            (new(1, "GФ", new[] { "3", "1" }), GlobalFlagReason.NonAscii),
            (new(1, "GF", Array.Empty<string>()), GlobalFlagReason.InvalidArity),
            (new(1, "GF", new[] { "3" }), GlobalFlagReason.InvalidArity),
            (new(1, "GF", new[] { "3", "1", "0" }), GlobalFlagReason.InvalidArity),
            (Descriptor(null!, "1"), GlobalFlagReason.InvalidOperand),
            (Descriptor("", "1"), GlobalFlagReason.InvalidOperand),
            (Descriptor("-1", "1"), GlobalFlagReason.InvalidOperand),
            (Descriptor("-0", "1"), GlobalFlagReason.InvalidOperand),
            (Descriptor("+3", "1"), GlobalFlagReason.InvalidOperand),
            (Descriptor("3 ", "1"), GlobalFlagReason.InvalidOperand),
            (Descriptor("(3)", "1"), GlobalFlagReason.InvalidOperand),
            (Descriptor("3", "1;GF 4 0"), GlobalFlagReason.InvalidOperand),
            (Descriptor("３", "1"), GlobalFlagReason.NonAscii),
            (Descriptor("3", "١"), GlobalFlagReason.NonAscii),
            (Descriptor("3", "\n"), GlobalFlagReason.InvalidOperand),
            (Descriptor("2147483648", "1"), GlobalFlagReason.OperandOverflow),
            (Descriptor("99999999999999999999999999999999", "1"), GlobalFlagReason.OperandOverflow),
            (Descriptor(new string('0', 33), "1"), GlobalFlagReason.BudgetExceeded),
            (Descriptor("3", new string('0', 33)), GlobalFlagReason.BudgetExceeded),
            (Descriptor("3", "2147483648"), GlobalFlagReason.OperandOverflow),
            (Descriptor("3", "2"), GlobalFlagReason.InvalidBoolean),
            (Descriptor("3", "2147483647"), GlobalFlagReason.InvalidBoolean),
            (Descriptor("3", "-1"), GlobalFlagReason.InvalidOperand),
            (Descriptor("3", "+1"), GlobalFlagReason.InvalidOperand),
            (Descriptor("7", "0"), GlobalFlagReason.FlagNotAdmitted),
        };
        foreach (var row in rows)
        {
            Assert.AreEqual(new GlobalFlagConditionResult(new(RequestId, Version(0), null, GlobalFlagReadStatus.Failed, null, null, row.Reason), null),
                adapter.Compare(RequestId, Version(0), row.Descriptor));
            Assert.AreEqual(new GlobalFlagWriteReceipt(RequestId, GlobalFlagOperation.Write, Version(0), null, null, null, GlobalFlagWriteDisposition.Rejected, row.Reason),
                adapter.Write(RequestId, Version(0), row.Descriptor));
            State(owner, 0, false, true, false, false, false, null);
        }
        Assert.AreEqual(0, calls.Reads);
        Assert.AreEqual(0, calls.Writes);
    }

    [TestMethod]
    public void Foreign_invalid_and_old_epoch_descriptors_fail_before_dispatch_but_revision_is_checked_by_owner()
    {
        using var owner = Owner();
        var calls = new CountingOwner(owner);
        var adapter = new GlobalFlagAdapter(owner.Binding, calls);
        var rows = new (GlobalFlagRequestId Request, GlobalFlagVersion Version, GlobalFlagReason Reason, GlobalFlagReadStatus Status)[]
        {
            (default, Version(0), GlobalFlagReason.InvalidRequest, GlobalFlagReadStatus.Failed),
            (RequestId, default, GlobalFlagReason.InvalidIdentity, GlobalFlagReadStatus.Failed),
            (RequestId, Version(0) with { Epoch = default }, GlobalFlagReason.InvalidEpoch, GlobalFlagReadStatus.Failed),
            (RequestId, Version(-1), GlobalFlagReason.InvalidRevision, GlobalFlagReadStatus.Failed),
            (RequestId, Version(0) with { Identity = Identity with { World = new(Other) } }, GlobalFlagReason.WrongWorld, GlobalFlagReadStatus.Failed),
            (RequestId, Version(0) with { Identity = Identity with { Module = new(Other) } }, GlobalFlagReason.WrongModule, GlobalFlagReadStatus.Failed),
            (RequestId, Version(0) with { Epoch = new(Other) }, GlobalFlagReason.StaleEpoch, GlobalFlagReadStatus.Stale),
        };
        foreach (var row in rows)
        {
            Assert.AreEqual(new GlobalFlagConditionResult(new(row.Request, row.Version, null, row.Status, null, null, row.Reason), null), adapter.Compare(row.Request, row.Version, Descriptor("3", "1")));
            Assert.AreEqual(new GlobalFlagWriteReceipt(row.Request, GlobalFlagOperation.Write, row.Version, null, null, null, GlobalFlagWriteDisposition.Rejected, row.Reason), adapter.Write(row.Request, row.Version, Descriptor("3", "1")));
        }
        Assert.AreEqual(0, calls.Reads);
        Assert.AreEqual(0, calls.Writes);
        adapter.Write(RequestId, Version(0), Descriptor("3", "1"));
        Assert.AreEqual(new GlobalFlagWriteReceipt(RequestId, GlobalFlagOperation.Write, Version(0), new FlagId(3), false, Version(1), GlobalFlagWriteDisposition.Rejected, GlobalFlagReason.StaleRevision), adapter.Write(RequestId, Version(0), Descriptor("3", "0")));
        Assert.AreEqual(2, calls.Writes);
        State(owner, 1, true, true, false, false, false, null);
    }

    [TestMethod]
    public void Transport_boundary_ascii_case_leading_zero_maximum_index_and_token_length_are_supported()
    {
        using var owner = GlobalFlagOwner.Create(new(Identity, Epoch, 0, new[] { new FlagId(0), new FlagId(2_147_483_647) }, Array.Empty<GlobalFlagBootstrapValue>())).Owner!;
        var adapter = new GlobalFlagAdapter(owner.Binding, owner);
        Assert.AreEqual(new GlobalFlagWriteReceipt(RequestId, GlobalFlagOperation.Write, Version(0), new FlagId(0), false, Version(1), GlobalFlagWriteDisposition.Applied, GlobalFlagReason.None),
            adapter.Write(RequestId, Version(0), new(1, "gF", new[] { new string('0', 32), "00" })));
        Assert.AreEqual(new GlobalFlagWriteReceipt(RequestId, GlobalFlagOperation.Write, Version(1), new FlagId(2_147_483_647), true, Version(2), GlobalFlagWriteDisposition.Applied, GlobalFlagReason.None),
            adapter.Write(RequestId, Version(1), Descriptor("2147483647", "01")));
        Assert.AreEqual(true, adapter.Compare(RequestId, Version(2), Descriptor("0", "0")).Matches);
        Assert.AreEqual(true, adapter.Compare(RequestId, Version(2), Descriptor("2147483647", "1")).Matches);
        CollectionAssert.AreEqual(new[] { new GlobalFlagEntry(new(0), false), new(new(2_147_483_647), true) }, owner.CaptureSnapshot().Entries.ToArray());
    }

    [TestMethod]
    public void Equal_write_still_has_distinct_request_receipt_no_bump_and_no_request_cache()
    {
        using var owner = Owner();
        var adapter = new GlobalFlagAdapter(owner.Binding, owner);
        Assert.AreEqual(new GlobalFlagWriteReceipt(RequestId, GlobalFlagOperation.Write, Version(0), new FlagId(3), false, Version(0), GlobalFlagWriteDisposition.NoChange, GlobalFlagReason.None), adapter.Write(RequestId, Version(0), Descriptor("3", "0")));
        Assert.AreEqual(new GlobalFlagWriteReceipt(new(Other), GlobalFlagOperation.Write, Version(0), new FlagId(3), false, Version(0), GlobalFlagWriteDisposition.NoChange, GlobalFlagReason.None), adapter.Write(new(Other), Version(0), Descriptor("3", "0")));
        // Request identity is correlation, not idempotency. Reusing it with a new version does not deduplicate.
        Assert.AreEqual(GlobalFlagWriteDisposition.Applied, adapter.Write(RequestId, Version(0), Descriptor("3", "1")).Disposition);
        Assert.AreEqual(GlobalFlagWriteDisposition.Applied, adapter.Write(RequestId, Version(1), Descriptor("3", "0")).Disposition);
        State(owner, 2, false, true, false, false, false, null);
    }

    [TestMethod]
    public void C09_GF_only_order_and_C16_GF_only_prefix_checkpoint_do_not_claim_inventory_or_dialog_integration()
    {
        using var owner = Owner();
        var adapter = new GlobalFlagAdapter(owner.Binding, owner);
        var receipts = new[]
        {
            adapter.Write(RequestId, Version(0), Descriptor("3", "1")),
            adapter.Write(RequestId, Version(1), Descriptor("4", "0")),
            adapter.Write(RequestId, Version(2), Descriptor("5", "1")),
        };
        CollectionAssert.AreEqual(new[]
        {
            new GlobalFlagWriteReceipt(RequestId, GlobalFlagOperation.Write, Version(0), new FlagId(3), true, Version(1), GlobalFlagWriteDisposition.Applied, GlobalFlagReason.None),
            new GlobalFlagWriteReceipt(RequestId, GlobalFlagOperation.Write, Version(1), new FlagId(4), false, Version(2), GlobalFlagWriteDisposition.Applied, GlobalFlagReason.None),
            new GlobalFlagWriteReceipt(RequestId, GlobalFlagOperation.Write, Version(2), new FlagId(5), true, Version(3), GlobalFlagWriteDisposition.Applied, GlobalFlagReason.None),
        }, receipts);
        // C16's first-three-GF checkpoint only. No IN/QU/RU, session, recovery or duplicate-call PASS.
        State(owner, 3, true, false, true, false, false, null);
        var prefix = owner.CaptureSnapshot();
        Assert.AreEqual(new GlobalFlagWriteReceipt(RequestId, GlobalFlagOperation.Write, Version(3), new FlagId(6), true, Version(4), GlobalFlagWriteDisposition.Applied, GlobalFlagReason.None), adapter.Write(RequestId, Version(3), Descriptor("6", "1")));
        State(owner, 4, true, false, true, true, false, null);
        Assert.AreEqual(new GlobalFlagWriteReceipt(RequestId, GlobalFlagOperation.Write, Version(4), new FlagId(8), true, Version(5), GlobalFlagWriteDisposition.Applied, GlobalFlagReason.None), adapter.Write(RequestId, Version(4), Descriptor("8", "1")));
        State(owner, 5, true, false, true, true, true, null);
        CollectionAssert.AreEqual(new[] { true, false, true, false, false, (bool?)null }, prefix.Entries.Select(e => e.Value).ToArray());
    }

    [TestMethod]
    public void External_read_exception_is_failed_and_external_write_exception_is_indeterminate_not_rejected()
    {
        using var owner = Owner();
        var adapter = new GlobalFlagAdapter(owner.Binding, new ThrowingOwner(owner.Binding));
        Assert.AreEqual(new GlobalFlagConditionResult(new(RequestId, Version(0), new FlagId(3), GlobalFlagReadStatus.Failed, null, null, GlobalFlagReason.OwnerFailed), null), adapter.Compare(RequestId, Version(0), Descriptor("3", "1")));
        Assert.AreEqual(new GlobalFlagWriteReceipt(RequestId, GlobalFlagOperation.Write, Version(0), new FlagId(3), true, null, GlobalFlagWriteDisposition.Indeterminate, GlobalFlagReason.OwnerFailed), adapter.Write(RequestId, Version(0), Descriptor("3", "1")));
        State(owner, 0, false, true, false, false, false, null);
    }

    [TestMethod]
    public void Adapter_cannot_be_bound_to_another_owners_domain_even_with_equal_identity_epoch()
    {
        using var owner = Owner();
        using var other = Owner();
        try { _ = new GlobalFlagAdapter(owner.Binding, other); Assert.Fail("Foreign binding was accepted."); }
        catch (ArgumentException exception) { Assert.AreEqual("owner", exception.ParamName); }
    }

    [TestMethod]
    public void Foreign_or_malformed_port_results_fail_closed_without_fabricating_write_rejection()
    {
        var readMutations = new Func<GlobalFlagReadResult, GlobalFlagReadResult>[]
        {
            _ => null!,
            read => read with { RequestId = new(Other) },
            read => read with { Expected = Version(1) },
            read => read with { Flag = new FlagId(4) },
            read => read with { Status = (GlobalFlagReadStatus)99 },
            read => read with { Reason = (GlobalFlagReason)99 },
            read => read with { Value = null },
            read => read with { ResultVersion = null },
            read => read with { ResultVersion = Version(1) },
            read => read with { ResultVersion = Version(0) with { Identity = Identity with { World = new(Other) } } },
            read => read with { ResultVersion = Version(0) with { Epoch = new(Other) } },
            read => read with { Reason = GlobalFlagReason.OwnerFailed },
            read => read with { Status = GlobalFlagReadStatus.Unknown, Reason = GlobalFlagReason.MissingBootstrapValue },
        };
        foreach (var mutate in readMutations)
        {
            using var owner = Owner();
            var adapter = new GlobalFlagAdapter(owner.Binding, new AlteredOwner(owner, mutate, receipt => receipt));
            Assert.AreEqual(new GlobalFlagConditionResult(new(RequestId, Version(0), new FlagId(3), GlobalFlagReadStatus.Failed, null, null, GlobalFlagReason.InvalidOwnerResult), null),
                adapter.Compare(RequestId, Version(0), Descriptor("3", "0")));
            State(owner, 0, false, true, false, false, false, null);
        }
        var writeMutations = new Func<GlobalFlagWriteReceipt, GlobalFlagWriteReceipt>[]
        {
            _ => null!,
            receipt => receipt with { RequestId = new(Other) },
            receipt => receipt with { Operation = GlobalFlagOperation.Read },
            receipt => receipt with { Expected = Version(1) },
            receipt => receipt with { Flag = new FlagId(4) },
            receipt => receipt with { Value = false },
            receipt => receipt with { Disposition = (GlobalFlagWriteDisposition)99 },
            receipt => receipt with { Reason = (GlobalFlagReason)99 },
            receipt => receipt with { ResultVersion = null },
            receipt => receipt with { ResultVersion = Version(0) },
            receipt => receipt with { ResultVersion = Version(2) },
            receipt => receipt with { ResultVersion = Version(1) with { Epoch = new(Other) } },
            receipt => receipt with { Disposition = GlobalFlagWriteDisposition.NoChange },
            receipt => receipt with { Disposition = GlobalFlagWriteDisposition.Rejected },
        };
        foreach (var mutate in writeMutations)
        {
            using var owner = Owner();
            var adapter = new GlobalFlagAdapter(owner.Binding, new AlteredOwner(owner, read => read, mutate));
            Assert.AreEqual(new GlobalFlagWriteReceipt(RequestId, GlobalFlagOperation.Write, Version(0), new FlagId(3), true, null, GlobalFlagWriteDisposition.Indeterminate, GlobalFlagReason.InvalidOwnerResult),
                adapter.Write(RequestId, Version(0), Descriptor("3", "1")));
            // The real owner applied before its port corrupted the receipt. It would be dishonest to say Rejected.
            State(owner, 1, true, true, false, false, false, null);
        }
    }

    private sealed class AlteredOwner(GlobalFlagOwner owner,
        Func<GlobalFlagReadResult, GlobalFlagReadResult> readMutation,
        Func<GlobalFlagWriteReceipt, GlobalFlagWriteReceipt> writeMutation) : IGlobalFlagOwner
    {
        public GlobalFlagBinding Binding => owner.Binding;
        public GlobalFlagReadResult Read(GlobalFlagReadRequest request) => readMutation(owner.Read(request));
        public GlobalFlagWriteReceipt Write(GlobalFlagWriteRequest request) => writeMutation(owner.Write(request));
    }

    private sealed class ThrowingOwner(GlobalFlagBinding binding) : IGlobalFlagOwner
    {
        public GlobalFlagBinding Binding => binding;
        public GlobalFlagReadResult Read(GlobalFlagReadRequest request) => throw new InvalidOperationException("untrusted diagnostic must not escape");
        public GlobalFlagWriteReceipt Write(GlobalFlagWriteRequest request) => throw new InvalidOperationException("untrusted diagnostic must not escape");
    }
}
