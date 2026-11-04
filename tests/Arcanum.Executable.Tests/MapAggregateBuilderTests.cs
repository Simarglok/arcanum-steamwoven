using Arcanum.Executable.Maps;
using Arcanum.Executable.Prototypes;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Executable.Tests;

/// <summary>ARC-DEV-082: the map aggregate builder's typed refusals, the module GUID rule and the session's file rules, on synthetic input (no original bytes).</summary>
[TestClass]
public sealed class MapAggregateBuilderTests
{
    private sealed class NoFiles : IOriginalFileSource
    {
        public byte[]? Read(string path) => null;
        public IReadOnlyList<string> List(string directory) => [];
    }

    private static readonly byte[] Guid16 = Enumerable.Range(1, 16).Select(i => (byte)i).ToArray();

    [TestMethod]
    public void TheModuleGuidIsTheSixteenBytesTheOriginalReadsFromTheArchivesEnd()
    {
        // 0x53BC50: the tag "1TAD" 12 bytes before the end, then 16 bytes from 24 bytes before the end (the last four of them are the tag).
        var footer = new byte[MapAggregateBuilder.ArchiveFooterBytes];
        for (var i = 0; i < footer.Length; i++) footer[i] = (byte)(0x40 + i);
        "1TAD"u8.CopyTo(footer.AsSpan(footer.Length - 12));
        var guid = MapAggregateBuilder.ModuleGuidFromArchiveFooter(footer);
        CollectionAssert.AreEqual(footer[4..20], guid);
        CollectionAssert.AreEqual("1TAD"u8.ToArray(), guid![12..], "the last four bytes of what the original takes are the tag itself");

        var old = new byte[MapAggregateBuilder.ArchiveFooterBytes];
        Array.Fill(old, (byte)0xEE);
        " DAT"u8.CopyTo(old.AsSpan(old.Length - 12));
        CollectionAssert.AreEqual(new byte[16], MapAggregateBuilder.ModuleGuidFromArchiveFooter(old), "the older format has no GUID");

        Assert.IsNull(MapAggregateBuilder.ModuleGuidFromArchiveFooter(new byte[MapAggregateBuilder.ArchiveFooterBytes]), "no tag: not an archive the original reads");
        Assert.IsNull(MapAggregateBuilder.ModuleGuidFromArchiveFooter(footer.AsSpan(0, 27)), "a short footer");
        Assert.IsNull(MapAggregateBuilder.ModuleGuidFromArchiveFooter([]));
    }

    [TestMethod]
    public void BytesThatAreNotAnImageAndAnImageThatIsNotTheRetailExecutableAreRefused()
    {
        var bad = MapAggregateBuilder.Build([1, 2, 3], new NoFiles(), Guid16);
        Assert.AreEqual(PrototypeBuildFailureKind.InvalidExecutable, bad.Failure?.Kind);
        Assert.IsFalse(bad.Succeeded);
        Assert.AreEqual(0, bad.Files.Count);
        var other = MapAggregateBuilder.Build(PeImageTests.SyntheticImage(), new NoFiles(), Guid16);
        Assert.AreEqual(PrototypeBuildFailureKind.UnverifiedExecutable, other.Failure?.Kind);
    }

    [TestMethod]
    public void AnImageWithoutTheGamesTablesStopsAsATypedFailure()
    {
        var result = MapAggregateBuilder.Build(PeImageTests.SyntheticImage(), new NoFiles(), Guid16, new MapAggregateBuildOptions { RequireVerifiedExecutable = false });
        Assert.AreEqual(PrototypeBuildFailureKind.StartupFault, result.Failure?.Kind);
        Assert.IsNotNull(result.Failure?.Fault);
        Assert.AreEqual(0, result.Files.Count);
    }

    [TestMethod]
    public void ArgumentsAndLimitsAreValidated()
    {
        Assert.ThrowsExactly<ArgumentException>(() => MapAggregateBuilder.Build([1], new NoFiles(), new byte[15]));
        Assert.ThrowsExactly<ArgumentNullException>(() => MapAggregateBuilder.Build(null!, new NoFiles(), Guid16));
        Assert.ThrowsExactly<ArgumentNullException>(() => MapAggregateBuilder.Build([1], null!, Guid16));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => MapAggregateBuilder.Build([1], new NoFiles(), Guid16, new MapAggregateBuildOptions { MaxHeapBytes = 0 }));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => MapAggregateBuilder.Build([1], new NoFiles(), Guid16, new MapAggregateBuildOptions { MaxHeapBytes = PrototypeBuilder.MaxHeapLimit + 1 }));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => MapAggregateBuilder.Build([1], new NoFiles(), Guid16, new MapAggregateBuildOptions { MaxStepsPerCall = 0 }));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => MapAggregateBuilder.Build([1], new NoFiles(), Guid16, new MapAggregateBuildOptions { MaxWrittenBytes = -1 }));
    }

    [TestMethod]
    public void AnAggregateIsAFlatFileDirectlyUnderMaps()
    {
        Assert.IsTrue(PrototypeBuilder.BuildSession.IsAggregateFile("maps\\1 krY cdvhcnZ hrinO"));
        Assert.IsTrue(PrototypeBuilder.BuildSession.IsAggregateFile("MAPS\\x"));
        Assert.IsFalse(PrototypeBuilder.BuildSession.IsAggregateFile("maps\\Arcanum1-024\\map.prp"), "a map's own file is in its folder");
        Assert.IsFalse(PrototypeBuilder.BuildSession.IsAggregateFile("maps\\"));
        Assert.IsFalse(PrototypeBuilder.BuildSession.IsAggregateFile("proto\\001001 - Wall.pro"));
        Assert.IsFalse(PrototypeBuilder.BuildSession.IsAggregateFile("mapsx\\a"));
    }

    [TestMethod]
    public void TheSourceListsFoldersOnlyWhenAskedAndAFolderlessSourceAnswersNone()
    {
        IOriginalFileSource source = new NoFiles();
        Assert.AreEqual(0, source.ListDirectories("maps").Count);
    }
}
