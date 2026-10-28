using Arcanum.Simulation.Equipment;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Arcanum.Data;
using Arcanum.Data.Area;
using Arcanum.Data.Terrain;
using Arcanum.Session;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

// ARC-COMBAT-005 viewer core: the attack playback, the status text, the facing, the fallen poses read on demand and the frame
// overlay that holds them. Archives, ART and records are synthetic.
[TestClass]
public sealed class AreaCombatTests
{
    internal static readonly string Digest = new('a', 64);

    [TestMethod]
    public void TheHeroFacesTheTargetsNeighbourDirection()
    {
        var from = new CellCoord(10, 10);
        Assert.AreEqual(NeighborDirection.XPlus, AreaCombatHost.Facing(from, new(11, 10)));
        Assert.AreEqual(NeighborDirection.XMinusYMinus, AreaCombatHost.Facing(from, new(9, 9)));
        Assert.AreEqual(NeighborDirection.YPlus, AreaCombatHost.Facing(from, new(10, 14)), "the direction of a step toward it");
        Assert.AreEqual(NeighborDirection.XPlusYMinus, AreaCombatHost.Facing(from, new(12, 8)));
        Assert.IsNull(AreaCombatHost.Facing(from, from));
    }

    [TestMethod]
    public void ThePlayerRunsTheHitOnceOnTheActionFrameAndEndsAfterTheLastFrame()
    {
        // The hero's bare-handed attack: 9 frames, action frame 6; Speed 8 gives 15 fps, 66 ms a frame.
        var clip = new AttackClip(9, 6, RealTimeCombatRules.AttackFramePauseMs(15, 10), AttackArtWeapon.Unarmed);
        Assert.AreEqual(66, clip.PauseMs);
        var player = new AreaAttackPlayer(clip, 1000);
        Assert.AreEqual((0, true, 1000L), (player.Playback.Frame, player.Playing, player.NextUpdateMs));
        Assert.AreEqual(0, player.Advance(999, () => true).Count, "nothing is due before the start");
        var first = player.Advance(1000, () => true);
        Assert.AreEqual(1, first.Count);
        Assert.AreEqual((AttackUpdateKind.Advanced, 1), (first[0].Update.Kind, first[0].Update.Playback.Frame));
        var rest = player.Advance(1000 + 66 * 20, () => true);
        Assert.AreEqual(1, rest.Count(u => u.Update.Kind == AttackUpdateKind.Hit));
        Assert.AreEqual(6, rest.Single(u => u.Update.Kind == AttackUpdateKind.Hit).Update.Playback.Frame);
        Assert.AreEqual(1000 + 66 * 5, rest.Single(u => u.Update.Kind == AttackUpdateKind.Hit).AtMs, "frame f at (f − 1) × pause");
        Assert.AreEqual(AttackUpdateKind.Ended, rest[^1].Update.Kind);
        Assert.IsFalse(player.Playing);
        Assert.AreEqual(0, player.Advance(long.MaxValue, () => true).Count);

        var stunned = new AreaAttackPlayer(clip, 0);
        stunned.Advance(0, () => true);
        Assert.AreEqual(AttackUpdateKind.Ended, stunned.Advance(66, () => false).Single().Update.Kind, "an attacker that cannot act stops");
        var aborted = new AreaAttackPlayer(clip, 0);
        aborted.Abort();
        Assert.IsFalse(aborted.Playing);
        Assert.AreEqual(3, new AreaAttackPlayer(clip, 0).Advance(1_000_000, () => true, limit: 3).Count, "a stall plays a bounded number of frames");
    }

    private static AttackResolution Resolution(AttackOutcome outcome, AttackMissCause cause = AttackMissCause.None, bool aimed = false,
        DamagePrevention prevented = DamagePrevention.None, DamageRecipient recipient = DamageRecipient.Target) =>
        new(AttackRefusal.None, outcome, cause, HitLocation.Head, aimed, false, null, null, null, FateUse.None, false,
            outcome is AttackOutcome.Hit or AttackOutcome.CriticalHit or AttackOutcome.CriticalMiss
                ? new AttackDamage(recipient, DamageByType.Zero, DamageByType.Zero, 4, 4, 0, DamageMultiplier.None, false, default, default, false, prevented)
                : null, []);

