using Arcanum.Simulation.Character;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Randomness;
using Arcanum.Simulation.Skills;

namespace Arcanum.Session;

/// <summary>
/// The victim of a pick-pocket attempt (ARC-SKILLS-010) as the host measures it: the NPC; its effective perception (the opponent of the
/// opposed check and the target of the perception die, 0x45F060); its effective alignment (the theft's alignment change, PP5); its state
/// for the check's awareness term (paralysed, unconscious, falling, stunned, asleep); whether it is unaware of the hero (not in combat
/// mode and it neither sees nor hears the hero at normal loudness, SC6); and whether the hero summoned it (spell flag 0x4000 with the
/// hero as the caster: no alignment change).
/// </summary>
public sealed record SessionPickPocketVictim(string Npc)
{
    public int Perception { get; init; }
    public int Alignment { get; init; }
    public SkillTargetConditions Conditions { get; init; }
    public bool Unaware { get; init; }
    public bool SummonedByHero { get; init; }
}

/// <summary>
/// One pick-pocket attempt (ARC-SKILLS-010): the disposition, the failure, the action-point charge of a turn-based round (null outside
/// one or without a world), the check, how much the success moved (coins, units or 1; 0 when nothing moved), the move's own result
/// (null when no move ran), the victim's perception die (null when not rolled), whether the failure was noticed (the host then runs the
/// caught thief's consequences, <see cref="PickPocketRules.Caught"/>), the hero's stored alignment after a theft that changed it (null
/// otherwise), whether the pick-pocket fate was used, and the version after the call.
/// </summary>
public sealed record SessionPickPocketResult(GameSessionDisposition Disposition, GameSessionFailure Failure, SessionCombatResult? Charge,
    SkillCheckResult? Check, int Moved, InventoryMutationResult? Move, int? PerceptionRoll, bool Noticed, int? Alignment, bool FateUsed,
    GameSessionVersion Version);

