using Arcanum.Simulation.Character;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Magic;

namespace Arcanum.Session;

/// <summary>
/// One spend or refund of the hero's character points (ARC-WORLD-052): the disposition, the failure, the rules' answer (null when
/// the call was refused before the rules ran) and the version after the call.
/// </summary>
public sealed record SessionPointResult(GameSessionDisposition Disposition, GameSessionFailure Failure, PointChange? Change,
    GameSessionVersion Version);

public sealed partial class GameSessionOwner
{
    // The hero's sheet when the character editor opened (0x5597C0): the floor of every refund while it stays open. Not saved: a
    // restored session starts with the editor closed, so nothing bought before the save is refundable after it.
    private AdvancementSheet? _editorFloor;

    /// <summary>Whether the hero's character editor is open (ARC-WORLD-052).</summary>
    public bool HeroEditorOpen { get { ThrowIfDisposed(); return _editorFloor is not null; } }

    /// <summary>The editor's floor, the hero's sheet when the editor opened, or null while it is closed.</summary>
    public AdvancementSheet? QueryHeroEditorFloor() { ThrowIfDisposed(); return _editorFloor; }

    /// <summary>
    /// Opens the hero's character editor (0x5597C0): takes the floor, the hero's sheet now (stats, pools, skill points, colleges,
    /// degrees). Refused without a hero or for a dead hero (<see cref="GameSessionFailure.HeroEditorUnavailable"/>, CS2); an open
    /// editor is <see cref="GameSessionDisposition.NoChange"/> and keeps its floor. Editor state is not saved, so the revision does
    /// not move.
    /// </summary>
    [Simulation.OriginalFunction(0x5597C0, Simulation.OriginalFunctionScope.Partial)]
    public GameSessionMutationResult OpenHeroEditor()
    {
        ThrowIfDisposed();
        if (_hero is null || HeroDead()) return new(GameSessionDisposition.Rejected, GameSessionFailure.HeroEditorUnavailable, Version);
        if (_editorFloor is not null) return new(GameSessionDisposition.NoChange, GameSessionFailure.None, Version);
        _editorFloor = AdvancementDomain.HeroSheet(_hero.State);
        return new(GameSessionDisposition.Changed, GameSessionFailure.None, Version);
    }

    /// <summary>Closes the editor and drops its floor: nothing spent so far can be refunded any more (owner rule 2026-10-20). A closed
    /// editor is <see cref="GameSessionDisposition.NoChange"/>.</summary>
    public GameSessionMutationResult CloseHeroEditor()
    {
        ThrowIfDisposed();
        if (_editorFloor is null) return new(GameSessionDisposition.NoChange, GameSessionFailure.None, Version);
        _editorFloor = null;
        return new(GameSessionDisposition.Changed, GameSessionFailure.None, Version);
    }

    /// <summary>
    /// Spends the hero's unspent points on one step of <paramref name="track"/> at <paramref name="index"/> while the editor is open
    /// (<see cref="CharacterPointRules.Spend"/> over the hero's sheet, its effects' changes and poison). Refused, in order: no hero
    /// or a dead one (<see cref="GameSessionFailure.HeroEditorUnavailable"/>; death does not close the editor); a closed editor (<see cref="GameSessionFailure.HeroEditorClosed"/>);
    /// an undefined track or click, or an index out of range (<see cref="GameSessionFailure.InvalidPointTarget"/>); the version; the rules'
    /// refusal (<see cref="GameSessionFailure.PointRefused"/>, the answer in <see cref="SessionPointResult.Change"/>; a pool stops at
    /// <see cref="SessionAdvancementLimits.MaxHeroPoolPoints"/>); the revision. Spell ranks learn through the spell book
    /// (<see cref="SpellRankLane"/>); tech degrees answer <see cref="PointChangeStatus.LaneNotReady"/> until their lane plugs in.
    /// </summary>
    public SessionPointResult SpendHeroPoint(GameSessionVersion expected, AdvancementTrack track, int index)
    {
        ThrowIfDisposed();
        if (PointPrecheck(expected, track, index) is { } refused) return refused;
        var hero = _hero!;
        return CommitPoint(CharacterPointRules.Spend(AdvancementDomain.HeroSheet(hero.State), PointModifiers(), track, index, SpellWillpower(),
            PointLane(track), SessionAdvancementLimits.MaxHeroPoolPoints));
    }

