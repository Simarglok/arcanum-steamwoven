using System.Globalization;

namespace Arcanum.Experiments.HdUiDensityLab.Core;

/// <summary>Text drawn with its top-left corner at a device pixel, at a font size in device pixels.</summary>
public sealed record TextOp(string Text, int X, int Y, int SizePx, Token Token);

/// <summary>A vector brass ring with a glass lens, drawn by a distance-field shader centred on a device pixel.</summary>
public sealed record LensOp(int CenterX, int CenterY, int OuterRadiusPx, int RingPx, int BevelPx, int LightAngleDegrees, int GlassIndex);

public sealed record HitTarget(string Id, DeviceRect Rect, bool Enabled);

/// <summary>Parameters of the vector lens that the lab changes live: ring width in UI units, light direction, glass tint.</summary>
public sealed record LensStyle(double RingUnits, int LightAngleDegrees, int GlassIndex)
{
    public static readonly LensStyle Default = new(6, 225, 0);
    public static readonly double[] RingChoices = [4, 6, 9, 12];
    public const int GlassChoices = 3;
}

public sealed record Interaction(string? Hover, string? Down, LensStyle Lens)
{
    public static readonly Interaction None = new(null, null, LensStyle.Default);
}

public sealed record ScreenModel(
    int WidthPx,
    int HeightPx,
    double Scale,
    IReadOnlyList<ChromeOp> Ops,
    IReadOnlyList<TextOp> Texts,
    IReadOnlyList<LensOp> Lenses,
    IReadOnlyList<HitTarget> Buttons,
    DeviceRect WindowOuter,
    DeviceRect TopStripOuter,
    DeviceRect BottomStripOuter,
    DeviceRect PlayArea);

/// <summary>The lab's mock screen: the two HUD strips at the original's rectangles (top 0..41, bottom 441..600 UI units,
/// centred 800 units with generated side fill), one 800 × 400 placeholder window in the play area, solid colours only.
/// The window's inner layout is a placeholder, not the original's.</summary>
public static class MockScreen
{
    public const double TopStripUnits = 41;
    public const double BottomStripUnits = 159;
    public const double WorldTileUnits = 40;

