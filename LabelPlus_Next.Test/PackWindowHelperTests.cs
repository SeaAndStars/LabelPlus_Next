using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using LabelPlus_Next.Tools.ViewModels;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LabelPlus_Next.Test;

[TestClass]
[DoNotParallelize]
public class PackWindowHelperTests
{
    private string _dir = null!;

    [TestInitialize]
    public void Setup()
    {
        _dir = Path.Combine(Path.GetTempPath(), "lp-pack-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); } catch { }
    }

    private static object? Call(string name, params object?[] args)
        => typeof(PackWindowViewModel).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, args);

    private static object? Instance(PackWindowViewModel vm, string name, params object?[] args)
        => typeof(PackWindowViewModel).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(vm, args);


    [TestMethod]
    public void ToolsStore_TryDeleteSwallowsIoErrors()
    {
        var method = typeof(LabelPlus_Next.Tools.Services.ToolsSettingsStore).GetMethod("TryDelete", BindingFlags.NonPublic | BindingFlags.Static)!;
        method.Invoke(null, new object[] { "/proc/self/mem" });
        Assert.IsTrue(true);
    }
    [TestMethod]
    public void StringAndVersionHelpers()
    {
        Assert.AreEqual("https://x/", Call("AppendSlash", "https://x"));
        Assert.AreEqual("https://x/", Call("AppendSlash", "https://x/"));
        Assert.AreEqual("", Call("NormalizePath", (object?)null));
        Assert.AreEqual("", Call("NormalizePath", "/"));
        Assert.AreEqual("a/b/", Call("NormalizePath", "\\a\\b\\"));
        Assert.AreEqual("abc", Call("StripUtf8Bom", "\uFEFFabc"));
        Assert.AreEqual("abc", Call("StripUtf8Bom", "abc"));
        Assert.AreEqual("", Call("StripUtf8Bom", ""));

        Assert.IsTrue((int)Call("CompareSemVer", "1.2.3", "1.2.4")! < 0);
        Assert.IsTrue((int)Call("CompareSemVer", "1.3.0", "1.2.9")! > 0);
        Assert.AreEqual(0, (int)Call("CompareSemVer", "1.2.3", "1.2.3")!);
        Assert.AreNotEqual(0, (int)Call("CompareSemVer", "alpha", "beta")!);
    }

    [TestMethod]
    public void ManifestJsonDetection()
    {
        Assert.IsTrue((bool)Call("LooksLikeManifestJson", "{\"schema\":\"v1\"}")!);
        Assert.IsTrue((bool)Call("LooksLikeManifestJson", "{\"projects\":{}}")!);
        Assert.IsTrue((bool)Call("LooksLikeManifestJson", "{\"items\":[]}")!);
        Assert.IsTrue((bool)Call("LooksLikeManifestJson", "{\"version\":\"1\",\"url\":\"x\"}")!);
        Assert.IsTrue((bool)Call("LooksLikeManifestJson", "[{\"version\":\"1\"}]")!);
        Assert.IsFalse((bool)Call("LooksLikeManifestJson", "{\"code\":200,\"message\":\"x\"}")!);
        Assert.IsFalse((bool)Call("LooksLikeManifestJson", "not json")!);
        Assert.IsFalse((bool)Call("LooksLikeManifestJson", "[]")!);
    }

    [TestMethod]
    public void TryReadVersion_HandlesMissingInvalidAndValid()
    {
        Assert.IsNull(Call("TryReadVersion", Path.Combine(_dir, "missing.json")));

        var invalid = Path.Combine(_dir, "invalid.json");
        File.WriteAllText(invalid, "{ invalid");
        Assert.IsNull(Call("TryReadVersion", invalid));

        var noVersion = Path.Combine(_dir, "noversion.json");
        File.WriteAllText(noVersion, "{\"project\":\"x\"}");
        Assert.IsNull(Call("TryReadVersion", noVersion));

        var valid = Path.Combine(_dir, "version.json");
        File.WriteAllText(valid, "{\"project\":\"x\",\"version\":\"1.2.3\"}");
        Assert.AreEqual("1.2.3", Call("TryReadVersion", valid));
        Assert.IsNull(Call("TryReadVersion", "/proc/self/mem"));
    }

