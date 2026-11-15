using Arcanum.Data.Scripts;
using Arcanum.Scripting.Scr;

namespace Arcanum.CrashSiteProbe;

// ARC-RES-2262 research only: the focus kinds the set under test (run with `--set S60` or S61) still refuses, counted as ARC-RES-1101 counts types.
// Per root (an attached script run from line 0, calls followed) it names the first refusal the production admission gives, the focus
// kinds the reached lines name that the set does not admit, and whether admitting a kind group frees the root. Only numbers and map
// names leave the probe.
internal static partial class Program
{
    /// <summary>The kind groups the issue names, keyed by a short id; a kind that is in none is its own group "K&lt;n&gt;".</summary>
    internal static string RcKindGroup(int kind) => (ScrFocusKind)kind switch
    {
        ScrFocusKind.EveryFollower or ScrFocusKind.AnyFollower => "FOLLOWER",
        ScrFocusKind.EveryoneInParty or ScrFocusKind.AnyoneInParty => "PARTY",
        ScrFocusKind.EveryoneInTeam or ScrFocusKind.AnyoneInTeam => "TEAM",
        ScrFocusKind.EveryoneInGroup or ScrFocusKind.AnyoneInGroup => "GROUP",
        ScrFocusKind.EveryContainerInVicinity or ScrFocusKind.AnyContainerInVicinity => "CONTAINER",
        ScrFocusKind.EveryItemInVicinity or ScrFocusKind.AnyItemInVicinity => "ITEM",
        ScrFocusKind.EveryPortalInVicinity or ScrFocusKind.AnyPortalInVicinity => "PORTAL",
        ScrFocusKind.EverySceneryInVicinity or ScrFocusKind.AnySceneryInVicinity => "SCENERY",
        ScrFocusKind.EveryoneInVicinity or ScrFocusKind.AnyoneInVicinity => "CRITTER",
        _ => "K" + kind.ToString(System.Globalization.CultureInfo.InvariantCulture),
    };

    /// <summary>What a root's reached lines name outside the set: the refused focus kinds, and whether a condition names a critter vicinity kind
    /// (8 or 9), which the admission refuses in conditions whatever the set holds (ARC-SCRIPT-047).</summary>
    internal sealed record RcFocusNeeds(SortedSet<int> Kinds, bool ConditionVicinity);

    /// <summary>The focus kinds the reached lines of one live set name that <paramref name="set"/> does not admit: object operands, and the
    /// focus kind packed into the high 16 bits of a PC variable or PC flag operand (ARC-DEV-100, GV3, GV4).</summary>
    internal static RcFocusNeeds RcFocusNeedsOf(IReadOnlyDictionary<int, ScrProgram> programs, CsLive live, ScrAdmissionSet set)
    {
        var kinds = new SortedSet<int>(); var conditionVicinity = false;
        foreach (var (number, reach) in live.Reach)
        {
            if (!programs.TryGetValue(number, out var program)) continue;
            for (var at = 0; at < program.Lines.Count && at < reach.True.Length; at++)
            {
                if (!(reach.True[at] || reach.False[at])) continue;
                var line = program.Lines[at];
                void Scan(ScrClause clause, ScrClauseSide side)
                {
                    var condition = side == ScrClauseSide.Condition;
                    if ((condition ? !ScrCatalogue.IsKnownCondition(clause.Type) : !ScrCatalogue.IsKnownAction(clause.Type)) || ScrCatalogue.Signature(side, clause.Type) is not { } signature) return;
                    for (var slot = 0; slot < ScrClause.Operands; slot++)
                    {
                        // Float newspaper headline reads its object from slot 1; slot 0 is never read (X9).
                        var role = !condition && clause.Type == ScrCatalogue.Action.FloatNewspaperHeadline ? (slot == 1 ? ScrOperandRole.Object : ScrOperandRole.Unused) : signature[slot];
                        var kind = clause.Kind(slot); var value = clause.Value(slot);
                        if (role == ScrOperandRole.Object && ScrCatalogue.IsKnownFocusKind(kind))
                        {
                            if (condition && (ScrFocusKind)kind is ScrFocusKind.EveryoneInVicinity or ScrFocusKind.AnyoneInVicinity) conditionVicinity = true;
                            else if (!set.FocusKinds.Contains((ScrFocusKind)kind)) kinds.Add(kind);
                        }
                        else if (role == ScrOperandRole.Number && ScrCatalogue.IsKnownValueKind(kind) && (ScrValueKind)kind is ScrValueKind.PcVariable or ScrValueKind.PcFlag)
                        {
                            var focus = value >> 16;
                            if (focus is >= 0 and <= byte.MaxValue && ScrCatalogue.IsKnownFocusKind((byte)focus) && !set.FocusKinds.Contains((ScrFocusKind)focus)) kinds.Add(focus);
                        }
                    }
                }
                Scan(line.Condition, ScrClauseSide.Condition);
                if (reach.True[at]) Scan(line.OnTrue, ScrClauseSide.OnTrue);
                if (reach.False[at]) Scan(line.OnFalse, ScrClauseSide.OnFalse);
            }
        }
        return new(kinds, conditionVicinity);
    }