    private static SessionAttackResult Result(AttackResolution resolution, LifeOutcome? life = null, DamageRecipient? recipient = DamageRecipient.Target,
        int hitAward = 0, int killAward = 0) => new(life is null ? GameSessionDisposition.NoChange : GameSessionDisposition.Changed, GameSessionFailure.None,
        resolution, life is null ? null : recipient, life, hitAward == 0 ? null : new(hitAward, 0, 0, null), killAward == 0 ? null : new(killAward, 0, 0, null),
        GameSessionFailure.None, default);

    [TestMethod]
    public void TheStatusLineNamesTheOutcomeDamageAndExperience()
    {
        var hit = new LifeOutcome(LifeState.Unharmed, new(4, 4, 0, LifeConditions.None, 0), LifeEvents.None, 4, DeathAnimation.None);
        Assert.AreEqual("hit (aimed at the head): 4 damage, 4 fatigue, HP 6/10, +3 XP",
            AreaCombatHost.Describe(Result(Resolution(AttackOutcome.Hit, aimed: true), hit, hitAward: 3), 6, 10));
        var died = new LifeOutcome(new(8, 0, 0, LifeConditions.None, 0), new(32000, 0, 0, LifeConditions.None, 0), LifeEvents.Died, 5, DeathAnimation.Plain);
        Assert.AreEqual("critical hit: 5 damage, HP 0/10, killed, +35 XP",
            AreaCombatHost.Describe(Result(Resolution(AttackOutcome.CriticalHit), died, hitAward: 3, killAward: 32), -31990, 10));
        var knocked = new LifeOutcome(LifeState.Unharmed, new(1, 30, 0, LifeConditions.None, 0), LifeEvents.KnockedOut | LifeEvents.LostConsciousness, 1, DeathAnimation.None);
        StringAssert.EndsWith(AreaCombatHost.Describe(Result(Resolution(AttackOutcome.Hit), knocked), 9, 10), "knocked out");
        Assert.AreEqual("dodged", AreaCombatHost.Describe(Result(Resolution(AttackOutcome.Miss, AttackMissCause.Dodged)), 10, 10));
        Assert.AreEqual("miss", AreaCombatHost.Describe(Result(Resolution(AttackOutcome.Miss, AttackMissCause.SkillCheck)), 10, 10));
        var self = new LifeOutcome(LifeState.Unharmed, new(4, 0, 0, LifeConditions.None, 0), LifeEvents.None, 4, DeathAnimation.None);
        Assert.AreEqual("critical miss: the hero takes 4 damage",
            AreaCombatHost.Describe(Result(Resolution(AttackOutcome.CriticalMiss, recipient: DamageRecipient.Attacker), self, DamageRecipient.Attacker), 10, 10));
        Assert.AreEqual("hit: no damage (Invulnerable)",
            AreaCombatHost.Describe(Result(Resolution(AttackOutcome.Hit, prevented: DamagePrevention.Invulnerable)), 10, 10));
        Assert.AreEqual("attack refused: AttackUnsupported", AreaCombatHost.Describe(new(GameSessionDisposition.Rejected, GameSessionFailure.AttackUnsupported,
            null, null, null, null, null, GameSessionFailure.None, default), null, null));
        var corpse = Resolution(AttackOutcome.Refused) with { Refusal = AttackRefusal.TargetDead };
        Assert.AreEqual("attack: TargetDead", AreaCombatHost.Describe(Result(corpse), null, null));

        // ARC-COMBAT-009: injuries and the critical item steps.
        var crippled = new LifeOutcome(LifeState.Unharmed, new(4, 0, 0, LifeConditions.Blinded | LifeConditions.CrippledArmOne, 1),
            LifeEvents.Blinded | LifeEvents.CrippledArm | LifeEvents.Scarred, 4, DeathAnimation.None);
        var steps = new[]
        {
            new SessionCriticalStep(SessionCriticalStepKind.DropWeapon, DamageRecipient.Target, EquipmentSlot.Weapon, "sword", SessionCriticalStepOutcome.Dropped, new(1, 2)),
            new SessionCriticalStep(SessionCriticalStepKind.DropHelmet, DamageRecipient.Target, EquipmentSlot.Helmet, "cap", SessionCriticalStepOutcome.HeroCellUnfit, null),
            new SessionCriticalStep(SessionCriticalStepKind.ArmHitShield, DamageRecipient.Target, EquipmentSlot.Shield, "torch", SessionCriticalStepOutcome.Kept, null),
        };
        Assert.AreEqual("critical hit: 4 damage, HP 6/10, blinded, crippled, scarred, drops its weapon, would drop the helmet (no cell for it)",
            AreaCombatHost.Describe(Result(Resolution(AttackOutcome.CriticalHit), crippled) with { Criticals = steps }, 6, 10));
        Assert.AreEqual("None|None,DropWeapon:sword:Dropped,DropHelmet:cap:HeroCellUnfit,ArmHitShield:torch:Kept",
            AreaCombatHost.Criticals(Resolution(AttackOutcome.CriticalHit), steps));
    }