    public static ScreenModel Build(int widthPx, int heightPx, double scale, Interaction interaction)
    {
        if (widthPx <= 0 || heightPx <= 0) throw new ArgumentOutOfRangeException(nameof(widthPx), "screen size must be positive");
        if (!double.IsFinite(scale) || scale < DensityScale.MinScale) throw new ArgumentOutOfRangeException(nameof(scale), scale, null);
        ArgumentNullException.ThrowIfNull(interaction);

        var s = scale;
        var ops = new List<ChromeOp>();
        var texts = new List<TextOp>();
        var lenses = new List<LensOp>();
        var buttons = new List<HitTarget>();

        World(ops, widthPx, heightPx, s);

        var stripWidth = DeviceGrid.Edge(DensityScale.UnitsWide, s);
        var blockX = Math.Max(0, (widthPx - stripWidth) / 2);

        // Top strip.
        var top = DeviceGrid.Place(new UnitRect(0, 0, DensityScale.UnitsWide, TopStripUnits), s, blockX, 0);
        SideFill(ops, widthPx, top);
        Chrome.Fill(ops, Chrome.Frame(ops, top, Chrome.ButtonFrame, s), Token.Wood);
        for (var i = 0; i < 4; i++)
        {
            var x = 8 + i * 38;
            Socket(ops, new UnitRect(x - 2, 4, 34, 33), s, blockX, 0);
            Button(ops, texts, buttons, interaction, $"top{i}", (i + 1).ToString(CultureInfo.InvariantCulture), new UnitRect(x, 6, 30, 29), s, blockX, 0, enabled: true);
        }
        Text(texts, "TOP STRIP 800 x 41 units", 180, 12, 13, Token.Text, s, blockX, 0);

        // Bottom strip, anchored to the bottom edge on the same unit grid as a full 600-unit screen, so that its top edge is
        // Edge(441) whenever the screen is exactly 600 units high (no 1 px clash with the 400-unit window).
        var gridY = heightPx - DeviceGrid.Edge(DensityScale.UnitsHigh, s);
        var bottom = DeviceGrid.Place(new UnitRect(0, DensityScale.UnitsHigh - BottomStripUnits, DensityScale.UnitsWide, BottomStripUnits), s, blockX, gridY);
        var bottomY = bottom.Y;
        SideFill(ops, widthPx, bottom);
        Chrome.Fill(ops, Chrome.Frame(ops, bottom, Chrome.ButtonFrame, s), Token.Wood);
        Gauge(ops, new UnitRect(14, 12, 30, 104), 0.7, Token.GaugeRed, s, blockX, bottomY);
        Gauge(ops, new UnitRect(756, 12, 30, 104), 0.45, Token.GaugeBlue, s, blockX, bottomY);
        var log = DeviceGrid.Place(new UnitRect(56, 10, 400, 110), s, blockX, bottomY);
        Chrome.Fill(ops, Chrome.Frame(ops, log, Chrome.SunkFrame, s), Token.Parchment);
        Text(texts, "Message log placeholder", 64, 18, 12, Token.TextDark, s, blockX, bottomY);
        Text(texts, "Text is drawn at the device size.", 64, 36, 12, Token.TextDark, s, blockX, bottomY);
        Text(texts, $"s = {s.ToString("0.00", CultureInfo.InvariantCulture)} device px per UI unit", 64, 54, 12, Token.TextDark, s, blockX, bottomY);
        for (var i = 0; i < 10; i++)
        {
            var slot = DeviceGrid.Place(new UnitRect(56 + i * 40, 126, 36, 26), s, blockX, bottomY);
            Chrome.Fill(ops, Chrome.Frame(ops, slot, Chrome.SunkFrame, s), Token.Socket);
            Text(texts, ((i + 1) % 10).ToString(CultureInfo.InvariantCulture), 56 + i * 40 + 4, 128, 10, Token.Text, s, blockX, bottomY);
        }
        Button(ops, texts, buttons, interaction, "inventory", "Inventory", new UnitRect(470, 12, 120, 30), s, blockX, bottomY, enabled: true);
        Button(ops, texts, buttons, interaction, "character", "Character", new UnitRect(470, 48, 120, 30), s, blockX, bottomY, enabled: true);
        Button(ops, texts, buttons, interaction, "map", "Map (off)", new UnitRect(470, 84, 120, 30), s, blockX, bottomY, enabled: false);
        lenses.Add(new LensOp(
            blockX + DeviceGrid.Edge(680, s),
            bottomY + DeviceGrid.Edge(66, s),
            DeviceGrid.Line(52, s),
            DeviceGrid.Line(interaction.Lens.RingUnits, s),
            DeviceGrid.Line(2, s),
            interaction.Lens.LightAngleDegrees,
            interaction.Lens.GlassIndex));

        // The placeholder window, centred in the play area.
        var playArea = new DeviceRect(0, top.Bottom, widthPx, Math.Max(0, bottomY - top.Bottom));
        var windowHeight = DeviceGrid.Edge(400, s);
        var windowY = top.Bottom + Math.Max(0, (playArea.Height - windowHeight) / 2);
        var placed = DeviceGrid.Place(new UnitRect(0, 0, 800, 400), s, blockX, windowY);
        var window = placed with { Height = Math.Max(0, Math.Min(placed.Bottom, Math.Max(bottomY, top.Bottom)) - placed.Y) };
        Chrome.Fill(ops, Chrome.Frame(ops, window, Chrome.PanelFrame, s), Token.Wood);
        Text(texts, "INVENTORY (placeholder layout)", 16, 10, 14, Token.Text, s, blockX, windowY);
        Button(ops, texts, buttons, interaction, "close", "X", new UnitRect(764, 8, 26, 22), s, blockX, windowY, enabled: true);
        var picture = DeviceGrid.Place(new UnitRect(20, 40, 220, 330), s, blockX, windowY);
        Chrome.Fill(ops, Chrome.Frame(ops, picture, Chrome.SunkFrame, s), Token.Picture);
        Text(texts, "PICTURE (art slot)", 60, 196, 13, Token.Text, s, blockX, windowY);
        for (var row = 0; row < 7; row++)
        for (var col = 0; col < 10; col++)
        {
            var slot = DeviceGrid.Place(new UnitRect(258 + col * 48, 40 + row * 48, 44, 44), s, blockX, windowY);
            Chrome.Fill(ops, Chrome.Frame(ops, slot, Chrome.SunkFrame, s), Token.WoodDark);
        }

        return new ScreenModel(widthPx, heightPx, s, ops, texts, lenses, buttons, window, top, bottom, playArea);
    }

