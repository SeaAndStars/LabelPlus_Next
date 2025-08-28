using LabelPlus_Next.Models;
using LabelPlus_Next.Serialization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace LabelPlus_Next.Services;

public interface ISettingsService
{
    Task<AppSettings> LoadAsync(CancellationToken ct = default);
    Task SaveAsync(AppSettings settings, CancellationToken ct = default);
}

public class JsonSettingsService : ISettingsService
{
    private readonly string _path;

    public JsonSettingsService(string? path = null)
    {
        _path = path ?? Path.Combine(AppContext.BaseDirectory, "settings.json");
    }

    public async Task<AppSettings> LoadAsync(CancellationToken ct = default)
    {
        if (!File.Exists(_path)) return new AppSettings();
        await using var fs = File.OpenRead(_path);
        var s = await JsonSerializer.DeserializeAsync(fs, AppJsonContext.Default.AppSettings, ct);
        return s ?? new AppSettings();
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken ct = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        await using var fs = File.Create(_path);
        await JsonSerializer.SerializeAsync(fs, settings, AppJsonContext.Default.AppSettings, ct);
    }
}

public interface IUpdateService
{
    Task<UpdateManifest?> FetchManifestAsync(UpdateSettings upd, CancellationToken ct = default);
}

public class WebDavUpdateService : IUpdateService
{
    private readonly HttpClient _http;

    public WebDavUpdateService(HttpClient? httpClient = null)
    {
        _http = httpClient ?? new HttpClient();
    }

    public async Task<UpdateManifest?> FetchManifestAsync(UpdateSettings upd, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(upd.BaseUrl) || string.IsNullOrWhiteSpace(upd.ManifestPath))
            throw new InvalidOperationException("BaseUrl/ManifestPath 未配置。");

        var uri = new Uri(new Uri(AppendSlash(upd.BaseUrl!)), upd.ManifestPath);
        using var req = new HttpRequestMessage(HttpMethod.Get, uri);

        if (!string.IsNullOrEmpty(upd.Username))
        {
            var token = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{upd.Username}:{upd.Password}"));
            req.Headers.Authorization = new AuthenticationHeaderValue("Basic", token);
        }

        using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        await using var s = await resp.Content.ReadAsStreamAsync(ct);
        return await JsonSerializer.DeserializeAsync(s, AppJsonContext.Default.UpdateManifest, ct);
    }

    private static string AppendSlash(string baseUrl) => baseUrl.EndsWith('/') ? baseUrl : baseUrl + "/";
}
