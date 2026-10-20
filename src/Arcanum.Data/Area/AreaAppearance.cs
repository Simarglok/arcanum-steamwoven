using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using Arcanum.Data.Messages;
using Arcanum.Data.Objects;
using Arcanum.Data.Terrain;

namespace Arcanum.Data.Area;

public sealed partial class AreaDataOwner
{
    private static readonly MesReaderOptions MappingMesOptions = new() { MaxBytes = 16 * 1048576, MaxMessages = 8192 };

    private Dictionary<AreaMappingRole, SortedDictionary<int, string>> ReadMappings(CancellationToken token, AreaOwnership ownership)
    {
        var result = new Dictionary<AreaMappingRole, SortedDictionary<int, string>>();
        foreach (var binding in _plan.Mappings)
        {
            token.ThrowIfCancellationRequested();
            using var payload = new AreaOwnership(_budget);
            var bytes = ReadEntry(binding.Entry, token, payload);
            if (ObjectWireReader.Hash(bytes) != binding.PayloadSha256) throw Fail(AreaErrorCode.InputIdentityMismatch, AreaStage.Mapping);
            if (binding.Role == AreaMappingRole.MissingPolicy) continue;
            ownership.Allocate(checked(bytes.Length * 16L + 65536));
            // ARC-DATA-027: the original MES grammar (MesTable); a duplicate number is ambiguous, a limit a budget refusal.
            var status = MesTable.Read(bytes, MappingMesOptions, out var table);
            if (status != MesParseStatus.Parsed)
                throw Fail(status == MesParseStatus.DuplicateNumber ? AreaErrorCode.AmbiguousSource
                    : MesTable.IsLimit(status) ? AreaErrorCode.BudgetExceeded : AreaErrorCode.MalformedRecord, AreaStage.Mapping);
            if (table.Count == 0) throw Fail(AreaErrorCode.MissingSource, AreaStage.Mapping);
            result.Add(binding.Role, table);
        }
        if (!result.ContainsKey(AreaMappingRole.Tile) || !result.ContainsKey(AreaMappingRole.Facade)) throw Fail(AreaErrorCode.MissingSource, AreaStage.Mapping);
        return result;
    }
    private static string Mapped(Dictionary<AreaMappingRole, SortedDictionary<int, string>> maps, AreaMappingRole role, int key) => maps.TryGetValue(role, out var table) && table.TryGetValue(key, out var value) ? Safe(value) : throw Fail(AreaErrorCode.MissingSource, AreaStage.Mapping);
    /// <summary>ARC-DATA-038: the ART id that names no ART file (ledger NA1). The game's ART name callback names none for ART
    /// type 15 (bits 28-31), so the ART cache finds nothing, the frame data fails and the object draw gets an empty rectangle.
    /// Only 0xFFFFFFFF, the id the installed data carries, is admitted this way; any other type-15 id stays refused.</summary>
    [OriginalFunction(0x41DAE0, OriginalFunctionScope.Partial)]
    public static bool NamesNoArt(uint aid) => aid == uint.MaxValue;

    /// <summary>ARC-DATA-038: an object of this type and ART id has no body to draw: its ART id names no ART. Walls and portals
    /// (types 0 and 1), whose wall check and edge rules read their ART, keep the refusal.</summary>
    public static bool HasNoBody(int type, uint aid) => type is not 0 and not 1 && NamesNoArt(aid);

