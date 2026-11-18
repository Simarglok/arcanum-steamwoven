using Arcanum.Scripting.GlobalFlags;
using Arcanum.Session;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Clock;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;

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

    /// <summary>ARC-UI-088: a session with a hero (an invented half-elf male, every primary stat 8, level 1), as the viewer's hero start makes.</summary>
    internal static GameSessionOwner CreateWithHero(int runtimeMap = 1, int portrait = 0)
    {
        var hero = new SessionHeroDefinition(16089, CharacterRace.HalfElf, CharacterGender.Male, [8, 8, 8, 8, 8, 8, 8, 8, 0, 0], 1, 0, 0, null, null, 385, []) { Portrait = portrait };
        var load = new AreaHeroLoad(AreaHeroStatus.Ready, AreaStartingHero.Find("half-elf-male")!, hero, 2352, 0, null, "synthetic");
        return AreaTransitionCoordinator.CreateSession(Clock, Guid.NewGuid(), Guid.NewGuid(), AreaGridSession.Cell(AreaGridSession.Spawn), runtimeMap, load).Owner
            ?? throw new InvalidOperationException("SESSION_HERO");
    }

    /// <summary>Capture → canonical text → decode → restore, as a save and load would.</summary>
    internal static GameSessionOwner SaveAndLoad(GameSessionOwner session)
    {
        var decoded = GameSessionSnapshotCodec.TryDecode(GameSessionSnapshotCodec.Encode(session.Capture()));
        var snapshot = decoded.Snapshot ?? throw new InvalidOperationException("DECODE_" + decoded.Failure + " " + decoded.Path);
        var restored = GameSessionOwner.Restore(snapshot, new(snapshot.Identity, snapshot.Clock.Profile, new OwnerEpoch(Guid.NewGuid())));
        return restored.Owner ?? throw new InvalidOperationException("RESTORE_" + restored.Failure);
    }

    /// <summary>ARC-SCRIPT-011: the installed game's script content (module Arcanum), as the viewer opens it.</summary>
    internal static Arcanum.Data.Scripts.ScriptContent Scripts(string gameRoot)
    {
        var load = AreaContentRoot.OpenScripts(Path.Combine(gameRoot, "modules", "Arcanum.dat"));
        return load.Content ?? throw new InvalidOperationException($"SCRIPT_CONTENT_{load.Status} {load.Detail}");
    }
}
