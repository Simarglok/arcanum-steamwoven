using System.Text;
using Arcanum.Data.Combat;
using Arcanum.Data.Content;
using Arcanum.Data.Messages;
using Arcanum.Data.Technology;
using Arcanum.Session;
using Arcanum.Simulation.Technology;

namespace Arcanum.TerrainViewer.Core;

/// <summary>The two tabs of the schematic window, in the original's order: learned (with the degrees) and found (documents).</summary>
public enum AreaSchematicTab { Learned, Found }

/// <summary>Why the window does not open: no hero (a hero crafter), the crafter or the hero is dead, or (ARC-TECHNOLOGY-006) the NPC
/// crafter's data are invalid or the session does not know the NPC.</summary>
public enum AreaSchematicRefusal { None, NoHero, Dead, NoCrafter }

/// <summary>One component or product slot: the prototype shown, its description number (the viewer names it), whether it was
/// found and whether the crafter's expertise reaches it (components only), and its complexity and discipline.</summary>
public sealed record AreaSchematicSlot(int Prototype, int Description, bool Found, bool ExpertiseOk, int Complexity, int Discipline);

/// <summary>
/// One page of the schematic window: the tab, the discipline (0..7), the page and the pages of that tab and discipline, the
/// record shown (a placeholder for an empty tab), its name and description, the two components, the product and quantity, the
/// button state (TECH-4) and the texts it shows, the crafter's expertise in the discipline, a note on what is missing, and
/// (ARC-TECHNOLOGY-006) the NPC crafter's entity, null for the hero.
/// </summary>
public sealed record AreaSchematicsView(AreaSchematicTab Tab, int Discipline, string DisciplineName, int Page, int Pages, int Record,
    bool Placeholder, string Name, string Description, AreaSchematicSlot? Component1, AreaSchematicSlot? Component2, AreaSchematicSlot? Product,
    int Quantity, AssemblyButton Button, string? ButtonText, string HoverText, int Expertise, string? Note, string? Crafter = null);

public sealed record AreaSchematicsBuild(AreaSchematicRefusal Refusal, AreaSchematicsView? View);

public enum AreaSchematicDataStatus { Ready, ModuleOutsideGameRoot, MountFailed, StackFailed, CatalogueFailed, TextsFailed }

/// <summary>
/// The schematic data of the module the area was read from (ARC-TECHNOLOGY-005): the catalogue (TECH-1), the tech texts, and
/// the facts of every prototype the records name (complexity, discipline, the saved item kind), read once in the background.
/// </summary>
public sealed class AreaSchematicData
{
    public AreaSchematicData(SchematicCatalog catalog, TechTexts texts, IReadOnlyDictionary<int, (ComponentFacts Facts, SessionItemKind Kind)> prototypes,
        IReadOnlyDictionary<int, (int AmmoType, int Units)>? ammo = null)
    {
        Ammo = ammo ?? new Dictionary<int, (int, int)>();
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(texts);
        ArgumentNullException.ThrowIfNull(prototypes);
        Catalog = catalog;
        Texts = texts;
        Prototypes = prototypes;
    }

    public SchematicCatalog Catalog { get; }
    public TechTexts Texts { get; }
    /// <summary>Prototype number (base + 20) → its item facts.</summary>
    public IReadOnlyDictionary<int, (ComponentFacts Facts, SessionItemKind Kind)> Prototypes { get; }

    /// <summary>ARC-WORLD-066: an ammunition prototype's type and units (its quantity), for an ammunition product.</summary>
    public IReadOnlyDictionary<int, (int AmmoType, int Units)> Ammo { get; }

    public (int AmmoType, int Units)? AmmoOf(int prototype) => Ammo.TryGetValue(prototype, out var a) ? a : null;

    public ComponentFacts? Facts(int prototype) => Prototypes.TryGetValue(prototype, out var p) ? p.Facts : null;
    public SessionItemKind? Kind(int prototype) => Prototypes.TryGetValue(prototype, out var p) ? p.Kind : null;
    public int? Discipline(int prototype) => Prototypes.TryGetValue(prototype, out var p) ? p.Facts.Discipline : null;
}

