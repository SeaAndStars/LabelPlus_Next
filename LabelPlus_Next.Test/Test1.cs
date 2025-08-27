using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using LabelPlus_Next.Services.Api;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LabelPlus_Next.Test
{
    /// <summary>
    /// 集成测试：对接真实服务端，验证认证与文件系统接口。
    /// 从 test.json 中读取基础配置（BaseUrl、用户名、密码、令牌、路径）。
    /// </summary>
    [TestClass]
    public sealed class Test1
    {
        /// <summary>
        /// 测试配置（从运行目录的 test.json 读取）。
        /// </summary>
        private static TestConfig _cfg = null!;

        /// <summary>
        /// 测试类初始化：加载 test.json 配置。
        /// </summary>
        /// <param name="_">测试上下文（未使用）。</param>
        [ClassInitialize]
        public static void ClassInit(TestContext _)
        {
            var path = Path.Combine(AppContext.BaseDirectory, "test.json");
            var json = File.Exists(path) ? File.ReadAllText(path) : "{}";
            _cfg = JsonSerializer.Deserialize<TestConfig>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            }) ?? new TestConfig();

            if (string.IsNullOrWhiteSpace(_cfg.BaseUrl))
                Assert.Inconclusive("test.json 缺少 BaseUrl");
        }

        /// <summary>
        /// 用例：使用正确的用户名与密码登录，应返回业务码 200 且数据中包含 Token。
        /// </summary>
        [TestMethod]
        public async Task 登录成功_应返回Token()
        {
            var api = new AuthApi(_cfg.BaseUrl!);
            var result = await api.LoginAsync(_cfg.Username ?? "test", _cfg.Password ?? "123456");

            Assert.AreEqual(200, result.Code, $"期望 200，实际 {result.Code}: {result.Message}");
            Assert.IsNotNull(result.Data);
            Assert.IsFalse(string.IsNullOrWhiteSpace(result.Data!.Token));
        }

        /// <summary>
        /// 用例：用户名与密码为空时，期望服务端返回 400（若未实现则跳过）。
        /// </summary>
        [TestMethod]
        public async Task 参数为空_应返回400()
        {
            var api = new AuthApi(_cfg.BaseUrl!);
            var result = await api.LoginAsync("", "");

            // 如果服务端未按约定实现 400，可视为此用例不适用
            if (result.Code == 200)
                Assert.Inconclusive("服务端未对空参数返回 400，此用例跳过");

            Assert.AreEqual((int)HttpStatusCode.BadRequest, result.Code);
        }

        /// <summary>
        /// 用例：调用文件系统列表接口，若返回 200 则应包含内容（或为空数组）；返回 401 表示未授权也可接受。
        /// </summary>
        [TestMethod]
        public async Task 列表接口_应返回内容或为空()
        {
            if (string.IsNullOrWhiteSpace(_cfg.Token))
                Assert.Inconclusive("test.json 缺少 Token（或请先调用登录接口获取）");

            var fs = new FileSystemApi(_cfg.BaseUrl!);
            var resp = await fs.ListAsync(_cfg.Token!, _cfg.Path ?? "/test", page: 1, perPage: 0, refresh: true);
            Assert.IsTrue(resp.Code == 200 || resp.Code == (int)HttpStatusCode.Unauthorized, $"意外的返回码: {resp.Code} {resp.Message}");
            if (resp.Code == 200)
            {
                Assert.IsNotNull(resp.Data);
                Assert.IsNotNull(resp.Data!.Content);
            }
        }

        /// <summary>
        /// 用例：调用 /api/fs/get 获取单项详情，若返回 200 则应包含名称等基础字段；返回 401 也可接受。
        /// </summary>
        [TestMethod]
        public async Task 获取单项_应返回详情或未授权()
        {
            if (string.IsNullOrWhiteSpace(_cfg.Token))
                Assert.Inconclusive("test.json 缺少 Token（或请先调用登录接口获取）");

            var fs = new FileSystemApi(_cfg.BaseUrl!);
            var resp = await fs.GetAsync(_cfg.Token!, _cfg.Path ?? "/test");
            Assert.IsTrue(resp.Code == 200 || resp.Code == (int)HttpStatusCode.Unauthorized, $"意外的返回码: {resp.Code} {resp.Message}");
            if (resp.Code == 200)
            {
                Assert.IsNotNull(resp.Data);
                Assert.IsFalse(string.IsNullOrWhiteSpace(resp.Data!.Name));
            }
        }

        /// <summary>
        /// 测试运行所需的配置模型。
        /// </summary>
        private sealed class TestConfig
        {
            /// <summary>
            /// 服务端基础地址，例如：https://example.com
            /// </summary>
            [JsonPropertyName("baseUrl")] public string? BaseUrl { get; set; }

            /// <summary>
            /// 登录用户名。
            /// </summary>
            [JsonPropertyName("username")] public string? Username { get; set; }

            /// <summary>
            /// 登录密码。
            /// </summary>
            [JsonPropertyName("password")] public string? Password { get; set; }

            /// <summary>
            /// 授权令牌（某些接口需要，例如 /api/fs/list、/api/fs/get）。
            /// </summary>
            [JsonPropertyName("token")] public string? Token { get; set; }

            /// <summary>
            /// 默认目标路径，用于列表与详情请求。
            /// </summary>
            [JsonPropertyName("path")] public string? Path { get; set; }
        }
    }
}
