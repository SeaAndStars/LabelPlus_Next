using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using NLog;

namespace LabelPlus_Next.DeeplinkClients
{
    /// <summary>
    /// Deeplink 回调帮助类（主项目内实现）。
    /// 使用手动 JSON 串以避免 System.Text.Json 裁剪/AOT 警告。
    /// </summary>
    public static class DeeplinkAckHelper
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private static readonly HttpClient httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10)
        };

        private static readonly int MaxRetries = 4;
        private static readonly TimeSpan InitialBackoff = TimeSpan.FromMilliseconds(500);

        private static string JsonEscapeForSimple(string? s)
        {
            if (s is null) return string.Empty;
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\b", "\\b").Replace("\f", "\\f").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");
        }

        /// <summary>
        /// 发送 ack（供 App 在接收到 deeplink 后调用）。
        /// </summary>
        public static async Task<bool> SendAckWithRetryAsync(string callbackUrl, string key, bool allowLocalHttpFallback = false, string? authorizationHeader = null, bool isAckSecret = false)
        {
            if (string.IsNullOrWhiteSpace(callbackUrl) || string.IsNullOrWhiteSpace(key)) return false;
            var payload = isAckSecret ? "{\"ackSecret\":\"" + JsonEscapeForSimple(key) + "\"}" : "{\"token\":\"" + JsonEscapeForSimple(key) + "\"}";
            Logger.Info("Deeplink ack: preparing to POST to {url} payload={payloadShort}", callbackUrl, payload.Length > 200 ? payload.Substring(0, 200) + "..." : payload);
            TimeSpan backoff = InitialBackoff;
            bool triedHttpFallback = false; // for https://localhost fallback
            for (int attempt = 0; attempt < MaxRetries; attempt++)
            {
                try
                {
                    using var c = new StringContent(payload, Encoding.UTF8, "application/json");
                    using var req = new HttpRequestMessage(HttpMethod.Post, callbackUrl) { Content = c };
                    if (!string.IsNullOrWhiteSpace(authorizationHeader))
                    {
                        try { req.Headers.Authorization = AuthenticationHeaderValue.Parse(authorizationHeader); } catch { }
                    }
                    var resp = await httpClient.SendAsync(req).ConfigureAwait(false);
                    var status = (int)resp.StatusCode;
                    string respBody = "";
                    try { respBody = await resp.Content.ReadAsStringAsync().ConfigureAwait(false); } catch (Exception re) { Logger.Debug(re, "Read response body failed"); }
                    if (resp.IsSuccessStatusCode)
                    {
                        Logger.Info("Deeplink ack succeeded to {url} status={status} body={bodyShort}", callbackUrl, status, respBody.Length > 200 ? respBody.Substring(0, 200) + "..." : respBody);
                        return true;
                    }
                    if (status >= 400 && status < 500)
                    {
                        Logger.Warn("Deeplink ack returned client error {status} to {url} body={bodyShort}", status, callbackUrl, respBody.Length > 200 ? respBody.Substring(0, 200) + "..." : respBody);
                        return false;
                    }
                    Logger.Warn("Deeplink ack returned non-success status {status} to {url} body={bodyShort}, will retry", status, callbackUrl, respBody.Length > 200 ? respBody.Substring(0, 200) + "..." : respBody);
                }
                catch (HttpRequestException hre)
                {
                    Logger.Warn(hre, "HttpRequestException while sending deeplink ack (attempt {attempt}) to {url}", attempt + 1, callbackUrl);
                    try
                    {
                        // If this looks like an SSL/TLS handshake error and target is localhost, and developer allows fallback, try an http:// fallback once
                        if (allowLocalHttpFallback && !triedHttpFallback && Uri.TryCreate(callbackUrl, UriKind.Absolute, out var parsed) &&
                            string.Equals(parsed.Scheme, "https", StringComparison.OrdinalIgnoreCase) &&
                            (string.Equals(parsed.Host, "localhost", StringComparison.OrdinalIgnoreCase) || parsed.Host == "127.0.0.1" || parsed.Host == "::1"))
                        {
                            triedHttpFallback = true;
                            var fb = new UriBuilder(parsed) { Scheme = "http" };
                            // If original port was 443 and scheme changed to http, leave port unset so default is used
                            if (parsed.Port == 443) fb.Port = -1;
                            var fallbackUrl = fb.Uri.ToString();
                            Logger.Info("SSL error detected; attempting http:// fallback to {fb}", fallbackUrl);
                            using var c2 = new StringContent(payload, Encoding.UTF8, "application/json");
                            using var req2 = new HttpRequestMessage(HttpMethod.Post, fallbackUrl) { Content = c2 };
                            if (!string.IsNullOrWhiteSpace(authorizationHeader))
                            {
                                try { req2.Headers.Authorization = AuthenticationHeaderValue.Parse(authorizationHeader); } catch { }
                            }
                            try
                            {
                                var resp2 = await httpClient.SendAsync(req2).ConfigureAwait(false);
                                if (resp2.IsSuccessStatusCode)
                                {
                                    var respBody2 = "";
                                    try { respBody2 = await resp2.Content.ReadAsStringAsync().ConfigureAwait(false); } catch { }
                                    Logger.Info("Deeplink ack succeeded to {url} (fallback) status={status} body={bodyShort}", fallbackUrl, (int)resp2.StatusCode, respBody2.Length > 200 ? respBody2.Substring(0, 200) + "..." : respBody2);
                                    return true;
                                }
                                Logger.Warn("Deeplink ack fallback to {fb} returned status {status}, will continue retrying", fallbackUrl, (int)resp2.StatusCode);
                            }
                            catch (Exception fex)
                            {
                                Logger.Warn(fex, "Fallback http attempt to {fb} failed", fallbackUrl);
                            }
                        }
                    }
                    catch (Exception) { }
                }
                catch (TaskCanceledException tce)
                {
                    Logger.Warn(tce, "Timeout while sending deeplink ack (attempt {attempt}) to {url}", attempt + 1, callbackUrl);
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "Unexpected error while sending deeplink ack (attempt {attempt}) to {url}", attempt + 1, callbackUrl);
                }
                await Task.Delay(backoff).ConfigureAwait(false);
                backoff = TimeSpan.FromMilliseconds(backoff.TotalMilliseconds * 2);
            }

            // queue for later
            Logger.Warn("Deeplink ack to {url} failed after retries; queueing for later delivery", callbackUrl);
            await QueueAckForLaterAsync(callbackUrl, key, authorizationHeader, isAckSecret).ConfigureAwait(false);
            return false;
        }

        private static readonly string QueueFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "labelplus", "pending_ack.jsonl");

        public static async Task QueueAckForLaterAsync(string callbackUrl, string key, string? authorizationHeader = null, bool isAckSecret = false)
        {
            try
            {
                var dir = Path.GetDirectoryName(QueueFile);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir!);
                var line = "{\"key\":\"" + JsonEscapeForSimple(key) + "\",\"isAckSecret\":\"" + (isAckSecret ? "true" : "false") + "\",\"ts\":\"" + DateTimeOffset.UtcNow.ToString("o") + "\",\"url\":\"" + JsonEscapeForSimple(callbackUrl) + "\"";
                if (!string.IsNullOrEmpty(authorizationHeader)) line += ",\"auth\":\"" + JsonEscapeForSimple(authorizationHeader) + "\"";
                line += "}";
                await File.AppendAllTextAsync(QueueFile, line + Environment.NewLine).ConfigureAwait(false);
                Logger.Info("Queued deeplink ack to local file {file} key={keyShort} isAckSecret={isAck}", QueueFile, key.Length > 8 ? key.Substring(0, 8) + "..." : key, isAckSecret);
            }
            catch { }
        }

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
                        // quick-and-dirty parse: find key value and isAckSecret flag
                        var key = ExtractJsonValue(line, "key");
                        var url = ExtractJsonValue(line, "url");
                        var auth = ExtractJsonValue(line, "auth");
                        var isAckSecretStr = ExtractJsonValue(line, "isAckSecret");
                        var isAckSecret = string.Equals(isAckSecretStr, "true", StringComparison.OrdinalIgnoreCase);
                        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(url)) { remaining.Add(line); continue; }
                        Logger.Info("Flushing queued ack to {url} key={keyShort} isAckSecret={isAck}", url, key.Length > 8 ? key.Substring(0, 8) + "..." : key, isAckSecret);
                        var ok = await SendAckWithRetryAsync(url, key, false, auth, isAckSecret).ConfigureAwait(false);
                        if (!ok) remaining.Add(line);
                    }
                    catch
                    {
                        remaining.Add(line);
                        Logger.Warn("Failed to process queued ack line, left for later: {line}", line.Length > 200 ? line.Substring(0, 200) + "..." : line);
                    }
                }
                if (remaining.Count == 0)
                {
                    try { File.Delete(QueueFile); } catch { }
                }
                else
                {
                    await File.WriteAllTextAsync(QueueFile, string.Join(Environment.NewLine, remaining) + Environment.NewLine).ConfigureAwait(false);
                }
            }
            catch { }
        }

        private static string? ExtractJsonValue(string jsonLine, string key)
        {
            if (string.IsNullOrEmpty(jsonLine) || string.IsNullOrEmpty(key)) return null;
            var tokenKey = "\"" + key + "\":\"";
            var idx = jsonLine.IndexOf(tokenKey, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return null;
            var start = idx + tokenKey.Length;
            var end = jsonLine.IndexOf('"', start);
            if (end < 0) return null;
            var raw = jsonLine.Substring(start, end - start);
            return raw.Replace("\\\"", "\"").Replace("\\\\", "\\");
        }
    }
}
