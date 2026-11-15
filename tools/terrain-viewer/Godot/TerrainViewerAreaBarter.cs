using Arcanum.Data.Area;
using Arcanum.Data.Combat;
using Arcanum.Data.Messages;
using Arcanum.Input;
using Arcanum.Session;
using Arcanum.Simulation.Items;
using Arcanum.Simulation.Reaction;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
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
    private bool BarterOpen => WindowOpen(InterfaceMode.Barter);
    private bool BarterActive => _barterInShop ? ShopBarterActive : _areaSession is not null && _readyMode == TerrainViewerMode.Area;

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

    /// <summary>ARC-DEV-297 (NR2): the hero's recognition of an NPC for the names the windows show (the hover's rule, read from the live session).</summary>
    private CritterRecognition WindowRecognition(AreaObjectRecord row) =>
        AreaHoverIndex.RecognitionFrom(row, entity => _areaSession?.QueryReactionRecord(entity));

    private void OpenBarter(AreaConversationHost host, string entity, int speaker)
    {
        var (barter, refusal) = BarterModel(host, entity, speaker, substitute: true, "ARC-UI-050 barter");
        if (barter is null) { _barterStatus = refusal; RefreshAreaStatus(); return; }
        OpenBarterWindow(barter);
    }

    /// <summary>The barter model of a speaker over the area session (the window's, and ARC-UI-1731 the item identify and repair windows' item
    /// facts); null with the reason when the open is refused. With <paramref name="substitute"/> the merchant's substitute container is found
    /// (ARC-WORLD-090, BT4), which only the barter window shows.</summary>
    private (AreaBarter? Barter, string? Refusal) BarterModel(AreaConversationHost host, string entity, int speaker, bool substitute, string tag)
    {
        var session = _areaSession!;
        (AreaBarter?, string?) Refuse(string why) { GD.Print($"{tag} refused: {why}"); return (null, why); }
        if (!AreaBarter.HeroCanTrade(session)) return Refuse("no living hero");
        if (_barterData is not { Data: { } data }) return Refuse($"no barter data ({_barterData?.Status}: {_barterData?.Detail})");
        if (speaker < 0 || speaker >= host.Speakers.Count || CombatFieldReader.Read(host.Speakers[speaker].Row).Fields is not { } fields) return Refuse("no data of the merchant");
        // ARC-DEV-297 (NR2): the window's name is the examine rule for the PC (0x441B60 at the open): the merchant's own description once it remembers the hero.
        var name = AreaHoverIndex.Text(host.Speakers[speaker].Row, _hoverNames?.Catalog, WindowRecognition(host.Speakers[speaker].Row)) ?? "merchant";
        if (AreaBarterMerchant.From(entity, name, fields, data, session) is not { } merchant) return Refuse("the speaker is not an NPC");
        var inventory = _inventoryHost ?? new AreaInventoryHost([]);
        // ARC-WORLD-090: the substitute container of a merchant without a leader within 20 tiles of where it talked (BT4).
        var talker = host.Speakers[speaker];
        if (substitute)
            merchant = merchant.WithSubstitute(inventory.Maps.FirstOrDefault(map => map.TryGetRow(entity, out _))?.Substitute(entity),
                new(checked((int)talker.WorldX), checked((int)talker.WorldY)), data);
        var state = host.State;
        return (new AreaBarter(session, inventory, data, merchant, () => state.Reaction(speaker),
            delta => session.AdjustReaction(session.Version, entity, state.NpcFacts(), state.PcFactsFor(speaker), delta).Disposition == GameSessionDisposition.Changed,
            (item, kind) => inventory.Maps.Any(map => map.TryGetRow(item, out _)) ? inventory.Name(item, _hoverNames?.Catalog) : AreaInventoryHost.KindName(kind, _hoverNames?.Catalog),
            // ARC-UI-1740: the merchant's Gambling skill from its sheet, and its generated lines for the bet (the gate's refusals, the confirm, the won and lost lines).
            merchantGambling: () => AreaFollowerSheet(entity) is { } sheet ? session.QueryNpcSkills(sheet.Loaded, sheet.Modifiers)[Arcanum.Simulation.Skills.Skill.Gambling] : null,
            npcLine: category => host.PickLine(host.Speakers[speaker], new(GamblingRules.LineRange(category).First, GamblingRules.LineRange(category).Last))), null);
    }

    /// <summary>Shows the window over the area session for <paramref name="barter"/> (the dialog path above, or a self-test's model), after
    /// the open's session part (ARC-WORLD-090: the container's coins, the identification); a refused open shows no window.</summary>
    private void OpenBarterWindow(AreaBarter barter)
    {
        // Before the session part of the open moves anything: another window on the stack refuses it.
        if (!WindowCanOpen(InterfaceMode.Barter)) return;
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
        if (!WindowCanOpen(InterfaceMode.Barter)) return;
        var merchant = barter.Merchant;
        _barter = barter;
        if (_barterPanel is null)
        {
            _barterPanel = new AreaBarterPanel(); AddChild(_barterPanel);
            _barterPanel.CloseRequested += CloseBarterByPlayer;
            _barterPanel.SellRequested += SellToMerchant;
            _barterPanel.BuyRequested += BuyFromMerchant;
            _barterPanel.DropRequested += BarterDrop;
            _barterPanel.GambleEntered += BarterGambleEntered;
            _barterPanel.PointerStill += BarterPointerStill;
            _barterPanel.ArrangeRequested += BarterArrange;
            _barterPanel.StatRequested += ShowStatLines;
            _barterPanel.CycleRequested += BarterCycle;
            _barterPanel.SoundRequested += id => PlayInterfaceSound(id, "barter arrange button");
            _barterPanel.HolderView = InventoryHolderView;
        }
        // A quick key held across the window must not act on its release afterwards (its press predates the window).
        QuarantineWorld(); _quickKeys.Reset();
        _barterMessage = null; _barterStatus = null; _barterSession = session; _barterInShop = shop;
        // ARC-DEV-339: the arrange button's layout flag is cleared at every open (ledger IW37).
        _arrangeLayout = InventoryArrangeLayout.Horizontal;
        _barterPanel.Docked = shop;
        _mainMenuPanel?.SetDockedBelow(shop);
        // ARC-UI-064: the window in the world shows the original look when the game's art read; the open centres the game view on the PC
        // (0x5732D5 → 0x4B8CE0, IW33) at the current zoom, as the inventory window's. ARC-UI-069: the opening shop's docked window too (with
        // the main menu up the view is not centred, IW33), when the shop's own strips and buttons can be drawn around it; else both stay
        // diagnostic (the docked diagnostic window keeps room for the shop's panel below it).
        _barterPanel.SetArtLook(!shop || _mainMenuPanel?.ShopArtSet == true ? InventoryArtLook() : null);
        if (!shop && _camera is not null)
        {
            var zoom = _worldFitView ? _worldPreferredZoom : _camera.State.Zoom;
            _worldFitView = false; _camera.ResetWorld(WorldAnchor, WorldViewport, zoom);
        }
        var view = _barter.View();
        var art = BarterArt(view);
        if (!OpenWindowMode(InterfaceMode.Barter, () => _barterPanel!.Open(view, null, art), () => _barterPanel!.CloseView()))
        {
            _barter = null; _barterSession = null; _barterInShop = false; _barterPanel.Docked = false; _mainMenuPanel?.SetDockedBelow(false);
            return;
        }
        _barterRevision = session.Version.Revision;
        GD.Print($"ARC-UI-050 barter open merchant={merchant.Entity} follower={merchant.Follower} multiplier={merchant.Multiplier} buys={merchant.BuyList.Status} fence={merchant.Fence} script={merchant.BuyScript} hero={view.Hero.Count} wares={view.Wares.Count} gold={view.HeroGold}/{view.MerchantGold} haggle={view.Haggle} reaction={view.Reaction}");
        GD.Print($"ARC-WORLD-090 barter stock container={merchant.Container ?? "none"} containerBuys={merchant.ContainerBuyList?.Status.ToString() ?? "-"} movedCoins={opened.Coins} movedPiles={opened.Piles} identified={opened.Identified}");
        RefreshReadyStatus();
    }

    private void SellToMerchant(string entity) => Trade(entity, sell: true);

    private void BuyFromMerchant(string entity) => Trade(entity, sell: false);

    private bool Trade(string entity, bool sell)
    {
        if (!BarterOpen || _barter is null || !BarterActive) return false;
        var trade = sell ? _barter.Sell(entity) : _barter.Buy(entity);
        _barterMessage = trade.Text;
        GD.Print($"ARC-UI-050 barter {(sell ? "sell" : "buy")} item={entity} moved={trade.Moved} count={trade.Count} total={trade.Total} reaction={trade.ReactionChange} failure={trade.Result.Failure} text={trade.Text}");
        if (trade.Moved) PlayBarterTradeSound();
        SyncAreaBarter(force: true);
        return trade.Moved;
    }

    private void PlayBarterTradeSound()
    {
        if (_barterSounds.Count == 16) _barterSounds.Dequeue();
        _barterSounds.Enqueue(BarterTradeSound);
        PlayInterfaceSound(BarterTradeSound, "barter trade");
    }

    // The player closed it (Escape, Cancel, the button); a teardown closes it the same way, silently (the original plays nothing).
    private void CloseBarterByPlayer() => EndBarter();

    private void EndBarter()
    {
        if (_barterPanel is null || !BarterOpen) return;
        GD.Print("ARC-UI-050 barter closed");
        CloseWindowMode(InterfaceMode.Barter); _barter = null; _barterSession = null; _barterRevision = -1;
        if (_barterInShop)
        {
            _barterInShop = false; _barterPanel.Docked = false; _mainMenuPanel?.SetDockedBelow(false);
            // ARC-UI-069: a session the start took is the game's now; its items' rows come from the game's maps (the art and sizes
            // read from the shop map's rows are read again).
            (_shopInventoryHost, _shopInventorySession) = (null, null);
            _inventoryItemArtCache.Clear(); _inventoryItemSizeCache.Clear();
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
        var inventory = WithMerchantDoll(InventoryView(merchant.Container ?? merchant.Entity) with { HolderImage = _inventoryHost.HolderImage(CurrentAreaMap, merchant.Entity) },
            merchant.Container is null ? null : InventoryView(merchant.Entity));
        var frame = new AreaBarterFrame(AreaBarterArtLayout.Background(merchant.Follower, session.QueryParty().Followers.Count), merchant.Name, _barterMessage ?? string.Empty);
        return (inventory, frame);
    }

    /// <summary>ARC-UI-1917 (IW29): the barter window's doll view shows the merchant's worn items and ratings (the window's redraw reads the merchant with the doll toggle set), even when the shown
    /// grid is its substitute container's: then the merchant's own view (<paramref name="merchantBody"/>) gives them; without one the shown view is the merchant's.</summary>
    internal static AreaInventoryView WithMerchantDoll(AreaInventoryView shown, AreaInventoryView? merchantBody) =>
        merchantBody is null ? shown : shown with { BodyDoll = merchantBody.BodyDoll, BodyRatings = merchantBody.BodyRatings };

    /// <summary>
    /// ARC-UI-1120 (BW6, 0x574FD0): a cycle button of a follower's window was released: the window switches to the previous (<paramref name="direction"/> 0)
    /// or next (1) follower of the PC's follower list, wrapping, skipping the ones that cannot be talked to (<see cref="BarterCycleAccepts"/>); the
    /// window closes and the other follower's opens through the pending open the dialog's <c>b:</c> reply uses, so the next frame shows it. No other
    /// acceptable follower leaves the window as it is.
    /// </summary>
    private void BarterCycle(int direction)
    {
        if (!BarterOpen || _barter is not { Merchant.Follower: true } barter || !BarterActive || _barterInShop || _barterSession is not { } session || _conversationHost is not { } host) return;
        AdmitCompanionSpeakers();
        var followers = session.QueryParty().Followers.Select(follower => follower.Entity).ToList();
        var chosen = AreaBarterCycle.Step(followers, barter.Merchant.Entity, direction == 1, entity => BarterCycleSpeaker(host, session, entity) is not null);
        GD.Print($"ARC-UI-1120 barter cycle direction={direction} from={barter.Merchant.Entity} to={chosen ?? "-"} followers={followers.Count}");
        if (chosen is null || BarterCycleSpeaker(host, session, chosen) is not { } speaker) return;
        EndBarter();
        _barterPending = (session, host, chosen, speaker.Index);
    }

    /// <summary>ARC-UI-1120 (0x575080): the speaker of a follower the cycle may switch to, or null. The original asks the can-speak test (0x4AD800; its
    /// head is <see cref="CanSpeakRules.Head"/>, its tail is not modelled), then refuses the critter flags 0x20000008 and accepts a critter whose spell
    /// flags do not carry bit 0x4000 (summoned); the viewer also asks that the follower is a speaker standing in the world (<see cref="SpeakerPresent"/>).</summary>
    private AreaSpeaker? BarterCycleSpeaker(AreaConversationHost host, GameSessionOwner session, string entity)
    {
        var speaker = host.Speakers.FirstOrDefault(candidate => AreaConversationState.EntityId(candidate.Row) == entity);
        if (speaker is null || !SpeakerPresent(speaker)) return null;
        var (critter, spell) = FollowerFlagWords(session, entity);
        var head = CanSpeakRules.Head(SpeakerCanSpeakFacts(session, speaker, entity));
        return head is { } answer && answer != CanSpeakRules.Free || (critter & 0x20000008) != 0 || (spell & 0x4000) != 0 ? null : speaker;
    }

    /// <summary>The critter flags and the spell flags of a follower now: the session's writes over the stored fields.</summary>
    private (uint Critter, uint Spell) FollowerFlagWords(GameSessionOwner session, string entity)
    {
        var fields = _combatHost?.Fields(entity);
        var written = session.QueryObjectFlags(entity);
        return (unchecked((uint)written.Critter(unchecked((int)(fields?.Critter?.Flags ?? 0)))),
            unchecked((uint)written.Spell(unchecked((int)(fields?.Object.SpellFlags ?? 0)))));
    }

    /// <summary>ARC-UI-1736 (FP8, FP11): what a follower's order menu draws greyed: Wait for a mind-controlled follower (spell flag 0x8000000), Inventory when the
    /// can-open test (0x575080, <see cref="BarterCycleSpeaker"/>) fails.</summary>
    private (bool MindControlled, bool CanOpenInventory) FollowerMenuFacts(string entity)
    {
        if (_areaSession is not { } session || _conversationHost is not { } host) return (false, false);
        AdmitCompanionSpeakers();
        var (_, spell) = FollowerFlagWords(session, entity);
        return ((spell & AreaFollowerPanelRules.MindControlledSpellFlag) != 0, BarterCycleSpeaker(host, session, entity) is not null);
    }

    /// <summary>ARC-UI-1736 (FP11, 0x56AB09–0x56AB67): the menu's Inventory row opens the follower's barter-style inventory (BW1, mode 1) when the can-open test
    /// passes; the window opens through the pending open the dialog's <c>b:</c> reply uses.</summary>
    private void OpenFollowerInventory(string entity)
    {
        if (_areaSession is not { } session || _conversationHost is not { } host || !BarterActive) return;
        AdmitCompanionSpeakers();
        if (BarterCycleSpeaker(host, session, entity) is not { } speaker) { _companionNotice = "inventory: the follower cannot be opened now"; RefreshReadyStatus(); return; }
        GD.Print($"ARC-UI-1736 follower inventory entity={entity}");
        _barterPending = (session, host, entity, speaker.Index);
    }

    /// <summary>ARC-DEV-339 (B54): the arrange button of the barter window (IW30) lays the hero's items out as the own window's does; the
    /// merchant's grid is not arranged (PROJECT: the shown wares have no grid slots here; the original arranges a shop NPC's, 0x6810FC).</summary>
    private void BarterArrange()
    {
        if (!BarterOpen || _barter is null || !BarterActive || _barterSession is null) return;
        _barterMessage = ArrangeHero(_barterSession);
        SyncAreaBarter(force: true);
    }

    /// <summary>ARC-UI-064: an item released in the original look: the merchant's on the hero's grid is bought, the hero's on the merchant's grid
    /// sold (BT5; PROJECT: a bought item goes to the hero's first free cells, a stack trades whole); anything else goes back.</summary>
    private void BarterDrop(AreaInventoryDrop drop)
    {
        if (!BarterOpen || _barter is null || !BarterActive) return;
        GD.Print($"ARC-UI-064 barter drag kind={drop.Kind} item={drop.Held.Entity} cell={drop.GridSlot?.ToString() ?? "-"}");
        if (drop.Kind == AreaInventoryDropKind.Buy) Trade(drop.Held.Entity, sell: false);
        else if (drop.Kind == AreaInventoryDropKind.Sell) Trade(drop.Held.Entity, sell: true);
        else if (drop.Kind == AreaInventoryDropKind.Gamble) Gamble(drop.Held.Entity);
        // ARC-UI-1925: the wear moves on the hero's doll (a ring to the other slot, the merchant's item bought and worn).
        else if (drop.Kind is AreaInventoryDropKind.Wear or AreaInventoryDropKind.BuyWear or AreaInventoryDropKind.SellWear) BarterWearDrop(drop);
        else SyncAreaBarter(force: true);
    }

    /// <summary>
    /// ARC-UI-1740 (BW3): an item held over the bet box entered it: the bet gate (0x579840) runs once and writes its line on the quote line (the merchant's
    /// refusal or its confirm line with the price); the box shows art 344 when it passes and 346 when it does not.
    /// </summary>
    private void BarterGambleEntered(AreaInventoryHeld held)
    {
        if (!BarterOpen || _barter is null || _barterPanel is null || !BarterActive) return;
        var check = _barter.CheckGamble(held.Entity);
        _barterMessage = check.Text;
        _barterPanel.ShowQuote(check.Text);
        _barterPanel.SetGambleGate(check.Allowed);
        GD.Print($"ARC-UI-1740 barter gamble gate item={held.Entity} allowed={check.Allowed} refusal={check.Gate.Refusal} price={check.Price} text={check.Text}");
    }

    /// <summary>ARC-UI-1740 (0x579B60): the merchant's item released on the bet box is bet. A refused gate says nothing (the hover's line stays); a settled bet
    /// prints its line, asks the trade sound (0x578EA0, SN31) and rebuilds the window.</summary>
    private void Gamble(string entity)
    {
        if (!BarterOpen || _barter is null || !BarterActive) return;
        var result = _barter.Gamble(entity);
        if (result.Bet is null)
        {
            GD.Print($"ARC-UI-1740 barter gamble item={entity} refused by the gate: {result.Gate.Refusal}");
            SyncAreaBarter(force: true);
            return;
        }
        _barterMessage = result.Text;
        GD.Print($"ARC-UI-1740 barter gamble item={entity} settled={result.Settled} won={result.Won} paid={result.Paid} reaction={result.ReactionChange} failure={result.Bet.Failure} roll={result.Bet.Check?.Check.Roll.ToString() ?? "-"} effectiveness={result.Bet.Check?.Check.Effectiveness.ToString() ?? "-"} fate={result.Bet.FateUsed} text={result.Text}");
        if (result.Settled)
        {
            if (_barterSounds.Count == 16) _barterSounds.Dequeue();
            _barterSounds.Enqueue(BarterTradeSound);
            PlayInterfaceSound(BarterTradeSound, "barter gamble");
        }
        SyncAreaBarter(force: true);
    }

    /// <summary>ARC-UI-064: the pointer rested on an item of the original look: its quote goes on the quote line (PROJECT text, 0x578330 prints
    /// a generated phrase); resting on no item keeps the line. ARC-UI-1120 (HK25): the window's still-pointer handler is the inventory's (0x573C74,
    /// mode 1 among its modes), which first describes the item for the window's holder, the PC (0x57CCF0: the item's examine script, then the item
    /// window in the message mode, <see cref="DescribeItem"/>), and only then, in mode 1, makes the quote; an examine that answers skip default leaves
    /// no description and the quote is made all the same.</summary>
    private void BarterPointerStill(string? item)
    {
        if (!BarterOpen || _barterPanel?.View is not { } view || item is null) return;
        if (_areaSession is { } session && InterfaceBarsOn)
        {
            // The viewer hides the bars while the window is open (PROJECT: its message window is not there), so the examine script runs and the window
            // is drawn only when the bars show (known gap: the bars are normally hidden under the barter window, so only the script runs); the script's effects
            // and printed lines are the game's either way. PROJECT: the interface-on test is the viewer's (the original's still-pointer branch has no 0x5578C0 test),
            // so that an automated run without bars stays quiet.
            _hotkeyDescriptionsForSelfTest++;
            if (_gameBarPanel is { Visible: true }) DescribeItem(session, item);
            else if (InventoryItemKind(session, item) is not null) ItemExamineAllows(session, item, out _);
        }
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
        // ARC-DEV-111: Escape opens the menu (kind 3), which closes the window; the window has no key of its own.
    }

    private void HandleBarterPulse(IntentFrame intent)
    {
        // ARC-UI-038: in the opening shop a controller moves over the shop's Accept and Back; the wares take the pointer.
        if (_barterInShop) { HandleMainMenuPulse(intent); return; }
        if (intent.Cancel) CloseBarterByPlayer();
        else if (intent.MenuPressedDirection != 0) _barterPanel!.Navigate(intent.MenuPressedDirection);
        else if (intent.Confirm) _barterPanel!.Confirm();
    }

    private string AreaBarterStatus() => !BarterActive ? "" : BarterOpen ? " | barter open"
        : _barterStatus is null ? "" : " | barter: " + _barterStatus;
}
