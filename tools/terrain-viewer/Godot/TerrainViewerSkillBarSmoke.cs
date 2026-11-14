using Arcanum.Input;
using Arcanum.Presentation.Hud;
using Arcanum.Session;
using Arcanum.Simulation.Hotkeys;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

public partial class TerrainViewerApp
{
    // ARC-UI-1113 native observation, inside the hotkey-bar smoke of the cast stage (the bars show; the hero is on a cast map): the key K turns the
    // Skills toggle down, Spells goes up, and the strip draws the skill pane (background art 274, the four skill-bar buttons with their arts);
    // entering a button names it in the mode's text area (skill_ui.mes 100 + button); a click on each plays 3000 and 3001, becomes the latest recent
    // action and runs the button's press (prowling toggles concealment, pick pocket and disarm traps arm the skill cursor or say why not, repair opens
    // the inventory window); a right drag from a button puts it on a hotkey slot; the Spells pane takes the window back (Skills goes up); the pointer
    // above the strip gives the window to the log while the toggle stays down; K a second time puts the pane away.
    private async Task RunSkillPaneStripSmokeAsync(GameSessionOwner session, Func<int, Task> frames, Func<Vector2, Task> click,
        Func<Vector2, Vector2?, Task> hover, Func<Vector2, MouseButton, bool, Task> button)
    {
        static void Require(bool value, string why) { if (!value) { GD.Print("ARC-UI-1113 skill-pane smoke FAIL " + why); throw new InvalidOperationException("ARC-UI-1113 skill-pane smoke: " + why); } }
        CancelSpellTargeting(null);
        _spellPane?.HidePane();
        await frames(2);
        Require(!_gameBar.IsOn(AreaBarButton.Skills) && !_messageLog.PaneMode, "no pane holds the window at the start");
        HotkeyKey(Key.K);
        await frames(3);
        var view = _skillBarView;
        Require(_gameBar.IsOn(AreaBarButton.Skills) && !_gameBar.IsOn(AreaBarButton.Spells) && _messageLog.PaneMode && view is { Visible: true },
            $"K turned Skills down and the strip draws the skill pane: {_gameBarNotice}");
        Require(view!.BackgroundForSelfTest == Art(SkillPaneRules.BackgroundArt)?[0], "the pane draws the background art 274");
        // ARC-UI-1200: the opt-in generated-chrome part (ARC_AREA_WINDOW_ART): the pane's background draws as the mode under test says.
        if (WindowArtSmokeRequested) { await PaneArtSmokeAsync("skill-pane", () => view.BackgroundForSelfTest, SkillPaneRules.BackgroundArt); await frames(3); }
        Require(_spellBarView is not { Visible: true } && _spellPane?.Shown != true, "the spell pane is away");
        var buttons = Enumerable.Range(0, SkillPaneRules.Buttons).ToArray();
        Require(buttons.All(b => view.ButtonArtForSelfTest(b) is { } art && Art(SkillPaneRules.ButtonArt(b)!.Value)!.Contains(art)), "every skill button draws its art");

        // The buttons lie 63 pixels apart in one row (records 0x5C6C68).
        var centres = buttons.Select(b => view.ButtonCentreForSelfTest(b)!.Value).ToArray();
        Require(centres.Zip(centres.Skip(1)).All(pair => Math.Abs(pair.Second.Y - pair.First.Y) < 0.5f && pair.Second.X > pair.First.X), "the four buttons stand in one row, left to right");

        // Entering a button names it in the mode's text area (from the strip's empty part below the buttons, so the pane keeps the window).
        var log = _gameBarPanel!.MessageLog;
        var texts = _gameBarContent!.Texts;
        foreach (var b in buttons)
        {
            await hover(centres[b], centres[b] + new Vector2(0, 40));
            Require(_messageLog.PaneMode && log.PaneNameForSelfTest == texts.SkillButtonName(b),
                $"entering button {b} names it: '{log.PaneNameForSelfTest}' wanted '{texts.SkillButtonName(b)}'");
        }

        await CaptureSkillPaneAsync(view, centres[1], frames, hover);

        var outcomes = new List<string>();
        foreach (var b in new[] { (int)SkillBarButton.Prowling, (int)SkillBarButton.PickPocket, (int)SkillBarButton.DisarmTraps, (int)SkillBarButton.Repair })
        {
            _gameBarSoundsForSelfTest.Clear();
            var concealed = HeroConcealed;
            await click(centres[b]);
            await frames(1);
            Require(_gameBarSoundsForSelfTest.Select(s => s.Id).SequenceEqual([AreaGameBarLayout.PressSound, AreaGameBarLayout.ReleaseSound]),
                $"button {b} played 3000 then 3001: " + string.Join(",", _gameBarSoundsForSelfTest.Select(s => s.Id)));
            Require(session.QueryHotkeys().Recent[0] == HotkeyEntry.ForSkill((SkillBarButton)b), $"the press of button {b} became the latest recent action: {session.QueryHotkeys().Recent[0]}");
            var outcome = _skillBarOutcome ?? "";
            outcomes.Add($"{(SkillBarButton)b}={outcome}");
            switch ((SkillBarButton)b)
            {
                case SkillBarButton.Prowling:
                    Require(HeroConcealed != concealed || outcome.StartsWith("prowl refused", StringComparison.Ordinal), $"the prowling button toggled concealment: {outcome}");
                    if (HeroConcealed != concealed) { await click(centres[b]); await frames(1); Require(HeroConcealed == concealed, "a second press put concealment back"); }
                    break;
                case SkillBarButton.PickPocket:
                    Require(PickPocketArmed ? outcome == "pick pocket: press an NPC" : outcome.StartsWith("pick pocket:", StringComparison.Ordinal), $"the pick pocket button armed the cursor or said why not: {outcome}");
                    if (PickPocketArmed) CancelPickPocket("the smoke ended");
                    break;
                case SkillBarButton.DisarmTraps:
                    Require(DisarmArmed ? outcome == "disarm traps: press a trapped object" : outcome.StartsWith("disarm traps:", StringComparison.Ordinal), $"the disarm traps button armed the cursor or said why not: {outcome}");
                    if (DisarmArmed) CancelDisarm("the smoke ended");
                    break;
                case SkillBarButton.Repair:
                    Require(InventoryOpen ? outcome.StartsWith("repair: pick", StringComparison.Ordinal) : outcome.StartsWith("repair:", StringComparison.Ordinal), $"the repair button opened the inventory or said why not: {outcome}");
                    if (InventoryOpen) EndInventory();
                    break;
            }
            await frames(2);
            Require(_gameBar.IsOn(AreaBarButton.Skills) && view.Visible, $"the pane stays after button {b}");
        }

        // A right drag from the disarm traps button puts it on slot 5.
        await hover(centres[2], centres[2] + new Vector2(0, 40));
        await button(centres[2], MouseButton.Right, true);
        Require(_hotkeyDrag is { Kind: HotkeyDragSourceKind.SkillButton }, "a right press on the pane's button starts a drag");
        await button(_hotkeyBarView!.SlotCentreForSelfTest(5), MouseButton.Right, false);
        Require(_hotkeyDrag is null && session.QueryHotkeys().Entries[5] == HotkeyEntry.ForSkill(SkillBarButton.DisarmTraps), "the release over slot 5 put the button there: " + _hotkeyNotice);
        session.ClearHotkey(session.Version, 5);

        // The Spells pane takes the window back (Skills goes up and the skill pane is away), then K again.
        ShowSpellPane();
        await frames(3);
        Require(!_gameBar.IsOn(AreaBarButton.Skills) && _gameBar.IsOn(AreaBarButton.Spells) && !view.Visible && log.PaneNameForSelfTest.Length == 0,
            "the Spells pane took the window: Skills is up, the skill pane away and its name gone");
        _spellPane!.HidePane();
        await frames(2);
        HotkeyKey(Key.K);
        await frames(3);
        Require(_gameBar.IsOn(AreaBarButton.Skills) && view.Visible, "K brought the skill pane back");
        // ARC-UI-1924 (IW46): an inventory window's stat hot spot in a pane mode: entering it (subtype 2) writes only the title into the pane's text area; a release over it brings the bar to the
        // message mode (0x5506C0 with 0: Skills goes up, the pane away) and shows the stat lines; then K brings the pane back for the steps below.
        if (_combatHost?.HeroDollRatings(session) is { Detail: not null } statRatings)
        {
            ShowStatLines(new(AreaStatTarget.HeroArmour, statRatings, Release: false));
            await frames(2);
            Require(_messageLog.PaneMode && log.PaneNameForSelfTest == AreaStatLines.Title(texts.Text(AreaStatLines.ArmourTitleMessage), statRatings.Armour),
                $"in a pane mode the pointer over the armour number writes only its title: '{log.PaneNameForSelfTest}'");
            ShowStatLines(new(AreaStatTarget.HeroWeapon, statRatings, Release: true));
            await frames(3);
            Require(!_messageLog.PaneMode && !_gameBar.IsOn(AreaBarButton.Skills) && !view.Visible && _messageLog.Shown is { Kind: AreaMessageKind.StatLines },
                "a release over the weapon number brought the message mode back and showed the stat lines");
            // ARC_AREA_STAT_CAPTURE=PREFIX saves the bottom strip as PREFIX-strip-before.png (the stat lines gone), -strip-weapon.png and -strip-armour.png for the graphics report; never committed.
            if (System.Environment.GetEnvironmentVariable("ARC_AREA_STAT_CAPTURE") is { Length: > 0 } statCapture)
            {
                var strip = _gameBarPanel!.StripRectForSelfTest(AreaBarStrip.Bottom);
                async Task Save(string name)
                {
                    await frames(3);
                    await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                    var path = System.IO.Path.GetFullPath(statCapture) + $"-strip-{name}.png";
                    System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
                    using var image = GetViewport().GetTexture().GetImage();
                    using var region = image?.GetRegion(new Rect2I((int)strip.Position.X, (int)strip.Position.Y, (int)strip.Size.X, (int)strip.Size.Y));
                    if (region is null || region.SavePng(path) != Error.Ok) throw new InvalidOperationException("ARC_AREA_STAT_CAPTURE not saved: " + path);
                }
                await Save("weapon");
                ShowStatLines(new(AreaStatTarget.HeroArmour, statRatings, Release: false));
                await Save("armour");
                _messageLog.ClearHover();
                _gameBarPanel.MessageLog.Render();
                await Save("before");
            }
            HotkeyKey(Key.K);
            await frames(3);
            Require(_gameBar.IsOn(AreaBarButton.Skills) && view.Visible, "K brought the skill pane back after the stat lines");
            outcomes.Add("stat hot spots in a pane mode");
        }
        // The pointer above the strip gives the window to the log; the toggle stays down (ML8).
        await hover(new Vector2(40, 120), null);
        Require(!_messageLog.PaneMode && !view.Visible && _gameBar.IsOn(AreaBarButton.Skills), "the pointer above the strip gave the window to the log; Skills stays down");
        HotkeyKey(Key.K);
        await frames(3);
        Require(!_gameBar.IsOn(AreaBarButton.Skills) && !view.Visible && !_messageLog.PaneMode, "K a second time put the pane away");
        GD.Print($"ARC-UI-1113 native-smoke skill-pane PASS {string.Join("; ", outcomes)}");
    }

