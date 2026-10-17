using System.Collections.ObjectModel;
using Arcanum.Data.Scripts;

namespace Arcanum.Data.Maps;

/// <summary>What sets a transition off: the player entering a jump point's tile, a use click on an
/// object, a critter entering a scripted tile, or the player entering a scripted sector.</summary>
public enum MapTransitionTrigger { JumpPoint, Use, TileEntry, SectorEntry }

/// <summary>Why a scripted transition is not unconditional (docs/world/map-transitions.md). None means it
/// always runs when its trigger fires.</summary>
[Flags]
public enum MapTransitionGuard
{
    None = 0,
    /// <summary>The teleport is reached only behind a condition, inside or after a loop, through a deferred
    /// call or script replacement, through a call whose attachee may resolve to nothing, or through a callee
    /// with a trap flag.</summary>
    ConditionalPath = 1 << 0,
    /// <summary>The map, x or y is not a number literal.</summary>
    ComputedDestination = 1 << 1,
    /// <summary>The teleport moves an object other than the triggerer or the player.</summary>
    MovesOtherObject = 1 << 2,
    /// <summary>A call on the way names another triggerer than the triggerer or the player.</summary>
    CallChangesTriggerer = 1 << 3,
    /// <summary>The object is destroyed or off at T0.</summary>
    ObjectOffOrDestroyed = 1 << 4,
    /// <summary>A use click cannot pick the object (don't-draw, invisible or click-through at T0).</summary>
    ObjectNotClickable = 1 << 5,
    /// <summary>The attached script carries a trap flag, so a trap check runs first.</summary>
    TrapScript = 1 << 6,
    /// <summary>A use click on a container opens it first and runs the use script only from the loot screen.</summary>
    ContainerOpenCheck = 1 << 7,
    /// <summary>The destination catalogue number is not a row of the map list.</summary>
    UnlistedDestination = 1 << 8,
    /// <summary>A use click on an object that is not a portal, container or scenery runs another action first (a critter
    /// talks, an item is picked up), not its use script.</summary>
    UseRunsOtherAction = 1 << 9,
}

/// <summary>The object a use click starts from: its sector and window ordinal (null sector for a mobile, whose
/// index is its record ordinal in the map's mobile objects), the SHA-256 of its own id (the key the area owner
/// uses too), its type and prototype.</summary>
public sealed record MapTransitionObject(WorldSectorCoordinate? Sector, int Index, string OwnIdSha256, int Type, int? Prototype);

/// <summary>
/// One way out of a trigger: a jump point, or one teleport action a trigger's script can reach (one row per
/// teleport and call chain). <see cref="Location"/> is the trigger's tile (the object's tile for a use click,
/// the sector origin for a sector entry). The destination is resolved: a jump point's −1 and a script's
/// runtime −1 keep the current map. <see cref="Script"/> is the attached script (0 for a jump point),
/// <see cref="Chain"/> the scripts from it to the teleport, <see cref="Line"/> the teleport's line in the last
/// one. <see cref="OneShot"/>: the attached script removes or replaces itself, or is auto-removing, so the
/// trigger works once; never for a tile entry, whose script changes the original does not keep (ledger X3).
/// </summary>
public sealed record MapTransitionSite(MapTransitionTrigger Trigger, WorldLocation Location, MapTransitionObject? Object, int Script,
    IReadOnlyList<int> Chain, int Line, bool Fade, bool OneShot, int DestinationCatalogue, int DestinationRuntime, WorldLocation Arrival,
    MapTransitionGuard Guards)
{
    public bool Unconditional => Guards == MapTransitionGuard.None;
}

