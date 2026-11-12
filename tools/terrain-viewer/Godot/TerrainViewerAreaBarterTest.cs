using System.Reflection;
using Arcanum.Data.Area;
using Arcanum.Data.Characters;
using Arcanum.Data.Combat;
using Arcanum.Data.Messages;
using Arcanum.Session;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Combat;
using Arcanum.TerrainViewer.Core;
using Arcanum.TerrainViewer.Tests;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

public static partial class TerrainViewerSelfTest
{
    /// <summary>ARC-UI-050 over the strict synthetic AREA owner in the production app: with a synthetic hero and a merchant of a
    /// synthetic seed, the barter window opens modal with both sides' quotes, owns the keys, a sale and a buy move items and coins
    /// through the session (trade sound 8158), Escape closes, and a replaced session without a hero closes it.</summary>
    private static async Task AssertAreaBarterAsync(Node host)
    {
        using var source = new AreaOwnerFixture(motionBodies: true);
        var read = typeof(TerrainLoadCoordinator).Assembly.GetType("Arcanum.World.AreaLoadOperation")!
            .GetMethod("ReadArea", BindingFlags.NonPublic | BindingFlags.Static)!;
        var area = (AreaLoadResult)read.Invoke(null, [7L, source.Owner, CancellationToken.None, null])!;
        var steps = AreaOwnerFixture.Steps(area.Snapshot, source.Streams[new(AreaSourceRole.Module)].Bytes, 7);
        source.Owner.Dispose();
        var result = SyntheticActorFixture.Create(human: true, area: true).Result with { Generation = 7, Area = area, AreaStepSnapshot = steps };
        var arguments = TerrainViewerArguments.Parse(["--mode", "crash-site-area", "--module", "synthetic-module",
            "--resource", "synthetic-terrain", "--actor-resource", "synthetic-actor", "--prototypes", "synthetic-prototypes",
            "--area-mobile", "synthetic-mobile", "--area-effects", "synthetic-effects", "--area-patch", "synthetic-patch", "--source-id", "synthetic"]);
        var viewport = new SubViewport { Size = new(800, 600), TransparentBg = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
        var app = new TerrainViewerApp { SelfTestBootstrap = true };
        viewport.AddChild(app); host.AddChild(viewport);
        await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
        app.SetProcess(false); app.SetProcessInput(false);
        string summary;
        try
        {
            app.PublishSyntheticForSelfTest(arguments, result);
            await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
            summary = await app.AreaBarterForSelfTestAsync();
        }
        finally
        {
            app.ClearSyntheticForSelfTest();
            viewport.Free();
        }
        GD.Print($"ARC-UI-050 self-test PASS synthetic barter ({summary}); original data covered by the native smoke");
    }
}

public partial class TerrainViewerApp
{
    internal async Task<string> AreaBarterForSelfTestAsync()
    {
        static void Require(bool value, string why) { if (!value) throw new InvalidOperationException("AREA barter: " + why); }
        if (!SelfTestBootstrap || _readyArea is null || _areaSession is null) throw new InvalidOperationException("AREA_BARTER_READY_REQUIRED");
        var simulatedFocus = _worldFocusSimulationForSelfTest;
        _worldFocusSimulationForSelfTest = true; _worldFocused = true;
        try
        {
            // A synthetic hero (1000 gold, one kit sword of worth 100) through the production replacement path, as a quick load does,
            // and a merchant (an axe of worth 400, a worn vest, 100 coins) whose set buys the sword's basic number.
            const int Sword = 4030, Axe = 4040;
            var session = _areaSession;
            var hero = new SessionHeroDefinition(16089, CharacterRace.HalfElf, CharacterGender.Male, [8, 8, 8, 8, 8, 8, 8, 8, 0, 0], 1, 5, 0,
                null, null, 1000, [new SessionHeroKitItem(new(SessionItemType.Weapon, Sword, 4100, 34100))]);
            var actor = session.QueryActor();
            var created = AreaTransitionCoordinator.CreateSession(session.QueryClockSnapshot().ProfileBinding, Guid.NewGuid(), Guid.NewGuid(),
                new(actor.Cell.X, actor.Cell.Y), actor.Map.RuntimeMap,
                new AreaHeroLoad(AreaHeroStatus.Ready, AreaStartingHero.Find("half-elf-male")!, hero, 2352, 0, null, "synthetic"));
            var owner = created.Owner ?? throw new InvalidOperationException("AREA barter: session " + created.Failure);
            ReplaceAreaSession(owner, AreaRuntimes(), actor.Map.RuntimeMap, _conversationHost?.Rebind(owner));
            SessionItemFacts Facts(SessionItemType type, int prototype, int location = 0, int? quantity = null) => new(new(type, prototype, 4200, 34200), 10, 0, location, quantity, []);
            var seed = new SessionMapInventorySeed([new("merchant", SessionHolderKind.Npc, false, false, [], ["axe", "vest", "coins"])],
                [new("axe", Facts(SessionItemType.Weapon, Axe), null), new("vest", Facts(SessionItemType.Weapon, Axe, location: 1006), null),
                 new("coins", Facts(SessionItemType.Gold, AreaBarterData.GoldPrototype, quantity: 100), null)]);
            Require(owner.AttachMapInventory(actor.Map, seed).Failure == GameSessionFailure.None, "the merchant's seed attached");
            static CombatFields Weapon(int prototype, int worth) =>
                new(CombatFieldOrigin.Prototype, new CombatObjectFields((int)SessionItemType.Weapon, prototype, 0, 0, 0, 0, 0, 40, 0, 0, 4, [0, 0, 0, 0, 0], 0),
                    new CombatItemFields(0, 10, worth, 0, 0, 0, 0, [], 0, 0, 0, 0), null, null, null, null, null, null);
            static MesFile Mes(string text) => MesFile.Parse(System.Text.Encoding.ASCII.GetBytes(text)).File!;
            var data = AreaBarterData.Create(Mes("{1}{Smith: 1,1}{2}{End: 1,1}"), Mes("{1}{4010 4020}"),
                new Dictionary<int, CombatFields> { [Sword] = Weapon(Sword, 100), [Axe] = Weapon(Axe, 400) });
            var merchant = new AreaBarterMerchant("merchant", "Smith", 100, false, false, data.BuyList(1), false);
            var changes = new List<int>();
            AreaBarter Model(GameSessionOwner on) => new(on, _inventoryHost ?? new AreaInventoryHost([]), data, merchant, () => 50,
                delta => { changes.Add(delta); return true; }, (entity, _) => entity);
            _barterSounds.Clear();

            OpenBarterWindow(Model(owner));
            var view = _barterPanel!.View!;
            Require(BarterOpen && WorldModal && WindowPanelsMatchStack && !_worldGate.IsOpen, "the window is modal");
            Require(view is { HeroGold: 1000, MerchantGold: 100, Haggle: 10, Training: SkillTraining.None } && view.Hero.Single().Quote.Price == 55
                && view.Wares.Single() is { Entity: "axe", Quote.Price: 760 }, "quotes: sell 55, buy 760");
            Require(GetSelfTestSnapshot().Hud.Contains(" | barter open", StringComparison.Ordinal), "HUD open status");
            // ARC-DEV-111: the sheet key is a global key (it replaces the open window); the logbook test covers the move.

            _barterPanel.SellButtonsForSelfTest[0].EmitSignal(BaseButton.SignalName.Pressed);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Require(owner.QueryGold() == 1055 && owner.QueryNpcGold("merchant") == 45 && _barterSounds.Last() == BarterTradeSound
                && _barterPanel.MessageForSelfTest == "sold kit:1 for 55", "the sale moved the sword and 55 coins: " + _barterPanel.MessageForSelfTest);
            Require(_barterPanel.View!.Hero.Count == 0 && _barterPanel.View.Wares.Count == 2, "the sword is now a ware");
            _barterPanel.BuyButtonsForSelfTest.Single(button => button.Text.StartsWith("Buy: axe", StringComparison.Ordinal)).EmitSignal(BaseButton.SignalName.Pressed);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Require(owner.QueryGold() == 295 && owner.QueryNpcGold("merchant") == 805 && changes.Count == 0, "the buy moved the axe and 760 coins");
            var sounds = _barterSounds.Count;
            Require(_barterPanel.View!.Hero.Single(line => line.Entity == "axe").Quote.Price > 0, "the axe sells back");
            Require(_barterPanel.BuyButtonsForSelfTest.Single().Text.Contains("kit:1", StringComparison.Ordinal), "the sword can be bought back");
            Require(_barterSounds.Count == sounds, "no sound without a move");
            var art = await AreaBarterArtForSelfTestAsync(owner, Model);
            // The original look's part closed the window through the round view; the diagnostic window opens again for the keys.
            OpenBarterWindow(Model(owner));

            // ARC-DEV-111: the window has no Escape of its own: the menu Escape opens (kind 3, the barter's dialog is still active in the
            // original) closes the barter (the inventory family); Escape again continues play.
            InventoryKey(Key.Escape);
            Require(!BarterOpen && MainMenuOpen && _mainMenu!.Window == AreaMenuWindow.InPlayLimited && WindowPanelsMatchStack, "Escape opened the limited menu, which closed the barter");
            InventoryKey(Key.Escape);
            Require(!BarterOpen && !MainMenuOpen && !WorldModal, "Escape continued play");
            OpenBarterWindow(Model(owner));
            var replacement = AreaTransitionCoordinator.CreateSession(owner.QueryClockSnapshot().ProfileBinding, Guid.NewGuid(), Guid.NewGuid(),
                new(actor.Cell.X, actor.Cell.Y), actor.Map.RuntimeMap).Owner!;
            ReplaceAreaSession(replacement, AreaRuntimes(), actor.Map.RuntimeMap, _conversationHost?.Rebind(replacement));
            SyncAreaBarter();
            Require(!BarterOpen, "a replaced session closes the window");
            Require(!AreaBarter.HeroCanTrade(replacement), "a session without a hero cannot trade");
            return "sell 55, buy 760, keys, close, " + art + ", replaced session";
        }
        finally
        {
            if (BarterOpen) EndBarter();
            _worldFocusSimulationForSelfTest = simulatedFocus;
        }
    }

