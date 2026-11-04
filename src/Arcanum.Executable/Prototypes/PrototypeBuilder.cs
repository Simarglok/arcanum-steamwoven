using System.Security.Cryptography;
using System.Text;

namespace Arcanum.Executable.Prototypes;

/// <summary>
/// ARC-DEV-068 (slice E3 of the <see href="../../../docs/research/prototype-extraction.md">prototype extraction</see>): builds the
/// prototype files the way the player's own <c>Arcanum.exe</c> does, by interpreting its start-up functions and its
/// prototype build in <see cref="X86Machine"/>. Only the services the original takes from the system are Steamwoven code
/// (memory, the file layer over <see cref="IOriginalFileSource"/>, the clock, the GUID source); the object system, the
/// message-file reader, the group fill and the record writer are the executable's own. Nothing of the executable or of
/// the built files is kept here.
/// </summary>
public static class PrototypeBuilder
{
    public const string RetailSha256 = "9bd80511f8663a41a25a20a701959e394e0065e4ffc68a8dc69fe19c23678d52";

    /// <summary>
    /// The prototype module's check (0x468660, called from the module's init 0x4681B0 with a flag it sets when it built anything): for
    /// each of the 20 groups and each basic number from the group's first (table 0x5B37FC) to its end (0x5B384C, exclusive) it composes
    /// <c>proto\%06d - %s.pro</c> and, when the file layer says the file does not exist, calls the build 0x468890 with the number.
    /// </summary>
    internal const uint ProtoCheck = 0x468660, BuildFunction = 0x468890;
    internal const uint GroupStartTable = 0x5B37FC, GroupEndTable = 0x5B384C;
    internal const int GroupCount = 20;

