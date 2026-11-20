using Arcanum.Presentation.Steamwoven;
using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>
/// ARC-UI-2461 (SWI-RUN-2, SWI-RUN-3, SWI-RUN-5): the viewer's holder of the baked Steamwoven kit: it finds the folder of the baked sets (<see cref="SteamwovenKitRoot"/>), reads the set of a step once, and loads a part's PNG into a texture
/// the first time it is drawn. A set, a part or a file that does not read gives null, never an exception, and the caller draws the generated chrome (SWI-RUN-3). Only the open window's background and the plate of the open menu stay
/// resident: <see cref="Retain"/> drops every other background and plate, and a change of step drops the old step's textures. The strips, their caps and the buttons' faces stay loaded.
/// </summary>
internal static class SteamwovenKitStore
{
    private static readonly Dictionary<SteamwovenStep, SteamwovenKitSet?> Sets = [];
    private static readonly Dictionary<string, Texture2D?> Textures = [];
    private static readonly HashSet<string> Reported = [];
    private static string? _rootOverride;
    private static bool _overridden;
    private static string? _root;
    private static bool _resolved;

    /// <summary>The folder of the baked sets for this run, or null when there is none or the kit is turned off (<c>ARC_STEAMWOVEN_KIT=off</c>).</summary>
    public static string? Root
    {
        get
        {
            if (_overridden) return _rootOverride;
            if (_resolved) return _root;
            _resolved = true;
            var env = System.Environment.GetEnvironmentVariable(SteamwovenKitRoot.EnvName);
            string?[] starts = [System.IO.Directory.GetCurrentDirectory(), System.AppContext.BaseDirectory, ProjectSettings.GlobalizePath("res://"), System.IO.Path.GetDirectoryName(OS.GetExecutablePath())];
            _root = SteamwovenKitRoot.Resolve(env, starts.Where(start => start is not null)!, [ProjectSettings.GlobalizePath("user://steamwoven-ui/baked")]);
            return _root;
        }
    }

    /// <summary>The smoke's own folder (a synthetic kit), or none; the sets and textures read so far are forgotten.</summary>
    public static void UseRoot(string? root)
    {
        (_overridden, _rootOverride) = (true, root);
        Reset();
    }

    /// <summary>Back to the folder the environment and the checkout say.</summary>
    public static void ClearOverride()
    {
        (_overridden, _rootOverride) = (false, null);
        Reset();
    }

    /// <summary>Forgets every set and texture (a rebaked kit is read again).</summary>
    public static void Reset()
    {
        Sets.Clear(); Reported.Clear();
        foreach (var texture in Textures.Values) texture?.Dispose();
        Textures.Clear();
    }

    /// <summary>The baked set of a step, read once; null with no folder, no manifest or a manifest of another step.</summary>
    public static SteamwovenKitSet? SetFor(SteamwovenStep step)
    {
        if (Sets.TryGetValue(step, out var known)) return known;
        var root = Root;
        SteamwovenKitSet? set = null;
        if (root is not null)
        {
            set = SteamwovenKitSet.Read(root, step, out var problem);
            if (problem is not null && Reported.Add("set:" + step)) GD.Print("ARC-UI-2461 kit: " + problem);
            // A step is the only one in memory: the others' textures go (SWI-RUN-5).
            DropOtherSteps(step);
        }
        Sets[step] = set;
        return set;
    }

    private static void DropOtherSteps(SteamwovenStep step)
    {
        var keep = System.IO.Path.DirectorySeparatorChar + SteamwovenKitSet.FolderName(step) + System.IO.Path.DirectorySeparatorChar;
        foreach (var key in Textures.Keys.Where(key => !key.Contains(keep, StringComparison.Ordinal)).ToList())
            Drop(key);
    }

    private static string Key(SteamwovenKitSet set, string id) => set.PathOf(id);

    private static void Drop(string key)
    {
        if (Textures.Remove(key, out var texture)) texture?.Dispose();
    }

