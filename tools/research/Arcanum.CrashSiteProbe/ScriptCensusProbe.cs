using System.Text.Json;
using Arcanum.Data.Content;

namespace Arcanum.CrashSiteProbe;

// ARC-RES-045 research only (slice S0 of docs/research/scr-event-dispatcher.md): which
// attachment points carry scripts anywhere in the module, and which condition, action,
// value and focus types the installed scripts use, over every line and over the lines a
// run can reach. It decodes, it never executes. Operand signatures (which operand slots
// take a number and which an object) are format facts taken from the order of number and
// object placeholders in the ScriptEd 1.50 editor data (TimoP80/ScriptEd_v1_50 da7e6d2,
// GPL-2.0; no text) and agree with the source interpreter reading for 194 of the 195 types (that reading
// takes the object of action 86 from slot 1, census anomaly X9). No code or structure
// is reused.
// Only numbers leave the probe: no script, map or object names and no text.
internal static partial class Program
{
    private const int CsConditionTypes = 53, CsActionTypes = 142, CsValueKinds = 7, CsFocusKinds = 24, CsAttachmentPoints = 36;
    private const int CsDialogOverridePoint = 31;
    private const int CsLocalFlags = 32, CsCounters = 4, CsLocalSlots = 10, CsGlobalFlags = 3200, CsGlobalVars = 2000;

    // Operand signatures by type: 'n' a number (value kind), 'o' an object (focus kind), '-' none.
    private static readonly string[] CsConditionSignatures =
        ("- - on n nn nn onn nn on on oo o o o o o o on oo on on on o o oo o o o oo oo o o onn on onnn on o o o o on n o n oon nn n o on on - o o").Split(' ');
    private static readonly string[] CsActionSignatures =
        ("- - - n n - n nnoo n n nn nnn nnn nnn nnn oo onn nn o - - oo o no nn no no no no oon oon oon on non on on oo nnn on on n noo n n onnn onn onn non ono no no no no on nnoon nnoon o o o on onn no on on no no no o o onn on oon on on onn on on onn on ooonn nooon onno n no on nn o nn o on n noon on o on onn n n n onn n n nnnonnn nnnn no n nn onn onn o n on on n no oo no o ono on no ono ono n no no no o on o no n - on on no no non onn on onnn onnn").Split(' ');

    // The candidate first admitted set of slice S1: control flow and return codes, goto, local
    // flags and counters, local variables, global flags, story state. Nothing here reads or
    // writes an object.
    private static readonly int[] CsS1Conditions = [0, 3, 4, 5, 43];
    private static readonly int[] CsS1Actions = [0, 1, 2, 3, 8, 9, 10, 11, 12, 13, 14, 42, 43, 100, 101];
    private static readonly int[] CsS1ValueKinds = [0, 2, 3, 4];

    internal static string CsSignature(bool condition, int type)
    {
        var table = condition ? CsConditionSignatures : CsActionSignatures;
        return type >= 0 && type < table.Length ? table[type] == "-" ? "" : table[type] : "?";
    }

    /// <summary>Operand use of one clause: per slot its role from the signature, the kind byte
    /// and the value; slots past the signature are "extra" and should hold zeros.</summary>
    internal static IEnumerable<(int Slot, char Role, int Kind, int Value)> CsOperands(bool condition, WorldClause clause)
    {
        var signature = CsSignature(condition, clause.Type);
        if (signature == "?") yield break;
        for (var slot = 0; slot < 8; slot++)
            yield return (slot, slot < signature.Length ? signature[slot] : 'x', clause.Kinds[slot], clause.Values[slot]);
    }

    /// <summary>The first component of a clause outside the S1 candidate set, or null. Slots past
    /// the signature are never read by the interpreter and are not checked (the census shows
    /// that the installed data leaves editor bytes in them).</summary>
    internal static string? CsS1Refusal(bool condition, WorldClause clause)
    {
        if (!(condition ? CsS1Conditions : CsS1Actions).Contains(clause.Type)) return (condition ? "condition:" : "action:") + clause.Type;
        foreach (var (slot, role, kind, value) in CsOperands(condition, clause))
        {
            if (role == 'n' && !CsS1ValueKinds.Contains(kind)) return "value_kind:" + kind;
            if (role == 'o') return "focus_kind:" + kind;
        }
        return null;
    }