    // ARC-UI-1113: with ARC_AREA_SKILLPANE_CAPTURE=PREFIX saves the bottom strip as PREFIX-before.png (Skills down, the pane not drawn: how the window
    // looked before the pane), PREFIX-after.png (the pane drawn) and PREFIX-hover.png (the pointer on the pick pocket button, its name written);
    // never committed (the report's images). Runs inside the smoke, so nothing else reads what it leaves.
    private bool _skillBarHiddenForCapture;

    private async Task CaptureSkillPaneAsync(AreaSkillBarView view, Vector2 pickPocket, Func<int, Task> frames, Func<Vector2, Vector2?, Task> hover)
    {
        if (System.Environment.GetEnvironmentVariable("ARC_AREA_SKILLPANE_CAPTURE") is not { Length: > 0 } capture) return;
        var strip = _gameBarPanel!.StripRectForSelfTest(AreaBarStrip.Bottom);
        async Task Save(string name)
        {
            await frames(3);
            var path = System.IO.Path.GetFullPath(capture) + $"-{name}.png";
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            using var image = GetViewport().GetTexture().GetImage();
            using var region = image?.GetRegion(new Rect2I((int)strip.Position.X, (int)strip.Position.Y, (int)strip.Size.X, (int)strip.Size.Y));
            if (region is null || region.SavePng(path) != Error.Ok) throw new InvalidOperationException("ARC-UI-1113 capture not saved: " + path);
            GD.Print($"ARC-UI-1113 capture {name} {region.GetWidth()}x{region.GetHeight()}");
        }
        // The pointer rests inside the strip below the buttons, so the pane keeps the window (ML8).
        await hover(pickPocket + new Vector2(0, 40), pickPocket + new Vector2(0, 41));
        ClearMessagePaneName();
        _skillBarHiddenForCapture = true;
        await Save("before");
        _skillBarHiddenForCapture = false;
        await Save("after");
        await hover(pickPocket, pickPocket + new Vector2(0, 40));
        await Save("hover");
    }
}
