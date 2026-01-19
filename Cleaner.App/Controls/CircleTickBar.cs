using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace Cleaner.App.Controls;

public sealed class CircleTickBar : TickBar
{
    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
        nameof(Stroke),
        typeof(Brush),
        typeof(CircleTickBar),
        new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeThicknessProperty = DependencyProperty.Register(
        nameof(StrokeThickness),
        typeof(double),
        typeof(CircleTickBar),
        new FrameworkPropertyMetadata(1d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TickRadiusProperty = DependencyProperty.Register(
        nameof(TickRadius),
        typeof(double),
        typeof(CircleTickBar),
        new FrameworkPropertyMetadata(4.5d, FrameworkPropertyMetadataOptions.AffectsRender));

    public Brush Stroke
    {
        get => (Brush)GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    public double StrokeThickness
    {
        get => (double)GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    public double TickRadius
    {
        get => (double)GetValue(TickRadiusProperty);
        set => SetValue(TickRadiusProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        if (Maximum <= Minimum)
        {
            return;
        }

        var ticks = new List<double>();
        if (Ticks.Count > 0)
        {
            ticks.AddRange(Ticks);
        }
        else if (TickFrequency > 0)
        {
            for (var tick = Minimum; tick <= Maximum + 0.0001; tick += TickFrequency)
            {
                ticks.Add(tick);
            }
        }
        else
        {
            ticks.Add(Minimum);
            ticks.Add(Maximum);
        }

        var range = Maximum - Minimum;
        var length = Placement is TickBarPlacement.Top or TickBarPlacement.Bottom
            ? ActualWidth
            : ActualHeight;
        var usable = Math.Max(0d, length - (TickRadius * 2d));
        var start = TickRadius;
        var center = new Point(0, 0);

        if (Placement is TickBarPlacement.Top or TickBarPlacement.Bottom)
        {
            center.Y = ActualHeight / 2d;
            foreach (var tick in ticks)
            {
                var ratio = (tick - Minimum) / range;
                var x = start + usable * ratio;
                center.X = x;
                dc.DrawEllipse(Fill, new Pen(Stroke, StrokeThickness), center, TickRadius, TickRadius);
            }
        }
        else
        {
            center.X = ActualWidth / 2d;
            foreach (var tick in ticks)
            {
                var ratio = (tick - Minimum) / range;
                var y = start + usable * ratio;
                center.Y = y;
                dc.DrawEllipse(Fill, new Pen(Stroke, StrokeThickness), center, TickRadius, TickRadius);
            }
        }
    }
}
