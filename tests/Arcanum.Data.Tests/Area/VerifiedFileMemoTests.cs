using System.Security.Cryptography;
using Arcanum.Data.Area;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Data.Tests.Area.AreaIdentityTests;

namespace Arcanum.Data.Tests.Area;

/// <summary>ARC-DEV-374 (O5a): a file of at least 1 MiB is hashed in full on the first check in the process and later checks reuse
/// the hash while the file's stamp is unchanged. The tests share the process-wide memo, so they run one after another.</summary>
[TestClass, DoNotParallelize]
public sealed class VerifiedFileMemoTests
{
    private static string Temp() => Path.Combine(Environment.GetEnvironmentVariable("TMPDIR") ?? AppContext.BaseDirectory, "memo-" + Guid.NewGuid().ToString("N") + ".bin");
    private static byte[] Bytes(byte seed, int length) { var bytes = new byte[length]; for (var i = 0; i < length; i++) bytes[i] = (byte)(seed + i * 7); return bytes; }

    [TestMethod]
    public void A_large_file_is_hashed_once_and_reused_while_it_is_unchanged()
    {
        var path = Temp(); var bytes = Bytes(1, 2 * 1024 * 1024);
        File.WriteAllBytes(path, bytes);
        try
        {
            var passes = VerifiedFileMemo.FullPasses; var reused = VerifiedFileMemo.Reused;
            var first = VerifiedFileMemo.Identity(path);
            Assert.AreEqual(bytes.Length, first.Length);
            Assert.AreEqual(Hash(bytes), first.Sha256);
            Assert.AreEqual(passes + 1, VerifiedFileMemo.FullPasses);
            var second = VerifiedFileMemo.Identity(path);
            Assert.AreEqual(first, second);
            Assert.AreEqual(passes + 1, VerifiedFileMemo.FullPasses, "the second check reads nothing");
            Assert.AreEqual(reused + 1, VerifiedFileMemo.Reused);
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public void A_rewritten_file_of_the_same_size_is_hashed_in_full_again()
    {
        var path = Temp(); var before = Bytes(2, 1024 * 1024 + 5); var after = Bytes(3, before.Length);
        File.WriteAllBytes(path, before);
        try
        {
            Assert.AreEqual(Hash(before), VerifiedFileMemo.Identity(path).Sha256);
            File.WriteAllBytes(path, after);
            var passes = VerifiedFileMemo.FullPasses;
            Assert.AreEqual(Hash(after), VerifiedFileMemo.Identity(path).Sha256, "a changed stamp is a first check again");
            Assert.AreEqual(passes + 1, VerifiedFileMemo.FullPasses);
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public void A_file_that_is_replaced_by_another_file_at_the_same_path_is_hashed_in_full_again()
    {
        // Windows stamps only the size and the last write time, so a same-size copy with the same time is not told apart there (documented).
        if (OperatingSystem.IsWindows()) Assert.Inconclusive("The Windows stamp has no inode or change time.");
        var path = Temp(); var other = Temp(); var before = Bytes(4, 1024 * 1024); var after = Bytes(5, before.Length);
        File.WriteAllBytes(path, before); File.WriteAllBytes(other, after);
        try
        {
            Assert.AreEqual(Hash(before), VerifiedFileMemo.Identity(path).Sha256);
            // The other file's modification time is put back to the first's: the inode and change time still differ.
            File.SetLastWriteTimeUtc(other, File.GetLastWriteTimeUtc(path));
            File.Move(other, path, overwrite: true);
            Assert.AreEqual(Hash(after), VerifiedFileMemo.Identity(path).Sha256);
        }
        finally { File.Delete(path); File.Delete(other); }
    }

    [TestMethod]
    public void A_file_under_one_mebibyte_is_hashed_on_every_check()
    {
        var path = Temp(); var bytes = Bytes(6, 1024 * 1024 - 1);
        File.WriteAllBytes(path, bytes);
        try
        {
            var passes = VerifiedFileMemo.FullPasses; var reused = VerifiedFileMemo.Reused;
            Assert.AreEqual(Hash(bytes), VerifiedFileMemo.Identity(path).Sha256);
            Assert.AreEqual(Hash(bytes), VerifiedFileMemo.Identity(path).Sha256);
            Assert.AreEqual(reused, VerifiedFileMemo.Reused);
            Assert.AreEqual(passes, VerifiedFileMemo.FullPasses, "small files are neither counted nor remembered");
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public void The_owner_reads_a_large_input_once_per_process_and_still_refuses_a_different_file()
    {
        var path = Temp(); var bytes = Dat(("padding.bin", Bytes(7, 1_150_000)));
        File.WriteAllBytes(path, bytes);
        try
        {
            var key = new AreaInputKey(AreaSourceRole.Module);
            AreaSourcePlan Plan(string sha) => new(1, 1, new(1, 1), 2, "maps/test", [new(key, bytes.Length, sha)], []);
            var passes = VerifiedFileMemo.FullPasses; var reused = VerifiedFileMemo.Reused;
            using (AreaDataOwner.Open(new([AreaInput.FromFile(key, path)]), Plan(Hash(bytes)), new())) { }
            Assert.AreEqual(passes + 1, VerifiedFileMemo.FullPasses, "the first check of the file in the process hashes it");
            using (var again = AreaDataOwner.Open(new([AreaInput.FromFile(key, path)]), Plan(Hash(bytes)), new()))
            {
                Assert.AreEqual(passes + 1, VerifiedFileMemo.FullPasses, "a later load reads no bytes for it");
                Assert.AreEqual(reused + 1, VerifiedFileMemo.Reused);
                // The first Inspect re-checks the binding; it never reads the file's bytes again either.
                again.Inspect();
                Assert.AreEqual(passes + 1, VerifiedFileMemo.FullPasses);
            }
            // A plan that pins another hash is not satisfied by the memo.
            var wrong = Throws<AreaException>(() => AreaDataOwner.Open(new([AreaInput.FromFile(key, path)]), Plan(Hash([1, 2, 3])), new()));
            Assert.AreEqual(AreaErrorCode.InputIdentityMismatch, wrong.Issue.Code);
            // A file rewritten to the same length is not satisfied either.
            var changed = Dat(("padding.bin", Bytes(8, 1_150_000)));
            Assert.AreEqual(bytes.Length, changed.Length);
            File.WriteAllBytes(path, changed);
            var stale = Throws<AreaException>(() => AreaDataOwner.Open(new([AreaInput.FromFile(key, path)]), Plan(Hash(bytes)), new()));
            Assert.AreEqual(AreaErrorCode.InputIdentityMismatch, stale.Issue.Code);
            using (AreaDataOwner.Open(new([AreaInput.FromFile(key, path)]), Plan(Hash(changed)), new())) { }
        }
        finally { File.Delete(path); }
    }
}
