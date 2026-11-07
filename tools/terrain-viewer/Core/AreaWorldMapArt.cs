using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using Arcanum.Data.Content;
using Arcanum.Data.Messages;
using Arcanum.World;

namespace Arcanum.TerrainViewer.Core;

/// <summary>The four buttons of the world-map window (art 811 and 812 are the zoom and whole-map buttons, 813 Travel, 139 the compass).</summary>
public enum AreaWorldMapArtButton { Zoom, Whole, Travel, Compass }

/// <summary>
/// ARC-UI-073: the original look of the world-map window (interface mode 5; ledger WM1–WM39), read in the retail executable: the window
/// is 800 × 400 at the screen's (0, 41) as every game window (table 0x5C9B00), art 138 <c>mapmain</c> at its (0, 0) and, in window
/// coordinates, the buttons 811 at (20, 165), 812 at (19, 267), 813 at (698, 312) and the compass 139 at (375, 347). The map view
/// ("Map View", kind 0) is the executable's draw rectangle (150, 52, 501, 365) of the SCREEN, which is (150, 11) in the window: art 138's
/// transparent hole is exactly x 150..650, y 11..375 (read in the art, ARC-UI-073). It shows the 2000 × 2000 picture of eight by eight tiles
/// (<c>worldmap/smallmapchunksNNN.bmp</c>, 250 × 250) at an offset the scroll keeps in [0, picture − view]; tile (c, r) is drawn at
/// (150 + 250 c − offset x, 52 + 250 r − offset y) of the screen. All coordinates here are the window's. Godot-free.
/// </summary>
public static class AreaWorldMapArtLayout
{
    public const int BackgroundArt = 138, ZoomArt = 811, WholeArt = 812, TravelArt = 813, CompassArt = 139;
    /// <summary>The marker arts the view draws (WM10): the party cross, an area, a waypoint and the newly found area.</summary>
    public const int PartyArt = 204, AreaArt = 205, WaypointArt = 206, NewAreaArt = 814;

    public const int WindowWidth = 800, WindowHeight = 400;
    /// <summary>The map view's rectangle in the window: the executable's (150, 52, 501, 365) of the screen minus the window's 41 rows (WM14);
    /// it is the hole of the background art.</summary>
    public static readonly (int X, int Y, int Width, int Height) View = (150, 11, 501, 365);
    public const int TileSize = 250, PictureSize = 2000, TileColumns = 8, TileRows = 8;

    /// <summary>Every interface art the look needs.</summary>
    public static IReadOnlyList<int> ArtNumbers { get; } = [BackgroundArt, ZoomArt, WholeArt, TravelArt, CompassArt, PartyArt, AreaArt, WaypointArt, NewAreaArt];

    /// <summary>A button's art and its top-left in the window (WM9).</summary>
    public static (int Art, int X, int Y) Button(AreaWorldMapArtButton button) => button switch
    {
        AreaWorldMapArtButton.Zoom => (ZoomArt, 20, 165),
        AreaWorldMapArtButton.Whole => (WholeArt, 19, 267),
        AreaWorldMapArtButton.Travel => (TravelArt, 698, 312),
        AreaWorldMapArtButton.Compass => (CompassArt, 375, 347),
        _ => throw new ArgumentOutOfRangeException(nameof(button)),
    };

    /// <summary>The compass' dead zone (WM31), a rectangle of the screen's coordinates in the executable, (391, 403) 10 × 12, which is the
    /// window's (391, 362): a press inside it does not scroll.</summary>
    public static readonly (int X, int Y, int Width, int Height) CompassDeadZone = (391, 362, 10, 12);

    /// <summary>The scroll direction a compass press at a window point gives: outside the dead zone on one axis it scrolls that way, on both
    /// diagonally (WM31); -1 inside it.</summary>
    public static int CompassDirection(double x, double y)
    {
        var zone = CompassDeadZone;
        var left = x < zone.X; var right = x >= zone.X + zone.Width;
        var up = y < zone.Y; var down = y >= zone.Y + zone.Height;
        return Direction(left, right, up, down);
    }

