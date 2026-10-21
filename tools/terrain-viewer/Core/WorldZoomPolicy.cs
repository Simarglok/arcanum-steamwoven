using System.Text.Json;

namespace Arcanum.TerrainViewer.Core;

/// <summary>WORLD-only camera ratio; never an actor, asset, UI or physical pixel scale.</summary>
public static class WorldZoomPolicy
{
    public const double DefaultZoom = 2;
    public const double SaveDebounceSeconds = .25;
    public static bool IsStoredZoom(double value) => double.IsFinite(value) && value >= .125 && value <= 8;
    public static double Absolute(double current, double value) => double.IsFinite(value) ? Math.Clamp(value, .125, 8) : current;
    public static double Step(double current, double factor)
    {
        if (!double.IsFinite(factor) || factor <= 0) return current;
        var next = Absolute(current, current * factor);
        return current < 1 && next >= 1 || current > 1 && next <= 1 ? 1 : next;
    }
    /// <summary>ARC-RENDER-032: the smallest zoom of a map loaded as a window (the main world map): the smallest power of two
    /// (1/2^k) at which the view holds no more than the window's drawn bounds on either axis, so it never shows past what is
    /// loaded (the camera keeps the view inside the bounds), and never less than the ordinary 1/8. A power of two keeps the
    /// terrain in its exact reduced chunks (ARC-RENDER-019), which only such zooms have. A Steamwoven viewer rule: the
    /// original has no zoom.</summary>
    public static double WindowFloor(double viewportWidth, double viewportHeight, double boundsWidth, double boundsHeight)
    {
        static void Positive(double value, string name) { if (!(value > 0) || !double.IsFinite(value)) throw new ArgumentOutOfRangeException(name); }
        Positive(viewportWidth, nameof(viewportWidth)); Positive(viewportHeight, nameof(viewportHeight));
        Positive(boundsWidth, nameof(boundsWidth)); Positive(boundsHeight, nameof(boundsHeight));
        var exact = Math.Max(viewportWidth / boundsWidth, viewportHeight / boundsHeight);
        return Math.Clamp(Math.Pow(2, Math.Ceiling(Math.Log2(Math.Max(exact, .125)))), .125, 8);
    }
    public static double Advance(double current, double drive, double delta)
    {
        if (!IsStoredZoom(current) || !double.IsFinite(drive) || Math.Abs(drive) > 1
            || !double.IsFinite(delta) || delta < 0 || drive == 0 || delta == 0) return current;
        // Multiplication cannot overflow: |drive|<=1. Bound before exponentiation,
        // including huge finite frame intervals; fixed one octave/second.
        var logarithm = Math.Clamp(Math.Log2(current) + drive * delta, -3, 3);
        return Math.Pow(2, logarithm);
    }
}

public static class WorldZoomCodec
{
    public static double DeserializeOrDefault(string? json)
    {
        if (json is null) return 2;
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("version", out var version) || version.ValueKind != JsonValueKind.Number
                || !version.TryGetDouble(out var v) || v != 1
                || !root.TryGetProperty("zoom", out var zoom) || zoom.ValueKind != JsonValueKind.Number
                || !zoom.TryGetDouble(out var z) || !WorldZoomPolicy.IsStoredZoom(z)) return 2;
            return z;
        }
        catch (JsonException) { return 2; }
    }
    public static string Serialize(double zoom)
    {
        if (!WorldZoomPolicy.IsStoredZoom(zoom)) throw new ArgumentOutOfRangeException(nameof(zoom));
        return JsonSerializer.Serialize(new { version = 1, zoom });
    }
}
