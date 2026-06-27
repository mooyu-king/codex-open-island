using CodexIsland.Core.Models;

namespace CodexIsland.App.ViewModels;

public sealed record ProjectItemViewModel(
    string Title,
    ProjectSignal Signal,
    string Detail,
    string ActionText,
    string ToolTipText,
    string? WorkingDirectory,
    string? ProjectRoot,
    string? ProjectName,
    string? ThreadId);
