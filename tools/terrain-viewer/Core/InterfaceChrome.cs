using Arcanum.Data.Hd;

namespace Arcanum.TerrainViewer.Core;

/// <summary>ARC-UI-140: a surface the generated interface chrome is painted with (our own colours and noise, chosen by eye; not
/// measured from any game art).</summary>
public enum ChromeMaterial { Wood, WoodDark, Iron, Dark, Face }

/// <summary>The edges of a frame that get a rim (a panel's edge facing the play view).</summary>
[Flags]
public enum ChromeSides { None = 0, Top = 1, Bottom = 2, Left = 4, Right = 8, All = Top | Bottom | Left | Right }

/// <summary>The one rounded corner a plate may have.</summary>
public enum ChromeCorner { None, TopLeft, TopRight, BottomLeft, BottomRight }

/// <summary>One part of a recipe, in Legacy pixels of the frame.</summary>
public abstract record ChromePart;

/// <summary>A raised plate of a material with a one-unit bevel, optionally with one rounded corner.</summary>
public sealed record ChromePlate(int X, int Y, int Width, int Height, ChromeMaterial Material, int CornerRadius = 0, ChromeCorner Corner = ChromeCorner.None) : ChromePart;

/// <summary>A sunk panel: a one-unit sunk brass bevel, a one-unit ink line, then the fill.</summary>
public sealed record ChromeSunk(int X, int Y, int Width, int Height, ChromeMaterial Fill) : ChromePart;

/// <summary>A brass rivet (a vector disc with radial light).</summary>
public sealed record ChromeRivet(double X, double Y, double Radius) : ChromePart;

/// <summary>A picture region: its pixels come from the art (the HD redraw when the pack has one, else the Legacy frame).</summary>
public sealed record ChromeKeep(int X, int Y, int Width, int Height) : ChromePart;

public enum ChromeKind
{
    /// <summary>A panel or strip: base material, parts, rims around its holes, rims on <see cref="ChromeRecipe.Rim"/>.</summary>
    Panel,
    /// <summary>A button: generated face and rim following its silhouette (raised, sunk on the down frame), the glyph kept from the art.</summary>
    Button,
}

/// <summary>How to generate the chrome of every frame of one interface art file. The Legacy frame still decides the size, the
/// silhouette (alpha), the holes and hit tests; only the drawn pixels change. A button's <paramref name="DownFrame"/> draws pressed
/// and its <paramref name="HoverFrame"/> lit (the bar's arts: idle 0, down 1, hover 2).</summary>
public sealed record ChromeRecipe(string File, ChromeKind Kind, ChromeMaterial Base, ChromeSides Rim, IReadOnlyList<ChromePart> Parts, int DownFrame = 1, int HoverFrame = 2)
{
    public string NameSha256 { get; } = HdInterfaceKey.NameSha256(File);
}

/// <summary>ARC-UI-140: the recipes of the generated interface (slice S1: both HUD strips and the bar buttons). Coordinates are Legacy
/// pixels of each frame (layout facts of the original windows, as in the ledgers); everything drawn is ours.</summary>
public static class InterfaceChromeRecipes
{
    public static IReadOnlyList<ChromeRecipe> All { get; } = Build();

    private static readonly Dictionary<string, ChromeRecipe> ByName = All.ToDictionary(recipe => recipe.NameSha256, StringComparer.Ordinal);

    public static ChromeRecipe? For(HdSpriteQuery? query) => query is null ? null : ByName.GetValueOrDefault(query.NameSha256);

