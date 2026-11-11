using Arcanum.Data.Scripts;
using Arcanum.Scripting.Scr;
using Arcanum.Session;
using Arcanum.Simulation.Movement;

namespace Arcanum.World;

/// <summary>
/// ARC-MAGIC-042 (technology queue slice T-m1, ledger TI4–TI6): what the shown map's host gives the run of a thrown item's hit or miss script (points 7 and 8): which basic prototypes name critters, who stands around a tile, and what
/// happens to a critter the run made. <see cref="Critter"/> says whether a script's basic prototype number (the prototype file is the number + <see cref="AreaPrototypeItems.PrototypeOffset"/>) names a critter prototype the session can create;
/// without it a transform is refused. <see cref="CrittersNear"/> lists the NPCs of the shown map within <paramref name="reach"/> tiles of a tile on both axes, not destroyed or switched off, the dead included (the vicinity list of the
/// original, ledger TG4; the PC is the run's own); without it the vicinity is empty. <see cref="PcCell"/> is the PC's live cell. <see cref="CritterMade"/> is told each critter the run made at once, so the host adds it to its
/// world before the run's later requests (an attack names it as the attacker); <see cref="Attack"/> raises the attack event of the first entity on the second (the AI host's attack event with no fan-out, 0x4A9650 flags 2) and
/// answers what it did; without it a run with an attack is refused.
/// </summary>
public sealed record AreaThrownItemWorld(Func<int, bool>? Critter, Func<CellCoord, int, IReadOnlyList<string>>? CrittersNear, Func<CellCoord?> PcCell, Action<string>? CritterMade = null,
    Func<string, string, string>? Attack = null);

/// <summary>What a run of a thrown item's hit or miss script came to: the item has no script at the point (<see cref="NoScript"/>, nothing happened), the host has no script content (<see cref="ContentUnavailable"/>), the dispatcher
/// or the host refused the run (<see cref="Refused"/>, nothing applied: a script outside the admitted set, a request the host cannot take, an effect the host does not keep), or it ran and its requests were applied in order
/// (<see cref="Applied"/>; <see cref="Stopped"/> when one of them failed and the later ones were left, as the original's failed creation stops the action).</summary>
public enum AreaThrownItemOutcome { NoScript, ContentUnavailable, Refused, Applied, Stopped }

/// <summary>One run of a thrown item's script: the outcome, the point, the item, the dispatcher's result and a detail (identifiers and statuses only). <see cref="Critter"/> is the critter the item became, <see cref="Followed"/> whether the
/// run made it follow the PC and <see cref="Attacks"/> the attacks it raised (attacker, victim, what the host answered).</summary>
public sealed record AreaThrownItemRun(AreaThrownItemOutcome Outcome, int Point, string Item, ScrRunResult? Run, string Detail)
{
    public string? Critter { get; init; }
    public bool Followed { get; init; }
    public IReadOnlyList<(string Attacker, string Victim, string Detail)> Attacks { get; init; } = [];
}

