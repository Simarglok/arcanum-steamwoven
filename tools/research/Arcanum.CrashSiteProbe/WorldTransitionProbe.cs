using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using Arcanum.Data;
using Arcanum.Data.Content;

namespace Arcanum.CrashSiteProbe;

// ARC-RES-035 research only: a catalogue of every map in the module's map list and of
// every map-to-map transition the data can express. Independently authored from the
// pinned CE locators cited in docs/research/world-transitions.md; no CE code or
// structure is reused. It decodes, it does not execute: no script VM, movement, dialog
// or map lifecycle runs. Inputs resolve through the ARC-DATA-011 content stack, so the
// winner of every name is what the original opens on a static installation.
// Only numbers, booleans and SHA-256 digests leave the probe: no map, script or
// object names, no text and no payload bytes.
internal static partial class Program
{
    private const int WorldCatalogueBase = 5000;
    private const int WorldScriptHeaderBytes = 64;
    private const int WorldScriptLineBytes = 132;
    private const int WorldClauseBytes = 44;
    private const int WorldMaxScriptBytes = 1024 * 1024;
    private const int WorldMaxCallDepth = 6;
    private const int WorldJumpRecordBytes = 32;
    private const int WorldMaxJumpPoints = 4096;
    private const int WorldMapPropertiesBytes = 24;
    private const int WorldMaxTextBytes = 64 * 1024;
    // Action types (script.h ScriptActionType order).
    private const int SatReturnSkip = 1, SatReturnRun = 2, SatGoto = 3, SatDialog = 4, SatChangeThis = 6, SatCall = 7;
    private const int SatLoopFor = 18, SatLoopEnd = 19, SatLoopBreak = 20, SatTeleport = 44, SatCallIn = 54, SatCallAt = 55;
    private const int SatCallEx = 81, SatChangeScript = 99, SatFadeTeleport = 102;
    // Value kind 3 returns the operand literally; focus kinds 0 and 21 are the triggerer and the player.
    private const int LiteralKind = 3, FocusTriggerer = 0, FocusPlayer = 21;
    // Object flags OF_DESTROYED | OF_OFF; script file flags for the two trap kinds.
    private const uint ObjectGoneFlags = 0x3;
    private const uint ScriptTrapFlags = 0x3;
    // OF_DONTDRAW | OF_INVISIBLE | OF_CLICK_THROUGH: a use click cannot pick the object.
    private const uint ObjectUnclickableFlags = 0x100 | 0x200 | 0x800;
    private const int ContainerFlagsField = 36;

    internal sealed record WorldClause(int Type, byte[] Kinds, int[] Values)
    {
        public bool Literal(int slot) => Kinds[slot] == LiteralKind;
    }
    internal sealed record WorldLine(int Line, WorldClause Condition, WorldClause True, WorldClause False);
    internal sealed record WorldScript(uint Flags, int Capacity, IReadOnlyList<WorldLine> Lines);

    /// <summary>Header: an 8-byte script header and 40 description bytes the probe never reads, then
    /// flags (48), line count (52), capacity (56) and 4 more bytes. Each 132-byte line is a condition clause and its true and false action clauses,
    /// 44 bytes each: type, eight operand kinds, eight operand values.</summary>
    internal static WorldScript DecodeWorldScript(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < WorldScriptHeaderBytes || bytes.Length > WorldMaxScriptBytes) throw new InvalidDataException("WORLD_SCRIPT_BOUND");
        var flags = BinaryPrimitives.ReadUInt32LittleEndian(bytes[48..]);
        var count = BinaryPrimitives.ReadInt32LittleEndian(bytes[52..]);
        var capacity = BinaryPrimitives.ReadInt32LittleEndian(bytes[56..]);
        if (count < 0 || capacity < count || bytes.Length != WorldScriptHeaderBytes + (long)count * WorldScriptLineBytes) throw new InvalidDataException("WORLD_SCRIPT_FRAMING");
        var lines = new WorldLine[count];
        for (var line = 0; line < count; line++)
        {
            var row = bytes.Slice(WorldScriptHeaderBytes + line * WorldScriptLineBytes, WorldScriptLineBytes);
            lines[line] = new(line, Clause(row[..WorldClauseBytes]), Clause(row.Slice(WorldClauseBytes, WorldClauseBytes)), Clause(row.Slice(2 * WorldClauseBytes, WorldClauseBytes)));
        }
        return new(flags, capacity, lines);

