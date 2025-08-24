using Avalonia.Controls;
using Avalonia.Interactivity;
using LabelPlus_Next.ViewModels;
using System.Linq;
using System;
using System.IO;
using System.Collections.Generic;
using NLog;
using Ursa.Controls;

namespace LabelPlus_Next.Views.Pages;

public partial class ImageManager : UrsaWindow
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    public ImageManager()
    {
        InitializeComponent();
        this.Opened += OnOpened;
    }

    private ImageManagerViewModel? VM => DataContext as ImageManagerViewModel;

    private void OnOpened(object? sender, EventArgs e)
    {
        if (Owner is not Window owner) return;
        if (owner.DataContext is not MainWindowViewModel mvm) return;
        if (VM is null) return;
        Logger.Info("ImageManager opened for directory of translation: {file}", mvm.OpenTranslationFilePath);

        // Load included list from current translation file store keys
        VM.FileList.Clear();
        foreach (var name in mvm.GetIncludedImageFiles())
            VM.FileList.Add(name);

        // Scan directory for all images and diff
        var transPath = mvm.OpenTranslationFilePath;
        if (string.IsNullOrEmpty(transPath)) return;
        var dir = Path.GetDirectoryName(transPath)!;
        var all = new List<string>();
        string[] patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.bmp", "*.gif", "*.webp" };
        foreach (var pat in patterns)
        {
            try
            {
                foreach (var f in Directory.GetFiles(dir, pat, SearchOption.TopDirectoryOnly))
                {
                    var rel = Path.GetFileName(f);
                    if (!string.IsNullOrEmpty(rel)) all.Add(rel);
                }
            }
            catch (Exception ex) { Logger.Warn(ex, "Failed to enumerate files with pattern {pat}", pat); }
        }
        // Put those not in included to left list (ignored)
        VM.FileFolderList.Clear();
        var included = VM.FileList.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var f in all.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!included.Contains(f)) VM.FileFolderList.Add(f);
        }
        Logger.Info("ImageManager loaded: included={included}, ignored={ignored}", VM.FileList.Count, VM.FileFolderList.Count);
    }

    private void SelectOneFile(object? sender, RoutedEventArgs e)
    {
        if (VM?.SelectedFolerFile is string item)
        {
            VM.FileFolderList.Remove(item);
            if (!VM.FileList.Contains(item)) VM.FileList.Add(item);
        }
    }

    private void SelectAllFile(object? sender, RoutedEventArgs e)
    {
        if (VM is null) return;
        foreach (var item in VM.FileFolderList.ToList())
        {
            VM.FileFolderList.Remove(item);
            if (!VM.FileList.Contains(item)) VM.FileList.Add(item);
        }
    }

    private void UnselectOneFile(object? sender, RoutedEventArgs e)
    {
        if (VM?.SelectedFile is string item)
        {
            VM.FileList.Remove(item);
            if (!VM.FileFolderList.Contains(item)) VM.FileFolderList.Add(item);
        }
    }

    private void UnselectAllFile(object? sender, RoutedEventArgs e)
    {
        if (VM is null) return;
        foreach (var item in VM.FileList.ToList())
        {
            VM.FileList.Remove(item);
            if (!VM.FileFolderList.Contains(item)) VM.FileFolderList.Add(item);
        }
    }

    private async void OnOkClick(object? sender, RoutedEventArgs e)
    {
        if (Owner is not Window owner) { Close(false); return; }
        if (owner.DataContext is not MainWindowViewModel mvm) { Close(false); return; }
        if (VM is null) { Close(false); return; }

        var before = mvm.GetIncludedImageFiles().ToHashSet(StringComparer.OrdinalIgnoreCase);
        var after = VM.FileList.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var toRemove = before.Except(after, StringComparer.OrdinalIgnoreCase).ToList();
        var toAdd = after.Except(before, StringComparer.OrdinalIgnoreCase).ToList();

        Logger.Info("ImageManager confirm: toAdd={add}, toRemove={remove}", toAdd.Count, toRemove.Count);
        foreach (var f in toRemove)
        {
            await mvm.RemoveImageFileAsync(f);
        }
        foreach (var f in toAdd)
        {
            await mvm.AddImageFileAsync(f);
        }
        mvm.RefreshImagesList();
        Close(true);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Logger.Info("ImageManager canceled");
        Close(false);
    }
}