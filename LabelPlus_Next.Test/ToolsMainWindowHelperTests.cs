using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using LabelPlus_Next.Tools.Models;
using LabelPlus_Next.Tools.ViewModels;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LabelPlus_Next.Test;

[TestClass]
[DoNotParallelize]
public class ToolsMainWindowHelperTests
{
    private string _dir = null!;

    [TestInitialize]
    public void Setup()
    {
        _dir = Path.Combine(Path.GetTempPath(), "lp-tools-main-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); } catch { }
    }

    private static object? Call(string name, params object?[] args)
        => typeof(MainWindowViewModel).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, args);

    private static object? Instance(MainWindowViewModel vm, string name, params object?[] args)
        => typeof(MainWindowViewModel).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(vm, args);

    private MainWindowViewModel NewVm()
        => new(Path.Combine(_dir, "tools.settings.json"));

    [TestMethod]
    public void PathAndUrlHelpers()
    {
        Assert.AreEqual("/a", Call("CombineDavPath", "", "a"));
        Assert.AreEqual("/dav/a", Call("CombineDavPath", "dav", "a"));
        Assert.AreEqual("/dav/a", Call("CombineDavPath", "/dav/", "a"));

        Assert.IsFalse((bool)Call("ShouldTryDavSuffix", "https://x/dav", 404)!);
        Assert.IsTrue((bool)Call("ShouldTryDavSuffix", "https://x", 404)!);
        Assert.IsTrue((bool)Call("ShouldTryDavSuffix", "https://x", 405)!);
        Assert.IsFalse((bool)Call("ShouldTryDavSuffix", "not a url", 404)!);
        Assert.IsFalse((bool)Call("ShouldTryDavSuffix", "https://x", 200)!);

        Assert.AreEqual("https://x/", Call("NormalizeBaseUrl", " https://x "));
        Assert.AreEqual("https://x/", Call("EnsureSlash", "https://x"));
        Assert.AreEqual("https://x/", Call("EnsureSlash", "https://x/"));

        Assert.IsNull(Call("NormalizeUrl", (object?)null));
        Assert.IsNull(Call("NormalizeUrl", " "));
        Assert.AreEqual("https://x/a", Call("NormalizeUrl", "https://x/a/"));
        Assert.AreEqual("not a url", Call("NormalizeUrl", "not a url"));

        StringAssert.Contains((string)Call("BuildListingError", 400, null)!, "400");
        StringAssert.Contains((string)Call("BuildListingError", 401, "denied")!, "denied");
        StringAssert.Contains((string)Call("BuildListingError", 404, "")!, "WebDAV");
        StringAssert.Contains((string)Call("BuildListingError", 500, null)!, "500");

        Assert.AreEqual("a/b/f%20x.png", Call("BuildRemotePath", "a/b", "f x.png"));
        Assert.AreEqual("f.png", Call("BuildRemotePath", (object?)null, "f.png"));
    }

    [TestMethod]
    public void TransientChecks()
    {
        Assert.IsTrue((bool)Call("IsTransientStatus", 408)!);
        Assert.IsTrue((bool)Call("IsTransientStatus", 429)!);
        Assert.IsTrue((bool)Call("IsTransientStatus", 500)!);
        Assert.IsTrue((bool)Call("IsTransientStatus", 599)!);
        Assert.IsFalse((bool)Call("IsTransientStatus", 400)!);
        Assert.IsFalse((bool)Call("IsTransientStatus", 600)!);

        Assert.IsTrue((bool)Call("IsTransientException", new TaskCanceledException())!);
        Assert.IsTrue((bool)Call("IsTransientException", new IOException())!);
        Assert.IsTrue((bool)Call("IsTransientException", new HttpRequestException())!);
        Assert.IsFalse((bool)Call("IsTransientException", new InvalidOperationException())!);
    }

