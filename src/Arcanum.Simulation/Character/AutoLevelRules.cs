using System.Collections.Immutable;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Magic;
using Arcanum.Simulation.Skills;

namespace Arcanum.Simulation.Character;

/// <summary>What one auto-level step buys: a primary stat, a basic or tech skill, a spell college, a tech discipline, or hit
/// or fatigue points (the source's <c>maxhps</c> and <c>maxfatigue</c>).</summary>
public enum AdvancementTrack { Stat, BasicSkill, TechSkill, College, TechDiscipline, HitPoints, Fatigue }

/// <summary>
/// One step of a scheme rule: buy <paramref name="Track"/> <paramref name="Index"/> until it reaches <paramref name="Target"/>.
/// A null track is a token that names nothing: the rule stops there, not completed, as the source does.
/// </summary>
public readonly record struct AutoLevelStep(AdvancementTrack? Track, int Index, int Target);

/// <summary>A parsed scheme rule: its steps in order, and how many targets had text after their number, which the source's
/// atoi ignores (a missing comma there swallows the next step, ledger AL3).</summary>
public sealed record AutoLevelScheme(ImmutableArray<AutoLevelStep> Steps, int IgnoredTargetText = 0);

public enum AutoLevelParseStatus
{
    Parsed,
    /// <summary>A byte outside printable ASCII. The source splits only on spaces and commas, so a tab or line feed would glue
    /// two tokens together; such a rule is refused instead of read partially.</summary>
    InvalidText,
    /// <summary>A token without a target: the original passes a null pointer to the C library's atoi (ledger A3, an original bug).</summary>
    MissingTarget,
    /// <summary>A target whose number has more than 9 digits (atoi would overflow); or a college target above 5 or a tech discipline target above 7, where the source would buy the next
    /// college's first spell (past the last college, the mastery field) or read past its degree cost table (ledger AL7, AL8).</summary>
    InvalidTarget,
}

public readonly record struct AutoLevelParse(AutoLevelParseStatus Status, AutoLevelScheme? Scheme);

/// <summary>
/// The stored values a scheme buys with (a PC's or an NPC's, ARC-WORLD-036): race (for the stat maxima), the monstrous flag
/// (melee level 20), the eight primary stats and the stored magick and tech points, level and unspent points, the stored
/// basic and tech skill fields (points in bits 0..5, training in bits 6..7), the sixteen college levels (0..5), the eight
/// tech degrees (0..7), the hit and fatigue points and adjustments, and the mastered college (−1 none; ARC-MAGIC-004).
/// Equality compares the arrays' values.
/// </summary>
public sealed record AdvancementSheet(CharacterRace Race, bool Monstrous, ImmutableArray<int> Stats, int MagickPoints, int TechPoints,
    int Level, int UnspentPoints, ImmutableArray<int> BasicSkills, ImmutableArray<int> TechSkills, ImmutableArray<int> Colleges,
    ImmutableArray<int> TechDegrees, int HitPointPoints, int HitPointAdjustment, int FatiguePoints, int FatigueAdjustment,
    int MasteredCollege = SpellBookRules.NoMastery)
{
    public bool Equals(AdvancementSheet? other) => other is not null && Race == other.Race && Monstrous == other.Monstrous &&
        Same(Stats, other.Stats) && MagickPoints == other.MagickPoints && TechPoints == other.TechPoints && Level == other.Level &&
        UnspentPoints == other.UnspentPoints && Same(BasicSkills, other.BasicSkills) && Same(TechSkills, other.TechSkills) &&
        Same(Colleges, other.Colleges) && Same(TechDegrees, other.TechDegrees) && HitPointPoints == other.HitPointPoints &&
        HitPointAdjustment == other.HitPointAdjustment && FatiguePoints == other.FatiguePoints && FatigueAdjustment == other.FatigueAdjustment &&
        MasteredCollege == other.MasteredCollege;

    public override int GetHashCode() => HashCode.Combine(Race, Level, UnspentPoints, HitPointPoints, FatiguePoints, Hash(Stats), Hash(BasicSkills), Hash(Colleges));

    internal static bool Same(ImmutableArray<int> a, ImmutableArray<int> b) => a.IsDefault ? b.IsDefault : !b.IsDefault && a.SequenceEqual(b);

    internal static int Hash(ImmutableArray<int> values)
    {
        var hash = new HashCode();
        if (!values.IsDefault) foreach (var value in values) hash.Add(value);
        return hash.ToHashCode();
    }
}

/// <summary>A change of one basic or tech skill's level (the effect collections of skills), in level units.</summary>
public readonly record struct SkillModifier(int Skill, StatModifierOperator Operator, int Value);

