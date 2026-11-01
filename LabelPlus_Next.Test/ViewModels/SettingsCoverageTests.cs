using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LabelPlus_Next.Models;
using LabelPlus_Next.Services;
using LabelPlus_Next.ViewModels;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ToolsMainWindowViewModel = LabelPlus_Next.Tools.ViewModels.MainWindowViewModel;
using ToolsServerSettingsViewModel = LabelPlus_Next.Tools.ViewModels.ServerSettingsViewModel;

namespace LabelPlus_Next.Test.ViewModels;

[TestClass]
[DoNotParallelize]
public class SettingsCoverageTests
{
    private sealed class MemorySettings : ISettingsService
    {
        public AppSettings Data { get; set; } = new() { Language = "en" };
        public Exception? LoadError { get; set; }
        public Exception? SaveError { get; set; }
        public int Saves { get; private set; }

        public Task<AppSettings> LoadAsync(CancellationToken ct = default)
            => LoadError is null ? Task.FromResult(Data) : Task.FromException<AppSettings>(LoadError);
        public Task SaveAsync(AppSettings settings, CancellationToken ct = default)
        {
            if (SaveError is not null) return Task.FromException(SaveError);
            Data = settings;
            Saves++;
            return Task.CompletedTask;
        }
    }

    private sealed class StubUpdateService : IUpdateService
    {
        private readonly Func<UpdateSettings, CancellationToken, Task<UpdateManifest?>> _impl;
        public StubUpdateService(Func<UpdateSettings, CancellationToken, Task<UpdateManifest?>> impl) => _impl = impl;
        public Task<UpdateManifest?> FetchManifestAsync(UpdateSettings upd, CancellationToken ct = default) => _impl(upd, ct);
    }

    private string _dir = null!;

