using Arcanum.Simulation.Goals;
using Arcanum.Simulation.Movement;

namespace Arcanum.World;

/// <summary>
/// ARC-DEV-145 (MIG13 S10a, GQ5 A, TM11): one typed request per player action on the hero, applied at once in single player with no
/// queue, returning a <see cref="HeroRequestResult"/>. The viewer turns clicks and buttons into requests and draws; the rules inside change
/// state directly, as the original's single-player branch does (SA34). The set grows with the slices: S10a has the tile click, the right
/// button's release and the turn.
/// </summary>
public abstract record HeroRequest;

/// <summary>
/// A left click on a tile: the hero walks, or runs by the always-run rule (0x4304C0). <paramref name="Ctrl"/> is the Ctrl key at the press. <paramref name="Held"/> marks a repeat of an input that is held down (the held button of
/// the original, the stick of the gamepad: ARC-DEV-147, Q7): the walk is the same starter, and the run it asks for is marked as a held walk. <paramref name="Shift"/> is either Shift key at the press: the original's
/// tile press does nothing then (0x54DE50, mode 0 and 3, GT107, SA47; ARC-INPUT-1936), so the request is <see cref="HeroRequestOutcome.Ignored"/> and sets no walk and no held flag.
/// </summary>
public sealed record WalkRequest(CellCoord Tile, bool Ctrl, bool Held = false, bool Shift = false) : HeroRequest;

/// <summary>The right button's release (0x54DE50, subtype 3), after its combat-mode or concealment part: interrupts the hero at level 3 and turns it toward <paramref name="Pointer"/>, the tile under the pointer (null when the pointer is off the game view or off the map).</summary>
public sealed record StopRequest(CellCoord? Pointer) : HeroRequest;

/// <summary>
/// ARC-DEV-147: a held input that asked for walks (the stick) is released: the hero's held walk ends where the hero is. The original's button release changes nothing and the hero walks on to its last tile (GT112);
/// a stick cannot ask that, so the release is Steamwoven's decision (the level-3 interrupt of the hero's walk, no turn).
/// </summary>
public sealed record ReleaseWalkRequest : HeroRequest;

/// <summary>
/// ARC-DEV-169 (MIG13 S11a, GS26 and GS27): a left click on an object. The host decides the goal the click builds (<see cref="HeroClickRules.Decide"/>) and starts it. S11a starts the talk (goal 21), S11b the use (goals 38 and 81); the other goals the decision names are
/// answered as <see cref="HeroRequestOutcome.Ignored"/> with the goal in <see cref="HeroRequestResult.Click"/> until their slices land. <paramref name="Target"/> is the target's session entity string; <paramref name="Ctrl"/> and
/// the object click always runs (GS27), whatever the Ctrl key says.
/// </summary>
public sealed record ObjectClickRequest(string Target, bool Alt = false, bool Shift = false) : HeroRequest;

/// <summary>The hero turns toward a tile through the rotate starter 0x433440 (goal 53).</summary>
public sealed record FaceRequest(CellCoord Toward) : HeroRequest;

/// <summary>ARC-DEV-182: the press in the game view with an item on the cursor (TT2, TT6): the hero throws <paramref name="Item"/> at <paramref name="Tile"/> (goal 37, no approach).</summary>
public sealed record ThrowRequest(string Item, CellCoord Tile) : HeroRequest;

/// <summary>ARC-DEV-183: a cast of the hero's spell whose magic run <paramref name="Run"/> the cast host built (0x57C110): the casting goal 26 starts with it.</summary>
public sealed record CastRequest(int Run, string? Target) : HeroRequest;

/// <summary>ARC-DEV-184: a Steal or Plant of <paramref name="Item"/> in the steal window of the victim named <paramref name="Victim"/>: the skill goal 33 starts with skill 5 (the plant flag ORed in).</summary>
public sealed record PickPocketRequest(string Victim, string Item, bool Plant) : HeroRequest;

