using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Trisat.Core.Models;

namespace Trisat.Win.Controls;

/// <summary>
/// Hardware-accelerated polar celestial SkyPlot control matching TRISAT iOS/macOS/Web visual design.
/// </summary>
public sealed class SkyPlotControl : Border
{
    public static readonly DependencyProperty SatellitesProperty =
        DependencyProperty.Register(nameof(Satellites), typeof(IReadOnlyList<SatelliteInfo>), typeof(SkyPlotControl),
            new PropertyMetadata(null, OnVisualStateChanged));

    public static readonly DependencyProperty IsAwaitingLocationProperty =
        DependencyProperty.Register(nameof(IsAwaitingLocation), typeof(bool), typeof(SkyPlotControl),
            new PropertyMetadata(false, OnVisualStateChanged));

    public static readonly DependencyProperty IsAwaitingTleProperty =
        DependencyProperty.Register(nameof(IsAwaitingTle), typeof(bool), typeof(SkyPlotControl),
            new PropertyMetadata(false, OnVisualStateChanged));

    public static readonly DependencyProperty ElevationMaskProperty =
        DependencyProperty.Register(nameof(ElevationMask), typeof(double), typeof(SkyPlotControl),
            new PropertyMetadata(10.0, OnVisualStateChanged));

    public IReadOnlyList<SatelliteInfo>? Satellites
    {
        get => (IReadOnlyList<SatelliteInfo>?)GetValue(SatellitesProperty);
        set => SetValue(SatellitesProperty, value);
    }

    public bool IsAwaitingLocation
    {
        get => (bool)GetValue(IsAwaitingLocationProperty);
        set => SetValue(IsAwaitingLocationProperty, value);
    }

    public bool IsAwaitingTle
    {
        get => (bool)GetValue(IsAwaitingTleProperty);
        set => SetValue(IsAwaitingTleProperty, value);
    }

    public double ElevationMask
    {
        get => (double)GetValue(ElevationMaskProperty);
        set => SetValue(ElevationMaskProperty, value);
    }

    private readonly Canvas _canvas = new() { ClipToBounds = true };

    public SkyPlotControl()
    {
        Child = _canvas;
        Background = Brushes.Transparent;
        SizeChanged += (_, _) => Redraw();
    }