    /// <summary>ARC-UI-064 over a synthetic art set (blank arts of the original sizes): the window opens in the original look (mode 1, background
    /// 235, the merchant's name), a resting pointer puts a ware's quote on the quote line, the hero's item dragged onto the merchant's grid is
    /// sold and the merchant's dragged onto the hero's grid bought (coins and the trade sound as the buttons'), a right release on the hero's
    /// item sells it (IW36), a ware released back on its grid changes nothing, the switch shows the diagnostic window and back, and a release on the round view closes the window.</summary>
    private async Task<string> AreaBarterArtForSelfTestAsync(GameSessionOwner owner, Func<GameSessionOwner, AreaBarter> model)
    {
        static void Require(bool value, string why) { if (!value) throw new InvalidOperationException("ARC-UI-064 barter art: " + why); }
        static Vector2 Ware(int location) => new(AreaBarterArtLayout.GridX + (location % 10 + .5f) * AreaBarterArtLayout.CellPixels,
            AreaBarterArtLayout.GridY + (location / 10 + .5f) * AreaBarterArtLayout.CellPixels);
        static Texture2D Blank(int width, int height) => ImageTexture.CreateFromImage(Image.CreateEmpty(width, height, false, Image.Format.Rgba8));
        if (BarterOpen) CloseBarterByPlayer();
        var host = _inventoryHost;
        _inventoryHost ??= new AreaInventoryHost([]);
        _inventoryArtLookForSelfTest = new(AreaInventoryArtContent.Numbers.ToDictionary(number => number, number => number switch
        {
            AreaBarterArtLayout.BarterArt or AreaBarterArtLayout.FollowerArt or AreaInventoryArtLayout.DollArt or AreaLootArtLayout.LootArt => Blank(358, 400),
            AreaInventoryArtLayout.PackArt => Blank(442, 400),
            _ => Blank(80, 43),
        }), _ => null, AreaInventoryTexts.Project("self-test")) { LootReady = true, BarterReady = true };
        try
        {
            OpenBarterWindow(model(owner));
            var panel = _barterPanel!;
            var area = panel.GridAreaForSelfTest;
            Require(panel.ArtMode && area.BarterLook && area.LookMode == AreaInventoryMode.Barter && area.BarterFrame is { Background: 235, Name: "Smith" },
                "the original look opened in mode 1 with background 235");
            Require(panel.LookToggleForSelfTest is { Visible: true, Text: "Diagnostic view" } && !panel.SellButtonsForSelfTest[0].IsVisibleInTree(), "the switch below the frame");
            var view = panel.ArtView!;
            var sword = view.LootGrid.Single(item => item.Entity == "kit:1");
            var axe = view.Grid.Single(item => item.Entity == "axe");
            Require(!view.LootGrid.Any(item => item.Entity == "vest"), "the worn vest is not in the grid");
            // ARC-UI-1120 (HK25): the still pointer describes the ware first (0x57CCF0: the examine script, then the item window), then quotes it (0x578330).
            var described = _hotkeyDescriptionsForSelfTest;
            area.StillAt(Ware(sword.Location));
            Require(_hotkeyDescriptionsForSelfTest == described, "with the interface off (the synthetic run) the resting pointer describes nothing (0x5578C0)");
            area.ResetStill();
            var bars = _gameBarForSelfTest;
            _gameBarForSelfTest = true;
            try
            {
                area.StillAt(Ware(sword.Location));
                Require(_hotkeyDescriptionsForSelfTest == described + 1, "the resting pointer described the ware for the hero");
                area.StillAt(Ware(sword.Location));
                Require(_hotkeyDescriptionsForSelfTest == described + 1, "resting on the same ware again describes nothing new");
            }
            finally { _gameBarForSelfTest = bars; _messageLog.ClearHover(); }
            Require(area.BarterFrame!.Quote.StartsWith("Buy kit:1: ", StringComparison.Ordinal), "the resting pointer quotes the ware: " + area.BarterFrame.Quote);

            // ARC-UI-1120 (BW6): the cycle buttons are made on the follower background only, and a release over one asks for the previous or next follower.
            Require(!area.CycleShown && area.CycleAt(new(AreaBarterArtLayout.CycleLeft.X + 5, AreaBarterArtLayout.CycleLeft.Y + 5)) is null, "a merchant's window has no cycle buttons");
            var cycles = new List<int>();
            area.CycleRequested += cycles.Add;
            var plain = area.BarterFrame!;
            area.SetBarter(plain with { Background = AreaBarterArtLayout.FollowerArt });
            var (left, right) = (new Vector2(AreaBarterArtLayout.CycleLeft.X + 5, AreaBarterArtLayout.CycleLeft.Y + 5), new Vector2(AreaBarterArtLayout.CycleRight.X + 5, AreaBarterArtLayout.CycleRight.Y + 5));
            Require(area.CycleShown && area.CycleAt(left) == 0 && area.CycleAt(right) == 1, "the follower background shows both cycle buttons");
            var buttonSounds = new List<int>();
            area.SoundRequested += buttonSounds.Add;
            area.PressAt(right); area.ReleaseAt(right);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            area.PressAt(left); area.ReleaseAt(new(right.X, right.Y));
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            area.PressAt(left); area.ReleaseAt(left);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Require(cycles.SequenceEqual([1, 0]), "a release over the pressed button cycles (next, then previous); a release elsewhere does not: " + string.Join(",", cycles));
            Require(buttonSounds.Count(id => id == AreaGameBarLayout.PressSound) == 3 && buttonSounds.Count(id => id == AreaGameBarLayout.ReleaseSound) == 2, "press and release sounds: " + string.Join(",", buttonSounds));
            area.SetBarter(plain);
            Require(!area.CycleShown, "the merchant background takes the buttons away");

            var revision = owner.Version.Revision;
            area.PressAt(Ware(sword.Location)); area.ReleaseAt(Ware(69));
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Require(owner.Version.Revision == revision && area.Held is null, "a ware released on its own grid goes back");

            var (gold, merchantGold, sounds) = (owner.QueryGold(), owner.QueryNpcGold("merchant") ?? 0, _barterSounds.Count);
            var sale = panel.View!.Hero.Single(line => line.Entity == "axe").Quote.Price;
            area.PressAt(AreaInventoryGridArea.CellCentre(axe.Slot)); area.ReleaseAt(Ware(69));
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var paid = Math.Min(sale, merchantGold);
            Require(owner.QueryGold() == gold + paid && panel.ArtView!.LootGrid.Any(item => item.Entity == "axe") && _barterSounds.Count == sounds + 1,
                $"the axe dragged onto the merchant's grid sold for {paid}: {area.BarterFrame!.Quote}");
            Require(area.BarterFrame!.Quote == $"sold axe for {paid}", "the trade message is on the quote line: " + area.BarterFrame.Quote);

            gold = owner.QueryGold();
            var price = panel.View!.Wares.Single(line => line.Entity == "kit:1").Quote.Price;
            Require(price > 0 && price <= gold, $"the sword is affordable ({price} of {gold})");
            var ware = panel.ArtView!.LootGrid.Single(item => item.Entity == "kit:1");
            area.PressAt(Ware(ware.Location)); area.ReleaseAt(AreaInventoryGridArea.CellCentre(119));
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Require(owner.QueryGold() == gold - price && panel.ArtView!.Grid.Any(item => item.Entity == "kit:1") && _barterSounds.Count == sounds + 2,
                $"the sword dragged onto the hero's grid was bought for {price}: {area.BarterFrame!.Quote}");

            // A right release on the hero's sword moves it to the merchant's side: a sale (IW36).
            gold = owner.QueryGold();
            var resale = Math.Min(panel.View!.Hero.Single(line => line.Entity == "kit:1").Quote.Price, owner.QueryNpcGold("merchant") ?? 0);
            area.RightReleaseAt(AreaInventoryGridArea.CellCentre(panel.ArtView!.Grid.Single(item => item.Entity == "kit:1").Slot));
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Require(resale > 0 && owner.QueryGold() == gold + resale && panel.ArtView!.LootGrid.Any(item => item.Entity == "kit:1") && _barterSounds.Count == sounds + 3,
                $"the right release sold the sword for {resale}: {area.BarterFrame!.Quote}");

            panel.LookToggleForSelfTest.EmitSignal(BaseButton.SignalName.Pressed);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Require(!panel.ArtMode && panel.BuyButtonsForSelfTest.Count > 0 && panel.BuyButtonsForSelfTest[0].IsVisibleInTree(), "the switch shows the diagnostic window");
            panel.LookToggleForSelfTest.EmitSignal(BaseButton.SignalName.Pressed);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Require(panel.ArtMode, "and the original look again");

            var (holeX, holeY) = AreaInventoryHole.Corner(AreaInventoryMode.Barter);
            area.ReleaseAt(new(holeX + 10, holeY + 10));
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Require(!BarterOpen, "a release on the round view closed the window");
            return $"original look: quote, sell {paid} and buy {price} by drag, right-release sale {resale}, switch, round-view close";
        }
        finally
        {
            if (BarterOpen) EndBarter();
            _inventoryArtLookForSelfTest = null;
            _inventoryHost = host;
        }
    }

