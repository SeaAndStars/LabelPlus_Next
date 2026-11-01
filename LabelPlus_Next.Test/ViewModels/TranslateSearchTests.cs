using LabelPlus_Next.Models;
using LabelPlus_Next.Services;
using LabelPlus_Next.ViewModels;

namespace LabelPlus_Next.Test.ViewModels;

[TestClass]
[DoNotParallelize]
public class TranslateSearchTests
{
    private sealed class Fixture
    {
        public required TranslateViewModel Vm { get; init; }
        public required LabelFileManager Manager { get; init; }
        public required LabelItem A1 { get; init; }
        public required LabelItem A2 { get; init; }
        public required LabelItem B1 { get; init; }
    }

    private static Fixture CreateFixture()
    {
        var manager = new LabelFileManager();
        var a1 = new LabelItem { Text = "Hello world", Category = 1 };
        var a2 = new LabelItem { Text = "hello again", Category = 2 };
        var b1 = new LabelItem { Text = "HELLO hidden", Category = 1 };
        manager.StoreManager.Store["a.png"] = new List<LabelItem> { a1, a2 };
        manager.StoreManager.Store["b.png"] = new List<LabelItem> { b1 };

        var vm = new TranslateViewModel(new NoopFileDialogService(), new InlineUiDispatcher(), manager);
        vm.RefreshImagesList();
        return new Fixture { Vm = vm, Manager = manager, A1 = a1, A2 = a2, B1 = b1 };
    }

    [TestMethod]
    public void Search_FindsAcrossImagesAndNavigatesWithWrap()
    {
        var f = CreateFixture();
        var previews = 0;
        f.Vm.SearchPreviewRequested += (_, _) => previews++;

        f.Vm.SearchText = "hello";

        Assert.AreEqual(3, f.Vm.SearchMatchCount);
        Assert.AreSame(f.A1, f.Vm.SelectedLabel);
        Assert.AreEqual(0, f.Vm.SearchPreviewStart);
        Assert.AreEqual(5, f.Vm.SearchPreviewEnd);
        Assert.IsTrue(f.A1.IsSearchMatch);
        Assert.IsTrue(f.A2.IsSearchMatch);
        Assert.IsTrue(f.B1.IsSearchMatch);
        Assert.IsFalse(string.IsNullOrEmpty(f.Vm.SearchStatus));

        f.Vm.FindNextCommand.Execute(null);
        Assert.AreSame(f.A2, f.Vm.SelectedLabel);

        f.Vm.FindNextCommand.Execute(null);
        Assert.AreSame(f.B1, f.Vm.SelectedLabel);
        Assert.AreEqual("b.png", f.Vm.SelectedImageFile);

        f.Vm.FindNextCommand.Execute(null);
        Assert.AreSame(f.A1, f.Vm.SelectedLabel);

        f.Vm.FindPreviousCommand.Execute(null);
        Assert.AreSame(f.B1, f.Vm.SelectedLabel);
        Assert.IsTrue(previews >= 5);
    }

    [TestMethod]
    public void Search_RespectsMatchCaseAndReportsNoResult()
    {
        var f = CreateFixture();
        f.Vm.SearchText = "hello";
        Assert.AreEqual(3, f.Vm.SearchMatchCount);

        f.Vm.SearchMatchCase = true;
        Assert.AreEqual(1, f.Vm.SearchMatchCount);
        Assert.AreSame(f.A2, f.Vm.SelectedLabel);

        f.Vm.SearchMatchCase = false;
        f.Vm.SearchText = "zzz";
        Assert.AreEqual(0, f.Vm.SearchMatchCount);
        Assert.AreEqual(-1, f.Vm.SearchPreviewStart);
        Assert.AreEqual(-1, f.Vm.SearchPreviewEnd);
        Assert.IsFalse(f.A1.IsSearchMatch);
        Assert.IsFalse(string.IsNullOrEmpty(f.Vm.SearchStatus));

        f.Vm.FindNextCommand.Execute(null);
        f.Vm.FindPreviousCommand.Execute(null);
        f.Vm.ReplaceSearchMatchCommand.Execute(null);
        f.Vm.ReplaceAllSearchMatchesCommand.Execute(null);
        Assert.AreEqual(0, f.Vm.SearchMatchCount);
    }

    [TestMethod]
    public void Search_WithEmptyTermAndClose_ClearsState()
    {
        var f = CreateFixture();
        var previews = 0;
        f.Vm.SearchPreviewRequested += (_, _) => previews++;
        f.Vm.SearchText = "hello";
        Assert.IsTrue(f.A1.IsSearchMatch);

        f.Vm.CloseSearchCommand.Execute(null);
        Assert.IsTrue(previews > 0);
        Assert.IsFalse(f.Vm.IsSearchOpen);
        Assert.IsNull(f.Vm.SearchText);
        Assert.IsNull(f.Vm.SearchStatus);
        Assert.AreEqual(0, f.Vm.SearchMatchCount);
        Assert.AreEqual(-1, f.Vm.SearchPreviewStart);
        Assert.IsFalse(f.A1.IsSearchMatch);

        f.Vm.FindNextCommand.Execute(null);
        f.Vm.FindPreviousCommand.Execute(null);
        f.Vm.ReplaceSearchMatchCommand.Execute(null);
        f.Vm.ReplaceAllSearchMatchesCommand.Execute(null);
        Assert.IsNull(f.Vm.SearchStatus);
    }

