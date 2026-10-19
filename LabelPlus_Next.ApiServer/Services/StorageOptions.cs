namespace LabelPlus_Next.ApiServer.Services;

public sealed class StorageOptions
{
    public string RootPath { get; set; } = Path.Combine(AppContext.BaseDirectory, "storage");
    public string TempPath { get; set; } = Path.Combine(AppContext.BaseDirectory, "storage", "tmp");
}

