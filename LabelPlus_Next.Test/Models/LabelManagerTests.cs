using LabelPlus_Next.Models;
using System.Collections.ObjectModel;

namespace LabelPlus_Next.Test.Models;

[TestClass]
[DoNotParallelize]
public class LabelManagerTests
{
    [TestMethod]
    public async Task RemoveAndUndo_Works()
    {
        var fm = new LabelFileManager();
        var img = "x.png";
        var current = new ObservableCollection<LabelItem>();

        await fm.StoreManager.AddFileAsync(img);
        await LabelManager.Instance.AddLabelAsync(fm, img, new LabelItem { Text = "A" });
        await LabelManager.Instance.AddLabelAsync(fm, img, new LabelItem { Text = "B" });

        // sync CurrentLabels view
        current.Clear();
        current.AddRange(fm.StoreManager.Store[img]);

        // remove selected "B"
        await LabelManager.Instance.RemoveSelectedAsync(fm, img, current, current.Last());
        Assert.AreEqual(1, fm.StoreManager.Store[img].Count);

        // undo remove -> count back to 2
        await LabelManager.Instance.UndoRemoveAsync(fm, img);
        Assert.AreEqual(2, fm.StoreManager.Store[img].Count);
    }

    [TestMethod]
    public async Task RemoveSelected_IgnoresInvalid()
    {
        var fm = new LabelFileManager();
        var img = "y.png";
        var current = new ObservableCollection<LabelItem>();
        await fm.StoreManager.AddFileAsync(img);
        await LabelManager.Instance.AddLabelAsync(fm, img, new LabelItem { Text = "A" });

        // selected not in collection -> no throw, no change
        await LabelManager.Instance.RemoveSelectedAsync(fm, img, current, new LabelItem { Text = "ghost" });
        Assert.AreEqual(1, fm.StoreManager.Store[img].Count);
    }
}

internal static class ObservableCollectionExtensions
{
    public static void AddRange<T>(this ObservableCollection<T> col, IEnumerable<T> items)
    {
        foreach (var i in items) col.Add(i);
    }
}
