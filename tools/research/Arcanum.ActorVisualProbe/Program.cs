using System.Buffers.Binary;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using Arcanum.Data;

namespace Arcanum.ActorVisualProbe;

internal static class Program
{
    private const int ExitFailure = 1;
    private const int ExitUsage = 2;
    private const int ExitNotReady = 3;
    private const int ObjectVersion = 119;
    private const int MaxEligibleRecords = 256;
    private const int MaxEntryBytes = 1024 * 1024;
    private const int NpcObjectType = 16;
    private const int PlayerObjectType = 15;
    private const int MonsterArtType = 12;
    private const int StandAction = 0;
    private const int WalkAction = 1;
    private const int CurrentArtField = 0;

    private static readonly WireKind[] CommonFields = ParseFields("i,p64,i*3,au32*3,i*7,au32*3,i*12,ai32,ascript,i*2,au32,au64");
    private static readonly WireKind[] CritterFields = ParseFields("i*2,ai32*3,au32,i*4,au32*2,poid,i,poid*5,i,aoid,i*2,poid,p64,i*6,au32,au64");
    private static readonly WireKind[] NpcFields = ParseFields("i,poid,i,poid*2,i*2,au64,i,p64*2,i*3,poid,i*2,aoid,au32*2,i*3,au32,aoid");
    private static readonly WireKind[] PcFields = ParseFields("i*2,au32,au64,i*2,aquest,au32,au64,au32,au64,i,au64,au32*3,i,s,i,au32*2,i*2,au32,au64");

    private static int Main(string[] args)
    {
        try
        {
            if (args.SequenceEqual(["--self-test"]))
            {
                SelfTest();
                Console.WriteLine("self-test: PASS");
                return 0;
            }

            var input = Input.Parse(args);
            if (input is null)
            {
                Console.Error.WriteLine("usage: --self-test | --module PATH --resource PATH --prototype-root PATH --source-id SAFE_LABEL");
                return ExitUsage;
            }
            return Probe(input, Console.Out);
        }
        catch (ProbeException error)
        {
            Console.Error.WriteLine($"actor-visual-probe: FAIL stage={error.Stage} reason={error.Reason}");
            return ExitFailure;
        }
        catch (Exception)
        {
            Console.Error.WriteLine("actor-visual-probe: FAIL stage=internal reason=unexpected");
            return ExitFailure;
        }
    }

    // This is deliberately a research reader rather than a production object parser. It consumes
    // every selected wire value only to establish one bounded envelope and current-ART resolution.
    private static int Probe(Input input, TextWriter output)
    {
        var before = Fingerprints(input);
        var result = new Census();
        Candidate? selected = null;
        try
        {
            using var module = DatArchive.Open(input.ModulePath, DatOptions());
            using var resource = DatArchive.Open(input.ResourcePath, DatOptions());
            var monsterNames = ReadMonsterNames(resource);

            foreach (var entry in module.Entries.Where(IsMob).OrderBy(entry => entry.Ordinal))
            {
                // Once 256 full v119 player/NPC envelopes have been admitted, do not even
                // examine another module entry. Malformed/non-v119 entries before that point
                // remain rejections rather than consuming the eligible-envelope budget.
                if (result.Eligible == MaxEligibleRecords) break;
                if (!Bounded(entry)) { result.Add("entry-cap"); continue; }
                ObjectRecord record;
                try { record = ParseInstance(module.ReadEntry(entry)); }
                catch (ProbeException error)
                {
                    result.Add($"record-{error.Reason}");
                    continue;
                }
                // Only a successfully full-consumed v119 player/NPC envelope is eligible for the cap.
                result.Eligible++;
                if (record.Type == PlayerObjectType) { result.PlayerExcluded++; continue; }
                if (record.Type != NpcObjectType) { result.Add("non-npc"); continue; }
                result.Npc++;

                var effective = ResolveEffectiveArt(record, input.PrototypeRoot, result);
                if (effective is null) continue;
                if (!IsSingleLayerMonster(effective.Value.Art)) { result.Add("composite-or-nonmonster"); continue; }

                var examined = InspectActions(resource, monsterNames, effective.Value.Art, result);
                if (examined is null) continue;
                var candidate = new Candidate(entry.Ordinal, effective.Value.Source, effective.Value.Art, examined.Value);
                if (selected is null || Candidate.Compare(candidate, selected) < 0) selected = candidate;
            }
        }
        catch (DatException) { throw new ProbeException("archive", "unavailable-or-invalid"); }
        finally
        {
            var after = Fingerprints(input);
            if (!before.SequenceEqual(after)) throw new ProbeException("immutability", "changed-input");
        }

        // Frame metadata validates source-facing action/facing/palette selectors; source inspection
        // establishes mirror behavior and separates raw ART FPS from the movement scheduler. The
        // source does not establish an actor world-anchor delta, so completion remains not-ready.
        var status = selected is null ? "no-complete-monster-chain" : "actor-anchor-unobserved";
        output.WriteLine($"result=NOT_READY source={input.SourceId} module_length={before[0].Length} module_sha256={before[0].Sha256} resource_length={before[1].Length} resource_sha256={before[1].Sha256} prototype_aggregate_length={before[2].Length} prototype_aggregate_sha256={before[2].Sha256} eligible={result.Eligible} npc={result.Npc} player_excluded={result.PlayerExcluded} direct={result.Direct} prototype_default={result.PrototypeDefault} missing={result.Missing} invalid={result.Invalid} inspected_chains={result.InspectedChains} selected_ordinal={(selected?.Ordinal.ToString() ?? "none")} selected_source={(selected?.Source.ToString() ?? "none")} reason={status}");
        if (selected is not null) output.WriteLine("selected_metadata=" + selected.SafeMetadata());
        output.WriteLine("cadence_authority=SourceScheduler raw_art_fps=metadata-only movement_speed=not-inferred");
        output.WriteLine($"rejections={result.RenderReasons()}");
        output.WriteLine("next_evidence=measure-actor-world-anchor-to-top-left-delta-without-inferring-collision-or-movement-speed");
        return ExitNotReady;
    }

    private static InputFingerprint[] Fingerprints(Input input) =>
    [
        Fingerprint(input.ModulePath, "module"),
        Fingerprint(input.ResourcePath, "resource"),
        FingerprintDirectory(input.PrototypeRoot),
    ];

    private static EffectiveArt? ResolveEffectiveArt(ObjectRecord record, string root, Census census)
    {
        if (record.DirectCurrentArt is int direct)
        {
            census.Direct++;
            return new EffectiveArt(direct, ResolutionSource.Direct);
        }
        try
        {
            var prototype = ReadExactPrototype(root, record.Prototype);
            if (prototype.Type != record.Type || !prototype.Id.Matches(record.Prototype))
                throw new ProbeException("prototype", "mismatched");
            census.PrototypeDefault++;
            return new EffectiveArt(prototype.CurrentArt, ResolutionSource.PrototypeDefault);
        }
        catch (ProbeException error) when (error.Reason is "missing" or "ambiguous") { census.Missing++; census.Add("prototype-missing-or-ambiguous"); return null; }
        catch (ProbeException) { census.Invalid++; census.Add("prototype-invalid"); return null; }
    }

