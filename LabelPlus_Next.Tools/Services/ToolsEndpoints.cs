using System;

namespace LabelPlus_Next.Tools.Services;

/// <summary>
/// URL helpers shared by the WebDAV browser and the publish tool.
/// </summary>
public static class ToolsEndpoints
{
    public static string EnsureSlash(string url)
        => url.EndsWith('/') ? url : url + "/";

    /// <summary>
    /// Resolves the AList file-API root (e.g. https://host/).
    /// <para>
    /// The WebDAV endpoint is <c>https://host/dav/</c> while file/API calls must go to
    /// the site root: RestSharp appends the base path, so using the WebDAV URL for
    /// <c>/api/auth/login</c> posts to <c>/dav/api/auth/login</c> and AList answers 401.
    /// Prefer the explicitly configured API base URL, but normalize a WebDAV URL that
    /// was pasted into it as well. When no API base is configured, derive it from the
    /// WebDAV URL by stripping the trailing <c>/dav</c> segment.
    /// </para>
    /// </summary>
    public static string? ResolveApiBaseUrl(string? apiBaseUrl, string? webDavBaseUrl)
    {
        if (!string.IsNullOrWhiteSpace(apiBaseUrl))
        {
            return NormalizeApiRoot(apiBaseUrl);
        }

        return string.IsNullOrWhiteSpace(webDavBaseUrl) ? null : NormalizeApiRoot(webDavBaseUrl);
    }

    private static string NormalizeApiRoot(string url)
    {
        var baseUrl = url.Trim();

        if (Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri))
        {
            var path = uri.AbsolutePath.TrimEnd('/');
            if (path.EndsWith("/dav", StringComparison.OrdinalIgnoreCase))
            {
                var rootPath = path[..^"/dav".Length];
                var builder = new UriBuilder(uri)
                {
                    Path = string.IsNullOrEmpty(rootPath) ? "/" : rootPath + "/",
                    Query = string.Empty,
                    Fragment = string.Empty
                };
                return EnsureSlash(builder.Uri.ToString());
            }
        }

        return EnsureSlash(baseUrl);
    }
}