/// <summary>
/// ARC-MAGIC-042 (T-m1, TI4–TI6): the run of the hit (7) or miss (8) script of the hero's thrown item at its landing. The original runs the weapon's point 7 on a hit and 8 on a miss with the weapon as the attachee, the attacker as
/// the triggerer and the target as the extra object, whatever the weapon's type (0x4B3770, and the range end 0x4B2870 for a flight that ended short); a thrown item lies on the landing tile before it runs. The installed users are the six
/// creature items (flag 0x20, prototypes 15086, 15112, 15136, 15153, 15192 and 15232): their scripts transform the item into a critter of a basic prototype (action 40), make it follow the PC with the follow check skipped (21), or, for
/// 29017, attack everyone in the vicinity (36).
/// <para>The set is <see cref="Admission"/> (<see cref="ScrAdmissionSet.S1"/> and the three actions, the single-object focus kinds and the vicinity kinds): a script outside it is refused whole. The host applies only what the run's object
/// requests ask for (<see cref="ScrObjectStateChange.Transform"/>, <see cref="ScrObjectStateChange.MakeFollow"/>, <see cref="ScrObjectStateChange.Attack"/>) and refuses a run that wrote anything else (a global flag, the story state), so nothing
/// half-applies before the first request is applied (what can still fail after it: the session refuses the transform or the follow, for example when its party records are full; the run then stops with <see cref="AreaThrownItemOutcome.Stopped"/> and what was applied stays). The attachment's own local flags and counters are not written back (the six installed scripts change none, and a transformed item is gone). The run applies in action order and reads the attachee as the critter once a
/// transform made one (the original makes the new object the run's attachee); a follow or an attack before a transform, a follow of anyone but the PC, and an attacker that is not the attachee are not modelled and refuse the run.</para>
/// <para>Limits of the first slice: the old art's rotation and the hit-point share are not carried (<see cref="GameSessionOwner.TransformPlacedItem"/>); only a critter prototype is made (the original makes any basic prototype); only
/// the hero's throw lands here; the created critter's own scripts (points 9, 19 and 26) are not run; a follower made this way has no companion body: it is in the party (led by the PC) but the AI host does not walk it after the hero. Caller-serialized; no Godot.</para>
/// </summary>
public sealed class AreaThrownItemScripts
{
    /// <summary>The commands a thrown item's script may use: <see cref="ScrAdmissionSet.S1"/> (control flow, the returns, local flags and counters, arithmetic, global flags and story state) plus transform (40), force follow (21) and
    /// attack (36) of <see cref="ScrAdmissionSet.S32"/>, the single-object focus kinds and everyone or anyone in the vicinity. A script that writes a global flag or the story state is refused when it runs (<see cref="Run"/>).</summary>
    public static ScrAdmissionSet Admission { get; } = new("S32-thrown-item", ScrAdmissionSet.S1.Conditions,
        ScrAdmissionSet.S1.Actions.Concat([ScrCatalogue.Action.TransformObject, ScrCatalogue.Action.MakeFollow, ScrCatalogue.Action.Attack]), ScrAdmissionSet.S1.ValueKinds,
        [ScrFocusKind.Triggerer, ScrFocusKind.Attachee, ScrFocusKind.ExtraObject, ScrFocusKind.Player, ScrFocusKind.EveryoneInVicinity, ScrFocusKind.AnyoneInVicinity], counterIndexWraps: true);

    private readonly GameSessionOwner _session;
    private readonly AreaItemScriptHost _items;
    private readonly AreaThrownItemWorld _world;
    private readonly int _shownMap;

