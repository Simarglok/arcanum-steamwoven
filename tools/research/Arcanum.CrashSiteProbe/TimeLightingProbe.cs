using System.Buffers.Binary;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Arcanum.Data;
using Microsoft.Win32.SafeHandles;

namespace Arcanum.CrashSiteProbe;

internal static partial class Program
{
    // Independently authored bounded research, NOT a game clock/light runtime.
    // Schema specifications: the canonical contract (docs/world/time-lighting-contract.md).
    private static partial class TimeLightingProbe
    {
        private const int PayloadCap = 1_048_576;
        private const int SectorCap = 32;
        private const int SchemeCap = 32;
        private const int MesCap = 8_192;
        private const int EmitterCap = 4_096;
        private sealed class InspectionFailure(string code) : Exception(code);
        private static InspectionFailure Fail(string code) => new(code);
        public static string Diagnostic(Exception error) => error is InspectionFailure ? error.Message : "TIME_LIGHTING_INSPECTION_FAILED";
        private static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        private static void Admit(long count, long cap) { if (count < 0 || count > cap) throw Fail("BUDGET_EXCEEDED"); }
        private static void SelectSector(ref int selected)
        {
            var next = checked(selected + 1); Admit(next, SectorCap); selected = next;
        }
        private static void ReferenceScheme(HashSet<int> schemes, int id)
        {
            if (schemes.Contains(id)) return;
            Admit(checked(schemes.Count + 1), SchemeCap); schemes.Add(id);
        }
        private static int CountBytes(int count, int width, int cap) { Admit(count, cap); return checked(count * width); }
        private static DatReaderOptions Options() => new()
        {
            MaxArchiveBytes = 536_870_912, MaxIndexBytes = 67_108_864,
            MaxEntryCount = 131_072, MaxNameBytes = 4_096, MaxTotalNameBytes = 67_108_864,
            MaxStoredEntryBytes = PayloadCap, MaxDecodedEntryBytes = PayloadCap,
        };

        // File status ABI matches the existing macOS/Linux .NET8 Area input adapter.
        // Held read-only handle, path identity and complete streamed digest checked on publication.
        private sealed class Input : IDisposable
        {
            private readonly string path;
            private readonly Status initial;
            public FileStream Stream { get; }
            public string Sha256 { get; }
            public Input(string path, long cap)
            {
                if (!OperatingSystem.IsMacOS() && !OperatingSystem.IsLinux()) throw Fail("PATH_IDENTITY_PLATFORM_NOT_READY");
                this.path = path;
                Stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65_536, FileOptions.RandomAccess);
                try
                {
                    if (FStat(Stream.SafeFileHandle, out initial) != 0) throw Fail("INPUT_STAT");
                    Admit(initial.Size, cap); Sha256 = Digest(); Check();
                }
                catch { Stream.Dispose(); throw; }
            }
            private string Digest() { Stream.Position = 0; var h = Convert.ToHexString(SHA256.HashData(Stream)).ToLowerInvariant(); Stream.Position = 0; return h; }
            public void Check()
            {
                if (FStat(Stream.SafeFileHandle, out var handle) != 0 || Stat(path, out var current) != 0 ||
                    handle != initial || current != initial || Digest() != Sha256) throw Fail("INPUT_CHANGED");
            }
            public byte[] Bytes() { Check(); Admit(Stream.Length, PayloadCap); var result = new byte[checked((int)Stream.Length)]; Stream.ReadExactly(result); Check(); return result; }
            public void Dispose() => Stream.Dispose();
            public object Receipt(string role) => new { role, bytes = initial.Size, sha256 = Sha256, device = initial.Device, inode = initial.Inode, modified = initial.Modified, modified_nanos = initial.ModifiedNanos, changed = initial.Changed, changed_nanos = initial.ChangedNanos };
        }
        [StructLayout(LayoutKind.Explicit, Size = 120)]
        private struct Status : IEquatable<Status>
        {
            [FieldOffset(16)] public long Size;
            [FieldOffset(40)] public long Modified;
            [FieldOffset(48)] public long ModifiedNanos;
            [FieldOffset(56)] public long Changed;
            [FieldOffset(64)] public long ChangedNanos;
            [FieldOffset(88)] public long Device;
            [FieldOffset(104)] public long Inode;
            public readonly bool Equals(Status s) => Size == s.Size && Modified == s.Modified && ModifiedNanos == s.ModifiedNanos && Changed == s.Changed && ChangedNanos == s.ChangedNanos && Device == s.Device && Inode == s.Inode;
            public override readonly bool Equals(object? other) => other is Status s && Equals(s);
            public override readonly int GetHashCode() => HashCode.Combine(Size, Modified, ModifiedNanos, Changed, ChangedNanos, Device, Inode);
            public static bool operator ==(Status a, Status b) => a.Equals(b);
            public static bool operator !=(Status a, Status b) => !a.Equals(b);
        }
        [DllImport("System.Native", EntryPoint = "SystemNative_FStat")] private static extern int FStat(SafeFileHandle handle, out Status status);
        [DllImport("System.Native", EntryPoint = "SystemNative_Stat")] private static extern int Stat([MarshalAs(UnmanagedType.LPUTF8Str)] string path, out Status status);

