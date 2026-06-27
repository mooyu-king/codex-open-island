using CodexIsland.Core.Models;

namespace CodexIsland.Core.Signals;

public interface IProjectSignalService
{
    Task<ProjectStatusSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default);
    IReadOnlyList<ProjectStatusSnapshot> GetRecentProjects(int maxCount = 6);
}
