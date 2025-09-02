using LabelPlus_Next.ViewModels;
using LabelPlus_Next.Services;
using LabelPlus_Next.Test;

namespace LabelPlus_Next.Test.ViewModels;

[TestClass]
public class UploadViewModelTests
{
    [TestMethod]
    public void Smoke_Construct_NoAutoRefresh()
    {
        var settings = new MemorySettingsService();
        var dialogs = new NoopFileDialogService();
        var vm = new UploadViewModel(settings, dialogs, autoRefresh: false);
        Assert.IsNotNull(vm);
        // Verify commands are initialized
        Assert.IsNotNull(vm.RefreshCommand);
        Assert.IsNotNull(vm.PickUploadFilesCommand);
        Assert.IsNotNull(vm.PickUploadFolderCommand);
    }
}