    internal static AreaObjectRecord Body(int scale = 100, uint flags = 0, int index = 7)
    {
        var key = new AreaObjectKey(Digest, 5051, AreaSourceKind.Mobile, null, index, index * 100, Digest, 100);
        var fields = new[] { Field(18, flags), Field(8, 0), Field(9, 0xFFFFFF), Field(10, 255), Field(11, scale), Field(36, 0) };
        return new(key, 119, 16, 28359, Digest, Digest, Digest, Digest, AreaScope.Core, null, 92930, 82565,
            Arcanum.Data.Area.AreaDisposition.BodyEligibleConditionally, fields, fields, true, true, true);
    }

    private static AreaFieldValue Field(int slot, long value) => new(slot, AreaPresence.Value, AreaValueOrigin.Instance, value, [], null);

    internal static AreaVisualRecord Visual(AreaObjectRecord body, AreaEntryIdentity entry, int width = 60, int height = 90, int hotX = 30, int hotY = 80,
        int scale = 100, uint art = 0x20000000, int group = 5, string policy = "synthetic") =>
        new(new(body.Key.Kind, new(Digest, 5051, 0), body.Key.SourceIndex, Digest, Object: body.Key), art, art, entry, Digest, 100, 5, group, group, 0, 0, 0,
            false, width, height, hotX, hotY, 0, 0, 0, 0, scale, 1, width * height * 4, Digest, policy);

    [TestMethod]
    public void AFallenPoseIsPlacedFromItsOwnHotspotAtTheBodysAnchor()
    {
        var body = Body();
        var visual = Visual(body, new(new(AreaSourceRole.Resource1), 1, 0, 100, 100, 0, Digest));
        var command = AreaScenePlan.Create([], [body], [visual]).Commands.Single();
        var frame = new AreaFrameKey(visual.Entry with { Ordinal = 2 }, 0, 5, 10, AreaFallenPoses.Policy, Digest, 100, Digest);
        var fallen = AreaScenePlan.Repose(command, body, visual, frame, 120, 40, 50, 30);
        Assert.AreEqual((command.Identity, command.Visual, command.Phase, command.MapX, command.MapY), (fallen.Identity, fallen.Visual, fallen.Phase, fallen.MapX, fallen.MapY));
        Assert.AreEqual(frame, fallen.Frame);
        Assert.AreEqual(new TerrainRect(0, 0, 120, 40), fallen.Source);
        Assert.AreEqual(new TerrainRect(command.Destination.X + 30 - 50, command.Destination.Y + 80 - 30, 120, 40), fallen.Destination);
        // A scaled body keeps its scale.
        var half = Body(scale: 50); var halfVisual = Visual(half, visual.Entry, scale: 50);
        var halfCommand = AreaScenePlan.Create([], [half], [halfVisual]).Commands.Single();
        var halfFallen = AreaScenePlan.Repose(halfCommand, half, halfVisual, frame, 120, 40, 50, 30);
        Assert.AreEqual(new TerrainRect(halfCommand.Destination.X + 15 - 25, halfCommand.Destination.Y + 40 - 15, 60, 20), halfFallen.Destination);
        Assert.ThrowsExactly<ArgumentException>(() => AreaScenePlan.Repose(command, Body(index: 8), visual, frame, 1, 1, 0, 0), "another object");
    }

