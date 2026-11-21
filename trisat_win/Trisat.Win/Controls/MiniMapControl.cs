using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace Trisat.Win.Controls;

/// <summary>
/// Interactive geospatial mini-map displaying live GNSS coordinates with OpenStreetMap tiles
/// (asynchronously cached) and a graceful aerospace graticule fallback when offline.
/// </summary>
public sealed class MiniMapControl : Border
{
    public static readonly DependencyProperty LatitudeProperty =
        DependencyProperty.Register(nameof(Latitude), typeof(double?), typeof(MiniMapControl),
            new PropertyMetadata(null, OnCoordinateChanged));

    public static readonly DependencyProperty LongitudeProperty =
        DependencyProperty.Register(nameof(Longitude), typeof(double?), typeof(MiniMapControl),
            new PropertyMetadata(null, OnCoordinateChanged));

    public static readonly DependencyProperty BadgeTextProperty =
        DependencyProperty.Register(nameof(BadgeText), typeof(string), typeof(MiniMapControl),
            new PropertyMetadata("GNSS LIVE", OnCoordinateChanged));

    public double? Latitude
    {
        get => (double?)GetValue(LatitudeProperty);
        set => SetValue(LatitudeProperty, value);
    }

    public double? Longitude
    {
        get => (double?)GetValue(LongitudeProperty);
        set => SetValue(LongitudeProperty, value);
    }

    public string BadgeText
    {
        get => (string)GetValue(BadgeTextProperty);
        set => SetValue(BadgeTextProperty, value);
    }

    private static readonly HttpClient TileHttp = CreateTileClient();
    private static readonly Dictionary<string, BitmapImage> TileCache = new();
    private static readonly object CacheLock = new();

    private readonly Grid _root = new() { ClipToBounds = true };
    private readonly Canvas _mapCanvas = new() { ClipToBounds = true };
    private readonly TextBlock _coordOverlay;
    private int _zoom = 13;

