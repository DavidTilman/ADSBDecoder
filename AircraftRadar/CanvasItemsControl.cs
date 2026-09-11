using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;

namespace AircraftRadar;

/// <summary>
/// An <see cref="ItemsControl"/> that positions its generated containers on a Canvas from the
/// item's own X/Y.
///
/// Canvas.Left/Top have to be set on the container, not on the DataTemplate's root: the root is
/// a child of the container, and the Canvas only reads attached properties from its direct
/// children. The obvious way to express that - binding Canvas.Left in ItemContainerStyle - does
/// not work, because WinUI does not support {Binding} in a Style setter's Value; such a setter
/// is ignored silently, leaving every container at (0, 0). Binding the container here in
/// PrepareContainerForItemOverride is the equivalent that WinUI does honour.
/// </summary>
public partial class CanvasItemsControl : ItemsControl
{
    private static readonly BoolToVisibilityConverter VisibilityConverter = new();

    /// <summary>Property on the item holding the container's X, in pixels.</summary>
    public string XPath { get; set; } = "X";

    /// <summary>Property on the item holding the container's Y, in pixels.</summary>
    public string YPath { get; set; } = "Y";

    /// <summary>Boolean property on the item deciding whether the container is drawn at all.</summary>
    public string VisiblePath { get; set; } = "HasPosition";

    protected override void PrepareContainerForItemOverride(DependencyObject element, object item)
    {
        base.PrepareContainerForItemOverride(element, item);

        if (element is not FrameworkElement container || item is null)
            return;

        Bind(container, Canvas.LeftProperty, this.XPath, item, null);
        Bind(container, Canvas.TopProperty, this.YPath, item, null);
        Bind(container, VisibilityProperty, this.VisiblePath, item, VisibilityConverter);
    }

    private static void Bind(
        FrameworkElement target,
        DependencyProperty property,
        string path,
        object source,
        IValueConverter? converter)
    {
        target.SetBinding(property, new Binding
        {
            Path = new PropertyPath(path),
            Source = source,
            Mode = BindingMode.OneWay,
            Converter = converter
        });
    }
}
