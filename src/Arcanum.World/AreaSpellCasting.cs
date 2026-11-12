using Arcanum.Data.Magic;
using Arcanum.Session;
using Arcanum.Simulation.Locks;
using Arcanum.Simulation.Magic;

namespace Arcanum.World;

/// <summary>One spell the hero can cast from the spell pane: its number, name, college and the cost the original's UI shows
/// (<see cref="SpellCostRules.CastCost"/>).</summary>
public sealed record AreaSpellChoice(int Spell, string Name, string College, int? Cost);

/// <summary>ARC-UI-016: one button of the weapon's spell pane: the item's slot, its spell and name, and whether a press activates it
/// (a spell with item triggers shows but does nothing, 0x57C040).</summary>
public sealed record AreaItemSpellChoice(int Slot, int Spell, string Name, bool Activates);

/// <summary>ARC-UI-016: the bar's weapon button and the weapon's spell pane (bar mode 8): the wielded weapon, whether its button shows,
/// whether the pane is open, its buttons and the gauge's text.</summary>
public sealed record AreaWeaponSpellPane(string Item, bool ButtonShown, bool Open, IReadOnlyList<AreaItemSpellChoice> Buttons, string Gauge)
{
    public bool Equals(AreaWeaponSpellPane? other) => other is not null && Item == other.Item && ButtonShown == other.ButtonShown && Open == other.Open
        && Buttons.SequenceEqual(other.Buttons) && Gauge == other.Gauge;

    public override int GetHashCode() => HashCode.Combine(Item, ButtonShown, Open, Buttons.Count, Gauge);
}

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

    /// <summary>ARC-UI-016: the weapon's spell pane from the cast host's answer (<see cref="SessionSpellCaster.ItemPane"/>), open or
    /// not; null without an answer.</summary>
    public static AreaWeaponSpellPane? WeaponPane(SessionItemSpellPane? pane, bool open, SpellData? data)
    {
        if (pane is null) return null;
        var buttons = pane.Buttons.Select(b => new AreaItemSpellChoice(b.Slot, b.Spell, AreaSpellsPage.SpellName(data, b.Spell), b.Activates)).ToArray();
        var gauge = pane.Gauge.Kind switch
        {
            ItemSpellGaugeKind.Unknown => "charges unknown (not identified)",
            ItemSpellGaugeKind.Unlimited => "unlimited charges",
            _ => $"{pane.Gauge.Charges} charges",
        };
        return new(pane.Item, pane.WeaponButton, open, buttons, gauge);
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
            // ARC-DEV-302: the hold flags an ObjFlag component wrote (the original tells none of them; the line is the viewer's).
            // ARC-DEV-304: the spoken-with-dead spell flag (spell 56) is no hold: the corpse can be talked to (or no longer can).
            SpellCastEventKind.ObjectFlagWritten when entry.Value == SessionSpellCaster.SpokenWithDeadSpellFlag => (entry.Detail & 0x1) != 0 ? $"{Who()} can be spoken with" : $"{Who()} can no longer be spoken with",
            // ARC-DEV-358: the other flags (every spell flag, the object flags and their side effects) tell no line; only the holds do.
            SpellCastEventKind.ObjectFlagWritten when HoldName(entry) is { } hold => (entry.Detail & 0x1) != 0 ? $"{Who()} is {hold}" : $"{Who()} is no longer {hold}",
            SpellCastEventKind.ObjectFlagWritten => null,
            SpellCastEventKind.ObjectFlagNotSaved => "the hold was not saved",
            // ARC-DEV-303: the Effect component's add and removal on the hero, the session's refusal, and the entangle breaking on a hit (the original tells none of them; the lines are the viewer's).
            SpellCastEventKind.EffectAdded => $"{Who()} gains effect {entry.Value} x{entry.Detail}",
            SpellCastEventKind.EffectRemoved => $"{Who()} loses effect {entry.Value}",
            SpellCastEventKind.EffectNotSaved => $"effect {entry.Detail} not saved",
            SpellCastEventKind.EntangleBroken => $"{Who()} breaks free of the entangle",
            // ARC-MAGIC-017: eye candy is drawn, not told. ARC-DEV-352: its callbacks (play flags 0x8, 0x10) call the run back; a summon is not told either (the original says nothing), its refusal is the viewer's line.
            SpellCastEventKind.SummonRefused => $"the summoning of prototype {entry.Value} was refused",
            // ARC-MAGIC-032 (MA-7, D5(a)): the Identify answer for the hero as text (the original opens a window, E8's: the sheet, the container's inventory, the item description) and the identified mark of Divine Magick (the original tells none of them; the lines are the viewer's).
            SpellCastEventKind.IdentifyCritter => entry.Value == SpellTargetObject.Pc ? $"identify: {Who()} (the hero's own sheet is the C key)" : $"identify: {Who()} (its character sheet)",
            SpellCastEventKind.IdentifyContainer => $"identify: the contents of {Who()} (its contents window)",
            SpellCastEventKind.IdentifyItem => $"identify: {Who()} (its item description)",
            SpellCastEventKind.ItemIdentified => $"{Who()} is identified",
            SpellCastEventKind.ItemIdentifyNotSaved => "the identification was not saved",
            // ARC-MAGIC-037 (MA-6): a lock flag a spell wrote on a portal or container (the original tells none of them; the lines are the viewer's); the refused unlock tells the "resisted" line of its own.
            SpellCastEventKind.OpenableFlagWritten when entry.Value == LockRules.LockedBit => (entry.Detail & 0x1) != 0 ? $"{Who()} is locked" : $"{Who()} is unlocked",
            SpellCastEventKind.OpenableFlagWritten when entry.Value == LockRules.MagicallyHeldBit => (entry.Detail & 0x1) != 0 ? $"{Who()} is held by magick" : $"{Who()} is no longer held by magick",
            SpellCastEventKind.OpenableFlagNotSaved => "the lock change was not saved",
            // ARC-MAGIC-1146: the Recharge component's new charge count of an item (the original tells none; the lines are the viewer's); Trait64's writes are state, not text.
            SpellCastEventKind.ItemRecharged => $"{Who()} is recharged to {entry.Value} charges",
            SpellCastEventKind.ItemRechargeNotSaved => "the recharge was not saved",
            _ => null,
        };
    }

    // The hold an ObjFlag line names (detail bit 0x2: the critter flags, 0x4: the object flags, 0x8: written by a side effect): paralysis, stone or the entangle; null for any other flag.
    private static string? HoldName(SpellCastEvent entry) => (entry.Detail & (0x4 | SessionSpellCaster.SideEffectDetail)) != 0 ? null
        : (entry.Detail & 0x2) != 0 ? entry.Value == SessionSpellCaster.ParalysedCritterFlag ? "paralysed" : null
        : entry.Value == SessionSpellCaster.StonedSpellFlag ? "stoned" : entry.Value == SessionSpellCaster.EntangledSpellFlag ? "entangled" : null;

    /// <summary>A magic message's text (spell.mes 600..699), or the project's label when the text is missing.</summary>
    public static string Message(SpellTexts? texts, int number) =>
        texts?.Message(number) is { Length: > 0 } text ? text : $"spell message {number}";
}
