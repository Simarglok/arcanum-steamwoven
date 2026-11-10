using Arcanum.Data.Area;
using Arcanum.Presentation.Hud;
using Arcanum.Session;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Party;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
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

            // The PC's gauges: fills and numbers from the hero's life (CH7); with the bars the vials' art (PA1–PA3), without them
            // the compact panel at the screen's bottom right (PA5, ARC-UI-024).
            void CheckGauges(string when)
            {
                RefreshFollowerPortraits();
                var life = AreaLife.From(session.QueryHeroLife()!);
                Require(_heroGauges is { ShownForSelfTest: true }, when + ": the PC's gauges are shown");
                var shown = _heroGauges!.ValuesForSelfTest;
                var placed = _heroGauges.PlacementForSelfTest;
                if (placed.InBars)
                {
                    Require(shown.Health == LifeBarRules.GaugeFill(life.Health, life.MaxHealth, LifeBarRules.GaugeHeight)
                        && shown.Fatigue == LifeBarRules.GaugeFill(life.Fatigue, life.MaxFatigue, LifeBarRules.GaugeHeight), $"{when}: gauge fills {shown}");
                    Require(shown.HealthText == LifeBarRules.GaugeNumber(life.Health) && shown.FatigueText == LifeBarRules.GaugeNumber(life.Fatigue), $"{when}: gauge numbers {shown}");
                    Require(shown.Poisoned == life.Poisoned && shown.PoisonText == (life.Poisoned ? LifeBarRules.GaugeNumber(life.Poison) : null), $"{when}: poison {shown}");
                    var art = _heroGauges.VialArtForSelfTest;
                    Require(art == ((int?)(life.Poisoned ? AreaGameBarLayout.PoisonedVialArt : AreaGameBarLayout.HealthVialArt), (int?)AreaGameBarLayout.FatigueVialArt, true),
                        $"{when}: the vials draw the original's art {art} (bars art {_gameBarContent?.Detail})");
                    return;
                }
                var health = LifeBarRules.CompactFill(life.Health, life.MaxHealth, fatigue: false);
                var fatigue = LifeBarRules.CompactFill(life.Fatigue, life.MaxFatigue, fatigue: true);
                Require(shown.Health == health.Rows && shown.Fatigue == fatigue.Rows, $"{when}: compact columns {shown}");
                Require(_heroGauges.CompactForSelfTest.ValuesForSelfTest.Health == health && _heroGauges.CompactForSelfTest.ValuesForSelfTest.Fatigue == fatigue,
                    $"{when}: compact rows {_heroGauges.CompactForSelfTest.ValuesForSelfTest}");
                Require(shown.HealthText == LifeBarRules.CompactNumber(life.Health) && shown.FatigueText == LifeBarRules.CompactNumber(life.Fatigue), $"{when}: compact numbers {shown}");
                Require(shown.Poisoned == life.Poisoned && shown.PoisonText == (life.Poisoned ? LifeBarRules.CompactNumber(life.Poison) : null), $"{when}: compact poison {shown}");
                var view = GetViewport().GetVisibleRect().Size; var factor = AreaGameBarPanel.ScreenFactor(view);
                var origin = new Vector2(AreaGameBarPanel.ScreenLeft(view, factor), view.Y - AreaGameBarLayout.ScreenHeight * factor);
                var (cx, cy, cw, ch) = AreaCompactPanelLayout.Window;
                Require(placed.Health.Position.IsEqualApprox(origin + new Vector2(cx, cy) * factor) && placed.Health.Size.IsEqualApprox(new Vector2(cw, ch) * factor),
                    $"{when}: the compact panel at the screen's bottom right {placed.Health} at factor {factor}");
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
            AreaSmokeFramesUntilConversation(); Require(ConversationOpen, "a conversation with the NPC opened: " + _conversationNotice);
            var joined = session.ForceJoinParty(session.Version, template.Entity);
            Require(joined.Disposition != GameSessionDisposition.Rejected, "joined: " + joined.Failure);
            for (var close = 0; close < 4 && ConversationOpen; close++) _conversationPanel!.LeaveForSelfTest.EmitSignal(BaseButton.SignalName.Pressed);
            Require(!ConversationOpen, "the conversation closed");
            var companion = _areaCompanions?.Companions.SingleOrDefault(c => c.Entity == template.Entity);
            Require(companion is { Standing: CompanionStanding.Following }, "the NPC is a following companion: " + _areaCompanions?.Status());
            for (var i = 0; i < 30; i++) AreaSmokeFrame();

            // Its portrait's bars (CH9), then a hit-point packet that leaves it below 20 %: the red tint.
            // ARC-UI-024: the frame is the original's (art 503, bars 505/616 and 504) in slot 0 at 11, 50, with the follower's
            // stored portrait when it has one that reads.
            var storedPortrait = AreaPortraits.Stored(companion!.Speaker.Row);
            void CheckPortrait(string when, bool tinted)
            {
                RefreshFollowerPortraits();
                var life = CompanionLife(companion!)!.Value;
                var bars = _followerPortraits?.BarsForSelfTest(companion!.Entity);
                Require(bars is { } b && b.Health == LifeBarRules.PortraitFill(LifeBarRules.Percent(life.Health, life.MaxHealth), AreaFollowerPortraits.BarWidth)
                    && b.Fatigue == LifeBarRules.PortraitFill(LifeBarRules.Percent(life.Fatigue, life.MaxFatigue), AreaFollowerPortraits.BarWidth) && b.Poisoned == life.Poisoned,
                    $"{when}: portrait bars {bars} for {life}");
                Require(bars!.Value.Tinted == tinted && LifeBarRules.PortraitTinted(life.Health, life.MaxHealth) == tinted, $"{when}: portrait tint {bars} for {life}");
                var frame = _followerPortraits!.FrameForSelfTest(companion!.Entity);
                Require(frame is { Frame: true } f && f.HealthArt == (life.Poisoned ? AreaFollowerFrameLayout.PoisonedHealthArt : AreaFollowerFrameLayout.HealthArt)
                    && f.FatigueArt == AreaFollowerFrameLayout.FatigueArt, $"{when}: the frame draws the original's art {frame} ({_portraitContent?.Detail})");
                var view = GetViewport().GetVisibleRect().Size; var factor = AreaGameBarPanel.ScreenFactor(view);
                Require(frame!.Value.Rect.Position.IsEqualApprox(new Vector2(AreaGameBarPanel.ScreenLeft(view, factor) + AreaFollowerFrameLayout.X * factor, AreaFollowerFrameLayout.FirstY * factor))
                    && frame.Value.Rect.Size.IsEqualApprox(new Vector2(AreaFollowerFrameLayout.Width, AreaFollowerFrameLayout.Height) * factor), $"{when}: frame place {frame.Value.Rect} at factor {factor}");
                // The stage's follower (on bates mansion lev 2) has a portrait: it must read and be drawn.
                var file = storedPortrait is { } id ? _portraitContent?.Portraits.GetValueOrDefault(id) : null;
                Require(frame.Value.Portrait is not null && file is { Status: AreaPortraitStatus.Ready }, $"{when}: portrait {storedPortrait} drawn={frame.Value.Portrait is not null} file={file?.Status} {file?.Detail}");
                Require(_hud is null || !_hud.Rect.Intersects(frame.Value.Rect), $"{when}: the diagnostic HUD {_hud?.Rect} starts right of the frame {frame.Value.Rect}");
                Require(frame.Value.PortraitRect.Position.IsEqualApprox(frame.Value.Rect.Position + new Vector2(AreaFollowerFrameLayout.ImageX, AreaFollowerFrameLayout.ImageY) * factor)
                    && frame.Value.PortraitRect.Size.IsEqualApprox(new Vector2(AreaFollowerFrameLayout.PortraitSize, AreaFollowerFrameLayout.PortraitSize) * factor),
                    $"{when}: the portrait is drawn at 4, 4 at 32 × 32: {frame.Value.PortraitRect}");
            }
            var start = CompanionLife(companion!)!.Value;
            CheckPortrait("joined", LifeBarRules.PortraitTinted(start.Health, start.MaxHealth));
            var target = host.Build(session, CurrentAreaMap, companion!.Entity).Combat!.Target;
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
                Require(bars is { } b && b.Health == LifeBarRules.HoverFill(LifeBarRules.Percent(life.Health, life.MaxHealth), AreaHoverLifeBars.BarWidth)
                    && b.Fatigue == LifeBarRules.HoverFill(LifeBarRules.Percent(life.Fatigue, life.MaxFatigue), AreaHoverLifeBars.BarWidth)
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
            RefreshFollowerPortraits();
            var placed = _heroGauges!.PlacementForSelfTest;
            Require(placed.InBars && _heroGauges.ShownForSelfTest, "the gauges moved into the bars");
            Require(placed.Health.Position.IsEqualApprox(origin + new Vector2(14, 472) * factor) && placed.Health.Size.IsEqualApprox(new Vector2(28, 88) * factor)
                && placed.Fatigue.Position.IsEqualApprox(origin + new Vector2(754, 473) * factor), $"vial places {placed} at factor {factor}");
            CheckGauges("in the bars");
            // ARC-UI-024, ARC-UI-057: hovering the follower with the bars drawn puts its portrait, name and bars in the message window's
            // object window (PA6, CH8, OH6, OH7); the floating tooltip is not shown.
            // The other NPC's hover framed that NPC (HoverPointAsync moves the camera to its body, possibly far from the PC): the camera goes
            // back on the PC, where the follower stands within a few cells, before the follower is looked for.
            PublishWorldSelection(); _camera!.ResetWorld(WorldAnchor, WorldViewport, 1);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var barsPoint = ScreenPoint(companion.Speaker.Row.Key);
            Require(barsPoint is not null, "the follower is drawn on screen with the bars");
            HoverAtForSelfTest(barsPoint);
            var messagePortrait = _gameBarPanel.HoverPortraitForSelfTest;
            var portraitShown = FollowerPortrait(companion) is not null;
            var window = HoverWindowForSelfTest;
            Require(portraitShown && messagePortrait.Image is not null && window is { Kind: AreaMessageKind.Object, Icon: null }
                && window.Title == _hoverIndex!.Name(companion.Speaker.Row.Key) && _gameBarPanel.MessageLog.Visible && HoverTextForSelfTest is null,
                $"the message window shows the follower's portrait ({portraitShown}) and name: {messagePortrait} {window} tooltip '{HoverTextForSelfTest}'");
            if (portraitShown)
                Require(messagePortrait.Rect.Position.IsEqualApprox(origin + new Vector2(AreaGameBarLayout.HoverImage.X, AreaGameBarLayout.HoverImage.Y) * factor)
                    && messagePortrait.Rect.Size.IsEqualApprox(new Vector2(AreaGameBarLayout.HoverPortraitSize, AreaGameBarLayout.HoverPortraitSize) * factor),
                    $"the portrait at 217, 510: {messagePortrait.Rect}");
            void CheckWindowBars(AreaLife life, bool pcSide, string what)
            {
                var bars = HoverWindowBarsForSelfTest;
                Require(bars is { } b && b.Health == LifeBarRules.HoverFill(LifeBarRules.Percent(life.Health, life.MaxHealth), AreaWindowLifeBars.BarWidth)
                    && b.Fatigue == LifeBarRules.HoverFill(LifeBarRules.Percent(life.Fatigue, life.MaxFatigue), AreaWindowLifeBars.BarWidth)
                    && b.HealthText == LifeBarRules.HoverText(life.Health, life.MaxHealth, pcSide)
                    && b.FatigueText == LifeBarRules.HoverText(life.Fatigue, life.MaxFatigue, pcSide) && b.Poisoned == life.Poisoned,
                    $"{what}: window bars {bars} for {life} pcSide={pcSide}");
            }
            CheckWindowBars(wounded, pcSide: true, "follower in the window");
            // ARC-UI-109 (OH7, OH11): the second line, "Level: N" at the right and the reaction (not concealed, no spell flag) at the left.
            void CheckSecondLine(AreaObjectKey key, AreaLife life, string what)
            {
                var entity = HoverEntity(key);
                var reaction = entity is null ? null : _aiHost?.StandardReaction(entity);
                var second = HoverWindowForSelfTest;
                Require(second is not null && second.SecondValue.Contains(": ") && second.SecondValue.Length > 3 && (life.Health <= 0 ? second.SecondLine.Length > 0
                    : reaction is null || second.SecondLine.Contains($": {reaction} (")),
                    $"{what}: second line left '{second?.SecondLine}' right '{second?.SecondValue}' reaction {reaction}");
            }
            CheckSecondLine(companion.Speaker.Row.Key, wounded, "follower");
            await Capture("bars");
            if (other is { } hovered)
            {
                var npcPoint = await HoverPointAsync(this, hovered.Command);
                HoverAtForSelfTest(npcPoint);
                var npcWindow = HoverWindowForSelfTest;
                Require(npcWindow is { Kind: AreaMessageKind.Object, Icon: not null } && npcWindow.Title == _hoverIndex!.Name(hovered.Command.Identity!.Value)
                    && _gameBarPanel.HoverPortraitForSelfTest.Image is null, $"the other NPC's image and name in the window: {npcWindow}");
                CheckWindowBars(hovered.Life, pcSide: false, "other NPC in the window");
                CheckSecondLine(hovered.Command.Identity!.Value, hovered.Life, "other NPC");
                await Capture("bars-other");
            }
            HoverReleaseForSelfTest(); HoverAtForSelfTest(null);
            Require(_gameBarPanel.HoverPortraitForSelfTest.Image is null && HoverWindowForSelfTest is null && HoverWindowBarsForSelfTest is null
                && _gameBarPanel.MessageLog.Visible, "no hover, no portrait, window or bars; the message log back");
            GD.Print("ARC-UI-057 native-smoke hover-window critter PASS");
            HoverReleaseForSelfTest();
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
                    Portrait = _followerPortraits!.BarsForSelfTest(companion.Entity).ToString(), Hover = followerBars.ToString(),
                    StoredPortrait = storedPortrait, PortraitDrawn = _followerPortraits.FrameForSelfTest(companion.Entity)?.Portrait is not null, MessagePortrait = portraitShown },
                OtherNpc = other is { } o ? new { Life = o.Life.ToString(), Text = otherText } : null,
                Art = new { Vials = _gameBarContent?.Detail, Frames = _portraitContent?.Detail },
                Parity = "NOT_VERIFIED (original art drawn; no screenshot comparison with the original)" }));
        }
        finally
        {
            _lifeBarsForSelfTest = false; _gameBarForSelfTest = false; _heroGaugeStamp = null;
            ReleaseSimulatedPadForSelfTest(); _worldFocusSimulationForSelfTest = simulatedFocus;
            _worldFocused = focused; RestoreWorldTestTime(testTime);
        }
    }
}
