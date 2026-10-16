using Arcanum.Data.Characters;
using Arcanum.Data.StaticObjects;

namespace Arcanum.Data.Objects;

/// <summary>Shared loose-prototype read: one file by number through a caller lookup into a complete v119 prototype record
/// whose own ID number is the file's and whose bytes are the whole file.</summary>
internal static class PrototypeRecords
{
    internal const int MaxPrototypeBytes = 64 * 1024;

    private static readonly StaticObjectLoaderOptions Limits = new() { MaxPrototypeBytes = MaxPrototypeBytes, MaxArrayElements = 4096, MaxStringBytes = 4096 };

    /// <summary>Found with a record, or the lookup's status; a file that is not a complete v119 prototype record of that
    /// number is <see cref="PrototypeFileStatus.Unreadable"/>.</summary>
    internal static PrototypeFileStatus Read(Func<int, PrototypeFile> lookup, int number, out ObjectWireRecord? record)
    {
        record = null;
        var file = lookup(number);
        if (file is null || file.Status != PrototypeFileStatus.Found)
            return file?.Status is { } status && status != PrototypeFileStatus.Found ? status : PrototypeFileStatus.Missing;
        if (file.Bytes is not { } bytes || bytes.Length > MaxPrototypeBytes) return PrototypeFileStatus.TooLarge;
        try { record = ObjectWireReader.Read(bytes, Limits, prototype: true, maxRecordBytes: MaxPrototypeBytes); }
        catch (StaticObjectException) { return PrototypeFileStatus.Unreadable; }
        if (record.Own.Number != number || record.Length != bytes.Length)
        {
            record = null;
            return PrototypeFileStatus.Unreadable;
        }
        return PrototypeFileStatus.Found;
    }
}
