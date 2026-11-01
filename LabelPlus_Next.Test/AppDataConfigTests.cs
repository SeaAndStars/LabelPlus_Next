using System.Text.Json;
using LabelPlus_Next.Models;
using LabelPlus_Next.Services;
using LabelPlus_Next.Shared;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LabelPlus_Next.Test;

[TestClass]
[DoNotParallelize]
public class AppDataConfigTests
{
    private static void Restore(string path, byte[]? backup)
    {
        try
        {
            if (backup is null)
            {
                if (File.Exists(path)) File.Delete(path);
            }
            else
            {
                AppConfigPaths.EnsureParent(path);
                File.WriteAllBytes(path, backup);
            }
        }
        catch
        {
            // best effort cleanup
        }
    }

    [TestMethod]
    public async Task JsonSettings_MigratesLegacyFileToAppData()
    {
        var target = AppConfigPaths.Settings;
        var legacy = AppConfigPaths.LegacySettings;
        var targetBackup = File.Exists(target) ? File.ReadAllBytes(target) : null;
        var legacyBackup = File.Exists(legacy) ? File.ReadAllBytes(legacy) : null;
        try
        {
            if (File.Exists(target)) File.Delete(target);
            AppConfigPaths.EnsureParent(legacy);
            await File.WriteAllTextAsync(legacy, "{\"language\":\"en\",\"theme\":\"dark\"}");

            var settings = await new JsonSettingsService().LoadAsync();

            Assert.AreEqual("en", settings.Language);
            Assert.AreEqual("dark", settings.Theme);
            Assert.IsTrue(File.Exists(target));

            // AppData must win even when a legacy file still exists.
            await new JsonSettingsService().SaveAsync(new AppSettings { Language = "zh-hant", Theme = "light" });
            await File.WriteAllTextAsync(legacy, "{\"language\":\"en\",\"theme\":\"dark\"}");
            var preferred = await new JsonSettingsService().LoadAsync();
            Assert.AreEqual("zh-hant", preferred.Language);
            Assert.AreEqual("light", preferred.Theme);
        }
        finally
        {
            Restore(target, targetBackup);
            Restore(legacy, legacyBackup);
        }
    }

    [TestMethod]
    public async Task UploadSettings_ExplicitAndLegacyPaths()
    {
        var dir = Path.Combine(Path.GetTempPath(), "lp-appdata-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var explicitPath = Path.Combine(dir, "upload.json");
        var target = AppConfigPaths.UploadSettings;
        var legacy = AppConfigPaths.LegacyUploadSettings;
        var targetBackup = File.Exists(target) ? File.ReadAllBytes(target) : null;
        var legacyBackup = File.Exists(legacy) ? File.ReadAllBytes(legacy) : null;
        try
        {
            var sample = new UploadSettings { BaseUrl = "https://explicit.invalid", Username = "u" };
            await UploadSettingsStore.SaveAsync(sample, explicitPath);
            var explicitLoaded = await UploadSettingsStore.LoadAsync(explicitPath);
            Assert.AreEqual("https://explicit.invalid", explicitLoaded!.BaseUrl);
            Assert.AreEqual("u", explicitLoaded.Username);

            if (File.Exists(target)) File.Delete(target);
            AppConfigPaths.EnsureParent(legacy);
            await File.WriteAllTextAsync(legacy, "{\"baseUrl\":\"https://legacy.invalid\",\"username\":\"legacy\"}");

            var migrated = await UploadSettingsStore.LoadAsync();
            Assert.AreEqual("https://legacy.invalid", migrated!.BaseUrl);
            Assert.IsTrue(File.Exists(target));

            await UploadSettingsStore.SaveAsync(new UploadSettings { BaseUrl = "https://appdata.invalid", Username = "appdata" });
            await File.WriteAllTextAsync(legacy, "{\"baseUrl\":\"https://legacy.invalid\",\"username\":\"legacy\"}");
            var preferred = await UploadSettingsStore.LoadAsync();
            Assert.AreEqual("https://appdata.invalid", preferred!.BaseUrl);
            Assert.AreEqual("appdata", preferred.Username);
        }
        finally
        {
            Restore(target, targetBackup);
            Restore(legacy, legacyBackup);
            try { Directory.Delete(dir, true); } catch { }
        }
    }
}