    [TestMethod]
    public void Search_UsesStoreKeysWhenImageListIsEmpty()
    {
        var manager = new LabelFileManager();
        var label = new LabelItem { Text = "alpha beta", Category = 1 };
        manager.StoreManager.Store["x.png"] = new List<LabelItem> { label };
        var vm = new TranslateViewModel(new NoopFileDialogService(), new InlineUiDispatcher(), manager);

        vm.SearchText = "beta";

        Assert.AreEqual(1, vm.SearchMatchCount);
        Assert.AreEqual("x.png", vm.SelectedImageFile);
        Assert.AreSame(label, vm.SelectedLabel);
        Assert.AreEqual(6, vm.SearchPreviewStart);
    }

    [TestMethod]
    public void ReplaceCurrent_UpdatesLabelAndMovesToNextMatch()
    {
        var f = CreateFixture();
        f.Vm.SearchText = "hello";
        f.Vm.ReplaceText = "hi";

        f.Vm.ReplaceSearchMatchCommand.Execute(null);

        Assert.AreEqual("hi world", f.A1.Text);
        Assert.AreEqual(2, f.Vm.SearchMatchCount);
        Assert.AreSame(f.A2, f.Vm.SelectedLabel);
        Assert.IsTrue(f.Manager.StoreManager.IsDirty);
        Assert.IsFalse(string.IsNullOrEmpty(f.Vm.SearchStatus));
    }

    [TestMethod]
    public void ReplaceCurrent_WhenAnotherLabelIsSelected_LeavesCurrentTextAlone()
    {
        var f = CreateFixture();
        f.Vm.SearchText = "hello";
        f.Vm.ReplaceText = "hi";
        f.Vm.SelectedLabel = f.A2;
        var selectedText = f.A2.Text;

        f.Vm.ReplaceSearchMatchCommand.Execute(null);

        Assert.AreEqual("hi world", f.A1.Text);
        Assert.AreEqual(selectedText, f.Vm.CurrentText);
    }

    [TestMethod]
    public void ReplaceCurrent_IgnoresStaleMatchRange()
    {
        var f = CreateFixture();
        f.Vm.SearchText = "hello";
        f.A1.Text = string.Empty;

        f.Vm.ReplaceSearchMatchCommand.Execute(null);

        Assert.AreEqual(string.Empty, f.A1.Text);
    }

    [TestMethod]
    public void ReplaceAll_ReplacesEveryCaseInsensitiveMatch()
    {
        var f = CreateFixture();
        f.Vm.SearchText = "hello";
        f.Vm.ReplaceText = "hi";

        f.Vm.ReplaceAllSearchMatchesCommand.Execute(null);

        Assert.AreEqual("hi world", f.A1.Text);
        Assert.AreEqual("hi again", f.A2.Text);
        Assert.AreEqual("hi hidden", f.B1.Text);
        Assert.AreEqual(0, f.Vm.SearchMatchCount);
        Assert.IsTrue(f.Manager.StoreManager.IsDirty);
        Assert.IsFalse(string.IsNullOrEmpty(f.Vm.SearchStatus));
    }

    [TestMethod]
    public void ReplaceAll_HonorsMatchCaseAndNullSelection()
    {
        var f = CreateFixture();
        f.Vm.SearchText = "HELLO";
        f.Vm.SearchMatchCase = true;
        f.Vm.ReplaceText = "hi";
        f.Vm.SelectedLabel = null;

        f.Vm.ReplaceAllSearchMatchesCommand.Execute(null);

        Assert.AreEqual("Hello world", f.A1.Text);
        Assert.AreEqual("hello again", f.A2.Text);
        Assert.AreEqual("hi hidden", f.B1.Text);
    }

    [TestMethod]
    public void QuickInputItems_ExposeOriginalCharacters()
    {
        var f = CreateFixture();
        Assert.AreEqual(12, f.Vm.QuickInputItems.Count);
        Assert.AreEqual("啊", f.Vm.QuickInputItems[0].Text);
        Assert.AreEqual("A", f.Vm.QuickInputItems[0].Key);
        Assert.AreEqual("♪", f.Vm.QuickInputItems[^1].Text);
    }

    [TestMethod]
    public void LabelItem_SearchMatchNotifiesOnlyOnChange()
    {
        var item = new LabelItem();
        var changes = 0;
        item.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(LabelItem.IsSearchMatch)) changes++;
        };

        item.IsSearchMatch = true;
        item.IsSearchMatch = true;
        item.IsSearchMatch = false;

        Assert.AreEqual(2, changes);
    }
}
