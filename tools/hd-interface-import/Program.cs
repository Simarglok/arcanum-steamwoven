using Arcanum.Data.Content;
using Arcanum.Data.Hd;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;

namespace Arcanum.Tools.HdInterfaceCli;

/// <summary>
/// ARC-DEV-242: imports the owner's local redraws of interface art into an HD pack. Reads the player's own installation (the module
/// archive, like the viewer) and a local plan file, builds the HD frames (registration, shading and colour lock, Legacy alpha with an anti-aliased edge, ARC-UI-111) and writes
/// them as the pack's <c>interface</c> slice. Nothing it reads or writes goes into git.
/// </summary>
internal static class Program
{
    private const string Usage = "usage: hd-interface-import --module PATH/modules/Arcanum.dat --plan PLAN.json --pack PACK_DIR [--dry-run]\n"
        + "  --module   the module archive of the installed game (as the viewer takes it)\n"
        + "  --plan     the local import plan (kind arcanum-steamwoven-hd-interface-plan); image paths are relative to it\n"
        + "  --pack     the pack directory: an existing pack gets the 'interface' slice added or replaced, a missing one is created\n"
        + "  --dry-run  build and report every frame, write nothing";

    private static int Main(string[] args) => Run(args, Console.Out, Console.Error);

    internal static int Run(string[] args, TextWriter stdout, TextWriter stderr)
    {
        if (args.Length == 1 && args[0] is "--help" or "-h") { stdout.WriteLine(Usage); return 0; }
        string? module = null, plan = null, pack = null; var dry = false;
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--module" when i + 1 < args.Length: module = args[++i]; break;
                case "--plan" when i + 1 < args.Length: plan = args[++i]; break;
                case "--pack" when i + 1 < args.Length: pack = args[++i]; break;
                case "--dry-run": dry = true; break;
                default: stderr.WriteLine($"hd-interface-import: unknown or incomplete argument {args[i]}\n{Usage}"); return 2;
            }
        }
        if (module is null || plan is null || pack is null) { stderr.WriteLine("hd-interface-import: --module, --plan and --pack are required\n" + Usage); return 2; }

        HdInterfacePlan parsed;
        try { parsed = HdInterfacePlan.Parse(File.ReadAllBytes(plan)); }
        catch (Exception error) when (error is FormatException or IOException or UnauthorizedAccessException)
        { stderr.WriteLine("hd-interface-import: plan: " + error.Message); return 2; }

        var found = AreaContentRoot.Discover(module);
        if (found.Status != AreaContentRootStatus.Ready) { stderr.WriteLine($"hd-interface-import: game not read ({found.Status} {found.Detail})"); return 3; }
        var opened = ContentStack.Open(found.Plan!);
        if (opened.Status != ContentStackStatus.Ready) { stderr.WriteLine($"hd-interface-import: content stack {opened.Status}"); return 3; }
        using var stack = opened.Stack!;

        var planDirectory = Path.GetDirectoryName(Path.GetFullPath(plan))!;
        var arts = new Dictionary<string, HdInterfaceArt?>(StringComparer.OrdinalIgnoreCase);
        HdInterfaceArt? Art(string name)
        {
            if (arts.TryGetValue(name, out var cached)) return cached;
            var load = AreaInterfaceArt.LoadNamed(stack, name);
            return arts[name] = load.Status == AreaInterfaceArtStatus.Ready && load.Identity is { } identity
                ? new HdInterfaceArt(load.Name ?? name, identity, [.. load.Frames.Select(f => new HdLegacyRaster(f.Width, f.Height, f.Rgba))])
                : null;
        }
        HdRgbImage? Image(string path)
        {
            try
            {
                var (status, image, detail) = HdImagePng.Decode(File.ReadAllBytes(Path.GetFullPath(Path.Combine(planDirectory, path))), 1L << 26);
                if (status != HdPngStatus.Decoded) stderr.WriteLine($"hd-interface-import: image {Path.GetFileName(path)}: {status} {detail}");
                return image;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return null; }
        }

        var built = HdInterfaceImport.Build(parsed, Art, Image);
        foreach (var frame in built.Frames)
            stdout.WriteLine($"built {frame.Art.FileName} frame {frame.Frame} {frame.Width * parsed.Scale}x{frame.Height * parsed.Scale} fit {frame.Ncc:F3} (scale {frame.Fit.Sx:F2}x{frame.Fit.Sy:F2}, offset {frame.Fit.Tx:F1},{frame.Fit.Ty:F1})");
        foreach (var problem in built.Problems) stderr.WriteLine("hd-interface-import: " + problem);
        if (!built.Ok) { stderr.WriteLine("hd-interface-import: FAIL (nothing written)"); return 3; }
        if (dry) { stdout.WriteLine($"PASS dry run, {built.Frames.Count} frames"); return 0; }

        var written = HdInterfacePackWriter.Write(pack, parsed.PackId, parsed.Scale, built.Frames, DateTimeOffset.UtcNow);
        if (written.Status != HdInterfaceWriteStatus.Written) { stderr.WriteLine($"hd-interface-import: FAIL {written.Status} {written.Detail}"); return 3; }
        stdout.WriteLine($"PASS {written.Entries} interface frames written ({written.Replaced} replaced); {written.Detail}");
        return 0;
    }
}
