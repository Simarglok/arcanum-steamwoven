using System.Globalization;
using System.Security.Cryptography;
using Arcanum.Data;
using Arcanum.Data.Area;
using Arcanum.Data.Content;
using Arcanum.Data.Messages;
using Arcanum.Data.Terrain;
using Arcanum.Simulation.Reaction;
using Arcanum.World;

namespace Arcanum.TerrainViewer.Core;

/// <summary>One frame of a hover ring art: the visual record and frame key it is drawn under (a visual of its own, so the canvas
/// uploads and draws it like any frame), its pixels, and the frame header's delta (<c>deltaX</c>, <c>deltaY</c>), which the ring's
/// rectangle adds to the object's anchor beside the frame's centre (0x443880).</summary>
public sealed record AreaHoverRingFrame(AreaVisualRecord Visual, AreaFrameKey Key, DecodedAreaFrame Pixels, int DeltaX, int DeltaY)
{
    public int Width => Visual.Width;
    public int Height => Visual.Height;
}

/// <summary>One hover ring art (interface art 467, 468 or 555): its frames of group 0 and the header's frame rate.</summary>
public sealed record AreaHoverRingArt(int Number, IReadOnlyList<AreaHoverRingFrame> Frames, int FramesPerSecond);

/// <summary>The rings the canvas draws for the hovered object <paramref name="Target"/> this frame: the two frames (null for a ring whose art did
/// not read) and the colour that tints them.</summary>
public sealed record AreaHoverRingDraw(AreaObjectKey Target, AreaHoverRingFrame? Under, AreaHoverRingFrame? Over, (byte R, byte G, byte B) Colour);

public enum AreaHoverRingArtStatus { Ready, Unnamed, MissingFile, Unreadable, LimitExceeded }

public sealed record AreaHoverRingArtLoad(int Number, AreaHoverRingArtStatus Status, AreaHoverRingArt? Art, string Detail);

/// <summary>
/// What the rings under and over the hovered critter are drawn with: the seven reaction colours of <c>rules\reaction_colors.mes</c>
/// (0x444150, read at game start) and the three ring arts by their number. Failures are kept and said once, never thrown: a ring art that
/// does not read draws no ring, and a colour table that does not read leaves every ring white (the colour the original starts every hover
/// with, 0x43C270). Godot-free.
/// </summary>
public sealed record AreaHoverRingContent(IReadOnlyList<(byte R, byte G, byte B)>? Colours, IReadOnlyDictionary<int, AreaHoverRingArtLoad> Arts, string Detail)
{
    /// <summary>The art loaded for <paramref name="number"/>, or null when it did not read.</summary>
    public AreaHoverRingArt? Art(int number) => Arts.TryGetValue(number, out var load) ? load.Art : null;

    public static AreaHoverRingContent Empty(string reason) => new(null, new Dictionary<int, AreaHoverRingArtLoad>(), reason ?? throw new ArgumentNullException(nameof(reason)));

    /// <summary>Reads the colour table and the three ring arts from <paramref name="stack"/> (the game's content stack).</summary>
    public static AreaHoverRingContent Load(ContentStack stack)
    {
        ArgumentNullException.ThrowIfNull(stack);
        var colours = AreaHoverRingRules.ReadColours(stack, out var colourDetail);
        var arts = AreaHoverRingRules.ArtNumbers.ToDictionary(number => number, number => AreaHoverRingArts.Load(stack, number));
        var failed = arts.Values.Count(load => load.Status != AreaHoverRingArtStatus.Ready);
        return new(colours, arts, $"colours {(colours is null ? colourDetail : "read")}; arts {arts.Count - failed}/{arts.Count} read"
            + string.Concat(arts.Values.Where(load => load.Status != AreaHoverRingArtStatus.Ready).Select(load => $" [{load.Number}: {load.Status} {load.Detail}]")));
    }
}

