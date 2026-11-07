using Arcanum.Simulation.Character;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Randomness;
using Arcanum.Simulation.Skills;

namespace Arcanum.Session;

/// <summary>
/// ARC-DEV-246 (MIG13 S14k, GT204–GT209): an NPC's pick pocket of gold (<see cref="GameSessionOwner.PickPocketByNpc"/>): the thief's life target, its map
/// data's advancement sheet and its effects' modifiers (the host's input, as <see cref="SessionNpcHeal"/>'s), the victim (an NPC's id, or
/// <see cref="SessionSpellCaster.Hero"/> for the PC), the gold prototype for a thief that holds no pile and the host's insert check for it. The
/// victim's measured facts are those of <see cref="SessionPickPocketVictim"/> (its effective perception, its state for the check's awareness
/// term, whether it is unaware of the thief); the gold's inventory art size (1 × 1 unless the host knows the art) joins the check's difficulty.
/// </summary>
public sealed record SessionNpcPickPocket(SessionLifeTarget Thief, SessionNpcAdvancement Loaded, AdvancementModifiers Modifiers, string Victim,
    SessionItemKind PileKind, SessionInsertCheck InsertCheck)
{
    public int Perception { get; init; }
    public SkillTargetConditions Conditions { get; init; }
    public bool Unaware { get; init; }
    public int ItemWidth { get; init; } = 1;
    public int ItemHeight { get; init; } = 1;
}

/// <summary>
/// What an NPC's pick pocket did (<see cref="GameSessionOwner.PickPocketByNpc"/>): the disposition and failure, the check, the coins that
/// moved (0 when nothing did), the move's own result (null when none ran), the victim's perception die (null when not rolled), whether the
/// failure was noticed (the host then runs the caught thief's consequences, <see cref="PickPocketRules.Caught"/>), and the version after the call.
/// </summary>
public sealed record SessionNpcPickPocketResult(GameSessionDisposition Disposition, GameSessionFailure Failure, SkillCheckResult? Check, int Moved,
    InventoryMutationResult? Move, int? PerceptionRoll, bool Noticed, GameSessionVersion Version);

public sealed partial class GameSessionOwner
{
    /// <summary>
    /// An NPC steals gold from the PC or from another NPC (script action 115 → the starter 0x4350F0 → goal 33 → 86 → the shared check and the pick-pocket
    /// case of 0x4C7160, the thief's type not tested: GT204–GT209). In order: the check (<see cref="SkillCheckRules.Run"/>) with the NPC's own pick-pocket
    /// level and training opposed by the victim's perception, the victim's awareness term, the gold pile's size (SC11), the thief's blindness; no fate (a
    /// PC's only) and no difficulty-setting term (the local PC's only). On a success min(coins, 100) of the victim's gold move
    /// (<see cref="GiveGoldToNpc"/> from the PC, <see cref="MoveGoldBetweenNpcs"/> from an NPC; the transfer's own refusals but a refused insert are
    /// refusals of the attempt, as the hero's); a move the insert check refuses is a failure never noticed. A failure is noticed per
    /// <see cref="PickPocketRules.Detection"/> (a critical failure always; an untrained thief when the victim's <c>random(1, 20)</c> is at most its
    /// perception). The rolls come from the session generator; the check and the move share the move's revision (one of their own when nothing moved).
    /// The original charges the thief's 4 action points in a round, makes no message, experience or reaction write, lowers the thief's alignment and sets the
    /// victim's "item stolen" mark: NOT READY here are the action-point charge (the AI host's round pays through its goal), the NPC's stored alignment
    /// (the session keeps none) and the mark. Refused before anything is rolled: a thief that is the hero, whose sheet or modifiers do not hold, that is not
    /// the sheet's NPC, or that is dead (<see cref="GameSessionFailure.PickPocketUnavailable"/>); a victim outside the bounds, the thief itself or a gold
    /// kind that is no gold (<see cref="GameSessionFailure.InvalidPickPocketVictim"/>); a victim the session does not know or one with no gold
    /// (<see cref="GameSessionFailure.ItemNotAvailable"/>); the version.
    /// </summary>
    public SessionNpcPickPocketResult PickPocketByNpc(GameSessionVersion expected, SessionNpcPickPocket steal)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(steal);
        ArgumentNullException.ThrowIfNull(steal.Thief);
        ArgumentNullException.ThrowIfNull(steal.Loaded);
        ArgumentNullException.ThrowIfNull(steal.Modifiers);
        ArgumentNullException.ThrowIfNull(steal.Victim);
        ArgumentNullException.ThrowIfNull(steal.InsertCheck);
        if (steal.Thief.IsHero || steal.Loaded.Entity != steal.Thief.Entity || !AutoLevelRules.IsValid(steal.Loaded.Sheet) || !AutoLevelRules.IsValid(steal.Modifiers))
            return RejectNpcPickPocket(GameSessionFailure.PickPocketUnavailable);
        if (Resolve(steal.Thief, out var thief, out var thiefFacts, out _) is var invalid && invalid != GameSessionFailure.None) return RejectNpcPickPocket(invalid);
        if (LifeRules.Evaluate(thief.State, thiefFacts!).IsDead) return RejectNpcPickPocket(GameSessionFailure.PickPocketUnavailable);
        var victimIsHero = steal.Victim == SessionSpellCaster.Hero;
        if (!victimIsHero && !SessionValidation.Entity(steal.Victim) || steal.Victim == steal.Thief.Entity || steal.PileKind.Type != SessionItemType.Gold ||
            steal.Perception is < -MaxPickPocketStat or > MaxPickPocketStat || (steal.Conditions & ~AllTargetConditions) != 0 ||
            steal.ItemWidth is < 0 or > MaxPickPocketCells || steal.ItemHeight is < 0 or > MaxPickPocketCells)
            return RejectNpcPickPocket(GameSessionFailure.InvalidPickPocketVictim);
        if (!IsCurrent(expected)) return RejectNpcPickPocket(GameSessionFailure.StaleVersion);
        int coins;
        if (victimIsHero)
        {
            if (_hero is null) return RejectNpcPickPocket(GameSessionFailure.PickPocketUnavailable);
            coins = _inventory.Gold;
        }
        else
        {
            if (!KnownNpc(steal.Victim)) return RejectNpcPickPocket(GameSessionFailure.ItemNotAvailable);
            coins = NpcPile(steal.Victim)?.Quantity ?? 0;
        }
        if (coins <= 0) return RejectNpcPickPocket(GameSessionFailure.ItemNotAvailable);

