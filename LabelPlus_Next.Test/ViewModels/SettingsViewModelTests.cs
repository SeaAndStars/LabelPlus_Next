using LabelPlus_Next.Models;
using LabelPlus_Next.Services;
using LabelPlus_Next.ViewModels;

namespace LabelPlus_Next.Test.ViewModels;

[TestClass]
public class SettingsViewModelTests
{
    private sealed class MemorySettingsService : ISettingsService
    {
        public AppSettings Data { get; set; } = new();

        public Task<AppSettings> LoadAsync(CancellationToken ct = default)
            => Task.FromResult(Data);

        public Task SaveAsync(AppSettings settings, CancellationToken ct = default)
        {
            Data = settings;
            return Task.CompletedTask;
        }
    }

    [TestMethod]
    public async Task LoadAsync_FillsDefaultsAndSavesBack()
    {
        var mem = new MemorySettingsService
        {
            Data = new AppSettings { Update = new UpdateSettings { BaseUrl = string.Empty, ManifestPath = string.Empty, Username = string.Empty, Password = string.Empty } }
        };

        var vm = new SettingsViewModel(mem);
        await vm.LoadAsync();

        Assert.IsFalse(string.IsNullOrWhiteSpace(vm.BaseUrl));
        Assert.IsFalse(string.IsNullOrWhiteSpace(vm.ManifestPath));
        Assert.IsFalse(string.IsNullOrWhiteSpace(vm.Username));
        Assert.IsNotNull(mem.Data.Update.BaseUrl);
    }

    [TestMethod]
    public async Task SaveAsync_PersistsValues()
    {
        var mem = new MemorySettingsService();
        var vm = new SettingsViewModel(mem)
        {
            BaseUrl = "https://example.com",
            ManifestPath = "/a/b/manifest.json",
            Username = "u",
            Password = "p"
        };

        await vm.SaveAsync();
        Assert.AreEqual("https://example.com", mem.Data.Update.BaseUrl);
        Assert.AreEqual("/a/b/manifest.json", mem.Data.Update.ManifestPath);
        Assert.AreEqual("u", mem.Data.Update.Username);
        Assert.AreEqual("p", mem.Data.Update.Password);
    }
}
