using Cleaner.Core.Models;
using Cleaner.Core.Safety;

namespace Cleaner.Core.Interfaces;

public interface ICleanupAction
{
    string Id { get; }
    bool SupportsBatch { get; }
    Task<CleanupOutcome> ExecuteAsync(Finding finding, SafetyPolicy policy, CancellationToken ct);
    Task<IReadOnlyList<CleanupOutcome>> ExecuteBatchAsync(
        IReadOnlyList<Finding> findings,
        SafetyPolicy policy,
        CancellationToken ct);
}
