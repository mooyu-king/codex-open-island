using CodexIsland.Core.Models;

namespace CodexIsland.Core.Quota;

public static class QuotaHealthMapper
{
    public static QuotaHealth FromRemainingPercent(int? remainingPercent)
    {
        if (remainingPercent is null)
        {
            return QuotaHealth.Unknown;
        }

        var clamped = Math.Clamp(remainingPercent.Value, 0, 100);
        if (clamped >= 10)
        {
            return QuotaHealth.Green;
        }

        if (clamped > 0)
        {
            return QuotaHealth.Yellow;
        }

        return QuotaHealth.Red;
    }

    public static int ClampPercent(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return 0;
        }

        return (int)Math.Round(Math.Clamp(value, 0, 100));
    }
}
