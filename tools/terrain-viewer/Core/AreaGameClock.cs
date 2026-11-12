using Arcanum.Simulation.Clock;

namespace Arcanum.TerrainViewer.Core;

/// <summary>Which of the two sky pieces a slice of the clock window comes from: the day strip (interface art 207) or the moon piece
/// of the day's phase (art 208..215).</summary>
public enum AreaClockPiece { Strip, Moon }

/// <summary>One slice of the clock window's sky: <paramref name="Piece"/> cut from its art at <paramref name="SourceX"/>, drawn
/// <paramref name="Width"/> pixels wide at <paramref name="DestX"/> (on the original's 800×600 screen).</summary>
public readonly record struct AreaClockSlice(AreaClockPiece Piece, int SourceX, int Width, int DestX);

/// <summary>How the date part of the clock's text is ordered: the original asks the system's short date order
/// (<c>GetLocaleInfoA(LOCALE_USER_DEFAULT, LOCALE_IDATE)</c>, 0x45AC70) and writes month/day/year for 0, day/month/year for 1 and
/// year/month/day otherwise.</summary>
public enum AreaClockDateOrder { MonthDayYear, DayMonthYear, YearMonthDay }

/// <summary>The sky the clock window shows after a refresh (ledger GB8, GB9): the scroll offset into the repeating sky (the day strip
/// followed by the moon piece), the moon art, and the game time of the refresh.</summary>
public readonly record struct AreaClockFace(int Offset, int MoonArt, long RefreshedAtMs)
{
    /// <summary>The slices that fill the window for sky pieces of the given widths.</summary>
    public IReadOnlyList<AreaClockSlice> Slices(int stripWidth, int moonWidth) => AreaClockLayout.Slices(Offset, stripWidth, moonWidth);
}

/// <summary>
/// ARC-UI-1111: the clock of the original's top interface strip, Godot-free (ledger GB8–GB12; the clock refresh 0x5522F0 and its event
/// 0x5526F0, the clock button's release in the in-game filter 0x54B5D0, the time and date text 0x45AC20 and 0x45AC70). A 128 × 30
/// window at (648, 5) shows a sky that scrolls once a game day: the day strip (art 207, 384 px) and the moon piece of the day's phase
/// (art 208–215, 128 px) repeat, the window's centre marks the game time, and a 5 px needle (art 216) stands at (708, 6).
/// </summary>
public static class AreaClockLayout
{
    /// <summary>The window and its hit area on the 800×600 screen (0x5C6F80: x, y, width, height).</summary>
    public const int WindowX = 648, WindowY = 5, WindowWidth = 128, WindowHeight = 30;
    /// <summary>The interface art of the day strip, of the first of the eight moon pieces and of the needle (the window's draw asks 0xCF,
    /// 0xD0 + phase index, 0xD8).</summary>
    public const int StripArt = 207, FirstMoonArt = 208, NeedleArt = 216;
    public const int MoonArtCount = 8;
    /// <summary>Where the needle is drawn (0x2C4, 6 of the top strip window, whose origin is the screen's).</summary>
    public const int NeedleX = 708, NeedleY = 6;
    /// <summary>The clock refreshes again this many game milliseconds after it ran (the next Clock event, 0x5526F0).</summary>
    public const long RefreshMs = 3_600_000;
    /// <summary>The widths the original reads from the strip and the first moon art (0x54A330, 0x64C47C and 0x64C480).</summary>
    public const int StripWidth = 384, MoonWidth = 128;
    /// <summary>The <c>intgame.mes</c> messages that label the time and the date in the clicked text (0x16 and 0x17).</summary>
    public const int TimeMessage = 22, DateMessage = 23;

    private const long SecondsPerDay = 86_400;
    // The scroll is 20.5 hours ahead of the game's own midnight; the moon's day turns at noon (0x12048 and 0xA8C0 seconds).
    private const long ScrollShiftSeconds = 0x12048, MoonShiftSeconds = 0xA8C0;
    private const int MoonDays = 28;
    // The phase thresholds (0x5C730C..0x5C7328): the moon art is the first whose threshold the phase does not pass.
    private static readonly int[] MoonThresholds = [0, 4, 9, 13, 14, 18, 23, 27];

