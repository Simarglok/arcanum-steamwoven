using Arcanum.Simulation.Character;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Skills;

namespace Arcanum.Session;

/// <summary>
/// One fate step of the hero (ARC-WORLD-054): the disposition, the failure, the rules' answer (null when the call was refused before
/// the rules ran), the full heal's life outcome when kind 0 was used, and the version after the call.
/// </summary>
public sealed record SessionFateResult(GameSessionDisposition Disposition, GameSessionFailure Failure, FateChange? Change,
    LifeOutcome? Heal, GameSessionVersion Version);

public sealed partial class GameSessionOwner
{
    /// <summary>The hero's fate points and armed fates (ARC-WORLD-054), or null without a hero. A new hero has none (the source's
    /// stat defaults, 0x4B0450).</summary>
    public FateState? QueryHeroFate()
    {
        ThrowIfDisposed();
        return _hero?.State.Fate;
    }

    /// <summary>Whether the hero's fate of <paramref name="kind"/> is armed (0x4F5090); false without a hero.</summary>
    public bool HeroFateArmed(FateKind kind)
    {
        ThrowIfDisposed();
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        return _hero is { } hero && FateRules.IsArmed(hero.State.Fate, kind);
    }

    /// <summary>
    /// The fate window's press (<see cref="FateRules.Arm"/>): one point is spent and the kind armed, or for the full heal the hero's
    /// hit-point damage, poison and fatigue damage become 0 at once (<see cref="LifeRules.FateFullHeal"/>) in the same revision.
    /// Refused, in order: no hero (<see cref="GameSessionFailure.FateUnavailable"/>), an undefined kind
    /// (<see cref="GameSessionFailure.InvalidFateKind"/>), the version, an armed kind or no point left
    /// (<see cref="GameSessionFailure.FateRefused"/>, the answer in <see cref="SessionFateResult.Change"/>), the full heal of a dead
    /// hero (<see cref="GameSessionFailure.FateRefused"/>, nothing spent; the original's dead PC cannot reach the window), the
    /// revision.
    /// </summary>
    public SessionFateResult ArmHeroFate(GameSessionVersion expected, FateKind kind)
    {
        ThrowIfDisposed();
        if (FatePrecheck(expected, kind) is { } refused) return refused;
        var change = FateRules.Arm(_hero!.State.Fate, kind);
        if (!change.Changed) return new(GameSessionDisposition.Rejected, GameSessionFailure.FateRefused, change, null, Version);
        LifeOutcome? heal = null;
        if (change.Status == FateStatus.UsedAtOnce)
        {
            var life = _life.Hero!.Value;
            heal = LifeRules.FateFullHeal(life.State, HeroLifeFacts(life.State));
            // Nothing is spent: the answer carries no change, only the refused heal.
            if (heal.Events.HasFlag(LifeEvents.AlreadyDead))
                return new(GameSessionDisposition.Rejected, GameSessionFailure.FateRefused, null, heal, Version);
        }
        if (_revision == long.MaxValue) return RejectFate(GameSessionFailure.RevisionExhausted);
        _hero.Apply(_hero.State.WithFate(change.After));
        // Every damage the heal removes goes to 0, so no life timer is due (fatigue, poison and resting timers start only on a
        // rise); a pending one finds nothing to do when it fires.
        if (heal is not null) _life.Hero = new(heal.After, _life.Hero!.Value.DeathTimeMs);
        Revise();
        return new(GameSessionDisposition.Changed, GameSessionFailure.None, change, heal, Version);
    }

    /// <summary>The fate window's release (<see cref="FateRules.Undo"/>): an armed kind is cleared and its point refunded (up to 100).
    /// Refused as <see cref="ArmHeroFate"/>; a kind that is not armed is <see cref="GameSessionFailure.FateRefused"/>.</summary>
    public SessionFateResult UndoHeroFate(GameSessionVersion expected, FateKind kind)
    {
        ThrowIfDisposed();
        if (FatePrecheck(expected, kind) is { } refused) return refused;
        return CommitFate(FateRules.Undo(_hero!.State.Fate, kind));
    }