public sealed partial class GameSessionOwner
{
    /// <summary>
    /// The hero steals an item from a living NPC on the actor's map, or plants one of its own on it (the steal-mode drags 0x575180 and
    /// 0x575100 → the use-skill goal → the shared check and the pick-pocket case of 0x4C7160, ledger PP2–PP8). In order:
    /// <list type="number">
    /// <item>With a world and a turn-based round, another's subturn or an inactive hero is refused
    /// (<see cref="GameSessionFailure.PickPocketOutOfTurn"/>); in the hero's subturn 4 action points are charged
    /// (<see cref="ConsumeActionPoints"/>, its own revision), and a charge the hero cannot pay ends its subturn and refuses the attempt
    /// (the same failure, with the charge).</item>
    /// <item>The check (<see cref="SkillCheckRules.Run"/>): the hero's pick-pocket level and training opposed by the victim's perception,
    /// the victim's awareness term, the item's worn slot and its size in inventory cells (SC11), the hero's injuries, the armed fate
    /// (which forces a critical success).</item>
    /// <item>On a success the item moves (PP3): coins min(quantity, 100) of an NPC's gold pile, ammunition units min(quantity, 20), any
    /// other item whole, through <see cref="TakeGoldFromNpc"/>, <see cref="TakeAmmoFromNpc"/>, <see cref="TakeItemFromNpc"/>,
    /// <see cref="GiveAmmoToNpc"/> or <see cref="GiveItemToNpc"/>. A move the host's insert check refuses, or that an item script
    /// declines, is a failure that is never noticed. A theft that moved something lowers the hero's stored alignment
    /// (<see cref="PickPocketRules.AlignmentAfterTheft"/>, the followers armed as for any alignment change) unless the hero summoned the
    /// victim; a plant changes none.</item>
    /// <item>On a failed check nothing moves; the failure is noticed per <see cref="PickPocketRules.Detection"/>, an untrained hero's
    /// through the victim's <c>random(1, 20)</c> against its perception.</item>
    /// </list>
    /// The rolls come from the session generator (the check's two d100, then the die); the check, the move, the alignment and the fate
    /// share the move's revision (one of their own when nothing moved). Not modelled: the victim's "item stolen" mark (critter flags 2
    /// bit 0x01, PP6: its one reader found is a PC victim's own inventory window, and the session has no NPC thief), the plant of the
    /// hero's coins (the hero's gold is a count, not a pile), experience (none on any branch, PP6). Refused before anything is charged
    /// or rolled: no hero or a dead one (<see cref="GameSessionFailure.PickPocketUnavailable"/>), victim facts outside their bounds, a
    /// victim that is no NPC of the actor's map seed or is dead (<see cref="GameSessionFailure.InvalidPickPocketVictim"/>), the version, no
    /// seed for the actor's map (<see cref="GameSessionFailure.InventoryUnavailable"/>), an item the victim (steal) or the hero (plant) does
    /// not hold, or the hero's coins (<see cref="GameSessionFailure.ItemNotAvailable"/>), a gold pile with an insert-item script
    /// (<see cref="GameSessionFailure.ItemScriptUnsupported"/>), an item the victim wears (the original steals it; unequipping an NPC
    /// is not modelled: <see cref="GameSessionFailure.WornItemUnsupported"/>), a key or key ring (<see cref="GameSessionFailure.ItemTypeUnsupported"/>),
    /// an ammunition move without an ammunition kind for a new stack
    /// (<see cref="GameSessionFailure.InvalidAmmo"/>). A move refused for any reason but the insert check (a script the session does not
    /// run, a budget, the revision) refuses the attempt after the charge, with nothing rolled or moved.
    /// </summary>
    [Arcanum.Simulation.OriginalFunction(0x575180, Arcanum.Simulation.OriginalFunctionScope.Partial)]
    [Arcanum.Simulation.OriginalFunction(0x575100, Arcanum.Simulation.OriginalFunctionScope.Partial)]
    public SessionPickPocketResult PickPocket(GameSessionVersion expected, SessionPickPocketVictim victim, PickPocketDirection direction, string item,
        SessionItemKind newStackKind, SessionInsertCheck insertCheck, ISessionCombatWorld? world = null, ISessionItemScriptHost? scripts = null,
        ISessionItemTriggers? triggers = null)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(victim);
        ArgumentNullException.ThrowIfNull(insertCheck);
        if (!Enum.IsDefined(direction)) throw new ArgumentOutOfRangeException(nameof(direction));
        if (_hero is null || _life.Hero is null || HeroDead()) return RejectPickPocket(GameSessionFailure.PickPocketUnavailable);
        if (!SessionValidation.Entity(victim.Npc) || victim.Npc.StartsWith(SessionMadeItem.Prefix, StringComparison.Ordinal) ||
            victim.Perception is < -MaxPickPocketStat or > MaxPickPocketStat || victim.Alignment is < SessionHeroLimits.MinAlignment or > SessionHeroLimits.MaxAlignment ||
            (victim.Conditions & ~AllTargetConditions) != 0)
            return RejectPickPocket(GameSessionFailure.InvalidPickPocketVictim);
        if (!SessionValidation.Entity(item)) return RejectPickPocket(GameSessionFailure.InvalidItem);
        if (!IsCurrent(expected)) return RejectPickPocket(GameSessionFailure.StaleVersion);
        var map = _actor.Map;
        if (QueryMapInventory(map) is not { } current) return RejectPickPocket(GameSessionFailure.InventoryUnavailable);
        if (current.Holders.FirstOrDefault(holder => holder.Entity == victim.Npc) is not { Kind: SessionHolderKind.Npc } holder || holder.Dead)
            return RejectPickPocket(GameSessionFailure.InvalidPickPocketVictim);

