using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-UI-1111: the clock window and the clock button of the top strip (ledger GB8–GB12): the layout, the sky's offset and moon,
/// the slices of the window, the refresh chain, the hit area and the clicked text.</summary>
[TestClass]
public sealed class AreaGameClockTests
{
    private const long Hour = 3_600_000, Day = 86_400_000;

    [TestMethod]
    public void The_layout_is_the_originals_table()
    {
        Assert.AreEqual((648, 5, 128, 30), (AreaClockLayout.WindowX, AreaClockLayout.WindowY, AreaClockLayout.WindowWidth, AreaClockLayout.WindowHeight), "GB8 window");
        Assert.AreEqual((708, 6), (AreaClockLayout.NeedleX, AreaClockLayout.NeedleY), "the needle stands at the window's centre column");
        CollectionAssert.AreEqual(new[] { 207, 208, 209, 210, 211, 212, 213, 214, 215, 216 }, AreaClockLayout.ArtNumbers.ToArray());
        var chain = new AreaGameClock();
        chain.Refresh(0);
        Assert.AreEqual(Hour, chain.NextRefreshMs, "the Clock event re-arms an hour later (TL22)");
        var spec = AreaGameBarLayout.Spec(AreaBarButton.Clock);
        Assert.AreEqual((AreaBarStrip.Top, 648, 5, -1, AreaBarButtonKind.Push, true), (spec.Strip, spec.X, spec.Y, spec.Art, spec.Kind, spec.Silent), "GB9 hit area");
        Assert.IsFalse(AreaGameBarLayout.Buttons.Any(button => button.Button == AreaBarButton.Clock), "not one of the ten art buttons");
    }

    [TestMethod]
    public void The_moon_art_follows_the_day_phase_from_noon()
    {
        // Phase = whole days since noon of day 0, modulo 28; the art is the first whose threshold {0,4,9,13,14,18,23,27} the phase does not pass.
        var expected = new Dictionary<int, int>();
        foreach (var (first, last, art) in new[] { (0, 0, 208), (1, 4, 209), (5, 9, 210), (10, 13, 211), (14, 14, 212), (15, 18, 213), (19, 23, 214), (24, 27, 215) })
            for (var phase = first; phase <= last; phase++) expected[phase] = art;
        for (var phase = 0; phase < 28; phase++)
        {
            // The phase turns at noon: the first second of a phase is noon of the day before.
            var ms = phase == 0 ? 0L : phase * Day - Day / 2;
            Assert.AreEqual(expected[phase], AreaClockLayout.MoonArt(ms), $"phase {phase}");
            Assert.AreEqual(expected[phase], AreaClockLayout.MoonArt(ms + Day / 2 - 1000), $"phase {phase} just before the next noon");
            Assert.AreEqual(expected[phase], AreaClockLayout.MoonArt(ms + 28 * Day), $"phase {phase} a lunar cycle later");
        }
        Assert.AreEqual(208, AreaClockLayout.MoonArt(Day / 2 - 1000), "phase 0 lasts until noon of day 0");
        Assert.AreEqual(209, AreaClockLayout.MoonArt(Day / 2), "noon of day 0 starts phase 1");
    }

