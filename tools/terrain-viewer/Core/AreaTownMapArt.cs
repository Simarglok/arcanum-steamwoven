using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using Arcanum.Data.Content;
using Arcanum.Data.Messages;
using Arcanum.Simulation.Travel;
using Arcanum.World;

namespace Arcanum.TerrainViewer.Core;

/// <summary>One line of <c>rules/townmap.mes</c> (WM66): the town map's number, its name (the text cut at <c>[w:</c> with the trailing blanks
/// removed; it names the folder and the tile files) and the whole flag (the map is shown whole from the start).</summary>
public sealed record AreaTownMapEntry(int Number, string Name, bool Whole);

/// <summary>
/// ARC-UI-084 (ledger WM65–WM94): the list of town maps, <c>rules/townmap.mes</c>. The executable keeps 8-byte records {number, whole flag} and
/// cuts the text at <c>[w:</c> or <c>[W:</c> (WM66); 0 is <c>&lt;none&gt;</c> and never a town map. Godot-free.
/// </summary>
public sealed class AreaTownMapTable
{
    public const string Path = "rules/townmap.mes";
    public const int MaxNameLength = 80;
    private readonly Dictionary<int, AreaTownMapEntry> _entries;

    private AreaTownMapTable(Dictionary<int, AreaTownMapEntry> entries) => _entries = entries;

    public int Count => _entries.Count;

    public AreaTownMapEntry? Find(int number) => _entries.TryGetValue(number, out var entry) ? entry : null;

    /// <summary>One entry from its message text: cut at <c>[w:</c> (either case), the whole flag when the first non-blank after it is <c>1</c> (WM66);
    /// null for number 0, an empty or too long name, or one that could leave its folder (a path separator, <c>..</c>, a control character).</summary>
    public static AreaTownMapEntry? ParseEntry(int number, string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (number <= 0) return null;
        var cut = text.IndexOf("[w:", StringComparison.OrdinalIgnoreCase);
        var name = (cut < 0 ? text : text[..cut]).TrimEnd();
        var whole = false;
        if (cut >= 0)
        {
            var rest = text[(cut + 3)..].TrimStart();
            whole = rest.Length > 0 && rest[0] == '1';
        }
        if (name.Length == 0 || name.Length > MaxNameLength || name.Contains("..", StringComparison.Ordinal)
            || name.Any(c => c is '/' or '\\' or ':' || char.IsControl(c))) return null;
        return new(number, name, whole);
    }

    public static AreaTownMapTable Parse(ReadOnlySpan<byte> payload)
    {
        var entries = new Dictionary<int, AreaTownMapEntry>();
        var parsed = MesFile.Parse(payload);
        if (parsed.Status != MesParseStatus.Parsed) return new(entries);
        foreach (var message in parsed.File!.Messages)
            if (ParseEntry(message.Number, Encoding.Latin1.GetString(message.Bytes.Span)) is { } entry) entries[entry.Number] = entry;
        return new(entries);
    }
}

/// <summary>
/// The 48-byte record of <c>townmap/&lt;name&gt;/&lt;name&gt;.tmi</c> (WM69): the first int must be 2, then version, unused, the map number, the full
/// picture's width and height in isometric screen pixels, the tile columns and rows, unused, the origin location (64 bit, x low and y high) and the
/// scale (a float; 0.25 in every shipped file).
/// </summary>
public sealed record AreaTownMapInfo(int Number, int Width, int Height, int Columns, int Rows, long OriginX, long OriginY, double Scale)
{
    public const int RecordSize = 48, Version = 2;
    public const int MaxPixels = 1 << 17, MaxTiles = 1 << 16;

