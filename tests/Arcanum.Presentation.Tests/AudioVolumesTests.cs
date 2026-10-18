using Arcanum.Presentation.Audio;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Presentation.Tests;

/// <summary>ARC-UI-040: the original's channel volumes and volume arithmetic (docs/research/audio-evidence.md SN6, SN8,
/// SN16); expected values are written from the recorded formulas.</summary>
[TestClass]
public sealed class AudioVolumesTests
{
    [TestMethod]
    public void Channel_volumes_follow_the_original_integer_steps()
    {
        Assert.AreEqual(50, AudioVolumeRules.ChannelVolume(AudioChannel.Effects, 5), "5 × 127 / 10 = 63, × 80 / 100 = 50");
        Assert.AreEqual(101, AudioVolumeRules.ChannelVolume(AudioChannel.Music, 10), "127 × 80 / 100 = 101");
        Assert.AreEqual(63, AudioVolumeRules.ChannelVolume(AudioChannel.Voice, 5));
        Assert.AreEqual(127, AudioVolumeRules.ChannelVolume(AudioChannel.Voice, 10));
        Assert.AreEqual(0, AudioVolumeRules.ChannelVolume(AudioChannel.Effects, 0));
        Assert.AreEqual(9, AudioVolumeRules.ChannelVolume(AudioChannel.Effects, 1), "1 × 127 / 10 = 12, × 80 / 100 = 9");
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => AudioVolumeRules.ChannelVolume(AudioChannel.Voice, 11));
        Assert.AreEqual(63, AudioVolumeRules.ChannelVolume(AudioVolumes.Defaults, AudioChannel.Voice));
    }

    [TestMethod]
    public void Start_and_recalculated_volumes_use_the_two_original_scales()
    {
        Assert.AreEqual(63, AudioVolumeRules.StartVolume(127, 50), "a full interface sound at the default effects volume");
        Assert.AreEqual(128, AudioVolumeRules.StartVolume(127, 101), "a fresh full effect at setting 10 computes 128");
        Assert.AreEqual(101, AudioVolumeRules.AdjustedVolume(127, 101), "about 21 % quieter after the first recalculation");
        Assert.AreEqual(1.0, AudioVolumeRules.Amplitude(128));
        Assert.AreEqual(0.0, AudioVolumeRules.Amplitude(-3));
        Assert.AreEqual(63 / 127.0, AudioVolumeRules.Amplitude(63));
    }

    [TestMethod]
    public void Settings_are_validated_and_replaced_per_channel()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new AudioVolumes(11, 5, 5));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new AudioVolumes(5, -1, 5));
        var changed = AudioVolumes.Defaults.With(AudioChannel.Music, 0);
        Assert.AreEqual((5, 5, 0), (changed.Effects, changed.Voice, changed.Music));
        Assert.AreEqual(0, changed[AudioChannel.Music]);
        Assert.AreEqual(new AudioVolumes(5, 5, 5), AudioVolumes.Defaults);
    }

    [TestMethod]
    public void The_preference_text_round_trips_and_anything_else_reads_as_null()
    {
        var text = AudioVolumesCodec.Serialize(new AudioVolumes(3, 10, 0));
        Assert.AreEqual("{\"version\":1,\"effects\":3,\"voice\":10,\"music\":0}", text);
        Assert.AreEqual(new AudioVolumes(3, 10, 0), AudioVolumesCodec.Deserialize(text));
        foreach (var bad in new[] { "", "[]", "{}", "{\"version\":2,\"effects\":3,\"voice\":10,\"music\":0}", "{\"version\":1,\"effects\":11,\"voice\":10,\"music\":0}",
                     "{\"version\":1,\"effects\":3,\"voice\":10}", "{\"version\":1,\"effects\":3,\"voice\":10,\"music\":0,\"extra\":1}",
                     "{\"version\":1,\"effects\":3.5,\"voice\":10,\"music\":0}", "{\"version\":1,\"effects\":\"3\",\"voice\":10,\"music\":0}", "not json" })
            Assert.IsNull(AudioVolumesCodec.Deserialize(bad), bad);
    }
}
