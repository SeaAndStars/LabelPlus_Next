using System.Text.Json.Serialization;

namespace LabelPlus_Next.Models;

public class UploadSettings
{
    [JsonPropertyName("baseUrl")] public string? BaseUrl { get; set; }
    [JsonPropertyName("username")] public string? Username { get; set; }
    [JsonPropertyName("password")] public string? Password { get; set; }
    // 分片上传开关与参数（可选）
    [JsonPropertyName("enableChunkUpload")] public bool? EnableChunkUpload { get; set; }
    // 触发分片的阈值（MB）；大于等于该大小的文件尝试分片
    [JsonPropertyName("chunkThresholdMB")] public int? ChunkThresholdMB { get; set; }
    // 分片大小（MB）
    [JsonPropertyName("chunkSizeMB")] public int? ChunkSizeMB { get; set; }
    // 每个分片最大重试次数
    [JsonPropertyName("chunkMaxRetries")] public int? ChunkMaxRetries { get; set; }
}
