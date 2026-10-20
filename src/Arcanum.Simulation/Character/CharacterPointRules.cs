using System.Collections.Immutable;
using Arcanum.Simulation.Skills;

namespace Arcanum.Simulation.Character;

/// <summary>Why a point was or was not spent or refunded.</summary>
public enum PointChangeStatus
{
    /// <summary>The point was spent or refunded (for a basic skill's refund, one or more points).</summary>
    Changed,
    /// <summary>The cost is above the unspent points.</summary>
    NotEnoughPoints,
    /// <summary>The value cannot rise: a stat whose effective value is at its maximum or whose base is at the race's maximum, a
    /// skill whose level is 20, a hit-point or fatigue pool at its bound.</summary>
    AtMaximum,
    /// <summary>The skill's key stat does not allow the points (four levels each above its cap); <see cref="PointChange.Stat"/>
    /// names the key stat.</summary>
    KeyStatTooLow,
    /// <summary>The value is at (or below) the one taken when the character editor opened: nothing bought since to refund.</summary>
    AtFloor,
    /// <summary>The value is at its minimum (a stat at the race's minimum, a skill or pool with no points).</summary>
    AtMinimum,
    /// <summary>Lowering the stat would leave a skill keyed on it with more points than its cap allows (<see cref="PointChange.Skill"/>).</summary>
    SkillNeedsStat,
    /// <summary>Lowering intelligence or willpower would leave a known spell rank below its minimum.</summary>
    SpellNeedsStat,
    /// <summary>Lowering intelligence would leave a tech degree below its minimum.</summary>
    DegreeNeedsStat,
    /// <summary>The track's lane (spell ranks: the magic lane; tech degrees: the technology lane) has not plugged in.</summary>
    LaneNotReady,
    /// <summary>An input outside the rules' domain (an invalid sheet or modifiers, an undefined track, an index out of range,
    /// a lane step that changes the unspent points or leaves an invalid sheet).</summary>
    Invalid,
}

/// <summary>
/// How much one refund of a basic skill takes back (skills plan S5, ledger CP4): <see cref="UntilLevelChanges"/> is the
/// original's click, which removes points one at a time until the skill's level changes (and, in Steamwoven, stops at the
/// editor's floor); <see cref="OnePoint"/> removes one point. A tech skill always refunds one point.
/// </summary>
public enum SkillRefundClick
{
    UntilLevelChanges,
    OnePoint,
}

/// <summary>
/// The answer of one spend or refund: the status, the sheet after it (the input sheet unless <see cref="PointChangeStatus.Changed"/>),
/// the character points spent or credited by a refund (the unspent points stop at 56; 0 unless changed), the target's value after it (a stat's base, a skill's points, a
/// pool's bought points, a college's rank or a degree), and for a refusal the stat it names (the key stat, intelligence or
/// willpower) and the skill that blocks a stat decrease.
/// </summary>
public sealed record PointChange(PointChangeStatus Status, AdvancementSheet Sheet, int Points, int Value, CharacterStat? Stat = null,
    Skill? Skill = null)
{
    public bool Changed => Status == PointChangeStatus.Changed;
}

/// <summary>One step of a lane: the status, the sheet after the step without the unspent points (the pool moves them) and the
/// character points the step costs or refunds.</summary>
public readonly record struct PointLaneStep(PointChangeStatus Status, AdvancementSheet Sheet, int Points);

/// <summary>
/// The rules a lane brings to the shared point path for its track: the magic lane's spell ranks (<see cref="AdvancementTrack.College"/>,
/// MT3) and the technology lane's degrees (<see cref="AdvancementTrack.TechDiscipline"/>, TECH-2). A step changes only the lane's
/// own fields (a rank and the magick points, a degree and the tech points), never the unspent points; the pool checks the
/// points, the editor's floor and the result.
/// </summary>
public interface IPointLane
{
    /// <summary>One step up of <paramref name="index"/> (a college or a discipline) and its cost, or a refusal.</summary>
    PointLaneStep Raise(AdvancementSheet sheet, AdvancementModifiers modifiers, int index);

    /// <summary>One step down of <paramref name="index"/> and its refund, or a refusal.</summary>
    PointLaneStep Lower(AdvancementSheet sheet, AdvancementModifiers modifiers, int index);
}