    /// <summary>Reads the record; null for a short file, a version other than 2 or sizes outside the limits (the original refuses the first two).</summary>
    public static AreaTownMapInfo? Parse(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < RecordSize || BinaryPrimitives.ReadInt32LittleEndian(bytes) != Version) return null;
        var number = BinaryPrimitives.ReadInt32LittleEndian(bytes[8..]);
        var width = BinaryPrimitives.ReadInt32LittleEndian(bytes[12..]);
        var height = BinaryPrimitives.ReadInt32LittleEndian(bytes[16..]);
        var columns = BinaryPrimitives.ReadInt32LittleEndian(bytes[20..]);
        var rows = BinaryPrimitives.ReadInt32LittleEndian(bytes[24..]);
        var origin = BinaryPrimitives.ReadInt64LittleEndian(bytes[32..]);
        var scale = BinaryPrimitives.ReadSingleLittleEndian(bytes[40..]);
        if (width is < 1 or > MaxPixels || height is < 1 or > MaxPixels || columns is < 1 or > 1024 || rows is < 1 or > 1024 || (long)columns * rows > MaxTiles
            || !(scale > 0 && scale <= 4)) return null;
        return new(number, width, height, columns, rows, (int)(origin & 0xFFFFFFFF), (int)(origin >> 32), scale);
    }
}

/// <summary>A town map ready to show: its table entry, its info and the tile size measured from tile 000000 (WM72).</summary>
public sealed record AreaTownMapPlan(AreaTownMapEntry Entry, AreaTownMapInfo Info, int TileWidth, int TileHeight)
{
    public int PictureWidth => TileWidth * Info.Columns;
    public int PictureHeight => TileHeight * Info.Rows;
    public int TileCount => Info.Columns * Info.Rows;
}

/// <summary>How a tile of a town map is drawn (WM77): plainly, as a fade into black beside revealed tiles, or black.</summary>
public enum AreaTownMapTileState { Plain, Faded, Black }

/// <summary>
/// ARC-UI-084: the geometry of the town-map view (ledger WM70–WM77, WM84); Godot-free. The view is the same rectangle as the other kinds
/// (<see cref="AreaWorldMapArtLayout.View"/>); tile (c, r) lies at (c × tile width − offset x, r × tile height − offset y) of it.
/// </summary>
public static class AreaTownMapLayout
{
    /// <summary>WM70: a location's pixel in the town's picture. Both the origin and the location go to isometric screen coordinates
    /// (x = (y − x − 1) × 40, y = (x + y) × 20); the offset from the origin lands from the picture's middle; outside the full picture the pixel stays
    /// (0, 0) (the party of a member far from the town is drawn at the picture's corner, WM81); inside, it is truncated after the scale.</summary>
    public static (int X, int Y) ToPixel(AreaTownMapInfo info, long locationX, long locationY)
    {
        ArgumentNullException.ThrowIfNull(info);
        var dx = ((locationY - locationX - 1) - (info.OriginY - info.OriginX - 1)) * 40;
        var dy = ((locationX + locationY) - (info.OriginX + info.OriginY)) * 20;
        var fullX = info.Width / 2 + dx;
        var fullY = info.Height / 2 + dy;
        if (fullX < 0 || fullY < 0 || fullX >= info.Width || fullY >= info.Height) return (0, 0);
        return ((int)(fullX * info.Scale), (int)(fullY * info.Scale));
    }

    /// <summary>The largest scroll offset: the picture's size minus the view, 0 for a picture smaller than the view (WM72).</summary>
    public static (int X, int Y) MaxOffset(AreaTownMapPlan plan) => AreaWorldMapArtLayout.MaxOffset(plan.PictureWidth, plan.PictureHeight);

    /// <summary>The tiles a view at <paramref name="offset"/> touches (WM77): columns from offset / tile width to that plus view width / tile width plus two,
    /// rows plus three, the ends exclusive and capped at the grid.</summary>
    public static (int FirstColumn, int EndColumn, int FirstRow, int EndRow) VisibleTiles(AreaTownMapPlan plan, (int X, int Y) offset)
    {
        var view = AreaWorldMapArtLayout.View;
        var first = Math.Clamp(offset.X / plan.TileWidth, 0, plan.Info.Columns);
        var firstRow = Math.Clamp(offset.Y / plan.TileHeight, 0, plan.Info.Rows);
        return (first, Math.Min(plan.Info.Columns, offset.X / plan.TileWidth + view.Width / plan.TileWidth + 2), firstRow,
            Math.Min(plan.Info.Rows, offset.Y / plan.TileHeight + view.Height / plan.TileHeight + 3));
    }

