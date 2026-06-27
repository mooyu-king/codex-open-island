namespace CodexIsland.Core.Models;

public sealed record QuotaSnapshot(
    string LimitId,
    string LimitName,
    string PlanType,
    int? RemainingPercent,
    int? UsedPercent,
    DateTimeOffset? ResetsAt,
    int? WeeklyRemainingPercent,
    int? WeeklyUsedPercent,
    DateTimeOffset? WeeklyResetsAt,
    DateTimeOffset FetchedAt,
    QuotaHealth Health,
    string? ErrorCode = null,
    string? ErrorMessage = null)
{
    public static QuotaSnapshot Loading() => new(
        "codex",
        "Codex",
        "unknown",
        null,
        null,
        null,
        null,
        null,
        null,
        DateTimeOffset.Now,
        QuotaHealth.Loading);

    public static QuotaSnapshot Error(string message, string? code = null) => new(
        "codex",
        "Codex",
        "unknown",
        null,
        null,
        null,
        null,
        null,
        null,
        DateTimeOffset.Now,
        QuotaHealth.Error,
        code,
        message);
}
