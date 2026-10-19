using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using LabelPlus_Next.Tools.Services;
using LabelPlus_Next.Tools.ViewModels;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LabelPlus_Next.Test;

[TestClass]
public class PackVersionLocatorTests
{
    private string root = null!;
    private string repo = null!;
    private string desktopProject = null!;
    private string mainProject = null!;
    private string updateProject = null!;
    private string solution = null!;

    [TestInitialize]
    public void Setup()
    {
        root = Path.Combine(Path.GetTempPath(), "labelplus-pack-tests", Guid.NewGuid().ToString("N"));
        repo = Path.Combine(root, "Repo");
        desktopProject = Path.Combine(repo, "LabelPlus_Next.Desktop");
        mainProject = Path.Combine(repo, "LabelPlus_Next");
        updateProject = Path.Combine(repo, "LabelPlus_Next.Update");

        Directory.CreateDirectory(desktopProject);
        Directory.CreateDirectory(mainProject);
        Directory.CreateDirectory(updateProject);

        solution = Path.Combine(repo, "LabelPlus_Next.sln");
        File.WriteAllText(solution, string.Empty);
        File.WriteAllText(Path.Combine(desktopProject, "LabelPlus_Next.Desktop.csproj"), "<Project/>");
        File.WriteAllText(Path.Combine(mainProject, "Client.version.json"),
            "{\"project\":\"LabelPlus_Next.Desktop\",\"version\":\"1.2.3\",\"manifest\":\"https://example.test/d/Update/manifest.json\"}");
        File.WriteAllText(Path.Combine(updateProject, "Update.version.json"),
            "{\"project\":\"LabelPlus_Next.Update\",\"version\":\"1.2.3\"}");
    }

    [TestCleanup]
    public void Cleanup()
    {
        try
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
        catch
        {
            // best effort
        }
    }

    [TestMethod]
    public void FindRepoRoot_FromSourceProject()
    {
        Assert.AreEqual(repo, PackVersionLocator.FindRepoRoot(desktopProject, null));
    }

    [TestMethod]
    public void FindRepoRoot_FromNestedBuildFolder()
    {
        var nested = Path.Combine(desktopProject, "bin", "Debug", "net10.0");
        Directory.CreateDirectory(nested);

        Assert.AreEqual(repo, PackVersionLocator.FindRepoRoot(nested, null));
    }

    [TestMethod]
    public void FindRepoRoot_FromSolution()
    {
        Assert.AreEqual(repo, PackVersionLocator.FindRepoRoot(null, solution));
    }

    [TestMethod]
    public void LooksLikeSourceProject_DetectsProjectsAndRejectsRandomFolders()
    {
        Assert.IsTrue(PackVersionLocator.LooksLikeSourceProject(desktopProject));
        Assert.IsTrue(PackVersionLocator.LooksLikeSourceProject(repo));

        var random = Path.Combine(root, "Downloads");
        Directory.CreateDirectory(random);
        Assert.IsFalse(PackVersionLocator.LooksLikeSourceProject(random));
        Assert.IsFalse(PackVersionLocator.LooksLikeSourceProject(Path.Combine(root, "missing")));
    }

    [TestMethod]
    public void FindRepoRoot_DoesNotUseToolDirectoryWhenDisabled()
    {
        var random = Path.Combine(root, "Downloads");
        Directory.CreateDirectory(random);

        Assert.IsNull(PackVersionLocator.FindRepoRoot(random, null, allowAppDirectoryFallback: false));
    }

    [TestMethod]
    public void FindVersionFilesInFolder_OnlyUsesThatFolder()
    {
        Assert.AreEqual(0, PackVersionLocator.FindVersionFilesInFolder(desktopProject).Count);
        Assert.AreEqual(1, PackVersionLocator.FindVersionFilesInFolder(mainProject).Count);
        Assert.AreEqual(0, PackVersionLocator.FindVersionFilesInFolder(Path.Combine(root, "missing")).Count);
    }

