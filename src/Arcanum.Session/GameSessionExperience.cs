using System.Collections.ObjectModel;
using Arcanum.Simulation;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Combat;

namespace Arcanum.Session;

/// <summary>Bounds of the experience domain (ARC-WORLD-030; project policy, the source has none).</summary>
public static class SessionExperienceLimits
{
    /// <summary>The largest magnitude of one award and of a quest table value: far above the installed tables (a few
    /// thousand), low enough that the difficulty and effect changes never overflow (the effect aggregation saturates at
    /// ±1,000,000,000).</summary>
    public const int MaxAward = 100_000_000;
    /// <summary>The largest experience worth or pool of an NPC.</summary>
    public const int MaxWorth = 100_000_000;
}

/// <summary>
/// The experience data a session awards with (ARC-WORLD-030), built by the host from the data tables (Data
/// <c>ExperienceTables</c>): the level thresholds, the quest experience of each experience level (<c>rules\xp_quest.mes</c>;
/// a level without an entry gives 0) and each quest's experience level (<c>rules\gamequest.mes</c>; a quest without an entry
/// is level 1). Data, not state: never saved.
/// </summary>
public sealed class SessionExperienceRules
{
    private readonly ReadOnlyDictionary<int, int> _questExperience;
    private readonly ReadOnlyDictionary<int, int> _questLevels;

    private SessionExperienceRules(ExperienceTable levels, Dictionary<int, int> questExperience, Dictionary<int, int> questLevels)
    {
        Levels = levels;
        _questExperience = new(questExperience);
        _questLevels = new(questLevels);
    }

    public ExperienceTable Levels { get; }

    /// <summary>The rules, or null when a table is missing, a quest experience value is beyond
    /// <see cref="SessionExperienceLimits.MaxAward"/> or a quest level is listed for a number outside 1000..1999.</summary>
    public static SessionExperienceRules? Create(ExperienceTable? levels, IReadOnlyDictionary<int, int>? questExperience,
        IReadOnlyDictionary<int, int>? questLevels)
    {
        if (levels is null || questExperience is null || questLevels is null) return null;
        var experience = questExperience.ToDictionary(pair => pair.Key, pair => pair.Value);
        var quests = questLevels.ToDictionary(pair => pair.Key, pair => pair.Value);
        if (experience.Values.Any(value => Math.Abs((long)value) > SessionExperienceLimits.MaxAward) ||
            quests.Keys.Any(quest => !SessionStoryLimits.IsQuest(quest))) return null;
        return new(levels, experience, quests);
    }

    /// <summary>The experience of a quest experience level (0x4C53C0): its table value, 0 without an entry.</summary>
    [OriginalFunction(0x4C53C0, OriginalFunctionScope.Full)]
    public int QuestExperience(int experienceLevel) => _questExperience.GetValueOrDefault(experienceLevel);

    /// <summary>A quest's experience level: its <c>rules\gamequest.mes</c> value, 1 without an entry.</summary>
    public int QuestLevel(int quest) => _questLevels.GetValueOrDefault(quest, 1);
}

/// <summary>
/// The hero's experience: level, experience and unspent points as saved, and, with experience data, what the next level
/// still needs (<see cref="ExperienceRules.ToNextLevel"/>) and the progress toward it in thousandths
/// (<see cref="ExperienceRules.Progress"/>); both are null without data.
/// </summary>
public sealed record SessionExperienceView(int Level, int Experience, int UnspentPoints, int? ToNextLevel, int? Progress);

/// <summary>
/// One NPC's experience values (OBJ_F_NPC_EXPERIENCE_WORTH and _POOL): as its map data loads them (the host's input) or as
/// the session saved them after it changed them. <see cref="Worth"/> is what a kill and each hit are measured against,
/// <see cref="Pool"/> what hits can still give.
/// </summary>
public readonly record struct SessionNpcExperience(string Entity, int Worth, int Pool);

/// <summary>Saved experience records of the NPCs the session changed, in ordinal entity order (snapshot schema 14).</summary>
public sealed class SessionExperienceState
{
    internal SessionExperienceState(SessionNpcExperience[] npcs) => Npcs = Array.AsReadOnly(npcs);

    public static SessionExperienceState Empty { get; } = new([]);

    public IReadOnlyList<SessionNpcExperience> Npcs { get; }
}

