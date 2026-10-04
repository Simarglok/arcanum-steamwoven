namespace Arcanum.Data.Area;

// Structural ownership, not a GC/RSS estimate. Detach transfers an already
// charged graph to the owner; disposal drops only this scope's reference bound.
internal sealed class AreaOwnership(AreaBudget budget) : IDisposable, IDatEntryAdmission
{
    private long _bytes;
    internal void Allocate(long bytes) => Allocate(bytes, bytes);
    void IDatEntryAdmission.Admit(long bytes) => Allocate(bytes);
    internal void Allocate(long allocated, long held)
    {
        var next = checked(_bytes + held);
        budget.Own(allocated, held); _bytes = next;
    }
    internal long Detach() { var bytes = _bytes; _bytes = 0; return bytes; }
    public void Dispose() { budget.Release(_bytes); _bytes = 0; }
}
