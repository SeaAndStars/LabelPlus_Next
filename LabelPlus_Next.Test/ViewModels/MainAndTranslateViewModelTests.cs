using LabelPlus_Next.ViewModels;
using LabelPlus_Next.Models;

namespace LabelPlus_Next.Test.ViewModels;

[TestClass]
public class MainAndTranslateViewModelTests
{
    [TestMethod]
    public void MainWindowViewModel_InitialState()
    {
        var vm = new MainWindowViewModel();
        Assert.IsNotNull(vm.ImageFileNames);
        Assert.AreEqual(0, vm.ImageFileNames.Count);
        Assert.IsNull(vm.SelectedImageFile);
        Assert.AreEqual("default", vm.SelectedLang);
        Assert.IsFalse(vm.HasUnsavedChanges);
    }

    [TestMethod]
    public void TranslateViewModel_InitialState()
    {
        var vm = new TranslateViewModel();
        Assert.IsNotNull(vm.ImageFileNames);
        Assert.AreEqual(0, vm.ImageFileNames.Count);
        Assert.IsNull(vm.SelectedImageFile);
        Assert.AreEqual("default", vm.SelectedLang);
        Assert.IsFalse(vm.HasUnsavedChanges);
    }

    [TestMethod]
    public async Task TranslateViewModel_LabelAddRemoveUndo()
    {
        var vm = new TranslateViewModel();

        // prepare image entry and select it
        await vm.AddImageFileAsync("b.png");
        vm.RefreshImagesList();
        Assert.IsTrue(vm.ImageFileNames.Contains("b.png"));
        vm.SelectedImageFile = "b.png";

        await vm.AddLabelAtAsync(0.2f, 0.8f, 1);
        Assert.AreEqual(1, vm.CurrentLabels.Count);

        await vm.RemoveLabelCommand();
        Assert.AreEqual(0, vm.CurrentLabels.Count);
        await vm.UndoRemoveLabelCommand();
        Assert.AreEqual(1, vm.CurrentLabels.Count);
    }

    [TestMethod]
    public async Task MainWindowViewModel_LabelAddRemoveUndo()
    {
        var vm = new MainWindowViewModel();

        // prepare image entry and select it
        await vm.AddImageFileAsync("a.png");
        vm.RefreshImagesList();
        Assert.IsTrue(vm.ImageFileNames.Contains("a.png"));
        vm.SelectedImageFile = "a.png";

        // add a label via method
        await vm.AddLabelAtAsync(0.3f, 0.6f, 1);
        Assert.AreEqual(1, vm.CurrentLabels.Count);

        // remove and undo via commands
        await vm.RemoveLabelCommand();
        Assert.AreEqual(0, vm.CurrentLabels.Count);
        await vm.UndoRemoveLabelCommand();
        Assert.AreEqual(1, vm.CurrentLabels.Count);

        // set category helper
        vm.SelectedLabel = vm.CurrentLabels[0];
        vm.SetSelectedCategory(2);
        Assert.AreEqual(2, vm.CurrentLabels[0].Category);
        Assert.AreEqual("框外", vm.CurrentLabels[0].CategoryString);
    }
}
