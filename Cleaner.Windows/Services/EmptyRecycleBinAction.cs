using Cleaner.Core.Interfaces;
using Cleaner.Core.Models;
using Cleaner.Core.Safety;

namespace Cleaner.Windows.Services;

public sealed class EmptyRecycleBinAction : ICleanupAction
{
    private readonly RecycleBinService _recycleBinService;

    public EmptyRecycleBinAction(RecycleBinService recycleBinService)
    {
        _recycleBinService = recycleBinService;
    }

    public string Id => CleanupActions.EmptyRecycleBin;
    public bool SupportsBatch => false;

    public Task<CleanupOutcome> ExecuteAsync(Finding finding, SafetyPolicy policy, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            _recycleBinService.Empty(finding.DriveRoot);
            return Task.FromResult(new CleanupOutcome(
                finding.Id,
                true,
                "Recycle Bin emptied.",
                finding.SizeBytes,
                CleanupOutcomeCategories.Deleted));
        }
        catch (Exception ex)
        {
            return Task.FromResult(new CleanupOutcome(
                finding.Id,
                false,
                ex.Message,
                0,
                CleanupOutcomeCategories.SkippedOther));
        }
    }

    public Task<IReadOnlyList<CleanupOutcome>> ExecuteBatchAsync(
        IReadOnlyList<Finding> findings,
        SafetyPolicy policy,
        CancellationToken ct)
    {
        var results = new CleanupOutcome[findings.Count];
        for (var index = 0; index < findings.Count; index++)
        {
            results[index] = new CleanupOutcome(
                findings[index].Id,
                false,
                "Batch recycle bin cleanup is not supported.",
                0,
                CleanupOutcomeCategories.SkippedOther);
        }

        return Task.FromResult<IReadOnlyList<CleanupOutcome>>(results);
    }
}