    private static bool IsSingleLayerMonster(int art)
    {
        var value = unchecked((uint)art);
        if ((value >> 28) != MonsterArtType) return false;
        var armor = (value >> 20) & 0x7;
        var shield = (value >> 19) & 0x1;
        var weapon = value & 0xf;
        return armor == 0 && shield == 0 && weapon == 0;
    }

    private static ActionEvidence? InspectActions(DatArchive resource, IReadOnlyDictionary<int, string> monsterNames, int baseArt, Census census)
    {
        try
        {
            var species = (int)((unchecked((uint)baseArt) >> 23) & 0x1f);
            if (!monsterNames.TryGetValue(species, out var directory) || !SafeComponent(directory))
                throw new ProbeException("mapping", "missing-or-unsafe");
            var actions = new List<ActionMetadata>(2);
            foreach (var action in new[] { StandAction, WalkAction })
            {
                var selector = SetAction(baseArt, action);
                var path = MonsterPath(directory, selector);
                var entry = Unique(resource, path, "resource");
                if (!Bounded(entry)) throw new ProbeException("resource", "entry-cap");
                var metadata = ArtDecoder.Inspect(resource.ReadEntry(entry), ArtOptions());
                var palette = (int)((unchecked((uint)selector) >> 4) & 0x3);
                var facings = new List<FacingMetadata>(8);
                for (var facing = 0; facing < 8; facing++)
                {
                    var physical = MapPhysicalFacing(facing);
                    if (physical.Group >= metadata.Header.PhysicalGroupCount || metadata.Header.PalettePresenceWords[palette] == 0)
                        throw new ProbeException("art", "incomplete-facing-or-palette");
                    var frames = Enumerable.Range(0, metadata.Header.FramesPerGroup).Select(frame => metadata.GetFrame(physical.Group, frame)).ToArray();
                    if (frames.Length == 0) throw new ProbeException("art", "empty-frames");
                    facings.Add(new FacingMetadata(facing, physical.Group, physical.FlipX, frames));
                }
                actions.Add(new ActionMetadata(action, entry.Ordinal, selector, metadata.Header.FramesPerSecond, facings));
            }
            census.InspectedChains++;
            return new ActionEvidence(actions[0], actions[1]);
        }
        catch (ProbeException error) { census.Add(error.Stage + "-" + error.Reason); return null; }
        catch (ArtException) { census.Add("art-invalid"); return null; }
        catch (DatException) { census.Add("resource-missing-or-ambiguous"); return null; }
    }

    // The source-specific mirror rule maps requested rotations 1..3 to 7..5 and flipX=true;
    // rotation 0 and 4..7 retain their physical ART groups. It is intentionally numeric/opaque.
    private static PhysicalFacing MapPhysicalFacing(int facing) => facing is > 0 and < 4 ? new(8 - facing, true) : new(facing, false);
    private static int SetAction(int art, int action) => unchecked((int)((unchecked((uint)art) & ~0x7c0u) | ((uint)action << 6)));

    private static string MonsterPath(string directory, int art)
    {
        var value = unchecked((uint)art);
        var armor = (int)((value >> 20) & 0x7);
        var shield = (int)((value >> 19) & 1);
        var weapon = (int)(value & 0xf);
        var action = (int)((value >> 6) & 0x1f);
        if (armor >= 9 || weapon >= 15 || action > 24) throw new ProbeException("selector", "unsupported");
        var armorCodes = new[] { "UW", "V1", "LA", "CM", "PM", "RB", "PC", "BN", "CD" };
        var weaponCodes = "ABCDEFGHIJKXYNZ";
        return $"art/monster/{directory}/{directory}{armorCodes[armor]}{(shield == 0 ? 'X' : 'S')}{weaponCodes[weapon]}{(char)('a' + action)}.art";
    }

    private static IReadOnlyDictionary<int, string> ReadMonsterNames(DatArchive resource)
    {
        var entry = Unique(resource, "art/monster/monster.mes", "mapping");
        if (!Bounded(entry)) throw new ProbeException("mapping", "entry-cap");
        return ParseMes(resource.ReadEntry(entry));
    }

    private static DatEntry Unique(DatArchive archive, string path, string stage)
    {
        var wanted = Normalize(Encoding.ASCII.GetBytes(path + "\0"));
        var matches = archive.Entries.Where(entry => Normalize(entry.RawNameBytes) == wanted).ToArray();
        if (matches.Length != 1) throw new ProbeException(stage, "missing-or-ambiguous");
        return matches[0];
    }

    private static Dictionary<int, string> ParseMes(byte[] bytes)
    {
        if (bytes.Any(value => value > 0x7f)) throw new ProbeException("mapping", "non-ascii");
        var text = Encoding.ASCII.GetString(bytes);
        var result = new Dictionary<int, string>();
        var cursor = 0;
        while (cursor < text.Length)
        {
            while (cursor < text.Length && char.IsWhiteSpace(text[cursor])) cursor++;
            if (cursor == text.Length || text[cursor] == '\0') break;
            if (text[cursor] == ';' || (cursor + 1 < text.Length && text[cursor] == '/' && text[cursor + 1] == '/'))
            {
                var end = text.IndexOf('\n', cursor); cursor = end < 0 ? text.Length : end + 1; continue;
            }
            if (text[cursor++] != '{') throw new ProbeException("mapping", "syntax");
            var keyEnd = text.IndexOf('}', cursor);
            if (keyEnd < 0 || !int.TryParse(text.AsSpan(cursor, keyEnd - cursor), out var key)) throw new ProbeException("mapping", "syntax");
            cursor = keyEnd + 1; while (cursor < text.Length && char.IsWhiteSpace(text[cursor])) cursor++;
            if (cursor == text.Length || text[cursor++] != '{') throw new ProbeException("mapping", "syntax");
            var valueEnd = text.IndexOf('}', cursor);
            if (valueEnd < 0 || !result.TryAdd(key, text[cursor..valueEnd])) throw new ProbeException("mapping", "syntax-or-duplicate");
            cursor = valueEnd + 1;
        }
        if (result.Count == 0) throw new ProbeException("mapping", "empty");
        return result;
    }

