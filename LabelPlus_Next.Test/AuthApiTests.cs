using LabelPlus_Next.Services.Api;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;

namespace LabelPlus_Next.Test;

[TestClass]
public class AuthApiTests
{
    private sealed class TestConfig
    {
        public string? baseUrl { get; set; }
        public string? token { get; set; }
        public string? username { get; set; }
        public string? password { get; set; }
    }

    [TestMethod]
    public async Task GetMe_WithTokenOrCredentials_ReturnsUserInfo()
    {
        // Arrange
        var cfgPath = Path.Combine(TestContext.TestRunDirectory ?? Directory.GetCurrentDirectory(), "test.json");
        if (!File.Exists(cfgPath))
            cfgPath = Path.Combine(AppContext.BaseDirectory, "test.json");
        Assert.IsTrue(File.Exists(cfgPath), $"Config not found: {cfgPath}");

        var cfg = JsonConvert.DeserializeObject<TestConfig>(await File.ReadAllTextAsync(cfgPath))!;
        Assert.IsFalse(string.IsNullOrWhiteSpace(cfg.baseUrl), "baseUrl missing in test.json");
        var api = new AuthApi(cfg.baseUrl!);

        // Act: prefer token, else use credentials to login then get /api/me
        var res = !string.IsNullOrWhiteSpace(cfg.token)
            ? await api.GetMeAsync(cfg.token!)
            : await api.GetMeAsync(cfg.username!, cfg.password!);

        // Assert
        Assert.IsNotNull(res);
        Assert.AreEqual(200, res.Code, res.Message);
        Assert.IsNotNull(res.Data);
        Assert.IsFalse(string.IsNullOrEmpty(res.Data!.Username));
    }

    public TestContext TestContext { get; set; } = null!;
}
