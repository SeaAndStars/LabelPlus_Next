using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using LabelPlus_Next.Services;
using LabelPlus_Next.ViewModels;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LabelPlus_Next.Test.ViewModels;

[TestClass]
[DoNotParallelize]
public class MainProjectsViewModelTests
{
    private sealed class FakeDialogs : IFileDialogService
    {
        public string? Folder { get; set; }
        public IReadOnlyList<string>? Images { get; set; }
        public string? OpenFile { get; set; }
        public string? SaveFile { get; set; }
        public List<string> Messages { get; } = new();

        public Task<IReadOnlyList<string>?> ChooseImagesAsync(string folderPath)
            => Task.FromResult(Images);
        public Task<string?> OpenTranslationFileAsync() => Task.FromResult(OpenFile);
        public Task<string?> PickFolderAsync(string title) => Task.FromResult(Folder);
        public Task<IReadOnlyList<string>?> PickFoldersAsync(string title)
            => Task.FromResult<IReadOnlyList<string>?>(Array.Empty<string>());
        public Task<IReadOnlyList<string>?> PickFilesAsync(string title)
            => Task.FromResult<IReadOnlyList<string>?>(Array.Empty<string>());
        public Task<string?> SaveAsTranslationFileAsync(string suggestedFileName = "translation")
            => Task.FromResult(SaveFile);
        public Task ShowMessageAsync(string message)
        {
            Messages.Add(message);
            return Task.CompletedTask;
        }
    }

    private string _dir = null!;
    private string _recentPath = null!;