    [TestMethod]
    public void TheOverlayAddsPosesWithoutReplacingTheMapsFrames()
    {
        var key = new AreaFrameKey(new(new(AreaSourceRole.Resource1), 1, 0, 1, 1, 0, Digest), 0, 0, 0, "map", Digest, 1, Digest);
        var map = new Dictionary<AreaFrameKey, DecodedAreaFrame> { [key] = new(1, 1, [1, 2, 3, 4]) };
        var overlay = new AreaFrameOverlay(map);
        var pose = key with { Policy = AreaFallenPoses.Policy };
        overlay.Add(pose, new(1, 1, [5, 6, 7, 8]));
        overlay.Add(pose, new(1, 1, [5, 6, 7, 8]));
        Assert.AreEqual(2, overlay.Count);
        Assert.IsTrue(overlay.ContainsKey(key) && overlay.ContainsKey(pose));
        Assert.AreEqual(5, overlay[pose].Rgba[0]);
        Assert.AreEqual(1, map.Count, "the map's repository is not changed");
        Assert.ThrowsExactly<InvalidOperationException>(() => overlay.Add(pose, new(1, 1, [0, 0, 0, 0])));
        Assert.ThrowsExactly<InvalidOperationException>(() => overlay.Add(key, new(1, 1, [9, 9, 9, 9])));
        Assert.ThrowsExactly<KeyNotFoundException>(() => _ = overlay[key with { Frame = 3 }]);
        CollectionAssert.AreEquivalent(new[] { key, pose }, overlay.Keys.ToArray());
    }

    [TestMethod]
    public void TheDyingAnimationFollowsTheHitLocation()
    {
        Assert.AreEqual(7, AreaFallenPoses.Animation(DeathAnimation.Plain));
        Assert.AreEqual(7, AreaFallenPoses.Animation(DeathAnimation.None));
        Assert.AreEqual(17, AreaFallenPoses.Animation(DeathAnimation.Head));
        Assert.AreEqual(19, AreaFallenPoses.Animation(DeathAnimation.Leg));
        Assert.AreEqual(18, AreaFallenPoses.Animation(DeathAnimation.Body));
    }

    [TestMethod]
    public void APoseIsReadFromTheBodysArchiveWithTheFallAsTheMissingArtRepair()
    {
        var path = Path.Combine(Path.GetTempPath(), $"arc-combat-005-{Guid.NewGuid():N}.dat");
        try
        {
            // Standing art, the fall (anim h, 3 frames) and a decapitation (anim r, 2 frames); no severed leg.
            File.WriteAllBytes(path, Dat([("art/critter/hmm/hmmuwxaa.art", Art(1)), ("art/critter/hmm/hmmuwxah.art", Art(3)),
                ("art/critter/hmm/hmmuwxar.art", Art(2))]));
            AreaEntryIdentity Identity(int ordinal)
            {
                using var archive = DatArchive.Open(path);
                var entry = archive.Entries.Single(e => e.Ordinal == ordinal);
                return new(new(AreaSourceRole.Resource1), entry.Ordinal, entry.Flags, entry.StoredSize, entry.DeclaredOutputSize, entry.Offset,
                    Convert.ToHexString(SHA256.HashData(entry.RawNameBytes.ToArray())).ToLowerInvariant());
            }
            var visual = Visual(Body(), Identity(0));
            var fall = AreaFallenPoses.Load(visual, path, AreaFallenPoses.FallDown);
            Assert.AreEqual(AreaFallenStatus.Ready, fall.Status, fall.Detail);
            Assert.AreEqual((7, 2, 5, 1, 1), (fall.Pose!.Animation, fall.Pose.Key.Frame, fall.Pose.Key.Group, fall.Pose.Frame.Width, fall.Pose.Frame.Height),
                "the last frame of the body's group");
            Assert.AreEqual(1, fall.Pose.Key.Entry.Ordinal);
            Assert.AreEqual(AreaFallenPoses.Policy, fall.Pose.Key.Policy);
            Assert.AreEqual((17, 1), (AreaFallenPoses.Load(visual, path, AreaFallenPoses.Decapitation).Pose!.Animation,
                AreaFallenPoses.Load(visual, path, AreaFallenPoses.Decapitation).Pose!.Key.Frame));
            Assert.AreEqual(7, AreaFallenPoses.Load(visual, path, AreaFallenPoses.SeveredLeg).Pose!.Animation, "a missing bloody death falls");

            Assert.AreEqual(AreaFallenStatus.EntryMismatch, AreaFallenPoses.Load(visual with { Entry = visual.Entry with { NameSha256 = Digest } }, path, 7).Status);
            Assert.AreEqual(AreaFallenStatus.EntryMismatch, AreaFallenPoses.Load(visual with { Entry = visual.Entry with { Offset = 5 } }, path, 7).Status);
            Assert.AreEqual(AreaFallenStatus.SourceUnavailable, AreaFallenPoses.Load(visual, null, 7).Status);
            Assert.AreEqual(AreaFallenStatus.SourceUnavailable, AreaFallenPoses.Load(visual, path + ".absent", 7).Status);
            Assert.AreEqual(AreaFallenStatus.NotACritterArt, AreaFallenPoses.Load(visual with { RawArt = 0x40000000 }, path, 7).Status);
            Assert.AreEqual(AreaFallenStatus.NotACritterArt, AreaFallenPoses.Load(visual with { Policy = "original-badart-v1" }, path, 7).Status);
            Assert.AreEqual(AreaFallenStatus.PoseUnreadable, AreaFallenPoses.Load(visual with { PhysicalGroup = 8 }, path, 7).Status);
            var fallOnly = Visual(Body(), Identity(1));
            Assert.AreEqual(AreaFallenStatus.Ready, AreaFallenPoses.Load(fallOnly, path, 7).Status, "a fallen body's own name works too");
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => AreaFallenPoses.Load(visual, path, 6));
        }
        finally { File.Delete(path); }

