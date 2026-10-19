using System;
using System.Reflection;
using LabelPlus_Next.Models;
using LabelPlus_Next.Services;
using LabelPlus_Next.ViewModels;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LabelPlus_Next.Test;

[TestClass]
[DoNotParallelize]
public class MainWindowHelperTests
{
    private static object? Call(string name, params object?[] args)
        => typeof(MainWindowViewModel).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, args);

    private static void ResetStore()
    {
        var field = typeof(MainWindowViewModel).GetField("LabelFileManager1", BindingFlags.NonPublic | BindingFlags.Static)!;
        if (field.GetValue(null) is LabelFileManager manager)
        {
            manager.StoreManager.Store.Clear();
            manager.StoreManager.ResetDirty();
        }
    }

    [TestCleanup]
    public void Cleanup() => ResetStore();

    [TestMethod]
    public void ComputeHash_IsStable()
    {
        var a = (string)Call("ComputeHash", "hello")!;
        var b = (string)Call("ComputeHash", "hello")!;
        Assert.AreEqual(a, b);
        Assert.AreEqual(64, a.Length);
        Assert.AreNotEqual(a, (string)Call("ComputeHash", "world")!);
    }

    [TestMethod]
    public void RefreshImagesList_EmptyStoreClearsSelection()
    {
        ResetStore();
        var vm = new MainWindowViewModel(new NoopFileDialogService()) { SelectedImageFile = "x.png" };
        vm.RefreshImagesList();
        Assert.IsNull(vm.SelectedImageFile);
        Assert.AreEqual(0, vm.ImageFileNames.Count);
    }

    [TestMethod]
    public void LocalizationApply_InvalidCultureFallsBack()
    {
        LocalizationService.Apply("not-a-culture-!!");
        Assert.IsNotNull(LocalizationService.CurrentCulture);
        LocalizationService.Apply("\0");
        Assert.IsNotNull(LocalizationService.CurrentCulture);
        LocalizationService.Apply("default");
    }
}
