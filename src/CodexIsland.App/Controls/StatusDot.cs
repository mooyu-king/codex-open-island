using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using CodexIsland.Core.Models;
using MediaColor = System.Windows.Media.Color;
using MediaColorConverter = System.Windows.Media.ColorConverter;
using WpfPoint = System.Windows.Point;
using WpfSize = System.Windows.Size;

namespace CodexIsland.App.Controls;

public sealed class StatusDot : FrameworkElement
{
    public static readonly DependencyProperty SignalProperty =
        DependencyProperty.Register(
            nameof(Signal),
            typeof(ProjectSignal),
            typeof(StatusDot),
            new FrameworkPropertyMetadata(ProjectSignal.Ready, FrameworkPropertyMetadataOptions.AffectsRender, OnSignalChanged));

    private readonly DispatcherTimer _timer = new();
    private bool _blinkOn = true;

    public StatusDot()
    {
        _timer.Tick += (_, _) =>
        {
            _blinkOn = !_blinkOn;
            InvalidateVisual();
        };
        UpdateTimer();
    }

    public ProjectSignal Signal
    {
        get => (ProjectSignal)GetValue(SignalProperty);
        set => SetValue(SignalProperty, value);
    }

    protected override WpfSize MeasureOverride(WpfSize availableSize) => new(16, 16);

    protected override void OnRender(DrawingContext dc)
    {
        var color = Signal switch
        {
            ProjectSignal.Permission => "#FFD60A",
            ProjectSignal.Blocked => "#FF453A",
            ProjectSignal.Working => "#FFD60A",
            ProjectSignal.Attention or ProjectSignal.Stale => "#FFD60A",
            ProjectSignal.Paused => "#4B525C",
            ProjectSignal.Thinking or ProjectSignal.ToolDone => "#32D74B",
            ProjectSignal.Completed or ProjectSignal.Ready => "#32D74B",
            _ => "#8E8E93"
        };

        var opacity = UsesBlink(Signal) ? (_blinkOn ? 1 : 0.28) : 1;
        var brush = new SolidColorBrush((MediaColor)MediaColorConverter.ConvertFromString(color)) { Opacity = opacity };
        brush.Freeze();
        var center = new WpfPoint(ActualWidth / 2, ActualHeight / 2);
        if (opacity > 0.75)
        {
            var glow = new SolidColorBrush((MediaColor)MediaColorConverter.ConvertFromString(color)) { Opacity = 0.18 };
            glow.Freeze();
            dc.DrawEllipse(glow, null, center, 9, 9);
        }
        dc.DrawEllipse(brush, null, center, 7, 7);
    }

    private void UpdateTimer()
    {
        _timer.Stop();
        _blinkOn = true;
        if (UsesBlink(Signal))
        {
            _timer.Interval = Signal is ProjectSignal.Thinking or ProjectSignal.Blocked or ProjectSignal.Permission
                ? TimeSpan.FromMilliseconds(260)
                : TimeSpan.FromMilliseconds(760);
            _timer.Start();
        }
        InvalidateVisual();
    }

    private static void OnSignalChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((StatusDot)d).UpdateTimer();

    private static bool UsesBlink(ProjectSignal signal)
        => signal is ProjectSignal.Thinking or ProjectSignal.Working or ProjectSignal.ToolDone
            or ProjectSignal.Attention or ProjectSignal.Permission or ProjectSignal.Blocked;
}