        var lonely = Path.Combine(Path.GetTempPath(), $"arc-combat-005-{Guid.NewGuid():N}.dat");
        try
        {
            File.WriteAllBytes(lonely, Dat([("art/critter/hmm/hmmuwxaa.art", Art(1))]));
            using var archive = DatArchive.Open(lonely);
            var entry = archive.Entries.Single();
            var identity = new AreaEntryIdentity(new(AreaSourceRole.Resource1), entry.Ordinal, entry.Flags, entry.StoredSize, entry.DeclaredOutputSize,
                entry.Offset, Convert.ToHexString(SHA256.HashData(entry.RawNameBytes.ToArray())).ToLowerInvariant());
            archive.Dispose();
            Assert.AreEqual(AreaFallenStatus.PoseMissing, AreaFallenPoses.Load(Visual(Body(), identity), lonely, 7).Status);
        }
        finally { File.Delete(lonely); }
    }

    // A minimal 8-bit ART: 8 groups of `frames` 1×1 frames, palette 0 white.
    private static byte[] Art(int frames)
    {
        const int headerBytes = 132, paletteBytes = 1024, frameHeaderBytes = 28;
        var total = 8 * frames;
        var table = headerBytes + paletteBytes;
        var payload = table + total * frameHeaderBytes;
        var bytes = new byte[payload + total];
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), 10);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8), 8);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), 1);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(32), frames);
        bytes[headerBytes + 4] = 255; bytes[headerBytes + 5] = 255; bytes[headerBytes + 6] = 255; bytes[headerBytes + 7] = 255;
        for (var index = 0; index < total; index++)
        {
            var frame = table + index * frameHeaderBytes;
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(frame), 1);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(frame + 4), 1);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(frame + 8), 1);
            bytes[payload + index] = 1;
        }
        return bytes;
    }

    // A 1TAD archive of stored (uncompressed) named entries, in order.
    internal static byte[] Dat(IReadOnlyList<(string Name, byte[] Payload)> entries)
    {
        using var output = new MemoryStream();
        var offsets = new uint[entries.Count];
        for (var index = 0; index < entries.Count; index++) { offsets[index] = checked((uint)output.Position); output.Write(entries[index].Payload); }
        using var table = new MemoryStream();
        WriteU32(table, checked((uint)entries.Count));
        for (var index = 0; index < entries.Count; index++)
        {
            var name = Encoding.ASCII.GetBytes(entries[index].Name.Replace('/', '\\') + "\0");
            WriteU32(table, checked((uint)name.Length)); table.Write(name); table.Write(new byte[4]);
            WriteU32(table, 1); WriteU32(table, checked((uint)entries[index].Payload.Length)); WriteU32(table, checked((uint)entries[index].Payload.Length));
            WriteU32(table, offsets[index]);
        }
        table.Position = 0; table.CopyTo(output);
        output.Write(new byte[16]); output.Write("1TAD"u8);
        WriteU32(output, checked((uint)entries.Count)); WriteU32(output, checked((uint)(table.Length + 28)));
        return output.ToArray();
    }

    private static void WriteU32(Stream stream, uint value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
        stream.Write(bytes);
    }
}