/// <summary>ARC-DEV-185: a press on a door or window with armed lockpicks: the skill goal 33 starts with skill 14 and the lockpicks (the target is the portal's session entity).</summary>
public sealed record LockpickRequest(string Portal, string Item) : HeroRequest;

/// <summary>
/// ARC-DEV-365 (T-g, ledger IA8): the item cursor's apply on an object that needs a walk first (out of the spell's range, or an item with <c>OIF_NO_RANGED_USE</c>): the item use goal 39 starts with the item and the target.
/// <paramref name="Ctrl"/> is the Ctrl key at the press (the always-run rule); <paramref name="Shift"/> sets the no-walk flag (the hero acts from where it stands).
/// </summary>
public sealed record UseItemRequest(string Item, string Target, bool Ctrl, bool Shift) : HeroRequest;

/// <summary>ARC-DEV-365 (T-g, ledger IA9, IA11): the item cursor's apply of a trap device on a tile: the goal 41 starts with the item and the tile (walks next to the tile, then uses the device).</summary>
public sealed record UseItemOnTileRequest(string Item, CellCoord Tile) : HeroRequest;

/// <summary>ARC-INPUT-1946: what the Alt action 0x4445A0 did to its target.</summary>
public enum HeroImmediateResult
{
    /// <summary>The target is no item or body the action takes (an item in an inventory, nothing the viewer holds): nothing happened.</summary>
    None,
    /// <summary>The target was within one tile of the hero and was moved onto the hero's tile.</summary>
    Moved,
    /// <summary>The target lies farther than one tile away: the original tells its UI hook (0x4606C0, not modelled) and moves nothing.</summary>
    TooFar,
}

/// <summary>What a request did.</summary>
public enum HeroRequestOutcome
{
    /// <summary>The request changed the hero's action.</summary>
    Done,
    /// <summary>The request asked for what already holds (the same goal tile, the same facing).</summary>
    Unchanged,
    /// <summary>A run of the hero refused (a spell attempt at priority 4 or 5), so nothing below the refusal happened.</summary>
    Refused,
    /// <summary>A gate stopped the request (the hero cannot act, another participant has the turn, a main run goes on, no tile).</summary>
    Ignored,
}

/// <summary>The result of a request: its outcome and, where it chose them, the gait of a walk and the rotation of a turn.</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="Run">The walk is a run (always-run rule); false for the other requests.</param>
/// <param name="Rotation">The rotation 0..7 of a turn the request started; null without one.</param>
/// <param name="Verdict">The rotate starter's verdict where one was asked (a stop or a face), else <see cref="RotateStartVerdict.NoObject"/>.</param>
/// <param name="Click">The goal an <see cref="ObjectClickRequest"/> decided on (<see cref="HeroClickGoal.None"/> for the other requests, and for a click the rules refuse).</param>
/// <param name="Start">ARC-COMBAT-1949: how an object click's goal started (the start branch of 0x54ED30); null for the other requests and for a click the rules refused first.</param>
public sealed record HeroRequestResult(HeroRequestOutcome Outcome, bool Run = false, int? Rotation = null, RotateStartVerdict Verdict = RotateStartVerdict.NoObject, HeroClickGoal Click = HeroClickGoal.None, HeroClickStart? Start = null);

/// <summary>What the hero is, and does, now (the facts the request rules read).</summary>
/// <param name="Cell">The hero's cell on the map; null when it has none.</param>
/// <param name="Facing">The rotation 0..7 the hero's art shows.</param>
/// <param name="Active">0x45D790: the hero can act (not dead, unconscious, stunned, paralysed, stoned or lying down).</param>
/// <param name="HighestRunPriority">The highest priority of what the hero does: 0 nothing, 2 walking, 3 an ordinary action (an attack, an approach to talk, use, loot or pick up), 4 casting.</param>
/// <param name="InRound">A turn-based round runs.</param>
/// <param name="IsCurrentParticipant">The hero is the round's current participant.</param>
public readonly record struct HeroFacts(CellCoord? Cell, int Facing, bool Active, int HighestRunPriority, bool InRound, bool IsCurrentParticipant);

