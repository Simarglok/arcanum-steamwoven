namespace Arcanum.Data.Area;

/// <summary>
/// ARC-RENDER-3362: the hash of the map's identity keys. These keys are looked up many thousands of times a frame, and the generated record hash
/// ran the randomized string hash over every 64-character digest of the key each time. The hash now takes the numbers that already tell keys apart
/// and, of a digest, only its length and four characters at each end. Equality is unchanged (every field), so equal keys give one value; two
/// different keys that share a value only cost one more comparison.
/// </summary>
internal static class AreaKeyHash
{
    private const int Edge = 4;

    internal static int Sample(string? text)
    {
        if (text is null) return 0;
        var length = text.Length;
        if (length < 2 * Edge) return string.GetHashCode(text, StringComparison.Ordinal);
        return HashCode.Combine(length, text[0] | text[1] << 16, text[2] | text[3] << 16,
            text[length - 4] | text[length - 3] << 16, text[length - 2] | text[length - 1] << 16);
    }
}
