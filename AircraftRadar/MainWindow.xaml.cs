using System;

using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

using Windows.UI;

namespace AircraftRadar;

public sealed partial class MainWindow : Window
{
    // Pulled in with the zoom so the outer ring and its bearing labels stay the same
    // distance from centre in pixels, and therefore still on screen.
    private const int OuterRangeNm = 80;
    private const int RingStepNm = 20;

    private static readonly Brush RingBrush = MakeBrush(26, 36, 48);
    private static readonly Brush SpokeBrush = MakeBrush(20, 28, 38);
    private static readonly Brush LabelBrush = MakeBrush(74, 91, 110);
    private static readonly Brush CentreBrush = MakeBrush(127, 160, 191);

    private readonly RadioService radio = new();
    private readonly TranslateTransform backgroundOffset = new();

    public MainWindow()
    {
        // Assigned before InitializeComponent so the x:Bind pass has something to bind to.
        this.ViewModel = new RadarViewModel(this.radio);

        this.InitializeComponent();

        this.BackgroundLayer.RenderTransform = this.backgroundOffset;
        this.BuildBackground();

        this.RangeText.Text = $"RINGS {RingStepNm} NM  ·  RANGE {OuterRangeNm} NM";

        this.Closed += (_, _) =>
        {
            this.ViewModel.Stop();
            this.radio.Stop();
        };

        this.radio.Start();
        this.ViewModel.Start();
    }

    public RadarViewModel ViewModel { get; }

    private void RadarHost_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        this.ViewModel.Projection.ViewWidth = e.NewSize.Width;
        this.ViewModel.Projection.ViewHeight = e.NewSize.Height;

        // The background is drawn once, in centre-relative coordinates; resizing only moves it.
        this.backgroundOffset.X = e.NewSize.Width / 2.0;
        this.backgroundOffset.Y = e.NewSize.Height / 2.0;

        this.ViewModel.Refresh();   // reposition immediately, don't wait for the timer
    }

    /// <summary>
    /// Builds the range rings, compass rose and centre marker once. Everything is positioned
    /// relative to (0, 0), which <see cref="backgroundOffset"/> then moves to the view centre.
    /// </summary>
    private void BuildBackground()
    {
        double pixelsPerNm = this.ViewModel.Projection.PixelsPerNauticalMile;
        double outerRadius = OuterRangeNm * pixelsPerNm;

        for (int nm = RingStepNm; nm <= OuterRangeNm; nm += RingStepNm)
        {
            double radius = nm * pixelsPerNm;

            Ellipse ring = new()
            {
                Width = radius * 2,
                Height = radius * 2,
                Stroke = RingBrush,
                StrokeThickness = 1
            };
            Canvas.SetLeft(ring, -radius);
            Canvas.SetTop(ring, -radius);
            this.BackgroundLayer.Children.Add(ring);

            TextBlock rangeLabel = new()
            {
                Text = nm.ToString(),
                FontFamily = new FontFamily("Consolas"),
                FontSize = 10,
                Foreground = LabelBrush
            };
            Canvas.SetLeft(rangeLabel, 4);
            Canvas.SetTop(rangeLabel, -radius - 14);
            this.BackgroundLayer.Children.Add(rangeLabel);
        }

        for (int bearing = 0; bearing < 360; bearing += 30)
        {
            double radians = bearing * Math.PI / 180.0;
            double dx = Math.Sin(radians);
            double dy = -Math.Cos(radians);     // screen y grows downward

            this.BackgroundLayer.Children.Add(new Line
            {
                X1 = dx * RingStepNm * pixelsPerNm,
                Y1 = dy * RingStepNm * pixelsPerNm,
                X2 = dx * outerRadius,
                Y2 = dy * outerRadius,
                Stroke = SpokeBrush,
                StrokeThickness = 1
            });

            TextBlock bearingLabel = new()
            {
                Text = bearing.ToString("000"),
                FontFamily = new FontFamily("Consolas"),
                FontSize = 10,
                Foreground = LabelBrush
            };
            Canvas.SetLeft(bearingLabel, (dx * (outerRadius + 14)) - 10);
            Canvas.SetTop(bearingLabel, (dy * (outerRadius + 14)) - 7);
            this.BackgroundLayer.Children.Add(bearingLabel);
        }

        this.BackgroundLayer.Children.Add(new Line
        {
            X1 = -6, Y1 = 0, X2 = 6, Y2 = 0,
            Stroke = CentreBrush, StrokeThickness = 1
        });
        this.BackgroundLayer.Children.Add(new Line
        {
            X1 = 0, Y1 = -6, X2 = 0, Y2 = 6,
            Stroke = CentreBrush, StrokeThickness = 1
        });
    }

    private static SolidColorBrush MakeBrush(byte r, byte g, byte b)
        => new(Color.FromArgb(255, r, g, b));
}