/// <summary>The hero's input settings the always-run rule reads.</summary>
/// <param name="AutoAttack">ARC-COMBAT-1949: the "auto attack" option (0x4B8230), which the click's start branch reads outside a round.</param>
public readonly record struct HeroInputOptions(bool AlwaysRunOption, bool NumLockRun, bool AutoAttack = false);

/// <summary>
/// ARC-COMBAT-1949 (VC62): what the click's start branch reads of the hero's runs for the goal it is about to start.
/// </summary>
/// <param name="EqualGoalQueued">An equal goal is queued (0x44E6F0): the bottom goal of a run of the hero is the goal or its alias, with the same objects.</param>
/// <param name="MainDepth">The main run's current-goal index (its goal count minus one); 0 with no main run in the table.</param>
/// <param name="MainTopIdle">The main run's top goal is 1 or 2 (0x4373F0).</param>
/// <param name="FidgetMain">The hero's first run is its main run and its bottom goal is the fidget 2 (0x44E830).</param>
public readonly record struct HeroStartContext(bool EqualGoalQueued, int MainDepth, bool MainTopIdle, bool FidgetMain);

/// <summary>
/// The hero's side of the request boundary: what a request needs from the world it is applied to. The viewer implements it over its
/// route, approach, attack and body hosts; S10b puts the walk, the interrupt and the turn on the goal table behind the same port.
/// </summary>
public interface IHeroActions
{
    HeroFacts Facts();
    HeroInputOptions Options();
    /// <summary>Sets the hero's walk goal to <paramref name="tile"/>, running when <paramref name="run"/>, replacing a walk already under way (retarget in place); false when the same goal was kept. <paramref name="held"/>: a repeat of a held input (see <see cref="WalkRequest.Held"/>).</summary>
    bool SetWalkTarget(CellCoord tile, bool run, bool held);
    /// <summary>Interrupts the hero's runs at level 3 (its walk, its approach intents, its attack); the caller has checked that none refuses.</summary>
    void InterruptOrdinary(string why);
    /// <summary>Starts goal 53, turning the hero to <paramref name="rotation"/> 45 degrees at a time.</summary>
    void StartTurn(int rotation);
    /// <summary>Ends the hero's held walk (the stick was released): its walk run is interrupted at level 3 and the step under way finishes; false when there was no walk to end or a run refused the interrupt.</summary>
    bool EndHeldWalk();
    /// <summary>ARC-DEV-169: the facts of an object click on <paramref name="target"/> (the class of the object, the hero's and the target's state) for the click decision.</summary>
    HeroClickFacts ClickFacts(string target, bool alt, bool shift) => new(false, false, false, false, false, HeroClickTarget.Other);
    /// <summary>ARC-INPUT-1946 (0x4445A0): the Alt click's immediate action on <paramref name="target"/>, a loose item or a body: within one tile of the hero (Chebyshev) it is moved onto the hero's tile with no offset, farther nothing moves; builds no goal and costs no action points.</summary>
    HeroImmediateResult ImmediateAction(string target) => HeroImmediateResult.None;
    /// <summary>ARC-DEV-169: starts the hero's talk goal 21 at <paramref name="target"/>, running when <paramref name="run"/> (ARC-AI-1137: with <paramref name="noWalk"/>, the click had Shift, the run flag 0x100 is set and no approach steps: GT122); false when the movement gate or a run's refusal stopped it.</summary>
    bool StartTalk(string target, bool run, bool noWalk) => false;
    /// <summary>ARC-DEV-174: starts the hero's use goal 38 (<paramref name="container"/>: 81) at <paramref name="target"/>, running when <paramref name="run"/>; false when the movement gate or a run's refusal stopped it.</summary>
    bool StartUse(string target, bool container, bool run) => false;
    /// <summary>ARC-DEV-175: starts the hero's pickup goal 15 at <paramref name="target"/> (an item on the ground), running when <paramref name="run"/>; false when the movement gate or a run's refusal stopped it.</summary>
    bool StartPickup(string target, bool run) => false;
    /// <summary>ARC-DEV-181: starts the hero's attack goal 18 at <paramref name="target"/> (a creature, or with Alt a door, container, scenery object or wall), running when <paramref name="run"/>; false when the movement gate or a run's refusal stopped it.</summary>
    bool StartAttack(string target, bool run) => false;
    /// <summary>ARC-DEV-270: starts the hero's attempt attack goal 19 at <paramref name="target"/> with the run flag 0x100 (the click had Shift, GS27): the hero swings from where it stands and walks no approach; false when the movement gate or a run's refusal stopped it.</summary>
    bool StartAttemptAttack(string target) => false;
    /// <summary>ARC-DEV-182: starts the hero's throw goal 37 of <paramref name="item"/> at <paramref name="tile"/>; false when the movement gate or a run's refusal stopped it.</summary>
    bool StartThrow(string item, CellCoord tile) => false;
    /// <summary>ARC-COMBAT-1949: what the start branch reads of the hero's runs for <paramref name="goal"/> at <paramref name="target"/> (the item: <paramref name="item"/> for a throw).</summary>
    HeroStartContext StartContext(HeroClickGoal goal, string? target, string? item = null) => default;
    /// <summary>ARC-COMBAT-1949: queues the click's goal <paramref name="goal"/> at <paramref name="target"/> beneath the hero's main run (0x44DBE0 -> 0x44D9B0); false when there is no such run or the table refused (nothing changed).</summary>
    bool QueueGoal(HeroClickGoal goal, string target) => false;
    /// <summary>ARC-COMBAT-1949: applies the click's run flags to the hero's existing main run (label 0x54FB19): Shift sets the no-walk flag 0x100, otherwise running 0x40 (refused at encumbrance 4); <paramref name="nearOccupied"/> (use 38 and 81) adds 0x4000. No-op with no main run in the table.</summary>
    void ApplyClickFlags(bool shift, bool nearOccupied) { }
    /// <summary>ARC-DEV-183: starts the hero's casting goal 26 for the magic run <paramref name="run"/>; false when a run's refusal stopped it.</summary>
    bool StartCast(int run, string? target) => false;
    /// <summary>ARC-DEV-184: starts the hero's skill goal 33 for a pick pocket (0x4350F0, skill 5); false when the gate or a run's refusal stopped it.</summary>
    bool StartPickPocket(string victim, string item, bool plant) => false;
    /// <summary>ARC-DEV-185: starts the hero's skill goal 33 for a lock pick (skill 14); false when the gate or a run's refusal stopped it.</summary>
    bool StartLockpick(string portal, string item) => false;
    /// <summary>ARC-DEV-365: starts the hero's item use goal 39 on <paramref name="target"/>, running when <paramref name="run"/>, with the no-walk flag when <paramref name="noWalk"/>; false when the gate or a run's refusal stopped it.</summary>
    bool StartUseItem(string item, string target, bool run, bool noWalk) => false;
    /// <summary>ARC-DEV-365: starts the hero's item use goal 41 of <paramref name="item"/> on <paramref name="tile"/>; false when the gate or a run's refusal stopped it.</summary>
    bool StartUseItemOnTile(string item, CellCoord tile) => false;
}