    /// <summary>The texture of a part of the set, loaded on first use and checked against the manifest's size; null (reported once) when the file does not read or is another size.</summary>
    public static Texture2D? Texture(SteamwovenKitSet set, string id)
    {
        if (!set.TryGet(id, out var part)) return null;
        var key = Key(set, id);
        if (Textures.TryGetValue(key, out var cached)) return cached;
        Texture2D? texture = null;
        try
        {
            var image = Image.LoadFromFile(set.PathOf(id));
            if (image is null || image.GetWidth() != part.Width || image.GetHeight() != part.Height)
            {
                if (Reported.Add(key)) GD.Print($"ARC-UI-2461 kit: {id} is not {part.Width} x {part.Height}; the generated chrome is drawn for it");
            }
            else
            {
                if (image.GetFormat() != Image.Format.Rgba8) image.Convert(Image.Format.Rgba8);
                texture = ImageTexture.CreateFromImage(image);
            }
        }
        catch (Exception exception)
        {
            if (Reported.Add(key)) GD.Print($"ARC-UI-2461 kit: {id} did not load ({exception.Message})");
        }
        Textures[key] = texture;
        return texture;
    }

    /// <summary>SWI-RUN-5: keeps the resident backgrounds and plates of <paramref name="set"/> to <paramref name="ids"/>; every other <c>backdrop.</c> texture is released.</summary>
    public static void Retain(SteamwovenKitSet set, IReadOnlyCollection<string> ids)
    {
        foreach (var id in set.Ids)
        {
            if (!id.StartsWith("backdrop.", StringComparison.Ordinal) || ids.Contains(id)) continue;
            Drop(Key(set, id));
        }
    }

    /// <summary>How many textures are resident now, for the self-test (SWI-RUN-5).</summary>
    public static IReadOnlyList<string> ResidentForSelfTest => [.. Textures.Where(pair => pair.Value is not null).Select(pair => pair.Key)];
}

/// <summary>
/// ARC-UI-2461: draws kit parts into their device rectangles: the strip art, the end caps, the hardware layers. The parts are exact when the baked pixels are the device pixels (sampled nearest, SWI-LAY-5) and scaled otherwise (linear);
/// the whole view takes one sampling, nearest only while every part in it is exact. A part's clipped rectangle is the window's part of it: a cap beyond the window's edge is cut, not squeezed.
/// </summary>
internal sealed partial class SteamwovenKitView : Control
{
    private readonly List<(Texture2D Texture, SteamwovenKitDraw Draw)> _items = [];

    public SteamwovenKitView()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsPreset(LayoutPreset.TopLeft);
    }

    /// <summary>The parts drawn now, for the self-test.</summary>
    internal IReadOnlyList<SteamwovenKitDraw> DrawsForSelfTest => [.. _items.Select(item => item.Draw)];

    /// <summary>True when every part drawn is exact (the view is sampled nearest).</summary>
    internal bool AllExact => _items.All(item => item.Draw.Exact);

    public void Set(IReadOnlyList<(Texture2D Texture, SteamwovenKitDraw Draw)> items, Vector2 windowSize)
    {
        Position = Vector2.Zero; Size = windowSize;
        _items.Clear(); _items.AddRange(items);
        TextureFilter = _items.Count == 0 || AllExact ? TextureFilterEnum.Nearest : TextureFilterEnum.Linear;
        QueueRedraw();
    }

    public override void _Draw()
    {
        foreach (var (texture, draw) in _items)
        {
            var (rect, clip) = (draw.Rect, draw.Clip);
            if (clip.Width <= 0 || clip.Height <= 0 || rect.Width <= 0 || rect.Height <= 0) continue;
            var (sx, sy) = ((float)texture.GetWidth() / rect.Width, (float)texture.GetHeight() / rect.Height);
            DrawTextureRectRegion(texture, new Rect2(clip.X, clip.Y, clip.Width, clip.Height), new Rect2((clip.X - rect.X) * sx, (clip.Y - rect.Y) * sy, clip.Width * sx, clip.Height * sy));
        }
    }
}