    /// <summary>The first refusal the production admission gives a root under <paramref name="set"/>: the root script's own entry first, then the other
    /// reached scripts by number and entry, each entry as <see cref="ScrAdmission.AdmitFrom"/> reports it (its lowest refused line). Null when the root is admitted whole.</summary>
    internal static ScrRefusal? RcFirstRefusal(IReadOnlyDictionary<int, ScrProgram> programs, CsLive live, int root, ScrAdmissionSet set)
    {
        foreach (var number in live.Entries.Keys.OrderBy(n => n == root ? -1 : n))
        {
            if (!programs.TryGetValue(number, out var program)) continue;
            foreach (var entry in live.Entries[number])
                if (ScrAdmission.AdmitFrom(program, set, entry).Refusal is { } refusal) return refusal;
        }
        return null;
    }

    /// <summary>The focus kind a focus kind refusal names: an object operand's own kind, or for a PC variable or flag operand (value kind 5 or 6, the refusal carries the value) the kind packed into its high 16 bits.</summary>
    internal static int? RcRefusedFocusKind(ScrRefusal refusal) => refusal.Value is { } value && refusal.Kind is 5 or 6 ? value >> 16 : refusal.Kind;

    /// <summary>The first refusal as a short code: the refusal code, with the kind for a focus or value kind refusal and the type for a type refusal.</summary>
    internal static string RcRefusalLabel(ScrRefusal refusal) => refusal.Code switch
    {
        ScrRefusalCode.FocusKindNotAdmitted when RcRefusedFocusKind(refusal) is { } focus => $"{refusal.Code}:{focus}",
        ScrRefusalCode.ValueKindNotAdmitted when refusal.Kind is { } kind => $"{refusal.Code}:{kind}",
        ScrRefusalCode.ConditionNotAdmitted => "ConditionNotAdmitted:c" + refusal.Type.ToString(System.Globalization.CultureInfo.InvariantCulture),
        ScrRefusalCode.ActionNotAdmitted => "ActionNotAdmitted:a" + refusal.Type.ToString(System.Globalization.CultureInfo.InvariantCulture),
        _ => refusal.Code.ToString(),
    };

    /// <summary>True when every reached entry of the root is admitted with every condition and action type open and the focus kinds of <paramref name="set"/> plus <paramref name="extra"/>.</summary>
    internal static bool RcAdmittedWithKinds(IReadOnlyDictionary<int, ScrProgram> programs, CsLive live, ScrAdmissionSet set, IEnumerable<int> extra)
    {
        var widened = new ScrAdmissionSet(set.Id + "+kinds", Enumerable.Range(0, CsConditionTypes), Enumerable.Range(0, CsActionTypes), set.ValueKinds,
            set.FocusKinds.Concat(extra.Select(k => (ScrFocusKind)k)), set.CounterIndexWraps);
        foreach (var (number, entries) in live.Entries)
        {
            if (!programs.TryGetValue(number, out var program)) return false;
            foreach (var entry in entries) if (ScrAdmission.AdmitFrom(program, widened, entry).Refusal is not null) return false;
        }
        return true;
    }

    /// <summary>One root with what the focus census needs of it.</summary>
    internal sealed record RcFocusRoot(int Script, ScrRefusal? First, RcFocusNeeds Needs, bool TypeRefused, bool FreedByAllKinds);

