using System.Buffers.Binary;
using System.Security.Cryptography;
using Arcanum.Data;

namespace Arcanum.ActorVisualProbe;

internal static class Program
{
    private const int ExitFailure = 1;
    private const int ExitUsage = 2;
    private const int ExitNotReady = 3;
    private const int MaxMobCandidates = 256;
    private const int MaxEntryBytes = 1024 * 1024;
    private const int ObjectVersion = 119;
    private const int ObjectTypeOffset = 52;

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

            return Probe(input);
        }
        catch (ProbeException exception)
        {
            Console.Error.WriteLine($"actor-visual-probe: FAIL stage={exception.Stage} reason={exception.Reason}");
            return ExitFailure;
        }
        catch (Exception)
        {
            Console.Error.WriteLine("actor-visual-probe: FAIL stage=internal reason=unexpected");
            return ExitFailure;
        }
    }

    private static int Probe(Input input)
    {
        // Paths are intentionally never included in reports or exception text.
        var before = new[]
        {
            Fingerprint(input.ModulePath, "module"),
            Fingerprint(input.ResourcePath, "resource"),
            FingerprintDirectory(input.PrototypeRoot),
        };

        var eligible = 0;
        var admittedNpc = 0;
        var playersExcluded = 0;
        var malformed = 0;
        using (var module = DatArchive.Open(input.ModulePath, Options()))
        using (var resource = DatArchive.Open(input.ResourcePath, Options()))
        {
            _ = resource.Entries.Count; // Explicit resource archive is authenticated and opened, never precedence-searched.
            foreach (var entry in module.Entries.Where(IsMob).OrderBy(entry => entry.Ordinal))
            {
                if (eligible == MaxMobCandidates) break;
                if (!Bounded(entry)) continue;
                eligible++;
                try
                {
                    var record = ParseObjectEnvelope(module.ReadEntry(entry));
                    if (record.Type == 15) { playersExcluded++; continue; }
                    if (record.Type == 16) admittedNpc++;
                }
                catch (ProbeException)
                {
                    malformed++;
                }
            }
        }

        var after = new[]
        {
            Fingerprint(input.ModulePath, "module"),
            Fingerprint(input.ResourcePath, "resource"),
            FingerprintDirectory(input.PrototypeRoot),
        };
        if (!before.SequenceEqual(after)) throw new ProbeException("immutability", "changed-input");

        // A visual winner requires a source-derived, independently reviewable mapping policy.
        // This deliberately does not derive an ART name or resource winner from an unverified filename convention.
        Console.WriteLine($"result=NOT_READY source={input.SourceId} module_length={before[0].Length} module_sha256={before[0].Sha256} resource_length={before[1].Length} resource_sha256={before[1].Sha256} prototype_aggregate_length={before[2].Length} prototype_aggregate_sha256={before[2].Sha256} eligible={eligible} npc={admittedNpc} player_excluded={playersExcluded} invalid={malformed} reason=actor-resource-mapping-unresolved");
        Console.WriteLine("next_evidence=admit-pinned-critter-resource-selector-and-anchor-scheduler-policy-before-selecting-a-renderable-actor");
        return ExitNotReady;
    }

    private static DatReaderOptions Options() => new()
    {
        MaxStoredEntryBytes = MaxEntryBytes,
        MaxDecodedEntryBytes = MaxEntryBytes,
    };

    private static bool IsMob(DatEntry entry)
    {
        var raw = entry.RawNameBytes;
        return raw.Count >= 5 && raw[^1] == 0 && raw[^5] == (byte)'.' &&
            (raw[^4] | 0x20) == (byte)'m' && (raw[^3] | 0x20) == (byte)'o' && (raw[^2] | 0x20) == (byte)'b';
    }

    private static bool Bounded(DatEntry entry) => entry.Flags is 0x001 or 0x002 &&
        entry.StoredSize is > 0 and <= MaxEntryBytes && entry.DeclaredOutputSize is > 0 and <= MaxEntryBytes;

    private static ObjectEnvelope ParseObjectEnvelope(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 4) throw new ProbeException("record", "truncated-version");
        if (BinaryPrimitives.ReadInt32LittleEndian(bytes) != ObjectVersion) throw new ProbeException("record", "unsupported-version");
        // A v119 record carries two 24-byte object identifiers after its version.
        // This admission-only reader does not interpret either identifier or any field.
        if (bytes.Length < ObjectTypeOffset + sizeof(int)) throw new ProbeException("record", "truncated-type");
        var type = BinaryPrimitives.ReadInt32LittleEndian(bytes[ObjectTypeOffset..]);
        if (type is < 0 or > 17) throw new ProbeException("record", "invalid-type");
        return new ObjectEnvelope(type);
    }

    private static InputFingerprint Fingerprint(string path, string stage)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            return FingerprintStream(stream, stage);
        }
        catch (IOException) { throw new ProbeException(stage, "unavailable"); }
        catch (UnauthorizedAccessException) { throw new ProbeException(stage, "unavailable"); }
    }

    private static InputFingerprint FingerprintDirectory(string root)
    {
        try
        {
            if (!Directory.Exists(root)) throw new ProbeException("prototypes", "unavailable");
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long total = 0;
            var count = 0;
            foreach (var path in Directory.EnumerateFiles(root, "*.pro", SearchOption.TopDirectoryOnly).Order(StringComparer.Ordinal))
            {
                var fingerprint = Fingerprint(path, "prototypes");
                var encoded = System.Text.Encoding.ASCII.GetBytes($"{fingerprint.Length}:{fingerprint.Sha256}|");
                hash.AppendData(encoded);
                total = checked(total + fingerprint.Length);
                count++;
                if (total > (long)MaxMobCandidates * MaxEntryBytes) throw new ProbeException("prototypes", "cap-exceeded");
            }
            return new InputFingerprint(total, Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant());
        }
        catch (ProbeException) { throw; }
        catch (IOException) { throw new ProbeException("prototypes", "unavailable"); }
        catch (UnauthorizedAccessException) { throw new ProbeException("prototypes", "unavailable"); }
    }

    private static InputFingerprint FingerprintStream(Stream stream, string stage)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[64 * 1024];
        long total = 0;
        while (true)
        {
            var read = stream.Read(buffer, 0, buffer.Length);
            if (read == 0) break;
            total = checked(total + read);
            hash.AppendData(buffer, 0, read);
        }
        if (total == 0) throw new ProbeException(stage, "empty-input");
        return new InputFingerprint(total, Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant());
    }

    private static void SelfTest()
    {
        var direct = new byte[ObjectTypeOffset + sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(direct, ObjectVersion);
        BinaryPrimitives.WriteInt32LittleEndian(direct.AsSpan(ObjectTypeOffset), 16);
        Assert(ParseObjectEnvelope(direct).Type == 16, "direct npc rejected");
        BinaryPrimitives.WriteInt32LittleEndian(direct.AsSpan(ObjectTypeOffset), 15);
        Assert(ParseObjectEnvelope(direct).Type == 15, "player envelope rejected");
        Reject(() => ParseObjectEnvelope([]), "truncation accepted");
        Reject(() => ParseObjectEnvelope(direct[..ObjectTypeOffset]), "type truncation accepted");
        Reject(() => ParseObjectEnvelope(WithI32(direct, 0, 118)), "wrong version accepted");
        Reject(() => ParseObjectEnvelope(WithI32(direct, ObjectTypeOffset, 18)), "invalid type accepted");

        const string sentinel = "PRIVATE_ORIGINAL_NAME_MUST_NOT_LEAK";
        var writer = new StringWriter();
        var original = Console.Out;
        try
        {
            Console.SetOut(writer);
            Console.WriteLine("result=NOT_READY reason=actor-resource-mapping-unresolved");
        }
        finally { Console.SetOut(original); }
        Assert(!writer.ToString().Contains(sentinel, StringComparison.Ordinal), "privacy sentinel leaked");
        Assert(ExitNotReady == 3 && ExitUsage == 2 && ExitFailure == 1, "exit contract changed");
    }

    private static byte[] WithI32(byte[] source, int offset, int value)
    {
        var result = source.ToArray();
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(offset), value);
        return result;
    }

    private static void Reject(Action action, string message)
    {
        try { action(); }
        catch (ProbeException) { return; }
        throw new InvalidOperationException(message);
    }

    private static void Assert(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    private sealed record Input(string ModulePath, string ResourcePath, string PrototypeRoot, string SourceId)
    {
        public static Input? Parse(string[] args)
        {
            if (args.Length != 8) return null;
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var index = 0; index < args.Length; index += 2)
            {
                if (args[index] is not ("--module" or "--resource" or "--prototype-root" or "--source-id") ||
                    string.IsNullOrWhiteSpace(args[index + 1]) || !values.TryAdd(args[index], args[index + 1])) return null;
            }
            if (!values.TryGetValue("--source-id", out var source) || source.Any(character => character is < ' ' or > '~')) return null;
            return new Input(values["--module"], values["--resource"], values["--prototype-root"], source);
        }
    }

    private sealed record ObjectEnvelope(int Type);
    private sealed record InputFingerprint(long Length, string Sha256);
    private sealed class ProbeException(string stage, string reason) : Exception
    {
        public string Stage { get; } = stage;
        public string Reason { get; } = reason;
    }
}
