using Arcanum.Session;
using Arcanum.Simulation;

namespace Arcanum.World;

/// <summary>One scheme of the sheet's scheme page: its number and its name (<c>mes\userlevelname.mes</c> or <c>mes\gamelevelname.mes</c>).</summary>
public sealed record AreaSchemeEntry(int Number, string Name);

/// <summary>
/// ARC-UI-1742: what the sheet's auto-level scheme page shows (window 0x64CA60, art 567, filled by 0x55D060 and drawn by 0x55D210; ledger ALP1..ALP9): the
/// listed schemes in order (every number from 0 whose name exists), the shown critter's scheme number and its name for the line at the top
/// (null: a scheme without a name, the line stays empty).
/// </summary>
public sealed record AreaSchemesPageView(IReadOnlyList<AreaSchemeEntry> Entries, int Scheme, string? SchemeName)
{
    /// <summary>The rows the page has room for; more entries scroll.</summary>
    public int ShownRows => Math.Min(Entries.Count, AreaSchemesPage.Rows);

    /// <summary>The scroll bar's largest value: the bar exists whatever the count, its range is 0 up to 15 fewer than the entries (0x55EFF0).</summary>
    public int ScrollMaximum => Entries.Count < AreaSchemesPage.Rows + 1 ? 0 : Entries.Count - AreaSchemesPage.Rows;
}

/// <summary>
/// ARC-UI-1742: the scheme page's model and its one action, Godot-free. The page lists the schemes by name; a press on a row writes that scheme into
/// the shown critter (0x4A6D90, ledger ALP5): nothing is bought now, the hero's scheme runs at its next level step (AL12).
/// </summary>
public static class AreaSchemesPage
{
    public const int Rows = 15;

    /// <summary>The page for a critter whose scheme is <paramref name="scheme"/> (null: no scheme read, the line stays empty), or null when the name files
    /// did not read.</summary>
    [OriginalFunction(0x55D060, OriginalFunctionScope.Partial)]
    public static AreaSchemesPageView? Build(AreaCharacterSheetTexts texts, int? scheme)
    {
        ArgumentNullException.ThrowIfNull(texts);
        if (!texts.HasSchemeNames) return null;
        var entries = texts.SchemeNumbers().Select(number => new AreaSchemeEntry(number, texts.SchemeName(number) ?? string.Empty)).ToArray();
        return new(entries, scheme ?? -1, scheme is { } number ? texts.SchemeName(number) : null);
    }

    /// <summary>The hero's page, or null without a hero or the name files.</summary>
    public static AreaSchemesPageView? BuildForHero(GameSessionOwner session, AreaCharacterSheetTexts texts)
    {
        ArgumentNullException.ThrowIfNull(session);
        return session.QueryHero() is { } hero ? Build(texts, hero.Advancement.AutoLevelScheme) : null;
    }

    /// <summary>A press on a row: the hero's scheme becomes <paramref name="scheme"/>.</summary>
    [OriginalFunction(0x55DF90, OriginalFunctionScope.Partial)]
    public static SessionSchemeResult Choose(GameSessionOwner session, int scheme)
    {
        ArgumentNullException.ThrowIfNull(session);
        return session.SetAutoLevelScheme(session.Version, SessionSchemeTarget.Hero, scheme);
    }
}
