using Arcanum.Data.Combat;
using Arcanum.Input;
using Arcanum.Session;
using Arcanum.TerrainViewer.Core;
using GamepadLab.Core;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-UI-050: the barter window of the session's hero in crash-site-area (exterior, cave and map mode). A conversation reply `b:` ends
// the talk (the original's interface-mode change, ledger BT7) and the window opens with the speaker as merchant once nothing else
// owns input. Like the schematic window it is modal (gate closed, clock paused, a walk stopped); Escape, a controller cancel or the
// button closes it, and the talk does not resume. Trades run through the session (AreaBarter); a merchant's buy list, price multiplier
// and fence flag come from its stored fields and the module's inventory sources, loaded once per module in the background. ARC-UI-064:
// with the game's art the window draws the original barter window (docs/ui/original-windows.md, ledger BW1–BW5): the open centres the
// game view on the PC for the round view, a drag from the merchant's grid onto the hero's buys, one the other way sells, and a resting
// pointer puts the item's quote on the frame's quote line.
public partial class TerrainViewerApp
{
    /// <summary>The trade sound the original requests after every completed move (0x578EA0, SN31).</summary>
    private const int BarterTradeSound = 8158;
    private AreaBarterPanel? _barterPanel;
    private AreaBarter? _barter;
    private GameSessionOwner? _barterSession;
    private long _barterRevision = -1;
    private string? _barterStatus;
    private string? _barterMessage;
    private Task<(AreaBarterDataStatus Status, AreaBarterData? Data, string Detail)>? _barterDataTask;
    private string? _barterDataModule;
    private (AreaBarterDataStatus Status, AreaBarterData? Data, string Detail)? _barterData;
    // The session and talk host whose b: reply asked for the window, and the merchant (entity, speaker); a replaced session drops it.
    private (GameSessionOwner Session, AreaConversationHost Host, string Entity, int Speaker)? _barterPending;
    // The trade sounds requested, newest last (the self-test reads them; the audio may be absent).
    private readonly Queue<int> _barterSounds = new();
    // ARC-UI-038: the window is the opening shop's (its session is the shop's, the menu owns the keys).
    private bool _barterInShop;
    private bool BarterOpen => _barterPanel?.IsOpen == true;
    private bool BarterActive => _barterInShop ? ShopBarterActive : _areaSession is not null && _readyMode == TerrainViewerMode.CrashSiteArea;

    /// <summary>Starts loading the barter data for this module once; a reload, the cave or a map transfer reuses them.</summary>
    private void BindAreaBarter(TerrainViewerArguments arguments)
    {
        if (_barterDataModule == arguments.ModulePath) return;
        _barterDataModule = arguments.ModulePath; _barterData = null;
        var module = arguments.ModulePath;
        _barterDataTask = Task.Run(() => AreaBarterData.Load(module));
    }

    private void AdoptBarterData()
    {
        if (_barterData is not null || _barterDataTask is not { IsCompleted: true } task) return;
        _barterData = task.IsCompletedSuccessfully ? task.Result
            : (AreaBarterDataStatus.SourcesFailed, null, "load threw " + (task.Exception?.GetBaseException().GetType().Name ?? "cancelled"));
        _barterDataTask = null;
        GD.Print($"ARC-UI-050 barter data status={_barterData.Value.Status} detail={_barterData.Value.Detail}");
    }

    /// <summary>A `b:` reply ended the conversation (<see cref="Arcanum.Scripting.Dialog.DialogEndKind.BarterWindow"/>): the window opens
    /// with the speaker as merchant once nothing else owns input.</summary>
    private void QueueDialogBarter(AreaConversationHost host, (string Entity, int Speaker) merchant)
    {
        GD.Print($"ARC-UI-050 dialog barter merchant={merchant.Entity}");
        _barterPending = (_areaSession!, host, merchant.Entity, merchant.Speaker);
    }

    private void TakeDialogBarter()
    {
        if (_barterPending is not { } pending) return;
        if (!ReferenceEquals(pending.Session, _areaSession) || !BarterActive) { _barterPending = null; return; }
        if (BarterOpen || WorldModal) return;
        AdoptBarterData();
        if (_barterData is null) { _barterStatus = "barter data loading"; return; }
        _barterPending = null;
        OpenBarter(pending.Host, pending.Entity, pending.Speaker);
    }

