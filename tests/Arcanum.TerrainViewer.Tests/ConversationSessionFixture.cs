using Arcanum.Scripting.GlobalFlags;
using Arcanum.Session;
using Arcanum.Simulation.Clock;
using Arcanum.TerrainViewer.Core;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>A viewer game session for conversation tests: an invented synthetic clock profile and identities.</summary>
internal static class ConversationSessionFixture
{
    internal static readonly ProfileBinding Clock = new(1, AreaAmbientDriver.ClockPolicy, AreaAmbientDriver.ClockSourceRevision,
        new string('a', 64), new string('c', 64), 1885, 15);

    internal static GameSessionOwner Create(int runtimeMap = 1)
    {
        var created = AreaTransitionCoordinator.CreateSession(Clock, Guid.NewGuid(), Guid.NewGuid(), AreaGridSession.Cell(AreaGridSession.Spawn), runtimeMap);
        return created.Owner ?? throw new InvalidOperationException("SESSION_" + created.Failure);
    }

    /// <summary>Capture → canonical text → decode → restore, as a save and load would.</summary>
    internal static GameSessionOwner SaveAndLoad(GameSessionOwner session)
    {
        var decoded = GameSessionSnapshotCodec.TryDecode(GameSessionSnapshotCodec.Encode(session.Capture()));
        var snapshot = decoded.Snapshot ?? throw new InvalidOperationException("DECODE_" + decoded.Failure + " " + decoded.Path);
        var restored = GameSessionOwner.Restore(snapshot, new(snapshot.Identity, snapshot.Clock.Profile, new OwnerEpoch(Guid.NewGuid())));
        return restored.Owner ?? throw new InvalidOperationException("RESTORE_" + restored.Failure);
    }
}
