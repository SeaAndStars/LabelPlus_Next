using System;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;
using LabelPlus_Next.Shared;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LabelPlus_Next.Test;

[TestClass]
[DoNotParallelize]
public class UpdateFilesEdgeTests
{
    private string _dir = null!;

    [TestInitialize]
    public void Setup()
    {
        _dir = Path.Combine(Path.GetTempPath(), "lp-update-files-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); } catch { }
    }

    [TestMethod]
    public void IsValidZip_RejectsMissingAndInvalidArchives()
    {
        Assert.IsFalse(UpdateFiles.IsValidZip(Path.Combine(_dir, "missing.zip")));
        var invalid = Path.Combine(_dir, "invalid.zip");
        File.WriteAllText(invalid, "not a zip");
        Assert.IsFalse(UpdateFiles.IsValidZip(invalid));

        var valid = Path.Combine(_dir, "valid.zip");
        using (var zip = ZipFile.Open(valid, ZipArchiveMode.Create))
        using (var s = zip.CreateEntry("a.txt").Open())
            s.WriteByte(1);
        Assert.IsTrue(UpdateFiles.IsValidZip(valid));
    }

    [TestMethod]
    public void TryDeleteAndTryDeleteDirectory_HandleExistingAndMissing()
    {
        var file = Path.Combine(_dir, "file.txt");
        File.WriteAllText(file, "x");
        UpdateFiles.TryDelete(file);
        Assert.IsFalse(File.Exists(file));
        UpdateFiles.TryDelete(file);

        var sub = Path.Combine(_dir, "sub");
        Directory.CreateDirectory(sub);
        File.WriteAllText(Path.Combine(sub, "x.txt"), "x");
        UpdateFiles.TryDeleteDirectory(sub);
        Assert.IsFalse(Directory.Exists(sub));
        UpdateFiles.TryDeleteDirectory(sub);

        if (OperatingSystem.IsLinux())
        {
            UpdateFiles.TryDelete("/proc/self/mem");
            UpdateFiles.TryDeleteDirectory("/proc/self");
        }
    }

    [TestMethod]
    public void NewTempFile_UsesExtensionAndIsUnique()
    {
        var a = UpdateFiles.NewTempFile();
        var b = UpdateFiles.NewTempFile(".bin");
        Assert.IsTrue(a.EndsWith(".zip"));
        Assert.IsTrue(b.EndsWith(".bin"));
        Assert.AreNotEqual(a, b);
        Assert.IsFalse(File.Exists(a));
        Assert.IsFalse(File.Exists(b));
    }

    [TestMethod]
    public async Task MatchesSha256_HandlesWhitespaceAndCase()
    {
        var file = Path.Combine(_dir, "hash.txt");
        await File.WriteAllTextAsync(file, "hello");
        var hash = await UpdateFiles.ComputeSha256Async(file);
        Assert.AreEqual(hash, hash.ToLowerInvariant());
        Assert.IsTrue(await UpdateFiles.MatchesSha256Async(file, "  "));
        Assert.IsTrue(await UpdateFiles.MatchesSha256Async(file, " " + hash.ToUpperInvariant() + " "));
        Assert.IsFalse(await UpdateFiles.MatchesSha256Async(file, new string('0', 64)));
    }
}
