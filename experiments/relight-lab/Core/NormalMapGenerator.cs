namespace Arcanum.Experiments.RelightLab;

/// <summary>What a sprite is, for the normal strength: ground is nearly flat, objects (walls, scenery) and critters bend more.</summary>
public enum SpriteKind { Ground, Object, Critter }

/// <summary>Which way the green channel points. Godot's <c>CanvasTexture</c> normal maps are OpenGL style (Y+ up, so green is the up
/// component); the viewer's relit shader works in image space (Y down) and needs no flip.</summary>
public enum NormalConvention { OpenGl, ImageSpace }

/// <summary>Knobs of <see cref="NormalMapGenerator"/>. All values are plain numbers so a run is reproducible from them.</summary>
/// <param name="Gain">Slope gain: how strongly the luminance gradient bends the normal (the shader's <c>relight_kinds</c>).</param>
/// <param name="Blur">Box blur radius of the height before the Sobel (0 none, 1 = 3x3). Blur calms palette dither and edge noise.</param>
/// <param name="Bevel">How much the silhouette (alpha) edge bends the normal outwards; 0 leaves a silhouette flat.</param>
/// <param name="AlphaThreshold">Alpha (0..255) below which a texel counts as empty.</param>
/// <param name="BevelStride">How many texels away the silhouette bevel reads the alpha (1 = the neighbouring texel). The viewer's relit shader uses the frame's scale N for an
/// N-times HD frame (<c>relight_scale</c>) so the bevel reaches the same distance as in the 1x look (ARC-RENDER-2063); only the bevel reads it, the Sobel of the height always takes the neighbouring texel.</param>
public sealed record NormalMapOptions(double Gain, int Blur, double Bevel, int AlphaThreshold = 128, int BevelStride = 1)
{
    public static NormalMapOptions For(SpriteKind kind) => kind switch
    {
        SpriteKind.Ground => new(0.15, 1, 0.0),
        SpriteKind.Object => new(0.5, 0, 0.6),
        SpriteKind.Critter => new(0.5, 1, 0.6),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };
}

/// <summary>A normal map: per texel the unit normal packed as 8-bit RGB (n * 0.5 + 0.5) with the sprite's alpha, row-major RGBA.</summary>
public sealed record NormalMap(int Width, int Height, byte[] Rgba, NormalConvention Convention)
{
    /// <summary>The unit normal of a texel (image space: x right, y down, z toward the viewer), whatever the stored convention.</summary>
    public (double X, double Y, double Z) Normal(int x, int y)
    {
        var i = (y * Width + x) * 4;
        var nx = Rgba[i] / 255.0 * 2 - 1; var ny = Rgba[i + 1] / 255.0 * 2 - 1; var nz = Rgba[i + 2] / 255.0 * 2 - 1;
        if (Convention == NormalConvention.OpenGl) ny = -ny;
        var length = Math.Sqrt(nx * nx + ny * ny + nz * nz);
        return length < 1e-9 ? (0, 0, 1) : (nx / length, ny / length, nz / length);
    }
}

