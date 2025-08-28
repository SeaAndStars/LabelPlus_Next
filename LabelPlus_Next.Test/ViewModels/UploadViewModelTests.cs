using LabelPlus_Next.ViewModels;

namespace LabelPlus_Next.Test.ViewModels;

[TestClass]
public class UploadViewModelTests
{
    [TestMethod]
    public void Smoke_Construct_NoAutoRefresh()
    {
        var vm = new UploadViewModel(autoRefresh: false);
        Assert.IsNotNull(vm);
        // Verify commands are initialized
        Assert.IsNotNull(vm.RefreshCommand);
        Assert.IsNotNull(vm.PickUploadFilesCommand);
        Assert.IsNotNull(vm.PickUploadFolderCommand);
    }
}