    private static void OnVisualStateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is SkyPlotControl ctrl)
            ctrl.Redraw();
    }

    public static Color GetConstellationColor(GnssConstellation c) => c switch
    {
        GnssConstellation.Galileo => Color.FromRgb(0x00, 0xE5, 0xFF), // Cyan
        GnssConstellation.Gps => Color.FromRgb(0xFF, 0xB8, 0x00),     // Amber
        GnssConstellation.Glonass => Color.FromRgb(0xFF, 0x2A, 0x55), // Red
        GnssConstellation.BeiDou => Color.FromRgb(0x38, 0xD9, 0x70),  // Green
        _ => Colors.White
    };

    private void Redraw()
    {
        _canvas.Children.Clear();

        double w = _canvas.ActualWidth;
        double h = _canvas.ActualHeight;
        if (w < 60 || h < 60) return;

        double cx = w / 2.0;
        double cy = h / 2.0;
        double radius = Math.Min(w, h) / 2.0 - 24.0;
        if (radius <= 10) return;

        var ringBrush = new SolidColorBrush(Color.FromArgb(0x55, 0x64, 0x74, 0x8B));
        var innerRingBrush = new SolidColorBrush(Color.FromArgb(0x40, 0x64, 0x74, 0x8B));
        var bgFill = new RadialGradientBrush(
            Color.FromArgb(0x38, 0x06, 0xB6, 0xD4),
            Color.FromArgb(0x14, 0x0F, 0x17, 0x2A));

        // 1. Outer horizon circle (0 deg)
        AddCircle(cx, cy, radius, ringBrush, 1.5, bgFill, null);
        // 30 deg ring
        AddCircle(cx, cy, radius * (60.0 / 90.0), innerRingBrush, 1.0, null, [4, 4]);
        // 60 deg ring
        AddCircle(cx, cy, radius * (30.0 / 90.0), innerRingBrush, 1.0, null, [4, 4]);

        // Elevation mask ring
        if (ElevationMask > 0 && ElevationMask < 85)
        {
            double maskR = radius * ((90.0 - ElevationMask) / 90.0);
            AddCircle(cx, cy, maskR, new SolidColorBrush(Color.FromArgb(0x66, 0x10, 0xB9, 0x81)), 1.0, null, [3, 3]);
        }

        // 2. Cardinal axes (N-S, E-W)
        AddLine(cx, cy - radius, cx, cy + radius, innerRingBrush);
        AddLine(cx - radius, cy, cx + radius, cy, innerRingBrush);

        // Cardinal labels
        AddCenteredText("N", cx, cy - radius - 12, Color.FromRgb(0x38, 0xBD, 0xF8), 11, FontWeights.Bold);
        AddCenteredText("S", cx, cy + radius + 12, Color.FromRgb(0x94, 0xA3, 0xB8), 11, FontWeights.Bold);
        AddCenteredText("E", cx + radius + 13, cy, Color.FromRgb(0x94, 0xA3, 0xB8), 11, FontWeights.Bold);
        AddCenteredText("W", cx - radius - 13, cy, Color.FromRgb(0x94, 0xA3, 0xB8), 11, FontWeights.Bold);

        // Ring elevation labels
        AddCenteredText("30°", cx + 14, cy - radius * (60.0 / 90.0) + 8, Color.FromArgb(0x99, 0x94, 0xA3, 0xB8), 9, FontWeights.Normal);
        AddCenteredText("60°", cx + 14, cy - radius * (30.0 / 90.0) + 8, Color.FromArgb(0x99, 0x94, 0xA3, 0xB8), 9, FontWeights.Normal);

        // 3. Satellites
        var sats = Satellites;
        if (sats is { Count: > 0 })
        {
            foreach (var sat in sats)
            {
                double clampedEl = Math.Clamp(sat.Elevation, 0.0, 90.0);
                double r = radius * (90.0 - clampedEl) / 90.0;
                double rad = (sat.Azimuth - 90.0) * Math.PI / 180.0;
                double x = cx + r * Math.Cos(rad);
                double y = cy + r * Math.Sin(rad);

                var color = GetConstellationColor(sat.Constellation);
                var fillColor = Color.FromArgb(0x66, color.R, color.G, color.B);

                var dot = new Ellipse
                {
                    Width = 20,
                    Height = 20,
                    Stroke = new SolidColorBrush(color),
                    StrokeThickness = sat.UsedInFix ? 2.2 : 1.3,
                    Fill = new SolidColorBrush(fillColor),
                    ToolTip = $"{sat.FormattedLabel} ({sat.Constellation})\n" +
                              $"Elev: {sat.Elevation:F1}°  |  Azim: {sat.Azimuth:F1}°\n" +
                              $"C/N0: {sat.Cno} dB-Hz  |  Band: {sat.Band}\n" +
                              $"Used in Fix: {(sat.UsedInFix ? "Yes" : "No")}" +
                              (sat.HasInav ? "\nGalileo I/NAV Capable" : "")
                };

                Canvas.SetLeft(dot, x - 10);
                Canvas.SetTop(dot, y - 10);
                _canvas.Children.Add(dot);

                var label = new TextBlock
                {
                    Text = sat.FormattedLabel,
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = 9,
                    FontWeight = FontWeights.Bold,
                    Foreground = Brushes.White,
                    IsHitTestVisible = false
                };
                label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                Canvas.SetLeft(label, x - label.DesiredSize.Width / 2.0);
                Canvas.SetTop(label, y + 10);
                _canvas.Children.Add(label);
            }
        }
        else
        {
            string title;
            string subtitle;
            Color accent;

            if (IsAwaitingLocation)
            {
                title = "Awaiting Location Fix";
                subtitle = "Connect receiver or switch to TLE Simulation";
                accent = Color.FromRgb(0xFB, 0x92, 0x3C);
            }
            else if (IsAwaitingTle)
            {
                title = "No TLE Orbits Loaded";
                subtitle = "Download fresh ephemeris in Settings";
                accent = Color.FromRgb(0x22, 0xD3, 0xEE);
            }
            else
            {
                title = $"No Satellites Above {ElevationMask:F0}° Mask";
                subtitle = "Check constellation filters or antenna view";
                accent = Color.FromRgb(0x94, 0xA3, 0xB8);
            }

            var hud = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0xCC, 0x02, 0x06, 0x17)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x66, accent.R, accent.G, accent.B)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(14, 10, 14, 10),
                Child = new StackPanel
                {
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = title,
                            Foreground = new SolidColorBrush(accent),
                            FontWeight = FontWeights.Bold,
                            FontSize = 12,
                            HorizontalAlignment = HorizontalAlignment.Center
                        },
                        new TextBlock
                        {
                            Text = subtitle,
                            Foreground = new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8)),
                            FontSize = 10,
                            Margin = new Thickness(0, 3, 0, 0),
                            HorizontalAlignment = HorizontalAlignment.Center
                        }
                    }
                }
            };

            hud.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(hud, cx - hud.DesiredSize.Width / 2.0);
            Canvas.SetTop(hud, cy - hud.DesiredSize.Height / 2.0);
            _canvas.Children.Add(hud);
        }
    }

    private void AddCircle(double cx, double cy, double r, Brush stroke, double thickness, Brush? fill, DoubleCollection? dash)
    {
        var ellipse = new Ellipse
        {
            Width = r * 2,
            Height = r * 2,
            Stroke = stroke,
            StrokeThickness = thickness,
            Fill = fill ?? Brushes.Transparent
        };
        if (dash is not null) ellipse.StrokeDashArray = dash;
        Canvas.SetLeft(ellipse, cx - r);
        Canvas.SetTop(ellipse, cy - r);
        _canvas.Children.Add(ellipse);
    }

    private void AddLine(double x1, double y1, double x2, double y2, Brush stroke)
    {
        _canvas.Children.Add(new Line
        {
            X1 = x1, Y1 = y1, X2 = x2, Y2 = y2,
            Stroke = stroke, StrokeThickness = 1
        });
    }

    private void AddCenteredText(string text, double x, double y, Color color, double size, FontWeight weight)
    {
        var tb = new TextBlock
        {
            Text = text,
            Foreground = new SolidColorBrush(color),
            FontSize = size,
            FontWeight = weight,
            FontFamily = new FontFamily("Consolas")
        };
        tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(tb, x - tb.DesiredSize.Width / 2.0);
        Canvas.SetTop(tb, y - tb.DesiredSize.Height / 2.0);
        _canvas.Children.Add(tb);
    }
}
