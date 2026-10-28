using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>
/// ARC-DEV-051: the engine logger that fills the bug report's 120 s buffer (<see cref="BugReportLog.Process"/>) with every message
/// and error the engine logs (what <c>GD.Print</c> and the engine's errors write to the console and <c>logs/godot.log</c>). Installed
/// once per process by the first viewer that starts, removed when it exits. The engine calls it from any thread; the buffer locks.
/// It must not print (the engine refuses a print from inside a logger).
/// </summary>
internal sealed partial class BugReportLogger : Logger
{
    private static BugReportLogger? _installed;

    public static void Install()
    {
        if (_installed is not null) return;
        _installed = new BugReportLogger();
        OS.AddLogger(_installed);
    }

    public static void Remove()
    {
        if (_installed is null) return;
        OS.RemoveLogger(_installed);
        _installed = null;
    }

    public override void _LogMessage(string message, bool error) =>
        BugReportLog.Process.Add(error ? BugReportLineKind.Error : BugReportLineKind.Log, message);

    public override void _LogError(string function, string file, int line, string code, string rationale, bool editorNotify, int errorType,
        Godot.Collections.Array<ScriptBacktrace> scriptBacktraces)
    {
        var kind = (ErrorType)errorType switch { ErrorType.Warning => "WARNING", ErrorType.Script => "SCRIPT ERROR", ErrorType.Shader => "SHADER ERROR", _ => "ERROR" };
        var text = string.IsNullOrEmpty(rationale) ? code : $"{rationale} ({code})";
        BugReportLog.Process.Add(BugReportLineKind.Error, $"{kind}: {text} at {function} ({file}:{line})");
    }
}