/// <summary>
/// What one award gave the hero: <see cref="Award"/> is the experience added after the difficulty and the hero's experience
/// effects (before the 0..2,000,000,000 clamp), <see cref="LevelsGained"/> and <see cref="PointsGained"/> the levels written
/// and their points (before the unspent cap), and <see cref="Followers"/> the PC level step the source passes to its followers
/// and nearby waiting followers (<see cref="ExperienceRules.FollowerLevel"/>), null when the recalculation stopped before it.
/// The host passes the step to <see cref="GameSessionOwner.LevelFollower"/> for each follower and nearby waiting follower,
/// also when the result is NoChange (at the last level an award can leave the hero unchanged and still step the followers, as
/// the source does). <see cref="Purchases"/> are what the hero's auto-level scheme bought at each level written (ARC-WORLD-036),
/// in order; null when it bought nothing. <see cref="LevelUps"/> are the level-up messages of the levels written (ARC-UI-046),
/// in order; null when no level was written.
/// </summary>
public sealed record SessionExperienceAward(int Award, int LevelsGained, int PointsGained, FollowerLevelStep? Followers,
    IReadOnlyList<AdvancementPurchase>? Purchases = null, IReadOnlyList<SessionLevelUp>? LevelUps = null);

/// <summary>
/// The level-up message of one level the hero's recalculation writes in its normal branch (ARC-UI-046, ledger OL27, 0x4A69C0):
/// the new level, then either the points line (<see cref="Points"/>, for a hero without an auto-level scheme; the active
/// <see cref="ExperienceRuleProfile.LevelMessagePointsBeforeCap"/> decides it, OL28) or the scheme's text (<see cref="Scheme"/>).
/// The source posts one per level for the local PC only: the last-level branch and a follower's level update post none.
/// </summary>
public sealed record SessionLevelUp(int Level, int Points, SessionLevelUpScheme? Scheme);

/// <summary>
/// What a level's scheme text names (0x4A7030): a scheme number without a rule (<see cref="HasRule"/> false: message 4), or the
/// purchases of this level in order (none: message 3) and whether the rule completed (a space and message 1 follow).
/// </summary>
public sealed record SessionLevelUpScheme(bool HasRule, IReadOnlyList<AdvancementPurchase> Purchases, bool Completed);

/// <summary>An experience call: the hero's award (null when none was given: refused, a zero amount, a dead hero, an award
/// to nobody; set also for NoChange when an award left the hero unchanged, such as a negative one at 0 or the last-level
/// branch, whose follower step still applies) and the NPC's record after the call (null for a hero-only call or a refusal).</summary>
public sealed record SessionExperienceResult(GameSessionDisposition Disposition, GameSessionFailure Failure,
    SessionExperienceAward? Award, SessionNpcExperience? Npc, GameSessionVersion Version);

/// <summary>The experience domain of one owner: its data and the NPC records it changed.</summary>
internal sealed class ExperienceDomain(SessionExperienceRules? rules)
{
    internal SessionExperienceRules? Rules { get; } = rules;
    internal Dictionary<string, SessionNpcExperience> Npcs { get; } = new(StringComparer.Ordinal);

    internal SessionExperienceState Capture() =>
        new(Npcs.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => pair.Value).ToArray());

    internal static bool Valid(SessionNpcExperience npc) => SessionValidation.Entity(npc.Entity) &&
        npc.Worth is >= 0 and <= SessionExperienceLimits.MaxWorth && npc.Pool is >= 0 and <= SessionExperienceLimits.MaxWorth;
}

/// <summary>A computed award: the hero after it and what it gave.</summary>
internal sealed record HeroAward(SessionHeroState Hero, SessionExperienceAward Award)
{
    internal bool Changed(SessionHeroState before) =>
        Hero.Experience != before.Experience || Hero.Level != before.Level || Hero.UnspentPoints != before.UnspentPoints ||
        !Hero.Stats.SequenceEqual(before.Stats) || Hero.Advancement != before.Advancement;
}

public sealed partial class GameSessionOwner
{
    /// <summary>The game difficulty awards follow (the player's setting, not saved; ARC-WORLD-030).</summary>
    public GameDifficulty Difficulty { get { ThrowIfDisposed(); return _difficulty; } }

    /// <summary>
    /// Switches the difficulty at once, like <see cref="SelectRules"/>: no revision, not saved. The same difficulty is
    /// <see cref="GameSessionDisposition.NoChange"/>; an undefined value is <see cref="GameSessionDisposition.Rejected"/>.
    /// </summary>
    public GameSessionDisposition SelectDifficulty(GameDifficulty difficulty)
    {
        ThrowIfDisposed();
        if (!Enum.IsDefined(difficulty)) return GameSessionDisposition.Rejected;
        if (difficulty == _difficulty) return GameSessionDisposition.NoChange;
        _difficulty = difficulty;
        return GameSessionDisposition.Changed;
    }

