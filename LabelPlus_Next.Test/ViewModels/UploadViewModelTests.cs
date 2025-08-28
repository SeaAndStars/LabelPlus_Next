using LabelPlus_Next.ViewModels;

namespace LabelPlus_Next.Test.ViewModels;

[TestClass]
public class UploadViewModelTests
{
    [TestMethod]
    [Ignore("Constructor triggers async refresh that depends on network; this smoke test verifies type is present without side effects.")]
    public void Smoke_Construct()
    {
        var vm = new UploadViewModel();
        Assert.IsNotNull(vm);
    }
}
