using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
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
