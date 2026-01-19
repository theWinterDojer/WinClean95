using Cleaner.Core.Interfaces;
using Cleaner.Core.Models;
using Cleaner.Core.Safety;
using Cleaner.Windows.Interop;

namespace Cleaner.Windows.Services;

public sealed class RecycleDeleteAction : ICleanupAction
{
    public string Id => CleanupActions.RecycleFile;
    public bool SupportsBatch => false;

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
                return Task.FromResult(DeletePath(finding, path));
            }

            if (Directory.Exists(path))
            {
                var directoryCheck = EnsureDirectoryEmpty(finding, path);
                if (directoryCheck is not null)
                {
                    return Task.FromResult(directoryCheck);
                }

                return Task.FromResult(DeletePath(finding, path));
            }

            return Task.FromResult(DeleteFailureClassifier.BuildNotFoundOutcome(finding));
        }
        catch (UnauthorizedAccessException ex)
        {
            return Task.FromResult(DeleteFailureClassifier.BuildAccessDeniedOutcome(finding, ex));
        }
        catch (IOException ex)
        {
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

    public async Task<IReadOnlyList<CleanupOutcome>> ExecuteBatchAsync(
        IReadOnlyList<Finding> findings,
        SafetyPolicy policy,
        CancellationToken ct)
    {
        if (findings.Count == 0)
        {
            return Array.Empty<CleanupOutcome>();
        }

        var results = new CleanupOutcome[findings.Count];
        for (var index = 0; index < findings.Count; index++)
        {
            ct.ThrowIfCancellationRequested();
            results[index] = await ExecuteAsync(findings[index], policy, ct).ConfigureAwait(false);
        }

        return results;
    }

    private static CleanupOutcome? EnsureDirectoryEmpty(Finding finding, string path)
    {
        try
        {
            if (Directory.EnumerateFileSystemEntries(path).Any())
            {
                return new CleanupOutcome(
                    finding.Id,
                    false,
                    "Directory not empty; skipped.",
                    0,
                    CleanupOutcomeCategories.SkippedLocked);
            }
        }
        catch (Exception ex)
        {
            return new CleanupOutcome(
                finding.Id,
                false,
                $"Unable to verify directory: {ex.Message}",
                0,
                CleanupOutcomeCategories.SkippedOther);
        }

        return null;
    }

    private static CleanupOutcome DeletePath(Finding finding, string path)
    {
        if (!TryRecycle(path, out var errorCode))
        {
            errorCode = DeleteFailureClassifier.NormalizeErrorCode(path, errorCode);
            return DeleteFailureClassifier.BuildFailureOutcome(finding, errorCode);
        }

        return new CleanupOutcome(
            finding.Id,
            true,
            "Sent to Recycle Bin.",
            finding.SizeBytes,
            CleanupOutcomeCategories.Deleted);
    }

    private static bool TryRecycle(string path, out int errorCode)
    {
        return TryRecycle(new[] { path }, out errorCode);
    }

    private static bool TryRecycle(IReadOnlyList<string> paths, out int errorCode)
    {
        if (paths.Count == 0)
        {
            errorCode = 0;
            return true;
        }

        var joined = string.Concat(paths.Select(path => string.Concat(path, "\0"))) + "\0";
        var op = new ShellFileOperationInterop.SHFILEOPSTRUCT
        {
            wFunc = ShellFileOperationInterop.FO_DELETE,
            pFrom = joined,
            fFlags = ShellFileOperationInterop.FILEOP_FLAGS.FOF_ALLOWUNDO
                | ShellFileOperationInterop.FILEOP_FLAGS.FOF_NOCONFIRMATION
                | ShellFileOperationInterop.FILEOP_FLAGS.FOF_NOERRORUI
                | ShellFileOperationInterop.FILEOP_FLAGS.FOF_SILENT
        };

        var result = ShellFileOperationInterop.SHFileOperation(ref op);
        if (result != 0 || op.fAnyOperationsAborted)
        {
            errorCode = result != 0 ? result : DeleteFailureClassifier.ErrorCancelled;
            return false;
        }

        errorCode = 0;
        return true;
    }

}
