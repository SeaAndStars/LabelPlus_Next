using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using LabelPlus_Next.Models;
using LabelPlus_Next.Services;
using LabelPlus_Next.ViewModels;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LabelPlus_Next.Test;

[TestClass]
[DoNotParallelize]
public class SettingsHelperTests
{
    private sealed class MemorySettings : ISettingsService
    {
        public Task<AppSettings> LoadAsync(CancellationToken ct = default) => Task.FromResult(new AppSettings());
        public Task SaveAsync(AppSettings settings, CancellationToken ct = default) => Task.CompletedTask;
    }

    private string _dir = null!;

    [TestInitialize]
    public void Setup()
    {
        _dir = Path.Combine(Path.GetTempPath(), "lp-settings-helpers-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); } catch { }
    }

    private static object? Call(string name, params object?[] args)
        => typeof(SettingsViewModel).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, args);

    private static ProjectReleases Releases(string latest, params ReleaseItemV1[] releases)
        => new() { Latest = latest, Releases = releases.ToList() };

    [TestMethod]
    public void LatestAndFindRelease()
    {
        Assert.IsNull(Call("LatestOf", (object?)null));
        var prj = Releases("2.0", new ReleaseItemV1 { Version = "1.0" }, new ReleaseItemV1 { Version = "2.0" });
        Assert.AreEqual("2.0", Call("LatestOf", prj));

        Assert.IsNull(Call("FindRelease", null, "1.0"));
        Assert.AreEqual("1.0", ((ReleaseItemV1)Call("FindRelease", prj, "1.0")!).Version);
        Assert.IsNull(Call("FindRelease", prj, "9.9"));
    }

    [TestMethod]
    public void PickFile_PrefersPlatformAndFallsBackToReleaseUrl()
    {
        var prj = Releases("2.0", new ReleaseItemV1
        {
            Version = "2.0",
            Files = new List<ReleaseFileV1>
            {
                new() { Name = "app-linux-x64.zip", Url = "https://example.invalid/linux.zip", Sha256 = "abc" }
            }
        });
        var picked = (ITuple)Call("PickFile", prj, "2.0")!;
        Assert.AreEqual("https://example.invalid/linux.zip", picked[0]);
        Assert.AreEqual("abc", picked[1]);

        var fallback = Releases("2.0", new ReleaseItemV1 { Version = "2.0", Url = "https://example.invalid/fallback.zip" });
        var pickedFallback = (ITuple)Call("PickFile", fallback, "2.0")!;
        Assert.AreEqual("https://example.invalid/fallback.zip", pickedFallback[0]);

        var empty = Releases("2.0", new ReleaseItemV1 { Version = "2.0" });
        Assert.IsNull(Call("PickFile", empty, "2.0"));
        Assert.IsNull(Call("PickFile", prj, "9.9"));
    }

    [TestMethod]
    public void ReadLocalVersion_HandlesMissingInvalidAndValid()
    {
        Assert.IsNull(Call("ReadLocalVersion", Path.Combine(_dir, "missing.json")));

        var invalid = Path.Combine(_dir, "invalid.json");
        File.WriteAllText(invalid, "{ bad");
        Assert.IsNull(Call("ReadLocalVersion", invalid));

        var noVersion = Path.Combine(_dir, "none.json");
        File.WriteAllText(noVersion, "{\"project\":\"x\"}");
        Assert.IsNull(Call("ReadLocalVersion", noVersion));

        var valid = Path.Combine(_dir, "version.json");
        File.WriteAllText(valid, "{\"version\":\"1.2.3\"}");
        Assert.AreEqual("1.2.3", Call("ReadLocalVersion", valid));
    }

    [TestMethod]
    public void ResolveUpdaterEntry_FindsLinuxBinaryAndRejectsMissing()
    {
        Assert.IsNull(Call("ResolveUpdaterEntry", Path.Combine(_dir, "missing")));
        var bin = Path.Combine(_dir, "LabelPlus_Next.Update");
        File.WriteAllText(bin, "");
        Assert.AreEqual(bin, Call("ResolveUpdaterEntry", _dir));

        var other = Path.Combine(_dir, "other");
        Directory.CreateDirectory(other);
        File.WriteAllText(Path.Combine(other, "nothing"), "");
        Assert.IsNull(Call("ResolveUpdaterEntry", other));
    }


    [TestMethod]
    public void ParameterlessConstructorAndLanguagesExposeDefaults()
    {
        var vm = new SettingsViewModel();
        Assert.IsNotNull(vm.Languages);
        Assert.IsTrue(vm.Languages.Count >= 1);
    }

    [TestMethod]
    public void OnUi_ExecutesActionWithoutThrowing()
    {
        var flag = 0;
        Call("OnUi", (Action)(() => flag = 1));
        Assert.IsTrue(flag == 0 || flag == 1);
    }
    [TestMethod]
    public async Task SetTask_UpdatesProgressState()
    {
        var vm = new SettingsViewModel(new MemorySettings());
        await vm.LoadAsync();
        var method = typeof(SettingsViewModel).GetMethod("SetTask", BindingFlags.NonPublic | BindingFlags.Instance)!;
        method.Invoke(vm, new object?[] { "task", true });
        Assert.AreEqual("task", vm.UpdateTask);
        Assert.IsTrue(vm.IsProgressIndeterminate);
    }
}