        private static string Name(DatEntry e)
        {
            var raw = e.RawNameBytes;
            if (raw.Count == 0 || raw[^1] != 0 || raw.Take(raw.Count - 1).Any(b => b == 0 || b > 127)) throw Fail("ENTRY_NAME");
            return Encoding.ASCII.GetString(raw.ToArray()[..^1]).Replace('\\', '/');
        }
        private static DatEntry Unique(DatArchive archive, string name)
        {
            var found = archive.Entries.Where(e => Name(e).Equals(name, StringComparison.OrdinalIgnoreCase)).Take(2).ToArray();
            if (found.Length != 1) throw Fail(found.Length == 0 ? "RESOURCE_MISSING" : "RESOURCE_AMBIGUOUS");
            return found[0];
        }
        private static string Token(string value)
        {
            value = value.Trim();
            if (value.Length is 0 or > 255 || value.Contains("..", StringComparison.Ordinal) || value.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is ' ' or '_' or '-'))) throw Fail("UNSAFE_RESOURCE_TOKEN");
            return value;
        }
        private static Dictionary<int, string> Mes(byte[] bytes)
        {
            Admit(bytes.Length, PayloadCap);
            // Values are original text only in transient memory, never diagnostic output.
            var text = Encoding.Latin1.GetString(bytes); var result = new Dictionary<int, string>(); var cursor = 0;
            while (cursor < text.Length)
            {
                if (char.IsWhiteSpace(text[cursor])) { cursor++; continue; }
                if (text[cursor] == ';' || text.AsSpan(cursor).StartsWith("//")) { var end = text.IndexOf('\n', cursor); cursor = end < 0 ? text.Length : end + 1; continue; }
                if (text[cursor] != '{') throw Fail("MES_GRAMMAR");
                var keyEnd = text.IndexOf('}', cursor + 1);
                if (keyEnd < 0 || !int.TryParse(text.AsSpan(cursor + 1, keyEnd - cursor - 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out var key)) throw Fail("MES_KEY");
                cursor = keyEnd + 1; while (cursor < text.Length && char.IsWhiteSpace(text[cursor])) cursor++;
                if (cursor == text.Length || text[cursor] != '{') throw Fail("MES_VALUE");
                var valueEnd = text.IndexOf('}', cursor + 1); if (valueEnd < 0) throw Fail("MES_TRUNCATED");
                Admit(checked(result.Count + 1), MesCap);
                if (!result.TryAdd(key, text[(cursor + 1)..valueEnd])) throw Fail("MES_DUPLICATE");
                cursor = valueEnd + 1;
                // Selected hourly resources include bare trailing hour annotations.
                // Admit only the bounded 1..12 AM/PM annotation, not arbitrary junk.
                var annotationStart = cursor; while (annotationStart < text.Length && text[annotationStart] is ' ' or '\t') annotationStart++;
                if (annotationStart < text.Length && char.IsAsciiDigit(text[annotationStart]))
                {
                    var lineEnd = text.IndexOfAny(['\r', '\n'], annotationStart); if (lineEnd < 0) lineEnd = text.Length;
                    var suffix = text.AsSpan(annotationStart, lineEnd - annotationStart).Trim();
                    if (suffix.Length is >= 3 and <= 4 && (suffix.EndsWith("AM", StringComparison.Ordinal) || suffix.EndsWith("PM", StringComparison.Ordinal)) &&
                        int.TryParse(suffix[..^2], NumberStyles.None, CultureInfo.InvariantCulture, out var annotation) && annotation is >= 1 and <= 12)
                        cursor = lineEnd;
                }
            }
            if (result.Count == 0) throw Fail("MES_EMPTY");
            return result;
        }
        private static int Required(Dictionary<int, string> mes, int key) => mes.TryGetValue(key, out var text) && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : throw Fail("REQUIRED_NUMERIC_KEY");
        private static int MapDefault(byte[] bytes)
        {
            Admit(bytes.Length, PayloadCap); int? value = null;
            foreach (var line in Encoding.Latin1.GetString(bytes).Split('\n'))
            {
                var parts = line.Split(':', 2);
                if (parts.Length != 2 || !parts[0].Trim().Equals("LightScheme", StringComparison.OrdinalIgnoreCase)) continue;
                if (value.HasValue || !int.TryParse(parts[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) || n <= 0) throw Fail("MAP_DEFAULT_MALFORMED_OR_DUPLICATE");
                value = n;
            }
            return value ?? throw Fail("MAP_DEFAULT_MISSING_NO_GUESSED_FALLBACK");
        }
        private static int Resolve(int map, int? sector)
        {
            if (map <= 0 || !sector.HasValue || sector < 0) throw Fail("SCHEME_NOT_READY");
            return sector == 0 ? map : sector.Value;
        }
        private sealed class Scheme
        {
            private readonly int[][] channels;
            public Scheme(Dictionary<int, string> values)
            {
                // Strict research proposal: fresh table, all keys present, bounded interior gaps.
                // Each gap uses its actual bracketing anchors. Not the source's mutable running-colour loop.
                channels = new int[24][];
                for (var h = 0; h < 24; h++)
                {
                    if (!values.TryGetValue(h, out var row)) throw Fail("SCHEME_MISSING_HOUR");
                    if (row.Trim().Length == 0) continue;
                    var parts = row.Split(','); if (parts.Length != 6) throw Fail("SCHEME_COMPONENTS");
                    var components = new int[6];
                    for (var c = 0; c < 6; c++) if (!int.TryParse(parts[c], NumberStyles.Integer, CultureInfo.InvariantCulture, out components[c]) || components[c] is < 0 or > 255) throw Fail("SCHEME_CHANNEL_RANGE");
                    channels[h] = components;
                }
                if (channels[0] is null || channels[23] is null) throw Fail("SCHEME_EDGE_GAP_UNSUPPORTED");
                var anchors = Enumerable.Range(0, 24).Where(h => channels[h] is not null).ToArray();
                foreach (var (left, right) in anchors.Zip(anchors.Skip(1)))
                    for (var h = left + 1; h < right; h++)
                        channels[h] = Enumerable.Range(0, 6).Select(c => channels[left][c] + (h - left) * ((channels[right][c] - channels[left][c]) / (right - left))).ToArray();
            }
            public IReadOnlyList<int> At(int hour)
            { if (hour is < 0 or > 23) throw Fail("HOUR_RANGE"); return Array.AsReadOnly((int[])channels[hour].Clone()); }
            public bool DistinctChannels => channels.Any(v => !v[..3].SequenceEqual(v[3..]));
        }
        private readonly record struct Clock(long Milliseconds, long Revision)
        {
            public Clock Advance(long duration, bool paused)
            {
                if (Milliseconds < 0 || Revision < 0 || duration < 0) throw Fail("CLOCK_RANGE");
                return paused || duration == 0 ? this : new Clock(checked(Milliseconds + duration), checked(Revision + 1));
            }
            public (long Day, int Hour, bool IsDay) Query()
            { if (Milliseconds < 0) throw Fail("CLOCK_RANGE"); var hour = (int)(Milliseconds / 3_600_000 % 24); return (Milliseconds / 86_400_000, hour, hour is >= 6 and < 18); }
        }
        private static (uint Days, uint Milliseconds) WireClock(ReadOnlySpan<byte> bytes)
        { if (bytes.Length != 8) throw Fail("CLOCK_WIRE_LENGTH"); return (BinaryPrimitives.ReadUInt32LittleEndian(bytes), BinaryPrimitives.ReadUInt32LittleEndian(bytes[4..])); }
        private static (uint Days, uint Milliseconds) SourceCarry(uint days, uint ms, uint duration)
        {
            // Checked worksheet of the observed strict > boundary; never emulate unsigned wrap.
            var sum = checked(ms + duration); var carries = sum > 86_400_000 ? sum / 86_400_000 : 0;
            return (checked(days + carries), carries == 0 ? sum : sum % 86_400_000);
        }
        private static (int Day, int Month, long Year, int CurrentDay, int CurrentMonth) SourceCalendar(uint days, int year) =>
            ((int)(days % 30 + 1), (int)(days / 30 % 12 + 1), checked((long)year + days / 360), (int)(days / 30 % 12 + 1), (int)(days % 30 + 1));
        private static (int Real, int Game, int Animation) SourceElapsed(int delta, bool gated, bool turnBased)
        { if (delta < 0) throw Fail("ELAPSED_RANGE"); if (delta < 5) return (0, 0, 0); var real = Math.Min(delta, 250); return (real, gated || turnBased ? 0 : real * 8, gated ? 0 : real * 8); }

        private static long RelativeDeadline(long now, long delay)
        { if (now < 0 || delay < 0) throw Fail("DEADLINE_RANGE"); return checked(now + (delay == 0 ? 1 : delay)); }
        private static bool AlreadyDue(long now, long deadline) => now >= deadline;
        private static int StationaryCount(ReadOnlySpan<byte> bytes)
        { if (bytes.Length < 4) throw Fail("SECTOR_COUNT_TRUNCATED"); var n = BinaryPrimitives.ReadInt32LittleEndian(bytes); var size = CountBytes(n, 48, 1024); if (size > bytes.Length - 4) throw Fail("LIGHTS_TRUNCATED"); return n; }
        private static (int? Town, int? Scheme) Metadata(ReadOnlySpan<byte> bytes, SectorFraming framing)
        {
            if (framing.MetadataAndSoundBytes != 24) return (null, null);
            var start = framing.MetadataAndSoundStart;
            if (start < 0 || start > bytes.Length - 24) throw Fail("METADATA_TRUNCATED");
            return (BinaryPrimitives.ReadInt32LittleEndian(bytes[start..]), BinaryPrimitives.ReadInt32LittleEndian(bytes[(start + 8)..]));
        }

        private sealed record Light(long OwnerWord, long Location, int OffsetX, int OffsetY, uint Flags, uint Art, byte R, byte B, byte G, byte Pad, uint TintWord, uint PaletteWord, uint PaddingWord);
        private static Light ReadLight(ReadOnlySpan<byte> bytes)
        {
            if (bytes.Length != 48) throw Fail("LIGHT_WIRE_LENGTH");
            return new(BinaryPrimitives.ReadInt64LittleEndian(bytes), BinaryPrimitives.ReadInt64LittleEndian(bytes[8..]),
                BinaryPrimitives.ReadInt32LittleEndian(bytes[16..]), BinaryPrimitives.ReadInt32LittleEndian(bytes[20..]),
                BinaryPrimitives.ReadUInt32LittleEndian(bytes[24..]), BinaryPrimitives.ReadUInt32LittleEndian(bytes[28..]),
                bytes[32], bytes[33], bytes[34], bytes[35], BinaryPrimitives.ReadUInt32LittleEndian(bytes[36..]), BinaryPrimitives.ReadUInt32LittleEndian(bytes[40..]), BinaryPrimitives.ReadUInt32LittleEndian(bytes[44..]));
        }
        private static string Contribution(uint flags) => (flags & 1) != 0 ? "OFF" : (flags & 2) != 0 ? "SUBTRACT" : "ADD";
        private static string Tint(uint flags) => (flags & 8) != 0 ? "INDOOR" : (flags & 16) != 0 ? "OUTDOOR" : "CUSTOM";
        private static uint UnknownFlags(uint flags) => flags & ~0xf000009fu; // named-but-unknown 0x20/0x08000000 stay unknown.
        private static (bool Insert, bool Off, bool Changed) ObjectGate(bool refresh, int type, uint objectFlags, uint sceneryFlags, int tileClass, int hour)
        {
            if (hour is < 0 or > 23) throw Fail("HOUR_RANGE");
            var off = (objectFlags & 2) != 0;
            // Inventory refusal is load/fold only. Hour-refresh itself lacks that guard.
            if (!refresh && (objectFlags & 0x1000) != 0) return (false, off, false);
            var applicable = type == 3 && (sceneryFlags & 4) != 0 && (!refresh || (sceneryFlags & 0x400) == 0);
            var targetOff = applicable ? tileClass != 0 && hour is >= 6 and < 18 : off;
            return (refresh ? applicable && targetOff != off : !(applicable && targetOff), targetOff, targetOff != off);
        }
        private static string Layer(int artClass, int tileClass, bool itemGround, bool enabled) => !enabled ? "NONE" : artClass switch
        {
            0 or 11 => tileClass == 0 ? "INDOOR" : "OUTDOOR",
            1 or 2 or 3 or 4 or 7 or 10 or 12 or 13 => "OUTDOOR",
            6 when itemGround => "OUTDOOR", _ => "NONE",
        };
        private static (int Left, int Top, int Right, int Bottom) Footprint(int x, int y, int width, int height, int hotX, int hotY)
        { if (width <= 0 || height <= 0) throw Fail("FOOTPRINT_RANGE"); var left = checked(x - hotX); var top = checked(y - hotY); return (left, top, checked(left + width), checked(top + height)); }
        private sealed record Emitter(string Key, int Sector, int X, int Width);
        private static IReadOnlyList<Emitter> Neighbours(IEnumerable<Emitter> source, IReadOnlySet<int> required, IReadOnlySet<int> present)
        {
            Admit(required.Count, SectorCap); if (!required.IsSubsetOf(present)) throw Fail("NEIGHBOUR_NOT_READY");
            var result = new SortedDictionary<string, Emitter>(StringComparer.Ordinal); var count = 0;
            foreach (var value in source)
            {
                Admit(++count, EmitterCap);
                if (!required.Contains(value.Sector)) throw Fail("EMITTER_COVERAGE");
                if (result.TryGetValue(value.Key, out var prior) && prior != value) throw Fail("EMITTER_KEY_CONFLICT");
                result[value.Key] = value;
            }
            return Array.AsReadOnly(result.Values.ToArray());
        }
        private static int Compose(int baseline, int mask, int tint, bool dark, bool transparent)
        {
            if (baseline is < 0 or > 255 || mask is < 0 or > 255 || tint is < 0 or > 255) throw Fail("COLOR_RANGE");
            var value = transparent ? 0 : mask * tint / 255; // test-only 8-bit project arithmetic; NOT final TIG/BPP parity.
            return Math.Clamp(baseline + (dark ? -value : value), 0, 255);
        }
        private sealed record Overlay(string Presence, IReadOnlyDictionary<int, uint> Values);
        private static Overlay OverlayField(byte[] bytes, IReadOnlyList<ArraySpan> arrays, int field, bool overridden)
        {
            if (!overridden) return new("ABSENT", new System.Collections.ObjectModel.ReadOnlyDictionary<int, uint>(new Dictionary<int, uint>()));
            var matches = arrays.Where(a => a.Field == field).Take(2).ToArray();
            if (matches.Length == 0) return new("NULL", new System.Collections.ObjectModel.ReadOnlyDictionary<int, uint>(new Dictionary<int, uint>()));
            if (matches.Length != 1) throw Fail("OVERLAY_AMBIGUOUS");
            var a = matches[0]; if (a.Size != 4 || a.Start < 0 || a.End > bytes.Length) throw Fail("OVERLAY_SCHEMA");
            var result = new SortedDictionary<int, uint>(); var rank = 0;
            for (var word = 0; word < a.Words; word++)
            {
                var bits = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(a.WordStart + 4 + word * 4));
                for (var bit = 0; bit < 32; bit++) if ((bits & (1u << bit)) != 0)
                {
                    var index = checked(word * 32 + bit); if (index >= 4) throw Fail("OVERLAY_INDEX_UNSUPPORTED");
                    if (rank >= a.Count) throw Fail("OVERLAY_POPULATION");
                    result.Add(index, BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(a.Start + 13 + rank++ * 4)));
                }
            }
            if (rank != a.Count) throw Fail("OVERLAY_POPULATION");
            return new(result.Count == 0 ? "EMPTY" : "VALUES", new System.Collections.ObjectModel.ReadOnlyDictionary<int, uint>(result));
        }
        private static Overlay EffectiveOverlay(Overlay direct, Overlay prototype) => direct.Presence == "ABSENT" ? prototype : direct;

        public static int Run(string[] args, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var options = new Dictionary<string, string>(StringComparer.Ordinal);
            if (args.Length % 2 != 0) throw Fail("ARGUMENTS");
            for (var i = 0; i < args.Length; i += 2) if (!options.TryAdd(args[i], args[i + 1])) throw Fail("DUPLICATE_ARGUMENT");
            var allowed = new[] { "--module", "--prototype-root", "--mobile", "--mobile-selection" };
            if (options.Keys.Any(k => !allowed.Contains(k)) || !options.ContainsKey("--module") || !options.ContainsKey("--prototype-root") || options.ContainsKey("--mobile") != options.ContainsKey("--mobile-selection")) throw Fail("ARGUMENTS");
            using var moduleInput = new Input(options["--module"], 536_870_912);
            if (moduleInput.Sha256 != ExpectedModuleSha256 || moduleInput.Stream.Length != ExpectedModuleBytes) throw Fail("MODULE_IDENTITY");
            using var module = DatArchive.Open(moduleInput.Stream, Options(), leaveOpen: true);
            var reads = new List<object>(); var rows = new List<object>(); var sectors = new List<object>();
            var pins = new List<Input>(); var prototypes = new Dictionary<int, (PrototypeInfo Info, byte[] Bytes, Input Input)>();
            var schemes = new HashSet<int>(); var lightsRetained = 0; var selected = 0; var stage = "STARTUP";
            byte[] Read(DatEntry e)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var b = module.ReadEntry(e); reads.Add(new { ordinal = e.Ordinal, flags = e.Flags, offset = e.Offset, stored = e.StoredSize, decoded = b.Length, name_sha256 = Hash(e.RawNameBytes.ToArray()), sha256 = Hash(b) }); return b;
            }
            byte[] Resource(string name) => Read(Unique(module, name));
            (PrototypeInfo Info, byte[] Bytes) Prototype(InstanceInfo instance)
            {
                var id = instance.PrototypeId.AidValue();
                if (prototypes.TryGetValue(id, out var cached)) { if (cached.Info.Type != instance.Type) throw Fail("PROTOTYPE_TYPE"); return (cached.Info, cached.Bytes); }
                Admit(prototypes.Count + 1, 128);
                var paths = Directory.EnumerateFiles(options["--prototype-root"], $"{id:D6} - *.pro", SearchOption.TopDirectoryOnly).Take(2).ToArray();
                if (paths.Length != 1) throw Fail("PROTOTYPE_MISSING_OR_AMBIGUOUS");
                var input = new Input(paths[0], PayloadCap); pins.Add(input); var bytes = input.Bytes();
                stage = "PROTOTYPE_PARSE"; var info = ObjectWire.ParsePrototype(bytes);
                if (info.Id.AidValue() != id || info.Type != instance.Type) throw Fail("PROTOTYPE_IDENTITY");
                prototypes.Add(id, (info, bytes, input)); return (info, bytes);
            }
            void ObjectRow(byte[] bytes, InstanceInfo instance, string source, long sector, int index, long offset, int length)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Admit(++lightsRetained, EmitterCap); // descriptions charged even if no effective emitter.
                var p = Prototype(instance); var effective = EffectiveNumeric(instance, p.Info);
                var overlay = EffectiveOverlay(OverlayField(bytes, instance.Arrays, 16, instance.OverlayOverrides.Contains(16)), OverlayField(p.Bytes, p.Info.Arrays, 16, true));
                rows.Add(new { source, sector, index, offset, length, type = instance.Type, prototype = p.Info.Id.AidValue(), record_sha256 = Hash(bytes.AsSpan(0, length)),
                    primary_direct = instance.NumericFields.TryGetValue(13, out var d) ? (int?)d : null,
                    primary_effective = effective[13], primary_inherited = !instance.NumericFields.ContainsKey(13),
                    flags_direct = instance.NumericFields.TryGetValue(12, out var f) ? (int?)f : null, flags_effective = effective[12],
                    overlay_direct = OverlayField(bytes, instance.Arrays, 16, instance.OverlayOverrides.Contains(16)).Presence,
                    overlay_effective = overlay.Presence, overlay_elements = overlay.Values.Count, overlay_noninvalid = overlay.Values.Count(v => v.Value != uint.MaxValue),
                    // Flags/color arrays are projected too; different create/update provenance remains source-only.
                    overlay_flags = EffectiveOverlay(OverlayField(bytes, instance.Arrays, 15, instance.OverlayOverrides.Contains(15)), OverlayField(p.Bytes, p.Info.Arrays, 15, true)).Presence,
                    overlay_colors = EffectiveOverlay(OverlayField(bytes, instance.Arrays, 17, instance.OverlayOverrides.Contains(17)), OverlayField(p.Bytes, p.Info.Arrays, 17, true)).Presence,
                    readiness = unchecked((uint)effective[13]) == uint.MaxValue && overlay.Values.All(v => v.Value == uint.MaxValue) ? "NO_NONINVALID_SELECTED_DESCRIPTION" : "NOT_READY_ART_MASK_STATE_COORDINATE_BINDING" });
            }
            try
            {
                var startup = Mes(Resource("rules/gameinit.mes")); var year = Required(startup, 4); var hour = Required(startup, 5); if (hour is < 0 or > 23) throw Fail("START_HOUR");
                var maps = Mes(Resource("rules/maplist.mes")); var catalogue = Mes(Resource("rules/lighting schemes.mes"));
                var defaults = new List<object>();
                foreach (var map in new[] { 5000, 5078 })
                {
                    if (!maps.TryGetValue(map, out var record)) throw Fail("MAP_CATALOGUE");
                    var prefix = "maps/" + Token(record.Split(',')[0]) + "/";
                    var mapDefault = MapDefault(Resource(prefix + "mapinfo.txt")); ReferenceScheme(schemes, mapDefault);
                    defaults.Add(new { catalogue = map, runtime = map - 4999, map_default = mapDefault, scope = map == 5000 ? "SELECTED_EXTERIOR" : "SUPPLEMENTARY_CAVE_EDITOR_SOURCE_ONLY" });
                    var points = map == 5000 ? Enumerable.Range(1288, 5).SelectMany(y => Enumerable.Range(1450, 5).Select(x => (X: x, Y: y))) : new[] { (X: 32, Y: 31) };
                    foreach (var point in points)
                    {
                        var id = checked(point.X + point.Y * 67_108_864L); var name = prefix + id.ToString(CultureInfo.InvariantCulture) + ".sec";
                        var found = module.Entries.Where(e => Name(e).Equals(name, StringComparison.OrdinalIgnoreCase)).Take(2).ToArray();
                        if (found.Length > 1) throw Fail("SECTOR_AMBIGUOUS");
                        if (found.Length == 0) { sectors.Add(new { catalogue = map, id, x = point.X, y = point.Y, present = false }); continue; }
                        stage = "SECTOR_FRAMING"; SelectSector(ref selected); var entry = found[0]; var bytes = Read(entry); _ = StationaryCount(bytes); var slice = SectorSlice.Parse(bytes, false); var framing = SectorFraming.Parse(bytes, slice.LightCount);
                        var (town, scheme) = Metadata(bytes, framing);
                        var resolved = Resolve(mapDefault, scheme); ReferenceScheme(schemes, resolved);
                        var lightRows = new List<object>();
                        for (var i = 0; i < slice.LightCount; i++)
                        {
                            Admit(++lightsRetained, EmitterCap); var offset = checked(4 + i * 48); var wire = bytes.AsSpan(offset, 48); var light = ReadLight(wire);
                            lightRows.Add(new { index = i, offset, sha256 = Hash(wire), owner_word = light.OwnerWord, location_word = light.Location, offset_x = light.OffsetX, offset_y = light.OffsetY, flags = light.Flags, art = light.Art,
                                contribution = Contribution(light.Flags), tint_source = Tint(light.Flags), unknown_flags = UnknownFlags(light.Flags),
                                padding_nonzero = light.Pad != 0 || light.PaddingWord != 0, palette_nonzero = light.PaletteWord != 0,
                                projection_sha256 = Hash(Encoding.ASCII.GetBytes(FormattableString.Invariant($"{light.OwnerWord},{light.Location},{light.OffsetX},{light.OffsetY},{light.Flags},{light.Art},{light.R},{light.B},{light.G},{light.Pad},{light.TintWord},{light.PaletteWord},{light.PaddingWord}"))),
                                readiness = "NOT_READY_STATIONARY_COORDINATE_TODO_ART_MASK" });
                        }
                        sectors.Add(new { catalogue = map, id, x = point.X, y = point.Y, present = true, ordinal = entry.Ordinal, town, scheme, resolved, metadata_offset = framing.MetadataAndSoundStart, count = slice.LightCount, lights = lightRows, object_count = slice.TrailingInt32, tile_classes = slice.ArtTypeCounts });
                        if (map != 5000 || point.X is < 1451 or > 1453 || point.Y is < 1289 or > 1291) continue;
                        var window = bytes.AsSpan(framing.ObjectStart, framing.ObjectBytes).ToArray(); var cursor = 0;
                        for (var i = 0; i < slice.TrailingInt32; i++)
                        {
                            stage = "STATIC_PARSE"; var instance = ObjectWire.ParseNext(window.AsSpan(cursor), out var consumed);
                            // Array offsets are relative to this individual record, not the containing object window.
                            ObjectRow(window.AsSpan(cursor, consumed).ToArray(), instance, "EXTERIOR_STATIC", id, i, framing.ObjectStart + cursor, consumed); cursor = checked(cursor + consumed);
                        }
                        if (cursor != window.Length) throw Fail("OBJECT_WINDOW_EOF");
                    }
                }
                var schemeRows = new List<object>();
                foreach (var id in schemes.Order())
                {
                    stage = "SCHEME_PARSE"; if (!catalogue.TryGetValue(id, out var token)) throw Fail("SCHEME_CATALOGUE_MISSING");
                    var entry = Unique(module, "rules/" + Token(token) + ".mes"); var bytes = Read(entry); var values = Mes(bytes); var normalized = new Scheme(values);
                    for (var h = 0; h < 24; h++) _ = normalized.At(h);
                    schemeRows.Add(new { id, ordinal = entry.Ordinal, sha256 = Hash(bytes), explicit_hours = Enumerable.Range(0, 24).Count(h => values[h].Trim().Length != 0), empty_hours = Enumerable.Range(0, 24).Where(h => values[h].Trim().Length == 0).ToArray(), distinct_indoor_outdoor = normalized.DistinctChannels, query_hours = 24, status = "STRICT_RESEARCH_NORMALIZED_NOT_ORIGINAL_RUNTIME" });
                }
                object mobileStatus = new { readiness = "NOT_READY_AUTHENTIC_AGGREGATE_AND_CORE_OFFSET_ORACLE_REQUIRED" };
                if (options.TryGetValue("--mobile", out var mobilePath))
                {
                    using var mobile = new Input(mobilePath, 1_823_280);
                    if (mobile.Stream.Length != 1_823_280 || mobile.Sha256 != "a548f611ff5de9842bba1f700f6614c0b9651cf3ab640a23d19efc7626dde6a5") throw Fail("MOBILE_IDENTITY");
                    using var selection = new Input(options["--mobile-selection"], PayloadCap); var witness = JsonSerializer.Deserialize<MobileSelection>(selection.Bytes()) ?? throw Fail("MOBILE_SELECTION");
                    if (witness.Schema != "time-lighting-core-selection-v1" || witness.SourceSha256 != mobile.Sha256 || witness.Records.Length != 88) throw Fail("MOBILE_WITNESS_BINDING");
                    var keys = new HashSet<long>(); var end = 16L;
                    var mobileIndex = 0;
                    foreach (var r in witness.Records.OrderBy(v => v.Offset))
                    {
                        if (!keys.Add(r.Offset) || r.Offset < end || r.Length < 70 || r.Length > PayloadCap || r.Offset > mobile.Stream.Length - r.Length) throw Fail("MOBILE_WITNESS_RANGE");
                        var b = new byte[r.Length]; mobile.Stream.Position = r.Offset; mobile.Stream.ReadExactly(b); end = checked(r.Offset + r.Length);
                        if (Hash(b) != r.Sha256) throw Fail("MOBILE_RECORD_IDENTITY");
                        stage = "MOBILE_PARSE"; var instance = ObjectWire.ParseNext(b, out var consumed); if (consumed != b.Length || instance.Location is null) throw Fail("MOBILE_RECORD_EOF_LOCATION");
                        var x = unchecked((int)instance.Location.Value); var y = unchecked((int)(instance.Location.Value >> 32));
                        if (x / 64 is < 1451 or > 1453 || y / 64 is < 1289 or > 1291 || x < 0 || y < 0) throw Fail("MOBILE_NOT_CORE");
                        ObjectRow(b, instance, "AUTHENTIC_CORE_MOBILE", x / 64 + y / 64 * 67_108_864L, mobileIndex++, r.Offset, b.Length);
                    }
                    mobile.Check(); selection.Check(); mobileStatus = new { readiness = "AUTHENTIC_CORE_SELECTION_INSPECTED_NOT_AREAORIGINAL", selected_records = 88, input = mobile.Receipt("normal-game-mobile"), witness = selection.Receipt("core-offset-oracle") };
                }
                foreach (var pin in pins) pin.Check(); moduleInput.Check(); cancellationToken.ThrowIfCancellationRequested();
                // No output before all identities/checks pass. No table, text, pixels, binary or machine path serialization.
                Console.WriteLine(JsonSerializer.Serialize(new { schema = "time-lighting-research-v1", original_runtime = "NOT_VERIFIED", input = moduleInput.Receipt("module"), start_year = year, start_hour = hour,
                    maps = defaults, sectors, schemes = schemeRows, objects = rows, mobile = mobileStatus, prototypes = prototypes.Keys.Order().Select(id => new { id, sha256 = Hash(prototypes[id].Bytes), input = prototypes[id].Input.Receipt("prototype") }).ToArray(), reads,
                    integrity = "BEFORE_AFTER_HANDLE_PATH_SHA256_MATCH", production_clock = false, production_lighting = false }));
                return 0;
            }
            catch (Exception error) { throw Fail(stage + "_" + Diagnostic(error)); }
            finally { foreach (var input in pins) input.Dispose(); }
        }
        private sealed record MobileRecord(long Offset, int Length, string Sha256);
        private sealed record MobileSelection(string Schema, string SourceSha256, MobileRecord[] Records);
    }
}