    private static ChromeRecipe[] Build()
    {
        var top = new ChromeRecipe("inttop.art", ChromeKind.Panel, ChromeMaterial.Wood, ChromeSides.Bottom,
        [
            new ChromePlate(150, 0, 112, 41, ChromeMaterial.Iron, 30, ChromeCorner.BottomRight),
            new ChromePlate(532, 0, 268, 41, ChromeMaterial.Iron, 30, ChromeCorner.BottomLeft),
            new ChromeSunk(189, 16, 27, 14, ChromeMaterial.Dark),
            new ChromeRivet(239.5, 4.5, 2.5), new ChromeRivet(219.5, 33.5, 2.5), new ChromeRivet(556.5, 4.5, 2.5), new ChromeRivet(572.5, 34.5, 2.5),
            new ChromeKeep(186, 1, 32, 11),
            new ChromeKeep(576, 1, 24, 38),
        ]);
        List<ChromePart> bottomParts =
        [
            new ChromeSunk(190, 47, 420, 112, ChromeMaterial.Dark),
            new ChromeSunk(57, 66, 100, 27, ChromeMaterial.Dark),
            new ChromeSunk(64, 102, 41, 41, ChromeMaterial.WoodDark),
            new ChromeSunk(111, 102, 38, 41, ChromeMaterial.WoodDark),
        ];
        for (var k = 0; k < 5; k++)
        {
            bottomParts.Add(new ChromeSunk(197 + 39 * k, 2, 35, 35, ChromeMaterial.WoodDark));
            bottomParts.Add(new ChromeSunk(416 + 39 * k, 2, 35, 35, ChromeMaterial.WoodDark));
        }
        bottomParts.AddRange(
        [
            new ChromeKeep(4, 0, 52, 159), new ChromeKeep(740, 0, 60, 159),
            new ChromeKeep(152, 0, 46, 52), new ChromeKeep(604, 0, 48, 48),
            new ChromeKeep(163, 55, 30, 92), new ChromeKeep(607, 55, 36, 95),
            new ChromeKeep(205, 37, 392, 11), new ChromeKeep(387, 0, 30, 40),
        ]);
        var bottom = new ChromeRecipe("intbotom.art", ChromeKind.Panel, ChromeMaterial.Wood, ChromeSides.Top, bottomParts);
        string[] buttons = ["char_but.art", "log_but.art", "invn_but.art", "tmap_but.art", "lilgrnbut.art",
            "combat_button.art", "skills_button.art", "spells_button.art", "schematics_button.art"];
        return [top, bottom, .. buttons.Select(file => new ChromeRecipe(file, ChromeKind.Button, ChromeMaterial.Face, ChromeSides.None, []))];
    }
}

/// <summary>ARC-UI-140: paints a recipe at <see cref="Scale"/> × the Legacy frame. Godot-free and deterministic: the same inputs give the
/// same bytes. Alpha is the Legacy alpha upscaled (round holes get an anti-aliased circular edge), so the silhouette, holes and hit tests
/// are the original's.</summary>
public static class InterfaceChromeRenderer
{
    public const int Scale = 4;
    public const int MaxSide = 1024;

    private readonly record struct Rgb(float R, float G, float B)
    {
        public static Rgb Lerp(Rgb a, Rgb b, float t) => new(a.R + (b.R - a.R) * t, a.G + (b.G - a.G) * t, a.B + (b.B - a.B) * t);
        public Rgb Times(float k) => new(R * k, G * k, B * k);
    }

    private static readonly Rgb Ink = new(20, 14, 8), BrassLight = new(214, 178, 96), Brass = new(160, 120, 52), BrassDark = new(96, 70, 30);
    private static readonly Rgb WoodLow = new(58, 37, 20), WoodHigh = new(112, 76, 43), IronLow = new(44, 46, 50), IronHigh = new(96, 98, 104);
    private static readonly Rgb DarkFill = new(8, 8, 14), FaceHigh = new(82, 74, 38), FaceLow = new(34, 30, 14), White = new(255, 255, 255);

    private sealed class Canvas
    {
        public readonly int W, H, LegacyW, LegacyH;
        public readonly Rgb[] Color;
        public readonly float[] Alpha;
        public readonly byte[] Legacy;
        public readonly byte[]? Picture;

        public Canvas(int legacyW, int legacyH, byte[] legacy, byte[]? picture)
        {
            LegacyW = legacyW; LegacyH = legacyH; W = legacyW * Scale; H = legacyH * Scale; Legacy = legacy; Picture = picture;
            Color = new Rgb[W * H]; Alpha = new float[W * H];
            for (var y = 0; y < H; y++)
            for (var x = 0; x < W; x++)
                Alpha[y * W + x] = LegacyOpaque(x / Scale, y / Scale) ? 1f : 0f;
        }