    /// <summary>
    /// ARC-WORLD-096 native observation (stage `barter`, after the barter part, on the live session): the open scheduled the merchant's
    /// substitute container (its inventory source); the game time jumps to the event with the container on screen (the event is
    /// scheduled again), then, with the camera moved until the container is off screen, to the new event: the merchant regenerates when
    /// it has a source of its own and the container is cleared and generated again; the window opened again shows the new wares.
    /// Prints counts and outcomes only. SKIP for a merchant without a container or without lighting (no event pump).
    /// </summary>
    private async Task RunAreaRestockSmokeAsync(AreaConversationHost talk, string merchant, int speaker, AreaBarterView before)
    {
        static void Require(bool value, string why) { if (!value) throw new InvalidOperationException("ARC-WORLD-096 restock smoke: " + why); }
        if (_areaSession is not { } session || before.Merchant.Container is not { } container) { GD.Print("ARC-WORLD-096 native-smoke restock SKIP (the merchant has no substitute container)"); return; }
        if (_areaAmbient is not { Events: not null } driver || _camera is null) { GD.Print("ARC-WORLD-096 native-smoke restock SKIP (no event pump)"); return; }
        var host = RestockHostNow();
        Require(host is not null, "the restock host is ready");
        var map = new SessionMapId(CurrentAreaMap);
        var key = _inventoryHost!.Maps.Single(m => m.RuntimeMap == map.RuntimeMap).Row(container).Key;
        Require(_canvas!.AreaBodyRect(key) is not null, "the container draws a body");
        Require(session.QueryRestocks().Any(e => e.Holder == container), "the open scheduled the container (RK2, RK9)");
        var zoom = _camera.State.Zoom;
        // Moves the camera by the production manual pan until the container's screen test answers as wanted.
        bool Camera(bool onScreen)
        {
            var body = _canvas.AreaBodyRect(key)!.Value;
            var centre = body.GetCenter();
            for (var attempt = 0; attempt < 12 && host!.OnScreen(map, container) != onScreen; attempt++)
            {
                // Farther in each direction in turn; the zoom stays (the merchant search's area does not follow it, but the view does).
                var far = onScreen ? centre : centre + new Vector2(attempt % 2 == 0 ? 4000 : -4000, attempt % 4 < 2 ? 3000 : -3000) * (1 + attempt / 4);
                _camera.UpdateWorld(WorldAnchor, default, new GamepadLab.Core.Vec2(_camera.State.X - far.X, _camera.State.Y - far.Y), false, true, WorldViewport);
                _camera.Camera.ForceUpdateScroll();
            }
            return host!.OnScreen(map, container) == onScreen;
        }
        SessionRestockFiring Wait()
        {
            var due = session.QueryRestocks().Single(e => e.Holder == container).DeadlineMs - session.QueryClockSnapshot().GameTimestampMs;
            driver.AdvanceMilliseconds(Math.Max(1, due), AmbientSector());
            LogAreaEvents(driver);
            return driver.Events!.Last.Restocks.Single(f => f.Holder == container);
        }
        try
        {
            string first;
            if (Camera(onScreen: true))
            {
                var waited = Wait();
                Require(waited.Decision.Step == Arcanum.Simulation.Items.RestockStep.Reschedule && session.QueryRestocks().Any(e => e.Holder == container),
                    "on screen the event is scheduled again: " + waited.Decision.Step);
                first = "on screen: rescheduled";
            }
            else first = "could not show the container: on-screen half skipped";
            Require(Camera(onScreen: false), "the camera could not leave the container off screen");
            var fired = Wait();
            Require(fired is { Postponed: false, Decision.Step: Arcanum.Simulation.Items.RestockStep.RegenerateMerchantAndContainer },
                $"off screen it regenerates through its merchant: {fired.Decision.Step} postponed {fired.Postponed}");
            var done = fired.Regenerations.Single(r => r.Holder == container);
            Require(done.Outcome == SessionRestockOutcome.Regenerated && session.QueryRestocks().All(e => e.Holder != container), "the container regenerated: " + done.Outcome);
            _camera.ResetWorld(WorldAnchor, WorldViewport, zoom); _camera.Camera.ForceUpdateScroll();
            QueueDialogBarter(talk, (merchant, speaker));
            TakeDialogBarter();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Require(BarterOpen && _barterPanel!.View is { } again && again.Merchant.Entity == merchant, "the window opened again: " + _barterStatus);
            var after = _barterPanel!.View!;
            CloseBarterByPlayer();
            Require(!BarterOpen, "the player's close closed it");
            GD.Print("ARC-WORLD-096 native-smoke restock PASS " + System.Text.Json.JsonSerializer.Serialize(new {
                First = first,
                Fired = $"{fired.Decision.Step}, merchant regenerates {fired.Decision.MerchantRegenerates}",
                Container = $"removed {done.Removed}, coins {done.Coins}, created {done.Created.Count}, refused {done.Refused}, skipped {done.Skipped}",
                Wares = $"{before.Wares.Count} before, {after.Wares.Count} after; merchant gold {before.MerchantGold} before, {after.MerchantGold} after",
                Rescheduled = session.QueryRestocks().Any(e => e.Holder == container) }));
        }
        finally { _camera.ResetWorld(WorldAnchor, WorldViewport, zoom); _camera.Camera.ForceUpdateScroll(); }
    }

