using Cleaner.Core.Interfaces;
using Cleaner.Core.Models;
using Cleaner.Core.Rules;
using Cleaner.Core.Safety;
using Cleaner.Windows.Services;

namespace Cleaner.Windows.Providers;

public sealed class DirectXShaderCacheProvider : IProvider
{
    public string Id => ProviderIds.DirectXShaderCache;
    public Category Category => Categories.DirectXShaderCache;

    public Task<IReadOnlyList<Finding>> ScanAsync(SafetyPolicy policy, CancellationToken ct)
    {
        var findings = new List<Finding>();
        var utcNow = DateTime.UtcNow;
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData))
        {
            return Task.FromResult<IReadOnlyList<Finding>>(findings);
        }

        var root = Path.Combine(localAppData, "D3DSCache");
        if (!Directory.Exists(root))
        {
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

            var reason = entry.IsDirectory
                ? $"Empty DirectX shader cache folder older than {SafetyRules.GetRetentionDays(Category.Id, policy)} days."
                : $"DirectX shader cache file older than {SafetyRules.GetRetentionDays(Category.Id, policy)} days.";

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
                false,
                false,
                CleanupActions.RecycleFile));
        }

        return Task.FromResult<IReadOnlyList<Finding>>(findings);
    }
}
