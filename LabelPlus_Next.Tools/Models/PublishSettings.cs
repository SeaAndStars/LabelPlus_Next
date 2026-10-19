using System.Text.Json.Serialization;

namespace LabelPlus_Next.Tools.Models;

public sealed class PublishSettings
{
    [JsonPropertyName("baseUrl")] public string BaseUrl { get; set; } = "https://alist.seastarss.cn";
    [JsonPropertyName("username")] public string Username { get; set; } = string.Empty;
    [JsonPropertyName("password")] public string Password { get; set; } = string.Empty;
    // Where to write manifest.json, e.g. /OneDrive2/Update/manifest.json (relative to /dav root)
    [JsonPropertyName("manifestPath")] public string ManifestPath { get; set; } = "/OneDrive2/Update/manifest.json";
    // Where to upload release files, e.g. /OneDrive2/Update
    [JsonPropertyName("uploadRoot")] public string UploadRoot { get; set; } = "/OneDrive2/Update";

    // Optional local path to a json settings file to override fields above (passed via --settings)
}
