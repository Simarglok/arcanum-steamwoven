using Arcanum.Data;
using Arcanum.Data.Area;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Equipment;
using Arcanum.Simulation.Sound;
using Arcanum.World;

namespace Arcanum.TerrainViewer.Core;

/// <summary>A hero body's decoded ART: its profile, the gait timing from its strides and every frame the facing table reaches.</summary>
public sealed record HeroBodyLoad(HeroBody Body, ActorDiagnosticProfile Profile, GaitTiming Timing, IReadOnlyList<ActorDiagnosticFrame> Frames,
    IReadOnlyDictionary<ActorDiagnosticTextureKey, DecodedActorTexture> Textures, long RgbaBytes)
{
    /// <summary>The actor plan of this body at a placement (its action, facing and frame; the frame's geometry is filled in).</summary>
    public ActorDiagnosticScenePlan Plan(ActorDiagnosticPlacementInput input) => PlanOf(Profile, Frames, input);

    /// <summary>The actor plan of a profile's decoded frames at a placement, the frame's geometry filled in; a typed
    /// <see cref="TerrainViewerException"/> when the placement's frame is not among them.</summary>
    public static ActorDiagnosticScenePlan PlanOf(ActorDiagnosticProfile profile, IReadOnlyList<ActorDiagnosticFrame> frames, ActorDiagnosticPlacementInput input)
    {
        ArgumentNullException.ThrowIfNull(profile); ArgumentNullException.ThrowIfNull(frames); ArgumentNullException.ThrowIfNull(input);
        var facing = profile.Facing(input.Action, input.Facing);
        var selector = profile.Resource(input.Action).Selector;
        var matches = frames.Where(f => f.TextureKey.Selector == selector && f.TextureKey.PhysicalGroup == facing.PhysicalGroup
            && f.TextureKey.Frame == input.Frame).Take(2).ToArray();
        if (matches.Length != 1) throw new TerrainViewerException(new(TerrainViewerErrorCode.ActorFrame, Stage: TerrainViewerFailureStage.ActorFrame));
        var frame = matches[0];
        return ActorDiagnosticScenePlan.CreateSynthetic(input with { Width = frame.Width, Height = frame.Height, HotX = frame.HotX, HotY = frame.HotY,
            ArtOffsetX = frame.ArtOffsetX, ArtOffsetY = frame.ArtOffsetY }, frames, profile);
    }
}

/// <summary>ARC-UI-039: reads a hero body's five ARTs (stand, walk, run, vault, unarmed attack) by name from the actor archive
/// (resource 1, where every critter ART of the installation is; SH-04) and decodes the frames the fixed facing table draws. The
/// header gives frames, rate and action frame; the walk and run strides are the frame offsets of one cycle (research L7). Since
/// ARC-COMBAT-021 also the fall-down and get-up ARTs (animations 7 and 8) when the body has both. Since ARC-RENDER-037 the stand and
/// attack show the body's weapon and shield (<see cref="HeroBody.Selector"/>), a missing armed walk the walk without it. Since
/// ARC-RENDER-039 also the low attack (21, with the weapon and shield) and the throw (14, without them) when the body has them; a low
/// attack the repair turns into the attack is not read again (the attack ART plays it). Typed failures as
/// <see cref="TerrainViewerException"/>; nothing is retained on failure.</summary>
public static class HeroBodyArt
{
    /// <summary>Upper bounds of one body: distinct frames and their RGBA bytes (the human uses 195 and 1,705,144).</summary>
    public const int MaxKeys = 512;
    public const long MaxRgbaBytes = 16L * 1024 * 1024;

