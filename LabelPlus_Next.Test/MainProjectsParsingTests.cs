using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using System.Threading.Tasks;
using LabelPlus_Next.Models;
using LabelPlus_Next.Services;
using LabelPlus_Next.ViewModels;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LabelPlus_Next.Test;

[TestClass]
[DoNotParallelize]
public class MainProjectsParsingTests
{
    private string _dir = null!;

    [TestInitialize]
    public void Setup()
    {
        _dir = Path.Combine(Path.GetTempPath(), "lp-projects-parse-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        LocalizationService.Apply("zh-CN");
    }

    [TestCleanup]
    public void Cleanup()
    {
        LocalizationService.Apply("default");
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); } catch { }
    }

    private static object? Call(string name, params object?[] args)
        => typeof(MainProjectsViewModel).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, args);

    [TestMethod]
    public void ParseImageNames_HandlesDuplicatesLimitAndMissingFile()
    {
        var path = Path.Combine(_dir, "translation.txt");
        File.WriteAllLines(path, new[]
        {
            ">>>>>>>>[a.png]<<<<<<<<",
            ">>>>>>>>[b.png]<<<<<<<<",
            ">>>>>>>>[a.png]<<<<<<<<",
            ">>>>>>>>[c.psd]<<<<<<<<"
        });

        var all = (List<string>)Call("ParseImageNames", path, 50)!;
        CollectionAssert.AreEqual(new[] { "a.png", "b.png", "c.psd" }, all);

        var limited = (List<string>)Call("ParseImageNames", path, 2)!;
        CollectionAssert.AreEqual(new[] { "a.png", "b.png" }, limited);

        var missing = (List<string>)Call("ParseImageNames", Path.Combine(_dir, "missing.txt"), 50)!;
        Assert.AreEqual(0, missing.Count);
    }

    [TestMethod]
    public void ParseLabelsForImage_ReadsCoordinatesTextAndCategory()
    {
        var path = Path.Combine(_dir, "translation.txt");
        File.WriteAllLines(path, new[]
        {
            ">>>>>>>>[a.png]<<<<<<<<",
            "----------------[1]----------------[0.100,0.200,2]",
            "line one",
            "line two",
            "",
            "----------------[2]----------------[0.300,0.400]",
            "single",
            "",
            ">>>>>>>>[b.png]<<<<<<<<",
            "----------------[1]----------------[0.500,0.600,1]",
            "other",
            ""
        });

        var labels = (IEnumerable<LabelItem>)Call("ParseLabelsForImage", path, "a.png")!;
        var list = labels.ToList();
        Assert.AreEqual(2, list.Count);
        Assert.AreEqual(0.1f, list[0].XPercent, 0.0001);
        Assert.AreEqual(0.2f, list[0].YPercent, 0.0001);
        Assert.AreEqual(2, list[0].Category);
        Assert.AreEqual("框外", list[0].CategoryString);
        StringAssert.Contains(list[0].Text, "line one");
        StringAssert.Contains(list[0].Text, "line two");
        Assert.AreEqual(1, list[1].Category);
        Assert.AreEqual(1, list[0].Index);
        Assert.AreEqual(2, list[1].Index);

        var missing = (IEnumerable<LabelItem>)Call("ParseLabelsForImage", Path.Combine(_dir, "missing.txt"), "a.png")!;
        Assert.AreEqual(0, missing.Count());
    }



    [TestMethod]
    public void ParameterlessConstructor_UsesAppServices()
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection()
            .AddSingleton<IFileDialogService, NoopFileDialogService>()
            .BuildServiceProvider();
        var prop = typeof(LabelPlus_Next.App).GetProperty("Services", BindingFlags.Public | BindingFlags.Static)!;
        var old = prop.GetValue(null);
        try
        {
            prop.SetValue(null, services);
            var vm = new MainProjectsViewModel();
            Assert.IsNotNull(vm.RecentProjects);
        }
        finally
        {
            prop.SetValue(null, old);
            services.Dispose();
        }
    }
    [TestMethod]
    public void Constructors_UseDefaultRecentPathAndNullPath()
    {
        var a = new MainProjectsViewModel(new NoopFileDialogService());
        Assert.IsNotNull(a.RecentProjects);
        var b = new MainProjectsViewModel(new NoopFileDialogService(), null);
        Assert.IsNotNull(b.RecentProjects);
    }

    [TestMethod]
    public void Parsers_HandleDirectoryPathsAsUnauthorized()
    {
        var names = (System.Collections.Generic.List<string>)Call("ParseImageNames", _dir, 50)!;
        Assert.AreEqual(0, names.Count);
        var labels = (IEnumerable<LabelItem>)Call("ParseLabelsForImage", _dir, "a.png")!;
        Assert.AreEqual(0, labels.Count());
    }

    [TestMethod]
    public void LoadRecent_InvalidJsonIsIgnored()
    {
        var recent = Path.Combine(_dir, "bad_recent.json");
        File.WriteAllText(recent, "{ invalid");
        var vm = new MainProjectsViewModel(new NoopFileDialogService(), recent);
        Assert.AreEqual(0, vm.RecentProjects.Count);
    }

    [TestMethod]
    public void SaveRecent_FailureIsSwallowed()
    {
        var recentDir = Path.Combine(_dir, "recent_dir");
        Directory.CreateDirectory(recentDir);
        var vm = new MainProjectsViewModel(new NoopFileDialogService(), recentDir);
        vm.RefreshRecentCommand.Execute(null);
        Assert.IsNotNull(vm.RecentProjects);
    }

    [TestMethod]
    public async Task OpenProject_SameFileTwiceUpdatesExistingRecent()
    {
        var file = Path.Combine(_dir, "same.txt");
        await File.WriteAllTextAsync(file, "1,0\n-\n");
        var dialogs = new NoopFileDialogService();
        var vm = new MainProjectsViewModel(dialogs, Path.Combine(_dir, "recent_same.json"));
        // Use reflection to invoke the open method with a fake dialog that returns this file.
        var fake = new FakeDialog(file);
        var vm2 = new MainProjectsViewModel(fake, Path.Combine(_dir, "recent_same2.json"));
        await vm2.OpenProjectCommand.ExecuteAsync(null);
        await vm2.OpenProjectCommand.ExecuteAsync(null);
        Assert.AreEqual(1, vm2.RecentProjects.Count);
    }

    private sealed class FakeDialog : IFileDialogService
    {
        private readonly string _file;
        public FakeDialog(string file) => _file = file;
        public Task<IReadOnlyList<string>?> ChooseImagesAsync(string folderPath) => Task.FromResult<IReadOnlyList<string>?>(Array.Empty<string>());
        public Task<string?> OpenTranslationFileAsync() => Task.FromResult<string?>(_file);
        public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);
        public Task<IReadOnlyList<string>?> PickFoldersAsync(string title) => Task.FromResult<IReadOnlyList<string>?>(Array.Empty<string>());
        public Task<IReadOnlyList<string>?> PickFilesAsync(string title) => Task.FromResult<IReadOnlyList<string>?>(Array.Empty<string>());
        public Task<string?> SaveAsTranslationFileAsync(string suggestedFileName = "translation") => Task.FromResult<string?>(null);
        public Task ShowMessageAsync(string message) => Task.CompletedTask;
    }


    [TestMethod]
    public void RecentStore_ProcMemIoFailuresAreSwallowed()
    {
        var vm = new MainProjectsViewModel(new NoopFileDialogService(), "/proc/self/mem", new InlineUiDispatcher());
        vm.RefreshRecentCommand.Execute(null);

        var denied = new MainProjectsViewModel(new NoopFileDialogService(), "/proc/1/mem", new InlineUiDispatcher());
        denied.RefreshRecentCommand.Execute(null);
        Assert.IsNotNull(vm.RecentProjects);
    }
    [TestMethod]
    public void CheckAutosaveRecovery_ReportsRecentBackup()
    {
        var projectDir = Path.Combine(_dir, "project");
        var bakDir = Path.Combine(projectDir, "bak");
        Directory.CreateDirectory(bakDir);
        var translation = Path.Combine(projectDir, "translation.txt");
        File.WriteAllText(translation, ">>>>>>>>[a.png]<<<<<<<<\n");
        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        File.WriteAllText(Path.Combine(bakDir, $"{stamp}_translation.txt"), "backup");

        var recentPath = Path.Combine(_dir, "recent_projects.json");
        File.WriteAllText(recentPath, JsonSerializer.Serialize(new[]
        {
            new RecentProject { Name = "project", TranslationFile = translation, Folder = projectDir, LastOpened = DateTimeOffset.Now }
        }));

        var vm = new MainProjectsViewModel(new NoopFileDialogService(), recentPath);
        var method = typeof(MainProjectsViewModel).GetMethod("CheckAutosaveRecovery", BindingFlags.NonPublic | BindingFlags.Instance)!;
        method.Invoke(vm, null);

        StringAssert.Contains(vm.PreviewStatus ?? string.Empty, "自动保存备份");
    }

    [TestMethod]
    public void CheckAutosaveRecovery_IgnoresOldBackupsAndMissingDirs()
    {
        var projectDir = Path.Combine(_dir, "project2");
        var bakDir = Path.Combine(projectDir, "bak");
        Directory.CreateDirectory(bakDir);
        var translation = Path.Combine(projectDir, "translation.txt");
        File.WriteAllText(translation, "");
        File.WriteAllText(Path.Combine(bakDir, $"20200101_000000_translation.txt"), "old");

        var recentPath = Path.Combine(_dir, "recent_projects2.json");
        File.WriteAllText(recentPath, JsonSerializer.Serialize(new[]
        {
            new RecentProject { Name = "project2", TranslationFile = translation, Folder = projectDir, LastOpened = DateTimeOffset.Now }
        }));
        var vm = new MainProjectsViewModel(new NoopFileDialogService(), recentPath);
        var method = typeof(MainProjectsViewModel).GetMethod("CheckAutosaveRecovery", BindingFlags.NonPublic | BindingFlags.Instance)!;
        method.Invoke(vm, null);
        Assert.IsNull(vm.PreviewStatus);
    }
}