/// <summary>Applies <see cref="HeroRequest"/>s to the hero through <see cref="IHeroActions"/>.</summary>
public sealed class HeroRequestHost
{
    private readonly IHeroActions _hero;

    public HeroRequestHost(IHeroActions hero) => _hero = hero ?? throw new ArgumentNullException(nameof(hero));

    public HeroRequestResult Apply(HeroRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return request switch
        {
            WalkRequest walk => Walk(walk),
            StopRequest stop => Stop(stop),
            FaceRequest face => Face(face),
            ReleaseWalkRequest => Release(),
            ObjectClickRequest click => ObjectClick(click),
            ThrowRequest throwAt => Throw(throwAt),
            CastRequest cast => Cast(cast),
            PickPocketRequest steal => PickPocket(steal),
            LockpickRequest pick => Lockpick(pick),
            UseItemRequest use => UseItem(use),
            UseItemOnTileRequest onTile => UseItemOnTile(onTile),
            _ => throw new ArgumentException("Unknown hero request.", nameof(request)),
        };
    }

    private HeroRequestResult Walk(WalkRequest request)
    {
        // GT107: after the can-act test a Shift key (scan codes 0x2A, 0x36) returns at once: no walk, no run choice, no held flag.
        if (request.Shift) return new(HeroRequestOutcome.Ignored);
        var options = _hero.Options();
        var run = HeroInputRules.AlwaysRun(options.AlwaysRunOption, request.Ctrl, options.NumLockRun);
        return new(_hero.SetWalkTarget(request.Tile, run, request.Held) ? HeroRequestOutcome.Done : HeroRequestOutcome.Unchanged, run);
    }

