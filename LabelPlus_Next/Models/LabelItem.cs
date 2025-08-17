using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace LabelPlus_Next.Models;

public class LabelItem : INotifyPropertyChanged
{
    private string categoryString = "";

    // 新增：编号和分类字符串属性
    private int index;
    private string? text;

    public LabelItem(float xPercent, float yPercent, string? text, int category)
    {
        if (!(category >= 1 && category <= 9))
            throw new Exception();
        XPercent = xPercent;
        YPercent = yPercent;
        Text = text;
        Category = category;
    }

    public LabelItem()
    {
    }

    public float XPercent { get; set; }
    public float YPercent { get; set; }

    public string? Text
    {
        get => text;
        set
        {
            text = value;
            OnPropertyChanged();
        }
    }

    public int Category { get; set; }

    public int Index
    {
        get => index;
        set
        {
            index = value;
            OnPropertyChanged();
        }
    }

    public string CategoryString
    {
        get => categoryString;
        set
        {
            categoryString = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}