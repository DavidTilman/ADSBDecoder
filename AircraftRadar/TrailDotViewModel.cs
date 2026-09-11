using CommunityToolkit.Mvvm.ComponentModel;

using Microsoft.UI.Xaml.Media;

namespace AircraftRadar;

/// <summary>
/// One history dot. Offsets are relative to the aircraft symbol's centre, so they are
/// re-projected each refresh as the aircraft moves.
///
/// The property names match CanvasItemsControl's defaults (X, Y, HasPosition) so the nested
/// control needs no configuration.
/// </summary>
public sealed partial class TrailDotViewModel : ObservableObject
{
    [ObservableProperty] public partial double X { get; set; }
    [ObservableProperty] public partial double Y { get; set; }

    /// <summary>False for dots not yet filled - a new aircraft has fewer than six.</summary>
    [ObservableProperty] public partial bool HasPosition { get; set; }

    /// <summary>Fades towards the oldest dot.</summary>
    [ObservableProperty] public partial double Opacity { get; set; }

    [ObservableProperty] public partial Brush? DotBrush { get; set; }
}
