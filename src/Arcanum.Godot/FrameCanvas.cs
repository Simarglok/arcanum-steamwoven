using Godot;

namespace Arcanum.Godot;

public partial class FrameCanvas : Control
{
    private Texture2D? _texture;
    private FrameHotspot _hotspot;
    private FrameCanvasGroupLayout? _groupLayout;
    private Vector2 _pan;
    private float _zoom = 1f;
    private bool _fit;
    private bool _panning;

    public FrameCanvas()
    {
        TextureFilter = TextureFilterEnum.Nearest;
        MouseFilter = MouseFilterEnum.Stop;
        CustomMinimumSize = new Vector2(320, 280);
        Resized += OnResized;
    }

    public float Zoom => _zoom;
    public Vector2 Pan => _pan;
    public bool FitMode => _fit;
    public bool HasTexture => _texture is not null;
    public bool HasAsset => _groupLayout is not null;
    public FrameHotspot Hotspot => _hotspot;
    public FrameAlignment Alignment { get; private set; } = FrameAlignment.RawHotspot;
    public bool AnchorMarkerVisible { get; private set; } = true;

    public void ResetAsset(Texture2D texture, FrameHotspot hotspot, FrameCanvasGroupLayout groupLayout)
    {
        ArgumentNullException.ThrowIfNull(texture);
        ArgumentNullException.ThrowIfNull(groupLayout);
        _texture = texture;
        _hotspot = hotspot;
        _groupLayout = groupLayout;
        _pan = Vector2.Zero;
        _zoom = 1f;
        _fit = false;
        QueueRedraw();
    }

    public void UpdateFrame(Texture2D texture, FrameHotspot hotspot)
    {
        ArgumentNullException.ThrowIfNull(texture);
        if (_groupLayout is null)
        {
            throw new InvalidOperationException("ResetAsset must establish a group layout before frames can be updated.");
        }

        _texture = texture;
        _hotspot = hotspot;
        QueueRedraw();
    }

    public void UpdateGroupLayout(FrameCanvasGroupLayout groupLayout)
    {
        ArgumentNullException.ThrowIfNull(groupLayout);
        _groupLayout = groupLayout;
        if (_fit)
        {
            ApplyFit();
        }
        else
        {
            QueueRedraw();
        }
    }

    public void ClearFrame()
    {
        _texture = null;
        QueueRedraw();
    }

    public void ClearAsset()
    {
        _texture = null;
        _hotspot = FrameHotspot.Zero;
        _groupLayout = null;
        _pan = Vector2.Zero;
        _zoom = 1f;
        _fit = false;
        QueueRedraw();
    }

    // Temporary source-compatibility shim for the ARC-RENDER-001 viewer. New callers must choose ResetAsset or UpdateFrame.
    internal void SetTexture(Texture2D texture)
    {
        ArgumentNullException.ThrowIfNull(texture);
        var size = texture.GetSize();
        ResetAsset(texture, FrameHotspot.Zero, new FrameCanvasGroupLayout([new FrameCanvasFrame((int)size.X, (int)size.Y, FrameHotspot.Zero)]));
    }

    public void SetAlignment(FrameAlignment alignment)
    {
        if (!Enum.IsDefined(alignment))
        {
            throw new ArgumentOutOfRangeException(nameof(alignment));
        }

        Alignment = alignment;
        if (_fit && _groupLayout is not null)
        {
            ApplyFit();
        }
        else
        {
            QueueRedraw();
        }
    }

    public void SetAnchorMarkerVisible(bool visible)
    {
        AnchorMarkerVisible = visible;
        QueueRedraw();
    }

    public void ResetOneToOne()
    {
        _zoom = 1f;
        _pan = Vector2.Zero;
        _fit = false;
        QueueRedraw();
    }

    public void SetFit()
    {
        if (_texture is null || _groupLayout is null)
        {
            return;
        }

        ApplyFit();
    }

    public void AdjustZoom(float multiplier, Vector2 around)
    {
        if (_texture is null || !float.IsFinite(multiplier) || multiplier <= 0f)
        {
            return;
        }

        var before = ToWorldSpace(around);
        _zoom = Mathf.Clamp(_zoom * multiplier, 0.0625f, 64f);
        _fit = false;
        _pan = around - (Size / 2f) - (before * _zoom);
        QueueRedraw();
    }

