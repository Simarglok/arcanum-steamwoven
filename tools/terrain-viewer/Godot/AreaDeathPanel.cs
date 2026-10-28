using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>
/// ARC-UI-026 the hero's death screen: the whole window black, a slide's picture centred and scaled to fit (the original's
/// slides are 800 × 600), and a black veil whose opacity makes the fades (ledger PD7). It sits above the main menu and the bars, so
/// the menu fades in under it after the reset. The app drives it: the veil moves only by <see cref="Advance"/>, with the same
/// time as the death's state, and the input goes to the app.
/// </summary>
public partial class AreaDeathPanel : CanvasLayer
{
    private ColorRect _back = null!;
    private TextureRect _picture = null!;
    private ColorRect _veil = null!;
    private float _from, _to;
    private double _fade, _elapsed;

    public bool IsOpen => Visible;
    /// <summary>The veil's opacity: 1 black, 0 clear.</summary>
    public float Veil => _veil.Color.A;
    public bool PictureShown => _picture.Texture is not null;
    internal Vector2I? PictureSizeForSelfTest => _picture.Texture is { } texture ? new(texture.GetWidth(), texture.GetHeight()) : null;

    public override void _Ready()
    {
        Layer = 7;
        Visible = false;
        _back = new ColorRect { Name = "DeathBack", Color = Colors.Black, MouseFilter = Control.MouseFilterEnum.Stop };
        _back.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_back);
        _picture = new TextureRect
        {
            Name = "DeathSlide", ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = Control.MouseFilterEnum.Ignore, TextureFilter = CanvasItem.TextureFilterEnum.Linear,
        };
        _picture.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_picture);
        _veil = new ColorRect { Name = "DeathVeil", Color = new Color(0, 0, 0, 0), MouseFilter = Control.MouseFilterEnum.Ignore };
        _veil.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_veil);
    }

    /// <summary>Opens over the game with the veil clear (the first fade then darkens it).</summary>
    public void Open()
    {
        if (Visible) return;
        _picture.Texture = null;
        // The game shows through until the first fade: only the back and the picture are hidden.
        _back.Visible = false;
        SetVeil(0); _from = _to = 0; _fade = 0;
        Visible = true;
    }

    public void Close()
    {
        Visible = false;
        _picture.Texture = null;
    }

    /// <summary>Starts a fade of the veil to black or to clear over <paramref name="milliseconds"/>.</summary>
    public void Fade(bool toBlack, int milliseconds)
    {
        _from = _veil.Color.A; _to = toBlack ? 1 : 0;
        _fade = Math.Max(1, milliseconds); _elapsed = 0;
        // A fade in without a picture is the main menu's after the reset: the back lets the new scene through under the veil.
        if (!toBlack && _picture.Texture is null) _back.Visible = false;
    }

    /// <summary>The picture stays black until it fades in; null clears it.</summary>
    public void ShowPicture(Texture2D? texture) => _picture.Texture = texture;

    /// <summary>Moves the fade on by <paramref name="milliseconds"/>.</summary>
    public void Advance(double milliseconds)
    {
        if (_fade <= 0) return;
        _elapsed = Math.Min(_fade, _elapsed + Math.Max(0, milliseconds));
        SetVeil((float)(_from + (_to - _from) * (_elapsed / _fade)));
        if (_elapsed < _fade) return;
        _fade = 0;
        // Once black the game is not shown again until the main menu: the black back stays under the pictures.
        if (_to >= 1) _back.Visible = true;
    }

    private void SetVeil(float alpha) => _veil.Color = new Color(0, 0, 0, Math.Clamp(alpha, 0, 1));
}