    private static HttpClient CreateTileClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("TRISAT-Windows/2.4 (+https://github.com/jreyes13/TRISAT)");
        return client;
    }

    public MiniMapControl()
    {
        CornerRadius = new CornerRadius(12);
        BorderBrush = new SolidColorBrush(Color.FromRgb(0x1E, 0x29, 0x3B));
        BorderThickness = new Thickness(1);
        Background = new SolidColorBrush(Color.FromRgb(0x0B, 0x13, 0x24));

        _root.Children.Add(_mapCanvas);

        // Zoom controls
        var zoomPanel = new StackPanel
        {
            Orientation = Orientation.Vertical,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 8, 8, 0)
        };
        zoomPanel.Children.Add(CreateZoomButton("+", () => { _zoom = Math.Min(17, _zoom + 1); _ = RenderAsync(); }));
        zoomPanel.Children.Add(CreateZoomButton("−", () => { _zoom = Math.Max(4, _zoom - 1); _ = RenderAsync(); }));
        _root.Children.Add(zoomPanel);

        // Bottom coordinate HUD
        _coordOverlay = new TextBlock
        {
            FontFamily = new FontFamily("Consolas"),
            FontSize = 10,
            Foreground = new SolidColorBrush(Color.FromRgb(0xE2, 0xE8, 0xF0))
        };
        _root.Children.Add(new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xCC, 0x02, 0x06, 0x17)),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8, 4, 8, 4),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(8),
            Child = _coordOverlay
        });

        Child = _root;
        SizeChanged += (_, _) => _ = RenderAsync();
    }

    private static Button CreateZoomButton(string label, Action onClick)
    {
        var btn = new Button
        {
            Content = label,
            Width = 26,
            Height = 24,
            Margin = new Thickness(0, 0, 0, 4),
            Background = new SolidColorBrush(Color.FromArgb(0xCC, 0x0F, 0x17, 0x2A)),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x33, 0x41, 0x55)),
            FontWeight = FontWeights.Bold,
            Cursor = System.Windows.Input.Cursors.Hand
        };
        btn.Click += (_, _) => onClick();
        return btn;
    }

    private static void OnCoordinateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is MiniMapControl ctrl)
            _ = ctrl.RenderAsync();
    }

    private async Task RenderAsync()
    {
        double w = _mapCanvas.ActualWidth;
        double h = _mapCanvas.ActualHeight;
        if (w < 40 || h < 40) return;

        _mapCanvas.Children.Clear();
        DrawFallbackGraticule(w, h);

        if (!Latitude.HasValue || !Longitude.HasValue)
        {
            _coordOverlay.Text = "Awaiting GNSS coordinates...";
            return;
        }

        double lat = Math.Clamp(Latitude.Value, -85.0, 85.0);
        double lon = Longitude.Value;
        string latDir = lat >= 0 ? "N" : "S";
        string lonDir = lon >= 0 ? "E" : "W";
        _coordOverlay.Text = FormattableString.Invariant(
            $"{Math.Abs(lat):F6}° {latDir}, {Math.Abs(lon):F6}° {lonDir}  ·  Z{_zoom}");

        // Convert lat/lon to Web Mercator slippy tile coordinates
        double n = Math.Pow(2, _zoom);
        double xTileF = (lon + 180.0) / 360.0 * n;
        double latRad = lat * Math.PI / 180.0;
        double yTileF = (1.0 - Math.Log(Math.Tan(latRad) + 1.0 / Math.Cos(latRad)) / Math.PI) / 2.0 * n;

        int centerTileX = (int)Math.Floor(xTileF);
        int centerTileY = (int)Math.Floor(yTileF);
        double offsetPxX = (xTileF - centerTileX) * 256.0;
        double offsetPxY = (yTileF - centerTileY) * 256.0;

        int maxTile = (int)n;
        var tileTasks = new List<(int dx, int dy, Task<BitmapImage?> task)>();
        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                int tx = ((centerTileX + dx) % maxTile + maxTile) % maxTile;
                int ty = centerTileY + dy;
                if (ty < 0 || ty >= maxTile) continue;
                tileTasks.Add((dx, dy, FetchTileAsync(_zoom, tx, ty)));
            }
        }

        await Task.WhenAll(tileTasks.Select(t => t.task));

        // Guard against stale async callback
        if (Latitude != lat || Longitude != lon)
            return;

        _mapCanvas.Children.Clear();
        bool anyTileLoaded = false;

        foreach (var (dx, dy, task) in tileTasks)
        {
            var bmp = task.Result;
            if (bmp is null) continue;
            anyTileLoaded = true;

            var img = new Image
            {
                Source = bmp,
                Width = 256,
                Height = 256,
                Opacity = 0.85
            };
            Canvas.SetLeft(img, (w / 2.0) - offsetPxX + (dx * 256.0));
            Canvas.SetTop(img, (h / 2.0) - offsetPxY + (dy * 256.0));
            _mapCanvas.Children.Add(img);
        }

        if (!anyTileLoaded)
            DrawFallbackGraticule(w, h);

        DrawGnssMarker(w / 2.0, h / 2.0);
    }

    private void DrawFallbackGraticule(double w, double h)
    {
        var gridBrush = new SolidColorBrush(Color.FromArgb(0x30, 0x38, 0xBD, 0xF8));
        for (double x = 0; x < w; x += 36)
        {
            _mapCanvas.Children.Add(new Line { X1 = x, Y1 = 0, X2 = x, Y2 = h, Stroke = gridBrush, StrokeThickness = 1 });
        }
        for (double y = 0; y < h; y += 36)
        {
            _mapCanvas.Children.Add(new Line { X1 = 0, Y1 = y, X2 = w, Y2 = y, Stroke = gridBrush, StrokeThickness = 1 });
        }
    }

    private void DrawGnssMarker(double cx, double cy)
    {
        var halo = new Ellipse
        {
            Width = 34,
            Height = 34,
            Fill = new SolidColorBrush(Color.FromArgb(0x40, 0x06, 0xB6, 0xD4)),
            Stroke = new SolidColorBrush(Color.FromRgb(0x22, 0xD3, 0xEE)),
            StrokeThickness = 1.5
        };
        Canvas.SetLeft(halo, cx - 17);
        Canvas.SetTop(halo, cy - 17);
        _mapCanvas.Children.Add(halo);

        var dot = new Ellipse
        {
            Width = 10,
            Height = 10,
            Fill = new SolidColorBrush(Color.FromRgb(0x00, 0xE5, 0xFF)),
            Stroke = Brushes.White,
            StrokeThickness = 1.5
        };
        Canvas.SetLeft(dot, cx - 5);
        Canvas.SetTop(dot, cy - 5);
        _mapCanvas.Children.Add(dot);

        var badge = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xDD, 0x02, 0x06, 0x17)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x22, 0xD3, 0xEE)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(5),
            Padding = new Thickness(6, 2, 6, 2),
            Child = new TextBlock
            {
                Text = BadgeText,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 9,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x22, 0xD3, 0xEE))
            }
        };
        badge.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(badge, cx - badge.DesiredSize.Width / 2.0);
        Canvas.SetTop(badge, cy - 36);
        _mapCanvas.Children.Add(badge);
    }

    private static async Task<BitmapImage?> FetchTileAsync(int z, int x, int y)
    {
        var key = $"{z}/{x}/{y}";
        lock (CacheLock)
        {
            if (TileCache.TryGetValue(key, out var cached))
                return cached;
        }

        try
        {
            var bytes = await TileHttp.GetByteArrayAsync($"https://tile.openstreetmap.org/{z}/{x}/{y}.png").ConfigureAwait(false);
            var bmp = new BitmapImage();
            using (var ms = new MemoryStream(bytes))
            {
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.StreamSource = ms;
                bmp.EndInit();
                bmp.Freeze();
            }

            lock (CacheLock)
            {
                if (TileCache.Count > 160)
                    TileCache.Clear();
                TileCache[key] = bmp;
            }
            return bmp;
        }
        catch
        {
            return null;
        }
    }
}
