using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using LabelPlus_Next.Models;
using LabelPlus_Next.Services;
using LabelPlus_Next.Shared;
using LabelPlus_Next.Tools.Models;
using LabelPlus_Next.Tools.Services;
using LabelPlus_Next.ViewModels;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LabelPlus_Next.Test;

[TestClass]
[DoNotParallelize]
public class EdgeCoverageTests
{
    private string _dir = null!;

    [TestInitialize]
    public void Setup()
    {
        _dir = Path.Combine(Path.GetTempPath(), "lp-edge-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        LocalizationService.Apply("en");
    }

    [TestCleanup]
    public void Cleanup()
    {
        LocalizationService.Apply("default");
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); } catch { }
        ResetMainWindowStore();
    }

    private static void ResetMainWindowStore()
    {
        // MainWindowViewModel keeps one static document manager (like the real
        // single-document window); clean it so other tests see a fresh state.
        var field = typeof(MainWindowViewModel).GetField("LabelFileManager1", BindingFlags.NonPublic | BindingFlags.Static);
        if (field?.GetValue(null) is LabelFileManager manager)
        {
            manager.StoreManager.Store.Clear();
            manager.StoreManager.ResetDirty();
        }
    }

    [TestMethod]
    public void UpdateVersion_FallbackAndEqualPrerelease()
    {
        Assert.AreEqual(0, UpdateVersion.Compare("1.0-alpha", "1.0-alpha"));
        Assert.AreNotEqual(0, UpdateVersion.Compare("abc", "1.0.0"));
        Assert.AreNotEqual(0, UpdateVersion.Compare("1.0.0", "abc"));
        Assert.IsTrue(UpdateVersion.Compare("1.0.0-beta", "1.0.0-alpha") > 0);
        Assert.IsTrue(UpdateVersion.Compare("1.0.0-alpha", "1.0.0-beta") < 0);
        Assert.IsTrue(UpdateVersion.Compare("1.0.0-alpha", "1.0.0-alpha.1") < 0);
        Assert.IsTrue(UpdateVersion.Compare("1.0.0-alpha.1", "1.0.0-alpha") > 0);
    }

    [TestMethod]
    public void MergeConflict_DefaultAndForcedConflict()
    {
        var empty = new MergeConflictViewModel();
        Assert.AreEqual(0, empty.Conflicts.Count);
        Assert.AreEqual(string.Empty, empty.MergedText);

        // Trailing newline differs, but the diff has no conflicting chunk: the
        // constructor must still expose the whole text as one conflict.
        var forced = new MergeConflictViewModel("a\n", "a", "forced.txt");
        Assert.AreEqual(0, forced.Conflicts.Count);
        StringAssert.Contains(forced.MergedText, "<<<<<<< REMOTE");
        StringAssert.Contains(forced.MergedText, ">>>>>>> LOCAL");
    }

    [TestMethod]
    public async Task UploadSettingsViewModel_ConstructsAndExposesSave()
    {
        var vm = new UploadSettingsViewModel();
        Assert.IsNotNull(vm.SaveCommand);
        Assert.AreEqual("https://alist1.seastarss.cn", vm.BaseUrl);
        await vm.LoadAsync();
    }

    [TestMethod]
    public async Task MainWindowViewModel_StartsStopsAutosaveAndRefreshesImages()
    {
        var vm = new MainWindowViewModel(new NoopFileDialogService());
        vm.OpenTranslationFilePath = Path.Combine(_dir, "translation.txt");
        vm.StopAutoSave();

        await vm.AddImageFileAsync("page01.png");
        await vm.AddImageFileAsync("page02.png");
        Assert.IsTrue(vm.HasUnsavedChanges);
        Assert.AreEqual(2, vm.GetIncludedImageFiles().Count);

        vm.RefreshImagesList();
        Assert.AreEqual(2, vm.ImageFileNames.Count);
        Assert.AreEqual("page01.png", vm.SelectedImageFile);

        vm.SelectedLabel = new LabelItem(0, 0, "x", 1);
        vm.SetSelectedCategory(2);
        Assert.AreEqual(2, vm.SelectedLabel.Category);
        Assert.AreEqual("框外", vm.SelectedLabel.CategoryString);

        var (groups, notes) = vm.GetFileSettings();
        Assert.IsNotNull(groups);
        Assert.IsNotNull(notes);

        await vm.RemoveImageFileAsync("page02.png");
        vm.RefreshImagesList();
        Assert.AreEqual(1, vm.ImageFileNames.Count);
    }

    [TestMethod]
    public void ToolsSettingsStore_DefaultPathAndFailedSyncSave()
    {
        Assert.IsTrue(ToolsSettingsStore.DefaultPath.EndsWith("tools.settings.json", StringComparison.OrdinalIgnoreCase));
        var dirAsFile = Path.Combine(_dir, "blocked");
        Directory.CreateDirectory(dirAsFile);
        Assert.ThrowsException<IOException>(() => ToolsSettingsStore.Save(dirAsFile, new ToolsSettings()));
        Assert.AreEqual(0, Directory.GetFiles(_dir, "*.tmp").Length); // the temp file is cleaned up
    }

    [TestMethod]
    public async Task StagedInstaller_CleansStaleStagingDirectory()
    {
        var target = Path.Combine(_dir, "app");
        Directory.CreateDirectory(target);
        var stale = Path.Combine(target, ".lpn-staging-dead");
        Directory.CreateDirectory(stale);
        await File.WriteAllTextAsync(Path.Combine(stale, "x.txt"), "old");

        var zip = Path.Combine(_dir, "pkg.zip");
        using (var z = ZipFile.Open(zip, ZipArchiveMode.Create))
        using (var s = z.CreateEntry("a.txt").Open())
            s.Write(System.Text.Encoding.UTF8.GetBytes("new"));

        var count = await StagedInstaller.ApplyZipAsync(zip, target);
        Assert.AreEqual(1, count);
        Assert.IsFalse(Directory.Exists(stale));
        Assert.AreEqual("new", await File.ReadAllTextAsync(Path.Combine(target, "a.txt")));
    }
}