    /// <summary>
    /// Refunds one step of <paramref name="track"/> at <paramref name="index"/> down to the editor's floor
    /// (<see cref="CharacterPointRules.Refund"/>); <paramref name="click"/> sets how much one refund of a basic skill takes back
    /// (default: the original's click, until the level changes, stopping at the floor). Refused as <see cref="SpendHeroPoint"/>.
    /// </summary>
    public SessionPointResult RefundHeroPoint(GameSessionVersion expected, AdvancementTrack track, int index,
        SkillRefundClick click = SkillRefundClick.UntilLevelChanges)
    {
        ThrowIfDisposed();
        if (PointPrecheck(expected, track, index, click) is { } refused) return refused;
        var hero = _hero!;
        return CommitPoint(CharacterPointRules.Refund(AdvancementDomain.HeroSheet(hero.State), _editorFloor!, PointModifiers(), track, index, click,
            SpellWillpower(), PointLane(track)));
    }

    /// <summary>
    /// The lane of a track the pool does not buy itself: spell ranks (<see cref="AdvancementTrack.College"/>) are the spell book's
    /// (<see cref="SpellRankLane"/>, ARC-MAGIC-004); tech degrees (<see cref="AdvancementTrack.TechDiscipline"/>) are the technology
    /// lane's (TECH-2), which adds its <see cref="IPointLane"/> here; until then the pool answers <see cref="PointChangeStatus.LaneNotReady"/>.
    /// </summary>
    private IPointLane? PointLane(AdvancementTrack track) => track == AdvancementTrack.College ? new SpellRankLane(SpellWillpower()) : null;

    private SessionPointResult? PointPrecheck(GameSessionVersion expected, AdvancementTrack track, int index,
        SkillRefundClick click = SkillRefundClick.UntilLevelChanges)
    {
        if (_hero is null || HeroDead()) return RejectPoint(GameSessionFailure.HeroEditorUnavailable);
        if (_editorFloor is null) return RejectPoint(GameSessionFailure.HeroEditorClosed);
        if (!Enum.IsDefined(track) || index < 0 || index >= CharacterPointRules.Indices(track) || !Enum.IsDefined(click))
            return RejectPoint(GameSessionFailure.InvalidPointTarget);
        if (!IsCurrent(expected)) return RejectPoint(GameSessionFailure.StaleVersion);
        return null;
    }

    private SessionPointResult CommitPoint(PointChange change)
    {
        if (!change.Changed) return new(GameSessionDisposition.Rejected, GameSessionFailure.PointRefused, change, Version);
        var hero = _hero!;
        if (_revision == long.MaxValue) return RejectPoint(GameSessionFailure.RevisionExhausted);
        hero.Apply(AdvancementDomain.WithSheet(hero.State, change.Sheet, hero.State.Experience));
        _revision++;
        return new(GameSessionDisposition.Changed, GameSessionFailure.None, change, Version);
    }

    private SessionPointResult RejectPoint(GameSessionFailure failure) => new(GameSessionDisposition.Rejected, failure, null, Version);

    private AdvancementModifiers PointModifiers() =>
        AdvancementDomain.HeroModifiers(_hero!, _effects, _life.Hero is { } life ? life.State.PoisonLevel : 0);

    private System.Collections.Immutable.ImmutableArray<int> SpellWillpower() => _advancement.Rules?.SpellWillpower ?? AutoLevelRules.DefaultSpellWillpower;

    private bool HeroDead() => _life.Hero is { } life && LifeRules.Evaluate(life.State, HeroLifeFacts(life.State)).IsDead;
}
