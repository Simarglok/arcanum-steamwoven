using Arcanum.Data.Combat;
using Arcanum.Data.Content;
using Arcanum.Session;

namespace Arcanum.World;

/// <summary>ARC-WORLD-081: what the viewer knows of an item prototype a script creates: its session kind, its ground art and its weight
/// facts, from the prototype's own fields.</summary>
public sealed record AreaPrototypeItem(SessionItemKind Kind, uint Art, SessionItemWeight Weight)
{
    /// <summary>ARC-DEV-097 (MIG8, SA4): the elements of the prototype's script array, which an object created from it reads until its first write.</summary>
    public IReadOnlyList<SessionPrototypeScript> Scripts { get; init; } = [];

    /// <summary>ARC-WORLD-1283: the prototype's no-drop item flag (item flag 0x20, field 0x57), which the remove check (0x466DA0) of a transfer reads on an object made from it.</summary>
    public bool NoDrop { get; init; }

    /// <summary>The no-drop item flag (0x20) of the item flags.</summary>
    public const uint NoDropFlag = 0x20;
}

/// <summary>
/// ARC-WORLD-081: the item prototypes scripts create (the plant generator's herbs, any create object of a use, tile or sector script), read
/// from the game's content stack by their prototype file numbers (the script's number + <see cref="PrototypeOffset"/>, ledger PG8) when first
/// asked and kept. A prototype that does not read or is no item the session knows is none. Owns the stack it is given; caller-serialized.
/// </summary>
public sealed class AreaPrototypeItems : IDisposable
{
    /// <summary>PG8: a script names a prototype by its basic number; the executable adds 20 before the lookup (the file numbering).</summary>
    public const int PrototypeOffset = 20;

    /// <summary>The prototypes kept at most (each read once); the installed plant generator names 8.</summary>
    public const int MaxPrototypes = 1024;

    private readonly ContentStack _stack;
    private readonly ContentPrototypes _prototypes;
    private readonly Dictionary<int, AreaPrototypeItem?> _items = [];
    private readonly Dictionary<int, CombatFields?> _fields = [];
    private bool _disposed;

    /// <param name="stack">The game's content stack; this object owns it and disposes it.</param>
    public AreaPrototypeItems(ContentStack stack)
    {
        _stack = stack ?? throw new ArgumentNullException(nameof(stack));
        _prototypes = ContentPrototypes.Open(stack);
    }

    /// <summary>ARC-DEV-305: the combat fields of prototype file <paramref name="prototype"/> (the facts a made item has until something writes
    /// them), read once and kept; null when it does not read or the bound of <see cref="MaxPrototypes"/> is reached.</summary>
    public CombatFields? Fields(int prototype)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_fields.TryGetValue(prototype, out var known)) return known;
        if (_fields.Count >= MaxPrototypes) return null;
        var fields = CombatFieldReader.ReadPrototype(_prototypes.Read, prototype).Fields;
        _fields[prototype] = fields;
        return fields;
    }

    /// <summary>The item of prototype file <paramref name="prototype"/>, or null when it does not read, is no item type the session knows,
    /// or the bound is reached.</summary>
    public AreaPrototypeItem? Item(int prototype)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_items.TryGetValue(prototype, out var known)) return known;
        if (_items.Count >= MaxPrototypes) return null;
        AreaPrototypeItem? item = null;
        if (Fields(prototype) is { Item: { } facts } fields && Enum.IsDefined((SessionItemType)fields.Object.Type))
            item = new(new((SessionItemType)fields.Object.Type, prototype, fields.Object.Name, fields.Object.Description), fields.Object.CurrentArt,
                new(facts.Weight, facts.MagicWeight, facts.MagickTechComplexity))
            {
                Scripts = Array.AsReadOnly(fields.Object.ScriptAttachments.Select(a => new SessionPrototypeScript(a.Point, a.Script, a.LocalFlags, a.Counters)).ToArray()),
                NoDrop = (facts.Flags & AreaPrototypeItem.NoDropFlag) != 0,
            };
        _items[prototype] = item;
        return item;
    }

    /// <summary>The item a script's prototype number names (<see cref="PrototypeOffset"/> added), or null.</summary>
    public AreaPrototypeItem? ScriptItem(int number) => number is >= 0 and <= int.MaxValue - PrototypeOffset ? Item(number + PrototypeOffset) : null;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _stack.Dispose();
    }
}