/// <summary>
/// ARC-UI-1117 (ledger OH2, OH4): the coloured rings the original draws under and over a hovered critter or trap. The setter 0x43C270 gives a
/// hovered object of type 15 to 17 (a PC, an NPC, a trap) two rings while a local PC exists: art 467 <c>hoverunder</c> under every object
/// (blit order 99,999,999, after the terrain and before the flat and upright bodies, SO1) and art 468 <c>hoverover</c> over them (0x7FFFFFFF),
/// both additive (blit 0x2010) with a constant colour; the colour is white, and for an NPC the colour of its reaction category (0x4C0FC0 of
/// the standard effective reaction 0x4C0CC0, the table of <c>rules\reaction_colors.mes</c>, 0x444150). While the local PC is in combat mode
/// both arts are 555 <c>hoverattack</c>. The ring's rectangle is the art's frame placed like an object's own frame (0x443880): the object's
/// anchor plus the frame's delta less its centre, 15 pixels lower for a wading object; each ring steps to the art's next frame every
/// 1000 / frames-per-second milliseconds (0x43A6D0). Godot-free.
/// </summary>
public static class AreaHoverRingRules
{
    public const int UnderArt = 467, OverArt = 468, AttackArt = 555;
    /// <summary>Object types the setter gives rings: a PC (15), an NPC (16) and a trap (17).</summary>
    public const int FirstRingType = 15, LastRingType = 17, NpcType = 16;
    /// <summary>The table's rows: one colour per reaction category (love 0 .. hatred 6).</summary>
    public const int ColourRows = 7;
    public const string ColoursPath = "rules/reaction_colors.mes";
    /// <summary>The rows a wading critter's ring is moved down (0x443880: object flag 0x20000 without 0x40000).</summary>
    public const int WadingShift = 15;
    /// <summary>The additive blend's art class for the ring commands: interface art, which HD pack binding leaves to Legacy.</summary>
    public const int InterfaceArtClass = 5;

    public static IReadOnlyList<int> ArtNumbers { get; } = [UnderArt, OverArt, AttackArt];

    public static readonly (byte R, byte G, byte B) White = (255, 255, 255);

    /// <summary>Whether a hovered object of this type gets rings (OH2): a critter or a trap.</summary>
    public static bool Applies(int type) => type is >= FirstRingType and <= LastRingType;

    /// <summary>The two ring arts (under, over): 467 and 468, both 555 while the local PC is in combat mode.</summary>
    public static (int Under, int Over) Arts(bool combatMode) => combatMode ? (AttackArt, AttackArt) : (UnderArt, OverArt);

    /// <summary>The ring colour: white; for an NPC with a reaction toward the PC, its reaction category's row of the table (0x43C270).</summary>
    public static (byte R, byte G, byte B) Colour(int type, int? reaction, IReadOnlyList<(byte R, byte G, byte B)>? table)
    {
        if (type != NpcType || reaction is not { } value || table is null) return White;
        var category = (int)ReactionRules.Category(value);
        return category < table.Count ? table[category] : White;
    }

