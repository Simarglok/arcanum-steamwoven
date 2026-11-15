using System.Diagnostics;
using Arcanum.Data.Hd;

namespace Arcanum.Experiments.RelightLab;

/// <summary>
/// ARC-RES-1978 relighting lab, command line. Reads 8-bit RGBA PNGs (an ImageMagick copy of a Godot capture: <c>magick in.png PNG32:out.png</c>),
/// writes normal maps and prints sizes and timings. Frames cut from the player's art are derived from it: keep them and the output outside git.
/// </summary>
internal static class Program
{
    private const long MaxPixels = 16L * 1024 * 1024;

    private static int Main(string[] args)
    {
        try
        {
            if (args.Length >= 3 && args[0] == "normals") return Normals(args);
            if (args.Length >= 2 && args[0] == "bench") return Bench(args);
            if (args.Length >= 2 && args[0] == "shimmer") return Shimmer(args);
            Console.Error.WriteLine("usage: normals IN.png OUT.png [--kind ground|object|critter] [--convention opengl|image] [--gain G --blur N --bevel B --bevel-stride S]\n"
                + "       bench DIR [--kind ground|object|critter]   (every *.png of DIR: milliseconds and bytes per frame, and a total)\n"
                + "       shimmer DIR [--kind ...]   (consecutive same-size frames of one animation, in name order: normal shimmer under a fixed light)");
            return 2;
        }
        catch (Exception exception) when (exception is IOException or ArgumentException or InvalidOperationException)
        {
            Console.Error.WriteLine("relight-lab: FAIL: " + exception.Message);
            return 1;
        }
    }

    private static (int Width, int Height, byte[] Rgba) Read(string path)
    {
        var result = HdPng.Decode(File.ReadAllBytes(path), MaxPixels);
        if (result.Image is not { } image) throw new InvalidOperationException($"{path}: {result.Status} {result.Detail}");
        return (image.Width, image.Height, image.Rgba);
    }

    private static NormalMapOptions Options(string[] args, SpriteKind kind)
    {
        var o = NormalMapOptions.For(kind);
        double Number(string name, double fallback) => Array.IndexOf(args, name) is var i and >= 0 && i + 1 < args.Length
            ? double.Parse(args[i + 1], System.Globalization.CultureInfo.InvariantCulture) : fallback;
        return o with { Gain = Number("--gain", o.Gain), Blur = (int)Number("--blur", o.Blur), Bevel = Number("--bevel", o.Bevel), BevelStride = (int)Number("--bevel-stride", o.BevelStride) };
    }

    private static SpriteKind Kind(string[] args) => Array.IndexOf(args, "--kind") is var i and >= 0 && i + 1 < args.Length
        ? Enum.Parse<SpriteKind>(args[i + 1], true) : SpriteKind.Object;

    private static int Normals(string[] args)
    {
        var kind = Kind(args);
        var convention = Array.IndexOf(args, "--convention") is var c and >= 0 && c + 1 < args.Length && args[c + 1] == "image" ? NormalConvention.ImageSpace : NormalConvention.OpenGl;
        var (w, h, rgba) = Read(args[1]);
        var map = NormalMapGenerator.Generate(rgba, w, h, Options(args, kind), convention);
        File.WriteAllBytes(args[2], HdPng.EncodeRgba(map.Width, map.Height, map.Rgba));
        Console.WriteLine($"normals {w}x{h} kind={kind} convention={convention} -> {args[2]}");
        return 0;
    }

    private static int Shimmer(string[] args)
    {
        var kind = Kind(args);
        var files = Directory.GetFiles(args[1], "*.png").OrderBy(f => f, StringComparer.Ordinal).ToArray();
        var options = Options(args, kind);
        var ratios = new List<double>(); var angles = new List<double>(); var skipped = 0;
        (int W, int H, byte[] Rgba, NormalMap Map)? previous = null;
        foreach (var file in files)
        {
            var (w, h, rgba) = Read(file);
            var current = (W: w, H: h, Rgba: rgba, Map: NormalMapGenerator.Generate(rgba, w, h, options));
            if (previous is { } p && p.W == w && p.H == h)
            {
                ratios.Add(NormalMapGenerator.ShimmerRatio(p.Rgba, p.Map, rgba, current.Map, (-1, -1, 1)));
                angles.Add(NormalMapGenerator.MeanAngleDegrees(p.Map, current.Map));
            }
            else if (previous is not null) skipped++;
            previous = current;
        }
        Console.WriteLine($"shimmer frames={files.Length} pairs={ratios.Count} skipped-different-size={skipped}"
            + (ratios.Count == 0 ? "" : $" ratio mean={ratios.Average():0.000} max={ratios.Max():0.000} mean-normal-change={angles.Average():0.0} deg"));
        return 0;
    }

    private static int Bench(string[] args)
    {
        var kind = Kind(args);
        var files = Directory.GetFiles(args[1], "*.png").OrderBy(f => f, StringComparer.Ordinal).ToArray();
        long pixels = 0; double ms = 0;
        foreach (var file in files)
        {
            var (w, h, rgba) = Read(file);
            var watch = Stopwatch.StartNew();
            var map = NormalMapGenerator.Generate(rgba, w, h, Options(args, kind));
            watch.Stop();
            pixels += (long)w * h; ms += watch.Elapsed.TotalMilliseconds;
            Console.WriteLine($"{Path.GetFileName(file)} {w}x{h} {watch.Elapsed.TotalMilliseconds:0.00} ms, {map.Rgba.Length} bytes");
        }
        Console.WriteLine($"total {files.Length} frames {pixels} px {ms:0.0} ms ({(pixels == 0 ? 0 : ms / (pixels / 1e6)):0.0} ms per megapixel), normal maps {pixels * 4} bytes as RGBA8");
        return 0;
    }
}