/// <summary>The transitions of one map and the reads that failed (a refused map file, sector, mobile file, prototype or
/// script drops only what depends on it; a call graph over <see cref="MapTransitions.MaxVisits"/> is a
/// <see cref="WorldIssueCode.LimitExceeded"/> for its script).</summary>
public sealed class MapTransitionSet
{
    internal MapTransitionSet(WorldMap map, MapListEntry? entry, IReadOnlyList<MapTransitionSite> sites, IReadOnlyList<WorldIssue> issues)
    { Map = map; Entry = entry; Sites = sites; Issues = issues; }
    public WorldMap Map { get; }
    /// <summary>The map's map-list row; null for an unlisted map (its sites then have no destination of their own map).</summary>
    public MapListEntry? Entry { get; }
    public IReadOnlyList<MapTransitionSite> Sites { get; }
    public IReadOnlyList<WorldIssue> Issues { get; }
    public IEnumerable<MapTransitionSite> Unconditional => Sites.Where(s => s.Unconditional);
}

/// <summary>
/// ARC-DATA-025: every transition of any map of the content stack, as data. Jump points come from
/// <c>map.jmp</c>; scripted transitions from the use script of every placed object, every tile script and the
/// sector scripts, followed through literal calls, with the classification of
/// docs/research/world-transitions.md (ARC-RES-035): a teleport is unconditional only on the forced path from
/// line 0 (always-true conditions, no loop, no deferred call), with a literal destination, moving the
/// triggerer or the player, from a present, clickable, untrapped non-container. It decodes and never runs a
/// script. No location is special: the map, its objects and its scripts come from the stack as the original
/// opens them.
/// </summary>
public static class MapTransitions
{
    /// <summary>Calls followed from the attached script, as in the research census.</summary>
    public const int MaxCallDepth = 6;
    /// <summary>Script entries visited per attached script before its paths are refused as too large.</summary>
    public const int MaxVisits = 4096;
    private const int UseSlot = 1, ScriptsField = 31, FlagsField = 18, LocationField = 1;
    private const int PortalType = 1, ContainerType = 2, SceneryType = 3;
    private const uint GoneFlags = 0x1 | 0x2, UnclickableFlags = 0x100 | 0x200 | 0x800;
    private const uint TrapFlags = ScrCatalogue.FileFlag.NonmagicalTrap | ScrCatalogue.FileFlag.MagicalTrap;

