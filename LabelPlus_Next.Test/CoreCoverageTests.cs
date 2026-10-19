using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LabelPlus_Next.Models;
using LabelPlus_Next.Services;
using LabelPlus_Next.Shared;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LabelPlus_Next.Test;

[TestClass]
[DoNotParallelize]
public class CoreCoverageTests
{
    private string _dir = null!;

    [TestInitialize]
    public void Setup()
    {
        _dir = Path.Combine(Path.GetTempPath(), "lp-core-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        LocalizationService.Apply("en");
    }

    [TestCleanup]
    public void Cleanup()
    {
        LocalizationService.Apply("default");
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); } catch { }
    }

    [TestMethod]
    public async Task TranslationFile_RoundTripsThroughWriterReaderAndManager()
    {
        var path = Path.Combine(_dir, "translation.txt");
        var header = await LabelFileHeaderManager.GenerateHeaderAsync(new[] { "1", "0" }, new List<string> { "框内", "框外" }, "Comment");
        var store = new Dictionary<string, List<LabelItem>>
        {
            ["page01.png"] = new()
            {
                new LabelItem(0.5f, 0.25f, "hello", 1) { Index = 1 },
                new LabelItem(0.1f, 0.2f, "world", 2) { Index = 2 }
            }
        };
        var writer = new LabelFileWriter();
        await writer.WriteAsync(path, header, store);

        var reader = new LabelFileReader();
        var (readHeader, readStore) = await reader.ReadAsync(path);
        Assert.IsTrue(readHeader.Contains("Comment"));
        Assert.AreEqual(2, readStore["page01.png"]!.Count);
        Assert.AreEqual("hello", readStore["page01.png"]![0].Text);

        var manager = new LabelFileManager();
        await manager.LoadAsync(path);
        Assert.AreEqual("1", manager.FileHead[0]);
        Assert.AreEqual("0", manager.FileHead[1]);
        CollectionAssert.AreEqual(new[] { "框内", "框外" }, manager.GroupStringList);
        Assert.AreEqual("Comment", manager.Comment);
        Assert.AreEqual(2, manager.StoreManager.Store["page01.png"].Count);
        Assert.IsFalse(manager.StoreManager.IsDirty);

        manager.StoreManager.Store["page02.png"] = new List<LabelItem>
        {
            new(0.2f, 0.3f, "second", 1)
        };
        manager.UpdateHeader(new List<string> { "inner" }, "updated");
        Assert.IsTrue(manager.StoreManager.IsDirty);
        await manager.SaveAsync(path);

        var reloaded = new LabelFileManager();
        await reloaded.LoadAsync(path);
        Assert.AreEqual("updated", reloaded.Comment);
        Assert.AreEqual(2, reloaded.StoreManager.Store.Count);
    }

    [TestMethod]
    public async Task HeaderManager_ParsesAndRejectsInvalidHeader()
    {
        var header = await LabelFileHeaderManager.GenerateHeaderAsync(new[] { "2", "0" }, new List<string> { "a", "b" }, "line1\nline2");
        var (head, groups, comment) = await LabelFileHeaderManager.ParseHeaderAsync(header);
        CollectionAssert.AreEqual(new[] { "2", "0" }, head);
        CollectionAssert.AreEqual(new[] { "a", "b" }, groups);
        Assert.AreEqual("line1\nline2", comment);

        await Assert.ThrowsExceptionAsync<Exception>(() => LabelFileHeaderManager.ParseHeaderAsync("bad"));
    }

    [TestMethod]
    public async Task Reader_ThrowsLocalizedErrorForMissingFile()
    {
        var reader = new LabelFileReader();
        var ex = await Assert.ThrowsExceptionAsync<FileNotFoundException>(() =>
            reader.ReadAsync(Path.Combine(_dir, "missing.txt")));
        StringAssert.Contains(ex.Message, "File not found");
    }

    [TestMethod]
    public async Task LabelItem_ValidatesCategoryAndRaisesPropertyChanges()
    {
        Assert.ThrowsException<Exception>(() => new LabelItem(0, 0, "x", 0));
        Assert.ThrowsException<Exception>(() => new LabelItem(0, 0, "x", 10));

        var item = new LabelItem(0.1f, 0.2f, "text", 1) { Index = 2 };
        var changed = new List<string?>();
        item.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        item.Text = "new";
        item.Index = 3;
        item.CategoryString = "框内";
        item.NotifyCategoryDisplayChanged();
        CollectionAssert.Contains(changed, nameof(LabelItem.Text));
        CollectionAssert.Contains(changed, nameof(LabelItem.Index));
        CollectionAssert.Contains(changed, nameof(LabelItem.CategoryString));
        Assert.AreEqual(0.1f, item.XPercent);
        Assert.AreEqual(0.2f, item.YPercent);
        Assert.AreEqual(1, item.Category);
    }

    [TestMethod]
    public async Task LabelStoreManager_CoversAddRemoveMoveAndDirty()
    {
        var store = new LabelStoreManager();
        Assert.IsFalse(store.IsDirty);
        await store.AddFileAsync("a.png");
        await store.AddFileAsync("a.png");
        Assert.IsTrue(store.IsDirty);
        Assert.AreEqual(1, store.Store.Count);

        var item1 = new LabelItem(0, 0, "1", 1);
        var item2 = new LabelItem(0, 0, "2", 1);
        var item3 = new LabelItem(0, 0, "3", 1);
        await store.AddLabelAsync("a.png", item1);
        await store.AddLabelAsync("a.png", item2);
        await store.AddLabelAsync("a.png", item3);
        Assert.IsTrue(store.HasLabels("a.png"));
        Assert.IsFalse(store.HasLabels("missing.png"));

        store.MoveLabel("a.png", 0, 2);
        Assert.AreEqual("2", store.Store["a.png"][0].Text);
        store.MoveLabel("a.png", 0, 0);
        store.MoveLabel("a.png", -1, 1);
        store.MoveLabel("a.png", 0, 99);
        store.MoveLabel("missing.png", 0, 1);

        await store.RemoveLabelAsync("a.png", 0);
        await store.RemoveLabelAsync("a.png", 99);
        await store.RemoveFileAsync("a.png");
        Assert.IsFalse(store.Store.ContainsKey("a.png"));
        await store.ClearAsync();
        store.ResetDirty();
        Assert.IsFalse(store.IsDirty);
    }

    [TestMethod]
    public void LanguageOption_ResolvesDisplayNames()
    {
        LocalizationService.Apply("en");
        Assert.AreEqual("System default", new LanguageOption("default").DisplayName);
        Assert.AreEqual("English", new LanguageOption("en", "English").DisplayName);
        Assert.AreEqual("en", LocalizationService.Resolve("EN").Code);
        Assert.AreEqual("default", LocalizationService.Resolve(null).Code);
        Assert.AreEqual("default", LocalizationService.Resolve("unknown").Code);
        Assert.AreEqual(4, LocalizationService.Options.Count);

        var raised = 0;
        EventHandler handler = (_, _) => raised++;
        LocalizationService.CultureChanged += handler;
        try { LocalizationService.Apply("zh-hant-tw"); }
        finally { LocalizationService.CultureChanged -= handler; }
        Assert.AreEqual(1, raised);
        Assert.AreEqual("zh-Hant-TW", LocalizationService.CurrentCulture.Name);
        LocalizationService.Apply("not-a-culture");
        Assert.IsNotNull(LocalizationService.CurrentCulture);
    }

    [TestMethod]
    public void ManifestV1_RoundTripsJson()
    {
        var manifest = new ManifestV1
        {
            Schema = "v1",
            GeneratedAt = new DateTimeOffset(2026, 2, 3, 4, 5, 6, TimeSpan.Zero),
            Projects =
            {
                ["app"] = new ProjectReleases
                {
                    Latest = "1.0.0",
                    Releases =
                    {
                        new ReleaseItemV1
                        {
                            Version = "1.0.0",
                            Url = "https://example.invalid/a.zip",
                            Time = DateTimeOffset.UtcNow,
                            Notes = "notes",
                            Files = new List<ReleaseFileV1>
                            {
                                new() { Name = "a.zip", Url = "https://example.invalid/a.zip", Sha256 = "abc", Size = 1 }
                            }
                        }
                    }
                }
            }
        };

        var json = JsonSerializer.Serialize(manifest);
        var round = JsonSerializer.Deserialize<ManifestV1>(json)!;
        Assert.AreEqual("v1", round.Schema);
        Assert.AreEqual("1.0.0", round.Projects["app"].Latest);
        Assert.AreEqual("notes", round.Projects["app"].Releases[0].Notes);
        Assert.AreEqual(1, round.Projects["app"].Releases[0].Files![0].Size);
    }

    [TestMethod]
    public void CollaborationSession_ComputesRemoteDirectory()
    {
        var session = new CollaborationSession
        {
            BaseUrl = "https://example.invalid",
            Token = "t",
            RemoteTranslatePath = "/proj/ch1/translation.txt",
            Username = "u",
            LastRemoteHash = "hash"
        };
        Assert.AreEqual("/proj/ch1", session.RemoteDirectory);
        session.RemoteTranslatePath = "translation.txt";
        Assert.AreEqual("/", session.RemoteDirectory);
        Assert.AreEqual("hash", session.LastRemoteHash);
        Assert.AreEqual("u", session.Username);
    }

    [TestMethod]
    public async Task NoopFileDialogService_ReturnsEmptyResults()
    {
        var service = new NoopFileDialogService();
        Assert.AreEqual(0, (await service.ChooseImagesAsync(_dir))!.Count);
        Assert.IsNull(await service.OpenTranslationFileAsync());
        Assert.IsNull(await service.PickFolderAsync("title"));
        Assert.AreEqual(0, (await service.PickFoldersAsync("title"))!.Count);
        Assert.AreEqual(0, (await service.PickFilesAsync("title"))!.Count);
        Assert.IsNull(await service.SaveAsTranslationFileAsync());
        await service.ShowMessageAsync("message");
    }

    [TestMethod]
    public void TranslationFileUtils_BuildsInitialContentAndAvoidsConflicts()
    {
        var content = TranslationFileUtils.BuildInitialContent(new[] { "a.png", "b.png" });
        StringAssert.Contains(content, "1,0");
        StringAssert.Contains(content, ">>>>>>>>[a.png]<<<<<<<<");
        StringAssert.Contains(content, ">>>>>>>>[b.png]<<<<<<<");

        var path = Path.Combine(_dir, "translation.txt");
        Assert.AreEqual(path, TranslationFileUtils.GetNonConflictPath(_dir, "translation.txt"));
        File.WriteAllText(path, "x");
        Assert.AreEqual(Path.Combine(_dir, "translation (1).txt"), TranslationFileUtils.GetNonConflictPath(_dir, "translation.txt"));
        File.WriteAllText(Path.Combine(_dir, "translation (1).txt"), "x");
        Assert.AreEqual(Path.Combine(_dir, "translation (2).txt"), TranslationFileUtils.GetNonConflictPath(_dir, "translation.txt"));
    }

    [TestMethod]
    public async Task UpdateFiles_HashesValidatesAndCleans()
    {
        var file = Path.Combine(_dir, "a.bin");
        await File.WriteAllTextAsync(file, "abc");
        var hash = await UpdateFiles.ComputeSha256Async(file);
        Assert.AreEqual("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", hash);
        Assert.IsTrue(await UpdateFiles.MatchesSha256Async(file, null));
        Assert.IsTrue(await UpdateFiles.MatchesSha256Async(file, hash.ToUpperInvariant()));
        Assert.IsFalse(await UpdateFiles.MatchesSha256Async(file, "bad"));
        Assert.IsFalse(UpdateFiles.IsValidZip(file));

        var zip = Path.Combine(_dir, "a.zip");
        var zipDir = Path.Combine(_dir, "zipdir");
        Directory.CreateDirectory(zipDir);
        await File.WriteAllTextAsync(Path.Combine(zipDir, "x.txt"), "x");
        ZipFile.CreateFromDirectory(zipDir, zip);
        Assert.IsTrue(UpdateFiles.IsValidZip(zip));

        UpdateFiles.TryDelete(file);
        Assert.IsFalse(File.Exists(file));
        UpdateFiles.TryDelete(file); // missing is a no-op
        UpdateFiles.TryDeleteDirectory(zipDir);
        Assert.IsFalse(Directory.Exists(zipDir));
        UpdateFiles.TryDeleteDirectory(zipDir);
        var temp = UpdateFiles.NewTempFile(".tmp");
        Assert.IsTrue(temp.EndsWith(".tmp"));
        StringAssert.Contains(temp, "lpn-");
    }

    [TestMethod]
    public void TransferProgress_ExposesValues()
    {
        var p = new TransferProgress(10, 20, 1.5);
        Assert.AreEqual(10, p.Received);
        Assert.AreEqual(20, p.Total);
        Assert.AreEqual(1.5, p.BytesPerSecond);
    }

    [TestMethod]
    public void UiText_HandlesUnknownAndMalformedFormats()
    {
        LocalizationService.Apply("en");
        Assert.AreEqual("unknown text", UiText.Tr("unknown text"));
        Assert.AreEqual("broken {0} {1}", UiText.Tr("broken {0} {1}", 1));
        Assert.AreEqual("Notice", UiText.Tr("提示"));
    }
}