public sealed record AreaSchematicDataLoad(AreaSchematicDataStatus Status, AreaSchematicData? Data, string Detail);

/// <summary>
/// ARC-TECHNOLOGY-005: the schematic window of a crafter, Godot-free. Every value comes from a session query
/// (<see cref="GameSessionOwner.QueryHeroTechnology"/>, <see cref="GameSessionOwner.QueryHeroFoundSchematics"/>,
/// <see cref="GameSessionOwner.QueryHeroAssembly"/>) or the player's own data (<see cref="AreaSchematicData"/>); the assemble
/// button calls <see cref="GameSessionOwner.AssembleSchematic"/>. The window's behaviour was read in the executable (ledger
/// "Schematic window"): the learned tab per discipline shows the first "degree" records, the found tab the found list by
/// discipline (a PC crafter only), an empty tab shows the discipline's placeholder record, the page wraps within the tab's
/// count. Original UI text (tab and button labels) is not used; the labels are the project's; the record texts are the data's.
/// </summary>
public static class AreaSchematics
{
    public const int Disciplines = 8;

    /// <summary>Loads the schematic data through the game folder's content stack (<see cref="AreaContentRoot.Discover"/>).</summary>
    public static AreaSchematicDataLoad LoadData(string modulePath)
    {
        var found = AreaContentRoot.Discover(modulePath);
        if (found.Status != AreaContentRootStatus.Ready)
            return new(found.Status == AreaContentRootStatus.MountFailed ? AreaSchematicDataStatus.MountFailed : AreaSchematicDataStatus.ModuleOutsideGameRoot, null, found.Detail);
        var opened = ContentStack.Open(found.Plan!);
        if (opened.Status != ContentStackStatus.Ready) return new(AreaSchematicDataStatus.StackFailed, null, opened.Status.ToString());
        using var stack = opened.Stack!;
        var catalog = SchematicCatalog.Open(stack);
        if (catalog.Catalog is null) return new(AreaSchematicDataStatus.CatalogueFailed, null, $"{catalog.Refusal}");
        var texts = TechTexts.Open(stack);
        if (texts.Texts is null) return new(AreaSchematicDataStatus.TextsFailed, null, $"{texts.Refusal}");
        var prototypes = ContentPrototypes.Open(stack);
        var facts = new Dictionary<int, (ComponentFacts, SessionItemKind)>();
        var ammo = new Dictionary<int, (int, int)>();
        foreach (var record in catalog.Catalog.Records)
            foreach (var base_ in record.Component1.Bases.Concat(record.Component2.Bases).Concat(record.Product.Bases).Distinct())
            {
                var prototype = base_ + SchematicAlternatives.PrototypeOffset;
                if (facts.ContainsKey(prototype) || CombatFieldReader.ReadPrototype(prototypes.Read, prototype).Fields is not { Item: { } item } fields) continue;
                if (!Enum.IsDefined((SessionItemType)fields.Object.Type)) continue;
                facts[prototype] = (new(item.MagickTechComplexity, item.Discipline),
                    new((SessionItemType)fields.Object.Type, prototype, fields.Object.Name, fields.Object.Description));
                if (fields.Ammo is { } a) ammo[prototype] = (a.AmmoType, a.Quantity);
            }
        return new(AreaSchematicDataStatus.Ready, new(catalog.Catalog, texts.Texts, facts, ammo),
            $"records {catalog.Catalog.Records.Count}, prototypes {facts.Count}");
    }

