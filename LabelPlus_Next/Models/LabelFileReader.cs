using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace LabelPlus_Next.Models;

public class LabelFileReader
{
    public async Task<(string, Dictionary<string, List<LabelItem>?> store)> ReadAsync(string path)
    {
        // 路径解码
        var decodedPath = Uri.UnescapeDataString(path);
        var normalizedPath = decodedPath.Replace('/', '\\');

        if (!File.Exists(normalizedPath))
            throw new FileNotFoundException($"文件未找到或无法访问: {normalizedPath}，请检查路径是否正确，文件是否存在。");

        var store = new Dictionary<string, List<LabelItem>?>();
        var headerBuilder = new StringBuilder();
        var headerRead = false;
        string? nowFilename = null;
        List<LabelItem>? currentList = null;

        using var sr = new StreamReader(normalizedPath, Encoding.UTF8);
        string? line;
        while ((line = await sr.ReadLineAsync()) != null)
        {
            // 只要没遇到文件内容标记，就一直读 header
            if (!headerRead)
            {
                if (line.StartsWith(">>>>>>>>[") || line.StartsWith("----------------["))
                {
                    headerRead = true;
                    nowFilename = line.Substring(9, line.IndexOf("]<<<<<<<<", StringComparison.Ordinal) - 9);
                    currentList = new List<LabelItem>();
                    store[nowFilename] = currentList;
                }
                  
                // 这一行属于内容部分，不加到 header
                // 跳出 header 读取
                else
                    headerBuilder.AppendLine(line);
                continue;
            }

            if (line.StartsWith(">>>>>>>>[") && line.Contains("]<<<<<<<<"))
            {
                nowFilename = line.Substring(9, line.IndexOf("]<<<<<<<<", StringComparison.Ordinal) - 9);
                currentList = new List<LabelItem>();
                store[nowFilename] = currentList;
            }
            else if (line.StartsWith("----------------[") && line.Contains("]----------------"))
            {
                // 标签头
                var labelInfo = line.Substring(17, line.IndexOf("]----------------", StringComparison.Ordinal) - 17);
                var rightText = line.Substring(line.IndexOf("]----------------", StringComparison.Ordinal) + 17);
                var category = 1;
                float x = 0, y = 0;
                if (rightText.StartsWith("[") && rightText.EndsWith("]"))
                {
                    var splitText = rightText.Substring(1, rightText.Length - 2).Split(',');
                    if (splitText.Length >= 3)
                    {
                        x = float.Parse(splitText[0]);
                        y = float.Parse(splitText[1]);
                        category = int.Parse(splitText[2]);
                    }
                }

                var text = await sr.ReadLineAsync();
                if (currentList != null) currentList.Add(new LabelItem(x, y, text, category));
            }
        }

        return (headerBuilder.ToString(), store);
    }
}