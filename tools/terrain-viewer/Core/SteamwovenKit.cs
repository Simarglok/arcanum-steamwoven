using System.Text.Json;
using Arcanum.Presentation.Steamwoven;

namespace Arcanum.TerrainViewer.Core;

/// <summary>One part of a baked set: its id and its size in pixels at the set's step.</summary>
public readonly record struct SteamwovenKitPart(string Id, int Width, int Height);

/// <summary>
/// ARC-UI-2461 (SWI-RUN-1, SWI-RUN-2): one baked set of the Steamwoven kit, <c>&lt;root&gt;/&lt;step&gt;/manifest.json</c> with <c>&lt;part id&gt;.png</c> beside it, as <c>tools/steamwoven_ui/bake.py</c> writes it.
/// Godot-free: it reads the manifest and checks that every listed file is there and that each baked size is the master size times the step, exactly; the viewer loads the pixels. A set that does not
/// read is no set (the fallback is drawn: the HD pack's frame, the Legacy art where the pack lacks it, SWI-RUN-3), and a part whose record is broken is left out of it, so the composer sees it as missing.
/// </summary>
public sealed class SteamwovenKitSet
{
    private readonly IReadOnlyDictionary<string, SteamwovenKitPart> _parts;

    private SteamwovenKitSet(SteamwovenStep step, string directory, IReadOnlyDictionary<string, SteamwovenKitPart> parts)
    {
        Step = step; Directory = directory; _parts = parts;
    }

    public SteamwovenStep Step { get; }

    /// <summary>The folder of the set (<c>&lt;root&gt;/x1-2</c>).</summary>
    public string Directory { get; }

    public int Count => _parts.Count;

    public IEnumerable<string> Ids => _parts.Keys;

    public bool TryGet(string id, out SteamwovenKitPart part) => _parts.TryGetValue(id, out part);

    public bool Has(string id) => _parts.ContainsKey(id);

    /// <summary>The file of a part (its id is checked when the set is read, so it stays inside the folder).</summary>
    public string PathOf(string id) => System.IO.Path.Combine(Directory, id + ".png");

    /// <summary>The folder name of a step in the baked tree (the bake tool's: x1, x2-3, x1-2, x1-3).</summary>
    public static string FolderName(SteamwovenStep step) => step switch
    {
        SteamwovenStep.One => "x1",
        SteamwovenStep.TwoThirds => "x2-3",
        SteamwovenStep.Half => "x1-2",
        SteamwovenStep.Third => "x1-3",
        _ => throw new ArgumentOutOfRangeException(nameof(step)),
    };

    /// <summary>A set made of given parts (the tests and the smoke's synthetic kit); the sizes are taken as they are.</summary>
    public static SteamwovenKitSet FromParts(SteamwovenStep step, string directory, IEnumerable<SteamwovenKitPart> parts) =>
        new(step, directory, parts.ToDictionary(part => part.Id, StringComparer.Ordinal));

    /// <summary>A part id is safe to turn into a file name: segments of letters, digits, <c>_</c>, <c>-</c> and inner dots, no <c>..</c>, no leading dot or slash (the bake tool's rule).</summary>
    public static bool ValidId(string? id)
    {
        if (string.IsNullOrEmpty(id) || id.Contains("..", StringComparison.Ordinal) || id.Contains('\\')) return false;
        foreach (var segment in id.Split('/'))
        {
            if (segment.Length == 0 || segment[0] == '.') return false;
            foreach (var c in segment) if (!(char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '.')) return false;
        }
        return true;
    }