    public static HeroBodyLoad Load(DatArchive resource, HeroBody body, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(resource);
        if (body.Body is < 0 or > 4 || body.Gender is not (0 or 1) || body.Armor is < 0 or > 8 || body.Palette is < 0 or > 3
            || body.Weapon is < 0 or >= WeaponArtRules.WeaponParts || body.Shield is not (0 or 1)) throw Failure(TerrainViewerErrorCode.ActorInput, TerrainViewerFailureStage.ActorInput);
        var human = ActorDiagnosticProfile.HumanFixedArea;
        var palette = body.Palette;
        var options = new ArtDecoderOptions { MaxInputBytes = 1024 * 1024, MaxFrameCount = 8 * 64 };
        var read = new Dictionary<ActorDiagnosticAction, (ActorDiagnosticResource Resource, byte[] Bytes, DatEntry Entry, ArtMetadata Metadata)>();
        try
        {
            // ARC-COMBAT-021: the fall-down and get-up ARTs follow the five; a body without both, or with one that is not one entry
            // with a readable header in the body's palette (none of the installed creatable bodies), loads without them, and its hero
            // then lies down without a drawn fall. ARC-RENDER-039: the low attack and throw likewise, each on its own.
            var wanted = human.Actions.Select(action => (Action: action, Selector: human.Resource(action).Selector, Optional: false))
                .Append((ActorDiagnosticAction.FallDown, ActorDiagnosticProfile.HumanFallDownSelector, true))
                .Append((ActorDiagnosticAction.GetUp, ActorDiagnosticProfile.HumanGetUpSelector, true))
                .Append((ActorDiagnosticAction.LowAttack, ActorDiagnosticProfile.HumanLowAttackSelector, true))
                .Append((ActorDiagnosticAction.Throw, ActorDiagnosticProfile.HumanThrowSelector, true))
                // ARC-COMBAT-1741: the stunned ART (animation 23, unarmed, no shield), optional.
                .Append((ActorDiagnosticAction.Stunned, ActorDiagnosticProfile.HumanStunnedSelector, true));
            foreach (var (action, humanSelector, optional) in wanted)
            {
                token.ThrowIfCancellationRequested();
                // ARC-RENDER-037: the first of the repair's candidates (MA1) the archive holds (a name held twice fails below); an armed
                // body walks without its weapon.
                var (selector, entries) = body.Candidates(humanSelector).Select(candidate => (Selector: candidate, Entries: Named(resource, body.ArtPath(candidate))))
                    .FirstOrDefault(found => found.Entries.Length != 0, (body.Selector(humanSelector), []));
                // ARC-RENDER-039: a low attack the repair turned into the attack is the attack's ART, already read.
                if (action == ActorDiagnosticAction.LowAttack && ((selector >> 6) & 31) != WeaponArtRules.LowAttackAnimation) continue;
                // ARC-COMBAT-1741: a stun the repair turned into the stand (MA1 pair 23 -> 0) is not a stunned ART: the stun then ends at once, as an unreadable header ends it (GT26).
                if (action == ActorDiagnosticAction.Stunned && ((selector >> 6) & 31) != HeroBody.StunnedAnimation) continue;
                if (entries.Length != 1)
                {
                    if (optional) continue;
                    throw Failure(TerrainViewerErrorCode.ActorMetadata, TerrainViewerFailureStage.ActorMetadata);
                }
                var bytes = resource.ReadEntry(entries[0]);
                ArtMetadata metadata;
                try { metadata = ArtDecoder.Inspect(bytes, options); }
                catch (ArtException) when (optional) { continue; }
                var h = metadata.Header;
                if (h.PhysicalGroupCount != 8 || h.FramesPerGroup <= 0 || h.FramesPerSecond <= 0 || h.PalettePresenceWords.Count <= palette
                    || h.PalettePresenceWords[palette] == 0 || (Attacks(action) && (h.ActionFrame < 0 || h.ActionFrame >= h.FramesPerGroup)))
                {
                    if (optional) continue;
                    throw Failure(TerrainViewerErrorCode.ActorMetadata, TerrainViewerFailureStage.ActorMetadata);
                }
                var descriptor = new ActorDiagnosticResource(action, selector, entries[0].Ordinal, palette, 8, h.FramesPerGroup, h.FramesPerSecond, h.FramesPerSecond)
                {
                    Stride = action is ActorDiagnosticAction.Walk or ActorDiagnosticAction.Run ? Stride(metadata) : null,
                    ActionFrame = Attacks(action) ? h.ActionFrame : 0,
                };
                if (descriptor.Stride is { IsValid: false }) throw Failure(TerrainViewerErrorCode.ActorMetadata, TerrainViewerFailureStage.ActorMetadata);
                read.Add(action, (descriptor, bytes, entries[0], metadata));
            }
            return Finish(body, read, MaxKeys, MaxRgbaBytes, options, token);
        }
        catch (TerrainViewerException) { throw; }
        catch (DatException) { throw Failure(TerrainViewerErrorCode.ActorDecode, TerrainViewerFailureStage.ActorDecode); }
        catch (ArtException) { throw Failure(TerrainViewerErrorCode.ActorDecode, TerrainViewerFailureStage.ActorDecode); }
        catch (IOException) { throw Failure(TerrainViewerErrorCode.ActorDecode, TerrainViewerFailureStage.ActorDecode); }
        catch (ArgumentException) { throw Failure(TerrainViewerErrorCode.ActorMetadata, TerrainViewerFailureStage.ActorMetadata); }
        catch (OverflowException) { throw Failure(TerrainViewerErrorCode.ResourceLimit, TerrainViewerFailureStage.ActorDecode); }
    }


