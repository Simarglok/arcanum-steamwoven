using System.Buffers.Binary;
using Arcanum.Data.Characters;
using Arcanum.Data.Content;
using Arcanum.Simulation;

namespace Arcanum.World;

/// <summary>
/// ARC-UI-3501: what the start-up picture step picked (docs/research/startup-loading-picture.md). <see cref="Path"/> and
/// <see cref="Screen"/> (800 × 600 × 4 bytes, opaque RGBA, top row first) are null when nothing is drawn. <see cref="NextCounter"/> is the
/// value the <c>splash</c> line takes, null when it stays as it is (there is no picture at all).
/// </summary>
public sealed record AreaStartupPictureChoice(string? Path, byte[]? Screen, int? NextCounter);

/// <summary>
/// ARC-UI-3501 (docs/research/startup-loading-picture.md SL1-SL6): the "Loading Arcanum..." picture the original draws after the two logo
/// movies, while the game loads, and before movie 8. The pictures are the bitmaps of <c>art\splash\</c> (their text is part of the
/// picture); the one shown rotates from start to start by the saved <c>splash</c> number, it is not random.
/// </summary>
public static class AreaStartupPicture
{
    public const string Folder = "art/splash/";
    /// <summary>The configuration line that holds where the rotation stands (default 0).</summary>
    public const string CounterName = "splash";
    /// <summary>How many files in turn are tried when one cannot be drawn (not the number of pictures).</summary>
    public const int Attempts = 3;
    public const int ScreenWidth = 800, ScreenHeight = 600;
    /// <summary>The window the picture is drawn into: the 800 × 400 play view at (0, 41), between the two interface strips.</summary>
    public const int ViewTop = 41, ViewWidth = 800, ViewHeight = 400;
    /// <summary>A bitmap of the view's size in 24 bits is under 1 MiB; a larger file cannot be drawn into the view.</summary>
    public const int MaxFileBytes = 2 * 1024 * 1024;

    /// <summary>The pictures the original lists at this point of the start-up (before the module is mounted): the <c>.bmp</c> files directly
    /// under <c>art\splash\</c>, each name once, in the order of their names without letter case.</summary>
    public static IReadOnlyList<string> Paths(ContentStack stack)
    {
        ArgumentNullException.ThrowIfNull(stack);
        return [.. stack.Keys(Folder).Where(key => key.EndsWith(".bmp", StringComparison.Ordinal) && key.IndexOf('/', Folder.Length) < 0
            && ContentPhase.Candidate(stack, key, ContentLoadPhase.GameStart) is not null)];
    }

    /// <summary>A picture's bytes as the original reads them at this point; null when no source holds it, it is too large or unreadable.</summary>
    public static byte[]? Read(ContentStack stack, string path)
    {
        ArgumentNullException.ThrowIfNull(stack);
        ArgumentNullException.ThrowIfNull(path);
        if (ContentPhase.Candidate(stack, path, ContentLoadPhase.GameStart) is not { } found || found.Length > MaxFileBytes) return null;
        var read = stack.Read(found);
        return read.Status == ContentReadStatus.Read ? read.Bytes : null;
    }

    /// <summary>The saved line read as the original reads an integer: leading spaces, a sign and digits; anything else ends the number, and a
    /// missing or empty line is 0. The result wraps at 32 bits and is used unsigned.</summary>
    public static uint Counter(string? value)
    {
        if (value is null) return 0;
        var at = 0;
        while (at < value.Length && value[at] is ' ' or (>= '\t' and <= '\r')) at++;
        var negative = at < value.Length && value[at] == '-';
        if (at < value.Length && value[at] is '-' or '+') at++;
        uint total = 0;
        for (; at < value.Length && char.IsAsciiDigit(value[at]); at++) total = unchecked(total * 10 + (uint)(value[at] - '0'));
        return negative ? unchecked(0u - total) : total;
    }

    /// <summary>Picks and composes the picture of this start: the file at <c>(counter + i) mod count</c>, the next one when it cannot be drawn,
    /// three tries at most. The next saved number is the index of the last file tried plus one, not wrapped.</summary>
    [OriginalFunction(0x404830, OriginalFunctionScope.Partial)]
    public static AreaStartupPictureChoice Choose(IReadOnlyList<string> paths, uint counter, Func<string, byte[]?> read)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(read);
        if (paths.Count == 0) return new(null, null, null);
        var index = 0;
        for (var attempt = 0; attempt < Attempts; attempt++)
        {
            index = (int)(unchecked(counter + (uint)attempt) % (uint)paths.Count);
            if (read(paths[index]) is { } bytes && Compose(bytes) is { } screen) return new(paths[index], screen, index + 1);
        }
        return new(null, null, index + 1);
    }

    /// <summary>The original's screen with <paramref name="file"/> on it: black, and the bitmap at the left edge of the view, centred in its
    /// height when the file is stored bottom-up (the usual way) and at its top when stored top-down. Null when the original cannot draw the
    /// file: not an uncompressed 8- or 24-bit bitmap, or larger than the view.</summary>
    public static byte[]? Compose(ReadOnlySpan<byte> file)
    {
        if (file.Length < 54 || BinaryPrimitives.ReadUInt16LittleEndian(file[28..]) is not (8 or 24)) return null;
        var topDown = BinaryPrimitives.ReadInt32LittleEndian(file[22..]) < 0;
        if (PortraitBitmap.Decode(file) is not { } picture || picture.Width > ViewWidth || picture.Height > ViewHeight) return null;
        var screen = new byte[ScreenWidth * ScreenHeight * 4];
        for (var alpha = 3; alpha < screen.Length; alpha += 4) screen[alpha] = byte.MaxValue;
        var top = ViewTop + (topDown ? 0 : (ViewHeight - picture.Height) / 2);
        for (var row = 0; row < picture.Height; row++)
            picture.Rgba.AsSpan(row * picture.Width * 4, picture.Width * 4).CopyTo(screen.AsSpan((top + row) * ScreenWidth * 4));
        return screen;
    }
}
