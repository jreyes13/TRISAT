using System.Windows;
using System.Windows.Controls;

namespace Trisat.Win.Controls;

/// <summary>
/// A ScrollViewer that stretches its direct FrameworkElement child to fill the full
/// viewport height whenever the window is taller than MinContentHeight, while
/// gracefully enabling vertical scrolling when the window is resized smaller.
/// </summary>
public sealed class AdaptiveScrollViewer : ScrollViewer
{
    public static readonly DependencyProperty MinContentHeightProperty =
        DependencyProperty.Register(
            nameof(MinContentHeight),
            typeof(double),
            typeof(AdaptiveScrollViewer),
            new PropertyMetadata(520.0, OnMinContentHeightChanged));

    public double MinContentHeight
    {
        get => (double)GetValue(MinContentHeightProperty);
        set => SetValue(MinContentHeightProperty, value);
    }

    public AdaptiveScrollViewer()
    {
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        Loaded += (_, _) => SyncChildHeight();
        SizeChanged += (_, _) => SyncChildHeight();
    }

    protected override void OnContentChanged(object oldContent, object newContent)
    {
        base.OnContentChanged(oldContent, newContent);
        SyncChildHeight();
    }

    private static void OnMinContentHeightChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is AdaptiveScrollViewer sv)
            sv.SyncChildHeight();
    }

    private void SyncChildHeight()
    {
        if (Content is not FrameworkElement child)
            return;

        double availableHeight = ActualHeight - Padding.Top - Padding.Bottom - child.Margin.Top - child.Margin.Bottom;
        if (availableHeight <= 0)
            return;

        double targetHeight = Math.Max(Math.Floor(availableHeight), MinContentHeight);
        if (double.IsNaN(child.Height) || Math.Abs(child.Height - targetHeight) > 0.5)
        {
            child.Height = targetHeight;
        }
    }
}
