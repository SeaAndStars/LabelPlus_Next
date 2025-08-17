using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace LabelPlus_Next.Models;

public class LabelFileWriter
{
    public async Task WriteAsync(string path, string header, Dictionary<string, List<LabelItem>> store)
    {
        // 解码路径
        path = Uri.UnescapeDataString(path);

        var dir = Path.GetDirectoryName(path);
        if (!Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        using var sw = new StreamWriter(path, false, Encoding.UTF8);
        await sw.WriteLineAsync(header);

        foreach (var kvp in store)
        {
            await sw.WriteLineAsync();
            await sw.WriteLineAsync($">>>>>>>>[{kvp.Key}]<<<<<<<<");
            var count = 0;
            foreach (var n in kvp.Value)
            {
                count++;
                await sw.WriteLineAsync(
                    $"----------------[{count}]----------------[{n.XPercent:F3},{n.YPercent:F3},{n.Category}]");
                await sw.WriteLineAsync(n.Text);
                await sw.WriteLineAsync();
            }
        }
    }
}