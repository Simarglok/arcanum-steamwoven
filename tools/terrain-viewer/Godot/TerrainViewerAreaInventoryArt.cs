using Arcanum.Data.Combat;
using Arcanum.Session;
using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-UI-051: the inventory window's original look (docs/ui/original-windows.md; ledger IW20–IW25). The window's interface art and texts are
// read once a module; an item's inventory art once per item and session. Without a game folder (the synthetic self-tests) the window keeps
// its diagnostic look.
public partial class TerrainViewerApp
{
    private AreaInventoryArtContent? _inventoryArtContent;
    private string? _inventoryArtModule;
    private AreaInventoryArtLook? _inventoryArtLook;
    private AreaItemIcons? _inventoryItemIcons;
    private readonly Dictionary<string, Texture2D?> _inventoryItemArtCache = new(StringComparer.Ordinal);
    private GameSessionOwner? _inventoryItemArtSession;
    private bool _inventoryItemIconsRead;
    private string? _inventoryItemIconsModule;
    private string _inventoryItemIconsDetail = "not read";
    private readonly Dictionary<string, (uint Width, uint Height)?> _inventoryItemSizeCache = new(StringComparer.Ordinal);
    private GameSessionOwner? _inventoryItemSizeSession;

    /// <summary>The window's original look for the current module, read on first use; null when its art did not read.</summary>
    private AreaInventoryArtLook? InventoryArtLook()
    {
        var module = _arguments?.ModulePath;
        if (_inventoryArtContent is not null && module == _inventoryArtModule) return _inventoryArtLook;
        _inventoryArtModule = module;
        _inventoryArtContent = SelfTestBootstrap || module is null ? AreaInventoryArtContent.Empty("self-test: no game folder") : AreaInventoryArtContent.Load(module);
        _inventoryItemArtCache.Clear();
        _inventoryArtLook = null;
        if (_inventoryArtContent.ArtReady)
        {
            InventoryItemIcons();
            var art = _inventoryArtContent.Art.Values.ToDictionary(load => load.Number, load => (Texture2D)HotkeyTexture(load.Frames[0]));
            _inventoryArtLook = new(art, InventoryItemArt, _inventoryArtContent.Texts);
            GD.Print($"ARC-UI-051 inventory art ready {_inventoryArtContent.Detail}; item art {_inventoryItemIconsDetail}");
        }
        else GD.Print($"ARC-UI-051 inventory art not ready: {_inventoryArtContent.Detail}");
        return _inventoryArtLook;
    }

    /// <summary>The item art reader of the current module (ARC-UI-051 drawing, ARC-UI-055 sizes), opened on first use; null without a game
    /// folder (the synthetic self-tests) or when its content stack does not open.</summary>
    private AreaItemIcons? InventoryItemIcons()
    {
        var module = _arguments?.ModulePath;
        if (_inventoryItemIconsRead && module == _inventoryItemIconsModule) return _inventoryItemIcons;
        _inventoryItemIcons?.Dispose();
        _inventoryItemIcons = null;
        _inventoryItemArtCache.Clear();
        _inventoryItemSizeCache.Clear();
        (_inventoryItemIconsRead, _inventoryItemIconsModule) = (true, module);
        if (SelfTestBootstrap || module is null) _inventoryItemIconsDetail = "not read (self-test: no game folder)";
        else _inventoryItemIcons = AreaItemIcons.Open(module, out _inventoryItemIconsDetail);
        return _inventoryItemIcons;
    }

    /// <summary>ARC-UI-055: gives the session the frames of its items' inventory art (0x461780 sizes them in grid cells, ledger JP3);
    /// called wherever the running session is set, as the encumbrance effect is.</summary>
    private void AttachAreaItemArtSizes()
    {
        if (_areaSession is not { } session) return;
        var before = session.QueryHeroGrid();
        session.AttachItemArtSizes((item, kind) => InventoryItemArtSize(session, item, kind));
        var after = session.QueryHeroGrid();
        GD.Print($"ARC-UI-055 item art sizes {(InventoryItemIcons() is null ? "not attached: " + _inventoryItemIconsDetail : "attached")}; grid " +
            string.Join(' ', after.Items.Select(placed => $"{placed.Entity}@{placed.Slot}:{placed.Width}x{placed.Height}")) +
            $"; unplaced={after.Unplaced.Count}; relaid={(before.Items.SequenceEqual(after.Items) ? "no" : "yes")}");
    }

    /// <summary>ARC-UI-055: the frame of an item's inventory art in pixels (its map row's fields when a map holds it, else its kind's
    /// prototype's), read once per item and session; null when its fields or art do not read.</summary>
    private (uint Width, uint Height)? InventoryItemArtSize(GameSessionOwner session, string item, SessionItemKind kind)
    {
        if (InventoryItemIcons() is not { } icons) return null;
        if (!ReferenceEquals(_inventoryItemSizeSession, session)) { _inventoryItemSizeCache.Clear(); _inventoryItemSizeSession = session; }
        if (_inventoryItemSizeCache.TryGetValue(item, out var known)) return known;
        var (frame, detail) = icons.InventoryFrame(kind, InventoryItemRow(item));
        (uint, uint)? size = frame is null ? null : ((uint)frame.Width, (uint)frame.Height);
        if (frame is null) GD.Print($"ARC-UI-055 inventory item-size item={item} not read: {detail}");
        _inventoryItemSizeCache[item] = size;
        return size;
    }

    // An item's stored fields from the map row that holds it, null when no map of this run does.
    private CombatFields? InventoryItemRow(string item)
    {
        foreach (var map in _inventoryHost?.Maps ?? [])
            if (map.TryGetRow(item, out var found)) return CombatFieldReader.Read(found).Fields;
        return null;
    }

    /// <summary>An item's inventory art (field 0x5D, IW22), read once per item and session; null when its fields or art do not read.</summary>
    private Texture2D? InventoryItemArt(string item)
    {
        if (_areaSession is not { } session || InventoryItemIcons() is not { } icons) return null;
        if (!ReferenceEquals(_inventoryItemArtSession, session)) { _inventoryItemArtCache.Clear(); _inventoryItemArtSession = session; }
        if (_inventoryItemArtCache.TryGetValue(item, out var known)) return known;
        Texture2D? texture = null;
        if (HeroItemKind(session, item) is { } kind)
        {
            var (frame, detail) = icons.InventoryFrame(kind, InventoryItemRow(item));
            if (frame is not null) texture = HotkeyTexture(frame);
            else GD.Print($"ARC-UI-051 inventory item-art item={item} not read: {detail}");
        }
        _inventoryItemArtCache[item] = texture;
        return texture;
    }
}