/// <summary>
/// The changes a purchase reads besides the stored values: every stat change of the critter's effects (innate ones included),
/// the skill changes, the changes of the two maxima, and the poison level that lowers strength and dexterity.
/// </summary>
public sealed record AdvancementModifiers(ImmutableArray<StatModifier> Stats, ImmutableArray<SkillModifier> BasicSkills,
    ImmutableArray<SkillModifier> TechSkills, ImmutableArray<EffectModifier> MaxHitPoints, ImmutableArray<EffectModifier> MaxFatigue,
    int PoisonLevel = 0)
{
    public static AdvancementModifiers None { get; } = new([], [], [], [], []);
}

/// <summary>One purchase: what was bought and the value the source logs for it (a stat's effective value, a skill's points, a
/// college's level, a degree, or the new maximum).</summary>
public readonly record struct AdvancementPurchase(AdvancementTrack Track, int Index, int Value);

/// <summary>A scheme run: the sheet after it, whether the rule completed, and the purchases in order.</summary>
public sealed record AutoLevelOutcome(AdvancementSheet Sheet, bool Completed, ImmutableArray<AdvancementPurchase> Purchases);

/// <summary>
/// The source's auto-level schemes (ARC-WORLD-036): a rule of <c>&lt;token&gt; &lt;target&gt;,</c> steps that spends unspent
/// points, run for a PC at each level written and at creation, and for an NPC after its level update; and the purchase rules
/// it uses (stats, skills with their key-stat caps, spells with their level, intelligence and willpower minima, tech degrees
/// with their intelligence minima, hit and fatigue points). Checked in the original 1.0.7.4 executable, ledger AL1–AL14.
/// Pure functions over explicit values.
/// </summary>
public static class AutoLevelRules
{
    public const int StandardNpcScheme = 70;
    public const int PrimaryStats = 8, BasicSkills = 12, TechSkills = 4, Colleges = 16, TechDisciplines = 8;
    public const int SpellsPerCollege = 5, Spells = Colleges * SpellsPerCollege, MaxDegree = 7;
    /// <summary>The skill field's point bits.</summary>
    public const int SkillPointMask = SkillRules.PointMask;
    /// <summary>The bound of hit and fatigue points and adjustments a sheet admits (project policy; the installed critters stay far below).</summary>
    public const int MaxPoolValue = 10_000;
    private const int MaxPointStat = 210;
    /// <summary>The intelligence every spell rank needs (<see cref="SpellBookRules.MinimumIntelligence"/>).</summary>
    internal const int MinimumSpellIntelligence = SpellBookRules.MinimumIntelligence;

    private static readonly string[][] Tokens =
    [
        ["st", "dx", "cn", "be", "in", "pe", "wp", "ch"],
        ["bow", "dodge", "melee", "throwing", "backstab", "pickpocket", "prowling", "spottrap", "gambling", "haggle", "heal", "persuasion"],
        ["repair", "firearms", "picklock", "armtrap"],
        ["conveyance", "divination", "air", "earth", "fire", "water", "force", "mental", "meta", "morph", "nature", "necro_evil",
            "necro_good", "phantasm", "summoning", "temporal"],
        ["anatomical", "chemistry", "electric", "explosives", "gun_smithy", "mechanical", "smithy", "therapeutics"],
        ["maxhps", "maxfatigue"],
    ];

    private static readonly int[] DefaultWillpower = [6, 9, 12, 15, 18];
    private static readonly int[] DegreeCosts = [0, 1, 1, 1, 1, 1, 1, 1];
    private static readonly int[] DegreeIntelligence = [0, 5, 8, 11, 13, 15, 17, 19];

    /// <summary>The willpower each of the 80 spells needs when its record names none: 6, 9, 12, 15, 18 by rank.</summary>
    public static ImmutableArray<int> DefaultSpellWillpower { get; } = [.. Enumerable.Range(0, Spells).Select(DefaultSpellMinimumWillpower)];