    private void OpenBarter(AreaConversationHost host, string entity, int speaker)
    {
        var session = _areaSession!;
        string Refuse(string why) { _barterStatus = why; GD.Print($"ARC-UI-050 barter refused: {why}"); RefreshAreaStatus(); return why; }
        if (!AreaBarter.HeroCanTrade(session)) { Refuse("no living hero"); return; }
        if (_barterData is not { Data: { } data }) { Refuse($"no barter data ({_barterData?.Status}: {_barterData?.Detail})"); return; }
        if (speaker < 0 || speaker >= host.Speakers.Count || CombatFieldReader.Read(host.Speakers[speaker].Row).Fields is not { } fields) { Refuse("no data of the merchant"); return; }
        var name = AreaHoverIndex.Text(host.Speakers[speaker].Row, _hoverNames?.Catalog) ?? "merchant";
        if (AreaBarterMerchant.From(entity, name, fields, data, session.QueryCompanion(entity).LedByPc) is not { } merchant) { Refuse("the speaker is not an NPC"); return; }
        var inventory = _inventoryHost ?? new AreaInventoryHost([]);
        // ARC-WORLD-090: the substitute container of a merchant without a leader within 20 tiles of where it talked (BT4).
        var talker = host.Speakers[speaker];
        merchant = merchant.WithSubstitute(inventory.Maps.FirstOrDefault(map => map.TryGetRow(entity, out _))?.Substitute(entity),
            new(checked((int)talker.WorldX), checked((int)talker.WorldY)), data);
        var state = host.State;
        OpenBarterWindow(new AreaBarter(session, inventory, data, merchant, () => state.Reaction(speaker),
            delta => session.AdjustReaction(session.Version, entity, state.NpcFacts(), state.PcFactsFor(speaker), delta).Disposition == GameSessionDisposition.Changed,
            (item, kind) => inventory.Maps.Any(map => map.TryGetRow(item, out _)) ? inventory.Name(item, _hoverNames?.Catalog) : AreaInventoryHost.KindName(kind, _hoverNames?.Catalog)));
    }

    /// <summary>Shows the window over the area session for <paramref name="barter"/> (the dialog path above, or a self-test's model), after
    /// the open's session part (ARC-WORLD-090: the container's coins, the identification); a refused open shows no window.</summary>
    private void OpenBarterWindow(AreaBarter barter)
    {
        var opened = barter.Open();
        // ARC-WORLD-090: a substitute container an attack broke is gone (0x45F650 finds no object): the merchant trades from its own list.
        if (opened is { Disposition: GameSessionDisposition.Rejected, Failure: GameSessionFailure.UnknownHolder } && barter.Merchant.Container is not null)
        {
            barter = barter.WithoutContainer();
            opened = barter.Open();
        }
        if (opened.Disposition == GameSessionDisposition.Rejected)
        {
            _barterStatus = "open refused: " + opened.Failure;
            GD.Print($"ARC-UI-050 barter refused: open {opened.Failure}");
            RefreshAreaStatus();
            return;
        }
        // ARC-WORLD-096: the open schedules the shown object's restock: the substitute container, else the merchant (0x573042, RK9).
        ScheduleRestock(barter.Merchant.Container ?? barter.Merchant.Entity, "barter");
        ShowBarterWindow(barter, _areaSession!, opened, shop: false);
    }

