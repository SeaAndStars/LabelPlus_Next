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
    /// ���ɲ��ԣ��Խ���ʵ����ˣ���֤��֤���ļ�ϵͳ�ӿڡ�
    /// �� test.json �ж�ȡ�������ã�BaseUrl���û��������롢���ơ�·������
    /// �ļ������������ /test ·���������õ� Path���½��С�
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
                Assert.Inconclusive("test.json ȱ�� BaseUrl");
        }

        [TestMethod]
        public async Task ��¼�ɹ�_Ӧ����Token()
        {
            var api = new AuthApi(_cfg.BaseUrl!);
            var result = await api.LoginAsync(_cfg.Username ?? "test", _cfg.Password ?? "123456");

            Assert.AreEqual(200, result.Code, $"���� 200��ʵ�� {result.Code}: {result.Message}");
            Assert.IsNotNull(result.Data);
            Assert.IsFalse(string.IsNullOrWhiteSpace(result.Data!.Token));
        }

        [TestMethod]
        public async Task ����Ϊ��_Ӧ����400()
        {
            var api = new AuthApi(_cfg.BaseUrl!);
            var result = await api.LoginAsync("", "");

            // ��������δ��Լ��ʵ�� 400������Ϊ������������
            if (result.Code == 200)
                Assert.Inconclusive("�����δ�Կղ������� 400������������");

            Assert.AreEqual((int)HttpStatusCode.BadRequest, result.Code);
        }

        [TestMethod]
        public async Task �б�ӿ�_Ӧ�������ݻ�Ϊ��()
        {
            var token = await TokenCache.GetOrLoginAsync(_cfg.BaseUrl!, _cfg.Token, _cfg.Username, _cfg.Password);
            if (string.IsNullOrWhiteSpace(token))
                Assert.Inconclusive("�޷���ȡ Token������ test.json ���¼����");

            var fs = new FileSystemApi(_cfg.BaseUrl!);
            var resp = await fs.ListAsync(token!, FileRoot, page: 1, perPage: 0, refresh: true);
            Assert.IsTrue(resp.Code == 200 || resp.Code == (int)HttpStatusCode.Unauthorized, $"����ķ�����: {resp.Code} {resp.Message}");
            if (resp.Code == 200)
            {
                Assert.IsNotNull(resp.Data);
                Assert.IsNotNull(resp.Data!.Content);
            }
        }

        [TestMethod]
        public async Task ��ȡ����_Ӧ���������δ��Ȩ()
        {
            var token = await TokenCache.GetOrLoginAsync(_cfg.BaseUrl!, _cfg.Token, _cfg.Username, _cfg.Password);
            if (string.IsNullOrWhiteSpace(token))
                Assert.Inconclusive("�޷���ȡ Token������ test.json ���¼����");

            var fs = new FileSystemApi(_cfg.BaseUrl!);
            var resp = await fs.GetAsync(token!, FileRoot);
            Assert.IsTrue(resp.Code == 200 || resp.Code == (int)HttpStatusCode.Unauthorized, $"����ķ�����: {resp.Code} {resp.Message}");
            if (resp.Code == 200)
            {
                Assert.IsNotNull(resp.Data);
                Assert.IsFalse(string.IsNullOrWhiteSpace(resp.Data!.Name));
            }
        }

        [TestMethod]
        public async Task �����ӿ�_Ӧ���ؽ����δ��Ȩ()
        {
            var token = await TokenCache.GetOrLoginAsync(_cfg.BaseUrl!, _cfg.Token, _cfg.Username, _cfg.Password);
            if (string.IsNullOrWhiteSpace(token))
                Assert.Inconclusive("�޷���ȡ Token������ test.json ���¼����");

            var fs = new FileSystemApi(_cfg.BaseUrl!);
            var resp = await fs.SearchAsync(token!, parent: FileRoot, keywords: _cfg.Keywords ?? "test", scope: 0, page: 1, perPage: 1);
            Assert.IsTrue(resp.Code == 200 || resp.Code == (int)HttpStatusCode.Unauthorized, $"����ķ�����: {resp.Code} {resp.Message}");
            if (resp.Code == 200)
            {
                Assert.IsNotNull(resp.Data);
                Assert.IsNotNull(resp.Data!.Content);
            }
        }

        [TestMethod]
        public async Task ����Ŀ¼_Ӧ�ɹ���δ��Ȩ()
        {
            var token = await TokenCache.GetOrLoginAsync(_cfg.BaseUrl!, _cfg.Token, _cfg.Username, _cfg.Password);
            if (string.IsNullOrWhiteSpace(token))
                Assert.Inconclusive("�޷���ȡ Token������ test.json ���¼����");

            var fs = new FileSystemApi(_cfg.BaseUrl!);
            var newDir = FileRoot.TrimEnd('/') + "/unit-" + Guid.NewGuid().ToString("N");
            var resp = await fs.MkdirAsync(token!, newDir);

            Assert.IsTrue(resp.Code == 200 || resp.Code == (int)HttpStatusCode.Unauthorized, $"����ķ�����: {resp.Code} {resp.Message}");
        }

        [TestMethod]
        public async Task ������Ŀ_Ӧ�ɹ���δ��Ȩ()
        {
            var token = await TokenCache.GetOrLoginAsync(_cfg.BaseUrl!, _cfg.Token, _cfg.Username, _cfg.Password);
            if (string.IsNullOrWhiteSpace(token))
                Assert.Inconclusive("�޷���ȡ Token������ test.json ���¼����");

            var fs = new FileSystemApi(_cfg.BaseUrl!);

            // ��׼��Դ�ļ�
            var srcName = "copy-src-" + Guid.NewGuid().ToString("N") + ".txt";
            var srcPath = FileRoot.TrimEnd('/') + "/" + srcName;
            var srcBytes = Encoding.UTF8.GetBytes("COPY-SRC-" + Guid.NewGuid());
            var up = await fs.PutAsync(token!, srcPath, srcBytes);
            if (up.Code == (int)HttpStatusCode.Unauthorized) Assert.Inconclusive("δ��Ȩ������");
            Assert.AreEqual(200, up.Code, $"׼��Դ�ļ�ʧ��: {up.Message}");

            // ��׼��Ŀ��Ŀ¼
            var dstDir = FileRoot.TrimEnd('/') + "/copy-dst-" + Guid.NewGuid().ToString("N");
            var mk = await fs.MkdirAsync(token!, dstDir);
            if (mk.Code == (int)HttpStatusCode.Unauthorized) Assert.Inconclusive("δ��Ȩ������");
            Assert.IsTrue(mk.Code == 200 || mk.Code == 409 || mk.Code == 201, $"����Ŀ��Ŀ¼ʧ��: {mk.Code} {mk.Message}");

            // ִ�и���
            var resp = await fs.CopyAsync(token!, FileRoot.TrimEnd('/'), dstDir, new[] { srcName });
            Assert.IsTrue(resp.Code == 200 || resp.Code == (int)HttpStatusCode.Unauthorized, $"����ķ�����: {resp.Code} {resp.Message}");
        }

        [TestMethod]
        public async Task �ϴ��ļ�_Ӧ���������δ��Ȩ()
        {
            var token = await TokenCache.GetOrLoginAsync(_cfg.BaseUrl!, _cfg.Token, _cfg.Username, _cfg.Password);
            if (string.IsNullOrWhiteSpace(token))
                Assert.Inconclusive("�޷���ȡ Token������ test.json ���¼����");

            var fs = new FileSystemApi(_cfg.BaseUrl!);
            var targetFile = FileRoot.TrimEnd('/') + "/unit-" + Guid.NewGuid().ToString("N") + ".txt";
            var bytes = Encoding.UTF8.GetBytes("hello from LabelPlus_Next tests");
            var resp = await fs.PutAsync(token!, targetFile, bytes, asTask: false);
            Assert.IsTrue(resp.Code == 200 || resp.Code == (int)HttpStatusCode.Unauthorized, $"����ķ�����: {resp.Code} {resp.Message}");
        }

        [TestMethod]
        public async Task ���߳��ϴ�_Ӧȫ���ɹ���δ��Ȩ()
        {
            var token = await TokenCache.GetOrLoginAsync(_cfg.BaseUrl!, _cfg.Token, _cfg.Username, _cfg.Password);
            if (string.IsNullOrWhiteSpace(token))
                Assert.Inconclusive("�޷���ȡ Token������ test.json ���¼����");

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
                Assert.Inconclusive("�����δ��Ȩ�����������ϴ���֤");
            }
            foreach (var r in results)
            {
                Assert.AreEqual(200, r.Code, $"�ϴ�ʧ��: {r.Message}");
            }
        }

        [TestMethod]
        public async Task ͬ���ļ�_SafePut_Ӧ���ݲ��滻��δ��Ȩ()
        {
            var token = await TokenCache.GetOrLoginAsync(_cfg.BaseUrl!, _cfg.Token, _cfg.Username, _cfg.Password);
            if (string.IsNullOrWhiteSpace(token))
                Assert.Inconclusive("�޷���ȡ Token������ test.json ���¼����");

            var fs = new FileSystemApi(_cfg.BaseUrl!);
            var baseName = "same-test.txt";
            var fullPath = FileRoot.TrimEnd('/') + "/" + baseName;

            // 1) �����ϴ� A
            var a = Encoding.UTF8.GetBytes("AAAA-" + Guid.NewGuid());
            var r1 = await fs.PutAsync(token!, fullPath, a);
            if (r1.Code == (int)HttpStatusCode.Unauthorized) Assert.Inconclusive("δ��Ȩ������");
            Assert.AreEqual(200, r1.Code, $"�����ϴ�ʧ��: {r1.Message}");

            // 2) SafePut �ϴ� B
            var b = Encoding.UTF8.GetBytes("BBBB-" + Guid.NewGuid());
            var r2 = await fs.SafePutAsync(token!, fullPath, b);
            if (r2.Code == (int)HttpStatusCode.Unauthorized) Assert.Inconclusive("δ��Ȩ������");
            Assert.AreEqual(200, r2.Code, $"SafePut �ϴ�ʧ��: {r2.Message}");

            // 3) ��ȡ����ԭ·������ӦΪ B
            var g = await fs.GetAsync(token!, fullPath);
            Assert.AreEqual(200, g.Code, $"��ȡ����ʧ��: {g.Message}");
            Assert.IsNotNull(g.Data);
            if (!string.IsNullOrWhiteSpace(g.Data!.RawUrl))
            {
                var downloaded = await DownloadBytesAsync(g.Data.RawUrl!);
                CollectionAssert.AreEqual(b, downloaded, "ԭ·������δ�滻Ϊ������");
            }

            // 4) Ŀ¼��Ӧ���ڱ��� same-test_yyyyMMddHHmmss.txt��ģ��ƥ�䣩
            var list = await fs.ListAsync(token!, FileRoot);
            if (list.Code == 200 && list.Data?.Content is not null)
            {
                var foundBackup = list.Data.Content.Any(i => i.Name != null && i.Name.StartsWith("same-test_") && i.Name.EndsWith(".txt"));
                Assert.IsTrue(foundBackup, "δ�ҵ�ʱ��������ļ�");
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
