// filepath: c:\Users\SeaStar\source\repos\SeaAndStars\LabelPlus_Next\LabelPlus_Next.Test\MemorySettingsService.cs
using LabelPlus_Next.Models;
using LabelPlus_Next.Services;

namespace LabelPlus_Next.Test;

public sealed class MemorySettingsService : ISettingsService
{
    public AppSettings Data { get; set; } = new();
    public Task<AppSettings> LoadAsync(CancellationToken ct = default) => Task.FromResult(Data);
    public Task SaveAsync(AppSettings settings, CancellationToken ct = default) { Data = settings; return Task.CompletedTask; }
}

