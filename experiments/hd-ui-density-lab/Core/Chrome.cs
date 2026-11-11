namespace Arcanum.Experiments.HdUiDensityLab.Core;

/// <summary>Solid-colour placeholder tokens of the lab's theme (our own values, not measured from any game art).</summary>
public enum Token
{
    Ink,
    BrassLight,
    Brass,
    BrassDark,
    Wood,
    WoodDark,
    Parchment,
    Socket,
    World,
    WorldAlt,
    Face,
    FaceHover,
    FaceDown,
    FaceDisabled,
    Picture,
    GaugeRed,
    GaugeBlue,
    GaugeEmpty,
    Text,
    TextDark,
    Diagnostics,
}

public static class Palette
{
    public static (byte R, byte G, byte B) Rgb(Token token) => token switch
    {
        Token.Ink => (20, 14, 8),
        Token.BrassLight => (214, 178, 96),
        Token.Brass => (160, 120, 52),
        Token.BrassDark => (96, 70, 30),
        Token.Wood => (92, 60, 34),
        Token.WoodDark => (60, 38, 22),
        Token.Parchment => (200, 184, 150),
        Token.Socket => (12, 10, 8),
        Token.World => (34, 52, 40),
        Token.WorldAlt => (38, 58, 44),
        Token.Face => (120, 86, 44),
        Token.FaceHover => (146, 106, 56),
        Token.FaceDown => (84, 60, 30),
        Token.FaceDisabled => (82, 74, 66),
        Token.Picture => (70, 70, 92),
        Token.GaugeRed => (170, 34, 30),
        Token.GaugeBlue => (40, 70, 170),
        Token.GaugeEmpty => (30, 24, 20),
        Token.Text => (236, 226, 200),
        Token.TextDark => (40, 28, 16),
        Token.Diagnostics => (250, 250, 250),
        _ => throw new ArgumentOutOfRangeException(nameof(token), token, null),
    };
}

/// <summary>One filled device-pixel rectangle of one token.</summary>
public readonly record struct ChromeOp(DeviceRect Rect, Token Token);

/// <summary>A band of a frame: its width in UI units and the token of its lit (top, left) and shaded (bottom, right) sides.</summary>
public sealed record Band(double WidthUnits, Token Lit, Token Shaded)
{
    public static Band Flat(double widthUnits, Token token) => new(widthUnits, token, token);
    public Band Sunk() => new(WidthUnits, Shaded, Lit);
}

public static class Chrome
{
    public static readonly IReadOnlyList<Band> PanelFrame =
    [
        Band.Flat(1, Token.Ink),
        new Band(1, Token.BrassLight, Token.BrassDark),
        Band.Flat(1, Token.Brass),
        Band.Flat(1, Token.Ink),
    ];

    public static readonly IReadOnlyList<Band> ButtonFrame =
    [
        Band.Flat(1, Token.Ink),
        new Band(1, Token.BrassLight, Token.BrassDark),
    ];

    public static readonly IReadOnlyList<Band> SunkFrame =
    [
        new Band(1, Token.BrassDark, Token.BrassLight),
        Band.Flat(1, Token.Ink),
    ];

    /// <summary>Draws the bands from the outside in, stacked by their rounded widths, and returns the interior. A band that
    /// no longer fits on both sides is skipped with all bands inside it.</summary>
    public static DeviceRect Frame(List<ChromeOp> ops, DeviceRect outer, IReadOnlyList<Band> bands, double scale)
    {
        ArgumentNullException.ThrowIfNull(ops);
        ArgumentNullException.ThrowIfNull(bands);
        var r = outer;
        foreach (var band in bands)
        {
            var w = DeviceGrid.Line(band.WidthUnits, scale);
            if (2 * w > r.Width || 2 * w > r.Height) break;
            ops.Add(new ChromeOp(new DeviceRect(r.X, r.Y, r.Width, w), band.Lit));
            ops.Add(new ChromeOp(new DeviceRect(r.X, r.Bottom - w, r.Width, w), band.Shaded));
            ops.Add(new ChromeOp(new DeviceRect(r.X, r.Y + w, w, r.Height - 2 * w), band.Lit));
            ops.Add(new ChromeOp(new DeviceRect(r.Right - w, r.Y + w, w, r.Height - 2 * w), band.Shaded));
            r = r.Inset(w);
        }
        return r;
    }

    public static void Fill(List<ChromeOp> ops, DeviceRect rect, Token token)
    {
        ArgumentNullException.ThrowIfNull(ops);
        if (!rect.IsEmpty) ops.Add(new ChromeOp(rect, token));
    }

    /// <summary>The widths in device pixels that <see cref="Frame"/> gives the bands at a scale.</summary>
    public static IReadOnlyList<int> BandWidths(IReadOnlyList<Band> bands, double scale) =>
        bands.Select(b => DeviceGrid.Line(b.WidthUnits, scale)).ToArray();
}
