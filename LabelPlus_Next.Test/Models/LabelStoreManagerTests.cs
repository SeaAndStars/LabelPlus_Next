using LabelPlus_Next.Models;

namespace LabelPlus_Next.Test.Models;

[TestClass]
[DoNotParallelize]
public class LabelStoreManagerTests
{
    [TestMethod]
    public async Task AddRemove_File_Label_DirtyState()
    {
        var m = new LabelStoreManager();
        Assert.IsFalse(m.IsDirty);

        await m.AddFileAsync("a.png");
        Assert.IsTrue(m.IsDirty);
        Assert.IsTrue(m.Store.ContainsKey("a.png"));

        m.ResetDirty();
        Assert.IsFalse(m.IsDirty);

        await m.AddLabelAsync("a.png", new LabelItem { Text = "t", XPercent = 0.1f, YPercent = 0.2f, Category = 1 });
        Assert.IsTrue(m.IsDirty);
        Assert.AreEqual(1, m.Store["a.png"].Count);

        await m.RemoveLabelAsync("a.png", 5); // out of range, should not throw
        Assert.AreEqual(1, m.Store["a.png"].Count);

        await m.RemoveLabelAsync("a.png", 0);
        Assert.AreEqual(0, m.Store["a.png"].Count);

        await m.RemoveFileAsync("a.png");
        Assert.IsFalse(m.Store.ContainsKey("a.png"));

        await m.ClearAsync();
        Assert.AreEqual(0, m.Store.Count);
        Assert.IsTrue(m.IsDirty);
    }
}