    /// <summary>
    /// ARC-WORLD-103 native observation (stage `barter`, after the barter part, on the live session): the first living NPC of the shown
    /// map's seed with an inventory source of its own whose set the project admits and with combat values gets its restock event through the window open's schedule
    /// (<see cref="AreaRestockHost.Schedule"/>); the game time jumps to it, the NPC regenerates and then wields its best items through the
    /// production event world (its combat values from the area's combat fields, its items' wear facts from their rows or prototypes), and
    /// the session's list shows the worn slots its record names. Prints counts and slots only. SKIP when the map has no such NPC.
    /// </summary>
    private void RunAreaNpcRedressSmoke()
    {
        static void Require(bool value, string why) { if (!value) throw new InvalidOperationException("ARC-WORLD-103 redress smoke: " + why); }
        if (_areaSession is not { } session || _areaAmbient is not { Events: not null } driver || _inventoryHost is not { } inventory || _combatHost is not { } combat)
        { GD.Print("ARC-WORLD-103 native-smoke redress SKIP (no session, event pump or combat host)"); return; }
        var host = RestockHostNow();
        Require(host is not null, "the restock host is ready");
        var map = inventory.Maps.Single(m => m.RuntimeMap == CurrentAreaMap);
        // A set the project does not admit keeps the list (Unsupported): only an NPC whose set reads regenerates.
        var npc = map.Seed.Holders.Where(holder => holder.Kind == SessionHolderKind.Npc).Select(holder => holder.Entity)
            .FirstOrDefault(entity => map.RestockSeed(entity).InventorySource is var source and not 0 && host!.Set(source).Status != SessionRestockSetStatus.Unsupported &&
                !session.QueryNpcDead(entity) && !session.QueryCompanion(entity).LedByPc && combat.Critter(CurrentAreaMap, entity) is not null &&
                !session.QueryRestocks().Any(e => e.Holder == entity));
        if (npc is null) { GD.Print("ARC-WORLD-103 native-smoke redress SKIP (no living NPC with a readable source of its own on this map)"); return; }
        Require(AreaRestockHost.Schedule(session, inventory, npc)?.Disposition == GameSessionDisposition.Changed, "the window open's schedule set the NPC's event");
        var before = session.QueryMapInventory(new(CurrentAreaMap))!.Holders.Single(h => h.Entity == npc).Items.Count(item => item.Facts.Worn);
        var due = session.QueryRestocks().Single(e => e.Holder == npc).DeadlineMs - session.QueryClockSnapshot().GameTimestampMs;
        driver.AdvanceMilliseconds(Math.Max(1, due), AmbientSector());
        LogAreaEvents(driver);
        var fired = driver.Events!.Last.Restocks.Single(f => f.Holder == npc);
        Require(fired is { Postponed: false, Decision.Step: Arcanum.Simulation.Items.RestockStep.Regenerate }, $"the NPC regenerates: {fired.Decision.Step} postponed {fired.Postponed}");
        var done = fired.Regenerations.Single();
        Require(done.Outcome is SessionRestockOutcome.Regenerated or SessionRestockOutcome.Cleared, "a regeneration: " + done.Outcome);
        Require(done.Wield is { Failure: GameSessionFailure.None }, "the NPC wielded its best items: " + done.Wield?.Failure);
        var worn = session.QueryNpcWorn(npc);
        Require(worn is not null, "the session records what the NPC wears");
        var shown = session.QueryMapInventory(new(CurrentAreaMap))!.Holders.Single(h => h.Entity == npc).Items.Where(item => item.Facts.Worn)
            .Select(item => (item.Entity, item.Facts.InventoryLocation)).ToArray();
        Require(shown.OrderBy(item => item.InventoryLocation).SequenceEqual(worn!.Worn.Select(item => (item.Item, (int)item.Slot))), "the list shows the recorded slots");
        GD.Print("ARC-WORLD-103 native-smoke redress PASS " + System.Text.Json.JsonSerializer.Serialize(new {
            Npc = $"source {map.RestockSeed(npc).InventorySource}, worn {before} before",
            Regeneration = $"{done.Outcome}: removed {done.Removed}, coins {done.Coins}, created {done.Created.Count}, refused {done.Refused}, skipped {done.Skipped}",
            Wield = string.Join(", ", done.Wield!.Steps.Select(step => $"{step.Slot}:{(step.Wielded is null ? "-" : "on")}{(step.Unsupported is null ? "" : " unsupported " + step.Failure)}")),
            Worn = string.Join(", ", worn.Worn.Select(item => $"{item.Slot}:{(item.Item.StartsWith("made:", StringComparison.Ordinal) ? "made" : "kept")}")) }));

        // ARC-WORLD-106: the viewer's NPC combat values read what the session dressed the NPC in (made items by their prototypes).
        static void Check(bool value, string why) { if (!value) throw new InvalidOperationException("ARC-WORLD-106 npc-wear smoke: " + why); }
        var fights = combat.Worn(session, CurrentAreaMap, npc);
        Check(fights is not null && fights.Select(item => (item.Entity, item.Slot)).SequenceEqual(worn.Worn.Select(item => (item.Item, item.Slot))),
            "the combat host wears the recorded items: " + string.Join(", ", fights?.Select(item => item.Slot) ?? []));
        var build = combat.Build(session, CurrentAreaMap, npc);
        Check(build.Combat is not null, "the NPC builds: " + build.Failure);
        var life = session.QueryLife(build.Combat!.Target).View;
        Check(life is not null, "the NPC's life view");
        var state = life!.Record.State;
        var attack = build.Combat.AttackValues(life.Status, state, session.Rules.Combat);
        var weapon = worn.Worn.Any(item => item.Slot == Arcanum.Simulation.Equipment.EquipmentSlot.Weapon);
        Check(weapon == (attack.Weapon.Kind == WeaponKind.Weapon), $"a wielded weapon strikes ({weapon}, {attack.Weapon.Kind})");
        GD.Print("ARC-WORLD-106 native-smoke npc-wear PASS " + System.Text.Json.JsonSerializer.Serialize(new {
            Worn = string.Join(", ", fights!.Select(item => item.Slot)), Weapon = attack.Weapon.Kind.ToString(), attack.Skill, attack.SkillLevel,
            Firearms = build.Combat.Chooser(state).FirearmsLevel }));
    }

