using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Trisat.Core.Models;

namespace Trisat.Linux.Controls;

/// <summary>
/// Hardware-accelerated polar celestial SkyPlot control matching TRISAT iOS/macOS/Windows visual design.
/// </summary>
public sealed class SkyPlotControl : Border
{
    public static readonly StyledProperty<IReadOnlyList<SatelliteInfo>?> SatellitesProperty =
        AvaloniaProperty.Register<SkyPlotControl, IReadOnlyList<SatelliteInfo>?>(nameof(Satellites));

    public static readonly StyledProperty<bool> IsAwaitingLocationProperty =
        AvaloniaProperty.Register<SkyPlotControl, bool>(nameof(IsAwaitingLocation));

    public static readonly StyledProperty<bool> IsAwaitingTleProperty =
        AvaloniaProperty.Register<SkyPlotControl, bool>(nameof(IsAwaitingTle));

    public static readonly StyledProperty<double> ElevationMaskProperty =
        AvaloniaProperty.Register<SkyPlotControl, double>(nameof(ElevationMask), 10.0);

    private static readonly FontFamily MonoFont = new("Ubuntu Mono, DejaVu Sans Mono, Liberation Mono, monospace");

    public IReadOnlyList<SatelliteInfo>? Satellites
    {
        get => GetValue(SatellitesProperty);
        set => SetValue(SatellitesProperty, value);
    }

    public bool IsAwaitingLocation
    {
        get => GetValue(IsAwaitingLocationProperty);
        set => SetValue(IsAwaitingLocationProperty, value);
    }

    public bool IsAwaitingTle
    {
        get => GetValue(IsAwaitingTleProperty);
        set => SetValue(IsAwaitingTleProperty, value);
    }

    public double ElevationMask
    {
        get => GetValue(ElevationMaskProperty);
        set => SetValue(ElevationMaskProperty, value);
    }

    private readonly Canvas _canvas = new() { ClipToBounds = true };

    static SkyPlotControl()
    {
        SatellitesProperty.Changed.AddClassHandler<SkyPlotControl>((c, _) => c.Redraw());
        IsAwaitingLocationProperty.Changed.AddClassHandler<SkyPlotControl>((c, _) => c.Redraw());
        IsAwaitingTleProperty.Changed.AddClassHandler<SkyPlotControl>((c, _) => c.Redraw());
        ElevationMaskProperty.Changed.AddClassHandler<SkyPlotControl>((c, _) => c.Redraw());
    }

