using System.Text;

namespace Arcanum.Scripting.Dialog;

/// <summary>
/// Explicit text code page. Windows-1252 is the data-observed encoding of the selected English
/// installation (0x85 ellipsis, Latin-1 letters); it is not a universal claim for every localization.
/// </summary>
public enum DialogTextEncoding
{
    Windows1252,
}

public enum DialogTextFailureKind
{
    None,
    ControlCharacter,
    UndefinedByte,
    UnterminatedToken,
    UnsupportedToken,
    TextLimitExceeded,
}

/// <summary>Host-supplied display names. Neither is read from original data by this runtime.</summary>
public sealed record DialogSpeakerNames(string PcName, string NpcName);

/// <summary>
/// A generated-text marker: the record text is exactly or begins with one ASCII letter and ':'.
/// Generated phrases live in separate generic message resources that this runtime does not load.
/// </summary>
public readonly record struct DialogGeneratedMarker(char Letter);

internal static class DialogText
{
    // Windows-1252 0x80..0x9F. Zero marks the five undefined code points.
    private static readonly char[] HighControlRange =
    [
        '€', '\0', '‚', 'ƒ', '„', '…', '†', '‡',
        'ˆ', '‰', 'Š', '‹', 'Œ', '\0', 'Ž', '\0',
        '\0', '‘', '’', '“', '”', '•', '–', '—',
        '˜', '™', 'š', '›', 'œ', '\0', 'ž', 'Ÿ',
    ];

    internal static DialogTextFailureKind Decode(IReadOnlyList<byte> bytes, DialogTextEncoding encoding, int maxBytes, out string text)
    {
        text = string.Empty;
        if (encoding != DialogTextEncoding.Windows1252)
        {
            throw new ArgumentOutOfRangeException(nameof(encoding));
        }
        if (bytes.Count > maxBytes)
        {
            return DialogTextFailureKind.TextLimitExceeded;
        }
        var builder = new StringBuilder(bytes.Count);
        for (var index = 0; index < bytes.Count; index++)
        {
            var value = bytes[index];
            if (value == (byte)'\r' && index + 1 < bytes.Count && bytes[index + 1] == (byte)'\n')
            {
                continue;
            }
            if (value is < 0x20 and not (byte)'\t' and not (byte)'\n' || value == 0x7F)
            {
                return DialogTextFailureKind.ControlCharacter;
            }
            if (value is >= 0x80 and <= 0x9F)
            {
                var mapped = HighControlRange[value - 0x80];
                if (mapped == '\0')
                {
                    return DialogTextFailureKind.UndefinedByte;
                }
                builder.Append(mapped);
                continue;
            }
            builder.Append((char)value);
        }
        text = builder.ToString();
        return DialogTextFailureKind.None;
    }

    /// <summary>A marker is one ASCII letter then ':' at the very start; exact means nothing follows.</summary>
    internal static DialogGeneratedMarker? Marker(string text, out bool exact)
    {
        exact = false;
        if (text.Length < 2 || text[1] != ':' || !char.IsAsciiLetter(text[0]))
        {
            return null;
        }
        exact = text.Length == 2;
        return new DialogGeneratedMarker(char.ToLowerInvariant(text[0]));
    }

    internal static bool IsBlank(string text)
    {
        foreach (var character in text)
        {
            if (character is not (' ' or '\t' or '\n'))
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>Validates @pcname@/@npcname@ tokens (ASCII case-insensitive) without substituting.</summary>
    internal static DialogTextFailureKind ValidateTokens(string text)
    {
        var index = 0;
        while (true)
        {
            var start = text.IndexOf('@', index);
            if (start < 0)
            {
                return DialogTextFailureKind.None;
            }
            var end = text.IndexOf('@', start + 1);
            if (end < 0)
            {
                return DialogTextFailureKind.UnterminatedToken;
            }
            var token = text.AsSpan(start + 1, end - start - 1);
            if (!token.Equals("pcname", StringComparison.OrdinalIgnoreCase) &&
                !token.Equals("npcname", StringComparison.OrdinalIgnoreCase))
            {
                return DialogTextFailureKind.UnsupportedToken;
            }
            index = end + 1;
        }
    }

    /// <summary>
    /// ARC-WORLD-058: the original's name substitution of a generated line (0x416B00): <c>@pcname@</c> and <c>@npcname@</c> (ASCII
    /// case-insensitive) become the names and any other <c>@…@</c> token is dropped. An <c>@</c> without its closing one is
    /// kept with the rest of the text (not in the installed files).
    /// </summary>
    internal static string SubstituteGenerated(string text, DialogSpeakerNames names)
    {
        var builder = new StringBuilder(text.Length);
        var index = 0;
        while (true)
        {
            var start = text.IndexOf('@', index);
            var end = start < 0 ? -1 : text.IndexOf('@', start + 1);
            if (end < 0)
            {
                builder.Append(text, index, text.Length - index);
                return builder.ToString();
            }
            builder.Append(text, index, start - index);
            var token = text.AsSpan(start + 1, end - start - 1);
            if (token.Equals("pcname", StringComparison.OrdinalIgnoreCase)) builder.Append(names.PcName);
            else if (token.Equals("npcname", StringComparison.OrdinalIgnoreCase)) builder.Append(names.NpcName);
            index = end + 1;
        }
    }

    internal static string Substitute(string text, DialogSpeakerNames names)
    {
        var builder = new StringBuilder(text.Length);
        var index = 0;
        while (true)
        {
            var start = text.IndexOf('@', index);
            if (start < 0)
            {
                builder.Append(text, index, text.Length - index);
                return builder.ToString();
            }
            var end = text.IndexOf('@', start + 1);
            builder.Append(text, index, start - index);
            var token = text.AsSpan(start + 1, end - start - 1);
            builder.Append(token.Equals("pcname", StringComparison.OrdinalIgnoreCase) ? names.PcName : names.NpcName);
            index = end + 1;
        }
    }
}
