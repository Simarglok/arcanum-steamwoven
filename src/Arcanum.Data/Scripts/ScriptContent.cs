using System.Security.Cryptography;
using Arcanum.Data.Content;

namespace Arcanum.Data.Scripts;

public enum ScriptContentStatus
{
    Read,
    /// <summary>The original has no name for the number (<see cref="ScriptNameIndex.Find"/>).</summary>
    NoName,
    /// <summary>The number has a name, but no source holds the derived DLG file.</summary>
    NotFound,
    /// <summary>The winning copy could not be read within the stack's limits.</summary>
    Unreadable,
    /// <summary>The bytes are not a script (<see cref="ScrProgram.Read"/>) or a dialog (<see cref="DlgReader"/>).</summary>
    Invalid,
    /// <summary>The winning copy's bytes differ from the first read of the same path through this object.</summary>
    Changed,
}

/// <summary>A script read: the program when <see cref="Status"/> is <see cref="ScriptContentStatus.Read"/>.
/// <see cref="Detail"/> names a status or a virtual path, never an absolute path or payload bytes.</summary>
public sealed record ScriptRead(ScriptContentStatus Status, ScriptName? Name, ScrProgram? Program, string Detail);

/// <summary>A dialog read: the document when <see cref="Status"/> is <see cref="ScriptContentStatus.Read"/>.</summary>
public sealed record DialogRead(ScriptContentStatus Status, ScriptName? Name, DlgDocument? Document, string Detail);

public sealed record ScriptContentOpenResult(ContentStackStatus Status, ScriptContent? Content, string? Detail = null);

/// <summary>
/// Script and dialog files by script number for any map (ARC-SCRIPT-011, docs/scripting/script-content.md): the
/// <see cref="ScriptNameIndex"/> picks the name, the content stack's winning copy of that path is read, as the original
/// opens it. Each read goes to the stack again; the first bytes read for a path are remembered by SHA-256, and a later
/// read that differs is <see cref="ScriptContentStatus.Changed"/>, so one object never mixes two versions of a file.
/// Thread-safe for reads.
/// </summary>
public sealed class ScriptContent : IDisposable
{
    private readonly ContentStack _stack;
    private readonly bool _ownsStack;
    private readonly ScrReaderOptions _scripts;
    private readonly DlgReaderOptions _dialogs;
    private readonly Dictionary<string, byte[]> _seen = new(StringComparer.Ordinal);
    private readonly object _gate = new();
    private int _changed;

    private ScriptContent(ContentStack stack, bool ownsStack, ScrReaderOptions? scripts, DlgReaderOptions? dialogs)
    {
        _stack = stack; _ownsStack = ownsStack; _scripts = scripts ?? new(); _dialogs = dialogs ?? new();
        Names = ScriptNameIndex.Build(stack);
    }

    public ScriptNameIndex Names { get; }
    /// <summary>The mount plan the files come from.</summary>
    public ContentMountPlan Plan => _stack.Plan;
    /// <summary>True once any read found bytes different from the first read of its path.</summary>
    public bool Changed => Volatile.Read(ref _changed) != 0;

    /// <summary>Opens a stack over <paramref name="plan"/> and owns it.</summary>
    public static ScriptContentOpenResult Open(ContentMountPlan plan, ContentStackOptions? options = null, ScrReaderOptions? scripts = null, DlgReaderOptions? dialogs = null)
    {
        var opened = ContentStack.Open(plan, options);
        if (opened.Status != ContentStackStatus.Ready) return new(opened.Status, null, opened.Detail);
        try { return new(ContentStackStatus.Ready, new ScriptContent(opened.Stack!, true, scripts, dialogs)); }
        catch { opened.Stack!.Dispose(); throw; }
    }

    /// <summary>Reads through a stack the caller keeps open and disposes.</summary>
    public static ScriptContent Over(ContentStack stack, ScrReaderOptions? scripts = null, DlgReaderOptions? dialogs = null)
    {
        ArgumentNullException.ThrowIfNull(stack);
        return new(stack, false, scripts, dialogs);
    }

    /// <summary>The script numbered <paramref name="number"/>, framed strictly.</summary>
    public ScriptRead Script(int number)
    {
        if (Names.Find(number) is not { } name) return new(ScriptContentStatus.NoName, null, null, $"script {number} has no name");
        var (status, bytes, detail) = ReadWinner(name.ScriptPath);
        if (status != ScriptContentStatus.Read) return new(status, name, null, detail);
        var read = ScrProgram.Read(bytes, _scripts);
        return read.Status == ScrReadStatus.Read
            ? new(ScriptContentStatus.Read, name, read.Program, name.ScriptPath)
            : new(ScriptContentStatus.Invalid, name, null, $"{name.ScriptPath}: {read.Status}");
    }

    /// <summary>The dialog of script <paramref name="number"/>: the script's name with <c>dlg</c> folder and extension.</summary>
    public DialogRead Dialog(int number)
    {
        if (Names.Find(number) is not { } name) return new(ScriptContentStatus.NoName, null, null, $"script {number} has no name");
        var (status, bytes, detail) = ReadWinner(name.DialogPath);
        if (status != ScriptContentStatus.Read) return new(status, name, null, detail);
        try { return new(ScriptContentStatus.Read, name, DlgReader.Read(bytes, _dialogs), name.DialogPath); }
        catch (DlgException exception) { return new(ScriptContentStatus.Invalid, name, null, $"{name.DialogPath}: {exception.GetType().Name}"); }
    }

    private (ScriptContentStatus, byte[], string) ReadWinner(string path)
    {
        var resolution = _stack.Resolve(path);
        if (resolution.Winner is not { } winner) return (ScriptContentStatus.NotFound, [], $"{path}: not in the content stack");
        var read = _stack.Read(winner);
        if (read.Status != ContentReadStatus.Read) return (ScriptContentStatus.Unreadable, [], $"{path}: {read.Status}");
        var digest = SHA256.HashData(read.Bytes!);
        lock (_gate)
        {
            if (!_seen.TryGetValue(path, out var first)) _seen[path] = digest;
            else if (!first.AsSpan().SequenceEqual(digest))
            {
                Volatile.Write(ref _changed, 1);
                return (ScriptContentStatus.Changed, [], $"{path}: changed since it was first read");
            }
        }
        return (ScriptContentStatus.Read, read.Bytes!, path);
    }

    public void Dispose()
    {
        if (_ownsStack) _stack.Dispose();
    }
}
