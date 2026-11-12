using Arcanum.Data.Content;
using Arcanum.Data.Messages;
using Arcanum.Simulation;

namespace Arcanum.World;

/// <summary>
/// ARC-RENDER-1170: the original's movie table <c>movies\movies.mes</c> (docs/research/bink-movies.md section 1): numbered rows that
/// each name one <c>.bik</c> file of the movies folder; number 0 is reserved ("the empty non-movie to scripts") and a number with no row
/// plays nothing (0x40DE70). The file is found through the content stack like any resource (0x52F410); a loose file is played from where
/// it lies, an archived one is copied to a cache folder first, as the original does with its TIG cache. Godot-free.
/// </summary>
public sealed class AreaMovieTable
{
    public const string TablePath = "movies/movies.mes";
    public const string Folder = "movies/";
    /// <summary>A name longer than this is refused (a path the content stack would refuse anyway).</summary>
    public const int MaxName = 64;
    private readonly Func<int, string?> _names;

    private AreaMovieTable(Func<int, string?> names, bool loaded, string detail) { _names = names; Loaded = loaded; Detail = detail; }

    /// <summary>True when the table was read.</summary>
    public bool Loaded { get; }
    public string Detail { get; }

    /// <summary>No table (no game folder or a failed read): no movie plays.</summary>
    public static AreaMovieTable None(string reason) => new(_ => null, false, reason ?? throw new ArgumentNullException(nameof(reason)));

    /// <summary>A table over a message lookup (tests).</summary>
    public static AreaMovieTable From(Func<int, string?> messages, string detail) =>
        new(messages ?? throw new ArgumentNullException(nameof(messages)), true, detail ?? throw new ArgumentNullException(nameof(detail)));

    /// <summary>Reads <c>movies\movies.mes</c> from the stack's module-load winner; any failure gives no table, with the reason.</summary>
    public static AreaMovieTable Load(ContentStack stack)
    {
        ArgumentNullException.ThrowIfNull(stack);
        if (AreaStartingHero.Table(stack, TablePath, ContentLoadPhase.ModuleLoaded, required: true, out var detail) is not { } table)
            return None($"{TablePath} not read ({detail})");
        return new(number => table.Find(number) is { } message
            && MesText.Decode(message, AreaHoverIndex.TextProfile) is { Status: MesDecodeStatus.Decoded, Text: { } text } ? text : null,
            true, $"{TablePath} read");
    }

    /// <summary>The file name of movie <paramref name="number"/>, or null for 0, a number with no row and a name that is not a plain file name.</summary>
    public string? Name(int number)
    {
        if (number <= 0 || _names(number) is not { } text) return null;
        // The row holds the file name; a trailing comment is not part of the message, and nothing but a plain name is accepted.
        var name = text.Trim();
        return name.Length is > 0 and <= MaxName && name.EndsWith(".bik", StringComparison.OrdinalIgnoreCase)
            && name.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '.') && !name.Contains("..", StringComparison.Ordinal) ? name : null;
    }

    /// <summary>The local path of movie <paramref name="number"/>: the loose file of the content stack's module-load winner, or an archived
    /// entry copied once to <paramref name="cacheFolder"/>. Null when the number has no movie, the file is not in the stack or the copy fails.</summary>
    [OriginalFunction(0x40DE70, OriginalFunctionScope.Partial)]
    public string? Path(ContentStack stack, int number, string cacheFolder)
    {
        ArgumentNullException.ThrowIfNull(stack);
        ArgumentNullException.ThrowIfNull(cacheFolder);
        if (Name(number) is not { } name) return null;
        return PathOf(stack, Folder + name, cacheFolder);
    }

    /// <summary>The local path of the movie at the virtual path <paramref name="virtualPath"/> (<c>movies/&lt;name&gt;</c>); see <see cref="Path"/>.</summary>
    public static string? PathOf(ContentStack stack, string virtualPath, string cacheFolder)
    {
        ArgumentNullException.ThrowIfNull(stack);
        if (ContentPhase.Candidate(stack, virtualPath, ContentLoadPhase.ModuleLoaded) is not { } winner) return null;
        if (winner.Entry is null) return winner.FilePath is { } loose && File.Exists(loose) ? loose : null;
        try
        {
            var target = System.IO.Path.Combine(cacheFolder, System.IO.Path.GetFileName(virtualPath));
            if (File.Exists(target) && new FileInfo(target).Length == winner.Length) return target;
            var bytes = stack.Read(winner);
            if (bytes.Status != ContentReadStatus.Read || bytes.Bytes is null) return null;
            Directory.CreateDirectory(cacheFolder);
            File.WriteAllBytes(target, bytes.Bytes);
            return target;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return null; }
    }
}
