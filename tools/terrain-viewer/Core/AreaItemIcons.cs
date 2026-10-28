using Arcanum.Data;
using Arcanum.Data.Area;
using Arcanum.Data.Audio;
using Arcanum.Data.Combat;
using Arcanum.Data.Content;
using Arcanum.Data.Messages;
using Arcanum.Presentation.Hud;
using Arcanum.Session;

namespace Arcanum.TerrainViewer.Core;

public enum AreaItemIconStatus { InventoryArt, TypeIcon, NoIcon }

/// <summary>What an item entry of the hotkey bar shows (ARC-UI-032): its inventory art's frame when the art fits a slot, else the
/// interface art number of its type icon (0x554C20); <see cref="AreaItemIconStatus.NoIcon"/> when neither is known.</summary>
public sealed record AreaItemIcon(AreaItemIconStatus Status, AreaInterfaceFrame? Art, int? TypeIcon, string Detail);

/// <summary>Explicit limits of the inventory art one viewer run reads from one module: distinct art ids, one file's size and the pixels
/// of all frames kept. An art id past them is refused and kept refused.</summary>
public sealed record AreaItemIconLimits(int MaxArts = 1024, int MaxFileBytes = 4 * 1048576, long MaxRgbaBytes = 16L * 1048576, int MaxPrototypes = 1024);

/// <summary>
/// ARC-UI-032: the icons of the hotkey bar's item entries (ledger HK12, HK17, HK18). An item's stored fields are its object row's when
/// a map of this run holds it (instance over prototype), else its kind's prototype's. Its inventory art (field 0x5D) names its file
/// through <c>art\item\item_inven.mes</c> (<see cref="AreaObjectArt.ResolveItemInventory"/>, 0x4EC290); the frame (the only one every
/// installed inventory art has) is shown when both sides are at most 32, otherwise the type icon (<see cref="HotkeyBarRules.ItemTypeIcon"/>),
/// also when the art does not read. Identified is the original's test (0x464370): complexity 0 or less, else the stored item flag 0x1
/// or the session's identification mark. Each art id and prototype is read once; owns the stack it is given; caller-serialized.
/// </summary>
public sealed class AreaItemIcons : IDisposable
{
    private const uint IdentifiedFlag = 0x1, HexedFlag = 0x40;

    private readonly ContentStack _stack;
    private readonly ContentPrototypes _prototypes;
    private readonly AreaItemIconLimits _limits;
    private readonly Dictionary<int, CombatFields?> _prototypeFields = [];
    private readonly Dictionary<int, int?> _unknownDescriptions = [];
    private readonly Dictionary<uint, (AreaInterfaceFrame? Frame, string Detail)> _arts = [];
    private IReadOnlyDictionary<int, string>? _table;
    private bool _tableRead, _disposed;
    private long _rgbaBytes;

    /// <param name="stack">The game's content stack; this object owns it and disposes it.</param>
    public AreaItemIcons(ContentStack stack, AreaItemIconLimits? limits = null)
    {
        _stack = stack ?? throw new ArgumentNullException(nameof(stack));
        _limits = limits ?? new();
        if (_limits.MaxArts <= 0 || _limits.MaxFileBytes <= 0 || _limits.MaxRgbaBytes <= 0 || _limits.MaxPrototypes <= 0) throw new ArgumentOutOfRangeException(nameof(limits));
        _prototypes = ContentPrototypes.Open(stack);
    }

    /// <summary>Opens the content stack of the game folder of <paramref name="modulePath"/>; null (with the reason) when it does not open.</summary>
    public static AreaItemIcons? Open(string modulePath, out string detail)
    {
        ArgumentNullException.ThrowIfNull(modulePath);
        var found = AreaContentRoot.Discover(modulePath);
        if (found.Status != AreaContentRootStatus.Ready) { detail = $"not read ({found.Status} {found.Detail})"; return null; }
        var opened = ContentStack.Open(found.Plan!);
        if (opened.Status != ContentStackStatus.Ready) { detail = $"not read (content stack {opened.Status})"; return null; }
        detail = "content stack open";
        try { return new(opened.Stack!); }
        catch { opened.Stack!.Dispose(); throw; }
    }

