using LabelPlus_Next.Models;
using System.Text.Json.Serialization;

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
[JsonSerializable(typeof(ProjectCn))]
[JsonSerializable(typeof(EpisodeCn))]
[JsonSerializable(typeof(AggregateProjects))]
public partial class AppJsonContext : JsonSerializerContext
{
}