    /// <summary>The scroll step of the window's own poll (WM18: 5 × 4) and of a compass press or the dragged waypoint's edge (WM31, WM29).</summary>
    public const int KeyStep = 20, CompassStep = 40, DragStep = 50;
    /// <summary>The poll runs at most once for this many milliseconds (WM18).</summary>
    public const long PollMilliseconds = 10;
    /// <summary>The compass repeats a held press this often (the executable's 250 ms button repeat, WM31).</summary>
    public const long CompassRepeatMilliseconds = 250;

    /// <summary>The largest offset: the picture's size minus the view (WM7; the keyed map's width / 64 and height / 64 are 2000).</summary>
    public static (int X, int Y) MaxOffset(long pictureWidth = PictureSize, long pictureHeight = PictureSize) =>
        ((int)Math.Max(0, pictureWidth - View.Width), (int)Math.Max(0, pictureHeight - View.Height));

    public static (int X, int Y) Clamp((int X, int Y) offset, (int X, int Y) max) =>
        (Math.Clamp(offset.X, 0, max.X), Math.Clamp(offset.Y, 0, max.Y));

    /// <summary>
    /// The offset that centres the view on a picture point (WM12, WM13). The original clamps the upper end against the picture's size, not the
    /// scroll maximum, so centring near the east or south edge shows blank fill until the next scroll step snaps it back: a technical,
    /// presentation-only flaw fixed in both rule modes (AGENTS.md original bugs, no switch).
    /// </summary>
    public static (int X, int Y) CentreOn(long x, long y, (int X, int Y) max) =>
        Clamp(((int)Math.Clamp(x - View.Width / 2, int.MinValue / 2, int.MaxValue / 2), (int)Math.Clamp(y - View.Height / 2, int.MinValue / 2, int.MaxValue / 2)), max);

    /// <summary>The scroll direction code of the executable (0 up, clockwise to 7 up-left; -1 for none) of the held arrow keys: left beats
    /// right and up beats down (WM18).</summary>
    public static int Direction(bool left, bool right, bool up, bool down)
    {
        var dx = left ? -1 : right ? 1 : 0;
        var dy = up ? -1 : down ? 1 : 0;
        return (dx, dy) switch
        {
            (0, -1) => 0, (1, -1) => 1, (1, 0) => 2, (1, 1) => 3, (0, 1) => 4, (-1, 1) => 5, (-1, 0) => 6, (-1, -1) => 7, _ => -1,
        };
    }

    /// <summary>One scroll step of <paramref name="step"/> pixels on each moving axis (a diagonal moves both), clamped to [0, max] (WM19).</summary>
    public static (int X, int Y) Scroll((int X, int Y) offset, int direction, int step, (int X, int Y) max)
    {
        var (dx, dy) = direction switch
        {
            0 => (0, -1), 1 => (1, -1), 2 => (1, 0), 3 => (1, 1), 4 => (0, 1), 5 => (-1, 1), 6 => (-1, 0), 7 => (-1, -1), _ => (0, 0),
        };
        return Clamp((offset.X + dx * step, offset.Y + dy * step), max);
    }

    /// <summary>The inclusive tile columns and rows a view at <paramref name="offset"/> touches (WM14), clamped to the grid.</summary>
    public static (int FirstColumn, int LastColumn, int FirstRow, int LastRow) VisibleTiles((int X, int Y) offset) => (
        Math.Clamp(offset.X / TileSize, 0, TileColumns - 1), Math.Clamp((offset.X + View.Width - 1) / TileSize, 0, TileColumns - 1),
        Math.Clamp(offset.Y / TileSize, 0, TileRows - 1), Math.Clamp((offset.Y + View.Height - 1) / TileSize, 0, TileRows - 1));

