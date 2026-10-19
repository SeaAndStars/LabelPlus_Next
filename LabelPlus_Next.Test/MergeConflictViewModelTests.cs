using System.Linq;
using LabelPlus_Next.Services;
using LabelPlus_Next.ViewModels;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LabelPlus_Next.Test;

[TestClass]
[DoNotParallelize]
public class MergeConflictViewModelTests
{
    [TestInitialize]
    public void Setup() => LocalizationService.Apply("en");

    [TestCleanup]
    public void Cleanup() => LocalizationService.Apply("default");

    [TestMethod]
    public void EqualTexts_HaveNoConflicts()
    {
        var vm = new MergeConflictViewModel("a\nb\n", "a\nb\n", "same.txt");

        Assert.AreEqual(0, vm.Conflicts.Count);
        Assert.AreEqual("No conflicts", vm.ConflictStatus);
        Assert.AreEqual(0, vm.CurrentStart);
        Assert.AreEqual(0, vm.CurrentLength);
        Assert.IsNull(vm.SelectedConflictItem);
        Assert.AreEqual("Remote", vm.RemoteLabel);
        Assert.AreEqual("Local", vm.LocalLabel);
        Assert.AreEqual("Save Conflict Merge", vm.Title);
    }

    [TestMethod]
    public void DifferentTexts_BuildConflictItemsAndStatus()
    {
        var vm = new MergeConflictViewModel("a\nb\nc", "a\nX\nc", "diff.txt");

        Assert.IsTrue(vm.Conflicts.Count >= 1);
        var item = vm.Conflicts.First();
        Assert.AreEqual(1, item.Index);
        Assert.AreEqual("b", item.Summary);
        Assert.AreEqual(2, item.RemoteLines);
        Assert.AreEqual(2, item.LocalLines);
        Assert.AreEqual(MergeConflictViewModel.ChoiceOption.None, item.Choice);
        StringAssert.Contains(vm.ConflictStatus, "Conflict 1/");
        StringAssert.Contains(vm.ConflictStatus, "selected 0/");
        StringAssert.Contains(vm.MergedText, "<<<<<<< REMOTE");
        Assert.IsNotNull(vm.SelectedConflictItem);
        Assert.AreEqual(0, vm.SelectedConflictIndex);
    }

    [TestMethod]
    public void Commands_ChooseRemoteAndLocalForAllConflicts()
    {
        var vm = new MergeConflictViewModel("a\nb\nc", "a\nX\nc", "diff.txt");

        vm.UseRemoteCommand.Execute(null);
        Assert.AreEqual(MergeConflictViewModel.ChoiceOption.Remote, vm.Conflicts[0].Choice);
        Assert.IsFalse(vm.MergedText.Contains("<<<<<<< REMOTE"));

        vm.UseLocalCommand.Execute(null);
        Assert.AreEqual(MergeConflictViewModel.ChoiceOption.Local, vm.Conflicts[0].Choice);

        vm.AcceptAllRemoteCommand.Execute(null);
        Assert.AreEqual(MergeConflictViewModel.ChoiceOption.Remote, vm.Conflicts[0].Choice);

        vm.AcceptAllLocalCommand.Execute(null);
        Assert.AreEqual(MergeConflictViewModel.ChoiceOption.Local, vm.Conflicts[0].Choice);
    }

    [TestMethod]
    public void Commands_ChooseCurrentAndNavigateMultipleConflicts()
    {
        var remote = "a\nb\nc\nd\ne";
        var local = "a\nX\nc\nY\ne";
        var vm = new MergeConflictViewModel(remote, local, "multi.txt");

        Assert.AreEqual(2, vm.Conflicts.Count);
        vm.AcceptCurrentRemoteCommand.Execute(null);
        Assert.AreEqual(MergeConflictViewModel.ChoiceOption.Remote, vm.Conflicts[0].Choice);

        vm.NextConflictCommand.Execute(null);
        Assert.AreEqual(1, vm.SelectedConflictIndex);
        vm.AcceptCurrentLocalCommand.Execute(null);
        Assert.AreEqual(MergeConflictViewModel.ChoiceOption.Local, vm.Conflicts[1].Choice);

        vm.PrevConflictCommand.Execute(null);
        Assert.AreEqual(0, vm.SelectedConflictIndex);

        vm.SelectedConflictIndex = 1;
        Assert.IsNotNull(vm.SelectedConflictItem);

        vm.BuildDiffCommand.Execute(null);
        Assert.AreEqual(2, vm.Conflicts.Count);
    }

    [TestMethod]
    public void ItemCommands_NotifyOwnerAndRebuild()
    {
        var vm = new MergeConflictViewModel("a\nb\nc", "a\nX\nc", "diff.txt");
        var item = vm.Conflicts[0];

        item.ChooseRemoteSelfCommand.Execute(null);
        Assert.AreEqual(MergeConflictViewModel.ChoiceOption.Remote, item.Choice);
        item.ChooseLocalSelfCommand.Execute(null);
        Assert.AreEqual(MergeConflictViewModel.ChoiceOption.Local, item.Choice);

        vm.ChooseRemoteCommand.Execute(item);
        Assert.AreEqual(MergeConflictViewModel.ChoiceOption.Remote, item.Choice);
        vm.ChooseLocalCommand.Execute(item);
        Assert.AreEqual(MergeConflictViewModel.ChoiceOption.Local, item.Choice);

        vm.ChooseRemoteCommand.Execute(null);
        vm.ChooseLocalCommand.Execute(null);
    }

    [TestMethod]
    public void SaveAndCancel_RaiseRequestClose()
    {
        var vm = new MergeConflictViewModel("a\nb\nc", "a\nX\nc", "diff.txt");
        string? closed = null;
        vm.RequestClose += (_, text) => closed = text;

        vm.SaveMergedCommand.Execute(null);
        Assert.IsNotNull(closed);
        StringAssert.Contains(closed!, "<<<<<<< REMOTE");

        vm.CancelCommand.Execute(null);
        Assert.IsNull(closed);
    }

    [TestMethod]
    public void ForceConflict_WhenTextsDifferWithoutConflictMarkers()
    {
        var vm = new MergeConflictViewModel("only remote", "only local", "force.txt");

        Assert.IsTrue(vm.Conflicts.Count >= 1);
        StringAssert.Contains(vm.MergedText, "<<<<<<< REMOTE");
        Assert.IsTrue(vm.CurrentLength > 0);
    }

    [TestMethod]
    public void EmptyTexts_AreHandled()
    {
        var vm = new MergeConflictViewModel("", "", "empty.txt");
        Assert.AreEqual(0, vm.Conflicts.Count);
        Assert.AreEqual("No conflicts", vm.ConflictStatus);

        var diff = new MergeConflictViewModel("", "x", "one.txt");
        Assert.AreEqual(1, diff.Conflicts.Count);
    }
}