    /// <summary>Whether the window opens for this session: a hero that is alive; for an NPC crafter (ARC-TECHNOLOGY-006) an NPC the
    /// session knows with valid data, alive, and a viewer (the hero, when there is one) alive, as the original's open refuses a dead
    /// crafter or viewer (ledger SW3).</summary>
    public static AreaSchematicRefusal Refusal(GameSessionOwner session, SessionNpcCrafter? crafter = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (crafter is not null)
        {
            if (session.QueryNpcTechnology(crafter) is null || !session.QueryNpcKnown(crafter.Npc)) return AreaSchematicRefusal.NoCrafter;
            if (session.QueryNpcDead(crafter.Npc) || session.QueryHeroLife() is { Status.IsDead: true }) return AreaSchematicRefusal.Dead;
            return AreaSchematicRefusal.None;
        }
        if (session.QueryHero() is null) return AreaSchematicRefusal.NoHero;
        return session.QueryHeroLife() is { Status.IsDead: true } ? AreaSchematicRefusal.Dead : AreaSchematicRefusal.None;
    }

    /// <summary>The records of a tab and discipline for the crafter: the learned records up to its stored degree, or the found
    /// records of the discipline, in the window's order. An NPC crafter's found tab is empty (the original builds it for a PC crafter
    /// only, ledger SK4, NC5).</summary>
    public static IReadOnlyList<int> Records(GameSessionOwner session, AreaSchematicData data, AreaSchematicTab tab, int discipline,
        SessionNpcCrafter? crafter = null)
    {
        var technology = crafter is null ? session.QueryHeroTechnology() : session.QueryNpcTechnology(crafter);
        var degrees = technology is { } known ? known.Select(d => d.Degree).ToArray() : new int[Disciplines];
        var found = crafter is null && session.QueryHeroFoundSchematics() is { } list ? list.ToArray() : [];
        var tabs = data.Catalog.Tabs(degrees, found, crafterIsPc: crafter is null, data.Discipline);
        return (tab == AreaSchematicTab.Learned ? tabs.Learned : tabs.Found)[discipline];
    }

    /// <summary>The page of the window, or the refusal. The page wraps into the tab's count; an empty tab shows the placeholder.</summary>
    public static AreaSchematicsBuild Build(GameSessionOwner session, AreaSchematicData? data, AreaSchematicTab tab, int discipline, int page,
        SessionCraftLookup items, MesTextProfile profile, SessionNpcCrafter? crafter = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(items);
        if (discipline is < 0 or >= Disciplines) throw new ArgumentOutOfRangeException(nameof(discipline));
        if (Refusal(session, crafter) is var refusal and not AreaSchematicRefusal.None) return new(refusal, null);
        var technology = crafter is null ? session.QueryHeroTechnology()!.Value : session.QueryNpcTechnology(crafter)!.Value;
        var who = crafter?.Npc;
        if (data is null)
            return new(AreaSchematicRefusal.None, new(tab, discipline, $"Discipline {discipline}", 0, 0, 0, true, "", "", null, null, null, 0,
                AssemblyButton.ItemMissing, null, "", technology[discipline].Expertise, "schematic data not loaded", who));
        var records = Records(session, data, tab, discipline, crafter);
        var pages = records.Count;
        var shown = pages == 0 ? 0 : ((page % pages) + pages) % pages;
        var number = pages == 0 ? SchematicCatalog.PlaceholderNumber(discipline) : records[shown];
        var record = data.Catalog.Read(number).Record;
        var name = Text(data.Catalog.Text(record?.NameText ?? -1), profile);
        var description = Text(data.Catalog.Text(record?.DescriptionText ?? -1), profile);
        var disciplineName = Text(data.Texts.DisciplineNames[discipline], profile) is { Length: > 0 } n ? n : $"Discipline {discipline}";
        if (record is null || pages == 0)
            return new(AreaSchematicRefusal.None, new(tab, discipline, disciplineName, shown, pages, number, true, name, description, null, null, null, 0,
                AssemblyButton.ItemMissing, null, Hover(data), technology[discipline].Expertise, pages == 0 ? "no schematic in this tab" : "record unreadable", who));
        // An NPC the session does not know (no attached seed, party record or added items) has no list to search.
        if ((crafter is null ? session.QueryHeroAssembly(record, items, data.Facts) : session.QueryNpcAssembly(crafter, record, items, data.Facts)) is not { } plan)
            return new(AreaSchematicRefusal.NoCrafter, null);
        var product = plan.ProductBase is { } p ? p + SchematicAlternatives.PrototypeOffset
            : record.Product.Bases.Count > 0 ? record.Product.Bases[0] + SchematicAlternatives.PrototypeOffset : 0;
        return new(AreaSchematicRefusal.None, new(tab, discipline, disciplineName, shown, pages, number, false, name, description,
            Slot(plan.First, data), Slot(plan.Second, data), Slot(new(true, product, 0, null, true), data), record.Quantity, plan.Button,
            SchematicAssemblyRules.RefusalMessage(plan.Button) is { } message ? Text(data.Catalog.Text(message), profile) : null, Hover(data),
            technology[discipline].Expertise, null, who));

        string Hover(AreaSchematicData d) => Text(d.Catalog.Text(SchematicAssemblyRules.AssembleHoverMessage), profile);
    }