        public bool LegacyOpaque(int lx, int ly) => lx >= 0 && ly >= 0 && lx < LegacyW && ly < LegacyH && Legacy[(ly * LegacyW + lx) * 4 + 3] > 0;

        public Rgb PictureAt(int x, int y)
        {
            if (Picture is { } p) { var i = (y * W + x) * 4; return new Rgb(p[i], p[i + 1], p[i + 2]); }
            var j = ((y / Scale) * LegacyW + x / Scale) * 4;
            return new Rgb(Legacy[j], Legacy[j + 1], Legacy[j + 2]);
        }

        public void Blend(int x, int y, Rgb c, float coverage)
        {
            if (coverage <= 0) return;
            var i = y * W + x;
            Color[i] = coverage >= 1 ? c : Rgb.Lerp(Color[i], c, coverage);
        }
    }

    /// <summary>Paints frame <paramref name="frameIndex"/> of the recipe's art. <paramref name="legacy"/> is the Legacy RGBA
    /// (width × height × 4); <paramref name="picture"/> is the HD redraw at <see cref="Scale"/>× (same layout) or empty to sample the
    /// Legacy pixels. Returns RGBA of (width × 4) × (height × 4).</summary>
    public static byte[] Render(ChromeRecipe recipe, int width, int height, ReadOnlySpan<byte> legacy, ReadOnlySpan<byte> picture, int frameIndex)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        if (width is < 1 or > MaxSide || height is < 1 or > MaxSide) throw new ArgumentOutOfRangeException(nameof(width), $"{width}x{height}");
        if (legacy.Length != width * height * 4) throw new ArgumentException("legacy RGBA size", nameof(legacy));
        if (!picture.IsEmpty && picture.Length != width * Scale * height * Scale * 4) throw new ArgumentException("picture RGBA size", nameof(picture));
        if (frameIndex < 0) throw new ArgumentOutOfRangeException(nameof(frameIndex));

        var canvas = new Canvas(width, height, legacy.ToArray(), picture.IsEmpty ? null : picture.ToArray());
        if (recipe.Kind == ChromeKind.Button) PaintButton(canvas, frameIndex == recipe.DownFrame, frameIndex == recipe.HoverFrame && recipe.HoverFrame != recipe.DownFrame);
        else PaintPanel(canvas, recipe);

