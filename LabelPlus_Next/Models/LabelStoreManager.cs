using System.Collections.Generic;
using System.Threading.Tasks;

namespace LabelPlus_Next.Models;

public class LabelStoreManager
{
    public Dictionary<string, List<LabelItem>> Store { get; } = new();

    // Track dirty state
    public bool IsDirty { get; private set; }

    public void ResetDirty() => IsDirty = false;
    public void TouchDirty() => IsDirty = true;

    public async Task AddFileAsync(string file)
    {
        if (!Store.ContainsKey(file))
            Store[file] = new List<LabelItem>();
        IsDirty = true;
        await Task.CompletedTask;
    }

    public async Task AddLabelAsync(string file, LabelItem item)
    {
        if (!Store.ContainsKey(file))
            Store[file] = new List<LabelItem>();
        Store[file].Add(item);
        IsDirty = true;
        await Task.CompletedTask;
    }

    public async Task RemoveFileAsync(string file)
    {
        Store.Remove(file);
        IsDirty = true;
        await Task.CompletedTask;
    }

    public async Task RemoveLabelAsync(string file, int index)
    {
        if (Store.ContainsKey(file) && Store[file].Count > index)
        {
            Store[file].RemoveAt(index);
            IsDirty = true;
        }
        await Task.CompletedTask;
    }

    public async Task ClearAsync()
    {
        Store.Clear();
        IsDirty = true;
        await Task.CompletedTask;
    }
}