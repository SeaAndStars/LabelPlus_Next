using System.Collections.Generic;
using System.Threading.Tasks;
using NLog;

namespace LabelPlus_Next.Models;

public class LabelFileManager
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    public string[] FileHead { get; private set; } = new[] { "1", "0" };
    public List<string> GroupStringList { get; private set; } = new();
    public string Comment { get; private set; } = "";
    public LabelStoreManager StoreManager { get; } = new();

    public async Task LoadAsync(string path)
    {
        Logger.Info("Loading translation file: {path}", path);
        var reader = new LabelFileReader();
        var (header, store) = await reader.ReadAsync(path);

        var (fileHead, groupList, comment) = await LabelFileHeaderManager.ParseHeaderAsync(header);
        FileHead = fileHead;
        GroupStringList = groupList;
        Comment = comment;

        StoreManager.Store.Clear();
        foreach (var kvp in store)
            StoreManager.Store[kvp.Key] = kvp.Value;

        // Loaded from disk, clear dirty state
        StoreManager.ResetDirty();
        Logger.Info("Loaded: images={count}", StoreManager.Store.Count);
    }

    public async Task SaveAsync(string path)
    {
        Logger.Info("Saving translation file: {path}", path);
        var header = await LabelFileHeaderManager.GenerateHeaderAsync(FileHead, GroupStringList, Comment);
        var writer = new LabelFileWriter();
        await writer.WriteAsync(path, header, StoreManager.Store);
        // Saved to disk, clear dirty state
        StoreManager.ResetDirty();
        Logger.Info("Saved successfully: {path}", path);
    }

    // Update header data (groups and comment)
    public void UpdateHeader(List<string> groups, string comment)
    {
        GroupStringList = groups ?? new List<string>();
        Comment = comment ?? string.Empty;
        StoreManager.TouchDirty();
        Logger.Debug("Header updated: groups={groupsCount}", GroupStringList.Count);
    }
}