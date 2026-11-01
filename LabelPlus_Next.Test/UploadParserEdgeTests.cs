using System;
using System.Collections.Generic;
using System.Reflection;
using LabelPlus_Next.Models;
using LabelPlus_Next.ViewModels;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LabelPlus_Next.Test;

[TestClass]
[DoNotParallelize]
public class UploadParserEdgeTests
{
    private static object? Call(string name, params object?[] args)
        => typeof(UploadViewModel).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, args);

    [TestMethod]
    public void SpecialNumberParsers_HandleChineseAndReverseOrder()
    {
        var args = new object?[] { "番外十二", 0, null };
        Assert.IsTrue((bool)Call("TryParseSpecialNumber", args)!);
        Assert.AreEqual(12, args[1]);

        args = new object?[] { "十二番外", 0, null };
        Assert.IsTrue((bool)Call("TryParseSpecialNumber", args)!);
        Assert.AreEqual(12, args[1]);

        args = new object?[] { "序章", 0, null };
        Assert.IsTrue((bool)Call("TryParseSpecialNumber", args)!);

        args = new object?[] { "终章", 0, null };
        Assert.IsTrue((bool)Call("TryParseSpecialNumber", args)!);
        Assert.AreEqual(999, args[1]);
    }

    [TestMethod]
    public void EpisodeOrVolumeParsers_CoverVolumeRangesKeywordsAndEpisodeForms()
    {
        var result = (System.Runtime.CompilerServices.ITuple)Call("ParseEpisodeOrVolume", "1-3卷")!;
        Assert.AreEqual(3, result[1]);
        result = (System.Runtime.CompilerServices.ITuple)Call("ParseEpisodeOrVolume", "第十二卷")!;
        Assert.AreEqual(12, result[1]);
        result = (System.Runtime.CompilerServices.ITuple)Call("ParseEpisodeOrVolume", "ch12")!;
        Assert.AreEqual(12, result[1]);
        result = (System.Runtime.CompilerServices.ITuple)Call("ParseEpisodeOrVolume", "卷")!;
        Assert.AreEqual(0, result[1]);
        result = (System.Runtime.CompilerServices.ITuple)Call("ParseEpisodeOrVolume", "vol.2")!;
        Assert.AreEqual(2, result[1]);
        result = (System.Runtime.CompilerServices.ITuple)Call("ParseEpisodeOrVolume", "S01E05")!;
        Assert.AreEqual(5, result[1]);
        result = (System.Runtime.CompilerServices.ITuple)Call("ParseEpisodeOrVolume", "EP7")!;
        Assert.AreEqual(7, result[1]);
        Call("ParseEpisodeOrVolume", "ch十二");
        Call("ParseEpisodeOrVolume", "foo ch第十二");
        Call("ParseEpisodeOrVolume", "S01E05");
        Call("ParseEpisodeOrVolume", "E9");
        result = (System.Runtime.CompilerServices.ITuple)Call("ParseEpisodeOrVolume", "E9")!;
        Assert.AreEqual(9, result[1]);
    }

    [TestMethod]
    public void EpisodeNumberParser_CoversChineseEpisodeRangeAndSpecialForms()
    {
        var args = new object?[] { "第十二话", 0 };
        Assert.IsTrue((bool)Call("TryParseEpisodeNumber", args)!);
        Assert.AreEqual(12, args[1]);

        args = new object?[] { "EP7", 0 };
        Assert.IsTrue((bool)Call("TryParseEpisodeNumber", args)!);
        Assert.AreEqual(7, args[1]);

        args = new object?[] { "S01E05", 0 };
        Assert.IsTrue((bool)Call("TryParseEpisodeNumber", args)!);
        args = new object?[] { "E9", 0 };
        Assert.IsTrue((bool)Call("TryParseEpisodeNumber", args)!);

        args = new object?[] { "第十二", 0 };
        Assert.IsTrue((bool)Call("TryParseEpisodeNumber", args)!);
        Assert.AreEqual(12, args[1]);

        args = new object?[] { "12-15", 0 };
        Assert.IsTrue((bool)Call("TryParseEpisodeNumber", args)!);
        Assert.AreEqual(15, args[1]);
    }

    [TestMethod]
    public void PickNumbers_BuildsSubDirsKeysAndRangeKeys()
    {
        var files = new[] { "/x/第1卷.png", "/x/第2卷.png", "/x/第03话.png" };
        Assert.AreEqual(2, Call("TryPickNumberFromFiles", (object)files));

        var volume = new EpisodeEntry { Number = 0, IsVolume = true, LocalFiles = new List<string> { "/x/第4卷.png" } };
        Assert.AreEqual("卷04", Call("BuildSubDir", volume));
        Assert.AreEqual("卷04", Call("BuildEpisodeKey", volume, 4));

        var special = new EpisodeEntry { Number = 0, IsSpecial = true, LocalFiles = new List<string> { "/x/番外7.png" } };
        Assert.AreEqual("番外07", Call("BuildEpisodeKey", special, 0));
        special.Number = 9;
        Assert.AreEqual("番外09", Call("BuildEpisodeKey", special, 0));

        var misc = new EpisodeEntry { IsMisc = true };
        Assert.AreEqual("杂项", Call("BuildEpisodeKey", misc, 0));

        var range = new EpisodeEntry { Number = 5, RangeStart = 5, RangeEnd = 8 };
        Assert.AreEqual("5-8", Call("BuildEpisodeKey", range, 5));
        Assert.AreEqual("5-8", Call("BuildSubDir", range));
    }
}