    /// <summary>ARC-UI-064 native observation (stage `barter`, on the live window): with the game's art the window shows the original barter
    /// window: mode 1, the background by the follower rule, the merchant's image and every shown item's inventory art read, the round view
    /// shown; a resting pointer on the first shown ware puts its quote on the quote line, and that ware pressed and released on its own grid
    /// goes back, changing nothing. <c>ARC_AREA_BARTER_CAPTURE=PATH.png</c> saves a readback of the window. Prints counts only.</summary>
    private async Task<string> BarterArtForSmokeAsync()
    {
        static void Require(bool value, string why) { if (!value) throw new InvalidOperationException("ARC-UI-064 barter-art smoke: " + why); }
        var panel = _barterPanel!;
        var area = panel.GridAreaForSelfTest;
        Require(panel.ArtMode && area.BarterLook && area.LookMode == AreaInventoryMode.Barter && area.Look is { } look
            && AreaBarterArtLayout.ArtNumbers.All(look.Art.ContainsKey), "the barter window shows the original look: " + (_inventoryArtContent?.Detail ?? "no art read"));
        var view = panel.ArtView!;
        var merchant = panel.View!.Merchant;
        var background = AreaBarterArtLayout.Background(merchant.Follower, _areaSession!.QueryParty().Followers.Count);
        Require(area.BarterFrame is { } frame && frame.Background == background && frame.Name == merchant.Name, $"background {area.BarterFrame?.Background} name '{area.BarterFrame?.Name}'");
        Require(view.HolderImage is not { } image || area.Look!.Art.ContainsKey(image), $"the merchant's image {view.HolderImage} reads");
        Require(panel.RoundViewShown, "the round view shows the game view around the PC");
        var drawn = view.LootGrid.Count(item => area.Look!.ItemArt(item.Entity) is not null);
        Require(drawn == view.LootGrid.Count, $"every shown item has its art ({drawn}/{view.LootGrid.Count})");
        var quote = "-";
        if (view.LootGrid.FirstOrDefault(item => AreaBarterArtLayout.ItemRect(item, area.LootScroll) is not null) is { } ware)
        {
            var rect = AreaBarterArtLayout.ItemRect(ware, area.LootScroll)!.Value;
            var point = new Vector2(rect.X + 16, rect.Y + 16);
            area.StillAt(point);
            quote = area.BarterFrame!.Quote;
            Require(quote.Length > 0 || merchant.Follower || panel.View.Wares.All(line => line.Entity != ware.Entity), "the resting pointer quotes the ware");
            area.PressAt(point);
            Require(area.Held is { Partner: true }, "the ware is on the cursor");
            area.ReleaseAt(new(AreaBarterArtLayout.GridX + 1, AreaBarterArtLayout.GridY + 1));
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Require(area.Held is null && panel.ArtView!.LootGrid.Any(item => item.Entity == ware.Entity), "released on its own grid it went back");
        }
        if (System.Environment.GetEnvironmentVariable("ARC_AREA_BARTER_CAPTURE") is { Length: > 0 } capture)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            using var shot = GetViewport().GetTexture().GetImage();
            if (shot.SavePng(capture) != Error.Ok) throw new InvalidOperationException("AREA_BARTER_CAPTURE_FAILED");
        }
        var line = $"background {background}, image {view.HolderImage?.ToString() ?? "none"}, shown {view.LootGrid.Count} (drawn {drawn}, container {view.HolderContainer}), "
            + $"hero grid {view.Grid.Count}, quote '{quote}'";
        GD.Print("ARC-UI-064 native-smoke barter-art PASS " + line);
        return line;
    }

