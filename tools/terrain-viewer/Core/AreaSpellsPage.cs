using Arcanum.Data.Content;
using Arcanum.Data.Magic;
using Arcanum.Session;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Magic;

namespace Arcanum.TerrainViewer.Core;

public enum AreaSpellDataStatus { Ready, ModuleOutsideGameRoot, MountFailed, StackFailed, DataFailed }

/// <summary>The spell data (texts and records) for the area's game folder, or why none was loaded.</summary>
public sealed record AreaSpellDataLoad(AreaSpellDataStatus Status, SpellData? Data, string? Detail);

/// <summary>How a rank of the shown college looks: known (full colour), the next to learn, or above it (greyed).</summary>
public enum AreaSpellState { Known, Next, Locked }

/// <summary>
/// One spell of the shown college: its number (5 × college + rank − 1) and rank (1..5), its name (the player's <c>mes\spell.mes</c>,
/// else the project's label), its description, the character level it needs (shown on the page), and for its hover hint the
/// willpower it needs (the minima the session's pool checks: the spell list's <c>IQ:</c> loaded with the hero, else the defaults),
/// the fatigue cost and, for a maintained spell, the maintenance and its period in seconds as the hero would
/// pay them (null without the spell data), and how it looks.
/// </summary>
public sealed record AreaSpellRow(int Spell, int Rank, string Name, string? Description, int MinimumLevel, int MinimumWillpower,
    int? Cost, int? MaintainCost, int? MaintainPeriod, AreaSpellState State);

/// <summary>One college tab: the college, its name, the hero's known rank and whether the hero mastered it; its hover hint is
/// the name and the college's description, or the mastery description for the mastered college.</summary>
public sealed record AreaCollegeTab(int College, string Name, int Rank, bool Mastered, string? Description);

/// <summary>
/// The spells page of the hero: the sixteen college tabs, the shown college, its five spells, the known rank and the rank the
/// editor's floor holds, the unspent points and whether the editor is open. + learns the next rank (shown below five ranks) and −
/// unlearns the highest (shown above the floor), as the original's in-game sheet shows its buttons.
/// </summary>
public sealed record AreaSpellsPageView(IReadOnlyList<AreaCollegeTab> Colleges, int College, IReadOnlyList<AreaSpellRow> Spells, int Rank,
    int FloorRank, int UnspentPoints, bool EditorOpen)
{
    public bool CanLearn => EditorOpen && Rank < SpellBookRules.Ranks;
    public bool CanUnlearn => EditorOpen && Rank > FloorRank;
    /// <summary>The spell + learns (the next rank) and − unlearns (the highest known), or null when there is none.</summary>
    public AreaSpellRow? NextSpell => Rank < SpellBookRules.Ranks ? Spells[Rank] : null;
    public AreaSpellRow? HighestSpell => Rank > 0 ? Spells[Rank - 1] : null;
}

/// <summary>One + or − press: whether it changed the hero and the window's message.</summary>
public sealed record AreaSpellPress(bool Changed, string Message, SessionPointResult Result);

/// <summary>
/// ARC-MAGIC-008 (magic plan MT19): the spells page of the AREA viewer's character sheet, Godot-free. What the original college
/// page shows was read in the retail 1.0.7.4 executable (ledger "Spell page (ARC-MAGIC-008)", SG1–SG8): sixteen college tabs, the
/// selected college's five spells with their icons and names (the known ranks in full, the next one marked, the others greyed)
/// and each one's minimum level, a + that learns the next rank and a − that unlearns the highest, and hover hints (a spell: its
/// name, minimum willpower, fatigue cost with the maintenance, description; a college: its name and description, or the mastery
/// description for the mastered college). + and − go through the session's point pool (<see cref="GameSessionOwner.SpendHeroPoint"/>,
/// <see cref="GameSessionOwner.RefundHeroPoint"/>, track <see cref="AdvancementTrack.College"/>: the spell book's lane, MT3) with
/// the editor the sheet opens; a refusal names the failing check, as the original's messages do.
/// </summary>
public static class AreaSpellsPage
{
    public const int Colleges = SpellBookRules.Colleges, Ranks = SpellBookRules.Ranks;

