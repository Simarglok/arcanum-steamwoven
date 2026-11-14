using Arcanum.Input;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-GPAD-1376 (concept §0.8): the opt-in evidence log of the controller test. Off at every start and never saved: the player turns it on
// in Options (the Evidence Log line, Video page) for a test session. While on, one file per session under the device's own data folder
// (user://evidence, or the isolated folder of an automated run) takes one line per semantic command, window open or close, refused open and
// task marker, with the closed tokens of EvidenceLogCodec: no path, name, item, text or wall-clock time. Nothing is sent anywhere; the owner
// copies the file by hand to the summariser (tools/input/evidence_summary.py). The control schemes' slices call RecordEvidence for their commands.
public partial class TerrainViewerApp
{
    public const string EvidenceTestDirectoryVariable = "EVIDENCE_LOG_TEST_DIR";
    private EvidenceRecorder? _evidence;
    // The device in hand and the scheme it plays by, from the controller foundation (ARC-GPAD-1374): the mouse and keyboard are the original; a pad
    // plays by the Controls line (A and B are its pad schemes; C uses the pad too; Original is the original, as its prompts and commands are).
    private ActiveDevice _evidenceDevice => Controllers.Device;
    private EvidenceScheme _evidenceScheme => Controllers.Device switch
    {
        ActiveDevice.Pad => Controllers.Settings.Pad switch { PadScheme.Direct => EvidenceScheme.Direct, PadScheme.Pointer => EvidenceScheme.Pointer, _ => Controllers.Settings.Touch == TouchScheme.TouchPad ? EvidenceScheme.TouchPad : EvidenceScheme.Original },
        ActiveDevice.Touch => Controllers.Settings.Touch == TouchScheme.TouchPad ? EvidenceScheme.TouchPad : EvidenceScheme.Original,
        _ => EvidenceScheme.Original,
    };
    private int _evidenceTask;

    private EvidenceRecorder Evidence => _evidence ??= new EvidenceRecorder(OpenEvidenceSink);

    private bool EvidenceRecording => _evidence is { Recording: true };
    private static double EvidenceClock => Time.GetTicksMsec() / 1000.0;

    /// <summary>One semantic command and how it ended; nothing when the log is off.</summary>
    internal void RecordEvidence(EvidenceCommand command, EvidenceOutcome outcome = EvidenceOutcome.Ok)
    {
        if (_evidence is { Recording: true } log) log.Command(EvidenceClock, command, _evidenceScheme, _evidenceDevice, outcome, _evidenceTask);
    }

    /// <summary>The Evidence Log line: switches the session. Null when it took effect, else why the setting stayed.</summary>
    private string? SetEvidenceLog(bool on)
    {
        if (on == EvidenceRecording) return null;
        if (!on) SetEvidenceTask(0); // the running task ends with the session
        else _evidenceTask = 0; // a session that stopped by itself (full, disk error) left its task number behind
        var changed = Evidence.SetEnabled(on, EvidenceClock, _evidenceScheme, _evidenceDevice);
        GD.Print($"ARC-GPAD-1376 evidence log {(on ? "on" : "off")}{(changed ? "" : " refused")} lines={Evidence.Lines}");
        return changed ? null : "no place to write the log on this device";
    }

    /// <summary>The task part of the Evidence Log line (ARC-UI-1797): ends the running task and starts the chosen one (0 = none). Null when it took effect.</summary>
    private string? SetEvidenceTask(int task)
    {
        if (task < 0 || task > AreaOptionTable.TestTaskCount) return "no such task";
        if (!EvidenceRecording) return task == 0 ? null : "turn the evidence log on first";
        if (task == _evidenceTask) return null;
        var log = Evidence;
        if (_evidenceTask != 0) log.Record(EvidenceClock, EvidenceKind.TaskEnd, EvidenceCommand.None, _evidenceScheme, _evidenceDevice, EvidenceOutcome.Ok, _evidenceTask);
        _evidenceTask = task;
        if (task != 0) log.Record(EvidenceClock, EvidenceKind.TaskStart, EvidenceCommand.None, _evidenceScheme, _evidenceDevice, EvidenceOutcome.Ok, task);
        return null;
    }

    /// <summary>The app's exit: the open session ends with its last line.</summary>
    private void CloseEvidence()
    {
        if (!EvidenceRecording) return;
        SetEvidenceTask(0);
        Evidence.SetEnabled(false, EvidenceClock, _evidenceScheme, _evidenceDevice);
    }

    private IEvidenceSink? OpenEvidenceSink()
    {
        var test = System.Environment.GetEnvironmentVariable(EvidenceTestDirectoryVariable);
        var folder = !string.IsNullOrWhiteSpace(test) ? test
            : ViewerAutomatedStorage.Automated ? System.IO.Path.Combine(ViewerAutomatedStorage.Folder(), "evidence")
            : System.IO.Path.Combine(ProjectSettings.GlobalizePath("user://"), "evidence");
        return EvidenceFileSink.Open(folder, DateTime.UtcNow);
    }
}
