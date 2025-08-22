using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Controls;
using System.Threading.Tasks;
using LabelPlus_Next.UpdateClient.ViewModels;

namespace LabelPlus_Next.UpdateClient;

public class ViewLoader : IDataTemplate
{
    public Control? Build(object? param)
    {
        if (param is null)
            return null;

        var name = param.GetType().FullName!.Replace("ViewModel", "View", StringComparison.Ordinal);
        var type = Type.GetType(name);

        if (type != null)
        {
            return (Control)Activator.CreateInstance(type)!;
        }

        return new TextBlock { Text = "Not Found: " + name };
    }

    public bool Match(object? data)
    {
        return data is ViewModelBase;
    }
}