    /// <summary>The window over <paramref name="session"/> for <paramref name="barter"/> after its open (<paramref name="opened"/>).
    /// ARC-UI-038: <paramref name="shop"/>, the opening shop's: the window sits above the shop's buttons, which stay the menu's, and has no
    /// Close of its own (the shop's Back closes it, OS1).</summary>
    private void ShowBarterWindow(AreaBarter barter, GameSessionOwner session, SessionBarterOpenResult opened, bool shop)
    {
        var merchant = barter.Merchant;
        _barter = barter;
        if (_barterPanel is null)
        {
            _barterPanel = new AreaBarterPanel(); AddChild(_barterPanel);
            _barterPanel.CloseRequested += CloseBarterByPlayer;
            _barterPanel.SellRequested += SellToMerchant;
            _barterPanel.BuyRequested += BuyFromMerchant;
            _barterPanel.DropRequested += BarterDrop;
            _barterPanel.PointerStill += BarterPointerStill;
            _barterPanel.HolderView = InventoryHolderView;
        }
        // A quick key held across the window must not act on its release afterwards (its press predates the window).
        QuarantineWorld(); _quickKeys.Reset();
        _barterMessage = null; _barterStatus = null; _barterSession = session; _barterInShop = shop;
        _barterPanel.Docked = shop;
        _mainMenuPanel?.SetDockedBelow(shop);
        // ARC-UI-064: the window in the world shows the original look when the game's art read; the open centres the game view on the PC
        // (0x5732D5 → 0x4B8CE0, IW33) at the current zoom, as the inventory window's. ARC-UI-069: the opening shop's docked window too (with
        // the main menu up the view is not centred, IW33).
        _barterPanel.SetArtLook(InventoryArtLook());
        if (!shop && _camera is not null)
        {
            var zoom = _worldFitView ? _worldPreferredZoom : _camera.State.Zoom;
            _worldFitView = false; _camera.ResetWorld(WorldAnchor, WorldViewport, zoom);
        }
        var view = _barter.View();
        _barterPanel.Open(view, null, BarterArt(view));
        _barterRevision = session.Version.Revision;
        GD.Print($"ARC-UI-050 barter open merchant={merchant.Entity} follower={merchant.Follower} multiplier={merchant.Multiplier} buys={merchant.BuyList.Status} fence={merchant.Fence} script={merchant.BuyScript} hero={view.Hero.Count} wares={view.Wares.Count} gold={view.HeroGold}/{view.MerchantGold} haggle={view.Haggle} reaction={view.Reaction}");
        GD.Print($"ARC-WORLD-090 barter stock container={merchant.Container ?? "none"} containerBuys={merchant.ContainerBuyList?.Status.ToString() ?? "-"} movedCoins={opened.Coins} movedPiles={opened.Piles} identified={opened.Identified}");
        RefreshReadyStatus();
    }

    private void SellToMerchant(string entity) => Trade(entity, sell: true);

    private void BuyFromMerchant(string entity) => Trade(entity, sell: false);

    private void Trade(string entity, bool sell)
    {
        if (!BarterOpen || _barter is null || !BarterActive) return;
        var trade = sell ? _barter.Sell(entity) : _barter.Buy(entity);
        _barterMessage = trade.Text;
        GD.Print($"ARC-UI-050 barter {(sell ? "sell" : "buy")} item={entity} moved={trade.Moved} count={trade.Count} total={trade.Total} reaction={trade.ReactionChange} failure={trade.Result.Failure} text={trade.Text}");
        if (trade.Moved)
        {
            if (_barterSounds.Count == 16) _barterSounds.Dequeue();
            _barterSounds.Enqueue(BarterTradeSound);
            PlayInterfaceSound(BarterTradeSound, "barter trade");
        }
        SyncAreaBarter(force: true);
    }

    // The player closed it (Escape, Cancel, the button); a teardown closes it the same way, silently (the original plays nothing).
    private void CloseBarterByPlayer() => EndBarter();

    private void EndBarter()
    {
        if (_barterPanel is null || !_barterPanel.IsOpen) return;
        GD.Print("ARC-UI-050 barter closed");
        _barterPanel.CloseView(); _barter = null; _barterSession = null; _barterRevision = -1;
        if (_barterInShop)
        {
            _barterInShop = false; _barterPanel.Docked = false; _mainMenuPanel?.SetDockedBelow(false);
            // ARC-UI-069: a session the start took is the game's now; its items' rows come from the game's maps.
            (_shopInventoryHost, _shopInventorySession) = (null, null);
        }
        QuarantineWorld(); RefreshReadyStatus();
    }

    /// <summary>Per frame: a pending dialog open; while open, a changed session rebuilds the window and a replaced or inactive one
    /// (a quick load, a map change) or a dead hero closes it.</summary>
    private void SyncAreaBarter(bool force = false)
    {
        TakeDialogBarter();
        if (!BarterOpen) { AdoptBarterData(); return; }
        if (!BarterActive || !_barterInShop && !ReferenceEquals(_barterSession, _areaSession) || !AreaBarter.HeroCanTrade(_barterSession!)) { EndBarter(); return; }
        var revision = _barterSession!.Version.Revision;
        if (!force && revision == _barterRevision) return;
        _barterRevision = revision;
        var view = _barter!.View();
        _barterPanel!.Render(view, _barterMessage, BarterArt(view));
    }

