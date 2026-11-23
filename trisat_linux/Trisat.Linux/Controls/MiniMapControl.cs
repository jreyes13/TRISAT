using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace Trisat.Linux.Controls;

/// <summary>
/// Interactive geospatial mini-map displaying live GNSS coordinates with OpenStreetMap tiles
/// (asynchronously cached) and a graceful aerospace graticule fallback when offline.
/// </summary>
public sealed class MiniMapControl : Border
{
    public static readonly StyledProperty<double?> LatitudeProperty =
        AvaloniaProperty.Register<MiniMapControl, double?>(nameof(Latitude));

    public static readonly StyledProperty<double?> LongitudeProperty =
        AvaloniaProperty.Register<MiniMapControl, double?>(nameof(Longitude));

    public static readonly StyledProperty<string> BadgeTextProperty =
        AvaloniaProperty.Register<MiniMapControl, string>(nameof(BadgeText), "GNSS LIVE");

    private static readonly FontFamily MonoFont = new("Ubuntu Mono, DejaVu Sans Mono, Liberation Mono, monospace");

    public double? Latitude
    {
        get => GetValue(LatitudeProperty);
        set => SetValue(LatitudeProperty, value);
    }

    public double? Longitude
    {
        get => GetValue(LongitudeProperty);
        set => SetValue(LongitudeProperty, value);
    }

    public string BadgeText
    {
        get => GetValue(BadgeTextProperty);
        set => SetValue(BadgeTextProperty, value);
    }

    private static readonly HttpClient TileHttp = CreateTileClient();
    private static readonly Dictionary<string, Bitmap> TileCache = new();
    private static readonly object CacheLock = new();

    private readonly Grid _root = new() { ClipToBounds = true };
    private readonly Canvas _mapCanvas = new() { ClipToBounds = true };
    private readonly TextBlock _coordOverlay;
    private int _zoom = 13;

    static MiniMapControl()
    {
        LatitudeProperty.Changed.AddClassHandler<MiniMapControl>((c, e) => _ = c.RenderAsync());
        LongitudeProperty.Changed.AddClassHandler<MiniMapControl>((c, e) => _ = c.RenderAsync());
        BadgeTextProperty.Changed.AddClassHandler<MiniMapControl>((c, e) => _ = c.RenderAsync());
    }

    private static HttpClient CreateTileClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("TRISAT-Linux/2.4 (+https://github.com/jreyes13/TRISAT)");
        return client;
    }

    public MiniMapControl()
    {
        CornerRadius = new CornerRadius(12);
        BorderBrush = new SolidColorBrush(Color.FromRgb(0x1E, 0x29, 0x3B));
        BorderThickness = new Thickness(1);
        Background = new SolidColorBrush(Color.FromRgb(0x0B, 0x13, 0x24));

        _root.Children.Add(_mapCanvas);

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

        _coordOverlay = new TextBlock
        {
            FontFamily = MonoFont,
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
        AttachedToVisualTree += (_, _) => _ = RenderAsync();
        SizeChanged += (_, _) => _ = RenderAsync();
    }

    private static Button CreateZoomButton(string label, Action onClick)
    {
        var btn = new Button
        {
            Content = label,
            Width = 28,
            Height = 26,
            Padding = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 4),
            Background = new SolidColorBrush(Color.FromArgb(0xCC, 0x0F, 0x17, 0x2A)),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x33, 0x41, 0x55)),
            BorderThickness = new Thickness(1),
            FontWeight = FontWeight.Bold,
            Cursor = new Cursor(StandardCursorType.Hand)
        };
        btn.Click += (_, _) => onClick();
        return btn;
    }

    private async Task RenderAsync()
    {
        double w = Bounds.Width;
        double h = Bounds.Height;
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

        double n = Math.Pow(2, _zoom);
        double xTileF = (lon + 180.0) / 360.0 * n;
        double latRad = lat * Math.PI / 180.0;
        double yTileF = (1.0 - Math.Log(Math.Tan(latRad) + 1.0 / Math.Cos(latRad)) / Math.PI) / 2.0 * n;

        int centerTileX = (int)Math.Floor(xTileF);
        int centerTileY = (int)Math.Floor(yTileF);
        double offsetPxX = (xTileF - centerTileX) * 256.0;
        double offsetPxY = (yTileF - centerTileY) * 256.0;

        int halfTilesX = Math.Clamp((int)Math.Ceiling(w / 512.0) + 1, 1, 4);
        int halfTilesY = Math.Clamp((int)Math.Ceiling(h / 512.0) + 1, 1, 4);
        int maxTile = (int)n;
        var tileTasks = new List<(int dx, int dy, Task<Bitmap?> task)>();
        for (int dx = -halfTilesX; dx <= halfTilesX; dx++)
        {
            for (int dy = -halfTilesY; dy <= halfTilesY; dy++)
            {
                int tx = ((centerTileX + dx) % maxTile + maxTile) % maxTile;
                int ty = centerTileY + dy;
                if (ty < 0 || ty >= maxTile) continue;
                tileTasks.Add((dx, dy, FetchTileAsync(_zoom, tx, ty)));
            }
        }

        await Task.WhenAll(tileTasks.Select(t => t.task));

        if (Latitude != lat || Longitude != lon)
            return;

        double curW = Bounds.Width;
        double curH = Bounds.Height;
        if (curW < 40 || curH < 40) return;

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
            Canvas.SetLeft(img, (curW / 2.0) - offsetPxX + (dx * 256.0));
            Canvas.SetTop(img, (curH / 2.0) - offsetPxY + (dy * 256.0));
            _mapCanvas.Children.Add(img);
        }

        if (!anyTileLoaded)
            DrawFallbackGraticule(curW, curH);

        DrawGnssMarker(curW / 2.0, curH / 2.0);
    }

    private void DrawFallbackGraticule(double w, double h)
    {
        var gridBrush = new SolidColorBrush(Color.FromArgb(0x30, 0x38, 0xBD, 0xF8));
        for (double x = 0; x < w; x += 36)
        {
            _mapCanvas.Children.Add(new Line
            {
                StartPoint = new Point(x, 0),
                EndPoint = new Point(x, h),
                Stroke = gridBrush,
                StrokeThickness = 1
            });
        }
        for (double y = 0; y < h; y += 36)
        {
            _mapCanvas.Children.Add(new Line
            {
                StartPoint = new Point(0, y),
                EndPoint = new Point(w, y),
                Stroke = gridBrush,
                StrokeThickness = 1
            });
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
                FontFamily = MonoFont,
                FontSize = 9,
                FontWeight = FontWeight.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x22, 0xD3, 0xEE))
            }
        };
        badge.Measure(Size.Infinity);
        Canvas.SetLeft(badge, cx - badge.DesiredSize.Width / 2.0);
        Canvas.SetTop(badge, cy - 36);
        _mapCanvas.Children.Add(badge);
    }

    private static async Task<Bitmap?> FetchTileAsync(int z, int x, int y)
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
            using var ms = new MemoryStream(bytes);
            var bmp = new Bitmap(ms);

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
