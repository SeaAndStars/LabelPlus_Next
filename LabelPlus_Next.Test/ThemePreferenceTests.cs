using Avalonia.Styling;
using LabelPlus_Next.Models;

namespace LabelPlus_Next.Test;

[TestClass]
public class ThemePreferenceTests
{
    [TestMethod]
    public void Parse_MapsSupportedValuesAndFallsBack()
    {
        Assert.AreEqual(ThemeVariant.Light, ThemePreference.Parse("light"));
        Assert.AreEqual(ThemeVariant.Light, ThemePreference.Parse(" LIGHT "));
        Assert.AreEqual(ThemeVariant.Dark, ThemePreference.Parse("dark"));
        Assert.AreEqual(ThemeVariant.Default, ThemePreference.Parse(null));
        Assert.AreEqual(ThemeVariant.Default, ThemePreference.Parse(string.Empty));
        Assert.AreEqual(ThemeVariant.Default, ThemePreference.Parse("system"));
    }

    [TestMethod]
    public void Format_MapsVariantsToPersistedValues()
    {
        Assert.AreEqual(ThemePreference.Light, ThemePreference.Format(ThemeVariant.Light));
        Assert.AreEqual(ThemePreference.Dark, ThemePreference.Format(ThemeVariant.Dark));
        Assert.AreEqual(ThemePreference.Default, ThemePreference.Format(ThemeVariant.Default));
        Assert.AreEqual(ThemePreference.Default, ThemePreference.Format(null));
    }
}
