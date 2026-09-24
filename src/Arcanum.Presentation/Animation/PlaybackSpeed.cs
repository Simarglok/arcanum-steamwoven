namespace Arcanum.Presentation.Animation;

/// <summary>Supported inspector playback-rate multipliers.</summary>
public enum PlaybackSpeed
{
    Normal,
    Quarter,
    Half,
    Double,
}

/// <summary>Maps the closed playback-speed choices to their exact multipliers.</summary>
public static class PlaybackSpeedExtensions
{
    /// <summary>Gets the multiplier represented by <paramref name="speed" />.</summary>
    public static double ToMultiplier(this PlaybackSpeed speed) => speed switch
    {
        PlaybackSpeed.Quarter => 0.25d,
        PlaybackSpeed.Half => 0.5d,
        PlaybackSpeed.Normal => 1d,
        PlaybackSpeed.Double => 2d,
        _ => throw new ArgumentOutOfRangeException(nameof(speed), speed, "Unsupported playback speed."),
    };
}