    public static MapTransitionSet Read(WorldCatalog catalog, MapList list, WorldMap map, ScriptContent scripts)
    {
        ArgumentNullException.ThrowIfNull(catalog); ArgumentNullException.ThrowIfNull(list);
        ArgumentNullException.ThrowIfNull(map); ArgumentNullException.ThrowIfNull(scripts);
        var entry = list.Find(map.Name);
        var issues = new List<WorldIssue>();
        var sites = new List<MapTransitionSite>();
        var reader = new ScriptGraph(scripts, issues);
        issues.AddRange(map.Issues);

        // The original takes the first jump point of a tile (jumppoint_get), so a later one on the same tile never fires.
        var jumpTiles = new HashSet<WorldLocation>();
        foreach (var jump in map.JumpPoints)
        {
            if (!jumpTiles.Add(jump.Location)) continue;
            var runtime = jump.DestinationMap == -1 ? entry?.Runtime ?? -1 : jump.DestinationMap;
            var destination = list.ByRuntime(runtime);
            sites.Add(new(MapTransitionTrigger.JumpPoint, jump.Location, null, 0, [], 0, false, false, destination?.Catalogue ?? 0, runtime, jump.Destination,
                destination is null ? MapTransitionGuard.UnlistedDestination : MapTransitionGuard.None));
        }

        var prototypes = new Dictionary<int, WorldObject?>();
        WorldObject? Prototype(int number)
        {
            if (prototypes.TryGetValue(number, out var known)) return known;
            var read = catalog.ReadPrototype(number);
            if (read.Issue is not null) issues.Add(read.Issue);
            return prototypes[number] = read.Prototype;
        }
        void Attachment(MapTransitionTrigger trigger, WorldLocation location, MapTransitionObject? owner, uint objectFlags, int script)
        {
            if (script is <= 0 or > ScrCatalogue.MaxScriptNumber) return;
            foreach (var path in reader.Paths(script))
            {
                var guards = path.Guards;
                if (owner is not null)
                {
                    if ((objectFlags & GoneFlags) != 0) guards |= MapTransitionGuard.ObjectOffOrDestroyed;
                    if ((objectFlags & UnclickableFlags) != 0) guards |= MapTransitionGuard.ObjectNotClickable;
                    if (owner.Type == ContainerType) guards |= MapTransitionGuard.ContainerOpenCheck;
                    else if (owner.Type is not (PortalType or SceneryType)) guards |= MapTransitionGuard.UseRunsOtherAction;
                }
                if ((reader.Program(script)!.FileFlags & TrapFlags) != 0) guards |= MapTransitionGuard.TrapScript;
                var site = path.Site;
                var runtime = site.Map - (MapList.FirstCatalogue - 1);
                if (runtime == -1) runtime = entry?.Runtime ?? -1;
                var destination = site.Literal ? list.ByRuntime(runtime) : null;
                if (site.Literal && destination is null) guards |= MapTransitionGuard.UnlistedDestination;
                sites.Add(new(trigger, location, owner, script, path.Chain, site.Line, site.Fade, trigger != MapTransitionTrigger.TileEntry && reader.OneShot(script), destination?.Catalogue ?? 0,
                    site.Literal ? runtime : 0, new(site.X, site.Y), guards));
            }
        }
        void Object(WorldObject instance, WorldSectorCoordinate? sector, int index)
        {
            // An unresolved prototype (reported) leaves the instance's own fields.
            var prototype = instance.PrototypeNumber is { } number ? Prototype(number) : null;
            WorldFieldValue? Effective(int slot) => instance.Fields.TryGetValue(slot, out var own) ? own : prototype?.Fields.GetValueOrDefault(slot);
            if (Effective(ScriptsField) is not { } field || field.Elements.SingleOrDefault(e => e.Key == UseSlot) is not { Words.Count: 3 } use) return;
            if (instance.Fields.GetValueOrDefault(LocationField)?.Number is not { } packed) return;
            var raw = WorldLocation.FromPacked(packed);
            var location = sector is { } s ? new WorldLocation(s.X * 64L + (raw.X & 63), s.Y * 64L + (raw.Y & 63)) : raw;
            var flags = unchecked((uint)(Effective(FlagsField)?.Number ?? 0));
            if (use.Words[2] is < 1 or > ScrCatalogue.MaxScriptNumber) return;
            Attachment(MapTransitionTrigger.Use, location, new(sector, index, instance.OwnIdSha256, instance.Type, instance.PrototypeNumber), flags, (int)use.Words[2]);
        }

        foreach (var coordinate in map.SectorFiles)
        {
            var read = catalog.ReadSector(map, coordinate);
            if (read.Sector is not { } sector) { if (read.Issue is not null) issues.Add(read.Issue); continue; }
            if (sector.ObjectIssue is not null) issues.Add(sector.ObjectIssue);
            foreach (var tile in sector.TileScripts)
            {
                // Node: flags, tile index, the attachment's header flags and counters, script number, spare.
                if (tile.Count < 5 || tile[1] is < 0 or >= 4096) { issues.Add(new(WorldIssueCode.Malformed, sector.Path, null, "tile script tile")); continue; }
                Attachment(MapTransitionTrigger.TileEntry, new(coordinate.X * 64L + (tile[1] & 63), coordinate.Y * 64L + (tile[1] >> 6)), null, 0, tile[4]);
            }
            // Sector script: the attachment's header flags and counters, then its number.
            if (sector.SectorScript is { Count: 3 } own) Attachment(MapTransitionTrigger.SectorEntry, new(coordinate.X * 64L, coordinate.Y * 64L), null, 0, own[2]);
            for (var index = 0; index < sector.Objects.Count; index++) Object(sector.Objects[index], coordinate, index);
        }
        var mobiles = catalog.ReadMobiles(map);
        issues.AddRange(mobiles.Issues);
        for (var index = 0; index < mobiles.Objects.Count; index++) Object(mobiles.Objects[index], null, index);
        return new(map, entry, sites.AsReadOnly(), issues.AsReadOnly());
    }

    private enum Reach { Unreached, Conditional, Always }

