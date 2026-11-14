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
    private AreaInventoryArtLook? _inventoryArtLookForSelfTest;
    private AreaItemIcons? _inventoryItemIcons;
    private readonly Dictionary<string, Texture2D?> _inventoryItemArtCache = new(StringComparer.Ordinal);
    private GameSessionOwner? _inventoryItemArtSession;
    private bool _inventoryItemIconsRead;
    private string? _inventoryItemIconsModule;
    private string _inventoryItemIconsDetail = "not read";
    private readonly Dictionary<(string Item, SessionItemKind Kind), (uint Width, uint Height)?> _inventoryItemSizeCache = new();
    private GameSessionOwner? _inventoryItemSizeSession;

    /// <summary>The window's original look for the current module, read on first use; null when its art did not read.</summary>
    private AreaInventoryArtLook? InventoryArtLook()
    {
        // ARC-UI-064: a self-test's synthetic art set (the synthetic module has no game folder).
        if (_inventoryArtLookForSelfTest is { } synthetic) return synthetic;
        var module = _arguments?.ModulePath;
        if (_inventoryArtContent is not null && module == _inventoryArtModule) return _inventoryArtLook;
        _inventoryArtModule = module;
        _inventoryArtContent = SelfTestBootstrap || module is null ? AreaInventoryArtContent.Empty("self-test: no game folder")
            : WarmedWindowArt(module, slots => slots.Inventory) ?? AreaInventoryArtContent.Load(module);
        _inventoryItemArtCache.Clear();
        _inventoryArtLook = null;
        if (_inventoryArtContent.ArtReady)
        {
            InventoryItemIcons();
            var art = InventoryLookLoads(_inventoryArtContent).ToDictionary(load => load.Number, load => (Texture2D)HotkeyTexture(load.Frames[0]));
            _inventoryArtLook = new(art, InventoryItemArt, _inventoryArtContent.Texts)
                { LootReady = _inventoryArtContent.LootArtReady, BarterReady = _inventoryArtContent.BarterArtReady };
            GD.Print($"ARC-UI-051 inventory art ready {_inventoryArtContent.Detail}; item art {_inventoryItemIconsDetail}; ARC-UI-052 loot art ready={_inventoryArtContent.LootArtReady}; ARC-UI-064 barter art ready={_inventoryArtContent.BarterArtReady}");
        }
        else GD.Print($"ARC-UI-051 inventory art not ready: {_inventoryArtContent.Detail}");
        return _inventoryArtLook;
    }

    /// <summary>The arts the inventory look turns into textures: those that read, each by its first frame.</summary>
    private static IEnumerable<AreaInterfaceArtLoad> InventoryLookLoads(AreaInventoryArtContent content) =>
        content.Art.Values.Where(load => load.Status == AreaInterfaceArtStatus.Ready && load.Frames.Count > 0);

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
        if (_inventoryItemSizeCache.TryGetValue((item, kind), out var known)) return known;
        var (frame, detail) = icons.InventoryFrame(kind, InventoryItemRow(item, session));
        (uint, uint)? size = frame is null ? null : ((uint)frame.Width, (uint)frame.Height);
        if (frame is null) GD.Print($"ARC-UI-055 inventory item-size item={item} not read: {detail}");
        _inventoryItemSizeCache[(item, kind)] = size;
        return size;
    }

    // An item's stored fields from the map row that holds it, null when no map of this run does (ARC-UI-069: the shop map's for the
    // opening shop's session).
    private CombatFields? InventoryItemRow(string item, GameSessionOwner? session = null)
    {
        var host = _shopInventoryHost is not null && session is not null && ReferenceEquals(session, _shopInventorySession) ? _shopInventoryHost : _inventoryHost;
        foreach (var map in host?.Maps ?? [])
            if (map.TryGetRow(item, out var found)) return CombatFieldReader.Read(found).Fields;
        // ARC-DEV-354: a carried item taken in another window of the main map has the row the host keeps for it.
        return host?.KnownRow(item) is { } carried ? CombatFieldReader.Read(carried).Fields : null;
    }

    /// <summary>An item's inventory art (field 0x5D, IW22), read once per item and session; null when its fields or art do not read.
    /// ARC-UI-052: a looted holder's item too (its kind from the open window's grid). ARC-UI-069: the opening shop's barter window's items are
    /// its session's.</summary>
    private Texture2D? InventoryItemArt(string item)
    {
        var shown = _barterInShop && BarterOpen ? _barterSession : _areaSession;
        if (shown is not { } session || InventoryItemIcons() is not { } icons) return null;
        if (!ReferenceEquals(_inventoryItemArtSession, session)) { _inventoryItemArtCache.Clear(); _inventoryItemArtSession = session; }
        if (_inventoryItemArtCache.TryGetValue(item, out var known)) return known;
        Texture2D? texture = null;
        if (InventoryItemKind(session, item) is { } kind)
        {
            var (frame, detail) = icons.InventoryFrame(kind, InventoryItemRow(item, session));
            if (frame is not null) texture = HotkeyTexture(frame);
            else GD.Print($"ARC-UI-051 inventory item-art item={item} not read: {detail}");
        }
        _inventoryItemArtCache[item] = texture;
        return texture;
    }

    /// <summary>ARC-UI-052: the kind of an item the hero holds or the open loot window's holder holds (ARC-UI-064: or the barter window's
    /// shown object, ARC-UI-1917: or its merchant's worn item); null otherwise.</summary>
    private SessionItemKind? InventoryItemKind(GameSessionOwner session, string item) =>
        HeroItemKind(session, item) ?? (InventoryOpen ? _inventoryPanel?.CurrentView?.LootGrid.FirstOrDefault(entry => entry.Entity == item)?.Kind : null)
            ?? (InventoryOpen ? _inventoryPanel?.CurrentView?.BodyDoll.FirstOrDefault(entry => entry.Entity == item)?.Kind : null)
            ?? _barterPanel?.ArtView?.LootGrid.FirstOrDefault(entry => entry.Entity == item)?.Kind
            // ARC-UI-1917: the merchant's worn items, drawn in the barter window's doll view.
            ?? _barterPanel?.ArtView?.BodyDoll.FirstOrDefault(entry => entry.Entity == item)?.Kind;
}
