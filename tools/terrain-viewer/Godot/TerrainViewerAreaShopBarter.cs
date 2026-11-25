using Arcanum.Data.Traversal;
using Arcanum.Session;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
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

    // ARC-UI-1806: the script content the shop merchant's bet lines are read from (the generated dialog texts and its override dialog), opened at the first line and
    // owned by the shop (the game's conversation content is another, which the shop never releases); released when the shop's window ends.
    private AreaScriptContentLoad? _shopContent;

    private Arcanum.Data.Scripts.ScriptContent? ShopScriptContent()
    {
        // Only while the shop's window is open: a line asked after its end opens nothing.
        if (!_barterInShop || (_launchArguments ?? _arguments)?.ModulePath is not { } module) return null;
        if (_shopContent is null)
        {
            _shopContent = AreaContentRoot.OpenScripts(module);
            GD.Print($"ARC-UI-1806 shop script content status={_shopContent.Status} detail={_shopContent.Detail}");
        }
        return _shopContent.Content;
    }

    private void ReleaseShopContent() { _shopContent?.Content?.Dispose(); _shopContent = null; }

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
            hero, recipe.Reputations, NewSessionSeed(), NewSessionThrowsGamblingRoll());
        if (created.Owner is not { } owner) { why = $"the session was not created ({created.Failure})"; return null; }
        if (creation.Apply(owner, hero.Effects ?? (_ => null)) is { } applied) { owner.Dispose(); why = applied; return null; }
        var catalog = _hoverNames?.Catalog;
        var names = new AreaInventoryHost([map.Inventory]) { PortraitCatalogueSource = PortraitCatalogueNow };
        // ARC-UI-2283: the session knows its items' art sizes before the shop opens, so the merchant container's regeneration (0x463E20) lays the
        // created wares out by their inventory art (0x461780) and not as 1 x 1 cells drawn over each other; the shop's host rows name their art.
        (_shopInventoryHost, _shopInventorySession) = (names, owner);
        owner.AttachItemArtSizes((item, kind) => InventoryItemArtSize(owner, item, kind));
        var shop = AreaShopSession.Open(owner, map, data,
            (item, kind) => map.Inventory.TryGetRow(item, out _) ? names.Name(item, catalog) : AreaInventoryHost.KindName(kind, catalog),
            AreaHoverIndex.Text(map.MerchantRow, catalog) ?? "merchant", out why, hero.Effects, ShopScriptContent);
        if (shop is null) { owner.Dispose(); (_shopInventoryHost, _shopInventorySession) = (null, null); return null; }
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
            hero.KitDurability, hero.KitWear, hero.KitUse) with { HolderImage = host.HolderImage(map, merchant.Entity), HolderPortrait = host.HolderPortrait(owner, map, merchant.Entity) };
        // ARC-UI-1917: the merchant's doll view, from the merchant's own view when a substitute container is the shown grid (the shop map has no combat host: no ratings).
        if (merchant.Container is not null)
            inventory = WithMerchantDoll(inventory, host.View(owner, _hoverNames?.Catalog, map, merchant.Entity, hero.KitHealing, hero.KitLockpicks, hero.KitDurability, hero.KitWear, hero.KitUse));
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
        // ARC-UI-2924: in the column layout the shop stands in the fitted plate: its place, the black beside it, the strips' buttons and the barter window at the screen's (0, 41), per size.
        if (ColumnMenuSmokeOn())
            await TwoStripScreenColumnForSmoke("shop", () => (art.Position, art.Scale.X), () => _barterPanel!.GridAreaForSelfTest.GetGlobalRect().Position,
                () => art.HoveredForSelfTest?.ToString(), ShopStripSpots(), require, Frames, KitShopView);
        if (System.Environment.GetEnvironmentVariable("ARC_AREA_SHOP_CAPTURE") is { Length: > 0 } capture)
        {
            using var image = GetViewport().GetTexture().GetImage();
            require(image.SavePng(capture) == Error.Ok, "the capture saved to " + capture);
        }
        var (wares, hero) = (_barterPanel.ArtView!.LootGrid.Count, _barterPanel.ArtView.Grid.Count);
        // ARC-UI-2283: no two wares of the merchant's grid cover the same cell (the created wares were laid out as 1 x 1 cells and drew over each other).
        var cells = new HashSet<(int X, int Y)>();
        var overlapping = _barterPanel.ArtView.LootGrid.Where(item => AreaLootArtLayout.GridLocation(item.Location))
            .Count(item => !Enumerable.Range(0, item.Width * item.Height).Select(i => (item.Location % AreaLootArtLayout.Columns + i % item.Width, item.Location / AreaLootArtLayout.Columns + i / item.Width))
                .Select(cell => cells.Add(cell)).ToArray().All(added => added));
        require(overlapping == 0, $"the shop's wares do not overlap in the grid: {overlapping} of {wares} overlap");
        // ARC-UI-1806: the bet box of the docked window (the same hover as the dialog's window): a ware carried over it asks the gate (art 344 or 346 with its line), nothing is bet.
        var bet = await BarterGambleHoverSmokeAsync();
        require(bet.StartsWith("art ", StringComparison.Ordinal), "the docked window's bet box answers a carried ware: " + bet);
        // The line is the merchant's own, read from the generated NPC files through the shop's script content, not the window's fallback text.
        var betLine = bet.Contains("line '", StringComparison.Ordinal) ? bet[(bet.IndexOf("line '", StringComparison.Ordinal) + 6)..^1] : "";
        require(betLine.Length > 0 && !AreaBarter.IsBetFallback(betLine), $"the shop merchant's bet line is a generated NPC line: '{betLine}'");
        var gate = _barterPanel.ArtView!.LootGrid.FirstOrDefault() is { } first ? _barter!.CheckGamble(first.Entity).Gate.Refusal.ToString() : "no ware";
        var (_, bx, by) = AreaShopArtLayout.Button(AreaShopArtButton.Back);
        var over = art.ScreenPointForSelfTest(bx + 15, by + 15);
        // ARC-UI-3120: the editor's refusal is still the newest stored entry (the ring goes on across the two windows), so a hover is shown only 3 s after it (ML2).
        _messageClockShiftForSelfTest += AreaMessageLog.QuietMilliseconds + 1;
        GetViewport().PushInput(new InputEventMouseMotion { Position = over, GlobalPosition = over });
        await Frames(1);
        require(art.HoveredForSelfTest == AreaShopArtButton.Back && art.FrameForSelfTest(AreaShopArtButton.Accept) == 0,
            $"the pointer over Back lights it (and darkens the focus): back={art.FrameForSelfTest(AreaShopArtButton.Back)} accept={art.FrameForSelfTest(AreaShopArtButton.Accept)}");
        // ARC-UI-3120: entering Back writes its hint (MainMenu.mes 6999, a new line, 7002) to the bottom message window; the shop's window is the editor's.
        var (shopTitle, shopBody) = (_mainMenu!.Texts.Original(AreaMainMenuTexts.ButtonHintTitle), _mainMenu.Texts.Original(AreaShopArtLayout.BackHint));
        require(panel.MessageWindowShownForSelfTest && (shopTitle is null || shopBody is null || panel.MessageLog.Shown is { Kind: AreaMessageKind.Hover } shopHint && shopHint.Title == shopTitle + "\n" + shopBody
            && panel.MessageViewForSelfTest.IconForSelfTest == AreaMessageLayout.Icon(AreaMessageKind.Hover)),
            $"the pointer on Back writes its hint to the bottom message window: '{panel.MessageLog.Shown?.Title}' (expected '{shopTitle}' + '{shopBody}')");
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
            Corner = $"{corner.X},{corner.Y}", Wares = wares, Hero = hero, Bet = bet, Gate = gate, Back = back }));
    }
}
