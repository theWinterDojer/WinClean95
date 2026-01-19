using Cleaner.Core.Safety;

namespace Cleaner.Core.Rules;

public static class SafetyRules
{
    public static string NormalizePath(string path)
    {
        var full = Path.GetFullPath(path);
        return full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    public static string NormalizeRoot(string path)
    {
        var normalized = NormalizePath(path);
        if (!normalized.EndsWith(Path.DirectorySeparatorChar))
        {
            normalized += Path.DirectorySeparatorChar;
        }

        return normalized;
    }

    public static bool IsUnderAllowlist(string providerId, string path, SafetyPolicy policy)
    {
        if (!policy.AllowlistRootsByProvider.TryGetValue(providerId, out var roots) || roots.Count == 0)
        {
            return false;
        }

        var normalizedPath = NormalizePath(path);
        var normalizedPathRoot = NormalizeRoot(path);
        foreach (var root in roots)
        {
            var normalizedRoot = NormalizeRoot(root);
            if (normalizedPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.Equals(normalizedPathRoot, normalizedRoot, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public static bool IsProtectedPath(string path, SafetyPolicy policy)
    {
        var normalizedPath = NormalizePath(path) + Path.DirectorySeparatorChar;

        foreach (var pattern in policy.ProtectedPathPrefixes)
        {
            if (string.IsNullOrWhiteSpace(pattern))
            {
                continue;
            }

            if (pattern.Contains('*'))
            {
                if (MatchesWildcardPrefix(normalizedPath, pattern))
                {
                    return true;
                }
            }
            else
            {
                var normalizedPrefix = NormalizeRoot(pattern);
                if (normalizedPath.StartsWith(normalizedPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    public static bool IsAllowedPath(string providerId, string path, SafetyPolicy policy)
    {
        if (!IsUnderAllowlist(providerId, path, policy))
        {
            return false;
        }

        if (IsProtectedPath(path, policy))
        {
            return false;
        }

        return true;
    }

    public static bool IsOldEnough(string categoryId, DateTime? lastWriteTimeUtc, SafetyPolicy policy, DateTime utcNow)
    {
        if (!lastWriteTimeUtc.HasValue)
        {
            return false;
        }

        if (policy.RetentionDaysByCategory.TryGetValue(categoryId, out var retentionDays))
        {
            return lastWriteTimeUtc.Value <= utcNow.AddDays(-retentionDays);
        }

        return lastWriteTimeUtc.Value <= utcNow - policy.MinAgeDefault;
    }

    public static int GetRetentionDays(string categoryId, SafetyPolicy policy)
    {
        return policy.RetentionDaysByCategory.TryGetValue(categoryId, out var retentionDays)
            ? retentionDays
            : (int)Math.Ceiling(policy.MinAgeDefault.TotalDays);
    }

    private static bool MatchesWildcardPrefix(string normalizedPathWithTrailingSeparator, string pattern)
    {
        var normalizedPattern = pattern.Replace('/', '\\');
        var starIndex = normalizedPattern.IndexOf('*');
        if (starIndex < 0)
        {
            return false;
        }

        var prefix = normalizedPattern[..starIndex];
        var suffix = normalizedPattern[(starIndex + 1)..];

        var normalizedPrefix = NormalizeRoot(prefix);
        if (!normalizedPathWithTrailingSeparator.StartsWith(normalizedPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (string.IsNullOrEmpty(suffix))
        {
            return true;
        }

        var normalizedSuffix = suffix.Replace('/', '\\');
        if (!normalizedSuffix.StartsWith("\\", StringComparison.OrdinalIgnoreCase))
        {
            normalizedSuffix = "\\" + normalizedSuffix;
        }

        if (!normalizedSuffix.EndsWith("\\", StringComparison.OrdinalIgnoreCase))
        {
            normalizedSuffix += "\\";
        }

        var suffixIndex = normalizedPathWithTrailingSeparator.IndexOf(
            normalizedSuffix,
            normalizedPrefix.Length,
            StringComparison.OrdinalIgnoreCase);

        return suffixIndex >= 0;
    }
}
