namespace LabelPlus_Next.Services;

public interface IFileDialogService
{
    Task<string?> OpenTranslationFileAsync();
    Task<string?> SaveAsTranslationFileAsync(string suggestedFileName = "translation");
    Task<string?> PickFolderAsync(string title);
    Task<IReadOnlyList<string>?> PickFoldersAsync(string title);
    Task<IReadOnlyList<string>?> PickFilesAsync(string title);
    Task<IReadOnlyList<string>?> ChooseImagesAsync(string folderPath);
    Task ShowMessageAsync(string message);
}

// Test-friendly no-op implementation
public sealed class NoopFileDialogService : IFileDialogService
{
    public Task<IReadOnlyList<string>?> ChooseImagesAsync(string folderPath) => Task.FromResult<IReadOnlyList<string>?>(Array.Empty<string>());
    public Task<string?> OpenTranslationFileAsync() => Task.FromResult<string?>(null);
    public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);
    public Task<IReadOnlyList<string>?> PickFoldersAsync(string title) => Task.FromResult<IReadOnlyList<string>?>(Array.Empty<string>());
    public Task<IReadOnlyList<string>?> PickFilesAsync(string title) => Task.FromResult<IReadOnlyList<string>?>(Array.Empty<string>());
    public Task<string?> SaveAsTranslationFileAsync(string suggestedFileName = "translation") => Task.FromResult<string?>(null);
    public Task ShowMessageAsync(string message) => Task.CompletedTask;
}
