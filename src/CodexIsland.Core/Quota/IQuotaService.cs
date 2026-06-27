using CodexIsland.Core.Models;

namespace CodexIsland.Core.Quota;

public interface IQuotaService
{
    Task<QuotaSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default);
}