    /// <summary>The window position of a picture point at <paramref name="offset"/> (the tiles' formula, WM14); it may lie outside the view.</summary>
    public static (int X, int Y) ToWindow(long pictureX, long pictureY, (int X, int Y) offset) =>
        ((int)(View.X + pictureX - offset.X), (int)(View.Y + pictureY - offset.Y));

    /// <summary>The picture pixel under a window point (WM25), or null outside the view. The executable compares the raw mouse record with the
    /// view's screen rectangle while its converter subtracts a further 41 rows (WM26, unresolved: the mouse record's frame is not read); the
    /// viewer takes the drawn view for both, so a click lands on the pixel under the pointer.</summary>
    public static (long X, long Y)? ToPicture(double windowX, double windowY, (int X, int Y) offset)
    {
        if (windowX < View.X || windowY < View.Y || windowX >= View.X + View.Width || windowY >= View.Y + View.Height) return null;
        return ((long)Math.Floor(windowX) - View.X + offset.X, (long)Math.Floor(windowY) - View.Y + offset.Y);
    }

    /// <summary>The tile file of column <paramref name="column"/> and row <paramref name="row"/>: number 8 × row + column + 1 (WM14).</summary>
    public static string TilePath(string baseName, int column, int row) =>
        $"worldmap/{baseName.ToLowerInvariant()}{(TileColumns * row + column + 1).ToString("D3", CultureInfo.InvariantCulture)}.bmp";
}

/// <summary>Where the module keeps the world-map tiles: the keyed map's entry of <c>worldmap/worldmap.mes</c> (entry 50: columns, rows, base name).</summary>
public sealed record AreaWorldMapTileSet(int Columns, int Rows, string BaseName, int KeyedMap)
{
    /// <summary>Reads <c>cols, rows, BaseName, ZoomedName: X, MapKeyedTo: n</c>; null for an entry that does not give them.</summary>
    public static AreaWorldMapTileSet? Parse(string entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var parts = entry.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length < 3 || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var columns)
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var rows) || parts[2].Length == 0
            || columns is < 1 or > 64 || rows is < 1 or > 64) return null;
        var keyed = 0;
        foreach (var part in parts.Skip(3))
            if (part.StartsWith("MapKeyedTo:", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(part.AsSpan("MapKeyedTo:".Length).Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var map)) keyed = map;
        return new(columns, rows, parts[2], keyed);
    }
}

/// <summary>
/// ARC-UI-073: what the world-map window draws from the game folder: its interface art and the 64 tile files (8-bit BMPs of 250 × 250,
/// kept as the bytes the host decodes). The window shows the original look only when everything read; otherwise the diagnostic panel stays.
/// </summary>
public sealed record AreaWorldMapArtContent(IReadOnlyDictionary<int, AreaInterfaceArtLoad> Art, IReadOnlyList<byte[]?> Tiles, string Detail)
{
    public const string MesPath = "worldmap/worldmap.mes";
    /// <summary>The entry of the world map's tiles in <c>worldmap.mes</c> (ledger WM4).</summary>
    public const int TileEntry = 50;
    public const int MaxTileBytes = 256 * 1024;

    public bool ArtReady => AreaWorldMapArtLayout.ArtNumbers.All(number => Art.TryGetValue(number, out var load) && load.Status == AreaInterfaceArtStatus.Ready);

    public bool TilesReady => Tiles.Count == AreaWorldMapArtLayout.TileColumns * AreaWorldMapArtLayout.TileRows && Tiles.All(tile => tile is not null);

    public bool Ready => ArtReady && TilesReady;

    public static AreaWorldMapArtContent Empty(string reason) => new(new Dictionary<int, AreaInterfaceArtLoad>(), [], reason);

