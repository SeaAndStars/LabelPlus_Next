using System.Text.Json.Serialization;

namespace LabelPlus_Next.ApiServer.Models;

public sealed class FsListRequest
{
    [JsonPropertyName("path")] public string? Path { get; set; }
    [JsonPropertyName("password")] public string? Password { get; set; }
    [JsonPropertyName("page")] public int Page { get; set; } = 1;
    [JsonPropertyName("per_page")] public int PerPage { get; set; } = 0;
    [JsonPropertyName("refresh")] public bool Refresh { get; set; } = true;
}

public sealed class FsGetRequest
{
    [JsonPropertyName("path")] public string? Path { get; set; }
    [JsonPropertyName("password")] public string? Password { get; set; }
    [JsonPropertyName("page")] public int Page { get; set; } = 1;
    [JsonPropertyName("per_page")] public int PerPage { get; set; } = 0;
    [JsonPropertyName("refresh")] public bool Refresh { get; set; } = true;
}

public sealed class FsSearchRequest
{
    [JsonPropertyName("parent")] public string? Parent { get; set; }
    [JsonPropertyName("keywords")] public string? Keywords { get; set; }
    [JsonPropertyName("scope")] public int Scope { get; set; }
    [JsonPropertyName("page")] public int Page { get; set; } = 1;
    [JsonPropertyName("per_page")] public int PerPage { get; set; } = 20;
    [JsonPropertyName("password")] public string? Password { get; set; }
}

public sealed class FsListResponse
{
    [JsonPropertyName("code")] public int Code { get; set; }
    [JsonPropertyName("data")] public FsListData? Data { get; set; }
    [JsonPropertyName("message")] public string? Message { get; set; }
}

public sealed class FsListData
{
    [JsonPropertyName("content")] public FsItem[]? Content { get; set; }
    [JsonPropertyName("header")] public string? Header { get; set; }
    [JsonPropertyName("provider")] public string? Provider { get; set; }
    [JsonPropertyName("readme")] public string? Readme { get; set; }
    [JsonPropertyName("total")] public long Total { get; set; }
    [JsonPropertyName("write")] public bool Write { get; set; }
}

public sealed class FsItem
{
    [JsonPropertyName("created")] public string? Created { get; set; }
    [JsonPropertyName("hash_info")] public object? HashInfo { get; set; }
    [JsonPropertyName("hashinfo")] public string? Hashinfo { get; set; }
    [JsonPropertyName("is_dir")] public bool IsDir { get; set; }
    [JsonPropertyName("modified")] public string? Modified { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("sign")] public string? Sign { get; set; }
    [JsonPropertyName("size")] public long Size { get; set; }
    [JsonPropertyName("thumb")] public string? Thumb { get; set; }
    [JsonPropertyName("type")] public long Type { get; set; }
}

public sealed class FsGetResponse
{
    [JsonPropertyName("code")] public int Code { get; set; }
    [JsonPropertyName("data")] public FsGetData? Data { get; set; }
    [JsonPropertyName("message")] public string? Message { get; set; }
}

public sealed class FsGetData
{
    [JsonPropertyName("created")] public string? Created { get; set; }
    [JsonPropertyName("hash_info")] public object? HashInfo { get; set; }
    [JsonPropertyName("hashinfo")] public string? Hashinfo { get; set; }
    [JsonPropertyName("header")] public string? Header { get; set; }
    [JsonPropertyName("is_dir")] public bool IsDir { get; set; }
    [JsonPropertyName("modified")] public string? Modified { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("provider")] public string? Provider { get; set; }
    [JsonPropertyName("raw_url")] public string? RawUrl { get; set; }
    [JsonPropertyName("readme")] public string? Readme { get; set; }
    [JsonPropertyName("related")] public object? Related { get; set; }
    [JsonPropertyName("sign")] public string? Sign { get; set; }
    [JsonPropertyName("size")] public long Size { get; set; }
    [JsonPropertyName("thumb")] public string? Thumb { get; set; }
    [JsonPropertyName("type")] public long Type { get; set; }
}

public sealed class FsSearchResponse
{
    [JsonPropertyName("code")] public int Code { get; set; }
    [JsonPropertyName("data")] public FsSearchData? Data { get; set; }
    [JsonPropertyName("message")] public string? Message { get; set; }
}

public sealed class FsSearchData
{
    [JsonPropertyName("content")] public FsSearchItem[]? Content { get; set; }
    [JsonPropertyName("total")] public long Total { get; set; }
}

public sealed class FsSearchItem
{
    [JsonPropertyName("is_dir")] public bool IsDir { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("parent")] public string? Parent { get; set; }
    [JsonPropertyName("size")] public long Size { get; set; }
    [JsonPropertyName("type")] public long Type { get; set; }
}

public sealed class MkdirRequest { [JsonPropertyName("path")] public string? Path { get; set; } }

public sealed class CopyRequest
{
    [JsonPropertyName("src_dir")] public string? SrcDir { get; set; }
    [JsonPropertyName("dst_dir")] public string? DstDir { get; set; }
    [JsonPropertyName("names")] public string[]? Names { get; set; }
}

public sealed class FsPutResponse
{
    [JsonPropertyName("code")] public int Code { get; set; }
    [JsonPropertyName("data")] public FsPutData? Data { get; set; }
    [JsonPropertyName("message")] public string? Message { get; set; }
}

public sealed class FsPutData
{
    [JsonPropertyName("task")] public FsTask? Task { get; set; }
}

public sealed class FsTask
{
    [JsonPropertyName("error")] public string? Error { get; set; }
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("progress")] public long Progress { get; set; }
    [JsonPropertyName("state")] public long State { get; set; }
    [JsonPropertyName("status")] public string? Status { get; set; }
}

