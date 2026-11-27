using System.Security.Cryptography;
using Arcanum.Data;
using Arcanum.Data.Area;
using Arcanum.Simulation.Equipment;

namespace Arcanum.World;

public enum AreaBodyMoveStatus
{
    Ready,
    /// <summary>The body is not a critter, monster or unique NPC art (class 2, 12, 13), or it is drawn from bad art.</summary>
    NotACritterArt,
    /// <summary>The body's art comes from no archive the viewer holds (a loose file or an unknown source).</summary>
    SourceUnavailable,
    /// <summary>The archive's entry for the body is not the one the published snapshot read.</summary>
    EntryMismatch,
    /// <summary>No walk ART exists next to the body's ART.</summary>
    WalkMissing,
    /// <summary>ARC-COMBAT-1932: no ART file exists for an armed animation after the missing-art repair and the viewer's fallbacks (none was unreadable): the attack's start fails.</summary>
    ArtMissing,
    /// <summary>An ART could not be read or decoded.</summary>
    Unreadable,
}

/// <summary>One frame of a body's move ART for one facing: its frame key, the decoded frame, the hotspot it is placed by (mirrored
/// for a mirrored facing) and whether it is drawn mirrored.</summary>
public sealed record AreaBodyPose(AreaFrameKey Key, DecodedAreaFrame Frame, int HotX, int HotY, bool FlipX);

/// <summary>
/// ARC-WORLD-111: one animation ART of a critter's move set (stand, walk or run), read once from the body's archive; its frames are
/// decoded on first use. Facing octant = ART rotation; rotations 1..3 draw groups 7..5 mirrored, the hotspot X then
/// <c>width − hotX − 1</c> (the ART frame lookup, research actor placement AP-006); a one-rotation ART serves every facing from its
/// group 0, mirrored for rotations 1..3 as the T0 body is. Bytes after the last frame are no part of the ART (its own extent, as the
/// T0 loader reads it).
/// </summary>
public sealed class AreaBodyArt
{
    private static readonly ArtDecoderOptions Options = new() { MaxInputBytes = 1024 * 1024, MaxFrameCount = 8 * 64, AllowTrailingBytes = true };
    private readonly byte[] _bytes;
    private readonly AreaEntryIdentity _identity;
    private readonly string _digest;
    private readonly int _palette, _groups, _view;
    private readonly Dictionary<(int Group, int Frame), AreaBodyPose> _poses = [];

    internal AreaBodyArt(int animation, string name, byte[] bytes, AreaEntryIdentity identity, int palette)
    {
        var metadata = ArtDecoder.Inspect(bytes, Options);
        var header = metadata.Header;
        if (header.FramesPerGroup < 1 || header.PhysicalGroupCount < 1) throw new ArgumentException("AREA_BODY_ART_EMPTY");
        Animation = animation; Name = name; _bytes = bytes; _identity = identity; _palette = palette; _groups = header.PhysicalGroupCount;
        _view = bytes.Length - metadata.TrailingBytes;
        FramesPerGroup = header.FramesPerGroup; FramesPerSecond = header.FramesPerSecond; ActionFrame = header.ActionFrame;
        _digest = Digest(bytes);
    }

    /// <summary>The animation number (0 stand, 1 walk, 6 run; ARC-RENDER-038 also 14 throw, 20 attack, 21 low attack).</summary>
    public int Animation { get; }
    public string Name { get; }
    public int FramesPerGroup { get; }
    /// <summary>The ART's own frame rate (the middle rate of the original's Speed table, research ARC-RES-047 L6/L7).</summary>
    public int FramesPerSecond { get; }
    /// <summary>ARC-RENDER-038: the ART's action frame (an attack's hit).</summary>
    public int ActionFrame { get; }

