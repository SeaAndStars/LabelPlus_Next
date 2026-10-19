namespace LabelPlus_Next.Services;

public sealed class CollaborationSession
{
    public required string BaseUrl { get; init; }
    public required string Token { get; init; }
    public required string RemoteTranslatePath { get; set; }
    public string? Username { get; init; }

    // Last known remote content hash at load time to detect conflicts on save
    public string? LastRemoteHash { get; set; }

    public string RemoteDirectory
        => System.IO.Path.GetDirectoryName(RemoteTranslatePath)?.Replace('\\', '/') ?? "/";
}
