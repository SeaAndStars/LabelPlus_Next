using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LabelPlus_Next.Shared;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LabelPlus_Next.Test;

[TestClass]
[DoNotParallelize]
public class StagedInstallerEdgeTests
{
    private sealed class RecordingProgress : IProgress<double>
    {
        public List<double> Values { get; } = new();
        private readonly Action<double>? _onReport;
        public RecordingProgress(Action<double>? onReport = null) => _onReport = onReport;
        public void Report(double value)
        {
            Values.Add(value);
            _onReport?.Invoke(value);
        }
    }

    private string _root = null!;

    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "lp-staged-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, true); } catch { }
    }

    private string MakeZip(params (string Name, string Content)[] entries)
    {
        var path = Path.Combine(_root, Guid.NewGuid().ToString("N") + ".zip");
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (name, content) in entries)
        {
            using var s = zip.CreateEntry(name).Open();
            s.Write(Encoding.UTF8.GetBytes(content));
        }
        return path;
    }

    [TestMethod]
    public async Task ApplyZip_SkipsInternalPrefixesDirectoriesAndReportsProgress()
    {
        var target = Path.Combine(_root, "app");
        Directory.CreateDirectory(target);
        var zip = MakeZip(
            ("ok.txt", "ok"),
            (".lpn-staging-old/x.txt", "skip"),
            (".lpn-backup-old/y.txt", "skip"),
            ("folder/", ""));

        var progress = new RecordingProgress();
        var count = await StagedInstaller.ApplyZipAsync(zip, target, progress: progress);

        Assert.AreEqual(1, count);
        Assert.AreEqual("ok", await File.ReadAllTextAsync(Path.Combine(target, "ok.txt")));
        Assert.IsTrue(progress.Values.Count >= 2);
        Assert.IsTrue(progress.Values.Exists(v => v > 0 && v < 80));
        Assert.IsTrue(progress.Values.Contains(100));
        Assert.IsFalse(Directory.Exists(Path.Combine(target, ".lpn-staging-old")));
        Assert.IsFalse(Directory.Exists(Path.Combine(target, ".lpn-backup-old")));
    }

    [TestMethod]
    public async Task ApplyZip_CancelDuringSwapRollsBack()
    {
        var target = Path.Combine(_root, "app");
        Directory.CreateDirectory(target);
        await File.WriteAllTextAsync(Path.Combine(target, "a.txt"), "old-a");
        var zip = MakeZip(("a.txt", "new-a"), ("b.txt", "new-b"));

        using var cts = new CancellationTokenSource();
        var progress = new RecordingProgress(p => { if (p > 80) cts.Cancel(); });

        await Assert.ThrowsExceptionAsync<OperationCanceledException>(() =>
            StagedInstaller.ApplyZipAsync(zip, target, progress: progress, ct: cts.Token));

        Assert.AreEqual("old-a", await File.ReadAllTextAsync(Path.Combine(target, "a.txt")));
        Assert.IsFalse(File.Exists(Path.Combine(target, "b.txt")));
        Assert.IsFalse(Directory.Exists(Path.Combine(target, "a.txt")));
        Assert.AreEqual(0, Directory.GetDirectories(target).Length, "staging/backup should be cleaned");
    }

    [TestMethod]
    public async Task ApplyZip_PreCanceledTokenDoesNotModifyTarget()
    {
        var target = Path.Combine(_root, "app");
        Directory.CreateDirectory(target);
        var zip = MakeZip(("a.txt", "new"));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsExceptionAsync<TaskCanceledException>(() =>
            StagedInstaller.ApplyZipAsync(zip, target, ct: cts.Token));

        Assert.IsFalse(File.Exists(Path.Combine(target, "a.txt")));
    }
}