    /// <summary>Loads the spell data (<c>rules\magictech.mes</c>, <c>mes\spell.mes</c>, <c>rules\spelllist.mes</c>) through the game
    /// folder's content stack of the module, as the original's magic-tech init reads it; otherwise the page shows the project's labels.</summary>
    public static AreaSpellDataLoad LoadData(string modulePath)
    {
        var found = AreaContentRoot.Discover(modulePath);
        if (found.Status != AreaContentRootStatus.Ready)
            return new(found.Status switch
            {
                AreaContentRootStatus.MountFailed => AreaSpellDataStatus.MountFailed,
                AreaContentRootStatus.StackFailed => AreaSpellDataStatus.StackFailed,
                _ => AreaSpellDataStatus.ModuleOutsideGameRoot,
            }, null, found.Detail);
        var opened = ContentStack.Open(found.Plan!);
        if (opened.Status != ContentStackStatus.Ready) return new(AreaSpellDataStatus.StackFailed, null, opened.Status.ToString());
        using var stack = opened.Stack!;
        var loaded = SpellData.Open(stack, SpellRecordMode.LikeOriginal);
        return loaded.Status != SpellDataStatus.Read
            ? new(AreaSpellDataStatus.DataFailed, null, $"{loaded.Status} {loaded.Refusal}")
            : new(AreaSpellDataStatus.Ready, loaded.Data, "spell data loaded");
    }

