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
        var read = typeof(TerrainLoadCoordinator).Assembly.GetType("Arcanum.TerrainViewer.Core.AreaLoadOperation")!
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
            Require(BarterOpen && WorldModal && !_worldGate.IsOpen, "the window is modal");
            Require(view is { HeroGold: 1000, MerchantGold: 100, Haggle: 10, Training: SkillTraining.None } && view.Hero.Single().Quote.Price == 55
                && view.Wares.Single() is { Entity: "axe", Quote.Price: 760 }, "quotes: sell 55, buy 760");
            Require(GetSelfTestSnapshot().Hud.Contains(" | barter open: Escape closes", StringComparison.Ordinal), "HUD open status");
            InventoryKey(Key.C);
            Require(BarterOpen && !SheetOpen, "the open window owns the C key");

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

            InventoryKey(Key.Escape);
            Require(!BarterOpen && !WorldModal, "Escape closed it");
            OpenBarterWindow(Model(owner));
            var replacement = AreaTransitionCoordinator.CreateSession(owner.QueryClockSnapshot().ProfileBinding, Guid.NewGuid(), Guid.NewGuid(),
                new(actor.Cell.X, actor.Cell.Y), actor.Map.RuntimeMap).Owner!;
            ReplaceAreaSession(replacement, AreaRuntimes(), actor.Map.RuntimeMap, _conversationHost?.Rebind(replacement));
            SyncAreaBarter();
            Require(!BarterOpen, "a replaced session closes the window");
            Require(!AreaBarter.HeroCanTrade(replacement), "a session without a hero cannot trade");
            return "sell 55, buy 760, keys, close, replaced session";
        }
        finally
        {
            if (BarterOpen) EndBarter();
            _worldFocusSimulationForSelfTest = simulatedFocus;
        }
    }

    /// <summary>
    /// ARC-UI-050 native observation on the original data (stage `barter`, with --hero): a living NPC speaker of the shown map, a merchant
    /// when one is there, is passed to the production path a `b:` reply takes once its talk ended; the window opens over the live session
    /// with its quotes and Escape closes it, changing nothing. Then, on a detached copy with the seeds attached again, the hero sells one
    /// item to that NPC as a merchant buying everything and buys one ware it can pay for. Prints counts and outcomes only.
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
        if (chosen.Entity is null) chosen = candidates.FirstOrDefault(c => c.Fields!.Npc!.RetailPriceMultiplier != 0);
        if (chosen.Entity is null) chosen = candidates[0];
        var simulatedFocus = _worldFocusSimulationForSelfTest; var focused = _worldFocused;
        _worldFocusSimulationForSelfTest = true; _worldFocused = true;
        try
        {
            var revision = session.Version.Revision;
            QueueDialogBarter(talk, (chosen.Entity, chosen.Index));
            TakeDialogBarter();
            Require(BarterOpen && _barterPanel!.View is { } view && view.Merchant.Entity == chosen.Entity, "the window opened with the speaker: " + _barterStatus);
            var live = _barterPanel!.View!;
            InventoryKey(Key.Escape);
            Require(!BarterOpen && session.Version.Revision == revision, "Escape closed it; nothing changed");

            var saved = session.Capture();
            var options = new GameSessionRestoreOptions(new(saved.Identity.World, session.Flags.Binding.Identity.Module),
                session.QueryClockSnapshot().ProfileBinding, new(Guid.NewGuid()), session.Limits, session.Rules.Mode, session.Experience, session.Difficulty);
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
                Live = $"hero items {live.Hero.Count} (priced {live.Hero.Count(l => l.Quote.Price > 0)}), wares {live.Wares.Count} (priced {live.Wares.Count(l => l.Quote.Price > 0)}), gold {live.HeroGold}/{live.MerchantGold}, haggle {live.Haggle} {live.Training}, reaction {live.Reaction}",
                Sold = sold.Text, Bought = bought }));
        }
        finally
        {
            if (BarterOpen) EndBarter();
            _worldFocusSimulationForSelfTest = simulatedFocus; _worldFocused = focused;
        }
    }
}