    public SkyPlotControl()
    {
        Child = _canvas;
        Background = Brushes.Transparent;
        AttachedToVisualTree += (_, _) => Redraw();
        SizeChanged += (_, _) => Redraw();
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

        double w = Bounds.Width;
        double h = Bounds.Height;
        if (w < 60 || h < 60) return;

        double cx = w / 2.0;
        double cy = h / 2.0;
        double radius = Math.Min(w, h) / 2.0 - 24.0;
        if (radius <= 10) return;

        var ringBrush = new SolidColorBrush(Color.FromArgb(0x55, 0x64, 0x74, 0x8B));
        var innerRingBrush = new SolidColorBrush(Color.FromArgb(0x40, 0x64, 0x74, 0x8B));
        var bgFill = new RadialGradientBrush
        {
            GradientStops =
            [
                new GradientStop(Color.FromArgb(0x38, 0x06, 0xB6, 0xD4), 0.0),
                new GradientStop(Color.FromArgb(0x14, 0x0F, 0x17, 0x2A), 1.0)
            ]
        };

        // 1. Outer horizon circle (0 deg) + 30° & 60° elevation rings
        AddCircle(cx, cy, radius, ringBrush, 1.5, bgFill, null);
        AddCircle(cx, cy, radius * (60.0 / 90.0), innerRingBrush, 1.0, null, [4, 4]);
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
        AddCenteredText("N", cx, cy - radius - 12, Color.FromRgb(0x38, 0xBD, 0xF8), 11, FontWeight.Bold);
        AddCenteredText("S", cx, cy + radius + 12, Color.FromRgb(0x94, 0xA3, 0xB8), 11, FontWeight.Bold);
        AddCenteredText("E", cx + radius + 13, cy, Color.FromRgb(0x94, 0xA3, 0xB8), 11, FontWeight.Bold);
        AddCenteredText("W", cx - radius - 13, cy, Color.FromRgb(0x94, 0xA3, 0xB8), 11, FontWeight.Bold);

        // Ring elevation labels
        AddCenteredText("30°", cx + 14, cy - radius * (60.0 / 90.0) + 8, Color.FromArgb(0x99, 0x94, 0xA3, 0xB8), 9, FontWeight.Normal);
        AddCenteredText("60°", cx + 14, cy - radius * (30.0 / 90.0) + 8, Color.FromArgb(0x99, 0x94, 0xA3, 0xB8), 9, FontWeight.Normal);

        // 3. Satellites
        var sats = Satellites;
        if (sats is { Count: > 0 })
        {
            double dotSize = Math.Clamp(radius * 0.13, 16.0, 24.0);
            double dotHalf = dotSize / 2.0;
            double labelSize = Math.Clamp(radius * 0.06, 8.5, 11.0);

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
                    Width = dotSize,
                    Height = dotSize,
                    Stroke = new SolidColorBrush(color),
                    StrokeThickness = sat.UsedInFix ? 2.2 : 1.3,
                    Fill = new SolidColorBrush(fillColor)
                };

                ToolTip.SetTip(dot,
                    $"{sat.FormattedLabel} ({sat.Constellation})\n" +
                    $"Elev: {sat.Elevation:F1}°  |  Azim: {sat.Azimuth:F1}°\n" +
                    $"C/N0: {sat.Cno} dB-Hz  |  Band: {sat.Band}\n" +
                    $"Used in Fix: {(sat.UsedInFix ? "Yes" : "No")}" +
                    (sat.HasInav ? "\nGalileo I/NAV Capable" : ""));

                Canvas.SetLeft(dot, x - dotHalf);
                Canvas.SetTop(dot, y - dotHalf);
                _canvas.Children.Add(dot);

                var label = new TextBlock
                {
                    Text = sat.FormattedLabel,
                    FontFamily = MonoFont,
                    FontSize = labelSize,
                    FontWeight = FontWeight.Bold,
                    Foreground = Brushes.White,
                    IsHitTestVisible = false
                };
                label.Measure(Size.Infinity);
                Canvas.SetLeft(label, x - label.DesiredSize.Width / 2.0);
                Canvas.SetTop(label, y + dotHalf);
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
                            FontWeight = FontWeight.Bold,
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

            hud.Measure(Size.Infinity);
            Canvas.SetLeft(hud, cx - hud.DesiredSize.Width / 2.0);
            Canvas.SetTop(hud, cy - hud.DesiredSize.Height / 2.0);
            _canvas.Children.Add(hud);
        }
    }

    private void AddCircle(double cx, double cy, double r, IBrush stroke, double thickness, IBrush? fill, double[]? dash)
    {
        var ellipse = new Ellipse
        {
            Width = r * 2,
            Height = r * 2,
            Stroke = stroke,
            StrokeThickness = thickness,
            Fill = fill ?? Brushes.Transparent
        };
        if (dash is not null)
            ellipse.StrokeDashArray = new AvaloniaList<double>(dash);
        Canvas.SetLeft(ellipse, cx - r);
        Canvas.SetTop(ellipse, cy - r);
        _canvas.Children.Add(ellipse);
    }

    private void AddLine(double x1, double y1, double x2, double y2, IBrush stroke)
    {
        _canvas.Children.Add(new Line
        {
            StartPoint = new Point(x1, y1),
            EndPoint = new Point(x2, y2),
            Stroke = stroke,
            StrokeThickness = 1
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
            FontFamily = MonoFont
        };
        tb.Measure(Size.Infinity);
        Canvas.SetLeft(tb, x - tb.DesiredSize.Width / 2.0);
        Canvas.SetTop(tb, y - tb.DesiredSize.Height / 2.0);
        _canvas.Children.Add(tb);
    }
}
