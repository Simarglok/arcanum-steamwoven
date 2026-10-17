using System.Text;
using System.Text.RegularExpressions;
using Arcanum.Data.Messages;

namespace Arcanum.Data.Audio;

/// <summary>The size class of a sound source (docs/research/audio-evidence.md SN5): scenery with flag 0x40 is small, 0x80
/// medium, 0x100 extra-large; every other object is large.</summary>
public enum SoundSourceSize { Small = 0, Medium = 1, Large = 2, ExtraLarge = 3 }

/// <summary>Radii in projected screen units and the volume percentage applied after attenuation.</summary>
public sealed record SoundSizeParameters(long MinRadius, long MaxRadius, int MaxVolumePercent)
{
    /// <summary>The original's range in tiles: the low 32 bits of the max radius divided by 40, truncated.</summary>
    public int RangeTiles => unchecked((int)MaxRadius) / 40;
}

/// <summary>
/// The positional parameters the original's sound init builds from <c>sound/soundparams.mes</c> (docs/research/audio-evidence.md
/// SN3, checked in the 1.0.7.4 executable): built-in large min/max radius 150/800, pan distances 150/400 and large volume
/// 100, overridden by keys 1–4; keys 10–12, 20–22 and 30–32 set small, medium and extra-large min radius, max radius and
/// volume percentage, with defaults 50/150/40, 50/400/70 and 50/1500/100 when a key is absent. Without the file, the
/// three non-large sizes stay at zero, which makes them silent.
/// </summary>
public sealed record SoundPositionalProfile(SoundSizeParameters Small, SoundSizeParameters Medium, SoundSizeParameters Large,
    SoundSizeParameters ExtraLarge, long MinPanDistance, long MaxPanDistance, bool FromFile)
{
    public SoundSizeParameters this[SoundSourceSize size] => size switch
    {
        SoundSourceSize.Small => Small,
        SoundSourceSize.Medium => Medium,
        SoundSourceSize.Large => Large,
        SoundSourceSize.ExtraLarge => ExtraLarge,
        _ => throw new ArgumentOutOfRangeException(nameof(size)),
    };

    private static readonly Regex Plain = new(@"^[+-]?[0-9]+$", RegexOptions.CultureInvariant);

    /// <summary>What the original has when <c>soundparams.mes</c> does not load.</summary>
    public static SoundPositionalProfile WithoutFile { get; } = new(new(0, 0, 0), new(0, 0, 0), new(150, 800, 100), new(0, 0, 0), 150, 400, false);

    /// <summary>Builds the profile from the parsed file. Values are read like C <c>atoi</c>, as the original does; one that is
    /// not a plain whole number (optional sign and digits, surrounding spaces allowed) is still read that way and reported,
    /// and one outside int32 (undefined for <c>atoi</c>) is reported and its key treated as absent.</summary>
    internal static SoundPositionalProfile FromMes(MesFile file, string key, List<SoundIssue> issues)
    {
        ArgumentNullException.ThrowIfNull(file); ArgumentNullException.ThrowIfNull(issues);
        int? Value(int number)
        {
            if (file.Find(number) is not { } message) return null;
            var text = Encoding.Latin1.GetString(message.Bytes.Span).Trim();
            var value = SoundSchemeParser.Atoi(text, out var overflow);
            if (overflow || !Plain.IsMatch(text)) issues.Add(new(SoundIssueCode.PositionalParameterMalformed, key, number));
            return overflow ? null : value;
        }
        SoundSizeParameters Size(int min, int max, int volume, (int, int, int) defaults) =>
            new(Value(min) ?? defaults.Item1, Value(max) ?? defaults.Item2, Value(volume) ?? defaults.Item3);
        return new(
            Size(10, 11, 12, (50, 150, 40)),
            Size(20, 21, 22, (50, 400, 70)),
            new(Value(1) ?? 150, Value(2) ?? 800, 100),
            Size(30, 31, 32, (50, 1500, 100)),
            Value(3) ?? 150, Value(4) ?? 400, true);
    }
}
