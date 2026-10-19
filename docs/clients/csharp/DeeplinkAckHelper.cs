using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace LabelPlus_Next.DeeplinkClients
{
    /// <summary>
    /// Deeplink 回调帮助类：发送 ack 到后端，带指数退避、失败时入队本地持久化。
    /// 可直接拷贝到项目中并按需调整 AckEndpoint 与 HttpClient 的认证头。
    /// </summary>
    public static class DeeplinkAckHelper
    {
        private static readonly HttpClient httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10)
        };

        // 修改为你的后端地址
        private static readonly string AckEndpoint = "https://yourserver.example.com/api/deeplink/ack";
        private static readonly int MaxRetries = 4;
        private static readonly TimeSpan InitialBackoff = TimeSpan.FromMilliseconds(500);

        static DeeplinkAckHelper()
        {
            // 如需认证头，在这里设置，例如：
            // httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "<token>");
        }

        /// <summary>
        /// 尝试发送 ack，遇到可重试错误时进行指数退避，最终失败时本地入队以便后续重试。
        /// 返回 true 表示服务器已成功确认 ack。
        /// </summary>
        public static async Task<bool> SendAckWithRetryAsync(string token)
        {
            if (string.IsNullOrWhiteSpace(token)) return false;

            var payload = JsonSerializer.Serialize(new { token = token });
            var content = new StringContent(payload, Encoding.UTF8, "application/json");

            TimeSpan backoff = InitialBackoff;
            for (int attempt = 0; attempt < MaxRetries; attempt++)
            {
                try
                {
                    // 必须为每次请求使用新的 HttpContent（上面创建的 content 会在第一次请求后被处置）
                    using var c = new StringContent(payload, Encoding.UTF8, "application/json");
                    var resp = await httpClient.PostAsync(AckEndpoint, c).ConfigureAwait(false);
                    if (resp.IsSuccessStatusCode)
                    {
                        return true;
                    }
                    else if ((int)resp.StatusCode >= 400 && (int)resp.StatusCode < 500)
                    {
                        // 客户端错误（例如 400/404）通常表明 token 无效或已过期，不应重试
                        return false;
                    }
                    // 其他状态码（例如 5xx）会进入重试
                }
                catch (HttpRequestException)
                {
                    // 网络层错误，重试
                }
                catch (TaskCanceledException)
                {
                    // 超时，重试
                }
                catch (Exception)
                {
                    // 未知异常，不立即放弃，但做退避
                }

                await Task.Delay(backoff).ConfigureAwait(false);
                backoff = TimeSpan.FromMilliseconds(backoff.TotalMilliseconds * 2);
            }

            // 如果到这里还没成功，入队并返回 false
            await QueueAckForLaterAsync(token).ConfigureAwait(false);
            return false;
        }

        private static readonly string QueueFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "labelplus", "pending_ack.jsonl");

        /// <summary>
        /// 将 ack 写入本地持久化队列（JSONL 格式，每行一个 JSON 对象）。
        /// </summary>
        public static async Task QueueAckForLaterAsync(string token)
        {
            try
            {
                var dir = Path.GetDirectoryName(QueueFile);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir!);
                var item = new { token = token, ts = DateTimeOffset.UtcNow };
                var line = JsonSerializer.Serialize(item);
                await File.AppendAllTextAsync(QueueFile, line + Environment.NewLine).ConfigureAwait(false);
            }
            catch
            {
                // 无法持久化时吞掉异常（不要阻塞应用启动）
            }
        }

        /// <summary>
        /// 尝试清理并重发队列中的 ack（建议在应用启动或网络恢复时调用）。
        /// </summary>
        public static async Task FlushQueuedAcksAsync()
        {
            try
            {
                if (!File.Exists(QueueFile)) return;
                var lines = await File.ReadAllLinesAsync(QueueFile).ConfigureAwait(false);
                var remaining = new List<string>();
                foreach (var line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    try
                    {
                        using var doc = JsonDocument.Parse(line);
                        if (!doc.RootElement.TryGetProperty("token", out var tokenElem)) continue;
                        var token = tokenElem.GetString();
                        if (string.IsNullOrWhiteSpace(token)) continue;
                        var ok = await SendAckWithRetryAsync(token!).ConfigureAwait(false);
                        if (!ok) remaining.Add(line);
                    }
                    catch
                    {
                        // 如果单行解析失败，保留以便人工检查
                        remaining.Add(line);
                    }
                }

                // 覆写文件为剩余项（如果为空则删除文件）
                if (remaining.Count == 0)
                {
                    try { File.Delete(QueueFile); } catch { }
                }
                else
                {
                    await File.WriteAllTextAsync(QueueFile, string.Join(Environment.NewLine, remaining) + Environment.NewLine).ConfigureAwait(false);
                }
            }
            catch
            {
                // 日志可选：在主应用里记录失败信息
            }
        }
    }
}
