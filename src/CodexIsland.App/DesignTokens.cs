using MediaColor = System.Windows.Media.Color;
using MediaColorConverter = System.Windows.Media.ColorConverter;

namespace CodexIsland.App;

public static class DesignTokens
{
    public static readonly MediaColor SignalGreen       = Rgb("#32D74B");
    public static readonly MediaColor SignalYellow      = Rgb("#FFD60A");
    public static readonly MediaColor SignalRed         = Rgb("#FF453A");
    public static readonly MediaColor SignalStaleGray   = Rgb("#8E8E93");
    public static readonly MediaColor SignalWorkingBlue = Rgb("#0A84FF");

    public static readonly MediaColor WaterTopHealthy   = Rgb("#6EF0A4");
    public static readonly MediaColor WaterBotHealthy   = Rgb("#32D74B");
    public static readonly MediaColor WaterTopLow       = Rgb("#FFD60A");
    public static readonly MediaColor WaterBotLow       = Rgb("#FFB340");
    public static readonly MediaColor WaterTopCritical  = Rgb("#FF7A70");
    public static readonly MediaColor WaterBotCritical  = Rgb("#FF453A");
    public static readonly MediaColor WaterTopUnknown   = Rgb("#A5ACB8");
    public static readonly MediaColor WaterBotUnknown   = Rgb("#626B78");

    public static readonly MediaColor SphereRim         = Rgba(255, 255, 255, 107);
    public static readonly MediaColor SphereHighlight   = Rgba(255, 255, 255, 179);
    public static readonly MediaColor SphereGlow        = Rgba(255, 255, 255, 72);

    public static readonly MediaColor CapsuleBg         = Rgb("#090B0D");
    public static readonly MediaColor CapsuleBorder     = Rgba(39, 48, 56, 242);
    public static readonly MediaColor InactiveDot       = Rgb("#353A42");
    public const double InactiveOpacity = 0.2;
    public const double GlowOpacity     = 0.14;

    public static System.Windows.Media.SolidColorBrush Frozen(MediaColor color, double alpha = 1)
    {
        var effective = alpha >= 0.999 ? color : MediaColor.FromArgb((byte)(color.A * alpha), color.R, color.G, color.B);
        var brush = new System.Windows.Media.SolidColorBrush(effective);
        brush.Freeze();
        return brush;
    }

    private static MediaColor Rgb(string hex) => (MediaColor)MediaColorConverter.ConvertFromString(hex);
    private static MediaColor Rgba(byte r, byte g, byte b, byte a) => MediaColor.FromArgb(a, r, g, b);
}
