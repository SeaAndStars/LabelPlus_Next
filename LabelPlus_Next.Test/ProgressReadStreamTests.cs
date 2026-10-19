using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using LabelPlus_Next.Shared;
using LabelPlus_Next.Tools.ViewModels;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LabelPlus_Next.Test;

[TestClass]
public class ProgressReadStreamTests
{
    private static Stream Create(Stream inner, long size, Action<long, long>? onProgress)
    {
        var type = typeof(PackWindowViewModel).GetNestedType("ProgressReadStream", BindingFlags.NonPublic)!;
        return (Stream)Activator.CreateInstance(type, inner, size, onProgress)!;
    }

    [TestMethod]
    public async Task WrapsReadsSeeksAndReportsProgress()
    {
        var inner = new MemoryStream(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });
        long lastRead = 0, lastTotal = 0;
        var stream = Create(inner, inner.Length, (read, total) => { lastRead = read; lastTotal = total; });

        Assert.IsTrue(stream.CanRead);
        Assert.IsTrue(stream.CanSeek);
        Assert.IsFalse(stream.CanWrite);
        Assert.AreEqual(8, stream.Length);

        var buffer = new byte[3];
        Assert.AreEqual(3, stream.Read(buffer, 0, 3));
        Assert.AreEqual(3, lastRead);
        Assert.AreEqual(8, lastTotal);

        Assert.AreEqual(3, await stream.ReadAsync(buffer, 0, 3));
        Assert.AreEqual(6, lastRead);

        var memory = new byte[2].AsMemory();
        Assert.AreEqual(2, await stream.ReadAsync(memory));
        Assert.AreEqual(8, lastRead);

        Assert.AreEqual(8, stream.Position);
        stream.Position = 0;
        Assert.AreEqual(0, stream.Position);
        Assert.AreEqual(0, stream.Seek(0, SeekOrigin.Begin));
        stream.Flush();
        stream.SetLength(8);
        Assert.ThrowsException<NotSupportedException>(() => stream.Write(buffer, 0, 1));
    }

    [TestMethod]
    public void TransferProgress_ComputesPercentageAndRemaining()
    {
        var p = new TransferProgress(50, 100, 10);
        Assert.AreEqual(50, p.Percentage);
        Assert.IsFalse(p.IsIndeterminate);
        Assert.AreEqual(TimeSpan.FromSeconds(5), p.Remaining);

        var unknown = new TransferProgress(10, 0, 0);
        Assert.AreEqual(0, unknown.Percentage);
        Assert.IsTrue(unknown.IsIndeterminate);
        Assert.AreEqual(TimeSpan.Zero, unknown.Remaining);

        var capped = new TransferProgress(200, 100, 1);
        Assert.AreEqual(100, capped.Percentage);
        Assert.AreEqual(TimeSpan.Zero, capped.Remaining);
    }

    [TestMethod]
    public async Task InlineDispatcher_RunsSynchronously()
    {
        var ui = new LabelPlus_Next.Services.InlineUiDispatcher();
        var value = 0;
        Assert.IsTrue(ui.CheckAccess());
        await ui.InvokeAsync(() => value = 1);
        ui.Post(() => value = 2);
        Assert.AreEqual(2, value);
    }
}
