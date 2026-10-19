using System;
using System.Globalization;
using Avalonia.Data;
using Avalonia.Media;
using LabelPlus_Next.Converters;
using LabelPlus_Next.CustomControls;
using LabelPlus_Next.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LabelPlus_Next.Test;

[TestClass]
[DoNotParallelize]
public class ConvertersTests
{
    private static readonly CultureInfo En = CultureInfo.GetCultureInfo("en");

    [TestCleanup]
    public void Cleanup() => LocalizationService.Apply("default");

    [TestMethod]
    public void BoolToDouble_UsesConfiguredValues()
    {
        var converter = new BoolToDoubleConverter { TrueValue = 1.5, FalseValue = -1 };
        Assert.AreEqual(1.5, converter.Convert(true, typeof(double), null, CultureInfo.InvariantCulture));
        Assert.AreEqual(-1d, converter.Convert(false, typeof(double), null, CultureInfo.InvariantCulture));
        Assert.AreEqual(-1d, converter.Convert("not a bool", typeof(double), null, CultureInfo.InvariantCulture));
        Assert.ThrowsException<NotSupportedException>(() =>
            converter.ConvertBack(1d, typeof(bool), null, CultureInfo.InvariantCulture));
    }

    [TestMethod]
    public void CategoryToColor_MapsKnownCategories()
    {
        var converter = new CategoryToColorConverter();
        Assert.AreSame(Brushes.OrangeRed, converter.Convert("框内", typeof(IBrush), null, CultureInfo.InvariantCulture));
        Assert.AreSame(Brushes.DodgerBlue, converter.Convert("框外", typeof(IBrush), null, CultureInfo.InvariantCulture));
        Assert.AreSame(Avalonia.AvaloniaProperty.UnsetValue, converter.Convert("unknown", typeof(IBrush), null, CultureInfo.InvariantCulture));
        Assert.ThrowsException<NotImplementedException>(() =>
            converter.ConvertBack(null, typeof(string), null, CultureInfo.InvariantCulture));
    }

    [TestMethod]
    public void EnumEquals_ConvertsBothWays()
    {
        var converter = EnumEqualsConverter.Instance;
        Assert.AreEqual(true, converter.Convert(ViewerMode.Label, typeof(bool), "Label", En));
        Assert.AreEqual(false, converter.Convert(ViewerMode.Label, typeof(bool), "Browse", En));
        Assert.AreEqual(false, converter.Convert(null, typeof(bool), "Label", En));

        Assert.AreEqual(ViewerMode.Label, converter.ConvertBack(true, typeof(ViewerMode), "Label", En));
        Assert.AreEqual(ViewerMode.Label, converter.ConvertBack(true, typeof(ViewerMode?), "Label", En));
        Assert.AreSame(BindingOperations.DoNothing, converter.ConvertBack(false, typeof(ViewerMode), "Label", En));
        Assert.AreSame(BindingOperations.DoNothing, converter.ConvertBack(true, typeof(string), "Label", En));
        Assert.AreSame(BindingOperations.DoNothing, converter.ConvertBack(true, typeof(ViewerMode), "Missing", En));
    }

    [TestMethod]
    public void StringEquals_HandlesNullsAndDoNothing()
    {
        var converter = StringEqualsConverter.Instance;
        Assert.AreEqual(true, converter.Convert("框内", typeof(bool), "框内", En));
        Assert.AreEqual(false, converter.Convert("框外", typeof(bool), "框内", En));
        Assert.AreEqual(false, converter.Convert(null, typeof(bool), "框内", En));
        Assert.AreSame(BindingOperations.DoNothing, converter.ConvertBack(true, typeof(string), null, En));
    }

    [TestMethod]
    public void LocalizedCount_FormatsAndFallsBack()
    {
        LocalizationService.Apply("en");
        var converter = LocalizedCountConverter.Instance;
        Assert.AreEqual("3 projects", converter.Convert(3, typeof(string), "ui0180", En));
        Assert.AreEqual("3", converter.Convert(3, typeof(string), null, En));
        Assert.IsNotNull(converter.Convert(3, typeof(string), "missing_key", En));
        Assert.ThrowsException<NotSupportedException>(() =>
            converter.ConvertBack("x", typeof(int), null, En));
    }

    [TestMethod]
    public void LocalizedFallback_ReturnsValueOrPlaceholder()
    {
        LocalizationService.Apply("en");
        var converter = LocalizedFallbackConverter.Instance;
        Assert.AreEqual("page01.png", converter.Convert("page01.png", typeof(string), "ui0183", En));
        Assert.AreEqual("Select a project to preview", converter.Convert(null, typeof(string), "ui0183", En));
        Assert.AreEqual("Select a project to preview", converter.Convert(" ", typeof(string), "ui0183", En));
        Assert.IsNull(converter.Convert(null, typeof(string), null, En));
        Assert.ThrowsException<NotSupportedException>(() =>
            converter.ConvertBack("x", typeof(string), null, En));
    }

    [TestMethod]
    public void CategoryDisplay_MapsAndPassesThrough()
    {
        LocalizationService.Apply("en");
        var converter = CategoryDisplayConverter.Instance;
        Assert.AreEqual("Inner", converter.Convert("框内", typeof(string), null, En));
        Assert.AreEqual("Outer", converter.Convert("框外", typeof(string), null, En));
        Assert.AreEqual("其它", converter.Convert("其它", typeof(string), null, En));
        Assert.ThrowsException<NotSupportedException>(() =>
            converter.ConvertBack("Inner", typeof(string), null, En));
    }

    [TestMethod]
    public void StatusConverters_CoverAllBranches()
    {
        var bg = new StatusToBadgeBackgroundConverter();
        var fg = new StatusToBadgeForegroundConverter();
        foreach (var status in new[] { "发布", "嵌字", "校对", "翻译", "unknown", null })
        {
            Assert.IsNotNull(bg.Convert(status, typeof(IBrush), null, En));
            Assert.IsNotNull(fg.Convert(status, typeof(IBrush), null, En));
            Assert.IsNull(bg.ConvertBack(null, typeof(string), null, En));
            Assert.IsNull(fg.ConvertBack(null, typeof(string), null, En));
        }

        var ursa = StatusToUrsaClassConverter.Instance;
        Assert.AreEqual("Neutral", ursa.Convert(null, typeof(string), null, En));
        Assert.AreEqual("Neutral", ursa.Convert("  ", typeof(string), null, En));
        Assert.AreEqual("Primary", ursa.Convert("翻译", typeof(string), null, En));
        Assert.AreEqual("Info", ursa.Convert("校对", typeof(string), null, En));
        Assert.AreEqual("Warning", ursa.Convert("嵌字", typeof(string), null, En));
        Assert.AreEqual("Success", ursa.Convert("完成", typeof(string), null, En));
        Assert.AreEqual("Danger", ursa.Convert("错误", typeof(string), null, En));
        Assert.AreEqual("Secondary", ursa.Convert("other", typeof(string), null, En));
        Assert.ThrowsException<NotSupportedException>(() =>
            ursa.ConvertBack(null, typeof(string), null, En));
    }
}
