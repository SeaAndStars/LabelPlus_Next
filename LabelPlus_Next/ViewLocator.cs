using Avalonia.Controls;
using Avalonia.Controls.Templates;
using LabelPlus_Next.ViewModels;
using System.Diagnostics.CodeAnalysis;

namespace LabelPlus_Next;

public class ViewLocator : IDataTemplate
{
    [UnconditionalSuppressMessage("Trimming", "IL2057", Justification = "View type resolution is within the application's assembly and pattern-based. Views are kept; safe under trimming.")]
    public Control? Build(object? param)
    {
        if (param is null)
            return null;

        var vmType = param.GetType();
        var viewTypeName = vmType.FullName!.Replace("ViewModel", "View", StringComparison.Ordinal);
        var asmName = vmType.Assembly.GetName().Name;
        var qualified = $"{viewTypeName}, {asmName}";
        var type = Type.GetType(qualified);

        if (type != null)
        {
            return (Control)Activator.CreateInstance(type)!;
        }

        return new TextBlock { Text = "Not Found: " + viewTypeName };
    }

    public bool Match(object? data) => data is ViewModelBase;
}
