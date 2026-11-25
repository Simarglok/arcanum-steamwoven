using Godot;

namespace Arcanum.TerrainViewer;

// ARC-UI-3136 native observation (a step of the "hover" stage): a control's hover hint is drawn in the original's text font (art 229) by
// AreaArtTip, not by Godot's tooltip popup. With the fonts read, entering a control with a hint shows the hint after the half-second delay,
// leaving hides it, and no Godot tooltip text is set (`TooltipText` stays empty). ARC_AREA_HOVER_WINDOW_CAPTURE saves a readback of the shown
// hint beside the hover-window captures. Without the fonts (no game folder) the step reports "none": nothing is drawn.
public partial class TerrainViewerApp
{
    private async Task<string> ArtTipStageAsync(Action<bool, string> require)
    {
        if (TextFonts() is null) return "art-tip=none";
        const string Hint = "Dexterity: 6\nMinimum level: 3";
        var layer = new CanvasLayer { Name = "ArtTipSmoke", Layer = 50 };
        var button = new Button { Position = new Vector2(60, 60), Size = new Vector2(80, 28), FocusMode = Control.FocusModeEnum.None };
        layer.AddChild(button); AddChild(layer);
        try
        {
            AreaArtTip.Set(button, Hint);
            require(AreaArtTip.Get(button) == Hint && button.TooltipText.Length == 0, "the hint is the control's own and Godot's tooltip text stays empty");
            AreaArtTipPopup.Enter(button);
            // Time, not frames: the popup counts the frames' delta (0.5 s), and an automated host runs at any rate.
            var start = Time.GetTicksMsec();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            require(Time.GetTicksMsec() - start > 400 || AreaArtTip.ShownForSelfTest(GetTree()).Length == 0, "the hint waits for its delay");
            while (Time.GetTicksMsec() - start < 5000 && AreaArtTip.ShownForSelfTest(GetTree()).Length == 0) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            require(AreaArtTip.ShownForSelfTest(GetTree()) == Hint, "the hint shows after the delay: '" + AreaArtTip.ShownForSelfTest(GetTree()) + "'");
            var capture = System.Environment.GetEnvironmentVariable("ARC_AREA_HOVER_WINDOW_CAPTURE");
            if (!string.IsNullOrWhiteSpace(capture))
            {
                // The capture also keeps what the same hint looked like in Godot's tooltip font (a label on the tooltip's panel look), for a before/after composite.
                var before = new PanelContainer { Position = new Vector2(60, 300) };
                before.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color(14 / 255f, 16 / 255f, 22 / 255f, .9f), ContentMarginLeft = 8, ContentMarginRight = 8, ContentMarginTop = 4, ContentMarginBottom = 4 });
                before.AddChild(new Label { Text = Hint });
                layer.AddChild(before);
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                using var image = GetViewport().GetTexture().GetImage();
                require(image.SavePng($"{capture}-art-tip.png") == Error.Ok, "the art-tip capture saved");
            }
            AreaArtTipPopup.Leave(button);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            require(AreaArtTip.ShownForSelfTest(GetTree()).Length == 0, "leaving hides the hint");
            return "art-tip=ok";
        }
        finally { layer.QueueFree(); }
    }
}