    /// <summary>The frame <paramref name="frame"/> (taken modulo the cycle) of facing <paramref name="octant"/>.</summary>
    public AreaBodyPose Pose(int octant, int frame)
    {
        if (octant is < 0 or > 7) throw new ArgumentOutOfRangeException(nameof(octant));
        var mirror = octant is 1 or 2 or 3;
        var group = _groups == 1 ? 0 : mirror ? 8 - octant : octant;
        if (group >= _groups) throw new ArtException($"AREA_BODY_ART_GROUP {group}");
        frame = ((frame % FramesPerGroup) + FramesPerGroup) % FramesPerGroup;
        if (!_poses.TryGetValue((group, frame), out var pose))
        {
            var decoded = ArtDecoder.Decode(_bytes.AsSpan(0, _view), new ArtFrameSelection(group, frame, _palette), Options);
            var rgba = decoded.Rgba.ToArray();
            var key = new AreaFrameKey(_identity, decoded.EffectivePaletteSlot, group, frame, AreaBodyMoveArt.Policy, _digest, _view, Digest(rgba));
            pose = new(key, new(decoded.Width, decoded.Height, rgba), decoded.Frame.HotX, decoded.Frame.HotY, false);
            _poses[(group, frame)] = pose;
        }
        return mirror ? pose with { HotX = pose.Frame.Width - pose.HotX - 1, FlipX = true } : pose;
    }

    internal static string Digest(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}

/// <summary>ARC-RENDER-038: an armed animation ART of a body (<see cref="AreaBodyMoveArt.LoadArmed"/>): the ART read, or null with the
/// reason, and the animation, weapon part and shield bit the read file names (the request's when none was read).</summary>
public sealed record AreaBodyArmedLoad(AreaBodyMoveStatus Status, AreaBodyArt? Art, int Animation, int Weapon, int Shield, string Detail);

/// <summary>A body's move set: the stand, walk and run ARTs found next to its ART (null when absent).</summary>
public sealed record AreaBodyMoves(AreaBodyMoveStatus Status, AreaBodyArt? Stand, AreaBodyArt? Walk, AreaBodyArt? Run, string Detail)
{
    /// <summary>ARC-RENDER-2539: the stealth walk ART (animation 3, which keeps the weapon and shield like the stand) next to the body's ART, or null when the body has none (the walk is drawn, MA1 pair 3 to 1).</summary>
    public AreaBodyArt? Sneak { get; init; }

    /// <summary>The ART drawn for <paramref name="action"/>: the run ART (the walk without one, the missing-art repair), the walk ART
    /// for a walk or a vault, the stand ART standing; null when there is none.</summary>
    public AreaBodyArt? For(ActorDiagnosticAction action) => action switch
    {
        ActorDiagnosticAction.Run => Run ?? Walk,
        ActorDiagnosticAction.Sneak => Sneak ?? Walk,
        ActorDiagnosticAction.Walk or ActorDiagnosticAction.Vault => Walk,
        ActorDiagnosticAction.Idle => Stand,
        _ => null,
    };
}

/// <summary>
/// ARC-WORLD-111: the move ARTs of a critter that walks off its T0 cell (a companion following the PC, an NPC the AI walks), read on
/// demand from the archive its standing art came from. The original draws every walker's step with the step animation (0x42EDC0):
/// walk (animation 1) or run (6) by the gait, and stands it on frame 0 of the stand animation (0) when the move ends (research
/// ARC-RES-047 L5–L7, actor placement). A critter's ART name ends in the shield, weapon and animation letters (ledger MA3); walk clears
/// the shield, animations 6–19 clear shield and weapon (MA2); a missing run is drawn as the walk, a missing walk with the weapon as
/// the walk without it (MA1). The archive entry the body was drawn from must be the one the published snapshot names, so a changed
/// archive is refused. Godot-free; a typed status, never an exception for data.
/// </summary>
public static class AreaBodyMoveArt
{
    public const int Stand = 0, Walk = 1, Sneak = 3, Run = 6;
    public const string Policy = "steamwoven-body-move-v1";
    private static readonly DatReaderOptions Options = new() { MaxArchiveBytes = 536870912, MaxStoredEntryBytes = 1048576, MaxDecodedEntryBytes = 1048576 };

