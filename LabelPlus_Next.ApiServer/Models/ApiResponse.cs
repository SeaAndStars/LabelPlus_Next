using System.Text.Json.Serialization;

namespace LabelPlus_Next.ApiServer.Models;

public sealed class ApiResponse<T>
{
    [JsonPropertyName("code")] public int Code { get; set; }
    [JsonPropertyName("data")] public T? Data { get; set; }
    [JsonPropertyName("message")] public string? Message { get; set; }

    public static ApiResponse<T> Ok(T? data) => new() { Code = 200, Data = data, Message = "ok" };
    public static ApiResponse<T> Fail(int code, string? message) => new() { Code = code, Message = message };
}

