using Cleaner.Core.Interfaces;
using Cleaner.Core.Models;
using Cleaner.Core.Rules;
using Cleaner.Core.Safety;
using Cleaner.Windows.Services;

namespace Cleaner.Windows.Providers;

public sealed class BrowserCacheProvider : IProvider
{
    public string Id => ProviderIds.BrowserCache;
    public Category Category => Categories.BrowserCache;

    public Task<IReadOnlyList<Finding>> ScanAsync(SafetyPolicy policy, CancellationToken ct)
    {
        var findings = new List<Finding>();
        var utcNow = DateTime.UtcNow;
        foreach (var root in GetCacheRoots(policy))
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
                    ? $"Empty cache folder older than {SafetyRules.GetRetentionDays(Category.Id, policy)} days."
                    : $"Browser cache file older than {SafetyRules.GetRetentionDays(Category.Id, policy)} days.";

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
                    true,
                    CleanupActions.RecycleFile));
            }
        }

        return Task.FromResult<IReadOnlyList<Finding>>(findings);
    }

    private static IEnumerable<string> GetCacheRoots(SafetyPolicy policy)
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData))
        {
            yield break;
        }

        foreach (var root in EnumerateChromiumCacheRoots(Path.Combine(localAppData, "Microsoft", "Edge", "User Data"), policy))
        {
            yield return root;
        }

        foreach (var root in EnumerateChromiumCacheRoots(Path.Combine(localAppData, "Google", "Chrome", "User Data"), policy))
        {
            yield return root;
        }

        foreach (var root in EnumerateFirefoxCacheRoots(Path.Combine(localAppData, "Mozilla", "Firefox", "Profiles"), policy))
        {
            yield return root;
        }
    }

    private static IEnumerable<string> EnumerateChromiumCacheRoots(string userDataRoot, SafetyPolicy policy)
    {
        if (!Directory.Exists(userDataRoot))
        {
            yield break;
        }

        IEnumerable<string> profiles;
        try
        {
            profiles = Directory.EnumerateDirectories(userDataRoot);
        }
        catch (Exception ex)
        {
            policy.ReportWarning($"Skip browser profiles: {userDataRoot} ({ex.Message})");
            yield break;
        }

        foreach (var profile in profiles)
        {
            var name = Path.GetFileName(profile);
            if (!string.Equals(name, "Default", StringComparison.OrdinalIgnoreCase)
                && !name.StartsWith("Profile ", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var cacheDirs = new[]
            {
                Path.Combine(profile, "Cache"),
                Path.Combine(profile, "Code Cache"),
                Path.Combine(profile, "GPUCache")
            };

            foreach (var cache in cacheDirs)
            {
                yield return cache;
            }
        }
    }

    private static IEnumerable<string> EnumerateFirefoxCacheRoots(string profilesRoot, SafetyPolicy policy)
    {
        if (!Directory.Exists(profilesRoot))
        {
            yield break;
        }

        IEnumerable<string> profiles;
        try
        {
            profiles = Directory.EnumerateDirectories(profilesRoot);
        }
        catch (Exception ex)
        {
            policy.ReportWarning($"Skip Firefox profiles: {profilesRoot} ({ex.Message})");
            yield break;
        }

        foreach (var profile in profiles)
        {
            yield return Path.Combine(profile, "cache2");
        }
    }
}
