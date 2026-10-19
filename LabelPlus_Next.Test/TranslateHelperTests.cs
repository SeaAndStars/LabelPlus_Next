using System;
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
public class TranslateHelperTests
{
    private string _dir = null!;

    [TestInitialize]
    public void Setup()
    {
        _dir = Path.Combine(Path.GetTempPath(), "lp-translate-helper-" + Guid.NewGuid().ToString("N"));
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

    private static object? Static(string name, params object?[] args)
        => typeof(TranslateViewModel).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, args);

    private static object? Instance(TranslateViewModel vm, string name, params object?[] args)
        => typeof(TranslateViewModel).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(vm, args);

    [TestMethod]
    public void ComputeHash_IsStableAndContentSensitive()
    {
        var a = (string)Static("ComputeHash", "hello")!;
        var b = (string)Static("ComputeHash", "hello")!;
        var c = (string)Static("ComputeHash", "world")!;
        Assert.AreEqual(a, b);
        Assert.AreNotEqual(a, c);
        Assert.AreEqual(64, a.Length);
    }

    [TestMethod]
    public void ResolveImagePath_CombinesTranslationDirectoryAndDecodes()
    {
        var vm = new TranslateViewModel(new NoopFileDialogService());
        vm.OpenTranslationFilePath = Path.Combine(_dir, "translation.txt");

        var resolved = (string)Instance(vm, "ResolveImagePath", "img/a%20b.png")!;
        Assert.AreEqual(Path.Combine(_dir, "img", "a b.png"), resolved);

        var absolute = (string)Instance(vm, "ResolveImagePath", "/tmp/x.png")!;
        Assert.AreEqual(Path.GetFullPath("/tmp/x.png"), absolute);
        vm.OpenTranslationFilePath = null;
    }

    [TestMethod]
    public async Task BuildSnapshotAndAutosaveWriteBackup()
    {
        var vm = new TranslateViewModel(new NoopFileDialogService());
        await vm.AddImageFileAsync("a.png");
        vm.SelectedImageFile = "a.png";
        await vm.AddLabelAtAsync(0.25f, 0.75f, 2);

        var snapshot = (string)await (Task<string>)Instance(vm, "BuildSnapshotAsync")!;
        StringAssert.Contains(snapshot, "a.png");
        StringAssert.Contains(snapshot, "0.250");
        StringAssert.Contains(snapshot, ",2]");

        var path = Path.Combine(_dir, "translation.txt");
        vm.OpenTranslationFilePath = path;
        await (Task)Instance(vm, "AutoSaveTickAsync")!;
        var bak = Path.Combine(_dir, "bak");
        Assert.IsTrue(Directory.Exists(bak));
        Assert.AreEqual(1, Directory.GetFiles(bak, "*_translation.txt").Length);
        Assert.IsTrue(File.Exists(path));

        // Second tick with an unchanged snapshot should not create another backup.
        await (Task)Instance(vm, "AutoSaveTickAsync")!;
        Assert.AreEqual(1, Directory.GetFiles(bak, "*_translation.txt").Length);
        vm.OpenTranslationFilePath = null;
    }

    [TestMethod]
    public async Task MoveLabelAndTextNotificationWork()
    {
        var vm = new TranslateViewModel(new NoopFileDialogService());
        await vm.AddImageFileAsync("p.png");
        vm.SelectedImageFile = "p.png";
        await vm.AddLabelAtAsync(0.1f, 0.1f, 1);
        await vm.AddLabelAtAsync(0.2f, 0.2f, 2);

        vm.MoveLabelWithinCurrentImage(-1, 0);
        vm.MoveLabelWithinCurrentImage(5, 0);
        vm.MoveLabelWithinCurrentImage(0, 99);
        vm.MoveLabelWithinCurrentImage(0, 0);

        vm.SelectedLabel = new LabelItem { Text = "hello" };
        vm.CurrentText = "world";
        Assert.AreEqual("world", vm.SelectedLabel.Text);
    }
}