    public Vector2 GetScreenPositionForWorld(Vector2 world) => (Size / 2f) + _pan + (world * _zoom);

    public Vector2 GetFrameTopLeftWorld()
    {
        if (_texture is null)
        {
            return Vector2.Zero;
        }

        return Alignment == FrameAlignment.RawHotspot
            ? new Vector2(-_hotspot.X, -_hotspot.Y)
            : -(_texture.GetSize() / 2f);
    }

    internal static float? CalculateFitZoom(Vector2 canvasSize, Vector2 textureSize)
    {
        if (!float.IsFinite(canvasSize.X) || !float.IsFinite(canvasSize.Y) ||
            !float.IsFinite(textureSize.X) || !float.IsFinite(textureSize.Y) ||
            canvasSize.X <= 0 || canvasSize.Y <= 0 || textureSize.X <= 0 || textureSize.Y <= 0)
        {
            return null;
        }

        var zoom = MathF.Min(canvasSize.X / textureSize.X, canvasSize.Y / textureSize.Y);
        return float.IsFinite(zoom) && zoom > 0f ? zoom : null;
    }

    internal static float? CalculateFitZoom(Vector2 canvasSize, FrameCanvasBounds bounds) =>
        CalculateFitZoom(canvasSize, new Vector2(bounds.Width, bounds.Height));

    internal static float? CalculateFitZoom(Vector2 canvasSize, FrameCanvasWorldBounds bounds)
    {
        if (!float.IsFinite(canvasSize.X) || !float.IsFinite(canvasSize.Y) ||
            canvasSize.X <= 0 || canvasSize.Y <= 0 || bounds.Width <= 0 || bounds.Height <= 0)
        {
            return null;
        }

        var zoom = Math.Min(canvasSize.X / bounds.Width, canvasSize.Y / bounds.Height);
        return double.IsFinite(zoom) && zoom > 0d && zoom <= float.MaxValue ? (float)zoom : null;
    }

    private void ApplyFit()
    {
        var bounds = _groupLayout!.GetBounds(Alignment);
        var fitZoom = CalculateFitZoom(Size, bounds);
        if (fitZoom is null)
        {
            return;
        }

        _zoom = fitZoom.Value;
        var center = new Vector2((float)bounds.CenterX, (float)bounds.CenterY);
        _pan = -(center * _zoom);
        _fit = true;
        QueueRedraw();
    }

    private void OnResized()
    {
        if (_fit && _groupLayout is not null)
        {
            ApplyFit();
        }
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton button)
        {
            if (button.ButtonIndex == MouseButton.WheelUp && button.Pressed)
            {
                AdjustZoom(1.25f, button.Position);
                AcceptEvent();
            }
            else if (button.ButtonIndex == MouseButton.WheelDown && button.Pressed)
            {
                AdjustZoom(0.8f, button.Position);
                AcceptEvent();
            }
            else if (button.ButtonIndex == MouseButton.Left)
            {
                _panning = button.Pressed;
                AcceptEvent();
            }
        }
        else if (@event is InputEventMouseMotion motion && _panning)
        {
            _pan += motion.Relative;
            _fit = false;
            QueueRedraw();
            AcceptEvent();
        }
    }

    public override void _Draw()
    {
        var light = new Color("5d626b");
        var dark = new Color("373b42");
        const float square = 16f;
        for (var y = 0f; y < Size.Y; y += square)
        {
            for (var x = 0f; x < Size.X; x += square)
            {
                DrawRect(new Rect2(x, y, square, square), (((int)(x / square) + (int)(y / square)) & 1) == 0 ? light : dark);
            }
        }

        if (_texture is null)
        {
            return;
        }

        var origin = GetScreenPositionForWorld(GetFrameTopLeftWorld());
        DrawTextureRect(_texture, new Rect2(origin, _texture.GetSize() * _zoom), false);
        if (AnchorMarkerVisible)
        {
            var anchor = GetScreenPositionForWorld(Vector2.Zero);
            var marker = new Color("ff4d4d");
            DrawLine(anchor - new Vector2(6f, 0f), anchor + new Vector2(6f, 0f), marker, 1.5f);
            DrawLine(anchor - new Vector2(0f, 6f), anchor + new Vector2(0f, 6f), marker, 1.5f);
        }
    }

    private Vector2 ToWorldSpace(Vector2 screen) => (screen - (Size / 2f) - _pan) / _zoom;
}