    /// <summary>
    /// The table's rows from <c>reaction_colors.mes</c>: message 0 to 6, each the text <c>red, green, blue</c> (0x444150 reads three integers
    /// separated by commas). A value above 255 is held at 255 and a negative one at 0 (the original indexes a 256-entry channel table with it
    /// and would read beyond it, which no installed row does). A missing or malformed row refuses the whole table, as the original refuses to
    /// start the module.
    /// </summary>
    public static IReadOnlyList<(byte R, byte G, byte B)>? ParseColours(MesFile table, out string? detail)
    {
        ArgumentNullException.ThrowIfNull(table);
        var rows = new List<(byte, byte, byte)>(ColourRows);
        for (var row = 0; row < ColourRows; row++)
        {
            if (table.Find(row) is not { } message || MesText.Decode(message, AreaHoverIndex.TextProfile) is not { Status: MesDecodeStatus.Decoded, Text: { } text })
            { detail = $"no row {row}"; return null; }
            var parts = text.Split(',', StringSplitOptions.TrimEntries);
            if (parts.Length != 3 || parts.Any(part => !int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)))
            { detail = $"row {row} is not three numbers"; return null; }
            byte Channel(string part) => (byte)Math.Clamp(int.Parse(part, NumberStyles.Integer, CultureInfo.InvariantCulture), 0, 255);
            rows.Add((Channel(parts[0]), Channel(parts[1]), Channel(parts[2])));
        }
        detail = null;
        return rows;
    }

    /// <summary>Reads <see cref="ColoursPath"/> from the content stack (game start phase); null with the reason when it does not read.</summary>
    public static IReadOnlyList<(byte R, byte G, byte B)>? ReadColours(ContentStack stack, out string? detail)
    {
        ArgumentNullException.ThrowIfNull(stack);
        if (ContentPhase.Candidate(stack.Resolve(ColoursPath), ContentLoadPhase.GameStart) is not { } winner) { detail = $"{ColoursPath} not found"; return null; }
        var read = stack.Read(winner);
        if (read.Status != ContentReadStatus.Read || read.Bytes is null) { detail = $"{ColoursPath} {read.Status}"; return null; }
        var parsed = MesFile.Parse(read.Bytes);
        if (parsed.Status != MesParseStatus.Parsed || parsed.File is null) { detail = $"{ColoursPath} {parsed.Status}"; return null; }
        return ParseColours(parsed.File, out detail);
    }

    /// <summary>
    /// The command that draws <paramref name="frame"/> as a ring of the object whose body is <paramref name="body"/> (as the canvas holds it
    /// this submission): additive, unlit (the colour is the draw's tint), placed at the body's anchor (its destination plus its hotspot, the
    /// tile's centre and the object's offsets) plus the frame's delta less its centre; a wading body's ring is 15 pixels lower. Null for a
    /// body without a hotspot (one a seam moved without placing it from its frame).
    /// </summary>
    public static AreaDrawCommand? Command(AreaDrawCommand body, AreaHoverRingFrame frame)
    {
        ArgumentNullException.ThrowIfNull(body); ArgumentNullException.ThrowIfNull(frame);
        if (body.Hot is not { } hot) return null;
        var x = checked(body.Destination.X + hot.X + frame.DeltaX - frame.Visual.HotX);
        var y = checked(body.Destination.Y + hot.Y + frame.DeltaY - frame.Visual.HotY + (body.WadeRows > 0 ? WadingShift : 0));
        return new AreaDrawCommand(frame.Visual.Key, frame.Key, null, new TerrainRect(0, 0, frame.Width, frame.Height),
            new TerrainRect(x, y, frame.Width, frame.Height), false, false, body.Phase, body.MapX, body.MapY, 0, 0, 255, true)
        { ArtClass = InterfaceArtClass, Unlit = true };
    }
}

/// <summary>The frame stepping of the two rings (0x43A6D0): each ring has its own stamp and steps to the art's next frame, wrapping, when
/// 1000 / fps milliseconds (integer division) have passed since the stamp, then restamps. A hover starts both at frame 0 (0x43C270 loads
/// the arts afresh; the stamps themselves are not reset there, so the first step's phase is the original's undetermined remainder, which
/// Steamwoven starts at the hover). Pure, with the clock passed in.</summary>
public sealed class AreaHoverRingClock
{
    private long _stampUnder, _stampOver;
    private int _under, _over;

    public int UnderFrame => _under;
    public int OverFrame => _over;

    public void Start(long milliseconds) { _stampUnder = _stampOver = milliseconds; _under = _over = 0; }

    /// <summary>Steps each ring that is due. A rate of 0 or less never steps (the original would divide by zero; no installed ring has one).</summary>
    public (int Under, int Over) Step(long milliseconds, int underFramesPerSecond, int underFrames, int overFramesPerSecond, int overFrames)
    {
        (_under, _stampUnder) = Next(_under, _stampUnder, milliseconds, underFramesPerSecond, underFrames);
        (_over, _stampOver) = Next(_over, _stampOver, milliseconds, overFramesPerSecond, overFrames);
        return (_under, _over);
    }

    private static (int Frame, long Stamp) Next(int frame, long stamp, long now, int framesPerSecond, int frames)
    {
        if (frames <= 0) return (0, stamp);
        frame = Math.Min(frame, frames - 1);
        if (framesPerSecond <= 0 || now - stamp < 1000 / framesPerSecond) return (frame, stamp);
        return ((frame + 1) % frames, now);
    }
}

