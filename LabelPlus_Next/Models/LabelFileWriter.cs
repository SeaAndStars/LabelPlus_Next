using System;
using System.Collections.Generic;
using System.Globalization;
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
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
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
                var coord = string.Format(CultureInfo.InvariantCulture, "[{0:F3},{1:F3},{2}]", n.XPercent, n.YPercent, n.Category);
                await sw.WriteLineAsync($"----------------[{count}]----------------{coord}");
                await sw.WriteLineAsync(n.Text);
                await sw.WriteLineAsync();
            }
        }
    }
}