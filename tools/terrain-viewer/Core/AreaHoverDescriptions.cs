using System.Globalization;
using Arcanum.Data.Magic;
using Arcanum.Data.Messages;
using Arcanum.Presentation.Hud;
using Arcanum.Session;
using Arcanum.Simulation.Hotkeys;
using Arcanum.World;

namespace Arcanum.TerrainViewer.Core;

/// <summary>What a hover over a hotkey entry or a recent action shows (0x550150, HK19): nothing, an item's description, a skill-bar
/// button's hint or a spell's hint (also an item's spell).</summary>
public enum AreaHotkeyHoverKind { None, Item, Skill, Spell }

/// <summary>The hover's target: the kind, the item (an item entry) and the index (the button or the spell).</summary>
public readonly record struct AreaHotkeyHover(AreaHotkeyHoverKind Kind, string? Item = null, int Index = -1);

/// <summary>
/// ARC-UI-035: the hover descriptions of the hotkey bar's entries and recent actions (checked in the 1.0.7.4 executable, ledger
/// HK19–HK23), Godot-free. The pointer entering an entry dispatches on its type (0x550150): an entry being dragged or empty shows
/// nothing; an item's description goes to the item window (0x57CCF0 with the local PC, <see cref="ItemWindow"/>); a skill-bar button's
/// hint is a type-7 entry with the button's skill (0x5508C0, <see cref="SkillHint"/>); a spell's or an item's spell's hint a type-8
/// entry with the spell and the local PC (0x5507E0, <see cref="SpellHint"/>). In a pane mode only the name is written into the pane's
/// text area (<see cref="PaneName"/>).
/// </summary>
public static class AreaHoverDescriptions
{
    /// <summary>The dispatch of 0x550150 for <paramref name="entry"/>; <paramref name="dragged"/> is the entry's flag 1 (a drag holds it).</summary>
    public static AreaHotkeyHover Dispatch(HotkeyEntry entry, bool dragged)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (dragged) return default;
        return entry.Kind switch
        {
            HotkeyKind.Item when entry.Item is { } item => new(AreaHotkeyHoverKind.Item, item),
            HotkeyKind.Skill => new(AreaHotkeyHoverKind.Skill, null, entry.Index),
            HotkeyKind.Spell or HotkeyKind.ItemSpell => new(AreaHotkeyHoverKind.Spell, null, entry.Index),
            _ => default,
        };
    }

    /// <summary>A skill-bar button's hint (0x5529C0 case 7): the name and description of the button's skill (0x57A6A0) from
    /// <c>skill.mes</c>, else the button's project name; no number line (the hover posts 0).</summary>
    public static AreaMessageEntry SkillHint(SkillTexts? texts, AreaGameBarTexts labels, int button)
    {
        ArgumentNullException.ThrowIfNull(labels);
        string? name = null, description = null;
        if (texts is not null && HotkeyBarRules.ButtonSkill(button) is { } skill)
        {
            var tech = skill >= SkillTexts.BasicSkills;
            var index = tech ? skill - SkillTexts.BasicSkills : skill;
            name = Text(tech ? texts.TechName(index) : texts.BasicName(index));
            description = Text(tech ? texts.TechDescription(index) : texts.BasicDescription(index));
        }
        return new(AreaMessageKind.SkillHint, name ?? labels.SkillButtonName(button), Detail: description ?? "");
    }

    /// <summary>A spell's hint (0x5529C0 case 8, SG2): the college's icon, the name, the minimum willpower ("%s: %d" with intgame.mes 73),
    /// right of it the cast cost as the hero pays it with the maintenance ("%s: %d  (%d / %s)" with 74 for a period of one second,
    /// "%s: %d  (%d / %d %s)" with 75 for a longer one, "%s: %d" without), then the description. <paramref name="row"/> is the spell's
    /// row of the spells page (null without a hero: no willpower or cost line; the original reads the willpower from the spell's data,
    /// but the bar's hovers need a hero, so the case does not arise).</summary>
    public static AreaMessageEntry SpellHint(int spell, AreaSpellRow? row, SpellData? data, AreaGameBarTexts labels)
    {
        ArgumentNullException.ThrowIfNull(labels);
        var culture = CultureInfo.InvariantCulture;
        var willpower = row is null ? "" : string.Format(culture, "{0}: {1}", labels.Text(AreaGameBarTexts.WillpowerLabel), row.MinimumWillpower);
        var cost = row?.Cost is not { } value ? ""
            : row.MaintainCost is { } maintain && row.MaintainPeriod is 1
                ? string.Format(culture, "{0}: {1}  ({2} / {3})", labels.Text(AreaGameBarTexts.CostLabel), value, maintain, labels.Text(AreaGameBarTexts.SecondLabel))
            : row.MaintainCost is { } longer && row.MaintainPeriod is > 1 and var period
                ? string.Format(culture, "{0}: {1}  ({2} / {3} {4})", labels.Text(AreaGameBarTexts.CostLabel), value, longer, period, labels.Text(AreaGameBarTexts.SecondsLabel))
            : string.Format(culture, "{0}: {1}", labels.Text(AreaGameBarTexts.CostLabel), value);
        return new(AreaMessageKind.SpellHint, row?.Name ?? AreaSpellsPage.SpellName(data, spell), willpower,
            row?.Description ?? Text(data?.Texts.Description(spell)) ?? "")
        {
            Icon = HotkeyBarRules.CollegeIcon(spell / AreaSpellsPage.Ranks), Value = cost,
        };
    }

    /// <summary>The spells page row of <paramref name="spell"/> 0..79 for the hero (null without a hero or for another spell).</summary>
    public static AreaSpellRow? SpellRow(GameSessionOwner session, SpellData? data, int spell) =>
        spell is >= 0 and < AreaSpellsPage.Colleges * AreaSpellsPage.Ranks
            ? AreaSpellsPage.Build(session, data, spell / AreaSpellsPage.Ranks)?.Spells[spell % AreaSpellsPage.Ranks] : null;

    /// <summary>The item window's first part (0x554F10, HK22): the item's type icon (0x554C20) and its name (0x441B60) in the font of its
    /// kind: orange for an identified hexed item, blue for another magickal one (complexity above 0), white otherwise.</summary>
    public static AreaMessageEntry ItemWindow(AreaItemDescription item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return new(AreaMessageKind.Item, item.Name) { Icon = item.TypeIcon, TitleColour = item.Colour };
    }

    /// <summary>The name a hover writes into a pane's text area (0x5509C0): a skill-bar button's name (0x57A700), a spell's name
    /// (0x4B1600); an item's is its description name (0x553BE0 → 0x550770).</summary>
    public static string PaneName(AreaHotkeyHover hover, AreaGameBarTexts labels, SpellData? data, Func<string, string> itemName)
    {
        ArgumentNullException.ThrowIfNull(labels);
        ArgumentNullException.ThrowIfNull(itemName);
        return hover.Kind switch
        {
            AreaHotkeyHoverKind.Skill => labels.SkillButtonName(hover.Index),
            AreaHotkeyHoverKind.Spell => AreaSpellsPage.SpellName(data, hover.Index),
            AreaHotkeyHoverKind.Item when hover.Item is { } item => itemName(item),
            _ => "",
        };
    }

    private static string? Text(MesMessage? message) =>
        message is not null && MesText.Decode(message, AreaHoverIndex.TextProfile) is { Status: MesDecodeStatus.Decoded, Text: { Length: > 0 } text } ? text : null;

    private static string? Text(string? text) => string.IsNullOrEmpty(text) ? null : text;
}

/// <summary>An item as the item window names it (0x554F10's first part, HK22): its type icon (null when its fields do not read), its
/// name and the name's colour.</summary>
public sealed record AreaItemDescription(int? TypeIcon, string Name, AreaMessageColour Colour);
