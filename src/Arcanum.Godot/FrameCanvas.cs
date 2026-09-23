using Godot;

namespace Arcanum.Godot;

public partial class FrameCanvas : Control
{
    private Texture2D? _texture;
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
    public bool FitMode => _fit;

    public void SetTexture(Texture2D texture)
    {
        _texture = texture;
        _pan = Vector2.Zero;
        _zoom = 1f;
        _fit = false;
        QueueRedraw();
    }

    public void ClearFrame()
    {
        _texture = null;
        _pan = Vector2.Zero;
        _zoom = 1f;
        _fit = false;
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
        if (_texture is null)
        {
            return;
        }

        var source = _texture.GetSize();
        if (source.X <= 0 || source.Y <= 0 || Size.X <= 0 || Size.Y <= 0)
        {
            return;
        }

        _zoom = Mathf.Max(0.01f, Mathf.Min(Size.X / source.X, Size.Y / source.Y));
        _pan = Vector2.Zero;
        _fit = true;
        QueueRedraw();
    }

    public void AdjustZoom(float multiplier, Vector2 around)
    {
        if (_texture is null)
        {
            return;
        }

        var before = ToTextureSpace(around);
        _zoom = Mathf.Clamp(_zoom * multiplier, 0.0625f, 64f);
        _fit = false;
        var afterOrigin = (Size - (_texture.GetSize() * _zoom)) / 2f + _pan;
        _pan = around - afterOrigin - (before * _zoom);
        QueueRedraw();
    }

    private void OnResized()
    {
        if (_fit)
        {
            SetFit();
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

        var destinationSize = _texture.GetSize() * _zoom;
        var origin = ((Size - destinationSize) / 2f) + _pan;
        DrawTextureRect(_texture, new Rect2(origin, destinationSize), false);
    }

    private Vector2 ToTextureSpace(Vector2 screen)
    {
        if (_texture is null)
        {
            return Vector2.Zero;
        }

        var origin = ((Size - (_texture.GetSize() * _zoom)) / 2f) + _pan;
        return (screen - origin) / _zoom;
    }
}
