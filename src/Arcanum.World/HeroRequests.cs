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

/// <summary>A left click on a tile: the hero walks, or runs by the always-run rule (0x4304C0). <paramref name="Ctrl"/> is the Ctrl key at the press.</summary>
public sealed record WalkRequest(CellCoord Tile, bool Ctrl) : HeroRequest;

/// <summary>The right button's release (0x54DE50, subtype 3), after its combat-mode or concealment part: interrupts the hero at level 3 and turns it toward <paramref name="Pointer"/>, the tile under the pointer (null when the pointer is off the game view or off the map).</summary>
public sealed record StopRequest(CellCoord? Pointer) : HeroRequest;

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
public sealed record HeroRequestResult(HeroRequestOutcome Outcome, bool Run = false, int? Rotation = null, RotateStartVerdict Verdict = RotateStartVerdict.NoObject);

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
    /// <summary>Sets the hero's walk goal to <paramref name="tile"/>, running when <paramref name="run"/>, replacing a walk already under way (retarget in place); false when the same goal was kept.</summary>
    bool SetWalkTarget(CellCoord tile, bool run);
    /// <summary>Interrupts the hero's runs at level 3 (its walk, its approach intents, its attack); the caller has checked that none refuses.</summary>
    void InterruptOrdinary(string why);
    /// <summary>Starts goal 53, turning the hero to <paramref name="rotation"/> 45 degrees at a time.</summary>
    void StartTurn(int rotation);
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
            _ => throw new ArgumentException("Unknown hero request.", nameof(request)),
        };
    }

    private HeroRequestResult Walk(WalkRequest request)
    {
        var options = _hero.Options();
        var run = HeroInputRules.AlwaysRun(options.AlwaysRunOption, request.Ctrl, options.NumLockRun);
        return new(_hero.SetWalkTarget(request.Tile, run) ? HeroRequestOutcome.Done : HeroRequestOutcome.Unchanged, run);
    }

    private HeroRequestResult Stop(StopRequest request)
    {
        var facts = _hero.Facts();
        // RR2: a hero that is not active (or lies down) skips every action of the release, the tail included.
        if (!facts.Active) return new(HeroRequestOutcome.Ignored);
        var tail = HeroInputRules.RightReleaseTailOf(facts.HighestRunPriority, facts.Cell, request.Pointer, facts.Facing, facts.Active, facts.InRound, facts.IsCurrentParticipant);
        if (!tail.Interrupted) return new(HeroRequestOutcome.Refused);
        // The interrupt is level 3 and the hero has already been tested active by the caller's gate (RR2); a hero with nothing running has nothing to end.
        var interrupted = facts.HighestRunPriority > 0;
        if (interrupted) _hero.InterruptOrdinary("right release");
        if (tail.StartsTurn) _hero.StartTurn(tail.Rotation!.Value);
        var outcome = interrupted || tail.StartsTurn ? HeroRequestOutcome.Done : tail.Turn == RotateStartVerdict.AlreadyFacing ? HeroRequestOutcome.Unchanged : HeroRequestOutcome.Ignored;
        return new(outcome, Rotation: tail.StartsTurn ? tail.Rotation : null, Verdict: tail.Turn);
    }

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
