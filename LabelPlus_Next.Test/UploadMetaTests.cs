using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using LabelPlus_Next.Models;
using LabelPlus_Next.Services;
using LabelPlus_Next.ViewModels;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LabelPlus_Next.Test;

[TestClass]
[DoNotParallelize]
public class UploadMetaTests
{
    private sealed class MemorySettings : ISettingsService
    {
        public Task<AppSettings> LoadAsync(CancellationToken ct = default) => Task.FromResult(new AppSettings());
        public Task SaveAsync(AppSettings settings, CancellationToken ct = default) => Task.CompletedTask;
    }

    private static object? Instance(UploadViewModel vm, string name, params object?[] args)
        => typeof(UploadViewModel).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(vm, args);


    [TestMethod]
    public async Task OpenSettingsCommand_RaisesEvent()
    {
        var vm = new UploadViewModel(new MemorySettings(), new NoopFileDialogService(), false);
        Assert.IsFalse(vm.HasUploadProgress);
        typeof(UploadViewModel).GetField("Logger", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null);
        var raised = 0;
        vm.OpenSettingsRequested += (_, _) => raised++;
        await vm.OpenSettingsCommand.ExecuteAsync(null);
        Assert.AreEqual(1, raised);
    }
    [TestMethod]
    public void BuildMetaTree_CreatesRootsRowsAndCommands()
    {
        var vm = new UploadViewModel(new MemorySettings(), new NoopFileDialogService(), false);
        vm.PendingEpisodes.Add(new EpisodeEntry
        {
            Number = 3,
            Status = "翻译",
            LocalFiles = new List<string> { "/tmp/a.png", "/tmp/b.psd" },
            Owner = "owner",
            Tags = new List<string> { "tag1", "tag2" },
            Notes = "notes"
        });
        vm.PendingEpisodes.Add(new EpisodeEntry { Number = 0, IsSpecial = true, Status = "立项" });
        vm.PendingEpisodes.Add(new EpisodeEntry { Number = 5, IsVolume = true, RangeDisplay = "5~7" });
        vm.PendingEpisodes.Add(new EpisodeEntry { Number = 0, IsMisc = true });

        Instance(vm, "BuildMetaTree");

        Assert.AreEqual(4, vm.MetaRows.Count(r => r.Ref is not null));
        Assert.IsTrue(vm.MetaRows.Count > 4);
        var normal = vm.MetaRows.First(r => r.Ref?.Number == 3 && !r.IsFile);
        Assert.AreEqual(2, normal.Children.Count);
        Assert.IsTrue(normal.Children.All(c => c.IsFile));
        Assert.AreEqual("tag1,tag2", normal.Tags);
        Assert.IsNotNull(normal.ToggleCommand);
        Assert.AreEqual(0, normal.Depth);

        var expanded = vm.MetaRows.Count;
        vm.ToggleMetaNodeCommand.Execute(normal);
        Assert.IsTrue(vm.MetaRows.Count < expanded);
        vm.ToggleMetaNodeCommand.Execute(normal);
        Assert.AreEqual(expanded, vm.MetaRows.Count);
        vm.ToggleMetaNodeCommand.Execute(null);
    }

    [TestMethod]
    public void CreateMetaRoot_HandlesSpecialVolumeMiscAndDisplay()
    {
        var vm = new UploadViewModel(new MemorySettings(), new NoopFileDialogService(), false);

        var special = (MetaNode)Instance(vm, "CreateMetaRoot", new EpisodeEntry { IsSpecial = true, Number = 0 })!;
        Assert.AreEqual("番外", special.EpisodeDisplay);
        Assert.AreEqual("番外", special.Name);

        var volume = (MetaNode)Instance(vm, "CreateMetaRoot", new EpisodeEntry { IsVolume = true, Number = 4, RangeDisplay = "4 到 6" })!;
        Assert.AreEqual("04(卷)", volume.EpisodeDisplay);
        Assert.AreEqual("第4-6卷", volume.Name);

        var misc = (MetaNode)Instance(vm, "CreateMetaRoot", new EpisodeEntry { IsMisc = true, Number = 0 })!;
        Assert.AreEqual("杂项", misc.Name);

        var display = (MetaNode)Instance(vm, "CreateMetaRoot", new EpisodeEntry { Number = 8, Display = "08" })!;
        Assert.AreEqual("08", display.EpisodeDisplay);
        Assert.AreEqual("第8话", display.Name);
        Assert.AreEqual("", display.Tags);
    }
}
