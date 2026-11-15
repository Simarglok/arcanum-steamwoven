using Arcanum.Simulation.Character;
using Arcanum.Simulation.Items;
using Arcanum.Simulation.Randomness;
using Arcanum.Simulation.Skills;

namespace Arcanum.Session;

/// <summary>
/// A bet's inputs (ARC-UI-1740): the merchant's NPC, its Gambling level (the opposed value of the check), the merchant's item, the units that
/// move (a stack's all) and the unit price the hero pays for it on a loss (the barter quote), the kinds a new stack and a new coin pile take, and
/// the merchant's substitute container when the shown list is its. ARC-UI-2139: with <paramref name="WornItem"/> the item is one the merchant wears (the doll view's
/// lifted item): the move takes it off its slot (<see cref="GameSessionOwner.BarterBuy"/>'s worn move, over <paramref name="NpcWearer"/>), no container.
/// </summary>
public sealed record SessionGambleBet(string Npc, int MerchantLevel, string Item, int Count, int UnitPrice, SessionItemKind NewStackKind,
    SessionItemKind PileKind, string? Container = null, bool WornItem = false, SessionNpcWieldScripts? NpcWearer = null);

/// <summary>
/// One bet's outcome: the disposition and failure, the skill check (null when refused before it ran), whether the hero won, the item's move (the
/// barter buy at price 0 on a win, at the price on a loss), the coins the hero paid in all (0 on a win), whether the hero's gambling fate point was
/// used, and the version after the call.
/// </summary>
public sealed record SessionGambleResult(GameSessionDisposition Disposition, GameSessionFailure Failure, SkillCheckResult? Check, bool Won,
    InventoryMutationResult? Move, int Paid, bool FateUsed, GameSessionVersion Version)
{
    public bool Settled => Disposition == GameSessionDisposition.Changed;
}

public sealed partial class GameSessionOwner
{
    // ARC-UI-1740 (schema 101): the skill module's pre-thrown gambling roll 1..100, or null before the first one (SR12, PS14).
    private int? _gamblingRoll;

    /// <summary>ARC-UI-1740: the saved gambling roll the next bet's check will use, or null when none was thrown yet.</summary>
    public int? QueryGamblingRoll() { ThrowIfDisposed(); return _gamblingRoll; }

    /// <summary>
    /// ARC-UI-1740 (0x4C5BD0 draws the first roll when the skill module starts): throws the roll the next bet will use, so that it is saved
    /// before anyone can bet. The original draws it at the module's start, the first draw after the clock seed; the viewer's own session starts draw it first
    /// (ARC-UI-1807, <c>AreaTransitionCoordinator.CreateSession</c>), and a session that holds none (tests, smokes, a save of schema 100 or lower)
    /// draws it at the first barter window (<c>AreaBarter.Open</c>). One draw <c>random(1, 100)</c> of the session generator, one revision. A roll
    /// already thrown is no change.
    /// </summary>
    [Arcanum.Simulation.OriginalFunction(0x4C5BD0, Arcanum.Simulation.OriginalFunctionScope.Partial)]
    public GameSessionMutationResult EnsureGamblingRoll(GameSessionVersion expected)
    {
        ThrowIfDisposed();
        if (!IsCurrent(expected)) return Reject(GameSessionFailure.StaleVersion);
        if (_gamblingRoll is not null) return NoChange();
        if (_revision == long.MaxValue) return Reject(GameSessionFailure.RevisionExhausted);
        var trial = new SeededRandom(_random.Seed, _random.Draws);
        if (!trial.TryNext(1, 100, out var roll)) return Reject(GameSessionFailure.RandomExhausted);
        _random = trial;
        _gamblingRoll = roll;
        Revise();
        return new(GameSessionDisposition.Changed, GameSessionFailure.None, Version);
    }

