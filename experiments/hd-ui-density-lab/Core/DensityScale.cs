namespace Arcanum.Experiments.HdUiDensityLab.Core;

/// <summary>The host as far as the interface scale is concerned (ARC-RES-122, docs/rendering/hd-interface-density.md).</summary>
public enum HostKind
{
    MacOS,
    Windows,
    SteamOS,
    LinuxX11,
    LinuxWayland,
    Other,
}

/// <summary>What the engine reports about the window's screen: the reported scale (Godot <c>screen_get_scale</c>), the DPI
/// (<c>screen_get_dpi</c>) and the window's size in device pixels.</summary>
public sealed record DisplayReading(HostKind Host, double ReportedScale, int Dpi, int WindowWidthPx, int WindowHeightPx);

public enum ScaleSource
{
    /// <summary>OS scale × density base × player's scale, below the fit limit.</summary>
    Density,
    /// <summary>Limited by the window: an 800 × 600 window must fit.</summary>
    Fit,
    /// <summary>Set by hand (lab control or self-test).</summary>
    Forced,
}

public sealed record ScaleDecision(double OsScale, double UserFactor, double DensityScale, double FitScale, double Scale, ScaleSource Source);

/// <summary>The one interface scale <c>s</c> (device pixels per UI unit) of the concept: <c>s = k × 1.5 × u</c>, capped by
/// <c>min(W / 800, H / 600)</c>. SteamOS (handheld and TV) uses the fit alone, shrunk by the player's scale below 100 %.</summary>
public static class DensityScale
{
    public const double UnitsWide = 800;
    public const double UnitsHigh = 600;
    public const double PointsPerUnit = 1.5;
    public const int MinUserPercent = 50;
    public const int MaxUserPercent = 300;
    public const double MinScale = 0.25;
    public const double MaxOsScale = 4;

    /// <summary>The OS display scale <c>k</c>. macOS and Wayland report it; Windows has only the effective DPI (Godot 4.7.2
    /// returns 1.0 from <c>screen_get_scale</c> there); X11 and SteamOS have none.</summary>
    public static double OsScale(DisplayReading reading)
    {
        ArgumentNullException.ThrowIfNull(reading);
        return reading.Host switch
        {
            HostKind.MacOS or HostKind.LinuxWayland => ClampOs(reading.ReportedScale),
            HostKind.Windows => ClampOs(reading.Dpi / 96.0),
            _ => 1.0,
        };
    }

    public static ScaleDecision Decide(DisplayReading reading, int userPercent, double? forcedScale = null)
    {
        ArgumentNullException.ThrowIfNull(reading);
        if (userPercent is < MinUserPercent or > MaxUserPercent)
            throw new ArgumentOutOfRangeException(nameof(userPercent), userPercent, "outside 50..300");
        if (reading.WindowWidthPx <= 0 || reading.WindowHeightPx <= 0)
            throw new ArgumentOutOfRangeException(nameof(reading), "window size must be positive");
        if (forcedScale is { } f && (!double.IsFinite(f) || f < MinScale))
            throw new ArgumentOutOfRangeException(nameof(forcedScale), forcedScale, "forced scale too small");

        var k = OsScale(reading);
        var u = userPercent / 100.0;
        var fit = Math.Min(reading.WindowWidthPx / UnitsWide, reading.WindowHeightPx / UnitsHigh);
        var density = k * PointsPerUnit * u;
        if (forcedScale is { } forced)
            return new ScaleDecision(k, u, density, fit, forced, ScaleSource.Forced);
        if (reading.Host == HostKind.SteamOS)
            return new ScaleDecision(k, u, density, fit, Math.Max(MinScale, fit * Math.Min(u, 1.0)), ScaleSource.Fit);
        var source = density <= fit ? ScaleSource.Density : ScaleSource.Fit;
        return new ScaleDecision(k, u, density, fit, Math.Max(MinScale, Math.Min(density, fit)), source);
    }

    private static double ClampOs(double value) => double.IsFinite(value) ? Math.Clamp(value, 1.0, MaxOsScale) : 1.0;
}