    /// <summary>The session's experience data, or null when it awards no experience.</summary>
    public SessionExperienceRules? Experience { get { ThrowIfDisposed(); return _experience.Rules; } }

    /// <summary>The hero's experience, or null for a session without a hero.</summary>
    public SessionExperienceView? QueryHeroExperience()
    {
        ThrowIfDisposed();
        if (_hero is null) return null;
        var hero = _hero.State;
        var table = _experience.Rules?.Levels;
        return new(hero.Level, hero.Experience, hero.UnspentPoints,
            table is null ? null : Simulation.Character.ExperienceRules.ToNextLevel(table, hero.Level, hero.Experience),
            table is null ? null : Simulation.Character.ExperienceRules.Progress(table, hero.Level, hero.Experience, _rules.Experience));
    }

    /// <summary>An NPC's saved experience record, or null while the session never changed it. An invalid entity id throws.</summary>
    public SessionNpcExperience? QueryNpcExperience(string entity)
    {
        ThrowIfDisposed();
        if (!SessionValidation.Entity(entity)) throw new ArgumentException("Invalid entity id.", nameof(entity));
        return _experience.Npcs.TryGetValue(entity, out var record) ? record : null;
    }

    /// <summary>
    /// Gives the hero experience as 0x45F110 does: a zero amount changes nothing; otherwise the
    /// difficulty changes it (<see cref="Simulation.Character.ExperienceRules.AdjustForDifficulty"/>), a dead hero gets
    /// nothing, the hero's experience effects (<c>xpgain</c>) change it, the sum is clamped to 0..2,000,000,000 and the level is
    /// recalculated (<see cref="Simulation.Character.ExperienceRules.Advance"/>): every level reached is written with its
    /// points, unspent points stay at most 56, and the hero stays at level 50 (ledger XP5). With scheme data the hero's auto-level
    /// scheme spends each level's points as it is written (ARC-WORLD-036; a run past the pool bound buys nothing). Refused, in order: no hero or no experience data
    /// (<see cref="GameSessionFailure.ExperienceUnavailable"/>), an amount beyond <see cref="SessionExperienceLimits.MaxAward"/>
    /// (<see cref="GameSessionFailure.InvalidExperience"/>), the version, the revision. One revision when the hero changed.
    /// </summary>
    public SessionExperienceResult AwardHeroExperience(GameSessionVersion expected, int amount)
    {
        ThrowIfDisposed();
        if (_hero is null || _experience.Rules is null) return RejectExperience(GameSessionFailure.ExperienceUnavailable);
        if (Math.Abs((long)amount) > SessionExperienceLimits.MaxAward) return RejectExperience(GameSessionFailure.InvalidExperience);
        if (!IsCurrent(expected)) return RejectExperience(GameSessionFailure.StaleVersion);
        return CommitExperience(Give(amount), null, null);
    }

    /// <summary>
    /// The experience of a quest experience level (<see cref="SessionExperienceRules.QuestExperience"/>) given as
    /// <see cref="AwardHeroExperience"/>: the dialog <c>XP</c> action and the script action that gives quest experience. Any
    /// level is looked up; one without an entry gives 0, which changes nothing.
    /// </summary>
    public SessionExperienceResult AwardQuestExperience(GameSessionVersion expected, int experienceLevel)
    {
        ThrowIfDisposed();
        if (_hero is null || _experience.Rules is null) return RejectExperience(GameSessionFailure.ExperienceUnavailable);
        return AwardHeroExperience(expected, _experience.Rules.QuestExperience(experienceLevel));
    }

