using Cleaner.Core.Interfaces;
using Cleaner.Core.Models;
using Cleaner.Core.Rules;
using Cleaner.Core.Safety;

namespace Cleaner.Windows.Providers;

public sealed class ThumbnailCacheProvider : IProvider
{
    public string Id => ProviderIds.ThumbnailCache;
    public Category Category => Categories.ThumbnailCache;

    public Task<IReadOnlyList<Finding>> ScanAsync(SafetyPolicy policy, CancellationToken ct)
    {
        var findings = new List<Finding>();
        var utcNow = DateTime.UtcNow;
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData))
        {
            return Task.FromResult<IReadOnlyList<Finding>>(findings);
        }

        var root = Path.Combine(localAppData, "Microsoft", "Windows", "Explorer");
        if (!Directory.Exists(root))
        {
            return Task.FromResult<IReadOnlyList<Finding>>(findings);
        }

        policy.AddAllowlist(Id, root);

        var patterns = new[]
        {
            "thumbcache*.db",
            "iconcache*.db"
        };

        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var pattern in patterns)
            {
                foreach (var path in Directory.EnumerateFiles(root, pattern, SearchOption.TopDirectoryOnly))
                {
                    files.Add(path);
                }
            }
        }
        catch (Exception ex)
        {
            policy.ReportWarning($"Skip thumbnail cache listing: {root} ({ex.Message})");
            return Task.FromResult<IReadOnlyList<Finding>>(findings);
        }

        foreach (var path in files)
        {
            ct.ThrowIfCancellationRequested();
            if (!SafetyRules.IsAllowedPath(Id, path, policy))
            {
                continue;
            }

            DateTime? lastWrite;
            long sizeBytes;
            try
            {
                var info = new FileInfo(path);
                lastWrite = info.LastWriteTimeUtc;
                sizeBytes = info.Length;
            }
            catch (Exception ex)
            {
                policy.ReportWarning($"Skip entry: {path} ({ex.Message})");
                continue;
            }

            if (!SafetyRules.IsOldEnough(Category.Id, lastWrite, policy, utcNow))
            {
                continue;
            }

            var reason = $"Thumbnail/icon cache file older than {SafetyRules.GetRetentionDays(Category.Id, policy)} days.";

            findings.Add(new Finding(
                $"{Id}:{Guid.NewGuid():N}",
                Category.Id,
                Id,
                Path.GetPathRoot(path) ?? string.Empty,
                path,
                sizeBytes,
                lastWrite,
                "High",
                reason,
                false,
                true,
                CleanupActions.RecycleFile));
        }

        return Task.FromResult<IReadOnlyList<Finding>>(findings);
    }
}