/// <summary>
/// ARC-WORLD-052 (skills plan S5): the one spend-a-point / refund-a-point path over a character's unspent points, as the original's
/// character editor buys and refunds (0x55A5C0 stats and pools, 0x57ACD0 and 0x57AEB0 skills; ledger "Character point pool",
/// CP1–CP12; contract docs/world/character-point-pool.md). Refunds go only down to the editor's floor, the sheet taken when the
/// editor opened (0x5597C0): owner rule 2026-10-20, strict in both rule modes, for every track (the original has no floor for
/// tech degrees, TC10, and its skill refund loop can pass the floor, CP6). Pure functions over explicit values; the session holds
/// the floor and applies the sheet.
/// </summary>
public static class CharacterPointRules
{
    /// <summary>The cost of a hit or fatigue point and the refund of one (the editor's rows 0 and 1 take 1).</summary>
    public const int PoolPointCost = 1;

    /// <summary>
    /// Spends points on one step of <paramref name="track"/> (<paramref name="index"/>: a stat 0..7, a basic skill 0..11, a tech
    /// skill 0..3, a college 0..15, a discipline 0..7, 0 for the two pools):
    /// <list type="bullet">
    /// <item>a stat: refused at its maximum when its effective value has reached the race's maximum; the cost is
    /// <see cref="AutoLevelRules.StatCost"/> of base + 1; the base rises by one (a base already at the race's maximum is refused);</item>
    /// <item>a pool: costs 1; the original has no maximum, the rules stop at <paramref name="maxPoolPoints"/> (at most
    /// <see cref="AutoLevelRules.MaxPoolValue"/>; a session passes its own bound);</item>
    /// <item>a skill: costs <see cref="SkillRules.PointCost"/> (1); the point setter refuses at level 20 (<see cref="PointChangeStatus.AtMaximum"/>)
    /// or for the key stat (<see cref="PointChangeStatus.KeyStatTooLow"/>); a tech point also adds a tech point;</item>
    /// <item>a college or a discipline: through its <paramref name="lane"/>, <see cref="PointChangeStatus.LaneNotReady"/> without one.</item>
    /// </list>
    /// </summary>
    public static PointChange Spend(AdvancementSheet sheet, AdvancementModifiers modifiers, AdvancementTrack track, int index,
        ImmutableArray<int> spellWillpower, IPointLane? lane = null, int maxPoolPoints = AutoLevelRules.MaxPoolValue)
    {
        if (!Admit(sheet, modifiers, track, index, spellWillpower) || maxPoolPoints is < 0 or > AutoLevelRules.MaxPoolValue)
            return Refused(PointChangeStatus.Invalid, sheet, track, index);
        var work = new AutoLevelRules.Work(sheet, modifiers, spellWillpower);
        return track switch
        {
            AdvancementTrack.Stat => SpendStat(sheet, work, index),
            AdvancementTrack.HitPoints or AdvancementTrack.Fatigue => SpendPool(sheet, work, track, maxPoolPoints),
            AdvancementTrack.BasicSkill or AdvancementTrack.TechSkill => SpendSkill(sheet, work, track, index),
            _ => Lane(sheet, modifiers, track, index, lane, raise: true),
        };
    }

