using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading.Tasks;
using LabelPlus_Next.Shared;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LabelPlus_Next.Test;

[TestClass]
public class UpdateKitTests
{
    private string _root = null!;

    [TestInitialize]
    public void Init()
    {
        _root = Path.Combine(Path.GetTempPath(), "lpn-kit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    [DataTestMethod]
    [DataRow("1.10.0", "1.9.0", true)]   // 旧实现在 Version.TryParse 失败时按字符串比较会出错
    [DataRow("v1.2.5", "1.2.4", true)]
    [DataRow("1.2.4", "1.2.4.0", false)]
    [DataRow("1.3.0-beta.2", "1.3.0-beta.10", false)]
    [DataRow("1.3.0", "1.3.0-rc.1", true)]
    [DataRow("1.3.0-rc.1", "1.2.9", true)]
    [DataRow("1.2.4+build.7", "1.2.4", false)]
    [DataRow("", "1.0.0", false)]
    [DataRow("1.0.0", null, true)]
    public void 版本比较_语义化(string? remote, string? local, bool expected)
        => Assert.AreEqual(expected, UpdateVersion.IsNewer(remote, local));

    [DataTestMethod]
    [DataRow("https://h.example/d/OneDrive2/Update/a%20b.zip", "/OneDrive2/Update/a b.zip")]
    [DataRow("https://h.example/dav/OneDrive2/x.zip?sign=1", "/OneDrive2/x.zip")]
    [DataRow("https://h.example/david/x.zip", null)]
    [DataRow("https://h.example/OneDrive2/x.zip", null)]
    public void AList路径解析(string url, string? expected)
    {
        var ok = AlistUrl.TryGetStoragePath(url, out var path);
        Assert.AreEqual(expected is not null, ok);
        if (expected is not null) Assert.AreEqual(expected, path);
    }

    [TestMethod]
    public void AList公链_补充download参数()
    {
        Assert.AreEqual("https://h/d/a.zip?download=1", AlistUrl.WithDownloadFlag("https://h/d/a.zip"));
        Assert.AreEqual("https://h/d/a.zip?sign=x&download=1", AlistUrl.WithDownloadFlag("https://h/d/a.zip?sign=x"));
        Assert.AreEqual("https://h/raw/a.zip", AlistUrl.WithDownloadFlag("https://h/raw/a.zip"));
    }

    [TestMethod]
    public async Task SHA256_不匹配应失败()
    {
        var f = Path.Combine(_root, "f.bin");
        await File.WriteAllTextAsync(f, "hello");
        const string sha = "2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824";
        Assert.IsTrue(await UpdateFiles.MatchesSha256Async(f, sha.ToUpperInvariant()));
        Assert.IsTrue(await UpdateFiles.MatchesSha256Async(f, null));
        Assert.IsFalse(await UpdateFiles.MatchesSha256Async(f, new string('0', 64)));
    }

    [TestMethod]
    public async Task 分阶段安装_替换文件并跳过update目录()
    {
        var target = Path.Combine(_root, "app");
        Directory.CreateDirectory(Path.Combine(target, "update"));
        await File.WriteAllTextAsync(Path.Combine(target, "a.txt"), "old");
        await File.WriteAllTextAsync(Path.Combine(target, "keep.txt"), "keep");
        await File.WriteAllTextAsync(Path.Combine(target, "update", "u.txt"), "updater-old");
        var zip = MakeZip(("a.txt", "new"), ("sub/b.txt", "added"), ("update/u.txt", "updater-new"));

        var count = await StagedInstaller.ApplyZipAsync(zip, target, rel => rel.StartsWith("update/", StringComparison.OrdinalIgnoreCase));

        Assert.AreEqual(2, count);
        Assert.AreEqual("new", await File.ReadAllTextAsync(Path.Combine(target, "a.txt")));
        Assert.AreEqual("added", await File.ReadAllTextAsync(Path.Combine(target, "sub", "b.txt")));
        Assert.AreEqual("keep", await File.ReadAllTextAsync(Path.Combine(target, "keep.txt")));
        Assert.AreEqual("updater-old", await File.ReadAllTextAsync(Path.Combine(target, "update", "u.txt")));
        CollectionAssert.AreEquivalent(new[] { "sub", "update" }, Array.ConvertAll(Directory.GetDirectories(target), Path.GetFileName));
    }

    [TestMethod]
    public async Task 分阶段安装_越界条目应中止且不改动目标()
    {
        var target = Path.Combine(_root, "app");
        Directory.CreateDirectory(target);
        await File.WriteAllTextAsync(Path.Combine(target, "a.txt"), "old");
        // "app2" 与 "app" 前缀相同：旧实现的 StartsWith(destRoot) 检查会放行
        var zip = MakeZip(("a.txt", "new"), ("../app2/evil.txt", "x"));

        await Assert.ThrowsExceptionAsync<InvalidDataException>(() => StagedInstaller.ApplyZipAsync(zip, target));

        Assert.AreEqual("old", await File.ReadAllTextAsync(Path.Combine(target, "a.txt")));
        Assert.IsFalse(File.Exists(Path.Combine(_root, "app2", "evil.txt")));
        Assert.AreEqual(0, Directory.GetDirectories(target).Length, "staging 目录应被清理");
    }

    [TestMethod]
    public async Task 分阶段安装_替换中途失败应回滚()
    {
        var target = Path.Combine(_root, "app");
        Directory.CreateDirectory(target);
        await File.WriteAllTextAsync(Path.Combine(target, "a.txt"), "old-a");
        // 目标位置存在同名目录，移动文件时必然失败
        Directory.CreateDirectory(Path.Combine(target, "z.txt"));
        var zip = MakeZip(("a.txt", "new-a"), ("n.txt", "new-file"), ("z.txt", "boom"));

        await Assert.ThrowsExceptionAsync<IOException>(() => StagedInstaller.ApplyZipAsync(zip, target));

        Assert.AreEqual("old-a", await File.ReadAllTextAsync(Path.Combine(target, "a.txt")));
        Assert.IsFalse(File.Exists(Path.Combine(target, "n.txt")), "新增文件应被回滚删除");
        CollectionAssert.AreEquivalent(new[] { "z.txt" }, Array.ConvertAll(Directory.GetDirectories(target), Path.GetFileName));
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
}
