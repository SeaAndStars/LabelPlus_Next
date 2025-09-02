using System.ComponentModel.DataAnnotations;

namespace LabelPlus_Next.ApiServer.Entities;

public sealed class FileEntry
{
    public long Id { get; set; }
    [MaxLength(1024)] public string Path { get; set; } = "/"; // normalized absolute
    [MaxLength(1024)] public string ParentPath { get; set; } = "/";
    [MaxLength(255)] public string Name { get; set; } = string.Empty;
    public bool IsDir { get; set; }
    public long Size { get; set; }
    public DateTimeOffset Created { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset Modified { get; set; } = DateTimeOffset.UtcNow;
    public byte[]? Content { get; set; } // null for directory
    public string? Provider { get; set; }
    public string? Thumb { get; set; }
    public string? Hashinfo { get; set; }
    public string? Sign { get; set; }
    public long Type { get; set; }
}

