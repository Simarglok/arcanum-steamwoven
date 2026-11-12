using System.Globalization;
using Arcanum.Input;

namespace Arcanum.TerrainViewer.Core;

/// <summary>The session's file: <c>evidence-yyyyMMdd-HHmmss.jsonl</c> in the folder (UTC, in the name only), each append flushed so a crash
/// loses at most the line being written. A folder that can not be made or a file that can not be created gives null.</summary>
public sealed class EvidenceFileSink : IEvidenceSink
{
    private readonly FileStream _stream;
    private EvidenceFileSink(FileStream stream) => _stream = stream;
    public string FileName { get; private init; } = "";

    public static EvidenceFileSink? Open(string folder, DateTime utc)
    {
        try
        {
            Directory.CreateDirectory(folder);
            var stem = "evidence-" + utc.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            for (var i = 0; i < 100; i++)
            {
                var name = i == 0 ? stem + ".jsonl" : $"{stem}-{i}.jsonl";
                try { return new EvidenceFileSink(new FileStream(System.IO.Path.Combine(folder, name), FileMode.CreateNew, FileAccess.Write, FileShare.Read)) { FileName = name }; }
                catch (IOException) when (File.Exists(System.IO.Path.Combine(folder, name))) { }
            }
            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException) { return null; }
    }

    public bool Append(string text)
    {
        try
        {
            var bytes = new System.Text.UTF8Encoding(false).GetBytes(text);
            _stream.Write(bytes, 0, bytes.Length);
            _stream.Flush();
            return true;
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException or NotSupportedException) { return false; }
    }

    public void Close() => _stream.Dispose();
}
