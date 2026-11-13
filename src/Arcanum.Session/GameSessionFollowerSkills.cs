using Arcanum.Simulation.Character;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Items;
using Arcanum.Simulation.Skills;
using Arcanum.Simulation.Traps;

namespace Arcanum.Session;

/// <summary>
/// ARC-COMBAT-1776 (Follower Skills slice 1, ledger PL5, PL4, SP3, OH4, SC12, SC14): an NPC, a follower the chooser of the PC's lockpick use or Disarm Traps press handed the job to or any
/// other NPC, as the user of a pick-locks or disarm-traps check: its life target, its map data's advancement sheet and its effects' modifiers (the host's input, as
/// <see cref="SessionNpcPickPocket"/>'s and <see cref="SessionNpcHeal"/>'s). <see cref="LedByPc"/> picks the observer rules of a trap (<see cref="TrapObserver.LedNpc"/> knows what the PC spotted or placed,
/// <see cref="TrapObserver.FreeNpc"/> knows what the PC did not place; TT2).
/// </summary>
public sealed record SessionNpcSkillUser(SessionLifeTarget User, SessionNpcAdvancement Loaded, AdvancementModifiers Modifiers, bool LedByPc = true);

public sealed partial class GameSessionOwner
{
    /// <summary>
    /// An NPC picks the lock of a portal or a container (the item-use handler's chosen follower, 0x4C9050 → 0x571CB0, and the timed pick-lock goal of the AI, 0x42A4E0 → the shared check, ledger PL1–PL5). The check is the
    /// hero's (<see cref="PickLock"/>: the object's lock difficulty, the light, the lockpicks' bonus and condition, the outcome and the lock write with its Lock event, the generator draws) with the NPC's own pick-locks level
    /// and training (<see cref="QueryNpcSkills(SessionNpcAdvancement, AdvancementModifiers)"/>: the sheet the session levelled it to, else its map data's), its own effectiveness facts (its extraordinary intelligence, the game
    /// difficulty, no local-PC term: SC12), its injuries from its life record and no fate (the PC's only, SC14). <paramref name="lockpick"/> are the lockpicks the NPC holds (null: none, which a follower, the AI and a script
    /// may do, PL4, PL7: nothing is added to the lock's difficulty); the caller vouches that the NPC holds them (the host reads the holder's items). The original charges nothing here (the hero's action points were charged at the
    /// item's use or the AI's goal pays its own); witnesses and the hand-over line are the host's. Refused before anything is rolled: a user whose sheet or modifiers do not hold, that is not the sheet's NPC, is the hero or is dead
    /// (<see cref="GameSessionFailure.PickLockUnavailable"/>), an invalid object, difficulty or light (<see cref="GameSessionFailure.InvalidOpenable"/>), lockpicks that are none or hold values outside the bounds
    /// (<see cref="GameSessionFailure.InvalidLockpick"/>), with a use script (<see cref="GameSessionFailure.ItemScriptUnsupported"/>), destroyed or held by the hero (<see cref="GameSessionFailure.ItemNotAvailable"/>), the version.
    /// </summary>
    public SessionPickLockResult PickLockByNpc(GameSessionVersion expected, SessionNpcSkillUser user, SessionPickLockTarget target, SessionLockpick? lockpick)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(target);
        if (NpcSkillUserFailure(user, out var record, out var facts) is var failure && failure != GameSessionFailure.None) return RejectPickLock(GameSessionFailure.PickLockUnavailable);
        if (!ValidPickLockTarget(target)) return RejectPickLock(GameSessionFailure.InvalidOpenable);
        if (lockpick is not null)
        {
            if (LockpickRefusal(lockpick) is var refusal && refusal != GameSessionFailure.None) return RejectPickLock(refusal);
            if (_inventory.Hero.InPack(lockpick.Entity)) return RejectPickLock(GameSessionFailure.ItemNotAvailable);
        }
        if (!IsCurrent(expected)) return RejectPickLock(GameSessionFailure.StaleVersion);
        return PickLockStep(target, lockpick, NpcSkillRequest(user, Skill.PickLocks, record, LockpickCondition(lockpick)));
    }

    /// <summary>
    /// An NPC disarms a trap it knows (the Disarm Traps press's chosen follower, 0x57A620 → 0x4C9050 → the shared run's disarm case, ledger SP3, TT2, TT5, OH4): <see cref="RollHeroDisarmTrap"/>'s check
    /// (the light at the trap, its injuries and the trap object's difficulty, the master's second roll, the removal, the expert's device, the critical failure setting the trap off) with the NPC's own disarm-traps level
    /// and training, its own effectiveness facts, no local-PC term and no fate. The trap is known to the user by the observer rules of an NPC (<see cref="TrapRules.Known"/>: led by the PC, what the PC spotted or placed;
    /// free, what the PC did not place); an unknown or absent trap is <see cref="GameSessionDisposition.NoChange"/> with nothing rolled. The original charges nothing at this step (the PC's press paid, or the AI's goal does).
    /// The device an expert's critical success yields and a sprung trap's effect are the caller's (the hero's pack is not the NPC's). Refused: a user that is the hero, whose sheet or modifiers do not hold or that is dead
    /// (<see cref="GameSessionFailure.SkillsUnavailable"/>), an invalid object, difficulty or light (<see cref="GameSessionFailure.InvalidTrap"/>), the version, the generator or the revision exhausted, a budget the removal
    /// cannot take.
    /// </summary>
    public SessionDisarmRoll RollNpcDisarmTrap(GameSessionVersion expected, SessionNpcSkillUser user, SessionTrapped trapped, SessionTrapBaseline baseline, int trapDifficulty = 0,
        int light = SessionPickLockTarget.FullLight)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(user);
        if (NpcSkillUserFailure(user, out var record, out _) != GameSessionFailure.None) return RejectDisarm(GameSessionFailure.SkillsUnavailable);
        if (!ValidTrapped(trapped, baseline) || light is < 0 or > SessionPickLockTarget.FullLight || trapDifficulty is < -MaxTrapDifficulty or > MaxTrapDifficulty)
            return RejectDisarm(GameSessionFailure.InvalidTrap);
        if (!IsCurrent(expected)) return RejectDisarm(GameSessionFailure.StaleVersion);
        var observer = user.LedByPc ? TrapObserver.LedNpc : TrapObserver.FreeNpc;
        var state = TrapState(trapped, baseline);
        if (!state.Trapped || !TrapRules.Known(observer, state.ObjectFlags))
            return new(GameSessionDisposition.NoChange, GameSessionFailure.None, null, null, false, false,
                new(NoChange(), state.Trapped ? TrapDisarm.NotKnown : TrapDisarm.NotTrapped, 0, null, state), Version);
        if (_revision >= long.MaxValue - 1) return RejectDisarm(GameSessionFailure.RevisionExhausted);
        return DisarmStep(expected, trapped, baseline, NpcSkillRequest(user, Skill.DisarmTraps, record, null), trapDifficulty, light, observer, null);
    }

    // The NPC user's own facts: not the hero, the sheet and modifiers hold and belong to it, its life record resolves and it is alive.
    private GameSessionFailure NpcSkillUserFailure(SessionNpcSkillUser user, out SessionLifeRecord record, out LifeFacts? facts)
    {
        ArgumentNullException.ThrowIfNull(user.User);
        ArgumentNullException.ThrowIfNull(user.Loaded);
        ArgumentNullException.ThrowIfNull(user.Modifiers);
        record = default;
        facts = null;
        if (user.User.IsHero || user.Loaded.Entity != user.User.Entity || !AutoLevelRules.IsValid(user.Loaded.Sheet) || !AutoLevelRules.IsValid(user.Modifiers))
            return GameSessionFailure.SkillsUnavailable;
        if (Resolve(user.User, out record, out facts, out _) is var invalid && invalid != GameSessionFailure.None) return invalid;
        return LifeRules.Evaluate(record.State, facts!).IsDead ? GameSessionFailure.SkillsUnavailable : GameSessionFailure.None;
    }

    // The shared check of an NPC's skill use (as HealByNpc's): its level and training of the skill, its extraordinary intelligence, the game difficulty (no local-PC term), its
    // injuries, the used item's condition for a critical failure (none without an item) and no fate. The caller adds the skill's own difficulty terms.
    private SkillCheckRequest NpcSkillRequest(SessionNpcSkillUser user, Skill skill, SessionLifeRecord record, ItemCondition? item)
    {
        var sheet = _advancement.Npcs.TryGetValue(user.Loaded.Entity, out var levelled) ? levelled.Sheet : user.Loaded.Sheet;
        var level = QueryNpcSkills(user.Loaded, user.Modifiers)[skill];
        return new()
        {
            Skill = skill,
            Level = level.Level,
            Training = level.Training,
            Effectiveness = new()
            {
                ExtraordinaryIntelligence = AutoLevelRules.EffectiveStat(sheet, user.Modifiers, CharacterStat.Intelligence) >= CharacterStatRules.ExtraordinaryThreshold,
                Difficulty = _difficulty,
            },
            Difficulty = new() { SourceInjuries = SourceInjuries(record.State.Conditions) },
            Critical = item is { } condition ? new()
            {
                Item = true,
                ItemHitPoints = Math.Clamp(condition.HitPoints, 0, Math.Max(0, condition.MaximumHitPoints)),
                ItemMaxHitPoints = Math.Max(0, condition.MaximumHitPoints),
            } : new(),
        };
    }
}