    [TestInitialize]
    public void Setup()
    {
        _dir = Path.Combine(Path.GetTempPath(), "lp-settings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        LocalizationService.Apply("en");
    }

    [TestCleanup]
    public void Cleanup()
    {
        LocalizationService.Apply("default");
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); } catch { }
    }

    [TestMethod]
    public async Task LoadAsync_AppliesLanguageOverlayAndDeeplinkSettings()
    {
        var mem = new MemorySettings
        {
            Data = new AppSettings
            {
                Language = "zh-CN",
                LabelOverlay = new LabelOverlaySettings
                {
                    LabelIndexFontScale = 1.4,
                    LabelTipFontScale = 1.6,
                    LabelTipBackgroundOpacity = 0.3
                },
                Update = new UpdateSettings
                {
                    BaseUrl = "https://example.invalid",
                    ManifestPath = "/m.json",
                    Username = "u",
                    Password = "p",
                    DeeplinkCallbackUrl = "https://example.invalid/cb",
                    DeeplinkCallbackAuth = "Bearer x",
                    AllowLocalHttpFallback = true
                }
            }
        };
        var vm = new SettingsViewModel(mem);

        await vm.LoadAsync();

        Assert.AreEqual(1.4, vm.LabelIndexFontScale);
        Assert.AreEqual(1.6, vm.LabelTipFontScale);
        Assert.AreEqual(0.3, vm.LabelTipBackgroundOpacity);
        Assert.AreEqual("https://example.invalid", vm.BaseUrl);
        Assert.AreEqual("/m.json", vm.ManifestPath);
        Assert.AreEqual("u", vm.Username);
        Assert.AreEqual("p", vm.Password);
        Assert.AreEqual("https://example.invalid/cb", vm.DeeplinkCallbackUrl);
        Assert.AreEqual("Bearer x", vm.DeeplinkCallbackAuth);
        Assert.IsTrue(vm.AllowLocalHttpFallback);
        Assert.AreEqual("zh-CN", vm.SelectedLanguage!.Code);
        Assert.AreEqual("zh-CN", LocalizationService.CurrentCulture.Name);
    }

    [TestMethod]
    public async Task LoadAsync_FailureSetsStatus()
    {
        var mem = new MemorySettings { LoadError = new InvalidOperationException("boom") };
        var vm = new SettingsViewModel(mem);
        vm.SelectedLanguage = LocalizationService.Options.First(o => o.Code == "en");
        await vm.LoadAsync();
        StringAssert.Contains(vm.Status ?? string.Empty, "Failed to load");
    }

    [TestMethod]
    public async Task SaveAsync_PersistsLanguageOverlayAndRaisesEvent()
    {
        var mem = new MemorySettings();
        var vm = new SettingsViewModel(mem);
        await vm.LoadAsync();
        vm.BaseUrl = "https://example.invalid";
        vm.ManifestPath = "/x.json";
        vm.Username = "u";
        vm.Password = "p";
        vm.LabelIndexFontScale = 2;
        vm.LabelTipFontScale = 0.5;
        vm.LabelTipBackgroundOpacity = 1;
        vm.SelectedLanguage = LocalizationService.Options[1]; // zh-CN
        var raised = 0;
        vm.LabelOverlaySettingsChanged += (_, _) => raised++;

        await vm.SaveAsync();

        Assert.IsTrue(raised >= 1);
        Assert.AreEqual("zh-CN", mem.Data.Language);
        Assert.AreEqual("https://example.invalid", mem.Data.Update.BaseUrl);
        Assert.AreEqual(2, mem.Data.LabelOverlay.LabelIndexFontScale);
        Assert.IsTrue(mem.Saves >= 1);
        Assert.IsFalse(string.IsNullOrEmpty(vm.Status));
    }

    [TestMethod]
    public async Task SaveAsync_FailureSetsStatus()
    {
        var mem = new MemorySettings { SaveError = new InvalidOperationException("no write") };
        var vm = new SettingsViewModel(mem);
        await vm.LoadAsync();
        await vm.SaveAsync();
        StringAssert.Contains(vm.Status ?? string.Empty, "Save failed");
    }

    [TestMethod]
    public async Task VerifyHttp_RejectsEmptyConfiguration()
    {
        var vm = new SettingsViewModel(new MemorySettings());
        await vm.LoadAsync();
        vm.BaseUrl = "";
        await vm.VerifyHttpAsync();
        StringAssert.Contains(vm.Status ?? string.Empty, "BaseUrl");

        vm.BaseUrl = "https://example.invalid";
        vm.ManifestPath = "";
        await vm.VerifyHttpAsync();
        StringAssert.Contains(vm.Status ?? string.Empty, "manifest");
    }

    [TestMethod]
    public async Task VerifyViaUpdateService_SuccessJsonAndGenericFailure()
    {
        var vm = new SettingsViewModel(new MemorySettings());
        await vm.LoadAsync();

        var ok = new StubUpdateService((_, __) => Task.FromResult<UpdateManifest?>(new UpdateManifest
        {
            Files = Array.Empty<UpdateFile>()
        }));
        await vm.VerifyViaUpdateServiceAsync(ok, new UpdateSettings());
        StringAssert.Contains(vm.Status ?? string.Empty, "succeeded");

        var badJson = new StubUpdateService((_, __) => throw new JsonException("bad json"));
        await vm.VerifyViaUpdateServiceAsync(badJson, new UpdateSettings());
        StringAssert.Contains(vm.Status ?? string.Empty, "JSON");

        var generic = new StubUpdateService((_, __) => throw new InvalidOperationException("boom"));
        await vm.VerifyViaUpdateServiceAsync(generic, new UpdateSettings());
        StringAssert.Contains(vm.Status ?? string.Empty, "Verification error");
    }

    [TestMethod]
    public async Task CheckUpdate_EmptyServerSetsStatusAndResetsFlags()
    {
        var vm = new SettingsViewModel(new MemorySettings());
        await vm.LoadAsync();
        vm.BaseUrl = "";
        vm.ManifestPath = "";
        await vm.CheckAndUpdateOnStartupAsync();
        StringAssert.Contains(vm.Status ?? string.Empty, "Update server not configured");
        Assert.IsFalse(vm.IsUpdating);
        vm.CancelUpdate();
    }

    [TestMethod]
    public async Task CheckUpdate_UnreachableServerSetsErrorAndResetsState()
    {
        var vm = new SettingsViewModel(new MemorySettings());
        await vm.LoadAsync();
        vm.BaseUrl = "http://127.0.0.1:1";
        vm.ManifestPath = "/m.json";
        await vm.CheckAndUpdateOnStartupAsync();
        Assert.IsFalse(vm.IsUpdating);
        Assert.IsFalse(string.IsNullOrWhiteSpace(vm.Status));
    }


    [TestMethod]
    public async Task CheckUpdate_UpToDateManifestSetsStatus()
    {
        using var server = new FakeAlistServer();
        server.Files["/manifest.json"] = System.Text.Encoding.UTF8.GetBytes(
            "{\"schema\":\"v1\",\"projects\":{\"LabelPlus_Next.Desktop\":{\"latest\":\"1.3.3\",\"releases\":[{\"version\":\"1.3.3\",\"url\":\"https://example.invalid/c.zip\"}]}}}");

        var versionPath = Path.Combine(AppContext.BaseDirectory, "Client.version.json");
        var versionBackup = File.Exists(versionPath) ? File.ReadAllBytes(versionPath) : null;
        try
        {
            await File.WriteAllTextAsync(versionPath, "{\"project\":\"LabelPlus_Next.Desktop\",\"version\":\"1.3.3\"}");
            var vm = new SettingsViewModel(new MemorySettings());
            await vm.LoadAsync();
            vm.BaseUrl = server.BaseUrl;
            vm.ManifestPath = "/manifest.json";
            await vm.CheckAndUpdateOnStartupAsync();

            Assert.IsFalse(vm.IsUpdating);
        Assert.IsFalse(string.IsNullOrWhiteSpace(vm.Status));
        Assert.IsTrue(server.Requests.Any(r => r.Path.StartsWith("/api/auth/login")));
            Assert.IsTrue(server.Requests.Any(r => r.Path.StartsWith("/api/fs/get")));
        }
        finally
        {
            if (versionBackup is not null) await File.WriteAllBytesAsync(versionPath, versionBackup);
            else if (File.Exists(versionPath)) File.Delete(versionPath);
        }
    }

    [TestMethod]
    public async Task UploadSettings_SaveLoadAndRefreshEvent()
    {
        var path = Path.Combine(_dir, "upload.json");
        var vm = new UploadSettingsViewModel(path)
        {
            BaseUrl = "https://upload.invalid",
            Username = "user",
            Password = "pass"
        };
        var raised = 0;
        vm.RefreshRequested += (_, _) => raised++;
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.AreEqual(1, raised);
        Assert.IsTrue(File.Exists(path));

        var reloaded = new UploadSettingsViewModel(path);
        await reloaded.LoadAsync();
        Assert.AreEqual("https://upload.invalid", reloaded.BaseUrl);
        Assert.AreEqual("user", reloaded.Username);
        Assert.AreEqual("pass", reloaded.Password);

        await File.WriteAllTextAsync(path, "{ invalid json");
        var invalid = new UploadSettingsViewModel(path);
        await invalid.LoadAsync();
        Assert.IsNotNull(invalid.BaseUrl);
    }

    [TestMethod]
    public async Task ToolsServerSettings_ProxiesAndSaves()
    {
        var settingsPath = Path.Combine(_dir, "tools.settings.json");
        var main = new ToolsMainWindowViewModel(settingsPath);
        var vm = new ToolsServerSettingsViewModel(main);
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        vm.BaseUrl = "https://dav.invalid";
        vm.BaseUrl = "https://dav.invalid"; // already equal, no event
        vm.ApiBaseUrl = "https://api.invalid";
        vm.Username = "u";
        vm.Password = "p";
        vm.TargetPath = "/target";

        Assert.AreEqual("https://dav.invalid", main.BaseUrl);
        Assert.AreEqual("https://api.invalid", main.ApiBaseUrl);
        Assert.AreEqual("u", main.Username);
        Assert.AreEqual("p", main.Password);
        Assert.AreEqual("/target", main.TargetPath);
        Assert.AreEqual("https://dav.invalid", vm.BaseUrl);
        Assert.AreEqual("https://api.invalid", vm.ApiBaseUrl);
        Assert.AreEqual("u", vm.Username);
        Assert.AreEqual("p", vm.Password);
        Assert.AreEqual("/target", vm.TargetPath);
        CollectionAssert.Contains(changed, nameof(vm.BaseUrl));

        await vm.SaveCommand.ExecuteAsync(null);
        Assert.IsTrue(File.Exists(settingsPath));
        StringAssert.Contains(vm.SavedMessage ?? string.Empty, "已保存");
    }
}
