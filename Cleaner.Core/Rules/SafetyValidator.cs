using Cleaner.Core.Models;
using Cleaner.Core.Safety;

namespace Cleaner.Core.Rules;

public static class SafetyValidator
{
    public sealed record SafetyCheckResult(bool Ok, string Reason, string ReasonCategory);

    public static (bool Ok, string Reason) ValidateFindingForCleanup(Finding finding, SafetyPolicy policy)
    {
        var result = ValidateFindingForCleanupDetailed(finding, policy);
        return (result.Ok, result.Reason);
    }

    public static SafetyCheckResult ValidateFindingForCleanupDetailed(Finding finding, SafetyPolicy policy)
    {
        if (string.IsNullOrWhiteSpace(finding.ProviderId))
        {
            return new SafetyCheckResult(false, "Missing provider id.", CleanupOutcomeCategories.SkippedSafetyRecheck);
        }

        if (string.Equals(finding.RecommendedAction, CleanupActions.EmptyRecycleBin, StringComparison.OrdinalIgnoreCase))
        {
            return ValidateRecycleBinFinding(finding, policy);
        }

        if (string.IsNullOrWhiteSpace(finding.Path))
        {
            return new SafetyCheckResult(false, "Missing path.", CleanupOutcomeCategories.SkippedSafetyRecheck);
        }

        if (!SafetyRules.IsUnderAllowlist(finding.ProviderId, finding.Path, policy))
        {
            return new SafetyCheckResult(false, "Path is outside allowlist.", CleanupOutcomeCategories.SkippedSafetyRecheck);
        }

        if (SafetyRules.IsProtectedPath(finding.Path, policy))
        {
            return new SafetyCheckResult(false, "Path is protected by denylist.", CleanupOutcomeCategories.SkippedSafetyRecheck);
        }

        DateTime lastWriteUtc;
        try
        {
            var attributes = File.GetAttributes(finding.Path);
            var isDirectory = attributes.HasFlag(FileAttributes.Directory);
            if (isDirectory && attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                return new SafetyCheckResult(false, "Directory is a reparse point.", CleanupOutcomeCategories.SkippedSafetyRecheck);
            }

            lastWriteUtc = isDirectory
                ? Directory.GetLastWriteTimeUtc(finding.Path)
                : File.GetLastWriteTimeUtc(finding.Path);
        }
        catch (FileNotFoundException)
        {
            return new SafetyCheckResult(false, "Path not found.", CleanupOutcomeCategories.SkippedSafetyRecheck);
        }
        catch (DirectoryNotFoundException)
        {
            return new SafetyCheckResult(false, "Path not found.", CleanupOutcomeCategories.SkippedSafetyRecheck);
        }
        catch (Exception ex)
        {
            return new SafetyCheckResult(false, $"Unable to read attributes: {ex.Message}", CleanupOutcomeCategories.SkippedSafetyRecheck);
        }

        var utcNow = DateTime.UtcNow;
        if (!SafetyRules.IsOldEnough(finding.CategoryId, lastWriteUtc, policy, utcNow))
        {
            return new SafetyCheckResult(false, "Too new for retention policy.", CleanupOutcomeCategories.SkippedTooNew);
        }

        if (IsTempCategory(finding.CategoryId))
        {
            if (policy.RecentFileGuardHours > 0
                && lastWriteUtc >= utcNow.AddHours(-policy.RecentFileGuardHours))
            {
                return new SafetyCheckResult(
                    false,
                    $"Recent file guard (<{policy.RecentFileGuardHours}h).",
                    CleanupOutcomeCategories.SkippedTooNew);
            }

            if (policy.CompatibilityModeEnabled
                && policy.CompatibilityInstallerGuardDays > 0
                && IsInstallerLikeFile(finding.Path)
                && lastWriteUtc >= utcNow.AddDays(-policy.CompatibilityInstallerGuardDays))
            {
                return new SafetyCheckResult(
                    false,
                    $"Compatibility guard (installer-like, <{policy.CompatibilityInstallerGuardDays}d).",
                    CleanupOutcomeCategories.SkippedCompatibility);
            }
        }

        return new SafetyCheckResult(true, string.Empty, CleanupOutcomeCategories.Deleted);
    }

    private static SafetyCheckResult ValidateRecycleBinFinding(Finding finding, SafetyPolicy policy)
    {
        if (string.IsNullOrWhiteSpace(finding.Path) || string.IsNullOrWhiteSpace(finding.DriveRoot))
        {
            return new SafetyCheckResult(false, "Missing Recycle Bin drive root.", CleanupOutcomeCategories.SkippedSafetyRecheck);
        }

        if (!string.Equals(finding.Path, finding.DriveRoot, StringComparison.OrdinalIgnoreCase))
        {
            return new SafetyCheckResult(false, "Recycle Bin path mismatch.", CleanupOutcomeCategories.SkippedSafetyRecheck);
        }

        if (!SafetyRules.IsUnderAllowlist(finding.ProviderId, finding.Path, policy))
        {
            return new SafetyCheckResult(false, "Recycle Bin drive not in allowlist.", CleanupOutcomeCategories.SkippedSafetyRecheck);
        }

        if (SafetyRules.IsProtectedPath(finding.Path, policy))
        {
            return new SafetyCheckResult(false, "Drive root is protected.", CleanupOutcomeCategories.SkippedSafetyRecheck);
        }

        return new SafetyCheckResult(true, string.Empty, CleanupOutcomeCategories.Deleted);
    }

    private static bool IsTempCategory(string categoryId)
    {
        return string.Equals(categoryId, Categories.UserTemp.Id, StringComparison.OrdinalIgnoreCase)
               || string.Equals(categoryId, Categories.SystemTemp.Id, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsInstallerLikeFile(string path)
    {
        var ext = Path.GetExtension(path);
        if (string.IsNullOrWhiteSpace(ext))
        {
            return false;
        }

        return ext.Equals(".msi", StringComparison.OrdinalIgnoreCase)
               || ext.Equals(".msp", StringComparison.OrdinalIgnoreCase)
               || ext.Equals(".cab", StringComparison.OrdinalIgnoreCase)
               || ext.Equals(".exe", StringComparison.OrdinalIgnoreCase)
               || ext.Equals(".ps1", StringComparison.OrdinalIgnoreCase)
               || ext.Equals(".bat", StringComparison.OrdinalIgnoreCase)
               || ext.Equals(".cmd", StringComparison.OrdinalIgnoreCase);
    }
}