    /// <summary>
    /// Reads a rule as the original's strtok tokenizer does (0x4A7340): a token up to the next space, then its target
    /// up to the next comma, repeated; the target is read as atoi reads it (leading spaces, a sign, the digits up to the first
    /// other character, 0 without digits); tokens are matched ASCII case-insensitively against the stats, basic skills, tech
    /// skills, colleges, tech disciplines and the two maxima, in that order. Project admission refuses what the source would
    /// crash on or misread: see <see cref="AutoLevelParseStatus"/>.
    /// </summary>
    public static AutoLevelParse Parse(ReadOnlySpan<byte> text)
    {
        foreach (var b in text)
            if (b is < 0x20 or > 0x7e) return new(AutoLevelParseStatus.InvalidText, null);
        var steps = ImmutableArray.CreateBuilder<AutoLevelStep>();
        var ignored = 0;
        var position = 0;
        while (Next(text, ref position, (byte)' ') is { } token)
        {
            if (Next(text, ref position, (byte)',') is not { } target) return new(AutoLevelParseStatus.MissingTarget, null);
            if (!Number(text.Slice(target.Start, target.Length), out var value, out var trailing)) return new(AutoLevelParseStatus.InvalidTarget, null);
            if (trailing) ignored++;
            var step = Resolve(text.Slice(token.Start, token.Length), value);
            if ((step.Track == AdvancementTrack.College && value > SpellsPerCollege) ||
                (step.Track == AdvancementTrack.TechDiscipline && value > MaxDegree)) return new(AutoLevelParseStatus.InvalidTarget, null);
            steps.Add(step);
        }
        return new(AutoLevelParseStatus.Parsed, new(steps.ToImmutable(), ignored));
    }

    /// <summary>A sheet the rules admit: defined race, every array at its length, stats within their race's range, magick and tech
    /// points 0..210, level 0..51, unspent points 0..56, skill fields 0..255, colleges 0..5, degrees 0..7, pools within
    /// ±<see cref="MaxPoolValue"/>.</summary>
    public static bool IsValid(AdvancementSheet? sheet)
    {
        if (sheet is null || !CharacterStatRules.IsDefined(sheet.Race) || sheet.Stats.IsDefault || sheet.Stats.Length != PrimaryStats ||
            sheet.BasicSkills.IsDefault || sheet.BasicSkills.Length != BasicSkills || sheet.TechSkills.IsDefault ||
            sheet.TechSkills.Length != TechSkills || sheet.Colleges.IsDefault || sheet.Colleges.Length != Colleges ||
            sheet.TechDegrees.IsDefault || sheet.TechDegrees.Length != TechDisciplines) return false;
        for (var stat = 0; stat < PrimaryStats; stat++)
            if (!CharacterStatRules.Range(sheet.Race, (CharacterStat)stat).Contains(sheet.Stats[stat])) return false;
        return sheet.MagickPoints is >= 0 and <= MaxPointStat && sheet.TechPoints is >= 0 and <= MaxPointStat &&
            sheet.Level is >= 0 and <= ExperienceRules.MaxLevel && sheet.UnspentPoints is >= 0 and <= ExperienceRules.MaxUnspentPoints &&
            sheet.BasicSkills.All(v => v is >= 0 and <= 255) && sheet.TechSkills.All(v => v is >= 0 and <= 255) &&
            sheet.Colleges.All(v => v is >= 0 and <= SpellsPerCollege) && sheet.TechDegrees.All(v => v is >= 0 and <= MaxDegree) &&
            sheet.MasteredCollege is >= SpellBookRules.NoMastery and < Colleges &&
            new[] { sheet.HitPointPoints, sheet.HitPointAdjustment, sheet.FatiguePoints, sheet.FatigueAdjustment }.All(v => Math.Abs(v) <= MaxPoolValue);
    }

    /// <summary>Modifiers the rules admit: defined stats and operators, skills in range, no divide by 0, magnitudes within
    /// ±1,000,000, poison 0..1000.</summary>
    public static bool IsValid(AdvancementModifiers? modifiers) =>
        modifiers is not null && !modifiers.Stats.IsDefault && !modifiers.BasicSkills.IsDefault && !modifiers.TechSkills.IsDefault &&
        !modifiers.MaxHitPoints.IsDefault && !modifiers.MaxFatigue.IsDefault && modifiers.PoisonLevel is >= 0 and <= CharacterStatRules.MaxPoisonLevel &&
        modifiers.Stats.All(m => CharacterStatRules.IsDefined(m.Stat) && Valid(m.Operator, m.Value)) &&
        modifiers.BasicSkills.All(m => m.Skill is >= 0 and < BasicSkills && Valid(m.Operator, m.Value)) &&
        modifiers.TechSkills.All(m => m.Skill is >= 0 and < TechSkills && Valid(m.Operator, m.Value)) &&
        modifiers.MaxHitPoints.All(m => Valid(m.Operator, m.Value)) && modifiers.MaxFatigue.All(m => Valid(m.Operator, m.Value));

