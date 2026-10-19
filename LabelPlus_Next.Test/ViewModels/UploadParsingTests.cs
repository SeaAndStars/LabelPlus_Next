using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using LabelPlus_Next.Models;
using LabelPlus_Next.Services;
using LabelPlus_Next.ViewModels;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LabelPlus_Next.Test.ViewModels;

[TestClass]
[DoNotParallelize]
public class UploadParsingTests
{
    private sealed class MemorySettings : ISettingsService
    {
        public Task<AppSettings> LoadAsync(CancellationToken ct = default) => Task.FromResult(new AppSettings());
        public Task SaveAsync(AppSettings settings, CancellationToken ct = default) => Task.CompletedTask;
    }

    private string _dir = null!;

    [TestInitialize]
    public void Setup()
    {
        _dir = Path.Combine(Path.GetTempPath(), "lp-upload-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); } catch { }
    }

    private static object? Call(string name, params object?[] args)
        => typeof(UploadViewModel).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, args);

    [TestMethod]
    public void StatusRankingAndSpecialNames()
    {
        Assert.AreEqual(4, Call("StatusRank", "发布"));
        Assert.AreEqual(3, Call("StatusRank", "嵌字"));
        Assert.AreEqual(2, Call("StatusRank", "校对"));
        Assert.AreEqual(1, Call("StatusRank", "翻译"));
        Assert.AreEqual(0, Call("StatusRank", "unknown"));
        Assert.AreEqual("嵌字", Call("PickHigherStatus", "立项", "嵌字"));
        Assert.AreEqual("发布", Call("PickHigherStatus", "发布", "校对"));

        Assert.IsTrue((bool)Call("IsSpecialName", "番外01")!);
        Assert.IsTrue((bool)Call("IsSpecialName", "SP1")!);
        Assert.IsTrue((bool)Call("IsSpecialName", "特别篇2")!);
        Assert.IsFalse((bool)Call("IsSpecialName", "第01话")!);
        Assert.IsFalse((bool)Call("IsSpecialName", (object?)null)!);

        Assert.IsTrue((bool)Call("ContainsVolumeKeyword", "第1卷")!);
        Assert.IsTrue((bool)Call("ContainsVolumeKeyword", "vol.2")!);
        Assert.IsFalse((bool)Call("ContainsVolumeKeyword", "第1话")!);
    }

    [TestMethod]
    public void ParsesEpisodeSpecialAndRangeNumbers()
    {
        var args = new object?[] { "番外03", 0, null };
        Assert.IsTrue((bool)Call("TryParseSpecialNumber", args)!);
        Assert.AreEqual(3, args[1]);

        args = new object?[] { "特别篇2", 0, null };
        Assert.IsTrue((bool)Call("TryParseSpecialNumber", args)!);
        Assert.AreEqual(2, args[1]);

        args = new object?[] { "第12话", 0 };
        Assert.IsTrue((bool)Call("TryParseEpisodeNumber", args)!);
        Assert.AreEqual(12, args[1]);
        args = new object?[] { "第3卷", 0 };
        Assert.IsFalse((bool)Call("TryParseEpisodeNumber", args)!);

        args = new object?[] { "12-15", 0 };
        Assert.IsTrue((bool)Call("TryParseRangeNumber", args)!);
        Assert.AreEqual(15, args[1]);
        args = new object?[] { "abc", 0 };
        Assert.IsFalse((bool)Call("TryParseRangeNumber", args)!);

        Assert.AreEqual(12, Call("ParseChineseNumeral", "十二"));
        Assert.AreEqual(23, Call("ParseChineseNumeral", "二十三"));
        Assert.AreEqual(105, Call("ParseChineseNumeral", "一百零五"));
        Assert.AreEqual(0, Call("ParseChineseNumeral", "x"));
    }

    [TestMethod]
    public void ParsesEpisodeOrVolumeKinds()
    {
        var result = (ITuple)Call("ParseEpisodeOrVolume", "第12话")!;
        Assert.AreEqual(12, result[1]);

        result = (ITuple)Call("ParseEpisodeOrVolume", "第3卷")!;
        Assert.AreEqual(3, result[1]);

        result = (ITuple)Call("ParseEpisodeOrVolume", "番外05")!;
        Assert.AreEqual(0, result[1]);

        result = (ITuple)Call("ParseEpisodeOrVolume", "random")!;
        Assert.AreEqual(0, result[1]);
    }

