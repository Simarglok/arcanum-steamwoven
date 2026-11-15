using System.Text.RegularExpressions;

namespace Arcanum.TerrainViewer.Core;

/// <summary>
/// ARC-DEV-2189 (owner QA epic #2185): the owner check a bug report belongs to. The local QA page (<c>./arcanum qa</c>) writes the id of
/// the step the owner has open, <c>ARC-TYPE-NNN#k</c> (the format of ARC-DEV-2186), as one line into
/// <c>local/qa/active-check.txt</c> of the main checkout (git-ignored); Ctrl+B reads it, the report window shows it (the owner may edit
/// or clear it) and <c>report.md</c> gets the one machine-readable line <c>Check: ARC-TYPE-NNN#k</c> that the page reads back to link the
/// report to the step. Reading is best effort: a missing, stale, oversized, unreadable or malformed file is "no check" and never blocks
/// or fails a report. No Godot.
/// </summary>
public static class BugReportCheck
{
    /// <summary>The file the QA page writes, inside <c>local/qa</c>.</summary>
    public const string FileName = "active-check.txt";
    /// <summary>The prefix of the line in <c>report.md</c>.</summary>
    public const string LinePrefix = "Check: ";
    /// <summary>The environment variable that names the file directly (tests, an automated run, an unusual layout).</summary>
    public const string FileVariable = "ARC_QA_ACTIVE_CHECK";
    private const long MaxFileBytes = 256;
    private const int MaxLevels = 8;
    /// <summary>A file older than this is a forgotten check of an earlier day, not the one the owner is on.</summary>
    public static readonly TimeSpan MaxAge = TimeSpan.FromHours(12);
    private static readonly Regex StepId = new(@"^ARC-[A-Z]+-[0-9]+#[1-9][0-9]*$", RegexOptions.CultureInvariant);

    /// <summary>The trimmed step id when <paramref name="text"/> has the shape <c>ARC-TYPE-NNN#k</c>, else null.</summary>
    public static string? Normalize(string? text)
    {
        var trimmed = text?.Trim();
        return trimmed is { Length: > 0 and <= 64 } && StepId.IsMatch(trimmed) ? trimmed : null;
    }

    /// <summary>The step id the file at <paramref name="file"/> holds (its first line), or null when the file is missing, older than
    /// <see cref="MaxAge"/> at <paramref name="nowUtc"/>, larger than a step id could be, unreadable or not a step id.</summary>
    public static string? ReadActive(string? file, DateTime nowUtc)
    {
        if (string.IsNullOrEmpty(file)) return null;
        try
        {
            var info = new FileInfo(file);
            if (!info.Exists || info.Length > MaxFileBytes || nowUtc - info.LastWriteTimeUtc > MaxAge) return null;
            using var reader = new StreamReader(file);
            return Normalize(reader.ReadLine());
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>The active-check file: the file <see cref="FileVariable"/> names when it is set, else (unless
    /// <paramref name="automated"/>: an automated run reads only the variable) the first <c>local/qa/active-check.txt</c> found above one of
    /// <paramref name="startFolders"/> (the launch folder of a packaged game, the project folder of a run from a checkout or a worktree
    /// of it), looking up at most eight levels. Null when there is none.</summary>
    public static string? FindActiveFile(IEnumerable<string?> startFolders, bool automated, string? variableValue)
    {
        ArgumentNullException.ThrowIfNull(startFolders);
        if (!string.IsNullOrEmpty(variableValue)) return variableValue;
        if (automated) return null;
        foreach (var start in startFolders)
        {
            if (string.IsNullOrEmpty(start)) continue;
            try
            {
                var folder = Path.GetFullPath(start);
                for (var level = 0; level <= MaxLevels && !string.IsNullOrEmpty(folder); level++, folder = Path.GetDirectoryName(folder))
                {
                    var candidate = Path.Combine(folder, "local", "qa", FileName);
                    if (File.Exists(candidate)) return candidate;
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                // an unusable start folder is skipped
            }
        }
        return null;
    }

    /// <summary>The step id of the line <c>Check: ARC-TYPE-NNN#k</c> in the head of a <c>report.md</c> (the lines before its first
    /// <c>##</c> heading, so the player's own text never counts), or null.</summary>
    public static string? ReadReportLine(string reportMarkdown)
    {
        ArgumentNullException.ThrowIfNull(reportMarkdown);
        foreach (var line in reportMarkdown.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            if (line.StartsWith("## ", StringComparison.Ordinal)) return null;
            if (line.StartsWith(LinePrefix, StringComparison.Ordinal)) return Normalize(line[LinePrefix.Length..]);
        }
        return null;
    }
}
