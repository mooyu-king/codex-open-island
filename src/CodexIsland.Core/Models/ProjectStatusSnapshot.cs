namespace CodexIsland.Core.Models;

public sealed record ProjectStatusSnapshot(
    string ProjectId,
    string DisplayName,
    ProjectSignal Signal,
    string? LastEvent,
    DateTimeOffset UpdatedAt,
    bool IsFresh,
    string? WorkingDirectory = null,
    string? ProjectRoot = null,
    string? ProjectName = null)
{
    public static ProjectStatusSnapshot Ready() => new(
        "codex",
        "Codex",
        ProjectSignal.Ready,
        null,
        DateTimeOffset.Now,
        true,
        null,
        null,
        "Codex");

    public static ProjectStatusSnapshot Stale(string reason) => new(
        "codex",
        "Codex",
        ProjectSignal.Stale,
        reason,
        DateTimeOffset.Now,
        false,
        null,
        null,
        "Codex");
}