    /// <summary>
    /// An event uses an armed fate (<see cref="FateRules.Use"/>, 0x4F5270): the magic lane's "save against magick" and "spell at
    /// maximum", a skill check's kind, a conversation start's "force good reaction". A kind that is not armed is
    /// <see cref="GameSessionDisposition.NoChange"/> with <see cref="FateStatus.NotArmed"/>: the event goes on unfated. Refused as
    /// <see cref="ArmHeroFate"/> before the rules.
    /// </summary>
    public SessionFateResult UseHeroFate(GameSessionVersion expected, FateKind kind)
    {
        ThrowIfDisposed();
        if (FatePrecheck(expected, kind) is { } refused) return refused;
        var change = FateRules.Use(_hero!.State.Fate, kind);
        return change.Changed ? CommitFate(change) : new(GameSessionDisposition.NoChange, GameSessionFailure.None, change, null, Version);
    }

    /// <summary>One more fate point (<see cref="FateRules.Grant"/>): the dialog action <c>fp</c> or the script command "grant one
    /// fate point". At 100 it is <see cref="GameSessionDisposition.NoChange"/>, as the source's stat setter clamps it.</summary>
    public SessionFateResult GrantHeroFatePoint(GameSessionVersion expected)
    {
        ThrowIfDisposed();
        if (_hero is null) return RejectFate(GameSessionFailure.FateUnavailable);
        if (!IsCurrent(expected)) return RejectFate(GameSessionFailure.StaleVersion);
        var change = FateRules.Grant(_hero.State.Fate);
        return change.Changed ? CommitFate(change) : new(GameSessionDisposition.NoChange, GameSessionFailure.None, change, null, Version);
    }

    /// <summary>The fate kind a hero's check of <paramref name="skill"/> would use when armed (<see cref="SkillCheckRules.Fate"/>), for
    /// the request's <see cref="SkillCheckRequest.FateArmed"/>; the caller uses it (<see cref="UseHeroFate"/>) when the result says
    /// <see cref="SkillCheckResult.Fated"/>.</summary>
    public FateKind? HeroSkillFate(Skill skill)
    {
        ThrowIfDisposed();
        return FateRules.Of(SkillCheckRules.Fate(skill)) is { } kind && HeroFateArmed(kind) ? kind : null;
    }

    private SessionFateResult? FatePrecheck(GameSessionVersion expected, FateKind kind)
    {
        if (_hero is null) return RejectFate(GameSessionFailure.FateUnavailable);
        if (!Enum.IsDefined(kind)) return RejectFate(GameSessionFailure.InvalidFateKind);
        if (!IsCurrent(expected)) return RejectFate(GameSessionFailure.StaleVersion);
        return null;
    }

    private SessionFateResult CommitFate(FateChange change)
    {
        if (!change.Changed) return new(GameSessionDisposition.Rejected, GameSessionFailure.FateRefused, change, null, Version);
        if (_revision == long.MaxValue) return RejectFate(GameSessionFailure.RevisionExhausted);
        _hero!.Apply(_hero.State.WithFate(change.After));
        Revise();
        return new(GameSessionDisposition.Changed, GameSessionFailure.None, change, null, Version);
    }

    // The attack's fate (0x4B3170 → 0x4F5270, ledger FT6): the resolver read the armed kinds; the one it used is cleared here and
    // the caller (AttackCritter) counts the revision.
    private void ClearUsedAttackFate(FateUse use)
    {
        var kind = use switch
        {
            FateUse.AttackerCriticalHit => FateKind.CriticalHit,
            FateUse.TargetCriticalMiss => FateKind.CriticalMiss,
            _ => (FateKind?)null,
        };
        if (kind is { } used && _hero is { } hero && FateRules.Use(hero.State.Fate, used) is { Changed: true } change)
            hero.Apply(hero.State.WithFate(change.After));
    }

    private SessionFateResult RejectFate(GameSessionFailure failure) => new(GameSessionDisposition.Rejected, failure, null, null, Version);
}
