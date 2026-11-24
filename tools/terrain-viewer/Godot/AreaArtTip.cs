using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>
/// ARC-UI-3136: the hover hint of a control in the original's art font (<see cref="AreaTextFonts.Text"/>, text font 229, white; the HD redraw
/// with the HD interface on) in place of Godot's tooltip popup, which can only draw Godot's default font. <see cref="Set"/> replaces
/// <c>Control.TooltipText</c>: the text stays readable through <see cref="Get"/> and a popup shows it below and right of the pointer after the usual
/// half second (the placement of the floating hover tooltip, flipped inside the viewport), or nothing without the font. The original has no
/// tooltip of this kind (its hints are drawn by the windows themselves), so the hint is the viewer's own.
/// </summary>
public static class AreaArtTip
{
    private const string TextMeta = "arc_art_tip";
    private const string WiredMeta = "arc_art_tip_wired";

    /// <summary>Sets the hint of <paramref name="control"/> ("" none).</summary>
    public static void Set(Control control, string? text)
    {
        text ??= "";
        control.SetMeta(TextMeta, text);
        if (control.HasMeta(WiredMeta)) return;
        control.SetMeta(WiredMeta, true);
        control.MouseEntered += () => AreaArtTipPopup.Enter(control);
        control.MouseExited += () => AreaArtTipPopup.Leave(control);
    }

    /// <summary>Sets the hint of <paramref name="control"/> and returns it (for an object initialiser's place).</summary>
    public static T Tip<T>(T control, string text) where T : Control { Set(control, text); return control; }

    public static string Get(Control control) => control.HasMeta(TextMeta) ? control.GetMeta(TextMeta).AsString() : "";

    /// <summary>The hint now shown, "" none (a self-test).</summary>
    internal static string ShownForSelfTest(SceneTree tree) => tree.Root.GetNodeOrNull<AreaArtTipPopup>(AreaArtTipPopup.NodeName)?.Shown ?? "";
}
