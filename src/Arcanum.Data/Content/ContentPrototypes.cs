using System.Collections.ObjectModel;
using System.Globalization;
using Arcanum.Data.Characters;

namespace Arcanum.Data.Content;

/// <summary>
/// ARC-DATA-026: the prototype files the original reads, over a <see cref="ContentStack"/>; the one prototype source of
/// the universal readers (map catalogue, whole-map plans, map transitions, pre-made hero, combat fields). The pinned Crash
/// Site exterior and cave profiles still take their own prototype folder (a later task). The original lists and reads every
/// <c>proto\*.pro</c> once, while the game initialises and before it mounts a module, and keeps them for the process: the
/// map subsystem's Proto entry has an init function and no module-load function (checked in the 1.0.7.4 executable,
/// docs/research/original-verification-ledger.md "Prototype phase"). So a prototype comes from the base sources only —
/// <c>tig.dat</c>, the game folder, <c>arcanum*.dat</c> and <c>data/</c> — and of those from the first in search order;
/// a copy in the module archive, a module patch or the module folder is never read
/// (<see cref="IgnoredModuleKeys"/>). Addressable files are <c>proto/NNNNNN - label.pro</c> directly under
/// <c>proto/</c>; other <c>.pro</c> names are listed in <see cref="UnrecognisedKeys"/> (the original loads them too,
/// so they must not vanish silently), and a number held by two names is <see cref="PrototypeFileStatus.Ambiguous"/>.
/// The stack stays owned by the caller and must outlive this index.
/// </summary>
public sealed class ContentPrototypes
{
    /// <summary>The largest prototype file <see cref="Read"/> returns; the largest installed one is far below it.</summary>
    public const int MaxPrototypeBytes = PremadeHeroReader.MaxPrototypeBytes;
    private const string Folder = "proto/";
    private readonly ContentStack _stack;
    private readonly IReadOnlyDictionary<int, (string Key, ContentCandidate Candidate)> _files;
    private readonly IReadOnlySet<int> _ambiguous;

    private ContentPrototypes(ContentStack stack, Dictionary<int, (string, ContentCandidate)> files, SortedSet<int> ambiguous,
        IReadOnlyList<string> ambiguousKeys, IReadOnlyList<string> unrecognised, IReadOnlyList<string> ignored)
    {
        _stack = stack; _files = new ReadOnlyDictionary<int, (string, ContentCandidate)>(files); _ambiguous = ambiguous;
        Numbers = Array.AsReadOnly(files.Keys.Order().ToArray());
        AmbiguousNumbers = Array.AsReadOnly(ambiguous.ToArray());
        AmbiguousKeys = ambiguousKeys; UnrecognisedKeys = unrecognised; IgnoredModuleKeys = ignored;
    }

    /// <summary>Numbers held by exactly one addressable base file, ascending.</summary>
    public IReadOnlyList<int> Numbers { get; }
    /// <summary>Numbers held by two or more addressable base files, ascending; none of them is read.</summary>
    public IReadOnlyList<int> AmbiguousNumbers { get; }
    /// <summary>Every base key of an ambiguous number, in ordinal order.</summary>
    public IReadOnlyList<string> AmbiguousKeys { get; }
    /// <summary>Base <c>proto/*.pro</c> keys that are not <c>NNNNNN - label.pro</c>, in ordinal order.</summary>
    public IReadOnlyList<string> UnrecognisedKeys { get; }
    /// <summary><c>proto/*.pro</c> keys a module source holds, in ordinal order: the original never reads that copy (a
    /// base copy of the same key, when there is one, is read instead).</summary>
    public IReadOnlyList<string> IgnoredModuleKeys { get; }