    /// <summary>The neighbour table of the executable (0x4BEAF0): the corner bits (8 top left, 4 top right, 2 bottom right, 1 bottom left) that a revealed
    /// neighbour in direction (dx, dy) lights, row by row from the north-west.</summary>
    private static readonly byte[] CornerBits = [8, 0xC, 4, 9, 0, 6, 1, 3, 2];

    /// <summary>A tile's state (WM77): in a whole map every tile is plain; else a revealed one is plain, an unrevealed one with a revealed neighbour among
    /// its eight fades (<paramref name="corners"/>: the bits of the corners to show in white), any other is black.</summary>
    public static AreaTownMapTileState State(bool whole, int columns, int rows, int column, int row, Func<int, bool> revealed, out int corners)
    {
        ArgumentNullException.ThrowIfNull(revealed);
        corners = 0;
        if (whole || revealed(columns * row + column)) return AreaTownMapTileState.Plain;
        for (var dy = -1; dy <= 1; dy++)
            for (var dx = -1; dx <= 1; dx++)
            {
                var c = column + dx; var r = row + dy;
                if (c < 0 || c >= columns || r < 0 || r >= rows || (dx == 0 && dy == 0)) continue;
                if (revealed(columns * r + c)) corners |= CornerBits[(dy + 1) * 3 + dx + 1];
            }
        return corners == 0 ? AreaTownMapTileState.Black : AreaTownMapTileState.Faded;
    }

    /// <summary>The tile file of index <paramref name="index"/>: <c>townmap/&lt;name&gt;/&lt;name&gt;NNNNNN.bmp</c> (WM74), lower case for the content stack.</summary>
    public static string TilePath(string name, int index) =>
        $"townmap/{name.ToLowerInvariant()}/{name.ToLowerInvariant()}{index.ToString("D6", CultureInfo.InvariantCulture)}.bmp";

    public static string InfoPath(string name) => $"townmap/{name.ToLowerInvariant()}/{name.ToLowerInvariant()}.tmi";

    /// <summary>The picture pixel under a window point (WM84): the 1 : 1 formula of the other kinds at the town's offset, or null outside the view.</summary>
    public static (long X, long Y)? ToPicture(double windowX, double windowY, (int X, int Y) offset) => AreaWorldMapArtLayout.ToPicture(windowX, windowY, offset);

    /// <summary>Whether <paramref name="bytes"/> is a plain 8-bit uncompressed BMP, and its size (the tile format of the module).</summary>
    public static bool TryTileSize(ReadOnlySpan<byte> bytes, out int width, out int height)
    {
        width = height = 0;
        if (bytes.Length < 54 || bytes[0] != (byte)'B' || bytes[1] != (byte)'M') return false;
        var w = BinaryPrimitives.ReadInt32LittleEndian(bytes[18..]);
        var h = Math.Abs(BinaryPrimitives.ReadInt32LittleEndian(bytes[22..]));
        var planes = BinaryPrimitives.ReadInt16LittleEndian(bytes[26..]);
        var bits = BinaryPrimitives.ReadInt16LittleEndian(bytes[28..]);
        var compression = BinaryPrimitives.ReadInt32LittleEndian(bytes[30..]);
        if (w is < 1 or > 512 || h is < 1 or > 512 || planes != 1 || bits != 8 || compression != 0) return false;
        (width, height) = (w, h);
        return true;
    }
}

/// <summary>
/// ARC-UI-084: the PC's town map from the sectors a map loaded (WM65: the sector's town-map word; 0 = none); the same lookup that finds the area of
/// an object on the start map (<see cref="AreaStartMapAreas"/>). Godot-free.
/// </summary>
public static class AreaPcTownMap
{
    /// <summary>The town-map number of the sector holding <paramref name="x"/>, <paramref name="y"/> among <paramref name="shownSectors"/> (0 for a sector
    /// without its own file, or one the map did not load: the original would generate it, and a generated wilderness sector holds no town map).</summary>
    public static int At(IReadOnlyList<Arcanum.Data.Area.AreaSectorRecord> shownSectors, long x, long y)
    {
        ArgumentNullException.ThrowIfNull(shownSectors);
        var sector = WorldTravelGeometry.Sector(new(x, y));
        foreach (var record in shownSectors)
            if (record.Coordinate.X == sector.X && record.Coordinate.Y == sector.Y) return record.Present ? Math.Max(0, record.Townmap ?? 0) : 0;
        return 0;
    }
}