    private HeroRequestResult Stop(StopRequest request)
    {
        var facts = _hero.Facts();
        // RR2: a hero that is not active (or lies down) skips every action of the release, the tail included.
        if (!facts.Active) return new(HeroRequestOutcome.Ignored);
        // The interrupt first: a run of priority 4 or 5 refuses it and ends the tail (RR5).
        if (!HeroInputRules.RightReleaseTailOf(facts.HighestRunPriority, facts.Cell, request.Pointer, facts.Facing, facts.Active, facts.InRound, facts.IsCurrentParticipant).Interrupted)
            return new(HeroRequestOutcome.Refused);
        var interrupted = facts.HighestRunPriority > 0;
        if (interrupted)
        {
            _hero.InterruptOrdinary("right release");
            // The end of an interrupted turn snaps the facing (GT102): the rotate starter then reads the art as it stands now.
            facts = _hero.Facts();
        }
        var tail = HeroInputRules.RightReleaseTailOf(0, facts.Cell, request.Pointer, facts.Facing, facts.Active, facts.InRound, facts.IsCurrentParticipant);
        if (tail.StartsTurn) _hero.StartTurn(tail.Rotation!.Value);
        var outcome = interrupted || tail.StartsTurn ? HeroRequestOutcome.Done : tail.Turn == RotateStartVerdict.AlreadyFacing ? HeroRequestOutcome.Unchanged : HeroRequestOutcome.Ignored;
        return new(outcome, Rotation: tail.StartsTurn ? tail.Rotation : null, Verdict: tail.Turn);
    }

    private HeroRequestResult UseItem(UseItemRequest request)
    {
        // The item use starter 0x435450 passes the start gate with its cost for a hero that can act (a PC passes); the goal starts as a click's does (GS27): at once without a main run, otherwise after the level-3 interrupt.
        var facts = _hero.Facts();
        if (!facts.Active) return new(HeroRequestOutcome.Ignored);
        if (HeroClickRules.StartOf(false, facts.InRound, facts.HighestRunPriority > 0, false, false) == HeroClickStart.Ignored) return new(HeroRequestOutcome.Ignored);
        var options = _hero.Options();
        var run = HeroInputRules.AlwaysRun(options.AlwaysRunOption, request.Ctrl, options.NumLockRun);
        return new(_hero.StartUseItem(request.Item, request.Target, run, request.Shift) ? HeroRequestOutcome.Done : HeroRequestOutcome.Refused, run);
    }

    private HeroRequestResult UseItemOnTile(UseItemOnTileRequest request)
    {
        var facts = _hero.Facts();
        if (!facts.Active) return new(HeroRequestOutcome.Ignored);
        if (HeroClickRules.StartOf(false, facts.InRound, facts.HighestRunPriority > 0, false, false) == HeroClickStart.Ignored) return new(HeroRequestOutcome.Ignored);
        return new(_hero.StartUseItemOnTile(request.Item, request.Tile) ? HeroRequestOutcome.Done : HeroRequestOutcome.Refused);
    }

