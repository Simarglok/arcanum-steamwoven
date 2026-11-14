using System.Reflection;
using Arcanum.Data.Area;
using Arcanum.Data.Characters;
using Arcanum.Data.Combat;
using Arcanum.Data.Messages;
using Arcanum.Session;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Items;
using Arcanum.TerrainViewer.Core;
using Arcanum.TerrainViewer.Tests;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

public static partial class TerrainViewerSelfTest
{
    /// <summary>ARC-UI-1731 over the strict synthetic AREA owner in the production app: with a synthetic hero (an unidentified axe and a damaged
    /// sword) and a merchant of a synthetic seed, the item identify window opens modal on the interface mode stack with its quotes, two clicks
    /// pay and identify, the repair window quotes and mends the sword by the merchant's skill, Escape closes, and a replaced session closes it.</summary>
    private static async Task AssertAreaItemServiceAsync(Node host)
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
            summary = await app.AreaItemServiceForSelfTestAsync();
        }
        finally
        {
            app.ClearSyntheticForSelfTest();
            viewport.Free();
        }
        GD.Print($"ARC-UI-1731 self-test PASS synthetic item services ({summary}); original data covered by the dialog replies of the native smoke");
    }
}

public partial class TerrainViewerApp
{
    internal async Task<string> AreaItemServiceForSelfTestAsync()
    {
        static void Require(bool value, string why) { if (!value) throw new InvalidOperationException("AREA item service: " + why); }
        // ARC_UI_ITEM_SERVICE_CAPTURE: a folder; the window is saved as a PNG at each step there (owner graphics rule: before/after images in the thread, never in git).
        async Task Capture(string name)
        {
            if (System.Environment.GetEnvironmentVariable("ARC_UI_ITEM_SERVICE_CAPTURE") is not { Length: > 0 } folder) return;
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            using var shot = GetViewport().GetTexture().GetImage();
            if (shot.SavePng(System.IO.Path.Combine(folder, name + ".png")) != Error.Ok) throw new InvalidOperationException("AREA item service: capture failed");
        }
        if (!SelfTestBootstrap || _readyArea is null || _areaSession is null) throw new InvalidOperationException("AREA_ITEM_SERVICE_READY_REQUIRED");
        var simulatedFocus = _worldFocusSimulationForSelfTest;
        _worldFocusSimulationForSelfTest = true; _worldFocused = true;
        try
        {
            // A synthetic hero (1000 gold, a plain sword of worth 100 and a magickal axe of worth 400, complexity 30) through the production
            // replacement path, as a quick load does, and a merchant (100 coins) that repairs as an expert.
            const int Sword = 4030, Axe = 4040;
            var session = _areaSession;
            var hero = new SessionHeroDefinition(16089, CharacterRace.HalfElf, CharacterGender.Male, [8, 8, 8, 8, 8, 8, 8, 8, 0, 0], 1, 5, 0,
                null, null, 1000, [new SessionHeroKitItem(new(SessionItemType.Weapon, Sword, 4100, 34100)), new SessionHeroKitItem(new(SessionItemType.Weapon, Axe, 4200, 34200))]);
            var actor = session.QueryActor();
            var created = AreaTransitionCoordinator.CreateSession(session.QueryClockSnapshot().ProfileBinding, Guid.NewGuid(), Guid.NewGuid(),
                new(actor.Cell.X, actor.Cell.Y), actor.Map.RuntimeMap,
                new AreaHeroLoad(AreaHeroStatus.Ready, AreaStartingHero.Find("half-elf-male")!, hero, 2352, 0, null, "synthetic"));
            var owner = created.Owner ?? throw new InvalidOperationException("AREA item service: session " + created.Failure);
            ReplaceAreaSession(owner, AreaRuntimes(), actor.Map.RuntimeMap, _conversationHost?.Rebind(owner));
            var seed = new SessionMapInventorySeed([new("merchant", SessionHolderKind.Npc, false, false, [], ["coins"])],
                [new("coins", new(new(SessionItemType.Gold, AreaBarterData.GoldPrototype, 4200, 34200), 10, 0, 0, 100, []), null)]);
            Require(owner.AttachMapInventory(actor.Map, seed).Failure == GameSessionFailure.None, "the merchant's seed attached");
            static CombatFields Weapon(int prototype, int worth, int complexity) =>
                new(CombatFieldOrigin.Prototype, new CombatObjectFields((int)SessionItemType.Weapon, prototype, 0, 0, 0, 0, 0, 40, 0, 0, 4, [0, 0, 0, 0, 0], 0),
                    new CombatItemFields(0, 10, worth, 0, complexity, 0, 0, [], 0, 0, 0, 0), null, null, null, null, null, null);
            static CombatFields Gold() =>
                new(CombatFieldOrigin.Prototype, new CombatObjectFields((int)SessionItemType.Gold, AreaBarterData.GoldPrototype, 0, 0, 3200, 33200, 0, 1, 0, 0, 4, [0, 0, 0, 0, 0], 0),
                    new CombatItemFields(0, 0, 1, 0, 0, 0, 0, [], 0, 0, 0, 0), null, null, null, null, null, null);
            static MesFile Mes(string text) => MesFile.Parse(System.Text.Encoding.ASCII.GetBytes(text)).File!;
            var data = AreaBarterData.Create(Mes("{1}{Smith: 1,1}{2}{End: 1,1}"), Mes("{1}{4010 4020}"),
                new Dictionary<int, CombatFields> { [Sword] = Weapon(Sword, 100, 0), [Axe] = Weapon(Axe, 400, 30), [AreaBarterData.GoldPrototype] = Gold() });
            var merchant = new AreaBarterMerchant("merchant", "Smith", 100, false, false, data.BuyList(1), false);
            int[] tech = new int[4];
            tech[0] = 4 | (int)SkillTraining.Expert << 6;
            var repairer = (new SessionNpcAdvancement("merchant", new(CharacterRace.Human, false, [8, 8, 8, 8, 20, 8, 8, 8], 0, 0, 1, 0,
                [.. new int[12]], [.. tech], [.. new int[16]], [.. new int[8]], 0, 0, 0, 0), 0), AdvancementModifiers.None);
            AreaItemService Model(GameSessionOwner on, ItemServiceKind kind) => new(new AreaBarter(on, _inventoryHost ?? new AreaInventoryHost([]), data, merchant, () => 50,
                _ => true, (entity, _) => entity), kind, () => repairer);

            OpenItemServiceWindow(Model(owner, ItemServiceKind.Identify));
            Require(ItemServiceOpen && WindowOpen(InterfaceMode.InventoryKind5) && WorldModal && WindowPanelsMatchStack && !_worldGate.IsOpen, "the identify window is modal, mode 5 on the stack");
            var view = _servicePanel!.View!;
            Require(view.Items.Single(line => line.Entity == "kit:2").Quote is { Status: ItemServiceStatus.Quoted, Price: 100 }
                && view.Items.Single(line => line.Entity == "kit:1").Quote.Status == ItemServiceStatus.AlreadyIdentified, "quotes: the axe costs 100, the sword is identified");
            Require(GetSelfTestSnapshot().Hud.Contains(" | item service open", StringComparison.Ordinal), "HUD open status");
            await Capture("identify-1-open");
            var axe = _servicePanel.ButtonsForSelfTest.Single(button => button.Text.Contains("kit:2", StringComparison.Ordinal));
            axe.EmitSignal(BaseButton.SignalName.Pressed);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await Capture("identify-2-quote");
            Require(owner.QueryGold() == 1000 && !owner.QueryItemIdentified("kit:2") && _servicePanel.View!.Selected == "kit:2", "the first click only selects and quotes");
            _servicePanel.ButtonsForSelfTest.Single(button => button.Text.Contains("kit:2", StringComparison.Ordinal)).EmitSignal(BaseButton.SignalName.Pressed);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Require(owner.QueryGold() == 900 && owner.QueryNpcGold("merchant") == 200 && owner.QueryItemIdentified("kit:2")
                && _servicePanel.MessageForSelfTest == "identified kit:2 for 100 gold", "the second click paid 100 and identified the axe: " + _servicePanel.MessageForSelfTest);

            await Capture("identify-3-paid");
            // ARC-DEV-111: Escape opens the menu (kind 3), which closes the window; Escape again continues play.
            InventoryKey(Key.Escape);
            Require(!ItemServiceOpen && MainMenuOpen && WindowPanelsMatchStack, "Escape opened the limited menu, which closed the window");
            InventoryKey(Key.Escape);
            Require(!ItemServiceOpen && !MainMenuOpen && !WorldModal, "Escape continued play");

            // The repair window: the sword takes 20 damage, the expert merchant quotes and mends it for money.
            var stock = SessionItemConditions.FromCombatFields(Weapon(Sword, 100, 0))!.Value;
            Require(owner.DamageItem(owner.Version, "kit:1", stock, 20).Disposition == GameSessionDisposition.Changed, "the sword is damaged");
            OpenItemServiceWindow(Model(owner, ItemServiceKind.Repair));
            Require(ItemServiceOpen && WindowOpen(InterfaceMode.InventoryKind6) && WindowPanelsMatchStack, "the repair window is mode 6");
            var quote = _servicePanel!.View!.Items.Single(line => line.Entity == "kit:1").Quote;
            Require(quote.Quoted && quote.Price >= 2, "the damaged sword is quoted");
            await Capture("repair-1-open");
            _servicePanel.ButtonsForSelfTest.Single(button => button.Text.Contains("kit:1", StringComparison.Ordinal)).EmitSignal(BaseButton.SignalName.Pressed);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            _servicePanel.ButtonsForSelfTest.Single(button => button.Text.Contains("kit:1", StringComparison.Ordinal)).EmitSignal(BaseButton.SignalName.Pressed);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Require(owner.QueryGold() == 900 - quote.Price && owner.QueryItemCondition("kit:1", stock)!.Value.Damage < 20, "the repair was paid and restored hit points: " + _servicePanel.MessageForSelfTest);
            await Capture("repair-2-paid");

            // ARC-UI-1765: the original look over a synthetic art set (blank arts of the original sizes): mode 6's frame with the NPC's generated quote line, a press quotes (and
            // the remembered item is named in the frame), the boxes and the right release do nothing, the switch shows the diagnostic list and back, a release on the round view closes.
            EndItemService(); // the repair window above is still open
            Require(!ItemServiceOpen, "the repair window closed");
            var inventoryHost = _inventoryHost;
            _inventoryHost ??= new AreaInventoryHost([]);
            _inventoryArtLookForSelfTest = SyntheticBarterLook();
            AreaItemService ArtModel() => new(new AreaBarter(owner, _inventoryHost, data, merchant, () => 50, _ => true, (entity, _) => entity), ItemServiceKind.Repair,
                () => repairer, (first, last, price) => $"line{first}-{last} {price?.ToString() ?? "-"}", group => $"class{group}");
            try
            {
                OpenItemServiceWindow(ArtModel());
                var panel = _servicePanel!;
                var area = panel.GridAreaForSelfTest;
                Require(panel.ArtMode && area.ServiceLook && area.LookMode == AreaInventoryMode.Own && area.ServiceFrame is { Selected: null, Quote: "" },
                    $"the original look opened in the own window's frame (art {panel.ArtMode}, service {area.ServiceLook}, look {area.Look is not null}, mode {area.LookMode}, frame {area.ServiceFrame})");
                Require(panel.LookToggleForSelfTest is { Visible: true, Text: "Diagnostic view" } && !panel.ButtonsForSelfTest[0].IsVisibleInTree(), "the switch below the frame, the list hidden");
                var sword = panel.ArtView!.Grid.Single(item => item.Entity == "kit:1");
                area.PressAt(AreaInventoryGridArea.CellCentre(sword.Slot));
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                Require(area.ServiceFrame!.Selected == "kit:1" && area.ServiceFrame.Quote.StartsWith("line", StringComparison.Ordinal), "a press quoted the sword with the generated line: " + area.ServiceFrame.Quote);
                var revision = owner.Version.Revision;
                var stray = 0;
                area.UseRequested += _ => stray++; area.DropRequested += _ => stray++; area.ItemPicked += _ => stray++;
                area.PressAt(AreaInventoryGridArea.IconCentre(AreaInventoryIcon.Drop)); area.RightReleaseAt(AreaInventoryGridArea.CellCentre(sword.Slot));
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                Require(stray == 0 && owner.Version.Revision == revision && area.Held is null && area.ServiceFrame.Selected == "kit:1", $"the drop box and the right release do nothing ({stray} stray events)");
                // The pad: the D-pad steps over the hero's items and quotes the one it lands on, confirm clicks the remembered one.
                var items = panel.View!.Items;
                panel.Navigate(items.Count > 1 && items[0].Entity == "kit:1" ? 1 : -1);
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                Require(items.Count < 2 || area.ServiceFrame.Selected != "kit:1", "the pad moved to another item and quoted it: " + area.ServiceFrame.Selected);
                panel.LookToggleForSelfTest.EmitSignal(BaseButton.SignalName.Pressed);
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                Require(!panel.ArtMode && panel.ButtonsForSelfTest.Count > 0 && panel.ButtonsForSelfTest[0].IsVisibleInTree(), "the switch shows the diagnostic list");
                panel.LookToggleForSelfTest.EmitSignal(BaseButton.SignalName.Pressed);
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                Require(panel.ArtMode, "and the original look again");
                // ARC-UI-2071 (ledger DH10): the release of the reply click that opened the window (a press that began over the dialog, a release no press of the window began) reaches the window in both settings
                // (an item quotes as a click on it, the round view's square closes); a release whose press did not begin over the dialog is no reply's.
                {
                    var other = items.First(line => line.Entity != area.ServiceFrame!.Selected).Entity;
                    var otherCell = panel.ArtView!.Grid.Single(item => item.Entity == other).Slot;
                    var onOther = area.FrameToScreenForSelfTest(AreaInventoryGridArea.CellCentre(otherCell));
                    var (roundX, roundY) = AreaInventoryHole.Corner(AreaInventoryMode.Own);
                    var onRound = area.FrameToScreenForSelfTest(new(roundX + 10, roundY + 10));
                    InputEventMouseButton Release(Vector2 at) => new() { ButtonIndex = MouseButton.Left, Pressed = false, Position = at, GlobalPosition = at };
                    async Task ReplyClick(Vector2 at, bool pressedOverDialog)
                    {
                        _replyPressDown = pressedOverDialog; ReplyReleaseTargetForSelfTest = null;
                        InputWorld(Release(at));
                        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                    }
                    try
                    {
                        var kept = area.ServiceFrame!.Selected;
                        await ReplyClick(onOther, false); await ReplyClick(onRound, false);
                        Require(ItemServiceOpen && area.ServiceFrame!.Selected == kept && ReplyReleaseTargetForSelfTest is null, "a release whose press did not begin over the dialog is nobody's");
                        await ReplyClick(onOther, true);
                        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                        Require(ItemServiceOpen && ReplyReleaseTargetForSelfTest == "item-service" && area.ServiceFrame!.Selected == other && area.ServiceFrame.Quote.Length > 0,
                            $"The release quoted the item under it ({kept} -> {area.ServiceFrame.Selected}, {ReplyReleaseTargetForSelfTest})");
                        await ReplyClick(onRound, true);
                        Require(!ItemServiceOpen && ReplyReleaseTargetForSelfTest == "item-service", "The release on the round view closed the window");
                        // The window is not there yet at the release: the release waits for it (the viewer opens it a frame later) and is dropped when none opens.
                        _replyPressDown = true; ReplyReleaseTargetForSelfTest = null;
                        InputWorld(Release(onRound));
                        Require(_replyRelease is not null, "no window yet: the release is kept");
                        for (var frame = 0; frame < ReplyReleaseWaitFrames + 1; frame++) SyncReplyRelease();
                        Require(_replyRelease is null && ReplyReleaseTargetForSelfTest == "dropped", "no window opened: the release was dropped");
                    }
                    finally { _replyPressDown = false; _replyRelease = null; }
                    OpenItemServiceWindow(ArtModel());
                    panel = _servicePanel!; area = panel.GridAreaForSelfTest;
                    Require(ItemServiceOpen && panel.ArtMode, "the window opened again");
                }
                var (holeX, holeY) = AreaInventoryHole.Corner(AreaInventoryMode.Own);
                area.ReleaseAt(new(holeX + 10, holeY + 10));
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                Require(!ItemServiceOpen, "a release on the round view closed the window");
            }
            finally
            {
                if (ItemServiceOpen) EndItemService();
                _inventoryArtLookForSelfTest = null;
                _inventoryHost = inventoryHost;
            }

            // A replaced session without a hero closes the window.
            var replacement = AreaTransitionCoordinator.CreateSession(owner.QueryClockSnapshot().ProfileBinding, Guid.NewGuid(), Guid.NewGuid(),
                new(actor.Cell.X, actor.Cell.Y), actor.Map.RuntimeMap).Owner!;
            ReplaceAreaSession(replacement, AreaRuntimes(), actor.Map.RuntimeMap, _conversationHost?.Rebind(replacement));
            SyncAreaItemService();
            Require(!ItemServiceOpen, "a replaced session closes the window");
            return $"identify 100, repair {quote.Price}, original look, keys, close, replaced session";
        }
        finally
        {
            if (ItemServiceOpen) EndItemService();
            _worldFocusSimulationForSelfTest = simulatedFocus;
        }
    }
}
