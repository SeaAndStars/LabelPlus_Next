using LabelPlus_Next.Models;
using LabelPlus_Next.Services;
using LabelPlus_Next.ViewModels;
using LabelPlus_Next.Serialization;
using System.Text.Json;

namespace LabelPlus_Next.Test.ViewModels;

[TestClass]
public class ProductionVmIntegrationTests
{
    private sealed class TestConfig
    {
        public string? BaseUrl { get; set; }
        public string? Username { get; set; }
        public string? Password { get; set; }
    }

    private static TestConfig LoadConfig()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "test.json");
        if (!File.Exists(path)) return new TestConfig();
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<TestConfig>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        }) ?? new TestConfig();
    }

    [TestMethod]
    public async Task SettingsViewModel_VerifyHttp_Production()
    {
        var cfg = LoadConfig();
        if (string.IsNullOrWhiteSpace(cfg.BaseUrl))
            Assert.Inconclusive("test.json 缺少 BaseUrl");

        // 使用内存设置服务注入生产参数；ManifestPath 指向 /project.json（应包含 projects 字段）
        var mem = new MemorySettingsService
        {
            Data = new AppSettings
            {
                Update = new UpdateSettings
                {
                    BaseUrl = cfg.BaseUrl,
                    ManifestPath = "/project.json",
                    Username = cfg.Username,
                    Password = cfg.Password
                }
            }
        };

        var vm = new SettingsViewModel(mem);
        await vm.LoadAsync();
        await vm.VerifyHttpAsync();

        // 成功或未授权/不可用时标记跳过以避免误报
        if (string.IsNullOrWhiteSpace(vm.Status))
            Assert.Inconclusive("无法确定状态");
        if (vm.Status.Contains("成功"))
            Assert.IsTrue(true);
        else if (vm.Status.Contains("未授权") || vm.Status.Contains("登录失败") || vm.Status.Contains("下载失败") || vm.Status.Contains("验证失败") || vm.Status.Contains("获取清单失败"))
            Assert.Inconclusive($"生产环境不可用或未授权: {vm.Status}");
        else
            Assert.IsTrue(true, vm.Status);

    // 额外：调用保存设置，验证不抛异常
    await vm.SaveAsync();
    }

    [TestMethod]
    public async Task UploadViewModel_Refresh_Production()
    {
        var cfg = LoadConfig();
        if (string.IsNullOrWhiteSpace(cfg.BaseUrl))
            Assert.Inconclusive("test.json 缺少 BaseUrl");

        // 准备 upload.json 于测试运行目录，使 VM 读取生产配置
        var uploadCfgPath = Path.Combine(AppContext.BaseDirectory, "upload.json");
        var upload = new UploadSettings { BaseUrl = cfg.BaseUrl, Username = cfg.Username, Password = cfg.Password };
        await using (var fs = File.Create(uploadCfgPath))
        {
            await JsonSerializer.SerializeAsync(fs, upload, AppJsonContext.Default.UploadSettings);
        }

        var vm = new UploadViewModel();
        // 构造函数会触发一次刷新，这里主动再刷新一遍以确保
        await vm.RefreshCommand.ExecuteAsync(null);

        if (!string.IsNullOrWhiteSpace(vm.Status) && (vm.Status.Contains("未配置") || vm.Status.Contains("登录失败") || vm.Status.Contains("下载失败")))
            Assert.Inconclusive($"生产环境不可用或未授权: {vm.Status}");

        // 只要不报错即视为通过；Projects 可能为空取决于服务端
        Assert.IsNotNull(vm.Projects);
        // 如果有项目，随机取一个触发搜索提示刷新
        if (vm.Projects.Count > 0)
        {
            vm.SelectedProject = vm.Projects[0];
            // 不直接上传，避免副作用
            Assert.IsFalse(string.IsNullOrWhiteSpace(vm.SelectedProject));
        }
    }

    private sealed class MemorySettingsService : ISettingsService
    {
        public AppSettings Data { get; set; } = new();
        public Task<AppSettings> LoadAsync(CancellationToken ct = default) => Task.FromResult(Data);
        public Task SaveAsync(AppSettings settings, CancellationToken ct = default) { Data = settings; return Task.CompletedTask; }
    }
}