    private Request ObjectRequest(uint aid, Dictionary<AreaMappingRole, SortedDictionary<int, string>> maps, CancellationToken token)
    {
        var cls = aid >> 28; var frame = cls == 6 ? 0 : (int)((aid >> 14) & 31); var nominal = cls == 6 ? 0 : (int)((aid >> 11) & 7);
        var palette = (int)((aid >> 4) & 3); var mirror = cls is 2 or 12 or 13 && nominal is 1 or 2 or 3; var group = mirror ? 8 - nominal : nominal;
        string path; var lookup = aid;
        if (cls is 4 or 7)
        {
            var role = cls == 4 ? AreaMappingRole.Scenery : AreaMappingRole.Container;
            var key = (int)(1000 * ((aid >> 6) & 31) + ((aid >> 19) & 511));
            path = (cls == 4 ? "art/scenery/" : "art/container/") + Mapped(maps, role, key);
        }
        else if (cls == 6)
        {
            if (((aid >> 12) & 3) != 0) throw Fail(AreaErrorCode.UnsupportedArtClassOrState, AreaStage.Appearance);
            var type = aid & 15; var subtype = (aid >> 6) & 15; var number = (aid >> 17) & 2047; var coverage = (aid >> 14) & 7;
            var key = checked((int)(number + 20 * (subtype + 50 * type) + (type == 2 && coverage != 0 ? 20 * (5 * coverage + 10) : 0)));
            path = "art/item/" + Mapped(maps, AreaMappingRole.ItemGround, key);
            lookup = aid & ~0x30U;
        }
        else if (cls is 2 or 12 or 13)
        {
            // The original's critter names (N1, N2): any animation, the weapon/shield rules and
            // the missing-art repair. Frame, rotation and palette select inside the file.
            path = OriginalCritterPath(cls, aid, maps, token);
        }
        else if (cls == 3 && PortalsAdmitted)
        {
            // Door or window (the source's portal art names; docs/research/first-building-evidence.md):
            // number 19..27, frame 14..18, rotation 11..13, type bit 10 (1 door, 0 window: entry
            // number + 1001), busted bit 9 (the name's sixth-from-last letter becomes D), flag bit 0
            // mirrors. Every portal ART has one group (MakeVisual checks it). Flag bits 1-3 and
            // the unused bits 6-8 are not admitted.
            if ((aid & 0x1CE) != 0) throw Fail(AreaErrorCode.UnsupportedArtClassOrState, AreaStage.Appearance);
            var number = (int)((aid >> 19) & 511); var door = ((aid >> 10) & 1) != 0;
            var name = Mapped(maps, AreaMappingRole.Portal, door ? number : number + 1001);
            var space = name.IndexOf(' '); if (space >= 0) name = name[..space];
            if ((aid & 0x200) != 0)
            {
                if (name.Length < 6) throw Fail(AreaErrorCode.MalformedRecord, AreaStage.Mapping);
                name = string.Concat(name.AsSpan(0, name.Length - 6), "D", name.AsSpan(name.Length - 5));
            }
            path = "art/portal/" + Safe(name);
            nominal = (int)((aid >> 11) & 7); group = 0; mirror = (aid & 1) != 0;
        }
        else if (cls == 1 && WallsAdmitted)
        {
            // Wall art: one undamaged file chosen by the structure row (interior name for
            // rotations 0,1,6,7, exterior otherwise), the piece code, damage letter U and the
            // variation digit; MakeVisual then requires a single-rotation ART for group 0.
            // Rotation and the flip/palette bits stay in the raw selector; the renderer applies them.
            // Flag bits other than the flip bit (bits 1-3) and bit 6 are not admitted.
            // Damaged walls take their damage letter (bits 0x400/0x80, N6).
            if ((aid & 0x4E) != 0) throw Fail(AreaErrorCode.UnsupportedArtClassOrState, AreaStage.Appearance);
            var number = (int)((aid >> 20) & 255); var rotation = (int)((aid >> 11) & 7); var piece = (int)((aid >> 14) & 63); var variation = (int)((aid >> 8) & 3);
            if (piece >= WallPieces.Length) throw Fail(AreaErrorCode.UnsupportedArtClassOrState, AreaStage.Appearance);
            var (damageLetter, namedPiece) = WallDamage(aid, rotation, piece);
            // Structure rows carry option words such as "/nowindows"; only the first two
            // name tokens are read, so the row is not passed through the single-name check.
            // The source indexes rows by position from key 0 up to key 1000; admit only a table
            // whose keys below 1000 are exactly 0..n-1, so key and position agree.
            if (!maps.TryGetValue(AreaMappingRole.WallStructure, out var structures)) throw Fail(AreaErrorCode.MissingSource, AreaStage.Mapping);
            var positional = structures.Keys.TakeWhile(k => k < 1000).ToArray();
            if (positional.Where((k, i) => k != i).Any()) throw Fail(AreaErrorCode.MalformedRecord, AreaStage.Mapping);
            if (!structures.TryGetValue(number, out var row) || number >= positional.Length) throw Fail(AreaErrorCode.MissingSource, AreaStage.Mapping);
            var tokens = row.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length < 2) throw Fail(AreaErrorCode.MalformedRecord, AreaStage.Mapping);
            var side = tokens[rotation / 2 is 0 or 3 ? 0 : 1];
            if (side.Length < 3) throw Fail(AreaErrorCode.MalformedRecord, AreaStage.Mapping);
            var names = maps[AreaMappingRole.WallName].Values.Where(v => v.Length >= 3 && string.Equals(v[..3], side[..3], StringComparison.OrdinalIgnoreCase)).ToArray();
            if (names.Length != 1) throw Fail(names.Length == 0 ? AreaErrorCode.MissingSource : AreaErrorCode.AmbiguousSource, AreaStage.Mapping);
            path = "art/wall/" + Safe(names[0][..3]) + WallPieces[namedPiece] + damageLetter + (char)('0' + variation) + ".art";
            nominal = rotation; group = 0; frame = 0; mirror = (aid & 1) != 0; lookup = aid;
        }
        else if (cls == 0 && _objectTiles is not null)
        {
            // A tile id on an object names the tile's file as a terrain cell does (N7): no
            // rotation or frame, palette from bits 4-5, and a flippable tile (bits 6, 7) with
            // flag bit 0 is drawn mirrored.
            lookup = aid & ~63U;
            try { path = TerrainLoader.TilePath(_objectTiles, lookup).Path; }
            catch (TerrainException) { throw Fail(AreaErrorCode.MissingSource, AreaStage.Mapping); }
            nominal = 0; group = 0; frame = 0; mirror = (aid & 0xC1) == 0xC1;
        }
        else throw Fail(AreaErrorCode.UnsupportedArtClassOrState, AreaStage.Appearance);
        AreaEntryIdentity entry; var badArt = false;
        var explicitBindings = _plan.ArtBindings.Where(b => b.RawArt == aid).ToArray();
        if (explicitBindings.Length > 1) throw Fail(AreaErrorCode.AmbiguousSource, AreaStage.Appearance);
        if (explicitBindings.Length == 1)
        {
            entry = Unique(explicitBindings[0].Entry.Source, path);
            if (entry != explicitBindings[0].Entry) throw Fail(AreaErrorCode.InputIdentityMismatch, AreaStage.Appearance);
        }
        else (entry, badArt) = ResolveOrBadArt(path);
        // Bad art has one rotation and one frame; only frame 0 is defined (N4).
        if (badArt) { group = 0; frame = 0; }
        return new(entry, lookup, nominal, group, frame, palette, mirror, badArt ? BadArtPolicy : null);
    }
    /// <summary>A resource is read from whichever resource archive holds it, as the original's file system
    /// does across its mounted archives: the first in the plan's stack order (ARC-DATA-018), or, without
    /// one, the one of the two base archives that holds it (ARC-DATA-036: every plan, pinned profiles too).</summary>
    private AreaEntryIdentity Resolve(string path) => ClosedUnique(path);
    private AreaEntryIdentity ClosedUnique(string path)
    {
        if (_plan.ResourceOrder.Count > 0) return StackFirst(path);
        var matches = new List<AreaEntryIdentity>();
        foreach (var role in new[] { AreaSourceRole.Resource1, AreaSourceRole.Resource2 })
        {
            var key = new AreaInputKey(role);
            if (!_indexes.TryGetValue(key, out var index) || !index.TryGetValue(path.Replace('\\', '/').ToLowerInvariant(), out var entries)) continue;
            matches.AddRange(entries.Select(e => _entryIdentities[(key, e.Ordinal)]));
        }
        if (matches.Count != 1) throw Fail(matches.Count == 0 ? AreaErrorCode.MissingSource : AreaErrorCode.AmbiguousSource, AreaStage.Appearance);
        return matches[0];
    }
    // DAT record flags the content stack skips (ContentStack): a directory record and an ignored member hold no file.
    private const uint DirectoryRecord = 0x400, IgnoredRecord = 0x800;
    /// <summary>ARC-DATA-018: the first source of the plan's stack order that holds the path as a file.
    /// An archive holding it twice is ambiguous (the content stack refuses such an index).</summary>
    private AreaEntryIdentity StackFirst(string path)
    {
        var normalized = path.Replace('\\', '/').ToLowerInvariant();
        _loose.TryGetValue(normalized, out var loose);
        foreach (var source in _plan.ResourceOrder)
        {
            if (source.Key.Role == AreaSourceRole.Loose)
            {
                if (source.Path == normalized && source.Key == loose) return _entryIdentities[(loose, 0)];
                continue;
            }
            if (!_indexes.TryGetValue(source.Key, out var index) || !index.TryGetValue(normalized, out var entries)) continue;
            var files = entries.Where(e => (e.Flags & (DirectoryRecord | IgnoredRecord)) == 0).ToArray();
            if (files.Length > 1) throw Fail(AreaErrorCode.AmbiguousSource, AreaStage.Appearance);
            if (files.Length == 1) return _entryIdentities[(source.Key, files[0].Ordinal)];
        }
        throw Fail(AreaErrorCode.MissingSource, AreaStage.Appearance);
    }
    private AreaVisualRecord MakeVisual(AreaVisualKey key, uint raw, uint lookup, AreaEntryIdentity entry, int nominal, int group, int frame, int palette, bool mirrored,
        int ox, int oy, int scale, ObjectWireRecord? record, CancellationToken token, string? fallbackPolicy = null)
    {
        token.ThrowIfCancellationRequested();
        // Each distinct frame selection is decoded once per build (ARC-DATA-024): the selection
        // fixes the decode, so later visuals copy its frame facts.
        var shared = _sharedFrames is not null ? (entry, group, frame, palette, raw >> 28 is 1 or 3, fallbackPolicy) : default;
        if (_sharedFrames is not null && _sharedFrames.TryGetValue(shared, out var template))
            return template with { Key = key, RawArt = raw, LookupArt = lookup, NominalFacing = nominal, Mirrored = mirrored, ObjectOffsetX = ox, ObjectOffsetY = oy, Scale = scale };
        _budget.Context = new(AreaBudgetCallsite.Pixels, entry.Source, entry.Ordinal, entry.Offset, key.SourceIndex);
        _budget.StartFrame(entry.StoredBytes, entry.DecodedBytes);
        var (bytes, sha) = ReadFrameEntry(entry); var policy = "strict-full-entry"; var view = bytes.Length; var physical = group;
        // A wall or portal is drawn from group 0 only when its ART holds a single rotation; an
        // eight-rotation ART would need the rotation's own group, which is not admitted.
        if (raw >> 28 is 1 or 3 && (bytes.Length < 4 || (BinaryPrimitives.ReadUInt32LittleEndian(bytes) & 1) == 0))
            throw Fail(AreaErrorCode.UnsupportedPhysicalSelection, AreaStage.Frame);
        // ART is read as the original reads it (ARC-DATA-036: every plan). Bytes after the last frame are no part of
        // the ART: the frame is read from the ART's own extent, the view. A single-rotation ART has only group 0,
        // whatever rotation the object asks for (the art id's rotation-free flag).
        if (bytes.Length >= 4 && (BinaryPrimitives.ReadUInt32LittleEndian(bytes) & 1) == 1 && physical != 0)
            (policy, physical) = (SingleGroupPolicy, 0);
        var descriptor = InspectFrame(bytes, physical, frame, palette);
        if (descriptor.Trailing > 0) (policy, view) = (physical == group ? TrailingBytesPolicy : policy, bytes.Length - descriptor.Trailing);
        if (fallbackPolicy is not null) policy = fallbackPolicy;
        _budget.Context = _budget.Context with { Callsite = AreaBudgetCallsite.Pixels };
        _budget.CompleteFrame(checked((long)descriptor.Metadata.Width * descriptor.Metadata.Height * 4), descriptor.Metadata.EncodedPayloadLength);
        token.ThrowIfCancellationRequested();
        var decoded = DecodeFrame(bytes.AsSpan(0, view), new(physical, frame, palette));
        var rgbaSha = HashPixels(decoded);
        _budget.EndFrame();
        var visual = new AreaVisualRecord(key, raw, lookup, entry, sha, view, nominal, group, physical, frame, palette, decoded.EffectivePaletteSlot, mirrored,
            decoded.Width, decoded.Height, decoded.Frame.HotX, decoded.Frame.HotY, decoded.Frame.OffsetX, decoded.Frame.OffsetY, ox, oy, scale,
            descriptor.Metadata.EncodedPayloadLength, decoded.Rgba.Count, rgbaSha, policy)
        { FrameCount = descriptor.GroupFrames, FramesPerSecond = descriptor.FramesPerSecond };
        if (_sharedFrames is not null) { _budget.Reserve(512); _sharedFrames.Add(shared, visual); }
        return visual;
    }
    // A lease authenticates the whole source before and after each frame. A frame batch
    // authenticates each source before its first frame (inside that admitted frame, as a
    // lease does) and once more at Complete instead.
    private ArtFrame DecodeDescriptor(AreaVisualRecord descriptor, CancellationToken token, bool authenticateBefore = true, bool authenticateAfter = true)
    {
        _budget.Context = new(AreaBudgetCallsite.Pixels, descriptor.Entry.Source, descriptor.Entry.Ordinal, descriptor.Entry.Offset, descriptor.Key.SourceIndex);
        _budget.StartFrame(descriptor.Entry.StoredBytes, descriptor.Entry.DecodedBytes, descriptor.RgbaBytes, descriptor.EncodedBytes);
        var identity = Identity(descriptor.Entry.Source);
        if (authenticateBefore) Authenticate(identity, token);
        var (bytes, payloadSha) = ReadFrameEntry(descriptor.Entry);
        if (payloadSha != descriptor.PayloadSha256 || descriptor.ViewLength < 0 || descriptor.ViewLength > bytes.Length)
            throw Fail(AreaErrorCode.InputChangedDuringRead, AreaStage.Frame);
        var metadata = InspectFrame(bytes.AsSpan(0, descriptor.ViewLength), descriptor.PhysicalGroup, descriptor.Frame, descriptor.RequestedPalette);
        if (metadata.Metadata.Width != descriptor.Width || metadata.Metadata.Height != descriptor.Height || metadata.Metadata.HotX != descriptor.HotX || metadata.Metadata.HotY != descriptor.HotY ||
            metadata.Metadata.OffsetX != descriptor.ArtOffsetX || metadata.Metadata.OffsetY != descriptor.ArtOffsetY || metadata.Metadata.EncodedPayloadLength != descriptor.EncodedBytes)
            throw Fail(AreaErrorCode.InputChangedDuringRead, AreaStage.Frame);
        token.ThrowIfCancellationRequested();
        _budget.Context = _budget.Context with { Callsite = AreaBudgetCallsite.Pixels };
        _budget.CompleteFrameAllocation(descriptor.RgbaBytes);
        var frame = DecodeFrame(bytes.AsSpan(0, descriptor.ViewLength), new(descriptor.PhysicalGroup, descriptor.Frame, descriptor.RequestedPalette));
        if (HashPixels(frame) != descriptor.RgbaSha256 || frame.EffectivePaletteSlot != descriptor.EffectivePalette)
            throw Fail(AreaErrorCode.InputChangedDuringRead, AreaStage.Frame);
        if (authenticateAfter) Authenticate(identity, token);
        return frame;
    }
    private ArtDecoderOptions? _lenientArt;
    private ArtDecoderOptions LenientArt
    {
        get
        {
            var strict = WireLimits.ArtOptions;
            return _lenientArt ??= new()
            {
                MaxInputBytes = (int)EntryCap(_plan), MaxEncodedFrameBytes = (int)EntryCap(_plan), MaxFrameCount = strict.MaxFrameCount,
                MaxDimension = strict.MaxDimension, MaxFrameTableBytes = strict.MaxFrameTableBytes, MaxFramePixels = strict.MaxFramePixels,
                MaxRgbaBytes = strict.MaxRgbaBytes, AllowTrailingBytes = true,
            };
        }
    }
    private ArtDecoderOptions ArtOptions => LenientArt;
    internal const string TrailingBytesPolicy = "original-art-extent-v1", SingleGroupPolicy = "original-single-group-rotation-v1";
    private ArtFrame DecodeFrame(ReadOnlySpan<byte> bytes, ArtFrameSelection selection)
    {
        try { return ArtDecoder.Decode(bytes, selection, ArtOptions); }
        catch (ArtException error) { throw Fail(error is ArtLimitExceededException ? AreaErrorCode.BudgetExceeded : AreaErrorCode.MalformedRecord, AreaStage.Frame); }
        catch (ArgumentOutOfRangeException) { throw Fail(AreaErrorCode.UnsupportedPhysicalSelection, AreaStage.Frame); }
    }
    /// <summary>The selected frame's metadata, the ART's frame count and the bytes after its last frame payload.</summary>
    private (ArtFrameMetadata Metadata, int Frames, int Trailing, int GroupFrames, int FramesPerSecond) InspectFrame(ReadOnlySpan<byte> bytes, int group, int frame, int palette)
    {
        _budget.Context = _budget.Context with { Callsite = AreaBudgetCallsite.ArtTable };
        if (bytes.Length < 132) throw Fail(AreaErrorCode.UnsupportedPhysicalSelection, AreaStage.Frame);
        var groups = (BinaryPrimitives.ReadUInt32LittleEndian(bytes) & 1) == 0 ? 8 : 1;
        var frames = BinaryPrimitives.ReadInt32LittleEndian(bytes[32..]);
        if (frames <= 0 || frames > 4096 / groups) throw Fail(AreaErrorCode.BudgetExceeded, AreaStage.Frame);
        _budget.FrameAllocation(checked(frames * (long)groups * 384 + 16384));
        try
        {
            var metadata = ArtDecoder.Inspect(bytes, ArtOptions);
            if (palette is < 0 or > 3) throw Fail(AreaErrorCode.UnsupportedPhysicalSelection, AreaStage.Frame);
            return (metadata.GetFrame(group, frame), frames * groups, metadata.TrailingBytes, frames, metadata.Header.FramesPerSecond);
        }
        catch (Exception error) when (error is ArtException or ArgumentOutOfRangeException)
        { throw Fail(AreaErrorCode.UnsupportedPhysicalSelection, AreaStage.Frame); }
    }
    private string HashPixels(ArtFrame frame)
    {
        // Avoid an escaping copy and charge the explicit hash scratch.
        _budget.Context = _budget.Context with { Callsite = AreaBudgetCallsite.PixelHash };
        _budget.FrameAllocation(4096);
        using var hash = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
        Span<byte> chunk = stackalloc byte[4096];
        for (var start = 0; start < frame.Rgba.Count; start += chunk.Length)
        {
            var size = Math.Min(chunk.Length, frame.Rgba.Count - start);
            for (var at = 0; at < size; at++) chunk[at] = frame.Rgba[start + at];
            hash.AppendData(chunk[..size]);
        }
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }
    private sealed record Request(AreaEntryIdentity Entry, uint Lookup, int Nominal, int Group, int Frame, int Palette, bool Mirror, string? Policy = null);
    /// <summary>Doors and windows are drawn only by plans that bind the portal names.</summary>
    private bool PortalsAdmitted => _plan.Mappings.Any(m => m.Role == AreaMappingRole.Portal);
    private bool WallsAdmitted => _plan.Mappings.Any(m => m.Role == AreaMappingRole.WallStructure) && _plan.Mappings.Any(m => m.Role == AreaMappingRole.WallName);
    // Wall piece file codes by piece index: the file naming order of the source's wall file name builder,
    // docs/research/cave-transition-evidence.md T7.
    private static readonly string[] WallPieces =
    [
        "bse", "lfc", "bse", "bcl", "bcr", "tcl", "tcr", "uec", "lec", "w3l", "w3a", "w3r", "w4l", "w4a", "w4b", "w4r",
        "w5l", "w5a", "w5b", "w5c", "w5r", "d3l", "d3a", "d3r", "d4l", "d4a", "d4b", "d4r", "d6l", "d6a", "d6b", "d6c",
        "d6d", "d6r", "p3l", "p3a", "p3r", "p4l", "p4a", "p4b", "p4r", "p5l", "p5a", "p5b", "p5c", "p5r",
    ];
}