    private HeroRequestResult Lockpick(LockpickRequest request)
    {
        if (!_hero.Facts().Active) return new(HeroRequestOutcome.Ignored);
        return new(_hero.StartLockpick(request.Portal, request.Item) ? HeroRequestOutcome.Done : HeroRequestOutcome.Refused);
    }

    private HeroRequestResult PickPocket(PickPocketRequest request)
    {
        // The skill starter's gate 0x4348E0 (GT141): a thief that can act; in a round only the critter whose turn it is.
        var facts = _hero.Facts();
        if (!facts.Active) return new(HeroRequestOutcome.Ignored);
        return new(_hero.StartPickPocket(request.Victim, request.Item, request.Plant) ? HeroRequestOutcome.Done : HeroRequestOutcome.Refused);
    }

    private HeroRequestResult Cast(CastRequest request)
    {
        // The cast starter (0x455C30) calls no start gate and replaces the hero's run (the generic starter's interrupt at the goal's own priority); a hero that cannot act has no cast.
        if (!_hero.Facts().Active) return new(HeroRequestOutcome.Ignored);
        return new(_hero.StartCast(request.Run, request.Target) ? HeroRequestOutcome.Done : HeroRequestOutcome.Refused);
    }

    private HeroRequestResult Throw(ThrowRequest request)
    {
        var facts = _hero.Facts();
        // TT2: a hero that cannot act puts the item back (the viewer does that); the goal needs no walk, so the start is the click's (GS27): a hero with no main run starts at once, otherwise its runs are interrupted at level 3.
        if (!facts.Active) return new(HeroRequestOutcome.Ignored);
        // ARC-COMBAT-1949: the throw takes the click's start branch too (an equal throw already queued is ignored with the option on; its flags are the keyboard's at the press, which the request does not carry).
        var context = _hero.StartContext(HeroClickGoal.None, null, request.Item);
        var start = HeroClickRules.ClickStartOf(_hero.Options().AutoAttack, facts.InRound, facts.HighestRunPriority > 0, context.EqualGoalQueued, false, context.MainDepth, context.MainTopIdle, context.FidgetMain);
        if (start is HeroClickStart.Ignored or HeroClickStart.FlagsOnly or HeroClickStart.QueueBeneath) return new(HeroRequestOutcome.Ignored, Start: start);
        return new(_hero.StartThrow(request.Item, request.Tile) ? HeroRequestOutcome.Done : HeroRequestOutcome.Refused, Start: start);
    }

    /// <summary>ARC-INPUT-1946: the goal the click rules would decide for a press on <paramref name="target"/> now, so a caller can tell the Alt action (<see cref="HeroClickGoal.Immediate"/>, which replaces nothing the hero does) before it clears what a goal would replace.</summary>
    public HeroClickGoal ClickGoalOf(string target, bool alt, bool shift) => HeroClickRules.Decide(_hero.ClickFacts(target, alt, shift)).Goal;

