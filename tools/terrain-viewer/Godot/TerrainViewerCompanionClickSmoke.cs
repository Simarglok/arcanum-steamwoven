using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Magic;
using Arcanum.Session;
using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Party;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

public partial class TerrainViewerApp
{
    // ARC-DEV-263 (B33, smoke plan SM24) native observation (stage "companion-click", only when named, map mode with --hero, the start map): the buttons on a joined companion, judged by
    // the original's (the executable read of ARC-DEV-280, ledger OL34): the order menu opens on the RIGHT button's release over his portrait and on nothing else; the LEFT button's release
    // over his portrait is a click on him (a talk out of combat mode), as a left press on his body is; a right click on his body opens no menu and starts no talk; a release the menu does not
    // take closes it. Every fact is recorded, then the stage fails if any differs from the original's.
    private async Task RunAreaCompanionClickSmokeAsync(TerrainLoadResult? mapMode = null)
    {
        if (_areaSession is not { } session || session.QueryHero() is null || _readyArea is not { } area || _conversationHost is not { PartyWired: true })
        { GD.Print("ARC-DEV-263 native-smoke companion-click NONE (needs --hero, an area session and the session party)"); return; }
        static void Require(bool value, string why)
        {
            if (value) return;
            GD.Print("ARC-DEV-263 companion-click smoke FAIL: " + why);
            throw new InvalidOperationException("AREA_COMPANION_CLICK_SMOKE_FAILED");
        }
        var map = mapMode is { Area: { } loaded, AreaStepSnapshot: { } mapSteps }
            ? new AreaMapRuntime(loaded.Snapshot, mapSteps, loaded.Plan.Chart) : _areaTransitions!.Current;
        var template = _companionTemplates.FirstOrDefault(t => t.Home.RuntimeMap == CurrentAreaMap && !SpeakerDead(t.Speaker)
            && session.QueryCompanion(t.Entity).Standing == CompanionStanding.Free);
        if (template is null) { GD.Print($"ARC-DEV-263 native-smoke companion-click NO-FOLLOWER map={map.RuntimeMap}"); return; }
        var follower = template.Entity;
        var simulatedFocus = _worldFocusSimulationForSelfTest; var focused = _worldFocused; var testTime = _worldTestTime;
        _worldFocusSimulationForSelfTest = true;
        var joined = false;
        try
        {
            _worldFocused = true; SelectSimulatedPadForSelfTest();
            var join = session.ForceJoinParty(session.Version, follower);
            Require(join.Disposition != GameSessionDisposition.Rejected, "the speaker joined: " + join.Failure);
            joined = true;
            // The placement on a cell beside him binds the joined companion (the group), and he comes up to the hero.
            AreaSmokePlace(map, AreaSmokeStandable(map, template.HomeOrigin, 1));
            long Near() => _areaCompanions?.Companions.SingleOrDefault(c => c.Entity == follower) is { } now
                ? FollowDistanceRules.Distance(now.Grid.State.CommittedCell, ((AreaGridSession)_worldActor.Session!).State.CommittedCell) : long.MaxValue;
            for (var f = 0; f < 600 && (Near() > 3 || _followerPortraits?.EntitiesForSelfTest.Contains(follower) != true); f++) AreaSmokeFrame();
            Require(Near() <= 3, "the companion came up to the hero: " + _areaCompanions?.Status());
            Require(_followerPortraits?.EntitiesForSelfTest.Contains(follower) == true, "his portrait is shown");
            var companion = _areaCompanions!.Companions.Single(c => c.Entity == follower);
            var portraits = _followerPortraits!;

            // ARC_AREA_FOLLOWER_PANEL_CAPTURE saves in-app readbacks of the panel (the menu open, the scrolled strip) to the caller's ignored path (never committed).
            var capture = System.Environment.GetEnvironmentVariable("ARC_AREA_FOLLOWER_PANEL_CAPTURE");
            async Task Capture(string name)
            {
                if (string.IsNullOrWhiteSpace(capture)) return;
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                using var image = GetViewport().GetTexture().GetImage();
                Require(image.SavePng($"{capture}-{name}.png") == Error.Ok, "capture saved");
            }
            var facts = new List<string>();
            var failures = new List<string>();
            void Fact(string name, bool original, string seen)
            {
                facts.Add($"{name}={seen}");
                if (!original) failures.Add($"{name} ({seen})");
            }
            async Task ClickPortraitAsync(MouseButton button, bool press = true)
            {
                var at = portraits.PortraitCentreForSelfTest(follower) ?? throw new InvalidOperationException("ARC-DEV-263 smoke: no portrait");
                if (press) GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = button, Pressed = true, Position = at, GlobalPosition = at });
                GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = button, Pressed = false, Position = at, GlobalPosition = at });
                for (var frame = 0; frame < 3; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }
            void CloseConversation()
            {
                for (var close = 0; close < 4 && ConversationOpen; close++) _conversationPanel!.LeaveForSelfTest.EmitSignal(BaseButton.SignalName.Pressed);
                Require(!ConversationOpen && !WorldModal, "the conversation was closed");
                portraits.CloseMenu();
            }
            // After a talk, the companion stands by the hero again and nothing is pending.
            void Settle() { for (var f = 0; f < 40; f++) AreaSmokeFrame(); }
            bool OpensTalk(Action click)
            {
                click();
                for (var frame = 0; frame < 600 && !ConversationOpen; frame++) AreaSmokeTalkFrame();
                return ConversationOpen && _conversationSpeaker?.Row.Key == companion.Key;
            }

            // ARC-UI-2069 (DB1, DB2): with the dialog open, Original leaves the panel and its bars drawn and its handler (0x56A9D0) takes no record; Steamwoven hides the panel.
            async Task DialogPortraitFacts(string rules)
            {
                var stays = DialogBarStays;
                for (var frame = 0; frame < 3; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                var drawn = portraits.Visible && portraits.EntitiesForSelfTest.Contains(follower) && portraits.FrameForSelfTest(follower) is { Frame: true };
                Fact(rules + "the portrait panel " + (stays ? "stays drawn" : "is hidden") + " under the dialog", drawn == stays, drawn ? "drawn" : "hidden");
                await Capture("dialog-" + (stays ? "original" : "steamwoven"));
                if (stays)
                {
                    var bars = portraits.BarsForSelfTest(follower);
                    Fact(rules + "the portrait's bars stay drawn under the dialog", bars is { Health: > 0 }, bars?.ToString() ?? "none");
                    var at = portraits.PortraitCentreForSelfTest(follower)!.Value;
                    // Off the portrait and on again: the entry must not hover under the dialog (the pointer rested on it when the click opened the talk).
                    var away = new Vector2(at.X + 400, at.Y + 300);
                    GetViewport().PushInput(new InputEventMouseMotion { Position = away, GlobalPosition = away });
                    for (var frame = 0; frame < 3; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                    var hoversEntry = PortraitHoversForSelfTest;
                    GetViewport().PushInput(new InputEventMouseMotion { Position = at, GlobalPosition = at });
                    for (var frame = 0; frame < 3; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                    Fact(rules + "entering the portrait runs no hover under the dialog", PortraitHoversForSelfTest == hoversEntry && PortraitHoverKeyForSelfTest is null, $"{PortraitHoversForSelfTest - hoversEntry} run(s)");
                    Fact(rules + "the pointer over the portrait is no hover under the dialog", portraits.HoveredForSelfTest is null, portraits.HoveredForSelfTest ?? "none");
                    await ClickPortraitAsync(MouseButton.Right);
                    Fact(rules + "a right release on the portrait opens no menu under the dialog", !portraits.MenuShown, portraits.MenuShown ? "menu" : "none");
                    var hoversNow = PortraitHoversForSelfTest;
                    await ClickPortraitAsync(MouseButton.Left);
                    Fact(rules + "a left release on the portrait does nothing under the dialog", ConversationOpen && !portraits.MenuShown && PortraitHoversForSelfTest == hoversNow, "left release");
            }

            }

            // 1. The right release over his portrait opens the order menu (a right press alone opens nothing), a second one closes it.
            var rightPressOnly = false;
            {
                var at = portraits.PortraitCentreForSelfTest(follower)!.Value;
                GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true, Position = at, GlobalPosition = at });
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                rightPressOnly = portraits.MenuShown;
                await ClickPortraitAsync(MouseButton.Right, press: false);
            }
            Fact("right press alone opens no menu", !rightPressOnly, rightPressOnly ? "menu" : "none");
            Fact("right release on his portrait opens the menu", portraits.MenuShown, portraits.MenuShown ? "menu" : "none");
            await ClickPortraitAsync(MouseButton.Right);
            Fact("a second right click closes the menu", !portraits.MenuShown, portraits.MenuShown ? "menu" : "closed");

            // 2. With the menu open, a left click on his portrait closes it and starts no talk.
            await ClickPortraitAsync(MouseButton.Right);
            var menuBefore = portraits.MenuShown;
            await ClickPortraitAsync(MouseButton.Left);
            for (var frame = 0; frame < 20; frame++) AreaSmokeTalkFrame();
            Fact("a left click closes the open menu", menuBefore && !portraits.MenuShown, portraits.MenuShown ? "menu" : "closed");
            Fact("... and starts no talk", !ConversationOpen, ConversationOpen ? "talk" : "none");
            if (ConversationOpen) CloseConversation();
            portraits.CloseMenu();

            // 3. The left click on his portrait with no menu open is a talk with him, and opens no menu.
            var portraitTalk = false;
            {
                await ClickPortraitAsync(MouseButton.Left);
                var menuShown = portraits.MenuShown;
                for (var frame = 0; frame < 600 && !ConversationOpen; frame++) AreaSmokeTalkFrame();
                portraitTalk = ConversationOpen && _conversationSpeaker?.Row.Key == companion.Key;
                Fact("left click on his portrait opens no menu", !menuShown, menuShown ? "menu" : "none");
                Fact("left click on his portrait talks to him", portraitTalk, portraitTalk ? "talk" : $"none ({_conversationNotice})");
                // ARC-UI-1281 (HK26, 0x567400): with the dialog active the portrait's hover describes nothing.
                var hoversBefore = PortraitHoversForSelfTest;
                FollowerPortraitHovered(follower);
                Fact("a portrait hover with the dialog active runs nothing", ConversationOpen && PortraitHoversForSelfTest == hoversBefore, $"{PortraitHoversForSelfTest - hoversBefore} run(s)");
                await DialogPortraitFacts("");
            }
            if (ConversationOpen) CloseConversation();
            portraits.CloseMenu();
            Settle();

            // 3b. ARC-UI-2069: the same talk under the Original rules: the panel stays drawn and inert under the dialog.
            {
                var priorRules = session.Rules.Mode;
                Require(session.SelectRules(RuleMode.Original) != GameSessionDisposition.Rejected, "Original selected");
                try
                {
                    portraitTalk = false;
                    await ClickPortraitAsync(MouseButton.Left);
                    for (var frame = 0; frame < 600 && !ConversationOpen; frame++) AreaSmokeTalkFrame();
                    portraitTalk = ConversationOpen && _conversationSpeaker?.Row.Key == companion.Key;
                    Fact("Original: left click on his portrait talks to him", portraitTalk, portraitTalk ? "talk" : $"none ({_conversationNotice})");
                    if (portraitTalk) await DialogPortraitFacts("Original: ");
                }
                finally
                {
                    if (ConversationOpen) CloseConversation();
                    session.SelectRules(priorRules);
                    portraits.CloseMenu();
                }
                for (var frame = 0; frame < 3; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                Fact("Original: the panel takes input again once the dialog is closed", portraits.Visible && !portraits.Inert, portraits.Inert ? "inert" : "live");
            }
            Settle();

            // 4. The body: a left press talks (as the portrait's left click does), a right click opens no menu and starts no talk.
            var body = AreaCompanionCommands()?.FirstOrDefault(c => c.Identity == companion.Key) ?? throw new InvalidOperationException("ARC-DEV-263 smoke: his body is not drawn");
            var point = await HoverPointAsync(this, body) ?? throw new InvalidOperationException("ARC-DEV-263 smoke: his body is not pickable in view");
            var leftBody = OpensTalk(() => AreaSmokeClick(point));
            Fact("left press on his body talks to him", leftBody, leftBody ? "talk" : $"none ({_conversationNotice})");
            if (ConversationOpen) CloseConversation();
            Settle();
            body = AreaCompanionCommands()?.FirstOrDefault(c => c.Identity == companion.Key) ?? body;
            point = await HoverPointAsync(this, body) ?? point;
            _worldTestTime = SimulationTime.At(_areaSmokeTime);
            _UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true, Position = point, GlobalPosition = point });
            _UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = false, Position = point, GlobalPosition = point });
            for (var frame = 0; frame < 40; frame++) AreaSmokeTalkFrame();
            Fact("right click on his body opens no menu", !portraits.MenuShown, portraits.MenuShown ? "menu" : "none");
            Fact("right click on his body starts no talk", !ConversationOpen, ConversationOpen ? "talk" : "none");
            if (ConversationOpen) CloseConversation();

            // 5. A right release in the world, which no menu button takes, closes the open menu (0x56AC47).
            await ClickPortraitAsync(MouseButton.Right);
            var opened = portraits.MenuShown;
            _UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true, Position = point, GlobalPosition = point });
            _UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = false, Position = point, GlobalPosition = point });
            Fact("a right release in the world closes the open menu", opened && !portraits.MenuShown, portraits.MenuShown ? "menu" : "closed");
            portraits.CloseMenu();

            // 6. A release over the menu that no menu button takes closes it: a right release over an item, a left release over an item that is not modelled (inventory), the panel itself.
            async Task ReleaseAtAsync(Vector2? at, MouseButton button)
            {
                Require(at is not null, "a menu item is shown");
                GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = button, Pressed = true, Position = at!.Value, GlobalPosition = at.Value });
                GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = button, Pressed = false, Position = at.Value, GlobalPosition = at.Value });
                for (var frame = 0; frame < 3; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }
            await ClickPortraitAsync(MouseButton.Right);
            await ReleaseAtAsync(portraits.MenuItemCentreForSelfTest(FollowerMenuItem.Wait), MouseButton.Right);
            Fact("a right click on a menu item closes the menu", !portraits.MenuShown, portraits.MenuShown ? "menu" : "closed");
            // ARC-UI-1736 (FP9): a left release over the menu's own margins (no row) closes it and is consumed.
            await ClickPortraitAsync(MouseButton.Right);
            var marginAt = portraits.MenuForSelfTest is { } shownMenu ? shownMenu.Rect.Position + new Vector2(2, shownMenu.Rect.Size.Y - 2) : (Vector2?)null;
            await ReleaseAtAsync(marginAt, MouseButton.Left);
            Fact("a left release over the menu's margin closes the menu", !portraits.MenuShown, portraits.MenuShown ? "menu" : "closed");
            portraits.CloseMenu();

            // 7. ARC-UI-1281 (HK26): the pointer entering his portrait runs his examine and describes him; leaving clears; no menu and no dialog.
            foreach (var (name, original, seen) in await ObservePortraitHoverAsync(follower)) Fact(name, original, seen);
            portraits.CloseMenu();

            // 8. ARC-UI-1736 (FP5): the show/hide button hides his frame and shows it again; the GUI takes both presses (no world click, no talk).
            portraits.CloseMenu();
            Fact("one follower: the hide button and no arrows", portraits.PanelForSelfTest is { Shown: true, Count: 1, ToggleArt: 501, UpArt: null, DownArt: null },
                portraits.PanelForSelfTest.ToString());
            await ReleaseAtAsync(portraits.ButtonCentreForSelfTest("toggle"), MouseButton.Left);
            Fact("the hide button removes his frame and shows art 502", portraits.PanelForSelfTest is { Shown: false, ToggleArt: 502 } && !portraits.EntitiesForSelfTest.Contains(follower),
                portraits.PanelForSelfTest.ToString());
            Fact("... and starts no talk", !ConversationOpen, ConversationOpen ? "talk" : "none");
            if (ConversationOpen) CloseConversation();
            await ReleaseAtAsync(portraits.ButtonCentreForSelfTest("toggle"), MouseButton.Left);
            Fact("the show button brings his frame back with art 501", portraits.PanelForSelfTest is { Shown: true, ToggleArt: 501 } && portraits.EntitiesForSelfTest.Contains(follower),
                portraits.PanelForSelfTest.ToString());

            // 9. ARC-UI-1736 (FP7, FP8): the menu stands at the frame's right edge and top with the original's eight rows 18 pixels apart; Wait is normal for a follower in his mind.
            await ClickPortraitAsync(MouseButton.Right);
            {
                var factor = portraits.ScaleForSelfTest;
                var frameTop = portraits.PortraitCentreForSelfTest(follower)!.Value.Y - AreaFollowerFrameLayout.Height * factor / 2;
                var open = portraits.MenuForSelfTest;
                Fact("the menu has eight rows at his frame's top", open is { Rows: 8 } && Math.Abs(open.Value.Rect.Position.Y - frameTop) < 2, open?.ToString() ?? "none");
                var walk = portraits.MenuItemCentreForSelfTest(FollowerMenuItem.Walk); var sheet = portraits.MenuItemCentreForSelfTest(FollowerMenuItem.CharacterSheet);
                Fact("the rows stand 18 pixels apart", walk is not null && sheet is not null && Math.Abs(sheet.Value.Y - walk.Value.Y - 18 * 7 * factor) < 2
                    && portraits.MenuRowSizeForSelfTest(FollowerMenuItem.Walk) == new Vector2(118, 15), $"{walk} {sheet} at factor {factor}, row {portraits.MenuRowSizeForSelfTest(FollowerMenuItem.Walk)}");
                await Capture("menu");
                Fact("Wait and Character Sheet are normal rows", portraits.MenuRowStateForSelfTest(FollowerMenuItem.Wait) == FollowerMenuRowState.Normal
                    && portraits.MenuRowStateForSelfTest(FollowerMenuItem.CharacterSheet) == FollowerMenuRowState.Normal, "row states");
            }
            // ARC-UI-1786 (FP8): the rows' words are the ones read from mes\follower_ui.mes (the project's English only without a game folder), and a row whose message is missing is not drawn.
            {
                var read = portraits.MenuTexts;
                Fact("the rows' words are the follower_ui.mes texts", read is not null && Enum.GetValues<FollowerMenuItem>().All(item => portraits.MenuRowTextForSelfTest(item) == read.Text(item)),
                    $"{read?.Detail}: {string.Join("|", Enum.GetValues<FollowerMenuItem>().Select(item => portraits.MenuRowTextForSelfTest(item)))}");
                portraits.CloseMenu();
                portraits.MenuTexts = AreaFollowerMenuTexts.From(number => number == (int)FollowerMenuItem.StayClose ? null : $"row {number}", "smoke: message 2 missing");
                await ClickPortraitAsync(MouseButton.Right);
                Fact("a row without a message is skipped, the others keep their places and words",
                    portraits.MenuForSelfTest is { Rows: 7 } && portraits.MenuItemCentreForSelfTest(FollowerMenuItem.StayClose) is null
                    && portraits.MenuRowTextForSelfTest(FollowerMenuItem.SpreadOut) == "row 3", $"{portraits.MenuForSelfTest?.Rows} rows");
                portraits.CloseMenu();
                portraits.MenuTexts = read;
                await ClickPortraitAsync(MouseButton.Right);
            }
            var inventoryState = portraits.MenuRowStateForSelfTest(FollowerMenuItem.Inventory);
            // 10. ARC-UI-1736 (FP12): Character Sheet opens his NPC sheet over him (read-only, no plus and minus buttons).
            await ReleaseAtAsync(portraits.MenuItemCentreForSelfTest(FollowerMenuItem.CharacterSheet), MouseButton.Left);
            for (var frame = 0; frame < 40 && !(SheetOpen && _sheetNpc is not null); frame++) AreaSmokeFrame();
            Fact("the Character Sheet row opens his NPC sheet", !portraits.MenuShown && SheetOpen && _sheetNpc is not null, SheetOpen ? "sheet" : $"none ({_sheetNpcStatus})");
            if (SheetOpen) CloseWindowMode(InterfaceMode.Character);
            AreaSmokeFrame();
            // 11. ARC-UI-1736 (FP11): Inventory opens the follower's barter window (no prices) when the can-open test passes (the row is greyed and does nothing otherwise).
            await ClickPortraitAsync(MouseButton.Right);
            await ReleaseAtAsync(portraits.MenuItemCentreForSelfTest(FollowerMenuItem.Inventory), MouseButton.Left);
            if (inventoryState == FollowerMenuRowState.Normal)
            {
                for (var frame = 0; frame < 600 && !BarterOpen; frame++) AreaSmokeFrame();
                Fact("the Inventory row opens his barter window as a follower", BarterOpen && _barter is { Merchant.Follower: true } && _barter.Merchant.Entity == follower,
                    BarterOpen ? "barter" : $"none ({_barterStatus})");
                if (BarterOpen) EndBarter();
            }
            else Fact("the Inventory row is greyed and the click only closes the menu", !portraits.MenuShown && !BarterOpen, "greyed");
            for (var frame = 0; frame < 5; frame++) AreaSmokeFrame();

            // 12a. ARC-UI-1736 (FP6): with a spell cursor open (a spell of the data the pick accepts for him), a left release on his portrait takes him as the target and casts at him
            // (no talk), exactly as a click on his body would. A portrait click is the ordinary click otherwise (step 3).
            for (var frame = 0; frame < 600 && SpellRecordsNow() is null; frame++) AreaSmokeFrame();
            {
                int? chosen = null; uint chosenMask = 0;
                if (SpellRecordsNow() is { } spells && SpellCasterNow() is { } aimCaster && FollowerObjectId(follower) is { } followerId)
                {
                    for (var spell = 0; spell < spells.Length && chosen is null; spell++)
                    {
                        var record = spells[spell];
                        var begin = record.Areas.Length > 0 ? record.Areas[0].Targets : 0;
                        if (!record.Data.Enabled || record.Resist.Aggressive || SpellUiRules.TargetsInventory(begin)) continue;
                        var activation = SpellUiRules.Activate(true, true, begin, false, false, false, session.QueryActionPointCheck(SessionCombatant.Hero, TurnBasedRules.CastSpellCost));
                        if (activation.Kind == SpellActivationKind.Target && aimCaster.PickTarget(SessionSpellCaster.Hero, spell, activation.Mask, followerId, null).Test.Passes) { chosen = spell; chosenMask = (uint)activation.Mask; }
                    }
                }
                if (chosen is not { } aimed) Fact("a spell cursor takes his portrait as the target", true, "skipped (no spell data or no spell the pick accepts for him)");
                else
                {
                    portraits.CloseMenu();
                    ActivateSpell(aimed);
                    Require(_spellTargeting is not null, $"spell {aimed} opened its cursor: {_spellNotice}");
                    var casts = _spellCasts;
                    await ClickPortraitAsync(MouseButton.Left);
                    for (var frame = 0; frame < 20; frame++) AreaSmokeTalkFrame();
                    Fact("a spell cursor takes his portrait as the target and casts", _spellCasts == casts + 1 && !ConversationOpen,
                        $"spell {aimed} mask 0x{chosenMask:X} casts {casts}->{_spellCasts} cursor {(_spellTargeting is null ? "closed" : "open")} {_spellNotice}");
                    if (ConversationOpen) CloseConversation();
                    CancelSpellCast("the smoke ended");
                    Settle();
                }
            }

            // 12b. ARC-UI-1787 (FP6, 0x54EA80): armed lockpicks are interface mode 16, so the portrait takes the follower as the lockpicks' target object, which is no lock: the pick ends,
            // the mode pops and no talk starts. The follower order's target mode (18) is no mode of the dispatcher's three, so there the click is the ordinary one, a talk.
            {
                portraits.CloseMenu();
                Require(OpenTargetMode(InterfaceMode.ItemTarget, _ => _lockpickArmed = null), "the item target mode opened for the lockpicks");
                _lockpickArmed = "smoke:lockpicks";
                await ClickPortraitAsync(MouseButton.Left);
                for (var frame = 0; frame < 20; frame++) AreaSmokeTalkFrame();
                Fact("armed lockpicks take his portrait as a target that is no lock: the pick ends, no talk",
                    _lockpickArmed is null && !TargetModeOpen(InterfaceMode.ItemTarget) && !ConversationOpen && TargetsMatchStack && _portalNotice.Contains("not a door, window or container"),
                    $"armed {_lockpickArmed is not null} mode16 {TargetModeOpen(InterfaceMode.ItemTarget)} talk {ConversationOpen} notice {_portalNotice}");
                if (ConversationOpen) CloseConversation();
                Settle();

                Require(OpenOverlayMode(InterfaceMode.FollowerOrder, _ => _pendingOrder = null), "the follower order mode opened");
                _pendingOrder = (follower, CompanionOrderType.WalkHere);
                await ClickPortraitAsync(MouseButton.Left);
                for (var frame = 0; frame < 600 && !ConversationOpen; frame++) AreaSmokeTalkFrame();
                var orderTalk = ConversationOpen && _conversationSpeaker?.Row.Key == companion.Key;
                Fact("a pending order's mode leaves his portrait click the ordinary one: a talk, the order mode popped by the dialog",
                    orderTalk && _pendingOrder is null && !TargetModeOpen(InterfaceMode.FollowerOrder) && TargetsMatchStack,
                    $"talk {orderTalk} order {_pendingOrder is not null} mode18 {TargetModeOpen(InterfaceMode.FollowerOrder)} {_conversationNotice}");
                if (ConversationOpen) CloseConversation();
                if (_pendingOrder is not null) CancelPendingOrder("the smoke ended");
                Settle();
            }

            // 12. ARC-UI-1736 (FP3, FP5): eight followers scroll by their arrows, six frames from the offset, and clamp at both ends (a panel of its own with synthetic followers).
            {
                var strip = new AreaFollowerPortraits();
                portraits.Visible = false;
                AddChild(strip);
                try
                {
                    strip.Bind(InterfaceTextures(_portraitContent?.Art ?? new Dictionary<int, AreaInterfaceArtLoad>()));
                    strip.Show(Enumerable.Range(0, 8).Select(i => ($"smoke:{i}", $"F{i}", (AreaLife?)new AreaLife(100 - 9 * i, 100, 15 + 6 * i, 60, 0), (Texture2D?)null)).ToArray());
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                    string Frames() => string.Join(",", strip.EntitiesForSelfTest.Select(e => e[^1]));
                    Fact("eight followers: arrows, six frames from 0", strip.PanelForSelfTest is { Shown: true, Offset: 0, Count: 8, ToggleArt: 501, UpArt: 507, DownArt: 508 } && Frames() == "0,1,2,3,4,5", $"{strip.PanelForSelfTest} {Frames()}");
                    await Capture("strip-top");
                    await ReleaseAtAsync(strip.ButtonCentreForSelfTest("down"), MouseButton.Left);
                    await Capture("strip-scrolled");
                    Fact("down scrolls by one follower", strip.PanelForSelfTest is { Offset: 1, UpArt: 506, DownArt: 508 } && Frames() == "1,2,3,4,5,6", $"{strip.PanelForSelfTest} {Frames()}");
                    await ReleaseAtAsync(strip.ButtonCentreForSelfTest("down"), MouseButton.Left);
                    await ReleaseAtAsync(strip.ButtonCentreForSelfTest("down"), MouseButton.Left);
                    Fact("down clamps at count - 6 and shows its end art", strip.PanelForSelfTest is { Offset: 2, DownArt: 509 } && Frames() == "2,3,4,5,6,7", $"{strip.PanelForSelfTest} {Frames()}");
                    await ReleaseAtAsync(strip.ButtonCentreForSelfTest("up"), MouseButton.Left);
                    Fact("up scrolls back", strip.PanelForSelfTest is { Offset: 1 } && Frames() == "1,2,3,4,5,6", $"{strip.PanelForSelfTest} {Frames()}");
                    await ReleaseAtAsync(strip.ButtonCentreForSelfTest("toggle"), MouseButton.Left);
                    Fact("hidden: no frames and no arrows", strip.PanelForSelfTest is { Shown: false, ToggleArt: 502, UpArt: null, DownArt: null } && Frames() == "", $"{strip.PanelForSelfTest} {Frames()}");
                    await ReleaseAtAsync(strip.ButtonCentreForSelfTest("toggle"), MouseButton.Left);
                    Fact("shown again keeps the offset", strip.PanelForSelfTest is { Shown: true, Offset: 1 } && Frames() == "1,2,3,4,5,6", $"{strip.PanelForSelfTest} {Frames()}");
                }
                finally { strip.QueueFree(); portraits.Visible = true; }
            }

            var summary = string.Join("; ", facts);
            GD.Print("ARC-DEV-263 companion-click " + summary);
            if (failures.Count > 0)
            {
                GD.Print("ARC-DEV-263 companion-click smoke FAIL: B33 not as the original (ledger OL34): " + string.Join("; ", failures));
                throw new InvalidOperationException("AREA_COMPANION_CLICK_SMOKE_FAILED");
            }
            GD.Print("ARC-DEV-263 native-smoke companion-click PASS " + System.Text.Json.JsonSerializer.Serialize(new {
                map.RuntimeMap, Script = template.Speaker.ScriptId, Facts = facts.Count,
                Parity = "NOT_VERIFIED (button mapping read in the original executable, OL34; no original-runtime run)" }));
        }
        finally
        {
            if (ConversationOpen) for (var close = 0; close < 4 && ConversationOpen; close++) _conversationPanel!.LeaveForSelfTest.EmitSignal(BaseButton.SignalName.Pressed);
            _followerPortraits?.CloseMenu();
            CancelTalkApproach("the smoke ended");
            if (joined) session.LeaveParty(session.Version, follower, new PartyLeave(Force: true));
            ReleaseSimulatedPadForSelfTest(); _worldFocusSimulationForSelfTest = simulatedFocus;
            _worldFocused = focused; RestoreWorldTestTime(testTime);
            BindAreaCompanions();
            PublishWorldSelection();
        }
    }
}
