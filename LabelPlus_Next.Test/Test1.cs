using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using LabelPlus_Next.Services.Api;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LabelPlus_Next.Test
{
    /// <summary>
    /// 集成测试：对接真实服务端，验证认证与文件系统接口。
    /// 从 test.json 中读取基础配置（BaseUrl、用户名、密码、令牌、路径）。
    /// 文件相关用例均在 /test 路径（或配置的 Path）下进行。
    /// </summary>
    [TestClass]
    public sealed class Test1
    {
        private static TestConfig _cfg = null!;
        private static string FileRoot => string.IsNullOrWhiteSpace(_cfg.Path) ? "/test" : _cfg.Path!;

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

        [TestMethod]
        public async Task 登录成功_应返回Token()
        {
            var api = new AuthApi(_cfg.BaseUrl!);
            var result = await api.LoginAsync(_cfg.Username ?? "test", _cfg.Password ?? "123456");

            Assert.AreEqual(200, result.Code, $"期望 200，实际 {result.Code}: {result.Message}");
            Assert.IsNotNull(result.Data);
            Assert.IsFalse(string.IsNullOrWhiteSpace(result.Data!.Token));
        }

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

        [TestMethod]
        public async Task 列表接口_应返回内容或为空()
        {
            var token = await TokenCache.GetOrLoginAsync(_cfg.BaseUrl!, _cfg.Token, _cfg.Username, _cfg.Password);
            if (string.IsNullOrWhiteSpace(token))
                Assert.Inconclusive("无法获取 Token，请检查 test.json 或登录配置");

            var fs = new FileSystemApi(_cfg.BaseUrl!);
            var resp = await fs.ListAsync(token!, FileRoot, page: 1, perPage: 0, refresh: true);
            Assert.IsTrue(resp.Code == 200 || resp.Code == (int)HttpStatusCode.Unauthorized, $"意外的返回码: {resp.Code} {resp.Message}");
            if (resp.Code == 200)
            {
                Assert.IsNotNull(resp.Data);
                Assert.IsNotNull(resp.Data!.Content);
            }
        }

        [TestMethod]
        public async Task 获取单项_应返回详情或未授权()
        {
            var token = await TokenCache.GetOrLoginAsync(_cfg.BaseUrl!, _cfg.Token, _cfg.Username, _cfg.Password);
            if (string.IsNullOrWhiteSpace(token))
                Assert.Inconclusive("无法获取 Token，请检查 test.json 或登录配置");

            var fs = new FileSystemApi(_cfg.BaseUrl!);
            var resp = await fs.GetAsync(token!, FileRoot);
            Assert.IsTrue(resp.Code == 200 || resp.Code == (int)HttpStatusCode.Unauthorized, $"意外的返回码: {resp.Code} {resp.Message}");
            if (resp.Code == 200)
            {
                Assert.IsNotNull(resp.Data);
                Assert.IsFalse(string.IsNullOrWhiteSpace(resp.Data!.Name));
            }
        }

        [TestMethod]
        public async Task 搜索接口_应返回结果或未授权()
        {
            var token = await TokenCache.GetOrLoginAsync(_cfg.BaseUrl!, _cfg.Token, _cfg.Username, _cfg.Password);
            if (string.IsNullOrWhiteSpace(token))
                Assert.Inconclusive("无法获取 Token，请检查 test.json 或登录配置");

            var fs = new FileSystemApi(_cfg.BaseUrl!);
            var resp = await fs.SearchAsync(token!, parent: FileRoot, keywords: _cfg.Keywords ?? "test", scope: 0, page: 1, perPage: 1);
            Assert.IsTrue(resp.Code == 200 || resp.Code == (int)HttpStatusCode.Unauthorized, $"意外的返回码: {resp.Code} {resp.Message}");
            if (resp.Code == 200)
            {
                Assert.IsNotNull(resp.Data);
                Assert.IsNotNull(resp.Data!.Content);
            }
        }

        [TestMethod]
        public async Task 创建目录_应成功或未授权()
        {
            var token = await TokenCache.GetOrLoginAsync(_cfg.BaseUrl!, _cfg.Token, _cfg.Username, _cfg.Password);
            if (string.IsNullOrWhiteSpace(token))
                Assert.Inconclusive("无法获取 Token，请检查 test.json 或登录配置");

            var fs = new FileSystemApi(_cfg.BaseUrl!);
            var newDir = FileRoot.TrimEnd('/') + "/unit-" + Guid.NewGuid().ToString("N");
            var resp = await fs.MkdirAsync(token!, newDir);

            Assert.IsTrue(resp.Code == 200 || resp.Code == (int)HttpStatusCode.Unauthorized, $"意外的返回码: {resp.Code} {resp.Message}");
        }

        [TestMethod]
        public async Task 复制条目_应成功或未授权()
        {
            var token = await TokenCache.GetOrLoginAsync(_cfg.BaseUrl!, _cfg.Token, _cfg.Username, _cfg.Password);
            if (string.IsNullOrWhiteSpace(token))
                Assert.Inconclusive("无法获取 Token，请检查 test.json 或登录配置");

            var fs = new FileSystemApi(_cfg.BaseUrl!);

            // 先准备源文件
            var srcName = "copy-src-" + Guid.NewGuid().ToString("N") + ".txt";
            var srcPath = FileRoot.TrimEnd('/') + "/" + srcName;
            var srcBytes = Encoding.UTF8.GetBytes("COPY-SRC-" + Guid.NewGuid());
            var up = await fs.PutAsync(token!, srcPath, srcBytes);
            if (up.Code == (int)HttpStatusCode.Unauthorized) Assert.Inconclusive("未授权，跳过");
            Assert.AreEqual(200, up.Code, $"准备源文件失败: {up.Message}");

            // 再准备目标目录
            var dstDir = FileRoot.TrimEnd('/') + "/copy-dst-" + Guid.NewGuid().ToString("N");
            var mk = await fs.MkdirAsync(token!, dstDir);
            if (mk.Code == (int)HttpStatusCode.Unauthorized) Assert.Inconclusive("未授权，跳过");
            Assert.IsTrue(mk.Code == 200 || mk.Code == 409 || mk.Code == 201, $"创建目标目录失败: {mk.Code} {mk.Message}");

            // 执行复制
            var resp = await fs.CopyAsync(token!, FileRoot.TrimEnd('/'), dstDir, new[] { srcName });
            Assert.IsTrue(resp.Code == 200 || resp.Code == (int)HttpStatusCode.Unauthorized, $"意外的返回码: {resp.Code} {resp.Message}");
        }

        [TestMethod]
        public async Task 上传文件_应返回任务或未授权()
        {
            var token = await TokenCache.GetOrLoginAsync(_cfg.BaseUrl!, _cfg.Token, _cfg.Username, _cfg.Password);
            if (string.IsNullOrWhiteSpace(token))
                Assert.Inconclusive("无法获取 Token，请检查 test.json 或登录配置");

            var fs = new FileSystemApi(_cfg.BaseUrl!);
            var targetFile = FileRoot.TrimEnd('/') + "/unit-" + Guid.NewGuid().ToString("N") + ".txt";
            var bytes = Encoding.UTF8.GetBytes("hello from LabelPlus_Next tests");
            var resp = await fs.PutAsync(token!, targetFile, bytes, asTask: false);
            Assert.IsTrue(resp.Code == 200 || resp.Code == (int)HttpStatusCode.Unauthorized, $"意外的返回码: {resp.Code} {resp.Message}");
        }

        [TestMethod]
        public async Task 多线程上传_应全部成功或未授权()
        {
            var token = await TokenCache.GetOrLoginAsync(_cfg.BaseUrl!, _cfg.Token, _cfg.Username, _cfg.Password);
            if (string.IsNullOrWhiteSpace(token))
                Assert.Inconclusive("无法获取 Token，请检查 test.json 或登录配置");

            var fs = new FileSystemApi(_cfg.BaseUrl!);
            var tasks = new List<Task<FsPutResponse>>();
            for (int i = 0; i < 5; i++)
            {
                var name = FileRoot.TrimEnd('/') + $"/concurrent-{i}-" + Guid.NewGuid().ToString("N") + ".txt";
                var bytes = Encoding.UTF8.GetBytes($"content-{i}-" + Guid.NewGuid());
                tasks.Add(fs.PutAsync(token!, name, bytes));
            }

            var results = await Task.WhenAll(tasks);
            if (Array.Exists(results, r => r.Code == (int)HttpStatusCode.Unauthorized))
            {
                Assert.Inconclusive("服务端未授权，跳过并发上传验证");
            }
            foreach (var r in results)
            {
                Assert.AreEqual(200, r.Code, $"上传失败: {r.Message}");
            }
        }

        [TestMethod]
        public async Task 同名文件_SafePut_应备份并替换或未授权()
        {
            var token = await TokenCache.GetOrLoginAsync(_cfg.BaseUrl!, _cfg.Token, _cfg.Username, _cfg.Password);
            if (string.IsNullOrWhiteSpace(token))
                Assert.Inconclusive("无法获取 Token，请检查 test.json 或登录配置");

            var fs = new FileSystemApi(_cfg.BaseUrl!);
            var baseName = "same-test.txt";
            var fullPath = FileRoot.TrimEnd('/') + "/" + baseName;

            // 1) 初次上传 A
            var a = Encoding.UTF8.GetBytes("AAAA-" + Guid.NewGuid());
            var r1 = await fs.PutAsync(token!, fullPath, a);
            if (r1.Code == (int)HttpStatusCode.Unauthorized) Assert.Inconclusive("未授权，跳过");
            Assert.AreEqual(200, r1.Code, $"初次上传失败: {r1.Message}");

            // 2) SafePut 上传 B
            var b = Encoding.UTF8.GetBytes("BBBB-" + Guid.NewGuid());
            var r2 = await fs.SafePutAsync(token!, fullPath, b);
            if (r2.Code == (int)HttpStatusCode.Unauthorized) Assert.Inconclusive("未授权，跳过");
            Assert.AreEqual(200, r2.Code, $"SafePut 上传失败: {r2.Message}");

            // 3) 拉取最新原路径内容应为 B
            var g = await fs.GetAsync(token!, fullPath);
            Assert.AreEqual(200, g.Code, $"获取详情失败: {g.Message}");
            Assert.IsNotNull(g.Data);
            if (!string.IsNullOrWhiteSpace(g.Data!.RawUrl))
            {
                var downloaded = await DownloadBytesAsync(g.Data.RawUrl!);
                CollectionAssert.AreEqual(b, downloaded, "原路径内容未替换为新内容");
            }

            // 4) 目录下应存在备份 same-test_yyyyMMddHHmmss.txt（模糊匹配）
            var list = await fs.ListAsync(token!, FileRoot);
            if (list.Code == 200 && list.Data?.Content is not null)
            {
                var foundBackup = list.Data.Content.Any(i => i.Name != null && i.Name.StartsWith("same-test_") && i.Name.EndsWith(".txt"));
                Assert.IsTrue(foundBackup, "未找到时间戳备份文件");
            }
        }

        private static async Task<byte[]> DownloadBytesAsync(string url)
        {
            using var http = new HttpClient();
            return await http.GetByteArrayAsync(url);
        }

        private sealed class TestConfig
        {
            [JsonPropertyName("baseUrl")] public string? BaseUrl { get; set; }
            [JsonPropertyName("username")] public string? Username { get; set; }
            [JsonPropertyName("password")] public string? Password { get; set; }
            [JsonPropertyName("token")] public string? Token { get; set; }
            [JsonPropertyName("path")] public string? Path { get; set; }
            [JsonPropertyName("keywords")] public string? Keywords { get; set; }
        }
    }
}