    /// <summary>ARC-UI-064: the original look's view of the window: the hero's side and the shown object's grid (its substitute container when
    /// it has one, BT4) from the inventory host, the merchant's image (0x553D10 with the merchant, not the container, BW2), the background
    /// by the follower rule, the merchant's name and the quote line (the last quote or trade message). ARC-UI-069: in the opening shop the
    /// shop session's, from the shop map's host and the kit's tables. Null without the art.</summary>
    private (AreaInventoryView Inventory, AreaBarterFrame Frame)? BarterArt(AreaBarterView view)
    {
        if (_barterInShop) return ShopBarterArt(view);
        if (_inventoryHost is null || _areaSession is not { } session || !ReferenceEquals(_barterSession, session)) return null;
        var merchant = view.Merchant;
        var inventory = InventoryView(merchant.Container ?? merchant.Entity) with { HolderImage = _inventoryHost.HolderImage(CurrentAreaMap, merchant.Entity) };
        var frame = new AreaBarterFrame(AreaBarterArtLayout.Background(merchant.Follower, session.QueryParty().Followers.Count), merchant.Name, _barterMessage ?? string.Empty);
        return (inventory, frame);
    }

    /// <summary>ARC-UI-064: an item released in the original look: the merchant's on the hero's grid is bought, the hero's on the merchant's grid
    /// sold (BT5; PROJECT: a bought item goes to the hero's first free cells, a stack trades whole); anything else goes back.</summary>
    private void BarterDrop(AreaInventoryDrop drop)
    {
        if (!BarterOpen || _barter is null || !BarterActive) return;
        GD.Print($"ARC-UI-064 barter drag kind={drop.Kind} item={drop.Held.Entity} cell={drop.GridSlot?.ToString() ?? "-"}");
        if (drop.Kind == AreaInventoryDropKind.Buy) Trade(drop.Held.Entity, sell: false);
        else if (drop.Kind == AreaInventoryDropKind.Sell) Trade(drop.Held.Entity, sell: true);
        else SyncAreaBarter(force: true);
    }

    /// <summary>ARC-UI-064: the pointer rested on an item of the original look: its quote goes on the quote line (PROJECT text, 0x578330 prints
    /// a generated phrase); resting on no item keeps the line.</summary>
    private void BarterPointerStill(string? item)
    {
        if (!BarterOpen || _barterPanel?.View is not { } view || item is null) return;
        var quote = AreaBarterArtLayout.Quote(view, item);
        if (quote.Length == 0 || quote == _barterMessage) return;
        // Only the quote line changes: no rebuild of the window's lists.
        _barterMessage = quote;
        _barterPanel.ShowQuote(quote);
    }

    /// <summary>While the window is open it owns every key: Escape closes it on a fresh press.</summary>
    private void HandleBarterKey(InputEventKey key)
    {
        // ARC-UI-038: in the opening shop the menu keeps its keys (Return accepts, Escape goes back and closes the window, OS1).
        if (_barterInShop) { HandleMainMenuKey(key); return; }
        var routed = key.Keycode != Key.None ? key.Keycode : key.PhysicalKeycode;
        if (key.Pressed && !key.Echo && routed == Key.Escape) CloseBarterByPlayer();
    }

    private void HandleBarterPulse(IntentFrame intent)
    {
        // ARC-UI-038: in the opening shop a controller moves over the shop's Accept and Back; the wares take the pointer.
        if (_barterInShop) { HandleMainMenuPulse(intent); return; }
        if (intent.Cancel) CloseBarterByPlayer();
        else if (intent.MenuPressedDirection != 0) _barterPanel!.Navigate(intent.MenuPressedDirection);
        else if (intent.Confirm) _barterPanel!.Confirm();
    }

    private string AreaBarterStatus() => !BarterActive ? "" : BarterOpen ? " | barter open: Escape closes"
        : _barterStatus is null ? "" : " | barter: " + _barterStatus;
}