    private static HeroBodyLoad Finish(HeroBody body, Dictionary<ActorDiagnosticAction, (ActorDiagnosticResource Resource, byte[] Bytes, DatEntry Entry, ArtMetadata Metadata)> read, int maxKeys,
        long maxRgbaBytes, ArtDecoderOptions options, CancellationToken token)
    {
        var human = ActorDiagnosticProfile.HumanFixedArea;
        // Every action shares the fixed facing table, so the human's used groups are every action's. The fall and get-up come only
        // together and only within the key cap; then the low attack and the throw, each only within it.
        var groups = human.UsedGroups(ActorDiagnosticAction.Idle).Count();
        var keys = human.Actions.Sum(action => groups * read[action].Resource.FramesPerGroup);
        var falls = read.TryGetValue(ActorDiagnosticAction.FallDown, out var fall) && read.TryGetValue(ActorDiagnosticAction.GetUp, out var rise)
            && keys + groups * (fall.Resource.FramesPerGroup + rise.Resource.FramesPerGroup) <= maxKeys;
        if (falls) keys += groups * (read[ActorDiagnosticAction.FallDown].Resource.FramesPerGroup + read[ActorDiagnosticAction.GetUp].Resource.FramesPerGroup);
        if (keys > maxKeys) throw Failure(TerrainViewerErrorCode.ResourceLimit, TerrainViewerFailureStage.ActorDecode);
        ActorDiagnosticResource? Extra(ActorDiagnosticAction action)
        {
            if (!read.TryGetValue(action, out var extra) || keys + groups * extra.Resource.FramesPerGroup > maxKeys) return null;
            keys += groups * extra.Resource.FramesPerGroup;
            return extra.Resource;
        }
        var low = Extra(ActorDiagnosticAction.LowAttack); var thrown = Extra(ActorDiagnosticAction.Throw); var stunned = Extra(ActorDiagnosticAction.Stunned);
        var profile = ActorDiagnosticProfile.ForHeroBody(read[ActorDiagnosticAction.Idle].Resource, read[ActorDiagnosticAction.Walk].Resource,
            read[ActorDiagnosticAction.Run].Resource, read[ActorDiagnosticAction.Vault].Resource, read[ActorDiagnosticAction.Attack].Resource, keys,
            falls ? read[ActorDiagnosticAction.FallDown].Resource : null, falls ? read[ActorDiagnosticAction.GetUp].Resource : null, low, thrown, stunned);
        var frames = new List<ActorDiagnosticFrame>(keys);
        var textures = new Dictionary<ActorDiagnosticTextureKey, DecodedActorTexture>(keys);
        long rgbaBytes = 0;
        foreach (var action in profile.Actions)
        {
            var (descriptor, bytes, entry, _) = read[action];
            var art = HdFrameQueries.ArtIdentity(bytes, entry.RawNameBytes);
            foreach (var group in profile.UsedGroups(action))
            for (var frame = 0; frame < descriptor.FramesPerGroup; frame++)
            {
                token.ThrowIfCancellationRequested();
                // ARC-MAGIC-1219 (PR5): the resource's palette is the one its ART is read in (a monster ART that lacks the requested palette is read in palette 0).
                var palette = descriptor.Palette;
                var key = new ActorDiagnosticTextureKey(descriptor.Ordinal, descriptor.Selector, group, frame, palette);
                var decoded = ArtDecoder.Decode(bytes, new ArtFrameSelection(group, frame, palette), options);
                if (decoded.EffectivePaletteSlot != palette || decoded.Width <= 0 || decoded.Height <= 0)
                    throw Failure(TerrainViewerErrorCode.ActorMetadata, TerrainViewerFailureStage.ActorMetadata);
                rgbaBytes = checked(rgbaBytes + TerrainCachePolicy.CheckedRgbaBytes(decoded.Width, decoded.Height));
                if (rgbaBytes > maxRgbaBytes) throw Failure(TerrainViewerErrorCode.ResourceLimit, TerrainViewerFailureStage.ActorDecode);
                var rgba = decoded.Rgba.ToArray();
                textures.Add(key, new DecodedActorTexture(key, decoded.Width, decoded.Height, rgba)
                { HdQuery = HdFrameQueries.FromArt(art.PayloadSha256, art.ViewLength, art.NameSha256, group, frame, decoded.EffectivePaletteSlot, decoded.Width, decoded.Height, rgba) });
                var geometry = decoded.Frame;
                frames.Add(new ActorDiagnosticFrame(key, decoded.Width, decoded.Height, geometry.HotX, geometry.HotY, geometry.OffsetX, geometry.OffsetY,
                    descriptor.FramesPerGroup, descriptor.RawFramesPerSecond));
            }
        }
        var timing = new GaitTiming(profile.Walk.Stride!, profile.Run!.Stride!, GaitTiming.BaseSpeed, profile.Vault!.FramesPerGroup,
            profile.Vault.RawFramesPerSecond, body.GaitRateBonus);
        return new(body, profile, timing, frames.AsReadOnly(), textures.AsReadOnly(), rgbaBytes);
    }

