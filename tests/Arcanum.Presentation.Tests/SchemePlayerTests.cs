using Arcanum.Presentation.Audio;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Presentation.Tests;

/// <summary>ARC-UI-041: the scheme player against the behaviour recorded in docs/research/audio-evidence.md SN12–SN17 (the
/// original's scheme update, ping and combat music); schemes, hours and draws are invented.</summary>
[TestClass]
public sealed class SchemePlayerTests
{
    private sealed class Draws(params int[] values) : IAudioRandom
    {
        private readonly Queue<int> _values = new(values);
        public List<(int Min, int Max)> Calls { get; } = [];
        public int Next(int min, int max)
        {
            Calls.Add((min, max));
            var value = _values.Count > 0 ? _values.Dequeue() : max;
            Assert.IsTrue(value >= min && value <= max, $"scripted {value} outside {min}..{max}");
            return value;
        }
    }

    private static SchemeSound Ambient(int number, int frequency = 20, int start = 0, int end = 23, int volume = 127, int volumeMax = 127, int left = 63, int right = 63) =>
        new(number, SchemeSoundKind.Ambient, $"sound/a{number}.wav", frequency, start, end, volume, volumeMax, left, right);

    private static SchemeSound Song(int number, SchemeSoundKind kind, int start = 0, int end = 23, int volume = 63) =>
        new(number, kind, $"sound/music/s{number}.mp3", 5, start, end, volume, volume, 63, 63);

    private static SchemeInput Input(SchemeSounds? slot0, SchemeSounds? slot1 = null, bool pending = false, bool combat = false, long starts = 0) =>
        new(slot0, slot1, pending, combat, starts);

    private static readonly Func<long, bool> Playing = _ => true;

    [TestMethod]
    public void A_new_scheme_starts_its_once_songs_at_once_and_loops_on_the_tick_inside_their_window()
    {
        var player = new SchemePlayer(false);
        var scheme = new SchemeSounds(100, [Song(100, SchemeSoundKind.LoopSong, 5, 19), Song(101, SchemeSoundKind.AnchorSong), Song(102, SchemeSoundKind.LoopSong, 20, 23)]);
        var commands = player.Update(Input(scheme), 0, 12, Playing, new Draws());
        CollectionAssert.AreEqual(new[] { SchemeCommandKind.StartSong, SchemeCommandKind.StartSong }, commands.Select(c => c.Kind).ToArray());
        Assert.AreEqual((101, false), (commands[0].Sound!.ListNumber, commands[0].Loop), "the anchor song starts when the scheme loads");
        Assert.AreEqual((100, true, 63), (commands[1].Sound!.ListNumber, commands[1].Loop, commands[1].Volume), "the first tick starts the loop inside its window");
        Assert.AreEqual(0, player.Update(Input(scheme), 250, 12, Playing, new Draws()).Count, "a tick needs more than 250 ms");
        Assert.AreEqual(0, player.Update(Input(scheme), 251, 12, Playing, new Draws()).Count, "a playing loop is left alone");
        var night = player.Update(Input(scheme), 600, 20, Playing, new Draws());
        CollectionAssert.AreEqual(new[] { (SchemeCommandKind.FadeOut, 100, 25), (SchemeCommandKind.StartSong, 102, 0) },
            night.Select(c => (c.Kind, c.Sound!.ListNumber, c.FadeMs)).ToArray(), "outside its window a loop fades in 25 ms; the next one starts");
        Assert.AreEqual(commands[1].Voice, night[0].Voice);
    }

    [TestMethod]
    public void An_ambient_fires_below_its_per_mille_chance_and_stops_its_slot_for_the_tick()
    {
        var player = new SchemePlayer(false);
        var scheme = new SchemeSounds(200, [Ambient(200, frequency: 20, volume: 50, volumeMax: 100, left: 20, right: 40), Ambient(201, frequency: 1000), Song(202, SchemeSoundKind.LoopSong)]);
        var draws = new Draws(19, 7, 3);
        var commands = player.Update(Input(scheme), 0, 12, Playing, draws);
        Assert.AreEqual(1, commands.Count, "nothing after the fired ambient in this slot, not even the loop song");
        Assert.AreEqual((SchemeCommandKind.PlayAmbient, 200, 57, 23), (commands[0].Kind, commands[0].Sound!.ListNumber, commands[0].Volume, commands[0].Balance));
        CollectionAssert.AreEqual(new[] { (0, 999), (0, 49), (0, 19) }, draws.Calls.ToArray(), "chance 0..999, volume and balance below their maximum");

        var next = new Draws(20, 999);
        var later = player.Update(Input(scheme), 300, 12, Playing, next);
        CollectionAssert.AreEqual(new[] { (SchemeCommandKind.PlayAmbient, 201) }, later.Select(c => (c.Kind, c.Sound!.ListNumber)).ToArray(),
            "a draw equal to the frequency does not fire; frequency 1000 always fires");
        var quiet = new SchemePlayer(false);
        var third = quiet.Update(Input(new(300, [Ambient(300, frequency: 20), Song(301, SchemeSoundKind.LoopSong)])), 0, 12, Playing, new Draws(999));
        Assert.AreEqual((SchemeCommandKind.StartSong, 301), (third.Single().Kind, third.Single().Sound!.ListNumber), "with no ambient firing the loop song is reached");
    }

