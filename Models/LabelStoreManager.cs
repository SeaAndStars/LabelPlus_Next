using System.Collections.Generic;
using System.Threading.Tasks;

namespace LabelPlus_Next.Models;

public class LabelStoreManager
{
    public Dictionary<string, List<LabelItem>> Store { get; } = new();

    public async Task AddFileAsync(string file)
    {
        if (!Store.ContainsKey(file))
            Store[file] = new List<LabelItem>();
        await Task.CompletedTask;
    }

    public async Task AddLabelAsync(string file, LabelItem item)
    {
        if (!Store.ContainsKey(file))
            Store[file] = new List<LabelItem>();
        Store[file].Add(item);
        await Task.CompletedTask;
    }

    public async Task RemoveFileAsync(string file)
    {
        Store.Remove(file);
        await Task.CompletedTask;
    }

    public async Task RemoveLabelAsync(string file, int index)
    {
        if (Store.ContainsKey(file) && Store[file].Count > index)
            Store[file].RemoveAt(index);
        await Task.CompletedTask;
    }

    public async Task ClearAsync()
    {
        Store.Clear();
        await Task.CompletedTask;
    }
}