    /// <summary>Every interface art number the clock draws.</summary>
    public static IReadOnlyList<int> ArtNumbers { get; } = [StripArt, .. Enumerable.Range(FirstMoonArt, MoonArtCount), NeedleArt];

    /// <summary>Whole game seconds of a game time (0x45A840: milliseconds of the day over 1000 plus the days times 86,400).</summary>
    public static long Seconds(long gameMs) => gameMs / 1000;

    /// <summary>The moon art of the day's phase: the number of whole days since noon of day 0, modulo 28, mapped by the thresholds.</summary>
    public static int MoonArt(long gameMs)
    {
        var phase = (int)((Seconds(gameMs) + MoonShiftSeconds) / SecondsPerDay % MoonDays);
        for (var index = 0; index < MoonThresholds.Length; index++)
            if (phase <= MoonThresholds[index]) return FirstMoonArt + index;
        return FirstMoonArt + MoonThresholds.Length - 1;
    }

    /// <summary>The scroll offset into the repeating sky of <paramref name="stripWidth"/> + <paramref name="moonWidth"/> pixels: the
    /// share of the day since 03:30 of the sky's length, the window's half width back (so the window's centre is the game time).</summary>
    public static int Offset(long gameMs, int stripWidth, int moonWidth)
    {
        if (stripWidth <= 0 || moonWidth <= 0) throw new ArgumentOutOfRangeException(nameof(stripWidth));
        long total = stripWidth + moonWidth;
        var dayShare = (Seconds(gameMs) + ScrollShiftSeconds) % SecondsPerDay;
        var raw = total * dayShare / SecondsPerDay;
        return (int)((raw - WindowWidth / 2 + total) % total);
    }

    /// <summary>The slices that fill the window from <paramref name="offset"/>: the pieces alternate strip, moon, strip (at most three
    /// are drawn), each cut from where the offset falls in it, as the refresh's loop does.</summary>
    public static IReadOnlyList<AreaClockSlice> Slices(int offset, int stripWidth, int moonWidth)
    {
        if (stripWidth <= 0 || moonWidth <= 0) throw new ArgumentOutOfRangeException(nameof(stripWidth));
        if (offset < 0 || offset >= stripWidth + moonWidth) throw new ArgumentOutOfRangeException(nameof(offset));
        var slices = new List<AreaClockSlice>(3);
        var piece = AreaClockPiece.Strip;
        var remaining = WindowWidth;
        var destX = WindowX;
        for (var step = 0; step < 3; step++)
        {
            var width = piece == AreaClockPiece.Strip ? stripWidth : moonWidth;
            if (offset < width)
            {
                var drawn = Math.Min(width - offset, remaining);
                slices.Add(new(piece, offset, drawn, destX));
                destX += drawn; remaining -= drawn; offset = 0;
            }
            else offset -= width;
            if (remaining < 1) break;
            piece = piece == AreaClockPiece.Strip ? AreaClockPiece.Moon : AreaClockPiece.Strip;
        }
        return slices;
    }

    /// <summary>The face of a refresh at <paramref name="gameMs"/> for pieces of the given widths.</summary>
    public static AreaClockFace Face(long gameMs, int stripWidth = StripWidth, int moonWidth = MoonWidth) =>
        new(Offset(gameMs, stripWidth, moonWidth), MoonArt(gameMs), gameMs);
}

/// <summary>
/// ARC-UI-1111: when the clock window refreshes (ledger GB10, TL22 kind 16). The original draws it when the interface is made and in
/// the handler of the Clock event, which adds the next event 3,600,000 game ms after it ran and removes any other of its kind; a late
/// event fires once and a missed period is not replayed. So the face changes once a game hour counted from the last refresh, not on the
/// hour, and a jump over several hours refreshes once. Not saved here: a load or a new session starts the chain again with a refresh
/// (the original's interface is made again then). Caller-serialized.
/// </summary>
public sealed class AreaGameClock
{
    private readonly int _stripWidth, _moonWidth;

