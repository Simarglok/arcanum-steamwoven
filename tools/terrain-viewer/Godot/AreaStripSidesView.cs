using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>
/// ARC-UI-089: fills the sides of an interface strip on a screen wider than 4:3 with tiles of the strip's own HD art
/// (<see cref="AreaStripSides"/>, ARC-RES-093 layout, ARC-RES-094 U4 pieces). Draws nothing while the HD profile is off or the strip's art
/// has no HD redraw, so with the switch off the screen is exactly what it was. The node sits at the strip's origin and draws outside its
/// own rectangle, in the strip's unscaled UI units. ARC-UI-096: it draws nothing at all unless <see cref="AreaStripSides.SideArtReady"/>
/// (there is no proper side art yet), so the world shows beside the strips.
/// </summary>
public partial class AreaStripSidesView : Control
{
    private AreaBarStrip _strip;
    private Texture2D? _art;
    private float _left, _right;

    /// <summary>Whether the view draws sides; <see cref="AreaStripSides.SideArtReady"/> unless a self-test sets it to check the HD path.</summary>
    internal bool Enabled { get; set; } = AreaStripSides.SideArtReady;

    public AreaStripSidesView() { MouseFilter = MouseFilterEnum.Ignore; }

    public AreaStripSidesView(AreaBarStrip strip) : this() { _strip = strip; }

    public override void _EnterTree() => InterfaceHd.Changed += QueueRedraw;
    public override void _ExitTree() => InterfaceHd.Changed -= QueueRedraw;

    /// <summary>The strip's art (its frame 0) and the free width on each side, in UI units.</summary>
    public void Set(Texture2D? art, float leftMargin, float rightMargin)
    {
        if (ReferenceEquals(art, _art) && leftMargin == _left && rightMargin == _right) return;
        _art = art; _left = leftMargin; _right = rightMargin;
        QueueRedraw();
    }

    /// <summary>True when the next draw puts HD tiles on a side (for the self-test).</summary>
    internal bool DrawsHd => Enabled && _art is HdInterfaceTexture { IsHd: true } && (_left > 0.25f || _right > 0.25f);

    public override void _Draw()
    {
        if (!Enabled || _art is not HdInterfaceTexture { IsHd: true } art) return;
        var height = art.GetHeight();
        foreach (var run in AreaStripSides.Left(_strip, _left).Concat(AreaStripSides.Right(_strip, _right)))
            DrawTextureRectRegion(art, new Rect2(run.DestX, 0, run.Width, height), new Rect2(run.SourceX, 0, run.Width, height));
    }
}