/// <summary>Reads one hover ring art as frames the canvas can draw. Godot-free.</summary>
public static class AreaHoverRingArts
{
    public const string Policy = "steamwoven-hover-ring-v1";
    private const int MaxFrames = 32;
    private const int MaxFileBytes = 1 << 20;

    /// <summary>The visual key of frame <paramref name="frame"/> of ring art <paramref name="number"/> (a key of its own: no map's sector).</summary>
    public static AreaVisualKey VisualKey(int number, int frame) => new(AreaSourceKind.Static, new("hover-ring", 0, 0), number * 64 + frame, Policy);

    public static AreaHoverRingArtLoad Load(ContentStack stack, int number)
    {
        ArgumentNullException.ThrowIfNull(stack);
        var index = AreaStartingHero.Table(stack, AreaInterfaceArt.IndexPath, ContentLoadPhase.GameStart, required: true, out var indexDetail);
        if (index is null) return new(number, AreaHoverRingArtStatus.Unnamed, null, indexDetail ?? AreaInterfaceArt.IndexPath);
        if (AreaInterfaceArt.Name(index, number) is not { } name) return new(number, AreaHoverRingArtStatus.Unnamed, null, $"{AreaInterfaceArt.IndexPath} has no message {number}");
        var path = AreaInterfaceArt.Folder + name;
        if (stack.Resolve(path).Winner is not { } winner) return new(number, AreaHoverRingArtStatus.MissingFile, null, path);
        if (winner.Length > MaxFileBytes) return new(number, AreaHoverRingArtStatus.LimitExceeded, null, $"{path} {winner.Length} bytes");
        var read = stack.Read(winner);
        if (read.Status != ContentReadStatus.Read || read.Bytes is not { } bytes) return new(number, AreaHoverRingArtStatus.Unreadable, null, $"{path} {read.Status}");
        try
        {
            var options = new ArtDecoderOptions { AllowTrailingBytes = true, MaxInputBytes = MaxFileBytes, MaxEncodedFrameBytes = MaxFileBytes };
            var metadata = ArtDecoder.Inspect(bytes, options);
            var count = metadata.Header.FramesPerGroup;
            if (count <= 0 || count > MaxFrames) return new(number, AreaHoverRingArtStatus.LimitExceeded, null, $"{path} {count} frames");
            var view = bytes.Length - metadata.TrailingBytes;
            var payload = Digest(bytes.AsSpan(0, view));
            var entry = new AreaEntryIdentity(new(AreaSourceRole.Loose, -1), 0, 0, 0, 0, 0, Digest(System.Text.Encoding.ASCII.GetBytes(path)));
            var frames = new List<AreaHoverRingFrame>(count);
            for (var i = 0; i < count; i++)
            {
                var decoded = ArtDecoder.Decode(bytes.AsSpan(0, view), new ArtFrameSelection(0, i, 0), options);
                var rgba = decoded.Rgba.ToArray();
                var raw = unchecked((uint)(AreaHoverRingRules.InterfaceArtClass << 28 | number));
                var visual = new AreaVisualRecord(VisualKey(number, i), raw, raw, entry, payload, view, 0, 0, 0, i, 0, decoded.EffectivePaletteSlot, false,
                    decoded.Width, decoded.Height, decoded.Frame.HotX, decoded.Frame.HotY, decoded.Frame.OffsetX, decoded.Frame.OffsetY, 0, 0, 100,
                    decoded.Frame.EncodedPayloadLength, rgba.Length, Digest(rgba), Policy);
                frames.Add(new(visual, AreaFrameKey.From(visual), new(decoded.Width, decoded.Height, rgba), decoded.Frame.OffsetX, decoded.Frame.OffsetY));
            }
            return new(number, AreaHoverRingArtStatus.Ready, new(number, frames, metadata.Header.FramesPerSecond), $"{path} {count} frames");
        }
        catch (Exception error) when (error is ArtException or ArgumentException)
        {
            return new(number, AreaHoverRingArtStatus.Unreadable, null, $"{path} {error.GetType().Name}");
        }
    }

    private static string Digest(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
