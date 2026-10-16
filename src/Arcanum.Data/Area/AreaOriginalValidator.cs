using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Arcanum.Data.Area;

/// <summary>Opt-in original-data validation. Original identity assertions always come
/// from the pinned profile, never from a caller-authored structural source plan.</summary>
public static class AreaOriginalValidator
{
    public static AreaDataOwner Open(AreaInputSet inputs, AreaLoaderOptions? options = null)
        => AreaDataOwner.Open(inputs, OriginalProfile.Plan(), options ?? new());

    /// <summary>The Crash Site cave (catalogue map 5078) under its own pinned profile. The
    /// normal-game mobile role is the cave's game-generated object file.</summary>
    public static AreaDataOwner OpenCave(AreaInputSet inputs, AreaLoaderOptions? options = null)
        => AreaDataOwner.Open(inputs, CaveProfile.Plan(), options ?? new());

    /// <summary>Opens the explicit original roles and uniquely selected prototype
    /// files. Selection belongs to the authenticated Data profile, not a renderer
    /// resource list. All handles close if assembly or owner creation fails.</summary>
    public static AreaDataOwner OpenFiles(string module, string resource1, string resource2,
        string mobile, string prototypeDirectory, AreaLoaderOptions? options = null)
        => OpenFiles(module, resource1, resource2, mobile, prototypeDirectory, OriginalProfile.Prototypes.Keys, inputs => Open(inputs, options));

    /// <summary>As <see cref="OpenFiles(string,string,string,string,string,AreaLoaderOptions?)"/> for the cave;
    /// <paramref name="caveMobile"/> is the cave's game-generated object file.</summary>
    public static AreaDataOwner OpenCaveFiles(string module, string resource1, string resource2,
        string caveMobile, string prototypeDirectory, AreaLoaderOptions? options = null)
        => OpenFiles(module, resource1, resource2, caveMobile, prototypeDirectory, CaveProfile.Prototypes.Keys, inputs => OpenCave(inputs, options));

    private static AreaDataOwner OpenFiles(string module, string resource1, string resource2, string mobile, string prototypeDirectory,
        IEnumerable<int> prototypes, Func<AreaInputSet, AreaDataOwner> open)
    {
        var inputs = new List<AreaInput>();
        try
        {
            inputs.Add(AreaInput.FromFile(new(AreaSourceRole.Module), module));
            inputs.Add(AreaInput.FromFile(new(AreaSourceRole.Resource1), resource1));
            inputs.Add(AreaInput.FromFile(new(AreaSourceRole.Resource2), resource2));
            inputs.Add(AreaInput.FromFile(new(AreaSourceRole.NormalGameMobile), mobile));
            foreach (var number in prototypes.Order())
            {
                var candidates = Directory.EnumerateFiles(prototypeDirectory, $"{number:D6} - *.pro", SearchOption.TopDirectoryOnly).Take(2).ToArray();
                if (candidates.Length != 1)
                    throw new AreaException(new(candidates.Length == 0 ? AreaErrorCode.MissingSource : AreaErrorCode.AmbiguousSource, AreaStage.Prototype));
                inputs.Add(AreaInput.FromFile(new(AreaSourceRole.Prototype, number), candidates[0]));
            }
            return open(new(inputs));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            foreach (var input in inputs) input.Stream.Dispose();
            throw new AreaException(new(AreaErrorCode.MissingSource, AreaStage.Input));
        }
        catch
        {
            foreach (var input in inputs) input.Stream.Dispose();
            throw;
        }
    }

    public static AreaInspection Validate(AreaInputSet inputs, CancellationToken cancellationToken = default)
    {
        using var owner = Open(inputs);
        return owner.Inspect(cancellationToken);
    }
}

/// <summary>Read-only Unix file adapter with handle/path device+inode binding.
/// The normalized .NET 8 System.Native file-status ABI is used, not platform
/// guesses about libc stat layouts. Public errors never include input paths.</summary>
internal sealed class AreaPathStream : Stream
{
    private readonly string _path;
    private readonly FileStream _file;
    private readonly Status _opened;
    internal AreaPathStream(string path)
    {
        _path = path;
        if (!OperatingSystem.IsMacOS() && !OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("The path-bound adapter requires macOS or Linux.");
        try
        {
            _file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.RandomAccess);
            if (FileStatus(_file.SafeFileHandle, out _opened) != 0) { _file.Dispose(); throw Changed(); }
            try { Check(); } catch { _file.Dispose(); throw; }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        { throw new AreaException(new(AreaErrorCode.MissingSource, AreaStage.Input)); }
    }
    internal void Check()
    {
        if (FileStatus(_file.SafeFileHandle, out var opened) != 0 || PathStatus(_path, out var path) != 0 ||
            opened.Device != _opened.Device || opened.Inode != _opened.Inode || path.Device != _opened.Device || path.Inode != _opened.Inode ||
            opened.Size != _opened.Size || path.Size != _opened.Size || opened.Modified != _opened.Modified || opened.ModifiedNanos != _opened.ModifiedNanos ||
            opened.Changed != _opened.Changed || opened.ChangedNanos != _opened.ChangedNanos)
            throw Changed();
    }
    private static AreaException Changed() => new(new(AreaErrorCode.InputChangedDuringRead, AreaStage.Input));
    [StructLayout(LayoutKind.Explicit, Size = 120)]
    private struct Status
    {
        [FieldOffset(16)] internal long Size;
        [FieldOffset(40)] internal long Modified;
        [FieldOffset(48)] internal long ModifiedNanos;
        [FieldOffset(56)] internal long Changed;
        [FieldOffset(64)] internal long ChangedNanos;
        [FieldOffset(88)] internal long Device;
        [FieldOffset(104)] internal long Inode;
    }
    [DllImport("System.Native", EntryPoint = "SystemNative_FStat", SetLastError = true)]
    private static extern int FileStatus(SafeFileHandle handle, out Status status);
    [DllImport("System.Native", EntryPoint = "SystemNative_Stat", SetLastError = true)]
    private static extern int PathStatus([MarshalAs(UnmanagedType.LPUTF8Str)] string path, out Status status);
    public override bool CanRead => _file.CanRead;
    public override bool CanSeek => _file.CanSeek;
    public override bool CanWrite => false;
    public override long Length => _file.Length;
    public override long Position { get => _file.Position; set => _file.Position = value; }
    public override int Read(byte[] buffer, int offset, int count) => _file.Read(buffer, offset, count);
    public override int Read(Span<byte> buffer) => _file.Read(buffer);
    public override long Seek(long offset, SeekOrigin origin) => _file.Seek(offset, origin);
    public override void Flush() { }
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    protected override void Dispose(bool disposing) { if (disposing) _file.Dispose(); base.Dispose(disposing); }
}