/// <summary>
/// ARC-UI-084: the town-map files of the game folder, kept open for the window (the pictures run to 19 600 tiles of about 3 KB, so tiles are read as the
/// view needs them, WM74): <c>rules/townmap.mes</c> read at once, a town's info and tile 000000 on <see cref="Prepare"/>. Failures are reported as
/// null, never thrown. Owns its content stack.
/// </summary>
public sealed class AreaTownMapSource : IDisposable
{
    public const int MaxInfoBytes = 4096, MaxTileBytes = 64 * 1024, MaxMesBytes = 1024 * 1024;
    private readonly ContentStack _stack;
    private bool _disposed;

    private AreaTownMapSource(ContentStack stack, AreaTownMapTable table) { _stack = stack; Table = table; }

    public AreaTownMapTable Table { get; }

    /// <summary>Opens the game folder of <paramref name="modulePath"/> and reads the town-map list; null when either fails.</summary>
    public static AreaTownMapSource? Open(string modulePath, out string detail)
    {
        ArgumentNullException.ThrowIfNull(modulePath);
        var found = AreaContentRoot.Discover(modulePath);
        if (found.Status != AreaContentRootStatus.Ready) { detail = $"not read ({found.Status})"; return null; }
        var opened = ContentStack.Open(found.Plan!);
        if (opened.Status != ContentStackStatus.Ready) { detail = $"not read (content stack {opened.Status})"; return null; }
        var stack = opened.Stack!;
        var candidate = ContentPhase.Candidate(stack, AreaTownMapTable.Path, ContentLoadPhase.ModuleLoaded) ?? stack.Resolve(AreaTownMapTable.Path).Winner;
        if (candidate is null || candidate.Length > MaxMesBytes || stack.Read(candidate) is not { Status: ContentReadStatus.Read, Bytes: { } bytes })
        { stack.Dispose(); detail = AreaTownMapTable.Path + " not read"; return null; }
        var table = AreaTownMapTable.Parse(bytes);
        detail = $"{table.Count} town maps";
        return new(stack, table);
    }

    /// <summary>A town map's entry, info and tile size (WM69, WM72); null for a number the list does not hold, a missing or refused info, or a missing or
    /// unreadable tile 000000 (the load of the original fails, and the window falls back to the map view).</summary>
    public AreaTownMapPlan? Prepare(int number)
    {
        if (Table.Find(number) is not { } entry) return null;
        if (Read(AreaTownMapLayout.InfoPath(entry.Name), MaxInfoBytes) is not { } infoBytes || AreaTownMapInfo.Parse(infoBytes) is not { } info) return null;
        if (ReadTile(entry, 0) is not { } tile || !AreaTownMapLayout.TryTileSize(tile, out var width, out var height)) return null;
        return new(entry, info, width, height);
    }

    /// <summary>Whether the file of tile <paramref name="index"/> exists (a revealed tile without a file is not drawn plainly, WM77).</summary>
    public bool TileExists(AreaTownMapEntry entry, int index) => !_disposed && index >= 0 && _stack.Resolve(AreaTownMapLayout.TilePath(entry.Name, index)).Winner is not null;

    /// <summary>The bytes of tile <paramref name="index"/>; tile 000000 stands in for a tile without a file (WM74); null when neither reads.</summary>
    public byte[]? ReadTile(AreaTownMapEntry entry, int index)
    {
        if (index < 0) return null;
        return Read(AreaTownMapLayout.TilePath(entry.Name, index), MaxTileBytes)
            ?? (index == 0 ? null : Read(AreaTownMapLayout.TilePath(entry.Name, 0), MaxTileBytes));
    }

    private byte[]? Read(string path, int limit) => _disposed ? null
        : _stack.Resolve(path).Winner is { } winner && winner.Length <= limit && _stack.Read(winner) is { Status: ContentReadStatus.Read, Bytes: { } bytes } ? bytes : null;

    public void Dispose() { _disposed = true; _stack.Dispose(); }
}