    /// <summary>Indexes the stack's <c>proto/</c> folder once; later reads go through the stack.</summary>
    public static ContentPrototypes Open(ContentStack stack)
    {
        ArgumentNullException.ThrowIfNull(stack);
        var files = new Dictionary<int, (string, ContentCandidate)>();
        var keysByNumber = new Dictionary<int, List<string>>();
        var unrecognised = new List<string>(); var ignored = new List<string>();
        foreach (var key in stack.Keys(Folder))
        {
            var name = key[Folder.Length..];
            if (name.Contains('/') || !name.EndsWith(".pro", StringComparison.Ordinal)) continue;
            var resolution = stack.Resolve(key);
            if (resolution.Candidates.Any(c => IsModule(c.Source.Role))) ignored.Add(key);
            if (BaseCandidate(resolution) is not { } candidate) continue;
            if (!TryNumber(name, out var number)) { unrecognised.Add(key); continue; }
            if (!keysByNumber.TryGetValue(number, out var keys)) keysByNumber.Add(number, keys = []);
            keys.Add(key);
            files[number] = (key, candidate);
        }
        var ambiguous = new SortedSet<int>(keysByNumber.Where(pair => pair.Value.Count > 1).Select(pair => pair.Key));
        foreach (var number in ambiguous) files.Remove(number);
        var ambiguousKeys = ambiguous.SelectMany(number => keysByNumber[number]).Order(StringComparer.Ordinal).ToArray();
        return new(stack, files, ambiguous, Array.AsReadOnly(ambiguousKeys), unrecognised.AsReadOnly(), ignored.AsReadOnly());
    }

    /// <summary>True when two or more base files hold <paramref name="number"/>, so none of them is read.</summary>
    public bool IsAmbiguous(int number) => _ambiguous.Contains(number);

    /// <summary>The virtual path of the file read for <paramref name="number"/>, or null when it has none or is ambiguous.</summary>
    public string? Path(int number) => _files.TryGetValue(number, out var file) ? file.Key : null;

    /// <summary>The base candidate read for <paramref name="number"/>, or null when it has none or is ambiguous.</summary>
    public ContentCandidate? Candidate(int number) => _files.TryGetValue(number, out var file) ? file.Candidate : null;

    /// <summary>The file of <paramref name="number"/> as the pre-made hero and combat field readers take it:
    /// <see cref="PrototypeFileStatus.Missing"/> without a base file (also out of range), <see cref="PrototypeFileStatus.Ambiguous"/>
    /// with two, <see cref="PrototypeFileStatus.TooLarge"/> above <see cref="MaxPrototypeBytes"/>,
    /// <see cref="PrototypeFileStatus.Unreadable"/> when the stack cannot read it.</summary>
    public PrototypeFile Read(int number)
    {
        if (_ambiguous.Contains(number)) return new(PrototypeFileStatus.Ambiguous, null);
        if (!_files.TryGetValue(number, out var file)) return new(PrototypeFileStatus.Missing, null);
        if (file.Candidate.Length > MaxPrototypeBytes) return new(PrototypeFileStatus.TooLarge, null);
        var read = _stack.Read(file.Candidate);
        return read.Status switch
        {
            ContentReadStatus.Read when read.Bytes!.Length <= MaxPrototypeBytes => new(PrototypeFileStatus.Found, read.Bytes),
            ContentReadStatus.Read or ContentReadStatus.TooLarge => new(PrototypeFileStatus.TooLarge, null),
            _ => new(PrototypeFileStatus.Unreadable, null),
        };
    }

    /// <summary>The first candidate the original reads at game start: the first base source in search order.</summary>
    public static ContentCandidate? BaseCandidate(ContentResolution resolution) => ContentPhase.Candidate(resolution, ContentLoadPhase.GameStart);

    /// <summary>The module archive, a module patch or the module folder: sources mounted after game start.</summary>
    public static bool IsModule(ContentSourceRole role) => ContentPhase.IsModule(role);

    /// <summary><c>NNNNNN - label.pro</c>: six decimal digits, <c>" - "</c>, a label and the extension.</summary>
    internal static bool TryNumber(string fileName, out int number)
    {
        number = 0;
        if (fileName.Length < 14 || !fileName.EndsWith(".pro", StringComparison.Ordinal) || fileName[6..9] != " - ") return false;
        return int.TryParse(fileName.AsSpan(0, 6), NumberStyles.None, CultureInfo.InvariantCulture, out number);
    }
}