    internal static object RcFocusReport(IReadOnlyList<RcSite> placed, IReadOnlyDictionary<int, RcRoot> roots, IReadOnlyDictionary<int, ScrProgram> programs, IReadOnlyDictionary<int, WorldScript> scripts, ScrAdmissionSet set)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        var focusRoots = new Dictionary<int, RcFocusRoot>();
        var allKinds = Enumerable.Range(0, CsFocusKinds).ToArray();
        foreach (var (number, root) in roots)
        {
            var live = CsLiveSet(scripts, [number]);
            var needs = RcFocusNeedsOf(programs, live, set);
            focusRoots[number] = new(number, RcFirstRefusal(programs, live, number, set), needs, root.Refused.Count > 0, RcAdmittedWithKinds(programs, live, set, allKinds));
        }
        var placedByRoot = placed.GroupBy(p => p.Script).ToDictionary(g => g.Key, g => g.ToArray());
        int SitesOf(IEnumerable<int> rootNumbers) => rootNumbers.Sum(r => placedByRoot.TryGetValue(r, out var s) ? s.Length : 0);
        IEnumerable<RcSite> SiteList(IEnumerable<int> rootNumbers) => rootNumbers.SelectMany(r => placedByRoot.TryGetValue(r, out var s) ? s : []);
        object Spread(IEnumerable<int> rootNumbers)
        {
            var sites = SiteList(rootNumbers).ToArray();
            return new
            {
                roots = rootNumbers.Count(), sites = sites.Length, maps = sites.Select(s => s.Map).Where(m => m.Length > 0).Distinct().Count(),
                tile_or_sector_sites = sites.Count(s => s.Sap < 0),
                by_point = sites.Where(s => s.Sap >= 0).GroupBy(s => s.Sap).OrderBy(g => g.Key).ToDictionary(g => g.Key.ToString(inv), g => g.Count()),
                top_maps = sites.Where(s => s.Map.Length > 0).GroupBy(s => s.Map).OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal).Take(6).Select(g => new { map = g.Key, sites = g.Count() }).ToArray(),
            };
        }

        // First refusal under the set: the label per root, with roots and sites.
        var firstByLabel = focusRoots.Values.Where(r => r.First is not null).GroupBy(r => RcRefusalLabel(r.First!)).OrderByDescending(g => SitesOf(g.Select(r => r.Script))).ThenBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => new { first_refusal = g.Key, spread = Spread(g.Select(r => r.Script)) }).ToArray();
        var firstIsFocus = focusRoots.Values.Where(r => r.First is { Code: ScrRefusalCode.FocusKindNotAdmitted }).Select(r => r.Script).ToArray();

        // Per refused kind: the roots whose reached lines name it, the roots it is the first refusal of, and the roots admitting it alone frees.
        var kindNames = focusRoots.Values.SelectMany(r => r.Needs.Kinds).Distinct().Order().ToArray();
        var perKind = kindNames.Select(kind =>
        {
            var reaching = focusRoots.Values.Where(r => r.Needs.Kinds.Contains(kind)).Select(r => r.Script).ToArray();
            var first = focusRoots.Values.Where(r => r.First is { Code: ScrRefusalCode.FocusKindNotAdmitted } fr && RcRefusedFocusKind(fr) == kind).Select(r => r.Script).ToArray();
            var freed = reaching.Where(r => !focusRoots[r].TypeRefused && RcAdmittedWithKinds(programs, CsLiveSet(scripts, [r]), set, [kind])).ToArray();
            return new
            {
                kind, name = ((ScrFocusKind)kind).ToString(), group = RcKindGroup(kind),
                reached = Spread(reaching), first_refusal = Spread(first), freed_alone = Spread(freed), also_blocked_by_type_roots = reaching.Count(r => focusRoots[r].TypeRefused),
                type_blockers = reaching.Where(r => focusRoots[r].TypeRefused).SelectMany(r => roots[r].Refused).GroupBy(t => t).OrderByDescending(g => g.Count()).ThenBy(g => RcKey(g.Key), StringComparer.Ordinal).Take(6).Select(g => new { type = RcKey(g.Key), roots = g.Count() }).ToArray(),
                top_roots = reaching.OrderByDescending(r => placedByRoot.TryGetValue(r, out var s) ? s.Length : 0).ThenBy(r => r).Take(5).Select(r => new { script = r, sites = placedByRoot.TryGetValue(r, out var s) ? s.Length : 0 }).ToArray(),
            };
        }).ToArray();

        // Per group: roots whose needs are all inside the group and that have no refused type are freed by the group alone; the greedy order covers the groups together.
        var groupIds = kindNames.Select(RcKindGroup).Distinct().Order(StringComparer.Ordinal).ToArray();
        var perGroup = groupIds.Select(id =>
        {
            var kindsOf = kindNames.Where(k => RcKindGroup(k) == id).ToArray();
            var touching = focusRoots.Values.Where(r => r.Needs.Kinds.Overlaps(kindsOf)).Select(r => r.Script).ToArray();
            var freed = touching.Where(r => !focusRoots[r].TypeRefused && RcAdmittedWithKinds(programs, CsLiveSet(scripts, [r]), set, kindsOf)).ToArray();
            return new { group = id, kinds = kindsOf, touching = Spread(touching), freed_alone = Spread(freed), blocked_by_type_too = touching.Count(r => focusRoots[r].TypeRefused) };
        }).ToArray();
        var groupNeeds = focusRoots.Values.Where(r => r.Needs.Kinds.Count > 0 && !r.TypeRefused).ToDictionary(r => r.Script, r => r.Needs.Kinds.Select(RcKindGroup).ToHashSet(StringComparer.Ordinal));
        var never = focusRoots.Values.Where(r => r.Needs.Kinds.Count > 0 && !r.TypeRefused && !r.FreedByAllKinds).Select(r => r.Script).ToHashSet();
        var sitesOfRoot = placedByRoot.ToDictionary(p => p.Key, p => p.Value.Length);
        var greedy = RcGreedy(groupNeeds, never, sitesOfRoot, 12);
        var cumulative = 0;
        var order = greedy.Select((g, i) => { cumulative += g.FreedSites; return new { step = i + 1, group = g.Need, freed_sites = g.FreedSites, freed_roots = g.FreedRoots, cumulative_sites = cumulative }; }).ToArray();

        // Every refused kind on every line of every decoded script (attached or not, reached or not): where a kind is named by no script at all, no host slice can free anything.
        var everywhere = new Dictionary<int, (int Clauses, HashSet<int> Scripts)>();
        foreach (var (number, program) in programs)
            foreach (var line in program.Lines)
                foreach (var (clause, side) in new[] { (line.Condition, ScrClauseSide.Condition), (line.OnTrue, ScrClauseSide.OnTrue), (line.OnFalse, ScrClauseSide.OnFalse) })
                {
                    var condition = side == ScrClauseSide.Condition;
                    if ((condition ? !ScrCatalogue.IsKnownCondition(clause.Type) : !ScrCatalogue.IsKnownAction(clause.Type)) || ScrCatalogue.Signature(side, clause.Type) is not { } signature) continue;
                    for (var slot = 0; slot < ScrClause.Operands; slot++)
                    {
                        var role = !condition && clause.Type == ScrCatalogue.Action.FloatNewspaperHeadline ? (slot == 1 ? ScrOperandRole.Object : ScrOperandRole.Unused) : signature[slot];
                        int? kind = null;
                        if (role == ScrOperandRole.Object && ScrCatalogue.IsKnownFocusKind(clause.Kind(slot))) kind = clause.Kind(slot);
                        else if (role == ScrOperandRole.Number && ScrCatalogue.IsKnownValueKind(clause.Kind(slot)) && (ScrValueKind)clause.Kind(slot) is ScrValueKind.PcVariable or ScrValueKind.PcFlag && clause.Value(slot) >> 16 is >= 0 and <= byte.MaxValue and var packed && ScrCatalogue.IsKnownFocusKind((byte)packed)) kind = packed;
                        if (kind is not { } k || set.FocusKinds.Contains((ScrFocusKind)k)) continue;
                        if (!everywhere.TryGetValue(k, out var u)) everywhere[k] = u = (0, []);
                        everywhere[k] = (u.Clauses + 1, u.Scripts); u.Scripts.Add(number);
                    }
                }
        var allKindRows = Enumerable.Range(0, CsFocusKinds).Where(k => !set.FocusKinds.Contains((ScrFocusKind)k)).Select(k =>
            new { kind = k, name = ((ScrFocusKind)k).ToString(), group = RcKindGroup(k), clauses_in_all_scripts = everywhere.TryGetValue(k, out var u) ? u.Clauses : 0, scripts_in_all_scripts = everywhere.TryGetValue(k, out var v) ? v.Scripts.Count : 0,
                attached_roots_reaching = focusRoots.Values.Count(r => r.Needs.Kinds.Contains(k)) }).ToArray();

        var withNeed = focusRoots.Values.Where(r => r.Needs.Kinds.Count > 0).Select(r => r.Script).ToArray();
        var typeFree = withNeed.Where(r => !focusRoots[r].TypeRefused).ToArray();
        return new
        {
            set = set.Id, admitted_kinds = set.FocusKinds.Select(k => (int)k).Order().ToArray(),
            roots_naming_refused_kind = Spread(withNeed), of_them_without_type_refusal = Spread(typeFree),
            of_them_never_freed_by_kinds = Spread(withNeed.Where(r => !focusRoots[r].FreedByAllKinds)),
            roots_naming_condition_vicinity = Spread(focusRoots.Values.Where(r => r.Needs.ConditionVicinity).Select(r => r.Script)),
            first_refusal_is_focus_kind = Spread(firstIsFocus), first_refusal_by_label = firstByLabel,
            every_refused_kind = allKindRows, per_kind = perKind, per_group = perGroup, group_order = order,
        };
    }
}
