using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using CodexIsland.Core.Models;
using MediaColor = System.Windows.Media.Color;
using MediaPen = System.Windows.Media.Pen;
using WpfPoint = System.Windows.Point;
using WpfRect = System.Windows.Rect;
using WpfSize = System.Windows.Size;

namespace CodexIsland.App.Controls;

public sealed class SignalLightBar : FrameworkElement
{
    public static readonly DependencyProperty SignalProperty =
        DependencyProperty.Register(
            nameof(Signal),
            typeof(ProjectSignal),
            typeof(SignalLightBar),
            new FrameworkPropertyMetadata(ProjectSignal.Ready, FrameworkPropertyMetadataOptions.AffectsRender, OnSignalChanged));

    public static readonly DependencyProperty ForceFastBlinkProperty =
        DependencyProperty.Register(
            nameof(ForceFastBlink),
            typeof(bool),
            typeof(SignalLightBar),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender, OnSignalChanged));

    public static readonly DependencyProperty AnimateProperty =
        DependencyProperty.Register(
            nameof(Animate),
            typeof(bool),
            typeof(SignalLightBar),
            new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender, OnSignalChanged));

    private readonly DispatcherTimer _timer = new();
    private bool _blinkOn = true;

    public SignalLightBar()
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

    public bool ForceFastBlink
    {
        get => (bool)GetValue(ForceFastBlinkProperty);
        set => SetValue(ForceFastBlinkProperty, value);
    }

    public bool Animate
    {
        get => (bool)GetValue(AnimateProperty);
        set => SetValue(AnimateProperty, value);
    }

    protected override WpfSize MeasureOverride(WpfSize availableSize) => new(110, 34);

    protected override void OnRender(DrawingContext dc)
    {
        var capsule = new WpfRect(1, 2, ActualWidth - 2, ActualHeight - 4);
        dc.DrawRoundedRectangle(
            DesignTokens.Frozen(DesignTokens.CapsuleBg),
            new MediaPen(DesignTokens.Frozen(DesignTokens.CapsuleBorder), 1),
            capsule, 14, 14);

        var active = ActiveLight(Signal);
        DrawDot(dc, new WpfPoint(25, ActualHeight / 2),
            active == Light.Red ? ActiveOpacity(Signal) : DesignTokens.InactiveOpacity,
            DesignTokens.SignalRed, active == Light.Red);
        DrawDot(dc, new WpfPoint(ActualWidth / 2, ActualHeight / 2),
            active == Light.Yellow ? ActiveOpacity(Signal) : DesignTokens.InactiveOpacity,
            DesignTokens.SignalYellow, active == Light.Yellow);
        DrawDot(dc, new WpfPoint(ActualWidth - 25, ActualHeight / 2),
            active == Light.Green ? ActiveOpacity(Signal) : DesignTokens.InactiveOpacity,
            DesignTokens.SignalGreen, active == Light.Green);
    }

    private void DrawDot(DrawingContext dc, WpfPoint center, double opacity, MediaColor color, bool glow)
    {
        var brush = DesignTokens.Frozen(color, opacity);
        if (glow)
        {
            dc.DrawEllipse(DesignTokens.Frozen(color, DesignTokens.GlowOpacity), null, center, 14, 14);
        }

        dc.DrawEllipse(brush, new MediaPen(DesignTokens.Frozen(Rgba(255, 255, 255, 160), opacity * 0.22), 0.8), center, 10, 10);
    }

    private double ActiveOpacity(ProjectSignal signal)
    {
        if (!Animate)
        {
            return 1;
        }

        var effect = ForceFastBlink ? BlinkEffect.Fast : BlinkEffectFor(signal);
        return effect == BlinkEffect.Steady ? 1 : (_blinkOn ? 1 : 0.24);
    }

    private void UpdateTimer()
    {
        _timer.Stop();
        _blinkOn = true;
        var effect = ForceFastBlink ? BlinkEffect.Fast : BlinkEffectFor(Signal);
        if (Animate && effect == BlinkEffect.Fast)
        {
            _timer.Interval = TimeSpan.FromMilliseconds(320);
            _timer.Start();
        }
        else if (Animate && effect == BlinkEffect.Slow)
        {
            _timer.Interval = TimeSpan.FromMilliseconds(900);
            _timer.Start();
        }
        InvalidateVisual();
    }

    private static void OnSignalChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((SignalLightBar)d).UpdateTimer();
    }

    private static Light ActiveLight(ProjectSignal signal)
    {
        return signal switch
        {
            ProjectSignal.Permission => Light.Yellow,
            ProjectSignal.Blocked => Light.Red,
            ProjectSignal.Working => Light.Yellow,
            ProjectSignal.Attention or ProjectSignal.Stale => Light.Yellow,
            ProjectSignal.Paused => Light.Off,
            _ => Light.Green
        };
    }

    private static BlinkEffect BlinkEffectFor(ProjectSignal signal)
    {
        return signal switch
        {
            ProjectSignal.Thinking or ProjectSignal.Blocked or ProjectSignal.Permission or ProjectSignal.Completed => BlinkEffect.Fast,
            ProjectSignal.Working or ProjectSignal.ToolDone or ProjectSignal.Attention or ProjectSignal.Stale => BlinkEffect.Slow,
            _ => BlinkEffect.Steady
        };
    }

    private enum Light
    {
        Off,
        Red,
        Yellow,
        Green
    }

    private enum BlinkEffect
    {
        Steady,
        Slow,
        Fast
    }

    private static MediaColor Rgba(byte r, byte g, byte b, byte a) => MediaColor.FromArgb(a, r, g, b);
}
