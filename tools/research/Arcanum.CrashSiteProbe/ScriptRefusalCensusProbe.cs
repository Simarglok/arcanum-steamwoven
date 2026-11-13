using System.Text.Json;
using Arcanum.Data.Content;
using Arcanum.Data.Scripts;
using Arcanum.Scripting.Scr;

namespace Arcanum.CrashSiteProbe;

// ARC-RES-1101 research only (plan slice E5-r of docs/project/missing-systems-plan.md): which condition and action
// types the widest admitted set (ScrAdmissionSet.S33) still refuses, how much installed content each one blocks, on
// which maps, attachment points and quests, and in which order admitting them frees the most. It decodes and checks
// admission, it never executes. The admission verdicts come from the production ScrAdmission, so the census cannot
// drift from the dispatcher; the set under test is a probe argument (default S33).
// A script is blocked for a root (an attached script, run from line 0) when any clause the run can reach, calls
// followed, is outside the set: the same live set as ARC-RES-045 (CsLiveSet). A clause type is the *sole blocker* of a
// root when it is the only refused type that root reaches and no other admission refusal (value or focus kind, index,
// goto) is left once every type is admitted.
// Only numbers and map names leave the probe (no script text, no object names, no payload).
internal static partial class Program
{
    private static readonly string[] RcSetNames = ["S30", "S31", "S32", "S33", "S38", "S43", "S52", "S53", "S54", "S56"];

    private static ScrAdmissionSet? RcNamedSet(string name) => name switch
    {
        "S30" => ScrAdmissionSet.S30, "S31" => ScrAdmissionSet.S31, "S32" => ScrAdmissionSet.S32, "S33" => ScrAdmissionSet.S33, "S38" => ScrAdmissionSet.S38, "S43" => ScrAdmissionSet.S43, "S52" => ScrAdmissionSet.S52, "S53" => ScrAdmissionSet.S53, "S54" => ScrAdmissionSet.S54, "S56" => ScrAdmissionSet.S56, _ => null,
    };

    /// <summary>The set under test with every catalogued condition and action type admitted, and its own value kinds, focus kinds and counter rule kept, so what the production
    /// admission still refuses is not a type refusal (a value or focus kind outside the set, an index, a goto).</summary>
    internal static ScrAdmissionSet RcTypesOpen(ScrAdmissionSet set) => new(set.Id + "+types", Enumerable.Range(0, CsConditionTypes), Enumerable.Range(0, CsActionTypes),
        set.ValueKinds, set.FocusKinds, set.CounterIndexWraps);

    /// <summary>A refused clause type: the side and the type number.</summary>
    internal readonly record struct RcType(bool Condition, int Type);

    /// <summary>What one root script (run from line 0, calls followed) needs: the refused types it reaches, the quest numbers its
    /// reached scripts name, how many scripts it reaches, and the admission refusals that stay when every type is admitted.</summary>
    internal sealed record RcRoot(int Script, HashSet<RcType> Refused, SortedSet<int> Quests, int Reached, HashSet<string> Other);

    /// <summary>A literal quest number the reached lines name: operand 1 of "pc quest state" (condition 6) and of "set pc quest state" (action 16).</summary>
    internal static IEnumerable<int> RcQuests(WorldScript script, (bool[] True, bool[] False) reach)
    {
        for (var line = 0; line < script.Lines.Count; line++)
        {
            var row = script.Lines[line];
            if (!(reach.True[line] || reach.False[line])) continue;
            if (row.Condition.Type == ScrCatalogue.Condition.PcQuestState && row.Condition.Literal(1)) yield return row.Condition.Values[1];
            foreach (var (taken, clause) in new[] { (reach.True[line], row.True), (reach.False[line], row.False) })
                if (taken && clause.Type == ScrCatalogue.Action.SetPcQuestState && clause.Literal(1)) yield return clause.Values[1];
        }
    }

    /// <summary>The refused condition and action types on the reached lines of one live set under <paramref name="set"/>.</summary>
    internal static IEnumerable<RcType> RcRefusedTypes(IReadOnlyDictionary<int, WorldScript> scripts, CsLive live, ScrAdmissionSet set)
    {
        foreach (var (number, reach) in live.Reach)
        {
            var script = scripts[number];
            for (var line = 0; line < script.Lines.Count; line++)
            {
                if (!(reach.True[line] || reach.False[line])) continue;
                var row = script.Lines[line];
                if (!set.Conditions.Contains(row.Condition.Type)) yield return new(true, row.Condition.Type);
                if (reach.True[line] && !set.Actions.Contains(row.True.Type)) yield return new(false, row.True.Type);
                if (reach.False[line] && !set.Actions.Contains(row.False.Type)) yield return new(false, row.False.Type);
            }
        }
    }