    /// <summary>
    /// Refunds points of one step of <paramref name="track"/> while it is above <paramref name="floor"/> (the sheet taken when the
    /// editor opened): <see cref="PointChangeStatus.AtFloor"/> at or below it.
    /// <list type="bullet">
    /// <item>a stat: refunds <see cref="AutoLevelRules.StatCost"/> of its base; the base falls by one unless it is at the race's
    /// minimum (<see cref="PointChangeStatus.AtMinimum"/>) or the stat setter's guards refuse: a skill keyed on it
    /// (<see cref="PointChangeStatus.SkillNeedsStat"/>), for intelligence a known spell rank and then a tech degree, for willpower a
    /// known spell rank (<see cref="PointChangeStatus.SpellNeedsStat"/>, <see cref="PointChangeStatus.DegreeNeedsStat"/>), each with
    /// the effective value the lower base gives;</item>
    /// <item>a pool: refunds 1;</item>
    /// <item>a basic skill: one point, or with <see cref="SkillRefundClick.UntilLevelChanges"/> one point at a time until its level
    /// changes, it reaches the floor or the setter refuses; a refusal of the first point is
    /// <see cref="PointChangeStatus.KeyStatTooLow"/> (the key stat now allows fewer points than one less);</item>
    /// <item>a tech skill: one point (its tech point too);</item>
    /// <item>a college or a discipline: through its <paramref name="lane"/>.</item>
    /// </list>
    /// </summary>
    public static PointChange Refund(AdvancementSheet sheet, AdvancementSheet floor, AdvancementModifiers modifiers, AdvancementTrack track,
        int index, SkillRefundClick click, ImmutableArray<int> spellWillpower, IPointLane? lane = null)
    {
        if (!Admit(sheet, modifiers, track, index, spellWillpower) || !AutoLevelRules.IsValid(floor) || !Enum.IsDefined(click))
            return Refused(PointChangeStatus.Invalid, sheet, track, index);
        if (AtFloor(sheet, floor, track, index)) return Refused(PointChangeStatus.AtFloor, sheet, track, index);
        var work = new AutoLevelRules.Work(sheet, modifiers, spellWillpower);
        return track switch
        {
            AdvancementTrack.Stat => RefundStat(sheet, work, index),
            AdvancementTrack.HitPoints or AdvancementTrack.Fatigue => RefundPool(sheet, work, track),
            AdvancementTrack.BasicSkill or AdvancementTrack.TechSkill => RefundSkill(sheet, floor, work, track, index, click),
            _ => Lane(sheet, modifiers, track, index, lane, raise: false),
        };
    }

