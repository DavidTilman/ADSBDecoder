using System;
using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;

using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

using Windows.UI;

namespace AircraftRadar;

/// <summary>
/// The display's view of one aircraft. Created once when the aircraft is first acquired and
/// then updated in place, so the visual tree it backs is never torn down and rebuilt.
/// </summary>
public sealed partial class AircraftViewModel : ObservableObject
{
    /// <summary>Six history dots, per radar convention.</summary>
    private const int TrailLength = 6;

    /// <summary>
    /// Spacing between recorded history points. Chosen so the trail spans about a minute,
    /// which at the display scale makes it roughly as long as the one-minute speed vector.
    /// </summary>
    private static readonly TimeSpan TrailInterval = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Ring buffer of past fixes, held as lat/lon rather than pixels: the projection changes
    /// with the view size, and the offsets are relative to a symbol that is itself moving.
    /// </summary>
    private readonly (double Lat, double Lon)[] trailPoints = new (double, double)[TrailLength];

    private int trailCount;
    private int trailNext;
    private DateTime lastTrailTime;

    public AircraftViewModel(uint icao)
    {
        this.Icao = icao;
        this.Callsign = $"[{icao:X6}]";
        this.SymbolBrush = AltitudeBrushes.Unknown;
        this.DataBlockDetail = "---  ---";

        // Fixed set, created once: the containers are generated on first bind and then only
        // ever updated in place.
        for (int i = 0; i < TrailLength; i++)
            this.Trail.Add(new TrailDotViewModel());
    }

    public ObservableCollection<TrailDotViewModel> Trail { get; } = [];

    public uint Icao { get; }

    /// <summary>Position of the symbol centre, in radar-canvas pixels.</summary>
    [ObservableProperty] public partial double X { get; set; }
    [ObservableProperty] public partial double Y { get; set; }

    /// <summary>
    /// False until a CPR even/odd pair has resolved. Newly acquired aircraft have no position
    /// for the first second or two, and must not be drawn at the top-left corner meanwhile.
    /// </summary>
    [ObservableProperty] public partial bool HasPosition { get; set; }

    [ObservableProperty] public partial string Callsign { get; set; }

    /// <summary>Feet, or null when no altitude has been received.</summary>
    [ObservableProperty] public partial int? Altitude { get; set; }

    /// <summary>Knots, or null when no velocity message has been received.</summary>
    [ObservableProperty] public partial int? GroundSpeed { get; set; }

    /// <summary>Degrees true, clockwise from north, or null when unknown.</summary>
    [ObservableProperty] public partial double? Heading { get; set; }

    [ObservableProperty] public partial Brush SymbolBrush { get; set; }

    /// <summary>
    /// Second line of the data block: altitude in hundreds of feet, then groundspeed in knots.
    /// Either may be absent, and each shows as dashes rather than collapsing the line, so the
    /// block keeps a stable shape.
    /// </summary>
    [ObservableProperty] public partial string DataBlockDetail { get; set; }

    /// <summary>
    /// End of the speed vector, relative to the symbol centre. One minute of travel at the
    /// current groundspeed, drawn along the track.
    /// </summary>
    [ObservableProperty] public partial double VectorEndX { get; set; }
    [ObservableProperty] public partial double VectorEndY { get; set; }

    /// <summary>False when groundspeed or track is missing, which is common.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VectorVisibility))]
    public partial bool HasVector { get; set; }

    /// <summary>
    /// Exposed as Visibility rather than bound through a converter: the XAML root here is a
    /// Window, and x:Bind's generated converter lookup needs a FrameworkElement root, so a
    /// {StaticResource} converter inside this DataTemplate will not compile.
    /// </summary>
    public Visibility VectorVisibility => this.HasVector ? Visibility.Visible : Visibility.Collapsed;

