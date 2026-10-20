namespace Arcanum.Data.Technology;

/// <summary>Which range a schematic number lies in.</summary>
public enum SchematicKind
{
    /// <summary>Below 2000: no schematic range; the original reads such a number like any other (all lines missing).</summary>
    Other,
    /// <summary>2000..3999: learned with the degrees of a discipline.</summary>
    Learned,
    /// <summary>4000 and up, other than a placeholder: found in a document.</summary>
    Found,
    /// <summary>6000 + 10 × discipline (discipline 0..7): the record the window shows for an empty tab.</summary>
    Placeholder,
}

/// <summary>The seven lines of a schematic record, in their order (offset from the record's number).</summary>
public enum SchematicLine
{
    Name,
    Description,
    Art,
    Component1,
    Component2,
    Product,
    Quantity,
}

/// <summary>
/// One list line of a schematic record (component 1, component 2 or the product): up to three alternative base prototype
/// numbers. The original fills three values: one or two tokens repeat the last; a line without a token leaves the field
/// unwritten (<see cref="Bases"/> empty). The prototype number is the base number + 20.
/// </summary>
/// <param name="Tokens">How many tokens the line held (0..3).</param>
public sealed record SchematicAlternatives(IReadOnlyList<int> Bases, int Tokens)
{
    public const int Count = 3, PrototypeOffset = 20;

    public static SchematicAlternatives Empty { get; } = new(Array.AsReadOnly(Array.Empty<int>()), 0);

    /// <summary>A line without a token: the field is left as it was.</summary>
    public bool IsEmpty => Bases.Count == 0;

    /// <summary>The prototype numbers of <see cref="Bases"/> (base + 20), in order.</summary>
    public IReadOnlyList<int> Prototypes => Array.AsReadOnly(Bases.Select(b => unchecked(b + PrototypeOffset)).ToArray());

    /// <summary>The distinct alternatives in order (a repeated value counts once).</summary>
    public IReadOnlyList<int> DistinctBases => Array.AsReadOnly(Bases.Distinct().ToArray());

    public bool Equals(SchematicAlternatives? other) => other is not null && Tokens == other.Tokens && Bases.SequenceEqual(other.Bases);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Tokens);
        foreach (var b in Bases) hash.Add(b);
        return hash.ToHashCode();
    }
}

/// <summary>
/// ARC-TECHNOLOGY-001: one schematic record of <c>rules\schematic.mes</c>, the seven entries n..n+6 (contract
/// docs/formats/schematics.md). <see cref="NameText"/> and <see cref="DescriptionText"/> are message numbers of
/// <c>mes\schematic_text.mes</c>. A missing entry reads as the original's missing-line text, whose number is 0: the value is 0
/// (a list line three zeros) and <see cref="MissingLines"/> flags it.
/// </summary>
public sealed record SchematicRecord
{
    public const int Lines = 7;

    public required int Number { get; init; }
    public required SchematicKind Kind { get; init; }
    public required int NameText { get; init; }
    public required int DescriptionText { get; init; }
    public required int Art { get; init; }
    public required SchematicAlternatives Component1 { get; init; }
    public required SchematicAlternatives Component2 { get; init; }
    public required SchematicAlternatives Product { get; init; }
    public required int Quantity { get; init; }
    /// <summary>Indexed by <see cref="SchematicLine"/>: true where the entry is not in the file.</summary>
    public required IReadOnlyList<bool> MissingLines { get; init; }

    /// <summary>All seven entries exist.</summary>
    public bool IsComplete => !MissingLines.Contains(true);

    public bool Equals(SchematicRecord? other) =>
        other is not null && Number == other.Number && Kind == other.Kind && NameText == other.NameText && DescriptionText == other.DescriptionText
        && Art == other.Art && Component1.Equals(other.Component1) && Component2.Equals(other.Component2) && Product.Equals(other.Product)
        && Quantity == other.Quantity && MissingLines.SequenceEqual(other.MissingLines);

    public override int GetHashCode() => HashCode.Combine(Number, NameText, DescriptionText, Art, Component1, Component2, Product, Quantity);
}