    private sealed record Site(int Line, bool Branch, bool Fade, ScrFocusKind? Focus, bool Literal, int Map, int X, int Y);
    private sealed record Call(int Line, bool Branch, int Action, int? Target, int? Entry, byte TriggererFocus, byte AttacheeFocus);
    private sealed record Flow(Reach[] True, Reach[] False);
    private sealed record PathRow(Site Site, IReadOnlyList<int> Chain, MapTransitionGuard Guards);

    /// <summary>Scripts read once per map, with their teleport sites, literal calls and flows per entry line.</summary>
    private sealed class ScriptGraph(ScriptContent content, List<WorldIssue> issues)
    {
        private readonly Dictionary<int, ScrProgram?> _programs = new();
        private readonly Dictionary<(int, int), Flow> _flows = new();
        private readonly Dictionary<int, IReadOnlyList<PathRow>> _paths = new();
        private readonly Dictionary<int, (IReadOnlyList<Site> Sites, IReadOnlyList<Call> Calls)> _edges = new();

        private (IReadOnlyList<Site> Sites, IReadOnlyList<Call> Calls) Edges(int number, ScrProgram program)
        {
            if (!_edges.TryGetValue(number, out var edges)) _edges[number] = edges = (Sites(program).ToArray(), Calls(program).ToArray());
            return edges;
        }

        public ScrProgram? Program(int number)
        {
            if (_programs.TryGetValue(number, out var known)) return known;
            var read = content.Script(number);
            if (read.Status is not (ScriptContentStatus.Read or ScriptContentStatus.NoName or ScriptContentStatus.NotFound))
                issues.Add(new(WorldIssueCode.Unreadable, read.Name?.ScriptPath ?? $"scr/{number:D5}", number, read.Detail));
            return _programs[number] = read.Program;
        }

        private Flow FlowOf(int number, int entry)
        {
            if (!_flows.TryGetValue((number, entry), out var flow)) _flows[(number, entry)] = flow = Analyse(Program(number)!, entry);
            return flow;
        }

        /// <summary>One attached script's own action removes or replaces it on a reached branch, or its file is auto-removing.</summary>
        public bool OneShot(int number)
        {
            if (Program(number) is not { } program) return false;
            if ((program.FileFlags & ScrCatalogue.FileFlag.AutoRemoving) != 0) return true;
            var flow = FlowOf(number, 0);
            static bool Removes(ScrClause clause) => clause.Type is ScrCatalogue.Action.RemoveThisScript or ScrCatalogue.Action.ChangeThisScript;
            return program.Lines.Any(l => (flow.True[l.Number] != Reach.Unreached && Removes(l.OnTrue)) || (flow.False[l.Number] != Reach.Unreached && Removes(l.OnFalse)));
        }

