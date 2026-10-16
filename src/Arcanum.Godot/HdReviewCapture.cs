using System.Globalization;
using Arcanum.Data.Hd;
using Godot;

namespace Arcanum.Godot;

/// <summary>ARC-RENDER-015 textures and local captures. Captures are original-derived
/// pixels: they are written only to the local capture directory (by default
/// <c>local/hd-review</c> of the checkout, ignored by git, or <c>user://hd-review</c> in an
/// exported viewer) and never belong in git, PRs or attachments.</summary>
internal static class HdReviewCapture
{
    /// <summary>An HD frame as a texture whose size override is the Legacy frame, so it
    /// takes the Legacy frame's place on the canvas and shows its detail from zoom N up.</summary>
    internal static ImageTexture CreateHdTexture(HdFrameResult hd, int legacyWidth, int legacyHeight)
    {
        ArgumentNullException.ThrowIfNull(hd);
        if (hd.Status != HdFrameStatus.Hd || hd.Rgba is null || hd.Rgba.Length != checked(hd.Width * hd.Height * 4)
            || hd.Width != legacyWidth * hd.Scale || hd.Height != legacyHeight * hd.Scale)
            throw new ArgumentException("HD frame is not exactly Scale × the Legacy frame.", nameof(hd));
        using var image = Image.CreateFromData(hd.Width, hd.Height, false, Image.Format.Rgba8, hd.Rgba);
        var texture = ImageTexture.CreateFromImage(image);
        texture.SetSizeOverride(new Vector2I(legacyWidth, legacyHeight));
        return texture;
    }

    /// <summary>Before and after on one transparent image at HD resolution: the Legacy
    /// frame replicated N× nearest on the left, a gap of N·4 pixels, the HD frame on the right.</summary>
    internal static Image ComposePair(Image legacy, Image hd, int scale)
    {
        ArgumentNullException.ThrowIfNull(legacy);
        ArgumentNullException.ThrowIfNull(hd);
        if (scale < 1 || hd.GetWidth() != legacy.GetWidth() * scale || hd.GetHeight() != legacy.GetHeight() * scale)
            throw new ArgumentException("HD image is not exactly scale × the Legacy image.");
        using var before = (Image)legacy.Duplicate();
        if (before.GetFormat() != Image.Format.Rgba8) before.Convert(Image.Format.Rgba8);
        before.Resize(hd.GetWidth(), hd.GetHeight(), Image.Interpolation.Nearest);
        using var after = (Image)hd.Duplicate();
        if (after.GetFormat() != Image.Format.Rgba8) after.Convert(Image.Format.Rgba8);
        var gap = 4 * scale;
        var pair = Image.CreateEmpty(hd.GetWidth() * 2 + gap, hd.GetHeight(), false, Image.Format.Rgba8);
        pair.BlitRect(before, new Rect2I(Vector2I.Zero, before.GetSize()), Vector2I.Zero);
        pair.BlitRect(after, new Rect2I(Vector2I.Zero, after.GetSize()), new Vector2I(hd.GetWidth() + gap, 0));
        return pair;
    }

    /// <summary>The capture directory: the given one, otherwise <c>local/hd-review</c> of the
    /// checkout when run from source, otherwise <c>user://hd-review</c>.</summary>
    internal static string DefaultDirectory()
    {
        if (!OS.HasFeature("template"))
        {
            var project = ProjectSettings.GlobalizePath("res://");
            var checkout = Path.GetFullPath(Path.Combine(project, "..", ".."));
            if (File.Exists(Path.Combine(checkout, "Arcanum.Steamwoven.sln"))) return Path.Combine(checkout, "local", "hd-review");
        }
        return ProjectSettings.GlobalizePath("user://hd-review");
    }

    /// <summary>A file name that names no local path: pack id, entry and UTC time.</summary>
    internal static string FileName(string subject, string kind, DateTime utc) =>
        $"{subject}-{utc.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture)}-{kind}.png";

    internal static string Subject(HdPackEntry? entry, HdPack? pack, int? ordinal, int group, int frame, int palette) =>
        entry is not null && pack is not null
            ? $"{pack.Id}-e{entry.Index.ToString("D6", CultureInfo.InvariantCulture)}"
            : string.Create(CultureInfo.InvariantCulture, $"art{ordinal ?? -1}-g{group}f{frame}p{palette}");

    /// <summary>Saves a PNG into the directory (created when missing); returns the file name.</summary>
    internal static string Save(Image image, string directory, string fileName)
    {
        ArgumentNullException.ThrowIfNull(image);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, fileName);
        var error = image.SavePng(path);
        if (error != Error.Ok) throw new IOException($"PNG capture failed ({error}).");
        return fileName;
    }
}
