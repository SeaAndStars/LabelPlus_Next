using System;
using System.IO;
using System.Text.Json;

namespace LabelPlus_Next.Models;

public class GlobalVar
{
    public static GroupDefineItem[] DefaultGroupDefineItems;
    public static QuickTextItem[] QuickTextItems;
    public static float SetLabelVisualRatioX;
    public static float SetLabelVisualRatioY;
    public static string DefaultComment;

    public static void Reload()
    {
        var configPath = "labelplus_config.json";
        if (!File.Exists(configPath))
            throw new Exception("Not found config file.");

        var json = File.ReadAllText(configPath);
        var config = JsonSerializer.Deserialize<Config>(json);

        // QuickText
        QuickTextItems = config.QuickText;

        // GroupDefine
        DefaultGroupDefineItems = config.GroupDefine;

        // SetLabelVisualRatio
        SetLabelVisualRatioX = config.SetLabelVisualRatio[0];
        SetLabelVisualRatioY = config.SetLabelVisualRatio[1];

        // DefaultComment
        DefaultComment = config.DefaultComment.Replace(@"\n", "\r\n");
    }

    public struct QuickTextItem
    {
        public string Text { get; set; }
        public string Key { get; set; }
    }

    public struct GroupDefineItem
    {
        public string Name { get; set; }
        public string RGB { get; set; }

        public GroupDefineItem(string name, string rgb)
        {
            Name = name;
            RGB = rgb;
        }
    }

    private class Config
    {
        public QuickTextItem[] QuickText { get; set; }
        public GroupDefineItem[] GroupDefine { get; set; }
        public float[] SetLabelVisualRatio { get; set; }
        public string DefaultComment { get; set; }
    }
}