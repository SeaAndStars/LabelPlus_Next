namespace LabelPlus_Next.Models;

public class LabelFileHeaderManager
{
    public static Task<(string[] fileHead, List<string> groupList, string comment)> ParseHeaderAsync(
        string headerText)
    {
        // 使用 Environment.NewLine 兼容所有平台
        var nl = Environment.NewLine;

        // 先按分隔符拆分 header，每个分隔符独占一行
        var blocks = headerText.Split(new[] { nl + "-" + nl }, StringSplitOptions.None);

        if (blocks.Length < 3)
            throw new Exception("文件头丢失");

        // 文件头部分
        var fileHead = blocks[0].Split(',');

        // 分组部分
        var groupList = new List<string>();
        foreach (var line in blocks[1].Split(new[] { nl }, StringSplitOptions.RemoveEmptyEntries))
        {
            var t = line.Trim();
            if (t != "")
                groupList.Add(t);
        }

        // 注释部分（允许多行）
        var comment = blocks[2].Trim();

        return Task.FromResult((fileHead, groupList, comment));
    }

    public static Task<string> GenerateHeaderAsync(string[] fileHead, List<string> groupList, string comment)
    {
        var nl = Environment.NewLine;
        var result = string.Join(",", fileHead) + nl + "-" + nl;
        foreach (var str in groupList)
            result += str + nl;
        result += "-" + nl;
        result += comment + nl;
        return Task.FromResult(result);
    }
}

