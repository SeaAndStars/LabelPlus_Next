using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using LabelPlus_Next.ViewModels;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LabelPlus_Next.Test.ViewModels;

[TestClass]
[DoNotParallelize]
public class TeamWorkParsingTests
{
    [TestInitialize]
    public void Setup() => LabelPlus_Next.Services.LocalizationService.Apply("zh-CN");

    [TestCleanup]
    public void Cleanup() => LabelPlus_Next.Services.LocalizationService.Apply("default");

    private static object? Static(string name, params object?[] args)
        => typeof(TeamWorkViewModel).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, args);

    private static object? Instance(TeamWorkViewModel vm, string name, params object?[] args)
        => typeof(TeamWorkViewModel).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(vm, args);

    [TestMethod]
    public void BuildsTreeAndWiresCommands()
    {
        var vm = new TeamWorkViewModel(false);
        var episodes = new List<TeamWorkViewModel.EpisodeItem>
        {
            new()
            {
                Number = 1,
                Key = "01",
                DisplayNumber = "01",
                Status = "翻译",
                SourcePath = "/remote/source.zip",
                TranslatePath = "/remote/translate.zip",
                FilePaths = new List<string> { "/remote/files/a.png", "/remote/files/b.png" }
            },
            new() { Number = 2, Key = "02", DisplayNumber = "02", Status = "立项" }
        };

        Instance(vm, "BuildTreeSource", episodes);

        Assert.IsTrue(vm.TreeRows.Count > 2);
        var root = vm.TreeRows[0];
        Assert.IsTrue(root.IsExpanded);
        Assert.AreEqual(0, root.Depth);
        Assert.IsNotNull(root.ToggleCommand);
        Assert.IsNotNull(root.TranslateCommand);
        Assert.IsNotNull(root.ProofCommand);
        Assert.IsNotNull(root.TypesetCommand);
        Assert.IsNotNull(root.DownloadCommand);
        Assert.AreEqual(5, root.Children.Count);
        Assert.AreEqual(2, root.Episode.FilePaths!.Count);
        Assert.IsTrue(root.Children.Any(c => c.IsGroupContainer));
        Assert.IsTrue(root.Children.Single(c => c.IsGroupContainer).Children.All(c => c.IsFile));

        var expandedCount = vm.TreeRows.Count;
        vm.ToggleTreeNodeCommand.Execute(root);
        Assert.IsTrue(vm.TreeRows.Count < expandedCount);
        vm.ToggleTreeNodeCommand.Execute(root);
        Assert.AreEqual(expandedCount, vm.TreeRows.Count);
        vm.ToggleTreeNodeCommand.Execute(null);
    }

    [TestMethod]
    public void CreateNode_SetsStageRowsAndFileChildren()
    {
        var ep = new TeamWorkViewModel.EpisodeItem
        {
            Number = 3,
            DisplayNumber = "03",
            Status = "校对",
            SourcePath = "/src.zip",
            TranslatePath = "/trans.zip",
            ProofPath = "/proof.zip",
            TypesetPath = "/type.zip",
            SourceOwner = "a",
            TranslateOwner = "b",
            ProofOwner = "c",
            PublishOwner = "d",
            SourceCreatedAt = DateTimeOffset.UtcNow,
            FilePaths = new List<string> { "/f/a.png", "b.png", "" }
        };

        var node = (NodeItem)Static("CreateNode", ep)!;
        Assert.IsTrue(node.IsEpisodeRoot);
        Assert.AreEqual("03", node.Title);
        Assert.AreEqual(5, node.Children.Count);
        Assert.IsTrue(node.Children.Take(4).All(c => c.Status == "已上传"));
        Assert.AreEqual("a", node.Children[0].Owner);
        Assert.AreEqual("b", node.Children[1].Owner);
        Assert.AreEqual("c", node.Children[2].Owner);
        Assert.AreEqual("d", node.Children[3].Owner);
        var container = node.Children.First(c => c.IsGroupContainer);
        Assert.AreEqual(2, container.Children.Count);
        Assert.AreEqual("a.png", container.Children[0].Title);
    }

    [TestMethod]
    public void RemotePathsAndSanitizers()
    {
        Assert.AreEqual("a.png", Static("GetFileNameFromRemote", "/x/y/a.png"));
        Assert.AreEqual("b.png", Static("GetFileNameFromRemote", "b.png"));
        Assert.AreEqual("", Static("GetFileNameFromRemote", ""));
        Assert.AreEqual("a", Static("GetFileNameFromRemote", "/x/a/"));
        Assert.AreEqual("a_b", Static("SanitizeFileName", "a/b"));
        Assert.AreEqual("file", Static("SanitizeFileName", " "));
        var dir = (string)Static("BuildDownloadDirectory", "proj", "ep1")!;
        StringAssert.Contains(dir, "LabelPlus_Downloads");
    }

    [TestMethod]
    public void BomAndNumberHelpers()
    {
        var withBom = new byte[] { 0xEF, 0xBB, 0xBF, 1, 2 };
        CollectionAssert.AreEqual(new byte[] { 1, 2 }, (byte[])Static("TrimBom", withBom)!);
        CollectionAssert.AreEqual(new byte[] { 1, 2 }, (byte[])Static("TrimBom", new byte[] { 1, 2 })!);
        Assert.AreEqual(12, Static("ToInt", "ch12"));
        Assert.AreEqual(0, Static("ToInt", "abc"));
        Assert.AreEqual("-", Static("Fmt", (DateTimeOffset?)null));
        Assert.IsNotNull(Static("Fmt", (DateTimeOffset?)DateTimeOffset.UtcNow));
    }

    [TestMethod]
    public async Task ShowFiles_HandlesEmptyAndLongLists()
    {
        var vm = new TeamWorkViewModel(false);
        var method = typeof(TeamWorkViewModel).GetMethod("ShowFilesAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;

        await (Task)method.Invoke(vm, new object?[] { null })!;
        var ep = new TeamWorkViewModel.EpisodeItem();
        await (Task)method.Invoke(vm, new object?[] { ep })!;
        ep.FilePaths = Enumerable.Range(0, 60).Select(i => $"f{i}.png").ToList();
        await (Task)method.Invoke(vm, new object?[] { ep })!;
    }

    [TestMethod]
    public async Task DownloadFile_HandlesNullAndMissingPath()
    {
        var vm = new TeamWorkViewModel(false);
        var method = typeof(TeamWorkViewModel).GetMethod("DownloadFileAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        await (Task)method.Invoke(vm, new object?[] { null })!;
        await (Task)method.Invoke(vm, new object?[] { new NodeItem { Path = "" } })!;
    }
}
