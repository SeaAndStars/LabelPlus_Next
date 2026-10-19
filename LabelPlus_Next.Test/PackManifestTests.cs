using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using LabelPlus_Next.Tools.ViewModels;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LabelPlus_Next.Test;

[TestClass]
public class PackManifestTests
{
    private static object? Call(string name, params object?[] args)
        => typeof(PackWindowViewModel).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, args);

    private static IDictionary Projects(object manifest)
        => (IDictionary)manifest.GetType().GetProperty("Projects")!.GetValue(manifest)!;

    private static object Project(object manifest, string name)
        => Projects(manifest)[name]!;

    private static List<object> Releases(object manifest, string name)
    {
        var releases = Project(manifest, name).GetType().GetProperty("Releases")!.GetValue(Project(manifest, name))!;
        return ((IEnumerable)releases!).Cast<object>().ToList();
    }

    private static string? Latest(object manifest, string name)
        => (string?)Project(manifest, name).GetType().GetProperty("Latest")!.GetValue(Project(manifest, name));

    private static string? Version(object release)
        => (string?)release.GetType().GetProperty("Version")!.GetValue(release);

    private static object NewReleaseFile(string name, string url)
    {
        var type = typeof(PackWindowViewModel).GetNestedType("ReleaseFile", BindingFlags.NonPublic)!;
        var file = Activator.CreateInstance(type, nonPublic: true)!;
        type.GetProperty("Name")!.SetValue(file, name);
        type.GetProperty("Url")!.SetValue(file, url);
        type.GetProperty("Sha256")!.SetValue(file, "abc");
        type.GetProperty("Size")!.SetValue(file, 3L);
        return file;
    }

    [TestMethod]
    public void MigrateOldToV1_HandlesNullArrayItemsAndSingleObject()
    {
        var empty = Call("MigrateOldToV1", "proj", null)!;
        Assert.AreEqual(0, Releases(empty, "proj").Count);
        Assert.IsNull(Latest(empty, "proj"));

        var array = Call("MigrateOldToV1", "proj", "[{\"version\":\"1.0\",\"url\":\"u1\"},{\"version\":\"2.0\",\"url\":\"u2\"},{\"version\":\"\",\"url\":\"x\"}]")!;
        var releases = Releases(array, "proj");
        Assert.AreEqual(2, releases.Count);
        Assert.AreEqual("2.0", Version(releases[0]));
        Assert.AreEqual("2.0", Latest(array, "proj"));

        var items = Call("MigrateOldToV1", "proj", "{\"items\":[{\"version\":\"3.0\",\"url\":\"u3\"}]}")!;
        Assert.AreEqual(1, Releases(items, "proj").Count);
        Assert.AreEqual("3.0", Latest(items, "proj"));

        var single = Call("MigrateOldToV1", "proj", "{\"version\":\"4.0\",\"url\":\"u4\"}")!;
        Assert.AreEqual(1, Releases(single, "proj").Count);
        Assert.AreEqual("4.0", Latest(single, "proj"));

        var invalid = Call("MigrateOldToV1", "proj", "{ invalid")!;
        Assert.AreEqual(0, Releases(invalid, "proj").Count);
    }

    [TestMethod]
    public void UpsertV1_AddsUpdatesAndDedupesFiles()
    {
        var manifest = Call("MigrateOldToV1", "proj", null)!;
        var file1 = NewReleaseFile("linux.zip", "https://u/linux.zip");
        Call("UpsertV1", manifest, "proj", "1.0", "https://u/1.0", file1);

        var releases = Releases(manifest, "proj");
        Assert.AreEqual(1, releases.Count);
        Assert.AreEqual("1.0", Latest(manifest, "proj"));
        var files = (IEnumerable)releases[0].GetType().GetProperty("Files")!.GetValue(releases[0])!;
        Assert.AreEqual(1, files.Cast<object>().Count());

        var file2 = NewReleaseFile("win.zip", "https://u/win.zip");
        Call("UpsertV1", manifest, "proj", "1.0", "https://u/other", file2);
        releases = Releases(manifest, "proj");
        files = (IEnumerable)releases[0].GetType().GetProperty("Files")!.GetValue(releases[0])!;
        Assert.AreEqual(2, files.Cast<object>().Count());
        Assert.AreEqual("https://u/1.0", (string?)releases[0].GetType().GetProperty("Url")!.GetValue(releases[0]));

        var file3 = NewReleaseFile("win.zip", "https://u/win2.zip");
        Call("UpsertV1", manifest, "proj", "1.0", null, file3);
        files = (IEnumerable)releases[0].GetType().GetProperty("Files")!.GetValue(releases[0])!;
        Assert.AreEqual(2, files.Cast<object>().Count());

        Call("UpsertV1", manifest, "proj", "2.0", "https://u/2.0", null);
        Assert.AreEqual("2.0", Latest(manifest, "proj"));
        Assert.AreEqual(2, Releases(manifest, "proj").Count);

        var list = Project(manifest, "proj").GetType().GetProperty("Releases")!.GetValue(Project(manifest, "proj"))!;
        var found = Call("FindByVersion", list, "2.0");
        Assert.IsNotNull(found);
        Assert.IsNull(Call("FindByVersion", list, "9.9"));
    }
}
