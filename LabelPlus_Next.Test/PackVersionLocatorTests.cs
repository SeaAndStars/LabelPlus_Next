using System;
using System.IO;
using System.Linq;
using System.Reflection;
using LabelPlus_Next.Tools.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LabelPlus_Next.Test;

[TestClass]
[DoNotParallelize]
public class PackVersionLocatorTests
{
    private string _dir = null!;

    [TestInitialize]
    public void Setup()
    {
        _dir = Path.Combine(Path.GetTempPath(), "lp-pack-locator-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); } catch { }
    }

    [TestMethod]
    public void FindVersionFilesInFolder_HandlesDirectAndLegacyFiles()
    {
        Assert.AreEqual(0, PackVersionLocator.FindVersionFilesInFolder(null).Count);
        Assert.AreEqual(0, PackVersionLocator.FindVersionFilesInFolder(Path.Combine(_dir, "missing")).Count);
        Assert.AreEqual(0, PackVersionLocator.FindVersionFilesInFolder(_dir).Count);

        File.WriteAllText(Path.Combine(_dir, "Update.version.json"), "{}");
        File.WriteAllText(Path.Combine(_dir, "Client.version.json"), "{}");
        File.WriteAllText(Path.Combine(_dir, "Clietn.version.json"), "{}");
        var files = PackVersionLocator.FindVersionFilesInFolder(_dir);
        Assert.AreEqual(2, files.Count);
        Assert.IsTrue(files.Any(f => f.EndsWith("Update.version.json")));
        Assert.IsTrue(files.Any(f => f.EndsWith("Client.version.json")));

        var legacyDir = Path.Combine(_dir, "legacy");
        Directory.CreateDirectory(legacyDir);
        File.WriteAllText(Path.Combine(legacyDir, "Clietn.version.json"), "{}");
        var legacy = PackVersionLocator.FindVersionFilesInFolder(legacyDir);
        Assert.AreEqual(1, legacy.Count);
        StringAssert.Contains(legacy[0], "Clietn.version.json");
    }

    [TestMethod]
    public void LooksLikeSourceProject_DetectsCsprojAndSolution()
    {
        Assert.IsFalse(PackVersionLocator.LooksLikeSourceProject(null));
        Assert.IsFalse(PackVersionLocator.LooksLikeSourceProject(_dir));

        File.WriteAllText(Path.Combine(_dir, "app.csproj"), "<Project/>");
        Assert.IsTrue(PackVersionLocator.LooksLikeSourceProject(_dir));

        var slnDir = Path.Combine(_dir, "sln");
        Directory.CreateDirectory(slnDir);
        File.WriteAllText(Path.Combine(slnDir, "app.sln"), "");
        Assert.IsTrue(PackVersionLocator.LooksLikeSourceProject(slnDir));
    }

    [TestMethod]
    public void FindRepoRoot_FindsSolutionAndProjectLayouts()
    {
        var repo = Path.Combine(_dir, "repo");
        Directory.CreateDirectory(Path.Combine(repo, "LabelPlus_Next"));
        Directory.CreateDirectory(Path.Combine(repo, "LabelPlus_Next.Update"));
        var sln = Path.Combine(repo, "repo.sln");
        File.WriteAllText(sln, "");

        Assert.AreEqual(Path.GetFullPath(repo), PackVersionLocator.FindRepoRoot(repo, null, false));
        Assert.AreEqual(Path.GetFullPath(repo), PackVersionLocator.FindRepoRoot(null, sln, false));
        Assert.IsNull(PackVersionLocator.FindRepoRoot(null, null, false));
    }

    [TestMethod]
    public void FindVersionFilesInRepo_DedupesPrefersCorrectSpellingAndSkipsIgnored()
    {
        var repo = Path.Combine(_dir, "repo2");
        var app = Path.Combine(repo, "LabelPlus_Next");
        var update = Path.Combine(repo, "LabelPlus_Next.Update");
        Directory.CreateDirectory(app);
        Directory.CreateDirectory(update);
        Directory.CreateDirectory(Path.Combine(repo, "bin"));
        Directory.CreateDirectory(Path.Combine(repo, "sub"));
        File.WriteAllText(Path.Combine(app, "Client.version.json"), "{}");
        File.WriteAllText(Path.Combine(update, "Update.version.json"), "{}");
        File.WriteAllText(Path.Combine(repo, "Client.version.json"), "{}");
        File.WriteAllText(Path.Combine(repo, "sub", "Clietn.version.json"), "{}");
        File.WriteAllText(Path.Combine(repo, "sub", "Client.version.json"), "{}");

        var files = PackVersionLocator.FindVersionFilesInRepo(repo);
        Assert.IsTrue(files.Any(f => f.EndsWith(Path.Combine("LabelPlus_Next", "Client.version.json"))));
        Assert.IsTrue(files.Any(f => f.EndsWith(Path.Combine("LabelPlus_Next.Update", "Update.version.json"))));
        Assert.AreEqual(files.Distinct(StringComparer.OrdinalIgnoreCase).Count(), files.Count);
        Assert.IsFalse(files.Any(f => f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar)));
        Assert.IsFalse(files.Any(f => f.EndsWith("sub" + Path.DirectorySeparatorChar + "Clietn.version.json")));
    }

    [TestMethod]
    public void PrivateHelpers_HandleFilePathsAsDirectories()
    {
        var file = Path.Combine(_dir, "plain.txt");
        File.WriteAllText(file, "");
        var looks = typeof(PackVersionLocator).GetMethod("LooksLikeRepoRoot", BindingFlags.NonPublic | BindingFlags.Static)!;
        Assert.IsFalse((bool)looks.Invoke(null, new object[] { file })!);
        var safe = typeof(PackVersionLocator).GetMethod("SafeEnumerateDirectories", BindingFlags.NonPublic | BindingFlags.Static)!;
        var result = (System.Collections.Generic.IEnumerable<string>)safe.Invoke(null, new object[] { file })!;
        Assert.AreEqual(0, result.Count());
    }
}
