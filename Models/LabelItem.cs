using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace LabelPlus_Next.Models;

public class LabelItem : INotifyPropertyChanged
{
    public float XPercent { get; set; }
    public float YPercent { get; set; }
    private string? text;
    public string? Text
    {
        get => text;
        set { text = value; OnPropertyChanged(); }
    }
    public int Category { get; set; }

    // 新增：编号和分类字符串属性
    private int index;
    public int Index
    {
        get => index;
        set { index = value; OnPropertyChanged(); }
    }
    private string categoryString = "";
    public string CategoryString
    {
        get => categoryString;
        set { categoryString = value; OnPropertyChanged(); }
    }

    public LabelItem(float xPercent, float yPercent, string? text, int category)
    {
        if (!(category >= 1 && category <= 9))
            throw new Exception();
        XPercent = xPercent;
        YPercent = yPercent;
        Text = text;
        Category = category;
    }
    public LabelItem() { }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}