using Arcanum.Scripting.GlobalFlags;
using Arcanum.Simulation.Clock;
using Arcanum.Simulation.Movement;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Session.Tests;

internal static class SessionFixtures
{
    internal static readonly WorldId World = new(Guid.Parse("11111111-1111-1111-1111-111111111111"));
    internal static readonly ModuleId Module = new(Guid.Parse("22222222-2222-2222-2222-222222222222"));
    internal static readonly GlobalFlagIdentity Identity = new(World, Module);
    internal static readonly OwnerEpoch Epoch = new(Guid.Parse("33333333-3333-3333-3333-333333333333"));
    internal static readonly OwnerEpoch RestoreEpoch = new(Guid.Parse("44444444-4444-4444-4444-444444444444"));
    internal static readonly SessionMapId Exterior = new(79);
    internal static readonly SessionMapId Cave = new(80);
    internal static readonly ActorPlacement Start = new(Exterior, new(100, 200), NeighborDirection.XPlus);

    internal static ProfileBinding Profile(int hour = 15) => new(1, "steamwoven-normalized-clock-v1",
        "9bd80511f8663a41a25a20a701959e394e0065e4ffc68a8dc69fe19c23678d52", new string('a', 64), new string('c', 64), 1885, hour);

    internal static GlobalFlagOwnerDescriptor Flags(GlobalFlagIdentity? identity = null) => new(identity ?? Identity, Epoch, 0,
        [new FlagId(3), new FlagId(7), new FlagId(9)],
        [new GlobalFlagBootstrapValue(new FlagId(3), false), new GlobalFlagBootstrapValue(new FlagId(7), true)]);

    internal static GameSessionDescriptor Descriptor(GameSessionLimits? limits = null) => new(Profile(), Flags(), Start, limits);

    internal static GameSessionRestoreOptions RestoreOptions(GameSessionLimits? limits = null) =>
        new(Identity, Profile(), RestoreEpoch, limits);

    internal static GameSessionOwner Create(GameSessionLimits? limits = null)
    {
        var result = GameSessionOwner.Create(Descriptor(limits));
        Assert.AreEqual(GameSessionFailure.None, result.Failure);
        Assert.IsNotNull(result.Owner);
        return result.Owner;
    }

    internal static GameSessionOwner Restore(GameSessionSnapshot snapshot, GameSessionRestoreOptions? options = null)
    {
        var result = GameSessionOwner.Restore(snapshot, options ?? RestoreOptions());
        Assert.AreEqual(GameSessionFailure.None, result.Failure);
        Assert.IsNotNull(result.Owner);
        return result.Owner;
    }

    internal static GameSessionMutationResult Changed(GameSessionMutationResult result, GameSessionOwner owner, long revision)
    {
        Assert.AreEqual(GameSessionDisposition.Changed, result.Disposition);
        Assert.AreEqual(GameSessionFailure.None, result.Failure);
        Assert.AreEqual(new GameSessionVersion(owner.Identity, revision), result.Version);
        Assert.AreEqual(result.Version, owner.Version);
        return result;
    }

    internal static void Rejected(GameSessionMutationResult result, GameSessionOwner owner, GameSessionFailure failure, long revision)
    {
        Assert.AreEqual(GameSessionDisposition.Rejected, result.Disposition);
        Assert.AreEqual(failure, result.Failure);
        Assert.AreEqual(new GameSessionVersion(owner.Identity, revision), result.Version);
        Assert.AreEqual(result.Version, owner.Version);
    }

    internal static bool? ReadFlag(GameSessionOwner owner, int flag)
    {
        var read = owner.Flags.Read(new(new(Guid.Parse("55555555-5555-5555-5555-555555555555")), owner.FlagVersion, new FlagId(flag)));
        return read.Value;
    }

    internal static GlobalFlagWriteReceipt WriteFlag(GameSessionOwner owner, int flag, bool value)
    {
        return owner.Flags.Write(new(new(Guid.Parse("66666666-6666-6666-6666-666666666666")), owner.FlagVersion, new FlagId(flag), value));
    }

