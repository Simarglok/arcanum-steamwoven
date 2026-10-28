using Arcanum.Data.Area;
using Arcanum.Presentation.Hud;
using Arcanum.Session;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Party;
using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-UI-020 native observation (map mode with --hero, the "life" stage alone): the PC's gauges show the hero's values and
// numbers and, once a poison packet lands, the poisoned colour and the poison level; a drawn living speaker with follow facts
// joins (the session's forced join inside a conversation, as the ARC-WORLD-037 smoke does), its portrait shows its bars, and a
// hit-point packet that leaves it below 20 % turns the portrait red; hovering it shows "current/maximum", hovering another NPC
// with combat values shows a percent; with the interface bars drawn the gauges fill their vials. The stage leaves the hero poisoned
// and the NPC joined and wounded. ARC_AREA_LIFE_CAPTURE saves in-app readbacks to the caller's ignored path.
public partial class TerrainViewerApp
{
    private async Task RunAreaLifeBarsSmokeAsync()
    {
        if (_areaSession is not { } session || session.QueryHero() is null || _combatHost is not { } host || _readyArea is not { } area
            || _conversationHost is not { PartyWired: true } || _worldActor.Session is not AreaGridSession grid)
        { GD.Print($"ARC-UI-020 native-smoke life NONE map={_readyMapName} (needs a hero, combat values and the session party)"); return; }
        static void Require(bool value, string why)
        {
            if (value) return;
            GD.Print("ARC-UI-020 life smoke FAIL: " + why);
            throw new InvalidOperationException("AREA_LIFE_SMOKE_FAILED");
        }
        var capture = System.Environment.GetEnvironmentVariable("ARC_AREA_LIFE_CAPTURE");
        async Task Capture(string name)
        {
            if (string.IsNullOrWhiteSpace(capture)) return;
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            using var image = GetViewport().GetTexture().GetImage();
            Require(image.SavePng($"{capture}-{name}.png") == Error.Ok, "capture saved");
        }
        var map = new AreaMapRuntime(area.Snapshot, grid.Snapshot, grid.Chart);
        var simulatedFocus = _worldFocusSimulationForSelfTest; var focused = _worldFocused; var testTime = _worldTestTime;
        _worldFocusSimulationForSelfTest = true; _lifeBarsForSelfTest = true; _heroGaugeStamp = null;
        try
        {
            _worldFocused = true; SelectSimulatedPadForSelfTest();
            for (var i = 0; i < 600 && _hoverNames is null; i++) { await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); AdoptHoverNames(); }

            // The PC's gauges: fills and numbers from the hero's life (CH7).
            void CheckGauges(string when)
            {
                RefreshFollowerPortraits();
                var life = AreaLife.From(session.QueryHeroLife()!);
                Require(_heroGauges is { ShownForSelfTest: true }, when + ": the PC's gauges are shown");
                var shown = _heroGauges!.ValuesForSelfTest;
                Require(shown.Health == LifeBarRules.GaugeFill(life.Health, life.MaxHealth, LifeBarRules.GaugeHeight)
                    && shown.Fatigue == LifeBarRules.GaugeFill(life.Fatigue, life.MaxFatigue, LifeBarRules.GaugeHeight), $"{when}: gauge fills {shown}");
                Require(shown.HealthText == LifeBarRules.GaugeNumber(life.Health) && shown.FatigueText == LifeBarRules.GaugeNumber(life.Fatigue), $"{when}: gauge numbers {shown}");
                Require(shown.Poisoned == life.Poisoned && shown.PoisonText == (life.Poisoned ? LifeBarRules.GaugeNumber(life.Poison) : null), $"{when}: poison {shown}");
            }
            CheckGauges("start");
            var heroBefore = AreaLife.From(session.QueryHeroLife()!);
            var poisoned = session.ApplyDamage(session.Version, SessionLifeTarget.Hero, new LifeDamage { Poison = 12, Fatigue = 3 });
            Require(poisoned.Disposition != GameSessionDisposition.Rejected, "the poison packet landed: " + poisoned.Failure);
            var heroAfter = AreaLife.From(session.QueryHeroLife()!);
            Require(heroAfter.Poisoned, "the hero is poisoned");
            CheckGauges("poisoned");

            // A drawn living speaker with follow facts joins; the PC stands next to it so both are on screen.
            var template = _companionTemplates.FirstOrDefault(t => t.Home.RuntimeMap == CurrentAreaMap && !SpeakerDead(t.Speaker)
                && session.QueryCompanion(t.Entity).Standing == CompanionStanding.Free && AreaLife.Of(session, host, CurrentAreaMap, t.Entity) is not null);
            Require(template is not null, $"a free living speaker with combat values (templates {_companionTemplates.Count})");
            AreaSmokePlace(map, AreaSmokeStandable(map, template!.HomeOrigin, 2));
            BeginConversation(template.Speaker);
            Require(ConversationOpen, "a conversation with the NPC opened: " + _conversationNotice);
            var joined = session.ForceJoinParty(session.Version, template.Entity);
            Require(joined.Disposition != GameSessionDisposition.Rejected, "joined: " + joined.Failure);
            for (var close = 0; close < 4 && ConversationOpen; close++) _conversationPanel!.LeaveForSelfTest.EmitSignal(BaseButton.SignalName.Pressed);
            Require(!ConversationOpen, "the conversation closed");
            var companion = _areaCompanions?.Companions.SingleOrDefault(c => c.Entity == template.Entity);
            Require(companion is { Standing: CompanionStanding.Following }, "the NPC is a following companion: " + _areaCompanions?.Status());
            for (var i = 0; i < 30; i++) AreaSmokeFrame();

            // Its portrait's bars (CH9), then a hit-point packet that leaves it below 20 %: the red tint.
            void CheckPortrait(string when, bool tinted)
            {
                RefreshFollowerPortraits();
                var life = CompanionLife(companion!)!.Value;
                var bars = _followerPortraits?.BarsForSelfTest(companion!.Entity);
                Require(bars is { } b && b.Health == LifeBarRules.PortraitFill(life.HealthPercent, AreaFollowerPortraits.BarWidth)
                    && b.Fatigue == LifeBarRules.PortraitFill(life.FatiguePercent, AreaFollowerPortraits.BarWidth) && b.Poisoned == life.Poisoned,
                    $"{when}: portrait bars {bars} for {life}");
                Require(bars!.Value.Tinted == tinted && LifeBarRules.PortraitTinted(life.Health, life.MaxHealth) == tinted, $"{when}: portrait tint {bars} for {life}");
            }
            var start = CompanionLife(companion!)!.Value;
            CheckPortrait("joined", LifeBarRules.PortraitTinted(start.Health, start.MaxHealth));
            var target = host.Build(CurrentAreaMap, companion!.Entity).Combat!.Target;
            var low = Math.Max(1, start.MaxHealth * 15 / 100);
            Require(LifeBarRules.PortraitTinted(low, start.MaxHealth) && start.Health > low, $"the follower can be hit to {low} of {start.MaxHealth} ({start.Health} now)");
            var hit = session.ApplyDamage(session.Version, target, new LifeDamage { HitPoints = start.Health - low });
            Require(hit.Disposition != GameSessionDisposition.Rejected, "the hit landed: " + hit.Failure);
            var wounded = CompanionLife(companion)!.Value;
            Require(wounded.Health == low, $"the follower has {low} hit points ({wounded})");
            CheckPortrait("wounded", true);

            // Hovering it: current/maximum (CH8); hovering another NPC with combat values: a percent.
            PublishWorldSelection(); _camera!.ResetWorld(WorldAnchor, WorldViewport, 1);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Vector2? ScreenPoint(AreaObjectKey key)
            {
                // The camera is on the PC and the follower stands within a few cells of it: the window's middle part is searched.
                var size = GetViewport().GetVisibleRect().Size; var hud = _hud!.Rect;
                for (var y = Math.Max(0, (int)size.Y / 2 - 240); y < Math.Min((int)size.Y, (int)size.Y / 2 + 240); y += 2)
                for (var x = Math.Max(0, (int)size.X / 2 - 320); x < Math.Min((int)size.X, (int)size.X / 2 + 320); x += 2)
                {
                    var p = new Vector2(x + .5f, y + .5f);
                    if (!hud.HasPoint(p) && AreaObjectAtScreen(p) == key) return p;
                }
                return null;
            }
            void CheckHover(AreaLife life, bool pcSide, string what)
            {
                var bars = HoverBarsForSelfTest;
                Require(bars is { } b && b.Health == LifeBarRules.HoverFill(life.HealthPercent, AreaHoverLifeBars.BarWidth)
                    && b.Fatigue == LifeBarRules.HoverFill(life.FatiguePercent, AreaHoverLifeBars.BarWidth)
                    && b.HealthText == LifeBarRules.HoverText(life.Health, life.MaxHealth, pcSide)
                    && b.FatigueText == LifeBarRules.HoverText(life.Fatigue, life.MaxFatigue, pcSide) && b.Poisoned == life.Poisoned,
                    $"{what}: hover bars {bars} for {life} pcSide={pcSide}");
            }
            var followerPoint = ScreenPoint(companion.Speaker.Row.Key);
            Require(followerPoint is not null, "the follower is drawn on screen");
            HoverAtForSelfTest(followerPoint);
            Require(HoverKeyForSelfTest == companion.Speaker.Row.Key, "the follower is hovered");
            CheckHover(wounded, pcSide: true, "follower");
            var followerBars = HoverBarsForSelfTest!.Value;
            await Capture("follower");

            (AreaDrawCommand Command, AreaLife Life)? other = null; Vector2? otherPoint = null;
            foreach (var command in area.Plan.Commands.Where(c => c.Identity is { } id && id != companion.Speaker.Row.Key))
            {
                if (host.Target(session, CurrentAreaMap, command.Identity!.Value) is not { Dead: false, Follower: false, Build.Combat: not null } npc
                    || AreaLife.Of(session, host, CurrentAreaMap, npc.Entity) is not { } life || IsAreaCompanion(command.Identity!.Value)) continue;
                otherPoint = await HoverPointAsync(this, command);
                if (otherPoint is null) continue;
                other = (command, life); break;
            }
            Require(other is not null, "another living NPC with combat values is drawn on screen");
            string? otherText = null;
            if (other is { } found)
            {
                HoverAtForSelfTest(otherPoint);
                Require(HoverKeyForSelfTest == found.Command.Identity, "the other NPC is hovered");
                CheckHover(found.Life, pcSide: false, "other NPC");
                otherText = HoverBarsForSelfTest!.Value.HealthText;
                Require(otherText.EndsWith('%'), "a non-follower reads a percent: " + otherText);
                await Capture("other");
            }
            HoverReleaseForSelfTest();
            // With the interface bars drawn (ARC-UI-018) the gauges fill the bottom strip's vials at the original's places, scaled with it.
            _gameBarForSelfTest = true;
            for (var i = 0; i < 120 && _gameBarPanel is not { Visible: true }; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Require(_gameBarPanel is { Visible: true }, "the interface bars are drawn: " + _gameBarNotice);
            for (var i = 0; i < 3; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var view = GetViewport().GetVisibleRect().Size; var factor = _gameBarPanel!.Factor;
            var origin = new Vector2(MathF.Round((view.X - AreaGameBarLayout.ScreenWidth * factor) / 2), view.Y - AreaGameBarLayout.ScreenHeight * factor);
            var placed = _heroGauges!.PlacementForSelfTest;
            Require(placed.InBars && _heroGauges.ShownForSelfTest, "the gauges moved into the bars");
            Require(placed.Health.Position.IsEqualApprox(origin + new Vector2(14, 472) * factor) && placed.Health.Size.IsEqualApprox(new Vector2(28, 88) * factor)
                && placed.Fatigue.Position.IsEqualApprox(origin + new Vector2(754, 473) * factor), $"vial places {placed} at factor {factor}");
            CheckGauges("in the bars");
            await Capture("bars");
            // A window the bars stay over (the inventory, by its key) keeps the gauges in the vials.
            void InventoryKey()
            {
                InputWorld(new InputEventKey { Keycode = Key.I, PhysicalKeycode = Key.I, Unicode = 'i', Pressed = true });
                InputWorld(new InputEventKey { Keycode = Key.I, PhysicalKeycode = Key.I, Unicode = 'i', Pressed = false });
            }
            InventoryKey();
            for (var i = 0; i < 3; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            RefreshFollowerPortraits();
            Require(InventoryOpen && _gameBarPanel is { Visible: true } && _heroGauges.ShownForSelfTest && _heroGauges.PlacementForSelfTest.InBars,
                $"with the inventory open the gauges stay in the vials (inventory {InventoryOpen}, bars {_gameBarPanel?.Visible})");
            InventoryKey();
            for (var i = 0; i < 3; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Require(!InventoryOpen, "the inventory closed");
            _gameBarForSelfTest = false;
            for (var i = 0; i < 120 && _gameBarPanel is { Visible: true }; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            for (var i = 0; i < 3; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Require(!_heroGauges.PlacementForSelfTest.InBars && _heroGauges.ShownForSelfTest, "without the bars the gauges are in their panel again");
            // A window hides the gauges.
            ToggleWorldSettings();
            RefreshFollowerPortraits();
            Require(_heroGauges?.ShownForSelfTest == false, "an open window hides the PC's gauges");
            ToggleWorldSettings();
            RefreshFollowerPortraits();
            Require(_heroGauges?.ShownForSelfTest == true, "closing it shows them again");
            GD.Print("ARC-UI-020 native-smoke life PASS " + System.Text.Json.JsonSerializer.Serialize(new {
                Map = _readyMapName,
                Hero = new { Before = heroBefore.ToString(), After = heroAfter.ToString(), Gauges = _heroGauges!.ValuesForSelfTest.ToString(), BarFactor = factor },
                Follower = new { template.Speaker.ScriptId, Start = start.ToString(), Wounded = wounded.ToString(),
                    Portrait = _followerPortraits!.BarsForSelfTest(companion.Entity).ToString(), Hover = followerBars.ToString() },
                OtherNpc = other is { } o ? new { Life = o.Life.ToString(), Text = otherText } : null,
                Parity = "NOT_VERIFIED (no original interface art)" }));
        }
        finally
        {
            _lifeBarsForSelfTest = false; _gameBarForSelfTest = false; _heroGaugeStamp = null;
            ReleaseSimulatedPadForSelfTest(); _worldFocusSimulationForSelfTest = simulatedFocus;
            _worldFocused = focused; RestoreWorldTestTime(testTime);
        }
    }
}
