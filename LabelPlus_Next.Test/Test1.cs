using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using LabelPlus_Next.Services.Api;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LabelPlus_Next.Test
{
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

        [TestMethod]
        public async Task ���߳��ϴ�����_���޲���Ӧ���()
        {
            var token = await TokenCache.GetOrLoginAsync(_cfg.BaseUrl!, _cfg.Token, _cfg.Username, _cfg.Password);
            if (string.IsNullOrWhiteSpace(token)) Assert.Inconclusive("�޷���ȡ Token");

            var fs = new FileSystemApi(_cfg.BaseUrl!);
            var items = Enumerable.Range(0, 8).Select(i => new FileUploadItem
            {
                FilePath = FileRoot.TrimEnd('/') + $"/pm-{i}-" + Guid.NewGuid().ToString("N") + ".txt",
                Content = Encoding.UTF8.GetBytes($"payload-{i}-" + Guid.NewGuid())
            });
            var res = await fs.PutManyAsync(token!, items, maxConcurrency: 3, asTask: false);
            if (Array.Exists(res.ToArray(), r => r.Code == (int)HttpStatusCode.Unauthorized))
                Assert.Inconclusive("δ��Ȩ������");
            foreach (var r in res) Assert.AreEqual(200, r.Code, r.Message);
        }

        [TestMethod]
        public async Task ���߳����ؿ���_���޲���Ӧ���()
        {
            var token = await TokenCache.GetOrLoginAsync(_cfg.BaseUrl!, _cfg.Token, _cfg.Username, _cfg.Password);
            if (string.IsNullOrWhiteSpace(token)) Assert.Inconclusive("�޷���ȡ Token");
            var fs = new FileSystemApi(_cfg.BaseUrl!);

            // ���� 3 ��С�ļ��Թ�����
            var files = new List<string>();
            for (int i = 0; i < 3; i++)
            {
                var p = FileRoot.TrimEnd('/') + $"/dl-{i}-" + Guid.NewGuid().ToString("N") + ".txt";
                var r = await fs.PutAsync(token!, p, Encoding.UTF8.GetBytes($"DL-{i}"));
                if (r.Code == 200) files.Add(p);
            }

            if (files.Count == 0) Assert.Inconclusive("δ�ܴ������ز����ļ�");

            var results = await fs.DownloadManyAsync(token!, files, maxConcurrency: 2);
            foreach (var d in results)
            {
                if (d.Code == (int)HttpStatusCode.Unauthorized) Assert.Inconclusive("δ��Ȩ������");
                Assert.AreEqual(200, d.Code, d.Message);
                Assert.IsNotNull(d.Content);
                Assert.IsTrue(d.Content!.Length > 0);
            }
        }

        [TestMethod]
        public async Task ͬ���ļ�_SafePut_Ӧ���ݲ��滻()
        {
            var token = await TokenCache.GetOrLoginAsync(_cfg.BaseUrl!, _cfg.Token, _cfg.Username, _cfg.Password);
            if (string.IsNullOrWhiteSpace(token)) Assert.Inconclusive("�޷���ȡ Token");

            var fs = new FileSystemApi(_cfg.BaseUrl!);
            var name = FileRoot.TrimEnd('/') + "/same-multi.txt";
            var a = Encoding.UTF8.GetBytes("A-" + Guid.NewGuid());
            var r1 = await fs.PutAsync(token!, name, a);
            if (r1.Code == (int)HttpStatusCode.Unauthorized) Assert.Inconclusive("δ��Ȩ������");
            Assert.AreEqual(200, r1.Code, r1.Message);

            var b = Encoding.UTF8.GetBytes("B-" + Guid.NewGuid());
            var r2 = await fs.SafePutAsync(token!, name, b);
            Assert.AreEqual(200, r2.Code, r2.Message);

            var meta = await fs.GetAsync(token!, name);
            Assert.AreEqual(200, meta.Code, meta.Message);
            if (!string.IsNullOrEmpty(meta.Data?.RawUrl))
            {
                using var http = new HttpClient();
                var bytes = await http.GetByteArrayAsync(meta.Data.RawUrl);
                CollectionAssert.AreEqual(b, bytes);
            }
        }

        [TestMethod]
        public async Task Downloader_���ļ��ϵ�����_Ӧ�ɹ���δ��Ȩ()
        {
            var token = await TokenCache.GetOrLoginAsync(_cfg.BaseUrl!, _cfg.Token, _cfg.Username, _cfg.Password);
            if (string.IsNullOrWhiteSpace(token)) Assert.Inconclusive("�޷���ȡ Token");

            var fs = new FileSystemApi(_cfg.BaseUrl!);
            var name = FileRoot.TrimEnd('/') + "/dl-resume-" + Guid.NewGuid().ToString("N") + ".bin";
            // ���ϴ�һ���ϴ���ļ����������ظ�����ģ�⣩
            var big = new byte[256 * 1024]; // 256KB�����ڲ���
            new Random().NextBytes(big);
            var up = await fs.PutAsync(token!, name, big);
            if (up.Code == (int)HttpStatusCode.Unauthorized) Assert.Inconclusive("δ��Ȩ");
            Assert.AreEqual(200, up.Code, up.Message);

            var local = Path.Combine(Path.GetTempPath(), Path.GetFileName(name));
            if (File.Exists(local)) File.Delete(local);

            // ��һ�����أ�����ж�
            var result = await fs.DownloadToFileAsync(token!, name, local);
            if (result.Code == (int)HttpStatusCode.Unauthorized) Assert.Inconclusive("δ��Ȩ");
            Assert.AreEqual(200, result.Code, result.Message);
        }

        [TestMethod]
        public async Task Downloader_���ļ�����_Ӧ��ɻ�δ��Ȩ()
        {
            var token = await TokenCache.GetOrLoginAsync(_cfg.BaseUrl!, _cfg.Token, _cfg.Username, _cfg.Password);
            if (string.IsNullOrWhiteSpace(token)) Assert.Inconclusive("�޷���ȡ Token");
            var fs = new FileSystemApi(_cfg.BaseUrl!);

            // ���� 3 ��Զ���ļ�
            var paths = new List<string>();
            for (int i = 0; i < 3; i++)
            {
                var p = FileRoot.TrimEnd('/') + $"/dl-par-{i}-" + Guid.NewGuid().ToString("N") + ".bin";
                var data = Encoding.UTF8.GetBytes("DATA-" + Guid.NewGuid());
                var r = await fs.PutAsync(token!, p, data);
                if (r.Code == 200) paths.Add(p);
            }
            if (paths.Count == 0) Assert.Inconclusive("����Զ���ļ�ʧ��");

            var pairs = paths.Select(p => (remotePath: p, localPath: Path.Combine(Path.GetTempPath(), Path.GetFileName(p)))).ToList();
            var results = await fs.DownloadManyToFilesAsync(token!, pairs, maxConcurrency: 2);
            foreach (var res in results)
            {
                if (res.Code == (int)HttpStatusCode.Unauthorized) Assert.Inconclusive("δ��Ȩ");
                Assert.AreEqual(200, res.Code, res.Message);
            }
        }

        [TestMethod]
        public async Task ͳһ�ϴ�_�ļ���_Ӧ����Ŀ¼�ṹ��δ��Ȩ()
        {
            var token = await TokenCache.GetOrLoginAsync(_cfg.BaseUrl!, _cfg.Token, _cfg.Username, _cfg.Password);
            if (string.IsNullOrWhiteSpace(token)) Assert.Inconclusive("�޷���ȡ Token");

            var fs = new FileSystemApi(_cfg.BaseUrl!);

            // ���챾����ʱĿ¼�ṹ
            var localRoot = Path.Combine(Path.GetTempPath(), "lp_up_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(localRoot);
            try
            {
                var sub1 = Path.Combine(localRoot, "many");
                var sub2 = Path.Combine(localRoot, "nested", "deep");
                Directory.CreateDirectory(sub1);
                Directory.CreateDirectory(sub2);

                // д���ļ�
                var fileA = Path.Combine(localRoot, "a.txt");
                var fileB = Path.Combine(sub1, "b.txt");
                var fileC = Path.Combine(sub1, "c.bin");
                var fileD = Path.Combine(sub2, "d.txt");

                var aBytes = Encoding.UTF8.GetBytes("A-" + Guid.NewGuid());
                var bBytes = Encoding.UTF8.GetBytes("B-" + Guid.NewGuid());
                var cBytes = new byte[1024]; new Random().NextBytes(cBytes);
                var dBytes = Encoding.UTF8.GetBytes("D-" + Guid.NewGuid());

                await File.WriteAllBytesAsync(fileA, aBytes);
                await File.WriteAllBytesAsync(fileB, bBytes);
                await File.WriteAllBytesAsync(fileC, cBytes);
                await File.WriteAllBytesAsync(fileD, dBytes);

                // Զ�˻�·��ʹ�� /test �µ�Ψһ��Ŀ¼
                var remoteBase = FileRoot.TrimEnd('/') + "/dir-" + Guid.NewGuid().ToString("N");

                var req = UploadRequest.FromDirectory(localRoot, remoteBase);
                var results = await fs.SafeUploadAsync(token!, req, maxConcurrency: 3, asTask: false);
                if (Array.Exists(results.ToArray(), r => r.Code == (int)HttpStatusCode.Unauthorized))
                    Assert.Inconclusive("δ��Ȩ������");
                foreach (var r in results) Assert.AreEqual(200, r.Code, r.Message);

                // ��֤Զ�˴���������һ��
                async Task AssertRemoteEqualsAsync(string rel, byte[] expected)
                {
                    var remotePath = remoteBase.TrimEnd('/') + "/" + rel.Replace('\\', '/');
                    var get = await fs.GetAsync(token!, remotePath);
                    Assert.AreEqual(200, get.Code, get.Message);
                    Assert.IsNotNull(get.Data);
                    Assert.IsFalse(get.Data!.IsDir, "ӦΪ�ļ�����Ŀ¼");
                    if (!string.IsNullOrWhiteSpace(get.Data.RawUrl))
                    {
                        var downloaded = await DownloadBytesAsync(get.Data.RawUrl!);
                        CollectionAssert.AreEqual(expected, downloaded, $"���ݲ�һ��: {rel}");
                    }
                }

                await AssertRemoteEqualsAsync("a.txt", aBytes);
                await AssertRemoteEqualsAsync("many/b.txt", bBytes);
                await AssertRemoteEqualsAsync("many/c.bin", cBytes);
                await AssertRemoteEqualsAsync("nested/deep/d.txt", dBytes);
            }
            finally
            {
                try { Directory.Delete(localRoot, recursive: true); } catch { }
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
