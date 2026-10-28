using Arcanum.Session;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Combat;

namespace Arcanum.TerrainViewer.Core;

/// <summary>
/// One row of the stats page: the row (0 hit points, 1 fatigue, 2..9 the primary stats in the sheet's order), its track and stat
/// (null for a pool), the project's label, the stored value (a stat's base, a pool's bought points), the effective value (a stat's
/// effective level, a pool's maximum), the cost the next point shows (the original's hint: <see cref="CharacterPointRules.NextStatCost"/>,
/// 0 at the base's maximum; a pool's 1) and the stored value the editor's floor holds (the − button refuses at it).
/// </summary>
public sealed record AreaStatRow(int Index, AdvancementTrack Track, CharacterStat? Stat, string Label, int Value, int Effective, int NextCost,
    int FloorValue);

/// <summary>The stats page of the hero: the ten rows and the unspent points.</summary>
public sealed record AreaStatsPageView(IReadOnlyList<AreaStatRow> Rows, int UnspentPoints, bool EditorOpen);

/// <summary>One + or − press on the stats page: whether it changed the hero and the window's message.</summary>
public sealed record AreaStatPress(bool Changed, string Message, SessionPointResult Result);

/// <summary>
/// ARC-UI-034 (N10b slice 3, FR-CHAR-015): the + and − buttons of the character editor's stats, Godot-free. What the original has
/// was read in the retail 1.0.7.4 executable (ledger CP3, CP4 and "Character editor in creation mode", CM8): the editor's button
/// handler (0x55A5C0) knows ten rows, hit points (entry 4) and fatigue (entry 6) and then the eight primary stats in the sheet's
/// order (strength, constitution, dexterity, beauty, intelligence, willpower, perception, charisma; entries 7, 9, ..., 21, each a
/// base value), and its − compares the row's stored value with the editor's snapshot. + and − go through the session's point pool
/// (<see cref="GameSessionOwner.SpendHeroPoint"/>, <see cref="GameSessionOwner.RefundHeroPoint"/>, ARC-WORLD-052) with the editor
/// the sheet opens; the rules (cost, maximum, the setter's guards) are the pool's.
/// </summary>
public static class AreaStatsPage
{
    public const int Rows = 2 + AutoLevelRules.PrimaryStats;

    /// <summary>The primary stats in the order of the original's sheet and button table (0x5C8034).</summary>
    public static IReadOnlyList<CharacterStat> StatOrder { get; } =
    [
        CharacterStat.Strength, CharacterStat.Constitution, CharacterStat.Dexterity, CharacterStat.Beauty,
        CharacterStat.Intelligence, CharacterStat.Willpower, CharacterStat.Perception, CharacterStat.Charisma,
    ];

    /// <summary>The track and index of a page row: 0 hit points, 1 fatigue, 2..9 a primary stat.</summary>
    public static (AdvancementTrack Track, int Index) Track(int row) => row switch
    {
        0 => (AdvancementTrack.HitPoints, 0),
        1 => (AdvancementTrack.Fatigue, 0),
        >= 2 and < Rows => (AdvancementTrack.Stat, (int)StatOrder[row - 2]),
        _ => throw new ArgumentOutOfRangeException(nameof(row)),
    };

