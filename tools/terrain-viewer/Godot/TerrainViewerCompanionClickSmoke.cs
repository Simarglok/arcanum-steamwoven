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
            }
            if (ConversationOpen) CloseConversation();
            portraits.CloseMenu();
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
            await ClickPortraitAsync(MouseButton.Right);
            await ReleaseAtAsync(portraits.MenuItemCentreForSelfTest(FollowerMenuItem.Inventory), MouseButton.Left);
            Fact("a left click on an unmodelled menu item closes the menu", !portraits.MenuShown, portraits.MenuShown ? "menu" : "closed");
            portraits.CloseMenu();

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
