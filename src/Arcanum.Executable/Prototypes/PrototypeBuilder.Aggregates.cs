using System.Text;
using Arcanum.Executable.Maps;

namespace Arcanum.Executable.Prototypes;

/// <summary>The settings only the map aggregate build (ARC-DEV-082) gives a session.</summary>
/// <param name="ModuleGuid">The 16 bytes the original holds as the loaded module's GUID (<c>0x5D10B0</c>), the first 16 bytes of every aggregate it writes.</param>
internal sealed record AggregateSettings(byte[] ModuleGuid);

/// <summary>One aggregate the executable wrote: the file name under <c>maps\</c> and its bytes.</summary>
internal sealed record WrittenAggregateFile(string Name, byte[] Bytes);

public static partial class PrototypeBuilder
{
    internal sealed partial class BuildSession
    {
        /// <summary>The original's logger (<c>0x4FEB70</c>, a <c>printf</c> to its log file).</summary>
        private const uint LogFunction = 0x4FEB70;
        /// <summary>The module GUID (<c>0x5D10B0</c>, 16 bytes) and the flag that a module is loaded (<c>0x5D10C4</c>) that <c>0x402C20</c> answers from.</summary>
        internal const uint ModuleGuidAddress = 0x5D10B0, ModuleLoadedFlag = 0x5D10C4;

        /// <summary>The per-map function of the module load (<c>0x4102F0</c>, the argument is the map's folder name): makes the map's mobile aggregate.</summary>
        internal const uint MapAggregateFunction = 0x4102F0;

        /// <summary>The Map module's init (<c>0x40EA90</c>): allocates its tables and runs the init of every object module in order, the prototype module's (<c>0x4681B0</c>) among them.</summary>
        internal const uint MapModuleInit = 0x40EA90;

        /// <summary>The object modules the Map module's init runs (table 0x59F068, up to 0x59F3E0).</summary>
        private const int ObjectModuleCount = 17;

        /// <summary>
        /// The object modules that make the screen's resources (DirectDraw surfaces for lights, tiles, roofs, tile blocks and tile fades): they
        /// call the display library, which the host does not have, and no object read or write uses what they set up. Their inits are left out.
        /// </summary>
        private static readonly HashSet<string> DisplayModules = new(StringComparer.Ordinal) { "Light", "Tile", "Roof", "TB", "TF" };

        /// <summary>
        /// The window library's colour conversion state (0x52C070 sets it from the screen's colour masks, with floating point the interpreter does not
        /// have): three 256-byte tables that map a colour component to its bits and the three shifts, here for a 16-bit 5-6-5 screen. The object
        /// modules read them when they set up their runtime colours; nothing an aggregate holds comes from them.
        /// </summary>
        private void InitializeColourTables()
        {
            var mem = machine.Memory;
            var red = heap.Allocate(256);
            var green = heap.Allocate(256);
            var blue = heap.Allocate(256);
            for (var v = 0; v < 256; v++)
            {
                mem.WriteBytes(red + (uint)v, [(byte)(v >> 3)]);
                mem.WriteBytes(green + (uint)v, [(byte)(v >> 2)]);
                mem.WriteBytes(blue + (uint)v, [(byte)(v >> 3)]);
            }
            mem.WriteU32(0x739F2C, red);      // red table, shift at 0x739EEC
            mem.WriteU32(0x739F28, green);    // green table, shift at 0x739EF0
            mem.WriteU32(0x739E98, blue);     // blue table, shift at 0x739EAC
            mem.WriteU32(0x739EEC, 11);
            mem.WriteU32(0x739EF0, 5);
            mem.WriteU32(0x739EAC, 0);
        }

        private HashSet<string>? sourcePrototypeNumbers;

