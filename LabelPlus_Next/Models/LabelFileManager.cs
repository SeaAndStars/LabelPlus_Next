using System.Collections.Generic;
using System.Threading.Tasks;

namespace LabelPlus_Next.Models;

public class LabelFileManager
{
    public string[] FileHead { get; private set; } = new[] { "1", "0" };
    public List<string> GroupStringList { get; private set; } = new();
    public string Comment { get; private set; } = "";
    public LabelStoreManager StoreManager { get; } = new();

    public async Task LoadAsync(string path)
    {
        var reader = new LabelFileReader();
        var (header, store) = await reader.ReadAsync(path);

        var (fileHead, groupList, comment) = await LabelFileHeaderManager.ParseHeaderAsync(header);
        FileHead = fileHead;
        GroupStringList = groupList;
        Comment = comment;

        StoreManager.Store.Clear();
        foreach (var kvp in store)
            StoreManager.Store[kvp.Key] = kvp.Value;
    }

    public async Task SaveAsync(string path)
    {
        var header = await LabelFileHeaderManager.GenerateHeaderAsync(FileHead, GroupStringList, Comment);
        var writer = new LabelFileWriter();
        await writer.WriteAsync(path, header, StoreManager.Store);
    }

    // Update header data (groups and comment)
    public void UpdateHeader(List<string> groups, string comment)
    {
        GroupStringList = groups ?? new List<string>();
        Comment = comment ?? string.Empty;
    }
}