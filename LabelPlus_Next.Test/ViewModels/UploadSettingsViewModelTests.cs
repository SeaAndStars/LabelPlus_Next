using LabelPlus_Next.ViewModels;

namespace LabelPlus_Next.Test.ViewModels;

[TestClass]
public class UploadSettingsViewModelTests
{
    [TestMethod]
    public void Defaults_And_CommandExists()
    {
        var vm = new UploadSettingsViewModel();
        Assert.IsFalse(string.IsNullOrWhiteSpace(vm.BaseUrl));
        Assert.IsNotNull(vm.SaveCommand);
    }
}
