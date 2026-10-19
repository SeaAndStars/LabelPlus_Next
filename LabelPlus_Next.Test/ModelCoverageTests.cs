using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text.Json;
using System.Windows.Input;
using Avalonia;
using LabelPlus_Next.Services;
using LabelPlus_Next.Shared;
using LabelPlus_Next.Tools.Models;
using LabelPlus_Next.ViewModels;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LabelPlus_Next.Test;

[TestClass]
[DoNotParallelize]
public class ModelCoverageTests
{
    [TestInitialize]
    public void Setup() => LocalizationService.Apply("en");

    [TestCleanup]
    public void Cleanup() => LocalizationService.Apply("default");

    [TestMethod]
    public void MetaNode_ComputesIndentAndChevronAndNotifies()
    {
        var node = new MetaNode { Name = "ch1", Number = 1, IsVolume = false, IsSpecial = false };
        var changed = new List<string?>();
        node.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        Assert.AreEqual(new Thickness(0), node.IndentMargin);
        Assert.AreEqual("›", node.ChevronGlyph);
        Assert.IsFalse(node.HasChildren);
        Assert.AreEqual("立项", node.Status);

        node.Depth = 2;
        node.IsExpanded = true;
        node.Children.Add(new MetaNode());

        Assert.AreEqual(new Thickness(36, 0, 0, 0), node.IndentMargin);
        Assert.AreEqual("⌄", node.ChevronGlyph);
        Assert.IsTrue(node.HasChildren);
        CollectionAssert.Contains(changed, nameof(MetaNode.IndentMargin));
        CollectionAssert.Contains(changed, nameof(MetaNode.ChevronGlyph));

        node.Include = true;
        Assert.IsTrue(node.Include);
        node.ToggleCommand = null;
        Assert.IsNull(node.ToggleCommand);
    }

    [TestMethod]
    public void NodeItem_ExposesFileCountAndTreeState()
    {
        var node = new NodeItem { Title = "source", Status = "done" };
        var changed = new List<string?>();
        node.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        Assert.AreEqual("0", node.FileCountText);
        Assert.AreEqual("›", node.ChevronGlyph);
        Assert.IsFalse(node.HasChildren);

        node.Episode = new TeamWorkViewModel.EpisodeItem
        {
            FilePaths = new List<string> { "a", "b", "c" }
        };
        Assert.AreEqual("3", node.FileCountText);

        node.Depth = 1;
        node.IsExpanded = true;
        node.Children.Add(new NodeItem());
        Assert.AreEqual(new Thickness(18, 0, 0, 0), node.IndentMargin);
        Assert.AreEqual("⌄", node.ChevronGlyph);
        Assert.IsTrue(node.HasChildren);
        CollectionAssert.Contains(changed, nameof(NodeItem.IndentMargin));
        CollectionAssert.Contains(changed, nameof(NodeItem.ChevronGlyph));

        node.IsFile = true;
        node.IsEpisodeRoot = true;
        node.IsGroupContainer = true;
        Assert.IsTrue(node.IsFile);
        Assert.IsTrue(node.IsEpisodeRoot);
        Assert.IsTrue(node.IsGroupContainer);
        Assert.IsNull(node.ToggleCommand);
        Assert.IsNull(node.TranslateCommand);
        Assert.IsNull(node.ProofCommand);
        Assert.IsNull(node.TypesetCommand);
        Assert.IsNull(node.DownloadCommand);
    }

    [TestMethod]
    public void EpisodeItem_FormatsInfoAndRaisesPropertyChanged()
    {
        var episode = new TeamWorkViewModel.EpisodeItem { Number = 3, Key = "03", DisplayNumber = "03" };
        var changed = new List<string?>();
        episode.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        StringAssert.Contains(episode.SourceInfo, "Owner: -");
        StringAssert.Contains(episode.TranslateInfo, "Owner: -");
        StringAssert.Contains(episode.ProofInfo, "Owner: -");
        StringAssert.Contains(episode.PublishInfo, "Owner: -");

        episode.Status = "done";
        episode.TranslatePath = "/translate.txt";
        CollectionAssert.Contains(changed, nameof(TeamWorkViewModel.EpisodeItem.Status));
        CollectionAssert.Contains(changed, nameof(TeamWorkViewModel.EpisodeItem.TranslatePath));

        episode.SourceOwner = "alice";
        episode.SourceCreatedAt = DateTimeOffset.UtcNow;
        StringAssert.Contains(episode.SourceInfo, "alice");
        episode.PublishOwner = "bob";
        episode.PublishUpdatedAt = DateTimeOffset.UtcNow;
        StringAssert.Contains(episode.PublishInfo, "bob");
    }