    /// <summary>The icon of an item of <paramref name="kind"/>: from <paramref name="rowFields"/> (its object row's, when a map holds it),
    /// else its prototype's; <paramref name="identifiedMark"/> is the session's identification mark.</summary>
    public AreaItemIcon Icon(SessionItemKind kind, CombatFields? rowFields, bool identifiedMark)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if ((rowFields ?? Prototype(kind.Prototype)) is not { Item: { } item } fields) return new(AreaItemIconStatus.NoIcon, null, null, $"prototype {kind.Prototype}: no item fields");
        var (frame, detail) = InventoryArt(item.InventoryArt);
        if (frame is not null && HotkeyBarRules.ItemShowsInventoryArt(frame.Width, frame.Height)) return new(AreaItemIconStatus.InventoryArt, frame, null, detail);
        return HotkeyBarRules.ItemTypeIcon(Facts(fields, identifiedMark)) is { } icon
            ? new(AreaItemIconStatus.TypeIcon, null, icon, $"type icon {icon} ({detail})")
            : new(AreaItemIconStatus.NoIcon, null, null, $"no type icon ({detail})");
    }

    /// <summary>ARC-UI-051: the frame of an item's inventory art (field 0x5D) at any size, as the inventory window draws it on the grid and
    /// the doll (IW25); null with the reason when the fields or the art do not read.</summary>
    public (AreaInterfaceFrame? Frame, string Detail) InventoryFrame(SessionItemKind kind, CombatFields? rowFields)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if ((rowFields ?? Prototype(kind.Prototype)) is not { Item: { } item }) return (null, $"prototype {kind.Prototype}: no item fields");
        return InventoryArt(item.InventoryArt);
    }

    /// <summary>ARC-UI-035: an item as the item window names it (0x554F10's first part, HK22): its type icon (0x554C20, always the type
    /// icon there, never the inventory art), its name as 0x441B60 gives it (the description when the item is identified, else its
    /// unknown description; a key its key's name) and the name's colour (an identified hexed item orange, another magickal one blue).
    /// <paramref name="row"/> is the item's object row when a map holds it, else its prototype is read. A name that does not resolve
    /// falls back to <paramref name="fallbackName"/>.</summary>
    public AreaItemDescription Describe(SessionItemKind kind, AreaObjectRecord? row, bool identifiedMark, ObjectDescriptionCatalog? catalog, string fallbackName)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(fallbackName);
        var fields = row is not null ? CombatFieldReader.Read(row).Fields : Prototype(kind.Prototype);
        if (fields is not { Item: { } item }) return new(null, fallbackName, AreaMessageColour.White);
        var facts = Facts(fields, identifiedMark);
        var colour = NameColour(facts);
        string? name = null;
        if (catalog is not null)
        {
            // The session's identification mark counts as the item flag 0x1 the name reads (0x464370).
            ObjectFieldReader read = row is not null ? slot => Int32(row, slot)
                : slot => slot switch
                {
                    ObjectDisplayName.ItemFlagsSlot => unchecked((int)item.Flags),
                    ObjectDisplayName.ItemComplexitySlot => item.MagickTechComplexity,
                    ObjectDisplayName.DescriptionSlot => kind.Description,
                    ObjectDisplayName.ItemUnknownDescriptionSlot => UnknownDescription(kind.Prototype),
                    _ => null,
                };
            ObjectFieldReader marked = slot => slot == ObjectDisplayName.ItemFlagsSlot && identifiedMark && read(slot) is { } flags
                ? flags | (int)ObjectDisplayName.IdentifiedItemFlag : read(slot);
            var resolved = ObjectDisplayName.Resolve(fields.Object.Type, marked, catalog, CritterRecognition.Recognized);
            if (resolved.Message is { } message && MesText.Decode(message, AreaHoverIndex.TextProfile) is { Status: MesDecodeStatus.Decoded, Text: { Length: > 0 } text })
                name = text;
        }
        return new(HotkeyBarRules.ItemTypeIcon(facts), name ?? fallbackName, colour);
    }

    /// <summary>The item window's name font (0x554F10): orange (0x64C538) for an identified item with item flag 0x40 (hexed), else blue
    /// (0x64C470) for a positive complexity (magickal), else white (0x739F88).</summary>
    public static AreaMessageColour NameColour(HotkeyItemIconFacts facts) =>
        facts.Identified && facts.Hexed ? AreaMessageColour.Hexed : facts.Complexity > 0 ? AreaMessageColour.Magick : AreaMessageColour.White;

    private static int? Int32(AreaObjectRecord record, int slot)
    {
        AreaFieldValue? found = null;
        foreach (var field in record.EffectiveFields)
        {
            if (field.Slot != slot) continue;
            if (found is not null) return null;
            found = field;
        }
        return found is { Presence: AreaPresence.Value, Number: { } n } && n is >= int.MinValue and <= int.MaxValue ? (int)n : null;
    }

    /// <summary>The fields 0x554C20 reads, from an item's stored fields and the session's identification mark.</summary>
    public static HotkeyItemIconFacts Facts(CombatFields fields, bool identifiedMark)
    {
        ArgumentNullException.ThrowIfNull(fields);
        var item = fields.Item ?? throw new ArgumentException("not an item", nameof(fields));
        return new(fields.Object.Type, item.MagickTechComplexity,
            item.MagickTechComplexity <= 0 || (item.Flags & IdentifiedFlag) != 0 || identifiedMark, (item.Flags & HexedFlag) != 0,
            CombatItemArt.Subtype(item.UseArtFragment), CombatItemArt.Coverage(item.InventoryArt), fields.Ammo?.AmmoType ?? 0,
            fields.Written?.Subtype ?? 0, item.Discipline);
    }

    // A prototype's unknown description (field 0x62), read once, within the same prototype limit.
    private int? UnknownDescription(int prototype)
    {
        if (_unknownDescriptions.TryGetValue(prototype, out var known)) return known;
        if (_unknownDescriptions.Count >= _limits.MaxPrototypes) return null;
        var number = ObjectSoundReader.ReadPrototype(_prototypes.Read, prototype).Fields?.Item?.UnknownDescription;
        _unknownDescriptions[prototype] = number;
        return number;
    }

    private CombatFields? Prototype(int prototype)
    {
        if (_prototypeFields.TryGetValue(prototype, out var known)) return known;
        if (_prototypeFields.Count >= _limits.MaxPrototypes) return null;
        var fields = CombatFieldReader.ReadPrototype(_prototypes.Read, prototype).Fields;
        _prototypeFields[prototype] = fields;
        return fields;
    }

    private (AreaInterfaceFrame? Frame, string Detail) InventoryArt(uint aid)
    {
        if (_arts.TryGetValue(aid, out var known)) return known;
        var read = _arts.Count >= _limits.MaxArts ? (null, $"more than {_limits.MaxArts} inventory arts") : Read(aid);
        if (read.Frame is { } frame) _rgbaBytes += frame.Rgba.Length;
        _arts[aid] = read;
        return read;
    }

    private (AreaInterfaceFrame? Frame, string Detail) Read(uint aid)
    {
        var resolved = AreaObjectArt.ResolveItemInventory(aid, (role, key) => Table() is { } table && table.TryGetValue(key, out var name) ? name : null);
        if (resolved.Path is not { } selection) return (null, $"inventory art {resolved.Status} {resolved.Detail}");
        if (_stack.Resolve(selection.Path).Winner is not { } winner) return (null, $"{selection.Path} missing");
        if (winner.Length > _limits.MaxFileBytes) return (null, $"{selection.Path} {winner.Length} bytes");
        var read = _stack.Read(winner);
        if (read.Status != ContentReadStatus.Read || read.Bytes is not { } bytes) return (null, $"{selection.Path} {read.Status}");
        try
        {
            var options = new ArtDecoderOptions { MaxInputBytes = _limits.MaxFileBytes, MaxEncodedFrameBytes = _limits.MaxFileBytes, AllowTrailingBytes = true };
            var decoded = ArtDecoder.Decode(bytes, new ArtFrameSelection(selection.Group, selection.Frame, selection.Palette), options);
            if (_rgbaBytes + decoded.Rgba.Count > _limits.MaxRgbaBytes) return (null, $"{selection.Path}: more than {_limits.MaxRgbaBytes} bytes of pixels");
            return (new(decoded.Width, decoded.Height, [.. decoded.Rgba]), $"{selection.Path} {decoded.Width}×{decoded.Height}");
        }
        catch (Exception error) when (error is ArtException or ArgumentException)
        {
            return (null, $"{selection.Path} {error.GetType().Name}");
        }
    }

    private IReadOnlyDictionary<int, string>? Table()
    {
        if (_tableRead) return _table;
        _tableRead = true;
        var found = _stack.Resolve(AreaObjectArt.MappingPath(AreaMappingRole.ItemInventory));
        _table = found.Winner is { } winner && _stack.Read(winner) is { Status: ContentReadStatus.Read, Bytes: { } bytes } ? AreaObjectArt.ReadNameTable(bytes) : null;
        return _table;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _stack.Dispose();
    }
}
