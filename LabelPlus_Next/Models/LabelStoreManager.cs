using System.Collections.Generic;
using System.Threading.Tasks;
using NLog;

namespace LabelPlus_Next.Models;

public class LabelStoreManager
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    public Dictionary<string, List<LabelItem>> Store { get; } = new();

    // Track dirty state
    public bool IsDirty { get; private set; }

    public void ResetDirty()
    {
        IsDirty = false;
        Logger.Debug("Dirty reset.");
    }
    public void TouchDirty()
    {
        IsDirty = true;
        Logger.Debug("Dirty touched.");
    }

    public async Task AddFileAsync(string file)
    {
        if (!Store.ContainsKey(file))
        {
            Store[file] = new List<LabelItem>();
            Logger.Info("Add file entry: {file}", file);
        }
        IsDirty = true;
        await Task.CompletedTask;
    }

    public async Task AddLabelAsync(string file, LabelItem item)
    {
        if (!Store.ContainsKey(file))
        {
            Store[file] = new List<LabelItem>();
            Logger.Info("Add file entry (on label add): {file}", file);
        }
        Store[file].Add(item);
        Logger.Debug("Add label: {file} -> count={count}", file, Store[file].Count);
        IsDirty = true;
        await Task.CompletedTask;
    }

    public async Task RemoveFileAsync(string file)
    {
        Store.Remove(file);
        Logger.Info("Remove file entry: {file}", file);
        IsDirty = true;
        await Task.CompletedTask;
    }

    public async Task RemoveLabelAsync(string file, int index)
    {
        if (Store.ContainsKey(file) && Store[file].Count > index)
        {
            Store[file].RemoveAt(index);
            Logger.Debug("Remove label: {file} -> count={count}", file, Store[file].Count);
            IsDirty = true;
        }
        await Task.CompletedTask;
    }

    public async Task ClearAsync()
    {
        Store.Clear();
        Logger.Warn("Store cleared");
        IsDirty = true;
        await Task.CompletedTask;
    }
}