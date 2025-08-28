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