    /// <param name="archives">The caller's open archives, or null to open (and close) the archive for this load.</param>
    public static AreaBodyMoves Load(AreaVisualRecord visual, string? archivePath, AreaArtArchives? archives = null)
    {
        ArgumentNullException.ThrowIfNull(visual);
        if (visual.RawArt >> 28 is not (2 or 12 or 13) || visual.Policy.Contains("badart", StringComparison.Ordinal))
            return Refused(AreaBodyMoveStatus.NotACritterArt, $"art class {visual.RawArt >> 28}");
        if (archivePath is null || !File.Exists(archivePath)) return Refused(AreaBodyMoveStatus.SourceUnavailable, visual.Entry.Source.ToString());
        try
        {
            using var own = archives is null ? new AreaArtArchives() : null;
            var held = (archives ?? own!).Get(archivePath, Options);
            var (archive, named) = (held.Archive, held.Named);
            if (AreaFallenPoses.StandingEntry(archive, visual) is not { } standing) return Refused(AreaBodyMoveStatus.EntryMismatch, $"ordinal {visual.Entry.Ordinal}");
            var name = AreaFallenPoses.Name(standing);
            var dot = name.LastIndexOf('.');
            if (dot < 4) return Refused(AreaBodyMoveStatus.WalkMissing, "name");
            char shield = name[dot - 3], weapon = name[dot - 2];
            var refused = new List<string>();
            AreaBodyArt? Find(int animation, params (char Shield, char Weapon)[] tries)
            {
                foreach (var (s, w) in tries)
                {
                    var wanted = string.Concat(name.AsSpan(0, dot - 3), $"{s}{w}{(char)('a' + animation)}", name.AsSpan(dot));
                    if (!named.TryGetValue(wanted, out var entry) || entry is null) continue;
                    try
                    {
                        var identity = new AreaEntryIdentity(visual.Entry.Source, entry.Ordinal, entry.Flags, entry.StoredSize, entry.DeclaredOutputSize, entry.Offset,
                            AreaBodyArt.Digest(entry.RawNameBytes.ToArray()));
                        return new AreaBodyArt(animation, AreaFallenPoses.Name(entry), archive.ReadEntry(entry), identity, visual.RequestedPalette);
                    }
                    // An unreadable candidate leaves the next one of the repair.
                    catch (Exception error) when (error is DatException or ArtException or ArgumentException) { refused.Add($"{wanted}: {error.GetType().Name}"); }
                }
                return null;
            }
            // MA1: a missing armed walk is the walk without the weapon, a missing run the walk, a missing stand with weapon 0 weapon 1.
            var walk = weapon == 'a' ? Find(Walk, ('x', 'a')) : Find(Walk, ('x', weapon), ('x', 'a'));
            if (walk is null) return Refused(refused.Count == 0 ? AreaBodyMoveStatus.WalkMissing : AreaBodyMoveStatus.Unreadable, string.Join("; ", ["walk", .. refused]));
            var run = Find(Run, ('x', 'a'));
            // ARC-RENDER-2539: the stealth walk (animation 3) is outside the 6..19 range that clears weapon and shield, so it keeps the body's own letters like the stand, except weapon 1 (letter b) becomes weapon 0 (MA2, 0x41D510); missing, the walk is drawn (MA1 pair 3 to 1).
            var sneak = Find(Sneak, (shield, weapon == 'b' ? 'a' : weapon));
            var stand = weapon == 'a' ? Find(Stand, (shield, 'a'), (shield, 'b')) : Find(Stand, (shield, weapon));
            return new(AreaBodyMoveStatus.Ready, stand, walk, run, $"{stand?.Name ?? "no stand"}, {walk.Name}, {run?.Name ?? "no run"}, {sneak?.Name ?? "no sneak"}"
                + (refused.Count == 0 ? "" : "; unreadable " + string.Join(", ", refused))) { Sneak = sneak };
        }
        catch (Exception error) when (error is DatException or ArtException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            if (error is IOException) archives?.Forget(archivePath);
            return Refused(AreaBodyMoveStatus.Unreadable, error.GetType().Name + " " + error.Message);
        }
    }

    /// <summary>The frame of a cycle of <paramref name="frames"/> frames at <paramref name="fps"/> after <paramref name="elapsedMs"/>
    /// milliseconds of the gait (0 without a rate).</summary>
    public static int CycleFrame(long elapsedMs, int fps, int frames) =>
        fps <= 0 || frames <= 0 || elapsedMs <= 0 ? 0 : (int)(elapsedMs * fps / 1000 % frames);

