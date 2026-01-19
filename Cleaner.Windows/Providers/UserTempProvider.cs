using Cleaner.Core.Interfaces;
using Cleaner.Core.Models;
using Cleaner.Core.Rules;
using Cleaner.Core.Safety;
using Cleaner.Windows.Services;

namespace Cleaner.Windows.Providers;

public sealed class UserTempProvider : IProvider
{
    public string Id => ProviderIds.UserTemp;
    public Category Category => Categories.UserTemp;

    public Task<IReadOnlyList<Finding>> ScanAsync(SafetyPolicy policy, CancellationToken ct)
    {
        var findings = new List<Finding>();
        var utcNow = DateTime.UtcNow;
        var recentGuardSkipped = false;
        var compatibilityGuardSkipped = false;
        var unsafeRoots = new List<string>();

        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var tempPath = Path.GetTempPath();
        if (!string.IsNullOrWhiteSpace(tempPath))
        {
            roots.Add(tempPath);
        }

        var envTemp = Environment.GetEnvironmentVariable("TEMP");
        if (!string.IsNullOrWhiteSpace(envTemp))
        {
            roots.Add(envTemp);
        }

        foreach (var root in roots)
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            if (!TempPathRules.IsSafeUserTempRoot(root, policy))
            {
                unsafeRoots.Add(root);
                continue;
            }

            policy.AddAllowlist(Id, root);
            ScanRoot(
                findings,
                root,
                policy,
                utcNow,
                ct,
                requiresAdmin: false,
                requiresAppClosed: false,
                ref recentGuardSkipped,
                ref compatibilityGuardSkipped);
        }

        if (unsafeRoots.Count > 0)
        {
            policy.ReportWarning($"Skipped unsafe temp roots: {string.Join(", ", unsafeRoots)}");
        }

        if (recentGuardSkipped)
        {
            policy.ReportWarning($"Recent-file guard skipped some user temp items (<{policy.RecentFileGuardHours}h).");
        }

        if (compatibilityGuardSkipped)
        {
            policy.ReportWarning($"Compatibility guard skipped installer-like user temp items (<{policy.CompatibilityInstallerGuardDays}d).");
        }

        return Task.FromResult<IReadOnlyList<Finding>>(findings);
    }

    private void ScanRoot(
        List<Finding> findings,
        string root,
        SafetyPolicy policy,
        DateTime utcNow,
        CancellationToken ct,
        bool requiresAdmin,
        bool requiresAppClosed,
        ref bool recentGuardSkipped,
        ref bool compatibilityGuardSkipped)
    {
        foreach (var entry in FileScanHelper.EnumerateEntries(root, policy, ct))
        {
            ct.ThrowIfCancellationRequested();
            if (!SafetyRules.IsAllowedPath(Id, entry.Path, policy))
            {
                continue;
            }

            DateTime? lastWrite;
            long sizeBytes;
            try
            {
                if (entry.IsDirectory)
                {
                    lastWrite = Directory.GetLastWriteTimeUtc(entry.Path);
                    sizeBytes = 0;
                }
                else
                {
                    var info = new FileInfo(entry.Path);
                    lastWrite = info.LastWriteTimeUtc;
                    sizeBytes = info.Length;
                }
            }
            catch (Exception ex)
            {
                policy.ReportWarning($"Skip entry: {entry.Path} ({ex.Message})");
                continue;
            }

            if (!SafetyRules.IsOldEnough(Category.Id, lastWrite, policy, utcNow))
            {
                continue;
            }

            if (policy.RecentFileGuardHours > 0
                && lastWrite.HasValue
                && lastWrite.Value >= utcNow.AddHours(-policy.RecentFileGuardHours))
            {
                recentGuardSkipped = true;
                continue;
            }

            if (!entry.IsDirectory
                && policy.CompatibilityModeEnabled
                && policy.CompatibilityInstallerGuardDays > 0
                && lastWrite.HasValue
                && lastWrite.Value >= utcNow.AddDays(-policy.CompatibilityInstallerGuardDays)
                && IsInstallerLikeFile(entry.Path))
            {
                compatibilityGuardSkipped = true;
                continue;
            }

            var reason = entry.IsDirectory
                ? $"Empty temp folder older than {SafetyRules.GetRetentionDays(Category.Id, policy)} days."
                : $"Temp file older than {SafetyRules.GetRetentionDays(Category.Id, policy)} days.";

            findings.Add(new Finding(
                $"{Id}:{Guid.NewGuid():N}",
                Category.Id,
                Id,
                Path.GetPathRoot(entry.Path) ?? string.Empty,
                entry.Path,
                sizeBytes,
                lastWrite,
                "High",
                reason,
                requiresAdmin,
                requiresAppClosed,
                CleanupActions.RecycleFile));
        }
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