    /// <summary>
    /// ARC-UI-1740 (0x579B60, ledger BT6): the hero bets the merchant's item. In order: the Gambling check of the hero against the merchant's
    /// Gambling level on the saved roll (<see cref="SkillCheckRules.Run"/>: the roll the check uses is the one saved, the check's own draw replaces
    /// it for the next bet, its critical draw is spent as for any check, an armed gambling fate point makes the check a success and is used); a win
    /// moves the item to the hero for nothing (the original's flags 9: the barter buy at price 0), a loss for the unit price (flags 0x11: the hero
    /// pays as for a buy). The reaction of the merchant is the caller's (a win costs it the item's worth, <see cref="GamblingRules.WonReaction"/>;
    /// a loss is an ordinary buy's, <see cref="Arcanum.Simulation.Items.TradeRules.GiftAmount"/>). The gate (<see cref="GamblingRules.Gate"/>) is the
    /// caller's too. A saved roll that does not exist yet is thrown first (<see cref="EnsureGamblingRoll"/>'s draw, in this call's revision).
    /// <para>The roll, the draws, the fate and the move are one revision: a move that is refused without changing anything (no room for the item, a script, a budget, the hero's
    /// coins for a lost stack) leaves the saved roll, the generator and the fate as they were, so a refused bet never rerolls. Refused before any
    /// effect: no living hero (<see cref="GameSessionFailure.GamblingUnavailable"/>), a merchant level outside 0..20, the version, and
    /// <see cref="BarterBuy"/>'s own refusals.</para>
    /// </summary>
    [Arcanum.Simulation.OriginalFunction(0x579B60, Arcanum.Simulation.OriginalFunctionScope.Partial)]
    public SessionGambleResult Gamble(GameSessionVersion expected, SessionGambleBet bet, SessionInsertCheck heroInsert, SessionInsertCheck npcInsert,
        ISessionItemScriptHost? scripts = null)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(bet);
        ArgumentNullException.ThrowIfNull(heroInsert);
        ArgumentNullException.ThrowIfNull(npcInsert);
        if (_hero is null || _life.Hero is null || HeroDead()) return RejectGamble(GameSessionFailure.GamblingUnavailable);
        if (bet.MerchantLevel is < 0 or > SkillRules.MaxLevel || bet.UnitPrice < 0 || bet.Count < 1) return RejectGamble(GameSessionFailure.InvalidItem);
        if (!IsCurrent(expected)) return RejectGamble(GameSessionFailure.StaleVersion);
        if (_revision == long.MaxValue) return RejectGamble(GameSessionFailure.RevisionExhausted);

        var hero = _hero!;
        var rolls = new SkillRolls(new SeededRandom(_random.Seed, _random.Draws));
        SkillCheckResult check;
        try
        {
            // A session that threw no roll draws it now, before the check's own draws.
            var stored = _gamblingRoll ?? rolls.D100(SkillCheckRoll.Check);
            var request = HeroSkillRequest(Skill.Gambling, null);
            request = request with { Effectiveness = request.Effectiveness with { OpponentValue = bet.MerchantLevel }, GamblingRoll = stored };
            check = SkillCheckRules.Run(request, rolls, _rules.Combat.InvocationModifierCountedTwice);
        }
        catch (SkillRolls.ExhaustedException) { return RejectGamble(GameSessionFailure.RandomExhausted); }

        var won = check.Success;
        // The draws and the new roll stand while the move runs (its item scripts draw after them); a refused move puts them back.
        var before = _random;
        var beforeRoll = _gamblingRoll;
        var committed = _revision;
        _random = rolls.Trial;
        _gamblingRoll = check.NextGamblingRoll;
        void PutBack() { if (_revision == committed) { _random = before; _gamblingRoll = beforeRoll; } }
        InventoryMutationResult move;
        try
        {
            move = BarterBuy(Version, bet.Npc, bet.Item, bet.Count, won ? 0 : bet.UnitPrice, bet.NewStackKind, bet.PileKind, heroInsert, npcInsert, scripts, bet.Container,
                wornItem: bet.WornItem, npcWearer: bet.NpcWearer);
        }
        catch { PutBack(); throw; }
        if (move.Disposition != GameSessionDisposition.Changed)
        {
            PutBack();
            return RejectGamble(move.Failure) with { Check = check, Won = won, Move = move };
        }
        // After the move (it may refuse) the fate joins its revision.
        var fateUsed = false;
        if (check.Fated && FateRules.Of(SkillCheckRules.Fate(Skill.Gambling)) is { } fate)
        {
            hero.Apply(hero.State.WithFate(FateRules.Use(hero.State.Fate, fate).After));
            fateUsed = true;
        }
        return new(GameSessionDisposition.Changed, GameSessionFailure.None, check, won, move, won ? 0 : checked(bet.UnitPrice * bet.Count), fateUsed, Version);
    }

    private SessionGambleResult RejectGamble(GameSessionFailure failure) => new(GameSessionDisposition.Rejected, failure, null, false, null, 0, false, Version);
}