    internal static RcRoot RcAnalyseRoot(IReadOnlyDictionary<int, WorldScript> scripts, IReadOnlyDictionary<int, ScrProgram> programs, int root, ScrAdmissionSet set)
    {
        var live = CsLiveSet(scripts, [root]);
        var quests = new SortedSet<int>(live.Reach.SelectMany(p => RcQuests(scripts[p.Key], p.Value)));
        var open = RcTypesOpen(set);
        var other = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (number, entries) in live.Entries)
        {
            if (!programs.TryGetValue(number, out var program)) { other.Add("no_program"); continue; }
            foreach (var entry in entries)
                if (ScrAdmission.AdmitFrom(program, open, entry).Refusal is { } refusal)
                    other.Add(refusal.Code is ScrRefusalCode.FocusKindNotAdmitted or ScrRefusalCode.ValueKindNotAdmitted && refusal.Kind is { } kind ? $"{refusal.Code}:{kind}" : refusal.Code.ToString());
        }
        return new(root, new HashSet<RcType>(RcRefusedTypes(scripts, live, set)), quests, live.Reach.Count, other);
    }

    /// <summary>One attached root and where it sits: map, kind of holder and attachment point (negative for tile and sector scripts).</summary>
    internal sealed record RcSite(string Map, string Kind, int Sap, int Script, int? ObjectType);

    internal static string RcKey(RcType type) => (type.Condition ? "c" : "a") + type.Type.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>The slices of the missing-systems plan the refused types fall to (E5-a, E5-b, E5-c, E5-e of docs/project/missing-systems-plan.md), the media slice E7-b,
    /// the spell hooks E4-g and E4-j, and three groups the plan does not name (vitals, critter state, scene queries), keyed "c" or "a" and the type number. Each
    /// type of the catalogue is in at most one group; the report lists every refused type that is in none.</summary>
    internal static readonly (string Id, string Label, string[] Types)[] RcGroups =
    [
        ("E5-a", "NPC state actions", ["a99", "a59", "a45", "a46", "a140", "a141", "a22", "a30", "a31"]),
        ("E5-b", "World-object actions and queries", ["a66", "a41", "a53", "a88", "a106", "c45", "c36", "a126", "a127", "a123", "a94", "a110", "c46"]),
        ("E5-c", "Story, time, reward, blessing and curse", ["c7", "a17", "c40", "c41", "a51", "c1", "a97", "a105", "a63", "a117", "a119", "a25", "a26", "a27", "a28", "c8", "c9"]),
        ("E5-e", "Loop break", ["a20"]),
        ("E7-b", "Media: fade, end slides, newspapers", ["a103", "a132", "a85", "a86"]),
        ("E4-g", "Spell effects and spell-state queries", ["a104", "a124", "a130", "a80", "c12", "c13", "c16"]),
        ("VIT", "Vitals: hit points, fatigue, poison, armor, stat adjust (no plan slice)", ["a107", "a108", "a128", "a32", "a111", "a70", "a137"]),
        ("CRQ", "Critter state and social queries (no plan slice)", ["c10", "c11", "c15", "c19", "c25", "c26", "c28", "c29", "c30", "c31", "c34", "c37", "c38", "c51", "a38", "a39", "a89", "a92", "a57"]),
        ("SCN", "Scene queries: location, scroll distance (no plan slice)", ["a95", "a90"]),
    ];

    /// <summary>Greedy order over named needs (a type or a group): each step adds the need that frees the most placed sites (roots whose needs are all chosen and that have no
    /// other refusal); a tie goes to more freed roots, then to more unfreed roots the need takes part in, then to the lower name. Roots that never free (an other refusal) are left out.</summary>
    internal static IReadOnlyList<(string Need, int FreedSites, int FreedRoots)> RcGreedy(IReadOnlyDictionary<int, HashSet<string>> needs, ISet<int> neverFree, IReadOnlyDictionary<int, int> sitesOfRoot, int limit)
    {
        var chosen = new HashSet<string>(StringComparer.Ordinal); var freed = new HashSet<int>(); var order = new List<(string, int, int)>();
        var candidates = needs.Where(p => p.Value.Count > 0 && !neverFree.Contains(p.Key)).ToArray();
        var pool = candidates.SelectMany(p => p.Value).Distinct().ToList();
        while (order.Count < limit && pool.Count > 0 && freed.Count < candidates.Length)
        {
            string? best = null; var bestSites = -1; var bestRoots = 0; var bestPending = -1;
            foreach (var need in pool.Order(StringComparer.Ordinal))
            {
                var gained = candidates.Where(p => !freed.Contains(p.Key) && p.Value.All(x => chosen.Contains(x) || x == need)).ToArray();
                var sites = gained.Sum(p => sitesOfRoot.GetValueOrDefault(p.Key));
                var pending = candidates.Count(p => !freed.Contains(p.Key) && p.Value.Contains(need));
                if (sites > bestSites || sites == bestSites && (gained.Length > bestRoots || gained.Length == bestRoots && pending > bestPending))
                { best = need; bestSites = sites; bestRoots = gained.Length; bestPending = pending; }
            }
            if (best is not { } pick) break;
            chosen.Add(pick); pool.Remove(pick);
            foreach (var p in candidates.Where(p => !freed.Contains(p.Key) && p.Value.All(chosen.Contains))) freed.Add(p.Key);
            order.Add((pick, bestSites, bestRoots));
        }
        return order;
    }

    private static int ProbeScriptRefusalCensus(string[] args)
    {
        var setName = "S33";
        if (args.Length is not (2 or 4 or 6) || args[0] != "--game-root") throw new ArgumentException("SCRIPT_REFUSAL_CENSUS_USAGE --script-refusal-census --game-root PATH [--module NAME] [--set S30|S31|S32|S33|S38|S43|S52|S53|S54|S56]");
        var module = ContentMountPlan.DefaultModule;
        for (var i = 2; i < args.Length; i += 2)
        {
            if (args[i] == "--module") module = args[i + 1];
            else if (args[i] == "--set") setName = args[i + 1];
            else throw new ArgumentException("SCRIPT_REFUSAL_CENSUS_USAGE --script-refusal-census --game-root PATH [--module NAME] [--set S30|S31|S32|S33|S38|S43|S52|S53|S54|S56]");
        }
        var set = RcNamedSet(setName) ?? throw new ArgumentException("SCRIPT_REFUSAL_CENSUS_SET");
        var discovery = ContentMountPlan.Discover(args[1], module);
        if (discovery.Status != ContentMountStatus.Ready) throw new InvalidDataException("SCRIPT_REFUSAL_CENSUS_MOUNT_" + discovery.Status);
        var opened = ContentStack.Open(discovery.Plan!);
        if (opened.Status != ContentStackStatus.Ready) throw new InvalidDataException("SCRIPT_REFUSAL_CENSUS_STACK_" + opened.Status);
        using var stack = opened.Stack!;
        var keys = CensusKeys(stack);
        var refusals = new CensusTally();
        byte[]? Read(ContentCandidate candidate, string role)
        {
            var result = stack.Read(candidate);
            if (result.Status == ContentReadStatus.Read) return result.Bytes!;
            refusals.Add($"{role}|SCRIPT_REFUSAL_CENSUS_READ_{result.Status}");
            return null;
        }

        // Scripts, as ARC-RES-045 reads them: scr/<digits>*.scr winners, the last name of a number in sorted order.
        var byNumber = keys.Keys.Where(k => k.StartsWith("scr/", StringComparison.Ordinal) && k.Count(c => c == '/') == 1 && k.EndsWith(".scr", StringComparison.Ordinal))
            .Select(k => (Key: k, Number: ScriptNumberFromFile(k[4..]))).Where(p => p.Number is > 0)
            .GroupBy(p => p.Number!.Value).ToDictionary(g => g.Key, g => g.Select(p => p.Key).Order(StringComparer.Ordinal).Last());
        var scripts = new SortedDictionary<int, WorldScript>(); var programs = new Dictionary<int, ScrProgram>(); var decodeFailures = new CensusTally();
        foreach (var (number, key) in byNumber)
        {
            if (Read(keys[key], "scr") is not { } bytes) { decodeFailures.Add("read"); continue; }
            try { scripts[number] = DecodeWorldScript(bytes); } catch (InvalidDataException e) { decodeFailures.Add(e.Message); continue; }
            var read = ScrProgram.Read(bytes);
            if (read.Program is { } program) programs[number] = program; else decodeFailures.Add("program_" + read.Status);
        }
        var trapScripts = scripts.Where(p => (p.Value.Flags & ScriptTrapFlags) != 0).Select(p => p.Key).ToHashSet();

        var walk = SaWalk(keys, Read, s => s != 0, refusals);
        var interpreted = walk.Hits.Where(h => h.Sap != CsDialogOverridePoint && h.Sap < CsAttachmentPoints && !trapScripts.Contains(h.Script)).ToArray();
        // Placed sites: an object, tile or sector holding the script. Aggregates repeat the mobiles and prototype rows are inherited defaults.
        var placed = interpreted.Where(h => h.Kind is not "aggregate" and not "prototype").Select(h => new RcSite(h.Map, h.Kind, h.Sap, h.Script, h.Type)).ToArray();
        var protoOnly = interpreted.Where(h => h.Kind == "prototype").Select(h => h.Script).Distinct().Where(s => placed.All(p => p.Script != s)).Count();
        var rootNumbers = interpreted.Select(h => h.Script).Distinct().Order().ToArray();
        var roots = rootNumbers.Where(scripts.ContainsKey).ToDictionary(r => r, r => RcAnalyseRoot(scripts, programs, r, set));
        var sitesOfRoot = placed.GroupBy(p => p.Script).ToDictionary(g => g.Key, g => g.Count());

        // Global live use of each refused type (all roots together, as ARC-RES-045): clauses, scripts.
        var live = CsLiveSet(scripts, rootNumbers);
        var uses = new Dictionary<RcType, (int Clauses, HashSet<int> Scripts)>();
        foreach (var (number, reach) in live.Reach)
        {
            var script = scripts[number];
            for (var line = 0; line < script.Lines.Count; line++)
            {
                if (!(reach.True[line] || reach.False[line])) continue;
                var row = script.Lines[line];
                void Count(bool condition, int type, bool taken)
                {
                    if (!taken || (condition ? set.Conditions : set.Actions).Contains(type)) return;
                    var key = new RcType(condition, type);
                    if (!uses.TryGetValue(key, out var u)) uses[key] = u = (0, []);
                    uses[key] = (u.Clauses + 1, u.Scripts); u.Scripts.Add(number);
                }
                Count(true, row.Condition.Type, true); Count(false, row.True.Type, reach.True[line]); Count(false, row.False.Type, reach.False[line]);
            }
        }

        // Types outside the set that occur on some line but on no live line (a dead branch, an unreached script, a prototype-only script): all-line clause and script counts.
        var allUses = new Dictionary<RcType, (int Clauses, HashSet<int> Scripts)>();
        foreach (var (number, script) in scripts)
            foreach (var row in script.Lines)
            {
                void Count(bool condition, int type)
                {
                    if ((condition ? set.Conditions : set.Actions).Contains(type)) return;
                    var key = new RcType(condition, type);
                    if (!allUses.TryGetValue(key, out var u)) allUses[key] = u = (0, []);
                    allUses[key] = (u.Clauses + 1, u.Scripts); u.Scripts.Add(number);
                }
                Count(true, row.Condition.Type); Count(false, row.True.Type);
                if (row.Condition.Type != 0) Count(false, row.False.Type);
            }
        var neverLive = allUses.Where(p => !uses.ContainsKey(p.Key)).OrderByDescending(p => p.Value.Clauses).ThenBy(p => p.Key.Condition ? 0 : 1).ThenBy(p => p.Key.Type)
            .Select(p => new { side = p.Key.Condition ? "condition" : "action", type = p.Key.Type, all_clauses = p.Value.Clauses, all_scripts = p.Value.Scripts.Count }).ToArray();
        var rows = uses.Keys.OrderByDescending(k => uses[k].Clauses).ThenBy(k => k.Condition ? 0 : 1).ThenBy(k => k.Type).Select(type =>
        {
            var blocked = roots.Values.Where(r => r.Refused.Contains(type)).ToArray();
            var blockedSet = blocked.Select(r => r.Script).ToHashSet();
            var sites = placed.Where(p => blockedSet.Contains(p.Script)).ToArray();
            var sole = blocked.Where(r => r.Refused.Count == 1 && r.Other.Count == 0).Select(r => r.Script).ToHashSet();
            return new
            {
                side = type.Condition ? "condition" : "action", type = type.Type, live_clauses = uses[type].Clauses, live_scripts = uses[type].Scripts.Count,
                blocked_roots = blocked.Length, blocked_sites = sites.Length, sole_blocker_roots = sole.Count, sole_blocker_sites = placed.Count(p => sole.Contains(p.Script)),
                maps = sites.Select(s => s.Map).Where(m => m.Length > 0).Distinct().Count(),
                top_maps = sites.Where(s => s.Map.Length > 0).GroupBy(s => s.Map).OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal).Take(8)
                    .Select(g => new { map = g.Key, sites = g.Count() }).ToArray(),
                tile_or_sector_sites = sites.Count(s => s.Sap < 0),
                by_point = sites.Where(s => s.Sap >= 0).GroupBy(s => s.Sap).OrderBy(g => g.Key).ToDictionary(g => g.Key.ToString(System.Globalization.CultureInfo.InvariantCulture), g => g.Count()),
                quest_roots = blocked.Count(r => r.Quests.Count > 0),
                top_quests = blocked.SelectMany(r => r.Quests).GroupBy(q => q).OrderByDescending(g => g.Count()).ThenBy(g => g.Key).Take(8).Select(g => new { quest = g.Key, roots = g.Count() }).ToArray(),
            };
        }).ToArray();

        var admittedRoots = roots.Values.Where(r => r.Refused.Count == 0 && r.Other.Count == 0).ToArray();
        var otherOnly = roots.Values.Where(r => r.Other.Count > 0).ToArray();
        var otherCodes = new CensusTally();
        foreach (var r in roots.Values) foreach (var code in r.Other) otherCodes.Add(code);
        var otherSites = roots.Values.SelectMany(r => r.Other.Select(code => (code, r.Script))).GroupBy(x => x.code).OrderBy(g => g.Key, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => new { roots = g.Count(), sites = placed.Count(p => g.Any(x => x.Script == p.Script)) });
        var neverFree = roots.Values.Where(r => r.Other.Count > 0).Select(r => r.Script).ToHashSet();
        var typeNeeds = roots.ToDictionary(p => p.Key, p => p.Value.Refused.Select(RcKey).ToHashSet(StringComparer.Ordinal));
        var groupOf = RcGroups.SelectMany(g => g.Types.Select(t => (Type: t, g.Id))).ToDictionary(x => x.Type, x => x.Id, StringComparer.Ordinal);
        var groupNeeds = typeNeeds.ToDictionary(p => p.Key, p => p.Value.Select(t => groupOf.GetValueOrDefault(t, "UNGROUPED:" + t)).ToHashSet(StringComparer.Ordinal));
        object Order(IReadOnlyList<(string Need, int FreedSites, int FreedRoots)> greedy)
        {
            var cumulativeSites = 0; var cumulativeRoots = 0;
            return greedy.Select((g, i) =>
            {
                cumulativeSites += g.FreedSites; cumulativeRoots += g.FreedRoots;
                return new { step = i + 1, need = g.Need, freed_sites = g.FreedSites, freed_roots = g.FreedRoots, cumulative_sites = cumulativeSites, cumulative_roots = cumulativeRoots };
            }).ToArray();
        }
        var order = Order(RcGreedy(typeNeeds, neverFree, sitesOfRoot, 40));
        var groupOrder = Order(RcGreedy(groupNeeds, neverFree, sitesOfRoot, 20));
        var groups = RcGroups.Select(g =>
        {
            var ids = g.Types.ToHashSet(StringComparer.Ordinal);
            var touching = typeNeeds.Where(p => p.Value.Overlaps(ids)).Select(p => p.Key).ToHashSet();
            var alone = touching.Where(r => typeNeeds[r].IsSubsetOf(ids) && !neverFree.Contains(r)).ToHashSet();
            var sites = placed.Where(p => touching.Contains(p.Script)).ToArray();
            return new
            {
                id = g.Id, label = g.Label, types = g.Types.Where(t => rows.Any(r => RcKey(new(r.side == "condition", r.type)) == t)).ToArray(), group_types_not_refused = g.Types.Count(t => rows.All(r => RcKey(new(r.side == "condition", r.type)) != t)),
                blocked_roots = touching.Count, blocked_sites = sites.Length, maps = sites.Select(p => p.Map).Where(m => m.Length > 0).Distinct().Count(),
                freed_alone_roots = alone.Count, freed_alone_sites = placed.Count(p => alone.Contains(p.Script)),
                freed_alone_maps = placed.Where(p => alone.Contains(p.Script) && p.Map.Length > 0).Select(p => p.Map).Distinct().Count(),
            };
        }).ToArray();
        var ungrouped = rows.Select(r => RcKey(new(r.side == "condition", r.type))).Where(t => !groupOf.ContainsKey(t)).ToArray();

        // Mapped by attachment point: how many placed sites of the point are blocked.
        var byPoint = placed.Where(p => p.Sap >= 0).GroupBy(p => p.Sap).OrderBy(g => g.Key).Select(g => new
        {
            point = g.Key, sites = g.Count(),
            blocked_sites = g.Count(p => roots.TryGetValue(p.Script, out var r) && (r.Refused.Count > 0 || r.Other.Count > 0)),
            top_types = g.Where(p => roots.ContainsKey(p.Script)).SelectMany(p => roots[p.Script].Refused).GroupBy(t => t).OrderByDescending(t => t.Count()).ThenBy(t => RcKey(t.Key), StringComparer.Ordinal).Take(6)
                .Select(t => new { type = RcKey(t.Key), sites = t.Count() }).ToArray(),
        }).ToArray();
        var byMap = placed.Where(p => p.Map.Length > 0).GroupBy(p => p.Map).OrderBy(g => g.Key, StringComparer.Ordinal).Select(g => new
        {
            map = g.Key, sites = g.Count(), blocked_sites = g.Count(p => roots.TryGetValue(p.Script, out var r) && (r.Refused.Count > 0 || r.Other.Count > 0)),
            blocked_types = g.Where(p => roots.ContainsKey(p.Script)).SelectMany(p => roots[p.Script].Refused).Distinct().Count(),
        }).OrderByDescending(m => m.blocked_sites).ThenBy(m => m.map, StringComparer.Ordinal).ToArray();
        var questTotals = roots.Values.Where(r => r.Refused.Count > 0).SelectMany(r => r.Quests.Select(q => (Quest: q, Root: r))).GroupBy(x => x.Quest)
            .OrderByDescending(g => g.Count()).ThenBy(g => g.Key).Take(20).Select(g => new
            {
                quest = g.Key, blocked_roots = g.Count(),
                types = g.SelectMany(x => x.Root.Refused).GroupBy(t => t).OrderByDescending(t => t.Count()).ThenBy(t => t.Key.Condition ? 0 : 1).ThenBy(t => t.Key.Type).Take(6)
                    .Select(t => (t.Key.Condition ? "c" : "a") + t.Key.Type.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToArray(),
            }).ToArray();

        // The blocked roots with the most placed sites: where one script stands on hundreds of objects, its refused types come first.
        var topRoots = roots.Values.Where(r => r.Refused.Count > 0 || r.Other.Count > 0).Select(r => (Root: r, Sites: placed.Where(p => p.Script == r.Script).ToArray()))
            .OrderByDescending(x => x.Sites.Length).ThenBy(x => x.Root.Script).Take(25).Select(x => new
            {
                script = x.Root.Script, sites = x.Sites.Length, maps = x.Sites.Select(p => p.Map).Where(m => m.Length > 0).Distinct().Count(),
                points = x.Sites.Where(p => p.Sap >= 0).Select(p => p.Sap).Distinct().Order().ToArray(), object_types = x.Sites.Select(p => p.ObjectType ?? -1).Distinct().Order().ToArray(), reached_scripts = x.Root.Reached,
                types = x.Root.Refused.OrderBy(t => t.Condition ? 0 : 1).ThenBy(t => t.Type).Select(t => (t.Condition ? "c" : "a") + t.Type.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToArray(),
                other = x.Root.Other.Order(StringComparer.Ordinal).ToArray(), quests = x.Root.Quests.Take(6).ToArray(),
            }).ToArray();

        var report = new
        {
            module = stack.Plan.ModuleName, set = set.Id, set_conditions = set.Conditions.Count, set_actions = set.Actions.Count, keys = keys.Count,
            scripts = new { decoded = scripts.Count, program_failures = decodeFailures.Snapshot(), trap_flagged = trapScripts.Count },
            scanned = new { maps = walk.Maps, sectors = walk.Sectors, mobiles = walk.Mobiles, aggregates = walk.Aggregates, terrain_templates = walk.Templates, prototypes = walk.Prototypes.Count },
            roots = new
            {
                attached = roots.Count, placed_sites = placed.Length, prototype_default_only_roots = protoOnly, missing_roots = rootNumbers.Length - roots.Count,
                admitted = admittedRoots.Length, admitted_sites = placed.Count(p => admittedRoots.Any(r => r.Script == p.Script)),
                refused_by_type = roots.Values.Count(r => r.Refused.Count > 0), refused_by_type_sites = placed.Count(p => roots.TryGetValue(p.Script, out var r) && r.Refused.Count > 0),
                blocked_roots_naming_quests = roots.Values.Count(r => r.Refused.Count > 0 && r.Quests.Count > 0),
                quests_named_by_two_or_more_blocked_roots = roots.Values.Where(r => r.Refused.Count > 0).SelectMany(r => r.Quests).GroupBy(q => q).Count(g => g.Count() >= 2),
                roots_with_type_and_other_refusal = roots.Values.Count(r => r.Refused.Count > 0 && r.Other.Count > 0),
                other_refusal_only = otherOnly.Count(r => r.Refused.Count == 0), other_refusal_codes = otherCodes.Snapshot(), other_refusals_with_sites = otherSites,
                reached_scripts_in_live = live.Reach.Count, live_lines = live.Reach.Values.Sum(r => r.True.Zip(r.False).Count(p => p.First || p.Second)),
            },
            refused_types = new { conditions = rows.Count(r => r.side == "condition"), actions = rows.Count(r => r.side == "action"), rows, never_live = neverLive },
            free_order = order, groups, group_order = groupOrder, ungrouped_types = ungrouped, top_blocked_roots = topRoots, by_point = byPoint, by_map = byMap, by_quest = questTotals, refusals = refusals.Snapshot(),
        };
        Console.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }

    private static void ScriptRefusalCensusSelfTest()
    {
        static void Check(bool condition, string label) { if (!condition) throw new InvalidOperationException("script-refusal-census self-test failed: " + label); }
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
        static (WorldScript World, ScrProgram Program) Decode(byte[] bytes) => (DecodeWorldScript(bytes), ScrProgram.Read(bytes).Program!);
        // Clause numbers used: 6 pc_quest_state (o n n) and 16 set pc quest state (o n n) name quests; 36 attack is admitted by S33,
        // 20 loop_break (20) and 85 (not in S33) and the condition 24 (not in S33) are refused.
        var admittedRoot = Decode(Script((0, [], 100, [5], 0, []), (0, [], SatReturnSkip, [], 0, [])));
        var refusedAction = Decode(Script((0, [], 85, [], 0, []), (0, [], SatReturnSkip, [], 0, [])));
        var refusedCondition = Decode(Script((24, [0], SatReturnSkip, [], SatReturnRun, []), (0, [], SatReturnSkip, [], 0, [])));
        var questy = Decode(Script((6, [0, 77, 1], 85, [], 16, [0, 78, 2]), (0, [], SatReturnSkip, [], 0, [])));
        var caller = Decode(Script((0, [], SatCall, [1, 0], 0, []), (0, [], SatReturnSkip, [], 0, [])));
        var worlds = new Dictionary<int, WorldScript> { [1] = admittedRoot.World, [2] = refusedAction.World, [3] = refusedCondition.World, [4] = questy.World, [5] = caller.World };
        var programs = new Dictionary<int, ScrProgram> { [1] = admittedRoot.Program, [2] = refusedAction.Program, [3] = refusedCondition.Program, [4] = questy.Program, [5] = caller.Program };
        var set = ScrAdmissionSet.S33;
        Check(!set.Actions.Contains(85) && !set.Conditions.Contains(24) && set.Actions.Contains(100), "the test types are outside S33 and the set flag action is inside");
        var r1 = RcAnalyseRoot(worlds, programs, 1, set); var r2 = RcAnalyseRoot(worlds, programs, 2, set); var r3 = RcAnalyseRoot(worlds, programs, 3, set);
        var r4 = RcAnalyseRoot(worlds, programs, 4, set); var r5 = RcAnalyseRoot(worlds, programs, 5, set);
        Check(r1.Refused.Count == 0 && r1.Other.Count == 0, "an admitted root refuses nothing");
        Check(r2.Refused.SetEquals([new RcType(false, 85)]), "a refused action is named by its type");
        Check(r3.Refused.SetEquals([new RcType(true, 24)]), "a refused condition is named by its type");
        Check(r4.Refused.SetEquals([new RcType(false, 85)]) && r4.Quests.SequenceEqual([77, 78]), "a quest condition and a set quest action name their literal quest numbers, true and false branch");
        Check(r5.Refused.Count == 0 && r5.Reached == 2, "a call into an admitted script adds no refusal and counts the callee");
        var calling = Decode(Script((0, [], SatCall, [2, 0], 0, []), (0, [], SatReturnSkip, [], 0, [])));
        worlds[6] = calling.World; programs[6] = calling.Program;
        Check(RcAnalyseRoot(worlds, programs, 6, set).Refused.SetEquals([new RcType(false, 85)]), "a reached call brings the callee's refused type into the root");
        // The type that no set refuses by number stays an "other" refusal: an out-of-range flag index is no type refusal.
        var badIndex = Decode(Script((0, [], 100, [9999], 0, []), (0, [], SatReturnSkip, [], 0, [])));
        worlds[7] = badIndex.World; programs[7] = badIndex.Program;
        var r7 = RcAnalyseRoot(worlds, programs, 7, set);
        Check(r7.Refused.Count == 0 && r7.Other.Contains(ScrRefusalCode.IndexOutOfRange.ToString()), "an index refusal is kept apart from the type refusals");
        // A focus kind outside the set is an "other" refusal: float line (23, a number and an object) with focus kind 3, which S33 does not admit.
        var focus = Decode(Script((0, [], 23, [0, 5], 0, []), (0, [], SatReturnSkip, [], 0, [])));
        worlds[8] = focus.World; programs[8] = focus.Program;
        var r8 = RcAnalyseRoot(worlds, programs, 8, set);
        Check(!set.FocusKinds.Contains((ScrFocusKind)3) && r8.Refused.Count == 0 && r8.Other.Contains(ScrRefusalCode.FocusKindNotAdmitted + ":3"), "a focus kind outside the set is kept apart from the type refusals");
        // The false action of an always-true line is never read (its type is not counted); a conditional line counts both branches.
        var alwaysTrue = Decode(Script((0, [], SatReturnSkip, [], 85, []), (0, [], SatReturnSkip, [], 0, [])));
        var conditional = Decode(Script((3, [0], SatReturnSkip, [], 85, []), (0, [], SatReturnSkip, [], 0, [])));
        worlds[9] = alwaysTrue.World; programs[9] = alwaysTrue.Program; worlds[10] = conditional.World; programs[10] = conditional.Program;
        Check(RcAnalyseRoot(worlds, programs, 9, set).Refused.Count == 0 && RcAnalyseRoot(worlds, programs, 10, set).Refused.SetEquals([new RcType(false, 85)]), "always-true line: false branch not read; conditional line: both branches");
        // Greedy order: roots A (85 only, 3 sites), B (85 and 24, 1 site), C (24 only, 2 sites), D (an other refusal, 9 sites).
        var needs = new Dictionary<int, HashSet<string>> { [10] = ["a85"], [11] = ["a85", "c24"], [12] = ["c24"], [13] = ["a85"] };
        var sites = new Dictionary<int, int> { [10] = 3, [11] = 1, [12] = 2, [13] = 9 };
        var order = RcGreedy(needs, new HashSet<int> { 13 }, sites, 5);
        Check(order.Count == 2 && order[0].Need == "a85" && order[0].FreedSites == 3 && order[0].FreedRoots == 1, "greedy: 85 first (it frees A, 3 sites), the root with another refusal never frees");
        Check(order[1].Need == "c24" && order[1].FreedSites == 3 && order[1].FreedRoots == 2, "greedy: 24 next frees B and C (3 sites)");
        // A root that needs three types together is freed only when the third arrives; a lone type frees nothing before.
        var together = RcGreedy(new Dictionary<int, HashSet<string>> { [1] = ["a1", "a2", "a3"], [2] = ["a4"] }, new HashSet<int>(), new Dictionary<int, int> { [1] = 50, [2] = 1 }, 5);
        Check(together.Count == 4 && together[0].Need == "a4" && together[1].FreedSites == 0 && together[3].FreedSites == 50, "greedy: a root needing three types is freed at the third; the single site goes first");
        var seen = new HashSet<string>(StringComparer.Ordinal); var duplicate = RcGroups.SelectMany(g => g.Types).FirstOrDefault(t => !seen.Add(t));
        Check(duplicate is null, "every type is in at most one group");
        Check(RcKey(new(true, 24)) == "c24" && RcKey(new(false, 85)) == "a85" && RcGroups.SelectMany(g => g.Types).All(t => t[0] is 'a' or 'c' && int.TryParse(t[1..], out var n) && n >= 0 && n < (t[0] == 'c' ? CsConditionTypes : CsActionTypes)), "group keys name catalogued types");
        Check(RcNamedSet("S33") == ScrAdmissionSet.S33 && RcNamedSet("S99") is null && RcTypesOpen(set).Actions.Count == CsActionTypes && RcTypesOpen(set).Conditions.Count == CsConditionTypes && RcTypesOpen(set).FocusKinds.SetEquals(set.FocusKinds), "named sets and the everything set");
        Console.WriteLine("script-refusal-census self-test: PASS (refused types by reach and calls, quests, other refusals apart, greedy order, sets)");
    }
}
