using System.Collections.Immutable;
using Arcanum.Simulation.Combat;

namespace Arcanum.Simulation.Character;

/// <summary>
/// The experience a PC needs for each level (ARC-WORLD-030, the source's <c>rules\xp_level.mes</c>): entry <c>i</c> is the
/// experience of level <c>i + 1</c>, for levels 1..<see cref="ExperienceRules.MaxLevel"/>. Level 1 needs 0, and every later
/// level more than the one before (project admission: an equal step would divide by zero in the progress formula, and the
/// installed table is strictly increasing).
/// </summary>
public sealed class ExperienceTable
{
    private ExperienceTable(ImmutableArray<int> thresholds) => Thresholds = thresholds;

    public ImmutableArray<int> Thresholds { get; }

    /// <summary>The table, or null unless it has <see cref="ExperienceRules.MaxLevel"/> entries starting at 0, strictly
    /// increasing, and at most <see cref="ExperienceRules.MaxExperience"/>.</summary>
    public static ExperienceTable? Create(IReadOnlyList<int>? thresholds)
    {
        var values = thresholds?.ToArray();
        if (values is null || values.Length != ExperienceRules.MaxLevel || values[0] != 0) return null;
        for (var i = 1; i < values.Length; i++)
            if (values[i] <= values[i - 1] || values[i] > ExperienceRules.MaxExperience) return null;
        return new([.. values]);
    }

    /// <summary>The experience of <paramref name="level"/> (1..<see cref="ExperienceRules.MaxLevel"/>).</summary>
    public int Threshold(int level) => level is >= 1 and <= ExperienceRules.MaxLevel
        ? Thresholds[level - 1] : throw new ArgumentOutOfRangeException(nameof(level));
}

/// <summary>
/// A PC's level, experience and unspent points after the source's level recalculation. <see cref="LevelsGained"/> and
/// <see cref="PointsGained"/> count the levels written and the points they granted (before the unspent cap).
/// <see cref="Followers"/> is the PC level step the source passes to its followers' level update, or null when the
/// recalculation stopped before reaching it; in the last-level branch its new level is one above the PC's.
/// </summary>
public readonly record struct ExperienceAdvance(int Level, int Experience, int UnspentPoints, int LevelsGained, int PointsGained,
    FollowerLevelStep? Followers);

/// <summary>The PC level step a follower's level follows: the PC's level before the recalculation and the level it reached.</summary>
public readonly record struct FollowerLevelStep(int OldPcLevel, int NewPcLevel);

/// <summary>A follower's level after the source's update and the points it granted (0 when the level stays).</summary>
public readonly record struct FollowerLevelUp(int Level, int PointsGained);

/// <summary>
/// The source's experience and level rules (ARC-WORLD-030, checked in the original 1.0.7.4 executable,
/// ledger XP1–XP12). Pure functions over explicit values; the session owns
/// the PC's numbers.
/// </summary>
public static class ExperienceRules
{
    /// <summary>The level maximum of the source's stat table. The last-level branch keeps a PC at <c>MaxLevel − 1</c>.</summary>
    public const int MaxLevel = 51;
    /// <summary>The experience maximum of the source's stat table.</summary>
    public const int MaxExperience = 2_000_000_000;
    /// <summary>The unspent-points maximum of the source's stat table: points above it are lost.</summary>
    public const int MaxUnspentPoints = 56;
    /// <summary>What <see cref="ToNextLevel"/> answers at or above <see cref="MaxLevel"/>.</summary>
    public const int NoNextLevel = 999_999;
    /// <summary>What <see cref="Progress"/> answers when the table gives no step (level 0, or experience above the next level).</summary>
    public const int ProgressFallback = 999;

