using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>What a board is painted from: the material and rim, the exact device size, the strip scale and the unit position of its top left (so it continues a strip's grain), and the
/// strip frame whose wood it matches.</summary>
internal sealed record SteamwovenBoardSpec(
    ChromeMaterial Material, ChromeSides Rim, Rect2I Rect, double Scale, double OriginX, double OriginY,
    ChromeRecipe? ToneRecipe, byte[]? ToneLegacy, int ToneWidth, int ToneHeight)
{
    /// <summary>The identity that decides a repaint (the tone pixels by reference: the strip frame is the same object while it lives).</summary>
    public (ChromeMaterial, ChromeSides, Rect2I, double, double, double, object?) Key => (Material, Rim, Rect, Scale, OriginX, OriginY, ToneLegacy);
}

/// <summary>
/// ARC-UI-2452: one generated board (the sides of a panel, a menu's backdrop) of the Steamwoven prototype: painted off the main thread at the strip scale into one texture of exactly the
/// board's device size, drawn 1:1. While it is painted the node shows nothing (the rest of the HUD stays); <see cref="Pending"/> is true until the texture is in.
/// </summary>
internal sealed partial class SteamwovenBoardView : TextureRect
{
    private (ChromeMaterial, ChromeSides, Rect2I, double, double, double, object?)? _key;
    private int _generation;
    private int _pending;

    public SteamwovenBoardView()
    {
        ExpandMode = ExpandModeEnum.IgnoreSize; StretchMode = StretchModeEnum.Scale;
        TextureFilter = TextureFilterEnum.Nearest;
        MouseFilter = MouseFilterEnum.Stop;
    }

    /// <summary>Raised on the main thread after a paint was put in.</summary>
    public event Action? Painted;

    /// <summary>True while a paint is on a worker (the texture shown, if any, is the previous one).</summary>
    public bool Pending => Volatile.Read(ref _pending) > 0;

    /// <summary>The size and place of the texture last put in, for the self-test.</summary>
    internal Vector2I PixelSizeForSelfTest => Texture is { } texture ? new Vector2I(texture.GetWidth(), texture.GetHeight()) : Vector2I.Zero;

    public void Request(SteamwovenBoardSpec spec)
    {
        Position = spec.Rect.Position; Size = spec.Rect.Size;
        if (_key == spec.Key) return;
        _key = spec.Key;
        var generation = ++_generation;
        Interlocked.Increment(ref _pending);
        Task.Run(() =>
        {
            byte[]? pixels = null;
            try
            {
                pixels = InterfaceChromeRenderer.RenderBoard(spec.Material, spec.Rim, spec.Rect.Size.X, spec.Rect.Size.Y, spec.Scale, spec.OriginX, spec.OriginY,
                    spec.ToneRecipe, spec.ToneLegacy ?? [], spec.ToneWidth, spec.ToneHeight);
            }
            catch (Exception exception)
            {
                GD.Print("ARC-UI-2452 board paint failed: " + exception.Message);
            }
            Callable.From(() => { if (IsInstanceValid(this)) Apply(generation, spec.Rect.Size, pixels); }).CallDeferred();
        });
    }

    private void Apply(int generation, Vector2I size, byte[]? pixels)
    {
        Interlocked.Decrement(ref _pending);
        if (generation != _generation || !IsInsideTree()) return;
        // A failed paint is forgotten, so the next request paints again.
        if (pixels is null) { _key = null; return; }
        using var image = Image.CreateFromData(size.X, size.Y, false, Image.Format.Rgba8, pixels);
        Texture = ImageTexture.CreateFromImage(image);
        Painted?.Invoke();
    }
}
