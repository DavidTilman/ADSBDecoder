using System;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace AircraftRadar;

/// <summary>
/// Collapses a container whose aircraft has no resolved position yet. XAML has no implicit
/// bool-to-Visibility conversion, and this is needed in a Style setter, where x:Bind cannot reach.
/// </summary>
public sealed partial class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}