/// <summary>
/// ARC-RES-1978 (FR-LIGHT-013 experiment): a small deterministic normal-map generator for the original sprites, which are pre-rendered with
/// their shading baked in, so there is no height map to read: the height is the luminance. The normal comes from the 3x3 Sobel of that
/// height (optionally blurred first), alpha aware: a transparent neighbour takes the centre's height (a silhouette is not a cliff) and the
/// alpha edge adds an outward bevel. It is the CPU counterpart of the viewer's relit shader (<c>tools/terrain-viewer/Godot/AreaRelight.cs</c>: the same Sobel, bevel sign and
/// gains; the shader has no blur and uses a continuous alpha in the bevel), so a frame can be checked, batch-generated for a later HD pack, or measured here. Integer maths where it matters, no random numbers, no
/// time: the same pixels and options always give the same bytes.
/// </summary>
public static class NormalMapGenerator
{
    public static NormalMap Generate(ReadOnlySpan<byte> rgba, int width, int height, NormalMapOptions options, NormalConvention convention = NormalConvention.OpenGl)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (width <= 0 || height <= 0 || rgba.Length != checked(width * height * 4)) throw new ArgumentException("rgba must be width * height * 4 bytes", nameof(rgba));
        if (options.Blur < 0 || options.Blur > 4) throw new ArgumentOutOfRangeException(nameof(options), "Blur is 0..4");
        if (options.BevelStride < 1 || options.BevelStride > 16) throw new ArgumentOutOfRangeException(nameof(options), "BevelStride is 1..16");
        var luminance = new double[width * height];
        var alpha = new byte[width * height];
        for (var i = 0; i < width * height; i++)
        {
            luminance[i] = (0.299 * rgba[i * 4] + 0.587 * rgba[i * 4 + 1] + 0.114 * rgba[i * 4 + 2]) / 255.0;
            alpha[i] = rgba[i * 4 + 3];
        }
        bool Solid(int x, int y) => x >= 0 && y >= 0 && x < width && y < height && alpha[y * width + x] >= options.AlphaThreshold;
        // Height: the luminance, the empty neighbours replaced by the centre's own (so a blur does not drag the silhouette's black in).
        double Raw(int x, int y, double centre) => Solid(x, y) ? luminance[y * width + x] : centre;
        var height0 = new double[width * height];
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var centre = luminance[y * width + x];
                if (options.Blur == 0) { height0[y * width + x] = centre; continue; }
                double sum = 0; var n = 0;
                for (var dy = -options.Blur; dy <= options.Blur; dy++)
                    for (var dx = -options.Blur; dx <= options.Blur; dx++) { sum += Raw(x + dx, y + dy, centre); n++; }
                height0[y * width + x] = sum / n;
            }
        double H(int x, int y, double centre) => Solid(x, y) ? height0[y * width + x] : centre;
        double A(int x, int y) => Solid(x, y) ? 1.0 : 0.0;
        var output = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var o = (y * width + x) * 4;
                output[o + 3] = alpha[y * width + x];
                if (!Solid(x, y)) { output[o] = 128; output[o + 1] = 128; output[o + 2] = 255; continue; }
                var c = height0[y * width + x];
                double h(int dx, int dy) => H(x + dx, y + dy, c);
                var gx = (h(1, -1) + 2 * h(1, 0) + h(1, 1)) - (h(-1, -1) + 2 * h(-1, 0) + h(-1, 1));
                var gy = (h(-1, 1) + 2 * h(0, 1) + h(1, 1)) - (h(-1, -1) + 2 * h(0, -1) + h(1, -1));
                var s = options.BevelStride; // the shader's relight_scale: the alpha taps reach N texels (the centre tap stays the texel itself)
                var ex = ((A(x - s, y - s) + 2 * A(x - s, y) + A(x - s, y + s)) - (A(x + s, y - s) + 2 * A(x + s, y) + A(x + s, y + s))) * 0.25;
                var ey = ((A(x - s, y - s) + 2 * A(x, y - s) + A(x + s, y - s)) - (A(x - s, y + s) + 2 * A(x, y + s) + A(x + s, y + s))) * 0.25;
                var nx = -gx * options.Gain * 2 + ex * options.Bevel;
                var ny = -gy * options.Gain * 2 + ey * options.Bevel;
                var length = Math.Sqrt(nx * nx + ny * ny + 1);
                nx /= length; ny /= length; var nz = 1 / length;
                if (convention == NormalConvention.OpenGl) ny = -ny;
                output[o] = Pack(nx); output[o + 1] = Pack(ny); output[o + 2] = Pack(nz);
            }
        return new NormalMap(width, height, output, convention);
    }

    private static byte Pack(double component) => (byte)Math.Clamp((int)Math.Round((component * 0.5 + 0.5) * 255.0), 0, 255);

    /// <summary>Mean angle in degrees between the normals of two same-size maps over the texels solid in both (0 when none are).</summary>
    public static double MeanAngleDegrees(NormalMap a, NormalMap b, int alphaThreshold = 128)
    {
        if (a.Width != b.Width || a.Height != b.Height) throw new ArgumentException("maps must be the same size");
        double sum = 0; long n = 0;
        for (var y = 0; y < a.Height; y++)
            for (var x = 0; x < a.Width; x++)
            {
                var i = (y * a.Width + x) * 4 + 3;
                if (a.Rgba[i] < alphaThreshold || b.Rgba[i] < alphaThreshold) continue;
                var (ax, ay, az) = a.Normal(x, y); var (bx, by, bz) = b.Normal(x, y);
                sum += Math.Acos(Math.Clamp(ax * bx + ay * by + az * bz, -1, 1)) * 180 / Math.PI; n++;
            }
        return n == 0 ? 0 : sum / n;
    }

    /// <summary>
    /// Frame-to-frame shimmer of a Lambert term under one fixed light: the mean absolute change of N·L between the same texel of two
    /// consecutive frames, over the texels solid in both, divided by the same change of the sprites' own luminance. A ratio well under 1
    /// means the derived normals move less than the picture does; a ratio over 1 means they amplify frame noise.
    /// </summary>
    public static double ShimmerRatio(ReadOnlySpan<byte> rgbaA, NormalMap a, ReadOnlySpan<byte> rgbaB, NormalMap b, (double X, double Y, double Z) light, int alphaThreshold = 128)
    {
        double term = 0, plain = 0; long n = 0;
        var length = Math.Sqrt(light.X * light.X + light.Y * light.Y + light.Z * light.Z);
        var (lx, ly, lz) = (light.X / length, light.Y / length, light.Z / length);
        for (var y = 0; y < a.Height; y++)
            for (var x = 0; x < a.Width; x++)
            {
                var i = (y * a.Width + x) * 4;
                if (rgbaA[i + 3] < alphaThreshold || rgbaB[i + 3] < alphaThreshold) continue;
                var (ax, ay, az) = a.Normal(x, y); var (bx, by, bz) = b.Normal(x, y);
                term += Math.Abs(Math.Max(ax * lx + ay * ly + az * lz, 0) - Math.Max(bx * lx + by * ly + bz * lz, 0));
                plain += Math.Abs((0.299 * rgbaA[i] + 0.587 * rgbaA[i + 1] + 0.114 * rgbaA[i + 2]) - (0.299 * rgbaB[i] + 0.587 * rgbaB[i + 1] + 0.114 * rgbaB[i + 2])) / 255.0;
                n++;
            }
        return n == 0 || plain == 0 ? 0 : term / plain;
    }
}