        var sheet = _advancement.Npcs.TryGetValue(steal.Loaded.Entity, out var levelled) ? levelled.Sheet : steal.Loaded.Sheet;
        var skill = QueryNpcSkills(steal.Loaded, steal.Modifiers)[Skill.PickPocket];
        var request = new SkillCheckRequest
        {
            Skill = Skill.PickPocket,
            Level = skill.Level,
            Training = skill.Training,
            Effectiveness = new()
            {
                ExtraordinaryIntelligence = AutoLevelRules.EffectiveStat(sheet, steal.Modifiers, CharacterStat.Intelligence) >= CharacterStatRules.ExtraordinaryThreshold,
                Difficulty = _difficulty,
                OpponentValue = steal.Perception,
            },
            Difficulty = new()
            {
                SourceInjuries = SourceInjuries(thief.State.Conditions),
                Target = true,
                TargetCritter = true,
                TargetConditions = steal.Conditions,
                TargetUnaware = steal.Unaware,
                ItemLocation = 0,
                ItemWidth = steal.ItemWidth,
                ItemHeight = steal.ItemHeight,
            },
        };
        var rolls = new SkillRolls(new SeededRandom(_random.Seed, _random.Draws));
        SkillCheckResult check;
        int? die = null;
        var noticed = false;
        try
        {
            check = SkillCheckRules.Run(request, rolls, _rules.Combat.InvocationModifierCountedTwice);
            switch (PickPocketRules.Detection(PickPocketDirection.Steal, request.Training, check.Success, check.Critical))
            {
                case PickPocketDetection.Noticed: noticed = true; break;
                case PickPocketDetection.PerceptionRoll:
                    if (!rolls.Trial.TryNext(1, PickPocketRules.PerceptionDie, out var roll)) throw new SkillRolls.ExhaustedException();
                    die = roll;
                    noticed = PickPocketRules.NoticedByRoll(roll, steal.Perception);
                    break;
            }
        }
        catch (SkillRolls.ExhaustedException) { return RejectNpcPickPocket(GameSessionFailure.RandomExhausted); }

        var amount = check.Success ? PickPocketRules.Amount(PickPocketItemKind.Gold, coins) : 0;
        // The check's draws are spent before the move, as the hero's attempt does; a refused move puts them back unless the revision moved meanwhile.
        var before = _random;
        var committed = _revision;
        _random = rolls.Trial;
        void PutBack() { if (_revision == committed) _random = before; }
        InventoryMutationResult? move = null;
        if (amount > 0)
        {
            try
            {
                move = victimIsHero
                    ? GiveGoldToNpc(Version, steal.Thief.Entity!, amount, steal.PileKind, steal.InsertCheck)
                    : MoveGoldBetweenNpcs(Version, steal.Victim, steal.Thief.Entity!, amount, steal.PileKind, steal.InsertCheck);
            }
            catch { PutBack(); throw; }
            // A move the insertion refuses is a failure that is never noticed (PP3); any other refusal is one the session cannot answer as the original does.
            if (move.Value.Disposition == GameSessionDisposition.Rejected && move.Value.Failure != GameSessionFailure.InsertRefused)
            {
                PutBack();
                return RejectNpcPickPocket(move.Value.Failure) with { Move = move };
            }
            if (move.Value.Disposition != GameSessionDisposition.Changed) amount = 0;
        }
        if (move is not { Disposition: GameSessionDisposition.Changed } && _revision == long.MaxValue)
        {
            PutBack();
            return RejectNpcPickPocket(GameSessionFailure.RevisionExhausted);
        }
        if (move is not { Disposition: GameSessionDisposition.Changed }) Revise();
        return new(GameSessionDisposition.Changed, GameSessionFailure.None, check, amount, move, die, noticed, Version);
    }

    private SessionNpcPickPocketResult RejectNpcPickPocket(GameSessionFailure failure) =>
        new(GameSessionDisposition.Rejected, failure, null, 0, null, null, false, Version);
}
