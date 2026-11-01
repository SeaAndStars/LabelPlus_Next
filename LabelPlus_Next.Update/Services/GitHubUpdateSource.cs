using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LabelPlus_Next.Shared;
using NLog;

namespace LabelPlus_Next.Update.Services;

public sealed record GitHubReleaseAsset(string Name, string Url, string? Sha256, long Size);

public sealed record GitHubReleaseInfo(string Version, IReadOnlyList<GitHubReleaseAsset> Assets);

/// <summary>
/// Last-resort update source when the AList manifest/download is unavailable.
/// GitHub release assets are public and use the same zip naming as the manifest.
/// </summary>
public static class GitHubUpdateSource
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    public const string Repository = "SeaAndStars/LabelPlus_Next";
    private const string LatestReleaseApi = $"https://api.github.com/repos/{Repository}/releases/latest";

    public static string DownloadUrl(string version, string project, string rid)
        => $"https://github.com/{Repository}/releases/download/v{version.TrimStart('v', 'V')}/{project}-{rid}.zip";

    public static async Task<GitHubReleaseInfo?> FetchLatestAsync(CancellationToken ct = default)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, LatestReleaseApi);
            req.Headers.TryAddWithoutValidation("User-Agent", UpdateHttp.UserAgent);
            req.Headers.TryAddWithoutValidation("Accept", "application/vnd.github+json");
            using var resp = await UpdateHttp.Client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                Logger.Warn("GitHub latest release API returned {code}", (int)resp.StatusCode);
                return null;
            }

            await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
            var root = doc.RootElement;
            var tag = root.TryGetProperty("tag_name", out var tagEl) ? tagEl.GetString() : null;
            if (string.IsNullOrWhiteSpace(tag)) return null;

            var version = tag.TrimStart('v', 'V');
            var assets = new List<GitHubReleaseAsset>();
            if (root.TryGetProperty("assets", out var assetsEl) && assetsEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var asset in assetsEl.EnumerateArray())
                {
                    var name = asset.TryGetProperty("name", out var nameEl) ? nameEl.GetString() : null;
                    var url = asset.TryGetProperty("browser_download_url", out var urlEl) ? urlEl.GetString() : null;
                    if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(url)) continue;

                    var digest = asset.TryGetProperty("digest", out var digestEl) ? digestEl.GetString() : null;
                    var sha = digest is not null && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)
                        ? digest["sha256:".Length..]
                        : null;
                    var size = asset.TryGetProperty("size", out var sizeEl) && sizeEl.TryGetInt64(out var n) ? n : 0;
                    assets.Add(new GitHubReleaseAsset(name, url, sha, size));
                }
            }

            Logger.Info("GitHub fallback release: {version}, assets={count}", version, assets.Count);
            return new GitHubReleaseInfo(version, assets);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            Logger.Warn(ex, "GitHub fallback release check failed");
            return null;
        }
    }

    public static GitHubReleaseAsset? FindAsset(GitHubReleaseInfo release, string project, string rid)
        => release.Assets.FirstOrDefault(a => string.Equals(a.Name, $"{project}-{rid}.zip", StringComparison.OrdinalIgnoreCase))
           ?? release.Assets.FirstOrDefault(a => a.Name.Contains(rid, StringComparison.OrdinalIgnoreCase)
                                                  && a.Name.StartsWith(project + "-", StringComparison.OrdinalIgnoreCase));
}
