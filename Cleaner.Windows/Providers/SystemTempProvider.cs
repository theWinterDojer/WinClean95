using Cleaner.Core.Interfaces;
using Cleaner.Core.Models;
using Cleaner.Core.Rules;
using Cleaner.Core.Safety;
using Cleaner.Windows.Services;

namespace Cleaner.Windows.Providers;

public sealed class SystemTempProvider : IProvider
{
    public string Id => ProviderIds.SystemTemp;
    public Category Category => Categories.SystemTemp;

    public Task<IReadOnlyList<Finding>> ScanAsync(SafetyPolicy policy, CancellationToken ct)
    {
        var findings = new List<Finding>();
        var utcNow = DateTime.UtcNow;
        var recentGuardSkipped = false;
        var compatibilityGuardSkipped = false;
        var unsafeRoots = new List<string>();

        var windowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (string.IsNullOrWhiteSpace(windowsDir))
        {
            return Task.FromResult<IReadOnlyList<Finding>>(findings);
        }

        var root = Path.Combine(windowsDir, "Temp");
        if (!Directory.Exists(root))
        {
            return Task.FromResult<IReadOnlyList<Finding>>(findings);
        }

        if (!TempPathRules.IsSafeSystemTempRoot(root, policy))
        {
            unsafeRoots.Add(root);
            policy.ReportWarning($"Skipped unsafe temp roots: {string.Join(", ", unsafeRoots)}");
            return Task.FromResult<IReadOnlyList<Finding>>(findings);
        }

        policy.AddAllowlist(Id, root);
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
                ? $"Empty system temp folder older than {SafetyRules.GetRetentionDays(Category.Id, policy)} days."
                : $"System temp file older than {SafetyRules.GetRetentionDays(Category.Id, policy)} days.";

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
                true,
                false,
                CleanupActions.RecycleFile));
        }

        if (recentGuardSkipped)
        {
            policy.ReportWarning($"Recent-file guard skipped some system temp items (<{policy.RecentFileGuardHours}h).");
        }

        if (compatibilityGuardSkipped)
        {
            policy.ReportWarning($"Compatibility guard skipped installer-like system temp items (<{policy.CompatibilityInstallerGuardDays}d).");
        }

        return Task.FromResult<IReadOnlyList<Finding>>(findings);
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
