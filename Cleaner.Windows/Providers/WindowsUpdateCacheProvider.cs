using Cleaner.Core.Interfaces;
using Cleaner.Core.Models;
using Cleaner.Core.Rules;
using Cleaner.Core.Safety;
using Cleaner.Windows.Services;

namespace Cleaner.Windows.Providers;

public sealed class WindowsUpdateCacheProvider : IProvider
{
    public string Id => ProviderIds.WindowsUpdateCache;
    public Category Category => Categories.WindowsUpdateCache;

    public Task<IReadOnlyList<Finding>> ScanAsync(SafetyPolicy policy, CancellationToken ct)
    {
        var findings = new List<Finding>();
        var utcNow = DateTime.UtcNow;
        var windowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (string.IsNullOrWhiteSpace(windowsDir))
        {
            return Task.FromResult<IReadOnlyList<Finding>>(findings);
        }

        var roots = new[]
        {
            Path.Combine(windowsDir, "SoftwareDistribution", "Download"),
            Path.Combine(windowsDir, "SoftwareDistribution", "DeliveryOptimization", "Cache")
        };

        foreach (var root in roots)
        {
            if (!Directory.Exists(root))
            {
                continue;
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

                var reason = entry.IsDirectory
                    ? $"Empty Windows Update cache folder older than {SafetyRules.GetRetentionDays(Category.Id, policy)} days."
                    : $"Windows Update cache file older than {SafetyRules.GetRetentionDays(Category.Id, policy)} days.";

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
        }

        return Task.FromResult<IReadOnlyList<Finding>>(findings);
    }
}
