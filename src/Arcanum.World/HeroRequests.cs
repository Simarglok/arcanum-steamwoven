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
/// the original, the stick of the gamepad: ARC-DEV-147, Q7): the walk is the same starter, and the run it asks for is marked as a held walk.
/// </summary>
public sealed record WalkRequest(CellCoord Tile, bool Ctrl, bool Held = false) : HeroRequest;

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
public sealed record HeroRequestResult(HeroRequestOutcome Outcome, bool Run = false, int? Rotation = null, RotateStartVerdict Verdict = RotateStartVerdict.NoObject, HeroClickGoal Click = HeroClickGoal.None);

/// <summary>What the hero is, and does, now (the facts the request rules read).</summary>
/// <param name="Cell">The hero's cell on the map; null when it has none.</param>
/// <param name="Facing">The rotation 0..7 the hero's art shows.</param>
/// <param name="Active">0x45D790: the hero can act (not dead, unconscious, stunned, paralysed, stoned or lying down).</param>
/// <param name="HighestRunPriority">The highest priority of what the hero does: 0 nothing, 2 walking, 3 an ordinary action (an attack, an approach to talk, use, loot or pick up), 4 casting.</param>
/// <param name="InRound">A turn-based round runs.</param>
/// <param name="IsCurrentParticipant">The hero is the round's current participant.</param>
public readonly record struct HeroFacts(CellCoord? Cell, int Facing, bool Active, int HighestRunPriority, bool InRound, bool IsCurrentParticipant);

/// <summary>The hero's input settings the always-run rule reads.</summary>
public readonly record struct HeroInputOptions(bool AlwaysRunOption, bool NumLockRun);

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
    /// <summary>ARC-DEV-169: starts the hero's talk goal 21 at <paramref name="target"/>, running when <paramref name="run"/>; false when the movement gate or a run's refusal stopped it.</summary>
    bool StartTalk(string target, bool run) => false;
    /// <summary>ARC-DEV-174: starts the hero's use goal 38 (<paramref name="container"/>: 81) at <paramref name="target"/>, running when <paramref name="run"/>; false when the movement gate or a run's refusal stopped it.</summary>
    bool StartUse(string target, bool container, bool run) => false;
    /// <summary>ARC-DEV-175: starts the hero's pickup goal 15 at <paramref name="target"/> (an item on the ground), running when <paramref name="run"/>; false when the movement gate or a run's refusal stopped it.</summary>
    bool StartPickup(string target, bool run) => false;
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
            _ => throw new ArgumentException("Unknown hero request.", nameof(request)),
        };
    }

    private HeroRequestResult Walk(WalkRequest request)
    {
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

    private HeroRequestResult ObjectClick(ObjectClickRequest request)
    {
        var decision = HeroClickRules.Decide(_hero.ClickFacts(request.Target, request.Alt, request.Shift));
        if (decision.Goal is not (HeroClickGoal.Talk or HeroClickGoal.UseObject or HeroClickGoal.UseContainer or HeroClickGoal.Pickup)) return new(HeroRequestOutcome.Ignored, Click: decision.Goal);
        var facts = _hero.Facts();
        // GS27: after the decision, running is turned on (refused at encumbrance 4 by the walk itself); Shift would set the no-walk flag 0x100, which the hero's walk does not model yet.
        var run = true;
        // GS27 outside combat mode (a talk is never taken in a round: the click there is an attack): the goal starts at once without a main run, otherwise the hero's runs are interrupted at level 3 first, and the new goal starts.
        var start = HeroClickRules.StartOf(false, facts.InRound, facts.HighestRunPriority > 0, false, false);
        if (start == HeroClickStart.Ignored) return new(HeroRequestOutcome.Ignored, Click: decision.Goal);
        var started = decision.Goal switch
        {
            HeroClickGoal.Talk => _hero.StartTalk(request.Target, run),
            HeroClickGoal.Pickup => _hero.StartPickup(request.Target, run),
            _ => _hero.StartUse(request.Target, decision.Goal == HeroClickGoal.UseContainer, run),
        };
        return new(started ? HeroRequestOutcome.Done : HeroRequestOutcome.Refused, run, Click: decision.Goal);
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