    /// <summary>The page of the session's hero showing <paramref name="college"/> (0..15), or null without a hero.</summary>
    public static AreaSpellsPageView? Build(GameSessionOwner session, SpellData? data, int college)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (college is < 0 or >= Colleges) throw new ArgumentOutOfRangeException(nameof(college));
        if (session.QueryHero() is not { } hero || session.QueryHeroSpellBook() is not { } book) return null;
        var floor = session.QueryHeroEditorFloor();
        // The stored race: the session has no effect that changes a hero's race stat.
        var caster = new SpellCaster(true, (int)hero.Race, book.MasteredCollege);
        var willpower = session.AutoLevel?.SpellWillpower ?? AutoLevelRules.DefaultSpellWillpower;
        var tabs = new AreaCollegeTab[Colleges];
        for (var c = 0; c < Colleges; c++)
        {
            var mastered = book.MasteredCollege == c;
            tabs[c] = new(c, CollegeName(data, c), book.Ranks[c], mastered, Text(data?.Texts.CollegeDescription(c, book.MasteredCollege)));
        }
        var rank = book.Ranks[college];
        var spells = new AreaSpellRow[Ranks];
        for (var r = 0; r < Ranks; r++)
        {
            var spell = college * Ranks + r;
            var record = data?.Records[spell];
            (int Cost, int Period)? maintain = record is { MaintainPeriod: > 0 }
                ? SpellCostRules.MaintainCost(record.MaintainCost, record.MaintainPeriod, spell, caster) : null;
            spells[r] = new(spell, r + 1, SpellName(data, spell), Text(data?.Texts.Description(spell)), SpellBookRules.MinimumLevel(spell),
                willpower[spell], record is null ? null : SpellCostRules.CastCost(record.Cost, spell, caster),
                maintain?.Cost, maintain?.Period, r < rank ? AreaSpellState.Known : r == rank ? AreaSpellState.Next : AreaSpellState.Locked);
        }
        return new(tabs, college, spells, rank, floor?.Colleges[college] ?? rank, hero.UnspentPoints, floor is not null);
    }

    /// <summary>+ on the shown college: learns its next rank through the session's pool.</summary>
    public static AreaSpellPress Learn(GameSessionOwner session, int college, SpellData? data)
    {
        ArgumentNullException.ThrowIfNull(session);
        return Press(session.SpendHeroPoint(session.Version, AdvancementTrack.College, college), true, session, college, data);
    }

    /// <summary>− on the shown college: unlearns its highest rank through the session's pool, down to the editor's floor.</summary>
    public static AreaSpellPress Unlearn(GameSessionOwner session, int college, SpellData? data)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (college is < 0 or >= Colleges) throw new ArgumentOutOfRangeException(nameof(college));
        var rank = session.QueryHeroSpellBook()?.Ranks[college];
        var press = Press(session.RefundHeroPoint(session.Version, AdvancementTrack.College, college), false, session, college, data);
        return press.Changed && rank is > 0 ? press with { Message = "unlearned " + SpellName(data, college * Ranks + rank.Value - 1) } : press;
    }

    private static AreaSpellPress Press(SessionPointResult result, bool learn, GameSessionOwner session, int college, SpellData? data)
    {
        if (result.Disposition == GameSessionDisposition.Changed)
        {
            var rank = session.QueryHeroSpellBook()!.Ranks[college];
            return new(true, learn ? "learned " + SpellName(data, college * Ranks + rank - 1) : "unlearned", result);
        }
        // Project texts for the refusals, in the original's terms (charedit.mes 18, 30, 26, 36 and 27, SG6).
        var message = result.Failure == GameSessionFailure.PointRefused ? result.Change!.Status switch
        {
            PointChangeStatus.NotEnoughPoints => "not enough unspent points",
            PointChangeStatus.LevelTooLow => "the character level is too low for this spell",
            PointChangeStatus.IntelligenceTooLow => "raise Intelligence first",
            PointChangeStatus.StatTooLow => $"raise {result.Change.Stat} first",
            PointChangeStatus.AtFloor => "a spell learned before the sheet opened cannot be unlearned",
            PointChangeStatus.AtMaximum => "all five spells of the college are known",
            PointChangeStatus.AtMinimum => "no spell of the college is known",
            var status => status.ToString(),
        } : result.Failure.ToString();
        return new(false, "refused: " + message, result);
    }

    /// <summary>The page's line of a spell: its name, how it looks and the minimum level (the original's "label: value").</summary>
    public static string Line(AreaSpellRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        var state = row.State switch { AreaSpellState.Known => "known", AreaSpellState.Next => "next", _ => "locked" };
        return $"{row.Rank}. {row.Name} ({state}) | minimum level {row.MinimumLevel}";
    }

    /// <summary>A spell's hover hint, in the original's order: the name, the minimum willpower, the fatigue cost with the
    /// maintenance per second or per period, the description.</summary>
    public static string Hint(AreaSpellRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        var cost = row.Cost is not { } value ? null
            : row.MaintainCost is not { } maintain ? $"Fatigue cost: {value}"
            : row.MaintainPeriod == 1 ? $"Fatigue cost: {value}  ({maintain} / second)"
            : $"Fatigue cost: {value}  ({maintain} / {row.MaintainPeriod} seconds)";
        return string.Join("\n", new[] { row.Name, $"Minimum willpower: {row.MinimumWillpower}", cost, row.Description }
            .Where(part => !string.IsNullOrEmpty(part)));
    }

    /// <summary>A college tab's hover hint: the name and the college's or, when mastered, the mastery's description.</summary>
    public static string Hint(AreaCollegeTab tab)
    {
        ArgumentNullException.ThrowIfNull(tab);
        return string.Join("\n", new[] { tab.Name, tab.Description }.Where(part => !string.IsNullOrEmpty(part)));
    }

    /// <summary>A college's name: the player's text (message 500 + college), else the project's label.</summary>
    public static string CollegeName(SpellData? data, int college) => Text(data?.Texts.CollegeName(college)) ?? $"College {college + 1}";

    /// <summary>A spell's name: the player's text (message n), else the project's label.</summary>
    public static string SpellName(SpellData? data, int spell) =>
        Text(data?.Texts.Name(spell)) ?? $"College {spell / Ranks + 1} spell {spell % Ranks + 1}";

    // An empty text counts as missing: the project's label (or no line) takes its place.
    private static string? Text(string? text) => string.IsNullOrEmpty(text) ? null : text;
}
