namespace CodexIsland.Core.Models;

public sealed record ModelProfile(
    string Id,
    string Name,
    string? Description = null)
{
    public static IReadOnlyList<ModelProfile> BuiltIn { get; } = new[]
    {
        new ModelProfile("codex-default", "Codex (default)", "Use the default Codex model"),
        new ModelProfile("deepseek-v4", "DeepSeek V4", "DeepSeek V4 via custom provider"),
    };
}