    /// <summary>
    /// Reads the set of <paramref name="step"/> under <paramref name="root"/>; null with <paramref name="problem"/> set when the folder, the manifest or its step is wrong. A part whose record is not well formed, whose
    /// baked size is not the master size times the step, or whose file is missing is left out and named in <paramref name="problem"/> (the set still reads).
    /// </summary>
    public static SteamwovenKitSet? Read(string root, SteamwovenStep step, out string? problem)
    {
        problem = null;
        var directory = System.IO.Path.Combine(root, FolderName(step));
        var manifestPath = System.IO.Path.Combine(directory, "manifest.json");
        string text;
        try { text = File.ReadAllText(manifestPath); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { problem = $"{FolderName(step)}: no readable manifest.json ({exception.GetType().Name})"; return null; }
        try
        {
            using var document = JsonDocument.Parse(text);
            var top = document.RootElement;
            if (top.ValueKind != JsonValueKind.Object || !top.TryGetProperty("schema", out var schema) || schema.ValueKind != JsonValueKind.Number || schema.GetInt32() != 1
                || !top.TryGetProperty("parts", out var list) || list.ValueKind != JsonValueKind.Array)
            { problem = $"{FolderName(step)}: manifest.json is not a manifest of schema 1"; return null; }
            var ratio = SteamwovenGrid.StepScale(step);
            if (!top.TryGetProperty("ratio", out var named) || named.ValueKind != JsonValueKind.Array || named.GetArrayLength() != 2
                || named[0].GetInt64() != ratio.Numerator || named[1].GetInt64() != ratio.Denominator)
            { problem = $"{FolderName(step)}: manifest.json is not the set of this step"; return null; }
            var parts = new Dictionary<string, SteamwovenKitPart>(StringComparer.Ordinal);
            var skipped = new List<string>();
            foreach (var entry in list.EnumerateArray())
            {
                var id = entry.ValueKind == JsonValueKind.Object && entry.TryGetProperty("id", out var idElement) && idElement.ValueKind == JsonValueKind.String ? idElement.GetString() : null;
                if (!ValidId(id)) { skipped.Add("(bad id)"); continue; }
                if (!TrySizes(entry, "master", out var masterW, out var masterH) || !TrySizeOf(entry, out var width, out var height)
                    || masterW % SteamwovenGrid.GridPx != 0 || masterH % SteamwovenGrid.GridPx != 0
                    || width != SteamwovenGrid.BakedLength(masterW, step) || height != SteamwovenGrid.BakedLength(masterH, step))
                { skipped.Add(id!); continue; }
                if (!File.Exists(System.IO.Path.Combine(directory, id + ".png"))) { skipped.Add(id!); continue; }
                parts[id!] = new SteamwovenKitPart(id!, width, height);
            }
            if (skipped.Count > 0) problem = $"{FolderName(step)}: left out {string.Join(", ", skipped.Take(8))}{(skipped.Count > 8 ? $" and {skipped.Count - 8} more" : "")}";
            return new SteamwovenKitSet(step, directory, parts);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException or ArgumentOutOfRangeException)
        {
            problem = $"{FolderName(step)}: manifest.json does not parse ({exception.GetType().Name})";
            return null;
        }
    }

    private static bool TrySizeOf(JsonElement entry, out int width, out int height)
    {
        (width, height) = (0, 0);
        return entry.TryGetProperty("size", out var size) && ReadPair(size, out width, out height);
    }

    private static bool TrySizes(JsonElement entry, string name, out int width, out int height)
    {
        (width, height) = (0, 0);
        return entry.TryGetProperty(name, out var master) && master.ValueKind == JsonValueKind.Object && master.TryGetProperty("size", out var size) && ReadPair(size, out width, out height);
    }

    private static bool ReadPair(JsonElement element, out int width, out int height)
    {
        (width, height) = (0, 0);
        if (element.ValueKind != JsonValueKind.Array || element.GetArrayLength() != 2 || !element[0].TryGetInt32(out width) || !element[1].TryGetInt32(out height)) return false;
        return width > 0 && height > 0 && width <= 16384 && height <= 16384;
    }
}

/// <summary>ARC-UI-2461: where the baked sets live on a device (SWI-PRV-4: <c>local/steamwoven-ui/baked/</c> of the owner's checkout; the kit is never committed or packaged, SWI-PRV-2).</summary>
public static class SteamwovenKitRoot
{
    /// <summary>The environment variable that names the folder of the baked sets for a run (the smoke's synthetic kit, a kit on another drive); <c>off</c> turns the kit off for the run.</summary>
    public const string EnvName = "ARC_STEAMWOVEN_KIT";

    /// <summary>The ignored folder a checkout keeps the baked sets in.</summary>
    public static readonly string[] DefaultSegments = ["local", "steamwoven-ui", "baked"];

    /// <summary>
    /// The folder holding the baked sets: <paramref name="env"/> when set (an existing folder, or <c>off</c>/<c>0</c>/<c>none</c> for null), else the first <c>local/steamwoven-ui/baked</c> found on the way up from a start folder
    /// (at most twelve levels; a worktree lives under <c>&lt;checkout&gt;/.claude/worktrees/</c>, so the climb reaches the owner's checkout, where the ignored <c>local/</c> is), else the first existing
    /// <paramref name="installFolders"/> entry: where a packaged game on a player's own device keeps the sets they baked (<c>user://steamwoven-ui/baked</c>, beside the HD packs of <c>user://hd-packs</c>). Null: no kit (the HD pack's frame is drawn, the Legacy art where the pack lacks it).
    /// </summary>
    public static string? Resolve(string? env, IEnumerable<string> startFolders, IEnumerable<string>? installFolders = null)
    {
        if (!string.IsNullOrWhiteSpace(env))
        {
            var value = env.Trim();
            if (value is "off" or "0" or "none") return null;
            return System.IO.Directory.Exists(value) ? value : null;
        }
        foreach (var start in startFolders)
        {
            if (string.IsNullOrWhiteSpace(start)) continue;
            try
            {
                var directory = new DirectoryInfo(System.IO.Path.GetFullPath(start));
                for (var level = 0; directory is not null && level < 12; level++, directory = directory.Parent)
                {
                    var candidate = System.IO.Path.Combine([directory.FullName, .. DefaultSegments]);
                    if (System.IO.Directory.Exists(candidate)) return candidate;
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { }
        }
        foreach (var install in installFolders ?? [])
            if (!string.IsNullOrWhiteSpace(install) && System.IO.Directory.Exists(install)) return install;
        return null;
    }
}