    /// <summary>
    /// One hit on an NPC with an attacker (0x4B4390): while the NPC's pool holds experience, the hit takes its
    /// share (<see cref="Simulation.Character.ExperienceRules.DamageShare"/>) from the pool, and the hero gets it as
    /// <see cref="AwardHeroExperience"/> when <paramref name="byHero"/>: another attacker (a follower included) empties the pool
    /// for nothing. <paramref name="loaded"/> is the NPC's map data, read only while the session has no record of it;
    /// <paramref name="damage"/> is the hit's hit-point damage (<see cref="LifeOutcome.HitPointDamage"/>) and
    /// <paramref name="maxHitPoints"/> the NPC's maximum. The host calls this only for an NPC that is not turned to stone, as the
    /// source does. Refused, in order: no experience data, or no hero for <paramref name="byHero"/>
    /// (<see cref="GameSessionFailure.ExperienceUnavailable"/>); an invalid entity, worth or pool
    /// (<see cref="GameSessionFailure.InvalidExperienceTarget"/>); a damage outside 0..1,000,000 or a maximum outside
    /// 1..1,000,000 (<see cref="GameSessionFailure.InvalidExperience"/>); the version; a new record beyond the budget; the
    /// revision.
    /// </summary>
    public SessionExperienceResult RecordNpcDamage(GameSessionVersion expected, SessionNpcExperience loaded, int damage, int maxHitPoints,
        bool byHero)
    {
        ThrowIfDisposed();
        if (_experience.Rules is null || (byHero && _hero is null)) return RejectExperience(GameSessionFailure.ExperienceUnavailable);
        if (!ExperienceDomain.Valid(loaded)) return RejectExperience(GameSessionFailure.InvalidExperienceTarget);
        if (damage is < 0 or > LifeLimits.MaxAmount || maxHitPoints is < 1 or > LifeLimits.MaxAmount)
            return RejectExperience(GameSessionFailure.InvalidExperience);
        if (!IsCurrent(expected)) return RejectExperience(GameSessionFailure.StaleVersion);
        var record = _experience.Npcs.TryGetValue(loaded.Entity, out var saved) ? saved : loaded;
        if (record.Pool <= 0) return new(GameSessionDisposition.NoChange, GameSessionFailure.None, null, record, Version);
        var (award, pool) = Simulation.Character.ExperienceRules.DamageShare(record.Worth, record.Pool, damage, maxHitPoints);
        return CommitExperience(byHero ? Give(award) : null, record, record with { Pool = pool });
    }

    /// <summary>
    /// An NPC's death (0x45DA20): when <paramref name="creditsHero"/> (the killer is the hero or a
    /// critter whose leader is the hero), the hero gets a fifth of the NPC's worth
    /// (<see cref="Simulation.Character.ExperienceRules.KillAward"/>) as <see cref="AwardHeroExperience"/> and the worth becomes 0,
    /// so the same NPC never pays twice; any other death changes nothing. The host calls this after the dying script and only
    /// for an NPC. Refused as <see cref="RecordNpcDamage"/>; a credited kill needs a hero.
    /// </summary>
    public SessionExperienceResult RecordNpcKill(GameSessionVersion expected, SessionNpcExperience loaded, bool creditsHero)
    {
        ThrowIfDisposed();
        if (_experience.Rules is null || (creditsHero && _hero is null)) return RejectExperience(GameSessionFailure.ExperienceUnavailable);
        if (!ExperienceDomain.Valid(loaded)) return RejectExperience(GameSessionFailure.InvalidExperienceTarget);
        if (!IsCurrent(expected)) return RejectExperience(GameSessionFailure.StaleVersion);
        var record = _experience.Npcs.TryGetValue(loaded.Entity, out var saved) ? saved : loaded;
        if (!creditsHero) return new(GameSessionDisposition.NoChange, GameSessionFailure.None, null, record, Version);
        return CommitExperience(Give(Simulation.Character.ExperienceRules.KillAward(record.Worth)), record, record with { Worth = 0 });
    }

