using Cleaner.Core.Interfaces;
using Cleaner.Core.Models;
using Cleaner.Core.Rules;
using Cleaner.Core.Safety;
using Cleaner.Windows.Services;

namespace Cleaner.Windows.Providers;

public sealed class WerReportsProvider : IProvider
{
    public string Id => ProviderIds.WerReports;
    public Category Category => Categories.WerReports;

    public Task<IReadOnlyList<Finding>> ScanAsync(SafetyPolicy policy, CancellationToken ct)
    {
        var findings = new List<Finding>();
        var utcNow = DateTime.UtcNow;
        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);

        if (string.IsNullOrWhiteSpace(programData))
        {
            return Task.FromResult<IReadOnlyList<Finding>>(findings);
        }

        var roots = new[]
        {
            Path.Combine(programData, "Microsoft", "Windows", "WER", "ReportArchive"),
            Path.Combine(programData, "Microsoft", "Windows", "WER", "ReportQueue")
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
                    ? $"Empty report folder older than {SafetyRules.GetRetentionDays(Category.Id, policy)} days."
                    : $"Error report file older than {SafetyRules.GetRetentionDays(Category.Id, policy)} days.";

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
