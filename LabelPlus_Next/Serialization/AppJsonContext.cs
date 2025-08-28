using System.Text.Json.Serialization;
using LabelPlus_Next.Models;

namespace LabelPlus_Next.Serialization;

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(UpdateSettings))]
[JsonSerializable(typeof(UpdateManifest))]
[JsonSerializable(typeof(UpdateFile))]
[JsonSerializable(typeof(ManifestV1))]
[JsonSerializable(typeof(UploadSettings))]
[JsonSerializable(typeof(Dictionary<string, object>))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(ProjectJson))]
[JsonSerializable(typeof(EpisodeInfo))]
public partial class AppJsonContext : JsonSerializerContext
{
}
