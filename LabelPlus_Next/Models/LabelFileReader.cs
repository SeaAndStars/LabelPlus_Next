using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace LabelPlus_Next.Models;

public class LabelFileReader
{
    public async Task<(string, Dictionary<string, List<LabelItem>?> store)> ReadAsync(string path)
    {
        // 路径解码并标准化
        var decodedPath = Uri.UnescapeDataString(path);
        var normalizedPath = decodedPath.Replace('/', Path.DirectorySeparatorChar);

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
            if (!headerRead)
            {
                if ((line.StartsWith(">>>>>>>>") || line.StartsWith("----------------")) && line.Contains('[') && line.Contains(']'))
                {
                    headerRead = true;
                    // 将该行作为内容部分处理，继续到主体逻辑
                }
                else
                {
                    headerBuilder.AppendLine(line);
                    continue;
                }
            }

            if (line.StartsWith(">>>>>>>>[") && line.Contains("]<<<<<<<<"))
            {
                var start = line.IndexOf('[') + 1;
                var end = line.IndexOf("]<<<<<<<<", StringComparison.Ordinal);
                if (end > start)
                {
                    nowFilename = line.Substring(start, end - start);
                    currentList = new List<LabelItem>();
                    store[nowFilename] = currentList;
                }
                continue;
            }

            if (line.StartsWith("----------------[") && line.Contains("]----------------"))
            {
                var headerStart = line.IndexOf('[') + 1;
                var headerEnd = line.IndexOf("]----------------", StringComparison.Ordinal);
                var rightStart = headerEnd + "]----------------".Length;
                if (headerEnd > headerStart && rightStart <= line.Length)
                {
                    var rightText = line.Substring(rightStart);
                    int category = 1;
                    float x = 0, y = 0;
                    if (rightText.StartsWith("[") && rightText.EndsWith("]"))
                    {
                        var content = rightText.Substring(1, rightText.Length - 2);
                        var splitText = content.Split(',');
                        if (splitText.Length >= 3)
                        {
                            float.TryParse(splitText[0], NumberStyles.Float, CultureInfo.InvariantCulture, out x);
                            float.TryParse(splitText[1], NumberStyles.Float, CultureInfo.InvariantCulture, out y);
                            int.TryParse(splitText[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out category);
                        }
                    }

                    var text = await sr.ReadLineAsync();
                    if (currentList != null)
                        currentList.Add(new LabelItem(x, y, text, category));
                }
                continue;
            }

            // 其他空行等忽略
        }

        return (headerBuilder.ToString(), store);
    }
}