        /// <summary>Teleports the attached script reaches from line 0 and through literal calls; one row per teleport
        /// and call chain, with the least restrictive guards found for it.</summary>
        public IReadOnlyList<PathRow> Paths(int root)
        {
            if (_paths.TryGetValue(root, out var known)) return known;
            var rows = new List<PathRow>();
            var visits = 0;
            void Visit(int number, int entry, Reach reach, MapTransitionGuard guards, List<int> chain, HashSet<(int, int)> active)
            {
                if (Program(number) is not { } program || chain.Count > MaxCallDepth) return;
                if (++visits > MaxVisits) return;
                var flow = FlowOf(number, entry);
                var (sites, calls) = Edges(number, program);
                Reach At(int line, bool branch) => branch ? flow.True[line] : flow.False[line];
                MapTransitionGuard Reached(Reach r) => r == Reach.Always ? MapTransitionGuard.None : MapTransitionGuard.ConditionalPath;
                foreach (var site in sites)
                {
                    var here = At(site.Line, site.Branch);
                    if (here == Reach.Unreached) continue;
                    var g = guards | Reached((Reach)Math.Min((int)reach, (int)here));
                    if (!site.Literal) g |= MapTransitionGuard.ComputedDestination;
                    if (site.Focus is not (ScrFocusKind.Triggerer or ScrFocusKind.Player)) g |= MapTransitionGuard.MovesOtherObject;
                    rows.Add(new(site, chain.ToArray(), g));
                }
                // Calls that reach the same callee entry with the same reach and guards are followed once.
                var followed = new HashSet<(int, int, Reach, MapTransitionGuard)>();
                foreach (var call in calls)
                {
                    var here = At(call.Line, call.Branch);
                    if (here == Reach.Unreached || call.Target is not { } target || call.Entry is not { } line || active.Contains((target, line))) continue;
                    var deferred = call.Action is ScrCatalogue.Action.CallScriptIn or ScrCatalogue.Action.CallScriptAt or ScrCatalogue.Action.ChangeThisScript or ChangeScript;
                    var g = guards;
                    if (call.TriggererFocus is not ((byte)ScrFocusKind.Triggerer or (byte)ScrFocusKind.Player)) g |= MapTransitionGuard.CallChangesTriggerer;
                    // The call runs once per object its attachee resolves to; only single-object kinds always resolve to one.
                    var group = call.AttacheeFocus is not ((byte)ScrFocusKind.Triggerer or (byte)ScrFocusKind.Attachee or (byte)ScrFocusKind.Player);
                    var trap = !deferred && Program(target) is { } callee && (callee.FileFlags & TrapFlags) != 0;
                    var next = (Reach)Math.Min((int)reach, (int)(deferred || group || trap ? Reach.Conditional : here));
                    if (!followed.Add((target, line, next, g))) continue;
                    Visit(target, line, next, g, [.. chain, target], [.. active, (target, line)]);
                }
            }
            Visit(root, 0, Reach.Always, MapTransitionGuard.None, [root], [(root, 0)]);
            if (visits > MaxVisits)
            {
                issues.Add(new(WorldIssueCode.LimitExceeded, content.Names.Find(root)?.ScriptPath ?? $"scr/{root:D5}", root, "call graph"));
                return _paths[root] = [];
            }
            // One row per teleport and chain: the row with the fewest guards.
            var result = rows.GroupBy(r => (r.Site, string.Join(',', r.Chain)))
                .Select(g => g.OrderBy(r => System.Numerics.BitOperations.PopCount((uint)r.Guards)).ThenBy(r => (int)r.Guards).First()).ToArray();
            return _paths[root] = Array.AsReadOnly(result);
        }
    }

    // Change script (99): the replacement runs from line 0 on the next invocation.
    private const int ChangeScript = 99;

    /// <summary>Teleport (44: focus, map, x, y) and fade-and-teleport (102: time, sound, movie, focus, map, x, y) on either branch.</summary>
    private static IEnumerable<Site> Sites(ScrProgram program)
    {
        foreach (var line in program.Lines)
            foreach (var (branch, clause) in new[] { (true, line.OnTrue), (false, line.OnFalse) })
            {
                var at = clause.Type switch { ScrCatalogue.Action.Teleport => 0, ScrCatalogue.Action.FadeAndTeleport => 3, _ => -1 };
                if (at < 0) continue;
                var focus = ScrCatalogue.IsKnownFocusKind(clause.Kind(at)) ? (ScrFocusKind?)clause.Kind(at) : null;
                var literal = clause.Kind(at + 1) == (byte)ScrValueKind.Number && clause.Kind(at + 2) == (byte)ScrValueKind.Number && clause.Kind(at + 3) == (byte)ScrValueKind.Number;
                yield return new(line.Number, branch, at == 3, focus, literal, clause.Value(at + 1), clause.Value(at + 2), clause.Value(at + 3));
            }
    }

