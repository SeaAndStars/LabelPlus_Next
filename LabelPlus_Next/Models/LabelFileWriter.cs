using NLog;
using System.Globalization;
using System.Text;

namespace LabelPlus_Next.Models;

public class LabelFileWriter
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    public async Task WriteAsync(string path, string header, Dictionary<string, List<LabelItem>> store)
    {
        try
        {
            // 解码路径
            path = Uri.UnescapeDataString(path);

            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            // 避免被其他进程短暂占用导致失败：增加轻量重试与共享读取
            const int maxAttempts = 3;
            const int delayMs = 150;
            StreamWriter? sw = null;
            for (var attempt = 1; attempt <= maxAttempts; attempt++)
            {
                try
                {
                    var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
                    sw = new StreamWriter(fs, Encoding.UTF8);
                    break;
                }
                catch (IOException) when (attempt < maxAttempts)
                {
                    await Task.Delay(delayMs);
                }
            }
            if (sw is null)
                throw new IOException("Failed to open file for writing after retries: " + path);
            await using var _ = sw.ConfigureAwait(false);
            await sw.WriteLineAsync(header);

            foreach (var kvp in store)
            {
                await sw.WriteLineAsync();
                await sw.WriteLineAsync($">>>>>>>>[{kvp.Key}]<<<<<<<<");
                var count = 0;
                foreach (var n in kvp.Value)
                {
                    count++;
                    var coord = string.Format(CultureInfo.InvariantCulture, "[{0:F3},{1:F3},{2}]", n.XPercent, n.YPercent, n.Category);
                    await sw.WriteLineAsync($"----------------[{count}]----------------{coord}");
                    await sw.WriteLineAsync(n.Text);
                    await sw.WriteLineAsync();
                }
            }
            Logger.Debug("Write completed: {path}, images={count}", path, store.Count);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Write failed: {path}", path);
            throw;
        }
    }
}
