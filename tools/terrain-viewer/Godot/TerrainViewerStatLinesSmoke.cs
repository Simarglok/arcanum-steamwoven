using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

public partial class TerrainViewerApp
{
    /// <summary>
    /// ARC-UI-1924 (ledger IW46): the stat hot spots of an inventory window on the original art (the real-data smokes <c>inv-ui</c> for the own window, <c>loot-worn</c> for a loot window). A release over the
    /// hero's weapon number shows the weapon stat lines in the message window, the pointer entering the armour number shows the armour stat lines, and in a pane mode the pointer writes only the title
    /// into the pane's text area while a release brings the window back to the message mode. In a window of modes 1 to 4 the hero's hot spots are hidden until its doll toggle is set (and the other party's
    /// until its doll view is on). Returns a line for the log.
    /// </summary>
    private async Task<string> StatHotSpotsStepAsync(AreaInventoryGridArea grid, AreaInventoryView view, string window)
    {
        void Require(bool value, string why)
        {
            if (value) return;
            GD.Print($"ARC-UI-1924 stat hot spots ({window}) FAIL: " + why);
            throw new InvalidOperationException($"ARC-UI-1924 stat hot spots ({window}): " + why);
        }
        if (grid.Look is null) return $"stat hot spots not driven ({window}: no original-look window)";
        if (view.HeroRatings?.Detail is null) return $"stat hot spots not driven ({window}: the hero's ratings carry no detail)";
        // A window shown again after the hero doll smoke's open (ResetLoot) clears its toggles when it next draws; let it draw before the toggle is set.
        await StatFrames(3);
        var mode = grid.LookMode;
        var sided = AreaHeroDollToggle.TogglesShown(mode);
        Vector2 Inside((int X, int Y, int Width, int Height) spot) => new(spot.X + 2, spot.Y + 2);
        var weaponSpot = Inside(AreaStatHotSpots.HeroSpot(mode, true));
        var armourSpot = Inside(AreaStatHotSpots.HeroSpot(mode, false));
        var texts = _gameBarContent?.Texts ?? AreaGameBarTexts.Project("self-test");
        var heroDoll = grid.HeroDoll;
        if (!sided && System.Environment.GetEnvironmentVariable("ARC_AREA_STAT_CAPTURE") is { Length: > 0 } capture) await CaptureStatButtonsAsync(grid, capture);
        if (sided)
        {
            Require(grid.StatAt(weaponSpot) is null && grid.StatAt(armourSpot) is null, "the hero's hot spots are hidden with the doll toggle clear");
            if (!grid.HeroTogglesShown) return $"stat hot spots not driven ({window}: the doll toggle's art does not read)";
            grid.SetHeroDoll(true);
        }
        try
        {
            Require(grid.StatAt(weaponSpot) == AreaStatTarget.HeroWeapon && grid.StatAt(armourSpot) == AreaStatTarget.HeroArmour && grid.StatAt(new(1, 1)) is null, "the hero's hot spots are hit over the numbers");
            // The pointer entering a spot (subtype 2) shows its lines at once, in the message mode, and does not leave the mode.
            LastStatLinesForSelfTest = null;
            grid.StatHoverAt(armourSpot);
            Require(LastStatLinesForSelfTest is { Request: { Target: AreaStatTarget.HeroArmour, Release: false }, Entry.Kind: AreaMessageKind.StatLines } hovered
                && ReferenceEquals(_messageLog.Shown, hovered.Entry) && !_messageLog.PaneMode, "the pointer over the armour number shows the armour stat lines");
            Require(LastStatLinesForSelfTest!.Value.Entry.Title == AreaStatLines.Title(texts.Text(AreaStatLines.ArmourTitleMessage), view.HeroRatings.Armour), "titled with the displayed armour rating");
            grid.StatHoverAt(new(1, 1));
            LastStatLinesForSelfTest = null;
            grid.StatHoverAt(armourSpot);
            Require(LastStatLinesForSelfTest is not null, "entering again shows them again");
            // A press alone does nothing; a release over the pressed spot shows the weapon's lines.
            LastStatLinesForSelfTest = null;
            grid.PressAt(weaponSpot);
            await StatFrames(2);
            Require(LastStatLinesForSelfTest is null, "a press alone shows nothing");
            grid.ReleaseAt(new(700, 380)); // away from the spot and from the round view's square (a release there closes the window)
            await StatFrames(2);
            Require(LastStatLinesForSelfTest is null, "a release away from the spot shows nothing");
            grid.PressAt(weaponSpot); grid.ReleaseAt(weaponSpot);
            for (var frame = 0; frame < 6 && LastStatLinesForSelfTest is null; frame++) await StatFrames(1);
            Require(LastStatLinesForSelfTest is { Request: { Target: AreaStatTarget.HeroWeapon, Release: true }, Entry.Kind: AreaMessageKind.StatLines } released
                && ReferenceEquals(_messageLog.Shown, released.Entry), "a release over the weapon number shows the weapon stat lines (last " + LastStatLinesForSelfTest?.Request.Target + " release "
                + LastStatLinesForSelfTest?.Request.Release + ", pane " + _messageLog.PaneMode + ", shown " + _messageLog.Shown?.Kind + ")");
            var weapon = LastStatLinesForSelfTest!.Value.Entry;
            Require(weapon.Icon == 675 && weapon.StatCells is not null && weapon.Title == AreaStatLines.Title(texts.Text(AreaStatLines.WeaponTitleMessage), view.HeroRatings.Damage), "titled with the displayed damage rating, art 675");
            var pane = "not driven";
            if (!sided)
            {
                // A pane mode (Skills): the pointer writes only the title; the release brings the message mode back (0x5506C0 with 0).
                ToggleSkillsPane();
                await StatFrames(3);
                if (_messageLog.PaneMode)
                {
                    LastStatLinesForSelfTest = null;
                    grid.StatHoverAt(new(1, 1));
                    grid.StatHoverAt(armourSpot);
                    Require(_messageLog.PaneName is { } named && named.Name == LastStatLinesForSelfTest?.Entry.Title, "in a pane mode the pointer writes only the title into the pane's text area");
                    grid.PressAt(weaponSpot); grid.ReleaseAt(weaponSpot);
                    await StatFrames(3);
                    Require(!_messageLog.PaneMode && !_gameBar.IsOn(AreaBarButton.Skills) && _messageLog.Shown is { Kind: AreaMessageKind.StatLines }, "a release brought the bar to the message mode and showed the lines");
                    pane = "title in the pane, release back to messages";
                }
                else
                {
                    pane = $"not driven (skills on={_gameBar.IsOn(AreaBarButton.Skills)}, bars={_gameBarPanel is { Visible: true }}, stripArt={SkillPaneInStrip})";
                    ToggleSkillsPane();
                }
            }
            return $"stat hot spots shown (weapon {weapon.Title}, hover armour, pane {pane})";
        }
        finally
        {
            if (sided) grid.SetHeroDoll(heroDoll);
            grid.StatHoverAt(new(1, 1));
        }
    }

    /// <summary>ARC_AREA_STAT_CAPTURE=PREFIX saves the own window as PREFIX-window-before.png (without the hot spots' arts, as before ARC-UI-1924) and PREFIX-window-after.png (with them) for the graphics report; never committed.</summary>
    private async Task CaptureStatButtonsAsync(AreaInventoryGridArea grid, string prefix)
    {
        async Task Shot(string name)
        {
            await StatFrames(2);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            var path = System.IO.Path.GetFullPath(prefix) + $"-window-{name}.png";
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            using var image = GetViewport().GetTexture().GetImage();
            if (image is null || image.SavePng(path) != Error.Ok) throw new InvalidOperationException("ARC_AREA_STAT_CAPTURE not saved: " + path);
        }
        var look = grid.Look!;
        grid.SetLook(look with { Art = look.Art.Where(entry => !AreaStatHotSpots.ArtNumbers.Contains(entry.Key)).ToDictionary(entry => entry.Key, entry => entry.Value) });
        await Shot("before");
        grid.SetLook(look);
        await Shot("after");
    }

    private async Task StatFrames(int count)
    {
        for (var frame = 0; frame < count; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }
}