    /// <summary>
    /// Experience still needed for the next level (0x4A68B0): the next level's
    /// threshold minus the experience, negative when it is already reached; <see cref="NoNextLevel"/> at or above the maximum.
    /// </summary>
    [OriginalFunction(0x4A68B0, OriginalFunctionScope.Full)]
    public static int ToNextLevel(ExperienceTable table, int level, int experience)
    {
        ArgumentNullException.ThrowIfNull(table);
        ValidateLevel(level);
        return level >= MaxLevel ? NoNextLevel : table.Thresholds[level] - experience;
    }

    /// <summary>
    /// Progress toward the next level in thousandths (0x4A6900):
    /// <c>1000 − 1000 × remaining / step</c>, truncated; 0 at or above the maximum; <see cref="ProgressFallback"/> for level 0
    /// or when the next level is already reached. The product is exact unless <paramref name="rules"/> has
    /// <see cref="ExperienceRuleProfile.ProgressProductWraps"/> (Original, ledger XP3): then it is a wrapping 32-bit product,
    /// and so is the final subtraction, as in the original.
    /// </summary>
    [OriginalFunction(0x4A6900, OriginalFunctionScope.Full)]
    public static int Progress(ExperienceTable table, int level, int experience, ExperienceRuleProfile rules)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(rules);
        ValidateLevel(level);
        if (level >= MaxLevel) return 0;
        if (level == 0) return ProgressFallback;
        var remaining = ToNextLevel(table, level, experience);
        if (remaining < 0) return ProgressFallback;
        var step = table.Thresholds[level] - table.Thresholds[level - 1];
        return rules.ProgressProductWraps ? unchecked(1000 - unchecked(1000 * remaining) / step) : 1000 - (int)(1000L * remaining / step);
    }

    /// <summary>Character points for the levels after <paramref name="from"/> up to <paramref name="to"/>
    /// (0x4A6980): one per level and one more for every fifth level.</summary>
    [OriginalFunction(0x4A6980, OriginalFunctionScope.Full)]
    public static int BonusPoints(int from, int to)
    {
        var points = 0;
        for (var level = from + 1; level <= to; level++) points += level % 5 == 0 ? 2 : 1;
        return points;
    }

    /// <summary>
    /// The source's level recalculation after the experience changed (0x4A69C0), for a PC with no
    /// effects on its level. Its auto-level scheme runs through <paramref name="spend"/> (ARC-WORLD-036): after each level is
    /// written with its points, the callback gets the new level and unspent points and returns the unspent points the scheme
    /// left (the caller keeps what the scheme bought); without it the points stay unspent. Nothing changes at or above the maximum level or while the next level
    /// is not reached. Otherwise each reached level is written with its points (the unspent total capped at
    /// <see cref="MaxUnspentPoints"/>), until a level is not reached or the next one would be <see cref="MaxLevel"/>: then the
    /// original writes the threshold of the PC's current level (<c>MaxLevel − 1</c>) as its experience, keeps the level and
    /// grants nothing (ledger XP5: reading the index one higher, as one reading of the source did, would recurse without end). The PC level step is
    /// passed on to followers either way, so in that branch it is one level above the PC.
    /// </summary>
    [OriginalFunction(0x4A69C0, OriginalFunctionScope.Partial)]
    public static ExperienceAdvance Advance(ExperienceTable table, int level, int experience, int unspentPoints,
        Func<int, int, int>? spend = null)
    {
        ArgumentNullException.ThrowIfNull(table);
        ValidateLevel(level);
        if (experience is < 0 or > MaxExperience) throw new ArgumentOutOfRangeException(nameof(experience));
        if (unspentPoints is < 0 or > MaxUnspentPoints) throw new ArgumentOutOfRangeException(nameof(unspentPoints));
        var toNext = ToNextLevel(table, level, experience);
        if (level >= MaxLevel || toNext > 0) return new(level, experience, unspentPoints, 0, 0, null);

        var old = level;
        var current = level;
        int gained = 0, points = 0;
        while (current < MaxLevel && toNext <= 0)
        {
            current++;
            if (current == MaxLevel)
            {
                // The experience of the level the PC keeps; with a strictly increasing table the recalculation this write
                // triggers in the source finds the next level unreached and stops at once.
                experience = table.Threshold(MaxLevel - 1);
                continue;
            }
            level = current;
            var bonus = BonusPoints(current - 1, current);
            gained++;
            points += bonus;
            unspentPoints = Math.Min(MaxUnspentPoints, unspentPoints + bonus);
            if (spend is not null)
            {
                unspentPoints = spend(level, unspentPoints);
                if (unspentPoints is < 0 or > MaxUnspentPoints) throw new InvalidOperationException("The scheme left unspent points outside 0..56.");
            }
            toNext = ToNextLevel(table, level, experience);
        }
        return new(level, experience, unspentPoints, gained, points, new(old, current));
    }

    /// <summary>
    /// A follower's level after its leader's level step (0x4A6CB0): a follower at or above the
    /// PC's new level stays (the original's first test; testing the maximum instead gives the same for every step the
    /// recalculation passes, ledger XP7); one at or below the PC's old level keeps its gap, one between is pulled to the PC's new
    /// level, never to <see cref="MaxLevel"/> or above; a level that would not rise stays. Points are the bonus points of the
    /// levels gained (the source then runs the NPC's auto-level scheme: <c>GameSessionOwner.LevelFollower</c>, ARC-WORLD-036).
    /// </summary>
    [OriginalFunction(0x4A6CB0, OriginalFunctionScope.Partial)]
    public static FollowerLevelUp FollowerLevel(int level, FollowerLevelStep step)
    {
        ValidateLevel(level);
        if (level >= step.NewPcLevel || level >= MaxLevel) return new(level, 0);
        var next = level <= step.OldPcLevel ? step.NewPcLevel + level - step.OldPcLevel : step.NewPcLevel;
        if (next >= MaxLevel) next = MaxLevel - 1;
        return next > level ? new(next, BonusPoints(level, next)) : new(level, 0);
    }

    /// <summary>
    /// The difficulty change of an award (0x45F110): easy adds half, hard takes a quarter away (the
    /// original takes a quarter, not half, ledger XP1), each truncated toward zero; normal keeps it.
    /// </summary>
    [OriginalFunction(0x45F110, OriginalFunctionScope.Partial)]
    public static long AdjustForDifficulty(long award, GameDifficulty difficulty) => difficulty switch
    {
        GameDifficulty.Normal => award,
        GameDifficulty.Easy => award + award / 2,
        GameDifficulty.Hard => award - award / 4,
        _ => throw new ArgumentOutOfRangeException(nameof(difficulty)),
    };

    /// <summary>The award for killing an NPC (0x45DA20): a fifth of its experience worth, truncated.</summary>
    [OriginalFunction(0x45DA20, OriginalFunctionScope.Partial)]
    public static int KillAward(int worth) => (int)(20L * worth / 100);

    /// <summary>
    /// One hit's share of an NPC's experience pool (0x4B4390): the damage as a truncated percentage of the
    /// maximum hit points, that percentage of the worth (truncated), at most what is left in the pool, which loses it.
    /// Nothing is taken from an empty pool. Computed without the original's 32-bit wrap of worth × percentage, which no
    /// installed worth (a few thousand) reaches short of a hit thousands of times larger than the maximum hit points.
    /// </summary>
    [OriginalFunction(0x4B4390, OriginalFunctionScope.Partial)]
    public static (int Award, int Pool) DamageShare(int worth, int pool, int damage, int maxHitPoints)
    {
        if (maxHitPoints < 1) throw new ArgumentOutOfRangeException(nameof(maxHitPoints));
        if (pool <= 0) return (0, pool);
        var percent = 100L * damage / maxHitPoints;
        var award = Math.Min(worth * percent / 100, pool);
        return ((int)award, (int)(pool - award));
    }

    private static void ValidateLevel(int level)
    {
        if (level is < 0 or > MaxLevel) throw new ArgumentOutOfRangeException(nameof(level));
    }
}