    public void Update(in AircraftState state, Projection projection)
    {
        this.Callsign = string.IsNullOrWhiteSpace(state.Callsign)
            ? $"[{state.Icao:X6}]"
            : state.Callsign;

        this.Altitude = state.Altitude;
        this.GroundSpeed = state.GroundSpeed;
        this.Heading = state.Heading;
        this.SymbolBrush = AltitudeBrushes.For(state.Altitude);

        string altitudeText = state.Altitude is int feet ? (feet / 100).ToString("000") : "---";
        string speedText = state.GroundSpeed is int knots ? knots.ToString("000") : "---";
        this.DataBlockDetail = $"{altitudeText}  {speedText}";

        this.UpdateVector(state, projection);

        if (state.Lat is double lat && state.Lon is double lon && projection.IsReady)
        {
            (this.X, this.Y) = projection.ToScreen(lat, lon);
            this.HasPosition = true;

            this.RecordTrail(lat, lon);
            this.UpdateTrail(lat, lon, projection);
        }
        else
        {
            this.HasPosition = false;
        }
    }

    /// <summary>Appends a history point, but no faster than <see cref="TrailInterval"/>.</summary>
    private void RecordTrail(double lat, double lon)
    {
        if (this.trailCount > 0)
        {
            (double Lat, double Lon) last = this.trailPoints[(this.trailNext - 1 + TrailLength) % TrailLength];

            // A stale CPR pair leaves the position unchanged; don't stack dots on one spot.
            if (last.Lat == lat && last.Lon == lon)
                return;

            if (DateTime.UtcNow - this.lastTrailTime < TrailInterval)
                return;
        }

        this.trailPoints[this.trailNext] = (lat, lon);
        this.trailNext = (this.trailNext + 1) % TrailLength;

        if (this.trailCount < TrailLength)
            this.trailCount++;

        this.lastTrailTime = DateTime.UtcNow;
    }

    /// <summary>Re-projects the history points relative to the symbol's current centre.</summary>
    private void UpdateTrail(double lat, double lon, Projection projection)
    {
        (double centreX, double centreY) = projection.ToScreen(lat, lon);

        for (int i = 0; i < TrailLength; i++)
        {
            TrailDotViewModel dot = this.Trail[i];

            if (i >= this.trailCount)
            {
                dot.HasPosition = false;
                continue;
            }

            // Oldest first, so index 0 is the faintest and furthest behind.
            int slot = (this.trailNext - this.trailCount + i + TrailLength) % TrailLength;
            (double Lat, double Lon) point = this.trailPoints[slot];

            (double dotX, double dotY) = projection.ToScreen(point.Lat, point.Lon);

            dot.X = dotX - centreX;
            dot.Y = dotY - centreY;
            dot.Opacity = 0.12 + (0.48 * (i + 1) / this.trailCount);
            dot.DotBrush = this.SymbolBrush;
            dot.HasPosition = true;
        }
    }

    private void UpdateVector(in AircraftState state, Projection projection)
    {
        // Both are legitimately absent until a velocity message arrives.
        if (state.GroundSpeed is not int knots || knots <= 0 || state.Heading is not double track)
        {
            this.HasVector = false;
            return;
        }

        double lengthPixels = knots / 60.0 * projection.PixelsPerNauticalMile;
        double radians = track * Math.PI / 180.0;

        // Track is degrees clockwise from north; screen y grows downward.
        this.VectorEndX = Math.Sin(radians) * lengthPixels;
        this.VectorEndY = -Math.Cos(radians) * lengthPixels;
        this.HasVector = true;
    }
}

/// <summary>Altitude-band colouring, by flight level.</summary>
internal static class AltitudeBrushes
{
    internal static readonly Brush Unknown = Make(154, 165, 177);   // grey
    private static readonly Brush Low = Make(79, 195, 247);         // below FL100  - light blue
    private static readonly Brush Medium = Make(102, 187, 106);     // FL100-FL200  - green
    private static readonly Brush High = Make(255, 238, 88);        // FL200-FL300  - yellow
    private static readonly Brush VeryHigh = Make(255, 167, 38);    // FL300-FL400  - orange
    private static readonly Brush Upper = Make(206, 147, 216);      // above FL400  - violet

    internal static Brush For(int? altitudeFeet) => altitudeFeet switch
    {
        null => Unknown,
        < 10_000 => Low,
        < 20_000 => Medium,
        < 30_000 => High,
        < 40_000 => VeryHigh,
        _ => Upper
    };

    private static SolidColorBrush Make(byte r, byte g, byte b)
        => new(Color.FromArgb(255, r, g, b));
}