    /// <summary>Rule P6: a literal goto inside the script or to exactly its end; nothing computed.</summary>
    internal static bool CsS1GotoOk(WorldClause clause, int count) => clause.Type != SatGoto || clause.Literal(0) && clause.Values[0] >= 0 && clause.Values[0] <= count;

    /// <summary>Entries of a script that a run can start at, found from its roots: line 0 for an
    /// attachment, the literal line of a call, the line after a reached dialog action (a DLG
    /// response of 0). Responses of −N resume at a line the DLG names and are not modelled.</summary>
    internal sealed record CsLive(Dictionary<int, SortedSet<int>> Entries, Dictionary<int, (bool[] True, bool[] False)> Reach, int CallEx, int Computed, int MissingRoots, int MissingTargets);

    internal static CsLive CsLiveSet(IReadOnlyDictionary<int, WorldScript> scripts, IEnumerable<int> roots)
    {
        var entries = new Dictionary<int, SortedSet<int>>(); var reach = new Dictionary<int, (bool[] True, bool[] False)>();
        var work = new Queue<(int Script, int Line)>(); var missingRoots = new HashSet<int>(); var missing = new HashSet<int>();
        void Add(int script, int line, bool root = false)
        {
            if (!scripts.TryGetValue(script, out var s)) { (root ? missingRoots : missing).Add(script); return; }
            if (line < 0 || line >= s.Lines.Count) return;
            if (!entries.TryGetValue(script, out var set)) entries[script] = set = new SortedSet<int>();
            if (set.Add(line)) work.Enqueue((script, line));
        }
        foreach (var root in roots) Add(root, 0, root: true);
        var callEx = 0; var computed = 0; var countedSteps = new HashSet<(int, int)>(); var countedCallEx = new HashSet<(int, int, bool)>();
        while (work.TryDequeue(out var next))
        {
            var script = scripts[next.Script];
            var flow = AnalyseWorldFlow(script, next.Line);
            if (!reach.TryGetValue(next.Script, out var r)) reach[next.Script] = r = (new bool[script.Lines.Count], new bool[script.Lines.Count]);
            foreach (var u in flow.UnresolvedLines) if (countedSteps.Add((next.Script, u))) computed++;
            for (var line = 0; line < script.Lines.Count; line++)
                foreach (var (branch, reached) in new[] { (true, flow.True[line]), (false, flow.False[line]) })
                {
                    if (reached == WorldReach.Unreached) continue;
                    (branch ? r.True : r.False)[line] = true;
                    var clause = branch ? script.Lines[line].True : script.Lines[line].False;
                    if (clause.Type == SatDialog) Add(next.Script, line + 1);
                    if (clause.Type == SatCallEx && countedCallEx.Add((next.Script, line, branch))) callEx++;
                }
            foreach (var call in WorldCalls(script))
                if (call.Target is { } target && (call.Branch ? r.True : r.False)[call.Line]) Add(target, call.EntryLine ?? 0);
        }
        return new(entries, reach, callEx, computed, missingRoots.Count, missing.Count);
    }

