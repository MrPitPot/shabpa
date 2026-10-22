using System.Windows;
using System.Windows.Controls;

namespace Shabpa.Kit;

/// <summary>An element can have only one logical parent. A cached or reused element that is added to a new panel while it is still
/// the child of the old one throws "Specified element is already the logical child of another element"; these helpers let go first.</summary>
public static class Reparent
{
    /// <summary>Removes the element from whatever holds it now (panel, decorator, content control, items control) and returns it.</summary>
    public static T Detach<T>(T element) where T : UIElement
    {
        switch (LogicalTreeHelper.GetParent(element) ?? (element as FrameworkElement)?.Parent ?? System.Windows.Media.VisualTreeHelper.GetParent(element))
        {
            case Panel panel: panel.Children.Remove(element); break;
            case Decorator d when ReferenceEquals(d.Child, element): d.Child = null; break;
            case ContentControl c when ReferenceEquals(c.Content, element): c.Content = null; break;
            case ContentPresenter p when ReferenceEquals(p.Content, element): p.Content = null; break;
            case ItemsControl i: i.Items.Remove(element); break;
        }
        return element;
    }

    /// <summary>Adds the element to the panel, taking it out of its old parent first.</summary>
    public static T AddTo<T>(Panel panel, T element) where T : UIElement
    {
        Detach(element);
        panel.Children.Add(element);
        return element;
    }
}
