using Arcanum.Scripting.GlobalFlags;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Scripting.Tests.GlobalFlags;

internal static class GlobalFlagFixture
{
    internal static readonly GlobalFlagIdentity Identity = new(
        new WorldId(Guid.Parse("11111111-1111-1111-1111-111111111111")),
        new ModuleId(Guid.Parse("22222222-2222-2222-2222-222222222222")));
    internal static readonly OwnerEpoch Epoch = new(Guid.Parse("33333333-3333-3333-3333-333333333333"));
    internal static readonly GlobalFlagRequestId RequestId = new(Guid.Parse("44444444-4444-4444-4444-444444444444"));
    internal static readonly Guid Other = Guid.Parse("55555555-5555-5555-5555-555555555555");
    internal static GlobalFlagVersion Version(long revision) => new(Identity, Epoch, revision);
    internal static GlobalFlagOwnerDescriptor Bootstrap(long revision = 0) => new(Identity, Epoch, revision,
        new[] { new FlagId(3), new FlagId(4), new FlagId(5), new FlagId(6), new FlagId(8), new FlagId(9) },
        new[] { new GlobalFlagBootstrapValue(new(3), false), new(new(4), true), new(new(5), false), new(new(6), false), new(new(8), false) });
    internal static GlobalFlagOwner Owner(long revision = 0)
    {
        var result = GlobalFlagOwner.Create(Bootstrap(revision));
        Assert.AreEqual(GlobalFlagReason.None, result.Reason);
        Assert.IsNotNull(result.Owner);
        return result.Owner;
    }

    // The oracle is a literal whole-state table, never a production comparison/snapshot helper.
    internal static void State(GlobalFlagOwner owner, long revision, params bool?[] values)
    {
        Assert.AreEqual(Version(revision), owner.Version);
        var snapshot = owner.CaptureSnapshot();
        Assert.AreEqual(Version(revision), snapshot.Version);
        Assert.AreEqual(6, snapshot.Entries.Count);
        CollectionAssert.AreEqual(new[] { 3, 4, 5, 6, 8, 9 }, snapshot.Entries.Select(e => e.Flag.Value).ToArray());
        CollectionAssert.AreEqual(values, snapshot.Entries.Select(e => e.Value).ToArray());
    }
}
