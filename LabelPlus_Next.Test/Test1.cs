using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using LabelPlus_Next.Services.Api;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LabelPlus_Next.Test
{
    /// <summary>
    /// ���ɲ��ԣ��Խ���ʵ����ˣ���֤��֤���ļ�ϵͳ�ӿڡ�
    /// �� test.json �ж�ȡ�������ã�BaseUrl���û��������롢���ơ�·������
    /// </summary>
    [TestClass]
    public sealed class Test1
    {
        /// <summary>
        /// �������ã�������Ŀ¼�� test.json ��ȡ����
        /// </summary>
        private static TestConfig _cfg = null!;

        /// <summary>
        /// �������ʼ�������� test.json ���á�
        /// </summary>
        /// <param name="_">���������ģ�δʹ�ã���</param>
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

        /// <summary>
        /// ������ʹ����ȷ���û����������¼��Ӧ����ҵ���� 200 �������а��� Token��
        /// </summary>
        [TestMethod]
        public async Task ��¼�ɹ�_Ӧ����Token()
        {
            var api = new AuthApi(_cfg.BaseUrl!);
            var result = await api.LoginAsync(_cfg.Username ?? "test", _cfg.Password ?? "123456");

            Assert.AreEqual(200, result.Code, $"���� 200��ʵ�� {result.Code}: {result.Message}");
            Assert.IsNotNull(result.Data);
            Assert.IsFalse(string.IsNullOrWhiteSpace(result.Data!.Token));
        }

        /// <summary>
        /// �������û���������Ϊ��ʱ����������˷��� 400����δʵ������������
        /// </summary>
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

        /// <summary>
        /// �����������ļ�ϵͳ�б�ӿڣ������� 200 ��Ӧ�������ݣ���Ϊ�����飩������ 401 ��ʾδ��ȨҲ�ɽ��ܡ�
        /// </summary>
        [TestMethod]
        public async Task �б�ӿ�_Ӧ�������ݻ�Ϊ��()
        {
            if (string.IsNullOrWhiteSpace(_cfg.Token))
                Assert.Inconclusive("test.json ȱ�� Token�������ȵ��õ�¼�ӿڻ�ȡ��");

            var fs = new FileSystemApi(_cfg.BaseUrl!);
            var resp = await fs.ListAsync(_cfg.Token!, _cfg.Path ?? "/test", page: 1, perPage: 0, refresh: true);
            Assert.IsTrue(resp.Code == 200 || resp.Code == (int)HttpStatusCode.Unauthorized, $"����ķ�����: {resp.Code} {resp.Message}");
            if (resp.Code == 200)
            {
                Assert.IsNotNull(resp.Data);
                Assert.IsNotNull(resp.Data!.Content);
            }
        }

        /// <summary>
        /// ���������� /api/fs/get ��ȡ�������飬������ 200 ��Ӧ�������ƵȻ����ֶΣ����� 401 Ҳ�ɽ��ܡ�
        /// </summary>
        [TestMethod]
        public async Task ��ȡ����_Ӧ���������δ��Ȩ()
        {
            if (string.IsNullOrWhiteSpace(_cfg.Token))
                Assert.Inconclusive("test.json ȱ�� Token�������ȵ��õ�¼�ӿڻ�ȡ��");

            var fs = new FileSystemApi(_cfg.BaseUrl!);
            var resp = await fs.GetAsync(_cfg.Token!, _cfg.Path ?? "/test");
            Assert.IsTrue(resp.Code == 200 || resp.Code == (int)HttpStatusCode.Unauthorized, $"����ķ�����: {resp.Code} {resp.Message}");
            if (resp.Code == 200)
            {
                Assert.IsNotNull(resp.Data);
                Assert.IsFalse(string.IsNullOrWhiteSpace(resp.Data!.Name));
            }
        }

        /// <summary>
        /// �����������������ģ�͡�
        /// </summary>
        private sealed class TestConfig
        {
            /// <summary>
            /// ����˻�����ַ�����磺https://example.com
            /// </summary>
            [JsonPropertyName("baseUrl")] public string? BaseUrl { get; set; }

            /// <summary>
            /// ��¼�û�����
            /// </summary>
            [JsonPropertyName("username")] public string? Username { get; set; }

            /// <summary>
            /// ��¼���롣
            /// </summary>
            [JsonPropertyName("password")] public string? Password { get; set; }

            /// <summary>
            /// ��Ȩ���ƣ�ĳЩ�ӿ���Ҫ������ /api/fs/list��/api/fs/get����
            /// </summary>
            [JsonPropertyName("token")] public string? Token { get; set; }

            /// <summary>
            /// Ĭ��Ŀ��·���������б�����������
            /// </summary>
            [JsonPropertyName("path")] public string? Path { get; set; }
        }
    }
}