    /// <summary>
    /// <paramref name="command"/> (the body of <paramref name="body"/> drawn from <paramref name="visual"/>, moved where it stands)
    /// with the frame of <paramref name="action"/> facing <paramref name="octant"/> after <paramref name="elapsedMs"/> of the gait: a
    /// walk or run cycle frame, or frame 0 of the stand ART. A body that has not <paramref name="moved"/> standing with its T0 facing,
    /// or one without the ART, keeps its command (once moved it stands on its stand ART whatever its facing, so a stop never shifts it
    /// to the T0 frame's placement). Placed by <see cref="AreaScenePlan.Repose"/> at the same anchor; the object's own flip stays.
    /// </summary>
    public static (AreaDrawCommand Command, AreaBodyPose? Pose) Animate(AreaDrawCommand command, AreaObjectRecord body, AreaVisualRecord visual,
        AreaBodyMoves moves, ActorDiagnosticAction action, int octant, long elapsedMs, bool moved = false)
    {
        ArgumentNullException.ThrowIfNull(command); ArgumentNullException.ThrowIfNull(body); ArgumentNullException.ThrowIfNull(visual);
        ArgumentNullException.ThrowIfNull(moves);
        if (command.Visual != visual.Key || moves.Status != AreaBodyMoveStatus.Ready) return (command, null);
        if (action == ActorDiagnosticAction.Idle && octant == visual.NominalFacing && !moved) return (command, null);
        if (moves.For(action) is not { } art) return (command, null);
        return Place(command, body, visual, art.Pose(octant, action == ActorDiagnosticAction.Idle ? 0 : CycleFrame(elapsedMs, art.FramesPerSecond, art.FramesPerGroup)));
    }

    /// <summary>
    /// ARC-RENDER-038: the ART of an armed animation of the body (an NPC in combat mode: its stand, attack, low attack or throw), named as
    /// the original resolves it (MA2, MA3): the stand and the attacks keep weapon and shield, the throw (14, among 6..19) clears both; the
    /// shield letter is <c>S</c> or <c>X</c>, the weapon letter <see cref="HeroBody.WeaponCode"/>'s (a two-handed sword with a shield is
    /// written as the sword). Tried in order: the missing-art repair turns a missing low attack into the attack for bare hands and weapons
    /// 2..8, 10 and 13 (MA1, WA10; another weapon's low attack is not repaired); then, as for the
    /// hero (the session's wear check does not test the art, WA8: NOT READY), without the shield, then bare-handed. The first readable one
    /// is the load's, with the animation, weapon and shield it names.
    /// </summary>
    public static AreaBodyArmedLoad LoadArmed(AreaVisualRecord visual, string? archivePath, int animation, int weapon, int shield, AreaArtArchives? archives = null)
    {
        ArgumentNullException.ThrowIfNull(visual);
        if (animation is < 0 or > 24 || weapon is < 0 or >= WeaponArtRules.WeaponParts || shield is not (0 or 1))
            return new(AreaBodyMoveStatus.WalkMissing, null, animation, weapon, shield, "no critter ART names this animation or weapon");
        if (visual.RawArt >> 28 is not (2 or 12 or 13) || visual.Policy.Contains("badart", StringComparison.Ordinal))
            return new(AreaBodyMoveStatus.NotACritterArt, null, animation, weapon, shield, $"art class {visual.RawArt >> 28}");
        if (archivePath is null || !File.Exists(archivePath)) return new(AreaBodyMoveStatus.SourceUnavailable, null, animation, weapon, shield, visual.Entry.Source.ToString());
        var tries = new List<(int Animation, int Weapon, int Shield)>();
        foreach (var (w, s) in new[] { (weapon, shield), (weapon, 0), (WeaponArtRules.Unarmed, 0) })
        foreach (var a in animation == WeaponArtRules.LowAttackAnimation && HeroBody.LowAttackRepaired(w) ? new[] { animation, WeaponArtRules.AttackAnimation } : [animation])
        {
            // MA2: animations 6..19 clear weapon and shield.
            var named = a is >= 6 and <= 19 ? (a, 0, 0) : (a, w, s);
            if (!tries.Contains(named)) tries.Add(named);
        }
        try
        {
            using var own = archives is null ? new AreaArtArchives() : null;
            var held = (archives ?? own!).Get(archivePath, Options);
            var (archive, named) = (held.Archive, held.Named);
            if (AreaFallenPoses.StandingEntry(archive, visual) is not { } standing)
                return new(AreaBodyMoveStatus.EntryMismatch, null, animation, weapon, shield, $"ordinal {visual.Entry.Ordinal}");
            var name = AreaFallenPoses.Name(standing);
            var dot = name.LastIndexOf('.');
            if (dot < 4) return new(AreaBodyMoveStatus.WalkMissing, null, animation, weapon, shield, "name");
            var refused = new List<string>();
            foreach (var (a, w, s) in tries)
            {
                var wanted = string.Concat(name.AsSpan(0, dot - 3), $"{(s != 0 ? 's' : 'x')}{char.ToLowerInvariant(HeroBody.WeaponCode(w, s))}{(char)('a' + a)}", name.AsSpan(dot));
                if (!named.TryGetValue(wanted, out var entry) || entry is null) continue;
                try
                {
                    var identity = new AreaEntryIdentity(visual.Entry.Source, entry.Ordinal, entry.Flags, entry.StoredSize, entry.DeclaredOutputSize, entry.Offset,
                        AreaBodyArt.Digest(entry.RawNameBytes.ToArray()));
                    var art = new AreaBodyArt(a, AreaFallenPoses.Name(entry), archive.ReadEntry(entry), identity, visual.RequestedPalette);
                    return new(AreaBodyMoveStatus.Ready, art, a, w, s, art.Name + (refused.Count == 0 ? "" : "; unreadable " + string.Join(", ", refused)));
                }
                // An unreadable candidate leaves the next one.
                catch (Exception error) when (error is DatException or ArtException or ArgumentException) { refused.Add($"{wanted}: {error.GetType().Name}"); }
            }
            return new(refused.Count == 0 ? AreaBodyMoveStatus.ArtMissing : AreaBodyMoveStatus.Unreadable, null, animation, weapon, shield,
                string.Join("; ", [$"animation {animation} weapon {weapon} shield {shield}", .. refused]));
        }
        catch (Exception error) when (error is DatException or ArtException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            if (error is IOException) archives?.Forget(archivePath);
            return new(AreaBodyMoveStatus.Unreadable, null, animation, weapon, shield, error.GetType().Name + " " + error.Message);
        }
    }

