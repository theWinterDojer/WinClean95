using Cleaner.Core.Safety;

namespace Cleaner.Core.Rules;

public static class TempPathRules
{
    public static bool IsSafeUserTempRoot(string root, SafetyPolicy policy)
    {
        if (!TryNormalizeRoot(root, out var normalizedRoot))
        {
            return false;
        }

        if (SafetyRules.IsProtectedPath(root, policy))
        {
            return false;
        }

        return ContainsTempSegment(normalizedRoot);
    }

    public static bool IsSafeSystemTempRoot(string root, SafetyPolicy policy)
    {
        if (!TryNormalizeRoot(root, out var normalizedRoot))
        {
            return false;
        }

        if (SafetyRules.IsProtectedPath(root, policy))
        {
            return false;
        }

        var windowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (string.IsNullOrWhiteSpace(windowsDir))
        {
            return false;
        }

        if (!TryNormalizeRoot(Path.Combine(windowsDir, "Temp"), out var windowsTempRoot))
        {
            return false;
        }

        return string.Equals(normalizedRoot, windowsTempRoot, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsUnderRoot(string normalizedRoot, string basePath)
    {
        if (!TryNormalizeRoot(basePath, out var normalizedBase))
        {
            return false;
        }

        return normalizedRoot.StartsWith(normalizedBase, StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsTempSegment(string normalizedRoot)
    {
        var trimmed = normalizedRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return false;
        }

        var segments = trimmed.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
        return segments.Any(segment =>
            segment.Equals("Temp", StringComparison.OrdinalIgnoreCase)
            || segment.Equals("Tmp", StringComparison.OrdinalIgnoreCase));
    }

    private static bool TryNormalizeRoot(string path, out string normalizedRoot)
    {
        normalizedRoot = string.Empty;
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            normalizedRoot = SafetyRules.NormalizeRoot(path);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
