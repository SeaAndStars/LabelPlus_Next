using LabelPlus_Next.ViewModels;

namespace LabelPlus_Next.Test.ViewModels;

[TestClass]
public class MainAndTranslateViewModelTests
{
    [TestMethod]
    public void MainWindowViewModel_InitialState()
    {
        var vm = new MainWindowViewModel();
        Assert.IsNotNull(vm.ImageFileNames);
        Assert.AreEqual(0, vm.ImageFileNames.Count);
        Assert.IsNull(vm.SelectedImageFile);
        Assert.AreEqual("default", vm.SelectedLang);
        Assert.IsFalse(vm.HasUnsavedChanges);
    }

    [TestMethod]
    public void TranslateViewModel_InitialState()
    {
        var vm = new TranslateViewModel();
        Assert.IsNotNull(vm.ImageFileNames);
        Assert.AreEqual(0, vm.ImageFileNames.Count);
        Assert.IsNull(vm.SelectedImageFile);
        Assert.AreEqual("default", vm.SelectedLang);
        Assert.IsFalse(vm.HasUnsavedChanges);
    }
}
