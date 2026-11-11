using Arcanum.Simulation;
using Arcanum.Simulation.Magic;

namespace Arcanum.Session;

/// <summary>
/// ARC-MAGIC-049 (technology audit E4-e, magic audit MA-9; the executable read NC1–NC3, ledger spell-runs): the Cast component (type 4, 0x451740). It starts another spell as a cast of the run's parent: the invocation 0x455A20 is made
/// with no source (so no item, no cost payer and no source of the new run), then the parent (+0x40) is the run's parent (run +0x58), the invocation's location of its source (+0x38) the parent's location, the target object (+0x70)
/// the current target object, the target tile (+0xD0) the current target tile (else the target object's location) and the flags (+0xDC) get bit 0x1, the cast without the casting goal (goal 73); nothing starts for a target with no object
/// and no tile. The dispatcher 0x455AC0 (single player: 0x455C30) checks the invocation (0x4564E0) and starts the run.
/// The session refuses a run start from inside the run rules, so the host keeps the request and starts it once the run rules are idle (<see cref="StartNestedCasts"/>, after the outer action or event pass, in order): the run's BEGIN
/// comes from its casting goal's release as an item executor's run does (<see cref="SessionItemTriggers"/>), through the host's goal start and release (the action points in a turn-based round, then BEGIN), or, for a parentless
/// run, as the event 2 ms later. The original starts the run inside the component; the order of the run slots, and so a run's id, can differ.
/// </summary>
public sealed partial class SessionSpellCaster
{
    /// <summary>The nested casts one start walks at most, those the started runs ask for included (a technical bound: a spell whose Cast component names itself makes one run a frame in the original, never ending).</summary>
    public const int MaxNestedCastsPerStart = 64;

    /// <summary>
    /// ARC-MAGIC-049: the combat world the goals of the nested casts play in (the action points a parent pays at the casting goal's start, 0x4B7CD0, in a turn-based round). None: the nested run is released at once with no action-point
    /// charge, the host having no combat world. The viewer sets the one its item triggers use.
    /// </summary>
    public ISessionCombatWorld? CastCombat { get; set; }

    // One nested cast, with the facts of the objects as they were when the component ran (the original starts the run there): the origin run's id, the spell, the run's parent, the target object and the tile.
    private sealed record NestedCast(int Run, int Spell, SpellObjectFacts? Parent, SpellObjectFacts? Target, SpellLocation Tile);

    private readonly List<NestedCast> _nestedCasts = [];
    private bool _startingNested;

    // 0x451740: a target with neither object nor tile starts nothing (the component returns); the tile is the target's (global 0x5E6D50), else its object's location (0x406DA0 with the location field). A parent that is not on the map
    // (a run whose caster is gone) casts nothing here. Without the Cast data (a record the reader gave none) the component is not ready.
    [OriginalFunction(0x451740, OriginalFunctionScope.Partial)]
    private SpellComponentOutcome? CastComponent(SpellRun run, SessionSpellComponent component, SpellRunTarget target)
    {
        if (component.Cast is not { } data) return null;
        var obj = target.Object is null ? null : Facts(target.Object.Id);
        // An object the host knows no facts of is no target it can cast at (the original holds its handle).
        if (target.Object is not null && obj is null) return SpellComponentOutcome.None;
        var tile = target.Tile.IsNone && obj is not null ? obj.Location : target.Tile;
        if (obj is null && tile.IsNone) return SpellComponentOutcome.None;
        var parent = run.Parent is null ? null : Facts(run.Parent.Id);
        if (run.Parent is not null && parent is null)
        {
            _log.Add(new(SpellCastEventKind.NestedCast, run.Id, data.Spell, target.Object?.Id, -1));
            return SpellComponentOutcome.None;
        }
        _nestedCasts.Add(new(run.Id, data.Spell, parent, obj, tile));
        return SpellComponentOutcome.None;
    }

    // The requests of the Cast components in order, each started with the run rules idle; the runs they start may ask for more, which join the end of the list, up to the bound. A call from inside a start only adds to it.
    private void StartNestedCasts()
    {
        if (_startingNested || _nestedCasts.Count == 0) return;
        _startingNested = true;
        try
        {
            for (var walked = 0; _nestedCasts.Count > 0; walked++)
            {
                if (walked == MaxNestedCastsPerStart)
                {
                    _log.Add(new(SpellCastEventKind.NestedCastsDropped, -1, _nestedCasts.Count));
                    _nestedCasts.Clear();
                    break;
                }
                var next = _nestedCasts[0];
                _nestedCasts.RemoveAt(0);
                StartNestedCast(next);
            }
        }
        catch
        {
            // A host that throws ends the start: nothing left over starts on a later, unrelated action.
            _nestedCasts.Clear();
            throw;
        }
        finally { _startingNested = false; }
    }

    private void StartNestedCast(NestedCast cast)
    {
        if (_records(cast.Spell) is not { } record)
        {
            _log.Add(new(SpellCastEventKind.NestedCast, cast.Run, cast.Spell, cast.Target?.Id, -1));
            return;
        }
        var parentId = cast.Parent?.Id;
        var round = _owner.QueryCombatRound();
        var invocation = new SpellInvocation(cast.Spell, cast.Parent, null, cast.Target, cast.Tile, null, TeleportAllowed: parentId is null || TeleportAllowedFor(cast.Spell, parentId), ParentIsLocalPlayer: parentId == Hero,
            TurnBased: round is not null, TurnBasedActor: round is null ? null : round.Actor.Entity ?? Hero)
        { SourceLocation = cast.Parent?.Location ?? SpellLocation.None };
        var started = _owner.StartSpellRun(_owner.Version, new(invocation, SkipCastGoal: true), record.Run, InvocationWorld(), _owner.SpellRolls);
        if (started.Disposition != GameSessionDisposition.Changed || started.Creation?.Run is not { } run)
        {
            // ARC-MAGIC-038 (0x4564E0): the anti-teleport refusal shows message 10000 whenever the parent is the local player.
            if (started.Creation?.Check is { ShowsTeleportMessage: true }) _log.Add(new(SpellCastEventKind.Message, -1, TeleportRefusedMessage));
            _log.Add(new(SpellCastEventKind.NestedCast, cast.Run, cast.Spell, cast.Target?.Id, -1));
            return;
        }
        _itemChains.Remove(run.Id);
        _log.Add(new(SpellCastEventKind.NestedCast, cast.Run, cast.Spell, cast.Target?.Id, run.Id));
        // A run with a parent waits for its goal 73's release frame; one without is begun by its event.
        if (run.Parent is not null) PlayNestedGoal(run.Id);
    }

    // Goal 73 (CG1) has the casting goal's start without its end node, and the art plays no frames here: the start (action points, the target check), then the release, which begins the run. A start that refuses leaves the original's run in
    // its slot for good (IT10, a slot leak); both rule modes free it unpaid, as the item executors' runs are.
    private void PlayNestedGoal(int run)
    {
        if (CastCombat is not { } combat)
        {
            Release(run);
            return;
        }
        if (StartGoal(run, combat).Outcome == SpellCastGoalOutcome.Started) Release(run);
        else Act(new(SessionSpellRunActionKind.Free, run));
    }
}