        /// <summary>
        /// The map aggregate build reads the prototypes the source already holds, so the prototype module's check must not build again: it composes a
        /// name from the group's name text, which for 307 numbers differs from the file's name (the build names the file after the object, see E3), and
        /// the original would rewrite those files with the same bytes at every start. A file with the number is found.
        /// </summary>
        private bool HasPrototypeNumber(string path)
        {
            if (aggregate is null || !path.StartsWith("proto\\", StringComparison.OrdinalIgnoreCase) || path.Length < 13) return false;
            sourcePrototypeNumbers ??= new HashSet<string>(source.List("proto").Where(name => name.Length >= 6).Select(name => name[..6]), StringComparer.Ordinal);
            return sourcePrototypeNumbers.Contains(path.Substring(6, 6));
        }

        private void InstallAggregateHooks()
        {
            // The log: the format and, for a %s, the first argument, kept for the failure text (the original writes them to its log file).
            machine.Hook(LogFunction, "log", m =>
            {
                logLines.Add(FormatLog(m));
                m.Eax = 0;
                return HookResult.Return();
            });
            // Making the folder <c>maps</c> (0x52DFE0 asks the file layer to create it): it exists in the host's view, nothing is made on the disk.
            machine.Hook(0x52DFE0, "make folder", m =>
            {
                m.Eax = 1;
                return HookResult.Return();
            });
        }

        /// <summary>The logger's text: its <c>printf</c> format with the arguments it names (strings, numbers), enough to say what the executable reported.</summary>
        private static string FormatLog(X86Machine m)
        {
            try
            {
                var format = PathOf(m, m.Argument(0));
                var text = new StringBuilder();
                var argument = 1;
                for (var i = 0; i < format.Length; i++)
                {
                    if (format[i] != '%' || i + 1 >= format.Length) { text.Append(format[i]); continue; }
                    var j = i + 1;
                    while (j < format.Length && (char.IsAsciiDigit(format[j]) || format[j] is '.' or '-' or '+' or ' ' or '#' or 'l' or 'h')) j++;
                    if (j >= format.Length) { text.Append(format, i, format.Length - i); break; }
                    switch (format[j])
                    {
                        case '%': text.Append('%'); break;
                        case 's': text.Append(TryString(m, m.Argument(argument++))); break;
                        case 'd' or 'i': text.Append((int)m.Argument(argument++)); break;
                        case 'u': text.Append(m.Argument(argument++)); break;
                        case 'x' or 'X' or 'p': text.Append("0x").Append(m.Argument(argument++).ToString("X")); break;
                        case 'c': text.Append((char)(m.Argument(argument++) & 0xFF)); break;
                        default: text.Append(format, i, j - i + 1); break;
                    }
                    i = j;
                }
                return text.ToString().TrimEnd('\n');
            }
            catch (MachineFaultException) { return "(log text unreadable)"; }
        }

        private static string TryString(X86Machine m, uint pointer)
        {
            try { return PathOf(m, pointer); }
            catch (MachineFaultException) { return $"(0x{pointer:X})"; }
        }

