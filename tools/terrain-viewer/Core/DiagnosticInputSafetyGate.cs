namespace Arcanum.TerrainViewer.Core;

/// <summary>Only frame-owned, complete observed levels can rearm world motion.</summary>
public sealed class DiagnosticInputSafetyGate
{
    private int _neutralFrames;
    public bool IsOpen { get; private set; }
    public void Close() { IsOpen = false; _neutralFrames = 0; }
    public void ObserveCompleteFrame(bool focused, bool modal, bool levelsComplete, bool allMappedLevelsNeutral)
    {
        if (!focused || modal || !levelsComplete) { Close(); return; }
        if (IsOpen) return;
        if (!allMappedLevelsNeutral) { _neutralFrames = 0; return; }
        if (++_neutralFrames >= 2) IsOpen = true;
    }
}
