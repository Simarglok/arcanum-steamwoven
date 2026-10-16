using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using Arcanum.Data.Objects;

namespace Arcanum.Data.Area;

public sealed partial class AreaDataOwner
{
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
            var text = Encoding.ASCII.GetString(bytes);
            if (bytes.Any(b => b > 127)) throw Fail(AreaErrorCode.MalformedRecord, AreaStage.Mapping);
            // MES paired braces with unbraced annotations. No atoi/truncation,
            // duplicate key, nested brace or unpaired-token convenience recovery.
            var table = new SortedDictionary<int, string>(); var at = 0;
            while (at < text.Length)
            {
                var open = text.IndexOf('{', at); var stray = text.IndexOf('}', at);
                if (open < 0) { if (stray >= 0) throw Fail(AreaErrorCode.MalformedRecord, AreaStage.Mapping); break; }
                if (stray >= 0 && stray < open) throw Fail(AreaErrorCode.MalformedRecord, AreaStage.Mapping);
                var close = text.IndexOf('}', open + 1);
                if (close < 0 || text.AsSpan(open + 1, close - open - 1).Contains('{') || !int.TryParse(text.AsSpan(open + 1, close - open - 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out var key))
                    throw Fail(AreaErrorCode.MalformedRecord, AreaStage.Mapping);
                at = close + 1; while (at < text.Length && char.IsWhiteSpace(text[at])) at++;
                if (at == text.Length || text[at] != '{') throw Fail(AreaErrorCode.MalformedRecord, AreaStage.Mapping);
                close = text.IndexOf('}', at + 1);
                if (close < 0 || text.AsSpan(at + 1, close - at - 1).Contains('{') || close - at > 1048576) throw Fail(AreaErrorCode.MalformedRecord, AreaStage.Mapping);
                if (!table.TryAdd(key, text[(at + 1)..close])) throw Fail(AreaErrorCode.AmbiguousSource, AreaStage.Mapping);
                if (table.Count > 8192) throw Fail(AreaErrorCode.BudgetExceeded, AreaStage.Mapping);
                at = close + 1;
            }
            if (table.Count == 0) throw Fail(AreaErrorCode.MissingSource, AreaStage.Mapping);
            result.Add(binding.Role, table);
        }
        if (!result.ContainsKey(AreaMappingRole.Tile) || !result.ContainsKey(AreaMappingRole.Facade)) throw Fail(AreaErrorCode.MissingSource, AreaStage.Mapping);
        return result;
    }
    private AreaInputKey MappingSource(AreaMappingRole role) => _plan.Mappings.SingleOrDefault(m => m.Role == role)?.Entry.Source ?? throw Fail(AreaErrorCode.MissingSource, AreaStage.Mapping);
    private static string Mapped(Dictionary<AreaMappingRole, SortedDictionary<int, string>> maps, AreaMappingRole role, int key) => maps.TryGetValue(role, out var table) && table.TryGetValue(key, out var value) ? Safe(value) : throw Fail(AreaErrorCode.MissingSource, AreaStage.Mapping);
    private Request ObjectRequest(uint aid, Dictionary<AreaMappingRole, SortedDictionary<int, string>> maps, CancellationToken token)
    {
        var cls = aid >> 28; var frame = cls == 6 ? 0 : (int)((aid >> 14) & 31); var nominal = cls == 6 ? 0 : (int)((aid >> 11) & 7);
        var palette = (int)((aid >> 4) & 3); var mirror = cls is 2 or 12 or 13 && nominal is 1 or 2 or 3; var group = mirror ? 8 - nominal : nominal;
        string path; AreaInputKey? source = null; var lookup = aid;
        if (cls is 4 or 7)
        {
            var role = cls == 4 ? AreaMappingRole.Scenery : AreaMappingRole.Container;
            var key = (int)(1000 * ((aid >> 6) & 31) + ((aid >> 19) & 511));
            path = (cls == 4 ? "art/scenery/" : "art/container/") + Mapped(maps, role, key); source = MappingSource(role);
        }
        else if (cls == 6)
        {
            if (((aid >> 12) & 3) != 0) throw Fail(AreaErrorCode.UnsupportedArtClassOrState, AreaStage.Appearance);
            var type = aid & 15; var subtype = (aid >> 6) & 15; var number = (aid >> 17) & 2047; var coverage = (aid >> 14) & 7;
            var key = checked((int)(number + 20 * (subtype + 50 * type) + (type == 2 && coverage != 0 ? 20 * (5 * coverage + 10) : 0)));
            path = "art/item/" + Mapped(maps, AreaMappingRole.ItemGround, key); source = MappingSource(AreaMappingRole.ItemGround);
            lookup = aid & ~0x30U;
        }
        else if (cls is 2 or 12 or 13)
        {
            var animation = (int)((aid >> 6) & 31); var weapon = (int)(aid & 15); var shield = (int)((aid >> 19) & 1);
            var armor = cls == 13 ? 0 : (int)((aid >> 20) & (cls == 2 ? 15U : 7U));
            if (animation is not 0 and not 7 || armor > 8 || weapon > 14) throw Fail(AreaErrorCode.UnsupportedArtClassOrState, AreaStage.Appearance);
            int population;
            if (cls == 2)
            {
                var body = (int)((aid >> 24) & 7); var gender = (int)((aid >> 27) & 1);
                if (body > 4) throw Fail(AreaErrorCode.UnsupportedArtClassOrState, AreaStage.Appearance);
                if (body == 4 && gender == 0) body = 0;
                if (armor is 4 or 6) { gender = 1; body = body == 4 ? 0 : body == 2 ? 1 : body; }
                population = body * 2 + gender;
            }
            else population = cls == 12 ? (int)((aid >> 23) & 31) : (int)((aid >> 20) & 255);
            if (animation == 0 && weapon == 0)
            {
                var stand = NpcPath(cls, population, 0, 0, 0, 0, maps);
                _ = ClosedUnique(stand); // PRESENT only; absent/ambiguous is strict failure, no weapon1 repair.
                if (_plan.Original)
                {
                    token.ThrowIfCancellationRequested();
                    var missing = _plan.Mappings.Single(m => m.Role == AreaMappingRole.MissingPolicy);
                    using var payload = new AreaOwnership(_budget);
                    var bytes = ReadEntry(missing.Entry, token, payload);
                    if (ObjectWireReader.Hash(bytes) != missing.PayloadSha256 || bytes.Length != 332 || BinaryPrimitives.ReadUInt32LittleEndian(bytes) != 4601)
                        throw Fail(AreaErrorCode.InputIdentityMismatch, AreaStage.Appearance);
                    var start = cls == 2 ? 1 : cls == 12 ? 11 : 43;
                    var max = cls == 2 ? 10 : cls == 12 ? 32 : 40;
                    if (population >= max || (BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan((start + population) * 4)) & 8) != 0)
                        throw Fail(AreaErrorCode.UnsupportedArtClassOrState, AreaStage.Appearance);
                }
            }
            if (animation == 7) { weapon = 0; shield = 0; }
            path = NpcPath(cls, population, armor, animation, weapon, shield, maps);
            lookup = aid; // source selector transformations are recorded, never replace raw state
        }
        else throw Fail(AreaErrorCode.UnsupportedArtClassOrState, AreaStage.Appearance);
        AreaEntryIdentity entry;
        var explicitBindings = _plan.ArtBindings.Where(b => b.RawArt == aid).ToArray();
        if (explicitBindings.Length > 1) throw Fail(AreaErrorCode.AmbiguousSource, AreaStage.Appearance);
        if (explicitBindings.Length == 1)
        {
            entry = Unique(explicitBindings[0].Entry.Source, path);
            if (entry != explicitBindings[0].Entry) throw Fail(AreaErrorCode.InputIdentityMismatch, AreaStage.Appearance);
        }
        else entry = source.HasValue ? Unique(source.Value, path) : ClosedUnique(path);
        return new(entry, lookup, nominal, group, frame, palette, mirror);
    }
    private AreaEntryIdentity ClosedUnique(string path)
    {
        var matches = new List<AreaEntryIdentity>();
        foreach (var role in new[] { AreaSourceRole.Resource1, AreaSourceRole.Resource2 })
        {
            var key = new AreaInputKey(role);
            if (!_indexes.TryGetValue(key, out var index) || !index.TryGetValue(path.ToLowerInvariant(), out var entries)) continue;
            matches.AddRange(entries.Select(e => _entryIdentities[(key, e.Ordinal)]));
        }
        if (matches.Count != 1) throw Fail(matches.Count == 0 ? AreaErrorCode.MissingSource : AreaErrorCode.AmbiguousSource, AreaStage.Appearance);
        return matches[0];
    }
    private static string NpcPath(uint cls, int population, int armor, int animation, int weapon, int shield, Dictionary<AreaMappingRole, SortedDictionary<int, string>> maps)
    {
        string[] armors = ["UW", "V1", "LA", "CM", "PM", "RB", "PC", "BN", "CD"];
        const string weapons = "ABCDEFGHIJKXYNZ";
        var suffix = (shield == 0 ? "X" : "S") + weapons[weapon == 7 && shield != 0 ? 3 : weapon] + (char)('a' + animation) + ".art";
        if (cls == 2)
        {
            string[] bodies = ["HM", "DF", "GH", "HG", "EF"];
            if (population < 0 || population >= 10) throw Fail(AreaErrorCode.UnsupportedArtClassOrState, AreaStage.Appearance);
            var label = bodies[population / 2] + (armor is 4 or 6 ? "X" : population % 2 == 0 ? "F" : "M");
            return "art/critter/" + label + "/" + label + armors[armor] + suffix;
        }
        var name = Mapped(maps, cls == 12 ? AreaMappingRole.MonsterNames : AreaMappingRole.UniqueNpcNames, population);
        return (cls == 12 ? "art/monster/" : "art/unique_npc/") + name + "/" + name + (cls == 12 ? armors[armor] : string.Empty) + suffix;
    }

    private AreaVisualRecord MakeVisual(AreaVisualKey key, uint raw, uint lookup, AreaEntryIdentity entry, int nominal, int group, int frame, int palette, bool mirrored,
        int ox, int oy, int scale, ObjectWireRecord? record, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        _budget.Context = new(AreaBudgetCallsite.Pixels, entry.Source, entry.Ordinal, entry.Offset, key.SourceIndex);
        _budget.StartFrame(entry.StoredBytes, entry.DecodedBytes);
        var bytes = ReadEntry(entry); var sha = ObjectWireReader.Hash(bytes); var policy = "strict-full-entry"; var view = bytes.Length; var physical = group;
        if (_plan.Original && record is not null)
            (policy, view, physical) = OriginalProfile.SelectionPolicy(key, raw, entry, bytes, record, nominal, group, frame, palette);
        var descriptor = InspectFrame(bytes.AsSpan(0, view), physical, frame, palette);
        _budget.Context = _budget.Context with { Callsite = AreaBudgetCallsite.Pixels };
        _budget.CompleteFrame(checked((long)descriptor.Metadata.Width * descriptor.Metadata.Height * 4), descriptor.Metadata.EncodedPayloadLength);
        token.ThrowIfCancellationRequested();
        var decoded = DecodeFrame(bytes.AsSpan(0, view), new(physical, frame, palette));
        var rgbaSha = HashPixels(decoded);
        _budget.EndFrame();
        return new(key, raw, lookup, entry, sha, view, nominal, group, physical, frame, palette, decoded.EffectivePaletteSlot, mirrored,
            decoded.Width, decoded.Height, decoded.Frame.HotX, decoded.Frame.HotY, decoded.Frame.OffsetX, decoded.Frame.OffsetY, ox, oy, scale,
            descriptor.Metadata.EncodedPayloadLength, decoded.Rgba.Count, rgbaSha, policy);
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
        var bytes = ReadEntry(descriptor.Entry);
        if (ObjectWireReader.Hash(bytes) != descriptor.PayloadSha256 || descriptor.ViewLength < 0 || descriptor.ViewLength > bytes.Length)
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
    private static ArtFrame DecodeFrame(ReadOnlySpan<byte> bytes, ArtFrameSelection selection)
    {
        try { return ArtDecoder.Decode(bytes, selection, WireLimits.ArtOptions); }
        catch (ArtException error) { throw Fail(error is ArtLimitExceededException ? AreaErrorCode.BudgetExceeded : AreaErrorCode.MalformedRecord, AreaStage.Frame); }
        catch (ArgumentOutOfRangeException) { throw Fail(AreaErrorCode.UnsupportedPhysicalSelection, AreaStage.Frame); }
    }
    private (ArtFrameMetadata Metadata, int Frames) InspectFrame(ReadOnlySpan<byte> bytes, int group, int frame, int palette)
    {
        _budget.Context = _budget.Context with { Callsite = AreaBudgetCallsite.ArtTable };
        if (bytes.Length < 132) throw Fail(AreaErrorCode.UnsupportedPhysicalSelection, AreaStage.Frame);
        var groups = (BinaryPrimitives.ReadUInt32LittleEndian(bytes) & 1) == 0 ? 8 : 1;
        var frames = BinaryPrimitives.ReadInt32LittleEndian(bytes[32..]);
        if (frames <= 0 || frames > 4096 / groups) throw Fail(AreaErrorCode.BudgetExceeded, AreaStage.Frame);
        _budget.FrameAllocation(checked(frames * (long)groups * 384 + 16384));
        try
        {
            var metadata = ArtDecoder.Inspect(bytes, WireLimits.ArtOptions);
            if (palette is < 0 or > 3) throw Fail(AreaErrorCode.UnsupportedPhysicalSelection, AreaStage.Frame);
            return (metadata.GetFrame(group, frame), frames * groups);
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
    private sealed record Request(AreaEntryIdentity Entry, uint Lookup, int Nominal, int Group, int Frame, int Palette, bool Mirror);
}
