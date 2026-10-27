using Arcanum.Data.Magic;
using Arcanum.Session;
using Arcanum.Simulation.Magic;

namespace Arcanum.TerrainViewer.Core;

/// <summary>One spell the hero can cast from the spell pane: its number, name, college and the cost the original's UI shows
/// (<see cref="SpellCostRules.CastCost"/>).</summary>
public sealed record AreaSpellChoice(int Spell, string Name, string College, int? Cost);

/// <summary>
/// ARC-MAGIC-015 (magic plan MT20a-2): the AREA viewer's casting, Godot-free. The spell pane lists the hero's known spells (college
/// by college, rank by rank: the spell book's ranks); the records are the data reader's in the rules' types
/// (<see cref="SessionSpellRecords"/>); the cast host's log turns into the feedback lines the viewer shows (spell.mes messages by
/// their number, the fatigue paid, damage, healing, resistance and the component types that are not ready).
/// </summary>
public static class AreaSpellCasting
{
    /// <summary>The hero's known spells in college and rank order, or none without a hero or a spell book.</summary>
    public static IReadOnlyList<AreaSpellChoice> Known(GameSessionOwner session, SpellData? data)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (session.QueryHero() is not { } hero || session.QueryHeroSpellBook() is not { } book) return [];
        var caster = new SpellCaster(true, (int)hero.Race, book.MasteredCollege);
        var known = new List<AreaSpellChoice>();
        for (var college = 0; college < SpellBookRules.Colleges; college++)
            for (var rank = 0; rank < book.Ranks[college]; rank++)
            {
                var spell = college * SpellBookRules.Ranks + rank;
                known.Add(new(spell, AreaSpellsPage.SpellName(data, spell), AreaSpellsPage.CollegeName(data, college),
                    data is null ? null : SpellCostRules.CastCost(data.Records[spell].Cost, spell, caster)));
            }
        return known;
    }

    /// <summary>All records in the rules' types, by number (0..222).</summary>
    public static SessionSpellRecord[] Records(SpellData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        return Enumerable.Range(0, SpellRecord.Count).Select(n => SessionSpellRecords.Translate(data.Records[n])).ToArray();
    }

    /// <summary>The feedback line of one logged cast event, or null for an event the viewer does not show (a freed run).</summary>
    /// <param name="name">The shown name of a map object id (the hero, an NPC).</param>
    public static string? Line(SpellCastEvent entry, SpellTexts? texts, Func<string, string> name)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(name);
        string Who() => entry.Object is { } id ? name(id) : "the target";
        return entry.Kind switch
        {
            SpellCastEventKind.Message => Message(texts, entry.Value),
            SpellCastEventKind.FatiguePaid => $"fatigue -{entry.Value}",
            SpellCastEventKind.Damaged => $"{Who()} takes {entry.Value} damage",
            SpellCastEventKind.Healed => $"{Who()} regains {entry.Value} hit points",
            SpellCastEventKind.Resisted => $"{Who()} resists",
            SpellCastEventKind.ComponentNotReady => $"{(SpellComponentType)entry.Value} effect not ready",
            _ => null,
        };
    }

    /// <summary>A magic message's text (spell.mes 600..699), or the project's label when the text is missing.</summary>
    public static string Message(SpellTexts? texts, int number) =>
        texts?.Message(number) is { Length: > 0 } text ? text : $"spell message {number}";
}
