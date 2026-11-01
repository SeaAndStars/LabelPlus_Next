using LabelPlus_Next.Models;
using LabelPlus_Next.Serialization;
using LabelPlus_Next.Shared;
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
    private readonly bool _allowLegacyMigration;

    public JsonSettingsService(string? path = null)
    {
        _allowLegacyMigration = string.IsNullOrWhiteSpace(path);
        _path = _allowLegacyMigration ? AppConfigPaths.Settings : path!;
    }

    public async Task<AppSettings> LoadAsync(CancellationToken ct = default)
    {
        var readPath = _allowLegacyMigration
            ? AppConfigPaths.ResolveExisting(_path, AppConfigPaths.SettingsCandidates)
            : _path;
        if (!File.Exists(readPath)) return new AppSettings();

        await using var fs = File.OpenRead(readPath);
        var s = await JsonSerializer.DeserializeAsync(fs, AppJsonContext.Default.AppSettings, ct) ?? new AppSettings();

        // The per-user AppData file wins; legacy files next to the executable are
        // read once and copied to AppData so the next launch no longer touches the
        // install directory.
        if (_allowLegacyMigration && !string.Equals(readPath, _path, StringComparison.Ordinal))
        {
            await SaveAsync(s, ct);
        }
        return s;
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken ct = default)
    {
        AppConfigPaths.EnsureParent(_path);
        await using var fs = File.Create(_path);
        await JsonSerializer.SerializeAsync(fs, settings, AppJsonContext.Default.AppSettings, ct);
        AppConfigPaths.SecureFile(_path);
    }
}

public interface IUpdateService
{
    Task<UpdateManifest?> FetchManifestAsync(UpdateSettings upd, CancellationToken ct = default);
}

public class WebDavUpdateService : IUpdateService
{
    private readonly HttpClient _http;
    private const string DefaultUserAgent = "pan.baidu.com";

    public WebDavUpdateService(HttpClient? httpClient = null)
    {
        _http = httpClient ?? new HttpClient();
        _http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", DefaultUserAgent);
    }

    public async Task<UpdateManifest?> FetchManifestAsync(UpdateSettings upd, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(upd.BaseUrl) || string.IsNullOrWhiteSpace(upd.ManifestPath))
            throw new InvalidOperationException(UiText.Tr("BaseUrl/ManifestPath 未配置。"));

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
