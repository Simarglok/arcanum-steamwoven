using System.Text;
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
        + "  --plan     an interface plan (kind arcanum-steamwoven-hd-interface-plan) or, ARC-UI-2865, a portrait plan (kind arcanum-steamwoven-hd-portrait-plan: the redraws of portrait\\*.bmp, written as 'portrait' entries)\n"
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

        var planBytes = File.ReadAllBytes(plan);
        var portraitPlan = Encoding.UTF8.GetString(planBytes).Contains(HdPortraitPlan.Kind, StringComparison.Ordinal);
        HdInterfacePlan? parsed = null;
        HdPortraitPlan? portraits = null;
        try { if (portraitPlan) portraits = HdPortraitPlan.Parse(planBytes); else parsed = HdInterfacePlan.Parse(planBytes); }
        catch (Exception error) when (error is FormatException or IOException or UnauthorizedAccessException)
        { stderr.WriteLine("hd-interface-import: plan: " + error.Message); return 2; }

        var found = AreaContentRoot.Discover(module);
        if (found.Status != AreaContentRootStatus.Ready) { stderr.WriteLine($"hd-interface-import: game not read ({found.Status} {found.Detail})"); return 3; }
        var opened = ContentStack.Open(found.Plan!);
        if (opened.Status != ContentStackStatus.Ready) { stderr.WriteLine($"hd-interface-import: content stack {opened.Status}"); return 3; }
        using var stack = opened.Stack!;

        var planDirectory = Path.GetDirectoryName(Path.GetFullPath(plan))!;
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
        if (portraits is not null) return RunPortraits(portraits, stack, Image, pack, dry, stdout, stderr);
        var interfacePlan = parsed!;
        var arts = new Dictionary<string, HdInterfaceArt?>(StringComparer.OrdinalIgnoreCase);
        HdInterfaceArt? Art(string name)
        {
            if (arts.TryGetValue(name, out var cached)) return cached;
            // ARC-DEV-361: a bitmap font has 225 frames, more than the default limit of 32 per file.
            var load = AreaInterfaceArt.LoadNamed(stack, name, new AreaInterfaceArtLimits(MaxFrames: 256));
            if (load.Status != AreaInterfaceArtStatus.Ready || load.Identity is null) stderr.WriteLine($"hd-interface-import: art {name} not read: {load.Status} {load.Detail}");
            return arts[name] = load.Status == AreaInterfaceArtStatus.Ready && load.Identity is { } identity
                ? new HdInterfaceArt(load.Name ?? name, identity, [.. load.Frames.Select(f => new HdLegacyRaster(f.Width, f.Height, f.Rgba))])
                : null;
        }
        var built = HdInterfaceImport.Build(interfacePlan, Art, Image);
        foreach (var frame in built.Frames)
            stdout.WriteLine($"built {frame.Art.FileName} frame {frame.Frame} {frame.Width * interfacePlan.Scale}x{frame.Height * interfacePlan.Scale} fit {frame.Ncc:F3} (scale {frame.Fit.Sx:F2}x{frame.Fit.Sy:F2}, offset {frame.Fit.Tx:F1},{frame.Fit.Ty:F1})");
        foreach (var problem in built.Problems) stderr.WriteLine("hd-interface-import: " + problem);
        if (!built.Ok) { stderr.WriteLine("hd-interface-import: FAIL (nothing written)"); return 3; }
        if (dry) { stdout.WriteLine($"PASS dry run, {built.Frames.Count} frames"); return 0; }

        var written = HdInterfacePackWriter.Write(pack, interfacePlan.PackId, interfacePlan.Scale, built.Frames, DateTimeOffset.UtcNow);
        if (written.Status != HdInterfaceWriteStatus.Written) { stderr.WriteLine($"hd-interface-import: FAIL {written.Status} {written.Detail}"); return 3; }
        stdout.WriteLine($"PASS {written.Entries} interface frames written ({written.Replaced} replaced); {written.Detail}");
        return 0;
    }

    // ARC-UI-2865: a portrait plan: each redraw is checked against the original's bitmap read from the content stack (the winner of portrait/<name>.bmp, as the viewer reads it).
    private static int RunPortraits(HdPortraitPlan plan, ContentStack stack, Func<string, HdRgbImage?> image, string pack, bool dry, TextWriter stdout, TextWriter stderr)
    {
        byte[]? Bitmap(string path)
        {
            if (stack.Resolve(path).Winner is not { } winner || winner.Length > AreaPortraits.MaxFileBytes) return null;
            var read = stack.Read(winner);
            return read.Status == ContentReadStatus.Read ? read.Bytes : null;
        }
        var built = HdPortraitImport.Build(plan, Bitmap, image);
        foreach (var portrait in built.Portraits) stdout.WriteLine($"built {portrait.Path} {portrait.Width * plan.Scale}x{portrait.Height * plan.Scale}");
        foreach (var problem in built.Problems) stderr.WriteLine("hd-interface-import: " + problem);
        if (!built.Ok) { stderr.WriteLine("hd-interface-import: FAIL (nothing written)"); return 3; }
        if (dry) { stdout.WriteLine($"PASS dry run, {built.Portraits.Count} portraits"); return 0; }
        var written = HdInterfacePackWriter.Write(pack, plan.PackId, plan.Scale, [], DateTimeOffset.UtcNow, built.Portraits);
        if (written.Status != HdInterfaceWriteStatus.Written) { stderr.WriteLine($"hd-interface-import: FAIL {written.Status} {written.Detail}"); return 3; }
        stdout.WriteLine($"PASS {written.Entries} portraits written ({written.Replaced} replaced); {written.Detail}");
        return 0;
    }
}