    private static int ProbeScriptCensus(string[] args)
    {
        if (args.Length is not (2 or 4) || args[0] != "--game-root" || (args.Length == 4 && args[2] != "--module"))
            throw new ArgumentException("SCRIPT_CENSUS_USAGE --script-census --game-root PATH [--module NAME]");
        var discovery = ContentMountPlan.Discover(args[1], args.Length == 4 ? args[3] : ContentMountPlan.DefaultModule);
        if (discovery.Status != ContentMountStatus.Ready) throw new InvalidDataException("SCRIPT_CENSUS_MOUNT_" + discovery.Status);
        var opened = ContentStack.Open(discovery.Plan!);
        if (opened.Status != ContentStackStatus.Ready) throw new InvalidDataException("SCRIPT_CENSUS_STACK_" + opened.Status);
        using var stack = opened.Stack!;
        var keys = CensusKeys(stack);
        var refusals = new CensusTally();
        byte[]? Read(ContentCandidate candidate, string role)
        {
            var result = stack.Read(candidate);
            if (result.Status == ContentReadStatus.Read) return result.Bytes!;
            refusals.Add($"{role}|SCRIPT_CENSUS_READ_{result.Status}");
            return null;
        }

        // Scripts: scr/<digits>*.scr winners; a number with several names keeps the last in sorted order.
        var scriptKeys = keys.Keys.Where(k => k.StartsWith("scr/", StringComparison.Ordinal) && k.Count(c => c == '/') == 1 && k.EndsWith(".scr", StringComparison.Ordinal)).ToArray();
        var byNumber = scriptKeys.Select(k => (Key: k, Number: ScriptNumberFromFile(k[4..]))).Where(p => p.Number is > 0)
            .GroupBy(p => p.Number!.Value).ToDictionary(g => g.Key, g => g.Select(p => p.Key).Order(StringComparer.Ordinal).ToArray());
        var scripts = new SortedDictionary<int, WorldScript>(); var failures = new CensusTally(); var flagBits = new CensusTally();
        foreach (var (number, names) in byNumber)
        {
            if (Read(keys[names[^1]], "scr") is not { } bytes) { failures.Add("read"); continue; }
            try { scripts[number] = DecodeWorldScript(bytes); }
            catch (InvalidDataException e) { failures.Add(e.Message); continue; }
            for (var bit = 0; bit < 32; bit++) if ((scripts[number].Flags & (1u << bit)) != 0) flagBits.Add(bit.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        var trapScripts = scripts.Where(p => (p.Value.Flags & ScriptTrapFlags) != 0).Select(p => p.Key).ToHashSet();

        // Attachments: every nonzero script on every object, prototype, tile and sector.
        var walk = SaWalk(keys, Read, s => s != 0, refusals);
        static string Source(string kind) => kind.EndsWith("-tile", StringComparison.Ordinal) ? "tile" : kind.EndsWith("-script", StringComparison.Ordinal) ? "sector" : kind;
        var bySap = Enumerable.Range(0, CsAttachmentPoints).Select(sap =>
        {
            var all = walk.Hits.Where(h => h.Sap == sap).ToArray();
            // The aggregate is the normal-game form of the editor mobiles (ARC-RES-036), so it is
            // a cross-check, not more objects; prototypes are the defaults those objects inherit.
            var rows = all.Where(h => h.Kind is not "aggregate" and not "prototype").ToArray();
            var mobiles = all.Where(h => h.Kind == "mobile").Select(h => h.Script).Order().ToArray();
            var aggregated = all.Where(h => h.Kind == "aggregate").Select(h => h.Script).Order().ToArray();
            return new
            {
                sap, rows = rows.Length, prototype_defaults = all.Count(h => h.Kind == "prototype"), aggregate_matches_mobiles = mobiles.SequenceEqual(aggregated), scripts = all.Select(h => h.Script).Distinct().Count(),
                by_source = rows.GroupBy(h => h.Kind).OrderBy(g => g.Key, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count()),
                missing_scripts = all.Select(h => h.Script).Distinct().Count(s => !scripts.ContainsKey(s)),
                trap_scripts = all.Select(h => h.Script).Distinct().Count(trapScripts.Contains),
            };
        }).ToArray();
        object Placed(string source)
        {
            var rows = walk.Hits.Where(h => h.Sap < 0 && Source(h.Kind) == source).ToArray();
            return new
            {
                rows = rows.Length, scripts = rows.Select(h => h.Script).Distinct().Count(),
                by_source = rows.GroupBy(h => h.Kind).OrderBy(g => g.Key, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count()),
                missing_scripts = rows.Select(h => h.Script).Distinct().Count(s => !scripts.ContainsKey(s)),
            };
        }
        var outOfRangeSap = walk.Hits.Count(h => h.Sap >= CsAttachmentPoints);
        // Roots: every interpreted attachment. The dialog-override slot names a DLG, not a script
        // to run; trap scripts are handled by trap code, not interpreted.
        var roots = walk.Hits.Where(h => h.Sap != CsDialogOverridePoint && h.Sap < CsAttachmentPoints && !trapScripts.Contains(h.Script)).Select(h => h.Script).ToHashSet();
        var live = CsLiveSet(scripts, roots);

        // Opcode and operand tallies over every line (all) and over reached branches (live).
        var conditions = new CensusTally(); var actions = new CensusTally(); var operands = new CensusTally(); var ranges = new CensusTally();
        var conditionScripts = new Dictionary<(string, int), HashSet<int>>(); var actionScripts = new Dictionary<(string, int), HashSet<int>>();
        var gotos = new CensusTally(); var deadFalse = 0;
        foreach (var (number, script) in scripts)
        {
            live.Reach.TryGetValue(number, out var reach);
            for (var line = 0; line < script.Lines.Count; line++)
            {
                var row = script.Lines[line];
                var lineLive = reach.True is not null && (reach.True[line] || reach.False[line]);
                foreach (var scope in lineLive ? new[] { "all", "live" } : ["all"])
                {
                    Tally(scope, true, row.Condition, number, conditions, conditionScripts);
                    var branches = row.Condition.Type == 0 ? new[] { (true, row.True) } : [(true, row.True), (false, row.False)];
                    foreach (var (branch, clause) in branches)
                        if (scope == "all" || (branch ? reach.True![line] : reach.False![line])) { Tally(scope, false, clause, number, actions, actionScripts); Goto(scope, clause, script.Lines.Count); }
                }
                if (row.Condition.Type == 0 && row.False.Type != 0) deadFalse++;
            }
        }
        void Tally(string scope, bool condition, WorldClause clause, int number, CensusTally types, Dictionary<(string, int), HashSet<int>> users)
        {
            var known = clause.Type >= 0 && clause.Type < (condition ? CsConditionTypes : CsActionTypes);
            types.Add($"{scope}|{(known ? clause.Type : "unknown")}");
            if (!users.TryGetValue((scope, clause.Type), out var set)) users[(scope, clause.Type)] = set = [];
            set.Add(number);
            var side = condition ? "condition" : "action"; var dirty = false;
            foreach (var (slot, role, kind, value) in CsOperands(condition, clause))
            {
                if (role == 'n') operands.Add($"{scope}|value|{(kind < CsValueKinds ? kind : "invalid")}");
                else if (role == 'o') operands.Add($"{scope}|focus|{(kind < CsFocusKinds ? kind : "invalid")}");
                else
                {
                    if (kind != 0) operands.Add($"{scope}|extra_slot_kind_nonzero|{side}");
                    if (value != 0) operands.Add($"{scope}|extra_slot_value_nonzero|{side}");
                    dirty |= kind != 0 || value != 0;
                }
                if (role == 'n') Range(scope, kind, value);
                if (role == 'o' && kind == 11 && value is < 0 or >= CsLocalSlots) ranges.Add($"{scope}|local_object_index");
            }
            // The S1 writes (arithmetic and get-story-state) store into operand 0; a number literal there is not writable.
            if (!condition && clause.Type is 10 or 11 or 12 or 13 or 14 or 42 && clause.Kinds[0] == LiteralKind) ranges.Add($"{scope}|write_to_number_literal");
            if (dirty) operands.Add($"{scope}|clauses_with_extra_bytes|{side}");
            operands.Add($"{scope}|clauses|{side}");
            // Flag and counter indices of the local-flag operations are values, usually literals.
            if ((condition && clause.Type == 3 || !condition && clause.Type is 8 or 9) && clause.Kinds[0] == LiteralKind && clause.Values[0] is < 0 or >= CsLocalFlags) ranges.Add($"{scope}|local_flag_literal");
            if ((condition && clause.Type == 43 || !condition && clause.Type is 100 or 101) && clause.Kinds[0] == LiteralKind && clause.Values[0] is < 0 or >= CsGlobalFlags) ranges.Add($"{scope}|global_flag_literal");
            if ((condition && clause.Type is 3 or 43 || !condition && clause.Type is 8 or 9 or 100 or 101) && clause.Kinds[0] != LiteralKind) ranges.Add($"{scope}|computed_flag_index");
        }
        void Range(string scope, int kind, int value)
        {
            if (kind == 0 && value is < 0 or >= CsCounters) ranges.Add($"{scope}|counter_index");
            if (kind == 1 && value is < 0 or >= CsGlobalVars) ranges.Add($"{scope}|global_var_index");
            if (kind == 2 && value is < 0 or >= CsLocalSlots) ranges.Add($"{scope}|local_var_index");
            if (kind == 4 && value is < 0 or >= CsGlobalFlags) ranges.Add($"{scope}|global_flag_index");
            if (kind is 5 or 6 && (value >> 16) is < 0 or >= CsFocusKinds) ranges.Add($"{scope}|pc_focus");
        }
        void Goto(string scope, WorldClause clause, int count)
        {
            if (clause.Type != SatGoto) return;
            if (!clause.Literal(0)) gotos.Add($"{scope}|computed");
            else if (clause.Values[0] is >= -4 and <= -1) gotos.Add($"{scope}|control_code_{-clause.Values[0]}");
            else if (clause.Values[0] == count) gotos.Add($"{scope}|past_last_line");
            else if (clause.Values[0] < 0) gotos.Add($"{scope}|negative");
            else if (clause.Values[0] > count) gotos.Add($"{scope}|beyond_end");
            else gotos.Add($"{scope}|in_range");
        }
        object[] Types(int count, bool condition, CensusTally tally, Dictionary<(string, int), HashSet<int>> users) => Enumerable.Range(0, count)
            .Select(t => (t, all: tally[$"all|{t}"], live: tally[$"live|{t}"])).Where(x => x.all > 0)
            .Select(x => (object)new { type = x.t, signature = CsSignature(condition, x.t), all = x.all, live = x.live,
                scripts_all = users.GetValueOrDefault(("all", x.t))?.Count ?? 0, scripts_live = users.GetValueOrDefault(("live", x.t))?.Count ?? 0 }).ToArray();

        // S1 coverage: live scripts whose every reached clause is inside the candidate set.
        var firstRefusal = new CensusTally(); var admitted = new HashSet<int>();
        foreach (var (number, (reachTrue, reachFalse)) in live.Reach)
        {
            string? refusal = null;
            var script = scripts[number];
            for (var line = 0; line < script.Lines.Count && refusal is null; line++)
            {
                if (!reachTrue[line] && !reachFalse[line]) continue;
                var row = script.Lines[line];
                refusal = CsS1Refusal(true, row.Condition) ?? (reachTrue[line] ? CsS1Refusal(false, row.True) : null) ?? (reachFalse[line] ? CsS1Refusal(false, row.False) : null);
                if (refusal is null && reachTrue[line] && !CsS1GotoOk(row.True, script.Lines.Count)) refusal = "goto_target";
                if (refusal is null && reachFalse[line] && !CsS1GotoOk(row.False, script.Lines.Count)) refusal = "goto_target";
            }
            if (refusal is null) admitted.Add(number); else firstRefusal.Add(refusal);
        }
        // Whole-script admission (rule P7): every line, the false branch only where the condition can fail.
        var wholeAdmitted = scripts.Where(p => p.Value.Lines.All(row =>
            CsS1Refusal(true, row.Condition) is null && CsS1Refusal(false, row.True) is null && CsS1GotoOk(row.True, p.Value.Lines.Count) &&
            (row.Condition.Type == 0 || CsS1Refusal(false, row.False) is null && CsS1GotoOk(row.False, p.Value.Lines.Count)))).Select(p => p.Key).ToHashSet();
        var admittedBySap = Enumerable.Range(0, CsAttachmentPoints).Select(sap => (sap, scripts: walk.Hits.Where(h => h.Sap == sap).Select(h => h.Script).Distinct().ToArray()))
            .Where(x => x.scripts.Length > 0).Select(x => new { x.sap, attached = x.scripts.Length, admitted = x.scripts.Count(admitted.Contains) }).ToArray();
        var placedScripts = walk.Hits.Where(h => h.Sap < 0).Select(h => h.Script).Distinct().ToArray();

        var report = new
        {
            module = stack.Plan.ModuleName, keys = keys.Count,
            scripts = new
            {
                names = scriptKeys.Length, numbers = byNumber.Count, numbers_with_several_names = byNumber.Count(p => p.Value.Length > 1), decoded = scripts.Count,
                failures = failures.Snapshot(), lines = scripts.Values.Sum(s => s.Lines.Count), max_lines = scripts.Values.Select(s => s.Lines.Count).DefaultIfEmpty().Max(),
                empty = scripts.Values.Count(s => s.Lines.Count == 0), capacity_above_count = scripts.Values.Count(s => s.Capacity > s.Lines.Count),
                file_flag_bits = flagBits.Snapshot(), trap_flagged = trapScripts.Count,
            },
            scanned = new { maps = walk.Maps, sectors = walk.Sectors, mobiles = walk.Mobiles, aggregates = walk.Aggregates, terrain_templates = walk.Templates, prototypes = walk.Prototypes.Count, unresolved_prototypes = walk.UnresolvedPrototypes },
            attachments = new
            {
                by_point = bySap.Where(x => x.scripts > 0).ToArray(), points_without_scripts = bySap.Where(x => x.scripts == 0).Select(x => x.sap).ToArray(),
                out_of_range_point_rows = outOfRangeSap, tile = Placed("tile"), sector = Placed("sector"),
                attached_scripts = walk.Hits.Select(h => h.Script).Distinct().Count(), roots = roots.Count,
            },
            live = new
            {
                scripts = live.Entries.Count, entries = live.Entries.Values.Sum(e => e.Count), entries_other_than_line_0 = live.Entries.Values.Sum(e => e.Count(l => l != 0)),
                lines = live.Reach.Values.Sum(r => r.True.Zip(r.False).Count(p => p.First || p.Second)),
                unreached_decoded_scripts = scripts.Count - live.Reach.Count, call_ex_sites = live.CallEx, computed_or_unresolved_steps = live.Computed, missing_roots = live.MissingRoots, missing_call_targets = live.MissingTargets,
            },
            conditions = new { types = Types(CsConditionTypes, true, conditions, conditionScripts), unknown_all = conditions["all|unknown"], unknown_live = conditions["live|unknown"],
                unused_types = Enumerable.Range(0, CsConditionTypes).Where(t => conditions[$"all|{t}"] == 0).ToArray() },
            actions = new { types = Types(CsActionTypes, false, actions, actionScripts), unknown_all = actions["all|unknown"], unknown_live = actions["live|unknown"],
                unused_types = Enumerable.Range(0, CsActionTypes).Where(t => actions[$"all|{t}"] == 0).ToArray(), false_branch_of_always_true_nonzero = deadFalse },
            operands = operands.Snapshot(), ranges = ranges.Snapshot(), gotos = gotos.Snapshot(),
            s1_candidate = new
            {
                conditions = CsS1Conditions, actions = CsS1Actions, value_kinds = CsS1ValueKinds,
                live_scripts = live.Reach.Count, admitted_live_scripts = admitted.Count, admitted_whole_scripts = wholeAdmitted.Count, admitted_whole_live_scripts = wholeAdmitted.Count(live.Reach.ContainsKey), first_refusal = firstRefusal.Snapshot().OrderByDescending(p => p.Value).ThenBy(p => p.Key, StringComparer.Ordinal).Take(40).ToDictionary(p => p.Key, p => p.Value),
                by_point = admittedBySap, placed = new { attached = placedScripts.Length, admitted = placedScripts.Count(admitted.Contains) },
            },
            refusals = refusals.Snapshot(),
        };
        Console.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }

    private static void ScriptCensusSelfTest()
    {
        static void Check(bool condition, string label) { if (!condition) throw new InvalidOperationException("script-census self-test failed: " + label); }
        Check(CsConditionSignatures.Length == CsConditionTypes && CsActionSignatures.Length == CsActionTypes, "signature table sizes");
        Check(CsSignature(false, SatTeleport) == "onnn" && CsSignature(false, SatFadeTeleport) == "nnnonnn" && CsSignature(false, SatCallEx) == "onno" && CsSignature(false, SatCall) == "nnoo", "signatures match the probe's earlier operand readings");
        Check(CsSignature(true, 0) == "" && CsSignature(true, 52) == "o" && CsSignature(false, 141) == "onnn" && CsSignature(false, 142) == "?", "signature bounds");

        // Synthetic script: 0 if LF 3 goto 3 else dialog 7; 1 return-run (dead); 2 call 9 line 1; 3 set GF 5; 4 return-skip.
        static byte[] Script(params (int Cond, int[] CondOps, int Yes, int[] YesOps, int No, int[] NoOps)[] lines)
        {
            var bytes = new byte[WorldScriptHeaderBytes + lines.Length * WorldScriptLineBytes];
            System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(52), lines.Length);
            System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(56), lines.Length);
            for (var i = 0; i < lines.Length; i++)
            {
                var at = WorldScriptHeaderBytes + i * WorldScriptLineBytes;
                foreach (var (offset, type, ops) in new[] { (0, lines[i].Cond, lines[i].CondOps), (44, lines[i].Yes, lines[i].YesOps), (88, lines[i].No, lines[i].NoOps) })
                {
                    System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(at + offset), type);
                    for (var k = 0; k < ops.Length; k++) { bytes[at + offset + 4 + k] = LiteralKind; System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(at + offset + 12 + 4 * k), ops[k]); }
                }
            }
            return bytes;
        }
        var root = DecodeWorldScript(Script((3, [3], SatGoto, [3], SatDialog, [7]), (0, [], SatReturnRun, [], 0, []), (0, [], SatCall, [9, 1], 0, []), (0, [], 100, [5], 0, []), (0, [], SatReturnSkip, [], 0, [])));
        var callee = DecodeWorldScript(Script((0, [], SatReturnRun, [], 0, []), (43, [5], SatReturnSkip, [], SatReturnRun, [])));
        var set = new Dictionary<int, WorldScript> { [1] = root, [9] = callee };
        var live = CsLiveSet(set, [1, 4]);
        Check(live.Entries[1].SequenceEqual([0, 1]) && live.MissingRoots == 1 && live.MissingTargets == 0, "entries: root line 0, line after the dialog action; a missing root counted");
        Check(live.Reach[1].True[0] && live.Reach[1].False[0] && live.Reach[1].True[1] && !live.Reach[1].True[2] && live.Reach[1].True[3], "reach: goto skips line 2; dialog resume reaches line 1");
        Check(!live.Entries.ContainsKey(9), "a call on an unreached line adds no entry");
        var called = CsLiveSet(new Dictionary<int, WorldScript> { [1] = DecodeWorldScript(Script((0, [], SatCall, [9, 1], 0, []))), [9] = callee }, [1]);
        Check(called.Entries[9].SequenceEqual([1]) && called.Reach[9].True[1] && called.Reach[9].False[1] && !called.Reach[9].True[0], "a reached call enters the callee at its literal line");
        Check(CsS1Refusal(true, root.Lines[0].Condition) is null && CsS1Refusal(false, root.Lines[0].False) == "action:4" && CsS1Refusal(false, root.Lines[3].True) is null, "S1 candidate: LF and GF admitted, dialog refused");
        var extra = root.Lines[3].True with { Kinds = [3, 3, 0, 0, 0, 0, 0, 0], Values = [5, 1, 0, 0, 0, 0, 0, 0] };
        Check(CsS1Refusal(false, extra) is null, "an operand past the signature is not read");
        Check(CsS1GotoOk(root.Lines[0].True, 5) && CsS1GotoOk(root.Lines[0].True with { Values = [5, 0, 0, 0, 0, 0, 0, 0] }, 5) &&
            !CsS1GotoOk(root.Lines[0].True with { Values = [6, 0, 0, 0, 0, 0, 0, 0] }, 5) && !CsS1GotoOk(root.Lines[0].True with { Values = [-2, 0, 0, 0, 0, 0, 0, 0] }, 5) &&
            !CsS1GotoOk(root.Lines[0].True with { Kinds = [2, 0, 0, 0, 0, 0, 0, 0] }, 5), "goto rule P6: inside or exactly the end, literal only");
        Check(CsOperands(false, root.Lines[2].True).Count(o => o.Role == 'o') == 2, "call takes two objects");
        var truncated = Script((0, [], SatReturnRun, [], 0, []))[..^1];
        try { DecodeWorldScript(truncated); Check(false, "framing refusal"); } catch (InvalidDataException) { }
        Console.WriteLine("script-census self-test: PASS (signature tables, live entries and reach, S1 candidate refusals, framing)");
    }
}