    private static void World(List<ChromeOp> ops, int widthPx, int heightPx, double s)
    {
        Chrome.Fill(ops, new DeviceRect(0, 0, widthPx, heightPx), Token.World);
        var tile = Math.Max(1, DeviceGrid.Edge(WorldTileUnits, s));
        for (var y = 0; y < heightPx; y += tile)
        for (var x = 0; x < widthPx; x += tile)
            if (((x / tile) + (y / tile)) % 2 == 1)
                Chrome.Fill(ops, new DeviceRect(x, y, Math.Min(tile, widthPx - x), Math.Min(tile, heightPx - y)), Token.WorldAlt);
    }

    private static void SideFill(List<ChromeOp> ops, int widthPx, DeviceRect strip)
    {
        if (strip.X > 0) Chrome.Fill(ops, new DeviceRect(0, strip.Y, strip.X, strip.Height), Token.WoodDark);
        if (strip.Right < widthPx) Chrome.Fill(ops, new DeviceRect(strip.Right, strip.Y, widthPx - strip.Right, strip.Height), Token.WoodDark);
    }

    private static void Socket(List<ChromeOp> ops, UnitRect rect, double s, int originX, int originY)
    {
        var socket = DeviceGrid.Place(rect, s, originX, originY);
        Chrome.Fill(ops, Chrome.Frame(ops, socket, Chrome.SunkFrame, s), Token.Socket);
    }

    private static void Gauge(List<ChromeOp> ops, UnitRect rect, double fraction, Token fill, double s, int originX, int originY)
    {
        var outer = DeviceGrid.Place(rect, s, originX, originY);
        var inner = Chrome.Frame(ops, outer, Chrome.SunkFrame, s);
        Chrome.Fill(ops, inner, Token.GaugeEmpty);
        var filled = DeviceGrid.Round(inner.Height * Math.Clamp(fraction, 0, 1));
        Chrome.Fill(ops, new DeviceRect(inner.X, inner.Bottom - filled, inner.Width, filled), fill);
    }

    private static void Button(List<ChromeOp> ops, List<TextOp> texts, List<HitTarget> buttons, Interaction interaction, string id, string label,
        UnitRect rect, double s, int originX, int originY, bool enabled)
    {
        var outer = DeviceGrid.Place(rect, s, originX, originY);
        var down = enabled && interaction.Down == id;
        var hover = enabled && interaction.Hover == id;
        var bands = down ? Chrome.ButtonFrame.Select(b => b.Sunk()).ToArray() : Chrome.ButtonFrame;
        var face = !enabled ? Token.FaceDisabled : down ? Token.FaceDown : hover ? Token.FaceHover : Token.Face;
        Chrome.Fill(ops, Chrome.Frame(ops, outer, bands, s), face);
        var shift = down ? 1 : 0;
        Text(texts, label, rect.X + 6 + shift, rect.Y + Math.Max(2, (rect.Height - 13) / 2) + shift, 13, enabled ? Token.Text : Token.TextDark, s, originX, originY);
        buttons.Add(new HitTarget(id, outer, enabled));
    }

    private static void Text(List<TextOp> texts, string text, double x, double y, double sizeUnits, Token token, double s, int originX, int originY) =>
        texts.Add(new TextOp(text, originX + DeviceGrid.Edge(x, s), originY + DeviceGrid.Edge(y, s), Math.Max(6, DeviceGrid.Round(sizeUnits * s)), token));
}
