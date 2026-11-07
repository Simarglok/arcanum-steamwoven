using System.Text;
using Arcanum.Data.Combat;
using Arcanum.Data.Content;
using Arcanum.Data.Messages;
using Arcanum.Data.Technology;
using Arcanum.Session;
using Arcanum.Simulation.Technology;

namespace Arcanum.World;

/// <summary>
/// The schematic data of the module the area was read from (ARC-TECHNOLOGY-005): the catalogue (TECH-1), the tech texts, and
/// the facts of every prototype the records name (complexity, discipline, the saved item kind), read once in the background.
/// </summary>
public sealed class AreaSchematicData
{
    public AreaSchematicData(SchematicCatalog catalog, TechTexts texts, IReadOnlyDictionary<int, (ComponentFacts Facts, SessionItemKind Kind)> prototypes,
        IReadOnlyDictionary<int, (int AmmoType, int Units)>? ammo = null, IReadOnlyDictionary<int, uint>? art = null,
        IReadOnlyDictionary<int, SessionItemWeight>? weights = null)
    {
        Weights = weights ?? new Dictionary<int, SessionItemWeight>();
        Ammo = ammo ?? new Dictionary<int, (int, int)>();
        Art = art ?? new Dictionary<int, uint>();
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

    /// <summary>ARC-WORLD-070: a schematic prototype's ground ART id (its current art), for a made product lying on the ground.</summary>
    public IReadOnlyDictionary<int, uint> Art { get; }

    public uint? ArtOf(int prototype) => Art.TryGetValue(prototype, out var a) ? a : null;

    /// <summary>ARC-WORLD-085: a prototype's weight facts (weight, magic weight adjustment, complexity), which the session gives the
    /// objects it creates from it (<see cref="GameSessionOwner.AttachItemWeights"/>).</summary>
    public IReadOnlyDictionary<int, SessionItemWeight> Weights { get; }

    public SessionItemWeight? WeightOf(int prototype) => Weights.TryGetValue(prototype, out var w) ? w : null;

    public ComponentFacts? Facts(int prototype) => Prototypes.TryGetValue(prototype, out var p) ? p.Facts : null;
    public SessionItemKind? Kind(int prototype) => Prototypes.TryGetValue(prototype, out var p) ? p.Kind : null;
    public int? Discipline(int prototype) => Prototypes.TryGetValue(prototype, out var p) ? p.Facts.Discipline : null;
}
