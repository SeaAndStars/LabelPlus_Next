using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace LabelPlus_Next.Models
{
    // Centralized label operations and per-image undo stack coordination
    public class LabelManager
    {
        public static readonly LabelManager Instance = new();
        private LabelManager() { }

        private readonly Dictionary<string, Stack<(LabelItem item, int index)>> _undoStacks = new();
        private Stack<(LabelItem item, int index)> GetStack(string imageFile)
        {
            if (!_undoStacks.TryGetValue(imageFile, out var s))
            {
                s = new Stack<(LabelItem, int)>();
                _undoStacks[imageFile] = s;
            }
            return s;
        }

        public async Task AddLabelAsync(LabelFileManager fileManager, string imageFile, LabelItem item)
        {
            await fileManager.StoreManager.AddLabelAsync(imageFile, item);
        }

        public async Task RemoveSelectedAsync(LabelFileManager fileManager, string imageFile,
            ObservableCollection<LabelItem> currentLabels, LabelItem? selected)
        {
            if (selected is null) return;
            var idx = currentLabels.IndexOf(selected);
            if (idx < 0) return;
            GetStack(imageFile).Push((selected, idx));
            await fileManager.StoreManager.RemoveLabelAsync(imageFile, idx);
        }

        public async Task UndoRemoveAsync(LabelFileManager fileManager, string imageFile)
        {
            var stack = GetStack(imageFile);
            if (stack.Count == 0) return;
            var (label, index) = stack.Pop();
            if (!fileManager.StoreManager.Store.ContainsKey(imageFile))
                fileManager.StoreManager.Store[imageFile] = new System.Collections.Generic.List<LabelItem>();
            var list = fileManager.StoreManager.Store[imageFile];
            if (index > list.Count) index = list.Count;
            list.Insert(index, label);
            await Task.CompletedTask;
        }
    }
}