    [TestMethod]
    public void ResolveAndRelativeUrls()
    {
        var vm = NewVm();
        vm.BaseUrl = "https://host/dav/";
        Assert.AreEqual("https://host/dav/file.png", Instance(vm, "ResolveToAbsolute", "file.png"));
        Assert.AreEqual("https://host/dav/file.png", Instance(vm, "ResolveToAbsolute", "/dav/file.png"));
        Assert.AreEqual("https://host/dav/file.png", Instance(vm, "ResolveToAbsolute", "dav/file.png"));
        Assert.AreEqual("https://host/dav/file.png", Instance(vm, "ResolveToAbsolute", "https://host/dav/file.png"));
        Assert.AreEqual("https://host/dav/file.png", Instance(vm, "ResolveToAbsolute", "file:///dav/file.png"));
        Assert.AreEqual("https://host/dav/", Instance(vm, "ResolveToAbsolute", ""));

        Assert.AreEqual("a/b", Instance(vm, "ToRelativePath", "https://host/dav/a/b"));
        Assert.IsNull(Instance(vm, "ToRelativePath", "not a url"));
        vm.BaseUrl = "";
        Assert.IsNull(Instance(vm, "ToRelativePath", "https://host/dav/a"));
    }

    [TestMethod]
    public void BuildAbsoluteTarget_UsesDestOrTargetPath()
    {
        var vm = NewVm();
        vm.BaseUrl = "https://host/dav/";
        vm.TargetPath = "/dir";

        Assert.AreEqual("https://host/dav/dir/f x.png", Instance(vm, "BuildAbsoluteTarget", (object?)null, "f x.png"));
        Assert.AreEqual("https://host/dav/other/f.png", Instance(vm, "BuildAbsoluteTarget", "other", "f.png"));
        Assert.AreEqual("https://host/dav/other/f.png", Instance(vm, "BuildAbsoluteTarget", "https://host/dav/other/", "f.png"));
    }


    [TestMethod]
    public void ConstructorPropertiesAndClientSetupWork()
    {
        var parameterless = new MainWindowViewModel();
        Assert.IsNotNull(parameterless.SettingsFilePath);
        var vm = new MainWindowViewModel(Path.Combine(_dir, "default.settings.json"));
        vm.Status = "ready";
        Assert.AreEqual("ready", vm.Status);
        vm.IsLoading = true;
        Assert.IsTrue(vm.IsLoading);
        vm.IsUploading = true;
        Assert.IsTrue(vm.IsUploading);
        vm.BaseUrl = "https://dav.invalid/";
        vm.Username = "u";
        vm.Password = "p";
        Instance(vm, "EnsureClient", false);
        Instance(vm, "EnsureClient", false);
        Instance(vm, "EnsureClient", true);

        var node = new DavNode { IsCollection = true };
        Call("EnsurePlaceholderForFolder", node);
        Assert.AreEqual(1, node.Children.Count);

        Instance(vm, "BeginTreeLoad");
        Assert.AreEqual("正在读取目录…", vm.TreeMessage);
        Instance(vm, "ShowTreeMessage", "err", true);
        Assert.IsTrue(vm.HasTreeError);
        Instance(vm, "HideTreeMessage");
        Assert.IsFalse(vm.IsTreeMessageVisible);
    }

    [TestMethod]
    public void LoadAndFlushSettings_FailuresAreSwallowed()
    {
        var bad = new MainWindowViewModel("/proc/self/mem");
        Assert.IsNotNull(bad.SettingsFilePath);

        var vm = new MainWindowViewModel("/proc/self/mem");
        vm.BaseUrl = "https://dav.invalid/";
        vm.FlushSettings();
        Assert.IsNotNull(vm.BaseUrl);
    }
    [TestMethod]
    public async Task SnapshotAndSavePersistSettings()
    {
        var path = Path.Combine(_dir, "tools.settings.json");
        var vm = new MainWindowViewModel(path);
        vm.BaseUrl = "https://dav.invalid/";
        vm.ApiBaseUrl = "https://api.invalid/";
        vm.Username = "u";
        vm.Password = "p";
        vm.TargetPath = "/t";
        vm.UploadConcurrency = 0;

        Assert.AreEqual(1, vm.UploadConcurrency);
        var snapshot = (LabelPlus_Next.Tools.Models.ToolsSettings)Instance(vm, "CreateSettingsSnapshot")!;
        Assert.AreEqual("https://dav.invalid/", snapshot.BaseUrl);

        Assert.IsTrue(await vm.SaveSettingsNowAsync());
        Assert.IsTrue(File.Exists(path));
    }
}