    private static ObjectRecord ParseInstance(ReadOnlySpan<byte> bytes)
    {
        var cursor = new Cursor(bytes);
        RequireVersion(ref cursor);
        var prototype = cursor.ObjectId(false);
        _ = cursor.ObjectId(false);
        var type = cursor.I32();
        if (type is < 0 or > 17) throw new ProbeException("record", "invalid-type");
        var fields = FieldsFor(type);
        var count = cursor.I16();
        var selected = ReadMasks(ref cursor, type, fields.Length, out var population);
        if (count < 0 || count != population) throw new ProbeException("record", "mask-count");
        int? currentArt = null;
        for (var field = 0; field < fields.Length; field++)
        {
            if (!selected[field]) continue;
            if (field == CurrentArtField) currentArt = cursor.I32();
            else Skip(ref cursor, fields[field]);
        }
        if (!cursor.AtEnd) throw new ProbeException("record", "trailing-bytes");
        return new ObjectRecord(type, prototype, currentArt);
    }

    private static PrototypeRecord ReadExactPrototype(string root, ObjectId required)
    {
        if (required.Aid is not int aid || aid is < 0 or > 999999) throw new ProbeException("prototype", "unlocatable");
        string[] paths;
        try { paths = Directory.EnumerateFiles(root, $"{aid:D6} - *.pro", SearchOption.TopDirectoryOnly).Order(StringComparer.Ordinal).ToArray(); }
        catch (IOException) { throw new ProbeException("prototype", "unavailable"); }
        catch (UnauthorizedAccessException) { throw new ProbeException("prototype", "unavailable"); }
        if (paths.Length == 0) throw new ProbeException("prototype", "missing");
        if (paths.Length != 1) throw new ProbeException("prototype", "ambiguous");
        var bytes = ReadBoundedFile(paths[0]);
        var cursor = new Cursor(bytes);
        RequireVersion(ref cursor);
        if (cursor.ObjectId(true).Kind != -1) throw new ProbeException("prototype", "marker");
        var id = cursor.ObjectId(false);
        var type = cursor.I32();
        if (type is < 0 or > 17) throw new ProbeException("prototype", "type");
        var fields = FieldsFor(type);
        _ = ReadMasks(ref cursor, type, fields.Length, out _);
        var currentArt = cursor.I32();
        for (var field = 1; field < fields.Length; field++) Skip(ref cursor, fields[field]);
        if (!cursor.AtEnd) throw new ProbeException("prototype", "trailing-bytes");
        return new PrototypeRecord(id, type, currentArt);
    }

