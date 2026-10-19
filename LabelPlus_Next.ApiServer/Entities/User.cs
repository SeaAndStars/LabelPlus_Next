using System.ComponentModel.DataAnnotations;

namespace LabelPlus_Next.ApiServer.Entities;

public sealed class User
{
    public long Id { get; set; }
    [MaxLength(64)] public string Username { get; set; } = string.Empty;
    [MaxLength(256)] public string PasswordHash { get; set; } = string.Empty;
    public string? BasePath { get; set; }
    public bool Disabled { get; set; }
    public long Permission { get; set; }
    public long Role { get; set; }
    public string? SsoId { get; set; }
    public bool Otp { get; set; }
}

