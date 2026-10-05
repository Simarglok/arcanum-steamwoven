using System.Reflection;
using Arcanum.Data.Area;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Data.Tests.Area;

[TestClass]
public sealed class AreaBudgetTests
{
    // These probes target the actual accounting object. Public owner tests below
    // independently exercise its production call sites; no substitute budget.
    private sealed class Budget(AreaLoaderOptions limits)
    {
        private static readonly Type Type = typeof(AreaDataOwner).Assembly.GetType("Arcanum.Data.Area.AreaBudget", throwOnError: true)!;
        private readonly object _instance = Activator.CreateInstance(Type, BindingFlags.Instance | BindingFlags.NonPublic, null, [limits], null)!;
        internal object? Call(string name, params object[] args)
        {
            try { return Type.GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(_instance, args); }
            catch (TargetInvocationException e) when (e.InnerException is not null)
            { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e.InnerException).Throw(); throw; }
        }
        internal T Get<T>(string name) => (T)Type.GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(_instance)!;
        internal AreaBudgetUsage Usage => Get<AreaBudgetUsage>("Usage");
        internal AreaBudgetRejection Rejection => Get<AreaBudgetRejection?>("FirstRejection")!.Value;
    }

    [TestMethod]
    public void Allocation_domains_admit_equality_reject_cap_plus_one_atomically_and_preserve_primary()
    {
        foreach (var (limits, domain, operation) in new (AreaLoaderOptions, AreaBudgetDomain, string)[]
        {
            (new() { MaxManagedBytes = 10 }, AreaBudgetDomain.Managed, "Reserve"),
            (new() { MaxBuildPhaseBytes = 10 }, AreaBudgetDomain.Phase, "Reserve"),
            (new() { MaxRetainedBytes = 10 }, AreaBudgetDomain.Retained, "Retain"),
            (new() { MaxFrameOperationBytes = 10 }, AreaBudgetDomain.Component, "Reserve")
        })
        {
            var budget = new Budget(limits);
            if (domain == AreaBudgetDomain.Component) budget.Call("StartFrame", 0L, 0L, 0L, 0L);
            budget.Call(operation, 10L);
            var before = budget.Usage;
            AreaIdentityTests.Throws<AreaException>(() => budget.Call(operation, 1L));
            Assert.AreEqual(before, budget.Usage);
            Assert.AreEqual(domain, budget.Rejection.Domain);
            Assert.AreEqual(10L, budget.Rejection.Used);
            Assert.AreEqual(1L, budget.Rejection.Requested);
            Assert.AreEqual(10L, budget.Rejection.Limit);
            Assert.AreEqual(before, budget.Rejection.Before);
            Assert.AreEqual(before, budget.Rejection.After);
            var first = budget.Rejection;
            AreaIdentityTests.Throws<AreaException>(() => budget.Call("Reserve", -1L));
            Assert.AreEqual(first, budget.Rejection);
            Assert.AreEqual(before, budget.Usage);
        }
    }

    [TestMethod]
    public void Every_vector_dimension_is_transactional_at_equality_and_cap_plus_one()
    {
        foreach (var (limits, domain, first, second) in new (AreaLoaderOptions, AreaBudgetDomain, long[], long[])[]
        {
            (new() { MaxFrameRequests = 1 }, AreaBudgetDomain.Requests, [0,0,0,0], [0,0,0,0]),
            (new() { MaxFrameStoredBytes = 10 }, AreaBudgetDomain.Stored, [10,0,0,0], [1,0,0,0]),
            (new() { MaxFrameDecodedBytes = 10 }, AreaBudgetDomain.Decoded, [0,10,0,0], [0,1,0,0]),
            (new() { MaxFrameRgbaBytes = 10 }, AreaBudgetDomain.Rgba, [0,0,10,0], [0,0,1,0]),
            (new() { MaxFrameEncodedBytes = 10 }, AreaBudgetDomain.Encoded, [0,0,0,10], [0,0,0,1])
        })
        {
            var b = new Budget(limits);
            b.Call("StartFrame", first.Cast<object>().ToArray()); b.Call("EndFrame");
            var before = b.Usage;
            AreaIdentityTests.Throws<AreaException>(() => b.Call("StartFrame", second.Cast<object>().ToArray()));
            Assert.AreEqual(before, b.Usage);
            Assert.AreEqual(domain, b.Rejection.Domain);
            Assert.IsFalse(b.Rejection.ComponentActive);
        }
    }

    [TestMethod]
    public void Negative_and_overflow_requests_fail_closed_in_every_affected_path()
    {
        foreach (var method in new[] { "Reserve", "Retain", "Hold", "FrameAllocation", "CompleteFrameAllocation" })
        {
            var b = new Budget(new()); var before = b.Usage;
            AreaIdentityTests.Throws<AreaException>(() => b.Call(method, -1L));
            Assert.AreEqual(before, b.Usage);
        }
        foreach (var position in Enumerable.Range(0, 4))
        {
            var b = new Budget(new()); var args = new object[] { 0L, 0L, 0L, 0L }; args[position] = -1L;
            AreaIdentityTests.Throws<AreaException>(() => b.Call("StartFrame", args));
            Assert.AreEqual(0L, b.Usage.FrameRequests);
        }
        var huge = new Budget(new() { MaxManagedBytes = long.MaxValue, MaxBuildPhaseBytes = long.MaxValue });
        huge.Call("Reserve", long.MaxValue); var full = huge.Usage;
        AreaIdentityTests.Throws<AreaException>(() => huge.Call("Reserve", 1L));
        Assert.AreEqual(full, huge.Usage);
        var frame = new Budget(new());
        AreaIdentityTests.Throws<AreaException>(() => frame.Call("CompleteFrame", long.MaxValue, 0L));
        Assert.AreEqual(0L, frame.Usage.FrameRgba);
        AreaIdentityTests.Throws<AreaException>(() => frame.Call("Copy", -1));
        Assert.AreEqual(0L, frame.Usage.ConsumerCopyBytes);
    }

    [TestMethod]
    public void Read_admission_precedes_actual_returns_and_preserves_terminal_requested_work()
    {
        var b = new Budget(new() { MaxReadBytes = 10 });
        b.Call("ReadReserve", 7); b.Call("ReadActual", 1, 7);
        b.Call("ReadReserve", 3); b.Call("ReadActual", 0, 3);
        Assert.AreEqual(10L, b.Usage.ReadReserved); Assert.AreEqual(1L, b.Usage.ReadActual);
        var before = b.Usage;
        AreaIdentityTests.Throws<AreaException>(() => b.Call("ReadReserve", 1));
        Assert.AreEqual(before, b.Usage);
        Assert.AreEqual(AreaBudgetDomain.Read, b.Rejection.Domain);
        AreaIdentityTests.Throws<AreaException>(() => b.Call("ReadReserve", -1));
        foreach (var invalid in new[] { -1, 4 })
            AreaIdentityTests.Throws<IOException>(() => b.Call("ReadActual", invalid, 3));
        Assert.AreEqual(before, b.Usage);
    }

    [TestMethod]
    public void Fixed_phases_keep_cumulative_work_and_components_cannot_hide_a_transition()
    {
        var b = new Budget(new() { MaxManagedBytes = 20, MaxBuildPhaseBytes = 10 });
        b.Call("BeginPhase", AreaBudgetPhase.I1); b.Call("Reserve", 10L);
        b.Call("BeginPhase", AreaBudgetPhase.I2); b.Call("Reserve", 10L);
        Assert.AreEqual(20L, b.Usage.ManagedReserved); Assert.AreEqual(10L, b.Usage.PhaseReserved);
        var history = b.Get<IReadOnlyList<AreaBudgetPhaseUsage>>("History");
        Assert.AreEqual(new AreaBudgetPhaseUsage(AreaBudgetPhase.I1, 1, 10, 10), history[2]);
        Assert.AreEqual(new AreaBudgetPhaseUsage(AreaBudgetPhase.I2, 1, 10, 10), history[3]);
        b.Call("StartFrame", 0L, 0L, 0L, 0L);
        AreaIdentityTests.Throws<InvalidOperationException>(() => b.Call("BeginPhase", AreaBudgetPhase.I3));
        b.Call("EndFrame"); b.Call("BeginPhase", AreaBudgetPhase.I3);
        AreaIdentityTests.Throws<AreaException>(() => b.Call("Reserve", 1L));
        Assert.AreEqual(AreaBudgetDomain.Managed, b.Rejection.Domain);
    }

    [TestMethod]
    public void Retained_handoff_does_not_double_charge_or_refund_and_copy_obeys_component()
    {
        var b = new Budget(new() { MaxRetainedBytes = 10, MaxFrameOperationBytes = 40 });
        b.Call("Reserve", 10L); b.Call("Hold", 10L); b.Call("Release", 10L);
        Assert.AreEqual(10L, b.Usage.ManagedReserved); Assert.AreEqual(0L, b.Usage.RetainedReserved);
        b.Call("BeginPhase", AreaBudgetPhase.C); b.Call("Copy", 8);
        Assert.AreEqual(50L, b.Usage.ManagedReserved); Assert.AreEqual(8L, b.Usage.ConsumerCopyBytes);
        b.Call("BeginPhase", AreaBudgetPhase.C); var before = b.Usage;
        AreaIdentityTests.Throws<AreaException>(() => b.Call("Copy", 9));
        Assert.AreEqual(before, b.Usage); Assert.AreEqual(AreaBudgetDomain.Component, b.Rejection.Domain);
    }

    [TestMethod]
    public void Late_multidimensional_rejections_do_not_commit_earlier_retained_or_frame_admissions()
    {
        var retained = new Budget(new() { MaxBuildPhaseBytes = 10 });
        retained.Call("Retain", 10L); var before = retained.Usage;
        AreaIdentityTests.Throws<AreaException>(() => retained.Call("Retain", 1L));
        Assert.AreEqual(before, retained.Usage);
        Assert.AreEqual(AreaBudgetDomain.Phase, retained.Rejection.Domain);
        var frame = new Budget(new() { MaxManagedBytes = 263 });
        frame.Call("StartFrame", 0L, 0L, 0L, 0L); before = frame.Usage;
        AreaIdentityTests.Throws<AreaException>(() => frame.Call("CompleteFrame", 8L, 2L));
        Assert.AreEqual(before, frame.Usage);
        Assert.AreEqual(AreaBudgetDomain.Managed, frame.Rejection.Domain);
        frame.Call("EndFrame");
    }

    [TestMethod]
    public void Distinct_owned_allocation_and_live_bounds_admit_atomically_without_refunding_work()
    {
        var b = new Budget(new() { MaxManagedBytes = 12, MaxRetainedBytes = 10 });
        b.Call("Own", 5L, 10L);
        Assert.AreEqual(5L, b.Usage.ManagedReserved);
        Assert.AreEqual(10L, b.Usage.RetainedReserved);
        var before = b.Usage;
        AreaIdentityTests.Throws<AreaException>(() => b.Call("Own", 1L, 1L));
        Assert.AreEqual(before, b.Usage);
        Assert.AreEqual(AreaBudgetDomain.Retained, b.Rejection.Domain);
        b.Call("Release", 10L);
        b.Call("Own", 7L, 2L);
        Assert.AreEqual(12L, b.Usage.ManagedReserved);
        before = b.Usage;
        AreaIdentityTests.Throws<AreaException>(() => b.Call("Own", 1L, 1L));
        Assert.AreEqual(before, b.Usage);
        AreaIdentityTests.Throws<InvalidOperationException>(() => b.Call("Release", 3L));
        b.Call("Release", 2L);
        Assert.AreEqual(12L, b.Usage.ManagedReserved);
        Assert.AreEqual(0L, b.Usage.RetainedReserved);
    }

    [TestMethod]
    public void Component_live_hold_rejects_before_frame_vector_commit_and_unwinds_exactly_once()
    {
        var rejected = new Budget(new() { MaxRetainedBytes = 10, MaxFrameOperationBytes = 11 });
        var before = rejected.Usage;
        AreaIdentityTests.Throws<AreaException>(() => rejected.Call("StartFrame", 1L, 2L, 3L, 4L));
        Assert.AreEqual(before, rejected.Usage);
        Assert.AreEqual(AreaBudgetDomain.Retained, rejected.Rejection.Domain);
        rejected.Call("EndFrame");
        Assert.AreEqual(before, rejected.Usage);
        var admitted = new Budget(new() { MaxRetainedBytes = 11, MaxFrameOperationBytes = 11 });
        admitted.Call("StartFrame", 1L, 2L, 3L, 4L);
        Assert.AreEqual(11L, admitted.Usage.RetainedReserved);
        Assert.AreEqual(0L, admitted.Usage.ManagedReserved);
        admitted.Call("FrameAllocation", 11L);
        admitted.Call("EndFrame"); admitted.Call("EndFrame");
        Assert.AreEqual(0L, admitted.Usage.RetainedReserved);
        Assert.AreEqual(11L, admitted.Usage.ManagedReserved);
        Assert.AreEqual(1L, admitted.Usage.FrameRequests);
    }

    [TestMethod]
    public void Public_owner_two_inspections_provider_and_copy_follow_whole_phase_graph()
    {
        using var fixture = new AreaIdentityTests.Fixture([(67108865, [AreaIdentityTests.Record(AreaIdentityTests.Id(0))])]);
        var first = fixture.Owner.LoadStrict(); var usage1 = fixture.Owner.Usage;
        var second = fixture.Owner.LoadStrict();
        Assert.IsTrue(fixture.Owner.Usage.ManagedReserved > usage1.ManagedReserved);
        Assert.AreEqual(1L, fixture.Owner.PhaseHistory[0].Visits);
        for (var phase = 1; phase <= 8; phase++) Assert.AreEqual(2L, fixture.Owner.PhaseHistory[phase].Visits);
        using var lease = fixture.Owner.AcquireSelectedFrame(second, second.Visuals[0].Key);
        CollectionAssert.AreEqual(new byte[] { 1,2,3,255,4,5,6,255 }, lease.CopyRgba());
        Assert.AreEqual(1L, fixture.Owner.PhaseHistory[9].Visits);
        Assert.AreEqual(1L, fixture.Owner.PhaseHistory[10].Visits);
        Assert.AreEqual(8L, fixture.Owner.Usage.ConsumerCopyBytes);
        Assert.IsNull(fixture.Owner.FirstBudgetRejection);
        AreaIdentityTests.Throws<AreaException>(() => fixture.Owner.AcquireSelectedFrame(first, first.Visuals[0].Key));
    }

    [TestMethod]
    public void Public_frame_vector_rejects_next_request_before_provider_reads_and_preserves_snapshot()
    {
        using var fixture = new AreaIdentityTests.Fixture([(67108865, [])], limits: new() { MaxFrameRequests = 1 });
        var snapshot = fixture.Owner.LoadStrict(); var before = fixture.Owner.Usage;
        AreaIdentityTests.Throws<AreaException>(() => fixture.Owner.AcquireSelectedFrame(snapshot, snapshot.Visuals[0].Key));
        Assert.AreEqual(before.ReadReserved, fixture.Owner.Usage.ReadReserved);
        Assert.AreEqual(before.FrameRequests, fixture.Owner.Usage.FrameRequests);
        Assert.AreSame(snapshot, fixture.Owner.PublishedSnapshot);
        Assert.AreEqual(AreaBudgetDomain.Requests, fixture.Owner.FirstBudgetRejection!.Value.Domain);
    }
}