    /// <summary>Literal script edges: call, call in and call at (script 0, entry 1, triggerer 2, attachee 3); change this
    /// script (0) and change script (2) run their replacement from line 0. Call-ex runs an object's own attachment and is
    /// not followed.</summary>
    private static IEnumerable<Call> Calls(ScrProgram program)
    {
        const byte Single = (byte)ScrFocusKind.Triggerer;
        foreach (var line in program.Lines)
            foreach (var (branch, c) in new[] { (true, line.OnTrue), (false, line.OnFalse) })
            {
                int? Literal(int slot) => c.Kind(slot) == (byte)ScrValueKind.Number ? c.Value(slot) : null;
                switch (c.Type)
                {
                    case ScrCatalogue.Action.CallScript or ScrCatalogue.Action.CallScriptIn or ScrCatalogue.Action.CallScriptAt:
                        yield return new(line.Number, branch, c.Type, Literal(0), Literal(1), c.Kind(2), c.Kind(3)); break;
                    case ScrCatalogue.Action.ChangeThisScript: yield return new(line.Number, branch, c.Type, Literal(0), 0, Single, Single); break;
                    case ChangeScript: yield return new(line.Number, branch, c.Type, Literal(2), 0, Single, Single); break;
                }
            }
    }

    /// <summary>How each line's branches are reached from <paramref name="entry"/>: any reached branch is
    /// Conditional; the forced path (always-true conditions, each with one forced successor) is Always. A loop's
    /// successors are not forced (they depend on how many objects its focus resolves to); a computed goto, a goto out
    /// of the script and an unmatched loop break end the walk.</summary>
    private static Flow Analyse(ScrProgram program, int entry)
    {
        var count = program.Count;
        var reachTrue = new Reach[count]; var reachFalse = new Reach[count];
        if (entry < 0 || entry >= count) return new(reachTrue, reachFalse);
        var seen = new bool[count]; var work = new Queue<int>();
        seen[entry] = true; work.Enqueue(entry);
        while (work.TryDequeue(out var line))
        {
            var row = program.Lines[line];
            var conditional = row.Condition.Type != ScrCatalogue.Condition.True;
            foreach (var (branch, clause) in conditional ? new[] { (true, row.OnTrue), (false, row.OnFalse) } : [(true, row.OnTrue)])
            {
                (branch ? reachTrue : reachFalse)[line] = Reach.Conditional;
                foreach (var next in Step(program, line, clause).Next)
                    if (!seen[next]) { seen[next] = true; work.Enqueue(next); }
            }
        }
        var visited = new HashSet<int>(); var current = entry;
        while (current >= 0 && current < count && visited.Add(current) && program.Lines[current].Condition.Type == ScrCatalogue.Condition.True)
        {
            reachTrue[current] = Reach.Always;
            var (next, forced) = Step(program, current, program.Lines[current].OnTrue);
            if (!forced || next.Count != 1) break;
            current = next[0];
        }
        return new(reachTrue, reachFalse);
    }

    private static (IReadOnlyList<int> Next, bool Forced) Step(ScrProgram program, int line, ScrClause action)
    {
        var count = program.Count;
        IReadOnlyList<int> Fall() => line + 1 < count ? [line + 1] : [];
        // A loop ends at the first later line whose condition is always true and whose true action ends the loop.
        int? AfterLoopEnd()
        {
            for (var l = line + 1; l < count; l++)
                if (program.Lines[l].Condition.Type == ScrCatalogue.Condition.True && program.Lines[l].OnTrue.Type == ScrCatalogue.Action.LoopEnd)
                    return l + 1 < count ? l + 1 : null;
            return null;
        }
        switch (action.Type)
        {
            case ScrCatalogue.Action.ReturnSkipDefault or ScrCatalogue.Action.ReturnRunDefault or ScrCatalogue.Action.Dialog: return ([], true);
            case ScrCatalogue.Action.Goto:
                return action.Kind(0) == (byte)ScrValueKind.Number && action.Value(0) >= 0 && action.Value(0) < count ? ([action.Value(0)], true) : ([], true);
            case ScrCatalogue.Action.LoopFor:
                return (AfterLoopEnd() is { } after ? Fall().Append(after).Distinct().ToArray() : Fall(), false);
            case ScrCatalogue.Action.LoopEnd: return (Fall(), false);
            case ScrCatalogue.Action.LoopBreak: return (AfterLoopEnd() is { } end ? [end] : [], false);
            default: return (Fall(), true);
        }
    }
}