    [TestMethod]
    public void DavNode_ExpansionNotifiesAndChildrenWork()
    {
        var node = new DavNode { Name = "root", Uri = "/dav/root", IsCollection = true };
        var changed = new List<string?>();
        node.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        Assert.IsFalse(node.IsExpanded);
        node.IsExpanded = true;
        Assert.IsTrue(node.IsExpanded);
        CollectionAssert.Contains(changed, nameof(DavNode.IsExpanded));
        node.Children.Add(new DavNode { Name = "child" });
        Assert.AreEqual(1, node.Children.Count);
        Assert.AreEqual("root", node.Name);
        Assert.AreEqual("/dav/root", node.Uri);
        Assert.IsTrue(node.IsCollection);
    }

    [TestMethod]
    public void Manifest_RoundTripsJson()
    {
        var manifest = new Manifest
        {
            Schema = "v1",
            GeneratedAt = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero),
            Projects =
            {
                ["LabelPlus_Next.Desktop"] = new Project
                {
                    Latest = "1.3.3",
                    Releases =
                    {
                        new ProjectRelease
                        {
                            Version = "1.3.3",
                            Url = "https://example.invalid/pkg.zip",
                            Files =
                            {
                                new ProjectReleaseFile
                                {
                                    Name = "win.zip",
                                    Url = "https://example.invalid/win.zip",
                                    Sha256 = "abc",
                                    Size = 42,
                                    Entry = "app",
                                    EntryWindows = "app.exe",
                                    EntryLinux = "app",
                                    EntryMacos = "app"
                                }
                            }
                        }
                    }
                }
            }
        };

        var json = JsonSerializer.Serialize(manifest);
        var round = JsonSerializer.Deserialize<Manifest>(json);
        Assert.IsNotNull(round);
        Assert.AreEqual("v1", round!.Schema);
        Assert.AreEqual("1.3.3", round.Projects["LabelPlus_Next.Desktop"].Latest);
        var release = round.Projects["LabelPlus_Next.Desktop"].Releases.Single();
        Assert.AreEqual("1.3.3", release.Version);
        Assert.AreEqual(42, release.Files.Single().Size);
        Assert.AreEqual("app.exe", release.Files.Single().EntryWindows);
    }

    [TestMethod]
    public void AlistUrl_ParsesStoragePathAndFlags()
    {
        Assert.IsTrue(AlistUrl.TryGetStoragePath("https://host.example/d/OneDrive2/Update/manifest.json", out var p1));
        Assert.AreEqual("/OneDrive2/Update/manifest.json", p1);
        Assert.IsTrue(AlistUrl.TryGetStoragePath("https://host.example/dav/a%20b/c.zip", out var p2));
        Assert.AreEqual("/a b/c.zip", p2);
        Assert.IsFalse(AlistUrl.TryGetStoragePath("https://host.example/other/a", out _));
        Assert.IsFalse(AlistUrl.TryGetStoragePath("not-a-url", out _));
        Assert.IsFalse(AlistUrl.TryGetStoragePath(null, out _));

        Assert.AreEqual("https://host/d/a?download=1", AlistUrl.WithDownloadFlag("https://host/d/a"));
        Assert.AreEqual("https://host/d/a?x=1&download=1", AlistUrl.WithDownloadFlag("https://host/d/a?x=1"));
        Assert.AreEqual("https://host/d/a?download=1", AlistUrl.WithDownloadFlag("https://host/d/a?download=1"));
        Assert.AreEqual("https://host/x", AlistUrl.WithDownloadFlag("https://host/x"));
        Assert.IsTrue(AlistUrl.IsBaiduHost("https://pan.baidu.com/s/1"));
        Assert.IsTrue(AlistUrl.IsBaiduHost("https://x.baidupcs.com/file"));
        Assert.IsFalse(AlistUrl.IsBaiduHost("https://example.com"));
    }

    [TestMethod]
    public void UpdateVersion_HandlesCoreAndPrereleaseOrdering()
    {
        Assert.IsTrue(UpdateVersion.IsNewer("1.3.3", "1.3.2"));
        Assert.IsFalse(UpdateVersion.IsNewer("1.3.2", "1.3.2"));
        Assert.IsTrue(UpdateVersion.IsNewer("1.3.2", null));
        Assert.IsFalse(UpdateVersion.IsNewer(null, "1.3.2"));
        Assert.IsTrue(UpdateVersion.Compare("v1.4.0", "1.3.9") > 0);
        Assert.IsTrue(UpdateVersion.Compare("1.3.2-beta.2", "1.3.2-beta.1") > 0);
        Assert.IsTrue(UpdateVersion.Compare("1.3.2", "1.3.2-beta.9") > 0);
        Assert.IsTrue(UpdateVersion.Compare("1.3.2-beta.1", "1.3.2") < 0);
        Assert.IsTrue(UpdateVersion.Compare("1.3.2-alpha", "1.3.2-beta") < 0);
        Assert.AreEqual(0, UpdateVersion.Compare("1.3.2+build", "1.3.2"));
    }
}