    /// <summary>The page of the session's hero, or null without a hero.</summary>
    public static AreaStatsPageView? Build(GameSessionOwner session)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (session.QueryHero() is not { } hero || session.QueryHeroExperience() is not { } experience || session.QueryHeroLife() is not { } life)
            return null;
        var state = life.Record.State;
        // The effective read of the sheet (AreaCharacterSheet.Build): poison and both crippled legs as the source reads them.
        var stats = session.QueryHeroStats(new(0, state.PoisonLevel, state.Conditions.HasFlag(LifeConditions.CrippledLegsBoth)))!.Value.Snapshot;
        var floor = session.QueryHeroEditorFloor();
        var sheet = new AdvancementSheet(hero.Race, false, [.. hero.Stats.Take(AutoLevelRules.PrimaryStats)], hero.Stats[8], hero.Stats[9],
            hero.Level, hero.UnspentPoints, hero.Advancement.BasicSkills, hero.Advancement.TechSkills, hero.Advancement.Colleges,
            hero.Advancement.TechDegrees, hero.Advancement.HitPointPoints, 0, hero.Advancement.FatiguePoints, 0, hero.Advancement.MasteredCollege);
        var rows = new AreaStatRow[Rows];
        rows[0] = new(0, AdvancementTrack.HitPoints, null, "Hit points", hero.Advancement.HitPointPoints, life.Status.MaxHitPoints,
            CharacterPointRules.PoolPointCost, floor?.HitPointPoints ?? hero.Advancement.HitPointPoints);
        rows[1] = new(1, AdvancementTrack.Fatigue, null, "Fatigue", hero.Advancement.FatiguePoints, life.Status.MaxFatigue,
            CharacterPointRules.PoolPointCost, floor?.FatiguePoints ?? hero.Advancement.FatiguePoints);
        for (var row = 2; row < Rows; row++)
        {
            var stat = StatOrder[row - 2];
            var value = hero.Stats[(int)stat];
            rows[row] = new(row, AdvancementTrack.Stat, stat, stat.ToString(), value, stats?.GetEffective(stat) ?? value,
                CharacterPointRules.NextStatCost(sheet, stat), floor?.Stats[(int)stat] ?? value);
        }
        return new(rows, experience.UnspentPoints, floor is not null);
    }

    /// <summary>+ on a row: one point (or the stat's cost) through the session's pool.</summary>
    public static AreaStatPress Spend(GameSessionOwner session, int row)
    {
        ArgumentNullException.ThrowIfNull(session);
        var (track, index) = Track(row);
        return Press(session.SpendHeroPoint(session.Version, track, index), true);
    }

    /// <summary>− on a row: one step back through the session's pool, down to the editor's floor.</summary>
    public static AreaStatPress Refund(GameSessionOwner session, int row)
    {
        ArgumentNullException.ThrowIfNull(session);
        var (track, index) = Track(row);
        return Press(session.RefundHeroPoint(session.Version, track, index), false);
    }

    private static AreaStatPress Press(SessionPointResult result, bool spend)
    {
        if (result.Disposition == GameSessionDisposition.Changed)
            return new(true, spend ? $"spent {result.Change!.Points} point(s)" : $"refunded {result.Change!.Points} point(s)", result);
        // Project texts for the refusals; the original shows its charedit.mes messages (CP3, CP4).
        var message = result.Failure == GameSessionFailure.PointRefused ? result.Change!.Status switch
        {
            PointChangeStatus.NotEnoughPoints => "not enough unspent points",
            PointChangeStatus.AtMaximum => "at its maximum",
            PointChangeStatus.AtFloor => "nothing bought since the sheet opened to refund",
            PointChangeStatus.AtMinimum => "at its minimum",
            PointChangeStatus.SkillNeedsStat => $"{result.Change.Skill} needs it",
            PointChangeStatus.SpellNeedsStat => "a known spell needs it",
            PointChangeStatus.DegreeNeedsStat => "a tech degree needs it",
            var status => status.ToString(),
        } : result.Failure.ToString();
        return new(false, "refused: " + message, result);
    }

    /// <summary>The window's text of one row: project words around the values.</summary>
    public static string Line(AreaStatRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (row.Stat is null) return $"{row.Label}: {row.Effective} ({row.Value} bought); next point costs {row.NextCost}";
        var next = row.NextCost == 0 ? "at its maximum" : $"next point costs {row.NextCost}";
        return row.Effective == row.Value ? $"{row.Label}: {row.Value}; {next}" : $"{row.Label}: {row.Effective} (base {row.Value}); {next}";
    }
}