    [TestMethod]
    public void Each_slot_fires_its_own_ambient_and_equal_bounds_draw_nothing()
    {
        var player = new SchemePlayer(false);
        var draws = new Draws(0, 0);
        var commands = player.Update(Input(new(1, [Ambient(1, volume: 80, volumeMax: 80, left: 10, right: 10)]), new(2, [Ambient(2)])), 0, 3, Playing, draws);
        CollectionAssert.AreEqual(new[] { 1, 2 }, commands.Select(c => c.Sound!.ListNumber).ToArray());
        Assert.AreEqual((80, 10), (commands[0].Volume, commands[0].Balance));
        Assert.AreEqual(2, draws.Calls.Count, "only the chance is drawn when min equals max");
    }

    [TestMethod]
    public void A_reversed_window_never_plays_in_the_original_and_runs_past_midnight_in_steamwoven()
    {
        var sound = Song(1, SchemeSoundKind.LoopSong, 19, 0);
        var original = new SchemePlayer(false);
        foreach (var hour in new[] { 0, 12, 19, 23 }) Assert.IsFalse(original.InWindow(sound, hour), $"{hour}");
        var steamwoven = new SchemePlayer(true);
        Assert.IsTrue(steamwoven.InWindow(sound, 19) && steamwoven.InWindow(sound, 23) && steamwoven.InWindow(sound, 0));
        Assert.IsFalse(steamwoven.InWindow(sound, 1) || steamwoven.InWindow(sound, 12));
        Assert.IsTrue(original.InWindow(Song(2, SchemeSoundKind.LoopSong, 5, 19), 5) && original.InWindow(Song(2, SchemeSoundKind.LoopSong, 5, 19), 19), "inclusive");
    }

    [TestMethod]
    public void A_changed_slot_fades_its_loops_and_leaves_its_once_songs_playing()
    {
        var player = new SchemePlayer(false);
        var first = new SchemeSounds(1, [Song(10, SchemeSoundKind.LoopSong), Song(11, SchemeSoundKind.AnchorSong)]);
        player.Update(Input(first), 0, 12, Playing, new Draws());
        Assert.AreEqual(2, player.TrackedVoices);
        var changed = player.Update(Input(new SchemeSounds(2, [Ambient(20, frequency: 0)])), 10, 12, Playing, new Draws());
        CollectionAssert.AreEqual(new[] { (SchemeCommandKind.FadeOut, 10) }, changed.Select(c => (c.Kind, c.Sound!.ListNumber)).ToArray(), "only the loop fades");
        Assert.AreEqual(0, player.TrackedVoices);
        Assert.AreEqual(0, player.Update(Input(new(1, [Song(30, SchemeSoundKind.LoopSong)])), 20, 12, Playing, new Draws()).Count,
            "an ambient-only scheme has nothing to fade, and the new loop waits for the next tick");
        Assert.AreEqual((SchemeCommandKind.StartSong, 30), (player.Update(Input(new(1, [Song(30, SchemeSoundKind.LoopSong)])), 300, 12, Playing, new Draws()).Single().Kind, 30));
    }

    [TestMethod]
    public void A_finished_transition_song_reports_the_restore_only_while_one_is_pending()
    {
        var player = new SchemePlayer(false);
        var scheme = new SchemeSounds(1, [Song(10, SchemeSoundKind.TransitionSong)]);
        var start = player.Update(Input(scheme, pending: true), 0, 12, Playing, new Draws());
        var voice = start.Single().Voice;
        Assert.AreEqual(0, player.Update(Input(scheme, pending: true), 300, 12, _ => true, new Draws()).Count, "still playing");
        Assert.AreEqual(0, player.Update(Input(scheme, pending: false), 600, 12, _ => false, new Draws()).Count, "no restore pending");
        var finished = player.Update(Input(scheme, pending: true), 900, 12, v => v != voice, new Draws());
        Assert.AreEqual((SchemeCommandKind.TransitionFinished, voice), (finished.Single().Kind, finished.Single().Voice));
        Assert.AreEqual(0, player.Update(Input(scheme, pending: true), 1200, 12, _ => false, new Draws()).Count, "reported once");
    }

    [TestMethod]
    public void Combat_music_starts_when_the_counter_moves_and_stops_when_the_flag_clears()
    {
        var player = new SchemePlayer(false, combatStarts: 2);
        Assert.AreEqual(0, player.Update(Input(null, combat: true, starts: 2), 0, 12, Playing, new Draws()).Count, "a loaded combat flag replays no stinger");
        var start = player.Update(Input(null, combat: true, starts: 3), 10, 12, Playing, new Draws(4));
        Assert.AreEqual((SchemeCommandKind.StartCombat, 4), (start.Single().Kind, start.Single().Stinger));
        Assert.AreEqual(0, player.Update(Input(null, combat: true, starts: 3), 20, 12, Playing, new Draws()).Count);
        Assert.AreEqual(SchemeCommandKind.StopCombat, player.Update(Input(null, starts: 3), 30, 12, Playing, new Draws()).Single().Kind);
        Assert.AreEqual(0, player.Update(Input(null, starts: 3), 40, 12, Playing, new Draws()).Count);
        player.Reset(7);
        Assert.AreEqual(0, player.Update(Input(null, combat: true, starts: 7), 50, 12, Playing, new Draws()).Count, "a reset adopts the counter");
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => player.Update(Input(null), 60, 24, Playing, new Draws()));
    }
}
