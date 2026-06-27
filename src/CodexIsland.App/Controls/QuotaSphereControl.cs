using System.Windows;
using System.Windows.Media;
using CodexIsland.Core.Models;
using MediaColor = System.Windows.Media.Color;
using MediaPen = System.Windows.Media.Pen;
using WpfPoint = System.Windows.Point;
using WpfSize = System.Windows.Size;

namespace CodexIsland.App.Controls;

public sealed class QuotaSphereControl : FrameworkElement
{
    public static readonly DependencyProperty RemainingPercentProperty =
        DependencyProperty.Register(
            nameof(RemainingPercent),
            typeof(double),
            typeof(QuotaSphereControl),
            new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty HealthProperty =
        DependencyProperty.Register(
            nameof(Health),
            typeof(QuotaHealth),
            typeof(QuotaSphereControl),
            new FrameworkPropertyMetadata(QuotaHealth.Unknown, FrameworkPropertyMetadataOptions.AffectsRender));

    public double RemainingPercent
    {
        get => (double)GetValue(RemainingPercentProperty);
        set => SetValue(RemainingPercentProperty, value);
    }

    public QuotaHealth Health
    {
        get => (QuotaHealth)GetValue(HealthProperty);
        set => SetValue(HealthProperty, value);
    }

    protected override WpfSize MeasureOverride(WpfSize availableSize)
    {
        return new WpfSize(88, 88);
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var side = Math.Min(ActualWidth, ActualHeight);
        if (side <= 0)
        {
            return;
        }

        var rect = new Rect((ActualWidth - side) / 2, (ActualHeight - side) / 2, side, side);
        var center = new WpfPoint(rect.Left + rect.Width / 2, rect.Top + rect.Height / 2);
        var radius = side / 2;

        dc.DrawEllipse(DesignTokens.Frozen(DesignTokens.SphereGlow), null, center, radius, radius);
        dc.PushClip(new EllipseGeometry(center, radius - 2, radius - 2));

        var fill = Math.Clamp(RemainingPercent, 0, 100) / 100.0;
        var waterTop = rect.Bottom - rect.Height * fill;
        dc.DrawGeometry(WaterBrush(), null, BuildWaterGeometry(rect, waterTop));
        dc.Pop();

        dc.DrawEllipse(
            new RadialGradientBrush(
                MediaColor.FromArgb(58, 255, 255, 255),
                MediaColor.FromArgb(8, 255, 255, 255))
            {
                GradientOrigin = new WpfPoint(0.32, 0.24),
                Center = new WpfPoint(0.5, 0.5),
                RadiusX = 0.7,
                RadiusY = 0.7
            },
            new MediaPen(DesignTokens.Frozen(DesignTokens.SphereRim), 1.2),
            center,
            radius - 1,
            radius - 1);

        dc.DrawEllipse(
            DesignTokens.Frozen(DesignTokens.SphereHighlight),
            null,
            new WpfPoint(rect.Left + rect.Width * 0.34, rect.Top + rect.Height * 0.26),
            rect.Width * 0.16,
            rect.Height * 0.06);
    }

    private static Geometry BuildWaterGeometry(Rect rect, double top)
    {
        var geometry = new StreamGeometry();
        using var ctx = geometry.Open();
        var amplitude = Math.Max(2, rect.Height * 0.025);
        var step = rect.Width / 10;

        ctx.BeginFigure(new WpfPoint(rect.Left, rect.Bottom), true, true);
        ctx.LineTo(new WpfPoint(rect.Left, top), true, false);
        for (var x = rect.Left; x <= rect.Right + 0.1; x += step)
        {
            var phase = (x - rect.Left) / rect.Width * Math.PI * 2;
            var y = top + Math.Sin(phase) * amplitude;
            ctx.LineTo(new WpfPoint(x, y), true, false);
        }

        ctx.LineTo(new WpfPoint(rect.Right, rect.Bottom), true, false);
        geometry.Freeze();
        return geometry;
    }

    private System.Windows.Media.Brush WaterBrush()
    {
        var (top, bottom) = Health switch
        {
            QuotaHealth.Green => (DesignTokens.WaterTopHealthy, DesignTokens.WaterBotHealthy),
            QuotaHealth.Yellow => (DesignTokens.WaterTopLow, DesignTokens.WaterBotLow),
            QuotaHealth.Red or QuotaHealth.Error => (DesignTokens.WaterTopCritical, DesignTokens.WaterBotCritical),
            _ => (DesignTokens.WaterTopUnknown, DesignTokens.WaterBotUnknown)
        };

        var brush = new LinearGradientBrush(top, bottom, 90);
        brush.Freeze();
        return brush;
    }
}
