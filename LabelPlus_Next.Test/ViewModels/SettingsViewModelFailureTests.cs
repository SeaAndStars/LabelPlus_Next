using LabelPlus_Next.Models;
using LabelPlus_Next.Services;
using LabelPlus_Next.ViewModels;

namespace LabelPlus_Next.Test.ViewModels;

[TestClass]
public class SettingsViewModelFailureTests
{
    private sealed class StubUpdateService : IUpdateService
    {
        private readonly Func<UpdateSettings, CancellationToken, Task<UpdateManifest?>> _impl;
        public StubUpdateService(Func<UpdateSettings, CancellationToken, Task<UpdateManifest?>> impl) => _impl = impl;
        public Task<UpdateManifest?> FetchManifestAsync(UpdateSettings upd, CancellationToken ct = default) => _impl(upd, ct);
    }

    [TestMethod]
    public async Task VerifyViaUpdateService_Timeout()
    {
        var vm = new SettingsViewModel(new JsonSettingsService());
        var stub = new StubUpdateService((_, ct) => Task.FromCanceled<UpdateManifest?>(new CancellationToken(true)));
        await vm.VerifyViaUpdateServiceAsync(stub, new UpdateSettings(), CancellationToken.None);
        StringAssert.Contains(vm.Status ?? string.Empty, "超时");
    }

    [TestMethod]
    public async Task VerifyViaUpdateService_NullManifest()
    {
        var vm = new SettingsViewModel(new JsonSettingsService());
        var stub = new StubUpdateService((_, __) => Task.FromResult<UpdateManifest?>(null));
        await vm.VerifyViaUpdateServiceAsync(stub, new UpdateSettings());
        StringAssert.Contains(vm.Status ?? string.Empty, "清单为空");
    }

    [TestMethod]
    public async Task VerifyViaUpdateService_HttpError()
    {
        var vm = new SettingsViewModel(new JsonSettingsService());
        var stub = new StubUpdateService((_, __) => throw new HttpRequestException("conn refused"));
        await vm.VerifyViaUpdateServiceAsync(stub, new UpdateSettings());
        StringAssert.Contains(vm.Status ?? string.Empty, "网络错误");
    }

    [TestMethod]
    public async Task VerifyViaUpdateService_EmptyFilesProperty()
    {
        var vm = new SettingsViewModel(new JsonSettingsService());
        var stub = new StubUpdateService((_, __) => Task.FromResult<UpdateManifest?>(new UpdateManifest { Files = null! }));
        await vm.VerifyViaUpdateServiceAsync(stub, new UpdateSettings());
        StringAssert.Contains(vm.Status ?? string.Empty, "清单格式错误");
    }
}
