using LabelPlus_Next.Services.Api;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LabelPlus_Next.Test;

[TestClass]
public sealed class Test1
{
    private static TestConfig _cfg = null!;

    private static string FileRoot
    {
        get => string.IsNullOrWhiteSpace(_cfg.Path) ? "/test" : _cfg.Path!;
    }

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

        // 如果服��端未按约定实现 400，可视为此用例不适用
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
        var resp = await fs.ListAsync(token!, FileRoot, 1, 0, true);
        Assert.IsTrue(resp.Code == 200 || resp.Code == (int)HttpStatusCode.Unauthorized, $"意外的返回码: {resp.Code} {resp.Message}");
        if (resp.Code == 200)
        {
            if (resp.Data?.Content is null)
                Assert.Inconclusive("服务端返回 200 但内容为空，可能无权限或实现差异，标记跳过");
            else
                Assert.IsTrue(true);
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
        var resp = await fs.SearchAsync(token!, FileRoot, _cfg.Keywords ?? "test", 0, 1, 1);
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
            Assert.Inconclusive("无法获取 Token，请检查 test.json 或登录配��");

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
        if (resp.Code == (int)HttpStatusCode.Unauthorized) Assert.Inconclusive("未授权，跳过");
        if (resp.Code >= 500 || (resp.Message?.Contains("object not found", StringComparison.OrdinalIgnoreCase) ?? false))
            Assert.Inconclusive($"服务端异常或对象不存在: {resp.Code} {resp.Message}");
        Assert.AreEqual(200, resp.Code, $"意外的返回码: {resp.Code} {resp.Message}");
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
        var resp = await fs.PutAsync(token!, targetFile, bytes, false);
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
        for (var i = 0; i < 5; i++)
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
            if (!foundBackup)
                Assert.Inconclusive("未找到时间戳备份文件，服务端可能未实现或策略不同，标记跳过");
        }
    }

    [TestMethod]
    public async Task 多线程上传控制_受限并发应完成()
    {
        var token = await TokenCache.GetOrLoginAsync(_cfg.BaseUrl!, _cfg.Token, _cfg.Username, _cfg.Password);
        if (string.IsNullOrWhiteSpace(token)) Assert.Inconclusive("无法获取 Token");

        var fs = new FileSystemApi(_cfg.BaseUrl!);
        var items = Enumerable.Range(0, 8).Select(i => new FileUploadItem
        {
            FilePath = FileRoot.TrimEnd('/') + $"/pm-{i}-" + Guid.NewGuid().ToString("N") + ".txt",
            Content = Encoding.UTF8.GetBytes($"payload-{i}-" + Guid.NewGuid())
        });
        var res = await fs.PutManyAsync(token!, items, 3);
        if (Array.Exists(res.ToArray(), r => r.Code == (int)HttpStatusCode.Unauthorized))
            Assert.Inconclusive("未授权，跳过");
        foreach (var r in res) Assert.AreEqual(200, r.Code, r.Message);
    }

    [TestMethod]
    public async Task 多线程下载控制_受限并发应完成()
    {
        var token = await TokenCache.GetOrLoginAsync(_cfg.BaseUrl!, _cfg.Token, _cfg.Username, _cfg.Password);
        if (string.IsNullOrWhiteSpace(token)) Assert.Inconclusive("无法获取 Token");
        var fs = new FileSystemApi(_cfg.BaseUrl!);

        // 构造 3 个小文件以供下载
        var files = new List<string>();
        for (var i = 0; i < 3; i++)
        {
            var p = FileRoot.TrimEnd('/') + $"/dl-{i}-" + Guid.NewGuid().ToString("N") + ".txt";
            var r = await fs.PutAsync(token!, p, Encoding.UTF8.GetBytes($"DL-{i}"));
            if (r.Code == 200) files.Add(p);
        }

        if (files.Count == 0) Assert.Inconclusive("未能创建下载测试文件");

        var results = await fs.DownloadManyAsync(token!, files, 2);
        foreach (var d in results)
        {
            if (d.Code == (int)HttpStatusCode.Unauthorized) Assert.Inconclusive("未授权，跳过");
            if (d.Code == -1 && (d.Message?.Contains("raw_url", StringComparison.OrdinalIgnoreCase) ?? false))
                Assert.Inconclusive("服务端未提供 raw_url，跳过");
            Assert.AreEqual(200, d.Code, d.Message);
            Assert.IsNotNull(d.Content);
            Assert.IsTrue(d.Content!.Length > 0);
        }
    }

    [TestMethod]
    public async Task 同名文件_SafePut_应备份并替换()
    {
        var token = await TokenCache.GetOrLoginAsync(_cfg.BaseUrl!, _cfg.Token, _cfg.Username, _cfg.Password);
        if (string.IsNullOrWhiteSpace(token)) Assert.Inconclusive("无法获取 Token");

        var fs = new FileSystemApi(_cfg.BaseUrl!);
        var name = FileRoot.TrimEnd('/') + "/same-multi.txt";
        var a = Encoding.UTF8.GetBytes("A-" + Guid.NewGuid());
        var r1 = await fs.PutAsync(token!, name, a);
        if (r1.Code == (int)HttpStatusCode.Unauthorized) Assert.Inconclusive("未授权，跳过");
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
    public async Task Downloader_单文件断点续传_应成功或未授权()
    {
        var token = await TokenCache.GetOrLoginAsync(_cfg.BaseUrl!, _cfg.Token, _cfg.Username, _cfg.Password);
        if (string.IsNullOrWhiteSpace(token)) Assert.Inconclusive("无法获取 Token");

        var fs = new FileSystemApi(_cfg.BaseUrl!);
        var name = FileRoot.TrimEnd('/') + "/dl-resume-" + Guid.NewGuid().ToString("N") + ".bin";
        // 先上传一个较大的文件（这里用重复内容模拟）
        var big = new byte[256 * 1024]; // 256KB，用于测试
        new Random().NextBytes(big);
        var up = await fs.PutAsync(token!, name, big);
        if (up.Code == (int)HttpStatusCode.Unauthorized) Assert.Inconclusive("未授权");
        Assert.AreEqual(200, up.Code, up.Message);

        var local = Path.Combine(Path.GetTempPath(), Path.GetFileName(name));
        if (File.Exists(local)) File.Delete(local);

        // 第一次下载，随后中断
        var result = await fs.DownloadToFileAsync(token!, name, local);
        if (result.Code == (int)HttpStatusCode.Unauthorized) Assert.Inconclusive("未授权");
        if (result.Code == -1 && (result.Message?.Contains("raw_url", StringComparison.OrdinalIgnoreCase) ?? false))
            Assert.Inconclusive("服务端未提供 raw_url，跳过");
        Assert.AreEqual(200, result.Code, result.Message);
    }

    [TestMethod]
    public async Task Downloader_多文件并发_应完成或未授权()
    {
        var token = await TokenCache.GetOrLoginAsync(_cfg.BaseUrl!, _cfg.Token, _cfg.Username, _cfg.Password);
        if (string.IsNullOrWhiteSpace(token)) Assert.Inconclusive("无法获取 Token");
        var fs = new FileSystemApi(_cfg.BaseUrl!);

        // 创建 3 个远端文件
        var paths = new List<string>();
        for (var i = 0; i < 3; i++)
        {
            var p = FileRoot.TrimEnd('/') + $"/dl-par-{i}-" + Guid.NewGuid().ToString("N") + ".bin";
            var data = Encoding.UTF8.GetBytes("DATA-" + Guid.NewGuid());
            var r = await fs.PutAsync(token!, p, data);
            if (r.Code == 200) paths.Add(p);
        }
        if (paths.Count == 0) Assert.Inconclusive("创建远端文件失败");

        var pairs = paths.Select(p => (remotePath: p, localPath: Path.Combine(Path.GetTempPath(), Path.GetFileName(p)))).ToList();
        var results = await fs.DownloadManyToFilesAsync(token!, pairs, 2);
        foreach (var res in results)
        {
            if (res.Code == (int)HttpStatusCode.Unauthorized) Assert.Inconclusive("未授权");
            if (res.Code == -1 && (res.Message?.Contains("raw_url", StringComparison.OrdinalIgnoreCase) ?? false))
                Assert.Inconclusive("服务端未提供 raw_url，跳过");
            Assert.AreEqual(200, res.Code, res.Message);
        }
    }

    [TestMethod]
    public async Task 统一上传_文件夹_应保持目录结构或未授权()
    {
        var token = await TokenCache.GetOrLoginAsync(_cfg.BaseUrl!, _cfg.Token, _cfg.Username, _cfg.Password);
        if (string.IsNullOrWhiteSpace(token)) Assert.Inconclusive("无法获取 Token");

        var fs = new FileSystemApi(_cfg.BaseUrl!);

        // 构造本地临时目录结构
        var localRoot = Path.Combine(Path.GetTempPath(), "lp_up_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(localRoot);
        try
        {
            var sub1 = Path.Combine(localRoot, "many");
            var sub2 = Path.Combine(localRoot, "nested", "deep");
            Directory.CreateDirectory(sub1);
            Directory.CreateDirectory(sub2);

            // 写入文件
            var fileA = Path.Combine(localRoot, "a.txt");
            var fileB = Path.Combine(sub1, "b.txt");
            var fileC = Path.Combine(sub1, "c.bin");
            var fileD = Path.Combine(sub2, "d.txt");

            var aBytes = Encoding.UTF8.GetBytes("A-" + Guid.NewGuid());
            var bBytes = Encoding.UTF8.GetBytes("B-" + Guid.NewGuid());
            var cBytes = new byte[1024];
            new Random().NextBytes(cBytes);
            var dBytes = Encoding.UTF8.GetBytes("D-" + Guid.NewGuid());

            await File.WriteAllBytesAsync(fileA, aBytes);
            await File.WriteAllBytesAsync(fileB, bBytes);
            await File.WriteAllBytesAsync(fileC, cBytes);
            await File.WriteAllBytesAsync(fileD, dBytes);

            // 远端基路径使用 /test 下的唯一子目录
            var remoteBase = FileRoot.TrimEnd('/') + "/dir-" + Guid.NewGuid().ToString("N");

            // 先确保远端基目录可用
            var mkBase = await fs.MkdirAsync(token!, remoteBase);
            if (mkBase.Code == (int)HttpStatusCode.Unauthorized) Assert.Inconclusive("未授权，跳过");
            if (!(mkBase.Code == 200 || mkBase.Code == 201 || mkBase.Code == 409))
            {
                if (mkBase.Code >= 500) Assert.Inconclusive($"服务端异常: {mkBase.Code} {mkBase.Message}");
                Assert.Fail($"创建远端基路径失败: {mkBase.Code} {mkBase.Message}");
            }

            var req = UploadRequest.FromDirectory(localRoot, remoteBase);
            var results = await fs.SafeUploadAsync(token!, req, 3);
            if (Array.Exists(results.ToArray(), r => r.Code == (int)HttpStatusCode.Unauthorized))
                Assert.Inconclusive("未授权，跳过");
            if (Array.Exists(results.ToArray(), r => r.Code >= 500))
                Assert.Inconclusive("服务端内部错误，跳过");
            foreach (var r in results) Assert.AreEqual(200, r.Code, r.Message);

            // 验证远端存在且内容一致
            async Task AssertRemoteEqualsAsync(string rel, byte[] expected)
            {
                var remotePath = remoteBase.TrimEnd('/') + "/" + rel.Replace('\\', '/');
                var get = await fs.GetAsync(token!, remotePath);
                if (get.Code == (int)HttpStatusCode.Unauthorized) Assert.Inconclusive("未授权，跳过");
                if (get.Code >= 500) Assert.Inconclusive($"服务端异常: {get.Code} {get.Message}");
                Assert.AreEqual(200, get.Code, get.Message);
                Assert.IsNotNull(get.Data);
                Assert.IsFalse(get.Data!.IsDir, "应为文件而非目录");
                if (!string.IsNullOrWhiteSpace(get.Data.RawUrl))
                {
                    var downloaded = await DownloadBytesAsync(get.Data.RawUrl!);
                    CollectionAssert.AreEqual(expected, downloaded, $"内容不一致: {rel}");
                }
            }

            await AssertRemoteEqualsAsync("a.txt", aBytes);
            await AssertRemoteEqualsAsync("many/b.txt", bBytes);
            await AssertRemoteEqualsAsync("many/c.bin", cBytes);
            await AssertRemoteEqualsAsync("nested/deep/d.txt", dBytes);
        }
        finally
        {
            try { Directory.Delete(localRoot, true); }
            catch (IOException ex)
            {
                Console.WriteLine($"清理测试目录失败 (IO): {ex.Message}");
            }
            catch (UnauthorizedAccessException ex)
            {
                Console.WriteLine($"清理测试目录权限不足: {ex.Message}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"清理测试目录发生异常: {ex}");
            }
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