    /// <summary>
    /// Whether the step's value is at or below the floor (the editor's button handlers 0x55D3A0, 0x55D6F0, 0x55A5C0 and the college
    /// handler compare it for equality before a refund; Steamwoven compares at or below). A stat compares its base, a pool its
    /// bought points, a skill its points, a college its rank, a discipline its degree.
    /// </summary>
    [OriginalFunction(0x55D3A0, OriginalFunctionScope.Partial)]
    internal static bool AtFloor(AdvancementSheet sheet, AdvancementSheet floor, AdvancementTrack track, int index)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        ArgumentNullException.ThrowIfNull(floor);
        return Value(sheet, track, index) <= Value(floor, track, index);
    }

    /// <summary>
    /// The cost the editor's hint shows for a stat's next point (0x55AE70): <see cref="AutoLevelRules.StatCost"/> of base + 1 while
    /// the <em>base</em> is below the race's maximum, else 0. (The "+" button tests the effective value instead, CP3.)
    /// </summary>
    [OriginalFunction(0x55AE70, OriginalFunctionScope.Partial)]
    public static int NextStatCost(AdvancementSheet sheet, CharacterStat stat)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        if (stat is < CharacterStat.Strength or > CharacterStat.Charisma) throw new ArgumentOutOfRangeException(nameof(stat));
        var value = sheet.Stats[(int)stat];
        return value < CharacterStatRules.Range(sheet.Race, stat).Maximum ? AutoLevelRules.StatCost(value + 1) : 0;
    }

    /// <summary>
    /// The stat setter's guards on a lower base (0x4B0980 with 0x4C6B50, and for intelligence 0x4B1B90 then 0x4B02B0, for
    /// willpower 0x4B1C00): <paramref name="effective"/> is the stat's effective value with the lower base. Null when allowed.
    /// </summary>
    internal static PointChange? Guard(AdvancementSheet sheet, AutoLevelRules.Work work, CharacterStat stat, int effective)
    {
        var skills = SkillRules.CheckKeyStat(stat, effective, sheet.BasicSkills, sheet.TechSkills);
        if (!skills.Allowed) return Refused(PointChangeStatus.SkillNeedsStat, sheet, AdvancementTrack.Stat, (int)stat, stat, skills.Blocking);
        if (stat == CharacterStat.Intelligence)
        {
            if (!SpellsHold(sheet, _ => AutoLevelRules.MinimumSpellIntelligence, effective))
                return Refused(PointChangeStatus.SpellNeedsStat, sheet, AdvancementTrack.Stat, (int)stat, stat);
            if (!DegreesHold(sheet, effective)) return Refused(PointChangeStatus.DegreeNeedsStat, sheet, AdvancementTrack.Stat, (int)stat, stat);
        }
        if (stat == CharacterStat.Willpower && !SpellsHold(sheet, spell => work.SpellWillpower(spell), effective))
            return Refused(PointChangeStatus.SpellNeedsStat, sheet, AdvancementTrack.Stat, (int)stat, stat);
        return null;
    }

    /// <summary>Every known rank (ranks 0 .. rank − 1 of each college) needs at most <paramref name="value"/> (0x4B1B90, 0x4B1C00).</summary>
    [OriginalFunction(0x4B1B90, OriginalFunctionScope.Full)]
    [OriginalFunction(0x4B1C00, OriginalFunctionScope.Full)]
    internal static bool SpellsHold(AdvancementSheet sheet, Func<int, int> minimum, int value)
    {
        for (var college = 0; college < AutoLevelRules.Colleges; college++)
            for (var rank = 0; rank < sheet.Colleges[college]; rank++)
                if (minimum(college * AutoLevelRules.SpellsPerCollege + rank) > value) return false;
        return true;
    }

    /// <summary>Every stored degree's intelligence minimum is at most <paramref name="intelligence"/> (0x4B02B0; a PC or NPC).</summary>
    [OriginalFunction(0x4B02B0, OriginalFunctionScope.Partial)]
    internal static bool DegreesHold(AdvancementSheet sheet, int intelligence) =>
        sheet.TechDegrees.All(degree => AutoLevelRules.DegreeMinimumIntelligence(degree) <= intelligence);

    // The editor's stat "+" (0x55A5C0): the effective value against the maximum first, then the cost of base + 1, then the
    // points; the setter clamps to the race's range, so a base at the maximum is refused after.
    [OriginalFunction(0x55A5C0, OriginalFunctionScope.Partial)]
    private static PointChange SpendStat(AdvancementSheet sheet, AutoLevelRules.Work work, int index)
    {
        var stat = (CharacterStat)index;
        var maximum = CharacterStatRules.Range(sheet.Race, stat).Maximum;
        if (work.Effective(stat) >= maximum) return Refused(PointChangeStatus.AtMaximum, sheet, AdvancementTrack.Stat, index);
        var next = work.Stats[index] + 1;
        var cost = AutoLevelRules.StatCost(next);
        if (cost > work.Unspent) return Refused(PointChangeStatus.NotEnoughPoints, sheet, AdvancementTrack.Stat, index);
        work.SetStat(index, next);
        if (work.Stats[index] < next) return Refused(PointChangeStatus.AtMaximum, sheet, AdvancementTrack.Stat, index);
        work.Unspent -= cost;
        return new(PointChangeStatus.Changed, work.Sheet(), cost, work.Stats[index]);
    }

    private static PointChange RefundStat(AdvancementSheet sheet, AutoLevelRules.Work work, int index)
    {
        var stat = (CharacterStat)index;
        var value = work.Stats[index];
        var refund = AutoLevelRules.StatCost(value);
        work.SetStat(index, value - 1);
        // Behind the floor (a valid floor's stats are at least 1) the race's minimum is not reached; kept for the setter's shape.
        if (work.Stats[index] >= value) return Refused(PointChangeStatus.AtMinimum, sheet, AdvancementTrack.Stat, index);
        if (Guard(sheet, work, stat, work.Effective(stat)) is { } refused) return refused;
        var credited = Credit(work, refund);
        return new(PointChangeStatus.Changed, work.Sheet(), credited, work.Stats[index]);
    }

    private static PointChange SpendPool(AdvancementSheet sheet, AutoLevelRules.Work work, AdvancementTrack track, int maximum)
    {
        var points = track == AdvancementTrack.HitPoints ? work.HitPointPoints : work.FatiguePoints;
        if (PoolPointCost > work.Unspent) return Refused(PointChangeStatus.NotEnoughPoints, sheet, track, 0);
        if (points >= maximum) return Refused(PointChangeStatus.AtMaximum, sheet, track, 0);
        SetPool(work, track, points + 1);
        work.Unspent -= PoolPointCost;
        return new(PointChangeStatus.Changed, work.Sheet(), PoolPointCost, points + 1);
    }

    private static PointChange RefundPool(AdvancementSheet sheet, AutoLevelRules.Work work, AdvancementTrack track)
    {
        var points = track == AdvancementTrack.HitPoints ? work.HitPointPoints : work.FatiguePoints;
        // Behind the floor only for a negative floor, which no hero sheet has.
        if (points <= 0) return Refused(PointChangeStatus.AtMinimum, sheet, track, 0);
        SetPool(work, track, points - 1);
        var credited = Credit(work, PoolPointCost);
        return new(PointChangeStatus.Changed, work.Sheet(), credited, points - 1);
    }

    // 0x57ACD0: the cost against the points, then the setter; its refusal is "at the maximum" when the level is 20, else the key
    // stat's.
    [OriginalFunction(0x57ACD0, OriginalFunctionScope.Partial)]
    private static PointChange SpendSkill(AdvancementSheet sheet, AutoLevelRules.Work work, AdvancementTrack track, int index)
    {
        var tech = track == AdvancementTrack.TechSkill;
        var skill = tech ? SkillRules.Tech(index) : SkillRules.Basic(index);
        var cost = SkillRules.PointCost(skill);
        if (cost > work.Unspent) return Refused(PointChangeStatus.NotEnoughPoints, sheet, track, index);
        var points = SkillRules.Points(tech ? work.Tech[index] : work.Basic[index]) + 1;
        if (SetSkill(work, tech, index, points) != points)
        {
            var level = tech ? work.TechLevel(index) : work.BasicLevel(index);
            return level >= SkillRules.MaxLevel ? Refused(PointChangeStatus.AtMaximum, sheet, track, index)
                : Refused(PointChangeStatus.KeyStatTooLow, sheet, track, index, SkillRules.KeyStat(skill));
        }
        work.Unspent -= cost;
        return new(PointChangeStatus.Changed, work.Sheet(), cost, points);
    }

    // 0x57AEB0: a pass sets points − 1 and refunds one; a basic skill repeats while its level stays the same (the original's
    // click), here also stopping at the floor. A tech skill takes one pass.
    [OriginalFunction(0x57AEB0, OriginalFunctionScope.Partial)]
    private static PointChange RefundSkill(AdvancementSheet sheet, AdvancementSheet floor, AutoLevelRules.Work work, AdvancementTrack track,
        int index, SkillRefundClick click)
    {
        var tech = track == AdvancementTrack.TechSkill;
        var skill = tech ? SkillRules.Tech(index) : SkillRules.Basic(index);
        var floorPoints = SkillRules.Points(tech ? floor.TechSkills[index] : floor.BasicSkills[index]);
        var level = tech ? work.TechLevel(index) : work.BasicLevel(index);
        int removed = 0, refunded = 0;
        while (true)
        {
            var points = SkillRules.Points(tech ? work.Tech[index] : work.Basic[index]);
            if (points <= floorPoints) break;
            // points > floor ≥ 0, so only the key stat refuses points − 1 (its cap fell below them).
            if (SetSkill(work, tech, index, points - 1) != points - 1)
            {
                if (removed > 0) break;
                return Refused(PointChangeStatus.KeyStatTooLow, sheet, track, index, SkillRules.KeyStat(skill));
            }
            removed++;
            refunded += Credit(work, SkillRules.PointRefund(skill));
            if (tech || click == SkillRefundClick.OnePoint || work.BasicLevel(index) != level) break;
        }
        return new(PointChangeStatus.Changed, work.Sheet(), refunded, SkillRules.Points(tech ? work.Tech[index] : work.Basic[index]));
    }

    // The lane may change only its own fields (a college's rank and the magick points, a discipline's degree and the tech
    // points), by exactly one step in the asked direction; a refund's step starts above the floor, so it ends at or above it.
    private static PointChange Lane(AdvancementSheet sheet, AdvancementModifiers modifiers, AdvancementTrack track, int index, IPointLane? lane,
        bool raise)
    {
        if (lane is null) return Refused(PointChangeStatus.LaneNotReady, sheet, track, index);
        var step = raise ? lane.Raise(sheet, modifiers, index) : lane.Lower(sheet, modifiers, index);
        if (step.Status != PointChangeStatus.Changed)
            return Refused(Enum.IsDefined(step.Status) ? step.Status : PointChangeStatus.Invalid, sheet, track, index);
        if (!AutoLevelRules.IsValid(step.Sheet) || step.Points < 0 || !OwnStep(sheet, step.Sheet, track, index, raise ? 1 : -1))
            return Refused(PointChangeStatus.Invalid, sheet, track, index);
        if (raise && step.Points > sheet.UnspentPoints) return Refused(PointChangeStatus.NotEnoughPoints, sheet, track, index);
        var unspent = raise ? sheet.UnspentPoints - step.Points : Math.Min(sheet.UnspentPoints + step.Points, ExperienceRules.MaxUnspentPoints);
        var after = step.Sheet with { UnspentPoints = unspent };
        return new(PointChangeStatus.Changed, after, raise ? step.Points : unspent - sheet.UnspentPoints, Value(after, track, index));
    }

    private static bool OwnStep(AdvancementSheet before, AdvancementSheet after, AdvancementTrack track, int index, int by)
    {
        var college = track == AdvancementTrack.College;
        var own = college ? after.Colleges : after.TechDegrees;
        var old = college ? before.Colleges : before.TechDegrees;
        if (own[index] != old[index] + by) return false;
        for (var other = 0; other < own.Length; other++)
            if (other != index && own[other] != old[other]) return false;
        var rest = college
            ? after with { Colleges = old, MagickPoints = before.MagickPoints }
            : after with { TechDegrees = old, TechPoints = before.TechPoints };
        return rest.Equals(before);
    }

    // The unspent points rise by the refund, clamped to 0..56 as the stat setter writes them (CP10); the points credited.
    private static int Credit(AutoLevelRules.Work work, int refund)
    {
        var before = work.Unspent;
        work.Unspent = Math.Min(before + refund, ExperienceRules.MaxUnspentPoints);
        return work.Unspent - before;
    }

    private static int SetSkill(AutoLevelRules.Work work, bool tech, int index, int points) =>
        tech ? work.SetTechPoints(index, points) : work.SetBasicPoints(index, points);

    private static void SetPool(AutoLevelRules.Work work, AdvancementTrack track, int points)
    {
        if (track == AdvancementTrack.HitPoints) work.HitPointPoints = points;
        else work.FatiguePoints = points;
    }

    private static int Value(AdvancementSheet sheet, AdvancementTrack track, int index) => track switch
    {
        AdvancementTrack.Stat => sheet.Stats[index],
        AdvancementTrack.BasicSkill => SkillRules.Points(sheet.BasicSkills[index]),
        AdvancementTrack.TechSkill => SkillRules.Points(sheet.TechSkills[index]),
        AdvancementTrack.College => sheet.Colleges[index],
        AdvancementTrack.TechDiscipline => sheet.TechDegrees[index],
        AdvancementTrack.HitPoints => sheet.HitPointPoints,
        AdvancementTrack.Fatigue => sheet.FatiguePoints,
        _ => throw new ArgumentOutOfRangeException(nameof(track)),
    };

    /// <summary>The number of indices of a track: 8 stats, 12 basic and 4 tech skills, 16 colleges, 8 disciplines, 1 pool each.</summary>
    public static int Indices(AdvancementTrack track) => track switch
    {
        AdvancementTrack.Stat => AutoLevelRules.PrimaryStats,
        AdvancementTrack.BasicSkill => AutoLevelRules.BasicSkills,
        AdvancementTrack.TechSkill => AutoLevelRules.TechSkills,
        AdvancementTrack.College => AutoLevelRules.Colleges,
        AdvancementTrack.TechDiscipline => AutoLevelRules.TechDisciplines,
        AdvancementTrack.HitPoints or AdvancementTrack.Fatigue => 1,
        _ => 0,
    };

    private static bool Admit(AdvancementSheet sheet, AdvancementModifiers modifiers, AdvancementTrack track, int index, ImmutableArray<int> spellWillpower) =>
        AutoLevelRules.IsValid(sheet) && AutoLevelRules.IsValid(modifiers) && index >= 0 && index < Indices(track) &&
        !spellWillpower.IsDefault && spellWillpower.Length == AutoLevelRules.Spells;

    private static PointChange Refused(PointChangeStatus status, AdvancementSheet sheet, AdvancementTrack track, int index,
        CharacterStat? stat = null, Skill? skill = null) =>
        new(status, sheet, 0, index >= 0 && index < Indices(track) && AutoLevelRules.IsValid(sheet) ? Value(sheet, track, index) : 0, stat, skill);
}