    [TestInitialize]
    public void Setup()
    {
        _dir = Path.Combine(Path.GetTempPath(), "lp-projects-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _recentPath = Path.Combine(_dir, "recent_projects.json");
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); } catch { }
    }

    [TestMethod]
    public void Constructor_LoadsRecentProjectsAndSortsThem()
    {
        var older = new RecentProject { Name = "old", TranslationFile = Path.Combine(_dir, "old.txt"), LastOpened = DateTimeOffset.Now.AddDays(-2) };
        var newer = new RecentProject { Name = "new", TranslationFile = Path.Combine(_dir, "new.txt"), LastOpened = DateTimeOffset.Now };
        File.WriteAllText(_recentPath, JsonSerializer.Serialize(new[] { older, newer }));

        var vm = new MainProjectsViewModel(new FakeDialogs(), _recentPath);

        Assert.AreEqual(2, vm.RecentProjects.Count);
        Assert.AreEqual("new", vm.RecentProjects[0].Name);
        Assert.IsFalse(vm.IsRecentEmpty);
        Assert.IsNull(vm.SelectedProject);
    }

    [TestMethod]
    public async Task CreateProject_WritesTranslationAndRaisesOpen()
    {
        var folder = Path.Combine(_dir, "project");
        Directory.CreateDirectory(folder);
        var image1 = Path.Combine(folder, "p1.png");
        var image2 = Path.Combine(folder, "p2.png");
        await File.WriteAllTextAsync(image1, "");
        await File.WriteAllTextAsync(image2, "");

        var dialogs = new FakeDialogs { Folder = folder, Images = new[] { image1, image2 } };
        var vm = new MainProjectsViewModel(dialogs, _recentPath);
        string? opened = null;
        vm.OpenProjectRequested += (_, path) => opened = path;

        await vm.CreateProjectCommand.ExecuteAsync(null);

        var translation = Path.Combine(folder, "translation.txt");
        Assert.IsTrue(File.Exists(translation));
        StringAssert.Contains(await File.ReadAllTextAsync(translation), "p1.png");
        Assert.AreEqual(translation, opened);
        Assert.AreEqual(1, vm.RecentProjects.Count);
        Assert.IsNotNull(vm.SelectedProject);
    }

    [TestMethod]
    public async Task CreateProject_DoesNothingWhenPickerCancels()
    {
        var dialogs = new FakeDialogs();
        var vm = new MainProjectsViewModel(dialogs, _recentPath);
        await vm.CreateProjectCommand.ExecuteAsync(null);
        Assert.AreEqual(0, vm.RecentProjects.Count);
        Assert.IsFalse(vm.IsBusy);
    }

    [TestMethod]
    public async Task OpenProject_AddsRecentAndRaisesOpen()
    {
        var file = Path.Combine(_dir, "existing.txt");
        await File.WriteAllTextAsync(file, "1,0\n-\n");
        var dialogs = new FakeDialogs { OpenFile = file };
        var vm = new MainProjectsViewModel(dialogs, _recentPath);
        string? opened = null;
        vm.OpenProjectRequested += (_, path) => opened = path;

        await vm.OpenProjectCommand.ExecuteAsync(null);

        Assert.AreEqual(file, opened);
        Assert.AreEqual(1, vm.RecentProjects.Count);
        Assert.IsNotNull(vm.SelectedProject);
    }

    [TestMethod]
    public async Task OpenSelectedProject_UpdatesTimestampAndRaisesOpen()
    {
        var file = Path.Combine(_dir, "existing.txt");
        await File.WriteAllTextAsync(file, "1,0\n-\n");
        File.WriteAllText(_recentPath, JsonSerializer.Serialize(new[]
        {
            new RecentProject { Name = "p", TranslationFile = file, LastOpened = DateTimeOffset.Now.AddDays(-1) }
        }));
        var vm = new MainProjectsViewModel(new FakeDialogs(), _recentPath);
        vm.SelectedProject = vm.RecentProjects[0];
        string? opened = null;
        vm.OpenProjectRequested += (_, path) => opened = path;

        vm.OpenSelectedProjectCommand.Execute(null);

        Assert.AreEqual(file, opened);
        Assert.IsTrue(vm.SelectedProject!.LastOpened > DateTimeOffset.Now.AddMinutes(-1));
    }

    [TestMethod]
    public async Task RemoveSelected_ClearsPreviewAndPersists()
    {
        var file = Path.Combine(_dir, "existing.txt");
        await File.WriteAllTextAsync(file, "1,0\n-\n");
        File.WriteAllText(_recentPath, JsonSerializer.Serialize(new[]
        {
            new RecentProject { Name = "p", TranslationFile = file, LastOpened = DateTimeOffset.Now }
        }));
        var vm = new MainProjectsViewModel(new FakeDialogs(), _recentPath);
        vm.SelectedProject = vm.RecentProjects[0];

        vm.RemoveSelectedCommand.Execute(null);

        Assert.AreEqual(0, vm.RecentProjects.Count);
        Assert.IsNull(vm.SelectedProject);
        Assert.IsTrue(vm.IsRecentEmpty);
        Assert.AreEqual(0, vm.PreviewImageNames.Count);
        Assert.IsNull(vm.PreviewStatus);
    }

    [TestMethod]
    public void RefreshRecent_RemovesMissingFilesAndSorts()
    {
        var existing = Path.Combine(_dir, "existing.txt");
        File.WriteAllText(existing, "1,0\n-\n");
        var missing = Path.Combine(_dir, "missing.txt");
        File.WriteAllText(_recentPath, JsonSerializer.Serialize(new[]
        {
            new RecentProject { Name = "missing", TranslationFile = missing, LastOpened = DateTimeOffset.Now },
            new RecentProject { Name = "existing", TranslationFile = existing, LastOpened = DateTimeOffset.Now.AddDays(-1) }
        }));
        var vm = new MainProjectsViewModel(new FakeDialogs(), _recentPath);
        Assert.AreEqual(2, vm.RecentProjects.Count);

        vm.RefreshRecentCommand.Execute(null);

        Assert.AreEqual(1, vm.RecentProjects.Count);
        Assert.AreEqual("existing", vm.RecentProjects[0].Name);
    }

    [TestMethod]
    public void NavigationCommands_RespectPreviewCount()
    {
        var vm = new MainProjectsViewModel(new FakeDialogs(), _recentPath);
        Assert.IsFalse(vm.PrevPreviewCommand.CanExecute(null));
        Assert.IsFalse(vm.NextPreviewCommand.CanExecute(null));
    }
}
