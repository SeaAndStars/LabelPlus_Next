using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LabelPlus_Next.Models;
using LabelPlus_Next.Services;
using LabelPlus_Next.ViewModels;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LabelPlus_Next.Test;

[TestClass]
[DoNotParallelize]
public class UploadIntegrationTests
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
        _dir = Path.Combine(Path.GetTempPath(), "lp-upload-int-" + Guid.NewGuid().ToString("N"));
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
    public async Task UploadPending_LogsInAndUploadsFilesAgainstAlist()
    {
        using var server = new FakeAlistServer();
        var configPath = Path.Combine(AppContext.BaseDirectory, "upload.json");
        var backup = File.Exists(configPath) ? File.ReadAllBytes(configPath) : null;
        try
        {
            await File.WriteAllTextAsync(configPath,
                "{\"baseUrl\":\"" + server.BaseUrl + "\",\"username\":\"u\",\"password\":\"p\"}");

            var chapter = Path.Combine(_dir, "proj", "01");
            Directory.CreateDirectory(chapter);
            var txt = Path.Combine(chapter, "translator.txt");
            var psd = Path.Combine(chapter, "art.psd");
            var zip = Path.Combine(chapter, "source.zip");
            await File.WriteAllTextAsync(txt, "翻译");
            await File.WriteAllTextAsync(psd, "psd");
            await File.WriteAllTextAsync(zip, "zip");

            var vm = new UploadViewModel(new MemorySettings(), new NoopFileDialogService(), false, configPath)
            {
                PendingProjectName = "proj"
            };
            vm.PendingEpisodes.Add(new EpisodeEntry
            {
                Number = 1,
                Status = "翻译",
                Include = true,
                LocalFiles = new List<string> { txt, psd, zip }
            });

            var ok = await vm.UploadPendingAsync();

            Assert.IsTrue(ok, vm.Status);
            Assert.IsTrue(server.Requests.Any(r => r.Path.StartsWith("/api/auth/login")));
            Assert.IsTrue(server.Requests.Any(r => r.Path.StartsWith("/api/fs/mkdir")));
            Assert.IsTrue(server.Requests.Any(r => r.Path.StartsWith("/api/fs/put")));
        }
        finally
        {
            if (File.Exists(configPath)) File.Delete(configPath);
            if (backup is not null) await File.WriteAllBytesAsync(configPath, backup);
        }
    }


    [TestMethod]
    public async Task ParameterlessConstructor_StartsRefreshWithoutThrowing()
    {
        var configPath = Path.Combine(AppContext.BaseDirectory, "upload.json");
        var backup = File.Exists(configPath) ? File.ReadAllBytes(configPath) : null;
        try
        {
            await File.WriteAllTextAsync(configPath, "{\"baseUrl\":\"http://127.0.0.1:1\",\"username\":\"u\",\"password\":\"p\"}");
            var vm = new UploadViewModel(new MemorySettings(), new NoopFileDialogService(), true, configPath);
            await Task.Delay(100);
            Assert.IsNotNull(vm.Status);
        }
        finally
        {
            if (File.Exists(configPath)) File.Delete(configPath);
            if (backup is not null) await File.WriteAllBytesAsync(configPath, backup);
        }
    }
    [TestMethod]
    public async Task UploadPending_RejectsMissingProjectName()
    {
        var vm = new UploadViewModel(new MemorySettings(), new NoopFileDialogService(), false);
        Assert.IsFalse(await vm.UploadPendingAsync());
        StringAssert.Contains(vm.Status ?? string.Empty, "project");
    }

    [TestMethod]
    public async Task UploadPending_RejectsMissingOrIncompleteCredentials()
    {
        using var server = new FakeAlistServer();
        var configPath = Path.Combine(AppContext.BaseDirectory, "upload.json");
        var backup = File.Exists(configPath) ? File.ReadAllBytes(configPath) : null;
        try
        {
            var vm = new UploadViewModel(new MemorySettings(), new NoopFileDialogService(), false, configPath) { PendingProjectName = "p" };
            await File.WriteAllTextAsync(configPath, "{\"baseUrl\":\"\",\"username\":\"\",\"password\":\"\"}");
            Assert.IsFalse(await vm.UploadPendingAsync());
            StringAssert.Contains(vm.Status ?? string.Empty, "configured");

            await File.WriteAllTextAsync(configPath, "{\"baseUrl\":\"" + server.BaseUrl + "\",\"username\":\"\",\"password\":\"\"}");
            Assert.IsFalse(await vm.UploadPendingAsync());
            StringAssert.Contains(vm.Status ?? string.Empty, "configured");
        }
        finally
        {
            if (File.Exists(configPath)) File.Delete(configPath);
            if (backup is not null) await File.WriteAllBytesAsync(configPath, backup);
        }
    }
}