    /// <summary>
    /// ARC-UI-050 native observation on the original data (stage `barter`, with --hero): a living NPC speaker of the shown map, a merchant
    /// when one is there, is passed to the production path a `b:` reply takes once its talk ended; the window opens over the live session
    /// with its quotes (ARC-WORLD-090: the open moves a substitute container's coins onto the merchant and identifies the goods, its only
    /// change) and Escape closes it, changing nothing. Then, on a detached copy with the seeds attached again, the hero sells one item to
    /// that NPC (into its container when it has one) as a merchant buying everything and buys one ware it can pay for. Prints counts and
    /// outcomes only.
    /// </summary>
    private async Task RunAreaBarterSmokeAsync()
    {
        static void Require(bool value, string why) { if (!value) throw new InvalidOperationException("ARC-UI-050 barter smoke: " + why); }
        if (_areaSession is not { } session || _conversationHost is not { } talk) { GD.Print("ARC-UI-050 native-smoke barter SKIP (no area session or dialog host)"); return; }
        if (!AreaBarter.HeroCanTrade(session)) { GD.Print("ARC-UI-050 native-smoke barter SKIP (no living hero: start with --hero)"); return; }
        for (var i = 0; i < 1200 && _barterData is null; i++) { await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); AdoptBarterData(); }
        Require(_barterData is { Status: AreaBarterDataStatus.Ready, Data: not null }, "data " + _barterData?.Status + " " + _barterData?.Detail);
        var data = _barterData!.Value.Data!;
        var candidates = talk.Speakers.Select((speaker, index) => (Entity: AreaConversationState.EntityId(speaker.Row), Index: index, Fields: CombatFieldReader.Read(speaker.Row).Fields))
            .Where(c => c.Fields?.Npc is not null && !session.QueryNpcDead(c.Entity)).ToArray();
        if (candidates.Length == 0) { GD.Print($"ARC-UI-050 native-smoke barter SKIP (no living NPC speaker among {talk.Speakers.Count})"); return; }
        var chosen = candidates.FirstOrDefault(c => data.BuyList(c.Fields!.Critter!.InventorySource).Status is InventorySourceBuyStatus.Listed or InventorySourceBuyStatus.Everything);
        // ARC-WORLD-090: else a speaker with a substitute container in reach that buys something (the installed merchants).
        if (chosen.Entity is null)
            chosen = candidates.FirstOrDefault(c => new AreaBarterMerchant(c.Entity, "", 0, false, false, InventorySourceBuyList.Nothing, session.QueryCompanion(c.Entity).LedByPc)
                .WithSubstitute(_inventoryHost?.Maps.FirstOrDefault(map => map.TryGetRow(c.Entity, out _))?.Substitute(c.Entity),
                    new(checked((int)talk.Speakers[c.Index].WorldX), checked((int)talk.Speakers[c.Index].WorldY)), data)
                .ContainerBuyList?.Status is InventorySourceBuyStatus.Listed or InventorySourceBuyStatus.Everything);
        if (chosen.Entity is null) chosen = candidates.FirstOrDefault(c => c.Fields!.Npc!.RetailPriceMultiplier != 0);
        if (chosen.Entity is null) chosen = candidates[0];
        var simulatedFocus = _worldFocusSimulationForSelfTest; var focused = _worldFocused;
        _worldFocusSimulationForSelfTest = true; _worldFocused = true;
        try
        {
            // The scenery loops of a newly shown map start on its first frame with a hero cell (ProcessAreaSceneryAnimation), a retry later when the
            // goal table was busy: four goal runs, one session revision. Let that happen before the baseline revision, or the look's frames below
            // pay for it (stage alone, ARC-DEV-393).
            for (var i = 0; i < 1200 && (ShownSceneryHost is not { StartPending: false }); i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Require(ShownSceneryHost is { StartPending: false }, "the scenery loops started within 1200 frames (the first frame with a hero cell)");
            var revision = session.Version.Revision;
            QueueDialogBarter(talk, (chosen.Entity, chosen.Index));
            TakeDialogBarter();
            Require(BarterOpen && _barterPanel!.View is { } view && view.Merchant.Entity == chosen.Entity, "the window opened with the speaker: " + _barterStatus);
            var live = _barterPanel!.View!;
            // ARC-WORLD-090: the open's coins and identification; ARC-WORLD-096: the restock schedule of the shown object.
            Require(session.Version.Revision - revision is >= 0 and <= 2, "the open is two revisions at most");
            revision = session.Version.Revision;
            var art = await BarterArtForSmokeAsync();
            Require(session.Version.Revision == revision, "the original look's checks changed nothing");
            CloseBarterByPlayer();
            Require(!BarterOpen && session.Version.Revision == revision, "the player's close closed it; nothing changed");

            var saved = session.Capture();
            var options = new GameSessionRestoreOptions(new(saved.Identity.World, session.Flags.Binding.Identity.Module),
                session.QueryClockSnapshot().ProfileBinding, new(Guid.NewGuid()), session.Limits, session.Rules.Mode, session.Experience, session.Difficulty,
                LoadRandom: session.QueryRandom());
            using var copy = GameSessionOwner.Restore(saved, options).Owner ?? throw new InvalidOperationException("copy");
            Require(_inventoryHost?.Attach(copy) == GameSessionFailure.None, "seeds attached to the copy");
            var buysAll = live.Merchant with { BuyList = new InventorySourceBuyList(InventorySourceBuyStatus.Everything, []) };
            var inventory = _inventoryHost!;
            var model = new AreaBarter(copy, inventory, data, buysAll, () => 50, _ => true,
                (item, kind) => inventory.Maps.Any(map => map.TryGetRow(item, out _)) ? inventory.Name(item, _hoverNames?.Catalog) : AreaInventoryHost.KindName(kind, _hoverNames?.Catalog));
            var before = model.View();
            var sellable = before.Hero.FirstOrDefault(line => line.Quote.Price > 0);
            Require(sellable is not null, "no hero item sells to a merchant buying everything");
            var gold = copy.QueryGold();
            var sold = model.Sell(sellable!.Entity);
            Require(sold.Moved && copy.QueryGold() == gold + sold.Total, "the sale moved the item and paid at most the merchant's coins: " + sold.Text);
            var ware = model.View().Wares.FirstOrDefault(line => line.Quote.Price > 0 && (long)line.Quote.Price * line.Count <= copy.QueryGold());
            var bought = ware is null ? "none affordable" : model.Buy(ware.Entity) is var trade && trade.Moved ? trade.Text : "refused: " + trade.Text;
            Require(session.Version.Revision == revision, "the live session is untouched");
            GD.Print("ARC-UI-050 native-smoke barter PASS " + System.Text.Json.JsonSerializer.Serialize(new {
                Merchant = $"speaker {chosen.Index} of {talk.Speakers.Count}, multiplier {live.Merchant.Multiplier}, buys {live.Merchant.BuyList.Status}, fence {live.Merchant.Fence}, script {live.Merchant.BuyScript}",
                Container = live.Merchant.Container is null ? "none" : $"buys {live.Merchant.ContainerBuyList?.Status}, identified {session.Capture().Inventory.Identified.Count}",
                Live = $"hero items {live.Hero.Count} (priced {live.Hero.Count(l => l.Quote.Price > 0)}), wares {live.Wares.Count} (priced {live.Wares.Count(l => l.Quote.Price > 0)}), gold {live.HeroGold}/{live.MerchantGold}, haggle {live.Haggle} {live.Training}, reaction {live.Reaction}",
                Sold = sold.Text, Bought = bought, Art = art }));
            await RunAreaRestockSmokeAsync(talk, chosen.Entity, chosen.Index, live);
        }
        finally
        {
            if (BarterOpen) EndBarter();
            _worldFocusSimulationForSelfTest = simulatedFocus; _worldFocused = focused;
        }
    }
}