    /// <summary>
    /// Runs a critter's scheme (0x4A7030): nothing without a rule (scheme 0 or a missing rule: not
    /// completed); otherwise the rule, and for an NPC whose rule completed the standard NPC rule 70 after it. A PC never runs
    /// rule 70. The result is completed when the last rule run completed.
    /// </summary>
    [OriginalFunction(0x4A7030, OriginalFunctionScope.Partial)]
    public static AutoLevelOutcome Apply(AdvancementSheet sheet, AdvancementModifiers modifiers, AutoLevelScheme? scheme,
        AutoLevelScheme? standardNpcScheme, bool npc, ImmutableArray<int> spellWillpower)
    {
        Check(sheet, modifiers, spellWillpower);
        if (scheme is null) return new(sheet, false, []);
        var work = new Work(sheet, modifiers, spellWillpower);
        var completed = Run(work, scheme);
        // A missing standard rule is "no rule": nothing runs and the run is not completed (as for the critter's own scheme).
        if (completed && npc) completed = standardNpcScheme is not null && Run(work, standardNpcScheme);
        return new(work.Sheet(), completed, work.Purchases.ToImmutable());
    }

    /// <summary>A stat's effective value: base, then poison on strength and dexterity, then the effects, clamped to the race's
    /// range (the source's stat read for a primary or point stat).</summary>
    public static int EffectiveStat(AdvancementSheet sheet, AdvancementModifiers modifiers, CharacterStat stat)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        ArgumentNullException.ThrowIfNull(modifiers);
        return new Work(sheet, modifiers, DefaultSpellWillpower).Effective(stat);
    }

    /// <summary>A basic skill's level (see <see cref="Work.BasicLevel"/>).</summary>
    public static int BasicSkillLevel(AdvancementSheet sheet, AdvancementModifiers modifiers, int skill) =>
        new Work(sheet, modifiers, DefaultSpellWillpower).BasicLevel(skill);

    /// <summary>A tech skill's level (see <see cref="Work.TechLevel"/>).</summary>
    public static int TechSkillLevel(AdvancementSheet sheet, AdvancementModifiers modifiers, int skill) =>
        new Work(sheet, modifiers, DefaultSpellWillpower).TechLevel(skill);

    /// <summary>The cost of raising a stat to <paramref name="value"/> (0x4B0F50): 0 up to 1, 1 above.</summary>
    [OriginalFunction(0x4B0F50, OriginalFunctionScope.Full)]
    public static int StatCost(int value) => Math.Clamp(value, 1, 20) <= 1 ? 0 : 1;

    /// <summary>The highest skill level a key stat allows (<see cref="SkillRules.KeyStatCap"/>, 0x4C5F70).</summary>
    public static int SkillCap(int keyStat) => SkillRules.KeyStatCap(keyStat);

    /// <summary>The willpower a spell needs when its record names none (0x4502F0, the value before the spell table is loaded;
    /// 0x4502E0 reads the intelligence minimum, 5): 6, 9, 12, 15, 18 by rank; the rank-2 value outside 0..79.</summary>
    [OriginalFunction(0x4502F0, OriginalFunctionScope.Partial)]
    public static int DefaultSpellMinimumWillpower(int spell) => spell is >= 0 and < Spells ? DefaultWillpower[spell % 5] : DefaultWillpower[1];

    /// <summary>The character level a spell rank needs (<see cref="SpellBookRules.MinimumLevel"/>, 0x4B1770): 1, 1, 5, 10, 15.</summary>
    public static int SpellMinimumLevel(int spell) => SpellBookRules.MinimumLevel(spell);

    /// <summary>The intelligence a tech degree needs (0x4B0110): 0, 5, 8, 11, 13, 15, 17, 19.</summary>
    [OriginalFunction(0x4B0110, OriginalFunctionScope.Full)]
    public static int DegreeMinimumIntelligence(int degree) => degree is >= 0 and <= MaxDegree ? DegreeIntelligence[degree]
        : throw new ArgumentOutOfRangeException(nameof(degree));

    /// <summary>The tech points a degree costs (0x4B00A0): 0 for degree 0, 1 for each of 1..7.</summary>
    [OriginalFunction(0x4B00A0, OriginalFunctionScope.Full)]
    public static int DegreeCost(int degree) => degree is >= 0 and <= MaxDegree ? DegreeCosts[degree]
        : throw new ArgumentOutOfRangeException(nameof(degree));

    // One rule (0x4A7340): each step buys until its target; running out of points ends the rule, not completed;
    // any other failure skips to the next step; a token that names nothing ends the rule, not completed.
    [OriginalFunction(0x4A7340, OriginalFunctionScope.Partial)]
    private static bool Run(Work work, AutoLevelScheme scheme)
    {
        foreach (var step in scheme.Steps)
        {
            var result = step.Track switch
            {
                AdvancementTrack.Stat => BuyStat(work, step.Index, step.Target),
                AdvancementTrack.BasicSkill => BuyBasicSkill(work, step.Index, step.Target),
                AdvancementTrack.TechSkill => BuyTechSkill(work, step.Index, step.Target),
                AdvancementTrack.College => BuySpell(work, step.Index, step.Target),
                AdvancementTrack.TechDiscipline => BuyDegree(work, step.Index, step.Target),
                AdvancementTrack.HitPoints or AdvancementTrack.Fatigue => BuyPool(work, step.Track.Value, step.Target),
                _ => Purchase.Unknown,
            };
            if (result is Purchase.NotEnoughPoints or Purchase.Unknown) return false;
        }
        return true;
    }

    private enum Purchase { Done, NotEnoughPoints, Failed, Unknown }

    // 0x4A75E0: from the base value, pay each unit's cost from the unspent points, write the base; a write that does not
    // reach the value (the race maximum) fails. The loop continues from the base value (the original; the reconstruction reads
    // the effective value, ledger AL4).
    [OriginalFunction(0x4A75E0, OriginalFunctionScope.Full)]
    private static Purchase BuyStat(Work work, int stat, int target)
    {
        var current = work.Stats[stat];
        while (current < target)
        {
            var next = current + 1;
            var cost = StatCost(next);
            if (cost > work.Unspent) return Purchase.NotEnoughPoints;
            work.SetStat(stat, next);
            current = work.Stats[stat];
            if (current < next) return Purchase.Failed;
            work.Unspent -= cost;
            work.Log(AdvancementTrack.Stat, stat, work.Effective((CharacterStat)stat));
        }
        return Purchase.Done;
    }

    // 0x4A76B0: one point at a time while the level is below the target; a point the key stat refuses raises
    // that stat by one instead.
    [OriginalFunction(0x4A76B0, OriginalFunctionScope.Full)]
    private static Purchase BuyBasicSkill(Work work, int skill, int target)
    {
        var level = work.BasicLevel(skill);
        while (level < target)
        {
            const int cost = 1;
            if (cost > work.Unspent) return Purchase.NotEnoughPoints;
            var points = (work.Basic[skill] & SkillPointMask) + cost;
            if (work.SetBasicPoints(skill, points) == points)
            {
                work.Unspent -= cost;
                level = work.BasicLevel(skill);
                work.Log(AdvancementTrack.BasicSkill, skill, points);
            }
            else
            {
                var key = (int)SkillRules.KeyStat(SkillRules.Basic(skill));
                var raised = BuyStat(work, key, work.Stats[key] + 1);
                if (raised != Purchase.Done) return raised;
            }
        }
        return Purchase.Done;
    }

    // 0x4A77A0: as the basic step, through the tech setter (which also moves the tech points).
    [OriginalFunction(0x4A77A0, OriginalFunctionScope.Full)]
    private static Purchase BuyTechSkill(Work work, int skill, int target)
    {
        var level = work.TechLevel(skill);
        while (level < target)
        {
            const int cost = 1;
            if (cost > work.Unspent) return Purchase.NotEnoughPoints;
            var points = (work.Tech[skill] & SkillPointMask) + cost;
            if (work.SetTechPoints(skill, points) == points)
            {
                work.Unspent -= cost;
                level = work.TechLevel(skill);
                work.Log(AdvancementTrack.TechSkill, skill, points);
            }
            else
            {
                var key = (int)SkillRules.KeyStat(SkillRules.Tech(skill));
                var raised = BuyStat(work, key, work.Stats[key] + 1);
                if (raised != Purchase.Done) return raised;
            }
        }
        return Purchase.Done;
    }

    // 0x4A7890: the next spell of the college while its level is below the target; a spell refused for the character
    // level fails the step, one refused otherwise raises intelligence (when below 5) or else willpower by one.
    [OriginalFunction(0x4A7890, OriginalFunctionScope.Full)]
    private static Purchase BuySpell(Work work, int college, int target)
    {
        var level = work.College[college];
        var spell = level + SpellsPerCollege * college;
        while (level < target)
        {
            const int cost = 1;
            if (cost > work.Unspent) return Purchase.NotEnoughPoints;
            if (work.AddSpell(spell))
            {
                work.Unspent -= cost;
                level++;
                work.Log(AdvancementTrack.College, college, level);
                spell++;
                continue;
            }
            if (SpellMinimumLevel(spell) > work.Level) return Purchase.Failed;
            var stat = MinimumSpellIntelligence > work.Effective(CharacterStat.Intelligence) ? 4 : 6;
            var raised = BuyStat(work, stat, work.Stats[stat] + 1);
            if (raised != Purchase.Done) return raised;
        }
        return Purchase.Done;
    }

    // 0x4A79C0: the next degree while below the target; one the intelligence refuses raises intelligence by one.
    [OriginalFunction(0x4A79C0, OriginalFunctionScope.Full)]
    private static Purchase BuyDegree(Work work, int discipline, int target)
    {
        var degree = work.Degrees[discipline];
        while (degree < target)
        {
            var cost = DegreeCosts[degree + 1];
            if (cost > work.Unspent) return Purchase.NotEnoughPoints;
            var next = work.RaiseDegree(discipline);
            if (next != degree)
            {
                work.Unspent -= cost;
                degree = next;
                work.Log(AdvancementTrack.TechDiscipline, discipline, next);
            }
            else
            {
                var raised = BuyStat(work, 4, work.Stats[4] + 1);
                if (raised != Purchase.Done) return raised;
            }
        }
        return Purchase.Done;
    }

    // 0x4A7AA0: one hit (fatigue) point at a time while the maximum is below the target; a point that does not raise the
    // maximum by at least one fails.
    [OriginalFunction(0x4A7AA0, OriginalFunctionScope.Full)]
    private static Purchase BuyPool(Work work, AdvancementTrack track, int target)
    {
        var hitPoints = track == AdvancementTrack.HitPoints;
        var current = hitPoints ? work.MaxHitPoints() : work.MaxFatigue();
        while (current < target)
        {
            if (work.Unspent < 1) return Purchase.NotEnoughPoints;
            if (hitPoints) work.HitPointPoints = Math.Min(work.HitPointPoints + 1, MaxPoolValue);
            else work.FatiguePoints = Math.Min(work.FatiguePoints + 1, MaxPoolValue);
            var next = hitPoints ? work.MaxHitPoints() : work.MaxFatigue();
            if (next < current + 1) return Purchase.Failed;
            work.Unspent -= 1;
            work.Log(track, 0, next);
            current = next;
        }
        return Purchase.Done;
    }

    private static AutoLevelStep Resolve(ReadOnlySpan<byte> token, int target)
    {
        for (var collection = 0; collection < Tokens.Length; collection++)
            for (var index = 0; index < Tokens[collection].Length; index++)
                if (EqualsIgnoreCase(token, Tokens[collection][index]))
                    return collection switch
                    {
                        0 => new(AdvancementTrack.Stat, index, target),
                        1 => new(AdvancementTrack.BasicSkill, index, target),
                        2 => new(AdvancementTrack.TechSkill, index, target),
                        3 => new(AdvancementTrack.College, index, target),
                        4 => new(AdvancementTrack.TechDiscipline, index, target),
                        _ => new(index == 0 ? AdvancementTrack.HitPoints : AdvancementTrack.Fatigue, 0, target),
                    };
        return new(null, 0, target);
    }

    private static bool EqualsIgnoreCase(ReadOnlySpan<byte> token, string name)
    {
        if (token.Length != name.Length) return false;
        for (var i = 0; i < token.Length; i++)
        {
            var c = token[i] is >= (byte)'A' and <= (byte)'Z' ? (char)(token[i] | 0x20) : (char)token[i];
            if (c != name[i]) return false;
        }
        return true;
    }

    // The source's tokenizer: skip separators, take the token up to the next separator (consumed) or the end.
    private static (int Start, int Length)? Next(ReadOnlySpan<byte> text, ref int position, byte separator)
    {
        while (position < text.Length && text[position] == separator) position++;
        if (position >= text.Length) return null;
        var start = position;
        while (position < text.Length && text[position] != separator) position++;
        var length = position - start;
        if (position < text.Length) position++;
        return (start, length);
    }

    // atoi: leading spaces, an optional sign, the digits up to the first other character (0 without digits); more than 9 digits
    // is refused, as atoi would overflow. Anything after the number is reported as ignored text.
    private static bool Number(ReadOnlySpan<byte> text, out int value, out bool trailing)
    {
        value = 0;
        var i = 0;
        while (i < text.Length && text[i] == (byte)' ') i++;
        var negative = false;
        if (i < text.Length && text[i] is (byte)'+' or (byte)'-') negative = text[i++] == (byte)'-';
        var start = i;
        long result = 0;
        while (i < text.Length && text[i] is >= (byte)'0' and <= (byte)'9')
        {
            if (i - start == 9) { trailing = false; return false; }
            result = result * 10 + (text[i++] - '0');
        }
        var rest = i;
        while (rest < text.Length && text[rest] == (byte)' ') rest++;
        trailing = rest < text.Length;
        value = (int)(negative ? -result : result);
        return true;
    }

    private static bool Valid(StatModifierOperator op, int value) =>
        CharacterStatRules.IsDefined(op) && !(op == StatModifierOperator.Divide && value == 0) &&
        Math.Abs((long)value) <= CharacterStatLimits.ModifierMagnitudeCeiling;

    private static void Check(AdvancementSheet sheet, AdvancementModifiers modifiers, ImmutableArray<int> spellWillpower)
    {
        if (!IsValid(sheet)) throw new ArgumentException("Invalid advancement sheet.", nameof(sheet));
        if (!IsValid(modifiers)) throw new ArgumentException("Invalid advancement modifiers.", nameof(modifiers));
        if (spellWillpower.IsDefault || spellWillpower.Length != Spells) throw new ArgumentException("80 spell minima expected.", nameof(spellWillpower));
    }

    /// <summary>The mutable copy of a sheet one run works on, with the source's setters and level reads.</summary>
    internal sealed class Work
    {
        private readonly AdvancementSheet _sheet;
        private readonly AdvancementModifiers _modifiers;
        private readonly ImmutableArray<int> _willpower;

        internal Work(AdvancementSheet sheet, AdvancementModifiers modifiers, ImmutableArray<int> willpower)
        {
            _sheet = sheet;
            _modifiers = modifiers;
            _willpower = willpower;
            Stats = [.. sheet.Stats];
            Basic = [.. sheet.BasicSkills];
            Tech = [.. sheet.TechSkills];
            College = [.. sheet.Colleges];
            Degrees = [.. sheet.TechDegrees];
            MagickPoints = sheet.MagickPoints;
            TechPoints = sheet.TechPoints;
            Unspent = sheet.UnspentPoints;
            HitPointPoints = sheet.HitPointPoints;
            FatiguePoints = sheet.FatiguePoints;
        }

        internal int[] Stats { get; }
        internal int[] Basic { get; }
        internal int[] Tech { get; }
        internal int[] College { get; }
        internal int[] Degrees { get; }
        internal int MagickPoints { get; private set; }
        internal int TechPoints { get; private set; }
        internal int Unspent { get; set; }
        internal int HitPointPoints { get; set; }
        internal int FatiguePoints { get; set; }
        internal int Level => _sheet.Level;
        internal ImmutableArray<AdvancementPurchase>.Builder Purchases { get; } = ImmutableArray.CreateBuilder<AdvancementPurchase>();

        internal void Log(AdvancementTrack track, int index, int value) => Purchases.Add(new(track, index, value));

        /// <summary>The willpower a spell rank needs (the run's table).</summary>
        internal int SpellWillpower(int spell) => _willpower[spell];

        // The stat setter clamps to the race's range.
        internal void SetStat(int stat, int value)
        {
            var range = CharacterStatRules.Range(_sheet.Race, (CharacterStat)stat);
            Stats[stat] = Math.Clamp(value, range.Minimum, range.Maximum);
        }

        internal int Effective(CharacterStat stat, int poison = -1)
        {
            var baseValue = stat switch
            {
                <= CharacterStat.Charisma => Stats[(int)stat],
                CharacterStat.MagickPoints => MagickPoints,
                CharacterStat.TechPoints => TechPoints,
                _ => throw new ArgumentOutOfRangeException(nameof(stat)),
            };
            var context = new CharacterStatContext(0, poison < 0 ? _modifiers.PoisonLevel : poison);
            var range = CharacterStatRules.Range(_sheet.Race, stat);
            var value = EffectModifiers.Apply(baseValue + CharacterStatRules.Situational(stat, context),
                _modifiers.Stats.Where(m => m.Stat == stat).Select(m => new EffectModifier(m.Operator, m.Value)).ToArray());
            return Math.Clamp(value, range.Minimum, range.Maximum);
        }

        // A basic skill's level (SkillRules.Level, 0x4C5EB0): 4 × points (20 for a monstrous critter's melee), then the
        // effects, clamped 0..20, then capped by the effective key stat.
        internal int BasicLevel(int skill)
        {
            var id = SkillRules.Basic(skill);
            return SkillRules.Level(id, Basic[skill], Effective(SkillRules.KeyStat(id)), _sheet.Monstrous, Skill(_modifiers.BasicSkills, skill));
        }

        // A tech skill's level (SkillRules.Level, 0x4C65E0): as the basic level, without the monstrous case.
        internal int TechLevel(int skill)
        {
            var id = SkillRules.Tech(skill);
            return SkillRules.Level(id, Tech[skill], Effective(SkillRules.KeyStat(id)), false, Skill(_modifiers.TechSkills, skill));
        }

        // The basic points setter (SkillRules.SetPoints, 0x4C6000): refused (the stored points returned) when 4 × points is
        // above the key stat's cap; the training bits stay.
        internal int SetBasicPoints(int skill, int points)
        {
            var id = SkillRules.Basic(skill);
            var change = SkillRules.SetPoints(id, Basic[skill], points, Effective(SkillRules.KeyStat(id)));
            Basic[skill] = change.Stored;
            return change.Points;
        }

        // The tech points setter (SkillRules.SetPoints, 0x4C66E0): as the basic setter, and the stored tech points move by the
        // change.
        internal int SetTechPoints(int skill, int points)
        {
            var id = SkillRules.Tech(skill);
            var change = SkillRules.SetPoints(id, Tech[skill], points, Effective(SkillRules.KeyStat(id)));
            Tech[skill] = change.Stored;
            TechPoints = Math.Clamp(TechPoints + change.TechPointsChange, 0, MaxPointStat);
            return change.Points;
        }

        // The spell book's unforced learn (SpellBookRules.Learn, 0x4B1790): the college's next rank only, with its character
        // level, intelligence 5 and its willpower; the magick points rise by the spell's cost, 1, clamped as the stat setter does.
        internal bool AddSpell(int spell)
        {
            var change = SpellBookRules.Learn(new([.. College], _sheet.MasteredCollege), spell, force: false, Level,
                Effective(CharacterStat.Intelligence), Effective(CharacterStat.Willpower), _willpower[spell]);
            if (!change.Succeeded) return false;
            MagickPoints = Math.Clamp(MagickPoints + change.MagickPoints, 0, MaxPointStat);
            College[spell / SpellsPerCollege] = change.Book.Ranks[spell / SpellsPerCollege];
            return true;
        }

        // 0x4AFEC0: the next degree up to 7 when the intelligence allows it; the tech points rise by its cost.
        [OriginalFunction(0x4AFEC0, OriginalFunctionScope.Full)]
        internal int RaiseDegree(int discipline)
        {
            var degree = Degrees[discipline];
            if (degree + 1 > MaxDegree) return degree;
            if (DegreeMinimumIntelligence(degree + 1) > Effective(CharacterStat.Intelligence)) return degree;
            TechPoints = Math.Clamp(TechPoints + DegreeCosts[degree + 1], 0, MaxPointStat);
            Degrees[discipline] = degree + 1;
            return degree + 1;
        }

        // 0x4AFFF0: a degree above 0 falls by one and the tech points by its cost; no intelligence check.
        [OriginalFunction(0x4AFFF0, OriginalFunctionScope.Full)]
        internal int LowerDegree(int discipline)
        {
            var degree = Degrees[discipline];
            if (degree == 0) return degree;
            TechPoints = Math.Clamp(TechPoints - DegreeCosts[degree], 0, MaxPointStat);
            Degrees[discipline] = degree - 1;
            return degree - 1;
        }

        internal int MaxHitPoints() => LifeRules.MaxHitPoints(Facts(), _modifiers.PoisonLevel);

        internal int MaxFatigue() => LifeRules.MaxFatigue(Facts());

        private LifeFacts Facts() => new()
        {
            HitPointPoints = HitPointPoints,
            HitPointAdjustment = _sheet.HitPointAdjustment,
            FatiguePoints = FatiguePoints,
            FatigueAdjustment = _sheet.FatigueAdjustment,
            Level = _sheet.Level,
            Strength = Effective(CharacterStat.Strength, 0),
            PoisonedStrength = [Effective(CharacterStat.Strength, 100), Effective(CharacterStat.Strength, 200), Effective(CharacterStat.Strength, 300)],
            Constitution = Effective(CharacterStat.Constitution),
            Willpower = Effective(CharacterStat.Willpower),
            MaxHitPointEffects = _modifiers.MaxHitPoints,
            MaxFatigueEffects = _modifiers.MaxFatigue,
        };

        private static EffectModifier[] Skill(ImmutableArray<SkillModifier> modifiers, int skill) =>
            modifiers.Where(m => m.Skill == skill).Select(m => new EffectModifier(m.Operator, m.Value)).ToArray();

        internal AdvancementSheet Sheet() => _sheet with
        {
            Stats = [.. Stats], MagickPoints = MagickPoints, TechPoints = TechPoints, UnspentPoints = Unspent, BasicSkills = [.. Basic],
            TechSkills = [.. Tech], Colleges = [.. College], TechDegrees = [.. Degrees], HitPointPoints = HitPointPoints,
            FatiguePoints = FatiguePoints,
        };
    }
}
