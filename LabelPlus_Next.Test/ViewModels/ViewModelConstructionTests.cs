using LabelPlus_Next.ViewModels;

namespace LabelPlus_Next.Test.ViewModels;

[TestClass]
public class ViewModelConstructionTests
{
    [TestMethod]
    public void ViewModelBase_CanConstruct()
    {
        var vm = new ViewModelBase();
        Assert.IsNotNull(vm);
    }

    [TestMethod]
    public void ImageOutputViewModel_CanConstruct()
    {
        var vm = new ImageOutputViewModel();
        Assert.IsNotNull(vm);
    }

    [TestMethod]
    public void ImageManagerViewModel_Defaults()
    {
        var vm = new ImageManagerViewModel();
        Assert.IsNull(vm.FolderPath);
        Assert.IsNull(vm.CreatedFilePath);
        Assert.IsNotNull(vm.FileFolderList);
        Assert.IsNotNull(vm.FileList);
        Assert.AreEqual(0, vm.FileFolderList.Count);
        Assert.AreEqual(0, vm.FileList.Count);
    }

    [TestMethod]
    public void FileSettingsViewModel_Defaults()
    {
        var vm = new FileSettingsViewModel();
        Assert.IsNotNull(vm.GroupList);
        Assert.AreEqual(0, vm.GroupList.Count);
        Assert.AreEqual(string.Empty, vm.Notes);
    }
}
