using Arcanum.Executable.Prototypes;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Executable.Tests;

/// <summary>ARC-DEV-068: the prototype builder's typed refusals and the guest heap, on synthetic input (no original bytes).</summary>
[TestClass]
public sealed class PrototypeBuilderTests
{
    private sealed class NoFiles : IOriginalFileSource
    {
        public byte[]? Read(string path) => null;
        public IReadOnlyList<string> List(string directory) => [];
    }

    [TestMethod]
    public void BytesThatAreNotAnImageAreRefused()
    {
        var result = PrototypeBuilder.Build([1, 2, 3], new NoFiles());
        Assert.AreEqual(PrototypeBuildFailureKind.InvalidExecutable, result.Failure?.Kind);
        Assert.AreEqual(0, result.Files.Count);
        Assert.IsFalse(result.Succeeded);
    }

    [TestMethod]
    public void AnImageThatIsNotTheRetailExecutableIsRefusedByDefault()
    {
        var result = PrototypeBuilder.Build(PeImageTests.SyntheticImage(), new NoFiles());
        Assert.AreEqual(PrototypeBuildFailureKind.UnverifiedExecutable, result.Failure?.Kind);
    }

    [TestMethod]
    public void AnImageWithoutTheGamesTablesStopsAsATypedFailure()
    {
        // Verification off (research on another build): the module tables are not mapped in the synthetic image.
        var result = PrototypeBuilder.Build(PeImageTests.SyntheticImage(), new NoFiles(), new PrototypeBuildOptions { RequireVerifiedExecutable = false });
        Assert.AreEqual(PrototypeBuildFailureKind.StartupFault, result.Failure?.Kind);
        Assert.IsNotNull(result.Failure?.Fault);
        Assert.AreEqual(0, result.Files.Count);
    }

    [TestMethod]
    public void OptionLimitsAreValidated()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => PrototypeBuilder.Build([1], new NoFiles(), new PrototypeBuildOptions { MaxHeapBytes = 0 }));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => PrototypeBuilder.Build([1], new NoFiles(), new PrototypeBuildOptions { MaxHeapBytes = PrototypeBuilder.MaxHeapLimit + 1 }));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => PrototypeBuilder.Build([1], new NoFiles(), new PrototypeBuildOptions { MaxStepsPerCall = 0 }));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => PrototypeBuilder.Build([1], new NoFiles(), new PrototypeBuildOptions { MaxWrittenBytes = -1 }));
    }

    [TestMethod]
    public void DirectoryPatternsMatchLikeTheFileSearch()
    {
        Assert.IsTrue(PrototypeBuilder.BuildSession.Glob("*.scr", "03000x.SCR"));
        Assert.IsTrue(PrototypeBuilder.BuildSession.Glob("*.*", "a.b"));
        Assert.IsTrue(PrototypeBuilder.BuildSession.Glob("*", "anything"));
        Assert.IsTrue(PrototypeBuilder.BuildSession.Glob("a?c.art", "ABC.art"));
        Assert.IsFalse(PrototypeBuilder.BuildSession.Glob("*.art", "a.artx"));
        Assert.IsFalse(PrototypeBuilder.BuildSession.Glob("a?c", "ac"));
        Assert.IsFalse(PrototypeBuilder.BuildSession.Glob("*.*", "nodot"));
    }

    [TestMethod]
    public void TextModeDropsACarriageReturnOnlyBeforeALineFeed()
    {
        CollectionAssert.AreEqual(
            new byte[] { (byte)'a', (byte)'\n', (byte)'b', (byte)'\r', (byte)'c', (byte)'\n', (byte)'d', (byte)'\r' },
            PrototypeBuilder.BuildSession.StripCarriageReturns([(byte)'a', (byte)'\r', (byte)'\n', (byte)'b', (byte)'\r', (byte)'c', (byte)'\r', (byte)'\n', (byte)'d', (byte)'\r']));
        var untouched = new byte[] { 1, 2, 3 };
        Assert.AreSame(untouched, PrototypeBuilder.BuildSession.StripCarriageReturns(untouched));
    }

    [TestMethod]
    public void TheHeapHandsOutZeroedBlocksAndReusesFreedOnes()
    {
        var memory = new MachineMemory();
        var heap = new GuestHeap(memory, 4u << 20);
        var a = heap.Allocate(24);
        Assert.AreNotEqual(0u, a);
        Assert.AreEqual(0u, a % 16);
        memory.WriteU32(a, 0xDEADBEEF);
        Assert.IsTrue(heap.Free(a));
        var b = heap.Allocate(20);               // same 32-byte class: the freed block comes back, zeroed again
        Assert.AreEqual(a, b);
        Assert.AreEqual(0u, memory.ReadU32(b));
        Assert.AreEqual(1, heap.LiveBlocks);
        Assert.IsTrue(heap.Free(0), "free(NULL) is accepted");
        Assert.IsTrue(heap.IsLive(b));
        Assert.IsFalse(heap.IsLive(b + 4));
        Assert.IsFalse(heap.Free(b + 4), "an address that is not a block start");
        Assert.IsFalse(heap.Free(0x1234_0000), "an unknown address");
    }

    [TestMethod]
    public void ReallocCopiesAndFreesAndAnUnrequestedZeroBlockHoldsTheFillByte()
    {
        var memory = new MachineMemory();
        var heap = new GuestHeap(memory, 4u << 20, fill: 0xA5);
        var raw = heap.Allocate(16, zero: false);
        Assert.AreEqual(0xA5A5A5A5u, memory.ReadU32(raw));
        Assert.AreEqual(0u, memory.ReadU32(heap.Allocate(16, zero: true)));
        var block = heap.Allocate(16);
        memory.WriteU32(block, 0x1122_3344);
        var grown = heap.Reallocate(block, 100);
        Assert.AreNotEqual(0u, grown);
        Assert.AreEqual(0x1122_3344u, memory.ReadU32(grown));
        Assert.AreEqual(0u, heap.Reallocate(grown, 0), "size 0 frees");
        Assert.AreEqual(0u, heap.Reallocate(0x1234_0000, 8), "an unknown block");
        Assert.AreNotEqual(0u, heap.Reallocate(0, 8), "realloc(NULL, n) allocates");
    }

    [TestMethod]
    public void TheHeapLimitIsExplicit()
    {
        var heap = new GuestHeap(new MachineMemory(), 1u << 16);
        Assert.AreEqual(0u, heap.Allocate(1u << 17));
        var count = 0;
        while (heap.Allocate(1024) != 0 && count < 1000) count++;
        Assert.AreEqual(64, count);
        Assert.AreEqual(1u << 16, heap.HighWater);
    }
}
