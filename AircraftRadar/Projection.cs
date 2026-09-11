using System;

namespace AircraftRadar;

/// <summary>
/// Flat equirectangular projection of lat/lon onto the radar canvas, centred on
/// <see cref="CentreLat"/>/<see cref="CentreLon"/>. Good enough over a few hundred
/// nautical miles, which is all a single receiver can see.
/// </summary>
public sealed class Projection
{
    /// <summary>Stansted.</summary>
    public const double DefaultCentreLat = 51.885;
    public const double DefaultCentreLon = 0.235;

    private double centreLat = DefaultCentreLat;

    /// <summary>
    /// cos(CentreLat). Cached because it depends only on the centre, never on the
    /// aircraft, so recomputing it per aircraft per frame would be pure waste.
    /// </summary>
    private double cosCentreLat = Math.Cos(DefaultCentreLat * Math.PI / 180.0);

    public double CentreLat
    {
        get => this.centreLat;
        set
        {
            this.centreLat = value;
            this.cosCentreLat = Math.Cos(value * Math.PI / 180.0);
        }
    }

    public double CentreLon { get; set; } = DefaultCentreLon;

    public double PixelsPerDegree { get; set; } = 360.0;

    public double ViewWidth { get; set; }
    public double ViewHeight { get; set; }

    /// <summary>
    /// False until the first layout pass has given the radar container a real size.
    /// Projecting before that would put every aircraft at (0, 0).
    /// </summary>
    public bool IsReady => this.ViewWidth > 0 && this.ViewHeight > 0;

    /// <summary>One degree of latitude is 60 nautical miles.</summary>
    public double PixelsPerNauticalMile => this.PixelsPerDegree / 60.0;

    public (double X, double Y) ToScreen(double lat, double lon)
    {
        double x = (lon - this.CentreLon) * this.cosCentreLat * this.PixelsPerDegree;
        double y = (lat - this.CentreLat) * this.PixelsPerDegree;

        return ((this.ViewWidth / 2.0) + x, (this.ViewHeight / 2.0) - y);
    }
}