    [TestMethod]
    public void BuildsSubDirsKeysAndNames()
    {
        var ep = new EpisodeEntry { Number = 3, Status = "翻译" };
        Assert.AreEqual("03", Call("BuildSubDir", ep));
        Assert.AreEqual("03", Call("BuildEpisodeKey", ep, 3));

        ep = new EpisodeEntry { Number = 0, IsSpecial = true, Display = "番外" };
        Assert.AreEqual("番外", Call("BuildSubDir", ep));
        ep.Number = 5;
        StringAssert.Contains((string)Call("BuildEpisodeKey", ep, 5)!, "番外");

        ep = new EpisodeEntry { Number = 2, IsVolume = true };
        StringAssert.Contains((string)Call("BuildSubDir", ep)!, "卷");

        ep = new EpisodeEntry { Number = 0, IsMisc = true };
        Assert.AreEqual("杂项", Call("BuildSubDir", ep));

        var name = (string)Call("BuildMetaName", ep, null)!;
        Assert.IsNotNull(name);
    }

    [TestMethod]
    public void PicksNumbersRangesAndDetectsRanges()
    {
        Assert.AreEqual(3, Call("TryPickNumberFromFiles", (object)new[] { "第03话.png", "other.png" }));
        Assert.AreEqual(0, Call("TryPickNumberFromFiles", (object)Array.Empty<string>()));

        var range = (ITuple)Call("DetectRangeFromFiles", (object)new[] { "12-15.png", "003.png" })!;
        Assert.AreEqual(12, range[0]);
        Assert.AreEqual(15, range[1]);
        Assert.IsNotNull(range[2]);

        var noRange = (ITuple)Call("DetectRangeFromFiles", (object)new[] { "single.png" })!;
        Assert.IsNull(noRange[0]);
    }


    [TestMethod]
    public void ScanEpisodes_CoversSpecialVolumeMiscAndRootBranches()
    {
        void Put(string relative, string content = "")
        {
            var full = Path.Combine(_dir, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, content);
        }

        Put("番外03/sp.png");
        Put("番外03/番外04.png");
        Put("特别篇/plain.png");
        Put("特别篇/番外06.psd");
        Put("第01话/001.png");
        Put("第01话/001.psd");
        Put("第01话/校对.txt");
        Put("vol02/v.png");
        Put("random/r.png");
        Put("番外/no-number.png");
        Put("第3卷/vol.png");
        Put("第3卷/03-check.txt");
        Put("第04话.png");
        Put("特别篇.png");
        Put("番外05.png");
        Put("第5卷.png");
        Put("random.png");
        Put("第6话.txt");

        var args = new object?[] { _dir, false, null };
        var list = (List<EpisodeEntry>)Call("ScanEpisodes", args)!;

        Assert.IsTrue(list.Any(e => e.IsSpecial && e.Number == 3));
        Assert.IsTrue(list.Any(e => e.IsSpecial && e.Number == 5));
        Assert.IsTrue(list.Any(e => e.IsSpecial && e.Number == 6));
        Assert.IsTrue(list.Any(e => e.Number == 1));
        Assert.IsTrue(list.Any(e => e.Number == 4));
        Assert.IsTrue(list.Any(e => e.Number == 6));
        Assert.IsTrue(list.Any(e => e.IsVolume));
        Assert.IsTrue(list.Any(e => e.IsMisc));
        Assert.IsTrue(list.Any(e => e.Status == "嵌字"));
        Assert.IsTrue(list.Any(e => e.Status == "校对"));
        Assert.IsTrue(list.Any(e => e.Status == "翻译"));
    }
    [TestMethod]
    public void ScanEpisodes_FindsFoldersAndSpecialEntries()
    {
        var chapter = Path.Combine(_dir, "第01话");
        var special = Path.Combine(_dir, "番外01");
        Directory.CreateDirectory(chapter);
        Directory.CreateDirectory(special);
        File.WriteAllText(Path.Combine(chapter, "001.png"), "");
        File.WriteAllText(Path.Combine(chapter, "002.psd"), "");
        File.WriteAllText(Path.Combine(special, "sp.png"), "");

        var args = new object?[] { _dir, false, null };
        var list = (List<EpisodeEntry>)Call("ScanEpisodes", args)!;

        Assert.IsTrue(list.Count >= 1);
        Assert.IsTrue(list.Any(e => e.IsSpecial));
        Assert.IsTrue(list.Any(e => e.Number == 1));
        Assert.IsNotNull(args[1]);
    }
}
