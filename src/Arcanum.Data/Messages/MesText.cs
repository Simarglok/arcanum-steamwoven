namespace Arcanum.Data.Messages;

/// <summary>How the caller declares MES bytes are encoded. The original stores bytes and draws
/// them with a font; it has no encoding contract, so a profile is always the caller's explicit
/// choice and never guessed from content. The installed English data is pure ASCII.</summary>
public enum MesTextProfile
{
    /// <summary>Bytes 0x00..0x7F only.</summary>
    Ascii,
    /// <summary>Windows code page 1252 (Western European); 0x81, 0x8D, 0x8F, 0x90 and 0x9D are undefined.</summary>
    Windows1252,
    /// <summary>Windows code page 1251 (Cyrillic); 0x98 is undefined.</summary>
    Windows1251,
}

public enum MesDecodeStatus { Decoded, UndefinedByte }

/// <param name="Offset">The first byte the profile does not define, for <see cref="MesDecodeStatus.UndefinedByte"/>.</param>
public sealed record MesDecodeResult(MesDecodeStatus Status, string? Text, int? Offset = null);

/// <summary>Strict decoding of stored MES bytes under a declared profile. A byte the profile
/// does not define fails; nothing is replaced, so the stored bytes stay the only source.</summary>
public static class MesText
{
    // Upper halves (0x80..0xFF) of the two Windows code pages; '\0' marks an undefined byte.
    private const string Upper1252 =
        "€\0‚ƒ„…†‡ˆ‰Š‹Œ\0Ž\0" +
        "\0‘’“”•–—˜™š›œ\0žŸ" +
        " ¡¢£¤¥¦§¨©ª«¬­®¯" +
        "°±²³´µ¶·¸¹º»¼½¾¿" +
        "ÀÁÂÃÄÅÆÇÈÉÊËÌÍÎÏ" +
        "ÐÑÒÓÔÕÖ×ØÙÚÛÜÝÞß" +
        "àáâãäåæçèéêëìíîï" +
        "ðñòóôõö÷øùúûüýþÿ";
    private const string Upper1251 =
        "ЂЃ‚ѓ„…†‡€‰Љ‹ЊЌЋЏ" +
        "ђ‘’“”•–—\0™љ›њќћџ" +
        " ЎўЈ¤Ґ¦§Ё©Є«¬­®Ї" +
        "°±Ііґµ¶·ё№є»јЅѕї" +
        "АБВГДЕЖЗИЙКЛМНОП" +
        "РСТУФХЦЧШЩЪЫЬЭЮЯ" +
        "абвгдежзийклмноп" +
        "рстуфхцчшщъыьэюя";

    public static MesDecodeResult Decode(ReadOnlySpan<byte> bytes, MesTextProfile profile)
    {
        var upper = profile switch
        {
            MesTextProfile.Ascii => null,
            MesTextProfile.Windows1252 => Upper1252,
            MesTextProfile.Windows1251 => Upper1251,
            _ => throw new ArgumentOutOfRangeException(nameof(profile)),
        };
        var chars = new char[bytes.Length];
        for (var i = 0; i < bytes.Length; i++)
        {
            var b = bytes[i];
            if (b < 0x80) { chars[i] = (char)b; continue; }
            if (upper is null || upper[b - 0x80] == '\0') return new(MesDecodeStatus.UndefinedByte, null, i);
            chars[i] = upper[b - 0x80];
        }
        return new(MesDecodeStatus.Decoded, new string(chars));
    }

    /// <summary>The bytes of <paramref name="text"/> under <paramref name="profile"/>, the strict inverse of <see cref="Decode(ReadOnlySpan{byte}, MesTextProfile)"/>
    /// (ARC-UI-033: text the player types, stored as the original stores it); null when a character has no byte in the profile.</summary>
    public static byte[]? Encode(string text, MesTextProfile profile)
    {
        ArgumentNullException.ThrowIfNull(text);
        var upper = profile switch
        {
            MesTextProfile.Ascii => null,
            MesTextProfile.Windows1252 => Upper1252,
            MesTextProfile.Windows1251 => Upper1251,
            _ => throw new ArgumentOutOfRangeException(nameof(profile)),
        };
        var bytes = new byte[text.Length];
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c < 0x80) { bytes[i] = (byte)c; continue; }
            var at = upper is null ? -1 : upper.IndexOf(c, StringComparison.Ordinal);
            if (at < 0) return null;
            bytes[i] = (byte)(0x80 + at);
        }
        return bytes;
    }

    public static MesDecodeResult Decode(MesMessage message, MesTextProfile profile)
    {
        ArgumentNullException.ThrowIfNull(message);
        return Decode(message.Bytes.Span, profile);
    }
}
