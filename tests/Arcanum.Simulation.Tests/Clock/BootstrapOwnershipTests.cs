using System.Reflection;
using System.Runtime.CompilerServices;
using Arcanum.Simulation.Clock;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Simulation.Tests.Clock.ClockFixtures;

namespace Arcanum.Simulation.Tests.Clock;

[TestClass]
public sealed class BootstrapOwnershipTests
{
    [TestMethod]
    [DataRow(0, 1885, 0L, false)]
    [DataRow(15, 1885, 54000000L, true)]
    [DataRow(0, 0, 0L, false)]
    [DataRow(23, 2147483647, 82800000L, false)]
    public void ExplicitStartup(int hour, int year, long time, bool isDay)
    {
        var profile = Profile(hour, year);
        using var owner = Ready(profile);
        var snapshot = owner.QueryClockSnapshot();
        State(snapshot, snapshot.OwnerIdentity, profile, time, default, 0, 0, time,
            hour, 0, 0, 1, 1, year, isDay);
    }

    [TestMethod]
    public void MalformedProfilesFailBeforeReadinessWithoutNormalization()
    {
        var p = Profile();
        foreach (var profile in new[] { default(ProfileBinding), p with { ClockSchemaVersion = 0 },
            p with { ClockSchemaVersion = 2 }, p with { ClockSchemaVersion = -1 } })
            Rejected(GameClockOwner.Bootstrap(profile), ClockFailure.InvalidSchema);
        foreach (var profile in new[] {
            p with { PolicyId = null! }, p with { PolicyId = "" }, p with { PolicyId = " " + p.PolicyId },
            p with { PolicyId = p.PolicyId.ToUpperInvariant() }, p with { PolicyId = "source-observation-v1" },
            p with { SourceRevision = null! }, p with { SourceRevision = "" },
            p with { SourceRevision = p.SourceRevision.ToUpperInvariant() }, p with { SourceRevision = p.SourceRevision + " " },
            p with { SourceDataSha256 = null! }, p with { SourceDataSha256 = "" },
            p with { StartupResourceSha256 = null! }, p with { StartupResourceSha256 = "" },
            p with { StartYear = -1 }, p with { StartHour = -1 }, p with { StartHour = 24 },
            p with { StartHour = int.MaxValue }, p with { StartHour = int.MinValue } })
            Rejected(GameClockOwner.Bootstrap(profile), ClockFailure.InvalidProfile);
        foreach (var bad in new[] { new string('a', 63), new string('a', 65), new string('A', 64),
            new string('g', 64), new string('０', 64), new string(' ', 64), new string('a', 63) + "\n" })
        {
            Rejected(GameClockOwner.Bootstrap(p with { SourceDataSha256 = bad }), ClockFailure.InvalidProfile);
            Rejected(GameClockOwner.Bootstrap(p with { StartupResourceSha256 = bad }), ClockFailure.InvalidProfile);
        }
        using var allHex = Ready(p with { SourceDataSha256 = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef" });
        Assert.AreEqual(54000000L, allHex.QueryClockSnapshot().GameTimestampMs);
    }

    [TestMethod]
    public void ValidatedFriendDescriptorsCannotImportUnsupportedStateOrIdentity()
    {
        var valid = new ClockDescriptor(Profile(), 0, default, 1, 0);
        foreach (var descriptor in new[] { valid with { GameTimestampMs = -1 }, valid with { Revision = -1 },
            valid with { Generation = 0 }, valid with { Generation = 2 }, valid with { Generation = long.MaxValue },
            valid with { PauseDisposition = new(true, PauseReason.None) },
            valid with { PauseDisposition = new(false, PauseReason.Menu) },
            valid with { PauseDisposition = new(true, (PauseReason)99) } })
            Rejected(GameClockOwner.FromDescriptor(descriptor), ClockFailure.InvalidState);
        Rejected(GameClockOwner.FromDescriptor(valid with { ProfileBinding = Profile() with { ClockSchemaVersion = 2 } }), ClockFailure.InvalidSchema);
        Rejected(GameClockOwner.FromDescriptor(valid with { ProfileBinding = Profile() with { StartHour = 24 } }), ClockFailure.InvalidProfile);
        using var first = At(0);
        using var second = At(0);
        Assert.AreNotSame(first.QueryClockSnapshot().OwnerIdentity, second.QueryClockSnapshot().OwnerIdentity);
    }

    [TestMethod]
    public void QueriesAreEqualDetachedAndIndependentOwnersHaveDistinctIdentity()
    {
        using var first = Ready(Profile());
        using var second = Ready(Profile());
        var old = first.QueryClockSnapshot();
        Assert.AreEqual(old, first.QueryClockSnapshot());
        Assert.AreNotSame(old.OwnerIdentity, second.QueryClockSnapshot().OwnerIdentity);
        Assert.AreEqual(old.ProfileBinding, second.QueryClockSnapshot().ProfileBinding);
        Assert.AreEqual(1L, second.QueryClockSnapshot().Generation);
        Assert.AreEqual(0L, second.QueryClockSnapshot().Revision);
        var edited = old with { GameTimestampMs = 0, Revision = 123, ProfileBinding = Profile(0) };
        Assert.AreNotEqual(old, edited);
        Assert.AreEqual(old, first.QueryClockSnapshot());
        var changed = first.AdvanceGameTime(old.Token, 1, ClockAdvanceReason.OrdinaryAdvance);
        State(changed.Snapshot, old.OwnerIdentity, Profile(), 54000001, default, 1,
            0, 54000001, 15, 0, 0, 1, 1, 1885, true);
        State(old, old.OwnerIdentity, Profile(), 54000000, default, 0,
            0, 54000000, 15, 0, 0, 1, 1, 1885, true);
        Assert.AreEqual(ClockMutationDisposition.Changed, changed.Disposition);
        Assert.AreEqual(ClockFailure.None, changed.Failure);
        State(second.QueryClockSnapshot(), second.QueryClockSnapshot().OwnerIdentity, Profile(), 54000000,
            default, 0, 0, 54000000, 15, 0, 0, 1, 1, 1885, true);
    }

    [TestMethod]
    public void DisposalIsIdempotentAndHistoriesRemainReadable()
    {
        var owner = Ready(Profile());
        var old = owner.QueryClockSnapshot();
        var paused = owner.SetPaused(old.Token, new(true, PauseReason.User)).Snapshot;
        owner.Dispose();
        owner.Dispose();
        Assert.ThrowsExactly<ObjectDisposedException>(() => owner.QueryClockSnapshot());
        Assert.ThrowsExactly<ObjectDisposedException>(() => owner.AdvanceGameTime(default, -1, (ClockAdvanceReason)99));
        Assert.ThrowsExactly<ObjectDisposedException>(() => owner.SetPaused(default, new(true, (PauseReason)99)));
        State(old, old.OwnerIdentity, Profile(), 54000000, default, 0, 0, 54000000, 15, 0, 0, 1, 1, 1885, true);
        State(paused, old.OwnerIdentity, Profile(), 54000000, new(true, PauseReason.User), 1,
            0, 54000000, 15, 0, 0, 1, 1, 1885, true);
    }

    [TestMethod]
    public void PublicSurfaceIsBoundedAndValueTypesAreReadonly()
    {
        Assert.AreEqual(0, typeof(GameClockOwner).GetConstructors().Length);
        Assert.AreEqual(0, typeof(ClockOwnerIdentity).GetConstructors().Length);
        Assert.AreEqual(0, typeof(ClockOwnerIdentity).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Length);
        Assert.AreEqual(typeof(object), typeof(ClockOwnerIdentity).GetMethod(nameof(object.Equals), [typeof(object)])!.DeclaringType);
        Assert.IsFalse(typeof(GameClockOwner).GetProperties().Any(p => p.SetMethod is { IsPublic: true }));
        CollectionAssert.AreEquivalent(new[] { "Bootstrap", "QueryClockSnapshot", "AdvanceGameTime", "SetPaused", "Dispose" },
            typeof(GameClockOwner).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly).Select(m => m.Name).ToArray());
        Assert.AreEqual(typeof(long), typeof(ClockSnapshot).GetProperty(nameof(ClockSnapshot.Year))!.PropertyType);
        foreach (var type in new[] { typeof(ProfileBinding), typeof(ClockToken), typeof(PauseDisposition),
            typeof(ClockSnapshot), typeof(ClockMutationResult), typeof(ClockBootstrapResult) })
            Assert.IsTrue(type.IsValueType && type.IsDefined(typeof(IsReadOnlyAttribute)));
        Assert.IsFalse(typeof(GameClockOwner).Assembly.GetReferencedAssemblies().Any(n => n.Name!.StartsWith("Arcanum.") || n.Name.StartsWith("Godot")));
    }
}
