using System;

namespace LabelPlus_Next.Models;

public class LabelItem
{
    public int Category;
    public string Text;
    public float X_percent;
    public float Y_percent;

    public LabelItem(float x_percent, float y_percent, string text, int category)
    {
        if (!(category >= 1 && category <= 9))
            throw new Exception();

        X_percent = x_percent;
        Y_percent = y_percent;
        Text = text;
        Category = category;
    }
}