    /// <summary>Data files without which a build is wrong; a missing one is a typed failure, any other missing file reads as absent, as in the original.</summary>
    internal static readonly HashSet<string> RequiredFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        "oemes\\oname.mes", "oemes\\faction.mes", "mes\\stat.mes", "rules\\magictech.mes", "mes\\spell.mes", "rules\\SpellEyeCandy.mes",
        "rules\\spelllist.mes", "rules\\effect.mes", "rules\\xp_critter.mes", "mes\\critter.mes", "rules\\npc.txt", "rules\\unique.txt",
        "rules\\monster.txt",
    };

    /// <summary>
    /// The original's start order up to the prototype check, read from the executable's own tables: the game modules
    /// 0 to 16 (<c>Description</c> … <c>Level</c>, record 0x2C bytes from 0x59A330, init at +4; the start function 0x4020F0
    /// runs them in this order with the start information) and then, inside the map module's init (0x40EA90), the first
    /// nine object modules (<c>Scroll</c> … <c>Obj</c>, record 0x34 bytes from 0x59F068, init at +4) that come before the
    /// prototype module <c>Proto</c> (0x4681B0), whose check (<see cref="ProtoCheck"/>) is then called as the module's init does.
    /// </summary>
    internal const uint GameModuleTable = 0x59A330, GameModuleStride = 0x2C;
    internal const int GameModulesBeforeMap = 17;
    internal const uint ObjectModuleTable = 0x59F068, ObjectModuleStride = 0x34;
    internal const int ObjectModulesBeforeProto = 9;

    public static PrototypeBuildResult Build(byte[] executable, IOriginalFileSource files, PrototypeBuildOptions? options = null, Action<int, int>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(executable);
        ArgumentNullException.ThrowIfNull(files);
        options ??= new PrototypeBuildOptions();
        var read = PeImage.Read(executable);
        if (read.Image is not { } image)
            return Fail(PrototypeBuildFailureKind.InvalidExecutable, $"{read.Failure!.Kind}: {read.Failure.Detail}");
        if (options.RequireVerifiedExecutable && !string.Equals(Convert.ToHexString(SHA256.HashData(executable)), RetailSha256, StringComparison.OrdinalIgnoreCase))
            return Fail(PrototypeBuildFailureKind.UnverifiedExecutable, "the executable is not the retail 1.0.7.4 Arcanum.exe");
        try
        {
            return new BuildSession(image, files, options).Run(progress);
        }
        catch (MachineFaultException exception)
        {
            // The host's own set-up reads or writes an address the image does not map (an image that is not the game's).
            return new([], new PrototypeBuildFailure(PrototypeBuildFailureKind.StartupFault, $"the start-up set-up: {exception.Fault}", exception.Fault), 0);
        }
    }

    private static PrototypeBuildResult Fail(PrototypeBuildFailureKind kind, string detail) => new([], new PrototypeBuildFailure(kind, detail), 0);

    private sealed class BuildSession
    {
        private const uint FileHandleTag = 0xF11E_0000;
        private readonly PeImage image;
        private readonly IOriginalFileSource source;
        private readonly PrototypeBuildOptions options;
        private readonly X86Machine machine;
        private readonly GuestHeap heap;
        private readonly Dictionary<uint, Stream> streams = [];
        private readonly Dictionary<uint, string> streamPaths = [];
        private readonly List<BuiltPrototype> built = [];
        private readonly List<string> missed = [];
        private readonly List<string> listed = [];
        private readonly List<string> readFiles = [];
        /// <summary>Files the executable wrote itself (caches, the prototypes), visible to its later reads and existence checks, never to the disk.</summary>
        private readonly Dictionary<string, byte[]> overlay = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> existingProtos;
        private const uint ViewChangeStub = 0xF000_0008, RedrawStub = 0xF000_000C;
        private int viewChangeCalls, redrawCalls;
        private int currentNumber = -1;
        private int builds;
        private int totalBuilds;
        private int nextStream = 1;
        private long writtenBytes;
        private string? hostFailure;
        private PrototypeBuildFailureKind hostFailureKind;

        public BuildSession(PeImage image, IOriginalFileSource source, PrototypeBuildOptions options)
        {
            this.image = image;
            this.source = source;
            this.options = options;
            existingProtos = new HashSet<string>((options.ExistingFiles ?? new HashSet<string>()).Select(name => "proto\\" + name), StringComparer.OrdinalIgnoreCase);
            // A research fill byte also moves the stack and the heap: pointers the code leaves in unused bytes then differ too.
            var shift = (uint)options.UninitializedFill;
            machine = new X86Machine(new MachineOptions { MaxStepsPerCall = options.MaxStepsPerCall, StackBase = 0x0010_0000 + (shift << 12) }) { Census = options.Census };
            heap = new GuestHeap(machine.Memory, options.MaxHeapBytes, options.UninitializedFill, GuestHeap.DefaultBase + (shift << 16));
        }

        public PrototypeBuildResult Run(Action<int, int>? progress)
        {
            image.LoadInto(machine.Memory);
            machine.BindImports(image);
            if (options.UninitializedFill != 0) PoisonUninitialized();
            InstallHooks();
            options.ConfigureMachine?.Invoke(machine);

            // The start information every module init receives (as 0x4020F0 builds it): the editor flag 0 (+0), the main window's
            // handle (+4, a fixed 1 here), and the two callbacks the game's window code supplies (view change at +8, redraw at +0xC). The callbacks
            // here are host stubs that only count the calls; no prototype reads what they would draw.
            var info = heap.Allocate(256);
            machine.Memory.WriteU32(info + 4, 1);
            machine.Memory.WriteU32(info + 8, ViewChangeStub);
            machine.Memory.WriteU32(info + 12, RedrawStub);
            machine.Hook(ViewChangeStub, "view change callback", m => { viewChangeCalls++; m.Eax = 0; return HookResult.Return(); });
            machine.Hook(RedrawStub, "redraw callback", m => { redrawCalls++; m.Eax = 0; return HookResult.Return(); });
            long steps = 0;
            InitializeArtLibrary();
            foreach (var (address, name) in StartupPlan())
            {
                var result = machine.Call(address, [info]);
                steps += result.Steps;
                if (!result.Completed) return Failed(result.Fault!, PrototypeBuildFailureKind.StartupFault, $"start-up function {name} (0x{address:X8})", null, steps);
            }

            // The prototype module's init: its check builds every prototype whose file the file layer does not report.
            for (var group = 0; group < GroupCount; group++)
            {
                var start = (int)machine.Memory.ReadU32(GroupStartTable + 4u * (uint)group);
                var end = (int)machine.Memory.ReadU32(GroupEndTable + 4u * (uint)group);
                totalBuilds += Math.Max(0, end - start);
            }
            totalBuilds -= existingProtos.Count;
            machine.Hook(BuildFunction, "prototype build", m =>
            {
                // The previous build must have written exactly one prototype file.
                if (built.Count != builds)
                    return Stop(PrototypeBuildFailureKind.BadOutput, $"the build of prototype {currentNumber} wrote {built.Count - builds + 1} proto files");
                currentNumber = (int)m.Argument(0);
                builds++;
                progress?.Invoke(builds, Math.Max(totalBuilds, builds));
                return HookResult.Continue();
            });
            var flag = heap.Allocate(16);
            var check = machine.Call(ProtoCheck, [flag]);
            steps += check.Steps;
            if (!check.Completed) return Failed(check.Fault!, PrototypeBuildFailureKind.BuildFault, builds == 0 ? "the prototype check" : $"the prototype check (the build of prototype {currentNumber})", builds == 0 ? null : currentNumber, steps);
            if (built.Count != builds)
                return new(built, new PrototypeBuildFailure(PrototypeBuildFailureKind.BadOutput, $"the build of prototype {currentNumber} wrote {built.Count - builds + 1} proto files", null, currentNumber), steps);
            return new(built, null, steps) { MissedFiles = missed, ReadFiles = readFiles, ListedPatterns = listed, CallbackCalls = (viewChangeCalls, redrawCalls) };
        }

        private void PoisonUninitialized()
        {
            var fill = options.UninitializedFill;
            var stack = new byte[machine.Options.StackSize];
            Array.Fill(stack, fill);
            machine.Memory.Load(machine.Options.StackBase, stack);
            var word = (uint)(fill * 0x0101_0101u);
            machine.Eax = machine.Ecx = machine.Edx = machine.Ebx = machine.Ebp = machine.Esi = machine.Edi = word;
        }

        /// <summary>
        /// The window library's art module (tig init table entry 11, 0x500590) as the start function 0x401100 gives it its data:
        /// colour depth 16 (struct +0x14), the game's art-name function 0x41DAE0 (+0x1C) and callback 0x41D510 (+0x20). The
        /// module sets a table of 512 cache entries and a handful of globals; its one floating-point step sizes the cache
        /// from the machine's memory, so the host writes a fixed 128 MiB limit and everything else as the code does. Frame
        /// counts of the art files (the last frame of an animation, the first frame of a critter) come from this cache.
        /// </summary>
        private void InitializeArtLibrary()
        {
            var mem = machine.Memory;
            var table = heap.Allocate(0x4D800);
            mem.WriteU32(0x60472C, 0x200);                 // cache entries
            mem.WriteU32(0x60471C, 0);                     // entries in use
            mem.WriteU32(0x604720, table);                 // the cache table
            mem.WriteU32(0x604724, 0x0400_0000);           // size figures derived from the memory size (not read by a prototype build)
            mem.WriteU32(0x604740, 0x0400_0000);
            mem.WriteU32(0x604730, CacheLimitBytes);       // the floating-point step: the cache's byte limit
            mem.WriteU32(0x604748, CacheLimitBytes);
            mem.WriteU32(0x60474C, ArtColourDepth);        // struct +0x14
            mem.WriteU32(0x604738, (ArtColourDepth + 7) / 8);
            mem.WriteU32(0x604710, ArtNameFunction);       // struct +0x1C
            mem.WriteU32(0x604750, ArtCallback);           // struct +0x20
            mem.WriteU32(0x60473C, 1);                     // initialised
            mem.WriteU32(0x604718, 0);                     // 0x51FB10: no 3-D device
        }

        private const uint ArtColourDepth = 16, ArtNameFunction = 0x41DAE0, ArtCallback = 0x41D510, CacheLimitBytes = 128u << 20;

        private List<(uint Address, string Name)> StartupPlan()
        {
            var plan = new List<(uint, string)>();
            AddModules(plan, GameModuleTable, GameModuleStride, GameModulesBeforeMap);
            AddModules(plan, ObjectModuleTable, ObjectModuleStride, ObjectModulesBeforeProto);
            return plan;
        }

        private void AddModules(List<(uint, string)> plan, uint table, uint stride, int count)
        {
            for (var i = 0; i < count; i++)
            {
                var record = table + stride * (uint)i;
                var init = machine.Memory.ReadU32(record + 4);
                if (init == 0) continue;
                var name = Encoding.Latin1.GetString(machine.Memory.ReadCString(machine.Memory.ReadU32(record), 64));
                plan.Add((init, name));
            }
        }

        private PrototypeBuildResult Failed(MachineFault fault, PrototypeBuildFailureKind stage, string where, int? number, long steps)
        {
            var kind = stage;
            var detail = $"{where}: {fault}";
            if (hostFailure is not null)
            {
                kind = hostFailureKind;
                detail = $"{where}: {hostFailure}";
            }
            return new(built, new PrototypeBuildFailure(kind, detail, fault, number), steps) { MissedFiles = missed, ReadFiles = readFiles, ListedPatterns = listed, CallbackCalls = (viewChangeCalls, redrawCalls) };
        }

        private HookResult Stop(PrototypeBuildFailureKind kind, string reason)
        {
            hostFailure = reason;
            hostFailureKind = kind;
            return HookResult.Stop(reason);
        }

        private void InstallHooks()
        {
            // Memory: the executable's C runtime heap is replaced by the guest heap.
            machine.Hook(0x58A716, "malloc", m => Return(m, heap.Allocate(m.Argument(0), zero: false)));
            machine.Hook(0x58A99E, "calloc", m => Return(m, (ulong)m.Argument(0) * m.Argument(1) is var total && total <= uint.MaxValue ? heap.Allocate((uint)total, zero: true) : 0));
            machine.Hook(0x58A072, "realloc", m => Return(m, heap.Reallocate(m.Argument(0), m.Argument(1))));
            machine.Hook(0x58A347, "free", m =>
            {
                if (!heap.Free(m.Argument(0))) return Stop(PrototypeBuildFailureKind.LimitExceeded, $"free of 0x{m.Argument(0):X8}, which is not a live block");
                return HookResult.Return();
            });

            // The file layer.
            machine.Hook(0x5303A0, "file open", OpenFile);
            machine.Hook(0x530330, "file close", CloseFile);
            machine.Hook(0x530540, "file gets", GetLine);
            machine.Hook(0x530640, "file read", ReadFile);
            machine.Hook(0x5306A0, "file write", WriteFile);
            machine.Hook(0x52FB40, "file length", m =>
            {
                var handle = m.Argument(0);
                if (!streams.TryGetValue(handle, out var stream)) return Stop(PrototypeBuildFailureKind.BadOutput, $"length of 0x{handle:X8}, which is not an open file");
                m.Eax = (uint)stream.Length;
                return HookResult.Return();
            });
            machine.Hook(0x52F760, "file list", ListFiles);
            machine.Hook(0x52FB80, "file exists", m =>
            {
                var path = PathOf(m, m.Argument(0));
                m.Eax = overlay.ContainsKey(path) || existingProtos.Contains(path) || FromSource(path) is not null ? 1u : 0u;
                return HookResult.Return();
            });

            // The display: the object modules ask the window library (0x51CEB0) for the main window's rectangle once, at start-up.
            // A fixed 800 x 600 window answers; nothing a prototype holds comes from it.
            machine.Hook(0x51CEB0, "window data", m =>
            {
                var data = m.Argument(1);
                uint[] words = [0, 0, 0, 0, 800, 600, 0, 0];
                for (var i = 0; i < words.Length; i++) m.Memory.WriteU32(data + 4u * (uint)i, words[i]);
                m.Eax = 0;
                return HookResult.Return();
            });

            // The display's colour conversion (a palette entry to the screen's pixel format, 0x52C560): the art loader converts
            // every palette once; no pixel is shown here and a prototype holds none of it.
            machine.Hook(0x52C560, "pixel format conversion", m =>
            {
                m.Eax = 0;
                return HookResult.Return();
            });

            // System services with a fixed answer: the clock and the GUID (the builds replace the GUID with a prototype ID).
            machine.Hook(0x58B846, "time", m =>
            {
                var target = m.Argument(0);
                if (target != 0) m.Memory.WriteU32(target, FixedTime);
                m.Eax = FixedTime;
                return HookResult.Return();
            });
            // Directory creation succeeds without touching the disk: the builder never writes outside its result.
            machine.HookImport("KERNEL32.DLL!CreateDirectoryA", m =>
            {
                m.Eax = 1;
                return HookResult.Return(8);
            });
            machine.HookImport("ole32.dll!CoCreateGuid", m =>
            {
                var guid = new byte[16];
                Array.Fill(guid, options.UninitializedFill);
                m.Memory.WriteBytes(m.Argument(0), guid);
                m.Eax = 0;
                return HookResult.Return(4);
            });
        }

        /// <summary>The clock the builds see (2000-01-01 00:00:00 UTC): the builds write no time into a prototype.</summary>
        private const uint FixedTime = 946_684_800;

        private static HookResult Return(X86Machine m, uint value)
        {
            m.Eax = value;
            return HookResult.Return();
        }

        private static string PathOf(X86Machine m, uint pointer) => Encoding.Latin1.GetString(m.Memory.ReadCString(pointer, 260));

        // --- file layer --------------------------------------------------------------------------------------------------

        private HookResult OpenFile(X86Machine m)
        {
            var path = PathOf(m, m.Argument(0));
            var mode = PathOf(m, m.Argument(1));
            Stream stream;
            if (mode.Contains('w') || mode.Contains('a') || mode.Contains('+'))
            {
                if (path.Length == 0 || path.Contains("..", StringComparison.Ordinal) || path[0] is '\\' or '/' or '.' || path.Contains(':'))
                    return Stop(PrototypeBuildFailureKind.BadOutput, $"the executable opens {path} for writing ({mode})");
                if (mode.Contains('a') || mode.Contains('+')) return Stop(PrototypeBuildFailureKind.BadOutput, $"the executable opens {path} for appending or updating ({mode})");
                if (built.Count + overlay.Count >= options.MaxWrittenFiles) return Stop(PrototypeBuildFailureKind.LimitExceeded, "too many files written");
                stream = new MemoryStream();
            }
            else
            {
                if (!overlay.TryGetValue(path, out var bytes)) bytes = FromSource(path);
                if (bytes is null)
                {
                    if (!missed.Contains(path)) missed.Add(path);
                    if (RequiredFiles.Contains(path)) return Stop(PrototypeBuildFailureKind.MissingSourceFile, $"the data file {path} is not in any mounted source");
                    m.Eax = 0;
                    return HookResult.Return();
                }
                if (!readFiles.Contains(path)) readFiles.Add(path);
                stream = new MemoryStream(TextMode(mode) ? StripCarriageReturns(bytes) : bytes, writable: false);
            }
            var handle = heap.Allocate(16);
            if (handle == 0) return Stop(PrototypeBuildFailureKind.LimitExceeded, "the guest heap is full");
            var id = (uint)nextStream++;
            // [+4] bit 0 marks a C-runtime stream and [+8] is its (here unmapped) FILE pointer: code that bypasses these hooks faults at once.
            m.Memory.WriteU32(handle, id);
            m.Memory.WriteU32(handle + 4, 1);
            m.Memory.WriteU32(handle + 8, FileHandleTag + id * 0x40);
            streams[handle] = stream;
            streamPaths[handle] = path;
            m.Eax = handle;
            return HookResult.Return();
        }

        /// <summary>
        /// The source's bytes for a path. Prototype files are never taken from it: the files of an installation are the thing built
        /// (<see cref="PrototypeBuildOptions.ExistingFiles"/> is the explicit way to say which exist), so what the host mounts under
        /// <c>proto\</c> cannot change the result.
        /// </summary>
        private byte[]? FromSource(string path) => path.StartsWith("proto\\", StringComparison.OrdinalIgnoreCase) ? null : source.Read(path);

        private static bool TextMode(string mode) => !mode.Contains('b');

        private static byte[] StripCarriageReturns(byte[] bytes)
        {
            if (Array.IndexOf(bytes, (byte)'\r') < 0) return bytes;
            var result = new List<byte>(bytes.Length);
            for (var i = 0; i < bytes.Length; i++)
                if (bytes[i] != '\r' || i + 1 >= bytes.Length || bytes[i + 1] != '\n') result.Add(bytes[i]);
            return [.. result];
        }

        private HookResult CloseFile(X86Machine m)
        {
            var handle = m.Argument(0);
            if (!streams.Remove(handle, out var stream)) return Stop(PrototypeBuildFailureKind.BadOutput, $"close of 0x{handle:X8}, which is not an open file");
            var path = streamPaths[handle];
            streamPaths.Remove(handle);
            if (stream is MemoryStream { CanWrite: true } memory)
            {
                var bytes = memory.ToArray();
                overlay[path] = bytes;
                if (path.StartsWith("proto\\", StringComparison.OrdinalIgnoreCase) && path.EndsWith(".pro", StringComparison.OrdinalIgnoreCase))
                    built.Add(new BuiltPrototype(currentNumber, path["proto\\".Length..], bytes));
            }
            stream.Dispose();
            heap.Free(handle);
            m.Eax = 0;
            return HookResult.Return();
        }

        private HookResult GetLine(X86Machine m)
        {
            // fgets(buffer, size, file)
            var buffer = m.Argument(0);
            var size = (int)m.Argument(1);
            var handle = m.Argument(2);
            if (!streams.TryGetValue(handle, out var stream)) return Stop(PrototypeBuildFailureKind.BadOutput, $"read from 0x{handle:X8}, which is not an open file");
            if (size <= 0) { m.Eax = 0; return HookResult.Return(); }
            var line = new List<byte>();
            while (line.Count < size - 1)
            {
                var b = stream.ReadByte();
                if (b < 0) break;
                line.Add((byte)b);
                if (b == '\n') break;
            }
            if (line.Count == 0) { m.Eax = 0; return HookResult.Return(); }
            line.Add(0);
            m.Memory.WriteBytes(buffer, [.. line]);
            m.Eax = buffer;
            return HookResult.Return();
        }

        private const uint ListEntryBytes = 0x110, ListNameBytes = 0x104;

        private HookResult ListFiles(X86Machine m)
        {
            // list(files, pattern): the file search path's files matching "dir\*.ext", as { count, entries } with 0x110-byte entries whose
            // first bytes are the file name
            var list = m.Argument(0);
            var pattern = PathOf(m, m.Argument(1));
            var split = pattern.LastIndexOf('\\');
            var directory = split < 0 ? "" : pattern[..split];
            var wild = pattern[(split + 1)..];
            if (!listed.Contains(pattern)) listed.Add(pattern);
            var names = source.List(directory).Where(name => Glob(wild, name)).Order(StringComparer.OrdinalIgnoreCase).ToList();
            if (names.Any(name => name.Length >= ListNameBytes)) return Stop(PrototypeBuildFailureKind.LimitExceeded, $"a file name in {directory} is too long");
            var entries = names.Count == 0 ? 0 : heap.Allocate((uint)names.Count * ListEntryBytes);
            if (names.Count > 0 && entries == 0) return Stop(PrototypeBuildFailureKind.LimitExceeded, "the guest heap is full");
            for (var i = 0; i < names.Count; i++) m.Memory.WriteBytes(entries + (uint)i * ListEntryBytes, Encoding.Latin1.GetBytes(names[i] + "\0"));
            m.Memory.WriteU32(list, (uint)names.Count);
            m.Memory.WriteU32(list + 4, entries);
            m.Eax = 0;
            return HookResult.Return();
        }

        private static bool Glob(string pattern, string name)
        {
            int p = 0, n = 0, star = -1, mark = 0;
            while (n < name.Length)
            {
                if (p < pattern.Length && (pattern[p] == '?' || char.ToLowerInvariant(pattern[p]) == char.ToLowerInvariant(name[n]))) { p++; n++; }
                else if (p < pattern.Length && pattern[p] == '*') { star = p++; mark = n; }
                else if (star >= 0) { p = star + 1; n = ++mark; }
                else return false;
            }
            while (p < pattern.Length && pattern[p] == '*') p++;
            return p == pattern.Length;
        }

        private HookResult ReadFile(X86Machine m)
        {
            // fread(buffer, size, count, file): returns the number of whole items read
            var buffer = m.Argument(0);
            var size = m.Argument(1);
            var count = m.Argument(2);
            var handle = m.Argument(3);
            if (!streams.TryGetValue(handle, out var stream)) return Stop(PrototypeBuildFailureKind.BadOutput, $"read from 0x{handle:X8}, which is not an open file");
            if (size == 0 || count == 0) { m.Eax = 0; return HookResult.Return(); }
            var wanted = (ulong)size * count;
            if (wanted > int.MaxValue) return Stop(PrototypeBuildFailureKind.LimitExceeded, "a read larger than 2 GiB");
            var bytes = new byte[wanted];
            var got = stream.Read(bytes, 0, bytes.Length);
            m.Memory.WriteBytes(buffer, bytes.AsSpan(0, got));
            m.Eax = (uint)(got / size);
            return HookResult.Return();
        }

        private HookResult WriteFile(X86Machine m)
        {
            // fwrite(buffer, size, count, file)
            var buffer = m.Argument(0);
            var size = m.Argument(1);
            var count = m.Argument(2);
            var handle = m.Argument(3);
            if (!streams.TryGetValue(handle, out var stream) || !stream.CanWrite) return Stop(PrototypeBuildFailureKind.BadOutput, $"write to 0x{handle:X8}, which is not a file open for writing");
            var total = (ulong)size * count;
            if (writtenBytes + (long)total > options.MaxWrittenBytes) return Stop(PrototypeBuildFailureKind.LimitExceeded, "too many bytes written");
            var bytes = new byte[total];
            m.Memory.ReadBytes(buffer, bytes);
            stream.Write(bytes);
            writtenBytes += bytes.Length;
            m.Eax = count;
            return HookResult.Return();
        }
    }
}
