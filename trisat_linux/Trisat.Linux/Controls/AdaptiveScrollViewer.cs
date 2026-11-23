using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace Trisat.Linux.Controls;

/// <summary>
/// A ScrollViewer that stretches its direct Control child to fill the full viewport height
/// whenever the window is taller than MinContentHeight, while gracefully enabling vertical
/// scrolling when the window is resized smaller.
/// </summary>
public sealed class AdaptiveScrollViewer : ScrollViewer
{
    public static readonly StyledProperty<double> MinContentHeightProperty =
        AvaloniaProperty.Register<AdaptiveScrollViewer, double>(nameof(MinContentHeight), 520.0);

    public double MinContentHeight
    {
        get => GetValue(MinContentHeightProperty);
        set => SetValue(MinContentHeightProperty, value);
    }

    protected override Type StyleKeyOverride => typeof(ScrollViewer);

    static AdaptiveScrollViewer()
    {
        MinContentHeightProperty.Changed.AddClassHandler<AdaptiveScrollViewer>((sv, _) => sv.SyncChildHeight());
        ContentProperty.Changed.AddClassHandler<AdaptiveScrollViewer>((sv, _) => sv.SyncChildHeight());
    }

    public AdaptiveScrollViewer()
    {
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        AttachedToVisualTree += (_, _) => SyncChildHeight();
        SizeChanged += (_, _) => SyncChildHeight();
    }

    private void SyncChildHeight()
    {
        if (Content is not Control child)
            return;

        double availableHeight = Bounds.Height - Padding.Top - Padding.Bottom - child.Margin.Top - child.Margin.Bottom;
        if (availableHeight <= 0)
            return;

        double targetHeight = Math.Max(Math.Floor(availableHeight), MinContentHeight);
        if (double.IsNaN(child.Height) || Math.Abs(child.Height - targetHeight) > 0.5)
        {
            child.Height = targetHeight;
        }
    }
}