        // The item, its kind and quantity, its location and size for the check.
        PickPocketItemKind kind;
        int quantity, location, width, height;
        if (direction == PickPocketDirection.Steal)
        {
            if (holder.Items.FirstOrDefault(entry => entry.Entity == item) is not { } listed) return RejectPickPocket(GameSessionFailure.ItemNotAvailable);
            var facts = listed.Facts;
            kind = facts.Kind.Type == SessionItemType.Gold ? PickPocketItemKind.Gold : facts.AmmoType is not null ? PickPocketItemKind.Ammunition : PickPocketItemKind.Item;
            if (kind == PickPocketItemKind.Gold && facts.HasScript(SessionScriptPoint.InsertItem)) return RejectPickPocket(GameSessionFailure.ItemScriptUnsupported);
            // The original steals a worn item too (PP3, its slot only raises the difficulty); the session does not unequip an NPC.
            if (facts.Worn) return RejectPickPocket(GameSessionFailure.WornItemUnsupported);
            // Keys and key rings are not modelled in the session's lists (the transfer would refuse them after the rolls).
            if (kind == PickPocketItemKind.Item && !InventoryDomain.Admitted(facts.Kind.Type)) return RejectPickPocket(GameSessionFailure.ItemTypeUnsupported);
            quantity = kind == PickPocketItemKind.Ammunition ? QueryAmmo(item)?.Units ?? facts.Quantity ?? 0 : facts.Quantity ?? 1;
            location = facts.InventoryLocation;
            (width, height) = (facts.InventoryWidth, facts.InventoryHeight);
        }
        else
        {
            if (HeroHeld().Where(held => held.Entity == item).Select(held => (SessionItemKind?)held.Kind).FirstOrDefault() is not { } held)
                return RejectPickPocket(GameSessionFailure.ItemNotAvailable);
            kind = held.Type == SessionItemType.Ammo ? PickPocketItemKind.Ammunition : PickPocketItemKind.Item;
            quantity = kind == PickPocketItemKind.Ammunition ? QueryAmmo(item)?.Units ?? 0 : 1;
            location = _equipment.SlotOf(item) is { } slot ? (int)slot : 0;
            var cells = _inventory.Grid.TryGetValue(item, out var placed) ? (placed.Width, placed.Height) : (1, 1);
            (width, height) = cells;
        }
        if (kind == PickPocketItemKind.Ammunition && newStackKind.Type != SessionItemType.Ammo) return RejectPickPocket(GameSessionFailure.InvalidAmmo);

