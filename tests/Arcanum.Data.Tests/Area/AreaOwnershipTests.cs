using Arcanum.Data.Area;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Data.Tests.Area.AreaIdentityTests;

namespace Arcanum.Data.Tests.Area;

[TestClass]
public sealed class AreaOwnershipTests
{
    [TestMethod]
    public void Bootstrap_ordering_is_primitive_stable_and_does_not_mutate_input_references()
    {
        // Equal keys cannot pass AreaSourcePlan validation. Invoke the actual private
        // production helper to verify its stability without relaxing that validation.
        AreaInputIdentity[] identities = [
            new(new(AreaSourceRole.Prototype, int.MaxValue), 0, "a"),
            new(new(AreaSourceRole.Resource1, 3), 0, "b"),
            new(new(AreaSourceRole.Module, int.MaxValue), 0, "c"),
            new(new(AreaSourceRole.Prototype, int.MinValue), 0, "d"),
            new(new(AreaSourceRole.Resource1, -2), 0, "e"),
            new(new(AreaSourceRole.Module, int.MinValue), 0, "f"),
            new(new(AreaSourceRole.Resource1, 3), 0, "g"),
            new(new(AreaSourceRole.NormalGameMobile), 0, "h"),
            new(new(AreaSourceRole.Resource2), 0, "i"),
            new(new(AreaSourceRole.Resource1, 3), 0, "j")];
        var original = identities.ToArray();
        var ordered = Order(identities);
        int[] expected = [5, 2, 4, 1, 6, 9, 8, 7, 3, 0];
        for (var i = 0; i < expected.Length; i++) Assert.AreSame(identities[expected[i]], ordered[i]);
        for (var i = 0; i < original.Length; i++) Assert.AreSame(original[i], identities[i]);
        Assert.AreNotSame(identities, ordered);
        Assert.AreEqual(0, Order([]).Length);
        Assert.AreSame(identities[0], Order([identities[0]])[0]);
        var reverse = Order([identities[0], identities[3], identities[7], identities[8], identities[1], identities[4], identities[2], identities[5]]);
        int[] reversedExpected = [5, 2, 4, 1, 8, 7, 3, 0];
        for (var i = 0; i < reversedExpected.Length; i++) Assert.AreSame(identities[reversedExpected[i]], reverse[i]);

        static AreaInputIdentity[] Order(AreaInputIdentity[] values) =>
            (AreaInputIdentity[])typeof(AreaDataOwner).GetMethod("OrderedInputs", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
                .Invoke(null, [Array.AsReadOnly(values)])!;
    }

    [TestMethod]
    public void Open_authenticates_interleaved_inputs_in_literal_role_then_number_order()
    {
        AreaInputKey[] keys = [new(AreaSourceRole.Prototype, 7), new(AreaSourceRole.Resource2, 9),
            new(AreaSourceRole.Module, int.MaxValue), new(AreaSourceRole.NormalGameMobile, -1),
            new(AreaSourceRole.Resource1, 3), new(AreaSourceRole.Prototype, -7),
            new(AreaSourceRole.Module, int.MinValue), new(AreaSourceRole.Resource1, -3), new(AreaSourceRole.Module)];
        var bytes = Dat();
        var streams = keys.Select(_ => new ProbeStream(bytes, false)).ToArray();
        var seen = new List<AreaInputKey>();
        for (var i = 0; i < keys.Length; i++)
        {
            var key = keys[i];
            streams[i].BeforeRead = (_, _) => { if (!seen.Contains(key)) seen.Add(key); };
        }
        var plan = new AreaSourcePlan(1, 1, new(1, 1), 2, "maps/test", keys.Select(k => new AreaInputIdentity(k, bytes.Length, Hash(bytes))), []);
        using (var owner = AreaDataOwner.Open(new(keys.Select((k, i) => new AreaInput(k, streams[i], leaveOpen: true))), plan, new()))
        {
            CollectionAssert.AreEqual(new AreaInputKey[] { new(AreaSourceRole.Module, int.MinValue), new(AreaSourceRole.Module),
                new(AreaSourceRole.Module, int.MaxValue), new(AreaSourceRole.Resource1, -3), new(AreaSourceRole.Resource1, 3),
                new(AreaSourceRole.Resource2, 9), new(AreaSourceRole.NormalGameMobile, -1), new(AreaSourceRole.Prototype, -7), new(AreaSourceRole.Prototype, 7) }, seen);
            CollectionAssert.AreEqual(keys, plan.Identities.Select(i => i.Key).ToArray());
        }
        Assert.IsTrue(streams.All(s => !s.Disposed));
        foreach (var stream in streams) stream.Dispose();
    }

    [TestMethod]
    public void Open_empty_singleton_and_invalid_identity_sets_keep_validation_and_lifetimes()
    {
        var empty = new AreaSourcePlan(1, 1, new(1, 1), 2, "maps/test", [], []);
        var error = Throws<AreaException>(() => AreaDataOwner.Open(new([]), empty, new()));
        Assert.AreEqual(AreaErrorCode.MissingSource, error.Issue.Code);
        var bytes = Dat(); var key = new AreaInputKey(AreaSourceRole.Module);
        var identity = new AreaInputIdentity(key, bytes.Length, Hash(bytes));
        Throws<ArgumentException>(() => new AreaSourcePlan(1, 1, new(1, 1), 2, "maps/test", [identity, identity], []));
        foreach (var leaveOpen in new[] { false, true })
        {
            using var stream = new ProbeStream(bytes, false);
            var input = new AreaInputSet([new AreaInput(key, stream, leaveOpen)]);
            var plan = new AreaSourcePlan(1, 1, new(1, 1), 2, "maps/test", [identity], []);
            using (var owner = AreaDataOwner.Open(input, plan, new())) Assert.IsTrue(stream.Reads.Count > 0);
            Assert.AreEqual(!leaveOpen, stream.Disposed);
            using var mismatch = new ProbeStream(bytes, false);
            error = Throws<AreaException>(() => AreaDataOwner.Open(new([new AreaInput(key, mismatch, leaveOpen)]), empty, new()));
            Assert.AreEqual(AreaErrorCode.MissingSource, error.Issue.Code);
            Assert.AreEqual(0, mismatch.Reads.Count);
            Assert.AreEqual(!leaveOpen, mismatch.Disposed);
        }
    }

    [TestMethod]
    public void Open_bootstrap_and_authentication_boundaries_reject_without_reading()
    {
        var bytes = Dat(); var key = new AreaInputKey(AreaSourceRole.Module);
        var plan = new AreaSourcePlan(1, 1, new(1, 1), 2, "maps/test", [new(key, bytes.Length, Hash(bytes))], []);
        foreach (var leaveOpen in new[] { false, true })
        foreach (var limits in new AreaLoaderOptions[] { new() { MaxManagedBytes = 66559 },
            new() { MaxRetainedBytes = 66559 }, new() { MaxManagedBytes = 136191 }, new() { MaxBuildPhaseBytes = 136191 } })
        {
            using var stream = new ProbeStream(bytes, false);
            var error = Throws<AreaException>(() => AreaDataOwner.Open(new([new AreaInput(key, stream, leaveOpen)]), plan, limits));
            Assert.AreEqual(AreaErrorCode.BudgetExceeded, error.Issue.Code);
            Assert.AreEqual(0, stream.Reads.Count);
            Assert.AreEqual(!leaveOpen, stream.Disposed);
        }
    }

    [TestMethod]
    public void Bootstrap_budget_rejection_precedes_first_authentication_and_honors_leave_open()
    {
        var bytes = Dat();
        var key = new AreaInputKey(AreaSourceRole.Module);
        var plan = new AreaSourcePlan(1, 1, new(1, 1), 2, "maps/test", [new(key, bytes.Length, Hash(bytes))], []);
        foreach (var leaveOpen in new[] { false, true })
        {
            using var stream = new ProbeStream(bytes, false);
            var inputs = new AreaInputSet([new AreaInput(key, stream, leaveOpen)]);
            Throws<AreaException>(() => AreaDataOwner.Open(inputs, plan, new() { MaxManagedBytes = 1 }));
            Assert.AreEqual(0, stream.Reads.Count);
            Assert.AreEqual(!leaveOpen, stream.Disposed);
        }
    }

    [TestMethod]
    public void Path_read_buffer_has_separate_persistent_admission_without_authentication_refunds()
    {
        var path = Path.Combine(Environment.GetEnvironmentVariable("TMPDIR") ?? AppContext.BaseDirectory, "area-budget-" + Guid.NewGuid().ToString("N") + ".dat");
        var bytes = Dat();
        File.WriteAllBytes(path, bytes);
        try
        {
            var key = new AreaInputKey(AreaSourceRole.Module);
            var plan = new AreaSourcePlan(1, 1, new(1, 1), 2, "maps/test", [new(key, bytes.Length, Hash(bytes))], []);
            using var memory = AreaDataOwner.Open(new([new AreaInput(key, new MemoryStream(bytes))]), plan, new());
            using var file = AreaDataOwner.Open(new([AreaInput.FromFile(key, path)]), plan, new());
            // Literal .NET8 64 KiB adapter buffer + header/alignment envelope;
            // expected values are not imported from the budget implementation.
            Assert.AreEqual(65568L, file.Usage.ManagedReserved - memory.Usage.ManagedReserved);
            Assert.AreEqual(65568L, file.Usage.RetainedReserved - memory.Usage.RetainedReserved);
            var before = file.Usage;
            Assert.IsFalse(file.Inspect().IsComplete); // empty archive has no start sector
            Assert.AreEqual(before.RetainedReserved, file.Usage.RetainedReserved);
            Assert.IsTrue(file.Usage.ManagedReserved > before.ManagedReserved);
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public void Publication_replacement_and_lease_release_drop_ownership_not_allocation_work()
    {
        using var fixture = new Fixture([(67108865, [Record(Id(0))])]);
        var first = fixture.Owner.LoadStrict(); var initial = fixture.Owner.Usage;
        var second = fixture.Owner.LoadStrict(); var repeated = fixture.Owner.Usage;
        Assert.AreEqual(initial.RetainedReserved, repeated.RetainedReserved);
        Assert.IsTrue(repeated.ManagedReserved > initial.ManagedReserved);
        var lease = fixture.Owner.AcquireSelectedFrame(second, second.Visuals[0].Key);
        Assert.IsTrue(fixture.Owner.Usage.RetainedReserved > repeated.RetainedReserved);
        var copy = lease.CopyRgba();
        CollectionAssert.AreEqual(new byte[] { 1,2,3,255,4,5,6,255 }, copy);
        copy[0] = 91;
        Assert.AreEqual((byte)1, lease.ReadPixelByte(0));
        var allocated = fixture.Owner.Usage.ManagedReserved;
        lease.Dispose(); lease.Dispose();
        Assert.AreEqual(repeated.RetainedReserved, fixture.Owner.Usage.RetainedReserved);
        Assert.AreEqual(allocated, fixture.Owner.Usage.ManagedReserved);
        Throws<ObjectDisposedException>(() => lease.ReadPixelByte(0));
        fixture.Owner.Dispose();
        Assert.IsTrue(fixture.Owner.Usage.RetainedReserved < repeated.RetainedReserved);
        Assert.AreEqual(allocated, fixture.Owner.Usage.ManagedReserved);
        Assert.AreEqual(64L, first.Objects[0].WorldX);
        Assert.AreEqual(4096, second.Terrain.Count);
        CollectionAssert.AreEqual(new byte[] { 91,2,3,255,4,5,6,255 }, copy);
    }

    [TestMethod]
    public void Late_survey_cancellation_unwinds_scratch_and_preserves_previous_publication()
    {
        var streams = new Dictionary<AreaSourceRole, ProbeStream>();
        using var fixture = Make(streams);
        var old = fixture.Owner.LoadStrict(); var before = fixture.Owner.Usage;
        using var cancel = new CancellationTokenSource();
        var module = streams[AreaSourceRole.Module];
        module.AfterRead = (position, requested, _) => { if (position == 0 && requested < module.Length) cancel.Cancel(); };
        Throws<OperationCanceledException>(() => fixture.Owner.Inspect(cancel.Token));
        Assert.AreSame(old, fixture.Owner.PublishedSnapshot);
        Assert.AreEqual(before.RetainedReserved, fixture.Owner.Usage.RetainedReserved);
        Assert.IsTrue(fixture.Owner.Usage.ManagedReserved > before.ManagedReserved);
        Assert.IsTrue(fixture.Owner.Usage.ReadActual > before.ReadActual);
        module.AfterRead = null;
        using var lease = fixture.Owner.AcquireSelectedFrame(old, old.Visuals[0].Key);
        Assert.AreEqual((byte)1, lease.ReadPixelByte(0));
    }

    [TestMethod]
    public void Failed_frame_read_keeps_old_snapshot_and_unwinds_component_and_payload_scopes()
    {
        var streams = new Dictionary<AreaSourceRole, ProbeStream>();
        using var fixture = Make(streams);
        var old = fixture.Owner.LoadStrict(); var before = fixture.Owner.Usage;
        streams[AreaSourceRole.Resource1].BeforeRead = (_, requested) => { if (requested == 1186) throw new IOException("synthetic fault"); };
        var report = fixture.Owner.Inspect();
        Assert.IsFalse(report.IsComplete); Assert.IsNull(report.Snapshot);
        Assert.AreSame(old, fixture.Owner.PublishedSnapshot);
        Assert.AreEqual(before.RetainedReserved, fixture.Owner.Usage.RetainedReserved);
        Assert.IsTrue(fixture.Owner.Usage.ManagedReserved > before.ManagedReserved);
        Assert.IsTrue(report.Issues.All(i => !i.ToString().Contains("synthetic fault", StringComparison.Ordinal)));
        streams[AreaSourceRole.Resource1].BeforeRead = null;
        using var lease = fixture.Owner.AcquireSelectedFrame(old, old.Visuals[0].Key);
        Assert.AreEqual((byte)6, lease.ReadPixelByte(6));
    }

    [TestMethod]
    public void Legal_one_byte_streams_charge_requested_remaining_work_without_changing_snapshot()
    {
        using var ordinary = new Fixture([(67108865, [Record(Id(0))])]);
        var streams = new Dictionary<AreaSourceRole, ProbeStream>();
        using var shortReads = Make(streams, oneByte: true);
        var a = ordinary.Owner.LoadStrict(); var b = shortReads.Owner.LoadStrict();
        Assert.AreEqual(a.Reconciliation, b.Reconciliation);
        CollectionAssert.AreEqual(a.Objects.Select(o => o.Key).ToArray(), b.Objects.Select(o => o.Key).ToArray());
        Assert.AreEqual(ordinary.Owner.Usage.ReadActual, shortReads.Owner.Usage.ReadActual);
        Assert.IsTrue(shortReads.Owner.Usage.ReadReserved > shortReads.Owner.Usage.ReadActual);
        Assert.IsTrue(streams.Values.All(s => s.Reads.All(r => r.Actual == 1)));
        using var lease = shortReads.Owner.AcquireSelectedFrame(b, b.Visuals[0].Key);
        CollectionAssert.AreEqual(new byte[] { 1,2,3,255,4,5,6,255 }, lease.CopyRgba());
    }

    [TestMethod]
    public void Input_mutation_after_build_before_publication_is_authenticated_and_atomic()
    {
        var streams = new Dictionary<AreaSourceRole, ProbeStream>();
        using var fixture = Make(streams);
        var old = fixture.Owner.LoadStrict(); var before = fixture.Owner.Usage;
        var module = streams[AreaSourceRole.Module]; var authentications = 0;
        module.BeforeRead = (position, requested) =>
        {
            if (position == 0 && requested == module.Length && ++authentications == 2) module.Bytes[0] ^= 1;
        };
        var report = fixture.Owner.Inspect();
        Assert.IsFalse(report.IsComplete);
        Assert.IsTrue(report.Issues.Any(i => i.Code == AreaErrorCode.InputIdentityMismatch));
        Assert.AreSame(old, fixture.Owner.PublishedSnapshot);
        Assert.AreEqual(before.RetainedReserved, fixture.Owner.Usage.RetainedReserved);
        Assert.AreEqual(2, authentications);
        Assert.IsTrue(fixture.Owner.Usage.ManagedReserved > before.ManagedReserved);
        module.Bytes[0] ^= 1; module.BeforeRead = null;
        Assert.IsTrue(fixture.Owner.Inspect().IsComplete);
    }

    [TestMethod]
    public void Provider_cancel_unwinds_hold_and_owner_disposal_revokes_lease_but_not_copy()
    {
        var streams = new Dictionary<AreaSourceRole, ProbeStream>();
        using var fixture = Make(streams);
        var snapshot = fixture.Owner.LoadStrict(); var before = fixture.Owner.Usage;
        using var cancel = new CancellationTokenSource();
        streams[AreaSourceRole.Resource1].AfterRead = (_, requested, _) => { if (requested == 1186) cancel.Cancel(); };
        Throws<OperationCanceledException>(() => fixture.Owner.AcquireSelectedFrame(snapshot, snapshot.Visuals[0].Key, cancel.Token));
        Assert.AreEqual(before.RetainedReserved, fixture.Owner.Usage.RetainedReserved);
        Assert.AreSame(snapshot, fixture.Owner.PublishedSnapshot);
        streams[AreaSourceRole.Resource1].AfterRead = null;
        var lease = fixture.Owner.AcquireSelectedFrame(snapshot, snapshot.Visuals[0].Key);
        Throws<AreaException>(() => fixture.Owner.AcquireSelectedFrame(snapshot, snapshot.Visuals[0].Key));
        var copy = lease.CopyRgba(); fixture.Owner.Dispose();
        Throws<ObjectDisposedException>(() => lease.CopyRgba());
        Throws<ObjectDisposedException>(() => fixture.Owner.Inspect());
        lease.Dispose();
        CollectionAssert.AreEqual(new byte[] { 1,2,3,255,4,5,6,255 }, copy);
        Assert.IsTrue(streams.Values.All(s => !s.Disposed)); // leaveOpen contract
    }

    [TestMethod]
    public async Task Concurrent_disposal_waits_for_authenticated_operation_then_revokes_provider()
    {
        var streams = new Dictionary<AreaSourceRole, ProbeStream>();
        using var fixture = Make(streams);
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        streams[AreaSourceRole.Module].BeforeRead = (position, requested) =>
        {
            if (position == 0 && requested == streams[AreaSourceRole.Module].Length)
            {
                entered.Set();
                if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("Test release timed out.");
            }
        };
        var inspect = Task.Run(() => fixture.Owner.LoadStrict());
        Task? dispose = null;
        try
        {
            Assert.IsTrue(entered.Wait(TimeSpan.FromSeconds(10)));
            dispose = Task.Run(fixture.Owner.Dispose);
            Assert.IsFalse(dispose.IsCompleted);
        }
        finally { release.Set(); }
        var snapshot = await inspect.WaitAsync(TimeSpan.FromSeconds(10));
        await dispose!.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.AreEqual(4096, snapshot.Terrain.Count);
        Throws<ObjectDisposedException>(() => fixture.Owner.AcquireSelectedFrame(snapshot, snapshot.Visuals[0].Key));
    }

    [TestMethod]
    public void Cancellation_in_each_io_phase_preserves_publication_and_releases_temporary_ownership()
    {
        foreach (var phase in new[] { AreaBudgetPhase.I0, AreaBudgetPhase.I1, AreaBudgetPhase.I2, AreaBudgetPhase.I3, AreaBudgetPhase.I4, AreaBudgetPhase.I5, AreaBudgetPhase.I7 })
        {
            var streams = new List<ProbeStream>();
            using var fixture = new Fixture([(67108865, [Record(Id(0))])], [Record(Id(1, 42), 274877907008)],
                streamFactory: (_, bytes) => { var s = new ProbeStream(bytes, false); streams.Add(s); return s; });
            var old = fixture.Owner.LoadStrict(); var retained = fixture.Owner.Usage.RetainedReserved;
            using var cancel = new CancellationTokenSource(); var reached = false;
            foreach (var stream in streams) stream.BeforeRead = (_, _) =>
            {
                var current = fixture.Owner.PhaseHistory.Last(p => p.Visits > 1).Phase;
                if (current == phase) { reached = true; cancel.Cancel(); }
            };
            // Before this attempt only O and one visit of I0..I7 exist.
            var allocated = GC.GetTotalAllocatedBytes(precise: true);
            var watch = System.Diagnostics.Stopwatch.StartNew();
            Throws<OperationCanceledException>(() => fixture.Owner.Inspect(cancel.Token));
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { CancelledPhase = phase.ToString(),
                AllocatedBytes = GC.GetTotalAllocatedBytes(precise: true) - allocated, ElapsedMilliseconds = watch.ElapsedMilliseconds,
                fixture.Owner.Usage }));
            Assert.IsTrue(reached, $"Cancellation phase {phase} was not reached.");
            Assert.AreSame(old, fixture.Owner.PublishedSnapshot);
            Assert.AreEqual(retained, fixture.Owner.Usage.RetainedReserved);
            foreach (var stream in streams) stream.BeforeRead = null;
            using var lease = fixture.Owner.AcquireSelectedFrame(old, old.Visuals[0].Key);
            CollectionAssert.AreEqual(new byte[] { 1,2,3,255,4,5,6,255 }, lease.CopyRgba());
        }
    }

    private static Fixture Make(Dictionary<AreaSourceRole, ProbeStream> streams, bool oneByte = false) =>
        new([(67108865, [Record(Id(0))])], streamFactory: (key, bytes) =>
        {
            var stream = new ProbeStream(bytes, oneByte); streams.Add(key.Role, stream); return stream;
        });

    private sealed class ProbeStream(byte[] bytes, bool oneByte) : Stream
    {
        private readonly MemoryStream _inner = new(bytes);
        internal byte[] Bytes => bytes;
        internal bool Disposed { get; private set; }
        internal Action<long, int>? BeforeRead { get; set; }
        internal Action<long, int, int>? AfterRead { get; set; }
        internal List<(long Position, int Requested, int Actual)> Reads { get; } = [];
        public override bool CanRead => _inner.CanRead;
        public override bool CanSeek => _inner.CanSeek;
        public override bool CanWrite => false;
        public override long Length => _inner.Length;
        public override long Position { get => _inner.Position; set => _inner.Position = value; }
        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
        public override int Read(Span<byte> buffer)
        {
            var at = Position; var requested = buffer.Length; BeforeRead?.Invoke(at, requested);
            var actual = _inner.Read(oneByte && buffer.Length > 1 ? buffer[..1] : buffer);
            Reads.Add((at, requested, actual)); AfterRead?.Invoke(at, requested, actual); return actual;
        }
        public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);
        public override void Flush() { }
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { Disposed = true; if (disposing) _inner.Dispose(); base.Dispose(disposing); }
    }
}
