using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Linq;
using System.Reflection;
using System;

namespace LabelPlus_Next.Test.Models;

[TestClass]
public class NameParseTests
{
    private static (string kind, int number) CallParse(string name)
    {
        // 反射调用 UploadViewModel.ParseEpisodeOrVolume（internal/private）
        var vmType = typeof(LabelPlus_Next.ViewModels.UploadViewModel);
        var mi = vmType.GetMethod("ParseEpisodeOrVolume", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.IsNotNull(mi, "ParseEpisodeOrVolume not found");
        var result = mi.Invoke(null, new object[] { name });
        // result is ValueTuple<NameKind,int>; use reflection to read Item1(kind enum) & Item2(number)
        var kindProp = result!.GetType().GetField("Item1");
        var numProp = result!.GetType().GetField("Item2");
        Assert.IsNotNull(kindProp);
        Assert.IsNotNull(numProp);
        var kindEnum = kindProp!.GetValue(result);
        var num = (int)numProp!.GetValue(result)!;
        return (kindEnum!.ToString()!, num);
    }

    [TestMethod]
    public void Parse_Special_Variants()
    {
        var cases = new[] { "番外", "番外1", "SP1", "Special02", "Extra 3", "外传" };
        foreach (var s in cases)
        {
            var (kind, number) = CallParse(s);
            Assert.AreEqual("Special", kind);
        }
    }

    [TestMethod]
    public void Parse_Volume_Variants()
    {
    var (k1, n1) = CallParse("卷3");
    Assert.AreEqual("Volume", k1);
    Assert.AreEqual(3, n1);
    var (k2, n2) = CallParse("Vol.12");
    Assert.AreEqual("Volume", k2);
    Assert.AreEqual(12, n2);
    var (k3, n3) = CallParse("V02");
    Assert.AreEqual("Volume", k3);
    Assert.AreEqual(2, n3);
    }

    [TestMethod]
    public void Parse_Episode_With_Noise()
    {
    var (k1, n1) = CallParse("第 5 话 2024-11-09 [1080x1920]");
    Assert.IsTrue(k1 == "Episode" || k1 == "Unknown"); // Episode/Unknown acceptable
    Assert.IsTrue(n1 > 0);
        var (k2, n2) = CallParse("CHAPTER 十三");
    Assert.AreEqual("Episode", k2);
    Assert.AreEqual(13, n2);
    }
}