        SessionCombatResult? charge = null;
        if (world is not null && _combat.Round is not null)
        {
            // The goal needs the hero's turn (PP2): another's subturn or an inactive hero is refused before any charge.
            if (QueryCombatActionGate(SessionCombatant.Hero, TurnBasedRules.UseItemCost) is SessionCombatGate.NotTheirTurn or SessionCombatGate.Inactive)
                return RejectPickPocket(GameSessionFailure.PickPocketOutOfTurn);
            if (_revision >= long.MaxValue - 1) return RejectPickPocket(GameSessionFailure.RevisionExhausted);
            charge = ConsumeActionPoints(Version, SessionCombatant.Hero, TurnBasedRules.UseItemCost, world);
            if (charge.Disposition == GameSessionDisposition.Rejected) return RejectPickPocket(charge.Failure) with { Charge = charge };
            // Too few action points end the subturn and the check node does not run the check (0x42A359, PP11).
            if (charge.Charge == SessionActionPointUse.Refused) return RejectPickPocket(GameSessionFailure.PickPocketOutOfTurn) with { Charge = charge };
        }
        return PickPocketStep(victim, direction, item, kind, quantity, location, width, height, newStackKind, insertCheck, scripts, triggers) with { Charge = charge };
    }

    private SessionPickPocketResult PickPocketStep(SessionPickPocketVictim victim, PickPocketDirection direction, string item, PickPocketItemKind kind,
        int quantity, int location, int width, int height, SessionItemKind newStackKind, SessionInsertCheck insertCheck, ISessionItemScriptHost? scripts,
        ISessionItemTriggers? triggers)
    {
        var hero = _hero!;
        var request = HeroSkillRequest(Skill.PickPocket, null);
        request = request with
        {
            Effectiveness = request.Effectiveness with { OpponentValue = victim.Perception },
            Difficulty = request.Difficulty with
            {
                Target = true,
                TargetCritter = true,
                TargetConditions = victim.Conditions,
                TargetUnaware = victim.Unaware,
                ItemLocation = location,
                // A size the art does not give (0x461780 wraps a 0-pixel side) is past any effectiveness; the check's bound is 64.
                ItemWidth = Math.Clamp(width, 0, MaxPickPocketCells),
                ItemHeight = Math.Clamp(height, 0, MaxPickPocketCells),
            },
        };
        var rolls = new SkillRolls(new SeededRandom(_random.Seed, _random.Draws));
        SkillCheckResult check;
        int? die = null;
        var noticed = false;
        try
        {
            check = SkillCheckRules.Run(request, rolls, _rules.Combat.InvocationModifierCountedTwice);
            switch (PickPocketRules.Detection(direction, request.Training, check.Success, check.Critical))
            {
                case PickPocketDetection.Noticed: noticed = true; break;
                case PickPocketDetection.PerceptionRoll:
                    if (!rolls.Trial.TryNext(1, PickPocketRules.PerceptionDie, out var roll)) throw new SkillRolls.ExhaustedException();
                    die = roll;
                    noticed = PickPocketRules.NoticedByRoll(roll, victim.Perception);
                    break;
            }
        }
        catch (SkillRolls.ExhaustedException) { return RejectPickPocket(GameSessionFailure.RandomExhausted); }

        var amount = check.Success ? PickPocketRules.Amount(kind, quantity) : 0;
        // The check's draws are spent before the move, whose item scripts and triggers draw after them; a refused move puts them back
        // unless a host's call back into the owner committed state meanwhile (its draws then stay).
        var before = _random;
        var committed = _revision;
        _random = rolls.Trial;
        void PutBack() { if (_revision == committed) _random = before; }
        InventoryMutationResult? move = null;
        var moved = false;
        if (amount > 0)
        {
            var npc = victim.Npc;
            try
            {
                move = (direction, kind) switch
                {
                    (PickPocketDirection.Steal, PickPocketItemKind.Gold) => TakeGoldFromNpc(Version, npc, amount, item),
                    (PickPocketDirection.Steal, PickPocketItemKind.Ammunition) => TakeAmmoFromNpc(Version, npc, item, amount, newStackKind, insertCheck),
                    (PickPocketDirection.Steal, _) => TakeItemFromNpc(Version, npc, item, insertCheck, scripts, triggers),
                    (_, PickPocketItemKind.Ammunition) => GiveAmmoToNpc(Version, npc, item, amount, newStackKind, insertCheck),
                    _ => GiveItemToNpc(Version, npc, item, insertCheck, triggers),
                };
            }
            catch { PutBack(); throw; }
            var result = move.Value;
            moved = result.Disposition == GameSessionDisposition.Changed && result.Item is not null;
            // A move the insertion refuses or a script declines is a failure that is never noticed (PP3); any other refusal is one the
            // session cannot answer as the original does, so the attempt is refused with nothing rolled.
            if (result.Disposition == GameSessionDisposition.Rejected && result.Failure != GameSessionFailure.InsertRefused)
            {
                PutBack();
                return RejectPickPocket(result.Failure) with { Move = result };
            }
            if (!moved) amount = 0;
        }
        if (move is not { Disposition: GameSessionDisposition.Changed } && _revision == long.MaxValue)
        {
            PutBack();
            return RejectPickPocket(GameSessionFailure.RevisionExhausted);
        }

        // After the move (it may refuse) the fate and the alignment join its revision.
        if (check.Fated && FateRules.Of(SkillCheckRules.Fate(Skill.PickPocket)) is { } fate) hero.Apply(hero.State.WithFate(FateRules.Use(hero.State.Fate, fate).After));
        int? alignment = null;
        if (moved && direction == PickPocketDirection.Steal && !victim.SummonedByHero &&
            PickPocketRules.AlignmentAfterTheft(victim.Alignment, hero.State.Alignment, hero.State.Alignment) is { } next)
        {
            var clamped = Math.Clamp(next, SessionHeroLimits.MinAlignment, SessionHeroLimits.MaxAlignment);
            if (clamped != hero.State.Alignment) { SetHeroAlignment(clamped); alignment = clamped; }
        }
        if (move is not { Disposition: GameSessionDisposition.Changed }) Revise();
        return new(GameSessionDisposition.Changed, GameSessionFailure.None, null, check, amount, move, die, noticed, alignment, check.Fated, Version);
    }

    /// <summary>The bound of a pick-pocket victim's perception (project policy, far past any stat).</summary>
    public const int MaxPickPocketStat = 1_000;
    private const int MaxPickPocketCells = 64;
    private const SkillTargetConditions AllTargetConditions = SkillTargetConditions.Paralyzed | SkillTargetConditions.Unconscious |
        SkillTargetConditions.FallingDown | SkillTargetConditions.Stunned | SkillTargetConditions.Sleeping;

    private SessionPickPocketResult RejectPickPocket(GameSessionFailure failure) =>
        new(GameSessionDisposition.Rejected, failure, null, null, 0, null, null, false, null, false, Version);
}