    /// <param name="items">The item script host of the shown map's inventory (the attachments of map items and the game's scripts); the session's own slots (made items) are asked first.</param>
    /// <param name="shownMap">The shown map's runtime number: the map of the PC and the NPCs of a run.</param>
    public AreaThrownItemScripts(GameSessionOwner session, AreaItemScriptHost items, AreaThrownItemWorld world, int shownMap)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _items = items ?? throw new ArgumentNullException(nameof(items));
        _world = world ?? throw new ArgumentNullException(nameof(world));
        if (shownMap <= 0) throw new ArgumentOutOfRangeException(nameof(shownMap));
        _shownMap = shownMap;
    }

    /// <summary>The attachment of <paramref name="item"/> at <paramref name="point"/>, as a run reads it: the session's kept or prototype slot (a made item), else the loaded one of a map item; null without a script there.</summary>
    public ScrSlot? Slot(string item, int point)
    {
        if (_session.QueryItemScriptSlot(item, point) is { } kept) return new(kept.Script, kept.LocalFlags, kept.Counters);
        return _session.QueryPlacedItems().FirstOrDefault(placed => placed.Entity == item) is { } p ? _items.Slot(new ScrObjectRef((p.Origin ?? p.Map).RuntimeMap, item), point) : null;
    }

    /// <summary>
    /// Runs the script of <paramref name="item"/> at <paramref name="point"/> (<see cref="ScrPoint.Hit"/> or <see cref="ScrPoint.Miss"/>) for a throw of the hero that landed it on <paramref name="tile"/> (the item lies there, in the session's
    /// placed items), with <paramref name="target"/> as the extra object (null for a flight that ended short) and applies it. NoScript (nothing asked of the host) for an item with no script there.
    /// </summary>
    public AreaThrownItemRun Run(int point, string item, CellCoord tile, string? target = null)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (point is not (ScrPoint.Hit or ScrPoint.Miss)) throw new ArgumentOutOfRangeException(nameof(point));
        if (Slot(item, point) is not { Script: not 0 }) return new(AreaThrownItemOutcome.NoScript, point, item, null, "no script");
        if (_session.QueryPlacedItems().FirstOrDefault(placed => placed.Entity == item) is not { } placed)
            return new(AreaThrownItemOutcome.Refused, point, item, null, "the item does not lie on the ground");
        var itemRef = new ScrObjectRef((placed.Origin ?? placed.Map).RuntimeMap, item);
        var host = new Host(this, itemRef, tile);
        if (!host.ContentReady) return new(AreaThrownItemOutcome.ContentUnavailable, point, item, null, "no script content");
        ScrRunResult run;
        try
        {
            run = ScrDispatcher.RunObject(new(point, itemRef, new ScrObjectRef(_shownMap, AreaScriptHost.PcCode), target is null ? null : new ScrObjectRef(_shownMap, target)), host,
                new() { Rules = _session.Rules.Scripts, Admission = Admission });
        }
        catch (InvalidOperationException ex) { return new(AreaThrownItemOutcome.Refused, point, item, null, "the host could not answer: " + ex.Message); }
        if (run.Stop == ScrStop.NoScript) return new(AreaThrownItemOutcome.NoScript, point, item, run, "no script");
        if (run.Outcome == ScrOutcome.Refused || run.Stop is ScrStop.UnreadableScript or ScrStop.MissingScript or ScrStop.NotModelled)
            return new(AreaThrownItemOutcome.Refused, point, item, run, $"script {run.Script} {run.Stop}{(run.Refusal is { } refusal ? ": " + refusal.Code : "")}");
        var effects = run.Effects;
        // What the host keeps: the object requests. Anything else the run wrote has no home here (the six installed scripts write none): refused whole, nothing applied.
        if (effects.GlobalFlags.Count > 0 || effects.StoryState is not null || effects.DestroyTrapObject is not null)
            return new(AreaThrownItemOutcome.Refused, point, item, run, $"script {run.Script} wrote global state, which a thrown item's run does not keep");
        if (effects.ObjectStates.Count == 0) return new(AreaThrownItemOutcome.Applied, point, item, run, $"script {run.Script} {run.Outcome}: no request");
        return Apply(point, item, itemRef, run);
    }

    private AreaThrownItemRun Apply(int point, string item, ScrObjectRef itemRef, ScrRunResult run)
    {
        AreaThrownItemRun Refused(string why) => new(AreaThrownItemOutcome.Refused, point, item, run, $"script {run.Script}: {why}");
        var requests = run.Effects.ObjectStates;
        // The whole run is checked before anything is applied: the first request must be the transform of the attachee into a critter prototype, the rest name that critter (the attachee after it) as the follower or the attacker.
        var transformed = false;
        foreach (var request in requests)
        {
            switch (request.Change)
            {
                case ScrObjectStateChange.Transform:
                    if (transformed || request.Object != itemRef) return Refused("a transform of any other object, or a second one, is not modelled");
                    if (_world.Critter is not { } critter || !critter(request.Value)) return Refused($"prototype {request.Value} is no critter prototype the session can create");
                    transformed = true;
                    break;
                case ScrObjectStateChange.MakeFollow:
                    if (!transformed || request.Object != itemRef) return Refused("a follow of anything but the transformed attachee is not modelled");
                    if (request.Other is not { Code: AreaScriptHost.PcCode }) return Refused("a follow of anyone but the PC is not modelled");
                    break;
                case ScrObjectStateChange.Attack:
                    if (!transformed || request.Other != itemRef) return Refused("an attack by anything but the transformed attachee is not modelled");
                    if (_world.Attack is null) return Refused("no attack host");
                    break;
                default: return Refused($"request {request.Change} is not modelled for a thrown item");
            }
        }
        string? made = null; var followed = false; var attacks = new List<(string, string, string)>();
        foreach (var request in requests)
        {
            switch (request.Change)
            {
                case ScrObjectStateChange.Transform:
                {
                    var result = _session.TransformPlacedItem(_session.Version, item, request.Value + AreaPrototypeItems.PrototypeOffset);
                    if (result.Critter is not { } created)
                        return new(AreaThrownItemOutcome.Stopped, point, item, run, $"script {run.Script}: the transform did not happen ({result.Disposition} {result.Failure})");
                    made = created.Entity;
                    _world.CritterMade?.Invoke(made);
                    break;
                }
                case ScrObjectStateChange.MakeFollow:
                {
                    var joined = _session.ForceJoinParty(_session.Version, made!);
                    if (joined.Disposition == GameSessionDisposition.Rejected)
                        return new(AreaThrownItemOutcome.Stopped, point, item, run, $"script {run.Script}: the follow was refused ({joined.Failure})") { Critter = made, Attacks = attacks };
                    followed = true;
                    break;
                }
                case ScrObjectStateChange.Attack:
                {
                    var victim = request.Object.Code;
                    attacks.Add((made!, victim, _world.Attack!(made!, victim)));
                    break;
                }
            }
        }
        return new(AreaThrownItemOutcome.Applied, point, item, run, $"script {run.Script} {run.Outcome}: critter {made}, followed={followed}, attacks={attacks.Count}") { Critter = made, Followed = followed, Attacks = attacks };
    }

    // The run's world: the item's own attachment and the game's scripts (the item script host), the PC and the shown map's NPCs around the landing tile.
    private sealed class Host(AreaThrownItemScripts owner, ScrObjectRef item, CellCoord tile) : IScrHost, IScrWorldQueries
    {
        internal bool ContentReady => owner._items.HasContent;

        public ScrSlot? Slot(ScrObjectRef attachee, int point) => attachee == item ? owner.Slot(item.Code, point) : null;
        public ScrScriptLookup Script(int number) => owner._items.Script(number);
        public bool GlobalFlag(int index) => owner._items.GlobalFlag(index);
        public int StoryState() => owner._session.QueryStoryState();
        public bool IsMonsterGenerator(ScrObjectRef attachee) => false;
        public ScrDialogState DialogState(ScrObjectRef attachee) => ScrDialogState.Normal;
        public ScrTrapCheck CheckTrapBeforeUse(ScrObjectRef triggerer, ScrObjectRef attachee) => ScrTrapCheck.NoTrap;
        public bool IsTrapObject(ScrObjectRef attachee) => false;
        public ScrObjectRef? Player() => new ScrObjectRef(owner._shownMap, AreaScriptHost.PcCode);
        public bool IsPc(ScrObjectRef critter) => critter.Code == AreaScriptHost.PcCode;
        public ScrObjectRef? Leader(ScrObjectRef critter) => !IsPc(critter) && owner._session.QueryCompanion(critter.Code).LedByPc ? Player() : null;
        public bool IsJilted(ScrObjectRef critter) => !IsPc(critter) && owner._session.QueryCompanion(critter.Code).Jilted;

        public IReadOnlyList<ScrObjectRef> SceneryInVicinity(ScrObjectRef centre) => [];

        /// <summary>TG4: the shown map's PCs and NPCs within <see cref="ScrWorld.VicinityReach"/> tiles of the item's tile (the NPCs from the host's list, then the PC at its live cell).</summary>
        public IReadOnlyList<ScrObjectRef> CrittersInVicinity(ScrObjectRef centre)
        {
            if (centre != item || owner._world.CrittersNear is not { } near) return [];
            var list = near(tile, ScrWorld.VicinityReach).Select(entity => new ScrObjectRef(owner._shownMap, entity)).ToList();
            if (owner._world.PcCell() is { } pc && Math.Abs((long)pc.X - tile.X) <= ScrWorld.VicinityReach && Math.Abs((long)pc.Y - tile.Y) <= ScrWorld.VicinityReach)
                list.Add(new ScrObjectRef(owner._shownMap, AreaScriptHost.PcCode));
            return list;
        }

        public int Random(int low, int high) => owner._session.Roll("script.random", low, high).Roll?.Value ?? throw new InvalidOperationException("AREA_THROWN_ITEM_RANDOM_REFUSED");

        public (long X, long Y)? Location(ScrObjectRef obj) => obj == item ? (tile.X, tile.Y) : IsPc(obj) && owner._world.PcCell() is { } pc ? (pc.X, pc.Y) : null;
    }
}
