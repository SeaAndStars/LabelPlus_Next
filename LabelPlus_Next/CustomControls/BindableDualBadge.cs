using Avalonia;
using Ursa.Controls;

namespace LabelPlus_Next.CustomControls;

public class BindableDualBadge : DualBadge
{
    public static readonly StyledProperty<string?> BindableClassProperty =
        AvaloniaProperty.Register<BindableDualBadge, string?>(nameof(BindableClass));

    public string? BindableClass
    {
        get => GetValue(BindableClassProperty);
        set => SetValue(BindableClassProperty, value);
    }

    static BindableDualBadge()
    {
        BindableClassProperty.Changed.AddClassHandler<BindableDualBadge>((o, e) =>
        {
            var oldClass = e.OldValue as string;
            if (!string.IsNullOrWhiteSpace(oldClass))
            {
                o.Classes.Remove(oldClass);
            }
            var newClass = e.NewValue as string;
            if (!string.IsNullOrWhiteSpace(newClass))
            {
                o.Classes.Add(newClass);
            }
        });
    }
}
