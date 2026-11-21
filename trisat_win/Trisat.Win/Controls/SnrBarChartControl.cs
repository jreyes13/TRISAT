using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Trisat.Core.Models;

namespace Trisat.Win.Controls;

/// <summary>
/// Real-time vertical C/N0 (dB-Hz) Signal Strength Bar Chart matching TRISAT color thresholds:
/// Green >= 35 dB-Hz, Amber >= 25 dB-Hz, Red < 25 dB-Hz.
/// </summary>
public sealed class SnrBarChartControl : Border
{
    public static readonly DependencyProperty SatellitesProperty =
        DependencyProperty.Register(nameof(Satellites), typeof(IReadOnlyList<SatelliteInfo>), typeof(SnrBarChartControl),
            new PropertyMetadata(null, OnSatellitesChanged));

    public IReadOnlyList<SatelliteInfo>? Satellites
    {
        get => (IReadOnlyList<SatelliteInfo>?)GetValue(SatellitesProperty);
        set => SetValue(SatellitesProperty, value);
    }

    private readonly ScrollViewer _scrollViewer;
    private readonly StackPanel _barsPanel;

    public SnrBarChartControl()
    {
        _barsPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(8, 8, 8, 4)
        };

        _scrollViewer = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = _barsPanel
        };

        Child = _scrollViewer;
        Background = Brushes.Transparent;
        Loaded += (_, _) => Redraw();
        SizeChanged += (_, _) => Redraw();
    }

    private static void OnSatellitesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is SnrBarChartControl ctrl)
            ctrl.Redraw();
    }

    private static Color GetSnrColor(int cno)
    {
        if (cno >= 35) return Color.FromRgb(0x22, 0xC5, 0x5E); // Green
        if (cno >= 25) return Color.FromRgb(0xF5, 0x9E, 0x0B); // Amber
        return Color.FromRgb(0xEF, 0x44, 0x44);                // Red
    }

    private void Redraw()
    {
        _barsPanel.Children.Clear();
        var sats = Satellites;

        if (sats is null || sats.Count == 0)
        {
            _barsPanel.VerticalAlignment = VerticalAlignment.Center;
            _barsPanel.Children.Add(new TextBlock
            {
                Text = "Awaiting Satellite C/N0 Telemetry...",
                Foreground = new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B)),
                FontFamily = new FontFamily("Consolas"),
                FontSize = 12,
                Margin = new Thickness(16, 12, 16, 12),
                VerticalAlignment = VerticalAlignment.Center
            });
            return;
        }

        _barsPanel.VerticalAlignment = VerticalAlignment.Bottom;
        double availableHeight = ActualHeight > 40 ? ActualHeight : 200.0;
        double maxBarHeight = Math.Max(60.0, availableHeight - 66.0);

        foreach (var sat in sats)
        {
            var snrColor = GetSnrColor(sat.Cno);
            var constColor = SkyPlotControl.GetConstellationColor(sat.Constellation);
            double clamped = Math.Clamp(sat.Cno, 0, 55);
            double barHeight = (clamped / 55.0) * maxBarHeight;

            var col = new StackPanel
            {
                Width = 30,
                Margin = new Thickness(4, 0, 4, 0),
                VerticalAlignment = VerticalAlignment.Bottom,
                ToolTip = $"{sat.FormattedLabel} ({sat.Constellation})\nC/N0: {sat.Cno} dB-Hz\nElev: {sat.Elevation:F1}° · Azim: {sat.Azimuth:F1}°"
            };

            // Numeric C/N0 text
            col.Children.Add(new TextBlock
            {
                Text = sat.Cno.ToString(),
                Foreground = new SolidColorBrush(snrColor),
                FontFamily = new FontFamily("Consolas"),
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 4)
            });

            // Bar track + filled bar
            var barTrack = new Grid
            {
                Width = 18,
                Height = maxBarHeight,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            barTrack.Children.Add(new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0x28, 0x94, 0xA3, 0xB8)),
                CornerRadius = new CornerRadius(3)
            });
            barTrack.Children.Add(new Border
            {
                Height = Math.Max(3, barHeight),
                VerticalAlignment = VerticalAlignment.Bottom,
                Background = new SolidColorBrush(snrColor),
                CornerRadius = new CornerRadius(3)
            });
            col.Children.Add(barTrack);

            // Constellation PRN badge
            col.Children.Add(new Border
            {
                Margin = new Thickness(0, 5, 0, 0),
                Padding = new Thickness(4, 2, 4, 2),
                CornerRadius = new CornerRadius(4),
                Background = new SolidColorBrush(Color.FromArgb(0x33, constColor.R, constColor.G, constColor.B)),
                HorizontalAlignment = HorizontalAlignment.Center,
                Child = new TextBlock
                {
                    Text = sat.FormattedLabel,
                    Foreground = new SolidColorBrush(constColor),
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = 9,
                    FontWeight = FontWeights.Bold
                }
            });

            _barsPanel.Children.Add(col);
        }
    }
}
