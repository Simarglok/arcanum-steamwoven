using System.Globalization;

namespace Arcanum.TerrainViewer.Core;

/// <summary>
/// ARC-DEV-047: which build is running. <c>./arcanum package</c> stamps the viewer assembly with the packaged commit and its
/// date (assembly metadata <see cref="CommitKey"/> and <see cref="DateKey"/>, from the MSBuild properties of the same names);
/// a source run or a developer export carries neither and is not a packaged build. The launcher and the main menu show
/// <see cref="Text"/>, so a player sees which main a build came from.
/// </summary>
public sealed record ViewerBuildInfo(string? Commit, string? Date)
{
    public const string CommitKey = "ArcanumPackageCommit";
    public const string DateKey = "ArcanumPackageDate";
    /// <summary>Digits of the commit shown to the player (git's usual short form).</summary>
    public const int ShortCommitLength = 10;

    public static ViewerBuildInfo Source { get; } = new(null, null);

    public bool IsPackage => Commit is not null;

    /// <summary>The build from assembly metadata pairs; a commit that is not 7–64 hex digits, or a date that is not
    /// YYYY-MM-DD, is ignored, so a broken stamp shows as an unpackaged build instead of as text from outside.</summary>
    public static ViewerBuildInfo FromMetadata(IEnumerable<KeyValuePair<string, string?>> metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        string? commit = null, date = null;
        foreach (var (key, value) in metadata)
        {
            if (key == CommitKey && value is { Length: >= 7 and <= 64 } && value.All(Uri.IsHexDigit)) commit = value.ToLowerInvariant();
            else if (key == DateKey && DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)) date = value;
        }
        return commit is null ? Source : new(commit, date);
    }

    /// <summary>The line the launcher and the main menu show.</summary>
    public string Text => Commit is null
        ? "Arcanum Steamwoven · not a packaged build"
        : $"Arcanum Steamwoven alpha · main {Commit[..Math.Min(ShortCommitLength, Commit.Length)]}" + (Date is null ? "" : $" · {Date}");
}
