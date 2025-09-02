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
        // Warn: if has labels, we still remove here; higher level should confirm before calling
        if (Store.Remove(file))
        {
            Logger.Info("Remove file entry: {file}", file);
            IsDirty = true;
        }
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

    public bool HasLabels(string file)
    {
        return Store.TryGetValue(file, out var list) && list is { Count: > 0 };
    }

    public async Task ClearAsync()
    {
        Store.Clear();
        Logger.Warn("Store cleared");
        IsDirty = true;
        await Task.CompletedTask;
    }

    // 新增：移动同一文件中的标签顺序
    public void MoveLabel(string file, int oldIndex, int newIndex)
    {
        if (!Store.TryGetValue(file, out var list)) return;
        if (list.Count == 0) return;
        if (oldIndex < 0 || oldIndex >= list.Count) return;
        // 允许插入到末尾
        if (newIndex < 0) newIndex = 0;
        if (newIndex > list.Count) newIndex = list.Count;
        if (oldIndex == newIndex || oldIndex == list.Count - 1 && newIndex == list.Count) return;

        var item = list[oldIndex];
        list.RemoveAt(oldIndex);
        // 移除后，如果向后移动，目标索引需要减一
        if (newIndex > oldIndex) newIndex--;
        if (newIndex < 0) newIndex = 0;
        if (newIndex > list.Count) newIndex = list.Count;
        list.Insert(newIndex, item);
        Logger.Info("Reorder label in {file}: {oldIndex} -> {newIndex}", file, oldIndex, newIndex);
        TouchDirty();
    }
}