    [TestMethod]
    public void The_offset_scrolls_once_a_day_with_the_window_centred_on_the_time()
    {
        // total = 384 + 128 = 512; the share of the day starts at 03:30 (20.5 h ahead of midnight) and the window's half width goes back.
        Assert.AreEqual(448, AreaClockLayout.Offset(3 * Hour + Hour / 2, 384, 128), "03:30 is the share's zero: 0 - 64 + 512");
        Assert.AreEqual(373, AreaClockLayout.Offset(0, 384, 128), "midnight: share 73,800/86,400 of 512 = 437, minus 64");
        Assert.AreEqual(117, AreaClockLayout.Offset(12 * Hour, 384, 128), "noon: share 30,600/86,400 of 512 = 181, minus 64");
        Assert.AreEqual(AreaClockLayout.Offset(5 * Hour, 384, 128), AreaClockLayout.Offset(5 * Hour + 4 * Day, 384, 128), "a day later it is the same");
        // The offset moves forward through the day and stays inside the sky.
        var previous = AreaClockLayout.Offset(3 * Hour + Hour / 2, 384, 128);
        for (var minutes = 1; minutes <= 24 * 60; minutes++)
        {
            var offset = AreaClockLayout.Offset(3 * Hour + Hour / 2 + minutes * 60_000L, 384, 128);
            Assert.IsTrue(offset >= 0 && offset < 512);
            Assert.IsTrue(offset == (previous + 1) % 512 || offset == previous || offset == (previous + 2) % 512, $"minute {minutes}: {previous} -> {offset}");
            previous = offset;
        }
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => AreaClockLayout.Offset(0, 0, 128));
    }

    [TestMethod]
    public void The_window_is_filled_by_strip_moon_strip_slices()
    {
        void Check(int offset, params (AreaClockPiece Piece, int Source, int Width, int Dest)[] expected) =>
            CollectionAssert.AreEqual(expected.Select(e => new AreaClockSlice(e.Piece, e.Source, e.Width, e.Dest)).ToArray(), AreaClockLayout.Slices(offset, 384, 128).ToArray(), $"offset {offset}");
        Check(0, (AreaClockPiece.Strip, 0, 128, 648));
        Check(300, (AreaClockPiece.Strip, 300, 84, 648), (AreaClockPiece.Moon, 0, 44, 732));
        Check(383, (AreaClockPiece.Strip, 383, 1, 648), (AreaClockPiece.Moon, 0, 127, 649));
        Check(384, (AreaClockPiece.Moon, 0, 128, 648));
        Check(450, (AreaClockPiece.Moon, 66, 62, 648), (AreaClockPiece.Strip, 0, 66, 710));
        Check(511, (AreaClockPiece.Moon, 127, 1, 648), (AreaClockPiece.Strip, 0, 127, 649));
        for (var offset = 0; offset < 512; offset++)
        {
            var slices = AreaClockLayout.Slices(offset, 384, 128);
            Assert.AreEqual(128, slices.Sum(slice => slice.Width), $"offset {offset} fills the window");
            var next = 648;
            foreach (var slice in slices) { Assert.AreEqual(next, slice.DestX, $"offset {offset} is contiguous"); next += slice.Width; }
            Assert.IsTrue(slices.Count is >= 1 and <= 3);
        }
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => AreaClockLayout.Slices(512, 384, 128));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => AreaClockLayout.Slices(-1, 384, 128));
    }

    [TestMethod]
    public void The_face_carries_offset_moon_and_the_time_of_the_refresh()
    {
        var face = AreaClockLayout.Face(12 * Hour);
        Assert.AreEqual(new AreaClockFace(117, 209, 12 * Hour), face);
        Assert.AreEqual(1, face.Slices(384, 128).Count, "at noon the window lies inside the day strip");
    }

    [TestMethod]
    public void The_clock_refreshes_when_made_and_an_hour_after_each_refresh()
    {
        var clock = new AreaGameClock();
        Assert.IsNull(clock.Face);
        Assert.IsTrue(clock.Tick(10 * Hour + 1234), "the first tick refreshes (the interface is made)");
        Assert.AreEqual(10 * Hour + 1234, clock.Face!.Value.RefreshedAtMs);
        Assert.AreEqual(11 * Hour + 1234, clock.NextRefreshMs, "the next event is an hour after the refresh, not on the hour");
        Assert.IsFalse(clock.Tick(11 * Hour + 1233));
        Assert.AreEqual(1, clock.Refreshes);
        Assert.IsTrue(clock.Tick(11 * Hour + 1234));
        Assert.AreEqual(12 * Hour + 1234, clock.NextRefreshMs);
        // A jump over several hours refreshes once; the missed periods are not replayed.
        Assert.IsTrue(clock.Tick(17 * Hour + 50));
        Assert.IsFalse(clock.Tick(17 * Hour + 51));
        Assert.AreEqual(3, clock.Refreshes);
        Assert.AreEqual(18 * Hour + 50, clock.NextRefreshMs);
    }

    [TestMethod]
    public void A_clock_that_runs_backwards_or_was_reset_refreshes_at_once()
    {
        var clock = new AreaGameClock();
        clock.Refresh(30 * Hour);
        Assert.IsTrue(clock.Tick(2 * Hour), "a load puts the game time back");
        Assert.AreEqual(2 * Hour, clock.Face!.Value.RefreshedAtMs);
        clock.Reset();
        Assert.IsNull(clock.Face);
        Assert.IsTrue(clock.Tick(2 * Hour));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new AreaGameClock(0, 128));
    }

    [TestMethod]
    public void The_clock_button_is_a_silent_push_button_without_a_hover_name()
    {
        var bar = new AreaGameBar();
        var enter = bar.Enter(AreaBarButton.Clock);
        Assert.IsFalse(enter.HoverChanged, "the filter has no enter case for the clock: nothing is written or cleared");
        Assert.IsNull(bar.HoverMessage);
        var press = bar.Press(AreaBarButton.Clock);
        Assert.AreEqual(0, press.Sounds.Count, "the button is made with all sounds -1");
        Assert.IsNull(press.Act);
        Assert.AreEqual(AreaBarButton.Clock, bar.Held);
        var release = bar.Release(AreaBarButton.Clock);
        Assert.AreEqual(0, release.Sounds.Count);
        Assert.AreEqual(AreaBarButton.Clock, release.Act, "the release over it acts");
        var exit = bar.Exit(AreaBarButton.Clock);
        Assert.IsTrue(exit.HoverChanged, "leaving it clears the hover entry (0x54DC80)");
        Assert.IsNull(exit.HoverMessage);
        // A release elsewhere only lets it go.
        bar.Press(AreaBarButton.Clock);
        Assert.IsNull(bar.Release(null).Act);
        Assert.IsNull(bar.Held);
    }

    [TestMethod]
    public void The_time_text_is_hour_minute_and_second_of_the_game_time()
    {
        Assert.AreEqual("00:00:00", AreaClockText.Time(0));
        Assert.AreEqual("15:04:09", AreaClockText.Time(((400L * 86_400) + 15 * 3600 + 4 * 60 + 9) * 1000 + 999));
        Assert.AreEqual("23:59:59", AreaClockText.Time(Day - 1));
    }

    [TestMethod]
    public void The_date_text_has_no_padding_and_follows_the_order()
    {
        // Day 400: day 400 % 30 + 1 = 11, month 400 / 30 % 12 + 1 = 2, year start + 400 / 360 (30-day months, 12-month years).
        var ms = 400 * Day + 5 * Hour;
        Assert.AreEqual("2/11/1001", AreaClockText.Date(ms, 1000, AreaClockDateOrder.MonthDayYear));
        Assert.AreEqual("11/2/1001", AreaClockText.Date(ms, 1000, AreaClockDateOrder.DayMonthYear));
        Assert.AreEqual("1001/2/11", AreaClockText.Date(ms, 1000, AreaClockDateOrder.YearMonthDay));
        Assert.AreEqual("1/1/1000", AreaClockText.Date(0, 1000, AreaClockDateOrder.MonthDayYear), "day 0 is 1/1");
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => AreaClockText.Date(-1, 1000, AreaClockDateOrder.MonthDayYear));
    }

    [TestMethod]
    public void The_line_joins_the_two_labelled_parts_with_three_spaces()
    {
        var ms = 400 * Day + 15 * Hour + 4 * 60_000 + 9_000;
        Assert.AreEqual("Time: 15:04:09   Date: 2/11/1001", AreaClockText.Line("Time", "Date", ms, 1000, AreaClockDateOrder.MonthDayYear));
        Assert.AreEqual("Zeit: 15:04:09   Datum: 11/2/1001", AreaClockText.Line("Zeit", "Datum", ms, 1000, AreaClockDateOrder.DayMonthYear));
    }

    [TestMethod]
    public void The_date_order_comes_from_the_short_date_pattern()
    {
        Assert.AreEqual(AreaClockDateOrder.MonthDayYear, AreaClockText.OrderOf("M/d/yyyy"));
        Assert.AreEqual(AreaClockDateOrder.MonthDayYear, AreaClockText.OrderOf("MM/dd/yyyy"));
        Assert.AreEqual(AreaClockDateOrder.DayMonthYear, AreaClockText.OrderOf("dd/MM/yyyy"));
        Assert.AreEqual(AreaClockDateOrder.DayMonthYear, AreaClockText.OrderOf("d.M.yyyy"));
        Assert.AreEqual(AreaClockDateOrder.YearMonthDay, AreaClockText.OrderOf("yyyy-MM-dd"));
        Assert.AreEqual(AreaClockDateOrder.MonthDayYear, AreaClockText.OrderOf(""), "a failed lookup is month/day/year");
        Assert.AreEqual(AreaClockDateOrder.MonthDayYear, AreaClockText.OrderOf(null));
    }

    [TestMethod]
    public void The_clock_labels_have_the_projects_words_until_the_games_file_reads()
    {
        var texts = AreaGameBarTexts.Project("no game folder");
        Assert.AreEqual("Time", texts.Text(AreaClockLayout.TimeMessage));
        Assert.AreEqual("Date", texts.Text(AreaClockLayout.DateMessage));
    }
}
