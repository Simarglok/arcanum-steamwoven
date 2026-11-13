using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>
/// ARC-UI-1200: what a window's own view needs for the generated interface chrome (the pattern of the logbook, the sheet and the world map): the window's device scale
/// (the control's scale times the canvas transform's and the viewport's stretch, which is what its frames are painted at) and a claim of that scale for the textures it draws,
/// so each paints its generated frame at the window's scale; a texture that a worker finished painting redraws the view (it draws the textures in its own <c>_Draw</c>).
/// A view claims at every draw (a texture may be shared with another view, and the one on screen claims it) and lets go when its art goes or it leaves the tree.
/// </summary>
internal sealed class AreaDeviceScale
{
    private readonly CanvasItem _owner;
    private readonly HashSet<HdInterfaceTexture> _watched = [];
    private readonly Func<float> _scale;
    private readonly Action _changed;

    public AreaDeviceScale(CanvasItem owner)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        _scale = Current;
        _changed = () => { if (GodotObject.IsInstanceValid(_owner)) _owner.QueueRedraw(); };
    }

    /// <summary>Device pixels per Legacy pixel of the owner (1 while it is outside the tree).</summary>
    public float Current() =>
        GodotObject.IsInstanceValid(_owner) && _owner.IsInsideTree() ? _owner.GetGlobalTransformWithCanvas().Scale.X * _owner.GetViewport().GetStretchTransform().Scale.X : 1f;

    /// <summary>Makes the owner's device scale the scale source of every interface texture among <paramref name="textures"/> and watches it for a new painted frame.</summary>
    public void Claim(IEnumerable<Texture2D> textures)
    {
        foreach (var texture in textures)
        {
            if (texture is not HdInterfaceTexture hd) continue;
            hd.ScaleSource = _scale;
            if (_watched.Add(hd)) hd.Changed += _changed;
        }
    }

    /// <summary>The same for the frames of the given art numbers in a view's art table.</summary>
    public void Claim(IReadOnlyDictionary<int, Texture2D[]> art, params int[] numbers) =>
        Claim(numbers.Where(art.ContainsKey).SelectMany(number => art[number]));

    /// <summary>Stops watching (the art was taken back or the view left the tree).</summary>
    public void Release()
    {
        foreach (var texture in _watched) texture.Changed -= _changed;
        _watched.Clear();
    }
}
