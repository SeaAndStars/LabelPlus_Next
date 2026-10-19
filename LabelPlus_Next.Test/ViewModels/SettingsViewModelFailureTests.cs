using LabelPlus_Next.Models;
using LabelPlus_Next.Services;
using LabelPlus_Next.ViewModels;

namespace LabelPlus_Next.Test.ViewModels;

[TestClass]
[DoNotParallelize]
public class SettingsViewModelFailureTests
{
    [TestInitialize]
    public void Setup() => LocalizationService.Apply("zh-CN");

    [TestCleanup]
    public void Cleanup() => LocalizationService.Apply("default");

    private sealed class StubUpdateService : IUpdateService
    {
        private readonly Func<UpdateSettings, CancellationToken, Task<UpdateManifest?>> _impl;
        public StubUpdateService(Func<UpdateSettings, CancellationToken, Task<UpdateManifest?>> impl) => _impl = impl;
        public Task<UpdateManifest?> FetchManifestAsync(UpdateSettings upd, CancellationToken ct = default) => _impl(upd, ct);
    }

    /// <summary>Keeps the view model on zh-CN regardless of the runner's UI culture.</summary>
    private sealed class StubSettingsService : ISettingsService
    {
        public Task<AppSettings> LoadAsync(CancellationToken ct = default)
            => Task.FromResult(new AppSettings { Language = "zh-CN" });

        public Task SaveAsync(AppSettings settings, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    private static async Task<SettingsViewModel> CreateViewModelAsync()
    {
        var vm = new SettingsViewModel(new StubSettingsService());
        await vm.LoadAsync();
        return vm;
    }

    [TestMethod]
    public async Task VerifyViaUpdateService_Timeout()
    {
        var vm = await CreateViewModelAsync();
        var stub = new StubUpdateService((_, ct) => Task.FromCanceled<UpdateManifest?>(new CancellationToken(true)));
        await vm.VerifyViaUpdateServiceAsync(stub, new UpdateSettings(), CancellationToken.None);
        StringAssert.Contains(vm.Status ?? string.Empty, "超时");
    }

    [TestMethod]
    public async Task VerifyViaUpdateService_NullManifest()
    {
        var vm = await CreateViewModelAsync();
        var stub = new StubUpdateService((_, __) => Task.FromResult<UpdateManifest?>(null));
        await vm.VerifyViaUpdateServiceAsync(stub, new UpdateSettings());
        StringAssert.Contains(vm.Status ?? string.Empty, "清单为空");
    }

    [TestMethod]
    public async Task VerifyViaUpdateService_HttpError()
    {
        var vm = await CreateViewModelAsync();
        var stub = new StubUpdateService((_, __) => throw new HttpRequestException("conn refused"));
        await vm.VerifyViaUpdateServiceAsync(stub, new UpdateSettings());
        StringAssert.Contains(vm.Status ?? string.Empty, "网络错误");
    }

    [TestMethod]
    public async Task VerifyViaUpdateService_EmptyFilesProperty()
    {
        var vm = await CreateViewModelAsync();
        var stub = new StubUpdateService((_, __) => Task.FromResult<UpdateManifest?>(new UpdateManifest { Files = null! }));
        await vm.VerifyViaUpdateServiceAsync(stub, new UpdateSettings());
        StringAssert.Contains(vm.Status ?? string.Empty, "清单格式错误");
    }
}