        var output = new byte[canvas.W * canvas.H * 4];
        for (var i = 0; i < canvas.Color.Length; i++)
        {
            var c = canvas.Color[i];
            var alpha = ToByte(canvas.Alpha[i] * 255f);
            // A transparent texel carries ink, not the wood under it, so the mipmapped edge of a hole fades to dark, not to wood.
            if (alpha == 0) c = Ink;
            output[i * 4] = ToByte(c.R); output[i * 4 + 1] = ToByte(c.G); output[i * 4 + 2] = ToByte(c.B);
            output[i * 4 + 3] = alpha;
        }
        return output;
    }

    private static byte ToByte(float v) => (byte)Math.Clamp((int)MathF.Round(v), 0, 255);

    private static void PaintPanel(Canvas c, ChromeRecipe recipe)
    {
        Fill(c, 0, 0, c.W, c.H, recipe.Base);
        foreach (var part in recipe.Parts)
        {
            switch (part)
            {
                case ChromePlate plate: Plate(c, plate); break;
                case ChromeSunk sunk: Sunk(c, sunk); break;
            }
        }
        foreach (var hole in Holes(c)) HoleRim(c, hole);
        foreach (var part in recipe.Parts)
        {
            switch (part)
            {
                case ChromeRivet rivet: Rivet(c, rivet); break;
                case ChromeKeep keep: Keep(c, keep.X * Scale, keep.Y * Scale, keep.Width * Scale, keep.Height * Scale); break;
            }
        }
        Rim(c, recipe.Rim);
    }

    private static void Fill(Canvas c, int x0, int y0, int w, int h, ChromeMaterial material)
    {
        for (var y = Math.Max(0, y0); y < Math.Min(c.H, y0 + h); y++)
        for (var x = Math.Max(0, x0); x < Math.Min(c.W, x0 + w); x++)
            c.Color[y * c.W + x] = Material(material, (x + 0.5f) / Scale, (y + 0.5f) / Scale);
    }

    private static Rgb Material(ChromeMaterial material, float u, float v) => material switch
    {
        ChromeMaterial.Wood => WoodAt(u, v),
        ChromeMaterial.WoodDark => WoodAt(u, v).Times(0.62f),
        ChromeMaterial.Iron => Rgb.Lerp(IronLow, IronHigh, Math.Clamp(0.2f + 0.65f * Fbm(u * 0.22f, v * 0.22f, 7), 0, 1)),
        ChromeMaterial.Dark => Rgb.Lerp(DarkFill, new Rgb(16, 16, 26), Fbm(u * 0.05f, v * 0.05f, 11)),
        _ => FaceHigh,
    };

    private static Rgb WoodAt(float u, float v)
    {
        var n = Fbm(u * 0.03f, v * 0.42f, 3);
        var stripe = 0.5f + 0.5f * MathF.Sin((v * 0.85f + n * 7f) * MathF.PI);
        return Rgb.Lerp(WoodLow, WoodHigh, Math.Clamp(0.15f + 0.55f * n + 0.3f * stripe, 0, 1));
    }

    private static void Plate(Canvas c, ChromePlate plate)
    {
        int x0 = plate.X * Scale, y0 = plate.Y * Scale, x1 = (plate.X + plate.Width) * Scale, y1 = (plate.Y + plate.Height) * Scale;
        var r = plate.CornerRadius * Scale;
        for (var y = Math.Max(0, y0); y < Math.Min(c.H, y1); y++)
        for (var x = Math.Max(0, x0); x < Math.Min(c.W, x1); x++)
        {
            float px = x + 0.5f, py = y + 0.5f;
            float edge; bool lit;
            if (InCorner(plate.Corner, px, py, x0, y0, x1, y1, r, out var cx, out var cy))
            {
                var d = r - MathF.Sqrt((px - cx) * (px - cx) + (py - cy) * (py - cy));
                if (d <= -0.5f) continue;
                var coverage = Math.Clamp(d + 0.5f, 0, 1);
                var fill = Bevelled(Material(plate.Material, px / Scale, py / Scale), d, (px - cx) + (py - cy) < 0);
                c.Blend(x, y, fill, coverage);
                continue;
            }
            var (left, right, top, bottom) = (px - x0, x1 - px, py - y0, y1 - py);
            edge = MathF.Min(MathF.Min(left, right), MathF.Min(top, bottom));
            lit = edge == left || edge == top;
            c.Color[y * c.W + x] = Bevelled(Material(plate.Material, px / Scale, py / Scale), edge, lit);
        }
    }

    private static bool InCorner(ChromeCorner corner, float px, float py, int x0, int y0, int x1, int y1, int r, out float cx, out float cy)
    {
        (cx, cy) = corner switch
        {
            ChromeCorner.TopLeft => (x0 + r, y0 + r),
            ChromeCorner.TopRight => (x1 - r, y0 + r),
            ChromeCorner.BottomLeft => (x0 + r, y1 - r),
            ChromeCorner.BottomRight => (x1 - r, y1 - r),
            _ => (0f, 0f),
        };
        return corner switch
        {
            ChromeCorner.TopLeft => px < cx && py < cy,
            ChromeCorner.TopRight => px > cx && py < cy,
            ChromeCorner.BottomLeft => px < cx && py > cy,
            ChromeCorner.BottomRight => px > cx && py > cy,
            _ => false,
        };
    }

    // A one-unit bevel inside a plate's edge: lighter on its lit sides, darker on the others.
    private static Rgb Bevelled(Rgb fill, float edgeDistance, bool lit) =>
        edgeDistance >= Scale ? fill : lit ? Rgb.Lerp(fill, White, 0.22f) : fill.Times(0.6f);

    private static void Sunk(Canvas c, ChromeSunk sunk)
    {
        int x0 = sunk.X * Scale, y0 = sunk.Y * Scale, x1 = (sunk.X + sunk.Width) * Scale, y1 = (sunk.Y + sunk.Height) * Scale;
        for (var y = Math.Max(0, y0); y < Math.Min(c.H, y1); y++)
        for (var x = Math.Max(0, x0); x < Math.Min(c.W, x1); x++)
        {
            int left = x - x0, right = x1 - 1 - x, top = y - y0, bottom = y1 - 1 - y;
            var edge = Math.Min(Math.Min(left, right), Math.Min(top, bottom));
            var i = y * c.W + x;
            if (edge < Scale) c.Color[i] = edge == left || edge == top ? BrassDark : BrassLight;
            else if (edge < 2 * Scale) c.Color[i] = Ink;
            else c.Color[i] = Material(sunk.Fill, (x + 0.5f) / Scale, (y + 0.5f) / Scale);
        }
    }

    private static void Rivet(Canvas c, ChromeRivet rivet)
    {
        float cx = (float)rivet.X * Scale, cy = (float)rivet.Y * Scale, r = (float)rivet.Radius * Scale;
        for (var y = Math.Max(0, (int)(cy - r - 1)); y < Math.Min(c.H, (int)(cy + r + 2)); y++)
        for (var x = Math.Max(0, (int)(cx - r - 1)); x < Math.Min(c.W, (int)(cx + r + 2)); x++)
        {
            float dx = x + 0.5f - cx, dy = y + 0.5f - cy, d = MathF.Sqrt(dx * dx + dy * dy);
            var coverage = Math.Clamp(r - d + 0.5f, 0, 1);
            if (coverage <= 0 || c.Alpha[y * c.W + x] <= 0) continue;
            var light = Math.Clamp(0.5f - (dx + dy) / (2.8f * r), 0, 1);
            var colour = Rgb.Lerp(BrassDark, BrassLight, light);
            c.Blend(x, y, d > r - Scale * 0.6f ? Rgb.Lerp(colour, Ink, 0.6f) : colour, coverage);
        }
    }

    private static void Keep(Canvas c, int x0, int y0, int w, int h)
    {
        for (var y = Math.Max(0, y0); y < Math.Min(c.H, y0 + h); y++)
        for (var x = Math.Max(0, x0); x < Math.Min(c.W, x0 + w); x++)
            if (c.Alpha[y * c.W + x] > 0) c.Color[y * c.W + x] = c.PictureAt(x, y);
    }

    private static void Rim(Canvas c, ChromeSides sides)
    {
        if (sides == ChromeSides.None) return;
        for (var y = 0; y < c.H; y++)
        for (var x = 0; x < c.W; x++)
        {
            var best = int.MaxValue; var lit = false;
            if (sides.HasFlag(ChromeSides.Top) && y < best) { best = y; lit = true; }
            if (sides.HasFlag(ChromeSides.Left) && x < best) { best = x; lit = true; }
            if (sides.HasFlag(ChromeSides.Bottom) && c.H - 1 - y < best) { best = c.H - 1 - y; lit = false; }
            if (sides.HasFlag(ChromeSides.Right) && c.W - 1 - x < best) { best = c.W - 1 - x; lit = false; }
            if (best >= 3 * Scale) continue;
            var i = y * c.W + x;
            c.Color[i] = best < Scale ? Ink : best < 2 * Scale ? (lit ? BrassLight : BrassDark) : Brass;
        }
    }

    /// <summary>A transparent region of the Legacy frame inside the panel, in Legacy pixels; <c>Round</c> when it fills a circle's share of
    /// its box (a round socket), else a rectangle.</summary>
    public readonly record struct Hole(int X0, int Y0, int X1, int Y1, bool Round);

    public static IReadOnlyList<Hole> Holes(int width, int height, ReadOnlySpan<byte> legacy) => Holes(new Canvas(width, height, legacy.ToArray(), null));

    private static List<Hole> Holes(Canvas c)
    {
        var seen = new bool[c.LegacyW * c.LegacyH];
        var holes = new List<Hole>();
        var queue = new Queue<int>();
        for (var start = 0; start < seen.Length; start++)
        {
            if (seen[start] || c.LegacyOpaque(start % c.LegacyW, start / c.LegacyW)) continue;
            int x0 = int.MaxValue, y0 = int.MaxValue, x1 = -1, y1 = -1, count = 0;
            var border = false;
            seen[start] = true; queue.Enqueue(start);
            while (queue.Count > 0)
            {
                var p = queue.Dequeue();
                int px = p % c.LegacyW, py = p / c.LegacyW;
                count++; x0 = Math.Min(x0, px); y0 = Math.Min(y0, py); x1 = Math.Max(x1, px); y1 = Math.Max(y1, py);
                border |= px == 0 || py == 0 || px == c.LegacyW - 1 || py == c.LegacyH - 1;
                foreach (var (nx, ny) in new[] { (px - 1, py), (px + 1, py), (px, py - 1), (px, py + 1) })
                {
                    if (nx < 0 || ny < 0 || nx >= c.LegacyW || ny >= c.LegacyH) continue;
                    var n = ny * c.LegacyW + nx;
                    if (seen[n] || c.LegacyOpaque(nx, ny)) continue;
                    seen[n] = true; queue.Enqueue(n);
                }
            }
            var box = (x1 - x0 + 1) * (y1 - y0 + 1);
            if (border && count * 4 > c.LegacyW * c.LegacyH) continue; // the outside of a shaped frame, not a hole
            if (count < MinHolePixels) continue; // a stray transparent speck gets no frame
            holes.Add(new Hole(x0, y0, x1 + 1, y1 + 1, IsRound(count, x1 - x0 + 1, y1 - y0 + 1)));
        }
        return holes;
    }

    private const int MinHolePixels = 6;

    // Round: a near-square box (sides within one Legacy pixel) that the shape fills like a circle (π/4 ≈ 0.785 of the box).
    private static bool IsRound(int count, int width, int height) =>
        Math.Abs(width - height) <= 1 && (float)count / (width * height) is > 0.6f and < 0.95f;

    private static void HoleRim(Canvas c, Hole hole)
    {
        if (hole.Round) { RoundRim(c, hole); return; }
        float hx0 = hole.X0 * Scale, hy0 = hole.Y0 * Scale, hx1 = hole.X1 * Scale, hy1 = hole.Y1 * Scale;
        var reach = (int)(2.5f * Scale);
        for (var y = Math.Max(0, (int)hy0 - reach); y < Math.Min(c.H, (int)hy1 + reach); y++)
        for (var x = Math.Max(0, (int)hx0 - reach); x < Math.Min(c.W, (int)hx1 + reach); x++)
        {
            var i = y * c.W + x;
            if (c.Alpha[i] <= 0) continue;
            float px = x + 0.5f, py = y + 0.5f;
            float dx = px < hx0 ? hx0 - px : px > hx1 ? px - hx1 : 0, dy = py < hy0 ? hy0 - py : py > hy1 ? py - hy1 : 0;
            var d = MathF.Max(dx, dy);
            if (d <= 0 || d >= reach) continue; // only the opaque band around the hole's box, never an opaque island inside it
            var lit = dy >= dx ? py < hy0 : px < hx0;
            c.Color[i] = d < Scale ? Ink : lit ? BrassLight : BrassDark;
        }
    }

    private static void RoundRim(Canvas c, Hole hole)
    {
        float cx = (hole.X0 + hole.X1) * 0.5f * Scale, cy = (hole.Y0 + hole.Y1) * 0.5f * Scale;
        var r = ((hole.X1 - hole.X0) + (hole.Y1 - hole.Y0)) * 0.25f * Scale;
        var ring = 3f * Scale;
        var reach = (int)(r + ring + 2);
        for (var y = Math.Max(0, (int)(cy - reach)); y < Math.Min(c.H, (int)(cy + reach)); y++)
        for (var x = Math.Max(0, (int)(cx - reach)); x < Math.Min(c.W, (int)(cx + reach)); x++)
        {
            var i = y * c.W + x;
            float dx = x + 0.5f - cx, dy = y + 0.5f - cy, d = MathF.Sqrt(dx * dx + dy * dy);
            if (d < r + 0.5f)
            {
                if (x < hole.X0 * Scale || y < hole.Y0 * Scale || x >= hole.X1 * Scale || y >= hole.Y1 * Scale) continue;
                // Inside the socket: the anti-aliased circular edge replaces the Legacy stairs.
                c.Alpha[i] = MathF.Min(c.Alpha[i], Math.Clamp(d - r + 0.5f, 0, 1));
                if (c.Alpha[i] > 0) c.Color[i] = Ink;
                continue;
            }
            if (c.Alpha[i] <= 0 || d > r + ring + 0.5f) continue;
            var t = (d - r) / ring;
            var facing = -(dx + dy) / (1.4142f * d);
            var brass = Rgb.Lerp(BrassDark, BrassLight, Math.Clamp(0.5f + 0.5f * (t < 0.5f ? -facing : facing), 0, 1));
            var colour = t < 0.18f ? Rgb.Lerp(Ink, brass, t / 0.18f) : t > 0.85f ? Rgb.Lerp(brass, Ink, (t - 0.85f) / 0.15f) : brass;
            c.Blend(x, y, colour, Math.Clamp(r + ring + 0.5f - d, 0, 1));
        }
    }

    private static void PaintButton(Canvas c, bool down, bool hover)
    {
        // The silhouette's box and kind (round when its fill is a circle's share of the box).
        int x0 = int.MaxValue, y0 = int.MaxValue, x1 = -1, y1 = -1, count = 0;
        for (var y = 0; y < c.LegacyH; y++)
        for (var x = 0; x < c.LegacyW; x++)
            if (c.LegacyOpaque(x, y)) { count++; x0 = Math.Min(x0, x); y0 = Math.Min(y0, y); x1 = Math.Max(x1, x); y1 = Math.Max(y1, y); }
        if (count == 0) return;
        var round = IsRound(count, x1 - x0 + 1, y1 - y0 + 1);
        float bx0 = x0 * Scale, by0 = y0 * Scale, bx1 = (x1 + 1) * Scale, by1 = (y1 + 1) * Scale;
        float cx = (bx0 + bx1) * 0.5f, cy = (by0 + by1) * 0.5f, radius = MathF.Min(bx1 - bx0, by1 - by0) * 0.5f;
        var glyph = GlyphMask(c, x0, y0, x1, y1);
        for (var y = (int)by0; y < (int)by1; y++)
        for (var x = (int)bx0; x < (int)bx1; x++)
        {
            var i = y * c.W + x;
            if (c.Alpha[i] <= 0) continue;
            float px = x + 0.5f, py = y + 0.5f, dx = px - cx, dy = py - cy;
            float inset; bool lit;
            if (round)
            {
                var d = MathF.Sqrt(dx * dx + dy * dy);
                inset = radius - d;
                lit = dx + dy < 0;
                c.Alpha[i] = MathF.Min(c.Alpha[i], Math.Clamp(inset + 0.5f, 0, 1));
            }
            else
            {
                float left = px - bx0, right = bx1 - px, top = py - by0, bottom = by1 - py;
                inset = MathF.Min(MathF.Min(left, right), MathF.Min(top, bottom));
                lit = inset == left || inset == top;
            }
            if (down) lit = !lit;
            var faceT = Math.Clamp(MathF.Sqrt(dx * dx + dy * dy) / MathF.Max(1, radius), 0, 1);
            var face = Rgb.Lerp(FaceHigh, FaceLow, down ? 1 - faceT * 0.6f : faceT);
            var rim = lit ? BrassLight : BrassDark;
            // Hover lifts the face and the rim toward light brass (the original's hover frame is the lit one).
            if (hover) { face = Rgb.Lerp(face, BrassLight, 0.3f); rim = Rgb.Lerp(rim, White, 0.2f); }
            Rgb colour = inset < Scale ? Ink : inset < 3 * Scale ? rim : inset < 3.6f * Scale ? Ink : face;
            if (inset >= 3.6f * Scale && glyph[(y / Scale) * c.LegacyW + x / Scale]) colour = c.PictureAt(x, y);
            c.Color[i] = colour;
        }
    }

    // The glyph of a button: Legacy pixels away from its edge whose colour stands out from the face's median, grown by one pixel.
    private static bool[] GlyphMask(Canvas c, int x0, int y0, int x1, int y1)
    {
        const int Inset = 5, Threshold = 60;
        var mask = new bool[c.LegacyW * c.LegacyH];
        var rs = new List<int>(); var gs = new List<int>(); var bs = new List<int>();
        for (var y = y0 + Inset; y <= y1 - Inset; y++)
        for (var x = x0 + Inset; x <= x1 - Inset; x++)
        {
            if (!c.LegacyOpaque(x, y)) continue;
            var j = (y * c.LegacyW + x) * 4;
            rs.Add(c.Legacy[j]); gs.Add(c.Legacy[j + 1]); bs.Add(c.Legacy[j + 2]);
        }
        if (rs.Count == 0) return mask;
        rs.Sort(); gs.Sort(); bs.Sort();
        int mr = rs[rs.Count / 2], mg = gs[gs.Count / 2], mb = bs[bs.Count / 2];
        var core = new bool[mask.Length];
        for (var y = y0 + Inset; y <= y1 - Inset; y++)
        for (var x = x0 + Inset; x <= x1 - Inset; x++)
        {
            if (!c.LegacyOpaque(x, y)) continue;
            var j = (y * c.LegacyW + x) * 4;
            core[y * c.LegacyW + x] = Math.Abs(c.Legacy[j] - mr) + Math.Abs(c.Legacy[j + 1] - mg) + Math.Abs(c.Legacy[j + 2] - mb) > Threshold;
        }
        for (var y = y0; y <= y1; y++)
        for (var x = x0; x <= x1; x++)
        {
            var hit = false;
            for (var oy = -1; oy <= 1 && !hit; oy++)
            for (var ox = -1; ox <= 1 && !hit; ox++)
            {
                int nx = x + ox, ny = y + oy;
                hit = nx >= 0 && ny >= 0 && nx < c.LegacyW && ny < c.LegacyH && core[ny * c.LegacyW + nx];
            }
            mask[y * c.LegacyW + x] = hit;
        }
        return mask;
    }

    // Deterministic value noise (our own hash), three octaves, in [0, 1).
    private static float Fbm(float x, float y, int seed)
    {
        float sum = 0, amplitude = 0.5f, total = 0;
        for (var octave = 0; octave < 3; octave++)
        {
            sum += amplitude * ValueNoise(x, y, seed + octave * 101);
            total += amplitude; x *= 2.03f; y *= 2.03f; amplitude *= 0.5f;
        }
        return sum / total;
    }

    private static float ValueNoise(float x, float y, int seed)
    {
        int ix = (int)MathF.Floor(x), iy = (int)MathF.Floor(y);
        float fx = x - ix, fy = y - iy;
        fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
        var a = Hash(ix, iy, seed); var b = Hash(ix + 1, iy, seed); var cc = Hash(ix, iy + 1, seed); var d = Hash(ix + 1, iy + 1, seed);
        return a + (b - a) * fx + (cc - a) * fy + (a - b - cc + d) * fx * fy;
    }

    private static float Hash(int x, int y, int seed)
    {
        unchecked
        {
            var h = (uint)(x * 374761393 + y * 668265263 + seed * 2147483647);
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            return (h & 0xFFFFFF) / 16777216f;
        }
    }
}