        static WorldClause Clause(ReadOnlySpan<byte> slot)
        {
            var values = new int[8];
            for (var i = 0; i < 8; i++) values[i] = BinaryPrimitives.ReadInt32LittleEndian(slot[(12 + 4 * i)..]);
            return new(BinaryPrimitives.ReadInt32LittleEndian(slot), slot.Slice(4, 8).ToArray(), values);
        }
    }

    internal enum WorldReach { Unreached = 0, Conditional = 1, Always = 2 }

    /// <summary>Per line: how the line and each branch are reached from <paramref name="entry"/>, and the
    /// guards (condition type, '!' for its false branch, 'loop' for a loop body) met on any path before
    /// the branch runs, the branch's own condition included. The forced path is followed while every
    /// condition is the always-true type 0; a branch on it is Always, every other reached branch is
    /// Conditional. A computed goto or an unmatched loop break stops the walk and is reported.</summary>
    internal sealed record WorldFlow(WorldReach[] True, WorldReach[] False, SortedSet<string>[] TrueGuards, SortedSet<string>[] FalseGuards, IReadOnlyList<int> UnresolvedLines);

    internal static WorldFlow AnalyseWorldFlow(WorldScript script, int entry)
    {
        var count = script.Lines.Count;
        var reachTrue = new WorldReach[count]; var reachFalse = new WorldReach[count];
        var guardsTrue = Enumerable.Range(0, count).Select(_ => new SortedSet<string>(StringComparer.Ordinal)).ToArray();
        var guardsFalse = Enumerable.Range(0, count).Select(_ => new SortedSet<string>(StringComparer.Ordinal)).ToArray();
        var unresolved = new SortedSet<int>();
        if (entry < 0 || entry >= count) return new(reachTrue, reachFalse, guardsTrue, guardsFalse, [entry]);

        // General reachability with guard sets (monotone union until nothing grows).
        var inbound = Enumerable.Range(0, count).Select(_ => (SortedSet<string>?)null).ToArray();
        inbound[entry] = new SortedSet<string>(StringComparer.Ordinal);
        var work = new Queue<int>(); work.Enqueue(entry);
        while (work.Count > 0)
        {
            var line = work.Dequeue(); var here = inbound[line]!; var row = script.Lines[line];
            var conditional = row.Condition.Type != 0;
            foreach (var (branch, clause) in conditional ? new[] { (true, row.True), (false, row.False) } : new[] { (true, row.True) })
            {
                var guards = new SortedSet<string>(here, StringComparer.Ordinal);
                if (conditional) guards.Add((branch ? "" : "!") + WorldGuard(row.Condition));
                (branch ? reachTrue : reachFalse)[line] = WorldReach.Conditional;
                (branch ? guardsTrue : guardsFalse)[line].UnionWith(guards);
                var (next, forced, unknown) = WorldStep(script, line, clause);
                if (unknown) unresolved.Add(line);
                if (!forced && next.Count > 0) guards.Add("loop");
                foreach (var target in next)
                {
                    if (inbound[target] is null) { inbound[target] = new SortedSet<string>(guards, StringComparer.Ordinal); work.Enqueue(target); }
                    else if (!guards.IsSubsetOf(inbound[target]!)) { inbound[target]!.UnionWith(guards); work.Enqueue(target); }
                }
            }
        }

        // Forced path: always-true conditions only, no loop or computed step.
        var seen = new HashSet<int>(); var current = entry;
        while (current >= 0 && current < count && seen.Add(current) && script.Lines[current].Condition.Type == 0)
        {
            reachTrue[current] = WorldReach.Always;
            var (next, forced, unknown) = WorldStep(script, current, script.Lines[current].True);
            if (!forced || unknown || next.Count != 1) break;
            current = next[0];
        }
        return new(reachTrue, reachFalse, guardsTrue, guardsFalse, unresolved.ToArray());
    }

    /// <summary>A guard label: the condition type, and for the two comparisons (4 equal, 5 less or
    /// equal) the value kinds of both sides, e.g. "4:4/3" (a global flag against a literal).</summary>
    private static string WorldGuard(WorldClause condition) => condition.Type is 4 or 5
        ? FormattableString.Invariant($"{condition.Type}:{condition.Kinds[0]}/{condition.Kinds[1]}")
        : condition.Type.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Successor lines of one executed action. Forced is false for loop control, whose
    /// successor depends on how many objects the loop's focus resolves to.</summary>
    private static (IReadOnlyList<int> Next, bool Forced, bool Unknown) WorldStep(WorldScript script, int line, WorldClause action)
    {
        var count = script.Lines.Count;
        IReadOnlyList<int> Fall() => line + 1 < count ? [line + 1] : [];
        // The loop end is the first later line whose condition is always true and whose true
        // action ends the loop; the walk resumes after it.
        int? AfterLoopEnd()
        {
            for (var l = line + 1; l < count; l++)
                if (script.Lines[l].Condition.Type == 0 && script.Lines[l].True.Type == SatLoopEnd) return l + 1 < count ? l + 1 : null;
            return null;
        }
        switch (action.Type)
        {
            case SatReturnSkip or SatReturnRun or SatDialog: return ([], true, false);
            case SatGoto:
                if (!action.Literal(0)) return ([], true, true);
                return action.Values[0] >= 0 && action.Values[0] < count ? ([action.Values[0]], true, false) : ([], true, true);
            case SatLoopFor:
            {
                var after = AfterLoopEnd();
                return (after is { } a ? Fall().Append(a).Distinct().ToArray() : Fall(), false, false);
            }
            case SatLoopEnd: return (Fall(), false, false);
            case SatLoopBreak:
            {
                var after = AfterLoopEnd();
                return after is { } a ? ([a], false, false) : ([], false, true);
            }
            default: return (Fall(), true, false);
        }
    }

    internal sealed record WorldSite(int Script, int Line, bool Branch, int Action, int FocusKind, int FocusValue, bool LiteralDestination, int Map, int X, int Y, int? Time, int? Sound, int? Movie);
    internal sealed record WorldCall(int Line, bool Branch, int Action, int? Target, int? EntryLine, int? TriggererFocus, int? AttacheeFocus = null);

    /// <summary>Teleport (44: focus, map, x, y) and fade-and-teleport (102: time, sound, movie, focus,
    /// map, x, y) actions on either branch of every line.</summary>
    internal static IReadOnlyList<WorldSite> WorldSites(int number, WorldScript script)
    {
        var sites = new List<WorldSite>();
        foreach (var row in script.Lines)
            foreach (var (branch, clause) in new[] { (true, row.True), (false, row.False) })
            {
                if (clause.Type == SatTeleport)
                    sites.Add(new(number, row.Line, branch, clause.Type, clause.Kinds[0], clause.Values[0], clause.Literal(1) && clause.Literal(2) && clause.Literal(3), clause.Values[1], clause.Values[2], clause.Values[3], null, null, null));
                else if (clause.Type == SatFadeTeleport)
                    sites.Add(new(number, row.Line, branch, clause.Type, clause.Kinds[3], clause.Values[3], clause.Literal(4) && clause.Literal(5) && clause.Literal(6), clause.Values[4], clause.Values[5], clause.Values[6],
                        clause.Literal(0) ? clause.Values[0] : null, clause.Literal(1) ? clause.Values[1] : null, clause.Literal(2) ? clause.Values[2] : null));
            }
        return sites;
    }

    /// <summary>Script-number edges: call (7), call-in (54), call-at (55): script operand 0, entry line
    /// operand 1, triggerer focus operand 2; change-this-script (6) operand 0; change-script (99)
    /// operand 2 (runs on a later invocation, entry 0). A call runs the callee once per resolved
    /// triggerer (operand 2) and attachee (operand 3), so an empty focus group skips it. Call-ex (81) runs an object's own attachment
    /// and is always unresolved (Target null).</summary>
    internal static IReadOnlyList<WorldCall> WorldCalls(WorldScript script)
    {
        var calls = new List<WorldCall>();
        foreach (var row in script.Lines)
            foreach (var (branch, clause) in new[] { (true, row.True), (false, row.False) })
            {
                switch (clause.Type)
                {
                    case SatCall or SatCallIn or SatCallAt:
                        calls.Add(new(row.Line, branch, clause.Type, clause.Literal(0) ? clause.Values[0] : null, clause.Literal(1) ? clause.Values[1] : null, clause.Kinds[2], clause.Kinds[3]));
                        break;
                    case SatChangeThis: calls.Add(new(row.Line, branch, clause.Type, clause.Literal(0) ? clause.Values[0] : null, 0, null)); break;
                    case SatChangeScript: calls.Add(new(row.Line, branch, clause.Type, clause.Literal(2) ? clause.Values[2] : null, 0, null)); break;
                    case SatCallEx: calls.Add(new(row.Line, branch, clause.Type, null, null, null)); break;
                }
            }
        return calls;
    }

    internal sealed record WorldJump(uint Flags, int X, int Y, int DestinationMap, int DestinationX, int DestinationY);

    /// <summary>map.jmp: signed count, then count 32-byte records: flags, 4 padding bytes, source
    /// location, destination runtime map, 4 padding bytes, destination location. A location packs
    /// x in the low and y in the high 32 bits.</summary>
    internal static IReadOnlyList<WorldJump> DecodeWorldJumps(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 4) throw new InvalidDataException("WORLD_JUMP_FRAMING");
        var count = BinaryPrimitives.ReadInt32LittleEndian(bytes);
        if (count < 0 || count > WorldMaxJumpPoints || bytes.Length != 4 + (long)count * WorldJumpRecordBytes) throw new InvalidDataException("WORLD_JUMP_FRAMING");
        var result = new WorldJump[count];
        for (var i = 0; i < count; i++)
        {
            var r = bytes.Slice(4 + i * WorldJumpRecordBytes, WorldJumpRecordBytes);
            var source = BinaryPrimitives.ReadInt64LittleEndian(r[8..]); var destination = BinaryPrimitives.ReadInt64LittleEndian(r[24..]);
            result[i] = new(BinaryPrimitives.ReadUInt32LittleEndian(r), LocX(source), LocY(source), BinaryPrimitives.ReadInt32LittleEndian(r[16..]), LocX(destination), LocY(destination));
        }
        return result;
    }

    private static int LocX(long location) => unchecked((int)(location & 0xFFFFFFFF));
    private static int LocY(long location) => unchecked((int)(location >> 32));

    internal sealed record WorldProperties(int BaseTerrain, long Width, long Height);

    /// <summary>map.prp: base terrain type, 4 padding bytes, then width and height in cells (64-bit).</summary>
    internal static WorldProperties DecodeWorldProperties(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != WorldMapPropertiesBytes) throw new InvalidDataException("WORLD_PROPERTIES_FRAMING");
        var width = BinaryPrimitives.ReadInt64LittleEndian(bytes[8..]); var height = BinaryPrimitives.ReadInt64LittleEndian(bytes[16..]);
        if (width <= 0 || height <= 0 || width % 64 != 0 || height % 64 != 0) throw new InvalidDataException("WORLD_PROPERTIES_SIZE");
        return new(BinaryPrimitives.ReadInt32LittleEndian(bytes), width, height);
    }

    /// <summary>startloc.txt: two lines, x then y, each read as a leading signed decimal number.</summary>
    internal static (long X, long Y) DecodeWorldStartLocation(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length > WorldMaxTextBytes) throw new InvalidDataException("WORLD_TEXT_BOUND");
        // A line read needs at least one character before end of file; the text after the last
        // newline is a line only when it is not empty. Each value reads like strtoll: leading
        // blanks, an optional sign, digits; no digits gives 0 (a blank line included).
        var lines = Encoding.ASCII.GetString(bytes).Replace("\r", "", StringComparison.Ordinal).Split('\n').ToList();
        if (lines.Count > 0 && lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);
        if (lines.Count < 2) throw new InvalidDataException("WORLD_STARTLOC_FRAMING");
        return (LeadingNumber(lines[0]), LeadingNumber(lines[1]));

        static long LeadingNumber(string line)
        {
            var text = line.TrimStart(' ', '\t'); var start = text.Length > 0 && text[0] is '-' or '+' ? 1 : 0; var end = start;
            while (end < text.Length && char.IsAsciiDigit(text[end])) end++;
            if (end == start) return 0;
            // An out-of-range value clamps the way strtoll does.
            return long.TryParse(text[..end], System.Globalization.NumberStyles.AllowLeadingSign, System.Globalization.CultureInfo.InvariantCulture, out var value) ? value : text[0] == '-' ? long.MinValue : long.MaxValue;
        }
    }

    internal sealed record WorldMapInfo(IReadOnlyList<int> LightSchemes, IReadOnlyList<(int Music, int Ambient)> SoundSchemes, int OtherLines);

    /// <summary>mapinfo.txt: "Key: value" lines; only LightScheme (one number) and SoundScheme (music,
    /// ambient) are read, case-insensitively, the way the map extension reader does. Every other
    /// non-blank line is only counted.</summary>
    internal static WorldMapInfo DecodeWorldMapInfo(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length > WorldMaxTextBytes) throw new InvalidDataException("WORLD_TEXT_BOUND");
        var light = new List<int>(); var sound = new List<(int, int)>(); var other = 0;
        foreach (var raw in Encoding.ASCII.GetString(bytes).Split('\n'))
        {
            var line = raw.Trim('\r', ' ', '\t', '\0');
            if (line.Length == 0) continue;
            var colon = line.IndexOf(':');
            var key = colon < 0 ? line : line[..colon].Trim();
            var value = colon < 0 ? "" : line[(colon + 1)..].Trim();
            if (key.Equals("LightScheme", StringComparison.OrdinalIgnoreCase) && int.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, out var scheme)) light.Add(scheme);
            else if (key.Equals("SoundScheme", StringComparison.OrdinalIgnoreCase) && value.Split(',') is [var music, var ambient]
                     && int.TryParse(music.Trim(), System.Globalization.CultureInfo.InvariantCulture, out var m) && int.TryParse(ambient.Trim(), System.Globalization.CultureInfo.InvariantCulture, out var a)) sound.Add((m, a));
            else other++;
        }
        return new(light, sound, other);
    }

    internal sealed record WorldCatalogueRow(int Catalogue, string Name, long StartX, long StartY, bool StartMap, int? WorldMap, int Area);

    /// <summary>rules/maplist.mes rows from 5000 while the run is unbroken: name, start x, start y,
    /// then optional "Type:", "WorldMap:" and "Area:" tags. The runtime map is the row's position + 1.</summary>
    internal static IReadOnlyList<WorldCatalogueRow> DecodeWorldCatalogue(IReadOnlyDictionary<int, string> mes)
    {
        var rows = new List<WorldCatalogueRow>();
        for (var key = WorldCatalogueBase; mes.TryGetValue(key, out var value); key++)
        {
            var fields = value.Split(',').Select(f => f.Trim()).ToArray();
            if (fields.Length < 3) throw new InvalidDataException("WORLD_CATALOGUE_ROW");
            var name = fields[0];
            if (name.Length == 0 || name.Any(c => c > 127 || c is '/' or '\\' or '\0') || name.Contains("..", StringComparison.Ordinal)) throw new InvalidDataException("WORLD_CATALOGUE_SELECTOR");
            var tags = fields.Skip(3).Select(f => f.Split(':', 2)).Where(p => p.Length == 2).GroupBy(p => p[0].Trim(), StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First()[1].Trim(), StringComparer.OrdinalIgnoreCase);
            int? Number(string tag) => tags.TryGetValue(tag, out var v) && int.TryParse(v, System.Globalization.CultureInfo.InvariantCulture, out var n) ? n : null;
            rows.Add(new(key, name, long.Parse(fields[1], System.Globalization.CultureInfo.InvariantCulture), long.Parse(fields[2], System.Globalization.CultureInfo.InvariantCulture),
                tags.TryGetValue("Type", out var type) && type.Equals("START_MAP", StringComparison.OrdinalIgnoreCase), Number("WorldMap"), Number("Area") ?? 0));
        }
        return rows;
    }

    internal sealed record WorldAttachment(int Map, string Source, int X, int Y, int ObjectType, int? Prototype, uint? ObjectFlags, int Sap, uint ScriptFlags, int Script, uint? ContainerFlags = null);

    internal sealed record WorldPath(WorldSite Site, WorldReach Reach, IReadOnlyList<string> Guards, IReadOnlyList<int> Chain, IReadOnlyList<string> Hops);

    /// <summary>Teleport sites one attachment can run: the attached script from line 0, then literal
    /// script edges up to <see cref="WorldMaxCallDepth"/>. A site is Always only if every hop and the
    /// site itself lie on forced paths and no hop is deferred (call-in/at, script replacement). The
    /// attached script is also entered at each of <paramref name="resumeLines"/> (a conversation
    /// resuming a dialog attachment), always as Conditional with the guard "dialog_reentry".</summary>
    internal static IReadOnlyList<WorldPath> WorldPaths(int root, IReadOnlyCollection<int> resumeLines, Func<int, WorldScript?> scripts, Func<int, int, WorldFlow?> flows, Func<int, IReadOnlyList<WorldSite>> sites, Func<int, IReadOnlyList<WorldCall>> calls)
    {
        var result = new List<WorldPath>();
        void Visit(int script, int entry, WorldReach reach, SortedSet<string> guards, List<int> chain, List<string> hops, HashSet<(int, int)> active)
        {
            if (scripts(script) is null || chain.Count > WorldMaxCallDepth) return;
            var flow = flows(script, entry); if (flow is null) return;
            WorldReach At(int line, bool branch) => branch ? flow.True[line] : flow.False[line];
            SortedSet<string> GuardsAt(int line, bool branch) => branch ? flow.TrueGuards[line] : flow.FalseGuards[line];
            foreach (var site in sites(script))
            {
                var here = At(site.Line, site.Branch); if (here == WorldReach.Unreached) continue;
                var g = new SortedSet<string>(guards, StringComparer.Ordinal); g.UnionWith(GuardsAt(site.Line, site.Branch));
                result.Add(new(site, (WorldReach)Math.Min((int)reach, (int)here), g.ToArray(), [.. chain], [.. hops]));
            }
            foreach (var call in calls(script))
            {
                var here = At(call.Line, call.Branch); if (here == WorldReach.Unreached || call.Target is not { } target || call.EntryLine is not { } line || active.Contains((target, line))) continue;
                var deferred = call.Action is SatCallIn or SatCallAt or SatChangeThis or SatChangeScript;
                var g = new SortedSet<string>(guards, StringComparer.Ordinal); g.UnionWith(GuardsAt(call.Line, call.Branch));
                if (deferred) g.Add("deferred_" + call.Action.ToString(System.Globalization.CultureInfo.InvariantCulture));
                if (call.TriggererFocus is { } focus && focus is not (FocusTriggerer or FocusPlayer)) g.Add("call_triggerer_" + focus.ToString(System.Globalization.CultureInfo.InvariantCulture));
                // Single-object focus kinds: triggerer, attachee, player. Any other may resolve to none.
                var group = call.AttacheeFocus is { } attachee && attachee is not (FocusTriggerer or 1 or FocusPlayer);
                if (group) g.Add("call_attachee_" + call.AttacheeFocus!.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
                var trap = !deferred && (scripts(target)?.Flags ?? 0) is var calleeFlags && (calleeFlags & ScriptTrapFlags) != 0;
                if (trap) g.Add("callee_trap");
                var next = (WorldReach)Math.Min((int)reach, (int)(deferred || group || trap ? WorldReach.Conditional : here));
                Visit(target, line, next, g, [.. chain, target], [.. hops, $"{script}:{call.Line}:{(call.Branch ? "t" : "f")}:{call.Action}"], [.. active, (target, line)]);
            }
        }
        Visit(root, 0, WorldReach.Always, new SortedSet<string>(StringComparer.Ordinal), [root], [], [(root, 0)]);
        if (scripts(root) is { } attached)
            foreach (var line in resumeLines.Where(l => l >= 0 && l < attached.Lines.Count).Distinct().Order())
                Visit(root, line, WorldReach.Conditional, new SortedSet<string>(["dialog_reentry"], StringComparer.Ordinal), [root], [$"{root}:{line}:dialog"], [(root, line)]);
        // One row per site and call chain: the best reach, the guards of that row.
        return result.GroupBy(p => (p.Site, string.Join(',', p.Chain))).Select(g => g.OrderByDescending(p => p.Reach).First()).ToArray();
    }

    /// <summary>Why a script edge is not an unconditional player transition. Empty means unconditional:
    /// a use click on a present object, a tile or sector entry; forced path; literal destination; the
    /// triggerer or the player travels; no trap file flag.</summary>
    internal static IReadOnlyList<string> WorldEdgeReasons(WorldAttachment attachment, WorldPath path, uint rootFlags)
    {
        var reasons = new List<string>();
        var trigger = attachment.Source is "tile" or "sector" || attachment.Sap == 1;
        if (!trigger) reasons.Add("sap_" + attachment.Sap.ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (attachment.ObjectFlags is { } flags && (flags & ObjectGoneFlags) != 0) reasons.Add("object_off_or_destroyed");
        if (attachment.Source is "sector_object" or "mobile" && attachment.Sap == 1 && attachment.ObjectFlags is { } shown && (shown & ObjectUnclickableFlags) != 0) reasons.Add("object_not_clickable");
        if ((rootFlags & ScriptTrapFlags) != 0) reasons.Add("trap_script");
        if (path.Reach != WorldReach.Always) reasons.Add("conditional_path");
        if (!path.Site.LiteralDestination) reasons.Add("computed_destination");
        if (path.Site.FocusKind is not (FocusTriggerer or FocusPlayer)) reasons.Add("moves_focus_" + path.Site.FocusKind.ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (path.Guards.Any(g => g.StartsWith("call_triggerer_", StringComparison.Ordinal))) reasons.Add("call_changes_triggerer");
        // A use click on a container opens it first (unlock script, lock, jam and magic-hold checks)
        // and runs its use script only from the loot screen.
        if (attachment.ObjectType == 2 && attachment.Source is "sector_object" or "mobile" && attachment.Sap == 1) reasons.Add("container_open_check");
        return reasons;
    }

    private sealed record WorldMapFiles(WorldCatalogueRow Row, Dictionary<string, string> Keys);

    private static int ProbeWorldTransitions(string[] args)
    {
        if (args.Length != 2 || args[0] != "--game-root") throw new ArgumentException("WORLD_USAGE");
        var discovery = ContentMountPlan.Discover(args[1]);
        if (discovery.Status != ContentMountStatus.Ready || discovery.Plan is not { } plan) throw new InvalidDataException("WORLD_MOUNT_" + discovery.Status.ToString().ToUpperInvariant());
        var archiveSources = plan.SearchOrder.Where(s => s.Kind == ContentSourceKind.Archive).ToArray();
        var before = archiveSources.Select(s => InputFingerprint.Create(s.Path)).ToArray();
        var opened = ContentStack.Open(plan);
        if (opened.Status != ContentStackStatus.Ready || opened.Stack is not { } stack) throw new InvalidDataException("WORLD_STACK_" + opened.Status.ToString().ToUpperInvariant());
        var gaps = new List<object>();
        using (stack)
        {
            // Name index: every archive member and every loose file under top-level maps/, scr/ and dlg/.
            var keys = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var source in plan.SearchOrder)
            {
                if (source.Kind == ContentSourceKind.Archive)
                {
                    using var archive = DatArchive.Open(source.Path);
                    foreach (var entry in archive.Entries)
                        if ((entry.Flags & 0x400) == 0) keys.Add(Encoding.ASCII.GetString(entry.RawNameBytes.ToArray()[..^1]).Replace('\\', '/').ToLowerInvariant());
                }
                else
                {
                    foreach (var top in new DirectoryInfo(source.Path).EnumerateDirectories().Where(d => d.LinkTarget is null && (d.Name.Equals("maps", StringComparison.OrdinalIgnoreCase) || d.Name.Equals("scr", StringComparison.OrdinalIgnoreCase) || d.Name.Equals("dlg", StringComparison.OrdinalIgnoreCase))))
                        foreach (var file in top.EnumerateFiles("*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint }))
                            keys.Add((top.Name + "/" + Path.GetRelativePath(top.FullName, file.FullName).Replace('\\', '/')).ToLowerInvariant());
                }
            }
            byte[]? Read(string key, out ContentResolution resolution)
            {
                resolution = stack.Resolve(key);
                if (resolution.Winner is not { } winner) return null;
                var read = stack.Read(winner);
                return read.Status == ContentReadStatus.Read ? read.Bytes : throw new InvalidDataException("WORLD_READ_" + read.Status.ToString().ToUpperInvariant());
            }

            var catalogueBytes = Read("rules/maplist.mes", out _) ?? throw new InvalidDataException("WORLD_CATALOGUE_MISSING");
            var catalogueMes = ObjectArt.ParseMes(catalogueBytes);
            var catalogue = DecodeWorldCatalogue(catalogueMes);
            var catalogueTail = catalogueMes.Keys.Count(k => k >= WorldCatalogueBase + catalogue.Count);
            var byName = catalogue.ToDictionary(r => r.Name.ToLowerInvariant(), r => r, StringComparer.Ordinal);
            var maps = catalogue.ToDictionary(r => r.Catalogue, r => new WorldMapFiles(r, new Dictionary<string, string>(StringComparer.Ordinal)));
            var unlistedDirectories = new SortedSet<string>(StringComparer.Ordinal); var nested = 0; var unlistedSectors = 0; var unlistedMobiles = 0;
            foreach (var key in keys.Where(k => k.StartsWith("maps/", StringComparison.Ordinal)))
            {
                var parts = key.Split('/');
                if (parts.Length < 3) continue; // normal-game aggregates and other files directly under maps/
                if (!byName.TryGetValue(parts[1], out var row))
                {
                    unlistedDirectories.Add(AreaHash(Encoding.ASCII.GetBytes(parts[1])));
                    if (key.EndsWith(".sec", StringComparison.Ordinal)) unlistedSectors++; else if (key.EndsWith(".mob", StringComparison.Ordinal)) unlistedMobiles++;
                    continue;
                }
                if (parts.Length > 3) { nested++; continue; }
                maps[row.Catalogue].Keys[parts[2]] = key;
            }

            // Prototypes from the data folder, one A ID at a time; unresolved ones become gaps.
            var data = plan.SearchOrder.FirstOrDefault(s => s.Role == ContentSourceRole.DataDirectory)?.Path;
            var prototypeRoot = data is null ? null : new DirectoryInfo(data).EnumerateDirectories().FirstOrDefault(d => d.Name.Equals("proto", StringComparison.OrdinalIgnoreCase) && d.LinkTarget is null)?.FullName;
            var prototypes = new Dictionary<int, PrototypeInfo?>();
            PrototypeInfo? Prototype(ObjectId id)
            {
                if (id.Type != 1 || prototypeRoot is null) return null;
                var aid = id.AidValue();
                if (!prototypes.TryGetValue(aid, out var found))
                {
                    try { found = ObjectWire.ReadReferencedPrototypesFromDirectory(prototypeRoot, [id]).Prototypes.Single(); }
                    catch (InvalidDataException) { found = null; }
                    prototypes[aid] = found;
                }
                return found;
            }

            var attachments = new List<WorldAttachment>(); var mapRows = new List<object>(); var jumps = new List<object>();
            var jumpEdges = new List<(int From, int To, WorldJump Jump)>();
            var sectorSets = new Dictionary<int, HashSet<long>>(); var sizes = new Dictionary<int, WorldProperties>();
            var objectsPerMap = new Dictionary<int, int>();
            foreach (var (number, files) in maps.OrderBy(m => m.Key))
            {
                var runtime = number - WorldCatalogueBase + 1;
                void Gap(string file, string code) => gaps.Add(new { map = number, file, code });
                T? Try<T>(string file, Func<T> action) where T : class
                {
                    try { return action(); }
                    catch (Exception error) when (error is InvalidDataException or ArgumentException or OverflowException or FormatException)
                    { Gap(file, error.Message.All(c => c is '_' || char.IsAsciiLetterUpper(c) || char.IsAsciiDigit(c)) ? error.Message : error.GetType().Name); return null; }
                }
                byte[]? Optional(string name, out ContentResolution? resolution)
                {
                    resolution = null;
                    if (!files.Keys.TryGetValue(name, out var key)) return null;
                    var bytes = Read(key, out var r); resolution = r; return bytes;
                }
                var prpBytes = Optional("map.prp", out _); var prp = prpBytes is null ? null : Try("map.prp", () => DecodeWorldProperties(prpBytes));
                if (prp is not null) sizes[runtime] = prp;
                var startBytes = Optional("startloc.txt", out _); var start = startBytes is null ? null : Try("startloc.txt", () => DecodeWorldStartLocation(startBytes) is var (sx0, sy0) ? (object)new { x = sx0, y = sy0 } : null);
                var infoBytes = Optional("mapinfo.txt", out _); var info = infoBytes is null ? null : Try("mapinfo.txt", () => DecodeWorldMapInfo(infoBytes));
                var jmpBytes = Optional("map.jmp", out var jmpResolution); var jmp = jmpBytes is null ? null : Try("map.jmp", () => DecodeWorldJumps(jmpBytes));
                foreach (var (jump, index) in (jmp ?? []).Select((j, i) => (j, i)))
                {
                    // Destination -1 keeps the current map (teleport processing substitutes it).
                    var to = jump.DestinationMap == -1 ? runtime : jump.DestinationMap;
                    jumps.Add(new { map = number, runtime, index, flags = jump.Flags, x = jump.X, y = jump.Y, destination_raw = jump.DestinationMap, destination_runtime = to, destination_catalogue = to + WorldCatalogueBase - 1, destination_x = jump.DestinationX, destination_y = jump.DestinationY, same_map = to == runtime });
                    jumpEdges.Add((runtime, to, jump));
                }

                // Sectors: tiles' and sector's own scripts, plus every object record.
                var sectors = new HashSet<long>(); var sectorFailures = 0; var objects = 0;
                foreach (var (name, key) in files.Keys.Where(k => k.Key.EndsWith(".sec", StringComparison.Ordinal)).OrderBy(k => k.Key, StringComparer.Ordinal))
                {
                    if (!long.TryParse(name[..^4], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var id)) { Gap("sector", "WORLD_SECTOR_NAME"); sectorFailures++; continue; }
                    sectors.Add(id);
                    var sx = checked((int)(id & 67108863)); var sy = checked((int)(id >> 26));
                    var ok = Try("sector", () =>
                    {
                        var bytes = Read(key, out _) ?? throw new InvalidDataException("WORLD_SECTOR_MISSING");
                        var slice = SectorSlice.Parse(bytes, false); var framing = SectorFraming.Parse(bytes, slice.LightCount);
                        for (var index = 0; index < framing.TileScriptCount; index++)
                        {
                            var node = bytes.AsSpan(framing.TileScriptsStart + 4 + index * 24, 24);
                            var tile = BinaryPrimitives.ReadInt32LittleEndian(node[4..]);
                            if (tile is < 0 or >= SectorTileCount) throw new InvalidDataException("WORLD_TILE_SCRIPT_TILE");
                            var script = BinaryPrimitives.ReadInt32LittleEndian(node[16..]);
                            if (script != 0) attachments.Add(new(number, "tile", sx * 64 + (tile & 63), sy * 64 + (tile >> 6), -1, null, null, 1, BinaryPrimitives.ReadUInt32LittleEndian(node[8..]), script));
                        }
                        if (framing.SectorScriptBytes == 12)
                        {
                            var node = bytes.AsSpan(framing.SectorScriptStart, 12); var script = BinaryPrimitives.ReadInt32LittleEndian(node[8..]);
                            if (script != 0) attachments.Add(new(number, "sector", sx * 64, sy * 64, -1, null, null, 1, BinaryPrimitives.ReadUInt32LittleEndian(node), script));
                        }
                        var window = bytes.AsSpan(framing.ObjectStart, framing.ObjectBytes).ToArray();
                        var instances = ObjectWire.ParseWindow(window, slice.TrailingInt32);
                        foreach (var instance in instances)
                        {
                            objects++;
                            var (wx, wy) = instance.Location is { } loc ? RebaseSectorLocation(loc, sx, sy) : (-1, -1);
                            Collect(number, "sector_object", window, instance, wx, wy);
                        }
                        return (object)true;
                    });
                    if (ok is null) sectorFailures++;
                }
                // Mobiles: one record per loose file in the map folder.
                var mobiles = 0; var mobileFailures = 0;
                foreach (var (_, key) in files.Keys.Where(k => k.Key.EndsWith(".mob", StringComparison.Ordinal)).OrderBy(k => k.Key, StringComparer.Ordinal))
                {
                    var ok = Try("mobile", () =>
                    {
                        var bytes = Read(key, out _) ?? throw new InvalidDataException("WORLD_MOBILE_MISSING");
                        var instance = ObjectWire.ParseNext(bytes, out var consumed);
                        if (consumed != bytes.Length) throw new InvalidDataException("WORLD_MOBILE_TRAILING_BYTES");
                        Collect(number, "mobile", bytes, instance, instance.Location is { } loc ? LocX(loc) : -1, instance.Location is { } l2 ? LocY(l2) : -1);
                        return (object)true;
                    });
                    if (ok is null) mobileFailures++; else { mobiles++; objects++; }
                }
                sectorSets[runtime] = sectors; objectsPerMap[number] = objects;
                var row = files.Row;
                mapRows.Add(new
                {
                    catalogue = number, runtime, start_x = row.StartX, start_y = row.StartY, start_map = row.StartMap, world_map = row.WorldMap, area = row.Area,
                    files = files.Keys.Count, properties = prp is null ? null : new { base_terrain = prp.BaseTerrain, width = prp.Width, height = prp.Height, sectors_x = prp.Width / 64, sectors_y = prp.Height / 64 },
                    startloc = start, mapinfo = info is null ? null : new { light = info.LightSchemes, sound = info.SoundSchemes.Select(s => new[] { s.Music, s.Ambient }), other_lines = info.OtherLines },
                    terrain = files.Keys.ContainsKey("terrain.tdf"), jump_points = jmp?.Count, jump_sha256 = jmpBytes is null ? null : AreaHash(jmpBytes), jump_shadowed = jmpResolution?.Shadowed.Count(),
                    sector_files = sectors.Count, sector_failures = sectorFailures, mobile_files = mobiles, mobile_failures = mobileFailures, objects,
                    other_files = files.Keys.Keys.Count(k => !k.EndsWith(".sec", StringComparison.Ordinal) && !k.EndsWith(".mob", StringComparison.Ordinal) && k is not ("map.prp" or "map.jmp" or "startloc.txt" or "mapinfo.txt" or "terrain.tdf")),
                });
            }

            void Collect(int map, string source, byte[] buffer, InstanceInfo instance, int x, int y)
            {
                var proto = Prototype(instance.PrototypeId);
                IReadOnlyList<ScriptAttachment>? scripts = instance.ScriptsOverridden ? ScriptAttachments(buffer, instance) : proto?.Scripts;
                if (scripts is null) { gaps.Add(new { map, file = source, code = "WORLD_PROTOTYPE_UNRESOLVED" }); return; }
                uint? flags = instance.DirectFlags ?? proto?.Flags;
                int? aid = instance.PrototypeId.Type == 1 ? instance.PrototypeId.AidValue() : null;
                // Container flags (first container field, serialized slot 36): direct value, else prototype.
                uint? containerFlags = instance.Type != 2 ? null : instance.NumericFields.TryGetValue(ContainerFlagsField, out var own) ? unchecked((uint)own) : proto?.NumericFields.TryGetValue(ContainerFlagsField, out var inherited) == true ? unchecked((uint)inherited) : null;
                foreach (var s in scripts.Where(s => s.script != 0)) attachments.Add(new(map, source, x, y, instance.Type, aid, flags, s.sap, s.flags, s.script, containerFlags));
            }

            // Scripts: scr/<digits>*.scr; several names with one number keep the last in sorted order.
            var scriptKeys = keys.Where(k => k.StartsWith("scr/", StringComparison.Ordinal) && k.Count(c => c == '/') == 1 && k.EndsWith(".scr", StringComparison.Ordinal)).ToArray();
            var byNumber = scriptKeys.Select(k => (Key: k, Number: ScriptNumberFromFile(k[4..]))).Where(p => p.Number is > 0).GroupBy(p => p.Number!.Value).ToDictionary(g => g.Key, g => g.Select(p => p.Key).Order(StringComparer.Ordinal).ToArray());
            var decoded = new Dictionary<int, WorldScript?>(); var scriptFailures = new List<object>(); var scriptShadowed = 0;
            WorldScript? Script(int number)
            {
                if (decoded.TryGetValue(number, out var known)) return known;
                WorldScript? result = null;
                if (byNumber.TryGetValue(number, out var names))
                {
                    try
                    {
                        var bytes = Read(names[^1], out var resolution) ?? throw new InvalidDataException("WORLD_SCRIPT_MISSING");
                        if (resolution.Shadowed.Any()) scriptShadowed++;
                        result = DecodeWorldScript(bytes);
                    }
                    catch (InvalidDataException error) { scriptFailures.Add(new { script = number, code = error.Message }); }
                }
                return decoded[number] = result;
            }
            foreach (var number in byNumber.Keys.Order()) Script(number);
            var siteCache = decoded.Where(p => p.Value is not null).ToDictionary(p => p.Key, p => WorldSites(p.Key, p.Value!));
            var callCache = decoded.Where(p => p.Value is not null).ToDictionary(p => p.Key, p => WorldCalls(p.Value!));
            var flowCache = new Dictionary<(int, int), WorldFlow?>();
            WorldFlow? Flow(int script, int entry) => flowCache.TryGetValue((script, entry), out var f) ? f : flowCache[(script, entry)] = Script(script) is { } s ? AnalyseWorldFlow(s, entry) : null;
            IReadOnlyList<WorldSite> Sites(int s) => siteCache.TryGetValue(s, out var v) ? v : [];
            IReadOnlyList<WorldCall> Calls(int s) => callCache.TryGetValue(s, out var v) ? v : [];

            // A conversation resumes the dialog attachment on the line after its dialog action (a
            // response of 0) or on line -N for a response of -N in the dialog file of that number.
            var dialogKeys = keys.Where(k => k.StartsWith("dlg/", StringComparison.Ordinal) && k.Count(c => c == '/') == 1 && k.EndsWith(".dlg", StringComparison.Ordinal))
                .Select(k => (Key: k, Digits: new string(k[4..].TakeWhile(char.IsAsciiDigit).ToArray()))).Where(p => p.Digits.Length is > 0 and <= 6)
                .GroupBy(p => int.Parse(p.Digits, System.Globalization.CultureInfo.InvariantCulture)).ToDictionary(g => g.Key, g => g.Select(p => p.Key).Order(StringComparer.Ordinal).Last());
            var resumeCache = new Dictionary<int, IReadOnlyList<int>>(); var dialogFailures = new List<object>(); var dialogResumeTargets = 0;
            IReadOnlyList<int> ResumeLines(int number)
            {
                if (resumeCache.TryGetValue(number, out var known)) return known;
                var lines = new SortedSet<int>();
                if (Script(number) is { } attached)
                    foreach (var l in attached.Lines.Where(l => (l.True.Type == SatDialog || l.False.Type == SatDialog) && l.Line + 1 < attached.Lines.Count)) lines.Add(l.Line + 1);
                if (dialogKeys.TryGetValue(number, out var dlgKey))
                {
                    try
                    {
                        var document = DlgReader.Read(Read(dlgKey, out _) ?? throw new InvalidDataException("WORLD_DIALOG_MISSING"));
                        foreach (var record in document.Records.Where(r => r.Role == DlgRecordRole.Player && r.ResponseValue is < 0)) { lines.Add(-record.ResponseValue!.Value); dialogResumeTargets++; }
                    }
                    catch (Exception error) when (error is DlgException or InvalidDataException) { dialogFailures.Add(new { script = number, code = error.GetType().Name }); }
                }
                return resumeCache[number] = lines.ToArray();
            }

            var allSites = siteCache.Values.SelectMany(v => v).ToArray();
            var siteRows = allSites.Select(site =>
            {
                var flow = Flow(site.Script, 0)!;
                var reach = site.Branch ? flow.True[site.Line] : flow.False[site.Line];
                var guards = site.Branch ? flow.TrueGuards[site.Line] : flow.FalseGuards[site.Line];
                var condition = decoded[site.Script]!.Lines[site.Line].Condition;
                return new { script = site.Script, line = site.Line, branch = site.Branch ? "true" : "false", action = site.Action, focus = site.FocusKind, literal = site.LiteralDestination, destination_catalogue = site.Map, destination_x = site.X, destination_y = site.Y, time = site.Time, sound = site.Sound, movie = site.Movie,
                    condition = condition.Type, condition_kinds = condition.Kinds.Take(3), condition_values = condition.Values.Take(3), file_flags = decoded[site.Script]!.Flags, reach_from_line0 = reach.ToString(), guards };
            }).ToArray();

            // Literal callers of each script, to find teleport scripts nothing reaches.
            var callers = callCache.SelectMany(p => p.Value.Where(c => c.Target is not null).Select(c => (Caller: p.Key, Target: c.Target!.Value))).ToLookup(p => p.Target, p => p.Caller);
            var reachesTeleport = new HashSet<int>(siteCache.Where(p => p.Value.Count > 0).Select(p => p.Key));
            for (var grew = true; grew;)
            {
                grew = false;
                foreach (var (script, list) in callCache)
                    if (!reachesTeleport.Contains(script) && list.Any(c => c.Target is { } t && reachesTeleport.Contains(t))) grew = reachesTeleport.Add(script) || grew;
            }

            var edges = new List<object>(); var scriptEdges = new List<(int From, int To, bool Unconditional, string Trigger, IReadOnlyList<string> Guards, IReadOnlyList<string> Reasons)>();
            // Steps the walk cannot follow, in every script that holds or reaches a teleport: computed
            // or out-of-range gotos and unmatched loop breaks (from line 0), and script edges without a
            // literal target (computed calls and every call-ex).
            var unresolvedSteps = reachesTeleport.Order().Select(n => (Script: n, Lines: Flow(n, 0)?.UnresolvedLines ?? [])).Where(p => p.Lines.Count > 0).Select(p => new { script = p.Script, lines = p.Lines }).ToArray();
            var unresolvedCalls = reachesTeleport.Order().SelectMany(n => Calls(n).Where(c => c.Target is null).Select(c => new { script = n, line = c.Line, branch = c.Branch ? "true" : "false", action = c.Action })).ToArray();
            var coveredSites = new HashSet<WorldSite>();
            // One-shot: the attached script removes or replaces itself on a reached branch, or carries
            // the auto-removing file flag (0x4), so the trigger works once.
            bool OneShot(int script)
            {
                if (Script(script) is not { } root) return false;
                if ((root.Flags & 0x4) != 0) return true;
                var flow = Flow(script, 0)!;
                return root.Lines.Any(l => (flow.True[l.Line] != WorldReach.Unreached && l.True.Type is 5 or SatChangeThis) || (flow.False[l.Line] != WorldReach.Unreached && l.False.Type is 5 or SatChangeThis));
            }
            foreach (var attachment in attachments.Where(a => reachesTeleport.Contains(a.Script)))
            {
                var rootFlags = Script(attachment.Script)?.Flags ?? 0;
                foreach (var path in WorldPaths(attachment.Script, attachment.Sap == 9 && attachment.Source is "sector_object" or "mobile" ? ResumeLines(attachment.Script) : [], Script, Flow, Sites, Calls))
                {
                    coveredSites.Add(path.Site);
                    var fromRuntime = attachment.Map - WorldCatalogueBase + 1;
                    var toRuntime = path.Site.LiteralDestination ? path.Site.Map - 4999 : (int?)null;
                    if (toRuntime == -1) toRuntime = fromRuntime; // map -1 keeps the current map
                    var reasons = WorldEdgeReasons(attachment, path, rootFlags);
                    bool? arrivalSector = toRuntime is { } t && sectorSets.TryGetValue(t, out var set) ? set.Contains((long)(uint)(path.Site.X >> 6) | ((long)(path.Site.Y >> 6) << 26)) : null;
                    bool? inBounds = toRuntime is { } t2 && sizes.TryGetValue(t2, out var size) ? path.Site.X >= 0 && path.Site.Y >= 0 && path.Site.X < size.Width && path.Site.Y < size.Height : null;
                    edges.Add(new
                    {
                        map = attachment.Map, runtime = fromRuntime, source = attachment.Source, x = attachment.X, y = attachment.Y, object_type = attachment.ObjectType, prototype = attachment.Prototype,
                        object_flags = attachment.ObjectFlags, container_flags = attachment.ContainerFlags, one_shot = OneShot(attachment.Script), sap = attachment.Sap, attached_script = attachment.Script, script_header_flags = attachment.ScriptFlags,
                        site_script = path.Site.Script, site_line = path.Site.Line, site_branch = path.Site.Branch ? "true" : "false", action = path.Site.Action, focus = path.Site.FocusKind,
                        destination_catalogue = path.Site.LiteralDestination ? path.Site.Map : (int?)null, destination_runtime = toRuntime, destination_x = path.Site.X, destination_y = path.Site.Y,
                        same_map = toRuntime == fromRuntime, arrival_in_bounds = inBounds, arrival_sector_file = arrivalSector,
                        reach = path.Reach.ToString(), guards = path.Guards, chain = path.Chain, hops = path.Hops, unconditional = reasons.Count == 0, reasons,
                    });
                    var trigger = attachment.Source is "tile" or "sector" ? attachment.Source + "_entry" : "sap_" + attachment.Sap.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    if (toRuntime is { } to && to != fromRuntime) scriptEdges.Add((fromRuntime, to, reasons.Count == 0, trigger, path.Guards, reasons));
                }
            }
            var attachedScripts = attachments.Select(a => a.Script).ToHashSet();
            var orphanSites = allSites.Where(s => !coveredSites.Contains(s)).Select(s => new { script = s.Script, line = s.Line, branch = s.Branch ? "true" : "false", action = s.Action, focus = s.FocusKind, attached = attachedScripts.Contains(s.Script), attached_saps = attachments.Where(a => a.Script == s.Script).Select(a => a.Sap).Distinct().Order().ToArray(), literal_callers = callers[s.Script].Distinct().Order().ToArray(), destination_catalogue = s.LiteralDestination ? s.Map : (int?)null, destination_x = s.X, destination_y = s.Y }).ToArray();

            // Map graph from the start map: jump points, then unconditional script edges, then all edges.
            var startRuntime = catalogue.FirstOrDefault(r => r.StartMap) is { } startRow ? startRow.Catalogue - WorldCatalogueBase + 1 : 1;
            int[] Reach(IEnumerable<(int From, int To)> graph)
            {
                var adjacency = graph.Where(e => e.From != e.To).ToLookup(e => e.From, e => e.To);
                var seen = new SortedSet<int> { startRuntime }; var queue = new Queue<int>(); queue.Enqueue(startRuntime);
                while (queue.Count > 0) foreach (var next in adjacency[queue.Dequeue()]) if (seen.Add(next)) queue.Enqueue(next);
                return seen.ToArray();
            }
            var jumpGraph = jumpEdges.Select(e => (e.From, e.To)).ToArray();
            var unconditionalGraph = jumpGraph.Concat(scriptEdges.Where(e => e.Unconditional).Select(e => (e.From, e.To))).ToArray();
            var anyGraph = jumpGraph.Concat(scriptEdges.Select(e => (e.From, e.To))).ToArray();
            var allRuntimes = Enumerable.Range(1, catalogue.Count).ToArray();
            var pairs = anyGraph.Where(e => e.From != e.To).Distinct().ToArray();
            var graph = new
            {
                start_runtime = startRuntime,
                map_pairs = pairs.Length,
                map_pairs_unconditional = unconditionalGraph.Where(e => e.From != e.To).Distinct().Count(),
                reachable_jump_only = Reach(jumpGraph),
                reachable_unconditional = Reach(unconditionalGraph),
                reachable_any = Reach(anyGraph),
                no_inbound = allRuntimes.Where(m => !pairs.Any(p => p.To == m)).ToArray(),
                no_outbound = allRuntimes.Where(m => !pairs.Any(p => p.From == m)).ToArray(),
                one_way_pairs = pairs.Where(p => !pairs.Contains((p.To, p.From))).OrderBy(p => p.From).ThenBy(p => p.To).Select(p => new[] { p.From, p.To }).ToArray(),
                pairs = pairs.OrderBy(p => p.From).ThenBy(p => p.To).Select(p =>
                {
                    var here = scriptEdges.Where(e => e.From == p.From && e.To == p.To).ToArray();
                    return new
                    {
                        from = p.From, to = p.To, jumps = jumpEdges.Count(e => e.From == p.From && e.To == p.To), script_paths = here.Length,
                        unconditional_triggers = here.Where(e => e.Unconditional).Select(e => e.Trigger).Distinct().Order(StringComparer.Ordinal).ToArray(),
                        conditional_triggers = here.Where(e => !e.Unconditional).Select(e => e.Trigger).Distinct().Order(StringComparer.Ordinal).ToArray(),
                        conditional_guards = here.Where(e => !e.Unconditional).SelectMany(e => e.Guards).Distinct().Order(StringComparer.Ordinal).ToArray(),
                        conditional_reasons = here.Where(e => !e.Unconditional).SelectMany(e => e.Reasons).Distinct().Order(StringComparer.Ordinal).ToArray(),
                    };
                }).ToArray(),
            };

            var after = archiveSources.Select(s => InputFingerprint.Create(s.Path)).ToArray();
            if (!before.SequenceEqual(after)) throw new InvalidDataException("WORLD_INPUT_CHANGED");
            var report = new
            {
                schema = 1,
                sources = archiveSources.Select((s, i) => new { role = s.Role.ToString(), number = s.Number, bytes = before[i].Bytes, sha256 = before[i].Sha256 }).ToArray(),
                directory_sources = plan.SearchOrder.Where(s => s.Kind == ContentSourceKind.Directory).Select(s => new { role = s.Role.ToString(), number = s.Number }).ToArray(),
                summary = new
                {
                    catalogue_rows = catalogue.Count, catalogue_tail_keys = catalogueTail, unlisted_map_directories = unlistedDirectories.Count, unlisted_sector_files = unlistedSectors, unlisted_mobile_files = unlistedMobiles, nested_map_files = nested,
                    sector_files = sectorSets.Values.Sum(v => v.Count), objects = objectsPerMap.Values.Sum(),
                    jump_points = jumps.Count, maps_with_jump_points = jumpEdges.Select(e => e.From).Distinct().Count(),
                    scripts = byNumber.Count, script_numbers_with_several_files = byNumber.Count(p => p.Value.Length > 1), scripts_shadowed = scriptShadowed, script_failures = scriptFailures.Count, dialog_files = dialogKeys.Count, dialog_failures = dialogFailures.Count, dialog_resume_targets = dialogResumeTargets,
                    teleport_sites = allSites.Length, teleport_scripts = siteCache.Count(p => p.Value.Count > 0), fade_teleport_sites = allSites.Count(s => s.Action == SatFadeTeleport),
                    computed_destination_sites = allSites.Count(s => !s.LiteralDestination), attachments = attachments.Count, teleport_attachments = attachments.Count(a => reachesTeleport.Contains(a.Script)),
                    edges = edges.Count, orphan_sites = orphanSites.Length, unresolved_step_scripts = unresolvedSteps.Length, unresolved_calls = unresolvedCalls.Length, prototypes_loaded = prototypes.Count(p => p.Value is not null), prototypes_unresolved = prototypes.Count(p => p.Value is null), gaps = gaps.Count,
                },
                maps = mapRows, jump_points = jumps, teleport_sites = siteRows, edges, orphan_sites = orphanSites, graph, unresolved_steps = unresolvedSteps, unresolved_calls = unresolvedCalls, script_failures = scriptFailures, dialog_failures = dialogFailures, gaps,
            };
            Console.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        }
        return 0;
    }

    private static void WorldTransitionSelfTest()
    {
        static void Rejects(Action action, string code)
        {
            try { action(); }
            catch (InvalidDataException error) when (error.Message == code) { return; }
            throw new InvalidOperationException("world self-test failed: " + code);
        }
        static void Check(bool condition, string what)
        {
            if (!condition) throw new InvalidOperationException("world self-test failed: " + what);
        }
        // Literal wire offsets, independent of the decoder's constants.
        static byte[] Script(int lines)
        {
            var bytes = new byte[64 + lines * 132];
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(52), lines);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(56), lines + 3);
            return bytes;
        }
        static void Clause(byte[] bytes, int line, int slot, int type, params (int Kind, int Value)[] operands)
        {
            var at = 64 + line * 132 + slot * 44;
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(at), type);
            for (var i = 0; i < operands.Length; i++) { bytes[at + 4 + i] = (byte)operands[i].Kind; BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(at + 12 + 4 * i), operands[i].Value); }
        }
        // Line 0: always-true literal teleport of the triggerer. Line 1: global flag 7 condition, false
        // branch fade-and-teleports the player with a computed x. Line 2: goto 4. Line 3: unreachable
        // teleport. Line 4: call script 9 at line 1 on the true branch, then return.
        var s = Script(5);
        Clause(s, 0, 1, 44, (0, 0), (3, 5078), (3, 2087), (3, 2018));
        Clause(s, 1, 0, 43, (3, 7)); Clause(s, 1, 2, 102, (3, 2), (3, 0), (3, 0), (21, 0), (3, 5001), (4, 12), (3, 30));
        Clause(s, 2, 1, 3, (3, 4));
        Clause(s, 3, 1, 44, (0, 0), (3, 5000), (3, 1), (3, 1));
        Clause(s, 4, 1, 7, (3, 9), (3, 1), (0, 0), (1, 0)); Clause(s, 4, 2, 1);
        var script = DecodeWorldScript(s);
        Check(script.Lines.Count == 5 && script.Capacity == 8 && script.Flags == 0, "framing");
        var sites = WorldSites(1, script);
        Check(sites.Count == 3 && sites[0] == new WorldSite(1, 0, true, 44, 0, 0, true, 5078, 2087, 2018, null, null, null), "literal teleport site");
        Check(sites[1].Action == 102 && !sites[1].Branch && sites[1].FocusKind == 21 && !sites[1].LiteralDestination && sites[1].Map == 5001 && sites[1].Time == 2, "fade teleport site");
        var flow = AnalyseWorldFlow(script, 0);
        Check(flow.True[0] == WorldReach.Always && flow.False[1] == WorldReach.Conditional && flow.True[1] == WorldReach.Conditional && flow.True[3] == WorldReach.Unreached, "reach");
        Check(flow.False[1].Equals(WorldReach.Conditional) && flow.FalseGuards[1].SetEquals(["!43"]) && flow.TrueGuards[4].SetEquals(["43", "!43"]) && WorldGuard(new WorldClause(4, [1, 3, 0, 0, 0, 0, 0, 0], new int[8])) == "4:1/3", "guards");
        Check(flow.True[4] == WorldReach.Conditional && flow.UnresolvedLines.Count == 0, "goto after condition stays conditional");
        var calls = WorldCalls(script);
        Check(calls.Count == 1 && calls[0] == new WorldCall(4, true, 7, 9, 1, 0, 1), "call edge");
        // Callee 9: line 0 conditional, line 1 always teleport from entry 1.
        var callee = Script(2); Clause(callee, 0, 0, 43, (3, 1)); Clause(callee, 1, 1, 44, (0, 0), (3, 5002), (3, 10), (3, 11));
        var scripts = new Dictionary<int, WorldScript> { [1] = script, [9] = DecodeWorldScript(callee) };
        var paths = WorldPaths(1, [], n => scripts.GetValueOrDefault(n), (n, e) => scripts.TryGetValue(n, out var x) ? AnalyseWorldFlow(x, e) : null, n => scripts.TryGetValue(n, out var x) ? WorldSites(n, x) : [], n => scripts.TryGetValue(n, out var x) ? WorldCalls(x) : []);
        Check(paths.Count == 3 && paths.Single(p => p.Site.Script == 9).Reach == WorldReach.Conditional && paths.Single(p => p.Site.Script == 9).Chain.SequenceEqual([1, 9]), "call path reach");
        var talk = Script(2); Clause(talk, 0, 1, 4, (3, 10)); Clause(talk, 1, 1, 44, (0, 0), (3, 5003), (3, 5), (3, 6));
        var talkScripts = new Dictionary<int, WorldScript> { [2] = DecodeWorldScript(talk) };
        WorldScript? TalkScript(int n) => talkScripts.GetValueOrDefault(n);
        Check(WorldPaths(2, [], TalkScript, (n, e) => AnalyseWorldFlow(talkScripts[n], e), n => WorldSites(n, talkScripts[n]), n => WorldCalls(talkScripts[n])).Count == 0, "dialog stops the walk");
        var resumed = WorldPaths(2, [1, 7], TalkScript, (n, e) => AnalyseWorldFlow(talkScripts[n], e), n => WorldSites(n, talkScripts[n]), n => WorldCalls(talkScripts[n]));
        Check(resumed.Count == 1 && resumed[0].Reach == WorldReach.Conditional && resumed[0].Guards.SequenceEqual(["dialog_reentry"]), "dialog re-entry");
        var use = new WorldAttachment(5000, "sector_object", 1, 2, 3, 4047, 0, 1, 0, 1);
        Check(WorldEdgeReasons(use, paths[0], 0).Count == 0, "unconditional use edge");
        Check(WorldEdgeReasons(use with { Sap = 9 }, paths[0], 0).SequenceEqual(["sap_9"]) && WorldEdgeReasons(use with { ObjectFlags = 2 }, paths[0], 0).SequenceEqual(["object_off_or_destroyed"]), "sap and off reasons");
        Check(WorldEdgeReasons(use, paths[0], 1).SequenceEqual(["trap_script"]) && WorldEdgeReasons(use, paths[1], 0).SequenceEqual(["conditional_path", "computed_destination"]), "trap, path and destination reasons");
        Check(WorldEdgeReasons(use with { Source = "tile", Sap = 1, ObjectFlags = null }, paths[0], 0).Count == 0, "tile entry");
        Check(WorldEdgeReasons(use with { ObjectType = 2, ContainerFlags = 0 }, paths[0], 0).SequenceEqual(["container_open_check"]) && WorldEdgeReasons(use with { ObjectType = 2, Sap = 9 }, paths[0], 0).SequenceEqual(["sap_9"]), "container use");
        // A call whose attachee is a focus group (10, looped object) may run zero times.
        var grouped = Script(2); Clause(grouped, 0, 1, 7, (3, 9), (3, 1), (0, 0), (10, 0)); var groupedScripts = new Dictionary<int, WorldScript> { [4] = DecodeWorldScript(grouped), [9] = DecodeWorldScript(callee) };
        var groupedPath = WorldPaths(4, [], n => groupedScripts.GetValueOrDefault(n), (n, e) => AnalyseWorldFlow(groupedScripts[n], e), n => WorldSites(n, groupedScripts[n]), n => WorldCalls(groupedScripts[n])).Single();
        Check(groupedPath.Reach == WorldReach.Conditional && groupedPath.Guards.Contains("call_attachee_10"), "call attachee group");
        var trapped = (byte[])callee.Clone(); trapped[48] = 2; groupedScripts[9] = DecodeWorldScript(trapped); Clause(grouped, 0, 1, 7, (3, 9), (3, 1), (0, 0), (1, 0)); groupedScripts[4] = DecodeWorldScript(grouped);
        var trappedPath = WorldPaths(4, [], n => groupedScripts.GetValueOrDefault(n), (n, e) => AnalyseWorldFlow(groupedScripts[n], e), n => WorldSites(n, groupedScripts[n]), n => WorldCalls(groupedScripts[n])).Single();
        Check(trappedPath.Reach == WorldReach.Conditional && trappedPath.Guards.SequenceEqual(["callee_trap"]), "callee trap");
        Check(WorldEdgeReasons(use with { ObjectFlags = 0x800 }, paths[0], 0).SequenceEqual(["object_not_clickable"]) && WorldEdgeReasons(use with { Sap = 9, ObjectFlags = 0x100 }, paths[0], 0).SequenceEqual(["sap_9"]), "clickability");
        // A script calling itself at another entry line is followed once; the same entry is a cycle.
        var self = Script(3); Clause(self, 0, 1, 7, (3, 3), (3, 2), (0, 0), (1, 0)); Clause(self, 1, 1, 1); Clause(self, 2, 1, 44, (0, 0), (3, 5004), (3, 1), (3, 2));
        var selfScripts = new Dictionary<int, WorldScript> { [3] = DecodeWorldScript(self) };
        var selfPaths = WorldPaths(3, [], n => selfScripts.GetValueOrDefault(n), (n, e) => AnalyseWorldFlow(selfScripts[n], e), n => WorldSites(n, selfScripts[n]), n => WorldCalls(selfScripts[n]));
        Check(selfPaths.Count == 1 && selfPaths[0].Reach == WorldReach.Always && selfPaths[0].Chain.SequenceEqual([3, 3]), "self call at another line");
        // Loop: line 0 loop-for, line 1 teleport, line 2 loop-end, line 3 teleport after the loop.
        var loop = Script(4); Clause(loop, 0, 1, 18, (2, 0)); Clause(loop, 1, 1, 44, (10, 0), (3, 5000), (3, 1), (3, 1)); Clause(loop, 2, 1, 19); Clause(loop, 3, 1, 44, (0, 0), (3, 5000), (3, 2), (3, 2));
        var loopFlow = AnalyseWorldFlow(DecodeWorldScript(loop), 0);
        var falseEnd = Script(3); Clause(falseEnd, 0, 1, 18, (2, 0)); Clause(falseEnd, 1, 0, 43, (3, 1)); Clause(falseEnd, 1, 2, 19); Clause(falseEnd, 2, 1, 19);
        Check(WorldStep(DecodeWorldScript(falseEnd), 0, DecodeWorldScript(falseEnd).Lines[0].True).Next.SequenceEqual([1]), "loop end needs an always-true line");
        Check(loopFlow.True[0] == WorldReach.Always && loopFlow.True[1] == WorldReach.Conditional && loopFlow.TrueGuards[1].SetEquals(["loop"]) && loopFlow.True[3] == WorldReach.Conditional, "loop");
        var computedGoto = Script(1); Clause(computedGoto, 0, 1, 3, (4, 1));
        Check(AnalyseWorldFlow(DecodeWorldScript(computedGoto), 0).UnresolvedLines.SequenceEqual([0]), "computed goto");
        Rejects(() => DecodeWorldScript(s.AsSpan(0, s.Length - 1)), "WORLD_SCRIPT_FRAMING");
        var over = Script(1); BinaryPrimitives.WriteInt32LittleEndian(over.AsSpan(56), 0);
        Rejects(() => DecodeWorldScript(over), "WORLD_SCRIPT_FRAMING");
        Rejects(() => DecodeWorldScript(new byte[63]), "WORLD_SCRIPT_BOUND");
        // Jump points: two records, location x low / y high.
        var jmp = new byte[4 + 2 * 32]; BinaryPrimitives.WriteInt32LittleEndian(jmp, 2);
        BinaryPrimitives.WriteUInt32LittleEndian(jmp.AsSpan(4), 5); BinaryPrimitives.WriteInt64LittleEndian(jmp.AsSpan(12), 10L | (20L << 32)); BinaryPrimitives.WriteInt32LittleEndian(jmp.AsSpan(20), 3); BinaryPrimitives.WriteInt64LittleEndian(jmp.AsSpan(28), 30L | (40L << 32));
        BinaryPrimitives.WriteInt64LittleEndian(jmp.AsSpan(44), 1L | (2L << 32)); BinaryPrimitives.WriteInt32LittleEndian(jmp.AsSpan(52), 1);
        var decodedJumps = DecodeWorldJumps(jmp);
        Check(decodedJumps.Count == 2 && decodedJumps[0] == new WorldJump(5, 10, 20, 3, 30, 40) && decodedJumps[1] == new WorldJump(0, 1, 2, 1, 0, 0), "jump records");
        Check(DecodeWorldJumps(new byte[4]).Count == 0, "empty jump file");
        Rejects(() => DecodeWorldJumps(jmp.AsSpan(0, 35)), "WORLD_JUMP_FRAMING");
        var negative = new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(negative, -1);
        Rejects(() => DecodeWorldJumps(negative), "WORLD_JUMP_FRAMING");
        // Properties: terrain 2, 128000 x 64 cells.
        var prp = new byte[24]; BinaryPrimitives.WriteInt32LittleEndian(prp, 2); BinaryPrimitives.WriteInt64LittleEndian(prp.AsSpan(8), 128000); BinaryPrimitives.WriteInt64LittleEndian(prp.AsSpan(16), 64);
        Check(DecodeWorldProperties(prp) == new WorldProperties(2, 128000, 64), "properties");
        Rejects(() => DecodeWorldProperties(prp.AsSpan(0, 23)), "WORLD_PROPERTIES_FRAMING");
        BinaryPrimitives.WriteInt64LittleEndian(prp.AsSpan(16), 65); Rejects(() => DecodeWorldProperties(prp), "WORLD_PROPERTIES_SIZE");
        // Text files: synthetic content only.
        Check(DecodeWorldStartLocation("123\r\n-45 trailing\r\n"u8) == (123, -45), "startloc");
        Rejects(() => DecodeWorldStartLocation("12\n"u8), "WORLD_STARTLOC_FRAMING");
        Check(DecodeWorldStartLocation("99999999999999999999\n-99999999999999999999\n"u8) == (long.MaxValue, long.MinValue) && DecodeWorldStartLocation("x\n\n"u8) == (0, 0) && DecodeWorldStartLocation(" +7z\n-\n"u8) == (7, 0), "startloc reads like strtoll");
        var info = DecodeWorldMapInfo("lightscheme: 4\r\nSoundScheme: 3, 5\r\nOther: 1\r\n\r\n"u8);
        Check(info.LightSchemes.SequenceEqual([4]) && info.SoundSchemes.SequenceEqual([(3, 5)]) && info.OtherLines == 1, "mapinfo");
        // Catalogue: unbroken run from 5000, tags, a gap ends the run.
        var mes = new SortedDictionary<int, string> { [5000] = "Alpha, 10, 20, Type: START_MAP, WorldMap: 0", [5001] = "beta,1,2,Area: 7", [5003] = "gamma,0,0" };
        var rows = DecodeWorldCatalogue(mes);
        Check(rows.Count == 2 && rows[0] == new WorldCatalogueRow(5000, "Alpha", 10, 20, true, 0, 0) && rows[1] == new WorldCatalogueRow(5001, "beta", 1, 2, false, null, 7), "catalogue");
        Rejects(() => DecodeWorldCatalogue(new SortedDictionary<int, string> { [5000] = "../x,1,2" }), "WORLD_CATALOGUE_SELECTOR");
        Rejects(() => DecodeWorldCatalogue(new SortedDictionary<int, string> { [5000] = "x,1" }), "WORLD_CATALOGUE_ROW");
        Console.WriteLine("world-transitions-self-test: PASS (script framing, sites, calls, forced/conditional reach, guards, loops, computed goto, call paths, self calls, call focus groups and traps, dialog re-entry, edge reasons, container use, clickability, loop end, jump points, properties, startloc, mapinfo, catalogue)");
    }
}