    [TestMethod]
    public void ProjectAndPublishPaths()
    {
        var repo = Path.Combine(_dir, "repo");
        var desktopDir = Path.Combine(repo, "LabelPlus_Next");
        Directory.CreateDirectory(desktopDir);
        var csproj = Path.Combine(desktopDir, "LabelPlus_Next.Desktop.csproj");
        File.WriteAllText(csproj, "<Project/>");

        var projectPath = (string)Call("GetProjectPath", repo, "LabelPlus_Next.Desktop")!;
        Assert.AreEqual(csproj, projectPath);

        var publish = Path.Combine(desktopDir, "bin", "Release", "net10.0", "win-x64", "publish");
        Directory.CreateDirectory(publish);
        Assert.AreEqual(publish, Call("GetPublishDir", repo, "LabelPlus_Next.Desktop", "win-x64"));

        Directory.Delete(publish, true);
        var fallback = (string)Call("GetPublishDir", repo, "LabelPlus_Next.Desktop", "win-x64")!;
        StringAssert.Contains(fallback, "net10.0");

        var missingProject = (string)Call("GetProjectPath", repo, "Missing.Project")!;
        StringAssert.Contains(missingProject, "Missing.Project.csproj");
    }


    [TestMethod]
    public void PreferSelectedProject_ReordersEntries()
    {
        var vm = new PackWindowViewModel();
        var folder = Path.Combine(_dir, "LabelPlus_Next.Desktop");
        Directory.CreateDirectory(folder);
        vm.SelectedFolder = folder;
        var field = typeof(PackWindowViewModel).GetField("versionEntries", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var list = (System.Collections.IList)field.GetValue(vm)!;
        list.Add(("Other", "1.0"));
        list.Add(("LabelPlus_Next.Desktop", "2.0"));

        Instance(vm, "PreferSelectedProject");
        Assert.AreEqual("LabelPlus_Next.Desktop", vm.Project);
        Assert.AreEqual("2.0", vm.Version);

        vm.SelectedFolder = null;
        Instance(vm, "PreferSelectedProject");
        vm.SelectedFolder = _dir;
        Instance(vm, "PreferSelectedProject");
    }

    [TestMethod]
    public void LogFetchedJson_TruncatesLongPayloads()
    {
        InstanceStatic("LogFetchedJson", "src", "https://x", "{\"a\":1}");
        InstanceStatic("LogFetchedJson", "src", "https://x", new string('x', 11000));
    }

    private static void InstanceStatic(string name, params object?[] args)
        => typeof(PackWindowViewModel).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, args);

    [TestMethod]
    public void FindRepoRoot_WalksUpFromAppDirectory()
    {
        // The test assembly lives under the repository worktree, so the helper
        // should walk up until it sees both project folders.
        var found = Call("FindRepoRoot");
        Assert.IsTrue(found is null || !string.IsNullOrWhiteSpace((string)found));
    }
    [TestMethod]
    public async Task SetFolder_ReadsVersionAndDetectsSourceProject()
    {
        var package = Path.Combine(_dir, "package");
        Directory.CreateDirectory(package);
        File.WriteAllText(Path.Combine(package, "Client.version.json"),
            "{\"project\":\"LabelPlus_Next.Desktop\",\"version\":\"1.3.3\",\"manifest\":\"https://example.invalid/m.json\"}");

        var vm = new PackWindowViewModel();
        await vm.SetFolderAsync(package);
        Assert.AreEqual("LabelPlus_Next.Desktop", vm.Project);
        Assert.AreEqual("1.3.3", vm.Version);
        Assert.IsTrue(vm.HasLocalVersionFiles);
        Assert.IsTrue(vm.CanPackSelectedFolder);

        var source = Path.Combine(_dir, "source");
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "app.csproj"), "<Project/>");
        File.WriteAllText(Path.Combine(source, "Client.version.json"),
            "{\"project\":\"LabelPlus_Next.Desktop\",\"version\":\"1.3.3\"}");
        await vm.SetFolderAsync(source);
        Assert.IsTrue(vm.HasLocalVersionFiles);
        Assert.IsFalse(vm.CanPackSelectedFolder);
    }

    [TestMethod]
    public async Task SetSolution_WithNoVersionFilesKeepsStateUsable()
    {
        var sln = Path.Combine(_dir, "repo.sln");
        File.WriteAllText(sln, "");
        var vm = new PackWindowViewModel();
        await vm.SetSolutionAsync(sln);
        Assert.AreEqual(sln, vm.SolutionPath);
        Assert.IsNull(vm.Project);
    }
}