        public MapAggregateRunResult RunAggregates(Action<int, int>? progress)
        {
            image.LoadInto(machine.Memory);
            machine.BindImports(image);
            if (options.UninitializedFill != 0) PoisonUninitialized();
            InstallHooks();
            options.ConfigureMachine?.Invoke(machine);

            var info = heap.Allocate(256);
            machine.Memory.WriteU32(info + 4, 1);
            machine.Memory.WriteU32(info + 8, ViewChangeStub);
            machine.Memory.WriteU32(info + 12, RedrawStub);
            machine.Hook(ViewChangeStub, "view change callback", m => { viewChangeCalls++; m.Eax = 0; return HookResult.Return(); });
            machine.Hook(RedrawStub, "redraw callback", m => { redrawCalls++; m.Eax = 0; return HookResult.Return(); });
            long steps = 0;
            var failedInits = new List<string>();
            InitializeArtLibrary();
            InitializeColourTables();
            var plan = new List<(uint Address, string Name)>();
            AddModules(plan, GameModuleTable, GameModuleStride, GameModulesBeforeMap);
            // The Map module (game module 17) runs every object module's init in this order (0x40EA90), the prototype module's among them: its
            // check finds the files in the source and reads them. The host calls them one by one: the Map module's own init stops at the first
            // that returns 0, and the display modules (Light, Tile, Roof) cannot start without a display.
            var objectModules = new List<(uint Address, string Name)>();
            AddModules(objectModules, ObjectModuleTable, ObjectModuleStride, ObjectModuleCount);
            plan.AddRange(objectModules.Where(module => !DisplayModules.Contains(module.Name)));
            foreach (var (address, name) in plan)
            {
                var result = machine.Call(address, [info]);
                steps += result.Steps;
                if (!result.Completed) return AggregateFailed(result.Fault!, PrototypeBuildFailureKind.StartupFault, $"start-up function {name} (0x{address:X8})", null, steps);
                if (result.Eax == 0) failedInits.Add(name);
            }

            // The loaded module, as the original's module load leaves it: its GUID and the flag the GUID getter (0x402C20) checks.
            machine.Memory.WriteBytes(ModuleGuidAddress, aggregate!.ModuleGuid);
            machine.Memory.WriteU32(ModuleLoadedFlag, 1);

            // The module load makes one aggregate per map folder (0x402A50 lists "maps\\*.*", takes the folders and calls 0x4102F0 with each name).
            var folders = source.ListDirectories("maps").Where(name => name.Length > 0 && name[0] != '.').Order(StringComparer.OrdinalIgnoreCase).ToList();
            listed.Add("maps\\*.*");
            var done = 0;
            var maps = new List<string>();
            foreach (var folder in folders)
            {
                progress?.Invoke(done, folders.Count);
                var before = aggregateFiles.Count;
                var name = heap.Allocate((uint)folder.Length + 1);
                machine.Memory.WriteBytes(name, Encoding.Latin1.GetBytes(folder + "\0"));
                var call = machine.Call(MapAggregateFunction, [name]);
                steps += call.Steps;
                heap.Free(name);
                if (!call.Completed) return AggregateFailed(call.Fault!, PrototypeBuildFailureKind.BuildFault, $"the aggregate of map {folder}", null, steps);
                if (call.Eax == 0) return AggregateFailed(null, PrototypeBuildFailureKind.BadOutput, $"the aggregate of map {folder} failed: {string.Join(" | ", logLines)}", null, steps);
                if (aggregateFiles.Count != before + 1) return AggregateFailed(null, PrototypeBuildFailureKind.BadOutput, $"the aggregate of map {folder} wrote {aggregateFiles.Count - before} files", null, steps);
                maps.Add(folder);
                done++;
            }
            progress?.Invoke(done, folders.Count);
            return new(aggregateFiles.Select((file, index) => new BuiltMapAggregate(maps[index], file.Name, file.Bytes)).ToList(), null, steps)
            {
                MissedFiles = missed, ReadFiles = readFiles, ListedPatterns = listed, Log = logLines, InitsReturningZero = failedInits,
            };
        }

        private MapAggregateRunResult AggregateFailed(MachineFault? fault, PrototypeBuildFailureKind kind, string where, int? number, long steps)
        {
            if (fault?.Kind == MachineFaultKind.InstructionLimit) kind = PrototypeBuildFailureKind.LimitExceeded;
            var detail = fault is null ? where : $"{where}: {fault}";
            if (logLines.Count > 0) detail += $" (log: {string.Join(" | ", logLines.TakeLast(5))}; last files asked for and missing: {string.Join(", ", missed.TakeLast(5))}; last read: {string.Join(", ", readFiles.TakeLast(3))})";
            if (hostFailure is not null)
            {
                kind = hostFailureKind;
                detail = $"{where}: {hostFailure}";
            }
            return new([], new PrototypeBuildFailure(kind, detail, fault, number), steps) { MissedFiles = missed, ReadFiles = readFiles, ListedPatterns = listed, Log = logLines };
        }
    }
}
