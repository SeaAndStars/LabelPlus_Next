using System;
using System.IO;
using System.Threading.Tasks;
using LabelPlus_Next.Tools.Models;
using LabelPlus_Next.Tools.Services;
using LabelPlus_Next.Tools.ViewModels;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LabelPlus_Next.Test;

[TestClass]
public class ToolsSettingsTests
{
    private string tempDir = null!;

    [TestInitialize]
    public void Setup()
    {
        tempDir = Path.Combine(Path.GetTempPath(), "labelplus-tools-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
        catch
        {
            // best effort
        }
    }

    [TestMethod]
    public async Task SaveAndLoadRoundTripsAllSettings()
    {
        var path = Path.Combine(tempDir, "nested", "tools.settings.json");
        var expected = new ToolsSettings
        {
            BaseUrl = "https://alist.example/dav/",
            ApiBaseUrl = "https://alist.example/",
            Username = "user",
            Password = "secret",
            TargetPath = "OneDrive2/Update"
        };

        await ToolsSettingsStore.SaveAsync(path, expected);

        Assert.IsTrue(File.Exists(path), "settings file should be created (including its directory)");
        Assert.AreEqual(0, Directory.GetFiles(Path.GetDirectoryName(path)!, "*.tmp").Length,
            "temporary files must not be left behind");

        var actual = await ToolsSettingsStore.LoadAsync(path);
        Assert.IsNotNull(actual);
        Assert.AreEqual(expected.BaseUrl, actual.BaseUrl);
        Assert.AreEqual(expected.ApiBaseUrl, actual.ApiBaseUrl);
        Assert.AreEqual(expected.Username, actual.Username);
        Assert.AreEqual(expected.Password, actual.Password);
        Assert.AreEqual(expected.TargetPath, actual.TargetPath);

        var syncActual = ToolsSettingsStore.Load(path);
        Assert.IsNotNull(syncActual);
        Assert.AreEqual(expected.BaseUrl, syncActual.BaseUrl);
    }

    [TestMethod]
    public async Task LoadMissingFileReturnsNull()
    {
        var path = Path.Combine(tempDir, "missing.json");
        Assert.IsNull(await ToolsSettingsStore.LoadAsync(path));
        Assert.IsNull(ToolsSettingsStore.Load(path));
    }

    [TestMethod]
    public void ResolveApiBaseUrlPrefersExplicitApiBase()
    {
        Assert.AreEqual(
            "https://host/api-root/",
            ToolsEndpoints.ResolveApiBaseUrl("https://host/api-root", "https://host/dav/"));
    }

    [TestMethod]
    public void ResolveApiBaseUrlFixesWebDavUrlPastedIntoApiBase()
    {
        Assert.AreEqual("https://host/", ToolsEndpoints.ResolveApiBaseUrl("https://host/dav/", "https://host/dav/"));
        Assert.AreEqual("https://host/", ToolsEndpoints.ResolveApiBaseUrl("https://host/dav", null));
    }

    [TestMethod]
    public void ResolveApiBaseUrlStripsDavFromWebDavEndpoint()
    {
        Assert.AreEqual("https://host/", ToolsEndpoints.ResolveApiBaseUrl(null, "https://host/dav/"));
        Assert.AreEqual("https://host/", ToolsEndpoints.ResolveApiBaseUrl("", "https://host/dav"));
        Assert.AreEqual("https://host/alist/", ToolsEndpoints.ResolveApiBaseUrl(" ", "https://host/alist/dav/"));
        Assert.AreEqual("https://host:8443/", ToolsEndpoints.ResolveApiBaseUrl(null, "https://host:8443/dav/"));
    }

    [TestMethod]
    public void ResolveApiBaseUrlKeepsSiteRoots()
    {
        Assert.AreEqual("https://host/", ToolsEndpoints.ResolveApiBaseUrl(null, "https://host"));
        Assert.IsNull(ToolsEndpoints.ResolveApiBaseUrl(null, null));
        Assert.IsNull(ToolsEndpoints.ResolveApiBaseUrl("  ", "  "));
    }

    [TestMethod]
    public void ViewModelLoadsSettingsOnConstruction()
    {
        var path = Path.Combine(tempDir, "tools.settings.json");
        ToolsSettingsStore.Save(path, new ToolsSettings
        {
            BaseUrl = "https://alist.example/dav/",
            ApiBaseUrl = "https://alist.example/",
            Username = "saved-user",
            Password = "saved-pass",
            TargetPath = "backups"
        });

        var vm = new MainWindowViewModel(path);

        Assert.AreEqual("https://alist.example/dav/", vm.BaseUrl);
        Assert.AreEqual("https://alist.example/", vm.ApiBaseUrl);
        Assert.AreEqual("saved-user", vm.Username);
        Assert.AreEqual("saved-pass", vm.Password);
        Assert.AreEqual("backups", vm.TargetPath);
    }

    [TestMethod]
    public async Task ViewModelSavesChangesImmediately()
    {
        var path = Path.Combine(tempDir, "tools.settings.json");
        ToolsSettingsStore.Save(path, new ToolsSettings { BaseUrl = "https://old.example/dav/" });
        var vm = new MainWindowViewModel(path);

        vm.BaseUrl = "https://new.example/dav/";
        vm.ApiBaseUrl = "https://new.example/";
        vm.Username = "changed-user";

        var saved = await vm.SaveSettingsNowAsync();
        Assert.IsTrue(saved);

        var reloaded = ToolsSettingsStore.Load(path);
        Assert.IsNotNull(reloaded);
        Assert.AreEqual("https://new.example/dav/", reloaded.BaseUrl);
        Assert.AreEqual("https://new.example/", reloaded.ApiBaseUrl);
        Assert.AreEqual("changed-user", reloaded.Username);
    }

    [TestMethod]
    public async Task ViewModelAutoSavesDebouncedChanges()
    {
        var path = Path.Combine(tempDir, "tools.settings.json");
        ToolsSettingsStore.Save(path, new ToolsSettings { BaseUrl = "https://old.example/dav/" });
        var vm = new MainWindowViewModel(path);

        vm.Password = "auto-saved";

        ToolsSettings? reloaded = null;
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            reloaded = ToolsSettingsStore.Load(path);
            if (reloaded?.Password == "auto-saved") break;
            await Task.Delay(50);
        }

        Assert.IsNotNull(reloaded);
        Assert.AreEqual("auto-saved", reloaded.Password, "a setting change should be persisted without an explicit save");
    }

    [TestMethod]
    public async Task ViewModelFlushesPendingChangeOnExit()
    {
        var path = Path.Combine(tempDir, "tools.settings.json");
        ToolsSettingsStore.Save(path, new ToolsSettings { BaseUrl = "https://old.example/dav/" });
        var vm = new MainWindowViewModel(path);

        vm.TargetPath = "flushed-on-exit";
        vm.FlushSettings();

        var reloaded = ToolsSettingsStore.Load(path);
        Assert.IsNotNull(reloaded);
        Assert.AreEqual("flushed-on-exit", reloaded.TargetPath);
    }
}