    /// <summary>The project's label of a tab.</summary>
    public static string TabName(AreaSchematicTab tab) => tab == AreaSchematicTab.Learned ? "Learned" : "Found";

    /// <summary>An item's shown name: the text of its description number in the description catalog, or a bracketed label.</summary>
    public static string Name(int description, ObjectDescriptionCatalog? catalog)
    {
        if (catalog is null) return $"[item {description}: name unavailable]";
        var found = catalog.Find(description);
        if (found.Status != DescriptionLookupStatus.Found || found.Message is null) return $"[item {description}: unnamed]";
        return MesText.Decode(found.Message, AreaHoverIndex.TextProfile) is { Status: MesDecodeStatus.Decoded, Text: { Length: > 0 } decoded }
            ? decoded : $"[item {description}: name not decodable]";
    }

    /// <summary>The completion text (<c>schematic_text.mes</c> 2) the original shows after an assembly.</summary>
    public static string CompletedText(AreaSchematicData data, MesTextProfile profile) =>
        Text(data.Catalog.Text(SchematicAssemblyRules.CompletedMessage), profile);

    /// <summary>The sound the original plays after an assembly: 3018 + the current discipline (ledger SW6).</summary>
    public static int CompletedSound(int discipline) => SchematicAssemblyRules.CompletedSoundBase + discipline;

    /// <summary>The text of a session refusal of an assembly for the status line.</summary>
    public static string Refused(SessionAssemblyResult result, bool npc = false) => result.Failure switch
    {
        GameSessionFailure.AssemblyRefused => result.Plan?.Button == AssemblyButton.ExpertiseLacking ? "expertise lacking" : "an item is missing",
        GameSessionFailure.InsertRefused => "no room for the product (the original would leave it on the ground)",
        GameSessionFailure.ItemTypeUnsupported => "the product is not an item the session holds (stack or key)",
        GameSessionFailure.BudgetExceeded => "inventory limit",
        GameSessionFailure.InvalidLifeTarget => npc ? "the NPC crafter is dead" : "the hero is dead",
        GameSessionFailure.UnknownHolder => "the session does not know the NPC crafter",
        GameSessionFailure.InvalidAdvancement => "the NPC crafter's data are invalid",
        GameSessionFailure.StaleVersion => "the session changed",
        _ => result.Failure.ToString(),
    };

    private static AreaSchematicSlot Slot(ComponentGate gate, AreaSchematicData data)
    {
        var facts = data.Facts(gate.Prototype);
        return new(gate.Prototype, data.Kind(gate.Prototype)?.Description ?? 0, gate.Found, gate.ExpertiseOk, facts?.Complexity ?? 0, facts?.Discipline ?? -1);
    }

    private static string Text(string? latin1, MesTextProfile profile)
    {
        if (latin1 is null) return "";
        var decoded = MesText.Decode(Encoding.Latin1.GetBytes(latin1), profile);
        return decoded is { Status: MesDecodeStatus.Decoded, Text: { } text } ? text : "";
    }
}
