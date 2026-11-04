using Arcanum.Data.Traversal;
using Arcanum.Session;
using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-UI-038: the opening shop's merchant (window 15, ledger OS2, OS7). The original's new game runs on the map list's shopping map behind
// the menu and the shop's refresh opens barter with that map's first NPC. Steamwoven loads the shop map in the background once a run (from
// the new-character window on), and when the shop opens it makes the new character's session on the shop map's start as the start makes
// it (the window's choices and the editor's presses), opens the shop there (AreaShopSession) and shows the barter window above the shop.
// Accept starts the game on that session (best items, then the teleport to the start map, OS5); Back drops it with the kit. ARC-UI-069: the
// barter window shows the original look (ARC-UI-064) docked at the screen's (0, 41) between the shop's strips and art buttons (MW30).
public partial class TerrainViewerApp
{
    // ARC-UI-069: the shop map's inventory host and the shop session it serves (the barter window's original look and its items' art).
    private AreaInventoryHost? _shopInventoryHost;
    private GameSessionOwner? _shopInventorySession;

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
            hero, recipe.Reputations, NewSessionSeed());
        if (created.Owner is not { } owner) { why = $"the session was not created ({created.Failure})"; return null; }
        if (creation.Apply(owner, hero.Effects ?? (_ => null)) is { } applied) { owner.Dispose(); why = applied; return null; }
        var catalog = _hoverNames?.Catalog;
        var names = new AreaInventoryHost([map.Inventory]);
        var shop = AreaShopSession.Open(owner, map, data,
            (item, kind) => map.Inventory.TryGetRow(item, out _) ? names.Name(item, catalog) : AreaInventoryHost.KindName(kind, catalog),
            AreaHoverIndex.Text(map.MerchantRow, catalog) ?? "merchant", out why);
        if (shop is null) { owner.Dispose(); return null; }
        (_shopInventoryHost, _shopInventorySession) = (names, owner);
        // ARC-UI-069: the hero's grid lays its items out by their inventory art, as the game's session does (ARC-UI-055).
        owner.AttachItemArtSizes((item, kind) => InventoryItemArtSize(owner, item, kind));
        return shop;
    }

    /// <summary>ARC-UI-069: the opening shop's barter window in the original look: the shop session's hero and the merchant's shown object
    /// (its substitute container when it has one) from the shop map's host, with the kit's tables; null when the shop's session is gone.</summary>
    private (AreaInventoryView Inventory, AreaBarterFrame Frame)? ShopBarterArt(AreaBarterView view)
    {
        if (_mainMenu?.Creation is not { Shop: { Owner: { } owner } shop, Kit: { Hero: var hero } } || _shopInventoryHost is not { } host
            || !ReferenceEquals(_shopInventorySession, owner) || !ReferenceEquals(_barterSession, owner)) return null;
        var merchant = view.Merchant;
        var map = shop.Map.RuntimeMap;
        var inventory = host.View(owner, _hoverNames?.Catalog, map, merchant.Container ?? merchant.Entity, hero.KitHealing, hero.KitLockpicks,
            hero.KitDurability, hero.KitWear, hero.KitUse) with { HolderImage = host.HolderImage(map, merchant.Entity) };
        var frame = new AreaBarterFrame(AreaBarterArtLayout.Background(merchant.Follower, owner.QueryParty().Followers.Count), merchant.Name, _barterMessage ?? string.Empty);
        return (inventory, frame);
    }

    /// <summary>ARC-UI-069: the opening shop's window is drawn in its art: its barter window is open in the original look.</summary>
    private bool ShopArtShown => ShopBarterActive && _barterPanel is { IsOpen: true, ArtMode: true };

    /// <summary>Whether the open barter window is the opening shop's and its session is still the shop's.</summary>
    private bool ShopBarterActive => _barterInShop && _mainMenu is { Window: AreaMenuWindow.Shop, QuitPending: false } menu && MainMenuOpen
        && menu.Creation?.Shop?.Owner is { } owner && ReferenceEquals(owner, _barterSession);

    /// <summary>ARC-UI-069: the menu smoke's shop part: the opening shop in the original's look (MW30): the strips and the art buttons, the
    /// barter window's original look docked at the screen's (0, 41) with the merchant's wares in its grid, no round view (IW33); the pointer
    /// over Back lights it, and with <paramref name="back"/> a click on it (pressed and released over itself) goes back to the editor. The
    /// stage runs on the game's data, so the art is required (without it the shop's diagnostic window is the self-test's).
    /// ARC_AREA_SHOP_CAPTURE saves an in-app readback of the window to the caller's ignored path.</summary>
    private async Task ShopArtForSmoke(Action<bool, string> require, bool back)
    {
        async Task Frames(int count)
        {
            for (var i = 0; i < count; i++)
            {
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            }
        }
        var panel = _mainMenuPanel!;
        var art = panel.ShopArtForSelfTest;
        require(panel.ShopArtMode && !panel.PanelVisibleForSelfTest && _barterPanel is { ArtMode: true, Docked: true, RoundViewShown: false }
            && _barterPanel.ArtView is { LootGrid.Count: > 0, Holder: not null },
            $"the shop shows its art with the barter window's original look: {_premadeArtContent?.Detail} wares={_barterPanel?.ArtView?.LootGrid.Count}");
        await Frames(2);
        var corner = _barterPanel!.GridAreaForSelfTest.GetGlobalRect().Position;
        var (wx, wy, _, _) = AreaShopArtLayout.Window;
        var expected = art.ScreenPointForSelfTest(wx, wy);
        require(corner.DistanceTo(expected) < 1, $"the barter window stands at the screen's (0, 41): {corner} (expected {expected})");
        if (System.Environment.GetEnvironmentVariable("ARC_AREA_SHOP_CAPTURE") is { Length: > 0 } capture)
        {
            using var image = GetViewport().GetTexture().GetImage();
            require(image.SavePng(capture) == Error.Ok, "the capture saved to " + capture);
        }
        var (wares, hero) = (_barterPanel.ArtView!.LootGrid.Count, _barterPanel.ArtView.Grid.Count);
        var (_, bx, by) = AreaShopArtLayout.Button(AreaShopArtButton.Back);
        var over = art.ScreenPointForSelfTest(bx + 15, by + 15);
        GetViewport().PushInput(new InputEventMouseMotion { Position = over, GlobalPosition = over });
        await Frames(1);
        require(art.HoveredForSelfTest == AreaShopArtButton.Back && art.FrameForSelfTest(AreaShopArtButton.Accept) == 0,
            $"the pointer over Back lights it (and darkens the focus): back={art.FrameForSelfTest(AreaShopArtButton.Back)} accept={art.FrameForSelfTest(AreaShopArtButton.Accept)}");
        if (back)
            foreach (var pressed in new[] { true, false })
            {
                GetViewport().PushInput(new InputEventMouseButton
                {
                    ButtonIndex = MouseButton.Left, Pressed = pressed, Position = over, GlobalPosition = over, ButtonMask = pressed ? MouseButtonMask.Left : 0,
                });
                await Frames(1);
            }
        GD.Print("ARC-UI-069 native-smoke shop art PASS " + System.Text.Json.JsonSerializer.Serialize(new {
            Corner = $"{corner.X},{corner.Y}", Wares = wares, Hero = hero, Back = back }));
    }
}
