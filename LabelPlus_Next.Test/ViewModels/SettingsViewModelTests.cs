using Antelcat.I18N.Avalonia;
using Avalonia.Styling;
using LabelPlus_Next.Lang;
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

    private sealed class ThrowingSettingsService : ISettingsService
    {
        public AppSettings Data { get; set; } = new()
        {
            Update = new UpdateSettings
            {
                BaseUrl = "https://example.com",
                ManifestPath = "/manifest.json",
                Username = "u",
                Password = "p"
            }
        };

        public Task<AppSettings> LoadAsync(CancellationToken ct = default)
            => Task.FromResult(Data);

        public Task SaveAsync(AppSettings settings, CancellationToken ct = default)
            => throw new InvalidOperationException("save failed");
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

    [TestMethod]
    public async Task Theme_LoadsPersistsAndUpdatesDisplayText()
    {
        var mem = new MemorySettingsService
        {
            Data = new AppSettings
            {
                Theme = "dark",
                Update = new UpdateSettings
                {
                    BaseUrl = "https://example.com",
                    ManifestPath = "/manifest.json",
                    Username = "u",
                    Password = "p"
                }
            }
        };

        var vm = new SettingsViewModel(mem);
        await vm.LoadAsync();

        Assert.AreEqual(ThemeVariant.Dark, vm.SelectedTheme);
        Assert.AreEqual(I18NExtension.Translate(LangKeys.settingsThemeDark), vm.ThemeDisplayName);

        vm.SelectedTheme = ThemeVariant.Light;
        Assert.AreEqual(ThemePreference.Light, mem.Data.Theme);
        Assert.AreEqual(I18NExtension.Translate(LangKeys.settingsThemeLight), vm.ThemeDisplayName);

        vm.SelectedTheme = ThemeVariant.Default;
        Assert.AreEqual(ThemePreference.Default, mem.Data.Theme);
        Assert.AreEqual(I18NExtension.Translate(LangKeys.settingsThemeSystem), vm.ThemeDisplayName);

        await vm.SaveAsync();
        Assert.AreEqual(ThemePreference.Default, mem.Data.Theme);
    }

    [TestMethod]
    public async Task ThemePersistenceFailure_IsSwallowed()
    {
        var vm = new SettingsViewModel(new ThrowingSettingsService());
        await vm.LoadAsync();

        vm.SelectedTheme = ThemeVariant.Dark;
        await Task.Delay(20);

        Assert.AreEqual(ThemeVariant.Dark, vm.SelectedTheme);
    }
}
