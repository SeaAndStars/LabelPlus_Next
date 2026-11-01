using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using LabelPlus_Next.Models;
using LabelPlus_Next.Services;
using LabelPlus_Next.ViewModels;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LabelPlus_Next.Test.ViewModels;

[TestClass]
[DoNotParallelize]
public class TranslateWorkflowTests
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

    [TestInitialize]
    public void Setup()
    {
        _dir = Path.Combine(Path.GetTempPath(), "lp-translate-" + Guid.NewGuid().ToString("N"));
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
        var field = typeof(TranslateViewModel).GetField("SharedLabelManager", BindingFlags.NonPublic | BindingFlags.Static);
        if (field?.GetValue(null) is LabelFileManager manager)
        {
            manager.StoreManager.Store.Clear();
            manager.StoreManager.ResetDirty();
        }
    }

    private static async Task InvokeAsync(TranslateViewModel vm, string methodName)
    {
        var method = typeof(TranslateViewModel).GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance)
                     ?? throw new MissingMethodException(typeof(TranslateViewModel).FullName, methodName);
        await (Task)method.Invoke(vm, null)!;
    }

    [TestMethod]
    public async Task OpenTranslationCommand_CancelDoesNothing()
    {
        var vm = new TranslateViewModel(new FakeDialogs { OpenFile = null }, new InlineUiDispatcher(), new LabelFileManager());
        await InvokeAsync(vm, "OpenTranslationFileCommand");
        Assert.IsNull(vm.OpenTranslationFilePath);
    }

    [TestMethod]
    public async Task SaveCurrentCommand_WithoutOpenPathShowsMessage()
    {
        var dialogs = new FakeDialogs();
        var vm = new TranslateViewModel(dialogs, new InlineUiDispatcher(), new LabelFileManager());
        await InvokeAsync(vm, "SaveCurrentCommand");
        Assert.AreEqual(1, dialogs.Messages.Count);
    }

    [TestMethod]
    public async Task SaveAsCommand_CancelDoesNothingAndDirectFileSaveWorks()
    {
        var vm = new TranslateViewModel(new FakeDialogs(), new InlineUiDispatcher(), new LabelFileManager());
        await vm.SaveAsCommand();
        Assert.IsNull(vm.OpenTranslationFilePath);

        var path = Path.Combine(_dir, "plain.txt");
        await vm.FileSave(path);
        Assert.IsTrue(File.Exists(path));
    }

    [TestMethod]
    public async Task ImageAndFileSettingsHelpers()
    {
        var vm = new TranslateViewModel(new FakeDialogs(), new InlineUiDispatcher(), new LabelFileManager());
        await vm.AddImageFileAsync("a.png");
        await vm.AddImageFileAsync("b.png");
        Assert.AreEqual(2, vm.GetIncludedImageFiles().Count);

        var (groups, notes) = vm.GetFileSettings();
        Assert.IsNotNull(groups);
        Assert.IsNotNull(notes);
        Assert.IsFalse(await vm.SaveFileSettingsAsync(new List<string> { "g" }, "note"));

        await vm.RemoveImageFileAsync("a.png");
        Assert.AreEqual(1, vm.GetIncludedImageFiles().Count);
        Assert.IsFalse(vm.HasLabelsForFile("a.png"));
    }

    [TestMethod]
    public void OpenAtPath_MissingFileIsIgnored()
    {
        var vm = new TranslateViewModel(new FakeDialogs(), new InlineUiDispatcher(), new LabelFileManager());
        vm.OpenTranslationFileAtPathAsync(Path.Combine(_dir, "missing.txt"));
        Assert.IsNull(vm.OpenTranslationFilePath);
    }
}
