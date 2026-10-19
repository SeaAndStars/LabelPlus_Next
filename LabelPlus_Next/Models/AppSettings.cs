using System.Text.Json.Serialization;

namespace LabelPlus_Next.Models;

public class AppSettings
{
    [JsonPropertyName("update")] public UpdateSettings Update { get; set; } = new();
    [JsonPropertyName("labelOverlay")] public LabelOverlaySettings LabelOverlay { get; set; } = new();
}

public class LabelOverlaySettings
{
    public const double DefaultLabelIndexFontScale = 1.0;
    public const double DefaultLabelTipFontScale = 1.0;
    public const double DefaultLabelTipBackgroundOpacity = 0.7;

    public const double MinLabelIndexFontScale = 0.5;
    public const double MaxLabelIndexFontScale = 2.5;

    public const double MinLabelTipFontScale = 0.5;
    public const double MaxLabelTipFontScale = 2.5;

    public const double MinLabelTipBackgroundOpacity = 0.0;
    public const double MaxLabelTipBackgroundOpacity = 1.0;

    [JsonPropertyName("labelIndexFontScale")] public double LabelIndexFontScale { get; set; } = DefaultLabelIndexFontScale;

    [JsonPropertyName("labelTipFontScale")] public double LabelTipFontScale { get; set; } = DefaultLabelTipFontScale;

    [JsonPropertyName("labelTipBackgroundOpacity")] public double LabelTipBackgroundOpacity { get; set; } = DefaultLabelTipBackgroundOpacity;
}

public class UpdateSettings
{
    // Default, hard-coded configuration for update checking (used by main app when needed)
    public const string DefaultBaseUrl = "https://alist.seastarss.cn";
    public const string DefaultManifestPath = "/OneDrive2/Update/manifest.json";

    // e.g. https://webdav.example.com/updates/
    [JsonPropertyName("baseUrl")] public string? BaseUrl { get; set; } = DefaultBaseUrl;

    // e.g. app/manifest.json (relative to base)
    [JsonPropertyName("manifestPath")] public string? ManifestPath { get; set; } = DefaultManifestPath;

    [JsonPropertyName("username")] public string? Username { get; set; }

    [JsonPropertyName("password")] public string? Password { get; set; }

    // Optional dev callback server for deeplink testing e.g. https://localhost:5175/api/deeplink/ack
    [JsonPropertyName("deeplinkCallbackUrl")] public string? DeeplinkCallbackUrl { get; set; }

    // Development switch: when true, client may fallback to http:// for localhost callback URLs on TLS failures
    [JsonPropertyName("allowLocalHttpFallback")] public bool AllowLocalHttpFallback { get; set; } = false;
    // Optional Authorization header to include when posting deeplink ack (e.g. "Bearer <token>")
    [JsonPropertyName("deeplinkCallbackAuth")] public string? DeeplinkCallbackAuth { get; set; }
}
