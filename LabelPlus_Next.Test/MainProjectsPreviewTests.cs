using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using LabelPlus_Next.Services;
using LabelPlus_Next.ViewModels;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LabelPlus_Next.Test;

[TestClass]
[DoNotParallelize]
public class MainProjectsPreviewTests
{
    private const string OnePixelPng =
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9Wl2nH0AAAAASUVORK5CYII=";

    private string _dir = null!;

    [TestInitialize]
    public void Setup()
    {
        _dir = Path.Combine(Path.GetTempPath(), "lp-preview-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        LocalizationService.Apply("zh-CN");
    }

    [TestCleanup]
    public void Cleanup()
    {
        LocalizationService.Apply("default");
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); } catch { }
    }

    private static async Task Invoke(string name, MainProjectsViewModel vm, params object?[] args)
    {
        var method = typeof(MainProjectsViewModel).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!;
        await (Task)method.Invoke(vm, args)!;
    }

    private static void SetSelected(MainProjectsViewModel vm, RecentProject project)
    {
        var field = typeof(MainProjectsViewModel).GetField("_selectedProject", BindingFlags.NonPublic | BindingFlags.Instance)!;
        field.SetValue(vm, project);
    }

    private string MakeTranslation(string name, bool withLabels)
    {
        var path = Path.Combine(_dir, name);
        var lines = new System.Collections.Generic.List<string>
        {
            "1,0",
            "-",
            "-",
            ">>>>>>>>[a.png]<<<<<<<<"
        };
        if (withLabels)
        {
            lines.Add("----------------[1]----------------[0.100,0.200,1]");
            lines.Add("hello");
            lines.Add("");
        }
        lines.Add(">>>>>>>>[b.png]<<<<<<<<");
        File.WriteAllLines(path, lines);
        return path;
    }

    private void MakeImages()
    {
        var bytes = Convert.FromBase64String(OnePixelPng);
        File.WriteAllBytes(Path.Combine(_dir, "a.png"), bytes);
        File.WriteAllBytes(Path.Combine(_dir, "b.png"), bytes);
    }

    [TestMethod]
    public async Task Preview_LoadsNamesLabelsAndNavigation()
    {
        MakeImages();
        var translation = MakeTranslation("translation.txt", withLabels: true);
        var project = new RecentProject { Name = "p", TranslationFile = translation, Folder = _dir, LastOpened = DateTimeOffset.Now };
        var recent = Path.Combine(_dir, "recent.json");
        File.WriteAllText(recent, "[]");
        var vm = new MainProjectsViewModel(new NoopFileDialogService(), recent, new InlineUiDispatcher());
        SetSelected(vm, project);

        await Invoke("LoadPreviewAsync", vm, project);
        Assert.AreEqual(2, vm.PreviewImageNames.Count);
        Assert.AreEqual(0, vm.CurrentPreviewIndex);
        Assert.AreEqual("a.png", vm.CurrentPreviewFileName);
        Assert.AreEqual("1/2", vm.PreviewStatus);
        Assert.AreEqual(1, vm.CurrentPreviewLabels.Count);
        Assert.AreEqual(1, vm.CurrentPreviewLabels[0].Category);

        await Invoke("LoadPreviewAtAsync", vm, 1);
        Assert.AreEqual(1, vm.CurrentPreviewIndex);
        Assert.AreEqual("b.png", vm.CurrentPreviewFileName);
        Assert.AreEqual("2/2", vm.PreviewStatus);

        vm.NextPreviewCommand.Execute(null);
        vm.PrevPreviewCommand.Execute(null);
        await Task.Delay(50);
        Assert.IsNotNull(vm.CurrentPreviewFileName);
    }

    [TestMethod]
    public async Task Preview_MissingFileAndEmptyNamesSetStatus()
    {
        var missing = new RecentProject { Name = "m", TranslationFile = Path.Combine(_dir, "missing.txt"), Folder = _dir };
        var recent = Path.Combine(_dir, "recent.json");
        File.WriteAllText(recent, "[]");
        var vm = new MainProjectsViewModel(new NoopFileDialogService(), recent, new InlineUiDispatcher());
        SetSelected(vm, missing);

        await Invoke("LoadPreviewAsync", vm, missing);
        StringAssert.Contains(vm.PreviewStatus ?? string.Empty, "文件不存在");

        var empty = Path.Combine(_dir, "empty.txt");
        File.WriteAllText(empty, "no entries");
        var emptyProject = new RecentProject { Name = "e", TranslationFile = empty, Folder = _dir };
        SetSelected(vm, emptyProject);
        await Invoke("LoadPreviewAsync", vm, emptyProject);
        StringAssert.Contains(vm.PreviewStatus ?? string.Empty, "无图片条目");
    }

    [TestMethod]
    public async Task Preview_NullProjectClearsState()
    {
        var recent = Path.Combine(_dir, "recent.json");
        File.WriteAllText(recent, "[]");
        var vm = new MainProjectsViewModel(new NoopFileDialogService(), recent, new InlineUiDispatcher());
        await Invoke("LoadPreviewAsync", vm, (object?)null);
        Assert.IsNull(vm.PreviewStatus);
        Assert.AreEqual(-1, vm.CurrentPreviewIndex);
    }
}
