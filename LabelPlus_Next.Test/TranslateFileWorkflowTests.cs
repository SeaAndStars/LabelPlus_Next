using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using LabelPlus_Next.Models;
using LabelPlus_Next.Services;
using LabelPlus_Next.ViewModels;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LabelPlus_Next.Test;

[TestClass]
[DoNotParallelize]
public class TranslateFileWorkflowTests
{
    private sealed class FakeDialogs : IFileDialogService
    {
        public string? Folder { get; set; }
        public IReadOnlyList<string>? Images { get; set; }
        public string? OpenFile { get; set; }
        public string? SaveFile { get; set; }
        public List<string> Messages { get; } = new();

        public Task<IReadOnlyList<string>?> ChooseImagesAsync(string folderPath) => Task.FromResult(Images);
        public Task<string?> OpenTranslationFileAsync() => Task.FromResult(OpenFile);
        public Task<string?> PickFolderAsync(string title) => Task.FromResult(Folder);
        public Task<IReadOnlyList<string>?> PickFoldersAsync(string title) => Task.FromResult<IReadOnlyList<string>?>(Array.Empty<string>());
        public Task<IReadOnlyList<string>?> PickFilesAsync(string title) => Task.FromResult<IReadOnlyList<string>?>(Array.Empty<string>());
        public Task<string?> SaveAsTranslationFileAsync(string suggestedFileName = "translation") => Task.FromResult(SaveFile);
        public Task ShowMessageAsync(string message)
        {
            Messages.Add(message);
            return Task.CompletedTask;
        }
    }

    private string _dir = null!;

    [TestInitialize]
    public void Setup()
    {
        _dir = Path.Combine(Path.GetTempPath(), "lp-translate-file-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        ResetStore();
    }

    [TestCleanup]
    public void Cleanup()
    {
        ResetStore();
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); } catch { }
    }

    private static void ResetStore()
    {
        var field = typeof(TranslateViewModel).GetField("LabelFileManager1", BindingFlags.NonPublic | BindingFlags.Static);
        if (field?.GetValue(null) is LabelFileManager manager)
        {
            manager.StoreManager.Store.Clear();
            manager.StoreManager.ResetDirty();
        }
    }

    private static async Task Invoke(TranslateViewModel vm, string name)
    {
        var method = typeof(TranslateViewModel).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!;
        await (Task)method.Invoke(vm, null)!;
    }

    private TranslateViewModel NewVm(FakeDialogs dialogs)
        => new(dialogs, new InlineUiDispatcher());

    [TestMethod]
    public async Task NewTranslation_WithInlineUi_CreatesLoadsAndSaves()
    {
        var image1 = Path.Combine(_dir, "p1.png");
        var image2 = Path.Combine(_dir, "p2.png");
        await File.WriteAllTextAsync(image1, "");
        await File.WriteAllTextAsync(image2, "");
        var dialogs = new FakeDialogs { Folder = _dir, Images = new[] { image1, image2 }, SaveFile = Path.Combine(_dir, "copy.txt") };
        var vm = NewVm(dialogs);

        await Invoke(vm, "NewTranslationCommand");

        var path = Path.Combine(_dir, "translation.txt");
        Assert.IsTrue(File.Exists(path));
        Assert.AreEqual(path, vm.OpenTranslationFilePath);
        Assert.AreEqual(2, vm.ImageFileNames.Count);
        Assert.AreEqual("p1.png", vm.SelectedImageFile);
        Assert.IsFalse(vm.IsBusy);

        await Invoke(vm, "SaveCurrentCommand");
        Assert.AreEqual(path, vm.OpenTranslationFilePath);

        await vm.SaveAsCommand();
        Assert.AreEqual(dialogs.SaveFile, vm.OpenTranslationFilePath);

        vm.SelectedImageFile = "p1.png";
        await vm.AddLabelAtAsync(0.1f, 0.2f, 2);
        Assert.AreEqual(1, vm.CurrentLabels.Count);
        Assert.IsTrue(vm.HasLabelsForFile("p1.png"));
        vm.SelectedLabel = vm.CurrentLabels[0];
        vm.SetSelectedCategory(1);
        vm.CurrentText = "changed";
        Assert.AreEqual("changed", vm.CurrentLabels[0].Text);
        vm.MoveLabelWithinCurrentImage(0, 0);

        var (groups, notes) = vm.GetFileSettings();
        Assert.IsNotNull(groups);
        Assert.IsNotNull(notes);
        Assert.IsTrue(await vm.SaveFileSettingsAsync(new List<string> { "g" }, "note"));

        await vm.RemoveLabelCommand();
        Assert.AreEqual(0, vm.CurrentLabels.Count);
        await vm.UndoRemoveLabelCommand();
        Assert.AreEqual(1, vm.CurrentLabels.Count);
        vm.OpenTranslationFilePath = null;
    }

    [TestMethod]
    public async Task NewTranslation_NoImagesMessage()
    {
        var dialogs = new FakeDialogs { Folder = _dir, Images = Array.Empty<string>() };
        var vm = NewVm(dialogs);
        await Invoke(vm, "NewTranslationCommand");
        Assert.IsNull(vm.OpenTranslationFilePath);
        Assert.IsFalse(vm.IsBusy);
    }

    [TestMethod]
    public async Task OpenTranslation_WithInlineUi_LoadsAndSaves()
    {
        var file = Path.Combine(_dir, "existing.txt");
        var content = TranslationFileUtils.BuildInitialContent(new[] { "a.png", "b.png" });
        await File.WriteAllTextAsync(file, content);
        var dialogs = new FakeDialogs { OpenFile = file };
        var vm = NewVm(dialogs);

        await Invoke(vm, "OpenTranslationFileCommand");
        Assert.AreEqual(file, vm.OpenTranslationFilePath);
        Assert.AreEqual(2, vm.ImageFileNames.Count);

        await Invoke(vm, "SaveCurrentCommand");
        Assert.IsFalse(vm.IsBusy);

        var bad = Path.Combine(_dir, "bad.txt");
        await File.WriteAllTextAsync(bad, "not a label file");
        await vm.OpenTranslationFileAtPathAsync(bad);
        Assert.IsFalse(vm.IsBusy);
        vm.OpenTranslationFilePath = null;
    }

    [TestMethod]
    public async Task SaveCurrent_WithoutOpenFileShowsMessage()
    {
        var dialogs = new FakeDialogs();
        var vm = NewVm(dialogs);
        await Invoke(vm, "SaveCurrentCommand");
        Assert.AreEqual(1, dialogs.Messages.Count);
    }

    [TestMethod]
    public async Task OpenTranslationFolder_HandlesMissingPath()
    {
        var vm = NewVm(new FakeDialogs());
        var method = typeof(TranslateViewModel).GetMethod("OpenTranslationFolder", BindingFlags.NonPublic | BindingFlags.Instance)!;
        method.Invoke(vm, null);
        vm.OpenTranslationFilePath = Path.Combine(_dir, "missing.txt");
        method.Invoke(vm, null);
    }
}