    [TestMethod]
    public void FindVersionFilesInRepo_PrefersKnownProjectLocations()
    {
        var files = PackVersionLocator.FindVersionFilesInRepo(repo);

        Assert.AreEqual(2, files.Count);
        StringAssert.EndsWith(files[0], Path.Combine("LabelPlus_Next", "Client.version.json"));
        StringAssert.EndsWith(files[1], Path.Combine("LabelPlus_Next.Update", "Update.version.json"));
    }

    [TestMethod]
    public void FindVersionFilesInFolder_HandlesLegacyMisspelling()
    {
        var legacy = Path.Combine(repo, "Legacy");
        Directory.CreateDirectory(legacy);
        var typo = Path.Combine(legacy, "Clietn.version.json");
        File.WriteAllText(typo, "{\"project\":\"Legacy\",\"version\":\"1.0.0\"}");

        Assert.AreEqual(1, PackVersionLocator.FindVersionFilesInFolder(legacy).Count);

        File.WriteAllText(Path.Combine(legacy, "Client.version.json"), "{\"project\":\"Legacy\",\"version\":\"1.0.0\"}");
        var files = PackVersionLocator.FindVersionFilesInFolder(legacy);
        Assert.AreEqual(1, files.Count);
        Assert.AreEqual("Client.version.json", Path.GetFileName(files[0]));
    }

    [TestMethod]
    public async Task ViewModel_LoadsVersionFromRepo_WhenFolderHasNoVersionFile()
    {
        var vm = new PackWindowViewModel
        {
            SelectedFolder = desktopProject,
            SolutionPath = solution
        };

        await vm.RefreshVersionInfoAsync();

        Assert.IsFalse(vm.HasLocalVersionFiles, "the desktop source folder has no version file of its own");
        Assert.AreEqual("LabelPlus_Next.Desktop", vm.Project);
        Assert.AreEqual("1.2.3", vm.Version);
        Assert.AreEqual("https://example.test/d/Update/manifest.json", vm.ManifestUrl);
        StringAssert.Contains(vm.Status, "仓库");
    }

    [TestMethod]
    public async Task ViewModel_FindsRepoWithoutSolution()
    {
        var vm = new PackWindowViewModel
        {
            SelectedFolder = desktopProject
        };

        await vm.RefreshVersionInfoAsync();

        Assert.IsFalse(vm.HasLocalVersionFiles);
        Assert.AreEqual("LabelPlus_Next.Desktop", vm.Project);
        Assert.AreEqual("1.2.3", vm.Version);
    }

    [TestMethod]
    public async Task ViewModel_ReportsMissingVersionsForNonProjectFolder()
    {
        var random = Path.Combine(root, "Downloads");
        Directory.CreateDirectory(random);
        var vm = new PackWindowViewModel
        {
            SelectedFolder = random
        };

        await vm.RefreshVersionInfoAsync();

        Assert.IsFalse(vm.HasLocalVersionFiles);
        Assert.IsNull(vm.Project);
        StringAssert.Contains(vm.Status, "未找到版本文件");
    }

    [TestMethod]
    public async Task ViewModel_UsesSelectedProjectVersion_WhenRepoHasSeveralProjects()
    {
        var vm = new PackWindowViewModel
        {
            SelectedFolder = desktopProject,
            SolutionPath = solution
        };

        await vm.RefreshVersionInfoAsync();

        // The desktop project must win over the update project in the display.
        Assert.AreEqual("LabelPlus_Next.Desktop", vm.Project);
    }

    [TestMethod]
    public async Task ViewModel_MarksPreparedPackageFolder()
    {
        var vm = new PackWindowViewModel
        {
            SelectedFolder = mainProject,
            SolutionPath = solution
        };

        await vm.RefreshVersionInfoAsync();

        Assert.IsTrue(vm.HasLocalVersionFiles);
        Assert.AreEqual("LabelPlus_Next.Desktop", vm.Project);
        Assert.AreEqual("1.2.3", vm.Version);
    }
}
