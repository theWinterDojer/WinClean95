using Cleaner.Core.Interfaces;
using Cleaner.Core.Models;
using Cleaner.Core.Safety;

namespace Cleaner.Windows.Services;

public sealed class PermanentDeleteAction : ICleanupAction
{
    public string Id => CleanupActions.PermanentDeleteFile;
    public bool SupportsBatch => true;

    public Task<CleanupOutcome> ExecuteAsync(Finding finding, SafetyPolicy policy, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(finding.Path))
        {
            return Task.FromResult(new CleanupOutcome(
                finding.Id,
                false,
                "Missing path.",
                0,
                CleanupOutcomeCategories.SkippedOther));
        }

        try
        {
            var path = finding.Path;

            if (File.Exists(path))
            {
                File.Delete(path);
                return Task.FromResult(BuildSuccessOutcome(finding));
            }

            if (Directory.Exists(path))
            {
                Directory.Delete(path, false);
                return Task.FromResult(BuildSuccessOutcome(finding));
            }

            return Task.FromResult(DeleteFailureClassifier.BuildNotFoundOutcome(finding));
        }
        catch (UnauthorizedAccessException ex)
        {
            return Task.FromResult(DeleteFailureClassifier.BuildAccessDeniedOutcome(finding, ex));
        }
        catch (IOException ex)
        {
            if (DeleteFailureClassifier.IsDirectoryNotEmpty(ex))
            {
                return Task.FromResult(new CleanupOutcome(
                    finding.Id,
                    false,
                    "Directory not empty; skipped.",
                    0,
                    CleanupOutcomeCategories.SkippedLocked));
            }

            return Task.FromResult(DeleteFailureClassifier.BuildLockedOutcome(finding, ex));
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
        if (findings.Count == 0)
        {
            return Task.FromResult<IReadOnlyList<CleanupOutcome>>(Array.Empty<CleanupOutcome>());
        }

        var results = new CleanupOutcome[findings.Count];
        for (var index = 0; index < findings.Count; index++)
        {
            ct.ThrowIfCancellationRequested();
            var finding = findings[index];
            if (string.IsNullOrWhiteSpace(finding.Path))
            {
                results[index] = new CleanupOutcome(
                    finding.Id,
                    false,
                    "Missing path.",
                    0,
                    CleanupOutcomeCategories.SkippedOther);
                continue;
            }

            try
            {
                var path = finding.Path;
                if (File.Exists(path))
                {
                    File.Delete(path);
                    results[index] = BuildSuccessOutcome(finding);
                }
                else if (Directory.Exists(path))
                {
                    Directory.Delete(path, false);
                    results[index] = BuildSuccessOutcome(finding);
                }
                else
                {
                    results[index] = DeleteFailureClassifier.BuildNotFoundOutcome(finding);
                }
            }
            catch (UnauthorizedAccessException ex)
            {
                results[index] = DeleteFailureClassifier.BuildAccessDeniedOutcome(finding, ex);
            }
            catch (IOException ex)
            {
                if (DeleteFailureClassifier.IsDirectoryNotEmpty(ex))
                {
                    results[index] = new CleanupOutcome(
                        finding.Id,
                        false,
                        "Directory not empty; skipped.",
                        0,
                        CleanupOutcomeCategories.SkippedLocked);
                }
                else
                {
                    results[index] = DeleteFailureClassifier.BuildLockedOutcome(finding, ex);
                }
            }
            catch (Exception ex)
            {
                results[index] = new CleanupOutcome(
                    finding.Id,
                    false,
                    ex.Message,
                    0,
                    CleanupOutcomeCategories.SkippedOther);
            }
        }

        return Task.FromResult<IReadOnlyList<CleanupOutcome>>(results);
    }

    private static CleanupOutcome BuildSuccessOutcome(Finding finding)
    {
        return new CleanupOutcome(
            finding.Id,
            true,
            "Permanently deleted.",
            finding.SizeBytes,
            CleanupOutcomeCategories.Deleted);
    }
}