    /// <summary>
    /// ARC-RENDER-038: <paramref name="command"/> (the body of <paramref name="body"/> drawn from <paramref name="visual"/>, moved where it
    /// stands) with frame <paramref name="frame"/> of <paramref name="art"/> facing <paramref name="octant"/>, placed as
    /// <see cref="Animate"/> places a move frame. Null when the command is not the visual's.
    /// </summary>
    public static (AreaDrawCommand Command, AreaBodyPose Pose)? Pose(AreaDrawCommand command, AreaObjectRecord body, AreaVisualRecord visual, AreaBodyArt art,
        int octant, int frame)
    {
        ArgumentNullException.ThrowIfNull(command); ArgumentNullException.ThrowIfNull(body); ArgumentNullException.ThrowIfNull(visual);
        ArgumentNullException.ThrowIfNull(art);
        if (command.Visual != visual.Key) return null;
        return Place(command, body, visual, art.Pose(octant, frame));
    }

    private static (AreaDrawCommand Command, AreaBodyPose Pose) Place(AreaDrawCommand command, AreaObjectRecord body, AreaVisualRecord visual, AreaBodyPose pose)
    {
        var objectFlip = command.FlipX != visual.Mirrored;
        var posed = AreaScenePlan.Repose(command, body, visual, pose.Key, pose.Frame.Width, pose.Frame.Height, pose.HotX, pose.HotY)
            with { FlipX = pose.FlipX != objectFlip };
        return (posed, pose);
    }

    private static AreaBodyMoves Refused(AreaBodyMoveStatus status, string detail) => new(status, null, null, null, detail);
}

/// <summary>
/// ARC-WORLD-111: the gait clock of one walker's drawn cycle, as the PC's: it restarts when the walker starts moving or changes gait
/// and runs on through a walk's steps (a walk, run or vault frame comes from the time since then).
/// </summary>
public sealed class AreaBodyCycle
{
    private long _startMs;

    public ActorDiagnosticAction Action { get; private set; } = ActorDiagnosticAction.Idle;
    /// <summary>Whether the walker has moved since the clock began (until then a standing companion keeps its T0 frame).</summary>
    public bool Moved { get; private set; }

    /// <summary>Observes the walker's action at <paramref name="nowMs"/>; returns the milliseconds its gait has run.</summary>
    public long Observe(ActorDiagnosticAction action, long nowMs)
    {
        // A clock set back (a load, a test run) restarts the cycle too.
        if (action != Action || nowMs < _startMs) { Action = action; _startMs = nowMs; }
        if (action != ActorDiagnosticAction.Idle) Moved = true;
        return action == ActorDiagnosticAction.Idle ? 0 : Math.Max(0, nowMs - _startMs);
    }
}