    /// <summary>
    /// The hero after an award, or null when the source gives nothing: a zero amount (before any change) or a dead hero
    /// (after the difficulty, as in the source). Needs a hero and experience data.
    /// </summary>
    internal HeroAward? Give(long amount)
    {
        if (amount == 0) return null;
        var hero = _hero!.State;
        var adjusted = Simulation.Character.ExperienceRules.AdjustForDifficulty(amount, _difficulty);
        var life = _life.Hero!.Value.State;
        if (LifeRules.Evaluate(life, HeroLifeFacts(life)).IsDead) return null;
        var gain = EffectModifiers.Apply(adjusted, _effects.Maximum(EffectSpecial.XpGain));
        var experience = (int)Math.Clamp(hero.Experience + (long)gain, SessionHeroLimits.MinExperience, SessionHeroLimits.MaxExperience);
        // The hero's scheme spends each level's points as the level is written (ARC-WORLD-036), over a running copy of its sheet.
        var sheet = AdvancementDomain.HeroSheet(hero);
        var purchases = new List<AdvancementPurchase>();
        // ARC-UI-046: one message per level written; a hero with a scheme number gets its scheme's text instead of the points.
        var levelUps = new List<SessionLevelUp>();
        var schemeNumber = hero.Advancement.AutoLevelScheme;
        Func<int, int, int>? spend = null;
        if (_advancement.Rules is { } rules && rules.Scheme(schemeNumber) is { } scheme)
        {
            var modifiers = AdvancementDomain.HeroModifiers(_hero, _effects, life.PoisonLevel);
            spend = (level, unspent) =>
            {
                var outcome = AutoLevelRules.Apply(sheet with { Level = level, UnspentPoints = unspent }, modifiers, scheme, null, npc: false,
                    rules.SpellWillpower);
                // A run past the pools' bound buys nothing; the level's points stay unspent.
                if (!AdvancementDomain.HeroBounded(outcome.Sheet)) return unspent;
                sheet = outcome.Sheet;
                purchases.AddRange(outcome.Purchases);
                levelUps[^1] = levelUps[^1] with { Scheme = new(true, outcome.Purchases.ToArray(), outcome.Completed) };
                return sheet.UnspentPoints;
            };
        }
        var noRule = schemeNumber == 0 ? null : new SessionLevelUpScheme(false, [], false);
        var withRule = new SessionLevelUpScheme(true, [], false);
        void Written(Simulation.Character.ExperienceLevelWritten level) => levelUps.Add(new(level.Level,
            Simulation.Character.ExperienceRules.LevelMessagePoints(level, _rules.Experience), spend is null ? noRule : withRule));
        var advance = Simulation.Character.ExperienceRules.Advance(_experience.Rules!.Levels, hero.Level, experience, hero.UnspentPoints, spend,
            Written);
        sheet = sheet with { Level = advance.Level, UnspentPoints = advance.UnspentPoints };
        return new(AdvancementDomain.WithSheet(hero, sheet, advance.Experience),
            new(gain, advance.LevelsGained, advance.PointsGained, advance.Followers, purchases.Count == 0 ? null : purchases.AsReadOnly(),
                levelUps.Count == 0 ? null : levelUps.AsReadOnly()));
    }

    // Every experience call commits here after its own checks: NoChange when neither the hero nor the NPC record changes, else
    // the record budget and the revision, then one revision for both.
    private SessionExperienceResult CommitExperience(HeroAward? award, SessionNpcExperience? before, SessionNpcExperience? after)
    {
        var heroChanged = award is not null && award.Changed(_hero!.State);
        var npcChanged = after is { } next && next != before;
        if (!heroChanged && !npcChanged) return new(GameSessionDisposition.NoChange, GameSessionFailure.None, award?.Award, after, Version);
        if (npcChanged && !_experience.Npcs.ContainsKey(after!.Value.Entity) && _experience.Npcs.Count >= _limits.MaxExperienceRecords)
            return RejectExperience(GameSessionFailure.BudgetExceeded);
        if (_revision == long.MaxValue) return RejectExperience(GameSessionFailure.RevisionExhausted);
        if (heroChanged) _hero!.Apply(award!.Hero);
        if (npcChanged) _experience.Npcs[after!.Value.Entity] = after.Value;
        Revise();
        return new(GameSessionDisposition.Changed, GameSessionFailure.None, award?.Award, after, Version);
    }

    private SessionExperienceResult RejectExperience(GameSessionFailure failure) =>
        new(GameSessionDisposition.Rejected, failure, null, null, new(_identity, _revision));

    // NPC records are valid, unique, in ordinal order and within the caller's budget. The hero's experience is the hero's import.
    private static GameSessionFailure ImportExperience(SessionExperienceState saved, SessionExperienceRules? rules, GameSessionLimits limits,
        out ExperienceDomain experience)
    {
        experience = new(rules);
        if (saved.Npcs.Count > limits.MaxExperienceRecords) return GameSessionFailure.BudgetExceeded;
        string? previous = null;
        foreach (var npc in saved.Npcs)
        {
            if (!ExperienceDomain.Valid(npc)) return GameSessionFailure.InvalidExperienceTarget;
            if (previous is not null && string.CompareOrdinal(previous, npc.Entity) >= 0) return GameSessionFailure.InvalidSnapshot;
            previous = npc.Entity;
            experience.Npcs.Add(npc.Entity, npc);
        }
        return GameSessionFailure.None;
    }
}