    private HeroRequestResult ObjectClick(ObjectClickRequest request)
    {
        var decision = HeroClickRules.Decide(_hero.ClickFacts(request.Target, request.Alt, request.Shift));
        // ARC-INPUT-1946 (0x4445A0): the Alt action builds no goal and runs no start branch; it moves the target at once.
        if (decision.Goal == HeroClickGoal.Immediate)
            return new(_hero.ImmediateAction(request.Target) == HeroImmediateResult.Moved ? HeroRequestOutcome.Done : HeroRequestOutcome.Ignored, Click: decision.Goal);
        if (decision.Goal is not (HeroClickGoal.Talk or HeroClickGoal.UseObject or HeroClickGoal.UseContainer or HeroClickGoal.Pickup or HeroClickGoal.Attack or HeroClickGoal.AttemptAttack)) return new(HeroRequestOutcome.Ignored, Click: decision.Goal);
        var facts = _hero.Facts();
        // GS27: after the decision, running is turned on (refused at encumbrance 4 by the walk itself); Shift sets the no-walk flag 0x100 instead (the attempt attack 19, ARC-DEV-270), and no run.
        // ARC-AI-1137 (GT122): the talk goal follows the same rule (the Shift branch replaces the running call).
        var run = decision.Goal != HeroClickGoal.AttemptAttack && !(decision.Goal == HeroClickGoal.Talk && request.Shift);
        // GS27 outside combat mode (a talk is never taken in a round: the click there is an attack): the goal starts at once without a main run, otherwise the hero's runs are interrupted at level 3 first, and the new goal starts.
        // ARC-COMBAT-1949 (VC62): the start branch reads the auto attack option, the equal goal queued and the main run's depth and top goal.
        var attack = decision.Goal is HeroClickGoal.Attack or HeroClickGoal.AttemptAttack;
        var context = _hero.StartContext(decision.Goal, request.Target);
        var start = HeroClickRules.ClickStartOf(_hero.Options().AutoAttack, facts.InRound, facts.HighestRunPriority > 0, context.EqualGoalQueued, attack, context.MainDepth, context.MainTopIdle, context.FidgetMain);
        if (start == HeroClickStart.QueueBeneath)
        {
            if (_hero.QueueGoal(decision.Goal, request.Target)) return new(HeroRequestOutcome.Done, run, Click: decision.Goal, Start: start);
            // The table refused (a full stack, the run being stepped) or the hero has no main run in the table to queue under (a route the viewer follows): nothing new is added, as when the stack is deep.
            start = HeroClickStart.FlagsOnly;
        }
        if (start == HeroClickStart.FlagsOnly)
        {
            _hero.ApplyClickFlags(request.Shift, decision.Goal is HeroClickGoal.UseObject or HeroClickGoal.UseContainer);
            return new(HeroRequestOutcome.Ignored, Click: decision.Goal, Start: start);
        }
        if (start == HeroClickStart.Ignored) return new(HeroRequestOutcome.Ignored, Click: decision.Goal, Start: start);
        var started = decision.Goal switch
        {
            HeroClickGoal.Talk => _hero.StartTalk(request.Target, run, request.Shift),
            HeroClickGoal.Pickup => _hero.StartPickup(request.Target, run),
            HeroClickGoal.Attack => _hero.StartAttack(request.Target, run),
            HeroClickGoal.AttemptAttack => _hero.StartAttemptAttack(request.Target),
            _ => _hero.StartUse(request.Target, decision.Goal == HeroClickGoal.UseContainer, run),
        };
        return new(started ? HeroRequestOutcome.Done : HeroRequestOutcome.Refused, run, Click: decision.Goal, Start: start);
    }

    private HeroRequestResult Release() =>
        new(_hero.EndHeldWalk() ? HeroRequestOutcome.Done : _hero.Facts().HighestRunPriority > 3 ? HeroRequestOutcome.Refused : HeroRequestOutcome.Unchanged);

    private HeroRequestResult Face(FaceRequest request)
    {
        var facts = _hero.Facts();
        if (facts.Cell is not { } from) return new(HeroRequestOutcome.Ignored);
        var rotation = Arcanum.Simulation.Combat.LineOfFireRules.Rotation(from, request.Toward);
        var verdict = HeroInputRules.RotateStart(new(true, facts.Facing, rotation, facts.Active, facts.InRound, facts.IsCurrentParticipant, HasMainRun: facts.HighestRunPriority > 0));
        if (verdict != RotateStartVerdict.Start) return new(verdict == RotateStartVerdict.AlreadyFacing ? HeroRequestOutcome.Unchanged : HeroRequestOutcome.Ignored, Verdict: verdict);
        _hero.StartTurn(rotation);
        return new(HeroRequestOutcome.Done, Rotation: rotation, Verdict: verdict);
    }
}
