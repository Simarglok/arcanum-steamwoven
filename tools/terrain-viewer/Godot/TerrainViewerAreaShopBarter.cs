using Arcanum.Data.Traversal;
using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-UI-038: the opening shop's merchant (window 15, ledger OS2, OS7). The original's new game runs on the map list's shopping map behind
// the menu and the shop's refresh opens barter with that map's first NPC. Steamwoven loads the shop map in the background once a run (from
// the new-character window on), and when the shop opens it makes the new character's session on the shop map's start as the start makes
// it (the window's choices and the editor's presses), opens the shop there (AreaShopSession) and shows the barter window above the shop.
// Accept starts the game on that session (best items, then the teleport to the start map, OS5); Back drops it with the kit.
public partial class TerrainViewerApp
{
    private Task<AreaShopMapLoad>? _shopMapTask;
    private string? _shopMapModule;
    private AreaShopMapLoad? _shopMap;
    // Self-test seam: the shop map a synthetic menu test gives instead of loading one.
    private Func<AreaShopMapLoad>? _shopMapForSelfTest;

    /// <summary>Starts loading the launch module's shop map once (a later call for the same module keeps the load or its result).</summary>
    private void BeginShopMapLoad()
    {
        if (_shopMapForSelfTest is { } fixture) { _shopMap ??= fixture(); return; }
        if ((_launchArguments ?? _arguments) is not { } launch) return;
        if (_shopMapModule == launch.ModulePath && (_shopMapTask is not null || _shopMap is not null)) return;
        _shopMapModule = launch.ModulePath; _shopMap = null; _shopMapTask = null;
        _shopMapTask = Task.Run(() => AreaShopMap.Load(launch));
    }

    private AreaShopMapLoad? AdoptShopMap()
    {
        if (_shopMap is null && _shopMapTask is { IsCompleted: true } task)
        {
            _shopMap = task.IsCompletedSuccessfully ? task.Result
                : new(AreaShopMapStatus.Failed, null, "the load threw " + (task.Exception?.GetBaseException().GetType().Name ?? "cancelled"));
            _shopMapTask = null;
            GD.Print($"ARC-UI-038 shop map status={_shopMap.Status} detail={_shopMap.Detail}");
        }
        return _shopMap;
    }

    /// <summary>Per frame while the menu shows the opening shop waiting for its merchant: once the shop map and the barter data are loaded
    /// the shop's session opens and the barter window shows; a module without a shop map or a shop map without an NPC starts the game at
    /// once, as the original (OS2); a shop the project cannot open is the kit alone, with the reason in the window's note.</summary>
    private void SyncShopBarter()
    {
        if (_mainMenu is not { Window: AreaMenuWindow.Shop, QuitPending: false, ShopNote: AreaMainMenu.ShopLoading, Creation: { Kit: { } kit, Shop: null } creation } menu
            || !MainMenuOpen) return;
        if (AdoptShopMap() is not { } load) { BeginShopMapLoad(); return; }
        if (load.Status is AreaShopMapStatus.NoShopMap or AreaShopMapStatus.NoMerchant)
        {
            menu.ShopNote = null;
            GD.Print($"ARC-UI-038 shop starts the game at once: {load.Detail}");
            MainMenuCommand(() => new(AreaMenuCommandKind.StartNewCharacter));
            return;
        }
        string? why = null;
        if (load.Map is not { } map) why = load.Detail;
        else
        {
            AdoptBarterData();
            if (_barterData is null) { if (_barterDataTask is null && _arguments is { } arguments) BindAreaBarter(arguments); return; }
            if (_barterData is not { Data: { } data }) why = $"no barter data ({_barterData.Value.Status}: {_barterData.Value.Detail})";
            else if (OpenShopSession(creation, kit, map, data, out why) is { Owner: { } owner } shop)
            {
                creation.TakeShop(shop, kit);
                menu.ShopNote = null;
                GD.Print($"ARC-UI-038 shop barter open map={map.Name} runtime={map.RuntimeMap} merchant={map.Merchant} container={shop.Barter.Merchant.Container ?? "none"} "
                    + $"merchantRegen={shop.MerchantRegeneration.Regeneration?.Outcome} removed={shop.MerchantRegeneration.Regeneration?.Removed} "
                    + $"containerRegen={shop.ContainerRegeneration?.Regeneration?.Outcome.ToString() ?? "-"} created={shop.ContainerRegeneration?.Regeneration?.Created.Count ?? 0} "
                    + $"coins={shop.ContainerRegeneration?.Regeneration?.Coins ?? 0} moved={shop.Opened.Coins} identified={shop.Opened.Identified}");
                ShowBarterWindow(shop.Barter, owner, shop.Opened, shop: true);
            }
        }
        if (why is not null)
        {
            menu.ShopNote = AreaMainMenu.ShopUnavailable(why);
            GD.Print("ARC-UI-038 shop without its merchant: " + why);
        }
        RenderMainMenu();
    }

    /// <summary>The new character's session on the shop map's start, made as the start makes it (this scene's new-game recipe, the kit's
    /// hero, the window's choices and the editor's presses), with the shop opened on it; null with the reason (the session is disposed).</summary>
    private AreaShopSession? OpenShopSession(AreaNewCharacter creation, AreaOpeningKit kit, AreaShopMap map, AreaBarterData data, out string? why)
    {
        why = null;
        if (_newGameRecipe is not { } recipe) { why = "this scene has no new-game start to make the session from"; return null; }
        var hero = kit.Hero;
        var created = AreaTransitionCoordinator.CreateSession(recipe.Clock, Guid.NewGuid(), recipe.Module, new AreaStepCell(map.Start.X, map.Start.Y), map.RuntimeMap,
            hero, recipe.Reputations);
        if (created.Owner is not { } owner) { why = $"the session was not created ({created.Failure})"; return null; }
        if (creation.Apply(owner, hero.Effects ?? (_ => null)) is { } applied) { owner.Dispose(); why = applied; return null; }
        var catalog = _hoverNames?.Catalog;
        var names = new AreaInventoryHost([map.Inventory]);
        var shop = AreaShopSession.Open(owner, map, data,
            (item, kind) => map.Inventory.TryGetRow(item, out _) ? names.Name(item, catalog) : AreaInventoryHost.KindName(kind, catalog),
            AreaHoverIndex.Text(map.MerchantRow, catalog) ?? "merchant", out why);
        if (shop is null) owner.Dispose();
        return shop;
    }

    /// <summary>Whether the open barter window is the opening shop's and its session is still the shop's.</summary>
    private bool ShopBarterActive => _barterInShop && _mainMenu is { Window: AreaMenuWindow.Shop, QuitPending: false } menu && MainMenuOpen
        && menu.Creation?.Shop?.Owner is { } owner && ReferenceEquals(owner, _barterSession);
}
