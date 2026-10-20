using Godot;

namespace Arcanum.TerrainViewer;

// ARC-INPUT-003 native observation: a reserved original key pressed through the viewport's input path names itself in
// the sticky HUD header, inside the panel and the window, with every header line shown (no scrolling needed); the
// next key press removes the notice and the detail text keeps its "last reserved key" entry.
public partial class TerrainViewerApp
{
    private async Task RunReservedKeyHintSmokeAsync()
    {
        static void Require(bool value, string why) { if (!value) throw new InvalidOperationException("ARC-INPUT-003 reserved-key smoke: " + why); }
        var hud = _hud!;
        Require(!WorldModal, "a modal surface is open");
        GetViewport().GuiReleaseFocus();
        var hudVisible = hud.Visible; hud.Visible = true;
        try
        {
            async Task Press(Key key)
            {
                GetViewport().PushInput(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = true });
                GetViewport().PushInput(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = false });
                RefreshReadyStatus();
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            }
            // F1 (Followers: go to point) is reserved in every context of the viewer.
            await Press(Key.F1);
            const string hint = "Reserved key: F1 = Followers: go to point (original; NOT IMPLEMENTED)";
            var window = GetViewport().GetVisibleRect(); var sticky = hud.StickyRect;
            Require(hud.StickyText.Contains(hint, StringComparison.Ordinal), "no notice in the sticky header: " + hud.StickyText);
            Require(hud.StickyVisible && hud.StickyFullyShown, "the sticky header is hidden or clipped");
            Require(window.Encloses(sticky) && hud.Rect.Encloses(sticky), $"the sticky header {sticky} is outside the panel {hud.Rect} or the window {window}");
            Require(hud.Text.Contains("last reserved key: F1 = Followers: go to point", StringComparison.Ordinal), "the detail text lost its last reserved key");
            var lines = hud.StickyText.Split('\n').Length;
            // Z is not mapped: the next key press removes the notice; the detail keeps the last reserved key.
            await Press(Key.Z);
            Require(!hud.StickyText.Contains("Reserved key:", StringComparison.Ordinal), "the notice outlived the next key press");
            Require(hud.Text.Contains("last reserved key: F1", StringComparison.Ordinal), "the detail text dropped the last reserved key");
            GD.Print("ARC-INPUT-003 native-smoke reserved-key hint PASS " + System.Text.Json.JsonSerializer.Serialize(new {
                Key = "F1", StickyLines = lines, Sticky = new[] { sticky.Position.X, sticky.Position.Y, sticky.Size.X, sticky.Size.Y },
                Panel = new[] { hud.Rect.Size.X, hud.Rect.Size.Y }, Window = new[] { window.Size.X, window.Size.Y }, ClearedByNextKey = true }));
        }
        finally { hud.Visible = hudVisible; }
    }
}
