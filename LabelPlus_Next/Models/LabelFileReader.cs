using NLog;
using System.Globalization;
using System.Text;

namespace LabelPlus_Next.Models;

public class LabelFileReader
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    public async Task<(string, Dictionary<string, List<LabelItem>?> store)> ReadAsync(string path)
    {
        Logger.Debug("Reading file: {path}", path);
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

        // 读取所有行以便支持多行文本解析
        var lines = await File.ReadAllLinesAsync(normalizedPath, Encoding.UTF8);
        var i = 0;

        // 先读取头部，直到遇到内容标记行为止（文件名或条目行）
        while (i < lines.Length && !headerRead)
        {
            var line0 = lines[i];
            if ((line0.StartsWith(">>>>>>>>") || line0.StartsWith("----------------")) && line0.Contains('[') && line0.Contains(']'))
            {
                headerRead = true;
                break; // 让外层循环处理该行
            }
            headerBuilder.AppendLine(line0);
            i++;
        }

        // 解析主体
        while (i < lines.Length)
        {
            var line = lines[i];

            // 文件名块
            if (line.StartsWith(">>>>>>>>") && line.Contains("]<<<<<<<<"))
            {
                var start = line.IndexOf('[') + 1;
                var end = line.IndexOf("]<<<<<<<<", StringComparison.Ordinal);
                if (end > start)
                {
                    nowFilename = line.Substring(start, end - start);
                    currentList = new List<LabelItem>();
                    store[nowFilename] = currentList;
                }
                i++;
                continue;
            }

            // 标签条目行
            if (line.StartsWith("----------------[") && line.Contains("]----------------"))
            {
                var headerStart = line.IndexOf('[') + 1;
                var headerEnd = line.IndexOf("]----------------", StringComparison.Ordinal);
                var rightStart = headerEnd + "]----------------".Length;
                var category = 1;
                float x = 0, y = 0;
                if (headerEnd > headerStart && rightStart <= line.Length)
                {
                    var rightText = line.Substring(rightStart);
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
                }

                // 读取多行文本，直到遇到下一个标记行（新的条目或新的图片块）或到达文件末尾
                i++;
                var textSb = new StringBuilder();
                while (i < lines.Length)
                {
                    var peek = lines[i];
                    if (peek.StartsWith("----------------[") || peek.StartsWith(">>>>>>>>") && peek.Contains("]<<<<<<<<"))
                    {
                        break; // 下一个块的开始，停止收集文本
                    }
                    textSb.AppendLine(peek);
                    i++;
                }
                var text = textSb.ToString().TrimEnd('\r', '\n');
                if (currentList != null)
                    currentList.Add(new LabelItem(x, y, text, category));

                continue;
            }

            // 其他行（空行等）跳过
            i++;
        }

        Logger.Debug("Read completed: images={count}", store.Count);
        return (headerBuilder.ToString(), store);
    }
}