    /// <summary>ARC-MAGIC-1219: upper bounds of one monster body: distinct frames and their RGBA bytes. The elementals' frames are large (measured on the installed art: at most 505 frames and 20.3 MB, the fire elemental; the human limits were 512 and 16 MiB).</summary>
    public const int MonsterMaxKeys = 512;
    public const long MonsterMaxRgbaBytes = 32L * 1024 * 1024;

    /// <summary>
    /// ARC-MAGIC-052 (ledger TR4), ARC-MAGIC-1219 (PR1 to PR5): the hero's monster body (<see cref="HeroBody.Monster"/>): the ARTs the actor needs, read from the monster ARTs of the species by the art id the original
    /// builds (class 12, the species, the animation, the weapon part and the palette; the file name is the critter name rules' N1, <see cref="AreaDataOwner.CritterArtPath"/>) from the first archive that holds the
    /// file. The weapon part is the body's (0 out of combat mode, 1 in it: <see cref="HeroBody.MonsterInCombat"/>) and the original's missing-art repair picks the file (<see cref="HeroBody.MonsterCandidates"/>: the stand
    /// with part 1 for part 0, the walk with part 0 for part 1, the walk for a missing run). A species has the animations of its own: stand (0), walk (1) and run (6) usually, attack (20), fall (7) and get up (8)
    /// sometimes, the low attack (21) for the fire elemental only, a vault (13) for five. The throw (14) is not loaded (owner decision D2: a monster body wields nothing). An action the species has no ART for plays the
    /// nearest it has (the vault and attack the stand): its resource is that ART's frames under the action's own selector with the weapon part 15 (no file has it), so the profile keeps one selector per action. A palette the
    /// ART does not hold is read as palette 0 (PR5: what the original draws; Body of Air, Fire and Water ask for palette 1, the elemental ARTs hold palette 0 only). A species without a stand ART, a header the actor cannot
    /// play (8 physical groups), or a walk with no stride is refused (typed failures as <see cref="TerrainViewerException"/>; nothing is retained).
    /// </summary>
    /// <param name="archives">The archives to search, in order; the first that holds the file once is read (a name held twice there is refused).</param>
    /// <param name="name">The name tables of the monster art ids.</param>
    public static HeroBodyLoad LoadMonster(IReadOnlyList<DatArchive> archives, HeroBody body, Func<AreaMappingRole, int, string?> name, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(archives); ArgumentNullException.ThrowIfNull(name);
        if (!body.IsMonster || body.MonsterSpecies >= 32 || body.Palette is < 0 or > 3 || body.Weapon is not (0 or 1)) throw Failure(TerrainViewerErrorCode.ActorInput, TerrainViewerFailureStage.ActorInput);
        var options = new ArtDecoderOptions { MaxInputBytes = 1024 * 1024, MaxFrameCount = 8 * 64 };
        var read = new Dictionary<ActorDiagnosticAction, (ActorDiagnosticResource Resource, byte[] Bytes, DatEntry Entry, ArtMetadata Metadata)>();
        try
        {
            // Each action: its animation and whether the profile can do without it; the throw has no consumer and is left out.
            (ActorDiagnosticAction Action, int Animation, bool Optional)[] wanted =
            [
                (ActorDiagnosticAction.Idle, 0, false), (ActorDiagnosticAction.Walk, 1, false), (ActorDiagnosticAction.Run, 6, false),
                (ActorDiagnosticAction.Vault, 13, false), (ActorDiagnosticAction.Attack, WeaponArtRules.AttackAnimation, false),
                (ActorDiagnosticAction.FallDown, 7, true), (ActorDiagnosticAction.GetUp, 8, true), (ActorDiagnosticAction.LowAttack, WeaponArtRules.LowAttackAnimation, true),
            ];
            foreach (var (action, animation, optional) in wanted)
            {
                token.ThrowIfCancellationRequested();
                var own = body.MonsterSelector(animation);
                // The repair's candidates first; then, for the actions a species may lack, the stand stands in for them.
                var tries = body.MonsterCandidates(animation).Concat(action is ActorDiagnosticAction.Vault or ActorDiagnosticAction.Attack ? body.MonsterCandidates(0) : []);
                var found = false;
                foreach (var selector in tries)
                {
                    // An ART of the action's own animation keeps its art id as the action's selector; the run's walk and the stand-ins keep the action's own with the weapon part 15 (no file names it).
                    var real = ((selector >> 6) & 31) == animation;
                    string path;
                    try { path = AreaDataOwner.CritterArtPath(selector, name); }
                    catch (AreaException) { continue; }
                    var (archive, entries) = archives.Select(a => (Archive: a, Entries: Named(a, path))).FirstOrDefault(f => f.Entries.Length != 0, (null!, []));
                    if (entries.Length == 0) continue;
                    if (entries.Length != 1) throw Failure(TerrainViewerErrorCode.ActorMetadata, TerrainViewerFailureStage.ActorMetadata);
                    var bytes = archive.ReadEntry(entries[0]);
                    ArtMetadata metadata;
                    try { metadata = ArtDecoder.Inspect(bytes, options); }
                    catch (ArtException) { continue; }
                    var h = metadata.Header;
                    // PR5: the palette the art id asks for, else palette 0 (the original's lookup reads palette 0's table when the requested one is absent).
                    var palette = Holds(h, body.Palette) ? body.Palette : Holds(h, 0) ? 0 : -1;
                    if (h.PhysicalGroupCount != 8 || h.FramesPerGroup <= 0 || h.FramesPerSecond <= 0 || palette < 0
                        || (Attacks(action) && real && (h.ActionFrame < 0 || h.ActionFrame >= h.FramesPerGroup))) continue;
                    var descriptor = new ActorDiagnosticResource(action, real ? selector : own | 0xFu, entries[0].Ordinal, palette, 8, h.FramesPerGroup, h.FramesPerSecond, h.FramesPerSecond)
                    {
                        Stride = action is ActorDiagnosticAction.Walk or ActorDiagnosticAction.Run ? Stride(metadata) : null,
                        ActionFrame = Attacks(action) && real ? h.ActionFrame : 0,
                    };
                    if (descriptor.Stride is { IsValid: false }) continue;
                    read.Add(action, (descriptor, bytes, entries[0], metadata));
                    found = true;
                    break;
                }
                // A low attack the repair turns into the attack (every species but the fire elemental) is not read again: the attack's ART plays it.
                if (!found && !optional) throw Failure(TerrainViewerErrorCode.ActorMetadata, TerrainViewerFailureStage.ActorMetadata);
            }
            return Finish(body, read, MonsterMaxKeys, MonsterMaxRgbaBytes, options, token);
        }
        catch (TerrainViewerException) { throw; }
        catch (DatException) { throw Failure(TerrainViewerErrorCode.ActorDecode, TerrainViewerFailureStage.ActorDecode); }
        catch (ArtException) { throw Failure(TerrainViewerErrorCode.ActorDecode, TerrainViewerFailureStage.ActorDecode); }
        catch (IOException) { throw Failure(TerrainViewerErrorCode.ActorDecode, TerrainViewerFailureStage.ActorDecode); }
        catch (ArgumentException) { throw Failure(TerrainViewerErrorCode.ActorMetadata, TerrainViewerFailureStage.ActorMetadata); }
        catch (OverflowException) { throw Failure(TerrainViewerErrorCode.ResourceLimit, TerrainViewerFailureStage.ActorDecode); }
    }

