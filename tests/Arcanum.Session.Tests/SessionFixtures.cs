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
        "{\"format\":\"arcanum-steamwoven-session\",\"schemaVersion\":99," +
        "\"world\":\"11111111-1111-1111-1111-111111111111\",\"module\":\"22222222-2222-2222-2222-222222222222\",\"revision\":2," +
        "\"clock\":{\"schemaVersion\":1,\"policyId\":\"steamwoven-normalized-clock-v1\"," +
        "\"sourceRevision\":\"9bd80511f8663a41a25a20a701959e394e0065e4ffc68a8dc69fe19c23678d52\"," +
        "\"sourceDataSha256\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\"," +
        "\"startupResourceSha256\":\"cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc\"," +
        "\"startYear\":1885,\"startHour\":15,\"timestampMs\":54000000,\"paused\":false,\"pauseReason\":\"None\",\"revision\":0,\"realMs\":1,\"animationMs\":54000000}," +
        "\"flags\":{\"revision\":0,\"entries\":[{\"flag\":3,\"value\":false},{\"flag\":7,\"value\":true},{\"flag\":9,\"value\":null}]}," +
        "\"actor\":{\"map\":79,\"x\":100,\"y\":200,\"facing\":\"XPlus\"}," +
        "\"maps\":[{\"map\":79,\"changes\":[{\"scope\":\"sector\",\"sector\":5,\"property\":\"door.open\",\"value\":1}," +
        "{\"scope\":\"entity\",\"entity\":\"npc-1\",\"property\":\"hp\",\"value\":12}]}]" + EmptyStory + EmptyParty + NoHero + EmptyInventory + NoEquipment + NoDurability + NoSchematics + NoEffects + NoExperience + NoAdvancement + FreshRandom + NoLife + NoReactions + NoCombat + NoCombatMode + NoConcealment + NoDanger + NoSound + NoEvents + NoHotkeys + NoScripts + NoResidency + NoGoals + NoSpells + NoNewspaper + NoGenerators + NoFlights + NoEncounters + NoBarLights + LightingTail;

    internal const string QuelledRumors = ",\"quelledRumors\":[]";
    internal const string NoReputations = ",\"reputations\":[]";
    internal const string EmptyStory = ",\"story\":{\"quests\":[],\"globalQuests\":[],\"rumors\":[],\"quelledRumors\":[],\"knownAreas\":[],\"lastArea\":0" + NoReputations + NoStoryState + NoTownMaps + NoTownMapCells + NoHeroKeys + NoKillLog + NoTownNotes + NoMapNotes + "}";
    // ARC-SCRIPT-019: the story-state number of a new game.
    internal const string NoStoryState = ",\"storyState\":0";
    // ARC-DEV-205 (schema 71): no town map revealed whole.
    internal const string NoTownMaps = ",\"revealedTownMaps\":[]";
    // ARC-WORLD-122 (schema 75): no cell walked into on any town map.
    internal const string NoTownMapCells = ",\"townMapCells\":[]";
    // ARC-UI-081 (schema 77): no key in the PC's key ring.
    internal const string NoHeroKeys = ",\"heroKeys\":[]";
    // ARC-UI-082 (schema 78): the PC's kill log of a new game (no kill, six empty slots, no injury).
    internal const string NoKillLog = ",\"killLog\":{\"total\":0,\"records\":[[0,0],[0,0],[0,0],[0,0],[0,0],[0,0]],\"injuries\":[]}";
    // ARC-UI-104 (schema 80): no note on any town map.
    internal const string NoTownNotes = ",\"townNotes\":[]";
    // ARC-UI-110 (schema 82): no note on the world map's views.
    internal const string NoMapNotes = ",\"mapNotes\":[]";
    internal const string EmptyParty = ",\"party\":{\"followers\":[],\"others\":[]}";
    internal const string NoHero = ",\"hero\":null";
    internal const string EmptyInventory = ",\"inventory\":{\"gold\":0,\"kit\":[],\"pc\":[],\"made\":[],\"npcs\":[],\"placed\":[],\"ammo\":[],\"given\":[],\"piles\":[],\"scriptSlots\":[],\"weights\":[],\"identified\":[],\"lastDiscardedMade\":0,\"grid\":[],\"generated\":[],\"npcWorn\":[],\"itemFields\":[],\"holderSlots\":[]},\"objects\":{\"junkPiles\":[],\"bloodPools\":[],\"lastJunkNumber\":0,\"lastPoolNumber\":0,\"state\":[],\"links\":[],\"created\":[],\"lastCreatedNumber\":0}";
    internal const string FreshRandom = ",\"random\":{\"seed\":\"0000000000000000\",\"draws\":0}";
    internal const string NoLife = ",\"life\":{\"hero\":null,\"critters\":[]}";
    internal const string NoReactions = ",\"reactions\":{\"records\":[]}";
    internal const string NoCombat = ",\"combat\":null";
    internal const string NoCombatMode = ",\"combatMode\":{\"hero\":false,\"critters\":[]}";
    internal const string NoConcealment = ",\"concealment\":{\"hero\":false,\"npcs\":[]}";
    /// <summary>ARC-WORLD-071: no given items and no tracked gold piles.</summary>
    internal const string NoTransfers = ",\"given\":[],\"piles\":[]";
    /// <summary>ARC-WORLD-085: no weight rows.</summary>
    internal const string NoWeights = ",\"weights\":[]";
    // ARC-WORLD-079: no NPC danger record.
    internal const string NoDanger = ",\"danger\":{\"records\":[]}";
    internal const string NoSound = ",\"sound\":{\"slots\":[0,0],\"restorePending\":false,\"restore\":[0,0],\"combatMusic\":false,\"preCombat\":[0,0]}";
    internal const string NoEquipment = ",\"equipment\":[]";
    internal const string NoSchematics = ",\"schematics\":{\"found\":[]}";
    internal const string NoDurability = ",\"durability\":{\"items\":[],\"destroyed\":[],\"uses\":[],\"charges\":[]}";
    internal const string NoEffects = ",\"effects\":{\"hero\":[],\"definitions\":[],\"blessings\":[],\"curses\":[],\"critters\":[]}";
    /// <summary>ARC-DEV-348 (schema 87): the effects section's empty list of NPC effect lists, at its end.</summary>
    internal const string NoCritterEffects = ",\"critters\":[]";
    internal const string NoExperience = ",\"experience\":{\"npcs\":[]}";
    internal const string NoAdvancement = ",\"advancement\":{\"npcs\":[],\"missed\":[]}";
    // ARC-UI-022: an empty hotkey bar (ten empty entries, two empty recent actions).
    internal const string NoHotkeys = ",\"hotkeys\":{\"entries\":[" + EmptyHotkey + "," + EmptyHotkey + "," + EmptyHotkey + "," + EmptyHotkey + "," +
        EmptyHotkey + "," + EmptyHotkey + "," + EmptyHotkey + "," + EmptyHotkey + "," + EmptyHotkey + "," + EmptyHotkey + "],\"recent\":[" +
        EmptyHotkey + "," + EmptyHotkey + "]}";
    // ARC-DEV-098: the script stores of a session that wrote none (since schema 79 the global variables alone; the PC's arrays are the hero's, ARC-DEV-266).
    internal const string NoScripts = ",\"scripts\":{\"globalVariables\":[]}";
    // ARC-DEV-266: the same section of schemas 67..78, which also held the PC's arrays.
    internal const string NoScripts78 = ",\"scripts\":{\"globalVariables\":[],\"pcFlags\":[],\"pcVariables\":[]}";
    // ARC-DEV-266 (schema 79): the PC's arrays at the end of the hero's section when the hero holds none.
    internal const string NoHeroScripts = ",\"scripts\":{\"flags\":[],\"variables\":[]}";
    // ARC-DEV-106 (schema 68): no sector eviction history.
    internal const string NoResidency = ",\"residency\":{\"map\":null,\"history\":[]}";
    // ARC-DEV-208 (schema 72): no saved goal run (the next run id of a fresh table is 1).
    internal const string NoGoals = ",\"goals\":{\"nextRunId\":1,\"runs\":[]}";
    // ARC-DEV-302 (schema 83): the spells section ends with the list of written flags; NoSpells82 is the section of schemas 73..82.
    internal const string NoObjectFlags = ",\"objectFlags\":[]";
    internal const string NoTraitArt = ",\"traitArt\":[]";
    internal const string NoSpells82 = ",\"spells\":{\"runs\":[],\"bar\":[{\"run\":-1,\"state\":0},{\"run\":-1,\"state\":0},{\"run\":-1,\"state\":0},{\"run\":-1,\"state\":0},{\"run\":-1,\"state\":0}],\"lastCost\":0}";
    internal const string NoSpells = ",\"spells\":{\"runs\":[],\"bar\":[{\"run\":-1,\"state\":0},{\"run\":-1,\"state\":0},{\"run\":-1,\"state\":0},{\"run\":-1,\"state\":0},{\"run\":-1,\"state\":0}],\"lastCost\":0" + NoObjectFlags + NoTraitArt + "}";
    // ARC-DEV-230 (schema 74): the newspaper queue of a new game (four words of -1, the head 5000, the rest 0, count 5).
    internal const string NoNewspaper = ",\"newspaper\":{\"entries\":[-1,-1,-1,-1,5000,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0],\"count\":5}";
    // ARC-WORLD-1157 (schema 91): the generators section of a new game (no slot used, nothing made, no copy), written before the flights section.
    internal const string NoGenerators = ",\"generators\":{\"slots\":[],\"made\":[],\"copies\":[]}";
    // ARC-DEV-288 (schema 80): no projectile in flight, both counters at a new game's 0.
    // ARC-WORLD-1159 (schema 97): the encounters section of a new game (no row used), written after the flights section.
    // ARC-UI-1542 (schema 98): no lit state recorded, written between the encounters and the lighting sections.
    internal const string NoBarLights = ",\"barLights\":null";
    // ARC-MAGIC-1658 (schema 99): the hero's stored resistance array and age, at the end of the hero section (a hero that has none written and whose age was not read).
    internal const string NoHeroStoredKeys = ",\"resistances\":[0,0,0,0,0],\"age\":null";
    internal const string NoEncounters = ",\"encounters\":{\"uses\":[]}";
    internal const string NoFlights80 = ",\"flights\":{\"lastProjectile\":0,\"projectiles\":[],\"nextShot\":0,\"heroShots\":[],\"npcThrows\":[],\"npcArrows\":[]}";
    // ARC-DEV-289 (schema 84): the flights section ends with the missile counter and the missiles in flight.
    internal const string NoFlights = ",\"flights\":{\"lastProjectile\":0,\"projectiles\":[],\"nextShot\":0,\"heroShots\":[],\"npcThrows\":[],\"npcArrows\":[],\"lastMissile\":0,\"missiles\":[]}";
    internal const string EmptyHotkey = "{\"kind\":0,\"item\":null,\"index\":-1}";
    internal const string NoEvents = ",\"events\":{\"registry\":1,\"nextSequence\":1,\"pending\":[]}";

    // A snapshot text without the event queue's next sequence (a restored run gets a new step event, so the counter moves on a restore).
    internal static string Unsequenced(string snapshot) =>
        System.Text.RegularExpressions.Regex.Replace(snapshot, "\"nextSequence\":\\d+", "");

    // Also without the goal table's next run id (a run the build does not save still used one).
    internal static string Uncounted(string snapshot) =>
        System.Text.RegularExpressions.Regex.Replace(snapshot, "\"(nextSequence|nextRunId|lastMissile)\":\\d+", "");

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
