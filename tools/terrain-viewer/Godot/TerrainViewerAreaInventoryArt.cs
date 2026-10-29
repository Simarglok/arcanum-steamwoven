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

    /// <summary>The window's original look for the current module, read on first use; null when its art did not read.</summary>
    private AreaInventoryArtLook? InventoryArtLook()
    {
        var module = _arguments?.ModulePath;
        if (_inventoryArtContent is not null && module == _inventoryArtModule) return _inventoryArtLook;
        _inventoryArtModule = module;
        _inventoryArtContent = SelfTestBootstrap || module is null ? AreaInventoryArtContent.Empty("self-test: no game folder") : AreaInventoryArtContent.Load(module);
        _inventoryItemIcons?.Dispose();
        _inventoryItemIcons = null;
        _inventoryItemArtCache.Clear();
        _inventoryArtLook = null;
        if (_inventoryArtContent.ArtReady)
        {
            _inventoryItemIcons = AreaItemIcons.Open(module!, out var detail);
            var art = _inventoryArtContent.Art.Values.Where(load => load.Status == AreaInterfaceArtStatus.Ready && load.Frames.Count > 0)
                .ToDictionary(load => load.Number, load => (Texture2D)HotkeyTexture(load.Frames[0]));
            _inventoryArtLook = new(art, InventoryItemArt, _inventoryArtContent.Texts) { LootReady = _inventoryArtContent.LootArtReady };
            GD.Print($"ARC-UI-051 inventory art ready {_inventoryArtContent.Detail}; item art {detail}; ARC-UI-052 loot art ready={_inventoryArtContent.LootArtReady}");
        }
        else GD.Print($"ARC-UI-051 inventory art not ready: {_inventoryArtContent.Detail}");
        return _inventoryArtLook;
    }

    /// <summary>An item's inventory art (field 0x5D, IW22), read once per item and session; null when its fields or art do not read.
    /// ARC-UI-052: a looted holder's item too (its kind from the open window's grid).</summary>
    private Texture2D? InventoryItemArt(string item)
    {
        if (_areaSession is not { } session || _inventoryItemIcons is not { } icons) return null;
        if (!ReferenceEquals(_inventoryItemArtSession, session)) { _inventoryItemArtCache.Clear(); _inventoryItemArtSession = session; }
        if (_inventoryItemArtCache.TryGetValue(item, out var known)) return known;
        Texture2D? texture = null;
        if (InventoryItemKind(session, item) is { } kind)
        {
            CombatFields? row = null;
            foreach (var map in _inventoryHost?.Maps ?? [])
                if (map.TryGetRow(item, out var found)) { row = CombatFieldReader.Read(found).Fields; break; }
            var (frame, detail) = icons.InventoryFrame(kind, row);
            if (frame is not null) texture = HotkeyTexture(frame);
            else GD.Print($"ARC-UI-051 inventory item-art item={item} not read: {detail}");
        }
        _inventoryItemArtCache[item] = texture;
        return texture;
    }

    /// <summary>ARC-UI-052: the kind of an item the hero holds or the open loot window's holder holds; null otherwise.</summary>
    private SessionItemKind? InventoryItemKind(GameSessionOwner session, string item) =>
        HeroItemKind(session, item) ?? (InventoryOpen ? _inventoryPanel?.CurrentView?.LootGrid.FirstOrDefault(entry => entry.Entity == item)?.Kind : null);
}