    private static bool Holds(ArtHeader header, int palette) => header.PalettePresenceWords.Count > palette && header.PalettePresenceWords[palette] != 0;

    /// <summary>
    /// The stride of a walk or run ART (research L7): the offsets of one cycle on an axis rotation (group 1) and on a diagonal
    /// rotation (group 2), as positive pixels; <see cref="ActorDiagnosticProfile.HumanFixedArea"/>'s strides restate the same sums.
    /// </summary>
    public static GaitStride Stride(ArtMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        (int X, int Y) Sum(int group)
        {
            var (x, y) = (0, 0);
            for (var frame = 0; frame < metadata.Header.FramesPerGroup; frame++) { var row = metadata.GetFrame(group, frame); x += row.OffsetX; y += row.OffsetY; }
            return (x, y);
        }
        var (axisX, axisY) = Sum(1); var (diagonal, _) = Sum(2);
        return new(metadata.Header.FramesPerGroup, metadata.Header.FramesPerSecond, Math.Abs(axisX), Math.Abs(axisY), Math.Abs(diagonal));
    }

    private static bool Attacks(ActorDiagnosticAction action) =>
        action is ActorDiagnosticAction.Attack or ActorDiagnosticAction.LowAttack or ActorDiagnosticAction.Throw;

    private static DatEntry[] Named(DatArchive resource, string path) =>
        resource.Entries.Where(entry => string.Equals(Name(entry), path, StringComparison.OrdinalIgnoreCase)).ToArray();

    private static string Name(DatEntry entry)
    {
        var raw = entry.RawNameBytes;
        var length = raw.Count > 0 && raw[^1] == 0 ? raw.Count - 1 : raw.Count;
        var chars = new char[length];
        for (var i = 0; i < length; i++) chars[i] = raw[i] == (byte)'\\' ? '/' : (char)raw[i];
        return new string(chars);
    }

    private static TerrainViewerException Failure(TerrainViewerErrorCode code, TerrainViewerFailureStage stage) => new(new TerrainViewerError(code, Stage: stage));
}