    // Independently written canonical text of the fixture session after one sector and one entity change
    // and recorded lighting (scheme 1 at 15:00), with an empty party and inventory, no hero, no roll from seed 0 and no life record.
    internal const string LightingTail = ",\"lighting\":{\"schemeId\":1,\"hour\":15}}";
    internal const string GoldenJson =
        "{\"format\":\"arcanum-steamwoven-session\",\"schemaVersion\":17," +
        "\"world\":\"11111111-1111-1111-1111-111111111111\",\"module\":\"22222222-2222-2222-2222-222222222222\",\"revision\":2," +
        "\"clock\":{\"schemaVersion\":1,\"policyId\":\"steamwoven-normalized-clock-v1\"," +
        "\"sourceRevision\":\"9bd80511f8663a41a25a20a701959e394e0065e4ffc68a8dc69fe19c23678d52\"," +
        "\"sourceDataSha256\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\"," +
        "\"startupResourceSha256\":\"cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc\"," +
        "\"startYear\":1885,\"startHour\":15,\"timestampMs\":54000000,\"paused\":false,\"pauseReason\":\"None\",\"revision\":0}," +
        "\"flags\":{\"revision\":0,\"entries\":[{\"flag\":3,\"value\":false},{\"flag\":7,\"value\":true},{\"flag\":9,\"value\":null}]}," +
        "\"actor\":{\"map\":79,\"x\":100,\"y\":200,\"facing\":\"XPlus\"}," +
        "\"maps\":[{\"map\":79,\"changes\":[{\"scope\":\"sector\",\"sector\":5,\"property\":\"door.open\",\"value\":1}," +
        "{\"scope\":\"entity\",\"entity\":\"npc-1\",\"property\":\"hp\",\"value\":12}]}]" + EmptyStory + EmptyParty + NoHero + EmptyInventory + NoEquipment + NoEffects + NoExperience + FreshRandom + NoLife + NoReactions + NoCombat + NoCombatMode + NoEvents + LightingTail;

    internal const string EmptyStory = ",\"story\":{\"quests\":[],\"globalQuests\":[],\"rumors\":[]}";
    internal const string EmptyParty = ",\"party\":{\"followers\":[],\"others\":[]}";
    internal const string NoHero = ",\"hero\":null";
    internal const string EmptyInventory = ",\"inventory\":{\"gold\":0,\"kit\":[],\"pc\":[]}";
    internal const string FreshRandom = ",\"random\":{\"seed\":\"0000000000000000\",\"draws\":0}";
    internal const string NoLife = ",\"life\":{\"hero\":null,\"critters\":[]}";
    internal const string NoReactions = ",\"reactions\":{\"records\":[]}";
    internal const string NoCombat = ",\"combat\":null";
    internal const string NoCombatMode = ",\"combatMode\":{\"hero\":false,\"critters\":[]}";
    internal const string NoEquipment = ",\"equipment\":[]";
    internal const string NoEffects = ",\"effects\":{\"hero\":[],\"definitions\":[],\"blessings\":[],\"curses\":[]}";
    internal const string NoExperience = ",\"experience\":{\"npcs\":[]}";
    internal const string NoEvents = ",\"events\":{\"registry\":1,\"nextSequence\":1,\"pending\":[]}";

    internal static GameSessionOwner GoldenSession()
    {
        var owner = Create();
        Changed(owner.SetMapChange(owner.Version, Exterior, MapChangeKey.ForEntity("npc-1", "hp"), 12), owner, 1);
        Changed(owner.SetMapChange(owner.Version, Exterior, MapChangeKey.ForSector(5, "door.open"), 1), owner, 2);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.RecordLighting(new(1, 15)).Disposition);
        return owner;
    }

    internal static GameSessionSnapshot Decode(string text)
    {
        var result = GameSessionSnapshotCodec.TryDecode(text);
        Assert.AreEqual(SnapshotDecodeFailure.None, result.Failure, result.Path);
        Assert.IsNotNull(result.Snapshot);
        return result.Snapshot;
    }
}