    public AreaGameClock(int stripWidth = AreaClockLayout.StripWidth, int moonWidth = AreaClockLayout.MoonWidth)
    {
        if (stripWidth <= 0 || moonWidth <= 0) throw new ArgumentOutOfRangeException(nameof(stripWidth));
        _stripWidth = stripWidth; _moonWidth = moonWidth;
    }

    /// <summary>What the window shows; null before the first refresh.</summary>
    public AreaClockFace? Face { get; private set; }
    /// <summary>The game time from which the next refresh is due; 0 before the first refresh.</summary>
    public long NextRefreshMs { get; private set; }
    /// <summary>How many times the clock refreshed.</summary>
    public int Refreshes { get; private set; }

    /// <summary>Draws the clock at <paramref name="gameMs"/> and arms the next refresh an hour later.</summary>
    public void Refresh(long gameMs)
    {
        Face = AreaClockLayout.Face(gameMs, _stripWidth, _moonWidth);
        NextRefreshMs = gameMs + AreaClockLayout.RefreshMs;
        Refreshes++;
    }

    /// <summary>The game clock is at <paramref name="gameMs"/>: refreshes when the event is due (or the time went back, as after a
    /// load, which the original answers with a fresh refresh); true when it did.</summary>
    public bool Tick(long gameMs)
    {
        if (Face is { } face && gameMs >= face.RefreshedAtMs && gameMs < NextRefreshMs) return false;
        Refresh(gameMs);
        return true;
    }

    /// <summary>Forgets the face: the next <see cref="Tick"/> refreshes.</summary>
    public void Reset()
    {
        Face = null; NextRefreshMs = 0;
    }
}

/// <summary>
/// ARC-UI-1111: the text a click on the clock writes (ledger GB11): the label of message 22 and the time, then three spaces, the label of
/// message 23 and the date, as one entry of the hover kind.
/// </summary>
public static class AreaClockText
{
    /// <summary>The time as <c>%02d:%02d:%02d</c>: hour, minute and the game seconds of the minute (0x45AC20).</summary>
    public static string Time(long gameMs)
    {
        var seconds = AreaClockLayout.Seconds(gameMs);
        return $"{seconds / 3600 % 24:00}:{seconds / 60 % 60:00}:{seconds % 60:00}";
    }

    /// <summary>The date as <c>%d/%d/%d</c> in <paramref name="order"/> (0x45AC70): day, month and year from the 30-day month and
    /// 12-month year calendar of the clock, the year counted from <paramref name="startYear"/>.</summary>
    public static string Date(long gameMs, long startYear, AreaClockDateOrder order)
    {
        if (ClockCalendar.TryDerive(gameMs, startYear, out var calendar) != ClockFailure.None) throw new ArgumentOutOfRangeException(nameof(gameMs));
        return order switch
        {
            AreaClockDateOrder.DayMonthYear => $"{calendar.DayOfMonth}/{calendar.Month}/{calendar.Year}",
            AreaClockDateOrder.YearMonthDay => $"{calendar.Year}/{calendar.Month}/{calendar.DayOfMonth}",
            _ => $"{calendar.Month}/{calendar.DayOfMonth}/{calendar.Year}",
        };
    }

    /// <summary>The whole entry: <c>"{timeLabel}: {time}   {dateLabel}: {date}"</c> (the formats <c>%s: %s</c> and <c>   %s: %s</c>).</summary>
    public static string Line(string timeLabel, string dateLabel, long gameMs, long startYear, AreaClockDateOrder order) =>
        $"{timeLabel}: {Time(gameMs)}   {dateLabel}: {Date(gameMs, startYear, order)}";

    /// <summary>The order the system's short date pattern implies (the original reads the system's date order): the first of month,
    /// day and year in the pattern; an empty or unreadable pattern gives month/day/year, as the original does when the lookup fails.</summary>
    public static AreaClockDateOrder OrderOf(string? shortDatePattern)
    {
        foreach (var letter in shortDatePattern ?? "")
        {
            if (letter == 'M') return AreaClockDateOrder.MonthDayYear;
            if (letter == 'd') return AreaClockDateOrder.DayMonthYear;
            if (letter == 'y') return AreaClockDateOrder.YearMonthDay;
        }
        return AreaClockDateOrder.MonthDayYear;
    }
}