    private static byte[] ReadBoundedFile(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length <= 0 || stream.Length > MaxEntryBytes) throw new ProbeException("prototype", "entry-cap");
            var bytes = new byte[checked((int)stream.Length)];
            stream.ReadExactly(bytes);
            if (stream.ReadByte() != -1) throw new ProbeException("prototype", "entry-cap");
            return bytes;
        }
        catch (IOException) { throw new ProbeException("prototype", "unavailable"); }
        catch (UnauthorizedAccessException) { throw new ProbeException("prototype", "unavailable"); }
    }

    private static void RequireVersion(ref Cursor cursor)
    {
        if (cursor.I32() != ObjectVersion) throw new ProbeException("record", "unsupported-version");
    }

    private static bool[] ReadMasks(ref Cursor cursor, int type, int fieldLength, out int count)
    {
        var selected = new bool[fieldLength]; count = 0;
        for (var word = 0; word < MaskWords(type); word++)
        {
            var start = MaskStart(type, word); var length = MaskLength(type, word); var mask = cursor.U32();
            var allowed = length == 32 ? uint.MaxValue : (1u << length) - 1u;
            if ((mask & ~allowed) != 0) throw new ProbeException("record", "reserved-mask");
            for (var bit = 0; bit < length; bit++) if ((mask & (1u << bit)) != 0) { selected[start + bit] = true; count++; }
        }
        return selected;
    }

    private static void Skip(ref Cursor cursor, WireKind kind)
    {
        switch (kind)
        {
            case WireKind.I32: cursor.Skip(4); return;
            case WireKind.OptI64: if (cursor.Present()) cursor.Skip(8); return;
            case WireKind.OptObjectId: if (cursor.Present()) _ = cursor.ObjectId(false); return;
            case WireKind.OptString:
                if (!cursor.Present()) return;
                var length = cursor.I32();
                if (length < 0 || length > MaxEntryBytes) throw new ProbeException("record", "string-cap");
                var text = cursor.Bytes(checked(length + 1));
                if (text[^1] != 0) throw new ProbeException("record", "string-terminator");
                return;
            default:
                if (!cursor.Present()) return;
                var size = cursor.I32(); var count = cursor.I32(); _ = cursor.I32();
                var expected = kind switch { WireKind.ArrI32 or WireKind.ArrU32 => 4, WireKind.ArrI64 or WireKind.ArrU64 => 8, WireKind.ArrScript => 12, WireKind.ArrQuest => 16, WireKind.ArrObjectId => 24, _ => throw new ProbeException("record", "wire-kind") };
                if (size != expected || count < 0 || count > MaxEntryBytes / expected) throw new ProbeException("record", "array-header");
                cursor.Skip(checked(size * count));
                var words = cursor.I32();
                if (words is < 2 or > MaxEntryBytes / 4) throw new ProbeException("record", "array-bitset");
                var bitset = cursor.Bytes(checked(words * 4)); var population = 0;
                for (var index = 0; index < words; index++) population += BitOperations.PopCount(BinaryPrimitives.ReadUInt32LittleEndian(bitset.Slice(index * 4, 4)));
                if (population != count) throw new ProbeException("record", "array-population");
                return;
        }
    }

    private static WireKind[] FieldsFor(int type) => type switch
    {
        NpcObjectType => [.. CommonFields, .. CritterFields, .. NpcFields],
        PlayerObjectType => [.. CommonFields, .. CritterFields, .. PcFields],
        _ => throw new ProbeException("record", "non-npc-envelope-unsupported")
    };
    private static int MaskWords(int type) => type is NpcObjectType or PlayerObjectType ? 5 : 3;
    private static int MaskStart(int type, int word) => word switch { 0 => 0, 1 => 32, 2 => CommonFields.Length, 3 when type is NpcObjectType or PlayerObjectType => CommonFields.Length + 32, 4 when type is NpcObjectType or PlayerObjectType => CommonFields.Length + CritterFields.Length, _ => throw new ProbeException("record", "mask-layout") };
    private static int MaskLength(int type, int word) => word switch { 0 => 32, 1 => CommonFields.Length - 32, 2 => Math.Min(32, CritterFields.Length), 3 when type is NpcObjectType or PlayerObjectType => CritterFields.Length - 32, 4 when type == NpcObjectType => NpcFields.Length, 4 when type == PlayerObjectType => PcFields.Length, _ => throw new ProbeException("record", "mask-layout") };

    private static WireKind[] ParseFields(string specification)
    {
        var result = new List<WireKind>();
        foreach (var item in specification.Split(','))
        {
            var parts = item.Split('*'); var repeat = parts.Length == 2 ? int.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture) : 1;
            var kind = parts[0] switch { "i" => WireKind.I32, "p64" => WireKind.OptI64, "ai32" => WireKind.ArrI32, "ai64" => WireKind.ArrI64, "au32" => WireKind.ArrU32, "au64" => WireKind.ArrU64, "ascript" => WireKind.ArrScript, "aquest" => WireKind.ArrQuest, "s" => WireKind.OptString, "poid" => WireKind.OptObjectId, "aoid" => WireKind.ArrObjectId, _ => throw new InvalidOperationException("Invalid independently authored wire descriptor.") };
            for (var count = 0; count < repeat; count++) result.Add(kind);
        }
        return result.ToArray();
    }

    private static DatReaderOptions DatOptions() => new() { MaxStoredEntryBytes = MaxEntryBytes, MaxDecodedEntryBytes = MaxEntryBytes };
    private static ArtDecoderOptions ArtOptions() => new() { MaxInputBytes = MaxEntryBytes, MaxFrameCount = 2048, MaxFrameTableBytes = MaxEntryBytes, MaxDimension = 4096, MaxFramePixels = MaxEntryBytes, MaxRgbaBytes = MaxEntryBytes, MaxEncodedFrameBytes = MaxEntryBytes };
    private static bool IsMob(DatEntry entry)
    {
        var raw = entry.RawNameBytes;
        return raw.Count >= 5 && raw[^1] == 0 && raw[^5] == (byte)'.' && (raw[^4] | 0x20) == (byte)'m' && (raw[^3] | 0x20) == (byte)'o' && (raw[^2] | 0x20) == (byte)'b';
    }
    private static bool Bounded(DatEntry entry) => entry.Flags is 0x001 or 0x002 && entry.StoredSize is > 0 and <= MaxEntryBytes && entry.DeclaredOutputSize is > 0 and <= MaxEntryBytes;
    private static bool SafeComponent(string value) => value.Length > 0 && value.All(character => character is >= ' ' and <= '~' && character is not '/' and not '\\' && character != '.');
    private static string Normalize(IReadOnlyList<byte> raw)
    {
        if (raw.Count == 0 || raw[^1] != 0 || raw.Take(raw.Count - 1).Any(value => value > 0x7f)) return string.Empty;
        var normalized = new byte[raw.Count - 1];
        for (var index = 0; index < normalized.Length; index++) { var value = raw[index]; normalized[index] = value == (byte)'\\' ? (byte)'/' : value is >= (byte)'A' and <= (byte)'Z' ? (byte)(value + 32) : value; }
        return Encoding.ASCII.GetString(normalized);
    }

    private static InputFingerprint Fingerprint(string path, string stage)
    {
        try { using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read); return FingerprintStream(stream, stage); }
        catch (IOException) { throw new ProbeException(stage, "unavailable"); }
        catch (UnauthorizedAccessException) { throw new ProbeException(stage, "unavailable"); }
    }
    private static InputFingerprint FingerprintDirectory(string root, long? aggregateCap = null)
    {
        try
        {
            if (!Directory.Exists(root)) throw new ProbeException("prototypes", "unavailable");
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256); long total = 0;
            var selectorBytes = new byte[4]; var lengthBytes = new byte[8];
            foreach (var path in Directory.EnumerateFiles(root, "*.pro", SearchOption.TopDirectoryOnly).Order(StringComparer.Ordinal))
            {
                var file = new FileInfo(path);
                if (file.Length <= 0 || file.Length > MaxEntryBytes) throw new ProbeException("prototypes", "entry-cap");
                var leafBytes = Encoding.UTF8.GetBytes(Path.GetFileName(path));
                AppendLengthPrefixed(hash, leafBytes);
                var selector = CanonicalPrototypeSelector(Path.GetFileName(path));
                hash.AppendData([selector is null ? (byte)0 : (byte)1]);
                if (selector is int aid)
                {
                    BinaryPrimitives.WriteInt32LittleEndian(selectorBytes, aid);
                    hash.AppendData(selectorBytes);
                }
                var fingerprint = Fingerprint(path, "prototypes");
                total = checked(total + fingerprint.Length);
                if (total > (aggregateCap ?? (long)MaxEligibleRecords * MaxEntryBytes)) throw new ProbeException("prototypes", "cap-exceeded");
                BinaryPrimitives.WriteInt64LittleEndian(lengthBytes, fingerprint.Length);
                hash.AppendData(lengthBytes);
                hash.AppendData(Convert.FromHexString(fingerprint.Sha256));
            }
            return new InputFingerprint(total, Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant());
        }
        catch (ProbeException) { throw; }
        catch (IOException) { throw new ProbeException("prototypes", "unavailable"); }
        catch (UnauthorizedAccessException) { throw new ProbeException("prototypes", "unavailable"); }
    }
    private static void AppendLengthPrefixed(IncrementalHash hash, ReadOnlySpan<byte> value)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(length, value.Length);
        hash.AppendData(length);
        hash.AppendData(value);
    }
    private static int? CanonicalPrototypeSelector(string leaf)
    {
        if (leaf.Length < 10 || leaf[6] != ' ' || leaf[7] != '-' || leaf[8] != ' ') return null;
        for (var index = 0; index < 6; index++) if (!char.IsAsciiDigit(leaf[index])) return null;
        return int.TryParse(leaf.AsSpan(0, 6), out var aid) ? aid : null;
    }
    private static InputFingerprint FingerprintStream(Stream stream, string stage)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256); var buffer = new byte[64 * 1024]; long total = 0;
        while (true) { var read = stream.Read(buffer, 0, buffer.Length); if (read == 0) break; total = checked(total + read); hash.AppendData(buffer, 0, read); }
        if (total == 0) throw new ProbeException(stage, "empty-input");
        return new InputFingerprint(total, Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant());
    }

    private static void SelfTest()
    {
        // These are independently authored, ordinary DAT/prototype fixtures. Cases that can enter
        // through Main do so; only sealed source-policy helpers remain direct assertions below.
        const string sentinel = "PRIVATE_ORIGINAL_NAME_MUST_NOT_LEAK";
        var root = Path.Combine(Path.GetTempPath(), "ActorVisualProbe-" + sentinel + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var direct = CreateInstance(NpcObjectType, ObjectId.A(7), true, unchecked((int)0xc0000000));
            var inherited = CreateInstance(NpcObjectType, ObjectId.A(8), false, 0);
            var idle = CreateArt(fps: 7, framesPerGroup: 2, geometrySeed: 10);
            var walk = CreateArt(fps: 13, framesPerGroup: 2, geometrySeed: 40);
            var safeDirectory = "safe" + sentinel;
            var completeResource = CreateResource(safeDirectory, idle, walk);
            var goodPrototype = Path.Combine(root, "000008 - " + sentinel + ".pro");
            File.WriteAllBytes(goodPrototype, CreatePrototype(NpcObjectType, ObjectId.A(8), unchecked((int)0xc0000000)));

            var positive = RunFixture(root, CreateDat(("003.mob", direct), ("001.mob", inherited), ("002.mob", CreateInstance(PlayerObjectType, ObjectId.A(9), true, 0))), completeResource);
            var repeated = RunFixture(root, CreateDat(("003.mob", direct), ("001.mob", inherited), ("002.mob", CreateInstance(PlayerObjectType, ObjectId.A(9), true, 0))), completeResource);
            Assert(positive.Exit == ExitNotReady && positive.Stdout == repeated.Stdout, "deterministic Main fixture changed");
            Assert(positive.Stdout.Contains("selected_ordinal=0", StringComparison.Ordinal) && positive.Stdout.Contains("direct=1", StringComparison.Ordinal) && positive.Stdout.Contains("prototype_default=1", StringComparison.Ordinal) && positive.Stdout.Contains("player_excluded=1", StringComparison.Ordinal), "Main did not exercise direct/inherited/player selection");
            Assert(positive.Stdout.Contains("fps=7", StringComparison.Ordinal) && positive.Stdout.Contains("fps=13", StringComparison.Ordinal) && positive.Stdout.Contains("0:11x12@13,14+15,16/1:12x13@14,15+16,17", StringComparison.Ordinal) && positive.Stdout.Contains("cadence_authority=SourceScheduler raw_art_fps=metadata-only movement_speed=not-inferred", StringComparison.Ordinal), "Main did not retain ordered frame geometry/cadence metadata");
            Assert(!positive.Stdout.Contains(sentinel, StringComparison.Ordinal) && !positive.Stderr.Contains(sentinel, StringComparison.Ordinal), "private successful inputs leaked through CLI");
            var fatal = RunCli(["--module", Path.Combine(root, sentinel + ".dat"), "--resource", "x", "--prototype-root", "x", "--source-id", "synthetic"]);
            Assert(fatal.Exit == ExitFailure && fatal.Stdout.Length == 0 && fatal.Stderr.Contains("stage=module", StringComparison.Ordinal) && !fatal.Stderr.Contains(sentinel, StringComparison.Ordinal), "fatal CLI privacy boundary changed");

            AssertCliReason(root, completeResource, WithI32(direct, 0, 118), "record-unsupported-version");
            AssertCliReason(root, completeResource, WithI32(direct, 52, 17), "record-non-npc-envelope-unsupported");
            AssertCliReason(root, completeResource, WithI16(direct, 56, -1), "record-mask-count");
            AssertCliReason(root, completeResource, WithU32(direct, 62, uint.MaxValue), "record-reserved-mask");
            AssertCliReason(root, completeResource, WithByte(CreateOptionalFixture(), 78, 2), "record-optional-presence");
            AssertCliReason(root, completeResource, WithI32(CreateArrayFixture(), 83, 999999), "record-array-header");
            AssertCliReason(root, completeResource, WithU32(CreateArrayFixture(), 99, 0), "record-array-population");
            AssertCliReason(root, completeResource, CreateStringFixture(valid: false), "record-string-terminator");
            AssertCliReason(root, completeResource, WithI32(CreateStringFixture(valid: true), 79, MaxEntryBytes + 1), "record-string-cap");
            var validString = RunFixture(root, CreateDat(("000.mob", CreateStringFixture(valid: true))), completeResource); Assert(validString.Exit == ExitNotReady && validString.Stdout.Contains("player_excluded=1", StringComparison.Ordinal), "valid string traversal did not reach player exclusion");
            AssertCliReason(root, completeResource, direct[..^1], "record-truncated");
            AssertCliReason(root, completeResource, WithTrailingByte(direct), "record-trailing-bytes");
            var exactCapString = CreateStringFixture(valid: true, textLength: MaxEntryBytes - 84);
            var exactCapResult = RunFixture(root, CreateDat(("000.mob", exactCapString)), completeResource);
            Assert(exactCapResult.Exit == ExitNotReady && exactCapResult.Stdout.Contains("player_excluded=1", StringComparison.Ordinal), "exact maximum bounded string did not traverse");

            var capEntries = Enumerable.Range(0, MaxEligibleRecords)
                .Select(index => ($"{index:D3}.mob", direct))
                .Append(("999.mob", direct[..^1]))
                .ToArray();
            var capResult = RunFixture(root, CreateDat(capEntries), completeResource);
            Assert(capResult.Exit == ExitNotReady && capResult.Stdout.Contains("eligible=256", StringComparison.Ordinal) && !capResult.Stdout.Contains("record-truncated", StringComparison.Ordinal), "post-cap module entry was inspected");

            File.Delete(goodPrototype);
            AssertCliReason(root, completeResource, inherited, "prototype-missing-or-ambiguous");
            File.WriteAllBytes(goodPrototype, CreatePrototype(NpcObjectType, ObjectId.A(8), unchecked((int)0xc0000000)));
            File.WriteAllBytes(Path.Combine(root, "000008 - duplicate.pro"), CreatePrototype(NpcObjectType, ObjectId.A(8), 0));
            AssertCliReason(root, completeResource, inherited, "prototype-missing-or-ambiguous"); File.Delete(Path.Combine(root, "000008 - duplicate.pro"));
            File.WriteAllBytes(goodPrototype, CreatePrototype(PlayerObjectType, ObjectId.A(8), 0)); AssertCliReason(root, completeResource, inherited, "prototype-invalid");
            File.WriteAllBytes(goodPrototype, CreatePrototype(NpcObjectType, ObjectId.A(9), 0)); AssertCliReason(root, completeResource, inherited, "prototype-invalid");
            File.WriteAllBytes(goodPrototype, CreatePrototype(NpcObjectType, ObjectId.A(8), 0)[..^1]); AssertCliReason(root, completeResource, inherited, "prototype-invalid");
            File.WriteAllBytes(goodPrototype, WithTrailingByte(CreatePrototype(NpcObjectType, ObjectId.A(8), 0))); AssertCliReason(root, completeResource, inherited, "prototype-invalid");
            File.WriteAllBytes(goodPrototype, new byte[MaxEntryBytes + 1]); AssertFatal(RunFixture(root, CreateDat(("000.mob", inherited)), completeResource), "prototypes", "entry-cap", "over-cap prototype did not fail closed");
            File.WriteAllBytes(goodPrototype, CreatePrototype(NpcObjectType, ObjectId.A(8), unchecked((int)0xc0000000)));

            AssertFatal(RunFixture(root, CreateDat(("000.mob", direct)), CreateDat()), "mapping", "missing-or-ambiguous", "missing mapping did not fail at Main");
            AssertFatal(RunFixture(root, CreateDat(("000.mob", direct)), CreateDat(("art/monster/monster.mes", Encoding.ASCII.GetBytes("bad")))), "mapping", "syntax", "malformed mapping did not fail at Main");
            AssertFatal(RunFixture(root, CreateDat(("000.mob", direct)), CreateDat(("art/monster/monster.mes", Encoding.ASCII.GetBytes("{0}{safe}")), ("ART/MONSTER/MONSTER.MES", Encoding.ASCII.GetBytes("{0}{safe}")))), "mapping", "missing-or-ambiguous", "duplicate mapping did not fail at Main");
            AssertCliReason(root, CreateResource("..", idle, walk), direct, "mapping-missing-or-unsafe");
            AssertCliReason(root, CreateDat(("art/monster/monster.mes", Encoding.ASCII.GetBytes("{0}{safe}")), ("art/monster/safe/safeUWXAa.art", idle)), direct, "resource-missing-or-ambiguous");
            AssertCliReason(root, CreateDat(("art/monster/monster.mes", Encoding.ASCII.GetBytes("{0}{safe}")), ("art/monster/safe/safeUWXAa.art", idle), ("ART/MONSTER/SAFE/SAFEUWXAA.ART", idle), ("art/monster/safe/safeUWXAb.art", walk)), direct, "resource-missing-or-ambiguous");
            AssertCliReason(root, CreateDat(("art/monster/monster.mes", Encoding.ASCII.GetBytes("{0}{safe}")), ("safeUWXAa.art", idle), ("safeUWXAb.art", walk)), direct, "resource-missing-or-ambiguous");
            AssertCliReason(root, CreateResource("safe", CreateArt(paletteSlot: 1), walk), direct, "art-incomplete-facing-or-palette");
            AssertCliReason(root, CreateResource("safe", idle[..^1], walk), direct, "art-invalid");
            AssertCliReason(root, CreateResource("safe", new byte[MaxEntryBytes + 1], walk), direct, "resource-entry-cap");
            AssertCliReason(root, CreateResource("safe", CreateArt(framesPerGroup: 257), walk), direct, "art-invalid");
            AssertCliReason(root, completeResource, CreateInstance(NpcObjectType, ObjectId.A(1), true, unchecked((int)0xb0000000)), "composite-or-nonmonster");
            AssertCliReason(root, completeResource, CreateInstance(NpcObjectType, ObjectId.A(1), true, unchecked((int)0xc0100000)), "composite-or-nonmonster");
            AssertCliReason(root, completeResource, CreateInstance(NpcObjectType, ObjectId.A(1), true, unchecked((int)0xc0080000)), "composite-or-nonmonster");
            AssertCliReason(root, completeResource, CreateInstance(NpcObjectType, ObjectId.A(1), true, unchecked((int)0xc0000001)), "composite-or-nonmonster");

            var beforeRename = FingerprintDirectory(root); File.Move(goodPrototype, Path.Combine(root, "000008 - renamed.pro")); var suffixRename = FingerprintDirectory(root);
            File.Move(Path.Combine(root, "000008 - renamed.pro"), Path.Combine(root, "000009 - renamed.pro")); var selectorRename = FingerprintDirectory(root);
            File.Move(Path.Combine(root, "000009 - renamed.pro"), goodPrototype); var restored = FingerprintDirectory(root);
            Assert(beforeRename.Length == suffixRename.Length && beforeRename.Sha256 != suffixRename.Sha256 && suffixRename.Sha256 != selectorRename.Sha256 && beforeRename == restored, "prototype filename/selector identity is not bound");
            File.WriteAllBytes(Path.Combine(root, "000010 - over-cap.pro"), new byte[MaxEntryBytes + 1]); Reject(() => FingerprintDirectory(root), "over-cap prototype fingerprint accepted"); File.Delete(Path.Combine(root, "000010 - over-cap.pro"));
            Reject(() => FingerprintDirectory(root, 1), "prototype aggregate cap accepted");

            var expectedFacings = new[] { new PhysicalFacing(0, false), new PhysicalFacing(7, true), new PhysicalFacing(6, true), new PhysicalFacing(5, true), new PhysicalFacing(4, false), new PhysicalFacing(5, false), new PhysicalFacing(6, false), new PhysicalFacing(7, false) };
            Assert(expectedFacings.Select((facing, index) => MapPhysicalFacing(index) == facing).All(value => value), "all source-facing group/flip bindings changed");
            Reject(() => MonsterPath("safe", SetAction(unchecked((int)0xc0000000), 25)), "out-of-domain action accepted by helper policy");
            Assert(ExitNotReady == 3 && ExitUsage == 2 && ExitFailure == 1, "exit contract changed");
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static byte[] CreateInstance(int type, ObjectId prototype, bool direct, int art)
    {
        var fields = FieldsFor(type); var masks = new uint[MaskWords(type)]; if (direct) SetMaskBit(type, masks, 0);
        var result = new List<byte>(); AddI32(result, ObjectVersion); AddObjectId(result, prototype); AddObjectId(result, ObjectId.A(1)); AddI32(result, type); AddI16(result, (short)(direct ? 1 : 0)); foreach (var mask in masks) AddU32(result, mask);
        if (direct) AddI32(result, art); return result.ToArray();
    }
    private static CliResult RunFixture(string root, byte[] module, byte[] resource)
    {
        var modulePath = Path.Combine(root, "module.dat"); var resourcePath = Path.Combine(root, "resource.dat");
        File.WriteAllBytes(modulePath, module); File.WriteAllBytes(resourcePath, resource);
        return RunCli(["--module", modulePath, "--resource", resourcePath, "--prototype-root", root, "--source-id", "synthetic"]);
    }
    private static CliResult RunCli(string[] args)
    {
        var previousOut = Console.Out; var previousError = Console.Error;
        using var output = new StringWriter(); using var error = new StringWriter();
        try { Console.SetOut(output); Console.SetError(error); return new CliResult(Main(args), output.ToString(), error.ToString()); }
        finally { Console.SetOut(previousOut); Console.SetError(previousError); }
    }
    private static void AssertCliReason(string root, byte[] resource, byte[] mob, string reason)
    {
        var result = RunFixture(root, CreateDat(("000.mob", mob)), resource);
        Assert(result.Exit == ExitNotReady && result.Stdout.Contains(reason, StringComparison.Ordinal), "Main scenario did not report " + reason);
    }
    private static void AssertFatal(CliResult result, string stage, string reason, string message)
    {
        Assert(result.Exit == ExitFailure && result.Stdout.Length == 0 && result.Stderr.Contains($"stage={stage} reason={reason}", StringComparison.Ordinal), message);
    }
    private static byte[] CreateResource(string directory, byte[] idle, byte[] walk) => CreateDat(
        ("art/monster/monster.mes", Encoding.ASCII.GetBytes("{0}{" + directory + "}")),
        ("art/monster/" + directory + "/" + directory + "UWXAa.art", idle),
        ("art/monster/" + directory + "/" + directory + "UWXAb.art", walk));
    private static void ResolvePrototypeForTest(string root, ObjectId id)
    {
        var prototype = ReadExactPrototype(root, id);
        if (!prototype.Id.Matches(id)) throw new ProbeException("prototype", "mismatched");
    }
    private static byte[] CreatePrototype(int type, ObjectId id, int art)
    {
        var fields = FieldsFor(type); var masks = new uint[MaskWords(type)]; for (var field = 0; field < fields.Length; field++) SetMaskBit(type, masks, field);
        var result = new List<byte>(); AddI32(result, ObjectVersion); AddObjectId(result, new ObjectId(-1, null, new byte[16])); AddObjectId(result, id); AddI32(result, type); foreach (var mask in masks) AddU32(result, mask);
        for (var field = 0; field < fields.Length; field++) AddWire(result, fields[field], field == 0 ? art : 0); return result.ToArray();
    }
    private static byte[] CreateOptionalFixture()
    {
        var fields = FieldsFor(NpcObjectType); var masks = new uint[MaskWords(NpcObjectType)]; SetMaskBit(NpcObjectType, masks, 1);
        var result = new List<byte>(); AddI32(result, ObjectVersion); AddObjectId(result, ObjectId.A(1)); AddObjectId(result, ObjectId.A(2)); AddI32(result, NpcObjectType); AddI16(result, 1); foreach (var mask in masks) AddU32(result, mask); result.Add(1); result.AddRange(new byte[8]); return result.ToArray();
    }
    private static byte[] CreateArrayFixture()
    {
        var fields = FieldsFor(NpcObjectType); var field = Array.FindIndex(fields, kind => kind == WireKind.ArrU32); var masks = new uint[MaskWords(NpcObjectType)]; SetMaskBit(NpcObjectType, masks, field);
        var result = new List<byte>(); AddI32(result, ObjectVersion); AddObjectId(result, ObjectId.A(1)); AddObjectId(result, ObjectId.A(2)); AddI32(result, NpcObjectType); AddI16(result, 1); foreach (var mask in masks) AddU32(result, mask); result.Add(1); AddI32(result, 4); AddI32(result, 1); AddI32(result, 0); AddI32(result, 0); AddI32(result, 2); AddU32(result, 1); AddU32(result, 0); return result.ToArray();
    }
    private static byte[] CreateStringFixture(bool valid, int textLength = 3)
    {
        if (textLength < 0) throw new ArgumentOutOfRangeException(nameof(textLength));
        var fields = FieldsFor(PlayerObjectType); var field = Array.FindIndex(fields, kind => kind == WireKind.OptString); var masks = new uint[MaskWords(PlayerObjectType)]; SetMaskBit(PlayerObjectType, masks, field);
        var result = new List<byte>(); AddI32(result, ObjectVersion); AddObjectId(result, ObjectId.A(1)); AddObjectId(result, ObjectId.A(2)); AddI32(result, PlayerObjectType); AddI16(result, 1); foreach (var mask in masks) AddU32(result, mask); result.Add(1); AddI32(result, textLength); result.AddRange(new byte[textLength]); result.Add(valid ? (byte)0 : (byte)1); return result.ToArray();
    }
    private static byte[] CreateArt(int fps = 8, int framesPerGroup = 1, int paletteSlot = 0, int geometrySeed = 0)
    {
        var frameCount = checked(8 * framesPerGroup); var paletteOffset = 132; var frameStart = paletteOffset + 1024; var payloadStart = frameStart + (frameCount * 28);
        var bytes = new byte[payloadStart + frameCount]; BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), fps); BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8), 8); BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12 + (paletteSlot * 4)), 1); BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(32), framesPerGroup);
        for (var index = 0; index < frameCount; index++)
        {
            var offset = frameStart + (index * 28); var seed = geometrySeed + index;
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset), geometrySeed == 0 ? 1 : seed + 1); BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset + 4), geometrySeed == 0 ? 1 : seed + 2); BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset + 8), 1);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset + 12), geometrySeed == 0 ? 0 : seed + 3); BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset + 16), geometrySeed == 0 ? 0 : seed + 4); BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset + 20), geometrySeed == 0 ? 0 : seed + 5); BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset + 24), geometrySeed == 0 ? 0 : seed + 6); bytes[payloadStart + index] = 1;
        }
        return bytes;
    }
    private static byte[] CreateDat(params (string Path, byte[] Payload)[] entries)
    {
        using var stream = new MemoryStream(); var offsets = new List<uint>(); foreach (var entry in entries) { offsets.Add(checked((uint)stream.Position)); stream.Write(entry.Payload); }
        using var index = new MemoryStream(); AddU32(index, (uint)entries.Length); for (var i = 0; i < entries.Length; i++) { var name = Encoding.ASCII.GetBytes(entries[i].Path + "\0"); AddU32(index, (uint)name.Length); index.Write(name); index.Write(new byte[4]); AddU32(index, 1); AddU32(index, (uint)entries[i].Payload.Length); AddU32(index, (uint)entries[i].Payload.Length); AddU32(index, offsets[i]); }
        index.Position = 0; index.CopyTo(stream); stream.Write(new byte[16]); stream.Write("1TAD"u8); AddU32(stream, 0); AddU32(stream, (uint)(index.Length + 28)); return stream.ToArray();
    }
    private static void AddWire(List<byte> result, WireKind kind, int value)
    {
        if (kind == WireKind.I32) { AddI32(result, value); return; }
        if (kind == WireKind.OptI64) { result.Add(0); return; }
        if (kind == WireKind.OptString || kind == WireKind.OptObjectId) { result.Add(0); return; }
        result.Add(0);
    }
    private static void SetMaskBit(int type, uint[] masks, int field) { for (var word = 0; word < masks.Length; word++) { var start = MaskStart(type, word); var length = MaskLength(type, word); if (field >= start && field < start + length) { masks[word] |= 1u << (field - start); return; } } throw new InvalidOperationException("field not mapped"); }
    private static byte[] WithI32(byte[] source, int offset, int value) { var copy = source.ToArray(); BinaryPrimitives.WriteInt32LittleEndian(copy.AsSpan(offset), value); return copy; }
    private static byte[] WithI16(byte[] source, int offset, short value) { var copy = source.ToArray(); BinaryPrimitives.WriteInt16LittleEndian(copy.AsSpan(offset), value); return copy; }
    private static byte[] WithU32(byte[] source, int offset, uint value) { var copy = source.ToArray(); BinaryPrimitives.WriteUInt32LittleEndian(copy.AsSpan(offset), value); return copy; }
    private static byte[] WithByte(byte[] source, int offset, byte value) { var copy = source.ToArray(); copy[offset] = value; return copy; }
    private static byte[] WithTrailingByte(byte[] source) { var copy = new byte[source.Length + 1]; source.CopyTo(copy, 0); return copy; }
    private static void AddObjectId(List<byte> result, ObjectId id) { AddI16(result, id.Kind); result.AddRange(new byte[6]); result.AddRange(id.Payload); }
    private static void AddI16(List<byte> result, short value) { var bytes = new byte[2]; BinaryPrimitives.WriteInt16LittleEndian(bytes, value); result.AddRange(bytes); }
    private static void AddI32(List<byte> result, int value) { var bytes = new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(bytes, value); result.AddRange(bytes); }
    private static void AddU32(List<byte> result, uint value) { var bytes = new byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(bytes, value); result.AddRange(bytes); }
    private static void AddU32(Stream stream, uint value) { Span<byte> bytes = stackalloc byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(bytes, value); stream.Write(bytes); }
    private static void Reject(Action action, string message) { try { action(); } catch (ProbeException) { return; } catch (ArtException) { return; } throw new InvalidOperationException(message); }
    private static void Assert(bool value, string message) { if (!value) throw new InvalidOperationException(message); }

    private sealed record Input(string ModulePath, string ResourcePath, string PrototypeRoot, string SourceId)
    {
        public static Input? Parse(string[] args)
        {
            if (args.Length != 8) return null; var values = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var index = 0; index < args.Length; index += 2) if (args[index] is not ("--module" or "--resource" or "--prototype-root" or "--source-id") || string.IsNullOrWhiteSpace(args[index + 1]) || !values.TryAdd(args[index], args[index + 1])) return null;
            return !values.TryGetValue("--source-id", out var source) || source.Any(c => c is < ' ' or > '~') ? null : new Input(values["--module"], values["--resource"], values["--prototype-root"], source);
        }
    }

    private readonly record struct CliResult(int Exit, string Stdout, string Stderr);
    private sealed class Census
    {
        private readonly SortedDictionary<string, int> _reasons = new(StringComparer.Ordinal);
        public int Eligible { get; set; } public int Npc { get; set; } public int PlayerExcluded { get; set; } public int Direct { get; set; } public int PrototypeDefault { get; set; } public int Missing { get; set; } public int Invalid { get; set; } public int InspectedChains { get; set; }
        public void Add(string reason) => _reasons[reason] = _reasons.GetValueOrDefault(reason) + 1;
        public string RenderReasons() => _reasons.Count == 0 ? "none" : string.Join(",", _reasons.Select(pair => $"{pair.Key}:{pair.Value}"));
    }
    private enum ResolutionSource { Direct, PrototypeDefault }
    private enum WireKind { I32, OptI64, ArrI32, ArrI64, ArrU32, ArrU64, ArrScript, ArrQuest, OptString, OptObjectId, ArrObjectId }
    private readonly record struct ObjectId(short Kind, int? Aid, byte[] Payload)
    {
        public static ObjectId A(int aid) { var payload = new byte[16]; BinaryPrimitives.WriteInt32LittleEndian(payload, aid); return new ObjectId(1, aid, payload); }
        public bool Matches(ObjectId other) => Kind == other.Kind && Payload.AsSpan().SequenceEqual(other.Payload);
    }
    private readonly record struct EffectiveArt(int Art, ResolutionSource Source);
    private readonly record struct PhysicalFacing(int Group, bool FlipX);
    private sealed record ObjectRecord(int Type, ObjectId Prototype, int? DirectCurrentArt);
    private sealed record PrototypeRecord(ObjectId Id, int Type, int CurrentArt);
    private sealed record FacingMetadata(int Selector, int PhysicalGroup, bool FlipX, IReadOnlyList<ArtFrameMetadata> Frames);
    private sealed record ActionMetadata(int Action, int ResourceOrdinal, int PackedSelector, int FramesPerSecond, IReadOnlyList<FacingMetadata> Facings);
    private readonly record struct ActionEvidence(ActionMetadata Idle, ActionMetadata Walk);
    private sealed record Candidate(int Ordinal, ResolutionSource Source, int Art, ActionEvidence Evidence)
    {
        public static int Compare(Candidate left, Candidate right) { var order = left.Ordinal.CompareTo(right.Ordinal); return order != 0 ? order : right.Source.CompareTo(left.Source); }
        public string SafeMetadata() => $"art=0x{unchecked((uint)Art):x8};idle={ActionMetadata(Evidence.Idle)};walk={ActionMetadata(Evidence.Walk)}";
        private static string ActionMetadata(ActionMetadata action) => $"selector=0x{unchecked((uint)action.PackedSelector):x8},resource_ordinal={action.ResourceOrdinal},fps={action.FramesPerSecond},facings=[{string.Join("|", action.Facings.Select(facing => $"{facing.Selector}>{facing.PhysicalGroup}:{(facing.FlipX ? "x" : "n")}:frames={string.Join("/", facing.Frames.Select((frame, index) => $"{index}:{frame.Width}x{frame.Height}@{frame.HotX},{frame.HotY}+{frame.OffsetX},{frame.OffsetY}"))}"))}]";
    }
    private sealed record InputFingerprint(long Length, string Sha256);
    private sealed class ProbeException(string stage, string reason) : Exception { public string Stage { get; } = stage; public string Reason { get; } = reason; }
    private ref struct Cursor
    {
        private readonly ReadOnlySpan<byte> _bytes; private int _offset;
        public Cursor(ReadOnlySpan<byte> bytes) { _bytes = bytes; _offset = 0; }
        public bool AtEnd => _offset == _bytes.Length;
        public short I16() => BinaryPrimitives.ReadInt16LittleEndian(Bytes(2)); public int I32() => BinaryPrimitives.ReadInt32LittleEndian(Bytes(4)); public uint U32() => BinaryPrimitives.ReadUInt32LittleEndian(Bytes(4));
        public bool Present() { var value = Bytes(1)[0]; if (value is not 0 and not 1) throw new ProbeException("record", "optional-presence"); return value == 1; }
        public ObjectId ObjectId(bool allowBlocked) { var kind = I16(); Skip(6); var payload = Bytes(16).ToArray(); if (kind == -1 && allowBlocked) return new ObjectId(kind, null, payload); if (kind is < 0 or > 3) throw new ProbeException("record", "object-id"); return new ObjectId(kind, kind == 1 ? BinaryPrimitives.ReadInt32LittleEndian(payload) : null, payload); }
        public ReadOnlySpan<byte> Bytes(int count) { if (count < 0 || count > _bytes.Length - _offset) throw new ProbeException("record", "truncated"); var result = _bytes.Slice(_offset, count); _offset = checked(_offset + count); return result; }
        public void Skip(int count) => _ = Bytes(count);
    }
}