    /// <summary>Whether <paramref name="bytes"/> is a plain 8-bit BMP of 250 × 250 (a top-down or bottom-up one), the tile format of the module.</summary>
    public static bool IsTile(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 54 || bytes[0] != (byte)'B' || bytes[1] != (byte)'M') return false;
        var width = BinaryPrimitives.ReadInt32LittleEndian(bytes[18..]);
        var height = BinaryPrimitives.ReadInt32LittleEndian(bytes[22..]);
        var planes = BinaryPrimitives.ReadInt16LittleEndian(bytes[26..]);
        var bits = BinaryPrimitives.ReadInt16LittleEndian(bytes[28..]);
        var compression = BinaryPrimitives.ReadInt32LittleEndian(bytes[30..]);
        return width == AreaWorldMapArtLayout.TileSize && Math.Abs(height) == AreaWorldMapArtLayout.TileSize && planes == 1 && bits == 8 && compression == 0;
    }

    /// <summary>Reads the window's art and tiles from the game folder of <paramref name="modulePath"/>; failures are reported, never thrown.</summary>
    public static AreaWorldMapArtContent Load(string modulePath)
    {
        ArgumentNullException.ThrowIfNull(modulePath);
        var found = AreaContentRoot.Discover(modulePath);
        if (found.Status != AreaContentRootStatus.Ready) return Empty($"not read ({found.Status} {found.Detail})");
        var opened = ContentStack.Open(found.Plan!);
        if (opened.Status != ContentStackStatus.Ready) return Empty($"not read (content stack {opened.Status})");
        using var stack = opened.Stack!;
        var art = AreaInterfaceArt.Load(stack, AreaWorldMapArtLayout.ArtNumbers);
        var failed = art.Values.Where(load => load.Status != AreaInterfaceArtStatus.Ready).Select(load => $"{load.Number} {load.Status} {load.Detail}").ToList();
        var tiles = ReadTiles(stack, out var tileDetail);
        return new(art, tiles, $"art {art.Count - failed.Count}/{art.Count} read{(failed.Count == 0 ? "" : " (" + string.Join("; ", failed) + ")")}; {tileDetail}");
    }

    private static IReadOnlyList<byte[]?> ReadTiles(ContentStack stack, out string detail)
    {
        if (ContentPhase.Candidate(stack, MesPath, ContentLoadPhase.ModuleLoaded) is not { } candidate) { detail = MesPath + " missing"; return []; }
        var read = stack.Read(candidate);
        if (read.Status != ContentReadStatus.Read || read.Bytes is not { } mesBytes) { detail = $"{MesPath} {read.Status}"; return []; }
        var parsed = MesFile.Parse(mesBytes);
        if (parsed.Status != MesParseStatus.Parsed || parsed.File!.Find(TileEntry) is not { } entry) { detail = $"{MesPath} has no entry {TileEntry}"; return []; }
        if (AreaWorldMapTileSet.Parse(Encoding.Latin1.GetString(entry.Bytes.Span)) is not { } set
            || set.Columns != AreaWorldMapArtLayout.TileColumns || set.Rows != AreaWorldMapArtLayout.TileRows)
        { detail = $"{MesPath} entry {TileEntry} is not an 8 x 8 tile set"; return []; }
        var tiles = new byte[]?[set.Columns * set.Rows];
        var bad = 0;
        for (var row = 0; row < set.Rows; row++)
            for (var column = 0; column < set.Columns; column++)
            {
                if (stack.Resolve(AreaWorldMapArtLayout.TilePath(set.BaseName, column, row)).Winner is not { } winner || winner.Length > MaxTileBytes) { bad++; continue; }
                var tile = stack.Read(winner);
                if (tile.Status == ContentReadStatus.Read && tile.Bytes is { } bytes && IsTile(bytes)) tiles[set.Columns * row + column] = bytes;
                else bad++;
            }
        detail = $"tiles {tiles.Length - bad}/{tiles.Length} read";
        return tiles;
    }
}
