using Arcanum.Data.Content;

namespace Arcanum.Data.Scripts;

/// <summary>
/// One script number's files: the SCR name the original maps the number to and the DLG name it derives from it
/// (the script name with the <c>scr</c> folder and extension swapped for <c>dlg</c>). Paths are content-stack keys
/// (lower case, <c>/</c>); <see cref="ReplacedNames"/> counts earlier listed names with the same number that this one
/// replaced (the original prints an error for each and keeps the last).
/// </summary>
public sealed record ScriptName(int Number, string ScriptPath, string DialogPath, int ReplacedNames);

/// <summary>
/// The original's script-number table over a <see cref="ContentStack"/> (ARC-SCRIPT-011, contract
/// docs/scripting/script-content.md): every <c>scr\*.scr</c> name in the sorted, de-duplicated listing maps the number
/// its leading digits spell (as C <c>atoi</c> reads them) to that name, a later name replacing an earlier one. Numbers
/// from 1000 up are listed at game start, before the module is mounted, so only names a base source holds (tig.dat,
/// the game folder, <c>arcanum*.dat</c>, <c>data</c>) count for them; 1..999 are listed after the module is mounted,
/// from every source. Built once; the stack is not kept.
/// </summary>
public sealed class ScriptNameIndex
{
    /// <summary>The first number the original lists at game start (<c>script_name_init</c> 0x4CEC10); lower numbers are
    /// listed at module load (<c>script_name_mod_load</c> 0x4CECE0).</summary>
    public const int FirstSystemNumber = 1000;
    private const string Folder = "scr/", Extension = ".scr";
    private readonly IReadOnlyDictionary<int, ScriptName> _names;

    private ScriptNameIndex(IReadOnlyDictionary<int, ScriptName> names, int listed, int replaced, int unnumbered, int moduleOnly)
    {
        _names = names; ListedNames = listed; ReplacedNames = replaced; UnnumberedNames = unnumbered; ModuleOnlySystemNames = moduleOnly;
    }

    /// <summary>Every number with a name, in number order.</summary>
    public IReadOnlyList<ScriptName> Names => _names.Values.OrderBy(name => name.Number).ToArray();
    /// <summary><c>scr\*.scr</c> names in the stack's listing.</summary>
    public int ListedNames { get; }
    /// <summary>Names a later name with the same number replaced.</summary>
    public int ReplacedNames { get; }
    /// <summary>Names whose leading digits spell no number above 0 (or more than nine digits, not modelled).</summary>
    public int UnnumberedNames { get; }
    /// <summary>Names numbered 1000 or more that only module sources hold: the original never lists them.</summary>
    public int ModuleOnlySystemNames { get; }

    /// <summary>The number's files, or null when the original has no name for it.</summary>
    public ScriptName? Find(int number) => _names.TryGetValue(number, out var name) ? name : null;

    public static ScriptNameIndex Build(ContentStack stack)
    {
        ArgumentNullException.ThrowIfNull(stack);
        var names = new Dictionary<int, ScriptName>();
        int listed = 0, replaced = 0, unnumbered = 0, moduleOnly = 0;
        // Keys are lower case, so ordinal order is the listing's case-insensitive order.
        foreach (var key in stack.Keys(Folder))
        {
            var file = key[Folder.Length..];
            if (file.Contains('/') || file.Length <= Extension.Length || !file.EndsWith(Extension, StringComparison.Ordinal)) continue;
            listed++;
            if (Number(file) is not { } number) { unnumbered++; continue; }
            if (number >= FirstSystemNumber && !stack.Resolve(key).Candidates.Any(candidate => IsBase(candidate.Source.Role))) { moduleOnly++; continue; }
            var previous = names.TryGetValue(number, out var earlier) ? earlier.ReplacedNames + 1 : 0;
            replaced += previous > 0 ? 1 : 0;
            names[number] = new(number, key, "dlg/" + file[..^3] + "dlg", previous);
        }
        return new(names, listed, replaced, unnumbered, moduleOnly);
    }

    /// <summary>Sources the original mounts before the game-start listing (docs/research/content-precedence.md).</summary>
    public static bool IsBase(ContentSourceRole role) =>
        role is ContentSourceRole.Tig or ContentSourceRole.GameRoot or ContentSourceRole.BaseArchive or ContentSourceRole.DataDirectory;

    /// <summary>
    /// C <c>atoi</c> over a file name: leading spaces, an optional sign, then decimal digits up to the first other
    /// character. Null for a value below 1 or a run of more than nine digits (the original's overflow is not modelled).
    /// </summary>
    public static int? Number(string file)
    {
        ArgumentNullException.ThrowIfNull(file);
        var i = 0;
        while (i < file.Length && file[i] is ' ' or '\t' or '\n' or '\v' or '\f' or '\r') i++;
        var negative = i < file.Length && file[i] == '-';
        if (i < file.Length && file[i] is '+' or '-') i++;
        var start = i;
        while (i < file.Length && char.IsAsciiDigit(file[i])) i++;
        var digits = file[start..i].TrimStart('0');
        if (digits.Length > 9) return null;
        var value = digits.Length == 0 ? 0 : int.Parse(digits, System.Globalization.CultureInfo.InvariantCulture);
        return negative || value < 1 ? null : value;
    }
}
