using System.Windows;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using Binding = System.Windows.Data.Binding;

namespace Netwright.Companion;

/// <summary>
/// WPF bindings with <see cref="UpdateSourceTrigger.LostFocus"/> (the default for TextBox.Text) write
/// to the source only when focus leaves the element. Text set through UI Automation never moves
/// focus, so the binding stays dirty until something commits it.
/// </summary>
internal static class WpfBindings
{
    /// <summary>Returns null when <paramref name="window"/> is not a WPF window.</summary>
    public static int? Commit(nint window)
    {
        if (window == 0 || HwndSource.FromHwnd(window) is not { } source)
        {
            return null;
        }

        return source.Dispatcher.Invoke(() => source.RootVisual is { } root ? CommitTree(root) : 0);
    }

    public static int CommitTree(DependencyObject root)
    {
        var count = 0;
        var pending = new Stack<DependencyObject>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            var values = current.GetLocalValueEnumerator();
            while (values.MoveNext())
            {
                var property = values.Current.Property;
                if (BindingOperations.GetBindingExpressionBase(current, property) is { IsDirty: true } expression
                    && UpdatesOnLostFocus(current, property, expression))
                {
                    expression.UpdateSource();
                    count++;
                }
            }

            if (current is Visual or Visual3D)
            {
                for (var i = 0; i < VisualTreeHelper.GetChildrenCount(current); i++)
                {
                    pending.Push(VisualTreeHelper.GetChild(current, i));
                }
            }
        }

        return count;
    }

    // Explicit bindings are left alone: the app decides when those are written.
    private static bool UpdatesOnLostFocus(DependencyObject target, DependencyProperty property, BindingExpressionBase expression)
    {
        var trigger = expression.ParentBindingBase switch
        {
            Binding binding => binding.UpdateSourceTrigger,
            MultiBinding multi => multi.UpdateSourceTrigger,
            _ => UpdateSourceTrigger.Default,
        };

        if (trigger == UpdateSourceTrigger.Default)
        {
            trigger = property.GetMetadata(target.DependencyObjectType) is FrameworkPropertyMetadata metadata
                ? metadata.DefaultUpdateSourceTrigger
                : UpdateSourceTrigger.PropertyChanged;
        }

        return trigger == UpdateSourceTrigger.LostFocus;
    }
}
